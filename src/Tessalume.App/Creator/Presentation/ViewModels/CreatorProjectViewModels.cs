using System.ComponentModel;
using System.IO;
using Tessalume.Core.Creator;

namespace Tessalume.App.Creator;

internal sealed class CreatorWorkspaceItemViewModel : INotifyPropertyChanged
{
    private bool _exists;

    public CreatorWorkspaceItemViewModel(CreatorWorkspaceRecord record)
    {
        DirectoryPath = record.DirectoryPath;
        DisplayName = record.DisplayName;
        LastOpenedAt = record.LastOpenedAt;
        _exists = Directory.Exists(record.DirectoryPath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DirectoryPath { get; }

    public string DisplayName { get; }

    public DateTimeOffset LastOpenedAt { get; }

    public bool Exists => _exists;

    public string StatusText => Exists ? "可用" : "需要重新定位";

    public string StatusTone => Exists ? "ready" : "error";

    public string LastOpenedText => LastOpenedAt == default
        ? "尚未打开"
        : $"上次打开 {LastOpenedAt.ToLocalTime():MM-dd HH:mm}";

    public void SetExists(bool exists)
    {
        if (_exists == exists) return;
        _exists = exists;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Exists)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusTone)));
    }
}

internal sealed record ThemeProjectItemViewModel
{
    public ThemeProjectItemViewModel(ThemeProjectSnapshot snapshot)
    {
        Snapshot = snapshot;
        HealthGroups = snapshot.Health.Checks
            .GroupBy(check => check.Group)
            .OrderBy(group => group.Key)
            .Select(group => new ThemeHealthGroupViewModel(group.Key, group))
            .ToArray();
    }

    public ThemeProjectSnapshot Snapshot { get; }

    public string DirectoryPath => Snapshot.DirectoryPath;

    public string DisplayName => Snapshot.DisplayName;

    public string CharacterName => string.IsNullOrWhiteSpace(Snapshot.CharacterName)
        ? "未填写角色名称"
        : Snapshot.CharacterName;

    public string ThemeId => Snapshot.ThemeId ?? Snapshot.DirectoryName;

    public string VersionText => string.IsNullOrWhiteSpace(Snapshot.Version) ? "版本未知" : $"v{Snapshot.Version}";

    public string CapabilityText => (Snapshot.SupportsLight, Snapshot.SupportsDark) switch
    {
        (true, true) => "亮色 / 暗色",
        (true, false) => "仅亮色",
        (false, true) => "仅暗色",
        _ => "模式未声明",
    };

    public string AssetText => $"{Snapshot.AssetCount} 个素材";

    public string ArtworkContractText
    {
        get
        {
            var checks = Snapshot.Health.Checks
                .Where(check => check.Group == ThemeProjectHealthGroup.Artwork)
                .ToArray();
            if (checks.Any(check => check.Severity == ThemeProjectHealthSeverity.Error)) return "需要修复";
            if (checks.Any(check => check.Severity == ThemeProjectHealthSeverity.Warning)) return "有建议";
            return "六槽独立";
        }
    }

    public string ModifiedText => Snapshot.LastModifiedAt == DateTimeOffset.MinValue
        ? "修改时间未知"
        : $"更新于 {Snapshot.LastModifiedAt.ToLocalTime():MM-dd HH:mm}";

    public int ErrorCount => Snapshot.Health.ErrorCount;

    public int WarningCount => Snapshot.Health.WarningCount;

    public bool CanExport => Snapshot.Health.CanExport;

    public bool CanCopyRepairPrompt => ErrorCount + WarningCount > 0;

    public string RepairPromptButtonText => CanCopyRepairPrompt
        ? $"复制 {ErrorCount + WarningCount} 项修复提示"
        : "无需修复";

    public string StatusTone => Snapshot.Health.State switch
    {
        ThemeProjectState.Ready => "ready",
        ThemeProjectState.NeedsAttention => "warning",
        _ => "error",
    };

    public string StatusText => Snapshot.Health.State switch
    {
        ThemeProjectState.Ready => "可以导出",
        ThemeProjectState.NeedsAttention => $"{WarningCount} 项建议",
        _ => $"{ErrorCount} 项错误",
    };

    public IReadOnlyList<ThemeHealthGroupViewModel> HealthGroups { get; }
}

internal sealed record ThemeHealthGroupViewModel
{
    public ThemeHealthGroupViewModel(
        ThemeProjectHealthGroup group,
        IEnumerable<ThemeProjectHealthCheck> checks)
    {
        Group = group;
        Checks = checks.Select(check => new ThemeHealthCheckViewModel(check)).ToArray();
    }

    public ThemeProjectHealthGroup Group { get; }

    public IReadOnlyList<ThemeHealthCheckViewModel> Checks { get; }

    public bool HasProblems => Checks.Any(check => check.StatusTone != "ready");

    public string Title => Group switch
    {
        ThemeProjectHealthGroup.Manifest => "主题清单",
        ThemeProjectHealthGroup.EntryPoints => "入口文件",
        ThemeProjectHealthGroup.Assets => "标准素材",
        ThemeProjectHealthGroup.Artwork => "六槽图像推荐值",
        ThemeProjectHealthGroup.Previews => "亮暗预览",
        ThemeProjectHealthGroup.Template => "Template 1.0",
        ThemeProjectHealthGroup.Css => "CSS 样式",
        ThemeProjectHealthGroup.Script => "主题脚本",
        ThemeProjectHealthGroup.Resources => "资源引用",
        _ => "工作区",
    };

    public string StatusTone => Checks.Any(check => check.StatusTone == "error")
        ? "error"
        : Checks.Any(check => check.StatusTone == "warning")
            ? "warning"
            : "ready";

    public string Summary => StatusTone switch
    {
        "error" => $"{Checks.Count(check => check.StatusTone == "error")} 项错误",
        "warning" => $"{Checks.Count(check => check.StatusTone == "warning")} 项建议",
        _ => "检查通过",
    };
}

internal sealed record ThemeHealthCheckViewModel
{
    public ThemeHealthCheckViewModel(ThemeProjectHealthCheck check)
    {
        Code = check.Code;
        Title = check.Title;
        Message = check.Message;
        StatusTone = check.Severity switch
        {
            ThemeProjectHealthSeverity.Error => "error",
            ThemeProjectHealthSeverity.Warning => "warning",
            _ => "ready",
        };
        FilePath = check.FilePath;
        SuggestedAction = check.SuggestedAction;
    }

    public string Code { get; }

    public string Title { get; }

    public string Message { get; }

    public string StatusTone { get; }

    public string? FilePath { get; }

    public string? SuggestedAction { get; }

    public bool HasFilePath => !string.IsNullOrWhiteSpace(FilePath);

    public string DetailText => string.IsNullOrWhiteSpace(SuggestedAction)
        ? Message
        : $"{Message}\n建议：{SuggestedAction}";
}
