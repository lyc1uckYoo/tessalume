using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Runtime;

namespace Tessalume.App;

public partial class MainWindow
{
    private string? _artworkContextThemeId;
    private bool _artworkCodexConnectionVerified;
    private int _artworkConnectionProbeVersion;
    private CancellationTokenSource? _artworkConnectionProbeCancellation;
    private DispatcherTimer? _artworkConnectionTimer;

    private void InitializeArtworkWorkbench()
    {
        ArtworkWorkbench.Configure(_personalImageStore);
        ArtworkWorkbench.SettingsChanged += ArtworkWorkbench_SettingsChanged;
        ArtworkWorkbench.EditingModeChanged += ArtworkWorkbench_EditingModeChanged;
        ArtworkWorkbench.ChooseImageRequested += ArtworkWorkbench_ChooseImageRequested;
        ArtworkWorkbench.NotificationRequested += message => ShowToast(message);
        _artworkConnectionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = ArtworkSurfaceMetricsProbeGate.RefreshInterval,
        };
        _artworkConnectionTimer.Tick += ArtworkConnectionTimer_Tick;
    }

    private void UpdateArtworkWorkbenchContext()
    {
        if (!_uiInitialized || ArtworkWorkbench is null) return;
        var theme = GetVisualAdjustmentTheme();
        var themeId = theme?.ThemeId;
        if (!string.Equals(
                _artworkContextThemeId,
                themeId,
                StringComparison.OrdinalIgnoreCase))
        {
            _artworkContextThemeId = themeId;
            FlushVisualSettingsForContextChange();
        }
        var settings = string.IsNullOrWhiteSpace(themeId)
            ? new ThemeVisualSettings()
            : GetVisualSettings(themeId);
        var resolution = string.IsNullOrWhiteSpace(themeId)
            ? null
            : GetVisualSettingsResolution(themeId);
        ArtworkWorkbench.SetContext(new ArtworkWorkbenchContext(
            themeId,
            theme?.Name ?? "尚未选择主题",
            theme?.CatalogItem.Package,
            settings,
            _editingVisualDarkMode ? ArtworkColorMode.Dark : ArtworkColorMode.Light,
            !string.IsNullOrWhiteSpace(themeId) && string.Equals(
                themeId,
                _activeThemeId,
                StringComparison.OrdinalIgnoreCase),
            _artworkCodexConnectionVerified,
            resolution));
        _ = ProbeArtworkConnectionAsync(themeId);
    }

    private async Task ProbeArtworkConnectionAsync(string? themeId)
    {
        if (Volatile.Read(ref _disposeStarted) != 0) return;
        _artworkConnectionProbeCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _personalizationCancellation.Token);
        _artworkConnectionProbeCancellation = cancellation;
        var cancellationToken = cancellation.Token;
        var version = ++_artworkConnectionProbeVersion;
        var connected = false;
        int? debugPort = null;
        try
        {
            debugPort = await ResolveArtworkDebugPortAsync(cancellationToken);
            connected = debugPort is not null;
            if (debugPort is { } port && !string.IsNullOrWhiteSpace(themeId))
            {
                await RefreshArtworkSurfaceMetricsAsync(
                    port,
                    themeId,
                    version,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            ArgumentOutOfRangeException or
            ObjectDisposedException or
            System.Net.Http.HttpRequestException)
        {
            connected = false;
        }
        finally
        {
            if (ReferenceEquals(_artworkConnectionProbeCancellation, cancellation))
            {
                _artworkConnectionProbeCancellation = null;
            }
            cancellation.Dispose();
        }

        if (version != _artworkConnectionProbeVersion ||
            !string.Equals(
                themeId,
                _artworkContextThemeId,
                StringComparison.OrdinalIgnoreCase)) return;
        var wasConnected = _artworkCodexConnectionVerified;
        _artworkCodexConnectionVerified = connected;
        ArtworkWorkbench.SetConnectionState(connected);
        if (!connected)
        {
            ClearArtworkSurfaceMetrics();
        }
        if (connected && !wasConnected &&
            !string.IsNullOrWhiteSpace(themeId) &&
            string.Equals(themeId, _activeThemeId, StringComparison.OrdinalIgnoreCase))
        {
            // Reconnection is an explicit synchronization point. Saved offline
            // edits are applied even when the user makes no further adjustment.
            ScheduleVisualSettingsUpdate();
        }
    }

    private async Task RefreshArtworkSurfaceMetricsAsync(
        int port,
        string themeId,
        int probeVersion,
        CancellationToken cancellationToken)
    {
        var snapshot = await _runtime.InspectArtworkSurfaceMetricsAsync(
            port,
            themeId,
            cancellationToken);
        var disposition = ArtworkSurfaceMetricsProbeGate.Evaluate(
            snapshot,
            probeVersion,
            _artworkConnectionProbeVersion,
            themeId,
            _artworkContextThemeId,
            _editingVisualDarkMode);
        if (disposition == ArtworkSurfaceMetricsProbeDisposition.IgnoreStale) return;
        if (disposition == ArtworkSurfaceMetricsProbeDisposition.ClearCurrent)
        {
            ClearArtworkSurfaceMetrics();
            return;
        }

        // Evaluate guarantees a non-null, current snapshot for this branch.
        var currentSnapshot = snapshot!;
        SetArtworkSurfaceMetric(ArtworkRegion.Hero, currentSnapshot.Hero, currentSnapshot);
        SetArtworkSurfaceMetric(ArtworkRegion.Sidebar, currentSnapshot.Sidebar, currentSnapshot);
        SetArtworkSurfaceMetric(ArtworkRegion.Chat, currentSnapshot.Chat, currentSnapshot);
    }

    private void SetArtworkSurfaceMetric(
        ArtworkRegion region,
        ThemeArtworkSurfaceMetric metric,
        ThemeArtworkSurfaceMetricsSnapshot snapshot)
    {
        if (!metric.Available || metric.Rect is not { Width: > 0d, Height: > 0d } rect)
        {
            ArtworkWorkbench.SetSurfaceMetrics(
                region,
                new ArtworkSurfacePreviewMetrics(
                    1d,
                    1d,
                    snapshot.DevicePixelRatio,
                    IsLive: false,
                    $"当前 Codex {snapshot.Route} 路由无法测量此区域：" +
                    (metric.UnavailableReason ?? "surface 不可用")));
            return;
        }
        var computed = metric.Computed;
        ArtworkWorkbench.SetSurfaceMetrics(
            region,
            new ArtworkSurfacePreviewMetrics(
                rect.Width,
                rect.Height,
                snapshot.DevicePixelRatio,
                IsLive: true,
                $"当前 Codex {snapshot.Route} surface · " +
                $"构图协议 v{snapshot.ArtworkCompositionProtocolVersion} · " +
                $"background-size {computed?.BackgroundSize ?? "未知"} · " +
                $"background-position {computed?.BackgroundPosition ?? "未知"} · " +
                $"transform {computed?.Transform ?? "未知"} · " +
                $"translate {computed?.Translate ?? "未知"} · " +
                $"scale {computed?.Scale ?? "未知"}"));
    }

    private void ClearArtworkSurfaceMetrics()
    {
        if (!_uiInitialized || ArtworkWorkbench is null) return;
        foreach (var region in Enum.GetValues<ArtworkRegion>())
        {
            ArtworkWorkbench.SetSurfaceMetrics(region, null);
        }
    }

    private async Task<int?> ResolveArtworkDebugPortAsync(CancellationToken cancellationToken)
    {
        var state = await _stateStore.LoadAsync(cancellationToken);
        var discovered = await _launcher.FindRunningDebugPortAsync(
            [state?.PreferredDebugPort, _activePort, state?.Port],
            cancellationToken);
        if (discovered is > 0 and <= 65535) _activePort = discovered;
        return discovered;
    }

    private void SetArtworkConnectionMonitoring(bool enabled)
    {
        if (_artworkConnectionTimer is null) return;
        if (enabled)
        {
            _artworkConnectionTimer.Start();
            _ = ProbeArtworkConnectionAsync(_artworkContextThemeId);
        }
        else
        {
            _artworkConnectionTimer.Stop();
            CancelArtworkConnectionProbe();
        }
    }

    private void ArtworkConnectionTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentRoute == Features.Navigation.AppRoute.ArtworkStudio)
        {
            _ = ProbeArtworkConnectionAsync(_artworkContextThemeId);
        }
    }

    private void DisposeArtworkWorkbench()
    {
        CancelArtworkConnectionProbe();
        if (_artworkConnectionTimer is not null)
        {
            _artworkConnectionTimer.Stop();
            _artworkConnectionTimer.Tick -= ArtworkConnectionTimer_Tick;
            _artworkConnectionTimer = null;
        }
        ArtworkWorkbench?.Dispose();
    }

    private void CancelArtworkConnectionProbe()
    {
        _artworkConnectionProbeVersion++;
        _artworkConnectionProbeCancellation?.Cancel();
        _artworkConnectionProbeCancellation = null;
    }

    private void ArtworkWorkbench_SettingsChanged(
        object? sender,
        ArtworkWorkbenchSettingsChangedEventArgs e)
    {
        SetResolvedVisualSettings(e.ThemeId, e.Settings);
        if (string.Equals(
                e.ThemeId,
                _artworkContextThemeId,
                StringComparison.OrdinalIgnoreCase))
        {
            ArtworkWorkbench.SetResolution(GetVisualSettingsResolution(e.ThemeId));
        }
        MarkVisualSettingsDirtyAndSchedule();
    }

    private void ArtworkWorkbench_EditingModeChanged(
        object? sender,
        ArtworkEditingModeChangedEventArgs e)
    {
        _editingVisualDarkMode = e.Mode == ArtworkColorMode.Dark;
        UpdateSettingsVisualHeader();
        // Do not leave the canvas showing the opposite color mode's computed
        // values until the periodic refresh. A mode switch is an explicit metrics
        // invalidation point and should probe the live surface immediately.
        _ = ProbeArtworkConnectionAsync(_artworkContextThemeId);
    }

}
