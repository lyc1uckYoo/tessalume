using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tessalume.App.Features.ArtworkLibrary.Application;
using Tessalume.App.Features.ArtworkLibrary.Domain;
using Tessalume.App.Features.ArtworkLibrary.Infrastructure;
using Tessalume.App.Features.ArtworkLibrary.Presentation;
using Tessalume.App.Features.Navigation;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;

internal static partial class TestSuite
{
    static Task ArtworkLibraryBrowsingIsDraftOnlyAsync() => RunArtworkLibraryExperienceAsync(async fixture =>
    {
        var view = fixture.Window.ArtworkLibraryPage;
        Ensure(view.GalleryPage.Visibility == Visibility.Visible && view.AlbumPanel.Visibility == Visibility.Visible &&
               view.CharactersPanel.Visibility == Visibility.Collapsed && view.DetailPage.Visibility == Visibility.Collapsed,
            "The selected character's album must keep target controls and preview confined to the hidden detail page.");
        var browseCharacterId = view.SelectedCharacterId;
        view.SearchBox.Text = "portrait";
        await view.SelectBrowseRegionAsync(ArtworkRegion.Hero);
        var browsePage = view.PageText.Text;
        var before = await File.ReadAllBytesAsync(fixture.PreferencesPath);
        var writtenAt = File.GetLastWriteTimeUtc(fixture.PreferencesPath);
        var settingsBefore = JsonSerializer.Serialize(GetArtworkLibrarySettings(fixture.Window));
        var revisionBefore = GetArtworkLibraryPreferencesRevision(fixture.Window);
        var applyCount = 0;
        view.ApplyRequested += (_, _) => applyCount++;
        await view.SelectItemAsync(fixture.FirstImage);
        Ensure(view.IsPreviewReady && view.ApplyButton.IsEnabled &&
               view.GalleryPage.Visibility == Visibility.Collapsed && view.DetailPage.Visibility == Visibility.Visible,
            "Selecting a picture must enter a separate detail page with a local preview and explicit application action.");
        ArrangeMainSurface(fixture.Window, new Size(1440, 900));
        view.ZoomInButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await view.SelectTargetAsync(fixture.ThemeId, ArtworkRegion.Sidebar, ArtworkColorMode.Dark);
        Ensure(view.SelectedItem?.Id == fixture.FirstImage.Id && view.IsPreviewReady && view.ApplyButton.IsEnabled &&
               view.SelectedRegion == ArtworkRegion.Sidebar && view.SelectedMode == ArtworkColorMode.Dark,
            "Changing a detail target must retain the chosen picture and rebuild its preview for that region and mode.");
        view.CancelDraft();
        view.RequestApply();
        await Task.Delay(250);
        Ensure(applyCount == 0 && view.SelectedItem is null && !view.IsPreviewReady &&
               view.GalleryPage.Visibility == Visibility.Visible && view.DetailPage.Visibility == Visibility.Collapsed,
            "Cancelling a draft must return to the browser, clear the preview, and suppress application.");
        Ensure(view.SearchBox.Text == "portrait" && view.SelectedCharacterId == browseCharacterId &&
               view.BrowseRegion == ArtworkRegion.Hero && view.PageText.Text == browsePage,
            "Returning from detail must preserve the browser's query, character, picture-type filter, and page.");
        Ensure((await File.ReadAllBytesAsync(fixture.PreferencesPath)).SequenceEqual(before) &&
               File.GetLastWriteTimeUtc(fixture.PreferencesPath) == writtenAt &&
               GetArtworkLibraryPreferencesRevision(fixture.Window) == revisionBefore &&
               JsonSerializer.Serialize(GetArtworkLibrarySettings(fixture.Window)) == settingsBefore,
            "Browsing, changing targets, adjusting framing, and cancelling must perform zero theme-settings writes.");

        await view.SelectItemAsync(fixture.FirstImage);
        ArrangeMainSurface(fixture.Window, new Size(1080, 820));
        CompletePageAnimation(fixture.Window.InfoPage);
        view.ApplyButton.BringIntoView();
        await Dispatcher.Yield(DispatcherPriority.Background);
        ArrangeMainSurface(fixture.Window, new Size(1080, 820));
        EnsureButtonContentFits(view.ApplyButton, 34, "Compact artwork library");
        Ensure(view.PreviewCanvas.ActualWidth > 250 &&
               view.PreviewPanel.ActualWidth <= view.ActualWidth + 1 &&
               view.GalleryPage.Visibility == Visibility.Collapsed && view.DetailPage.Visibility == Visibility.Visible,
            "The compact detail page must provide a usable preview without mixing the browser grid into its layout.");
        var surface = (FrameworkElement)fixture.Window.Content;
        var applyBounds = view.ApplyButton.TransformToAncestor(surface)
            .TransformBounds(new Rect(view.ApplyButton.RenderSize));
        Ensure(applyBounds.Left >= 0 && applyBounds.Right <= surface.ActualWidth + 1 &&
               applyBounds.Bottom <= surface.ActualHeight + 1 && applyBounds.Top >= 0,
            "The explicit apply action must be scroll-reachable inside the compact shell.");
    });

    static Task ArtworkLibraryAppliesOnlyTheCapturedSlotAsync() => RunArtworkLibraryExperienceAsync(async fixture =>
    {
        var view = fixture.Window.ArtworkLibraryPage;
        var settingsBefore = GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId];
        await view.SelectItemAsync(fixture.FirstImage);
        await view.SelectTargetAsync(fixture.ThemeId, ArtworkRegion.Chat, ArtworkColorMode.Dark);
        ArtworkLibraryApplyEventArgs? requested = null;
        var applyCount = 0;
        view.ApplyRequested += (_, args) => { requested = args; applyCount++; };
        view.RequestApply();
        Ensure(applyCount == 1 && requested is not null &&
               requested.ThemeId == fixture.ThemeId && requested.Region == ArtworkRegion.Chat &&
               requested.Mode == ArtworkColorMode.Dark && requested.Item.Id == fixture.FirstImage.Id,
            "An explicit application must emit one immutable request for the exact image, theme, region, and mode.");
        // A subsequent target change must not redirect asynchronous image import
        // or persistence to whatever the user is currently browsing.
        await view.SelectTargetAsync(fixture.ThemeId, ArtworkRegion.Hero, ArtworkColorMode.Light);
        await WaitForArtworkLibraryConditionAsync(() =>
            GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId].Dark.Chat.CustomImagePath ==
            fixture.FirstImage.StoredPath && view.TargetControls.IsEnabled,
            "The captured dark-chat application did not finish.");
        var after = GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId];
        foreach (var mode in Enum.GetValues<ArtworkColorMode>())
            foreach (var region in Enum.GetValues<ArtworkRegion>())
            {
                if (mode == ArtworkColorMode.Dark && region == ArtworkRegion.Chat) continue;
                Ensure(JsonSerializer.Serialize(ArtworkSettingsAccessor.GetAdjustment(after, mode, region)) ==
                       JsonSerializer.Serialize(ArtworkSettingsAccessor.GetAdjustment(settingsBefore, mode, region)),
                    $"Applying dark/chat must preserve the unrelated {mode}/{region} slot.");
            }
        Ensure((string?)typeof(MainWindow).GetField("_activeThemeId", BindingFlags.Instance | BindingFlags.NonPublic)
                   ?.GetValue(fixture.Window) is null,
            "Editing an unapplied theme through the library must not activate it in Codex.");
        using var persistedStore = new UiPreferencesStore(fixture.DataDirectory);
        Ensure(persistedStore.Load().ThemeVisualOverrides.ContainsKey(fixture.ThemeId),
            "An explicit library application must survive restart through the existing sparse preference override.");

        var unrelatedEdit = ArtworkSettingsAccessor.SetAdjustment(after, ArtworkColorMode.Light, ArtworkRegion.Hero,
            after.Light.Hero with { Brightness = after.Light.Hero.Brightness + 1 });
        InvokeMainWindowMethod(fixture.Window, "SetResolvedVisualSettings", fixture.ThemeId, unrelatedEdit);
        await InvokeMainWindowTaskAsync(fixture.Window, "UndoArtworkLibraryAsync");
        var undone = GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId];
        Ensure(JsonSerializer.Serialize(undone.Dark.Chat) == JsonSerializer.Serialize(settingsBefore.Dark.Chat) &&
               JsonSerializer.Serialize(undone.Light.Hero) == JsonSerializer.Serialize(unrelatedEdit.Light.Hero),
            "Undo must restore only the previously applied slot and preserve later changes in other slots.");

        await InvokeMainWindowTaskAsync(fixture.Window, "ApplyArtworkLibraryAsync", requested!);
        var reapplied = GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId];
        var externalEdit = ArtworkSettingsAccessor.SetAdjustment(reapplied, ArtworkColorMode.Dark, ArtworkRegion.Chat,
            reapplied.Dark.Chat with { Brightness = reapplied.Dark.Chat.Brightness + 2 });
        InvokeMainWindowMethod(fixture.Window, "SetResolvedVisualSettings", fixture.ThemeId, externalEdit);
        await InvokeMainWindowTaskAsync(fixture.Window, "UndoArtworkLibraryAsync");
        Ensure(JsonSerializer.Serialize(GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId].Dark.Chat) ==
               JsonSerializer.Serialize(externalEdit.Dark.Chat),
            "Stale undo must not overwrite a later edit made to the same slot in another workspace.");
        await view.SelectItemAsync(view.Items.First(item => item.Id == fixture.FirstImage.Id));
        await view.SelectTargetAsync(fixture.ThemeId, ArtworkRegion.Chat, ArtworkColorMode.Dark);
        ArrangeMainSurface(fixture.Window, new Size(1280, 820));
        var beforeZoom = JsonSerializer.Serialize(view.DraftAdjustment);
        view.ZoomInButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ensure(JsonSerializer.Serialize(view.DraftAdjustment) != beforeZoom &&
               JsonSerializer.Serialize(GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId].Dark.Chat) ==
               JsonSerializer.Serialize(externalEdit.Dark.Chat),
            "The restore fixture must contain a real, unsaved framing change that leaves saved settings untouched.");
        var editedDraft = JsonSerializer.Serialize(view.DraftAdjustment);
        var favoriteBefore = view.SelectedItem!.IsFavorite;
        view.FavoriteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForArtworkLibraryConditionAsync(() => view.SelectedItem is { } selected &&
            selected.IsFavorite != favoriteBefore && view.FavoriteButton.IsEnabled,
            "Favoriting the edited picture did not complete.");
        Ensure(JsonSerializer.Serialize(view.DraftAdjustment) == editedDraft &&
               view.PreviewStateText.Text == "构图未保存",
            "Favoriting a picture must keep the unsaved framing draft and its truthful not-yet-saved hint.");
        await InvokeMainWindowTaskAsync(fixture.Window, "RestoreArtworkLibraryAsync",
            new ArtworkLibraryTargetEventArgs(fixture.ThemeId, ArtworkRegion.Chat, ArtworkColorMode.Dark));
        var restored = GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId];
        Ensure(JsonSerializer.Serialize(restored.Dark.Chat) == JsonSerializer.Serialize(settingsBefore.Dark.Chat) &&
               JsonSerializer.Serialize(restored.Light.Hero) == JsonSerializer.Serialize(unrelatedEdit.Light.Hero),
            "Restore original must recover the target's recommended image and framing without resetting other slots.");
        var restoredItem = view.Items.First(item => item.Usages.Any(usage => usage.ThemeId == fixture.ThemeId &&
            usage.Region == ArtworkRegion.Chat && usage.Mode == ArtworkColorMode.Dark));
        var restoredDraft = restored.Dark.Chat.Normalize() with { CustomImagePath = null, ThemeAssetKey = null };
        Ensure(view.IsPreviewReady && view.SelectedItem?.Id == restoredItem.Id &&
               string.Equals(view.PreviewIdentityPath, restoredItem.AbsolutePath, StringComparison.OrdinalIgnoreCase) &&
               ThemeVisualSettingsSemanticComparer.AdjustmentEquals(view.DraftAdjustment, restoredDraft),
            "Successful restore must replace the unsaved draft with the restored picture and its saved framing in the visible preview.");
    });

    static Task ArtworkLibrarySaveFailureRestoresCleanStateAndUndoAsync() => RunArtworkLibraryExperienceAsync(async fixture =>
    {
        var view = fixture.Window.ArtworkLibraryPage;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var dirtyField = typeof(MainWindow).GetField("_preferencesDirty", flags)
            ?? throw new MissingFieldException(nameof(MainWindow), "_preferencesDirty");
        var undoField = typeof(MainWindow).GetField("_artworkLibraryUndo", flags)
            ?? throw new MissingFieldException(nameof(MainWindow), "_artworkLibraryUndo");
        await view.SelectItemAsync(fixture.FirstImage);
        await view.SelectTargetAsync(fixture.ThemeId, ArtworkRegion.Chat, ArtworkColorMode.Dark);
        await InvokeMainWindowTaskAsync(fixture.Window, "ApplyArtworkLibraryAsync",
            new ArtworkLibraryApplyEventArgs(fixture.ThemeId, ArtworkRegion.Chat, ArtworkColorMode.Dark,
                fixture.FirstImage, view.DraftAdjustment));
        Ensure(dirtyField.GetValue(fixture.Window) is false,
            "The initial successful application must leave a clean saved-state baseline.");
        var originalUndo = undoField.GetValue(fixture.Window)
            ?? throw new InvalidOperationException("The successful application did not create undo history.");
        var savedSettings = JsonSerializer.Serialize(GetArtworkLibrarySettings(fixture.Window));
        var savedBytes = await File.ReadAllBytesAsync(fixture.PreferencesPath);
        var savedTimestamp = File.GetLastWriteTimeUtc(fixture.PreferencesPath);
        await view.SelectItemAsync(fixture.SecondImage);
        await view.SelectTargetAsync(fixture.ThemeId, ArtworkRegion.Chat, ArtworkColorMode.Dark);
        var failedRequest = new ArtworkLibraryApplyEventArgs(
            fixture.ThemeId, ArtworkRegion.Chat, ArtworkColorMode.Dark,
            fixture.SecondImage, view.DraftAdjustment);

        // Windows denies atomic replacement while the destination is held
        // exclusively. Release the lock before the fixture disposes its window.
        using (new FileStream(fixture.PreferencesPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await InvokeMainWindowTaskAsync(fixture.Window, "ApplyArtworkLibraryAsync", failedRequest);
            Ensure(view.StatusText.Text.Contains("操作未完成", StringComparison.Ordinal),
                "An exclusively locked preferences file must report the failed save.");
            Ensure(JsonSerializer.Serialize(GetArtworkLibrarySettings(fixture.Window)) == savedSettings,
                "A failed save must restore the complete pre-application settings, including the targeted slot.");
            Ensure(dirtyField.GetValue(fixture.Window) is false,
                "Rolling back a failed application must keep an initially clean state clean.");
            Ensure(ReferenceEquals(undoField.GetValue(fixture.Window), originalUndo) && view.UndoButton.IsEnabled,
                "A failed application must preserve the prior successful undo entry.");
            Ensure(view.TargetControls.IsEnabled,
                "A failed save must release the operation gate and restore interactive controls.");
        }
        Ensure((await File.ReadAllBytesAsync(fixture.PreferencesPath)).SequenceEqual(savedBytes) &&
               File.GetLastWriteTimeUtc(fixture.PreferencesPath) == savedTimestamp,
            "A rejected save must leave the persisted preferences byte-for-byte unchanged.");

        var existing = GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId];
        var pending = ArtworkSettingsAccessor.SetAdjustment(existing, ArtworkColorMode.Light, ArtworkRegion.Hero,
            existing.Light.Hero with { Brightness = existing.Light.Hero.Brightness + 1 });
        InvokeMainWindowMethod(fixture.Window, "SetResolvedVisualSettings", fixture.ThemeId, pending);
        InvokeMainWindowMethod(fixture.Window, "MarkPreferencesDirty");
        var pendingSettings = JsonSerializer.Serialize(GetArtworkLibrarySettings(fixture.Window));
        using (new FileStream(fixture.PreferencesPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await InvokeMainWindowTaskAsync(fixture.Window, "ApplyArtworkLibraryAsync", failedRequest);
            Ensure(JsonSerializer.Serialize(GetArtworkLibrarySettings(fixture.Window)) == pendingSettings &&
                   dirtyField.GetValue(fixture.Window) is true,
                "A failed application must preserve pre-existing unsaved edits and their dirty state.");
            Ensure(ReferenceEquals(undoField.GetValue(fixture.Window), originalUndo),
                "Rollback from an already dirty state must also retain the original undo entry.");
        }
        Ensure((await File.ReadAllBytesAsync(fixture.PreferencesPath)).SequenceEqual(savedBytes),
            "The second rejected save must not persist either the pending edit or the failed image change.");
    });

    static Task ArtworkLibraryRejectsStalePreviewAsync() => RunArtworkLibraryExperienceAsync(async fixture =>
    {
        var view = fixture.Window.ArtworkLibraryPage;
        var first = view.SelectItemAsync(fixture.FirstImage);
        var second = view.SelectItemAsync(fixture.SecondImage);
        await Task.WhenAll(first, second);
        Ensure(view.IsPreviewReady && view.SelectedItem?.Id == fixture.SecondImage.Id &&
               string.Equals(view.PreviewIdentityPath, fixture.SecondImage.AbsolutePath, StringComparison.OrdinalIgnoreCase),
            "Rapid selection must display the latest image even if the older preview completes later.");
        var cancelled = view.SelectItemAsync(fixture.FirstImage);
        view.CancelDraft();
        await cancelled;
        Ensure(view.SelectedItem is null && view.PreviewIdentityPath is null && !view.IsPreviewReady &&
               view.PreviewCanvas.ArtworkImage.Source is null && !view.ApplyButton.IsEnabled,
            "A cancelled pending preview must not repopulate the canvas or re-enable apply.");
        var changingTarget = view.SelectItemAsync(fixture.FirstImage);
        var latestTarget = view.SelectTargetAsync(fixture.ThemeId, ArtworkRegion.Sidebar, ArtworkColorMode.Dark);
        await Task.WhenAll(changingTarget, latestTarget);
        Ensure(view.SelectedItem?.Id == fixture.FirstImage.Id && view.IsPreviewReady &&
               view.SelectedRegion == ArtworkRegion.Sidebar && view.SelectedMode == ArtworkColorMode.Dark &&
               view.PreviewCanvas.TargetSize == new ArtworkSize(260, 800),
            "An old preview must not replace the same picture's latest region-specific detail preview.");

        await view.SelectItemAsync(fixture.FirstImage);
        var refresh = view.RefreshAsync();
        var newerSelection = view.SelectItemAsync(fixture.SecondImage);
        await Task.WhenAll(refresh, newerSelection);
        Ensure(view.SelectedItem?.Id == fixture.SecondImage.Id && view.IsPreviewReady &&
               string.Equals(view.PreviewIdentityPath, fixture.SecondImage.AbsolutePath, StringComparison.OrdinalIgnoreCase),
            "Finishing a catalog refresh must not restore an earlier selection over the user's newer preview.");
    });

    static Task ArtworkLibraryBackInvalidatesPendingPreviewAsync() => RunArtworkLibraryExperienceAsync(async fixture =>
    {
        var view = fixture.Window.ArtworkLibraryPage;
        var pending = view.SelectItemAsync(fixture.FirstImage);
        view.BackToGallery();
        await pending;
        Ensure(view.GalleryPage.Visibility == Visibility.Visible && view.DetailPage.Visibility == Visibility.Collapsed &&
               view.SelectedItem is null && view.PreviewIdentityPath is null && !view.IsPreviewReady &&
               view.PreviewCanvas.ArtworkImage.Source is null && !view.ApplyButton.IsEnabled,
            "A preview that completes after returning to the browser must not reopen detail or restore its image.");
        var stale = view.SelectItemAsync(fixture.FirstImage);
        view.BackToGallery();
        var fresh = view.SelectItemAsync(fixture.SecondImage);
        await Task.WhenAll(stale, fresh);
        Ensure(view.DetailPage.Visibility == Visibility.Visible && view.GalleryPage.Visibility == Visibility.Collapsed &&
               view.SelectedItem?.Id == fixture.SecondImage.Id && view.IsPreviewReady &&
               string.Equals(view.PreviewIdentityPath, fixture.SecondImage.AbsolutePath, StringComparison.OrdinalIgnoreCase),
            "Returning and opening another card must keep only the newly chosen detail, even if an older load finishes later.");
    });

    static Task ArtworkLibraryBrowsingPreservesOriginalAspectRatiosAsync() => RunArtworkLibraryExperienceAsync(async fixture =>
    {
        var view = fixture.Window.ArtworkLibraryPage;
        await view.SelectBrowseRegionAsync(null);
        ArrangeMainSurface(fixture.Window, new Size(1280, 820));
        var frames = FindArtworkLibraryThumbnailFrames(view.ArtworkItems).ToArray();
        Ensure(frames.Length > 0, "The album must render original picture thumbnails.");
        foreach (var (frame, item) in frames)
        {
            Ensure(item.PixelWidth > 0 && item.PixelHeight > 0, "Original thumbnail dimensions must be known.");
            var ratio = (double)item.PixelWidth / item.PixelHeight;
            var dpi = VisualTreeHelper.GetDpi(frame);
            // Compare the short side so independent WPF layout rounding
            // remains bounded by one physical pixel for every aspect ratio.
            var error = ratio >= 1
                ? Math.Abs(frame.ActualHeight - frame.ActualWidth / ratio) * dpi.DpiScaleY
                : Math.Abs(frame.ActualWidth - frame.ActualHeight * ratio) * dpi.DpiScaleX;
            Ensure(frame.ActualWidth > 40 && frame.ActualHeight > 40 && error <= 1,
                $"The {item.Name} thumbnail must preserve its original {item.PixelWidth}×{item.PixelHeight} ratio " +
                $"without a preset crop (actual {frame.ActualWidth:0.##}×{frame.ActualHeight:0.##}).");
        }
        var applicationRegion = view.SelectedRegion;
        await view.SelectBrowseRegionAsync(ArtworkRegion.Sidebar);
        ArrangeMainSurface(fixture.Window, new Size(1280, 820));
        Ensure(view.RenderedItems.Count > 0 && view.RenderedItems.All(item => item.Regions.Contains(ArtworkRegion.Sidebar)) &&
               view.SelectedRegion == applicationRegion && view.DetailPage.Visibility == Visibility.Collapsed,
            "The browser's picture-type filter must not change the replacement target or open detail.");
        var sidebarFrame = FindArtworkLibraryThumbnailFrames(view.ArtworkItems).First();
        DependencyObject? ancestor = sidebarFrame.Frame;
        while (ancestor is not null && ancestor is not Button) ancestor = VisualTreeHelper.GetParent(ancestor);
        var cardButton = ancestor as Button ?? throw new InvalidOperationException("A picture card must have one actionable button.");
        cardButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForArtworkLibraryConditionAsync(() => view.IsPreviewReady && view.SelectedItem?.Id == sidebarFrame.Item.Id,
            "Clicking a sidebar card did not load its detail.");
        Ensure(view.GalleryPage.Visibility == Visibility.Collapsed && view.DetailPage.Visibility == Visibility.Visible &&
               view.SelectedRegion == ArtworkRegion.Sidebar,
            "Clicking a browser card must open its detail with the picture's recommended region.");
        await view.SelectTargetAsync(fixture.ThemeId, ArtworkRegion.Chat, ArtworkColorMode.Dark);
        Ensure(view.SelectedItem?.Id == sidebarFrame.Item.Id && view.IsPreviewReady &&
               view.BrowseRegion == ArtworkRegion.Sidebar && view.SelectedRegion == ArtworkRegion.Chat,
            "Detail target changes must retain the image and leave the independent browser filter intact.");
        view.BackToGallery();
        Ensure(view.GalleryPage.Visibility == Visibility.Visible && view.DetailPage.Visibility == Visibility.Collapsed &&
               view.BrowseRegion == ArtworkRegion.Sidebar && view.SelectedItem is null,
            "Returning from detail must restore the original image-type browse context.");
    });

    private static IEnumerable<(FrameworkElement Frame, ArtworkLibraryItem Item)> FindArtworkLibraryThumbnailFrames(
        DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement { Name: "ArtworkThumbnailFrame" } frame &&
                frame.DataContext?.GetType().GetProperty("Item")?.GetValue(frame.DataContext) is ArtworkLibraryItem item)
            {
                yield return (frame, item);
            }
            foreach (var nested in FindArtworkLibraryThumbnailFrames(child)) yield return nested;
        }
    }

    static Task ArtworkLibraryRespectsEntryTargetHintsAsync() => RunArtworkLibraryExperienceAsync(async fixture =>
    {
        var view = fixture.Window.ArtworkLibraryPage;
        var entryTarget = fixture.Package with
        {
            Manifest = fixture.Package.Manifest with { Id = "qa.entry-target", Name = "工作台入口目标" },
            ArtworkDefaultsPath = null,
        };
        var settings = new Dictionary<string, ThemeVisualSettings>(GetArtworkLibrarySettings(fixture.Window),
            StringComparer.OrdinalIgnoreCase)
        { [entryTarget.Manifest.Id] = new ThemeVisualSettings() };
        view.Configure(fixture.Service, [entryTarget, fixture.Package], settings,
            entryTarget.Manifest.Id, ArtworkRegion.Hero, ArtworkColorMode.Light);
        await view.RefreshAsync();
        var chat = view.Items.First(item => item.SourceThemeId == fixture.ThemeId &&
            item.Regions.Contains(ArtworkRegion.Chat) && item.Modes.Contains(ArtworkColorMode.Dark));
        await view.OpenCharacterAsync(chat.CharacterId);
        await view.SelectItemAsync(chat);
        Ensure(view.SelectedThemeId == fixture.ThemeId && view.SelectedRegion == ArtworkRegion.Chat &&
               view.SelectedMode == ArtworkColorMode.Dark,
            "A normal browse entry must initialize detail from the chosen picture, not a stale earlier target.");

        view.Configure(fixture.Service, [entryTarget, fixture.Package], settings,
            entryTarget.Manifest.Id, ArtworkRegion.Sidebar, ArtworkColorMode.Light, preserveEntryTarget: true);
        await view.RefreshAsync();
        await view.OpenCharacterAsync(chat.CharacterId);
        await view.SelectItemAsync(chat);
        Ensure(view.SelectedThemeId == entryTarget.Manifest.Id && view.SelectedRegion == ArtworkRegion.Sidebar &&
               view.SelectedMode == ArtworkColorMode.Light && view.SelectedItem?.Id == chat.Id,
            "A workbench entry must preserve its explicit theme, region, and mode when a different picture is chosen.");
        view.BackToGallery();
        view.Configure(fixture.Service, [entryTarget, fixture.Package], settings,
            entryTarget.Manifest.Id, ArtworkRegion.Hero, ArtworkColorMode.Light,
            initialCharacterThemeId: fixture.ThemeId);
        await view.RefreshAsync();
        Ensure(view.RenderedItems.Count > 0 && view.RenderedItems.All(item => item.CharacterId == chat.CharacterId),
            "A theme-detail entry should open only its character's album with detail still hidden.");
        await view.SelectItemAsync(chat);
        Ensure(view.SelectedThemeId == fixture.ThemeId && view.SelectedRegion == ArtworkRegion.Chat &&
               view.SelectedMode == ArtworkColorMode.Dark,
            "A character filter hint must not pin the replacement target like an explicit workbench entry.");
    });

    static Task ArtworkLibraryGeneralPicturesDoNotInheritPreviousDetailTargetsAsync() => RunArtworkLibraryExperienceAsync(async fixture =>
    {
        var view = fixture.Window.ArtworkLibraryPage;
        var fallback = fixture.Package with
        {
            Manifest = fixture.Package.Manifest with { Id = "qa.general-fallback", Name = "默认替换目标" },
            ArtworkDefaultsPath = null,
        };
        var settings = new Dictionary<string, ThemeVisualSettings>(GetArtworkLibrarySettings(fixture.Window),
            StringComparer.OrdinalIgnoreCase)
        { [fallback.Manifest.Id] = new ThemeVisualSettings() };
        view.Configure(fixture.Service, [fallback, fixture.Package], settings,
            fallback.Manifest.Id, ArtworkRegion.Hero, ArtworkColorMode.Light);
        await view.RefreshAsync();
        var generalPath = Path.Combine(fixture.DataDirectory, "generic-import.png");
        var characterPath = Path.Combine(fixture.DataDirectory, "character-general-import.png");
        WriteArtworkLibraryFixtureImage(generalPath, 84, 112, Colors.Purple);
        WriteArtworkLibraryFixtureImage(characterPath, 96, 120, Colors.Goldenrod);
        var generalImport = await fixture.Service.ImportAsync(generalPath, "personal", "我的图片", null, null);
        var character = ArtworkLibraryService.GetCharacter(fixture.Package);
        var characterImport = await fixture.Service.ImportAsync(characterPath, character.Id, character.Name, null, null);
        await view.RefreshAsync();
        var original = view.Items.First(item => item.SourceThemeId == fixture.ThemeId &&
            item.Regions.Contains(ArtworkRegion.Chat) && item.Modes.Contains(ArtworkColorMode.Dark));
        var general = view.Items.First(item => item.Id == generalImport.Id);
        var sameCharacter = view.Items.First(item => item.Id == characterImport.Id);
        Ensure(general.SourceThemeId is null && sameCharacter.SourceThemeId is null,
            "This fixture must exercise personal pictures without a source-theme target.");

        await view.OpenCharacterAsync(original.CharacterId);
        await view.SelectItemAsync(original);
        await view.SelectTargetAsync(fixture.ThemeId, ArtworkRegion.Chat, ArtworkColorMode.Dark);
        view.BackToGallery();
        await view.BackToCharactersAsync();
        await view.OpenCharacterAsync(general.CharacterId);
        await view.SelectItemAsync(general);
        Ensure(view.SelectedThemeId == fallback.Manifest.Id && view.SelectedRegion == ArtworkRegion.Sidebar &&
               view.SelectedMode == ArtworkColorMode.Light,
            "A general portrait must use its own region recommendation and the configured theme/mode fallback instead of the previous detail target.");
        view.BackToGallery();
        await view.BackToCharactersAsync();
        await view.OpenCharacterAsync(original.CharacterId);
        await view.SelectItemAsync(original);
        await view.SelectTargetAsync(fixture.ThemeId, ArtworkRegion.Chat, ArtworkColorMode.Dark);
        view.BackToGallery();
        await view.SelectItemAsync(sameCharacter);
        Ensure(view.SelectedThemeId == fixture.ThemeId && view.SelectedRegion == ArtworkRegion.Sidebar &&
               view.SelectedMode == ArtworkColorMode.Light,
            "A personal portrait should prefer its character's available theme and its own region recommendation, while resetting to the entry's light mode.");

        view.Configure(fixture.Service, [fallback, fixture.Package], settings,
            fallback.Manifest.Id, ArtworkRegion.Sidebar, ArtworkColorMode.Dark, preserveEntryTarget: true);
        await view.RefreshAsync();
        await view.OpenCharacterAsync(sameCharacter.CharacterId);
        await view.SelectItemAsync(sameCharacter);
        Ensure(view.SelectedThemeId == fallback.Manifest.Id && view.SelectedRegion == ArtworkRegion.Sidebar &&
               view.SelectedMode == ArtworkColorMode.Dark,
            "An explicit workbench entry must keep its complete target even when a personal image has a matching character theme.");
        view.BackToGallery();
        await view.BackToCharactersAsync();
        await view.OpenCharacterAsync(general.CharacterId);
        await view.SelectItemAsync(general);
        Ensure(view.SelectedThemeId == fallback.Manifest.Id && view.SelectedRegion == ArtworkRegion.Sidebar &&
               view.SelectedMode == ArtworkColorMode.Dark,
            "Returning to the browser must retain an explicit workbench entry for the next general picture.");
    });

    static Task ArtworkLibraryLargeCatalogKeepsPagesBoundedAsync() => RunArtworkLibraryExperienceAsync(async fixture =>
    {
        var view = fixture.Window.ArtworkLibraryPage;
        var catalogPath = Path.Combine(fixture.DataDirectory, "personalization", "library.json");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var document = JsonSerializer.Deserialize<ArtworkLibraryDocument>(await File.ReadAllTextAsync(catalogPath), options)
            ?? throw new InvalidOperationException("The fixture catalog is missing.");
        // Shared tiny originals deliberately isolate metadata/page behavior from
        // image size. The grid must remain bounded as the catalog grows.
        var records = Enumerable.Range(0, 1000).Select(index => new ArtworkLibraryStoredImage
        {
            Id = $"qa-large-{index:0000}",
            Name = $"图库探针{index:0000}",
            CharacterId = "qa-large",
            CharacterName = "大图库角色",
            StoredPath = fixture.FirstImage.StoredPath!,
            Regions = [ArtworkRegion.Hero],
            Modes = [ArtworkColorMode.Light],
        });
        await File.WriteAllTextAsync(catalogPath,
            JsonSerializer.Serialize(document with
            {
                Images = [.. document.Images, .. records],
                Favorites = [.. document.Favorites, .. records.Select(record => record.Id)],
            }, options));
        await view.RefreshAsync();
        await view.OpenCharacterAsync("qa-large");
        view.FavoritesOnly.IsChecked = true;
        await view.SelectBrowseRegionAsync(ArtworkRegion.Hero);
        await WaitForArtworkLibraryConditionAsync(() => view.RenderedItems.Count is > 0 and <= 24 && view.NextButton.IsEnabled,
            "The first large-catalog page did not become ready.");
        Ensure(view.Items.Count >= 1000,
            "The large fixture must be indexed in full even when only one image page is rendered.");
        var firstPageIds = ArtworkLibraryRenderedIds(view);
        view.NextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForArtworkLibraryConditionAsync(() => view.PageText.Text.StartsWith("2 /", StringComparison.Ordinal),
            "The next catalog page did not become reachable.");
        var secondPageIds = ArtworkLibraryRenderedIds(view);
        Ensure(secondPageIds.Length is > 0 and <= 24 && !secondPageIds.Intersect(firstPageIds, StringComparer.Ordinal).Any(),
            "Album paging must render at most twenty-four cards and advance to distinct entries.");
        ArrangeMainSurface(fixture.Window, new Size(1280, 820));
        fixture.Window.InfoScroll.ScrollToVerticalOffset(120);
        ArrangeMainSurface(fixture.Window, new Size(1280, 820));
        var browseOffset = fixture.Window.InfoScroll.VerticalOffset;
        await view.SelectItemAsync(view.RenderedItems[0]);
        view.BackToGallery();
        await Dispatcher.Yield(DispatcherPriority.Background);
        ArrangeMainSurface(fixture.Window, new Size(1280, 820));
        Ensure(view.GalleryPage.Visibility == Visibility.Visible && view.DetailPage.Visibility == Visibility.Collapsed &&
               view.FavoritesOnly.IsChecked == true && view.SelectedCharacterId == "qa-large" &&
               view.BrowseRegion == ArtworkRegion.Hero && ArtworkLibraryRenderedIds(view).SequenceEqual(secondPageIds) &&
               view.PageText.Text.StartsWith("2 /", StringComparison.Ordinal) &&
               Math.Abs(fixture.Window.InfoScroll.VerticalOffset - browseOffset) <= 1,
            "Returning from detail must restore the same filtered page and scroll position in a large library.");
        view.SearchBox.Text = "图库探针0999";
        await WaitForArtworkLibraryConditionAsync(() => view.RenderedItems.Count == 1,
            "Search did not find an item beyond the current page.");
        Ensure(ArtworkLibraryRenderedIds(view).SequenceEqual(["qa-large-0999"]) &&
               !view.PreviousButton.IsEnabled && !view.NextButton.IsEnabled,
            "Search must cover the full library and reset pagination to the matching tail entry.");
    });

    static Task ArtworkLibraryCharacterAlbumsKeepBrowseContextAndCompactToolbarAsync() =>
        RunArtworkLibraryExperienceAsync(async fixture =>
        {
            var view = fixture.Window.ArtworkLibraryPage;
            var catalogPath = Path.Combine(fixture.DataDirectory, "personalization", "library.json");
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            var document = JsonSerializer.Deserialize<ArtworkLibraryDocument>(await File.ReadAllTextAsync(catalogPath), options)
                ?? throw new InvalidOperationException("The fixture catalog is missing.");
            var records = Enumerable.Range(0, 36).Select(index => new ArtworkLibraryStoredImage
            {
                Id = $"qa-character-image-{index:00}",
                Name = $"相册图片{index:00}",
                CharacterId = $"qa-character-{index:00}",
                CharacterName = $"探针角色{index:00}",
                StoredPath = fixture.FirstImage.StoredPath!,
                Regions = [ArtworkRegion.Hero],
                Modes = [ArtworkColorMode.Light],
            }).ToArray();
            await File.WriteAllTextAsync(catalogPath, JsonSerializer.Serialize(document with
            {
                Images = [.. document.Images, .. records],
                Favorites = [.. document.Favorites, .. records.Select(record => record.Id)],
            }, options));
            var savedPreferences = await File.ReadAllBytesAsync(fixture.PreferencesPath);
            view.Configure(fixture.Service, [fixture.Package], GetArtworkLibrarySettings(fixture.Window), fixture.ThemeId);
            await view.RefreshAsync();
            ArrangeMainSurface(fixture.Window, new Size(1280, 820));
            Ensure(view.CharactersPanel.Visibility == Visibility.Visible && view.AlbumPanel.Visibility == Visibility.Collapsed &&
                   view.DetailPage.Visibility == Visibility.Collapsed && view.SelectedCharacterId is null &&
                   view.RenderedCharacters.Count > 0 && view.RenderedItems.Count == 0,
                "The default library entry must render character cards, without flattening all pictures into the first page.");
            Ensure(view.BrowseToolbar.ActualHeight is > 0 and <= 60,
                "The normal-width browser toolbar must stay compact instead of growing into a separate header section.");
            var firstCard = FindArtworkLibraryCharacterButtons(view.CharacterItems).First();
            var firstBounds = firstCard.Button.TransformToAncestor(view)
                .TransformBounds(new Rect(firstCard.Button.RenderSize));
            Ensure(firstBounds.Top >= 0 && firstBounds.Top <= 64 && firstBounds.Width > 100,
                "The first character card must begin within 64 DIPs of the browser top, leaving the viewport for artwork.");

            view.SearchBox.Text = "探针角色";
            view.FavoritesOnly.IsChecked = true;
            await WaitForArtworkLibraryConditionAsync(() => view.NextButton.IsEnabled &&
                view.RenderedCharacters.Count > 0 &&
                view.RenderedCharacters.All(character => character.Name.StartsWith("探针角色", StringComparison.Ordinal)),
                "The character search and favorites filter did not finish.");
            view.NextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForArtworkLibraryConditionAsync(() => view.PageText.Text.StartsWith("2 /", StringComparison.Ordinal),
                "The second page of character cards did not open.");
            // A shorter viewport exercises real scrolling while preserving the
            // same normal-width toolbar geometry checked above.
            ArrangeMainSurface(fixture.Window, new Size(1280, 520));
            fixture.Window.InfoScroll.ScrollToVerticalOffset(120);
            ArrangeMainSurface(fixture.Window, new Size(1280, 520));
            var characterOffset = fixture.Window.InfoScroll.VerticalOffset;
            var characterPage = view.PageText.Text;
            var characterIds = view.RenderedCharacters.Select(character => character.Id).ToArray();
            var openCard = FindArtworkLibraryCharacterButtons(view.CharacterItems).First();
            openCard.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForArtworkLibraryConditionAsync(() => view.SelectedCharacterId == openCard.Character.Id &&
                view.AlbumPanel.Visibility == Visibility.Visible && view.RenderedItems.Count > 0,
                "Clicking a character card did not open its album.");
            Ensure(view.CharactersPanel.Visibility == Visibility.Collapsed &&
                   view.RenderedItems.All(item => item.CharacterId == openCard.Character.Id),
                "A character album must contain only that character's pictures.");
            var albumTitle = view.BrowseTitleText.Text;
            view.BrowseTitleText.Text = "探针角色·月海长夜与星辰之冠特别珍藏相册";
            ArrangeMainSurface(fixture.Window, new Size(1080, 820));
            await Dispatcher.Yield(DispatcherPriority.Background);
            ArrangeMainSurface(fixture.Window, new Size(1080, 820));
            var importBounds = view.ImportButton.TransformToAncestor(view.BrowseToolbar)
                .TransformBounds(new Rect(view.ImportButton.RenderSize));
            Ensure(view.BrowseToolbar.ActualHeight is > 0 and <= 40 &&
                   importBounds.Left >= 0 && importBounds.Right <= view.BrowseToolbar.ActualWidth + 1 &&
                   importBounds.Top >= 0 && importBounds.Bottom <= view.BrowseToolbar.ActualHeight + 1,
                "A long character title at 1080×820 must keep the album toolbar on one row with its import action in bounds.");
            view.BrowseTitleText.Text = albumTitle;
            ArrangeMainSurface(fixture.Window, new Size(1280, 520));
            await view.BackToCharactersAsync();
            await Dispatcher.Yield(DispatcherPriority.Background);
            ArrangeMainSurface(fixture.Window, new Size(1280, 520));
            Ensure(view.CharactersPanel.Visibility == Visibility.Visible && view.AlbumPanel.Visibility == Visibility.Collapsed &&
                   view.SelectedCharacterId is null && view.RenderedItems.Count == 0 &&
                   view.SearchBox.Text == "探针角色" && view.FavoritesOnly.IsChecked == true &&
                   view.PageText.Text == characterPage &&
                   view.RenderedCharacters.Select(character => character.Id).SequenceEqual(characterIds) &&
                   Math.Abs(fixture.Window.InfoScroll.VerticalOffset - characterOffset) <= 1,
                "Returning from an album must restore the character list's query, favorites, page, and scroll position.");
            Ensure((await File.ReadAllBytesAsync(fixture.PreferencesPath)).SequenceEqual(savedPreferences),
                "Character browsing and album navigation must not change saved theme settings.");
        });

    private static IEnumerable<(Button Button, ArtworkLibraryCharacter Character)> FindArtworkLibraryCharacterButtons(
        DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button button && button.DataContext?.GetType().GetProperty("Character")
                    ?.GetValue(button.DataContext) is ArtworkLibraryCharacter character)
            {
                yield return (button, character);
            }
            foreach (var nested in FindArtworkLibraryCharacterButtons(child)) yield return nested;
        }
    }

    private static string[] ArtworkLibraryRenderedIds(ArtworkLibraryView view) =>
        view.RenderedItems.Select(item => item.Id).ToArray();

    private static async Task WaitForArtworkLibraryConditionAsync(Func<bool> condition, string failureMessage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(30);
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        Ensure(condition(), failureMessage);
    }

    private static int GetArtworkLibraryPreferencesRevision(MainWindow window) =>
        (int)(typeof(MainWindow).GetField("_preferencesRevision", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(window) ?? throw new MissingFieldException(nameof(MainWindow), "_preferencesRevision"));

    private static Task RunArtworkLibraryExperienceAsync(Func<ArtworkLibraryExperienceFixture, Task> test)
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(ArtworkLibraryQaTemporaryRoot, $".test-data-{Guid.NewGuid():N}");
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        Exception? failure = null;
        using (var preferences = new UiPreferencesStore(data))
        {
            preferences.SaveAsync(new UiPreferences
            {
                OnboardingCompleted = true,
                AutomaticUpdateChecks = false,
                QuickSwitchVisible = false,
            }).GetAwaiter().GetResult();
        }
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            MainWindow? window = null;
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    window = new MainWindow(new PortableLayout(root, Path.Combine(root, "themes"), data));
                    InvokeMainWindowMethod(window, "EnsureMainUiInitialized");
                    await AttachArtworkSnapshotThemeAsync(window, repositoryRoot);
                    await InvokeMainWindowTaskAsync(window, "ResolveThemeArtworkDefaultsAsync", CancellationToken.None);
                    var loaded = await new ThemePackageLoader().LoadAsync(
                        Path.Combine(repositoryRoot, "themes", "cartethyia.gale-tide-crown"));
                    var package = loaded.Package ?? throw new InvalidOperationException(FormatIssues(loaded.Validation));
                    var service = (ArtworkLibraryService)(typeof(MainWindow).GetField(
                        "_artworkLibraryService", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window)
                        ?? throw new MissingFieldException(nameof(MainWindow), "_artworkLibraryService"));
                    await service.LoadAsync([package], GetArtworkLibrarySettings(window));
                    var firstPath = Path.Combine(root, "portrait.png");
                    var secondPath = Path.Combine(root, "landscape.png");
                    WriteArtworkLibraryFixtureImage(firstPath, 96, 144, Colors.Teal);
                    WriteArtworkLibraryFixtureImage(secondPath, 180, 100, Colors.Orange);
                    var character = ArtworkLibraryService.GetCharacter(package);
                    var firstImage = await service.ImportAsync(firstPath, character.Id, character.Name,
                        ArtworkRegion.Hero, ArtworkColorMode.Light);
                    var secondImage = await service.ImportAsync(secondPath, character.Id, character.Name,
                        ArtworkRegion.Hero, ArtworkColorMode.Light);
                    window.ArtworkLibraryPage.Configure(service, [package], GetArtworkLibrarySettings(window),
                        package.Manifest.Id, ArtworkRegion.Hero, ArtworkColorMode.Light);
                    await window.ArtworkLibraryPage.RefreshAsync();
                    await window.ArtworkLibraryPage.OpenCharacterAsync(character.Id);
                    InvokeMainWindowMethod(window, "NavigateTo", AppRoute.ArtworkLibrary);
                    ArrangeMainSurface(window, new Size(1440, 900));
                    CompletePageAnimation(window.InfoPage);
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    await test(new ArtworkLibraryExperienceFixture(
                        window, data, package.Manifest.Id, firstImage, secondImage, service, package));
                }
                catch (Exception exception)
                {
                    failure = exception is TargetInvocationException invocation
                        ? invocation.InnerException ?? invocation : exception;
                }
                finally
                {
                    if (window is not null) await window.DisposeAsync();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        try
        {
            if (failure is not null) throw new InvalidOperationException("The artwork-library experience failed.", failure);
            return Task.CompletedTask;
        }
        finally { DeleteArtworkLibraryQaDirectory(root); }
    }

    private static void WriteArtworkLibraryFixtureImage(string path, int width, int height, Color color)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
            pixels[index + 3] = 255;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private sealed record ArtworkLibraryExperienceFixture(
        MainWindow Window,
        string DataDirectory,
        string ThemeId,
        ArtworkLibraryItem FirstImage,
        ArtworkLibraryItem SecondImage,
        ArtworkLibraryService Service,
        ThemePackage Package)
    {
        public string PreferencesPath => Path.Combine(DataDirectory, "ui-settings.json");
    }
}
