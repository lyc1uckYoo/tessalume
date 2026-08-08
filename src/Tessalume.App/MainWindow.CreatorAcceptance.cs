using Tessalume.App.Creator;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;

namespace Tessalume.App;

public partial class MainWindow
{
    private async Task<CreatorAcceptanceSnapshot> RunCreatorAcceptanceAsync(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        var loadResult = await new ThemePackageLoader().LoadAsync(projectDirectory, cancellationToken);
        if (!loadResult.Validation.IsValid || loadResult.Package is not { } package)
        {
            return FailedAcceptance(
                CreatorIssueOrigin.ThemeProject,
                "主题项目无法生成可用的运行包，请先处理项目体检错误。");
        }

        var applyResult = await ApplyCreatorProjectAsync(projectDirectory, automatic: false, cancellationToken);
        if (!applyResult.Succeeded || applyResult.Status.Port is not { } port)
        {
            var failureState = await _stateStore.LoadAsync(cancellationToken);
            var issueOrigin = applyResult.Status.IsConnected && failureState?.LastFailureStage is
                ThemeRuntimeFailureStage.ThemeScriptFailed or
                ThemeRuntimeFailureStage.ResourcePreflightFailed
                    ? CreatorIssueOrigin.ThemeProject
                    : CreatorIssueOrigin.RuntimeCompatibility;
            return FailedAcceptance(
                issueOrigin,
                string.IsNullOrWhiteSpace(applyResult.Message)
                    ? "主题未能应用到 Codex。"
                    : applyResult.Message);
        }

        var initialDark = applyResult.Status.IsDarkMode
            ?? await _runtime.ReadColorSchemeAsync(port, cancellationToken);
        ThemeRuntimeAcceptanceSnapshot? light = null;
        ThemeRuntimeAcceptanceSnapshot? dark = null;
        IReadOnlyList<ThemeRuntimeAcceptanceSnapshot> responsive = [];
        var currentDark = initialDark;
        try
        {
            if (currentDark)
            {
                dark = await _runtime.InspectAcceptanceAsync(port, cancellationToken);
            }
            else
            {
                light = await _runtime.InspectAcceptanceAsync(port, cancellationToken);
            }

            currentDark = await _runtime.ToggleColorSchemeAsync(port, cancellationToken);
            if (currentDark)
            {
                dark = await _runtime.InspectAcceptanceAsync(port, cancellationToken);
            }
            else
            {
                light = await _runtime.InspectAcceptanceAsync(port, cancellationToken);
            }

            responsive = await _runtime.InspectResponsiveAcceptanceAsync(
                port,
                [800, 1200, 1800],
                cancellationToken);
            return BuildAcceptance(package.Manifest.Id, light, dark, responsive);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return FailedAcceptance(CreatorIssueOrigin.RuntimeCompatibility, exception.Message);
        }
        finally
        {
            var restoreRequired = currentDark != initialDark;
            try
            {
                restoreRequired = await _runtime.ReadColorSchemeAsync(port, CancellationToken.None) != initialDark;
            }
            catch (Exception)
            {
                // Fall back to the last mode confirmed by this acceptance session.
            }
            if (restoreRequired)
            {
                try
                {
                    _ = await _runtime.ToggleColorSchemeAsync(port, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Acceptance results remain useful even if Codex closes while restoring its mode.
                }
            }
        }
    }

    private static CreatorAcceptanceSnapshot BuildAcceptance(
        string expectedThemeId,
        ThemeRuntimeAcceptanceSnapshot? light,
        ThemeRuntimeAcceptanceSnapshot? dark,
        IReadOnlyList<ThemeRuntimeAcceptanceSnapshot> responsive)
    {
        var checks = new List<CreatorAcceptanceCheck>
        {
            BuildModeCheck(CreatorAcceptanceCheckId.LightMode, "亮色模式", expectedThemeId, light),
            BuildModeCheck(CreatorAcceptanceCheckId.DarkMode, "暗色模式", expectedThemeId, dark),
        };

        var surfaces = new[] { light, dark }.Where(snapshot => snapshot is not null).Cast<ThemeRuntimeAcceptanceSnapshot>().ToArray();
        checks.Add(BuildComposerCheck(surfaces));
        checks.Add(BuildMessagesCheck(surfaces));
        checks.Add(BuildResponsiveCheck(responsive));
        return new CreatorAcceptanceSnapshot(DateTimeOffset.Now, checks);
    }

    private static CreatorAcceptanceCheck BuildModeCheck(
        CreatorAcceptanceCheckId id,
        string title,
        string expectedThemeId,
        ThemeRuntimeAcceptanceSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return FailedCheck(id, title, CreatorIssueOrigin.RuntimeCompatibility, "Codex 未返回该模式的验收快照。");
        }
        if (!snapshot.RuntimeReady || !snapshot.MainSurfaceReady)
        {
            return FailedCheck(id, title, CreatorIssueOrigin.RuntimeCompatibility, "页面语义标记或主内容区未被兼容引擎正确识别。");
        }
        if (!snapshot.ThemeMounted || !string.Equals(snapshot.ThemeId, expectedThemeId, StringComparison.OrdinalIgnoreCase))
        {
            return FailedCheck(id, title, CreatorIssueOrigin.ThemeProject, "当前页面未挂载正在验收的主题。");
        }
        return PassedCheck(id, title, $"已确认 {snapshot.ViewportWidth}×{snapshot.ViewportHeight} 视口下主题与主内容区正常。");
    }

    private static CreatorAcceptanceCheck BuildComposerCheck(IReadOnlyList<ThemeRuntimeAcceptanceSnapshot> snapshots)
    {
        if (snapshots.Any(snapshot => snapshot.ComposerPresent && !snapshot.ComposerDecorated))
        {
            return FailedCheck(CreatorAcceptanceCheckId.Composer, "输入框", CreatorIssueOrigin.RuntimeCompatibility, "已发现 Codex 输入框，但稳定语义标记未生效。");
        }
        if (snapshots.Any(snapshot => snapshot.ComposerPresent && snapshot.ComposerDecorated))
        {
            return PassedCheck(CreatorAcceptanceCheckId.Composer, "输入框", "亮暗切换后输入框的兼容标记仍然稳定。");
        }
        return AttentionCheck(CreatorAcceptanceCheckId.Composer, "输入框", "当前 Codex 不在任务页，未发现可见输入框；请打开会话后再运行。");
    }

    private static CreatorAcceptanceCheck BuildMessagesCheck(IReadOnlyList<ThemeRuntimeAcceptanceSnapshot> snapshots)
    {
        var observed = snapshots.Where(snapshot => snapshot.MessageCount > 0).ToArray();
        if (observed.Any(snapshot => snapshot.DecoratedMessageCount < snapshot.MessageCount))
        {
            return FailedCheck(CreatorAcceptanceCheckId.Messages, "消息框", CreatorIssueOrigin.RuntimeCompatibility, "部分消息未获得稳定的用户/助手语义标记。");
        }
        if (observed.Length > 0)
        {
            var count = observed.Max(snapshot => snapshot.MessageCount);
            return PassedCheck(CreatorAcceptanceCheckId.Messages, "消息框", $"已检查 {count} 个消息单元，语义标记完整。");
        }
        return AttentionCheck(CreatorAcceptanceCheckId.Messages, "消息框", "当前会话没有可见消息；消息规则已装载，建议在有对话的任务中复验。");
    }

    private static CreatorAcceptanceCheck BuildResponsiveCheck(IReadOnlyList<ThemeRuntimeAcceptanceSnapshot> snapshots)
    {
        if (snapshots.Count != 3 || snapshots.Any(snapshot =>
                snapshot.PageKind != "task" ||
                !snapshot.ResponsiveLayoutReady ||
                string.IsNullOrWhiteSpace(snapshot.ResponsiveLayout)))
        {
            return FailedCheck(
                CreatorAcceptanceCheckId.ResponsiveLayout,
                "响应式布局",
                CreatorIssueOrigin.RuntimeCompatibility,
                "请在任务会话中复验；紧凑、标准或宽屏视口没有生成完整的自适应布局。 ");
        }
        var distinctLayouts = snapshots
            .Select(snapshot => snapshot.ResponsiveLayout)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (distinctLayouts < 2)
        {
            return FailedCheck(
                CreatorAcceptanceCheckId.ResponsiveLayout,
                "响应式布局",
                CreatorIssueOrigin.RuntimeCompatibility,
                "三个测试视口始终使用同一布局，未观察到有效的响应式降级。 ");
        }
        var detail = string.Join(" · ", snapshots.Select(snapshot =>
            $"{snapshot.ViewportWidth}px {snapshot.ResponsiveLayout}"));
        return PassedCheck(
            CreatorAcceptanceCheckId.ResponsiveLayout,
            "响应式布局",
            $"已完成紧凑、标准和宽屏三档实测：{detail}。");
    }

    private static CreatorAcceptanceSnapshot FailedAcceptance(CreatorIssueOrigin origin, string detail) =>
        new(DateTimeOffset.Now, CreatorAcceptanceCatalog.CreatePendingChecks()
            .Select(check => check with { State = CreatorAcceptanceState.Failed, IssueOrigin = origin, Detail = detail })
            .ToArray());

    private static CreatorAcceptanceCheck PassedCheck(CreatorAcceptanceCheckId id, string title, string detail) =>
        new(id, CreatorAcceptanceState.Passed, CreatorIssueOrigin.None, title, detail);

    private static CreatorAcceptanceCheck AttentionCheck(CreatorAcceptanceCheckId id, string title, string detail) =>
        new(id, CreatorAcceptanceState.NeedsAttention, CreatorIssueOrigin.None, title, detail);

    private static CreatorAcceptanceCheck FailedCheck(
        CreatorAcceptanceCheckId id,
        string title,
        CreatorIssueOrigin origin,
        string detail) => new(id, CreatorAcceptanceState.Failed, origin, title, detail);
}
