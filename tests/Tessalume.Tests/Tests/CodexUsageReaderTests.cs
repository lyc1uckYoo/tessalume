internal static partial class TestSuite
{
    static Task CodexUsageReaderSupportsLegacyAndGroupedLimitsAsync()
    {
        using var legacy = JsonDocument.Parse(
            """
            {
              "result": {
                "rateLimits": {
                  "limitId": "codex",
                  "primary": { "usedPercent": 10, "windowDurationMins": 300, "resetsAt": 1787240989 },
                  "secondary": { "usedPercent": 25, "windowDurationMins": 10080, "resetsAt": 1787827789 }
                }
              }
            }
            """);
        var legacySnapshot = CodexUsageReader.ParseSnapshot(legacy.RootElement);
        Ensure(legacySnapshot is { Windows.Count: 2 } &&
               legacySnapshot.Windows[0] is
               { WindowDurationMinutes: 300, RemainingPercent: 90, LimitId: "codex" } &&
               legacySnapshot.Windows[1] is
               { WindowDurationMinutes: 10080, RemainingPercent: 75 },
            "The quota reader must preserve the legacy primary/secondary response shape.");

        using var grouped = JsonDocument.Parse(
            """
            {
              "result": {
                "rateLimits": {
                  "limitId": "codex",
                  "limitName": null,
                  "primary": { "usedPercent": 1, "windowDurationMins": 10080, "resetsAt": 1787815032 },
                  "secondary": null
                },
                "rateLimitsByLimitId": {
                  "codex_bengalfox": {
                    "limitId": "codex_bengalfox",
                    "limitName": "GPT-5.3-Codex-Spark",
                    "primary": { "usedPercent": 0, "windowDurationMins": 300, "resetsAt": 1787240989 },
                    "secondary": { "usedPercent": 4, "windowDurationMins": 10080, "resetsAt": 1787827789 }
                  },
                  "codex": {
                    "limitId": "codex",
                    "limitName": null,
                    "primary": { "usedPercent": 1, "windowDurationMins": 10080, "resetsAt": 1787815032 },
                    "secondary": null
                  }
                }
              }
            }
            """);
        var groupedSnapshot = CodexUsageReader.ParseSnapshot(grouped.RootElement);
        var canonicalWeek = groupedSnapshot?.Windows.FirstOrDefault(window =>
            window.WindowDurationMinutes == 10080 && window.LimitId == "codex");
        Ensure(groupedSnapshot is { Windows.Count: 1 } &&
               groupedSnapshot.Windows.All(window => window.LimitId == "codex") &&
               groupedSnapshot.Windows.All(window =>
                   !string.Equals(window.LimitName, "GPT-5.3-Codex-Spark", StringComparison.Ordinal)) &&
               canonicalWeek is { RemainingPercent: 99 } &&
               groupedSnapshot.Windows.All(window => window.WindowDurationMinutes != 300),
            "The quota reader must deduplicate the canonical group and never substitute a model-specific quota for a missing Codex window.");

        return Task.CompletedTask;
    }
}
