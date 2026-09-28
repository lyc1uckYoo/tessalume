using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.ArtworkLibrary.Presentation;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using Tessalume.Core.Runtime;

namespace Tessalume.App;

public partial class MainWindow
{
    private ArtworkLibraryUndo? _artworkLibraryUndo;

    private sealed record ArtworkLibraryUndo(
        string ThemeId, ArtworkRegion Region, ArtworkColorMode Mode,
        ThemeArtworkAdjustment Before, ThemeArtworkAdjustment After);

    private async void ArtworkLibrary_ApplyRequested(object? sender, ArtworkLibraryApplyEventArgs e) =>
        await ApplyArtworkLibraryAsync(e);

    private Task ApplyArtworkLibraryAsync(ArtworkLibraryApplyEventArgs request) =>
        RunArtworkLibraryOperationAsync(async () =>
        {
            if (_artworkLibraryService is null || !HasLibraryTarget(request.ThemeId)) return;
            EnsureLibraryTargetSupported(request);
            var token = _personalizationCancellation.Token;
            ArtworkLibraryPage.SetStatus("正在准备原图并保存构图…");
            var storedPath = await _artworkLibraryService.PrepareImageAsync(request.Item, token);
            var before = ArtworkSettingsAccessor.GetAdjustment(
                GetVisualSettings(request.ThemeId), request.Mode, request.Region);
            var previousItem = FindCurrentLibraryItem(request.ThemeId, request.Region, request.Mode, before);
            if (previousItem is not null && previousItem.Id != request.Item.Id)
            {
                await _artworkLibraryService.SaveCompositionAsync(previousItem.Id, request.ThemeId,
                    request.Region, request.Mode, before, token);
            }
            await _artworkLibraryService.SaveCompositionAsync(request.Item.Id, request.ThemeId,
                request.Region, request.Mode, request.Adjustment, token);
            var adjustment = request.Adjustment with { CustomImagePath = storedPath };
            await CommitArtworkLibrarySlotAsync(request, adjustment.Normalize());
        });

    private ArtworkLibraryItem? FindCurrentLibraryItem(
        string themeId, ArtworkRegion region, ArtworkColorMode mode, ThemeArtworkAdjustment current)
    {
        var package = _themes.FirstOrDefault(theme => theme.ThemeId == themeId)?.CatalogItem.Package;
        var source = package is null ? null : _artworkLibraryService?.ResolveTargetSource(package, region, mode, current);
        if (source is not null)
        {
            var resolved = ArtworkLibraryPage.Items.FirstOrDefault(item =>
                string.Equals(item.AbsolutePath, source.AbsolutePath, StringComparison.OrdinalIgnoreCase));
            if (resolved is not null) return resolved;
        }
        if (!string.IsNullOrWhiteSpace(current.CustomImagePath))
        {
            var absolute = _personalImageStore.ResolvePath(current.CustomImagePath);
            return ArtworkLibraryPage.Items.FirstOrDefault(item =>
                string.Equals(item.StoredPath, current.CustomImagePath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.AbsolutePath, absolute, StringComparison.OrdinalIgnoreCase));
        }
        return ArtworkLibraryPage.Items.FirstOrDefault(item =>
            item.SourceThemeId == themeId && item.Regions.Contains(region) && item.Modes.Contains(mode));
    }

    private async void ArtworkLibrary_RestoreRequested(object? sender, ArtworkLibraryTargetEventArgs e) =>
        await RestoreArtworkLibraryAsync(e);

    private Task RestoreArtworkLibraryAsync(ArtworkLibraryTargetEventArgs target) =>
        RunArtworkLibraryOperationAsync(async () =>
        {
            if (!HasLibraryTarget(target.ThemeId)) return;
            EnsureLibraryTargetSupported(target);
            var current = ArtworkSettingsAccessor.GetAdjustment(
                GetVisualSettings(target.ThemeId), target.Mode, target.Region);
            var outgoing = FindCurrentLibraryItem(target.ThemeId, target.Region, target.Mode, current);
            if (_artworkLibraryService is not null && outgoing is not null)
            {
                await _artworkLibraryService.SaveCompositionAsync(outgoing.Id, target.ThemeId,
                    target.Region, target.Mode, current, _personalizationCancellation.Token);
            }
            var defaults = _themeArtworkDefaults.GetValueOrDefault(target.ThemeId)
                ?? CreateStandardArtworkDefaults(target.ThemeId);
            var baseline = ThemeArtworkSettingsResolver.Resolve(defaults, null).Settings;
            await CommitArtworkLibrarySlotAsync(target,
                ArtworkSettingsAccessor.GetAdjustment(baseline, target.Mode, target.Region) with { CustomImagePath = null },
                restoredOriginal: true);
        });

    private async void ArtworkLibrary_UndoRequested(object? sender, EventArgs e) =>
        await UndoArtworkLibraryAsync();

    private Task UndoArtworkLibraryAsync() => RunArtworkLibraryOperationAsync(async () =>
    {
        if (_artworkLibraryUndo is not { } undo || !HasLibraryTarget(undo.ThemeId)) return;
        var current = ArtworkSettingsAccessor.GetAdjustment(GetVisualSettings(undo.ThemeId), undo.Mode, undo.Region);
        if (!LibraryAdjustmentsEqual(current, undo.After))
        {
            _artworkLibraryUndo = null;
            ArtworkLibraryPage.SetStatus("该位置已在其他页面调整，为保留最新设置，这次换图不再撤销。");
            return;
        }
        await CommitArtworkLibrarySlotAsync(
            new ArtworkLibraryTargetEventArgs(undo.ThemeId, undo.Region, undo.Mode), undo.Before,
            recordUndo: false);
        _artworkLibraryUndo = null;
    });

    private bool HasLibraryTarget(string themeId) =>
        _themes.Any(theme => theme.IsValid && string.Equals(theme.ThemeId, themeId, StringComparison.OrdinalIgnoreCase));

    private void EnsureLibraryTargetSupported(ArtworkLibraryTargetEventArgs target)
    {
        var package = _themes.FirstOrDefault(theme => theme.ThemeId == target.ThemeId)?.CatalogItem.Package;
        if (package is null || !ArtworkImageSourceResolver.IsRegionSupported(package, target.Region, target.Mode))
            throw new InvalidOperationException("这个主题没有此位置的图片，请选择其他位置");
    }

    private async Task CommitArtworkLibrarySlotAsync(
        ArtworkLibraryTargetEventArgs target, ThemeArtworkAdjustment adjustment, bool recordUndo = true, bool restoredOriginal = false)
    {
        _personalizationCancellation.Token.ThrowIfCancellationRequested();
        EnsureLibraryTargetSupported(target);
        // Always merge with the latest settings for the captured target. A theme or
        // mode switch while an image is importing must not retarget the operation.
        var current = GetVisualSettings(target.ThemeId);
        var before = ArtworkSettingsAccessor.GetAdjustment(current, target.Mode, target.Region);
        _visualSettingsDebounce?.Stop();
        _visualApplyCancellation?.Cancel();
        _visualApplyCancellation?.Dispose();
        _visualApplyCancellation = CancellationTokenSource.CreateLinkedTokenSource(_personalizationCancellation.Token);
        var synchronizationToken = _visualApplyCancellation.Token;
        var version = ++_visualApplyVersion;
        var wasDirtyBefore = _preferencesDirty;
        SetResolvedVisualSettings(target.ThemeId, ArtworkSettingsReducer.UpdateAdjustment(
            current, target.Mode, target.Region, _ => adjustment));
        // Sparse resolution restores inherited asset keys and normalized defaults.
        // Undo must compare against that effective state, not the incoming draft.
        var committed = ArtworkSettingsAccessor.GetAdjustment(
            GetVisualSettings(target.ThemeId), target.Mode, target.Region);
        var revision = MarkPreferencesDirty();
        try
        {
            await SavePreferencesAsync();
            MarkPreferencesPersisted(revision);
        }
        catch
        {
            var latest = GetVisualSettings(target.ThemeId);
            if (LibraryAdjustmentsEqual(
                    ArtworkSettingsAccessor.GetAdjustment(latest, target.Mode, target.Region), committed))
            {
                SetResolvedVisualSettings(target.ThemeId, ArtworkSettingsReducer.UpdateAdjustment(
                    latest, target.Mode, target.Region, _ => before));
                if (_preferencesRevision == revision)
                {
                    // A failed save that fully rolls back must not create an
                    // unsaved change, or closing the window will retry it.
                    _preferencesDirty = wasDirtyBefore;
                }
                else
                {
                    // Preserve edits made while saving, and persist this rollback
                    // with them without allowing an older save to mark it clean.
                    MarkPreferencesDirty();
                }
            }
            throw;
        }
        if (recordUndo && !LibraryAdjustmentsEqual(before, committed))
        {
            _artworkLibraryUndo = new ArtworkLibraryUndo(target.ThemeId, target.Region, target.Mode, before, committed);
        }
        ArtworkLibraryPage.UpdateSettings(CaptureLibrarySettings());
        await ArtworkLibraryPage.RefreshAsync();
        await ArtworkLibraryPage.ReloadSavedTargetPreviewAsync(target.ThemeId, target.Region, target.Mode);
        var label = $"{_themes.First(theme => theme.ThemeId == target.ThemeId).Name} · " +
                    $"{GetArtworkRegionDisplayName(target.Region)} · {GetArtworkModeDisplayName(target.Mode)}";
        var savedAction = restoredOriginal ? $"已恢复 {label} 的原图与推荐构图。" : $"已保存 {label}。";
        var status = savedAction + "应用该主题后生效。";
        if (string.Equals(target.ThemeId, _activeThemeId, StringComparison.OrdinalIgnoreCase))
        {
            status = await SyncArtworkLibraryCommitAsync(target.ThemeId, version, savedAction, synchronizationToken);
        }
        ArtworkLibraryPage.SetStatus(status);
        SetStatus(status);
        ShowToast(!recordUndo ? "已撤销上次图库修改" : restoredOriginal
            ? $"已恢复此位置原图 · {GetArtworkRegionDisplayName(target.Region)}"
            : $"已保存 · {GetArtworkRegionDisplayName(target.Region)} · {GetArtworkModeDisplayName(target.Mode)}");
    }

    private async Task<string> SyncArtworkLibraryCommitAsync(
        string themeId, int version, string saved, CancellationToken token)
    {
        try
        {
            var port = await ResolveArtworkDebugPortAsync(token);
            if (port is null) return saved + "Codex 尚未连接，下次应用主题时生效。";
            if (version != _visualApplyVersion || !string.Equals(themeId, _activeThemeId, StringComparison.OrdinalIgnoreCase))
                return saved + "当前编辑目标已变化。";
            await _runtime.ApplyVisualSettingsAsync(port.Value, themeId, GetVisualSettings(themeId), token);
            if (version != _visualApplyVersion || token.IsCancellationRequested) return saved;
            return saved + "已同步到 Codex。";
        }
        catch (OperationCanceledException) { return saved; }
        catch (Exception exception)
        {
            return saved + $"Codex 同步未完成：{exception.Message}。可重新应用主题。";
        }
    }

    private static bool LibraryAdjustmentsEqual(ThemeArtworkAdjustment left, ThemeArtworkAdjustment right) =>
        ThemeVisualSettingsSemanticComparer.Instance.Equals(
            new ThemeVisualSettings { Light = new ThemeVisualModeSettings { Hero = left } },
            new ThemeVisualSettings { Light = new ThemeVisualModeSettings { Hero = right } });
}
