using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

internal enum ArtworkApplyState
{
    Disconnected,
    Connected,
    Loading,
    Pending,
    Applying,
    Applied,
    Failed,
    Saving,
    SaveFailed,
    PreviewFailed,
    ImportFailed,
}

internal sealed record ArtworkWorkbenchContext(
    string? ThemeId,
    string ThemeName,
    ThemePackage? Package,
    ThemeVisualSettings Settings,
    ArtworkColorMode EditingMode,
    bool IsApplied,
    bool IsCodexConnected,
    ThemeVisualSettingsResolution? Resolution = null);

internal sealed record ArtworkSurfacePreviewMetrics(
    double Width,
    double Height,
    double DeviceScaleFactor,
    bool IsLive,
    string? Detail = null)
{
    public ArtworkSurfacePreviewMetrics Normalize()
    {
        var width = double.IsFinite(Width) ? Math.Clamp(Width, 1d, 10000d) : 1d;
        var height = double.IsFinite(Height) ? Math.Clamp(Height, 1d, 10000d) : 1d;
        var scale = double.IsFinite(DeviceScaleFactor)
            ? Math.Clamp(DeviceScaleFactor, .5d, 8d)
            : 1d;
        return this with
        {
            Width = width,
            Height = height,
            DeviceScaleFactor = scale,
            Detail = string.IsNullOrWhiteSpace(Detail) ? null : Detail.Trim(),
        };
    }
}

internal sealed class ArtworkWorkbenchSettingsChangedEventArgs(
    string themeId,
    ThemeVisualSettings settings,
    string description) : EventArgs
{
    public string ThemeId { get; } = themeId;

    public ThemeVisualSettings Settings { get; } = settings;

    public string Description { get; } = description;
}

internal sealed class ArtworkEditingModeChangedEventArgs(ArtworkColorMode mode) : EventArgs
{
    public ArtworkColorMode Mode { get; } = mode;
}

internal sealed class ArtworkChooseImageEventArgs(
    string themeId,
    ArtworkColorMode mode,
    ArtworkRegion region) : EventArgs
{
    public string ThemeId { get; } = themeId;

    public ArtworkColorMode Mode { get; } = mode;

    public ArtworkRegion Region { get; } = region;
}

public partial class ArtworkWorkbenchView : UserControl, IDisposable
{
    private readonly ArtworkWorkbenchSession _session = new();
    private readonly ArtworkPreviewImageCache _imageCache = new();
    private readonly Dictionary<string, ThemeVisualSettings> _knownThemeSettings =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ArtworkRegion, ArtworkSurfacePreviewMetrics> _surfaceMetrics = [];
    private readonly DispatcherTimer _effectTimer;
    private readonly DispatcherTimer _wheelTimer;
    private readonly DispatcherTimer _previewLayoutTimer;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _effectCancellation;
    private PersonalImageStore? _personalImageStore;
    private ThemePackage? _package;
    private ThemeVisualSettings _settings = new();
    private ThemeVisualSettingsResolution? _settingsResolution;
    private ArtworkPlacementProjection? _compositionGestureStart;
    private BitmapSource? _originalPreview;
    private BitmapSource? _themeOriginalPreview;
    private ArtworkImageSource? _resolvedSource;
    private ArtworkRegion _region = ArtworkRegion.Hero;
    private ArtworkColorMode _mode = ArtworkColorMode.Light;
    private string? _themeId;
    private string _themeName = "尚未选择主题";
    private bool _isApplied;
    private bool _isCodexConnected;
    private bool _showOriginal;
    private bool _compositionEditing;
    private ArtworkCanvasViewMode _canvasViewMode = ArtworkCanvasViewMode.Result;
    private ArtworkCanvasViewMode? _comparisonReturnViewMode;
    private bool _previewReloadRequired;
    private bool _disposed;
    private ArtworkApplyState _applyState = ArtworkApplyState.Disconnected;
    private int _previewVersion;
    private int _effectVersion;
    private int _lastDecodeWidth;

    public ArtworkWorkbenchView()
    {
        InitializeComponent();
        _effectTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(75),
        };
        _effectTimer.Tick += EffectTimer_Tick;
        _wheelTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(360),
        };
        _wheelTimer.Tick += WheelTimer_Tick;
        _previewLayoutTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(220),
        };
        _previewLayoutTimer.Tick += PreviewLayoutTimer_Tick;

        Inspector.NumericValueChanged += Inspector_NumericValueChanged;
        Inspector.TextValueChanged += Inspector_TextValueChanged;
        Inspector.InteractionStarted += Inspector_InteractionStarted;
        Inspector.InteractionCompleted += Inspector_InteractionCompleted;
        Inspector.GroupChanged += Inspector_GroupChanged;
        Inspector.ResetParameterRequested += Inspector_ResetParameterRequested;
        Inspector.ResetGroupRequested += Inspector_ResetGroupRequested;
        Inspector.ResetRegionRequested += Inspector_ResetRegionRequested;
        Inspector.ChooseImageRequested += Inspector_ChooseImageRequested;
        Inspector.ClearImageRequested += Inspector_ClearImageRequested;
        Inspector.PlacementChanged += Inspector_PlacementChanged;
        Inspector.RestoreOriginalBaselineRequested +=
            Inspector_RestoreOriginalBaselineRequested;
        Inspector.PlacementEditingStarted += Inspector_PlacementEditingStarted;
        Inspector.PlacementEditingCompleted += Inspector_PlacementEditingCompleted;

        PreviewCanvas.InteractionStarted += PreviewCanvas_InteractionStarted;
        PreviewCanvas.DragRequested += PreviewCanvas_DragRequested;
        PreviewCanvas.CropFrameChanged += PreviewCanvas_CropFrameChanged;
        PreviewCanvas.InteractionCompleted += PreviewCanvas_InteractionCompleted;
        PreviewCanvas.ZoomRequested += PreviewCanvas_ZoomRequested;
        PreviewCanvas.SizeChanged += PreviewCanvas_SizeChanged;
        Unloaded += ArtworkWorkbenchView_Unloaded;
        Loaded += ArtworkWorkbenchView_Loaded;

        RenderAll();
        SetApplyState(ArtworkApplyState.Disconnected, "仍可编辑与预览");
    }

    internal event EventHandler<ArtworkWorkbenchSettingsChangedEventArgs>? SettingsChanged;

    internal event EventHandler<ArtworkEditingModeChangedEventArgs>? EditingModeChanged;

    internal event EventHandler<ArtworkChooseImageEventArgs>? ChooseImageRequested;

    internal event Action<string>? NotificationRequested;

    internal ThemeVisualSettings CurrentSettings => _settings;

    internal ArtworkColorMode EditingMode => _mode;

    internal ArtworkRegion EditingRegion => _region;

    internal void SetSurfaceMetrics(
        ArtworkRegion region,
        ArtworkSurfacePreviewMetrics? metrics)
    {
        if (metrics is null)
        {
            if (!_surfaceMetrics.Remove(region)) return;
        }
        else
        {
            var normalized = metrics.Normalize();
            if (_surfaceMetrics.TryGetValue(region, out var current) && current == normalized) return;
            _surfaceMetrics[region] = normalized;
        }
        // Runtime probes refresh every few seconds. Never let an unchanged metrics
        // tick rewrite an exact-input TextBox while the user is typing.
        if (_region == region)
        {
            RenderSurfaceMetrics();
            RenderMappingHint();
            if (!Inspector.IsPlacementEditing)
            {
                RenderPlacementEditor(CurrentAdjustment);
            }
        }
    }

    internal void SetResolution(ThemeVisualSettingsResolution? resolution)
    {
        if (resolution is not null &&
            !string.Equals(resolution.ThemeId, _themeId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _settingsResolution = resolution;
        RenderAll();
    }

    internal void Configure(PersonalImageStore personalImageStore)
    {
        ArgumentNullException.ThrowIfNull(personalImageStore);
        _personalImageStore = personalImageStore;
    }

    internal void SetContext(ArtworkWorkbenchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var normalizedThemeId = string.IsNullOrWhiteSpace(context.ThemeId)
            ? null
            : context.ThemeId.Trim();
        var themeChanged = !string.Equals(
            _themeId,
            normalizedThemeId,
            StringComparison.OrdinalIgnoreCase);
        var preserveOperationFailure = !themeChanged && _applyState is
            ArtworkApplyState.SaveFailed or
            ArtworkApplyState.ImportFailed or
            ArtworkApplyState.Failed;
        if (themeChanged && _themeId is not null)
        {
            _session.History.CancelGesture(_themeId);
        }

        var normalizedSettings = context.Settings.Normalize();
        if (normalizedThemeId is not null &&
            _knownThemeSettings.TryGetValue(normalizedThemeId, out var known) &&
            !ArtworkParametersEqual(known, normalizedSettings))
        {
            // Another personalization surface changed the
            // same theme. Old artwork snapshots form a stale branch and must not
            // overwrite that external update on the next Undo.
            _session.History.Clear(normalizedThemeId);
        }

        _themeId = normalizedThemeId;
        _themeName = string.IsNullOrWhiteSpace(context.ThemeName)
            ? "尚未选择主题"
            : context.ThemeName.Trim();
        _package = context.Package;
        _settingsResolution = context.Resolution;
        _settings = normalizedSettings;
        if (themeChanged)
        {
            _compositionEditing = false;
        }
        if (_themeId is not null) _knownThemeSettings[_themeId] = _settings;
        _mode = context.EditingMode;
        _isApplied = context.IsApplied;
        _isCodexConnected = context.IsCodexConnected;
        RenderAll();
        QueuePreviewReload();
        if (preserveOperationFailure) return;
        if (!_isApplied)
        {
            SetApplyState(
                _isCodexConnected ? ArtworkApplyState.Connected : ArtworkApplyState.Disconnected,
                _isCodexConnected
                    ? "Codex 已连接；应用当前主题后参数生效"
                    : "参数已保存，应用主题后生效");
        }
        else if (!_isCodexConnected)
        {
            SetApplyState(ArtworkApplyState.Disconnected, "仍可编辑；连接后应用");
        }
        else
        {
            SetApplyState(ArtworkApplyState.Applied, "当前主题已连接");
        }
    }

    internal void SetApplyState(ArtworkApplyState state, string detail) =>
        RenderApplyState(state, detail);

    internal void SetConnectionState(bool connected)
    {
        if (_isCodexConnected == connected) return;
        _isCodexConnected = connected;
        RenderSurfaceMetrics();
        if (!connected)
        {
            if (_applyState is not (
                    ArtworkApplyState.Loading or
                    ArtworkApplyState.Pending or
                    ArtworkApplyState.Saving or
                    ArtworkApplyState.Applying or
                    ArtworkApplyState.SaveFailed or
                    ArtworkApplyState.PreviewFailed or
                    ArtworkApplyState.ImportFailed or
                    ArtworkApplyState.Failed))
            {
                SetApplyState(ArtworkApplyState.Disconnected, "仍可编辑；连接后安全应用");
            }
        }
        else if (_applyState == ArtworkApplyState.Disconnected)
        {
            SetApplyState(
                _isApplied ? ArtworkApplyState.Pending : ArtworkApplyState.Connected,
                _isApplied ? "连接可用，等待同步最新参数" : "应用主题后参数生效");
        }
    }

    internal bool TrySetCustomImagePath(
        string themeId,
        ArtworkColorMode mode,
        ArtworkRegion region,
        string? storedPath)
    {
        if (!CanEdit() || !string.Equals(
                _themeId,
                themeId,
                StringComparison.OrdinalIgnoreCase)) return false;
        return ApplyDiscrete(
            settings => ArtworkSettingsReducer.UpdateAdjustment(
                settings,
                mode,
                region,
                adjustment => adjustment with { CustomImagePath = storedPath }),
            string.IsNullOrWhiteSpace(storedPath) ? "使用主题原图" : "更换本地图片",
            reloadSource: true);
    }

    internal void Undo()
    {
        if (!CanEdit() ||
            !_session.TryUndo(_themeId!, _settings, out var restored)) return;
        // Artwork history must never roll back display preferences that may have
        // changed elsewhere in Personalization after this snapshot was created.
        _settings = restored with { Display = _settings.Display };
        CompleteSettingsChange("撤销图像修改", reloadSource: true);
    }

    internal void Redo()
    {
        if (!CanEdit() ||
            !_session.TryRedo(_themeId!, _settings, out var restored)) return;
        _settings = restored with { Display = _settings.Display };
        CompleteSettingsChange("重做图像修改", reloadSource: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _effectTimer.Stop();
        _wheelTimer.Stop();
        _previewLayoutTimer.Stop();
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
        _effectCancellation?.Cancel();
        _effectCancellation?.Dispose();
        _effectCancellation = null;
        _imageCache.Clear();
        _knownThemeSettings.Clear();
        GC.SuppressFinalize(this);
    }

    private void ArtworkWorkbenchView_Unloaded(object sender, RoutedEventArgs e)
    {
        _previewReloadRequired = CanEdit();
        _previewLayoutTimer.Stop();
        _previewCancellation?.Cancel();
        _effectCancellation?.Cancel();
    }

    private bool CanEdit() => !_disposed && _themeId is not null && _package is not null;

    private static bool ArtworkParametersEqual(
        ThemeVisualSettings left,
        ThemeVisualSettings right) =>
        ThemeVisualSettingsSemanticComparer.AdjustmentEquals(left.Light.Hero, right.Light.Hero) &&
        ThemeVisualSettingsSemanticComparer.AdjustmentEquals(left.Light.Sidebar, right.Light.Sidebar) &&
        ThemeVisualSettingsSemanticComparer.AdjustmentEquals(left.Light.Chat, right.Light.Chat) &&
        ThemeVisualSettingsSemanticComparer.AdjustmentEquals(left.Dark.Hero, right.Dark.Hero) &&
        ThemeVisualSettingsSemanticComparer.AdjustmentEquals(left.Dark.Sidebar, right.Dark.Sidebar) &&
        ThemeVisualSettingsSemanticComparer.AdjustmentEquals(left.Dark.Chat, right.Dark.Chat);
}
