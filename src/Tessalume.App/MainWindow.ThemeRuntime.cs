using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Tessalume.App.Creator;
using Tessalume.App.Features.Diagnostics;
using Tessalume.App.Infrastructure;
using Tessalume.App.Models;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;
using Tessalume.Core.Updates;
using Microsoft.Win32;

namespace Tessalume.App;

public partial class MainWindow
{
    private async void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTheme is not null)
        {
            await ApplyThemeAsync(_selectedTheme);
        }
    }

    private async Task<bool> ApplyThemeAsync(ThemeCardModel theme)
    {
        if (theme.CatalogItem.Package is not { } package) return false;
        var result = await ApplyPackageAsync(
            package,
            allowCodexStart: true,
            showFailureDialog: true,
            CancellationToken.None);
        if (result.Succeeded)
        {
            await RecordThemeUsageAsync(package.Manifest.Id);
            ScheduleCompanionPetSuggestion(package.Manifest.Id);
        }
        return result.Succeeded;
    }

    private async Task<ThemeApplicationResult> ApplyPackageAsync(
        ThemePackage package,
        bool allowCodexStart,
        bool showFailureDialog,
        CancellationToken cancellationToken)
    {
        SetBusy(true, "正在连接本机 Codex…");
        StudioState? previousState = null;
        int? resolvedPort = null;
        try
        {
            previousState = await _stateStore.LoadAsync(cancellationToken);
            var port = await ResolveThemeRuntimePortAsync(allowCodexStart, cancellationToken);
            if (port is null)
            {
                var unavailableMessage = allowCodexStart
                    ? "已取消应用，Codex 保持当前状态"
                    : "Codex 当前没有可用的本地主题连接";
                SetStatus(unavailableMessage);
                if (!allowCodexStart)
                {
                    await RecordCompatibilityFailureAsync(
                        previousState,
                        ThemeRuntimeFailureStage.PortUnavailable,
                        unavailableMessage,
                        cancellationToken);
                }
                return new ThemeApplicationResult(false, unavailableMessage);
            }

            resolvedPort = port.Value;
            var activeCompatibilityPack = _compatibilityPacks.Resolve();
            var compatibility = await CompatibilityHealthService.InspectAsync(
                previousState,
                activeCompatibilityPack,
                cancellationToken);
            var requiresPreflight = compatibility.RequiresPreflight ||
                                    previousState?.LastSuccessfulApplyAt is null;
            try
            {
                if (requiresPreflight)
                {
                    SetStatus(compatibility.RequiresPreflight
                        ? "检测到 Codex 或主题运行时版本变化，正在重新预检…"
                        : "正在完成首次兼容性预检…");
                    await _runtime.PreflightAsync(port.Value, package, cancellationToken);
                }

                SetStatus("正在应用本地主题…");
                await _runtime.StartAsync(
                    port.Value,
                    package,
                    GetVisualSettings(package.Manifest.Id),
                    cancellationToken);
            }
            catch (ThemeRuntimeException exception) when (
                !activeCompatibilityPack.IsBuiltIn &&
                exception.Stage == ThemeRuntimeFailureStage.RuntimeInjectionFailed)
            {
                var fallback = _compatibilityPacks.Rollback();
                LocalLog.Write(
                    $"Compatibility pack {activeCompatibilityPack.PackVersionLabel} failed at runtime; " +
                    $"rolled back to {fallback.PackVersionLabel}.",
                    exception);
                SetStatus($"兼容补丁运行失败，已自动回退到 {fallback.PackVersionLabel} 并重新验证…");
                await _runtime.PreflightAsync(port.Value, package, cancellationToken);
                await _runtime.StartAsync(
                    port.Value,
                    package,
                    GetVisualSettings(package.Manifest.Id),
                    cancellationToken);
                ShowToast($"兼容规则已自动回退到 {fallback.PackVersionLabel}");
            }
            await LegacyInjectorMigrator.TryStopAsync(cancellationToken);
            await _stateStore.SaveAsync((previousState ?? new StudioState()) with
            {
                Port = port.Value,
                ThemeId = package.Manifest.Id,
                UpdatedAt = DateTimeOffset.Now,
                Enabled = true,
                LastSuccessfulApplyAt = DateTimeOffset.Now,
                CodexVersionAtLastApply = compatibility.InstalledCodexVersion ??
                    previousState?.CodexVersionAtLastApply,
                RuntimeContractVersion = ThemeRuntime.ContractVersion,
                CompatibilityPackVersionAtLastApply = _compatibilityPacks.Resolve().PackVersion.ToString(),
                LastFailureStage = ThemeRuntimeFailureStage.None,
                LastFailureMessage = null,
                LastFailureAt = null,
            }, cancellationToken);
            _activePort = port.Value;
            _artworkCodexConnectionVerified = true;
            _activeThemeId = package.Manifest.Id;
            _lastThemeId = _activeThemeId;
            UpdateAppliedThemeState();
            SetEngineState($"运行中 · 本机 {port.Value}");
            var message = $"{package.Manifest.Name} 已应用，可继续实时切换";
            SetStatus(message);
            LocalLog.Write($"Applied theme {package.Manifest.Id} on port {port.Value}.");
            RefreshQuickSwitchWindow();
            UpdateVisualAdjustmentControls();
            return new ThemeApplicationResult(true, message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failureStage = ClassifyRuntimeFailure(exception);
            await RecordCompatibilityFailureAsync(
                previousState,
                failureStage,
                exception.Message,
                cancellationToken,
                resolvedPort);
            LocalLog.Write($"Applying theme {package.Manifest.Id} failed.", exception);
            SetEngineState("启动失败");
            SetStatus(exception.Message);
            if (showFailureDialog)
            {
                ShowProductMessage(
                    "无法应用主题",
                    $"{exception.Message}\n\n建议：{CompatibilityHealthService.GetRecommendation(failureStage)}",
                    ProductDialogKind.Error);
            }
            return new ThemeApplicationResult(false, exception.Message);
        }
        finally
        {
            SetBusy(false, null);
            IdleMemoryTrimmer.Schedule();
        }
    }

    private async Task RecordCompatibilityFailureAsync(
        StudioState? previousState,
        ThemeRuntimeFailureStage stage,
        string message,
        CancellationToken cancellationToken,
        int? port = null)
    {
        try
        {
            await _stateStore.SaveAsync((previousState ?? new StudioState()) with
            {
                Port = port ?? previousState?.Port ?? 0,
                UpdatedAt = DateTimeOffset.Now,
                LastFailureStage = stage,
                LastFailureMessage = message,
                LastFailureAt = DateTimeOffset.Now,
            }, cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LocalLog.Write("Persisting compatibility failure state failed.", exception);
        }
    }

    private static ThemeRuntimeFailureStage ClassifyRuntimeFailure(Exception exception)
    {
        if (exception is ThemeRuntimeException runtimeException)
        {
            return runtimeException.Stage;
        }

        if (exception is TimeoutException or System.Net.Sockets.SocketException)
        {
            return ThemeRuntimeFailureStage.PortUnavailable;
        }

        if (exception.Message.Contains("not installed", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("未找到 Codex", StringComparison.OrdinalIgnoreCase))
        {
            return ThemeRuntimeFailureStage.CodexNotFound;
        }

        return ThemeRuntimeFailureStage.RuntimeInjectionFailed;
    }

    private async Task<int?> ResolveThemeRuntimePortAsync(
        bool allowCodexStart,
        CancellationToken cancellationToken)
    {
        var state = await _stateStore.LoadAsync(cancellationToken);
        var port = state?.Port ?? 0;
        if (port <= 0 || !await _launcher.IsDebugPortReadyAsync(port, cancellationToken))
        {
            port = await _launcher.FindRunningDebugPortAsync(cancellationToken) ?? 0;
        }

        if (port > 0) return port;
        if (!allowCodexStart) return null;

        if (CodexPackageLauncher.IsCodexRunning())
        {
            var confirmed = ShowProductConfirmation(
                "需要重新启动 Codex",
                "Codex 当前没有可用的主题连接。为了应用所选主题，需要关闭并重新启动 Codex。\n\n请先保存正在编辑的内容并确认当前任务可以中断。",
                "已保存，重新启动");
            if (!confirmed) return null;

            SetStatus("正在关闭并重新启动 Codex…");
            await CodexPackageLauncher.CloseCodexAsync(cancellationToken);
        }

        port = CodexPackageLauncher.FindFreePort();
        SetStatus($"正在本机端口 {port} 启动 Codex…");
        await _launcher.LaunchAndWaitAsync(port, cancellationToken);
        return port;
    }

    private async Task<CreatorRuntimeActionResult> ApplyCreatorProjectAsync(
        string projectDirectory,
        bool automatic,
        CancellationToken cancellationToken)
    {
        try
        {
            var loadResult = await new ThemePackageLoader().LoadAsync(projectDirectory, cancellationToken);
            if (!loadResult.Validation.IsValid || loadResult.Package is not { } package)
            {
                var issue = loadResult.Validation.Issues.FirstOrDefault(item =>
                    item.Severity == ThemeValidationSeverity.Error);
                var message = issue is null
                    ? "主题项目未能生成可应用的运行时包。"
                    : $"{issue.Message}{(string.IsNullOrWhiteSpace(issue.Path) ? string.Empty : $"\n{issue.Path}")}";
                return new CreatorRuntimeActionResult(
                    false,
                    await ReadCreatorRuntimeStatusAsync(cancellationToken),
                    message);
            }

            var result = await ApplyPackageAsync(
                package,
                allowCodexStart: !automatic,
                showFailureDialog: false,
                cancellationToken);
            return new CreatorRuntimeActionResult(
                result.Succeeded,
                await ReadCreatorRuntimeStatusAsync(cancellationToken),
                result.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new CreatorRuntimeActionResult(
                false,
                CreatorRuntimeStatus.Disconnected(exception.Message),
                exception.Message);
        }
    }

    private async Task<CreatorRuntimeStatus> ReadCreatorRuntimeStatusAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var port = await ResolveThemeRuntimePortAsync(
                allowCodexStart: false,
                cancellationToken);
            if (port is null)
            {
                return CreatorRuntimeStatus.Disconnected();
            }

            _activePort = port.Value;
            try
            {
                var dark = await _runtime.ReadColorSchemeAsync(port.Value, cancellationToken);
                _codexDarkMode = dark;
                UpdateCodexModeButton();
                return new CreatorRuntimeStatus(true, port.Value, dark);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new CreatorRuntimeStatus(true, port.Value, null, exception.Message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return CreatorRuntimeStatus.Disconnected(exception.Message);
        }
    }

    private async Task<CreatorRuntimeStatus> ToggleCreatorRuntimeColorSchemeAsync(
        CancellationToken cancellationToken)
    {
        var dark = await ToggleCodexColorSchemeAsync(showFailureDialog: false, cancellationToken);
        return dark is null
            ? await ReadCreatorRuntimeStatusAsync(cancellationToken)
            : new CreatorRuntimeStatus(true, _activePort, dark);
    }

    private async void RestoreTheme_Click(object sender, RoutedEventArgs e)
    {
        await RestoreDefaultAsync();
    }

    private async void SettingsRestoreTheme_Click(object sender, RoutedEventArgs e)
    {
        await ToggleRestoreThemeAsync();
        UpdateSettingsVisualHeader();
    }

    private async Task<bool> ToggleRestoreThemeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_activeThemeId))
        {
            return await RestoreDefaultAsync();
        }

        var lastTheme = _themes.FirstOrDefault(theme =>
            theme.IsValid && string.Equals(theme.ThemeId, _lastThemeId, StringComparison.OrdinalIgnoreCase));
        if (lastTheme is null)
        {
            SetStatus("没有可恢复的上一主题");
            return false;
        }

        return await ApplyThemeAsync(lastTheme);
    }

    private async Task<bool> RestoreDefaultAsync()
    {
        var state = await _stateStore.LoadAsync();
        var port = _activePort ?? state?.Port;
        if (port is null || !await _launcher.IsDebugPortReadyAsync(port.Value))
        {
            SetStatus("当前没有活动的本地主题");
            return false;
        }

        SetBusy(true, "正在恢复 Codex 默认外观…");
        try
        {
            await LegacyInjectorMigrator.TryStopAsync();
            await _runtime.RemoveAsync(port.Value);
            await _stateStore.SaveAsync((state ?? new StudioState()) with
            {
                Port = port.Value,
                ThemeId = _selectedTheme?.CatalogItem.Package?.Manifest.Id ?? state?.ThemeId ?? string.Empty,
                UpdatedAt = DateTimeOffset.Now,
                Enabled = false,
            });
            SetEngineState("Codex 默认外观");
            if (!string.IsNullOrWhiteSpace(_activeThemeId))
            {
                _lastThemeId = _activeThemeId;
            }
            _activeThemeId = null;
            UpdateAppliedThemeState();
            SetStatus("本地主题已移除，Codex 安装文件未被修改");
            RefreshQuickSwitchWindow();
            UpdateVisualAdjustmentControls();
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            ShowProductMessage("恢复失败", exception.Message, ProductDialogKind.Error);
            return false;
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void StudioMode_Click(object sender, RoutedEventArgs e)
    {
        _darkMode = string.Equals((sender as Button)?.Tag?.ToString(), "dark", StringComparison.OrdinalIgnoreCase);
        ApplyStudioTheme(_darkMode);
        await SavePreferencesAsync();
    }

    private async void CodexMode_Click(object sender, RoutedEventArgs e)
    {
        await ToggleCodexColorSchemeAsync();
    }

    private Task<bool?> ToggleCodexColorSchemeAsync() =>
        ToggleCodexColorSchemeAsync(showFailureDialog: true, CancellationToken.None);

    private async Task<bool?> ToggleCodexColorSchemeAsync(
        bool showFailureDialog,
        CancellationToken cancellationToken)
    {
        var port = await ResolveThemeRuntimePortAsync(
            allowCodexStart: false,
            cancellationToken);
        if (port is null)
        {
            SetStatus($"请先用 {BrandInfo.ProductName} 启动 Codex");
            return null;
        }

        SetBusy(true, "正在切换 Codex 明暗色…");
        try
        {
            _activePort = port.Value;
            var dark = await _runtime.ToggleColorSchemeAsync(port.Value, cancellationToken);
            _codexDarkMode = dark;
            if (_currentRoute is Features.Navigation.AppRoute.ArtworkStudio or Features.Navigation.AppRoute.DisplayPreferences)
            {
                _editingVisualDarkMode = dark;
            }
            UpdateCodexModeButton();
            if (_currentRoute is Features.Navigation.AppRoute.ArtworkStudio or Features.Navigation.AppRoute.DisplayPreferences)
            {
                UpdateVisualAdjustmentControls();
            }

            SetStatus(dark ? "Codex 已切换为暗色" : "Codex 已切换为亮色");
            return dark;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            if (showFailureDialog)
            {
                ShowProductMessage("无法切换 Codex 明暗色", exception.Message, ProductDialogKind.Error);
            }
            return null;
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async Task<bool?> ReadCodexColorSchemeAsync()
    {
        try
        {
            var port = await ResolveThemeRuntimePortAsync(
                allowCodexStart: false,
                CancellationToken.None);
            if (port is null)
            {
                return null;
            }

            _activePort = port.Value;
            var dark = await _runtime.ReadColorSchemeAsync(port.Value);
            _codexDarkMode = dark;
            if (_currentRoute is Features.Navigation.AppRoute.ArtworkStudio or Features.Navigation.AppRoute.DisplayPreferences)
            {
                _editingVisualDarkMode = dark;
            }
            UpdateCodexModeButton();
            if (_currentRoute is Features.Navigation.AppRoute.ArtworkStudio or Features.Navigation.AppRoute.DisplayPreferences)
            {
                UpdateVisualAdjustmentControls();
            }
            return dark;
        }
        catch
        {
            return null;
        }
    }

    private async Task RefreshCodexColorSchemeAsync()
    {
        var dark = await ReadCodexColorSchemeAsync();
        if (dark is null)
        {
            return;
        }

        _codexDarkMode = dark;
        UpdateCodexModeButton();
    }

    private sealed record ThemeApplicationResult(bool Succeeded, string Message);

}
