using System.ComponentModel;
using System.Text.Json;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Diagnostics;

internal sealed record CompatibilityHealthSnapshot(
    string? InstalledCodexVersion,
    int RuntimeContractVersion,
    bool CodexVersionChanged,
    bool RuntimeContractChanged,
    DateTimeOffset? LastSuccessfulApplyAt,
    ThemeRuntimeFailureStage LastFailureStage,
    string? LastFailureMessage,
    DateTimeOffset? LastFailureAt)
{
    public bool RequiresPreflight => CodexVersionChanged || RuntimeContractChanged;
}

internal static class CompatibilityHealthService
{
    public static async Task<CompatibilityHealthSnapshot> InspectAsync(
        StudioState? state,
        CancellationToken cancellationToken = default)
    {
        string? installedVersion;
        try
        {
            installedVersion = await CodexPackageLauncher.FindInstalledVersionAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or JsonException or Win32Exception)
        {
            installedVersion = null;
        }

        var recordedVersion = state?.CodexVersionAtLastApply;
        var recordedContract = state?.RuntimeContractVersion ?? 0;
        return new CompatibilityHealthSnapshot(
            installedVersion,
            ThemeRuntime.ContractVersion,
            !string.IsNullOrWhiteSpace(recordedVersion) &&
                !string.IsNullOrWhiteSpace(installedVersion) &&
                !string.Equals(recordedVersion, installedVersion, StringComparison.OrdinalIgnoreCase),
            recordedContract > 0 && recordedContract != ThemeRuntime.ContractVersion,
            state?.LastSuccessfulApplyAt,
            state?.LastFailureStage ?? ThemeRuntimeFailureStage.None,
            state?.LastFailureMessage,
            state?.LastFailureAt);
    }

    public static string GetFailureStageLabel(ThemeRuntimeFailureStage stage) => stage switch
    {
        ThemeRuntimeFailureStage.None => "无失败记录",
        ThemeRuntimeFailureStage.CodexNotFound => "未找到 Codex",
        ThemeRuntimeFailureStage.PortUnavailable => "本机端口不可用",
        ThemeRuntimeFailureStage.PageTargetsMissing => "未发现可用页面",
        ThemeRuntimeFailureStage.ResourcePreflightFailed => "主题资源预检失败",
        ThemeRuntimeFailureStage.RuntimeInjectionFailed => "运行时注入失败",
        ThemeRuntimeFailureStage.ThemeScriptFailed => "主题脚本失败",
        _ => "未知阶段",
    };

    public static string GetRecommendation(ThemeRuntimeFailureStage stage) => stage switch
    {
        ThemeRuntimeFailureStage.None => "无需处理；Codex 更新后，下一次应用会自动重新预检。",
        ThemeRuntimeFailureStage.CodexNotFound => "确认 Microsoft Store 版 Codex 已安装，再重新应用主题。",
        ThemeRuntimeFailureStage.PortUnavailable => "保存 Codex 中的工作后重新应用主题，Tessalume 会协助重启并重建本机连接。",
        ThemeRuntimeFailureStage.PageTargetsMissing => "等待 Codex 主页面加载完成后重试；若仍失败，请重启 Codex。",
        ThemeRuntimeFailureStage.ResourcePreflightFailed => "重新导入该主题，或在创作中心修复缺失和损坏的本地素材。",
        ThemeRuntimeFailureStage.RuntimeInjectionFailed => "先重启 Codex 再重试，并确认 Tessalume 与 Codex 都已更新。",
        ThemeRuntimeFailureStage.ThemeScriptFailed => "在创作中心运行主题体检，修复 theme.js 后再应用。",
        _ => "重新应用主题；若问题持续，请打开本地日志查看详细原因。",
    };
}
