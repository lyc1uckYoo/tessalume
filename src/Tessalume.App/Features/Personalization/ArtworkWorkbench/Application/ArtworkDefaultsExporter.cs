using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;

internal static class ArtworkDefaultsExporter
{
    public static ThemeArtworkDefaultsDocument Create(
        string themeId,
        string defaultsVersion,
        ThemeVisualSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultsVersion);
        ArgumentNullException.ThrowIfNull(settings);
        if (!Version.TryParse(defaultsVersion, out _))
        {
            throw new ArgumentException(
                "Artwork defaults version must be a dotted numeric version.",
                nameof(defaultsVersion));
        }
        var normalized = settings.Normalize();
        if (EnumerateSlots(normalized).Any(slot =>
                slot.CompositionMode == ThemeArtworkCompositionMode.Legacy))
        {
            throw new InvalidOperationException(
                "旧版叠加构图不能直接导出为主题推荐值；请先在六个槽位中完成一次自定义取景转换。");
        }
        return new ThemeArtworkDefaultsDocument
        {
            Schema = "../../schemas/theme-artwork-defaults-v1.schema.json",
            ThemeId = themeId.Trim(),
            DefaultsVersion = defaultsVersion.Trim(),
            Slots = new ThemeArtworkDefaultSlots
            {
                Hero = CreateModes(normalized.Light.Hero, normalized.Dark.Hero, "hero"),
                Sidebar = CreateModes(
                    normalized.Light.Sidebar,
                    normalized.Dark.Sidebar,
                    "sidebar"),
                Chat = CreateModes(normalized.Light.Chat, normalized.Dark.Chat, "chat"),
            },
        }.Normalize();
    }

    private static IEnumerable<ThemeArtworkAdjustment> EnumerateSlots(
        ThemeVisualSettings settings)
    {
        yield return settings.Light.Hero;
        yield return settings.Light.Sidebar;
        yield return settings.Light.Chat;
        yield return settings.Dark.Hero;
        yield return settings.Dark.Sidebar;
        yield return settings.Dark.Chat;
    }

    private static ThemeArtworkDefaultSlotModes CreateModes(
        ThemeArtworkAdjustment light,
        ThemeArtworkAdjustment dark,
        string region) => new()
        {
            Light = CreateSlot(light, $"{region}-light"),
            Dark = CreateSlot(dark, $"{region}-dark"),
        };

    private static ThemeArtworkDefaultSlot CreateSlot(
        ThemeArtworkAdjustment source,
        string fallbackAsset)
    {
        var value = source.Normalize();
        var gradientVeil = ExportGradientVeil(value);
        var readabilityVeil = ExportReadabilityVeil(value);
        return new ThemeArtworkDefaultSlot
        {
            Asset = value.ThemeAssetKey ?? fallbackAsset,
            Placement = ToCssPlacement(value.Placement ?? new ThemeArtworkPlacementSpec()),
            Effects = new ThemeArtworkDefaultEffects
            {
                Brightness = value.Brightness,
                Contrast = value.Contrast,
                Saturation = value.Saturation,
                Opacity = value.Opacity,
                Grayscale = value.Grayscale,
                HueRotate = value.HueRotation,
                Blur = value.Blur,
                BlendMode = value.BlendMode,
                Overlay = new ThemeArtworkDefaultOverlay
                {
                    Color = value.OverlayColor,
                    Opacity = value.OverlayOpacity,
                },
                GradientVeil = gradientVeil,
                Vignette = value.Vignette,
                ReadabilityVeil = readabilityVeil,
            },
            ResponsiveVariants = value.ResponsiveVariants,
            Motion = value.Motion,
        }.Normalize();
    }

    private static ThemeArtworkGradientVeil ExportGradientVeil(
        ThemeArtworkAdjustment value)
    {
        if (value.GradientVeil.Enabled || value.GradientStrength <= 0d)
        {
            return value.GradientVeil;
        }

        // Schema-five stored one left-to-right gradient scalar. Author exports
        // must materialize it into the versioned contract instead of silently
        // dropping a visible compatibility setting.
        return new ThemeArtworkGradientVeil
        {
            Enabled = true,
            Strength = value.GradientStrength,
            Layers =
            [
                new ThemeArtworkGradientLayer
                {
                    DirectionDeg = 90d,
                    Start = 0d,
                    End = 72d,
                    Stops =
                    [
                        new ThemeArtworkGradientStop
                        {
                            Position = 0d,
                            Color = value.OverlayColor,
                            Opacity = 82d,
                        },
                        new ThemeArtworkGradientStop
                        {
                            Position = 72d,
                            Color = value.OverlayColor,
                            Opacity = 0d,
                        },
                    ],
                },
            ],
        }.Normalize();
    }

    private static ThemeArtworkReadabilityVeil ExportReadabilityVeil(
        ThemeArtworkAdjustment value)
    {
        if (value.ReadabilityVeil.Enabled || !value.ReadabilityProtection)
        {
            return value.ReadabilityVeil;
        }

        return value.ReadabilityVeil with
        {
            Enabled = true,
            Color = "#000000",
            Opacity = 42d,
            DirectionDeg = 90d,
            RangeStart = 0d,
            RangeEnd = 100d,
        };
    }

    private static ThemeArtworkCssPlacement ToCssPlacement(ThemeArtworkPlacementSpec source)
    {
        var value = source.Normalize();
        var size = value.SizeMode switch
        {
            ThemeArtworkSizeMode.Contain => new ThemeArtworkCssSize
            {
                Width = "contain",
                Height = "auto",
            },
            ThemeArtworkSizeMode.Explicit => new ThemeArtworkCssSize
            {
                Width = value.Width.ToCss(),
                Height = value.Height.ToCss(),
            },
            _ => new ThemeArtworkCssSize { Width = "cover", Height = "auto" },
        };
        return new ThemeArtworkCssPlacement
        {
            Size = size,
            Position = new ThemeArtworkCssPosition
            {
                X = value.PositionX.ToCss(horizontal: true),
                Y = value.PositionY.ToCss(horizontal: false),
            },
            Scale = value.Geometry.Scale,
            Origin = new ThemeArtworkCssPosition
            {
                X = value.Geometry.OriginX.ToCss(horizontal: true),
                Y = value.Geometry.OriginY.ToCss(horizontal: false),
            },
            MirrorX = value.Geometry.MirrorX,
            MirrorY = value.Geometry.MirrorY,
        }.Normalize();
    }
}
