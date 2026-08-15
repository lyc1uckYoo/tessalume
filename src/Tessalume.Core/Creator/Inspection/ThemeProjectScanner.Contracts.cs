using System.Text.Json;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;

namespace Tessalume.Core.Creator;

public sealed partial class ThemeProjectScanner
{
    private static readonly JsonSerializerOptions ArtworkDefaultsJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        };

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

            await AddArtworkDefaultsContractChecksAsync(
                root,
                manifest,
                package,
                checks,
                cancellationToken);
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

    private static async Task AddArtworkDefaultsContractChecksAsync(
        string root,
        ThemeManifest manifest,
        ThemePackage? package,
        List<ThemeProjectHealthCheck> checks,
        CancellationToken cancellationToken)
    {
        var declaredPath = manifest.EntryPoints.ArtworkDefaults;
        if (string.IsNullOrWhiteSpace(declaredPath))
        {
            AddArtworkDefaultsError(
                checks,
                "creator.artwork-defaults.required",
                "缺少六槽图像推荐值",
                "Template 1.0 创作项目必须声明 entryPoints.artworkDefaults。",
                Path.Combine(root, ThemePackageLoader.ManifestFileName),
                "从模板创建 artwork-defaults.json，并完整填写首页横幅、左栏图片、聊天背景的亮暗六个槽位。");
            return;
        }

        var path = package?.ArtworkDefaultsPath ?? TryResolveArtworkDefaultsPath(root, declaredPath);
        if (path is null || !File.Exists(path))
        {
            AddArtworkDefaultsError(
                checks,
                "creator.artwork-defaults.invalid",
                "图像推荐值入口无效",
                "entryPoints.artworkDefaults 必须指向主题目录内存在的 JSON 文件。",
                Path.Combine(root, ThemePackageLoader.ManifestFileName),
                "修正 artwork-defaults.json 的相对路径后重新体检。");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            using var syntax = JsonDocument.Parse(json);
            if (!HasSixArtworkSlots(syntax.RootElement))
            {
                throw new InvalidDataException(
                    "slots 必须完整包含 hero/sidebar/chat 的 light/dark 六个槽位，且每槽声明 asset、placement、effects。");
            }

            var document = JsonSerializer.Deserialize<ThemeArtworkDefaultsDocument>(
                json,
                ArtworkDefaultsJsonOptions)
                ?? throw new InvalidDataException("artwork-defaults.json 为空。");
            if (document.SchemaVersion != 1 ||
                !string.Equals(document.ThemeId, manifest.Id, StringComparison.Ordinal) ||
                !Version.TryParse(document.DefaultsVersion, out _))
            {
                throw new InvalidDataException(
                    "schemaVersion、themeId 或 defaultsVersion 与主题契约不一致。");
            }
            ThemeArtworkDefaultsValidator.Validate(document);

            var expectedSlots = new (string Asset, ThemeArtworkDefaultSlot Slot)[]
            {
                ("hero-light", document.Slots.Hero.Light),
                ("hero-dark", document.Slots.Hero.Dark),
                ("sidebar-light", document.Slots.Sidebar.Light),
                ("sidebar-dark", document.Slots.Sidebar.Dark),
                ("chat-light", document.Slots.Chat.Light),
                ("chat-dark", document.Slots.Chat.Dark),
            };
            foreach (var (asset, slot) in expectedSlots)
            {
                if (!string.Equals(slot.Asset, asset, StringComparison.OrdinalIgnoreCase) ||
                    !manifest.Assets.ContainsKey(slot.Asset))
                {
                    throw new InvalidDataException(
                        $"槽位 {asset} 必须引用 manifest 中同名的原始素材键。");
                }
            }
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidDataException or FormatException or IOException or UnauthorizedAccessException)
        {
            AddArtworkDefaultsError(
                checks,
                "creator.artwork-defaults.invalid",
                "六槽图像推荐值无效",
                exception.Message,
                path,
                "按 theme-artwork-defaults-v1 schema 修正六槽资源、最终构图和效果字段。");
        }
    }

    private static bool HasSixArtworkSlots(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("slots", out var slots) ||
            slots.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var region in new[] { "hero", "sidebar", "chat" })
        {
            if (!slots.TryGetProperty(region, out var modes) || modes.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            foreach (var mode in new[] { "light", "dark" })
            {
                if (!modes.TryGetProperty(mode, out var slot) ||
                    slot.ValueKind != JsonValueKind.Object ||
                    !slot.TryGetProperty("asset", out _) ||
                    !slot.TryGetProperty("placement", out _) ||
                    !slot.TryGetProperty("effects", out _))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static string? TryResolveArtworkDefaultsPath(string root, string declaredPath)
    {
        try
        {
            if (Path.IsPathRooted(declaredPath)) return null;
            var candidate = Path.GetFullPath(Path.Combine(root, declaredPath));
            var relative = Path.GetRelativePath(root, candidate);
            return relative == ".." ||
                   relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                   Path.IsPathRooted(relative)
                ? null
                : candidate;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static void AddArtworkDefaultsError(
        List<ThemeProjectHealthCheck> checks,
        string code,
        string title,
        string message,
        string filePath,
        string suggestedAction) => checks.Add(new ThemeProjectHealthCheck(
        ThemeProjectHealthGroup.EntryPoints,
        code,
        title,
        message,
        ThemeProjectHealthSeverity.Error,
        filePath,
        suggestedAction));

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
