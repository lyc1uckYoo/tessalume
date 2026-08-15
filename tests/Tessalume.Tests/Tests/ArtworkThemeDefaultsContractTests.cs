using System.Buffers.Binary;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;

internal static partial class TestSuite
{
    static async Task ArtworkThemeDefaultsMatchPublishedThemesAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var themeRoots = Directory
            .EnumerateDirectories(Path.Combine(repositoryRoot, "themes"))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Ensure(themeRoots.Length == 12,
            $"The built-in artwork contract must cover exactly 12 themes; found {themeRoots.Length}.");

        var loader = new ThemePackageLoader();
        var store = new ArtworkThemeDefaultsStore();
        var documents = new Dictionary<string, ThemeArtworkDefaultsDocument>(
            StringComparer.OrdinalIgnoreCase);
        var slotCount = 0;
        foreach (var themeRoot in themeRoots)
        {
            var loaded = await loader.LoadAsync(themeRoot);
            Ensure(loaded.Validation.IsValid, FormatIssues(loaded.Validation));
            var package = loaded.Package
                ?? throw new InvalidOperationException($"Theme package did not load: {themeRoot}.");
            var defaults = await store.LoadAsync(package);
            Ensure(package.ArtworkDefaultsPath is not null &&
                   File.Exists(package.ArtworkDefaultsPath) &&
                   string.Equals(
                       package.Manifest.EntryPoints.ArtworkDefaults,
                       "artwork-defaults.json",
                       StringComparison.Ordinal) &&
                   defaults is { IsExact: true, Diagnostic: null },
                $"{package.Manifest.Id} must load its declared defaults without a standard fallback: {defaults.Diagnostic}");
            ThemeArtworkDefaultsValidator.Validate(defaults.Defaults);
            documents[package.Manifest.Id] = defaults.Defaults;

            var resolution = ThemeArtworkSettingsResolver.Resolve(defaults.Defaults, null);
            foreach (var (region, mode, asset, slot, resolved) in EnumerateArtworkSlots(
                         defaults.Defaults,
                         resolution))
            {
                slotCount++;
                Ensure(slot.Asset == asset &&
                       package.AssetPaths.TryGetValue(asset, out var assetPath) &&
                       File.Exists(assetPath),
                    $"{package.Manifest.Id} {region}/{mode} must reference the matching manifest raw asset {asset}.");
                var placement = ThemeArtworkPlacementParser.Parse(slot.Placement);
                Ensure(resolved.AssetKey == asset &&
                       resolved.ThemeDefaultAdjustment.ThemeAssetKey == asset &&
                       resolved.Adjustment.ThemeAssetKey == asset &&
                       resolved.Adjustment.CompositionMode == ThemeArtworkCompositionMode.Theme &&
                       resolved.Adjustment.Placement == placement &&
                       resolved.Adjustment.Zoom == 100d &&
                       resolved.Adjustment.OffsetX == 0d &&
                       resolved.Adjustment.OffsetY == 0d &&
                       resolved.UserOverride is null,
                    $"{package.Manifest.Id} {region}/{mode} must resolve directly from one final theme placement.");
            }
        }
        Ensure(slotCount == 72, $"The defaults matrix must contain 72 exact slots; found {slotCount}.");

        var heroMotions = documents.Values
            .SelectMany(document => new[]
            {
                (ThemeId: document.ThemeId, Mode: "light", Motion: document.Slots.Hero.Light.Motion),
                (ThemeId: document.ThemeId, Mode: "dark", Motion: document.Slots.Hero.Dark.Motion),
            })
            .ToArray();
        var loopMotions = heroMotions.Where(item => item.Motion?.Normalize().Mode == "loop").ToArray();
        var stillMotions = heroMotions.Where(item => item.Motion?.Normalize().Mode == "none").ToArray();
        Ensure(loopMotions.Length == 20 &&
               stillMotions.Length == 4 &&
               stillMotions.All(item =>
                   item.ThemeId is "qingxiao.cloudsword-gate" or "suisui.inkscape-dawn"),
            $"Published hero motion must remain 20 relative loops plus four evidenced still slots; found {loopMotions.Length}/{stillMotions.Length}.");
        Ensure(loopMotions.All(item => item.Motion is
        { Keyframes.Count: >= 2 } motion &&
                   motion.Keyframes[0].At == 0d &&
                   motion.Keyframes[^1].At == 100d) &&
               documents.Values.All(document =>
                   document.Slots.Sidebar.Light.Motion is null &&
                   document.Slots.Sidebar.Dark.Motion is null &&
                   document.Slots.Chat.Light.Motion is null &&
                   document.Slots.Chat.Dark.Motion is null),
            "Artwork motion must be bounded relative deltas and must never become a hidden second crop on other regions.");
        var danya = documents["danya.bubble-void-duality"];
        Ensure(danya.Slots.Hero.Light.Motion?.DurationMs == 17000d &&
               danya.Slots.Hero.Dark.Motion?.DurationMs == 12000d,
            "Danya must retain the authored light/dark motion rhythm instead of collapsing both modes to one duration.");

        var runtimeSource = await ReadCompatibilityRuntimeSourceAsync(repositoryRoot);
        Ensure(runtimeSource.Contains("buildMotionKeyframes(state, .35", StringComparison.Ordinal) &&
               runtimeSource.Contains("animation-duration:${duration}ms", StringComparison.Ordinal) &&
               runtimeSource.Contains("data-tessalume-motion=\"off\"", StringComparison.Ordinal) &&
               runtimeSource.Contains("prefers-reduced-motion:reduce", StringComparison.Ordinal) &&
               runtimeSource.Contains("artworkCompositionProtocolVersion: 1", StringComparison.Ordinal),
            "The shared runtime must keep full rhythm, reduce only motion amplitude to 35%, and fully disable motion on request or OS preference.");

        AssertSixSlotPublishedBaseline(
            documents["xin.moonfox-sovereign"],
            heroY: "center",
            heroScale: 1d,
            heroOriginX: "73%",
            sidebarLight: ("214%", "86%", "-78px"),
            sidebarDark: ("225%", "92%", "-150px"),
            "flagship");
        AssertSixSlotPublishedBaseline(
            documents["mornye.first-star-observatory"],
            heroY: "46%",
            heroScale: 1.008d,
            heroOriginX: "73%",
            sidebarLight: ("206%", "58%", "-28px"),
            sidebarDark: ("220%", "66%", "-20px"),
            "Mornye");
        AssertSixSlotPublishedBaseline(
            documents["cartethyia.gale-tide-crown"],
            heroY: "center",
            heroScale: 1.004d,
            heroOriginX: "50%",
            sidebarLight: ("312%", "40%", "-148px"),
            sidebarDark: ("355%", "52%", "-200px"),
            "Cartethyia");

        var cartethyiaRoot = Path.Combine(
            repositoryRoot,
            "themes",
            "cartethyia.gale-tide-crown");
        var cartethyiaManifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(cartethyiaRoot, ThemePackageLoader.ManifestFileName)));
        var sidebarAsset = cartethyiaManifest.RootElement
            .GetProperty("assets")
            .GetProperty("sidebar-dark")
            .GetString()!;
        var dimensions = ReadPngDimensions(Path.Combine(cartethyiaRoot, sidebarAsset));
        Ensure(dimensions == (1024, 1536),
            $"The primary Cartethyia fixture must remain 1024×1536; found {dimensions.Width}×{dimensions.Height}.");
        var placementSpec = ThemeArtworkPlacementParser.Parse(
            documents["cartethyia.gale-tide-crown"].Slots.Sidebar.Dark.Placement);
        var projection = ArtworkPlacementMapper.Project(
            placementSpec,
            new Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain.ArtworkSize(
                dimensions.Width,
                dimensions.Height),
            new Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain.ArtworkSize(260, 800));
        EnsureAlmostEqual(projection.RenderedImage.Width, 923d, "Cartethyia exact rendered width");
        EnsureAlmostEqual(projection.RenderedImage.Height, 1384.5d, "Cartethyia exact rendered height");
        EnsureAlmostEqual(projection.RenderedImage.X, -344.76d, "Cartethyia exact rendered X");
        EnsureAlmostEqual(projection.RenderedImage.Y, -200d, "Cartethyia exact rendered Y");
    }

    private static IEnumerable<(
        string Region,
        string Mode,
        string Asset,
        ThemeArtworkDefaultSlot Slot,
        ThemeArtworkSlotResolution Resolution)> EnumerateArtworkSlots(
        ThemeArtworkDefaultsDocument defaults,
        ThemeVisualSettingsResolution resolution)
    {
        yield return ("hero", "light", "hero-light", defaults.Slots.Hero.Light, resolution.Light.Hero);
        yield return ("hero", "dark", "hero-dark", defaults.Slots.Hero.Dark, resolution.Dark.Hero);
        yield return ("sidebar", "light", "sidebar-light", defaults.Slots.Sidebar.Light, resolution.Light.Sidebar);
        yield return ("sidebar", "dark", "sidebar-dark", defaults.Slots.Sidebar.Dark, resolution.Dark.Sidebar);
        yield return ("chat", "light", "chat-light", defaults.Slots.Chat.Light, resolution.Light.Chat);
        yield return ("chat", "dark", "chat-dark", defaults.Slots.Chat.Dark, resolution.Dark.Chat);
    }

    private static void AssertSixSlotPublishedBaseline(
        ThemeArtworkDefaultsDocument document,
        string heroY,
        double heroScale,
        string heroOriginX,
        (string Width, string X, string Y) sidebarLight,
        (string Width, string X, string Y) sidebarDark,
        string scenario)
    {
        foreach (var (mode, hero, chat) in new[]
                 {
                     ("light", document.Slots.Hero.Light, document.Slots.Chat.Light),
                     ("dark", document.Slots.Hero.Dark, document.Slots.Chat.Dark),
                 })
        {
            Ensure(hero.Placement.Size.Width == "cover" &&
                   hero.Placement.Size.Height == "auto" &&
                   hero.Placement.Position.X == "center" &&
                   hero.Placement.Position.Y == heroY &&
                   Math.Abs(hero.Placement.Scale - heroScale) < .000001d &&
                   hero.Placement.Origin.X == heroOriginX &&
                   chat.Placement.Size.Width == "cover" &&
                   chat.Placement.Size.Height == "auto" &&
                   chat.Placement.Position.X == "center" &&
                   chat.Placement.Position.Y == "center",
                $"The {scenario} {mode} hero/chat published placement was not extracted exactly.");
        }
        AssertSidebar(document.Slots.Sidebar.Light, sidebarLight, $"{scenario} light sidebar");
        AssertSidebar(document.Slots.Sidebar.Dark, sidebarDark, $"{scenario} dark sidebar");

        static void AssertSidebar(
            ThemeArtworkDefaultSlot slot,
            (string Width, string X, string Y) expected,
            string name) => Ensure(
            slot.Placement.Size.Width == expected.Width &&
            slot.Placement.Size.Height == "auto" &&
            slot.Placement.Position.X == expected.X &&
            slot.Placement.Position.Y == expected.Y &&
            slot.Placement.Scale == 1d,
            $"The {name} published placement was not extracted exactly.");
    }

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using var stream = File.OpenRead(path);
        stream.ReadExactly(header);
        Ensure(header[..8].SequenceEqual(new byte[]
            { 137, 80, 78, 71, 13, 10, 26, 10 }),
            $"The artwork fixture is not a PNG: {path}.");
        return (
            BinaryPrimitives.ReadInt32BigEndian(header[16..20]),
            BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
    }
}
