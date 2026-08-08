using Tessalume.Core.Creator;
using Tessalume.Core.Themes;

namespace Tessalume.App.Creator;

internal sealed record CreatorApplicationServices(
    ICreatorProjectInspectionService ProjectInspection,
    ICreatorProjectExportService ProjectExport,
    ICreatorWorkflowEvaluator WorkflowEvaluator,
    ICreatorAcceptanceService Acceptance,
    ICreatorRuntimeGateway Runtime,
    IThemeProjectWatcherFactory WatcherFactory)
{
    public static CreatorApplicationServices CreateDefault(ICreatorRuntimeGateway? runtime = null)
    {
        var loader = new ThemePackageLoader();
        var runtimeGateway = runtime ?? CreatorRuntimeBridge.Unavailable;
        return new CreatorApplicationServices(
            new CreatorProjectInspectionService(new ThemeProjectScanner(loader)),
            new CreatorProjectExportService(new ThemeArchiveWriter(loader)),
            new CreatorWorkflowEvaluator(),
            new CreatorAcceptanceService(runtimeGateway),
            runtimeGateway,
            new ThemeProjectWatcherFactory());
    }
}
