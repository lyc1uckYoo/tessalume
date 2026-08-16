using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Tessalume.App.Features.Pets;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Pets;
using Tessalume.Core.Runtime;

namespace Tessalume.App;

public partial class MainWindow
{
    private readonly PetApplicationServiceOptions? _petOptions;
    private readonly IPetCommandClipboard _petClipboard;
    private readonly CancellationTokenSource _petCancellation = new();
    private PetApplicationService? _petApplicationService;
    private PetCenterPresentationState? _petCenterState;
    private bool _petOperationInProgress;

    private PetApplicationService PetService => _petApplicationService
        ?? throw new InvalidOperationException("Codex 宠物中心尚未初始化。");

    private void InitializePetCenterFeature()
    {
        _petApplicationService = new PetApplicationService(
            _layout,
            _petOptions ?? PetApplicationServiceOptions.ForCurrentUser(_layout.DataDirectory));
        _petCenterState = PetService.CreateLoadingState();
        PetCenterPage.Render(_petCenterState);
        PetCenterPage.RefreshRequested += PetCenterPage_RefreshRequested;
        PetCenterPage.PrimaryActionRequested += PetCenterPage_PrimaryActionRequested;
        PetCenterPage.CopyCommandRequested += PetCenterPage_CopyCommandRequested;
        PetCenterPage.OpenCodexRequested += PetCenterPage_OpenCodexRequested;
        PetCenterPage.RecommendedThemeRequested += PetCenterPage_RecommendedThemeRequested;
        PetCenterPage.ApplyRecommendedThemeRequested += PetCenterPage_ApplyRecommendedThemeRequested;
        PetCenterPage.UninstallRequested += PetCenterPage_UninstallRequested;
        PetCenterPage.SelectionAcknowledgementRequested +=
            PetCenterPage_SelectionAcknowledgementRequested;
        PetCenterPage.RestoreBackupRequested += PetCenterPage_RestoreBackupRequested;
    }

    private async void Pets_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(Features.Navigation.AppRoute.Pets);
        await RefreshPetCenterAsync();
    }

    private async void PetCenterPage_RefreshRequested(object? sender, EventArgs e) =>
        await RefreshPetCenterAsync();

    private async Task RefreshPetCenterAsync()
    {
        if (_petOperationInProgress)
        {
            return;
        }

        _petOperationInProgress = true;
        RenderPetState(PetService.CreateLoadingState(_petCenterState));
        try
        {
            RenderPetState(await PetService.RefreshAsync(_petCancellation.Token));
        }
        catch (OperationCanceledException) when (_petCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or ArgumentException or NotSupportedException)
        {
            LocalLog.Write("Pet center refresh failed.", exception);
            RenderPetState(PetService.CreateErrorState(exception));
        }
        finally
        {
            _petOperationInProgress = false;
        }
    }

    private async void PetCenterPage_PrimaryActionRequested(
        object? sender,
        PetCenterAction action)
    {
        switch (action)
        {
            case PetCenterAction.Refresh:
                await RefreshPetCenterAsync();
                break;
            case PetCenterAction.Install:
                if (await EnsurePetInformationDisclosureAsync())
                {
                    await RunPetMutationAsync(
                        "正在安装",
                        "正在 staging、校验并原子安装飞行雪绒…",
                        cancellationToken => PetService.InstallAsync(
                            PetInstallIntent.Install,
                            cancellationToken),
                        "飞行雪绒已安全安装，请在 Codex 的 Settings → Pets 中 Refresh 并选择它。");
                }
                break;
            case PetCenterAction.OpenCodex:
                await OpenCodexForPetSetupAsync();
                break;
            case PetCenterAction.Update:
                if (ShowProductConfirmation(
                        "更新飞行雪绒",
                        "更新前会完整备份当前受管目录；新版经过 staging 和 SHA-256 校验后才会原子替换。不会修改其他宠物。",
                        "备份并更新") &&
                    await EnsurePetInformationDisclosureAsync())
                {
                    await RunPetMutationAsync(
                        "正在更新",
                        "正在备份旧版本并校验新版…",
                        cancellationToken => PetService.InstallAsync(
                            PetInstallIntent.UpdateConfirmed,
                            cancellationToken),
                        "飞行雪绒已更新；请在 Codex Pets 中 Refresh 后重新选择。");
                }
                break;
            case PetCenterAction.Repair:
                if (ShowProductConfirmation(
                        "修复飞行雪绒",
                        "修复会先备份当前目录，再用内置包中完整校验的运行文件替换损坏内容。",
                        "备份并修复") &&
                    await EnsurePetInformationDisclosureAsync())
                {
                    await RunPetMutationAsync(
                        "正在修复",
                        "正在备份并恢复完整宠物文件…",
                        cancellationToken => PetService.InstallAsync(
                            PetInstallIntent.RepairConfirmed,
                            cancellationToken),
                        "飞行雪绒文件已修复；请回到 Codex Pets 刷新。");
                }
                break;
            case PetCenterAction.RecoverState:
                if (ShowProductConfirmation(
                        "恢复宠物管理状态",
                        "检测到 Tessalume 自己的宠物状态文件损坏。继续会先原样归档损坏文件，再重建空的 schema 1 状态并重新扫描。" +
                        $"\n\n不会修改任何 Codex 宠物文件；只会读取当前用户 Pets 目录：\n{PetService.CodexPetsRoot}" +
                        "\n\n现有同 ID 目录随后会按“非受管冲突”显示，仍需你另行确认后才能替换。",
                        "归档并重建",
                        dangerous: true))
                {
                    await RunPetMutationAsync(
                        "正在恢复管理状态",
                        "正在归档损坏状态并原子重建 schema 1…",
                        PetService.RecoverManagementStateAsync,
                        "损坏状态已保留归档并重新扫描；没有修改任何 Codex 宠物文件。");
                }
                break;
            case PetCenterAction.ReplaceModified:
                if (ShowProductConfirmation(
                        "处理已修改的文件",
                        "检测到未知改动。Tessalume 不会静默覆盖；继续后会先保留完整可恢复备份，再安装已校验的内置文件。",
                        "备份并替换",
                        dangerous: true) &&
                    await EnsurePetInformationDisclosureAsync())
                {
                    await RunPetMutationAsync(
                        "正在安全替换",
                        "正在备份未知改动并重建受管安装…",
                        cancellationToken => PetService.InstallAsync(
                            PetInstallIntent.ReplaceConfirmed,
                            cancellationToken),
                        "已保留原内容备份，并重新安装飞行雪绒。");
                }
                break;
            case PetCenterAction.ExplainConflict:
                if (ShowProductConfirmation(
                        "发现同 ID 宠物冲突",
                        "一个或多个目录都声明了 flying-snowfluff。Tessalume 不会自行猜测；继续会先完整备份这些同 ID 目录，再归并为一个已校验的受管安装。",
                        "全部备份并解决",
                        dangerous: true) &&
                    await EnsurePetInformationDisclosureAsync())
                {
                    await RunPetMutationAsync(
                        "正在解决冲突",
                        "正在备份同 ID 目录并建立唯一受管安装…",
                        cancellationToken => PetService.InstallAsync(
                            PetInstallIntent.ReplaceConfirmed,
                            cancellationToken),
                        "同 ID 目录已备份，飞行雪绒已建立唯一受管安装。");
                }
                break;
        }
    }

    private async Task<bool> EnsurePetInformationDisclosureAsync()
    {
        try
        {
            if (!await PetService.NeedsInformationalDisclosureAsync(_petCancellation.Token))
            {
                return true;
            }

            var confirmed = ShowProductConfirmation(
                "安装范围与隐私",
                $"本次操作只会读写当前用户的 Codex Pets 目录：\n{PetService.CodexPetsRoot}\n\n" +
                "Tessalume 只按 pet.json 的宠物 ID 管理相关文件，不读取聊天、账号、日志或其他 Codex 配置。",
                "继续这次操作");
            if (!confirmed)
            {
                return false;
            }

            await PetService.MarkInformationalDisclosureShownAsync(_petCancellation.Token);
            return true;
        }
        catch (OperationCanceledException) when (_petCancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException)
        {
            LocalLog.Write("Pet disclosure state could not be updated.", exception);
            RenderPetState(PetService.CreateErrorState(exception));
            ShowProductMessage(
                "无法准备宠物操作",
                exception.Message,
                ProductDialogKind.Error);
            return false;
        }
    }

    private async Task RunPetMutationAsync(
        string busyTitle,
        string busyDetail,
        Func<CancellationToken, Task<PetCenterPresentationState>> operation,
        string successMessage)
    {
        if (_petOperationInProgress)
        {
            return;
        }

        _petOperationInProgress = true;
        RenderPetState(PetApplicationService.CreateBusyState(
            _petCenterState ?? PetService.CreateLoadingState(),
            busyTitle,
            busyDetail));
        try
        {
            RenderPetState(await operation(_petCancellation.Token));
            ShowToast(successMessage);
        }
        catch (OperationCanceledException) when (_petCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or ArgumentException or NotSupportedException)
        {
            LocalLog.Write("Pet center operation failed.", exception);
            RenderPetState(PetService.CreateErrorState(exception));
            ShowProductMessage(
                "宠物操作未完成",
                $"没有静默覆盖任何未知内容。\n\n{exception.Message}",
                ProductDialogKind.Error);
        }
        finally
        {
            _petOperationInProgress = false;
        }
    }

    private void PetCenterPage_CopyCommandRequested(object? sender, EventArgs e)
    {
        try
        {
            _petClipboard.Copy(PetApplicationService.WakeCommand);
            ShowToast("已复制 /pet；请粘贴到 Codex，由你决定何时发送。");
        }
        catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
        {
            ShowProductMessage("无法复制命令", exception.Message, ProductDialogKind.Warning);
        }
    }

    private async void PetCenterPage_OpenCodexRequested(object? sender, EventArgs e) =>
        await OpenCodexForPetSetupAsync();

    private async Task OpenCodexForPetSetupAsync()
    {
        try
        {
            await CodexPackageLauncher.OpenCodexAsync(_petCancellation.Token);
            ShowToast("Codex 已打开：Settings → Pets → Refresh → 选择飞行雪绒 → 输入 /pet。");
        }
        catch (OperationCanceledException) when (_petCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or UnauthorizedAccessException or COMException or
            System.ComponentModel.Win32Exception)
        {
            ShowProductMessage(
                "无法打开 Codex",
                $"请确认已安装 Windows 版 Codex，再手动打开。\n\n{exception.Message}",
                ProductDialogKind.Warning);
        }
    }

    private async void PetCenterPage_UninstallRequested(object? sender, EventArgs e)
    {
        if (_petOperationInProgress)
        {
            return;
        }

        var requiresModifiedRemovalConfirmation = _petCenterState?.Status is
            PetCenterStatus.UnknownModification or PetCenterStatus.Damaged;
        if (!await EnsurePetInformationDisclosureAsync() ||
            !ShowProductConfirmation(
                "卸载飞行雪绒",
                requiresModifiedRemovalConfirmation
                    ? "受管文件已被修改、缺失或损坏。卸载前会完整备份目录；只移除 Tessalume 记录的受管文件，未知文件和其他宠物会保留。"
                    : "卸载前会创建可恢复备份；只移除 Tessalume 记录的受管文件，不会删除其他宠物。",
                "备份并卸载",
                dangerous: true))
        {
            return;
        }

        await RunPetMutationAsync(
            "正在卸载",
            "正在创建恢复备份并移除受管文件…",
            cancellationToken => PetService.UninstallAsync(
                requiresModifiedRemovalConfirmation
                    ? PetUninstallIntent.RemoveModifiedManagedFilesConfirmed
                    : PetUninstallIntent.Safe,
                cancellationToken),
            "受管文件已卸载，并保留了可恢复备份。");
    }

    private async void PetCenterPage_RestoreBackupRequested(object? sender, EventArgs e)
    {
        if (_petOperationInProgress)
        {
            return;
        }

        if (!await EnsurePetInformationDisclosureAsync() ||
            !ShowProductConfirmation(
                "恢复最近的宠物备份",
                "恢复前会先为当前目标目录创建安全备份，再完整校验并恢复最近一次飞行雪绒备份；若目录已属于其他宠物 ID 会拒绝覆盖。",
                "备份当前并恢复",
                dangerous: true))
        {
            return;
        }

        await RunPetMutationAsync(
            "正在恢复",
            "正在校验备份并原子恢复…",
            PetService.RestoreLatestBackupAsync,
            "最近的飞行雪绒备份已恢复，请在 Codex Pets 中刷新。");
    }

    private async void PetCenterPage_SelectionAcknowledgementRequested(
        object? sender,
        EventArgs e)
    {
        await RunPetMutationAsync(
            "正在记录",
            "只记录你的确认，不会检测或控制 Codex 界面…",
            PetService.AcknowledgeCodexSelectionAsync,
            "已记录你完成了 Codex 选择；Tessalume 仍不会声称能自动检测显示状态。");
    }

    private void PetCenterPage_RecommendedThemeRequested(object? sender, EventArgs e) =>
        OpenRecommendedPetTheme();

    private void OpenRecommendedPetTheme()
    {
        _themeLibraryFilter = ThemeLibraryFilter.All;
        if (!string.IsNullOrEmpty(ThemeSearchBox.Text))
        {
            ThemeSearchBox.Clear();
        }
        ShowThemeLibraryPage();
        ShowThemes(PetApplicationService.RecommendedThemeId);
        if (_selectedTheme is null ||
            !string.Equals(
                _selectedTheme.ThemeId,
                PetApplicationService.RecommendedThemeId,
                StringComparison.OrdinalIgnoreCase))
        {
            ShowProductMessage(
                "配套主题暂不可用",
                "本地主题库中没有找到爱弥斯 · 星海远航，请先恢复内置主题或刷新主题库。",
                ProductDialogKind.Warning);
            return;
        }

        ThemeDetailPanel.Present(_selectedTheme);
        ThemeDetailPanel.Visibility = Visibility.Visible;
    }

    private async void PetCenterPage_ApplyRecommendedThemeRequested(object? sender, EventArgs e)
    {
        var theme = _themes.FirstOrDefault(candidate => string.Equals(
            candidate.ThemeId,
            PetApplicationService.RecommendedThemeId,
            StringComparison.OrdinalIgnoreCase));
        if (theme is null || !theme.IsValid)
        {
            ShowProductMessage(
                "配套主题暂不可用",
                "本地主题库中没有找到可用的爱弥斯 · 星海远航主题。",
                ProductDialogKind.Warning);
            return;
        }

        // Visiting this page is already an explicit discovery of the pairing;
        // consume the one-time theme-side hint without a redundant toast.
        try
        {
            _ = await PetService.TryClaimCompanionSuggestionAsync(_petCancellation.Token);
        }
        catch (OperationCanceledException) when (_petCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Pet companion suggestion state could not be updated.", exception);
        }
        if (await ApplyThemeAsync(theme))
        {
            ShowToast("爱弥斯主题已应用；宠物安装状态没有被改变。");
        }
    }

    private void RenderPetState(PetCenterPresentationState state)
    {
        _petCenterState = state;
        PetCenterPage.Render(state);
        SetStatus($"Codex 宠物：{state.StatusTitle}");
    }

    private void ScheduleCompanionPetSuggestion(string themeId)
    {
        if (!_uiInitialized || !IsVisible || _petApplicationService is null ||
            !string.Equals(
                themeId,
                PetApplicationService.RecommendedThemeId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = ShowCompanionPetSuggestionAsync();
    }

    private async Task ShowCompanionPetSuggestionAsync()
    {
        try
        {
            if (await PetService.TryClaimCompanionSuggestionAsync(_petCancellation.Token))
            {
                ShowToast("爱弥斯主题有配套的飞行雪绒宠物，可从左栏“Codex 宠物”查看；不会自动安装。");
            }
        }
        catch (OperationCanceledException) when (_petCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LocalLog.Write("Companion pet suggestion state could not be updated.", exception);
        }
    }
}
