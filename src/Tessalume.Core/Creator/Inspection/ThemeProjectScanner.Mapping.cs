using System.Text.Json;
using Tessalume.Core.Themes;

namespace Tessalume.Core.Creator;

public sealed partial class ThemeProjectScanner
{
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

}
