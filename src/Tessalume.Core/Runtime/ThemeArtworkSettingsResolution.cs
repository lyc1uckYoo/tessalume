using System.Text.Json.Serialization;

namespace Tessalume.Core.Runtime;

public sealed record ThemeArtworkDefaultsDocument
{
    [JsonPropertyName("$schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Schema { get; init; }

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("themeId")]
    public string ThemeId { get; init; } = string.Empty;

    [JsonPropertyName("defaultsVersion")]
    public string DefaultsVersion { get; init; } = "1.0.0";

    [JsonPropertyName("slots")]
    public ThemeArtworkDefaultSlots Slots { get; init; } = new();

    public ThemeArtworkDefaultsDocument Normalize() => this with
    {
        ThemeId = NormalizeText(ThemeId, 256),
        DefaultsVersion = NormalizeText(DefaultsVersion, 64),
        Slots = (Slots ?? new ThemeArtworkDefaultSlots()).Normalize(),
    };

    private static string NormalizeText(string? value, int maximumLength)
    {
        var candidate = (value ?? string.Empty).Trim();
        return candidate.Length <= maximumLength ? candidate : candidate[..maximumLength];
    }
}

public sealed record ThemeArtworkDefaultSlots
{
    [JsonPropertyName("hero")]
    public ThemeArtworkDefaultSlotModes Hero { get; init; } = new();

    [JsonPropertyName("sidebar")]
    public ThemeArtworkDefaultSlotModes Sidebar { get; init; } = new();

    [JsonPropertyName("chat")]
    public ThemeArtworkDefaultSlotModes Chat { get; init; } = new();

    public ThemeArtworkDefaultSlots Normalize() => this with
    {
        Hero = (Hero ?? new ThemeArtworkDefaultSlotModes()).Normalize(),
        Sidebar = (Sidebar ?? new ThemeArtworkDefaultSlotModes()).Normalize(),
        Chat = (Chat ?? new ThemeArtworkDefaultSlotModes()).Normalize(),
    };
}

public sealed record ThemeArtworkDefaultSlotModes
{
    [JsonPropertyName("light")]
    public ThemeArtworkDefaultSlot Light { get; init; } = new();

    [JsonPropertyName("dark")]
    public ThemeArtworkDefaultSlot Dark { get; init; } = new();

    public ThemeArtworkDefaultSlotModes Normalize() => this with
    {
        Light = (Light ?? new ThemeArtworkDefaultSlot()).Normalize(),
        Dark = (Dark ?? new ThemeArtworkDefaultSlot()).Normalize(),
    };
}

public sealed record ThemeArtworkDefaultSlot
{
    [JsonPropertyName("asset")]
    public string Asset { get; init; } = string.Empty;

    [JsonPropertyName("placement")]
    public ThemeArtworkCssPlacement Placement { get; init; } = new();

    [JsonPropertyName("effects")]
    public ThemeArtworkDefaultEffects Effects { get; init; } = new();

    [JsonPropertyName("responsiveVariants")]
    public IReadOnlyList<ThemeArtworkResponsiveVariant> ResponsiveVariants { get; init; } = [];

    [JsonPropertyName("motion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeArtworkMotion? Motion { get; init; }

    public ThemeArtworkDefaultSlot Normalize() => this with
    {
        Asset = (Asset ?? string.Empty).Trim(),
        Placement = (Placement ?? new ThemeArtworkCssPlacement()).Normalize(),
        Effects = (Effects ?? new ThemeArtworkDefaultEffects()).Normalize(),
        ResponsiveVariants = (ResponsiveVariants ?? [])
            .Where(variant => variant is not null)
            .Take(16)
            .Select(variant => variant.Normalize())
            .ToArray(),
        Motion = Motion?.Normalize(),
    };
}

public sealed record ThemeArtworkDefaultEffects
{
    [JsonPropertyName("brightness")]
    public double Brightness { get; init; } = 100d;

    [JsonPropertyName("contrast")]
    public double Contrast { get; init; } = 100d;

    [JsonPropertyName("saturation")]
    public double Saturation { get; init; } = 100d;

    [JsonPropertyName("opacity")]
    public double Opacity { get; init; } = 100d;

    [JsonPropertyName("grayscale")]
    public double Grayscale { get; init; }

    [JsonPropertyName("hueRotate")]
    public double HueRotate { get; init; }

    [JsonPropertyName("blur")]
    public double Blur { get; init; }

    [JsonPropertyName("blendMode")]
    public string BlendMode { get; init; } = "normal";

    [JsonPropertyName("overlay")]
    public ThemeArtworkDefaultOverlay Overlay { get; init; } = new();

    [JsonPropertyName("gradientVeil")]
    public ThemeArtworkGradientVeil GradientVeil { get; init; } = new();

    [JsonPropertyName("vignette")]
    public double Vignette { get; init; }

    [JsonPropertyName("readabilityVeil")]
    public ThemeArtworkReadabilityVeil ReadabilityVeil { get; init; } = new();

    public ThemeArtworkDefaultEffects Normalize()
    {
        var normalized = new ThemeArtworkAdjustment
        {
            Brightness = Brightness,
            Contrast = Contrast,
            Saturation = Saturation,
            Opacity = Opacity,
            Grayscale = Grayscale,
            HueRotation = HueRotate,
            Blur = Blur,
            BlendMode = BlendMode,
            OverlayColor = Overlay?.Color ?? "#000000",
            OverlayOpacity = Overlay?.Opacity ?? 0d,
            GradientVeil = GradientVeil,
            Vignette = Vignette,
            ReadabilityVeil = ReadabilityVeil,
        }.Normalize();
        return this with
        {
            Brightness = normalized.Brightness,
            Contrast = normalized.Contrast,
            Saturation = normalized.Saturation,
            Opacity = normalized.Opacity,
            Grayscale = normalized.Grayscale,
            HueRotate = normalized.HueRotation,
            Blur = normalized.Blur,
            BlendMode = normalized.BlendMode,
            Overlay = new ThemeArtworkDefaultOverlay
            {
                Color = normalized.OverlayColor,
                Opacity = normalized.OverlayOpacity,
            },
            GradientVeil = normalized.GradientVeil,
            Vignette = normalized.Vignette,
            ReadabilityVeil = normalized.ReadabilityVeil,
        };
    }
}

public sealed record ThemeArtworkDefaultOverlay
{
    [JsonPropertyName("color")]
    public string Color { get; init; } = "#000000";

    [JsonPropertyName("opacity")]
    public double Opacity { get; init; }
}

/// <summary>A sparse, user-owned delta. Null means inherit the current theme default.</summary>
public sealed record ThemeArtworkOverride
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeArtworkImageSourceMode? ImageSourceMode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CustomImagePath { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeArtworkCompositionMode? CompositionMode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeArtworkPlacementSpec? Placement { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LegacyZoom { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LegacyOffsetX { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LegacyOffsetY { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LegacyGradientStrength { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyReadabilityProtection { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? MotionEnabled { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Brightness { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Contrast { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Saturation { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Opacity { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Grayscale { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? HueRotation { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Blur { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OverlayColor { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? OverlayOpacity { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeArtworkGradientVeil? GradientVeil { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Vignette { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BlendMode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeArtworkReadabilityVeil? ReadabilityVeil { get; init; }

    public ThemeArtworkOverride Normalize()
    {
        var path = (CustomImagePath ?? string.Empty).Trim();
        if (path.Length > 512) path = path[..512];
        return this with
        {
            CustomImagePath = ImageSourceMode == ThemeArtworkImageSourceMode.Custom && path.Length > 0
                ? path
                : null,
            Placement = Placement?.Normalize(),
            LegacyZoom = NormalizeOptional(LegacyZoom, 70d, 200d),
            LegacyOffsetX = NormalizeOptional(LegacyOffsetX, -200d, 200d),
            LegacyOffsetY = NormalizeOptional(LegacyOffsetY, -200d, 200d),
            LegacyGradientStrength = NormalizeOptional(LegacyGradientStrength, 0d, 100d),
            Brightness = NormalizeOptional(Brightness, 20d, 180d),
            Contrast = NormalizeOptional(Contrast, 20d, 180d),
            Saturation = NormalizeOptional(Saturation, 0d, 200d),
            Opacity = NormalizeOptional(Opacity, 0d, 100d),
            Grayscale = NormalizeOptional(Grayscale, 0d, 100d),
            HueRotation = NormalizeOptional(HueRotation, -180d, 180d),
            Blur = NormalizeOptional(Blur, 0d, 20d),
            OverlayColor = OverlayColor is null
                ? null
                : ThemeArtworkValueNormalization.NormalizeColor(OverlayColor),
            OverlayOpacity = NormalizeOptional(OverlayOpacity, 0d, 100d),
            GradientVeil = GradientVeil?.Normalize(),
            Vignette = NormalizeOptional(Vignette, 0d, 100d),
            BlendMode = BlendMode is null
                ? null
                : new ThemeArtworkAdjustment { BlendMode = BlendMode }.Normalize().BlendMode,
            ReadabilityVeil = ReadabilityVeil?.Normalize(),
        };
    }

    public bool IsEmpty =>
        ImageSourceMode is null &&
        CustomImagePath is null &&
        CompositionMode is null &&
        Placement is null &&
        LegacyZoom is null &&
        LegacyOffsetX is null &&
        LegacyOffsetY is null &&
        LegacyGradientStrength is null &&
        LegacyReadabilityProtection is null &&
        MotionEnabled is null &&
        Brightness is null &&
        Contrast is null &&
        Saturation is null &&
        Opacity is null &&
        Grayscale is null &&
        HueRotation is null &&
        Blur is null &&
        OverlayColor is null &&
        OverlayOpacity is null &&
        GradientVeil is null &&
        Vignette is null &&
        BlendMode is null &&
        ReadabilityVeil is null;

    private static double? NormalizeOptional(double? value, double minimum, double maximum) =>
        value is { } number && double.IsFinite(number)
            ? Math.Clamp(number, minimum, maximum)
            : null;
}

public sealed record ThemeVisualModeSettingsOverride
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeArtworkOverride? Hero { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeArtworkOverride? Sidebar { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeArtworkOverride? Chat { get; init; }

    public ThemeVisualModeSettingsOverride Normalize() => this with
    {
        Hero = NormalizeSlot(Hero),
        Sidebar = NormalizeSlot(Sidebar),
        Chat = NormalizeSlot(Chat),
    };

    private static ThemeArtworkOverride? NormalizeSlot(ThemeArtworkOverride? value)
    {
        var normalized = value?.Normalize();
        return normalized is null || normalized.IsEmpty ? null : normalized;
    }
}

public sealed record ThemeDisplayPreferencesOverride
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MotionIntensity { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TextScale { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Density { get; init; }

    public ThemeDisplayPreferencesOverride Normalize() => this with
    {
        MotionIntensity = NormalizeOption(MotionIntensity, ["full", "reduced", "off"]),
        TextScale = NormalizeOption(TextScale, ["small", "standard", "large"]),
        Density = NormalizeOption(Density, ["compact", "comfortable", "spacious"]),
    };

    public bool IsEmpty => MotionIntensity is null && TextScale is null && Density is null;

    private static string? NormalizeOption(string? value, IReadOnlyList<string> supported)
    {
        if (value is null) return null;
        var candidate = value.Trim().ToLowerInvariant();
        return supported.Contains(candidate, StringComparer.Ordinal) ? candidate : null;
    }
}

public sealed record ThemeVisualSettingsOverride
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeVisualModeSettingsOverride? Light { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeVisualModeSettingsOverride? Dark { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeDisplayPreferencesOverride? Display { get; init; }

    public ThemeVisualSettingsOverride Normalize()
    {
        var light = Light?.Normalize();
        var dark = Dark?.Normalize();
        var display = Display?.Normalize();
        return this with
        {
            Light = IsModeEmpty(light) ? null : light,
            Dark = IsModeEmpty(dark) ? null : dark,
            Display = display is null || display.IsEmpty ? null : display,
        };
    }

    public bool IsEmpty => Light is null && Dark is null && Display is null;

    private static bool IsModeEmpty(ThemeVisualModeSettingsOverride? value) =>
        value is null ||
        (value.Hero is null && value.Sidebar is null && value.Chat is null);
}

public enum ThemeArtworkValueSource
{
    OriginalAsset,
    ThemeDefault,
    UserOverride,
    LegacyMigration,
}

public sealed record ThemeArtworkSlotResolution(
    string AssetKey,
    ThemeArtworkAdjustment ThemeDefaultAdjustment,
    ThemeArtworkAdjustment Adjustment,
    ThemeArtworkOverride? UserOverride,
    IReadOnlyDictionary<string, ThemeArtworkValueSource> Provenance);

public sealed record ThemeVisualModeResolution(
    ThemeArtworkSlotResolution Hero,
    ThemeArtworkSlotResolution Sidebar,
    ThemeArtworkSlotResolution Chat);

public sealed record ThemeVisualSettingsResolution(
    string ThemeId,
    string DefaultsVersion,
    ThemeVisualSettings Settings,
    ThemeVisualSettingsOverride UserOverrides,
    ThemeVisualModeResolution Light,
    ThemeVisualModeResolution Dark,
    bool DefaultsAreExact = true,
    string? DefaultsDiagnostic = null);

public static class ThemeArtworkSettingsResolver
{
    public static ThemeVisualSettingsResolution Resolve(
        ThemeArtworkDefaultsDocument? defaults,
        ThemeVisualSettingsOverride? userOverrides)
    {
        var normalizedDefaults = (defaults ?? new ThemeArtworkDefaultsDocument()).Normalize();
        var overrides = (userOverrides ?? new ThemeVisualSettingsOverride()).Normalize();
        var light = ResolveMode(normalizedDefaults.Slots, overrides.Light, dark: false);
        var dark = ResolveMode(normalizedDefaults.Slots, overrides.Dark, dark: true);
        var display = ResolveDisplay(overrides.Display);
        return new ThemeVisualSettingsResolution(
            normalizedDefaults.ThemeId,
            normalizedDefaults.DefaultsVersion,
            new ThemeVisualSettings
            {
                Light = new ThemeVisualModeSettings
                {
                    Hero = light.Hero.Adjustment,
                    Sidebar = light.Sidebar.Adjustment,
                    Chat = light.Chat.Adjustment,
                },
                Dark = new ThemeVisualModeSettings
                {
                    Hero = dark.Hero.Adjustment,
                    Sidebar = dark.Sidebar.Adjustment,
                    Chat = dark.Chat.Adjustment,
                },
                Display = display,
            }.Normalize(),
            overrides,
            light,
            dark);
    }

    public static ThemeVisualSettingsOverride CreateSparseOverride(
        ThemeArtworkDefaultsDocument? defaults,
        ThemeVisualSettings resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        var baseline = Resolve(defaults, null).Settings;
        var normalized = resolved.Normalize();
        return new ThemeVisualSettingsOverride
        {
            Light = CreateModeOverride(baseline.Light, normalized.Light),
            Dark = CreateModeOverride(baseline.Dark, normalized.Dark),
            Display = CreateDisplayOverride(baseline.Display, normalized.Display),
        }.Normalize();
    }

    public static ThemeVisualSettingsOverride MigrateSchemaFive(ThemeVisualSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // Do not normalize first: schema-five has no CompositionMode, and migration
        // must decide legacy intent from the original Zoom/Offset values.
        return new ThemeVisualSettingsOverride
        {
            Light = MigrateMode(settings.Light),
            Dark = MigrateMode(settings.Dark),
            Display = CreateDisplayOverride(new ThemeDisplayPreferences(), settings.Display),
        }.Normalize();
    }

    private static ThemeVisualModeResolution ResolveMode(
        ThemeArtworkDefaultSlots defaults,
        ThemeVisualModeSettingsOverride? overrides,
        bool dark) => new(
        ResolveSlot(dark ? defaults.Hero.Dark : defaults.Hero.Light, overrides?.Hero),
        ResolveSlot(dark ? defaults.Sidebar.Dark : defaults.Sidebar.Light, overrides?.Sidebar),
        ResolveSlot(dark ? defaults.Chat.Dark : defaults.Chat.Light, overrides?.Chat));

    private static ThemeArtworkSlotResolution ResolveSlot(
        ThemeArtworkDefaultSlot defaults,
        ThemeArtworkOverride? userOverride)
    {
        var normalizedDefaults = (defaults ?? new ThemeArtworkDefaultSlot()).Normalize();
        var baseline = CreateDefaultAdjustment(normalizedDefaults);
        var delta = userOverride?.Normalize();
        if (delta is null)
        {
            return CreateResolution(normalizedDefaults.Asset, baseline, baseline, null);
        }

        var mode = delta.CompositionMode ?? ThemeArtworkCompositionMode.Theme;
        var placement = mode == ThemeArtworkCompositionMode.Custom
            ? delta.Placement ?? baseline.Placement
            : baseline.Placement;
        var adjustment = (baseline with
        {
            CustomImagePath = delta.ImageSourceMode == ThemeArtworkImageSourceMode.Custom
                ? delta.CustomImagePath
                : null,
            CompositionMode = mode,
            Placement = placement,
            Zoom = mode == ThemeArtworkCompositionMode.Legacy
                ? delta.LegacyZoom ?? 100d
                : 100d,
            OffsetX = mode == ThemeArtworkCompositionMode.Legacy
                ? delta.LegacyOffsetX ?? 0d
                : 0d,
            OffsetY = mode == ThemeArtworkCompositionMode.Legacy
                ? delta.LegacyOffsetY ?? 0d
                : 0d,
            Brightness = delta.Brightness ?? baseline.Brightness,
            Contrast = delta.Contrast ?? baseline.Contrast,
            Saturation = delta.Saturation ?? baseline.Saturation,
            Opacity = delta.Opacity ?? baseline.Opacity,
            Grayscale = delta.Grayscale ?? baseline.Grayscale,
            HueRotation = delta.HueRotation ?? baseline.HueRotation,
            Blur = delta.Blur ?? baseline.Blur,
            GradientStrength = delta.LegacyGradientStrength ?? baseline.GradientStrength,
            OverlayColor = delta.OverlayColor ?? baseline.OverlayColor,
            OverlayOpacity = delta.OverlayOpacity ?? baseline.OverlayOpacity,
            GradientVeil = delta.GradientVeil ?? baseline.GradientVeil,
            Vignette = delta.Vignette ?? baseline.Vignette,
            BlendMode = delta.BlendMode ?? baseline.BlendMode,
            ReadabilityVeil = delta.ReadabilityVeil ?? baseline.ReadabilityVeil,
            ReadabilityProtection = delta.LegacyReadabilityProtection ??
                baseline.ReadabilityProtection,
            Motion = delta.MotionEnabled == false ? null : baseline.Motion,
        }).Normalize();
        return CreateResolution(normalizedDefaults.Asset, baseline, adjustment, delta);
    }

    private static ThemeArtworkAdjustment CreateDefaultAdjustment(ThemeArtworkDefaultSlot slot)
    {
        ThemeArtworkPlacementSpec placement;
        try
        {
            placement = ThemeArtworkPlacementParser.Parse(slot.Placement);
        }
        catch (FormatException)
        {
            placement = new ThemeArtworkPlacementSpec();
        }
        var effects = slot.Effects;
        return new ThemeArtworkAdjustment
        {
            ThemeAssetKey = slot.Asset,
            CompositionMode = ThemeArtworkCompositionMode.Theme,
            Placement = placement,
            Brightness = effects.Brightness,
            Contrast = effects.Contrast,
            Saturation = effects.Saturation,
            Opacity = effects.Opacity,
            Grayscale = effects.Grayscale,
            HueRotation = effects.HueRotate,
            Blur = effects.Blur,
            BlendMode = effects.BlendMode,
            OverlayColor = effects.Overlay.Color,
            OverlayOpacity = effects.Overlay.Opacity,
            GradientVeil = effects.GradientVeil,
            Vignette = effects.Vignette,
            ReadabilityVeil = effects.ReadabilityVeil,
            ResponsiveVariants = slot.ResponsiveVariants,
            Motion = slot.Motion,
        }.Normalize();
    }

    private static ThemeArtworkSlotResolution CreateResolution(
        string asset,
        ThemeArtworkAdjustment baseline,
        ThemeArtworkAdjustment adjustment,
        ThemeArtworkOverride? userOverride)
    {
        var fields = new[]
        {
            nameof(ThemeArtworkAdjustment.CustomImagePath),
            nameof(ThemeArtworkAdjustment.CompositionMode),
            nameof(ThemeArtworkAdjustment.Placement),
            nameof(ThemeArtworkAdjustment.Brightness),
            nameof(ThemeArtworkAdjustment.Contrast),
            nameof(ThemeArtworkAdjustment.Saturation),
            nameof(ThemeArtworkAdjustment.Opacity),
            nameof(ThemeArtworkAdjustment.Grayscale),
            nameof(ThemeArtworkAdjustment.HueRotation),
            nameof(ThemeArtworkAdjustment.Blur),
            nameof(ThemeArtworkAdjustment.OverlayColor),
            nameof(ThemeArtworkAdjustment.OverlayOpacity),
            nameof(ThemeArtworkAdjustment.GradientVeil),
            nameof(ThemeArtworkAdjustment.Vignette),
            nameof(ThemeArtworkAdjustment.BlendMode),
            nameof(ThemeArtworkAdjustment.ReadabilityVeil),
            nameof(ThemeArtworkAdjustment.Motion),
        };
        var provenance = fields.ToDictionary(
            field => field,
            field => ResolveProvenance(field, userOverride),
            StringComparer.Ordinal);
        return new ThemeArtworkSlotResolution(
            asset,
            baseline,
            adjustment,
            userOverride,
            provenance);
    }

    private static ThemeArtworkValueSource ResolveProvenance(
        string field,
        ThemeArtworkOverride? delta)
    {
        if (delta is null) return ThemeArtworkValueSource.ThemeDefault;
        if (field == nameof(ThemeArtworkAdjustment.CustomImagePath))
        {
            return delta.ImageSourceMode is null
                ? ThemeArtworkValueSource.OriginalAsset
                : ThemeArtworkValueSource.UserOverride;
        }
        if (field is nameof(ThemeArtworkAdjustment.CompositionMode) or
            nameof(ThemeArtworkAdjustment.Placement))
        {
            return delta.CompositionMode == ThemeArtworkCompositionMode.Legacy
                ? ThemeArtworkValueSource.LegacyMigration
                : delta.CompositionMode is null
                    ? ThemeArtworkValueSource.ThemeDefault
                    : ThemeArtworkValueSource.UserOverride;
        }
        if (field == nameof(ThemeArtworkAdjustment.Motion))
        {
            return delta.MotionEnabled is null
                ? ThemeArtworkValueSource.ThemeDefault
                : ThemeArtworkValueSource.UserOverride;
        }
        var property = typeof(ThemeArtworkOverride).GetProperty(field);
        return property?.GetValue(delta) is null
            ? ThemeArtworkValueSource.ThemeDefault
            : ThemeArtworkValueSource.UserOverride;
    }

    private static ThemeDisplayPreferences ResolveDisplay(ThemeDisplayPreferencesOverride? delta) =>
        new ThemeDisplayPreferences
        {
            MotionIntensity = delta?.MotionIntensity ?? "full",
            TextScale = delta?.TextScale ?? "standard",
            Density = delta?.Density ?? "comfortable",
        }.Normalize();

    private static ThemeVisualModeSettingsOverride MigrateMode(ThemeVisualModeSettings? mode)
    {
        var value = mode ?? new ThemeVisualModeSettings();
        return new ThemeVisualModeSettingsOverride
        {
            Hero = MigrateSlot(value.Hero),
            Sidebar = MigrateSlot(value.Sidebar),
            Chat = MigrateSlot(value.Chat),
        }.Normalize();
    }

    private static ThemeArtworkOverride MigrateSlot(ThemeArtworkAdjustment? source)
    {
        var value = source ?? new ThemeArtworkAdjustment();
        var isLegacy = !AlmostEqual(value.Zoom, 100d) ||
                       !AlmostEqual(value.OffsetX, 0d) ||
                       !AlmostEqual(value.OffsetY, 0d);
        return new ThemeArtworkOverride
        {
            ImageSourceMode = string.IsNullOrWhiteSpace(value.CustomImagePath)
                ? null
                : ThemeArtworkImageSourceMode.Custom,
            CustomImagePath = value.CustomImagePath,
            CompositionMode = isLegacy ? ThemeArtworkCompositionMode.Legacy : null,
            LegacyZoom = isLegacy ? value.Zoom : null,
            LegacyOffsetX = isLegacy ? value.OffsetX : null,
            LegacyOffsetY = isLegacy ? value.OffsetY : null,
            LegacyGradientStrength = Different(value.GradientStrength, 0d),
            LegacyReadabilityProtection = value.ReadabilityProtection ? true : null,
            Brightness = Different(value.Brightness, 100d),
            Contrast = Different(value.Contrast, 100d),
            Saturation = Different(value.Saturation, 100d),
            Opacity = Different(value.Opacity, 100d),
            Grayscale = Different(value.Grayscale, 0d),
            HueRotation = Different(value.HueRotation, 0d),
            Blur = Different(value.Blur, 0d),
            OverlayColor = string.Equals(value.OverlayColor, "#000000", StringComparison.OrdinalIgnoreCase)
                ? null
                : value.OverlayColor,
            OverlayOpacity = Different(value.OverlayOpacity, 0d),
            GradientVeil = value.GradientVeil is { Enabled: true } veil ? veil : null,
            Vignette = Different(value.Vignette, 0d),
            BlendMode = string.Equals(value.BlendMode, "normal", StringComparison.OrdinalIgnoreCase)
                ? null
                : value.BlendMode,
            ReadabilityVeil = value.ReadabilityVeil is { Enabled: true } readability
                ? readability
                : null,
        }.Normalize();
    }

    private static ThemeVisualModeSettingsOverride CreateModeOverride(
        ThemeVisualModeSettings baseline,
        ThemeVisualModeSettings resolved) => new()
        {
            Hero = CreateSlotOverride(baseline.Hero, resolved.Hero),
            Sidebar = CreateSlotOverride(baseline.Sidebar, resolved.Sidebar),
            Chat = CreateSlotOverride(baseline.Chat, resolved.Chat),
        };

    private static ThemeArtworkOverride CreateSlotOverride(
        ThemeArtworkAdjustment baseline,
        ThemeArtworkAdjustment resolved)
    {
        var value = resolved.Normalize();
        var defaults = baseline.Normalize();
        return new ThemeArtworkOverride
        {
            ImageSourceMode = value.CustomImagePath is null
                ? null
                : ThemeArtworkImageSourceMode.Custom,
            CustomImagePath = value.CustomImagePath,
            CompositionMode = value.CompositionMode == ThemeArtworkCompositionMode.Theme
                ? null
                : value.CompositionMode,
            Placement = value.CompositionMode == ThemeArtworkCompositionMode.Custom
                ? value.Placement
                : null,
            LegacyZoom = value.CompositionMode == ThemeArtworkCompositionMode.Legacy
                ? value.Zoom
                : null,
            LegacyOffsetX = value.CompositionMode == ThemeArtworkCompositionMode.Legacy
                ? value.OffsetX
                : null,
            LegacyOffsetY = value.CompositionMode == ThemeArtworkCompositionMode.Legacy
                ? value.OffsetY
                : null,
            LegacyGradientStrength = Different(
                value.GradientStrength,
                defaults.GradientStrength),
            LegacyReadabilityProtection = value.ReadabilityProtection ==
                                          defaults.ReadabilityProtection
                ? null
                : value.ReadabilityProtection,
            Brightness = Different(value.Brightness, defaults.Brightness),
            Contrast = Different(value.Contrast, defaults.Contrast),
            Saturation = Different(value.Saturation, defaults.Saturation),
            Opacity = Different(value.Opacity, defaults.Opacity),
            Grayscale = Different(value.Grayscale, defaults.Grayscale),
            HueRotation = Different(value.HueRotation, defaults.HueRotation),
            Blur = Different(value.Blur, defaults.Blur),
            OverlayColor = string.Equals(
                value.OverlayColor,
                defaults.OverlayColor,
                StringComparison.OrdinalIgnoreCase) ? null : value.OverlayColor,
            OverlayOpacity = Different(value.OverlayOpacity, defaults.OverlayOpacity),
            GradientVeil = GradientVeilsEqual(value.GradientVeil, defaults.GradientVeil)
                ? null
                : value.GradientVeil,
            Vignette = Different(value.Vignette, defaults.Vignette),
            BlendMode = string.Equals(value.BlendMode, defaults.BlendMode, StringComparison.Ordinal)
                ? null
                : value.BlendMode,
            ReadabilityVeil = value.ReadabilityVeil == defaults.ReadabilityVeil
                ? null
                : value.ReadabilityVeil,
            MotionEnabled = value.Motion is null && defaults.Motion is not null
                ? false
                : null,
        }.Normalize();
    }

    private static ThemeDisplayPreferencesOverride? CreateDisplayOverride(
        ThemeDisplayPreferences baseline,
        ThemeDisplayPreferences resolved)
    {
        var defaults = baseline.Normalize();
        var value = resolved.Normalize();
        var result = new ThemeDisplayPreferencesOverride
        {
            MotionIntensity = value.MotionIntensity == defaults.MotionIntensity
                ? null
                : value.MotionIntensity,
            TextScale = value.TextScale == defaults.TextScale ? null : value.TextScale,
            Density = value.Density == defaults.Density ? null : value.Density,
        };
        return result == new ThemeDisplayPreferencesOverride() ? null : result;
    }

    private static double? Different(double value, double baseline) =>
        AlmostEqual(value, baseline) ? null : value;

    private static bool AlmostEqual(double left, double right) =>
        Math.Abs(left - right) <= .000001d;

    private static bool GradientVeilsEqual(
        ThemeArtworkGradientVeil left,
        ThemeArtworkGradientVeil right)
    {
        var first = left.Normalize();
        var second = right.Normalize();
        return first.Enabled == second.Enabled &&
               AlmostEqual(first.Strength, second.Strength) &&
               first.Layers.Count == second.Layers.Count &&
               first.Layers.Zip(second.Layers).All(pair => GradientLayersEqual(
                   pair.First,
                   pair.Second));
    }

    private static bool GradientLayersEqual(
        ThemeArtworkGradientLayer left,
        ThemeArtworkGradientLayer right) =>
        AlmostEqual(left.DirectionDeg, right.DirectionDeg) &&
        AlmostEqual(left.Start, right.Start) &&
        AlmostEqual(left.End, right.End) &&
        left.Stops.Count == right.Stops.Count &&
        left.Stops.Zip(right.Stops).All(pair =>
            AlmostEqual(pair.First.Position, pair.Second.Position) &&
            string.Equals(pair.First.Color, pair.Second.Color, StringComparison.OrdinalIgnoreCase) &&
            AlmostEqual(pair.First.Opacity, pair.Second.Opacity));
}
