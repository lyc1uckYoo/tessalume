using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;

internal static class ArtworkSettingsReducer
{
    public static ThemeVisualSettings SetCustomPlacement(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region,
        ThemeArtworkPlacementSpec placement) =>
        UpdateAdjustment(settings, mode, region, adjustment => adjustment with
        {
            CompositionMode = ThemeArtworkCompositionMode.Custom,
            Placement = (placement ?? new ThemeArtworkPlacementSpec()).Normalize(),
            Zoom = 100d,
            OffsetX = 0d,
            OffsetY = 0d,
        });

    public static ThemeVisualSettings RestoreParameterToTheme(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region,
        ArtworkParameter parameter,
        ThemeArtworkAdjustment themeDefault)
    {
        ArgumentNullException.ThrowIfNull(themeDefault);
        var baseline = themeDefault.Normalize();
        return UpdateAdjustment(settings, mode, region, adjustment => parameter switch
        {
            ArtworkParameter.Brightness => adjustment with { Brightness = baseline.Brightness },
            ArtworkParameter.Contrast => adjustment with { Contrast = baseline.Contrast },
            ArtworkParameter.Saturation => adjustment with { Saturation = baseline.Saturation },
            ArtworkParameter.Opacity => adjustment with { Opacity = baseline.Opacity },
            ArtworkParameter.Zoom => adjustment with { Zoom = 100d },
            ArtworkParameter.OffsetX => adjustment with { OffsetX = 0d },
            ArtworkParameter.OffsetY => adjustment with { OffsetY = 0d },
            ArtworkParameter.Grayscale => adjustment with { Grayscale = baseline.Grayscale },
            ArtworkParameter.HueRotation => adjustment with { HueRotation = baseline.HueRotation },
            ArtworkParameter.Blur => adjustment with { Blur = baseline.Blur },
            ArtworkParameter.OverlayColor => adjustment with
            {
                OverlayColor = baseline.OverlayColor,
            },
            ArtworkParameter.OverlayOpacity => adjustment with
            {
                OverlayOpacity = baseline.OverlayOpacity,
            },
            ArtworkParameter.GradientStrength => adjustment with
            {
                GradientStrength = baseline.GradientStrength,
                GradientVeil = baseline.GradientVeil,
            },
            ArtworkParameter.Vignette => adjustment with { Vignette = baseline.Vignette },
            ArtworkParameter.BlendMode => adjustment with { BlendMode = baseline.BlendMode },
            ArtworkParameter.ReadabilityProtection => adjustment with
            {
                ReadabilityProtection = baseline.ReadabilityProtection,
                ReadabilityVeil = baseline.ReadabilityVeil,
            },
            _ => adjustment,
        });
    }

    public static ThemeVisualSettings RestoreGroupToTheme(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region,
        ArtworkParameterGroup group,
        ThemeArtworkAdjustment themeDefault)
    {
        ArgumentNullException.ThrowIfNull(themeDefault);
        var baseline = themeDefault.Normalize();
        return UpdateAdjustment(settings, mode, region, adjustment => group switch
        {
            ArtworkParameterGroup.Composition => adjustment with
            {
                CompositionMode = ThemeArtworkCompositionMode.Theme,
                Placement = baseline.Placement,
                Zoom = 100d,
                OffsetX = 0d,
                OffsetY = 0d,
            },
            ArtworkParameterGroup.Effects => adjustment with
            {
                Grayscale = baseline.Grayscale,
                HueRotation = baseline.HueRotation,
                Blur = baseline.Blur,
                OverlayColor = baseline.OverlayColor,
                OverlayOpacity = baseline.OverlayOpacity,
                GradientStrength = baseline.GradientStrength,
                GradientVeil = baseline.GradientVeil,
                Vignette = baseline.Vignette,
                BlendMode = baseline.BlendMode,
                ReadabilityProtection = baseline.ReadabilityProtection,
                ReadabilityVeil = baseline.ReadabilityVeil,
            },
            _ => adjustment with
            {
                Brightness = baseline.Brightness,
                Contrast = baseline.Contrast,
                Saturation = baseline.Saturation,
                Opacity = baseline.Opacity,
            },
        });
    }

    public static ThemeVisualSettings RestoreSlotToTheme(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region,
        ThemeArtworkAdjustment themeDefault) =>
        UpdateAdjustment(settings, mode, region, current =>
        {
            var baseline = (themeDefault ?? new ThemeArtworkAdjustment()).Normalize();
            return baseline with { CustomImagePath = current.Normalize().CustomImagePath };
        });

    public static ThemeVisualSettings RestoreSlotToOriginalBaseline(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region) =>
        UpdateAdjustment(settings, mode, region, current => new ThemeArtworkAdjustment
        {
            // "Original baseline" is deliberately the manifest theme asset, not
            // a local replacement with neutral effects.
            CustomImagePath = null,
            ThemeAssetKey = current.Normalize().ThemeAssetKey,
            CompositionMode = ThemeArtworkCompositionMode.Custom,
            Placement = ArtworkOriginalBaseline.ContainCenteredPlacement,
        });

    public static ThemeVisualSettings UpdateAdjustment(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region,
        Func<ThemeArtworkAdjustment, ThemeArtworkAdjustment> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var current = ArtworkSettingsAccessor.GetAdjustment(settings, mode, region);
        var replacement = update(current) ?? current;
        return ArtworkSettingsAccessor.SetAdjustment(settings, mode, region, replacement.Normalize());
    }

    public static ThemeVisualSettings SetParameter(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region,
        ArtworkParameter parameter,
        double value) =>
        UpdateAdjustment(settings, mode, region, adjustment => parameter switch
        {
            ArtworkParameter.Brightness => adjustment with { Brightness = value },
            ArtworkParameter.Contrast => adjustment with { Contrast = value },
            ArtworkParameter.Saturation => adjustment with { Saturation = value },
            ArtworkParameter.Opacity => adjustment with { Opacity = value },
            ArtworkParameter.Zoom => adjustment with { Zoom = value },
            ArtworkParameter.OffsetX => adjustment with { OffsetX = value },
            ArtworkParameter.OffsetY => adjustment with { OffsetY = value },
            ArtworkParameter.Grayscale => adjustment with { Grayscale = value },
            ArtworkParameter.HueRotation => adjustment with { HueRotation = value },
            ArtworkParameter.Blur => adjustment with { Blur = value },
            ArtworkParameter.OverlayOpacity => adjustment with { OverlayOpacity = value },
            ArtworkParameter.GradientStrength => adjustment with
            {
                GradientStrength = 0d,
                GradientVeil = adjustment.GradientVeil with
                {
                    Enabled = value > 0d,
                    Strength = value,
                },
            },
            ArtworkParameter.Vignette => adjustment with { Vignette = value },
            _ => throw new ArgumentException(
                $"{parameter} is not a numeric artwork parameter.",
                nameof(parameter)),
        });

    public static ThemeVisualSettings SetParameter(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region,
        ArtworkParameter parameter,
        string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return UpdateAdjustment(settings, mode, region, adjustment => parameter switch
        {
            ArtworkParameter.OverlayColor => adjustment with { OverlayColor = value },
            ArtworkParameter.BlendMode => adjustment with { BlendMode = value },
            _ => throw new ArgumentException(
                $"{parameter} is not a text artwork parameter.",
                nameof(parameter)),
        });
    }

    public static ThemeVisualSettings SetParameter(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region,
        ArtworkParameter parameter,
        bool value) =>
        UpdateAdjustment(settings, mode, region, adjustment => parameter switch
        {
            ArtworkParameter.ReadabilityProtection => adjustment with
            {
                ReadabilityProtection = value,
                ReadabilityVeil = adjustment.ReadabilityVeil with
                {
                    Enabled = value,
                    Opacity = value && adjustment.ReadabilityVeil.Opacity <= 0d
                        ? 42d
                        : adjustment.ReadabilityVeil.Opacity,
                },
            },
            _ => throw new ArgumentException(
                $"{parameter} is not a Boolean artwork parameter.",
                nameof(parameter)),
        });

    public static ThemeVisualSettings Reset(
        ThemeVisualSettings settings,
        ArtworkResetRequest request) => request.Scope switch
        {
            ArtworkResetScope.Parameter when request.Parameter is { } parameter =>
                ResetParameter(settings, request.Mode, request.Region, parameter),
            ArtworkResetScope.ParameterGroup when request.Group is { } group =>
                ResetGroup(settings, request.Mode, request.Region, group),
            ArtworkResetScope.RegionMode =>
                ResetRegionMode(settings, request.Mode, request.Region),
            ArtworkResetScope.Mode => ResetMode(settings, request.Mode),
            ArtworkResetScope.Theme => ResetTheme(settings),
            ArtworkResetScope.Parameter => throw new ArgumentException(
                "A parameter reset requires a parameter.",
                nameof(request)),
            ArtworkResetScope.ParameterGroup => throw new ArgumentException(
                "A parameter-group reset requires a group.",
                nameof(request)),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

    public static ThemeVisualSettings ResetParameter(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region,
        ArtworkParameter parameter)
    {
        var defaults = new ThemeArtworkAdjustment();
        return UpdateAdjustment(settings, mode, region, adjustment => parameter switch
        {
            ArtworkParameter.Brightness => adjustment with { Brightness = defaults.Brightness },
            ArtworkParameter.Contrast => adjustment with { Contrast = defaults.Contrast },
            ArtworkParameter.Saturation => adjustment with { Saturation = defaults.Saturation },
            ArtworkParameter.Opacity => adjustment with { Opacity = defaults.Opacity },
            ArtworkParameter.Zoom => adjustment with { Zoom = defaults.Zoom },
            ArtworkParameter.OffsetX => adjustment with { OffsetX = defaults.OffsetX },
            ArtworkParameter.OffsetY => adjustment with { OffsetY = defaults.OffsetY },
            ArtworkParameter.Grayscale => adjustment with { Grayscale = defaults.Grayscale },
            ArtworkParameter.HueRotation => adjustment with { HueRotation = defaults.HueRotation },
            ArtworkParameter.Blur => adjustment with { Blur = defaults.Blur },
            ArtworkParameter.OverlayColor => adjustment with { OverlayColor = defaults.OverlayColor },
            ArtworkParameter.OverlayOpacity => adjustment with
            {
                OverlayOpacity = defaults.OverlayOpacity,
            },
            ArtworkParameter.GradientStrength => adjustment with
            {
                GradientStrength = defaults.GradientStrength,
            },
            ArtworkParameter.Vignette => adjustment with { Vignette = defaults.Vignette },
            ArtworkParameter.BlendMode => adjustment with { BlendMode = defaults.BlendMode },
            ArtworkParameter.ReadabilityProtection => adjustment with
            {
                ReadabilityProtection = defaults.ReadabilityProtection,
            },
            _ => adjustment,
        });
    }

    public static ThemeVisualSettings ResetGroup(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region,
        ArtworkParameterGroup group) =>
        UpdateAdjustment(settings, mode, region, adjustment => ResetGroup(adjustment, group));

    public static ThemeVisualSettings ResetRegionMode(
        ThemeVisualSettings settings,
        ArtworkColorMode mode,
        ArtworkRegion region) =>
        UpdateAdjustment(settings, mode, region, ResetAdjustmentPreservingImage);

    public static ThemeVisualSettings ResetMode(
        ThemeVisualSettings settings,
        ArtworkColorMode mode)
    {
        var current = ArtworkSettingsAccessor.GetMode(settings, mode);
        var replacement = new ThemeVisualModeSettings
        {
            Hero = ResetAdjustmentPreservingImage(current.Hero),
            Sidebar = ResetAdjustmentPreservingImage(current.Sidebar),
            Chat = ResetAdjustmentPreservingImage(current.Chat),
        };
        return ArtworkSettingsAccessor.SetMode(settings, mode, replacement);
    }

    public static ThemeVisualSettings ResetTheme(ThemeVisualSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return ResetMode(ResetMode(settings, ArtworkColorMode.Light), ArtworkColorMode.Dark);
    }

    public static ThemeVisualSettings PasteRegion(
        ThemeVisualSettings settings,
        ArtworkColorMode targetMode,
        ArtworkRegion targetRegion,
        ThemeArtworkAdjustment copiedParameters)
    {
        ArgumentNullException.ThrowIfNull(copiedParameters);
        var target = ArtworkSettingsAccessor.GetAdjustment(settings, targetMode, targetRegion);
        return ArtworkSettingsAccessor.SetAdjustment(
            settings,
            targetMode,
            targetRegion,
            CopyParametersPreservingTargetImage(copiedParameters, target));
    }

    public static ThemeVisualSettings CopyRegion(
        ThemeVisualSettings settings,
        ArtworkColorMode sourceMode,
        ArtworkRegion sourceRegion,
        ArtworkColorMode targetMode,
        ArtworkRegion targetRegion)
    {
        var source = ArtworkSettingsAccessor.GetAdjustment(settings, sourceMode, sourceRegion);
        return PasteRegion(settings, targetMode, targetRegion, source);
    }

    public static ThemeVisualSettings CopyMode(
        ThemeVisualSettings settings,
        ArtworkColorMode sourceMode,
        ArtworkColorMode targetMode)
    {
        var source = ArtworkSettingsAccessor.GetMode(settings, sourceMode);
        var target = ArtworkSettingsAccessor.GetMode(settings, targetMode);
        var replacement = MergeParametersPreservingTargetImages(source, target);
        return ArtworkSettingsAccessor.SetMode(settings, targetMode, replacement);
    }

    public static ThemeVisualSettings ApplyPreset(
        ThemeVisualSettings settings,
        ArtworkColorMode targetMode,
        ThemeVisualModeSettings preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var target = ArtworkSettingsAccessor.GetMode(settings, targetMode);
        var replacement = MergeParametersPreservingTargetImages(preset, target);
        return ArtworkSettingsAccessor.SetMode(settings, targetMode, replacement);
    }

    public static ThemeArtworkAdjustment CopyParametersPreservingTargetImage(
        ThemeArtworkAdjustment source,
        ThemeArtworkAdjustment target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var normalizedSource = source.Normalize();
        var normalizedTarget = target.Normalize();
        return (normalizedSource with
        {
            CustomImagePath = normalizedTarget.CustomImagePath,
            ThemeAssetKey = normalizedTarget.ThemeAssetKey,
            Motion = normalizedTarget.Motion,
            Placement = normalizedSource.CompositionMode == ThemeArtworkCompositionMode.Theme
                ? normalizedTarget.Placement
                : normalizedSource.Placement,
        }).Normalize();
    }

    private static ThemeVisualModeSettings MergeParametersPreservingTargetImages(
        ThemeVisualModeSettings source,
        ThemeVisualModeSettings target)
    {
        var normalizedSource = (source ?? new ThemeVisualModeSettings()).Normalize();
        var normalizedTarget = (target ?? new ThemeVisualModeSettings()).Normalize();
        return new ThemeVisualModeSettings
        {
            Hero = CopyParametersPreservingTargetImage(normalizedSource.Hero, normalizedTarget.Hero),
            Sidebar = CopyParametersPreservingTargetImage(
                normalizedSource.Sidebar,
                normalizedTarget.Sidebar),
            Chat = CopyParametersPreservingTargetImage(normalizedSource.Chat, normalizedTarget.Chat),
        }.Normalize();
    }

    private static ThemeArtworkAdjustment ResetGroup(
        ThemeArtworkAdjustment adjustment,
        ArtworkParameterGroup group)
    {
        var defaults = new ThemeArtworkAdjustment();
        return (group switch
        {
            ArtworkParameterGroup.Composition => adjustment with
            {
                Zoom = defaults.Zoom,
                OffsetX = defaults.OffsetX,
                OffsetY = defaults.OffsetY,
            },
            ArtworkParameterGroup.Effects => adjustment with
            {
                Grayscale = defaults.Grayscale,
                HueRotation = defaults.HueRotation,
                Blur = defaults.Blur,
                OverlayColor = defaults.OverlayColor,
                OverlayOpacity = defaults.OverlayOpacity,
                GradientStrength = defaults.GradientStrength,
                Vignette = defaults.Vignette,
                BlendMode = defaults.BlendMode,
                ReadabilityProtection = defaults.ReadabilityProtection,
            },
            _ => adjustment with
            {
                Brightness = defaults.Brightness,
                Contrast = defaults.Contrast,
                Saturation = defaults.Saturation,
                Opacity = defaults.Opacity,
            },
        }).Normalize();
    }

    private static ThemeArtworkAdjustment ResetAdjustmentPreservingImage(
        ThemeArtworkAdjustment adjustment) =>
        new ThemeArtworkAdjustment
        {
            CustomImagePath = (adjustment ?? new ThemeArtworkAdjustment()).Normalize().CustomImagePath,
        }.Normalize();
}

internal static class ArtworkOriginalBaseline
{
    public static ThemeArtworkPlacementSpec ContainCenteredPlacement { get; } = new()
    {
        SizeMode = ThemeArtworkSizeMode.Contain,
        PositionX = ThemeArtworkPositionValue.Center,
        PositionY = ThemeArtworkPositionValue.Center,
        Geometry = new ThemeArtworkGeometry(),
    };
}
