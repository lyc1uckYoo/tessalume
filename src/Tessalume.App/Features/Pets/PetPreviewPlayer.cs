using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Tessalume.App.Features.Pets;

/// <summary>
/// Decodes and retains only the selected product GIF. Frames are downsampled to
/// the visible stage, released on selection/route changes, and never installed
/// into the user's Codex pet directory.
/// </summary>
internal sealed class PetPreviewPlayer : IDisposable
{
    private const double DefaultStageWidth = 560;
    private const double DefaultStageHeight = 500;

    private readonly Image _target;
    private readonly TextBlock _label;
    private readonly DispatcherTimer _timer;
    private readonly IPetMotionPreference _motionPreference;
    private readonly SystemPetMotionPreference? _ownedMotionPreference;
    private readonly PetCodexRuntimeRenderer? _runtimeRenderer;
    private IReadOnlyList<PetPreviewFrame> _previews = [];
    private IReadOnlyList<BitmapSource> _frames = [];
    private IReadOnlyList<TimeSpan> _frameDurations = [];
    private CancellationTokenSource? _loadCancellation;
    private Task _loadTask = Task.CompletedTask;
    private PetPreviewFrame? _selection;
    private int _loadGeneration;
    private int _frameIndex;
    private double _stageWidth = DefaultStageWidth;
    private double _stageHeight = DefaultStageHeight;
    private bool _active;
    private bool _reducedMotion;
    private bool _runtimeAnimating;
    private bool _usesRealtimeAtlas;
    private bool _disposed;

    public PetPreviewPlayer(
        Image target,
        TextBlock label,
        IPetMotionPreference? motionPreference = null,
        WebView2CompositionControl? runtimeTarget = null)
    {
        _target = target;
        _label = label;
        if (motionPreference is null)
        {
            var systemPreference = new SystemPetMotionPreference();
            _motionPreference = systemPreference;
            _ownedMotionPreference = systemPreference;
        }
        else
        {
            _motionPreference = motionPreference;
        }
        _motionPreference.Changed += MotionPreference_Changed;
        if (runtimeTarget is not null)
        {
            _runtimeRenderer = new PetCodexRuntimeRenderer(runtimeTarget);
        }
        _timer = new DispatcherTimer(DispatcherPriority.Background, target.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _timer.Tick += Timer_Tick;
    }

    internal event EventHandler? PlaybackStateChanged;

    internal event EventHandler? SelectionChanged;

    internal string? CurrentKey => _selection?.Key;

    internal int CurrentFrameIndex => _frameIndex;

    internal int DecodedFrameCount => _frames.Count;

    internal long EstimatedDecodedBytes { get; private set; }

    internal bool IsReducedMotion => _reducedMotion;

    internal bool IsAnimating => _runtimeAnimating || _timer.IsEnabled;

    internal bool UsesRealtimeAtlas => _usesRealtimeAtlas;

    internal bool HasVisual => _target.Source is not null || _runtimeRenderer?.IsVisible == true;

    internal int RuntimeSequenceFrameCount { get; private set; }

    internal string PlaybackDescription { get; private set; } = "选择动作后开始预览";

    internal Task WaitForCurrentLoadAsync() => _loadTask;

    public void Configure(IReadOnlyList<PetPreviewFrame> previews)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalized = previews
            .Where(preview =>
                !string.IsNullOrWhiteSpace(preview.FilePath) ||
                (!string.IsNullOrWhiteSpace(preview.RuntimeSpritesheetPath) &&
                 PetCodexMotionSchedule.TryCreate(preview.Key, out _)))
            .ToArray();
        if (_previews.SequenceEqual(normalized))
        {
            return;
        }

        var previousKey = _selection?.Key;
        _previews = normalized;
        var first = _previews.Count == 0 ? null : _previews[0];
        var next = previousKey is null
            ? first
            : _previews.FirstOrDefault(preview =>
                string.Equals(preview.Key, previousKey, StringComparison.OrdinalIgnoreCase)) ??
              first;
        ChangeSelection(next, forceReload: true);
    }

    public void Select(string key)
    {
        if (_disposed)
        {
            return;
        }
        var next = _previews.FirstOrDefault(preview =>
            string.Equals(preview.Key, key, StringComparison.OrdinalIgnoreCase));
        if (next is null || Equals(next, _selection))
        {
            return;
        }
        ChangeSelection(next, forceReload: true);
    }

    public void SetDisplayBounds(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return;
        }
        _stageWidth = width;
        _stageHeight = height;
    }

    public void SetActive(bool active)
    {
        if (_disposed)
        {
            return;
        }
        if (_active == active)
        {
            if (active && _selection is not null && !HasVisual && _loadTask.IsCompleted)
            {
                StartLoadingCurrent();
            }
            return;
        }

        _active = active;
        if (!_active)
        {
            CancelLoadAndReleaseFrames();
            PlaybackDescription = _selection is null ? "暂无动态预览" : "页面不可见，预览已暂停";
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        StartLoadingCurrent();
    }

    private void ChangeSelection(PetPreviewFrame? next, bool forceReload)
    {
        if (!forceReload && Equals(next, _selection))
        {
            return;
        }
        CancelLoadAndReleaseFrames();
        _selection = next;
        _frameIndex = 0;
        _label.Text = next?.Label ?? "动态预览待加载";
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        if (next is null)
        {
            PlaybackDescription = "暂无动态预览";
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (_active)
        {
            StartLoadingCurrent();
        }
        else
        {
            PlaybackDescription = "进入宠物页面后加载当前动作";
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void StartLoadingCurrent()
    {
        if (!_active || _selection is null || _disposed)
        {
            return;
        }

        CancelLoadAndReleaseFrames();
        var selection = _selection;
        var generation = ++_loadGeneration;
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        _reducedMotion = _motionPreference.IsReducedMotion;
        var (pixelWidth, pixelHeight) = GetRequestedPixelBounds();
        PetCodexMotionSequence? runtimeSequence = null;
        var useRuntime = false;
        if (_runtimeRenderer is not null &&
            !string.IsNullOrWhiteSpace(selection.RuntimeSpritesheetPath) &&
            PetCodexMotionSchedule.TryCreate(selection.Key, out var resolvedSequence))
        {
            runtimeSequence = resolvedSequence;
            useRuntime = true;
        }
        PlaybackDescription = useRuntime
            ? "正在加载实时图集…"
            : _reducedMotion
                ? "正在加载代表帧…"
                : "正在解码兼容预览…";
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        _loadTask = useRuntime
            ? LoadRuntimeCurrentAsync(
                selection,
                runtimeSequence!,
                _reducedMotion,
                generation,
                cancellation.Token)
            : LoadGifCurrentAsync(
                selection,
                pixelWidth,
                pixelHeight,
                _reducedMotion,
                generation,
                cancellation.Token);
    }

    private async Task LoadRuntimeCurrentAsync(
        PetPreviewFrame selection,
        PetCodexMotionSequence sequence,
        bool reducedMotion,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await _runtimeRenderer!.ShowAsync(
                selection.RuntimeSpritesheetPath!,
                selection.RuntimeSpritesheetRevision,
                sequence,
                reducedMotion,
                active: true,
                cancellationToken);
            if (!CanPublishLoad(selection, generation, cancellationToken))
            {
                _runtimeRenderer.Hide();
                return;
            }

            _target.Source = null;
            _frames = [];
            _frameDurations = [];
            EstimatedDecodedBytes = 0;
            RuntimeSequenceFrameCount = sequence.TotalFrameCount;
            _frameIndex = 0;
            _usesRealtimeAtlas = true;
            _runtimeAnimating = !reducedMotion && sequence.TotalFrameCount > 1;
            PlaybackDescription = reducedMotion
                ? "实时图集 · 系统减少动态 · 显示静止画面"
                : sequence.IsShowcase
                    ? "实时图集 · 九动作独立循环"
                    : !sequence.MatchesCodexState
                        ? $"实时图集 · 16 向连续检查 · {sequence.Frames.Count} 帧"
                    : sequence.ReturnsToIdle
                        ? $"实时图集 · 动作 {sequence.ActionCycleCount} 轮后进入待机"
                        : "实时图集 · 待机 6.6 秒循环";
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Selection, route, or window lifetime moved on.
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or ArgumentException or FormatException or COMException or
            InvalidOperationException or TimeoutException or WebView2RuntimeNotFoundException)
        {
            if (!CanPublishLoad(selection, generation, cancellationToken))
            {
                return;
            }

            _runtimeRenderer?.Hide();
            if (string.IsNullOrWhiteSpace(selection.FilePath))
            {
                ReleaseFrames();
                PlaybackDescription = "实时图集暂不可用；当前项目没有 GIF 兼容预览";
                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            PlaybackDescription = "实时图集暂不可用，正在切换 GIF 兼容预览…";
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            var (pixelWidth, pixelHeight) = GetRequestedPixelBounds();
            await LoadGifCurrentAsync(
                selection,
                pixelWidth,
                pixelHeight,
                reducedMotion,
                generation,
                cancellationToken);
        }
    }

    private async Task LoadGifCurrentAsync(
        PetPreviewFrame selection,
        int pixelWidth,
        int pixelHeight,
        bool reducedMotion,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var decoded = await Task.Run(
                () => PetGifFrameDecoder.Decode(
                    selection,
                    pixelWidth,
                    pixelHeight,
                    reducedMotion,
                    cancellationToken),
                CancellationToken.None);
            if (!CanPublishLoad(selection, generation, cancellationToken))
            {
                return;
            }

            _frames = decoded.Frames;
            _frameDurations = decoded.FrameDurations;
            EstimatedDecodedBytes = decoded.EstimatedDecodedBytes;
            RuntimeSequenceFrameCount = 0;
            _usesRealtimeAtlas = false;
            _runtimeAnimating = false;
            _frameIndex = 0;
            _target.Source = _frames[0];
            PlaybackDescription = decoded.ReducedMotion
                ? "GIF 兼容回退 · 系统减少动态 · 显示代表帧"
                : $"GIF 兼容回退 · 循环播放 · {_frames.Count} 帧";
            UpdateTimer();
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Selection, route, or window lifetime moved on.
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or ArgumentException or FormatException or COMException or
            OutOfMemoryException)
        {
            if (!CanPublishLoad(selection, generation, cancellationToken))
            {
                return;
            }
            ReleaseFrames();
            PlaybackDescription = "动态预览暂不可用，安装包仍会独立校验";
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool CanPublishLoad(
        PetPreviewFrame selection,
        int generation,
        CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        !_disposed &&
        _active &&
        generation == _loadGeneration &&
        Equals(selection, _selection);

    private (int Width, int Height) GetRequestedPixelBounds()
    {
        var dpi = VisualTreeHelper.GetDpi(_target);
        var width = (int)Math.Ceiling(_stageWidth * Math.Max(1d, dpi.DpiScaleX));
        var height = (int)Math.Ceiling(_stageHeight * Math.Max(1d, dpi.DpiScaleY));
        return (
            Math.Clamp(width, 1, PetGifFrameDecoder.MaximumDecodeDimension),
            Math.Clamp(height, 1, PetGifFrameDecoder.MaximumDecodeDimension));
    }

    private void UpdateTimer()
    {
        var shouldRun = _active && !_reducedMotion && _frames.Count > 1 && !_disposed;
        if (shouldRun)
        {
            _timer.Interval = _frameDurations[_frameIndex];
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }
        else
        {
            _timer.Stop();
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_active || _reducedMotion || _frames.Count <= 1 || _disposed)
        {
            UpdateTimer();
            return;
        }

        _frameIndex = (_frameIndex + 1) % _frames.Count;
        _target.Source = _frames[_frameIndex];
        _timer.Interval = _frameDurations[_frameIndex];
    }

    private void MotionPreference_Changed(object? sender, EventArgs e)
    {
        if (_disposed || _target.Dispatcher.HasShutdownStarted || _target.Dispatcher.HasShutdownFinished)
        {
            return;
        }
        if (_target.Dispatcher.CheckAccess())
        {
            ReloadForMotionPreference();
            return;
        }
        _target.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(ReloadForMotionPreference));
    }

    private void ReloadForMotionPreference()
    {
        if (_active && !_disposed && _selection is not null)
        {
            StartLoadingCurrent();
        }
    }

    private void CancelLoadAndReleaseFrames()
    {
        _loadGeneration++;
        var cancellation = _loadCancellation;
        _loadCancellation = null;
        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
        _timer.Stop();
        _runtimeRenderer?.Hide();
        ReleaseFrames();
    }

    private void ReleaseFrames()
    {
        _target.Source = null;
        _frames = [];
        _frameDurations = [];
        _frameIndex = 0;
        EstimatedDecodedBytes = 0;
        RuntimeSequenceFrameCount = 0;
        _runtimeAnimating = false;
        _usesRealtimeAtlas = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _active = false;
        CancelLoadAndReleaseFrames();
        _timer.Tick -= Timer_Tick;
        _motionPreference.Changed -= MotionPreference_Changed;
        _ownedMotionPreference?.Dispose();
        _runtimeRenderer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
