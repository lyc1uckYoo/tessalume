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
