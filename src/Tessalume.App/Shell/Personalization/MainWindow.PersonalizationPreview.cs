using System.Windows.Input;
using Tessalume.Core.Runtime;

namespace Tessalume.App;

public partial class MainWindow
{
    private async void VisualOriginalPreview_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!VisualOriginalPreviewButton.IsEnabled) return;
        VisualOriginalPreviewButton.CaptureMouse();
        await SetOriginalPreviewAsync(showOriginal: true);
        e.Handled = true;
    }

    private async void VisualOriginalPreview_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        await SetOriginalPreviewAsync(showOriginal: false);
        VisualOriginalPreviewButton.ReleaseMouseCapture();
        e.Handled = true;
    }

    private async void VisualOriginalPreview_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_visualOriginalPreviewActive) await SetOriginalPreviewAsync(showOriginal: false);
    }

    private async Task SetOriginalPreviewAsync(bool showOriginal)
    {
        if (_visualOriginalPreviewActive == showOriginal) return;
        var version = ++_visualOriginalPreviewVersion;
        _visualOriginalPreviewActive = showOriginal;
        VisualOriginalPreviewButton.Content = showOriginal ? "正在显示原图" : "按住看原图";
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId ||
            !string.Equals(themeId, _activeThemeId, StringComparison.OrdinalIgnoreCase))
        {
            _visualOriginalPreviewActive = false;
            VisualOriginalPreviewButton.Content = "按住看原图";
            return;
        }

        var state = await _stateStore.LoadAsync();
        var port = _activePort ?? state?.Port;
        if (port is null || !await _launcher.IsDebugPortReadyAsync(port.Value))
        {
            if (version == _visualOriginalPreviewVersion)
            {
                _visualOriginalPreviewActive = false;
                VisualOriginalPreviewButton.Content = "按住看原图";
                SetStatus("Codex 尚未连接，暂时无法对比原图");
            }
            return;
        }
        if (version != _visualOriginalPreviewVersion) return;
        try
        {
            if (showOriginal)
            {
                _visualSettingsDebounce?.Stop();
                await SavePreferencesAsync();
            }
            var settings = GetVisualSettings(themeId);
            var preview = showOriginal
                ? _editingVisualDarkMode
                    ? settings with { Dark = new ThemeVisualModeSettings() }
                    : settings with { Light = new ThemeVisualModeSettings() }
                : settings;
            await _runtime.ApplyVisualSettingsAsync(port.Value, themeId, preview.Normalize());
            if (version == _visualOriginalPreviewVersion)
            {
                SetStatus(showOriginal ? "正在临时显示主题原图" : "已恢复个人图像参数");
            }
        }
        catch (Exception exception)
        {
            if (version == _visualOriginalPreviewVersion)
            {
                _visualOriginalPreviewActive = false;
                VisualOriginalPreviewButton.Content = "按住看原图";
                SetStatus($"原图对比失败：{exception.Message}");
            }
        }
    }

    private void ScheduleVisualSettingsUpdate()
    {
        if (_visualSettingsDebounce is null) return;
        _visualSettingsDebounce.Stop();
        _visualSettingsDebounce.Start();
    }

    private async void VisualSettingsDebounce_Tick(object? sender, EventArgs e)
    {
        _visualSettingsDebounce?.Stop();
        ResetVisualHistoryCoalescing();
        var theme = GetVisualAdjustmentTheme();
        if (theme?.ThemeId is not { Length: > 0 } themeId) return;

        try
        {
            await SavePreferencesAsync();
            if (!string.Equals(themeId, _activeThemeId, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus($"{theme.Name} 的显示与图像参数已保存，应用主题后生效");
                return;
            }

            var state = await _stateStore.LoadAsync();
            var port = _activePort ?? state?.Port;
            if (port is null || !await _launcher.IsDebugPortReadyAsync(port.Value))
            {
                SetStatus("显示与图像参数已保存；Codex 下次连接时自动生效");
                return;
            }

            await _runtime.ApplyVisualSettingsAsync(port.Value, themeId, GetVisualSettings(themeId));
            SetStatus($"已实时更新 {theme.Name} 的显示与图像参数");
        }
        catch (Exception exception)
        {
            SetStatus($"显示与图像参数已保留，但实时更新失败：{exception.Message}");
        }
    }
}
