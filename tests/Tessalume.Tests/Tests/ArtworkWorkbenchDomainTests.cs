using Tessalume.App.Features.Personalization;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

internal static partial class TestSuite
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZrF8AAAAASUVORK5CYII=");

    private static readonly byte[] TwoPixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGP4z8DwH4QZGBgYGMAAAEcABf2R4v8AAAAASUVORK5CYII=");

    static Task ArtworkWorkbenchKeepsSixTargetsIsolatedAsync()
    {
        var original = CreateDistinctArtworkSettings();
        var ordinal = 0;
        foreach (var mode in Enum.GetValues<ArtworkColorMode>())
        {
            foreach (var region in Enum.GetValues<ArtworkRegion>())
            {
                var value = 130d + ordinal++;
                var changed = ArtworkSettingsReducer.SetParameter(
                    original,
                    mode,
                    region,
                    ArtworkParameter.Brightness,
                    value);

                foreach (var candidateMode in Enum.GetValues<ArtworkColorMode>())
                {
                    foreach (var candidateRegion in Enum.GetValues<ArtworkRegion>())
                    {
                        var before = ReadAdjustment(original, candidateMode, candidateRegion);
                        var after = ReadAdjustment(changed, candidateMode, candidateRegion);
                        if (candidateMode == mode && candidateRegion == region)
                        {
                            Ensure(after.Brightness == value &&
                                   after with { Brightness = before.Brightness } == before,
                                $"Editing {mode}/{region} must change only that target parameter.");
                        }
                        else
                        {
                            Ensure(after == before,
                                $"Editing {mode}/{region} changed {candidateMode}/{candidateRegion}.");
                        }
                    }
                }

                Ensure(changed.Display == original.Display,
                    "Artwork edits must not change display preferences.");
                AssertArtworkImagesPreserved(original, changed, $"edit {mode}/{region}");
            }
        }

        return Task.CompletedTask;
    }

    static Task ArtworkWorkbenchLocalResetScopesAreStrictAsync()
    {
        var original = CreateDistinctArtworkSettings();
        var defaults = new ThemeArtworkAdjustment();

        var parameterReset = ArtworkSettingsReducer.Reset(
            original,
            ArtworkResetRequest.ForParameter(
                ArtworkColorMode.Dark,
                ArtworkRegion.Sidebar,
                ArtworkParameter.Brightness));
        var parameterBefore = ReadAdjustment(
            original,
            ArtworkColorMode.Dark,
            ArtworkRegion.Sidebar);
        var parameterAfter = ReadAdjustment(
            parameterReset,
            ArtworkColorMode.Dark,
            ArtworkRegion.Sidebar);
        AssertOnlyArtworkTargetMayDiffer(
            original,
            parameterReset,
            ArtworkColorMode.Dark,
            ArtworkRegion.Sidebar,
            "single-parameter reset");
        Ensure(parameterAfter.Brightness == defaults.Brightness &&
               parameterAfter with { Brightness = parameterBefore.Brightness } == parameterBefore,
            "A single-parameter reset must preserve every sibling parameter.");
        AssertArtworkImagesPreserved(original, parameterReset, "single-parameter reset");

        foreach (var group in Enum.GetValues<ArtworkParameterGroup>())
        {
            var groupReset = ArtworkSettingsReducer.Reset(
                original,
                ArtworkResetRequest.ForGroup(
                    ArtworkColorMode.Light,
                    ArtworkRegion.Chat,
                    group));
            var before = ReadAdjustment(original, ArtworkColorMode.Light, ArtworkRegion.Chat);
            var after = ReadAdjustment(groupReset, ArtworkColorMode.Light, ArtworkRegion.Chat);
            AssertOnlyArtworkTargetMayDiffer(
                original,
                groupReset,
                ArtworkColorMode.Light,
                ArtworkRegion.Chat,
                $"{group} group reset");
            Ensure(after == ResetExpectedGroup(before, group),
                $"Resetting {group} must reset exactly that parameter group.");
            AssertArtworkImagesPreserved(original, groupReset, $"{group} group reset");
        }

        var regionReset = ArtworkSettingsReducer.Reset(
            original,
            ArtworkResetRequest.ForRegionMode(ArtworkColorMode.Dark, ArtworkRegion.Hero));
        var regionBefore = ReadAdjustment(original, ArtworkColorMode.Dark, ArtworkRegion.Hero);
        AssertOnlyArtworkTargetMayDiffer(
            original,
            regionReset,
            ArtworkColorMode.Dark,
            ArtworkRegion.Hero,
            "region-mode reset");
        Ensure(ReadAdjustment(regionReset, ArtworkColorMode.Dark, ArtworkRegion.Hero) ==
               new ThemeArtworkAdjustment { CustomImagePath = regionBefore.CustomImagePath }.Normalize(),
            "A region-mode reset must restore every parameter while preserving its image.");
        AssertArtworkImagesPreserved(original, regionReset, "region-mode reset");

        return Task.CompletedTask;
    }

    static Task ArtworkWorkbenchHistoryCoalescesAndStaysBoundedAsync()
    {
        const string themeId = "history.theme";
        var original = CreateDistinctArtworkSettings();
        var session = new ArtworkWorkbenchSession(new ArtworkHistoryService(capacity: 3));
        Ensure(session.BeginGesture(themeId, original) &&
               !session.BeginGesture(themeId, original),
            "Only one history gesture may be active for a theme.");

        var current = original;
        foreach (var offset in new[] { 24d, 48d, 72d, 96d })
        {
            current = ArtworkWorkbenchSession.UpdateGesture(
                current,
                settings => ArtworkSettingsReducer.SetParameter(
                    settings,
                    ArtworkColorMode.Light,
                    ArtworkRegion.Hero,
                    ArtworkParameter.OffsetX,
                    offset));
        }
        Ensure(session.EndGesture(themeId, current),
            "A changed gesture must commit one history entry.");
        var gestureStatus = session.History.GetStatus(themeId);
        Ensure(gestureStatus is { UndoCount: 1, RedoCount: 0, GestureActive: false },
            "Multiple updates inside one gesture must create exactly one undo entry.");

        var gestureResult = current;
        Ensure(session.TryUndo(themeId, current, out current) && current == original,
            "Undo must restore the state from before the entire gesture.");
        Ensure(session.TryRedo(themeId, current, out current) && current == gestureResult,
            "Redo must restore the final coalesced gesture state.");
        Ensure(session.TryUndo(themeId, current, out current) && current == original,
            "A second undo must return to the gesture origin before branching.");

        current = session.Mutate(
            themeId,
            current,
            settings => ArtworkSettingsReducer.SetParameter(
                settings,
                ArtworkColorMode.Dark,
                ArtworkRegion.Chat,
                ArtworkParameter.Opacity,
                63d));
        Ensure(session.History.GetStatus(themeId).RedoCount == 0 &&
               !session.TryRedo(themeId, current, out _),
            "A new edit after undo must discard the redo branch.");
        Ensure(session.History.GetStatus("other.theme") == new ArtworkHistoryStatus(0, 0, false),
            "History stacks must remain isolated per theme.");

        var bounded = new ArtworkWorkbenchSession(new ArtworkHistoryService(capacity: 3));
        var boundedState = original;
        for (var value = 121; value <= 125; value++)
        {
            boundedState = bounded.Mutate(
                "bounded.theme",
                boundedState,
                settings => ArtworkSettingsReducer.SetParameter(
                    settings,
                    ArtworkColorMode.Light,
                    ArtworkRegion.Hero,
                    ArtworkParameter.Brightness,
                    value));
        }
        Ensure(bounded.History.GetStatus("bounded.theme").UndoCount == 3,
            "History must discard entries beyond its configured capacity.");
        foreach (var expected in new[] { 124d, 123d, 122d })
        {
            Ensure(bounded.TryUndo("bounded.theme", boundedState, out boundedState) &&
                   ReadAdjustment(
                       boundedState,
                       ArtworkColorMode.Light,
                       ArtworkRegion.Hero).Brightness == expected,
                "Bounded undo must retain the newest entries in order.");
        }
        Ensure(!bounded.TryUndo("bounded.theme", boundedState, out _),
            "History must not expose entries that were evicted by capacity.");

        var motionState = original with
        {
            Light = original.Light with
            {
                Hero = original.Light.Hero with
                {
                    Motion = new ThemeArtworkMotion
                    {
                        Mode = "loop",
                        DurationMs = 19000d,
                        Keyframes =
                        [
                            new ThemeArtworkMotionKeyframe { At = 0d },
                            new ThemeArtworkMotionKeyframe
                            {
                                At = 100d,
                                TranslateX = "-4px",
                                ScaleDelta = .007968d,
                            },
                        ],
                    },
                },
            },
        };
        var normalizedMotionState = motionState.Normalize();
        Ensure(!ReferenceEquals(
                   motionState.Light.Hero.Motion!.Keyframes,
                   normalizedMotionState.Light.Hero.Motion!.Keyframes) &&
               ThemeVisualSettingsSemanticComparer.Instance.Equals(
                   motionState,
                   normalizedMotionState),
            "Normalization may rebuild motion arrays without changing artwork semantics.");
        var semanticHistory = new ArtworkHistoryService();
        Ensure(semanticHistory.BeginGesture("semantic.theme", motionState) &&
               !semanticHistory.EndGesture("semantic.theme", normalizedMotionState) &&
               semanticHistory.GetStatus("semantic.theme").UndoCount == 0,
            "A motion-array normalization no-op must not create a phantom undo entry.");

        return Task.CompletedTask;
    }

    static Task ArtworkWorkbenchCanvasMappingAndOfflineSessionWorkAsync()
    {
        var source = new ArtworkSize(2048d, 1536d);
        foreach (var (region, target) in new[]
                 {
                     (ArtworkRegion.Hero, new ArtworkSize(1440d, 420d)),
                     (ArtworkRegion.Sidebar, new ArtworkSize(260d, 800d)),
                     (ArtworkRegion.Chat, new ArtworkSize(1440d, 900d)),
                 })
        {
            var fill = ArtworkPlacementMapper.Fill(source, target);
            var fillProjection = ArtworkPlacementMapper.Project(fill, source, target);
            Ensure(fillProjection.CoversSurface &&
                   !fillProjection.IsDistorted &&
                   fillProjection.SourceProjection.Mode == ThemeArtworkPlacementMode.Crop,
                $"{region} fill must create one centered, aspect-correct final crop.");

            var contain = ArtworkPlacementMapper.Contain();
            var containProjection = ArtworkPlacementMapper.Project(contain, source, target);
            Ensure(!containProjection.IsDistorted &&
                   containProjection.SourceProjection.Mode == ThemeArtworkPlacementMode.Contain &&
                   containProjection.SourceViewport.Width >= 1d - .000001d &&
                   containProjection.SourceViewport.Height >= 1d - .000001d,
                $"{region} contain must display the complete source without pretending it fills the target.");

            var moved = ArtworkPlacementMapper.MoveCrop(
                fillProjection.SourceProjection,
                4d,
                -4d,
                source,
                target);
            Ensure(moved.HitTop && moved.HitRight &&
                   moved.Crop.SourceX + moved.Crop.SourceWidth <= 1d + .000001d &&
                   moved.Crop.SourceY >= 0d,
                $"{region} crop movement must stop at real source boundaries.");

            var zoomed = ArtworkPlacementMapper.ZoomAt(
                fillProjection.SourceProjection,
                1.25d,
                .5d,
                .5d,
                source,
                target);
            var committed = ArtworkPlacementMapper.CommitCrop(zoomed.Crop, source, target);
            var projectedAgain = ArtworkPlacementMapper.Project(committed, source, target);
            Ensure(projectedAgain.CoversSurface &&
                   projectedAgain.SourceProjection.SourceWidth <
                   fillProjection.SourceProjection.SourceWidth &&
                   projectedAgain.SourceProjection.SourceHeight <
                   fillProjection.SourceProjection.SourceHeight,
                $"{region} zoom must commit directly to final percentage size/position.");
        }

        var exact = ThemeArtworkPlacementParser.Parse(new ThemeArtworkCssPlacement
        {
            Size = new ThemeArtworkCssSize { Width = "355%", Height = "auto" },
            Position = new ThemeArtworkCssPosition { X = "52%", Y = "-200px" },
        });
        Ensure(exact.SizeCss == "355% auto" &&
               exact.PositionCss == "52% -200px" &&
               exact.Normalize() == exact,
            "Typed px/%/auto placement must round-trip without hidden normalization.");

        var offlineSession = new ArtworkWorkbenchSession();
        var offlineBefore = CreateDistinctArtworkSettings();
        var offlineAfter = offlineSession.Mutate(
            "offline.theme",
            offlineBefore,
            settings => ArtworkSettingsReducer.SetParameter(
                settings,
                ArtworkColorMode.Dark,
                ArtworkRegion.Chat,
                ArtworkParameter.Brightness,
                144d));
        Ensure(ReadAdjustment(
                   offlineAfter,
                   ArtworkColorMode.Dark,
                   ArtworkRegion.Chat).Brightness == 144d &&
               offlineSession.History.GetStatus("offline.theme").CanUndo,
            "An offline workbench session must edit and record local state without a runtime gateway.");
        Ensure(offlineSession.TryUndo("offline.theme", offlineAfter, out var offlineRestored) &&
               offlineRestored == offlineBefore,
            "Offline local edits must remain undoable.");

        return Task.CompletedTask;
    }

    static async Task ArtworkWorkbenchPreviewInfrastructureCachesAndResolvesAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-workbench-preview-{Guid.NewGuid():N}");
        var themeRoot = Path.Combine(root, "theme");
        var themeAssets = Path.Combine(themeRoot, "assets");
        var dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(themeAssets);
        Directory.CreateDirectory(dataRoot);
        try
        {
            var firstPath = Path.Combine(root, "first.png");
            var secondPath = Path.Combine(root, "second.png");
            var thirdPath = Path.Combine(root, "third.png");
            var mutablePath = Path.Combine(root, "mutable.png");
            var corruptPath = Path.Combine(root, "corrupt.png");
            await File.WriteAllBytesAsync(firstPath, OnePixelPng);
            await File.WriteAllBytesAsync(secondPath, TwoPixelPng);
            await File.WriteAllBytesAsync(thirdPath, OnePixelPng);
            await File.WriteAllBytesAsync(mutablePath, OnePixelPng);
            await File.WriteAllBytesAsync(corruptPath, [1, 2, 3, 4, 5, 6]);

            var lru = new ArtworkPreviewImageCache(capacity: 2);
            var first = await lru.LoadAsync(firstPath, 64);
            var second = await lru.LoadAsync(secondPath, 64);
            var secondMetadata = await lru.LoadWithMetadataAsync(secondPath, 64);
            var firstHit = await lru.LoadAsync(firstPath, 64);
            Ensure(ReferenceEquals(first, firstHit) && first.IsFrozen && lru.Count == 2 &&
                   secondMetadata.SourcePixelWidth == 2 &&
                   secondMetadata.SourcePixelHeight == 2,
                "A preview cache hit must return the same frozen decoded bitmap.");
            _ = await lru.LoadAsync(thirdPath, 64);
            Ensure(ReferenceEquals(first, await lru.LoadAsync(firstPath, 64)),
                "Touching an entry must keep it ahead of the LRU eviction boundary.");
            var secondReloaded = await lru.LoadAsync(secondPath, 64);
            Ensure(!ReferenceEquals(second, secondReloaded) && lru.Count == 2,
                "Loading beyond capacity must evict the least-recently-used bitmap.");

            var byteBounded = new ArtworkPreviewImageCache(
                capacity: ArtworkPreviewImageCache.DefaultCapacity,
                byteBudget: 20 * 1024);
            _ = await byteBounded.LoadAsync(firstPath, 64);
            _ = await byteBounded.LoadAsync(secondPath, 64);
            Ensure(byteBounded.Count == 1 &&
                   byteBounded.CachedBytes <= 20 * 1024,
                "The preview cache must evict by decoded byte cost, not only entry count.");

            var invalidation = new ArtworkPreviewImageCache(capacity: 2);
            var beforeChange = await invalidation.LoadAsync(mutablePath, 64);
            await File.WriteAllBytesAsync(mutablePath, TwoPixelPng);
            var afterChange = await invalidation.LoadAsync(mutablePath, 64);
            Ensure(!ReferenceEquals(beforeChange, afterChange) &&
                   invalidation.Count == 1,
                "A changed preview file must invalidate its previous decoded version.");

            var countBeforeCorrupt = invalidation.Count;
            Exception? corruptFailure = null;
            try
            {
                _ = await invalidation.LoadAsync(corruptPath, 64);
            }
            catch (Exception exception)
            {
                corruptFailure = exception;
            }
            Ensure(corruptFailure is not null and not OperationCanceledException &&
                   invalidation.Count == countBeforeCorrupt,
                "A corrupt image must be rejected without entering the preview cache.");

            var themeImagePath = Path.Combine(themeAssets, "hero-light.png");
            var personalSourcePath = Path.Combine(root, "personal.png");
            await File.WriteAllBytesAsync(themeImagePath, TwoPixelPng);
            await File.WriteAllBytesAsync(personalSourcePath, OnePixelPng);
            var imageStore = new PersonalImageStore(dataRoot);
            var storedPersonalPath = await imageStore.ImportAsync(personalSourcePath);
            var package = new ThemePackage(
                themeRoot,
                Path.Combine(themeRoot, "manifest.json"),
                new ThemeManifest
                {
                    Id = "workbench.source",
                    Name = "Workbench Source",
                    Version = "1.0.0",
                    Author = "Tests",
                    Capabilities = new ThemeCapabilities { Light = true, Dark = true },
                },
                null,
                null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["hero-light"] = themeImagePath,
                },
                null,
                null);
            var adjustment = new ThemeArtworkAdjustment
            {
                CustomImagePath = storedPersonalPath,
            };
            var local = ArtworkImageSourceResolver.Resolve(
                package,
                imageStore,
                ArtworkRegion.Hero,
                ArtworkColorMode.Light,
                adjustment);
            Ensure(local is
            {
                SourceKind: ArtworkImageSourceKind.LocalReplacement,
                DisplayName: "本地图片",
            } &&
                   string.Equals(
                       local.AbsolutePath,
                       imageStore.ResolvePath(storedPersonalPath),
                       StringComparison.OrdinalIgnoreCase),
                "A valid local replacement must take precedence over the theme image.");

            File.Delete(imageStore.ResolvePath(storedPersonalPath)!);
            var fallback = ArtworkImageSourceResolver.Resolve(
                package,
                imageStore,
                ArtworkRegion.Hero,
                ArtworkColorMode.Light,
                adjustment);
            Ensure(fallback is
            {
                SourceKind: ArtworkImageSourceKind.ThemeOriginal,
                DisplayName: "主题原图",
            } &&
                   string.Equals(fallback.AbsolutePath, themeImagePath, StringComparison.OrdinalIgnoreCase) &&
                   adjustment.CustomImagePath == storedPersonalPath,
                "A missing local image must fall back to the theme source without mutating persistence.");
            Ensure(ArtworkImageSourceResolver.Resolve(
                       package,
                       imageStore,
                       ArtworkRegion.Chat,
                       ArtworkColorMode.Dark,
                       new ThemeArtworkAdjustment()) is null,
                "A source resolver must report no image when neither source exists.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ThemeVisualSettings CreateDistinctArtworkSettings() => new ThemeVisualSettings
    {
        Light = new ThemeVisualModeSettings
        {
            Hero = CreateDistinctAdjustment(1, "personalization/images/light-hero.png"),
            Sidebar = CreateDistinctAdjustment(2, "personalization/images/light-sidebar.png"),
            Chat = CreateDistinctAdjustment(3, "personalization/images/light-chat.png"),
        },
        Dark = new ThemeVisualModeSettings
        {
            Hero = CreateDistinctAdjustment(4, "personalization/images/dark-hero.png"),
            Sidebar = CreateDistinctAdjustment(5, "personalization/images/dark-sidebar.png"),
            Chat = CreateDistinctAdjustment(6, "personalization/images/dark-chat.png"),
        },
        Display = new ThemeDisplayPreferences
        {
            MotionIntensity = "reduced",
            TextScale = "large",
            Density = "spacious",
        },
    }.Normalize();

    private static ThemeArtworkAdjustment CreateDistinctAdjustment(int index, string imagePath) =>
        new ThemeArtworkAdjustment
        {
            CustomImagePath = imagePath,
            Brightness = 100d + index,
            Contrast = 110d + index,
            Saturation = 90d + index,
            Opacity = 80d + index,
            Zoom = 110d + index,
            OffsetX = 10d + index,
            OffsetY = -10d - index,
            Grayscale = 5d + index,
            HueRotation = 20d + index,
            Blur = index,
            OverlayColor = $"#{index:X1}{index:X1}{index:X1}{index:X1}{index:X1}{index:X1}",
            OverlayOpacity = 10d + index,
            GradientStrength = 20d + index,
            Vignette = 30d + index,
            BlendMode = index % 2 == 0 ? "screen" : "overlay",
            ReadabilityProtection = index % 2 == 0,
        }.Normalize();

    private static ThemeArtworkAdjustment ReadAdjustment(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region) =>
        ReadAdjustment(mode == ArtworkColorMode.Dark ? settings.Dark : settings.Light, region);

    private static ThemeArtworkAdjustment ReadAdjustment(
        ThemeVisualModeSettings mode,
        ArtworkRegion region) => region switch
        {
            ArtworkRegion.Sidebar => mode.Sidebar,
            ArtworkRegion.Chat => mode.Chat,
            _ => mode.Hero,
        };

    private static ThemeArtworkAdjustment ResetExpectedGroup(
        ThemeArtworkAdjustment adjustment,
        ArtworkParameterGroup group)
    {
        var defaults = new ThemeArtworkAdjustment();
        return (group switch
        {
            ArtworkParameterGroup.Basic => adjustment with
            {
                Brightness = defaults.Brightness,
                Contrast = defaults.Contrast,
                Saturation = defaults.Saturation,
                Opacity = defaults.Opacity,
            },
            ArtworkParameterGroup.Composition => adjustment with
            {
                Zoom = defaults.Zoom,
                OffsetX = defaults.OffsetX,
                OffsetY = defaults.OffsetY,
            },
            ArtworkParameterGroup.Mask => adjustment with
            {
                GradientStrength = defaults.GradientStrength,
                GradientVeil = defaults.GradientVeil,
                ReadabilityProtection = defaults.ReadabilityProtection,
                ReadabilityVeil = defaults.ReadabilityVeil,
            },
            ArtworkParameterGroup.Effects => adjustment with
            {
                Grayscale = defaults.Grayscale,
                HueRotation = defaults.HueRotation,
                Blur = defaults.Blur,
                OverlayColor = defaults.OverlayColor,
                OverlayOpacity = defaults.OverlayOpacity,
                Vignette = defaults.Vignette,
                BlendMode = defaults.BlendMode,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
        }).Normalize();
    }

    private static void AssertOnlyArtworkTargetMayDiffer(
        ThemeVisualSettings before,
        ThemeVisualSettings after,
        ArtworkColorMode targetMode,
        ArtworkRegion targetRegion,
        string scenario)
    {
        foreach (var mode in Enum.GetValues<ArtworkColorMode>())
        {
            foreach (var region in Enum.GetValues<ArtworkRegion>())
            {
                if (mode == targetMode && region == targetRegion) continue;
                Ensure(ReadAdjustment(before, mode, region) == ReadAdjustment(after, mode, region),
                    $"{scenario} changed {mode}/{region} outside its scope.");
            }
        }
        Ensure(before.Display == after.Display,
            $"{scenario} changed display preferences outside artwork scope.");
    }

    private static void AssertArtworkImagesPreserved(
        ThemeVisualSettings before,
        ThemeVisualSettings after,
        string scenario)
    {
        foreach (var mode in Enum.GetValues<ArtworkColorMode>())
        {
            foreach (var region in Enum.GetValues<ArtworkRegion>())
            {
                Ensure(string.Equals(
                        ReadAdjustment(before, mode, region).CustomImagePath,
                        ReadAdjustment(after, mode, region).CustomImagePath,
                        StringComparison.Ordinal),
                    $"{scenario} replaced the {mode}/{region} image source.");
            }
        }
    }

    private static void EnsureAlmostEqual(double actual, double expected, string scenario)
    {
        Ensure(Math.Abs(actual - expected) <= 0.000001d,
            $"{scenario} expected {expected}, received {actual}.");
    }
}
