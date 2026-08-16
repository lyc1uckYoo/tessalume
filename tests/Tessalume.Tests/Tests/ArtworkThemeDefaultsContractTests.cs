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
            Ensure(defaults.Defaults.DefaultsVersion == "1.1.0",
                $"{package.Manifest.Id} must publish the chat-mask defaults contract as version 1.1.0.");
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
                if (region == "hero")
                {
                    Ensure(slot.Effects.Overlay.Opacity == 0d &&
                           slot.Effects.GradientVeil is { Enabled: false, Layers.Count: 0 } &&
                           slot.Effects.ReadabilityVeil is { Enabled: false, Opacity: 0d },
                        $"{package.Manifest.Id} {mode} homepage artwork must not publish a readability mask.");
                }
                else if (region == "chat")
                {
                    Ensure(slot.Effects.GradientVeil is { Layers.Count: > 0 } veil &&
                           (veil.Enabled ? veil.Strength > 0d : veil.Strength == 0d) &&
                           slot.Effects.ReadabilityVeil is { Enabled: false, Opacity: 0d },
                        $"{package.Manifest.Id} {mode} chat artwork must publish exactly one adjustable mask system.");
                }
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
        var templateCss = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Compatibility",
            "theme-template-v1.css"));
        Ensure(runtimeSource.Contains("const maskLayers = [];", StringComparison.Ordinal) &&
               runtimeSource.Contains("state.maskVariable", StringComparison.Ordinal) &&
               templateCss.Contains("--tessalume-visual-chat-light-mask-image", StringComparison.Ordinal) &&
               templateCss.Contains("--tessalume-visual-chat-dark-mask-image", StringComparison.Ordinal),
            "Chat masks must remain an independent runtime layer instead of being baked into the artwork image.");

        AssertSixSlotPublishedBaseline(
            documents["xin.moonfox-sovereign"],
            heroLight: ("30.54872742%", 1d, "center", "30.54872742%"),
            heroDark: ("center", 1d, "73%", "50%"),
            sidebarLight: ("231.12%", "86.03183277%", "-88px"),
            sidebarDark: ("243%", "91.47461234%", "-175.17918318px"),
            chatLight: ("23.09063744%", "100%", 1.1664d, "23.09063744%", "100%"),
            chatDark: ("41.13820796%", "89.79151689%", 1.1664d, "41.13820796%", "89.79151689%"),
            "flagship");
        AssertSixSlotPublishedBaseline(
            documents["mornye.first-star-observatory"],
            heroLight: ("46.71728261%", 1d, "center", "46.71728261%"),
            heroDark: ("46.94176244%", 1d, "center", "46.94176244%"),
            sidebarLight: ("181.45454545%", "57.016783%", "0px"),
            sidebarDark: ("219.82803594%", "69.60847826%", "0px"),
            chatLight: ("center", "center", 1d, "50%", "50%"),
            chatDark: ("center", "center", 1d, "50%", "50%"),
            "Mornye");
        AssertAemeathPublishedBaseline(documents["aemeath.star-voyage"]);
        AssertCartethyiaPublishedBaseline(documents["cartethyia.gale-tide-crown"]);

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
        EnsureAlmostEqual(projection.RenderedImage.Width, 733.019296909046d, "Cartethyia exact rendered width");
        EnsureAlmostEqual(projection.RenderedImage.Height, 1099.52894536357d, "Cartethyia exact rendered height");
        EnsureAlmostEqual(projection.RenderedImage.X, -248.227753128852d, "Cartethyia exact rendered X");
        EnsureAlmostEqual(projection.RenderedImage.Y, -121.6463036525212d, "Cartethyia exact rendered Y");
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
        (string Y, double Scale, string OriginX, string OriginY) heroLight,
        (string Y, double Scale, string OriginX, string OriginY) heroDark,
        (string Width, string X, string Y) sidebarLight,
        (string Width, string X, string Y) sidebarDark,
        (string X, string Y, double Scale, string OriginX, string OriginY) chatLight,
        (string X, string Y, double Scale, string OriginX, string OriginY) chatDark,
        string scenario)
    {
        foreach (var (mode, hero, expected) in new[]
                 {
                     ("light", document.Slots.Hero.Light, heroLight),
                     ("dark", document.Slots.Hero.Dark, heroDark),
                 })
        {
            Ensure(hero.Placement.Size.Width == "cover" &&
                   hero.Placement.Size.Height == "auto" &&
                   hero.Placement.Position.X == "center" &&
                   hero.Placement.Position.Y == expected.Y &&
                   Math.Abs(hero.Placement.Scale - expected.Scale) < .000001d &&
                   hero.Placement.Origin.X == expected.OriginX &&
                   hero.Placement.Origin.Y == expected.OriginY,
                $"The {scenario} {mode} hero placement was not extracted exactly.");
        }
        foreach (var (mode, chat, expected) in new[]
                 {
                     ("light", document.Slots.Chat.Light, chatLight),
                     ("dark", document.Slots.Chat.Dark, chatDark),
                 })
        {
            Ensure(chat.Placement.Size.Width == "cover" &&
                   chat.Placement.Size.Height == "auto" &&
                   chat.Placement.Position.X == expected.X &&
                   chat.Placement.Position.Y == expected.Y &&
                   Math.Abs(chat.Placement.Scale - expected.Scale) < .000001d &&
                   chat.Placement.Origin.X == expected.OriginX &&
                   chat.Placement.Origin.Y == expected.OriginY,
                $"The {scenario} {mode} chat placement was not extracted exactly.");
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

    private static void AssertCartethyiaPublishedBaseline(
        ThemeArtworkDefaultsDocument document)
    {
        AssertPlacement(
            document.Slots.Hero.Light,
            "100%",
            "124.653664453524%",
            "center",
            "23.130475435973782%",
            "Cartethyia light hero");
        AssertPlacement(
            document.Slots.Hero.Dark,
            "117.10656000000002%",
            "145.97761835546478%",
            "96.87224806901243%",
            "32.97143678580001%",
            "Cartethyia dark hero");
        AssertPlacement(
            document.Slots.Sidebar.Light,
            "261.29454545%",
            "auto",
            "41.87172612%",
            "-79.84px",
            "Cartethyia light sidebar");
        AssertPlacement(
            document.Slots.Sidebar.Dark,
            "281.93049881117156%",
            "auto",
            "52.47729949938649%",
            "-121.6463036525212px",
            "Cartethyia dark sidebar");
        AssertPlacement(
            document.Slots.Chat.Dark,
            "107.66724286000954%",
            "100%",
            "83.96022976720552%",
            "center",
            "Cartethyia dark chat");
        Ensure(document.Slots.Chat.Light.Placement.Size.Width == "cover" &&
               document.Slots.Chat.Light.Placement.Size.Height == "auto" &&
               document.Slots.Chat.Light.Placement.Position.X == "center" &&
               document.Slots.Chat.Light.Placement.Position.Y == "center",
            "Cartethyia light chat must retain its unmodified recommended framing.");

        static void AssertPlacement(
            ThemeArtworkDefaultSlot slot,
            string width,
            string height,
            string x,
            string y,
            string scenario) => Ensure(
            slot.Placement.Size.Width == width &&
            slot.Placement.Size.Height == height &&
            slot.Placement.Position.X == x &&
            slot.Placement.Position.Y == y &&
            slot.Placement.Scale == 1d,
            $"The promoted {scenario} placement was not preserved exactly.");
    }

    private static void AssertAemeathPublishedBaseline(
        ThemeArtworkDefaultsDocument document)
    {
        foreach (var (slot, y, scenario) in new[]
                 {
                     (document.Slots.Hero.Light, "18.116048293327847%", "light hero"),
                     (document.Slots.Hero.Dark, "22.06296980388873%", "dark hero"),
                 })
        {
            Ensure(slot.Placement.Size.Width == "cover" &&
                   slot.Placement.Size.Height == "auto" &&
                   slot.Placement.Position.X == "center" &&
                   slot.Placement.Position.Y == y &&
                   slot.Placement.Origin.X == "center" &&
                   slot.Placement.Origin.Y == y,
                $"Aemeath's promoted {scenario} placement must remain the theme default.");
        }

        foreach (var (slot, scenario) in new[]
                 {
                     (document.Slots.Chat.Light, "light chat"),
                     (document.Slots.Chat.Dark, "dark chat"),
                 })
        {
            Ensure(slot.Placement.Size.Width == "cover" &&
                   slot.Placement.Size.Height == "auto" &&
                   slot.Placement.Position.X == "50%" &&
                   slot.Placement.Position.Y == "center" &&
                   slot.Placement.Origin.X == "50%" &&
                   slot.Placement.Origin.Y == "center" &&
                   slot.Effects.GradientVeil is { Enabled: false, Strength: 0d, Layers.Count: 1 },
                $"Aemeath's promoted {scenario} placement and mask must remain the theme default.");
        }
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
