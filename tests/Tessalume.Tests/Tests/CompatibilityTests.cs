internal static partial class TestSuite
{
    static async Task CompatibilityHealthStateIsDurableAsync()
    {
        var data = Path.Combine(Path.GetTempPath(), $"tessalume-compatibility-{Guid.NewGuid():N}");
        Directory.CreateDirectory(data);
        try
        {
            var store = new StudioStateStore(data);
            var failureAt = DateTimeOffset.UtcNow;
            await store.SaveAsync(new StudioState
            {
                Port = 9340,
                ThemeId = "sample.theme",
                Enabled = true,
                LastSuccessfulApplyAt = failureAt.AddMinutes(-2),
                CodexVersionAtLastApply = "1.2.3.4",
                RuntimeContractVersion = ThemeRuntime.ContractVersion,
                LastFailureStage = ThemeRuntimeFailureStage.ThemeScriptFailed,
                LastFailureMessage = "fixture script failure",
                LastFailureAt = failureAt,
            });
            var restored = await store.LoadAsync()
                ?? throw new InvalidOperationException("Compatibility state did not reload.");
            var json = await File.ReadAllTextAsync(Path.Combine(data, "state.json"));
            Ensure(restored.SchemaVersion == StudioState.CurrentSchemaVersion &&
                   restored.RuntimeContractVersion == ThemeRuntime.ContractVersion &&
                   restored.LastFailureStage == ThemeRuntimeFailureStage.ThemeScriptFailed &&
                   restored.CodexVersionAtLastApply == "1.2.3.4" &&
                   json.Contains("\"ThemeScriptFailed\"", StringComparison.Ordinal),
                "Compatibility baselines and failure stages must survive a restart in readable state JSON.");

            var repositoryRoot = FindRepositoryRoot();
            var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
            var mainSource = await ReadMainWindowSourceAsync(appRoot);
            var diagnosticsSource = await File.ReadAllTextAsync(Path.Combine(
                appRoot,
                "Diagnostics",
                "CompatibilityHealthService.cs"));
            var xaml = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml"));
            Ensure(mainSource.Contains("_runtime.PreflightAsync", StringComparison.Ordinal) &&
                   mainSource.Contains("RecordCompatibilityFailureAsync", StringComparison.Ordinal) &&
                   diagnosticsSource.Contains("CodexVersionChanged", StringComparison.Ordinal),
                "Theme application must preflight version changes and preserve actionable failure state.");
            Ensure(xaml.Contains("DiagnosticCodexVersionText", StringComparison.Ordinal) &&
                   xaml.Contains("DiagnosticLastFailureText", StringComparison.Ordinal) &&
                   xaml.Contains("兼容性健康", StringComparison.Ordinal),
                "The diagnostics page must expose the compatibility baseline and latest failure stage.");
        }
        finally
        {
            if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
        }
    }
}
