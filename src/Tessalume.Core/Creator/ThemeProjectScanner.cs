using System.Text.Json;
using System.Text.RegularExpressions;
using Tessalume.Core.Themes;

namespace Tessalume.Core.Creator;

public sealed partial class ThemeProjectScanner(ThemePackageLoader loader)
{
    private static readonly ThemeProjectHealthGroup[] ProjectGroups =
    [
        ThemeProjectHealthGroup.Manifest,
        ThemeProjectHealthGroup.EntryPoints,
        ThemeProjectHealthGroup.Assets,
        ThemeProjectHealthGroup.Previews,
        ThemeProjectHealthGroup.Template,
        ThemeProjectHealthGroup.Css,
        ThemeProjectHealthGroup.Script,
        ThemeProjectHealthGroup.Resources,
    ];

    private static readonly string[] RequiredTemplateV1Assets =
    [
        "hero-light", "hero-dark", "sidebar-light", "sidebar-dark", "chat-light", "chat-dark",
        "task-left", "task-right-secondary", "task-right-primary", "memory-light", "memory-dark",
    ];

    private static readonly HashSet<string> OptionalTemplateV1Assets = new(StringComparer.OrdinalIgnoreCase)
    {
        "task-left-dark", "task-right-secondary-dark", "task-right-primary-dark",
    };

    private static readonly string[] DraftTokens =
    [
        "data-theme-draft=", "assets/placeholder.svg", "assets/placeholder.png",
        "在这里填写", "亮色主标题", "暗色主标题", "角色挂件",
    ];

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public async Task<CreatorWorkspaceScanResult> ScanWorkspaceAsync(
        string workspaceDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        var workspace = Path.GetFullPath(workspaceDirectory);
        var themesDirectory = Path.Combine(workspace, "themes");
        var workspaceChecks = new List<ThemeProjectHealthCheck>();
        var workspaceContract = CreatorWorkspaceContract.Inspect(workspace);

        if (!Directory.Exists(workspace))
        {
            workspaceChecks.Add(new ThemeProjectHealthCheck(
                ThemeProjectHealthGroup.Workspace,
                "workspace.directory.missing",
                "工作区不可用",
                "工作区文件夹不存在，可能已被移动或删除。",
                ThemeProjectHealthSeverity.Error,
                workspace,
                "重新定位工作区，或将它从最近项目中移除。"));
            return new CreatorWorkspaceScanResult(
                workspace,
                themesDirectory,
                [],
                new ThemeProjectHealthReport(workspaceChecks))
            { Contract = workspaceContract };
        }

        if (!File.Exists(Path.Combine(workspace, "TESSALUME_CREATOR_WORKSPACE.md")))
        {
            workspaceChecks.Add(new ThemeProjectHealthCheck(
                ThemeProjectHealthGroup.Workspace,
                "workspace.marker.missing",
                "未找到创作者工作区标记",
                "该文件夹仍可扫描，但可能不是由 Tessalume 创建的标准工作区。",
                ThemeProjectHealthSeverity.Warning,
                workspace,
                "确认所选目录包含 themes 文件夹，或重新创建标准创作工作区。"));
        }

        AddWorkspaceContractCheck(workspaceChecks, workspace, workspaceContract);

        if (!Directory.Exists(themesDirectory))
        {
            workspaceChecks.Add(new ThemeProjectHealthCheck(
                ThemeProjectHealthGroup.Workspace,
                "workspace.themes.missing",
                "缺少 themes 文件夹",
                "工作区中没有用于保存主题项目的 themes 文件夹。",
                ThemeProjectHealthSeverity.Error,
                themesDirectory,
                "在工作区根目录创建 themes 文件夹。"));
            return new CreatorWorkspaceScanResult(
                workspace,
                themesDirectory,
                [],
                new ThemeProjectHealthReport(workspaceChecks))
            { Contract = workspaceContract };
        }

        string[] projectDirectories;
        try
        {
            projectDirectories = Directory.EnumerateDirectories(themesDirectory)
                .Where(directory => !Path.GetFileName(directory).StartsWith('.'))
                .Where(directory => (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            workspaceChecks.Add(new ThemeProjectHealthCheck(
                ThemeProjectHealthGroup.Workspace,
                "workspace.themes.unreadable",
                "无法读取主题目录",
                exception.Message,
                ThemeProjectHealthSeverity.Error,
                themesDirectory,
                "检查文件夹权限，并确认目录未被其他程序独占。"));
            return new CreatorWorkspaceScanResult(
                workspace,
                themesDirectory,
                [],
                new ThemeProjectHealthReport(workspaceChecks))
            { Contract = workspaceContract };
        }

        var projects = new List<ThemeProjectSnapshot>(projectDirectories.Length);
        foreach (var directory in projectDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            projects.Add(await ScanProjectAsync(directory, cancellationToken));
        }

        workspaceChecks.Add(new ThemeProjectHealthCheck(
            ThemeProjectHealthGroup.Workspace,
            projectDirectories.Length == 0 ? "workspace.projects.empty" : "workspace.ready",
            projectDirectories.Length == 0 ? "还没有主题项目" : "工作区可用",
            projectDirectories.Length == 0
                ? "Codex 生成的主题会出现在 themes 文件夹中。"
                : $"已发现 {projectDirectories.Length} 个主题项目。",
            projectDirectories.Length == 0
                ? ThemeProjectHealthSeverity.Warning
                : ThemeProjectHealthSeverity.Passed,
            themesDirectory,
            projectDirectories.Length == 0 ? "在 Codex 中完成一次主题创作后重新扫描。" : null));

        return new CreatorWorkspaceScanResult(
            workspace,
            themesDirectory,
            projects,
            new ThemeProjectHealthReport(workspaceChecks))
        { Contract = workspaceContract };
    }

    private static void AddWorkspaceContractCheck(
        List<ThemeProjectHealthCheck> checks,
        string workspace,
        CreatorWorkspaceContractInfo contract)
    {
        var markerPath = Path.Combine(workspace, CreatorWorkspaceContract.MarkerFileName);
        var (code, title, severity, action) = contract.State switch
        {
            CreatorWorkspaceContractState.Current => (
                "workspace.contract.current",
                "工作区工具链为最新",
                ThemeProjectHealthSeverity.Passed,
                (string?)null),
            CreatorWorkspaceContractState.Legacy => (
                "workspace.contract.legacy",
                "旧版工作区可以升级",
                ThemeProjectHealthSeverity.Warning,
                "在 Tessalume 创作项目中心点击“安全升级”。"),
            CreatorWorkspaceContractState.UpgradeAvailable => (
                "workspace.contract.upgrade",
                "工作区工具链有可用更新",
                ThemeProjectHealthSeverity.Warning,
                "在 Tessalume 创作项目中心点击“安全升级”。"),
            CreatorWorkspaceContractState.Newer => (
                "workspace.contract.newer",
                "工作区来自更新版本",
                ThemeProjectHealthSeverity.Warning,
                "使用创建这个工作区的 Tessalume 版本，避免降级工具文件。"),
            CreatorWorkspaceContractState.Invalid => (
                "workspace.contract.invalid",
                "工作区版本标记需要修复",
                ThemeProjectHealthSeverity.Warning,
                "使用安全升级备份旧工具文件并重建版本标记。"),
            _ => (
                "workspace.contract.missing",
                "未识别工作区工具链版本",
                ThemeProjectHealthSeverity.Warning,
                "如果这是 Tessalume 工作区，可重新创建标准工作区后迁移 themes 项目。"),
        };
        checks.Add(new ThemeProjectHealthCheck(
            ThemeProjectHealthGroup.Workspace,
            code,
            title,
            contract.Message,
            severity,
            markerPath,
            action));
    }

    public async Task<ThemeProjectSnapshot> ScanProjectAsync(
        string themeDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeDirectory);
        var root = Path.GetFullPath(themeDirectory);
        var directoryName = Path.GetFileName(root);
        var checks = new List<ThemeProjectHealthCheck>();
        if (!Directory.Exists(root))
        {
            checks.Add(new ThemeProjectHealthCheck(
                ThemeProjectHealthGroup.Manifest,
                "project.directory.missing",
                "主题项目不存在",
                "主题项目文件夹可能已被移动或删除。",
                ThemeProjectHealthSeverity.Error,
                root,
                "重新定位主题项目或从工作区中移除该记录。"));
            return CreateSnapshot(root, directoryName, null, checks, DateTimeOffset.MinValue);
        }

        ThemeLoadResult loadResult;
        try
        {
            loadResult = await loader.LoadAsync(root, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            checks.Add(new ThemeProjectHealthCheck(
                ThemeProjectHealthGroup.Resources,
                "project.read.failed",
                "无法读取主题项目",
                exception.Message,
                ThemeProjectHealthSeverity.Error,
                root,
                "等待 Codex 或图片工具完成写入，然后重新校验。"));
            return CreateSnapshot(root, directoryName, null, checks, GetLastModified(root, null));
        }

        var manifest = loadResult.Package?.Manifest ??
            await TryReadManifestAsync(Path.Combine(root, ThemePackageLoader.ManifestFileName), cancellationToken);
        foreach (var issue in loadResult.Validation.Issues)
        {
            checks.Add(MapValidationIssue(root, issue));
        }

        if (manifest is not null)
        {
            try
            {
                await AddCreatorContractChecksAsync(root, manifest, loadResult.Package, checks, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                checks.Add(new ThemeProjectHealthCheck(
                    ThemeProjectHealthGroup.Resources,
                    "project.changed-during-scan",
                    "项目文件正在变化",
                    exception.Message,
                    ThemeProjectHealthSeverity.Error,
                    root,
                    "等待 Codex 或图片工具完成写入，然后重新校验。"));
            }
            AddPassedGroupChecks(checks);
        }
        return CreateSnapshot(
            root,
            directoryName,
            manifest,
            checks,
            GetLastModified(root, loadResult.Package));
    }

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

    private static ThemeProjectSnapshot CreateSnapshot(
        string root,
        string directoryName,
        ThemeManifest? manifest,
        List<ThemeProjectHealthCheck> checks,
        DateTimeOffset lastModifiedAt)
    {
        var name = string.IsNullOrWhiteSpace(manifest?.Name) ? directoryName : manifest.Name;
        return new ThemeProjectSnapshot(
            root,
            directoryName,
            NullIfWhiteSpace(manifest?.Id),
            name,
            manifest is null ? null : ReadConfigString(manifest, "character") ?? ReadConfigString(manifest, "title"),
            NullIfWhiteSpace(manifest?.Version),
            NullIfWhiteSpace(manifest?.Author),
            manifest?.Capabilities.Light == true,
            manifest?.Capabilities.Dark == true,
            manifest?.Assets.Count ?? 0,
            lastModifiedAt,
            new ThemeProjectHealthReport(checks))
        {
            WatchedFiles = EnumerateDeclaredFiles(root, manifest).ToArray(),
        };
    }

    private static IEnumerable<string> EnumerateDeclaredFiles(string root, ThemeManifest? manifest)
    {
        yield return Path.Combine(root, ThemePackageLoader.ManifestFileName);
        if (manifest is null) yield break;

        foreach (var relativePath in new[]
                 {
                     manifest.EntryPoints.Css,
                     manifest.EntryPoints.Script,
                     manifest.Previews.Light,
                     manifest.Previews.Dark,
                 }.Concat(manifest.Assets.Values))
        {
            if (string.IsNullOrWhiteSpace(relativePath)) continue;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                continue;
            }
            if (Path.GetRelativePath(root, fullPath).StartsWith("..", StringComparison.Ordinal)) continue;
            yield return fullPath;
        }
    }

    private async Task<ThemeManifest?> TryReadManifestAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ThemeManifest>(
                stream,
                _jsonOptions,
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static ThemeProjectHealthCheck MapValidationIssue(string root, ThemeValidationIssue issue)
    {
        var group = ResolveGroup(issue.Code);
        return new ThemeProjectHealthCheck(
            group,
            issue.Code,
            GetIssueTitle(group),
            issue.Message,
            issue.Severity == ThemeValidationSeverity.Error
                ? ThemeProjectHealthSeverity.Error
                : ThemeProjectHealthSeverity.Warning,
            ResolveIssuePath(root, issue.Path, group),
            GetSuggestedAction(issue.Code, group));
    }

    private static string? ResolveIssuePath(
        string root,
        string? issuePath,
        ThemeProjectHealthGroup group)
    {
        if (string.IsNullOrWhiteSpace(issuePath))
        {
            return group is ThemeProjectHealthGroup.Manifest or
                ThemeProjectHealthGroup.Template or
                ThemeProjectHealthGroup.EntryPoints or
                ThemeProjectHealthGroup.Assets
                ? Path.Combine(root, ThemePackageLoader.ManifestFileName)
                : null;
        }
        if (Path.IsPathRooted(issuePath)) return issuePath;
        try
        {
            return Path.GetFullPath(Path.Combine(root, issuePath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return issuePath;
        }
    }

    private static ThemeProjectHealthGroup ResolveGroup(string code)
    {
        if (code.StartsWith("manifest.template", StringComparison.Ordinal)) return ThemeProjectHealthGroup.Template;
        if (code.StartsWith("manifest", StringComparison.Ordinal)) return ThemeProjectHealthGroup.Manifest;
        if (code.StartsWith("entry", StringComparison.Ordinal)) return ThemeProjectHealthGroup.EntryPoints;
        if (code.StartsWith("asset", StringComparison.Ordinal)) return ThemeProjectHealthGroup.Assets;
        if (code.StartsWith("preview", StringComparison.Ordinal)) return ThemeProjectHealthGroup.Previews;
        if (code.StartsWith("css", StringComparison.Ordinal)) return ThemeProjectHealthGroup.Css;
        if (code.StartsWith("script", StringComparison.Ordinal)) return ThemeProjectHealthGroup.Script;
        return ThemeProjectHealthGroup.Resources;
    }

    private static string GetIssueTitle(ThemeProjectHealthGroup group) => group switch
    {
        ThemeProjectHealthGroup.Manifest => "主题清单需要修复",
        ThemeProjectHealthGroup.EntryPoints => "入口文件需要修复",
        ThemeProjectHealthGroup.Assets => "主题素材需要修复",
        ThemeProjectHealthGroup.Previews => "主题预览需要修复",
        ThemeProjectHealthGroup.Template => "模板声明需要修复",
        ThemeProjectHealthGroup.Css => "主题样式需要修复",
        ThemeProjectHealthGroup.Script => "主题脚本需要修复",
        _ => "主题资源需要修复",
    };

    private static string GetSuggestedAction(string code, ThemeProjectHealthGroup group)
    {
        if (code == "manifest.missing") return "在主题根目录创建 manifest.json。";
        if (code == "manifest.invalid-json") return "修正 JSON 语法后重新校验。";
        if (code.Contains("missing", StringComparison.Ordinal)) return "补齐缺失文件或清单字段，并确认相对路径正确。";
        if (code.Contains("outside", StringComparison.Ordinal) || code.Contains("rooted", StringComparison.Ordinal))
            return "只使用主题目录内部的相对路径。";
        return group switch
        {
            ThemeProjectHealthGroup.Manifest => "按照 manifest v2 规范修正字段。",
            ThemeProjectHealthGroup.Css => "修正 CSS 后重新校验。",
            ThemeProjectHealthGroup.Script => "修正 theme.js 后重新校验。",
            ThemeProjectHealthGroup.Assets or ThemeProjectHealthGroup.Previews => "核对文件格式、大小和清单路径。",
            _ => "根据错误说明修正对应文件后重新校验。",
        };
    }

    private static void AddPassedGroupChecks(List<ThemeProjectHealthCheck> checks)
    {
        foreach (var group in ProjectGroups)
        {
            if (checks.Any(check => check.Group == group)) continue;
            checks.Add(new ThemeProjectHealthCheck(
                group,
                $"creator.{group.ToString().ToLowerInvariant()}.passed",
                $"{GetGroupDisplayName(group)}检查通过",
                "未发现需要处理的问题。",
                ThemeProjectHealthSeverity.Passed));
        }
    }

    private static string GetGroupDisplayName(ThemeProjectHealthGroup group) => group switch
    {
        ThemeProjectHealthGroup.Manifest => "主题清单",
        ThemeProjectHealthGroup.EntryPoints => "入口文件",
        ThemeProjectHealthGroup.Assets => "标准素材",
        ThemeProjectHealthGroup.Previews => "亮暗预览",
        ThemeProjectHealthGroup.Template => "Template 1.0",
        ThemeProjectHealthGroup.Css => "CSS",
        ThemeProjectHealthGroup.Script => "脚本",
        ThemeProjectHealthGroup.Resources => "资源引用",
        _ => "工作区",
    };

    private static DateTimeOffset GetLastModified(string root, ThemePackage? package)
    {
        try
        {
            var paths = package is null
                ? new[] { Path.Combine(root, ThemePackageLoader.ManifestFileName) }
                : EnumeratePackageFiles(package);
            var newest = paths
                .Where(File.Exists)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(Directory.GetLastWriteTimeUtc(root))
                .Max();
            return new DateTimeOffset(DateTime.SpecifyKind(newest, DateTimeKind.Utc));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    private static IEnumerable<string> EnumeratePackageFiles(ThemePackage package)
    {
        yield return package.ManifestPath;
        if (package.CssPath is not null) yield return package.CssPath;
        if (package.ScriptPath is not null) yield return package.ScriptPath;
        if (package.PreviewLightPath is not null) yield return package.PreviewLightPath;
        if (package.PreviewDarkPath is not null) yield return package.PreviewDarkPath;
        foreach (var path in package.AssetPaths.Values) yield return path;
    }

    private static string? ReadConfigString(ThemeManifest manifest, string key) =>
        manifest.Config.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? NullIfWhiteSpace(value.GetString())
            : null;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int CountOccurrences(string source, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    private static bool HasBalancedCssBraces(string css)
    {
        var depth = 0;
        var quote = '\0';
        var inComment = false;
        for (var index = 0; index < css.Length; index++)
        {
            var current = css[index];
            var next = index + 1 < css.Length ? css[index + 1] : '\0';
            if (inComment)
            {
                if (current == '*' && next == '/')
                {
                    inComment = false;
                    index++;
                }
                continue;
            }
            if (quote != '\0')
            {
                if (current == '\\')
                {
                    index++;
                    continue;
                }
                if (current == quote) quote = '\0';
                continue;
            }
            if (current == '/' && next == '*')
            {
                inComment = true;
                index++;
                continue;
            }
            if (current is '\'' or '"')
            {
                quote = current;
                continue;
            }
            if (current == '{') depth++;
            if (current == '}' && --depth < 0) return false;
        }
        return depth == 0 && quote == '\0' && !inComment;
    }

    [GeneratedRegex(@"var\(\s*--tessalume-asset-([A-Za-z0-9][A-Za-z0-9._-]*)\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex AssetVariableRegex();
}
