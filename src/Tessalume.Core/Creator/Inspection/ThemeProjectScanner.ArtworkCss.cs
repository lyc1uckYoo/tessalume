using System.Text.RegularExpressions;

namespace Tessalume.Core.Creator;

public sealed partial class ThemeProjectScanner
{
    private static readonly HashSet<string> RuntimeArtworkProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "background",
        "background-image",
        "background-size",
        "background-position",
        "background-position-x",
        "background-position-y",
        "background-blend-mode",
        "filter",
        "opacity",
        "transform",
        "transform-origin",
        "translate",
        "scale",
        "animation",
        "animation-name",
    };

    private static void AddArtworkCssOwnershipChecks(
        string css,
        string cssPath,
        List<ThemeProjectHealthCheck> checks)
    {
        var directArtworkReferences = AssetVariableRegex().Matches(css)
            .Select(match => match.Groups[1].Value)
            .Where(RuntimeArtworkAssetNames.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (directArtworkReferences.Length > 0)
        {
            checks.Add(new ThemeProjectHealthCheck(
                ThemeProjectHealthGroup.Artwork,
                "creator.artwork.css.asset-reference",
                "可调图像仍由 CSS 引用",
                $"skin.css 直接引用了运行时拥有的素材：{string.Join("、", directArtworkReferences)}。",
                ThemeProjectHealthSeverity.Error,
                cssPath,
                "删除这六个素材在 CSS 中的变量和图片引用；由 artwork-defaults.json 选择原图并提供推荐构图。"));
        }

        foreach (Match rule in CssRuleRegex().Matches(css))
        {
            var selector = rule.Groups[1].Value.Trim();
            var body = rule.Groups[2].Value;
            var artworkLayer = ResolveArtworkLayer(selector);
            if (artworkLayer is not null)
            {
                var conflicts = CssPropertyRegex().Matches(body)
                    .Select(match => match.Groups[1].Value)
                    .Where(RuntimeArtworkProperties.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (conflicts.Length > 0)
                {
                    checks.Add(new ThemeProjectHealthCheck(
                        ThemeProjectHealthGroup.Artwork,
                        $"creator.artwork.css.{artworkLayer}",
                        $"{GetArtworkLayerName(artworkLayer)}仍存在 CSS 写死值",
                        $"选择器 {NormalizeSelector(selector)} 定义了：{string.Join("、", conflicts)}。",
                        ThemeProjectHealthSeverity.Error,
                        cssPath,
                        "把最终尺寸、位置、滤镜、透明度、混合模式和图片动效迁入 artwork-defaults.json；CSS 只保留独立装饰层。"));
                }
            }

            if (IsTaskReadabilityVeil(selector))
            {
                var conflicts = CssPropertyRegex().Matches(body)
                    .Select(match => match.Groups[1].Value)
                    .Where(property => property.Equals("background", StringComparison.OrdinalIgnoreCase) ||
                        property.Equals("background-image", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (conflicts.Length > 0)
                {
                    checks.Add(new ThemeProjectHealthCheck(
                        ThemeProjectHealthGroup.Artwork,
                        "creator.artwork.css.chat-veil",
                        "聊天可读性遮罩仍由 CSS 写死",
                        $"选择器 {NormalizeSelector(selector)} 定义了聊天遮罩背景。",
                        ThemeProjectHealthSeverity.Error,
                        cssPath,
                        "将聊天遮罩迁入 artwork-defaults.json 的 gradientVeil/readabilityVeil。"));
                }
            }
        }
    }

    private static string? ResolveArtworkLayer(string selector)
    {
        if (selector.Contains("aside.app-shell-left-panel::after", StringComparison.OrdinalIgnoreCase))
        {
            return "sidebar";
        }
        if (selector.Contains("-home", StringComparison.OrdinalIgnoreCase) &&
            selector.Contains("div:first-child>div:first-child>div:first-child::before", StringComparison.OrdinalIgnoreCase))
        {
            return "hero";
        }
        if (selector.Contains("-is-task", StringComparison.OrdinalIgnoreCase) &&
            selector.Contains("main.", StringComparison.OrdinalIgnoreCase) &&
            selector.Contains("-main::before", StringComparison.OrdinalIgnoreCase))
        {
            return "chat";
        }
        return null;
    }

    private static bool IsTaskReadabilityVeil(string selector) =>
        selector.Contains("-is-task", StringComparison.OrdinalIgnoreCase) &&
        selector.Contains("main.", StringComparison.OrdinalIgnoreCase) &&
        selector.Contains("-main::after", StringComparison.OrdinalIgnoreCase);

    private static string GetArtworkLayerName(string layer) => layer switch
    {
        "hero" => "首页横幅",
        "sidebar" => "左栏图片",
        _ => "聊天背景",
    };

    private static string NormalizeSelector(string selector)
    {
        var compact = string.Join(' ', selector.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return compact.Length <= 180 ? compact : compact[..180] + "…";
    }

    [GeneratedRegex(@"([^{}]+)\{([^{}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex CssRuleRegex();

    [GeneratedRegex(@"(?:^|;)\s*([\w-]+)\s*:", RegexOptions.CultureInvariant)]
    private static partial Regex CssPropertyRegex();
}
