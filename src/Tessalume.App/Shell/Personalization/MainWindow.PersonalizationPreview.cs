using Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

namespace Tessalume.App;

public partial class MainWindow
{
    private int _visualApplyVersion;
    private CancellationTokenSource? _visualApplyCancellation;
    private int _preferencesRevision;
    private bool _preferencesDirty;

    private int MarkPreferencesDirty()
    {
        _preferencesDirty = true;
        return ++_preferencesRevision;
    }

    private void MarkPreferencesPersisted(int revision)
    {
        if (revision == _preferencesRevision) _preferencesDirty = false;
    }

    private void MarkVisualSettingsDirtyAndSchedule()
    {
        MarkPreferencesDirty();
        ScheduleVisualSettingsUpdate();
    }

    private void FlushVisualSettingsForContextChange()
    {
        _visualApplyVersion++;
        _visualApplyCancellation?.Cancel();
        _visualSettingsDebounce?.Stop();
        if (!_preferencesDirty) return;
        var version = _visualApplyVersion;
        var preferencesRevision = _preferencesRevision;
        _ = FlushVisualSettingsForContextChangeAsync(version, preferencesRevision);
    }

    private async Task FlushVisualSettingsForContextChangeAsync(
        int version,
        int preferencesRevision)
    {
        try
        {
            await SavePreferencesAsync();
            if (version == _visualApplyVersion)
            {
                MarkPreferencesPersisted(preferencesRevision);
            }
        }
        catch (Exception exception)
        {
            if (version != _visualApplyVersion) return;
            ArtworkWorkbench.SetApplyState(
                ArtworkApplyState.SaveFailed,
                "切换前的参数尚未写入磁盘；下一次修改会重试");
            SetStatus($"本地保存失败，参数仍保留在当前会话：{exception.Message}");
        }
    }

    private void ScheduleVisualSettingsUpdate()
    {
        if (_visualSettingsDebounce is null) return;
        _visualApplyVersion++;
        _visualApplyCancellation?.Cancel();
        _visualSettingsDebounce.Stop();
        _visualSettingsDebounce.Start();
    }

    private async void VisualSettingsDebounce_Tick(object? sender, EventArgs e)
    {
        _visualSettingsDebounce?.Stop();
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;
        var version = _visualApplyVersion;
        _visualApplyCancellation?.Dispose();
        _visualApplyCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _personalizationCancellation.Token);
        var cancellationToken = _visualApplyCancellation.Token;
        var preferencesRevision = _preferencesRevision;

        ArtworkWorkbench.SetApplyState(ArtworkApplyState.Saving, "正在写入本地配置");
        try
        {
            await SavePreferencesAsync();
            if (version == _visualApplyVersion)
            {
                MarkPreferencesPersisted(preferencesRevision);
            }
        }
        catch (OperationCanceledException) when (
            version != _visualApplyVersion || cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            if (version == _visualApplyVersion &&
                string.Equals(themeId, _artworkContextThemeId, StringComparison.OrdinalIgnoreCase))
            {
                ArtworkWorkbench.SetApplyState(
                    ArtworkApplyState.SaveFailed,
                    "尚未写入磁盘；再次调整任一参数即可重试");
                SetStatus($"本地保存失败，当前修改仅保留在内存：{exception.Message}");
            }
            return;
        }

        if (version != _visualApplyVersion || cancellationToken.IsCancellationRequested) return;
        try
        {
            if (!string.Equals(themeId, _activeThemeId, StringComparison.OrdinalIgnoreCase))
            {
                ArtworkWorkbench.SetApplyState(
                    _artworkCodexConnectionVerified
                        ? ArtworkApplyState.Connected
                        : ArtworkApplyState.Disconnected,
                    "已保存，应用主题后生效");
                SetStatus($"{theme.Name} 的显示与图像参数已保存，应用主题后生效");
                return;
            }

            var port = await ResolveArtworkDebugPortAsync(cancellationToken);
            if (port is null)
            {
                _artworkCodexConnectionVerified = false;
                ArtworkWorkbench.SetConnectionState(false);
                ArtworkWorkbench.SetApplyState(ArtworkApplyState.Disconnected, "已保存；连接后自动应用");
                SetStatus("显示与图像参数已保存；Codex 下次连接时自动生效");
                return;
            }

            if (version != _visualApplyVersion || cancellationToken.IsCancellationRequested) return;
            _artworkCodexConnectionVerified = true;
            ArtworkWorkbench.SetConnectionState(true);
            ArtworkWorkbench.SetApplyState(ArtworkApplyState.Applying, "正在同步当前最新参数");
            await _runtime.ApplyVisualSettingsAsync(
                port.Value,
                themeId,
                GetVisualSettings(themeId),
                cancellationToken);
            if (version != _visualApplyVersion || cancellationToken.IsCancellationRequested) return;
            ArtworkWorkbench.SetApplyState(ArtworkApplyState.Applied, "当前最新参数已生效");
            SetStatus($"已实时更新 {theme.Name} 的显示与图像参数");
        }
        catch (OperationCanceledException) when (
            version != _visualApplyVersion || cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (version == _visualApplyVersion &&
                string.Equals(themeId, _artworkContextThemeId, StringComparison.OrdinalIgnoreCase))
            {
                ArtworkWorkbench.SetApplyState(ArtworkApplyState.Failed, exception.Message);
                SetStatus($"参数已保存到本机，但实时应用失败：{exception.Message}");
                _ = ProbeArtworkConnectionAsync(themeId);
            }
        }
    }
}
