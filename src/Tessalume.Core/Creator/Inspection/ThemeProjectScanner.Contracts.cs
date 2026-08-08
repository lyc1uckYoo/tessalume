using System.Text.Json;
using Tessalume.Core.Themes;

namespace Tessalume.Core.Creator;

public sealed partial class ThemeProjectScanner
{
    private static async Task AddCreatorContractChecksAsync(
        string root,
        ThemeManifest manifest,
        ThemePackage? package,
        List<ThemeProjectHealthCheck> checks,
        CancellationToken cancellationToken)
    {
        if (manifest.Template is null)
        {
            checks.Add(new ThemeProjectHealthCheck(
                ThemeProjectHealthGroup.Template,
                "creator.template.required",
                "缺少 Template 1.0 声明",
                "创作项目必须声明旗舰共享模板，才能进入标准导出流程。",
                ThemeProjectHealthSeverity.Error,
                Path.Combine(root, ThemePackageLoader.ManifestFileName),
                "将 template 设置为 id=flagship、version=1.0、style=shared。"));
        }

        if (manifest.UsesSharedTemplateV1)
        {
            var missingAssets = RequiredTemplateV1Assets
                .Where(name => !manifest.Assets.ContainsKey(name))
                .ToArray();
            if (missingAssets.Length > 0)
            {
                checks.Add(new ThemeProjectHealthCheck(
                    ThemeProjectHealthGroup.Assets,
                    "creator.assets.required.missing",
                    "11 个标准素材位未完成",
                    $"缺少素材：{string.Join("、", missingAssets)}。",
                    ThemeProjectHealthSeverity.Error,
                    Path.Combine(root, ThemePackageLoader.ManifestFileName),
                    "补齐缺失素材，并在 manifest.json 的 assets 中逐项声明。"));
            }

            var standardAssets = RequiredTemplateV1Assets
                .Concat(OptionalTemplateV1Assets)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknownAssets = manifest.Assets.Keys
                .Where(name => !standardAssets.Contains(name))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (unknownAssets.Length > 0)
            {
                checks.Add(new ThemeProjectHealthCheck(
                    ThemeProjectHealthGroup.Assets,
                    "creator.assets.nonstandard",
                    "存在非标准素材键",
                    $"Template 1.0 未定义：{string.Join("、", unknownAssets)}。",
                    ThemeProjectHealthSeverity.Error,
                    Path.Combine(root, ThemePackageLoader.ManifestFileName),
                    "将素材归入 11 个标准素材位，或三个可选暗色任务卡素材位。"));
            }

            var mismatchedAssetNames = manifest.Assets
                .Where(pair => !string.Equals(
                    Path.GetFileNameWithoutExtension(pair.Value),
                    pair.Key,
                    StringComparison.OrdinalIgnoreCase))
                .Select(pair => $"{pair.Key} → {pair.Value}")
                .ToArray();
            if (mismatchedAssetNames.Length > 0)
            {
                checks.Add(new ThemeProjectHealthCheck(
                    ThemeProjectHealthGroup.Assets,
                    "creator.assets.filename.mismatch",
                    "素材文件名与素材位不一致",
                    string.Join("；", mismatchedAssetNames),
                    ThemeProjectHealthSeverity.Error,
                    Path.Combine(root, ThemePackageLoader.ManifestFileName),
                    "让每个素材文件的主文件名与 manifest.json 中的素材键一致。"));
            }

            AddRequiredPreviewCheck(root, manifest.Capabilities.Light, manifest.Previews.Light, "light", "亮色", checks);
            AddRequiredPreviewCheck(root, manifest.Capabilities.Dark, manifest.Previews.Dark, "dark", "暗色", checks);

            if (string.IsNullOrWhiteSpace(manifest.EntryPoints.Css))
            {
                checks.Add(new ThemeProjectHealthCheck(
                    ThemeProjectHealthGroup.EntryPoints,
                    "creator.entry.css.missing",
                    "缺少主题样式入口",
                    "Template 1.0 主题需要 CSS 入口文件。",
                    ThemeProjectHealthSeverity.Error,
                    Path.Combine(root, ThemePackageLoader.ManifestFileName),
                    "在 entryPoints.css 中声明 skin.css。"));
            }
        }

        if (package?.ScriptPath is { } scriptPath)
        {
            var script = await File.ReadAllTextAsync(scriptPath, cancellationToken);
            AddSingleCallCheck(
                checks,
                ThemeProjectHealthGroup.Script,
                script,
                "registerTheme(",
                "creator.script.registration.count",
                "主题生命周期注册数量不正确",
                scriptPath,
                "使用且只使用一次 registerTheme({ mount, unmount })。");
            AddSingleCallCheck(
                checks,
                ThemeProjectHealthGroup.Script,
                script,
                "context.mountCanonicalTheme(",
                "creator.script.canonical-host.count",
                "统一运行时挂载数量不正确",
                scriptPath,
                "在 mount 中调用且只调用一次 context.mountCanonicalTheme(...)。");

            if (manifest.UsesSharedTemplateV1)
            {
                AddSingleCallCheck(
                    checks,
                    ThemeProjectHealthGroup.Template,
                    script,
                    "context.renderTemplateV1(",
                    "creator.template.renderer.count",
                    "Template 1.0 渲染数量不正确",
                    scriptPath,
                    "使用且只使用一次 context.renderTemplateV1(...)。");
                AddRequiredTokenCheck(
                    checks,
                    ThemeProjectHealthGroup.Template,
                    script,
                    "templateVersion: \"1.0\"",
                    "creator.template.version-token.missing",
                    "运行时模板版本缺失",
                    scriptPath,
                    "在 mountCanonicalTheme 参数中声明 templateVersion: \"1.0\"。");
            }

            AddDraftChecks(script, scriptPath, ThemeProjectHealthGroup.Template, checks);
        }

        if (package?.CssPath is { } cssPath)
        {
            var css = await File.ReadAllTextAsync(cssPath, cancellationToken);
            if (!HasBalancedCssBraces(css))
            {
                checks.Add(new ThemeProjectHealthCheck(
                    ThemeProjectHealthGroup.Css,
                    "creator.css.braces.unbalanced",
                    "CSS 大括号不完整",
                    "样式文件包含未配对的大括号，浏览器可能忽略后续规则。",
                    ThemeProjectHealthSeverity.Error,
                    cssPath,
                    "检查最近修改的 CSS 规则并补齐大括号。"));
            }

            AddDraftChecks(css, cssPath, ThemeProjectHealthGroup.Css, checks);

            var referencedAssets = AssetVariableRegex().Matches(css)
                .Select(match => match.Groups[1].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var undeclaredAssets = referencedAssets
                .Where(name => !manifest.Assets.ContainsKey(name))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (undeclaredAssets.Length > 0)
            {
                checks.Add(new ThemeProjectHealthCheck(
                    ThemeProjectHealthGroup.Resources,
                    "creator.css.assets.undeclared",
                    "CSS 引用了未声明素材",
                    $"未声明的素材变量：{string.Join("、", undeclaredAssets)}。",
                    ThemeProjectHealthSeverity.Error,
                    cssPath,
                    "在 manifest.json 的 assets 中声明这些素材，或修正变量名称。"));
            }

            var unusedAssets = manifest.Assets.Keys
                .Where(name => !referencedAssets.Contains(name))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (unusedAssets.Length > 0)
            {
                checks.Add(new ThemeProjectHealthCheck(
                    ThemeProjectHealthGroup.Resources,
                    "creator.assets.unused",
                    "存在未使用的已声明素材",
                    $"当前 CSS 未引用：{string.Join("、", unusedAssets)}。",
                    ThemeProjectHealthSeverity.Warning,
                    cssPath,
                    "确认主题脚本是否使用这些素材；不需要的声明可从清单中移除。"));
            }
        }

        AddDraftChecks(
            JsonSerializer.Serialize(manifest),
            Path.Combine(root, ThemePackageLoader.ManifestFileName),
            ThemeProjectHealthGroup.Manifest,
            checks);
    }

    private static void AddRequiredPreviewCheck(
        string root,
        bool capabilityEnabled,
        string? preview,
        string codeSuffix,
        string modeName,
        List<ThemeProjectHealthCheck> checks)
    {
        if (!capabilityEnabled || !string.IsNullOrWhiteSpace(preview)) return;
        checks.Add(new ThemeProjectHealthCheck(
            ThemeProjectHealthGroup.Previews,
            $"creator.preview.{codeSuffix}.missing",
            $"缺少{modeName}预览",
            $"主题声明支持{modeName}模式，但没有对应预览图。",
            ThemeProjectHealthSeverity.Error,
            Path.Combine(root, ThemePackageLoader.ManifestFileName),
            $"生成{modeName}预览图，并在 previews.{codeSuffix} 中声明。"));
    }

    private static void AddRequiredTokenCheck(
        List<ThemeProjectHealthCheck> checks,
        ThemeProjectHealthGroup group,
        string source,
        string token,
        string code,
        string title,
        string filePath,
        string suggestion)
    {
        if (source.Contains(token, StringComparison.Ordinal)) return;
        checks.Add(new ThemeProjectHealthCheck(
            group,
            code,
            title,
            $"文件中未找到必需调用或声明：{token}",
            ThemeProjectHealthSeverity.Error,
            filePath,
            suggestion));
    }

    private static void AddSingleCallCheck(
        List<ThemeProjectHealthCheck> checks,
        ThemeProjectHealthGroup group,
        string source,
        string token,
        string code,
        string title,
        string filePath,
        string suggestion)
    {
        var count = CountOccurrences(source, token);
        if (count == 1) return;
        checks.Add(new ThemeProjectHealthCheck(
            group,
            code,
            title,
            count == 0
                ? $"文件中未找到必需调用：{token}"
                : $"必需调用 {token} 出现了 {count} 次。",
            ThemeProjectHealthSeverity.Error,
            filePath,
            suggestion));
    }

    private static void AddDraftChecks(
        string source,
        string filePath,
        ThemeProjectHealthGroup group,
        List<ThemeProjectHealthCheck> checks)
    {
        var unresolved = DraftTokens
            .Where(token => source.Contains(token, StringComparison.Ordinal))
            .ToArray();
        if (unresolved.Length == 0) return;
        checks.Add(new ThemeProjectHealthCheck(
            group,
            "creator.draft.unresolved",
            "仍有模板草稿未替换",
            $"发现占位内容：{string.Join("、", unresolved)}。",
            ThemeProjectHealthSeverity.Error,
            filePath,
            "替换占位素材、示例文案和草稿标记后重新校验。"));
    }

}
