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
    private readonly PetGalleryServiceOptions? _petGalleryOptions;
    private readonly CancellationTokenSource _petCancellation = new();
    private PetApplicationService? _petApplicationService;
    private PetGalleryService? _petGalleryService;
    private PetGallerySnapshot? _petGallerySnapshot;
    private PetGalleryEntry? _selectedPetEntry;
    private PetCenterPresentationState? _petCenterState;
    private bool _petOperationInProgress;

    private PetApplicationService PetService => _petApplicationService
        ?? throw new InvalidOperationException("Codex 宠物画廊尚未初始化。");

    private PetGalleryService PetGalleryService => _petGalleryService
        ?? throw new InvalidOperationException("Codex 宠物画廊尚未初始化。");

    private void InitializePetCenterFeature()
    {
        _petApplicationService = new PetApplicationService(
            _layout,
            _petOptions ?? PetApplicationServiceOptions.ForCurrentUser(_layout.DataDirectory));
        _petGalleryService = new PetGalleryService(_layout, _petGalleryOptions);
        _petGalleryService.PackagesChanged += PetGalleryService_PackagesChanged;
        PetCenterPage.ShowGalleryLoading();
        PetCenterPage.PetRequested += PetCenterPage_PetRequested;
        PetCenterPage.GalleryRefreshRequested += PetCenterPage_GalleryRefreshRequested;
        PetCenterPage.BackToGalleryRequested += PetCenterPage_BackToGalleryRequested;
        PetCenterPage.RefreshRequested += PetCenterPage_RefreshRequested;
        PetCenterPage.PrimaryActionRequested += PetCenterPage_PrimaryActionRequested;
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
        PetCenterPage.ShowGallery();
        await RefreshPetGalleryAsync(showGallery: true);
    }

    private async void PetCenterPage_PetRequested(object? sender, PetGalleryEntry entry)
    {
        await OpenPetGalleryEntryAsync(entry);
    }

    private async Task OpenPetGalleryEntryAsync(PetGalleryEntry entry)
    {
        if (_petOperationInProgress)
        {
            return;
        }

        await RefreshPetGalleryAsync(showGallery: false);
        entry = _petGallerySnapshot?.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.EntryKey, entry.EntryKey, StringComparison.OrdinalIgnoreCase)) ??
            entry;
        if (!entry.CanOpen)
        {
            return;
        }

        _selectedPetEntry = entry;
        PetService.SelectEntry(entry);
        _petCenterState = null;
        RenderPetState(PetService.CreateLoadingState(
            detail: "正在读取最新宠物资源并校验安装状态…"));
        await RefreshPetCenterAsync();
    }

    private async Task OpenRecommendedCompanionPetAsync()
    {
        PetCenterPage.ShowGallery();
        await RefreshPetGalleryAsync(showGallery: true);
        var officialEntry = _petGallerySnapshot?.Entries.FirstOrDefault(entry =>
            string.Equals(
                entry.PetId,
                PetApplicationService.BuiltInPetId,
                StringComparison.OrdinalIgnoreCase));
        if (officialEntry is not null)
        {
            await OpenPetGalleryEntryAsync(officialEntry);
        }
    }

    private async void PetCenterPage_GalleryRefreshRequested(object? sender, EventArgs e) =>
        await RefreshPetGalleryAsync(showGallery: true);

    private void PetCenterPage_BackToGalleryRequested(object? sender, EventArgs e)
    {
        _selectedPetEntry = null;
        _petCenterState = null;
        SetStatus("宠物画廊：选择一个角色伙伴查看完整动作");
        InfoScroll.ScrollToTop();
    }

    private async Task RefreshPetGalleryAsync(bool showGallery)
    {
        if (_petOperationInProgress)
        {
            return;
        }

        _petOperationInProgress = true;
        if (showGallery)
        {
            PetCenterPage.ShowGalleryLoading();
        }
        try
        {
            var snapshot = await Task.Run(
                () => PetGalleryService.ScanAsync(_petCancellation.Token),
                CancellationToken.None);
            _petGallerySnapshot = snapshot;
            if (showGallery)
            {
                PetCenterPage.RenderGallery(snapshot);
                InfoScroll.ScrollToTop();
            }
            else
            {
                PetCenterPage.UpdateGalleryData(snapshot);
            }
            SetStatus($"宠物画廊：{snapshot.Entries.Count} 个角色伙伴");
        }
        catch (OperationCanceledException) when (_petCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or ArgumentException or NotSupportedException)
        {
            LocalLog.Write("Pet gallery refresh failed.", exception);
            ShowProductMessage(
                "无法刷新宠物画廊",
                exception.Message,
                ProductDialogKind.Error);
        }
        finally
        {
            _petOperationInProgress = false;
        }
    }

    private void PetGalleryService_PackagesChanged(object? sender, EventArgs e)
    {
        if (_petCancellation.IsCancellationRequested || Dispatcher.HasShutdownStarted)
        {
            return;
        }
        _ = Dispatcher.InvokeAsync(RefreshPetResourcesAsync);
    }

    private async Task RefreshPetResourcesAsync()
    {
        if (_currentRoute != Features.Navigation.AppRoute.Pets || _petOperationInProgress)
        {
            return;
        }

        if (PetCenterPage.IsShowingGallery)
        {
            await RefreshPetGalleryAsync(showGallery: true);
            return;
        }

        await ReloadSelectedPetEntryAsync(showToast: true);
    }

    private async Task ReloadSelectedPetEntryAsync(bool showToast)
    {
        var selectedKey = _selectedPetEntry?.EntryKey;
        if (selectedKey is null)
        {
            await RefreshPetCenterAsync();
            return;
        }

        await RefreshPetGalleryAsync(showGallery: false);
        if (_petGallerySnapshot is null)
        {
            return;
        }

        var refreshed = _petGallerySnapshot.Entries.FirstOrDefault(entry =>
            string.Equals(entry.EntryKey, selectedKey, StringComparison.OrdinalIgnoreCase));
        if (refreshed is null || !refreshed.CanOpen)
        {
            return;
        }

        _selectedPetEntry = refreshed;
        PetService.SelectEntry(refreshed);
        await RefreshPetCenterAsync();
        if (showToast)
        {
            ShowToast($"{refreshed.DisplayName}资源与预览已刷新");
        }
    }

    private async void PetCenterPage_RefreshRequested(object? sender, EventArgs e)
    {
        if (_selectedPetEntry is not null)
        {
            await ReloadSelectedPetEntryAsync(showToast: false);
            return;
        }

        await RefreshPetCenterAsync();
    }

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
        var petName = PetService.CurrentPetDisplayName;
        var petId = PetService.CurrentPetId;
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
                        $"正在 staging、校验并原子安装{petName}…",
                        cancellationToken => PetService.InstallAsync(
                            PetInstallIntent.Install,
                            cancellationToken),
                        $"{petName}已安全安装，请在 Codex 的 Settings → Pets 中 Refresh 并选择它。");
                }
                break;
            case PetCenterAction.OpenCodex:
                await OpenCodexForPetSetupAsync();
                break;
            case PetCenterAction.Update:
                if (ShowProductConfirmation(
                        $"更新{petName}",
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
                        $"{petName}已更新；请在 Codex Pets 中 Refresh 后重新选择。");
                }
                break;
            case PetCenterAction.Repair:
                if (ShowProductConfirmation(
                        $"修复{petName}",
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
                        $"{petName}文件已修复；请回到 Codex Pets 刷新。");
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
                        $"已保留原内容备份，并重新安装{petName}。");
                }
                break;
            case PetCenterAction.ExplainConflict:
                if (ShowProductConfirmation(
                        "发现同 ID 宠物冲突",
                        $"一个或多个目录都声明了 {petId}。Tessalume 不会自行猜测；继续会先完整备份这些同 ID 目录，再归并为一个已校验的受管安装。",
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
                        $"同 ID 目录已备份，{petName}已建立唯一受管安装。");
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

    private async void PetCenterPage_OpenCodexRequested(object? sender, EventArgs e) =>
        await OpenCodexForPetSetupAsync();

    private async Task OpenCodexForPetSetupAsync()
    {
        try
        {
            await CodexPackageLauncher.OpenCodexAsync(_petCancellation.Token);
            ShowToast($"Codex 已打开：Settings → Pets → Refresh → 选择{PetService.CurrentPetDisplayName} → 输入 /pet。");
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
        var petName = PetService.CurrentPetDisplayName;
        if (!await EnsurePetInformationDisclosureAsync() ||
            !ShowProductConfirmation(
                $"卸载{petName}",
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
                $"恢复前会先为当前目标目录创建安全备份，再完整校验并恢复最近一次{PetService.CurrentPetDisplayName}备份；若目录已属于其他宠物 ID 会拒绝覆盖。",
                "备份当前并恢复",
                dangerous: true))
        {
            return;
        }

        await RunPetMutationAsync(
            "正在恢复",
            "正在校验备份并原子恢复…",
            PetService.RestoreLatestBackupAsync,
            $"最近的{PetService.CurrentPetDisplayName}备份已恢复，请在 Codex Pets 中刷新。");
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
        var recommendedThemeId = PetService.CurrentRecommendedThemeId;
        if (string.IsNullOrWhiteSpace(recommendedThemeId))
        {
            ShowProductMessage(
                "没有配套主题",
                "这个宠物暂未声明配套主题。",
                ProductDialogKind.Information);
            return;
        }
        _themeLibraryFilter = ThemeLibraryFilter.All;
        if (!string.IsNullOrEmpty(ThemeSearchBox.Text))
        {
            ThemeSearchBox.Clear();
        }
        ShowThemeLibraryPage();
        ShowThemes(recommendedThemeId);
        if (_selectedTheme is null ||
            !string.Equals(
                _selectedTheme.ThemeId,
                recommendedThemeId,
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
        var recommendedThemeId = PetService.CurrentRecommendedThemeId;
        var theme = _themes.FirstOrDefault(candidate => string.Equals(
            candidate.ThemeId,
            recommendedThemeId,
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
            ShowToast($"{theme.Name}已应用；宠物安装状态没有被改变。");
        }
    }

    private void RenderPetState(PetCenterPresentationState state)
    {
        _petCenterState = state;
        PetCenterPage.Render(state);
        SetStatus($"{state.DisplayName}：{state.StatusTitle}");
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
                ShowToast("爱弥斯主题有配套的飞行雪绒宠物，可从左栏“宠物画廊”查看；不会自动安装。");
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
