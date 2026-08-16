using System.Text.Json;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;

internal static partial class TestSuite
{
    private static readonly JsonSerializerOptions ArtworkIndentedJsonOptions =
        new() { WriteIndented = true };

    private static readonly JsonSerializerOptions ArtworkWebIndentedJsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly string[] ArtworkFixtureAssetKeys =
    [
        "hero-light", "hero-dark", "sidebar-light", "sidebar-dark", "chat-light", "chat-dark",
    ];

    static async Task ArtworkAbsoluteCompositionAndSparseSchemaMigrationWorkAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var themeRoot = Path.Combine(repositoryRoot, "themes", "cartethyia.gale-tide-crown");
        var loaded = await new ThemePackageLoader().LoadAsync(themeRoot);
        Ensure(loaded.Validation.IsValid, FormatIssues(loaded.Validation));
        var package = loaded.Package
            ?? throw new InvalidOperationException("The Cartethyia theme did not load.");
        var defaultsResult = await new ArtworkThemeDefaultsStore().LoadAsync(package);
        Ensure(defaultsResult.IsExact && defaultsResult.Diagnostic is null,
            $"Cartethyia artwork defaults fell back: {defaultsResult.Diagnostic}");

        var resolution = ThemeArtworkSettingsResolver.Resolve(defaultsResult.Defaults, null);
        var sidebar = resolution.Dark.Sidebar;
        var placement = sidebar.Adjustment.Placement
            ?? throw new InvalidOperationException("Cartethyia dark sidebar placement is missing.");
        Ensure(sidebar.AssetKey == "sidebar-dark" &&
               sidebar.Adjustment.ThemeAssetKey == "sidebar-dark" &&
               placement.SizeMode == ThemeArtworkSizeMode.Explicit &&
               placement.Width == ThemeArtworkLength.Percent(355d) &&
               placement.Height == ThemeArtworkLength.Auto &&
               placement.PositionX == ThemeArtworkPositionValue.Percent(52d) &&
               placement.PositionY == ThemeArtworkPositionValue.Pixels(-200d),
            "Cartethyia dark sidebar must resolve 52% -200px / 355% auto exactly.");
        Ensure(sidebar.Adjustment.CompositionMode == ThemeArtworkCompositionMode.Theme &&
               sidebar.Adjustment.Zoom == 100d &&
               sidebar.Adjustment.OffsetX == 0d &&
               sidebar.Adjustment.OffsetY == 0d,
            "A theme-default slot must not add a second user transform.");

        await using (var payloadRuntime = new ThemeRuntime(
                         new LoopbackCdpDiscovery(),
                         new ThemePayloadBuilder(new Dictionary<string, string>
                         {
                             [ThemePayloadBuilder.OpenRuntimeAdapterKey] =
                                 GetSourceRuntimeAssets(repositoryRoot).RuntimePath,
                         })))
        {
            var payload = await payloadRuntime.BuildVisualSettingsPayloadAsync(
                resolution.Settings,
                CancellationToken.None);
            using var payloadDocument = JsonDocument.Parse(payload.SettingsJson);
            var runtimeSidebar = payloadDocument.RootElement
                .GetProperty("dark")
                .GetProperty("sidebar");
            var runtimePlacement = runtimeSidebar.GetProperty("placement");
            Ensure(runtimeSidebar.GetProperty("compositionMode").GetString() == "Theme" &&
                   runtimeSidebar.GetProperty("zoom").GetDouble() == 100d &&
                   runtimeSidebar.GetProperty("offsetX").GetDouble() == 0d &&
                   runtimeSidebar.GetProperty("offsetY").GetDouble() == 0d &&
                   runtimePlacement.GetProperty("sizeMode").GetString() == "Explicit" &&
                   runtimePlacement.GetProperty("width").GetProperty("unit").GetString() == "Percent" &&
                   runtimePlacement.GetProperty("width").GetProperty("value").GetDouble() == 355d &&
                   runtimePlacement.GetProperty("height").GetProperty("unit").GetString() == "Auto" &&
                   runtimePlacement.GetProperty("positionX").GetProperty("value").GetDouble() == 52d &&
                   runtimePlacement.GetProperty("positionY").GetProperty("value").GetDouble() == -200d,
                "The renderer payload must receive Cartethyia's final typed placement directly, with neutral legacy transforms.");
        }

        var projected = ArtworkPlacementMapper.Project(
            placement,
            new ArtworkSize(1024d, 1536d),
            new ArtworkSize(260d, 800d));
        EnsureAlmostEqual(projected.RenderedImage.Width, 923d, "Cartethyia rendered width");
        EnsureAlmostEqual(projected.RenderedImage.Height, 1384.5d, "Cartethyia rendered height");
        EnsureAlmostEqual(projected.RenderedImage.X, -344.76d, "Cartethyia rendered X");
        EnsureAlmostEqual(projected.RenderedImage.Y, -200d, "Cartethyia rendered Y");
        Ensure(projected.CoversSurface && !projected.IsDistorted,
            "Cartethyia page-effect projection must cover without distorting the source.");

        var customSpec = ArtworkPlacementMapper.CommitCrop(
            new ThemeArtworkSourcePlacement
            {
                SourceX = .2d,
                SourceY = .1d,
                SourceWidth = .5d,
                SourceHeight = 260d,
            },
            new ArtworkSize(1024d, 1536d),
            new ArtworkSize(260d, 800d));
        var custom = ArtworkSettingsReducer.SetCustomPlacement(
            resolution.Settings,
            ArtworkColorMode.Dark,
            ArtworkRegion.Sidebar,
            customSpec);
        var customSidebar = custom.Dark.Sidebar;
        Ensure(customSidebar.CompositionMode == ThemeArtworkCompositionMode.Custom &&
               customSidebar.Zoom == 100d &&
               customSidebar.OffsetX == 0d &&
               customSidebar.OffsetY == 0d,
            "Starting a custom crop must atomically neutralize legacy transforms.");
        var customProjection = ArtworkPlacementMapper.Project(
            customSidebar.Placement!,
            new ArtworkSize(1024d, 1536d),
            new ArtworkSize(260d, 800d));
        Ensure(!customProjection.IsDistorted,
            "A committed crop must preserve the source aspect at the target aspect.");

        var fixedWidthSidebar = ArtworkPlacementMapper.AdaptFixedWidthSidebar(
            new ThemeArtworkPlacementSpec
            {
                SizeMode = ThemeArtworkSizeMode.Explicit,
                Width = ThemeArtworkLength.Percent(100d),
                Height = ThemeArtworkLength.Percent(100d),
                PositionX = ThemeArtworkPositionValue.Center,
                PositionY = ThemeArtworkPositionValue.Center,
            },
            new ArtworkSize(260d, 800d),
            new ArtworkSize(260d, 800d));
        var tallSidebarProjection = ArtworkPlacementMapper.Project(
            fixedWidthSidebar,
            new ArtworkSize(260d, 800d),
            new ArtworkSize(260d, 800d));
        var shortSidebarProjection = ArtworkPlacementMapper.Project(
            fixedWidthSidebar,
            new ArtworkSize(260d, 800d),
            new ArtworkSize(260d, 620d));
        Ensure(fixedWidthSidebar.Height == ThemeArtworkLength.Auto &&
               fixedWidthSidebar.PositionY == ThemeArtworkPositionValue.Pixels(0d) &&
               !tallSidebarProjection.IsDistorted &&
               !shortSidebarProjection.IsDistorted,
            "Sidebar placement must derive height from its fixed horizontal scale and retain the full-height top edge.");
        EnsureAlmostEqual(
            shortSidebarProjection.RenderedImage.Width,
            tallSidebarProjection.RenderedImage.Width,
            "fixed-width sidebar rendered width");
        EnsureAlmostEqual(
            shortSidebarProjection.RenderedImage.Height,
            tallSidebarProjection.RenderedImage.Height,
            "fixed-width sidebar rendered height");
        EnsureAlmostEqual(
            shortSidebarProjection.RenderedImage.Y,
            tallSidebarProjection.RenderedImage.Y,
            "fixed-width sidebar top edge");

        var anchoredCrop = ArtworkPlacementMapper.CommitCrop(
            new ThemeArtworkSourcePlacement
            {
                Mode = ThemeArtworkPlacementMode.Crop,
                SourceX = 0d,
                SourceY = .1d,
                SourceWidth = 1d,
                SourceHeight = .8d,
            },
            new ArtworkSize(260d, 1000d),
            new ArtworkSize(260d, 800d),
            fixedWidthSurface: true);
        Ensure(anchoredCrop.Height == ThemeArtworkLength.Auto &&
               anchoredCrop.PositionY.Kind == ThemeArtworkPositionKind.Pixels,
            "A sidebar crop must persist its full-height top coordinate in pixels.");
        EnsureAlmostEqual(
            anchoredCrop.PositionY.Value,
            -100d,
            "anchored sidebar persisted top edge");
        var anchoredShortProjection = ArtworkPlacementMapper.Project(
            anchoredCrop,
            new ArtworkSize(260d, 1000d),
            new ArtworkSize(260d, 620d));
        EnsureAlmostEqual(
            anchoredShortProjection.RenderedImage.Y,
            -100d,
            "anchored short sidebar top edge");

        const string schemaFive = """
            {
              "SchemaVersion": 5,
              "ThemeVisualSettings": {
                "cartethyia.gale-tide-crown": {
                  "Dark": {
                    "Sidebar": {
                      "CustomImagePath": "personalization/images/sentinel.png",
                      "Brightness": 88,
                      "Zoom": 127,
                      "OffsetX": -31,
                      "OffsetY": 19
                    },
                    "Chat": { "Zoom": 100, "OffsetX": 0, "OffsetY": 0 }
                  }
                }
              }
            }
            """;
        var options = ArtworkIndentedJsonOptions;
        var migrated = UiPreferencesMigration.Deserialize(schemaFive, options, out var didMigrate);
        var migratedOverride = migrated.ThemeVisualOverrides["cartethyia.gale-tide-crown"];
        Ensure(didMigrate &&
               migrated.SchemaVersion == UiPreferences.CurrentSchemaVersion &&
               migrated.ThemeVisualSettings.Count == 0,
            "Schema five must migrate to the current sparse preference state.");
        Ensure(migratedOverride.Dark?.Sidebar is
        {
            ImageSourceMode: ThemeArtworkImageSourceMode.Custom,
            CustomImagePath: "personalization/images/sentinel.png",
            CompositionMode: ThemeArtworkCompositionMode.Legacy,
            LegacyZoom: 127,
            LegacyOffsetX: -31,
            LegacyOffsetY: 19,
            Brightness: 88,
        } &&
            migratedOverride.Dark.Chat is null,
            "Only schema-five non-default fields may become sparse overrides; legacy crop and image must survive.");
        var savedJson = JsonSerializer.Serialize(UiPreferencesMigration.PrepareForSave(migrated), options);
        Ensure(savedJson.Contains("\"ThemeVisualOverrides\"", StringComparison.Ordinal) &&
               !savedJson.Contains("\"ThemeVisualSettings\"", StringComparison.Ordinal) &&
               savedJson.Contains("personalization/images/sentinel.png", StringComparison.Ordinal),
            "Schema-six persistence must omit the legacy full settings graph without changing image references.");

        var resolvedLegacy = ThemeArtworkSettingsResolver.Resolve(
            defaultsResult.Defaults,
            migratedOverride).Dark.Sidebar.Adjustment;
        Ensure(resolvedLegacy.CompositionMode == ThemeArtworkCompositionMode.Legacy &&
               resolvedLegacy.Placement == sidebar.ThemeDefaultAdjustment.Placement &&
               resolvedLegacy.Zoom == 127d &&
               resolvedLegacy.OffsetX == -31d &&
               resolvedLegacy.OffsetY == 19d,
            "A migrated legacy slot must retain the theme base placement plus its published transform.");

        var heroWithLocalImage = ArtworkSettingsReducer.UpdateAdjustment(
            resolution.Settings,
            ArtworkColorMode.Light,
            ArtworkRegion.Hero,
            adjustment => adjustment with
            {
                CustomImagePath = "personalization/images/hero-local.png",
                Brightness = 73d,
            });
        var originalBaseline = ArtworkSettingsReducer.RestoreSlotToOriginalBaseline(
            heroWithLocalImage,
            ArtworkColorMode.Light,
            ArtworkRegion.Hero);
        var originalHero = originalBaseline.Light.Hero;
        Ensure(originalHero.CustomImagePath is null &&
               originalHero.ThemeAssetKey == resolution.Light.Hero.AssetKey &&
               originalHero.CompositionMode == ThemeArtworkCompositionMode.Custom &&
               originalHero.Placement == ArtworkOriginalBaseline.ContainCenteredPlacement &&
               originalHero.Brightness == 100d &&
               originalHero.Contrast == 100d &&
               originalHero.Saturation == 100d &&
               originalHero.Opacity == 100d &&
               originalHero.GradientVeil is { Enabled: false } &&
               originalHero.ReadabilityVeil is { Enabled: false } &&
               originalHero.Motion is null,
            "Original baseline must switch to the manifest asset, full contain framing, neutral effects, and no motion.");
        var originalOverride = ThemeArtworkSettingsResolver.CreateSparseOverride(
            defaultsResult.Defaults,
            originalBaseline).Light?.Hero;
        Ensure(originalOverride is
        {
            CompositionMode: ThemeArtworkCompositionMode.Custom,
            MotionEnabled: false,
            ImageSourceMode: null,
        } &&
               originalOverride.Placement == ArtworkOriginalBaseline.ContainCenteredPlacement,
            "Original baseline must persist as a reversible sparse user override without a local image reference.");
        var themeRestored = ArtworkSettingsReducer.RestoreSlotToTheme(
            originalBaseline,
            ArtworkColorMode.Light,
            ArtworkRegion.Hero,
            resolution.Light.Hero.ThemeDefaultAdjustment);
        Ensure(ThemeVisualSettingsSemanticComparer.AdjustmentEquals(
                   themeRestored.Light.Hero,
                   resolution.Light.Hero.ThemeDefaultAdjustment) &&
               ThemeArtworkSettingsResolver.CreateSparseOverride(
                   defaultsResult.Defaults,
                   themeRestored).Light?.Hero is null,
            "Restoring the slot to its theme recommendation must delete the sparse override.");

        var validMotion = defaultsResult.Defaults.Slots.Hero.Light.Motion
            ?? throw new InvalidOperationException("Cartethyia hero motion is missing.");
        var invalidDefaults = defaultsResult.Defaults with
        {
            Slots = defaultsResult.Defaults.Slots with
            {
                Hero = defaultsResult.Defaults.Slots.Hero with
                {
                    Light = defaultsResult.Defaults.Slots.Hero.Light with
                    {
                        Motion = validMotion with { DurationMs = 99d },
                    },
                },
            },
        };
        try
        {
            ThemeArtworkDefaultsValidator.Validate(invalidDefaults);
            throw new InvalidOperationException("An out-of-contract artwork motion was accepted.");
        }
        catch (InvalidDataException exception) when (
            exception.Message.Contains("duration", StringComparison.OrdinalIgnoreCase))
        {
        }

        var fallbackRoot = Path.Combine(
            Path.GetTempPath(),
            $"tessalume-invalid-artwork-defaults-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fallbackRoot);
        try
        {
            var fallbackPath = Path.Combine(fallbackRoot, "artwork-defaults.json");
            var assetPath = Path.Combine(fallbackRoot, "source.png");
            await File.WriteAllBytesAsync(assetPath, [1]);
            await File.WriteAllTextAsync(
                fallbackPath,
                JsonSerializer.Serialize(
                    invalidDefaults with { ThemeId = "invalid.defaults" },
                    ArtworkWebIndentedJsonOptions));
            var assetPaths = ArtworkFixtureAssetKeys
                .ToDictionary(key => key, _ => assetPath, StringComparer.OrdinalIgnoreCase);
            var fallbackPackage = new ThemePackage(
                fallbackRoot,
                Path.Combine(fallbackRoot, ThemePackageLoader.ManifestFileName),
                new ThemeManifest
                {
                    Id = "invalid.defaults",
                    Name = "Invalid Defaults Probe",
                    Version = "1.0.0",
                    Author = "Tessalume Tests",
                    EntryPoints = new ThemeEntryPoints { ArtworkDefaults = "artwork-defaults.json" },
                },
                null,
                null,
                assetPaths,
                null,
                null,
                fallbackPath);
            var fallback = await new ArtworkThemeDefaultsStore().LoadAsync(fallbackPackage);
            var fallbackResolution = ThemeArtworkSettingsResolver.Resolve(fallback.Defaults, null);
            Ensure(!fallback.IsExact &&
                   fallback.Diagnostic?.Contains("无法精确读取", StringComparison.Ordinal) == true &&
                   fallbackResolution.Light.Hero.Adjustment.Placement?.SizeMode ==
                       ThemeArtworkSizeMode.Contain &&
                   fallbackResolution.Dark.Sidebar.Adjustment.Placement?.SizeMode ==
                       ThemeArtworkSizeMode.Contain &&
                fallbackResolution.Dark.Chat.Adjustment.Placement?.PositionX ==
                       ThemeArtworkPositionValue.Center,
                "Invalid theme defaults must visibly fall back to complete-image standard framing " +
                $"instead of displaying a false exact crop. exact={fallback.IsExact}; " +
                $"diagnostic={fallback.Diagnostic}; hero={fallbackResolution.Light.Hero.Adjustment.Placement?.SizeMode}; " +
                $"sidebar={fallbackResolution.Dark.Sidebar.Adjustment.Placement?.SizeMode}; " +
                $"chatX={fallbackResolution.Dark.Chat.Adjustment.Placement?.PositionX}");
        }
        finally
        {
            Directory.Delete(fallbackRoot, recursive: true);
        }
    }
}
