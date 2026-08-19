using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Tessalume.App.Features.Pets;

/// <summary>
/// Hosts the real spritesheet in Chromium as Tessalume's self-contained pet
/// runtime. Animated PNG/WebP subframes keep their native clock while the outer
/// v2 cell clock changes background position independently.
/// </summary>
internal sealed class PetCodexRuntimeRenderer : IDisposable
{
    private const string VirtualHostName = "pet-preview.tessalume.invalid";
    private const long MaximumSpritesheetBytes = 20L * 1024 * 1024;

    private readonly WebView2CompositionControl _target;
    private TaskCompletionSource<bool>? _documentReady;
    private string? _atlasIdentity;
    private bool _initialized;
    private bool _disposed;

    public PetCodexRuntimeRenderer(WebView2CompositionControl target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        _target.Visibility = Visibility.Collapsed;
        _target.IsHitTestVisible = false;
        _target.Focusable = false;
        _target.AllowExternalDrop = false;
        _target.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        _target.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tessalume",
                "WebView2"),
        };
    }

    public bool IsReady { get; private set; }

    public bool IsVisible => _target.Visibility == Visibility.Visible && IsReady;

    public async Task ShowAsync(
        string spritesheetPath,
        string revision,
        PetCodexMotionSequence sequence,
        bool reducedMotion,
        bool active,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateSpritesheet(spritesheetPath);
        _target.Visibility = Visibility.Visible;
        await EnsureInitializedAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(spritesheetPath);
        var atlasIdentity = $"{fullPath}|{revision}";
        var payload = CreateSequencePayload(sequence, reducedMotion, active);
        if (!string.Equals(_atlasIdentity, atlasIdentity, StringComparison.Ordinal))
        {
            IsReady = false;
            _documentReady = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidDataException("宠物图集缺少父目录。");
            _target.CoreWebView2.ClearVirtualHostNameToFolderMapping(VirtualHostName);
            _target.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHostName,
                directory,
                CoreWebView2HostResourceAccessKind.DenyCors);
            var fileName = Uri.EscapeDataString(Path.GetFileName(fullPath));
            var cacheKey = Uri.EscapeDataString(revision);
            var atlasUrl = $"https://{VirtualHostName}/{fileName}?revision={cacheKey}";
            _target.NavigateToString(CreateDocument(atlasUrl, payload));
            var loaded = await _documentReady.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken);
            if (!loaded)
            {
                throw new InvalidDataException("Chromium 无法读取真实宠物图集。");
            }
            _atlasIdentity = atlasIdentity;
        }
        else
        {
            var script = $"window.tessalumeSetSequence({payload});";
            await _target.CoreWebView2.ExecuteScriptAsync(script);
        }

        cancellationToken.ThrowIfCancellationRequested();
        IsReady = true;
        _target.Visibility = Visibility.Visible;
    }

    public void SetActive(bool active)
    {
        if (_disposed || !_initialized || _target.CoreWebView2 is null || !IsReady)
        {
            return;
        }
        _ = _target.CoreWebView2.ExecuteScriptAsync(
            $"window.tessalumeSetActive({active.ToString().ToLowerInvariant()});");
        _target.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Hide()
    {
        if (_disposed)
        {
            return;
        }
        SetActive(false);
        _target.Visibility = Visibility.Collapsed;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        if (!_target.IsLoaded || PresentationSource.FromVisual(_target) is null)
        {
            throw new InvalidOperationException(
                "实时图集预览尚未连接到可见窗口。");
        }

        await _target.EnsureCoreWebView2Async().WaitAsync(
            TimeSpan.FromSeconds(10),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var settings = _target.CoreWebView2.Settings;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsBuiltInErrorPageEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        _target.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
        _initialized = true;
    }

    private void CoreWebView2_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        _documentReady?.TrySetResult(e.IsSuccess);
    }

    private static void ValidateSpritesheet(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException("真实宠物图集路径为空。");
        }
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("真实宠物图集必须是 PNG 或 WebP。");
        }
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > MaximumSpritesheetBytes)
        {
            throw new InvalidDataException("真实宠物图集不存在、为空或超过 20 MiB。");
        }
    }

    private static string CreateSequencePayload(
        PetCodexMotionSequence sequence,
        bool reducedMotion,
        bool active) =>
        JsonSerializer.Serialize(new
        {
            layout = sequence.IsShowcase ? "grid" : "single",
            tracks = sequence.Tracks.Select(track => new
            {
                key = track.Key,
                frames = track.Frames.Select(frame => new
                {
                    row = frame.Row,
                    column = frame.Column,
                    durationMs = frame.DurationMilliseconds,
                }),
                loopStartIndex = track.LoopStartIndex,
                startDelayMs = track.StartDelayMilliseconds,
            }),
            reducedMotion,
            active,
        });

    private static string CreateDocument(string atlasUrl, string initialPayload)
    {
        var serializedAtlasUrl = JsonSerializer.Serialize(atlasUrl);
        return $$"""
            <!doctype html>
            <html>
            <head>
              <meta charset="utf-8">
              <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src https://{{VirtualHostName}}; style-src 'unsafe-inline'; script-src 'unsafe-inline'">
              <style>
                * { box-sizing: border-box; }
                html, body { width: 100%; height: 100%; margin: 0; overflow: hidden; background: transparent; }
                body { display: grid; place-items: center; }
                #stage {
                  display: grid;
                  flex: none;
                }
                #stage.single {
                  grid-template-columns: 1fr;
                  grid-template-rows: 1fr;
                }
                #stage.grid {
                  grid-template-columns: repeat(3, 1fr);
                  grid-template-rows: repeat(3, 1fr);
                }
                .sprite {
                  width: 100%;
                  height: 100%;
                  min-width: 0;
                  min-height: 0;
                  flex: none;
                  background-repeat: no-repeat;
                  background-size: 800% 1100%;
                  image-rendering: auto;
                }
              </style>
            </head>
            <body>
              <div id="stage" role="group" aria-label="Tessalume realtime pet preview"></div>
              <script>
                'use strict';
                const atlasUrl = {{serializedAtlasUrl}};
                const stage = document.getElementById('stage');
                let state = {{initialPayload}};
                let runtimes = [];
                let stillImage = null;
                let stillGeneration = 0;

                function fit() {
                  const nativeWidth = state.layout === 'grid' ? 576 : 192;
                  const nativeHeight = state.layout === 'grid' ? 624 : 208;
                  const availableWidth = Math.max(1, document.documentElement.clientWidth);
                  const availableHeight = Math.max(1, document.documentElement.clientHeight);
                  const width = Math.min(
                    availableWidth,
                    availableHeight * nativeWidth / nativeHeight);
                  stage.style.width = `${width}px`;
                  stage.style.height = `${width * nativeHeight / nativeWidth}px`;
                }

                function stopAll() {
                  for (const runtime of runtimes) {
                    if (runtime.timer) {
                      clearTimeout(runtime.timer);
                      runtime.timer = 0;
                    }
                  }
                }

                function buildTracks() {
                  stopAll();
                  stage.replaceChildren();
                  stage.className = state.layout === 'grid' ? 'grid' : 'single';
                  runtimes = state.tracks.map(track => {
                    const sprite = document.createElement(state.reducedMotion ? 'canvas' : 'div');
                    sprite.className = 'sprite';
                    sprite.setAttribute('role', 'img');
                    sprite.setAttribute('aria-label', track.key);
                    if (sprite instanceof HTMLCanvasElement) {
                      sprite.width = 192;
                      sprite.height = 208;
                    } else {
                      sprite.style.backgroundImage = `url(${JSON.stringify(atlasUrl)})`;
                    }
                    stage.appendChild(sprite);
                    return { spec: track, sprite, frameIndex: 0, timer: 0 };
                  });
                  fit();
                }

                function paint(runtime) {
                  if (!runtime.spec.frames.length) return;
                  const frame = runtime.spec.frames[runtime.frameIndex];
                  if (runtime.sprite instanceof HTMLCanvasElement) {
                    const context = runtime.sprite.getContext('2d');
                    context.clearRect(0, 0, 192, 208);
                    if (stillImage) {
                      context.drawImage(
                        stillImage,
                        frame.column * 192,
                        frame.row * 208,
                        192,
                        208,
                        0,
                        0,
                        192,
                        208);
                    }
                  } else {
                    const x = frame.column * 100 / 7;
                    const y = frame.row * 100 / 10;
                    runtime.sprite.style.backgroundPosition = `${x}% ${y}%`;
                  }
                }

                function schedule(runtime) {
                  if (!state.active || state.reducedMotion || runtime.spec.frames.length < 2) return;
                  const delay = runtime.spec.frames[runtime.frameIndex].durationMs;
                  runtime.timer = setTimeout(() => {
                    runtime.frameIndex += 1;
                    if (runtime.frameIndex >= runtime.spec.frames.length) {
                      runtime.frameIndex = runtime.spec.loopStartIndex;
                    }
                    paint(runtime);
                    schedule(runtime);
                  }, delay);
                }

                function restartTracks() {
                  stopAll();
                  for (const runtime of runtimes) {
                    runtime.frameIndex = 0;
                    paint(runtime);
                    if (!state.active || state.reducedMotion) continue;
                    if (runtime.spec.startDelayMs > 0) {
                      runtime.timer = setTimeout(
                        () => schedule(runtime),
                        runtime.spec.startDelayMs);
                    } else {
                      schedule(runtime);
                    }
                  }
                }

                function loadStillAtlas() {
                  const generation = ++stillGeneration;
                  const image = new Image();
                  image.onload = () => {
                    if (generation !== stillGeneration || !state.reducedMotion) return;
                    stillImage = image;
                    restartTracks();
                  };
                  image.src = `${atlasUrl}&still=${generation}`;
                }

                function rebuild() {
                  stillImage = null;
                  buildTracks();
                  if (state.reducedMotion) {
                    loadStillAtlas();
                  } else {
                    restartTracks();
                  }
                }

                window.tessalumeSetSequence = next => {
                  state = next;
                  rebuild();
                };

                window.tessalumeSetActive = active => {
                  state.active = active;
                  if (active) restartTracks(); else stopAll();
                };

                addEventListener('resize', fit);
                rebuild();
              </script>
            </body>
            </html>
            """;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _documentReady?.TrySetCanceled();
        if (_initialized && _target.CoreWebView2 is not null)
        {
            _target.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
        }
        try
        {
            _target.Dispose();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or COMException)
        {
            // The WPF owner may already have torn down the composition target.
        }
        GC.SuppressFinalize(this);
    }
}
