using System.Reflection;
using Tessalume.App.Features.ArtworkLibrary.Application;
using Tessalume.App.Features.ArtworkLibrary.Presentation;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

internal static partial class TestSuite
{
    private static readonly ArtworkRegion[] ArtworkLibraryCardRegions =
    [
        ArtworkRegion.TaskLeft, ArtworkRegion.Memory,
        ArtworkRegion.TaskRightSecondary, ArtworkRegion.TaskRightPrimary,
    ];
    private static readonly string[] ArtworkLibraryFailedOperations = ["apply", "restore", "undo"];

    static async Task ArtworkLibraryIndexesEveryPublishedOriginalAndTargetAsync()
    {
        var root = ArtworkLibraryTestDirectory();
        try
        {
            Ensure((int)ArtworkRegion.Hero == 0 && (int)ArtworkRegion.Sidebar == 1 && (int)ArtworkRegion.Chat == 2 &&
                   (int)ArtworkRegion.TaskLeft == 3 && (int)ArtworkRegion.Memory == 4 &&
                   (int)ArtworkRegion.TaskRightSecondary == 5 && (int)ArtworkRegion.TaskRightPrimary == 6,
                "Adding character-card targets must preserve the numeric identities of the three existing regions.");
            var scan = await new ThemeCatalog(new ThemePackageLoader()).ScanAsync(Path.Combine(FindRepositoryRoot(), "themes"));
            var packages = scan.Where(entry => entry.Validation.IsValid && entry.Package is not null)
                .Select(entry => entry.Package!).ToArray();
            Ensure(packages.Length > 0, "Published themes must be available to the complete-original regression.");
            var settings = new Dictionary<string, ThemeVisualSettings>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in packages)
            {
                var defaults = await new ArtworkThemeDefaultsStore().LoadAsync(package);
                settings[package.Manifest.Id] = ThemeArtworkSettingsResolver.Resolve(defaults.Defaults, null).Settings;
            }
            var settingsBefore = JsonSerializer.Serialize(settings);
            await using var service = new ArtworkLibraryService(root);
            var snapshot = await service.LoadAsync(packages, settings);
            foreach (var package in packages)
            {
                var originals = snapshot.Items.Where(item => item.IsBuiltIn && item.SourceThemeId == package.Manifest.Id).ToArray();
                var expectedPaths = package.AssetPaths.Values.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Ensure(expectedPaths.Count >= 11 && originals.Length == expectedPaths.Count &&
                       originals.Select(item => Path.GetFullPath(item.AbsolutePath)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                           .SetEquals(expectedPaths),
                    $"{package.Manifest.Id} must index every declared original once, including distinct native dark card artwork.");
                foreach (var mode in Enum.GetValues<ArtworkColorMode>())
                    foreach (var region in Enum.GetValues<ArtworkRegion>())
                    {
                        var asset = ExpectedLibraryAssetKey(package, region, mode);
                        var path = package.AssetPaths[asset];
                        var image = originals.Single(item => string.Equals(item.AbsolutePath, path, StringComparison.OrdinalIgnoreCase));
                        Ensure(image.Regions.Contains(region) && image.Modes.Contains(mode) &&
                               image.Recommendations.Count(recipe => recipe.Region == region && recipe.Mode == mode) == 1 &&
                               image.Usages.Any(usage => usage.ThemeId == package.Manifest.Id && usage.Region == region && usage.Mode == mode),
                            $"{package.Manifest.Id} {region}/{mode} must identify the correct original, recommendation, and current usage.");
                        Ensure(ArtworkImageSourceResolver.IsRegionSupported(package, region, mode),
                            $"The published {region}/{mode} original must be available as a replacement target.");
                        var recommended = service.ResolveInitialAdjustment(image, package.Manifest.Id, region, mode);
                        Ensure(recommended.CustomImagePath is null && recommended.ThemeAssetKey is null,
                            "Recommendations must not retain an image reference belonging to a previous replacement.");
                    }
                Ensure(originals.Sum(image => image.Recommendations.Count) == 14,
                    $"{package.Manifest.Id} must cover seven regions in both modes, merging shared source cards without duplicate recipes.");
            }
            Ensure(JsonSerializer.Serialize(settings) == settingsBefore,
                "Discovering all card originals must not mutate effective theme settings.");
            var legacy = packages[0] with
            {
                AssetPaths = packages[0].AssetPaths.Where(pair => pair.Key.StartsWith("hero-", StringComparison.Ordinal))
                    .ToDictionary(pair => pair.Key, pair => pair.Value),
            };
            foreach (var region in ArtworkLibraryCardRegions)
                foreach (var mode in Enum.GetValues<ArtworkColorMode>())
                    Ensure(!ArtworkImageSourceResolver.IsRegionSupported(legacy, region, mode),
                        "A legacy theme without a card original must not offer a target that could silently fall back to its hero.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    static Task ArtworkLibraryCardTargetsApplyRestoreUndoAndPersistExactlyAsync() =>
        RunArtworkLibraryExperienceAsync(async fixture =>
        {
            var view = fixture.Window.ArtworkLibraryPage;
            var defaults = await new ArtworkThemeDefaultsStore().LoadAsync(fixture.Package);
            var baseline = ThemeArtworkSettingsResolver.Resolve(defaults.Defaults, null).Settings;
            var sentinels = baseline;
            foreach (var mode in Enum.GetValues<ArtworkColorMode>())
                foreach (var region in Enum.GetValues<ArtworkRegion>())
                    sentinels = ArtworkSettingsAccessor.SetAdjustment(sentinels, mode, region,
                        ArtworkSettingsAccessor.GetAdjustment(sentinels, mode, region) with
                        { Brightness = 121 + (int)region + 10 * (int)mode });
            InvokeMainWindowMethod(fixture.Window, "SetResolvedVisualSettings", fixture.ThemeId, sentinels);
            view.UpdateSettings(GetArtworkLibrarySettings(fixture.Window));
            foreach (var region in ArtworkLibraryCardRegions)
                foreach (var mode in Enum.GetValues<ArtworkColorMode>())
                {
                    var before = GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId];
                    await view.SelectItemAsync(view.Items.First(item => item.Id == fixture.FirstImage.Id));
                    await view.SelectTargetAsync(fixture.ThemeId, region, mode);
                    Ensure(view.SelectedRegion == region && view.SelectedMode == mode && view.IsPreviewReady && view.ApplyButton.IsEnabled,
                        $"The {region}/{mode} detail must retain its real target and produce a usable replacement preview.");
                    var request = new ArtworkLibraryApplyEventArgs(fixture.ThemeId, region, mode, fixture.FirstImage,
                        view.DraftAdjustment with { Brightness = 153 + (int)region + (int)mode });
                    await InvokeMainWindowTaskAsync(fixture.Window, "ApplyArtworkLibraryAsync", request);
                    var applied = GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId];
                    var appliedSlot = ArtworkSettingsAccessor.GetAdjustment(applied, mode, region);
                    Ensure(appliedSlot.CustomImagePath == fixture.FirstImage.StoredPath &&
                           appliedSlot.Brightness == request.Adjustment.Brightness,
                        $"Applying {region}/{mode} must save that picture and its exact draft effects.");
                    EnsureLibraryOtherSlotsUnchanged(before, applied, region, mode, "Applying a card picture");
                    using (var persisted = new UiPreferencesStore(fixture.DataDirectory))
                    {
                        var reloaded = ThemeArtworkSettingsResolver.Resolve(defaults.Defaults,
                            persisted.Load().ThemeVisualOverrides[fixture.ThemeId]).Settings;
                        Ensure(ThemeVisualSettingsSemanticComparer.AdjustmentEquals(
                                ArtworkSettingsAccessor.GetAdjustment(reloaded, mode, region), appliedSlot),
                            $"The sparse {region}/{mode} override must survive a fresh settings-store load.");
                        EnsureLibraryOtherSlotsUnchanged(applied, reloaded, region, mode, "Reloading the saved card override");
                    }
                    await InvokeMainWindowTaskAsync(fixture.Window, "UndoArtworkLibraryAsync");
                    Ensure(ThemeVisualSettingsSemanticComparer.AdjustmentEquals(
                            ArtworkSettingsAccessor.GetAdjustment(GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId], mode, region),
                            ArtworkSettingsAccessor.GetAdjustment(before, mode, region)),
                        $"Undo must restore the exact previous {region}/{mode} adjustment, including its nondefault effects.");
                    EnsureLibraryOtherSlotsUnchanged(before, GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId],
                        region, mode, "Undoing a card replacement");

                    await InvokeMainWindowTaskAsync(fixture.Window, "ApplyArtworkLibraryAsync", request);
                    await InvokeMainWindowTaskAsync(fixture.Window, "RestoreArtworkLibraryAsync",
                        new ArtworkLibraryTargetEventArgs(fixture.ThemeId, region, mode));
                    var restored = GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId];
                    var restoredSlot = ArtworkSettingsAccessor.GetAdjustment(restored, mode, region);
                    Ensure(ThemeVisualSettingsSemanticComparer.AdjustmentEquals(restoredSlot,
                            ArtworkSettingsAccessor.GetAdjustment(baseline, mode, region)),
                        $"Restore must recover the native {region}/{mode} original and defaults.");
                    EnsureLibraryOtherSlotsUnchanged(applied, restored, region, mode, "Restoring a card original");
                    Ensure(view.IsPreviewReady && view.SelectedItem is { IsBuiltIn: true } &&
                           view.IsCurrentTargetDefault && view.CurrentTargetImageLabel == "主题原图" &&
                           string.Equals(view.PreviewIdentityPath,
                               fixture.Package.AssetPaths[ExpectedLibraryAssetKey(fixture.Package, region, mode)],
                               StringComparison.OrdinalIgnoreCase) &&
                           ThemeVisualSettingsSemanticComparer.AdjustmentEquals(view.DraftAdjustment,
                               restoredSlot with { CustomImagePath = null, ThemeAssetKey = null }),
                        $"Restoring {region}/{mode} must also replace the detail's image and framing with the saved original.");
                    await InvokeMainWindowTaskAsync(fixture.Window, "UndoArtworkLibraryAsync");
                    Ensure(ThemeVisualSettingsSemanticComparer.AdjustmentEquals(
                               ArtworkSettingsAccessor.GetAdjustment(GetArtworkLibrarySettings(fixture.Window)[fixture.ThemeId], mode, region),
                               appliedSlot) && view.SelectedItem?.Id == fixture.FirstImage.Id &&
                           string.Equals(view.PreviewIdentityPath, fixture.FirstImage.AbsolutePath, StringComparison.OrdinalIgnoreCase),
                        $"Undoing restore must return the custom {region}/{mode} picture in both saved settings and visible preview.");
                }
        });

    static Task ArtworkLibraryCardTargetsRollBackFailedApplyRestoreAndUndoAsync() =>
        RunArtworkLibraryExperienceAsync(async fixture =>
        {
            var view = fixture.Window.ArtworkLibraryPage;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var dirtyField = typeof(MainWindow).GetField("_preferencesDirty", flags)!;
            var undoField = typeof(MainWindow).GetField("_artworkLibraryUndo", flags)!;
            foreach (var region in ArtworkLibraryCardRegions)
                foreach (var mode in Enum.GetValues<ArtworkColorMode>())
                {
                    await view.SelectItemAsync(view.Items.First(item => item.Id == fixture.FirstImage.Id));
                    await view.SelectTargetAsync(fixture.ThemeId, region, mode);
                    await InvokeMainWindowTaskAsync(fixture.Window, "ApplyArtworkLibraryAsync",
                        new ArtworkLibraryApplyEventArgs(fixture.ThemeId, region, mode, fixture.FirstImage, view.DraftAdjustment));
                    var settingsBefore = JsonSerializer.Serialize(GetArtworkLibrarySettings(fixture.Window));
                    var undoBefore = undoField.GetValue(fixture.Window);
                    var bytesBefore = await File.ReadAllBytesAsync(fixture.PreferencesPath);
                    Ensure(undoBefore is not null && dirtyField.GetValue(fixture.Window) is false,
                        "The failure regression needs one successful, clean saved card replacement and its undo entry.");
                    using (new FileStream(fixture.PreferencesPath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        foreach (var operation in ArtworkLibraryFailedOperations)
                        {
                            if (operation == "apply")
                                await InvokeMainWindowTaskAsync(fixture.Window, "ApplyArtworkLibraryAsync",
                                    new ArtworkLibraryApplyEventArgs(fixture.ThemeId, region, mode, fixture.SecondImage,
                                        view.DraftAdjustment with { Brightness = 178 }));
                            else if (operation == "restore")
                                await InvokeMainWindowTaskAsync(fixture.Window, "RestoreArtworkLibraryAsync",
                                    new ArtworkLibraryTargetEventArgs(fixture.ThemeId, region, mode));
                            else
                                await InvokeMainWindowTaskAsync(fixture.Window, "UndoArtworkLibraryAsync");
                            Ensure(view.StatusText.Text.Contains("操作未完成", StringComparison.Ordinal) &&
                                   JsonSerializer.Serialize(GetArtworkLibrarySettings(fixture.Window)) == settingsBefore &&
                                   dirtyField.GetValue(fixture.Window) is false &&
                                   ReferenceEquals(undoField.GetValue(fixture.Window), undoBefore) && view.TargetControls.IsEnabled,
                                $"Failed {operation} at {region}/{mode} must preserve all settings, clean state, undo, and usable controls.");
                        }
                    }
                    Ensure((await File.ReadAllBytesAsync(fixture.PreferencesPath)).SequenceEqual(bytesBefore),
                        $"Rejected {region}/{mode} operations must leave persisted preferences byte-for-byte unchanged.");
                }
        });

    static async Task ArtworkLibraryCardSlotsProduceIndependentRuntimeImageOverridesAsync()
    {
        var root = ArtworkLibraryTestDirectory();
        try
        {
            var original = Path.Combine(root, "background.png");
            var replacement = Path.Combine(root, "card.png");
            await File.WriteAllBytesAsync(original, OnePixelPng);
            await File.WriteAllBytesAsync(replacement, TwoPixelPng);
            var settings = new ThemeVisualSettings
            {
                Light = new() { Hero = new() { CustomImagePath = original, Brightness = 111 } },
            };
            foreach (var region in ArtworkLibraryCardRegions)
                foreach (var mode in Enum.GetValues<ArtworkColorMode>())
                    settings = ArtworkSettingsAccessor.SetAdjustment(settings, mode, region,
                        new ThemeArtworkAdjustment { CustomImagePath = replacement, Brightness = 130 + (int)region + 10 * (int)mode });
            await using var runtime = new ThemeRuntime(new LoopbackCdpDiscovery(),
                new ThemePayloadBuilder(new Dictionary<string, string>
                {
                    [ThemePayloadBuilder.OpenRuntimeAdapterKey] = GetSourceRuntimeAssets(FindRepositoryRoot()).RuntimePath,
                }));
            var payload = await runtime.BuildVisualSettingsPayloadAsync(settings, CancellationToken.None);
            using var document = JsonDocument.Parse(payload.SettingsJson);
            var heroKey = document.RootElement.GetProperty("light").GetProperty("hero").GetProperty("customImageKey").GetString();
            string? cardKey = null;
            foreach (var region in ArtworkLibraryCardRegions)
                foreach (var mode in Enum.GetValues<ArtworkColorMode>())
                {
                    var slot = document.RootElement.GetProperty(mode == ArtworkColorMode.Dark ? "dark" : "light")
                        .GetProperty(LibraryRuntimeSlotKey(region));
                    var key = slot.GetProperty("customImageKey").GetString();
                    cardKey ??= key;
                    Ensure(key == cardKey && key != heroKey &&
                           slot.GetProperty("brightness").GetDouble() == 130 + (int)region + 10 * (int)mode,
                        $"The renderer payload must carry independent {region}/{mode} effects and the correct shared replacement image key.");
                }
            Ensure(payload.ImagePaths.Count == 2 && !payload.SettingsJson.Contains("customImagePath", StringComparison.Ordinal) &&
                   !payload.SettingsJson.Contains(root, StringComparison.OrdinalIgnoreCase),
                "The eight card overrides must deduplicate original bytes and never leak local paths into renderer settings.");
            foreach (var region in ArtworkLibraryCardRegions)
                foreach (var mode in Enum.GetValues<ArtworkColorMode>())
                    settings = ArtworkSettingsAccessor.SetAdjustment(settings, mode, region, new ThemeArtworkAdjustment());
            var cleared = await runtime.BuildVisualSettingsPayloadAsync(settings, CancellationToken.None);
            using var clearedDocument = JsonDocument.Parse(cleared.SettingsJson);
            Ensure(cleared.ImagePaths.Count == 1 && cleared.ImagePaths.ContainsKey(heroKey!),
                "Restoring all card originals must withdraw their image key while preserving the unrelated hero override.");
            foreach (var region in ArtworkLibraryCardRegions)
                foreach (var mode in Enum.GetValues<ArtworkColorMode>())
                    Ensure(!clearedDocument.RootElement.GetProperty(mode == ArtworkColorMode.Dark ? "dark" : "light")
                            .GetProperty(LibraryRuntimeSlotKey(region)).TryGetProperty("customImageKey", out _),
                        $"Restored {region}/{mode} must omit the withdrawn custom key from the renderer payload.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void EnsureLibraryOtherSlotsUnchanged(ThemeVisualSettings before, ThemeVisualSettings after,
        ArtworkRegion changedRegion, ArtworkColorMode changedMode, string operation)
    {
        foreach (var mode in Enum.GetValues<ArtworkColorMode>())
            foreach (var region in Enum.GetValues<ArtworkRegion>())
            {
                if (region == changedRegion && mode == changedMode) continue;
                Ensure(ThemeVisualSettingsSemanticComparer.AdjustmentEquals(
                        ArtworkSettingsAccessor.GetAdjustment(before, mode, region),
                        ArtworkSettingsAccessor.GetAdjustment(after, mode, region)),
                    $"{operation} must preserve unrelated {region}/{mode} settings.");
            }
    }

    private static string LibraryRuntimeSlotKey(ArtworkRegion region) => region switch
    {
        ArtworkRegion.TaskLeft => "taskLeft",
        ArtworkRegion.Memory => "memory",
        ArtworkRegion.TaskRightSecondary => "taskRightSecondary",
        ArtworkRegion.TaskRightPrimary => "taskRightPrimary",
        _ => throw new ArgumentOutOfRangeException(nameof(region)),
    };

    private static string ExpectedLibraryAssetKey(ThemePackage package, ArtworkRegion region, ArtworkColorMode mode)
    {
        var key = region switch
        {
            ArtworkRegion.Hero => "hero",
            ArtworkRegion.Sidebar => "sidebar",
            ArtworkRegion.Chat => "chat",
            ArtworkRegion.TaskLeft => "task-left",
            ArtworkRegion.Memory => "memory",
            ArtworkRegion.TaskRightSecondary => "task-right-secondary",
            ArtworkRegion.TaskRightPrimary => "task-right-primary",
            _ => throw new ArgumentOutOfRangeException(nameof(region)),
        };
        if (region is ArtworkRegion.Hero or ArtworkRegion.Sidebar or ArtworkRegion.Chat or ArtworkRegion.Memory)
            return key + (mode == ArtworkColorMode.Dark ? "-dark" : "-light");
        return mode == ArtworkColorMode.Dark && package.AssetPaths.ContainsKey(key + "-dark") ? key + "-dark" : key;
    }
}
