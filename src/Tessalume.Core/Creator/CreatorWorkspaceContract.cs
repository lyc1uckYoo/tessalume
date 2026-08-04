using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tessalume.Core.Creator;

public enum CreatorWorkspaceContractState
{
    Missing,
    Legacy,
    Current,
    UpgradeAvailable,
    Newer,
    Invalid,
}

public sealed record CreatorWorkspaceContractInfo(
    CreatorWorkspaceContractState State,
    string? WorkspaceVersion,
    string? TemplateVersion,
    string Message)
{
    public bool CanUpgrade => State is
        CreatorWorkspaceContractState.Legacy or
        CreatorWorkspaceContractState.UpgradeAvailable or
        CreatorWorkspaceContractState.Invalid;
}

public static class CreatorWorkspaceContract
{
    public const string MarkerFileName = "TESSALUME_CREATOR_WORKSPACE.json";
    public const string LegacyMarkerFileName = "TESSALUME_CREATOR_WORKSPACE.md";
    public const string CurrentWorkspaceVersion = "1.0";
    public const string CurrentTemplateVersion = "1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static CreatorWorkspaceContractInfo Inspect(string workspaceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        var workspace = Path.GetFullPath(workspaceDirectory);
        if (!Directory.Exists(workspace))
        {
            return new CreatorWorkspaceContractInfo(
                CreatorWorkspaceContractState.Missing,
                null,
                null,
                "工作区目录不存在。");
        }

        var markerPath = Path.Combine(workspace, MarkerFileName);
        if (!File.Exists(markerPath))
        {
            return File.Exists(Path.Combine(workspace, LegacyMarkerFileName))
                ? new CreatorWorkspaceContractInfo(
                    CreatorWorkspaceContractState.Legacy,
                    null,
                    CurrentTemplateVersion,
                    "这是旧版 Tessalume 创作者工作区，可以安全补齐当前工具链。")
                : new CreatorWorkspaceContractInfo(
                    CreatorWorkspaceContractState.Missing,
                    null,
                    null,
                    "没有找到标准创作者工作区版本标记。");
        }

        try
        {
            using var stream = File.OpenRead(markerPath);
            var marker = JsonSerializer.Deserialize<CreatorWorkspaceMarker>(stream, JsonOptions);
            if (marker is null ||
                marker.SchemaVersion != 1 ||
                !Version.TryParse(marker.WorkspaceVersion, out var workspaceVersion) ||
                !Version.TryParse(CurrentWorkspaceVersion, out var currentVersion) ||
                string.IsNullOrWhiteSpace(marker.TemplateVersion))
            {
                return Invalid(marker?.WorkspaceVersion, marker?.TemplateVersion);
            }

            var comparison = workspaceVersion.CompareTo(currentVersion);
            if (comparison > 0)
            {
                return new CreatorWorkspaceContractInfo(
                    CreatorWorkspaceContractState.Newer,
                    marker.WorkspaceVersion,
                    marker.TemplateVersion,
                    "工作区由更新版本的 Tessalume 创建，当前版本不会降级其中的工具文件。");
            }

            if (comparison < 0 ||
                !string.Equals(
                    marker.TemplateVersion,
                    CurrentTemplateVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new CreatorWorkspaceContractInfo(
                    CreatorWorkspaceContractState.UpgradeAvailable,
                    marker.WorkspaceVersion,
                    marker.TemplateVersion,
                    "工作区工具链有可用更新，用户主题项目不会被修改。");
            }

            return new CreatorWorkspaceContractInfo(
                CreatorWorkspaceContractState.Current,
                marker.WorkspaceVersion,
                marker.TemplateVersion,
                "工作区工具链和 Template 版本均为最新。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Invalid(null, null, exception.Message);
        }
    }

    private static CreatorWorkspaceContractInfo Invalid(
        string? workspaceVersion,
        string? templateVersion,
        string? detail = null) =>
        new(
            CreatorWorkspaceContractState.Invalid,
            workspaceVersion,
            templateVersion,
            string.IsNullOrWhiteSpace(detail)
                ? "工作区版本标记格式不完整，可以通过安全升级进行修复。"
                : $"工作区版本标记无法读取：{detail}");

    private sealed record CreatorWorkspaceMarker
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("workspaceVersion")]
        public string WorkspaceVersion { get; init; } = string.Empty;

        [JsonPropertyName("templateVersion")]
        public string TemplateVersion { get; init; } = string.Empty;
    }
}
