using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Tessalume.App.Features.Pets;

/// <summary>
/// Plays a small, bounded set of product-preview frames. It deliberately never
/// decodes the full runtime atlas and only runs while its owning page is visible.
/// </summary>
internal sealed class PetPreviewPlayer : IDisposable
{
    private const int MaximumCachedFrames = 6;
    private readonly Image _target;
    private readonly TextBlock _label;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, BitmapSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<PetPreviewFrame> _frames = [];
    private int _frameIndex;
    private DateTimeOffset _manualHoldUntil;
    private bool _active;
    private bool _disposed;

    public PetPreviewPlayer(Image target, TextBlock label)
    {
        _target = target;
        _label = label;
        _timer = new DispatcherTimer(DispatcherPriority.Background, target.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(900),
        };
        _timer.Tick += Timer_Tick;
    }

    public void Configure(IReadOnlyList<PetPreviewFrame> frames)
    {
        _frames = frames
            .Where(frame => !string.IsNullOrWhiteSpace(frame.FilePath) && File.Exists(frame.FilePath))
            .Take(MaximumCachedFrames)
            .ToArray();
        _frameIndex = 0;
        _manualHoldUntil = default;
        _cache.Clear();
        RenderCurrentFrame();
        UpdateTimer();
    }

    public void Select(string key)
    {
        var index = -1;
        for (var position = 0; position < _frames.Count; position++)
        {
            if (string.Equals(_frames[position].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                index = position;
                break;
            }
        }

        if (index < 0)
        {
            return;
        }

        _frameIndex = index;
        _manualHoldUntil = DateTimeOffset.UtcNow.AddSeconds(5);
        RenderCurrentFrame();
    }

    public void SetActive(bool active)
    {
        _active = active;
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        var shouldRun = _active && _frames.Count > 1 && !_disposed;
        if (shouldRun && !_timer.IsEnabled)
        {
            _timer.Start();
        }
        else if (!shouldRun && _timer.IsEnabled)
        {
            _timer.Stop();
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_frames.Count == 0 || DateTimeOffset.UtcNow < _manualHoldUntil)
        {
            return;
        }

        _frameIndex = (_frameIndex + 1) % _frames.Count;
        RenderCurrentFrame();
    }

    private void RenderCurrentFrame()
    {
        if (_frames.Count == 0)
        {
            _target.Source = null;
            _label.Text = "产品预览待加载";
            return;
        }

        var frame = _frames[Math.Clamp(_frameIndex, 0, _frames.Count - 1)];
        if (frame.FilePath is not { } path)
        {
            return;
        }

        try
        {
            if (!_cache.TryGetValue(path, out var source))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = 288;
                bitmap.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                source = bitmap;
                _cache[path] = source;
            }

            _target.Source = source;
            _label.Text = frame.Label;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ArgumentException)
        {
            _target.Source = null;
            _label.Text = "预览暂不可用，安装包仍会独立校验";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        _cache.Clear();
        _target.Source = null;
    }
}
