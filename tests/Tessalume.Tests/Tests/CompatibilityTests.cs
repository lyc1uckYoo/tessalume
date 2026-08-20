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
                PreferredDebugPort = CodexDebugPortPolicy.CodexPlusPlusPort,
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
                   restored.PreferredDebugPort == CodexDebugPortPolicy.CodexPlusPlusPort &&
                   restored.RuntimeContractVersion == ThemeRuntime.ContractVersion &&
                   restored.LastFailureStage == ThemeRuntimeFailureStage.ThemeScriptFailed &&
                   restored.CodexVersionAtLastApply == "1.2.3.4" &&
                   json.Contains("\"ThemeScriptFailed\"", StringComparison.Ordinal),
                "Compatibility baselines and failure stages must survive a restart in readable state JSON.");

            var automaticPorts = CodexDebugPortPolicy.BuildProbeOrder();
            var preferredPorts = CodexDebugPortPolicy.BuildProbeOrder(
                6123,
                9340,
                CodexDebugPortPolicy.CodexPlusPlusPort,
                6123,
                80);
            Ensure(
                automaticPorts[0] == CodexDebugPortPolicy.CodexPlusPlusPort &&
                automaticPorts[1] == CodexDebugPortPolicy.ManagedPortStart &&
                automaticPorts[^1] == CodexDebugPortPolicy.ManagedPortEnd &&
                automaticPorts.Count == 61,
                "Automatic Codex discovery must probe Codex++ port 9229 before Tessalume's managed range.");
            Ensure(
                preferredPorts.Take(3).SequenceEqual(
                    [6123, 9340, CodexDebugPortPolicy.CodexPlusPlusPort]) &&
                preferredPorts.Distinct().Count() == preferredPorts.Count &&
                !preferredPorts.Contains(80),
                "Explicit and last-known ports must lead the bounded probe order without duplicates or unsafe ports.");

            var listener = new System.Net.Sockets.TcpListener(
                IPAddress.Loopback,
                CodexDebugPortPolicy.CodexPlusPlusPort);
            try
            {
                listener.Start();
            }
            catch (System.Net.Sockets.SocketException)
            {
                listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                listener.Start();
            }
            System.Net.Sockets.TcpListener? slowListener = null;
            CancellationTokenSource? slowCancellation = null;
            Task slowResponseTask = Task.CompletedTask;
            try
            {
                var probePort = ((IPEndPoint)listener.LocalEndpoint).Port;
                var responseTask = ServeCodexDiscoveryResponseAsync(listener, probePort);
                slowListener = StartSlowManagedDebugEndpoint(probePort);
                slowCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                slowResponseTask = slowListener is null
                    ? Task.CompletedTask
                    : HoldDebugEndpointOpenAsync(slowListener, slowCancellation.Token);
                using var discovery = new LoopbackCdpDiscovery();
                var launcher = new CodexPackageLauncher(discovery);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var detectedPort = await launcher.FindRunningDebugPortAsync([probePort]);
                stopwatch.Stop();
                await responseTask;
                Ensure(
                    detectedPort == probePort && stopwatch.Elapsed < TimeSpan.FromMilliseconds(800),
                    "A known healthy Codex port must return immediately without waiting for slow managed-range probes.");
            }
            finally
            {
                slowCancellation?.Cancel();
                slowListener?.Stop();
                await slowResponseTask;
                slowCancellation?.Dispose();
                listener.Stop();
            }

            var repositoryRoot = FindRepositoryRoot();
            var appRoot = Path.Combine(repositoryRoot, "src", "Tessalume.App");
            var mainSource = await ReadMainWindowSourceAsync(appRoot);
            var resolverStart = mainSource.IndexOf(
                "private async Task<int?> ResolveThemeRuntimePortAsync",
                StringComparison.Ordinal);
            var resolverEnd = mainSource.IndexOf(
                "private async Task<CreatorRuntimeActionResult>",
                resolverStart,
                StringComparison.Ordinal);
            var resolverSource = resolverStart >= 0 && resolverEnd > resolverStart
                ? mainSource[resolverStart..resolverEnd]
                : string.Empty;
            var diagnosticsSource = await File.ReadAllTextAsync(Path.Combine(
                appRoot,
                "Features",
                "Diagnostics",
                "CompatibilityHealthService.cs"));
            var xaml = await File.ReadAllTextAsync(Path.Combine(
                appRoot,
                "Features",
                "Diagnostics",
                "DiagnosticsView.xaml"));
            Ensure(mainSource.Contains("_runtime.PreflightAsync", StringComparison.Ordinal) &&
                   mainSource.Contains("RecordCompatibilityFailureAsync", StringComparison.Ordinal) &&
                   mainSource.Contains("state?.PreferredDebugPort", StringComparison.Ordinal) &&
                   mainSource.Contains("FindRunningDebugPortAsync", StringComparison.Ordinal) &&
                   diagnosticsSource.Contains("CodexVersionChanged", StringComparison.Ordinal),
                "Theme application must preflight version changes and preserve actionable failure state.");
            Ensure(
                resolverSource.Contains("_activePort is", StringComparison.Ordinal) &&
                resolverSource.IndexOf("_activePort is", StringComparison.Ordinal) <
                resolverSource.IndexOf("_stateStore.LoadAsync", StringComparison.Ordinal) &&
                resolverSource.IndexOf("IsDebugPortReadyAsync", StringComparison.Ordinal) <
                resolverSource.IndexOf("FindRunningDebugPortAsync", StringComparison.Ordinal),
                "Theme switching must validate the cached active port before loading state or scanning fallback ports.");
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

    private static System.Net.Sockets.TcpListener? StartSlowManagedDebugEndpoint(int excludedPort)
    {
        for (var port = CodexDebugPortPolicy.ManagedPortEnd;
             port >= CodexDebugPortPolicy.ManagedPortStart;
             port--)
        {
            if (port == excludedPort) continue;
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.Start();
                return listener;
            }
            catch (System.Net.Sockets.SocketException)
            {
                listener.Stop();
            }
        }

        return null;
    }

    private static async Task HoldDebugEndpointOpenAsync(
        System.Net.Sockets.TcpListener listener,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (System.Net.Sockets.SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task ServeCodexDiscoveryResponseAsync(
        System.Net.Sockets.TcpListener listener,
        int port)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token)))
        {
        }

        var body =
            $"[{{\"id\":\"codex-page\",\"type\":\"page\",\"url\":\"app://codex/\"," +
            $"\"webSocketDebuggerUrl\":\"ws://127.0.0.1:{port}/devtools/page/codex-page\"}}]";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var headers = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(headers, timeout.Token);
        await stream.WriteAsync(bodyBytes, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }

    static async Task CompatibilityPacksInstallValidateAndRollBackAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-compatibility-pack-{Guid.NewGuid():N}");
        var builtIn = Path.Combine(root, "Compatibility");
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(builtIn);
        Directory.CreateDirectory(data);
        try
        {
            var source = Path.Combine(repositoryRoot, "src", "Tessalume.App", "Compatibility");
            var sourceAssets = GetSourceRuntimeAssets(repositoryRoot);
            File.Copy(sourceAssets.RuntimePath, Path.Combine(builtIn, CompatibilityPackStore.RuntimeFileName));
            File.Copy(sourceAssets.CompatibilityProfilePath, Path.Combine(builtIn, CompatibilityPackStore.ProfileFileName));
            File.Copy(sourceAssets.SharedTemplateStylePath, Path.Combine(builtIn, ThemePayloadBuilder.SharedTemplateStyleFileName));

            var legacyBaselineArchive = await CreateCompatibilityArchiveAsync(
                root,
                sourceAssets,
                new Version(3, 0, 2),
                runtimeContractVersion: 3);
            var legacyPackDirectory = Path.Combine(data, "compatibility", "packs", "3.0.2");
            Directory.CreateDirectory(legacyPackDirectory);
            ZipFile.ExtractToDirectory(legacyBaselineArchive, legacyPackDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(data, "compatibility", "state.json"),
                """
                {
                  "schemaVersion": 1,
                  "activePackVersion": "3.0.2",
                  "previousPackVersion": null
                }
                """);

            var localImagePath = Path.Combine(
                data,
                "personalization",
                "images",
                "sidebar-light.png");
            const string storedImagePath = "personalization/images/sidebar-light.png";
            Directory.CreateDirectory(Path.GetDirectoryName(localImagePath)!);
            var localImageBytes = new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                0x54, 0x65, 0x73, 0x73, 0x61, 0x6C, 0x75, 0x6D, 0x65,
            };
            await File.WriteAllBytesAsync(localImagePath, localImageBytes);
            var preferencesPath = Path.Combine(data, "ui-settings.json");
            var preferencesBytes = JsonSerializer.SerializeToUtf8Bytes(
                new UiPreferences
                {
                    SchemaVersion = UiPreferences.CurrentSchemaVersion,
                    ThemeVisualOverrides = new Dictionary<string, ThemeVisualSettingsOverride>
                    {
                        ["legacy.theme"] = new ThemeVisualSettingsOverride
                        {
                            Light = new ThemeVisualModeSettingsOverride
                            {
                                Sidebar = new ThemeArtworkOverride
                                {
                                    ImageSourceMode = ThemeArtworkImageSourceMode.Custom,
                                    CustomImagePath = storedImagePath,
                                    Brightness = 87,
                                    CompositionMode = ThemeArtworkCompositionMode.Legacy,
                                    LegacyOffsetX = 21,
                                    LegacyOffsetY = -14,
                                    LegacyZoom = 128,
                                },
                            },
                            Dark = new ThemeVisualModeSettingsOverride
                            {
                                Chat = new ThemeArtworkOverride
                                {
                                    Contrast = 112,
                                    OverlayColor = "#28304A",
                                    OverlayOpacity = 18,
                                },
                            },
                        },
                    },
                });
            await File.WriteAllBytesAsync(preferencesPath, preferencesBytes);

            var studioStatePath = Path.Combine(data, "state.json");
            await new StudioStateStore(data).SaveAsync(new StudioState
            {
                Port = 9340,
                ThemeId = "legacy.theme",
                Enabled = true,
                RuntimeContractVersion = 3,
                CompatibilityPackVersionAtLastApply = "3.0.2",
            });
            var studioStateBytes = await File.ReadAllBytesAsync(studioStatePath);

            var store = new CompatibilityPackStore(
                builtIn,
                data,
                new Version(1, 4, 1),
                ThemeRuntime.ContractVersion);
            var baseline = store.Resolve();
            Ensure(baseline.IsBuiltIn && baseline.PackVersion == new Version(3, 0, 6),
                "Contract 3 pack 3.0.2 must be rejected after upgrading to contract 4 and the embedded 3.0.6 baseline.");
            using (var repairedState = JsonDocument.Parse(await File.ReadAllBytesAsync(
                       Path.Combine(data, "compatibility", "state.json"))))
            {
                Ensure(
                    repairedState.RootElement.GetProperty("activePackVersion").ValueKind == JsonValueKind.Null &&
                    repairedState.RootElement.GetProperty("previousPackVersion").ValueKind == JsonValueKind.Null,
                    "Rejecting an active legacy-contract pack must clear its compatibility selection.");
            }
            Ensure(
                (await File.ReadAllBytesAsync(preferencesPath)).SequenceEqual(preferencesBytes) &&
                (await File.ReadAllBytesAsync(localImagePath)).SequenceEqual(localImageBytes) &&
                (await File.ReadAllBytesAsync(studioStatePath)).SequenceEqual(studioStateBytes),
                "Compatibility fallback must not rewrite user preferences, artwork parameters, local image data, or application state.");
            using (var preferencesStore = new UiPreferencesStore(data))
            {
                var restoredPreferences = preferencesStore.Load();
                var restoredVisualSettings = restoredPreferences.ThemeVisualOverrides["legacy.theme"];
                var restoredSidebar = restoredVisualSettings.Light?.Sidebar
                    ?? throw new InvalidOperationException("The sparse sidebar override was lost.");
                var restoredChat = restoredVisualSettings.Dark?.Chat
                    ?? throw new InvalidOperationException("The sparse chat override was lost.");
                var resolvedImagePath = new Tessalume.App.Features.Personalization.PersonalImageStore(data)
                    .ResolvePath(restoredSidebar.CustomImagePath);
                Ensure(
                    restoredSidebar.CustomImagePath == storedImagePath &&
                    resolvedImagePath == localImagePath &&
                    restoredSidebar.Brightness == 87 &&
                    restoredSidebar.LegacyOffsetX == 21 &&
                    restoredSidebar.LegacyOffsetY == -14 &&
                    restoredSidebar.LegacyZoom == 128 &&
                    restoredChat.Contrast == 112 &&
                    restoredChat.OverlayColor == "#28304A" &&
                    restoredChat.OverlayOpacity == 18 &&
                    (await File.ReadAllBytesAsync(preferencesPath)).SequenceEqual(preferencesBytes),
                    "The contract upgrade fallback must preserve typed theme parameters and the local image reference without migrating preferences.");
            }
            var restoredStudioState = await new StudioStateStore(data).LoadAsync();
            Ensure(
                restoredStudioState is
                {
                    RuntimeContractVersion: 3,
                    CompatibilityPackVersionAtLastApply: "3.0.2",
                    ThemeId: "legacy.theme",
                },
                "Compatibility selection repair must not rewrite the application's last-applied runtime state.");

            var staleArchive = await CreateCompatibilityArchiveAsync(
                root,
                sourceAssets,
                new Version(3, 0, 1));

            var legacyContractArchive = await CreateCompatibilityArchiveAsync(
                root,
                sourceAssets,
                new Version(3, 0, 9),
                runtimeContractVersion: 3);
            var legacyContractHash = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(legacyContractArchive)));
            var legacyContractRejected = false;
            try
            {
                await store.InstallAsync(legacyContractArchive, legacyContractHash);
            }
            catch (InvalidDataException)
            {
                legacyContractRejected = true;
            }
            Ensure(legacyContractRejected && store.Resolve().IsBuiltIn,
                "A newer pack using the legacy image protocol contract must be rejected in favor of the built-in runtime.");

            var staleHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(staleArchive)));
            var staleRejected = false;
            try
            {
                await store.InstallAsync(staleArchive, staleHash);
            }
            catch (InvalidDataException)
            {
                staleRejected = true;
            }
            Ensure(staleRejected && store.Resolve().IsBuiltIn,
                "A stale pack must neither override nor be installed over a newer embedded compatibility baseline.");
            var stalePackDirectory = Path.Combine(data, "compatibility", "packs", "3.0.1");
            Directory.CreateDirectory(stalePackDirectory);
            ZipFile.ExtractToDirectory(staleArchive, stalePackDirectory);

            var firstArchive = await CreateCompatibilityArchiveAsync(root, sourceAssets, new Version(3, 0, 7));
            var firstHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(firstArchive)));
            var firstInstall = await store.InstallAsync(firstArchive, firstHash);
            Ensure(firstInstall.Changed && !firstInstall.ActivePack.IsBuiltIn &&
                   firstInstall.ActivePack.PackVersion == new Version(3, 0, 7),
                "A fully verified official compatibility pack must become active without replacing the executable.");

            await File.WriteAllTextAsync(
                Path.Combine(data, "compatibility", "state.json"),
                """
                {
                  "schemaVersion": 1,
                  "activePackVersion": "3.0.7",
                  "previousPackVersion": "3.0.1"
                }
                """);
            var staleRollback = store.Rollback();
            Ensure(staleRollback.IsBuiltIn && staleRollback.PackVersion == baseline.PackVersion,
                "Rollback must prefer the embedded baseline over an older previous pack.");

            firstInstall = await store.InstallAsync(firstArchive, firstHash);
            Ensure(firstInstall.Changed && firstInstall.ActivePack.PackVersion == new Version(3, 0, 7),
                "A newer verified pack must remain installable after falling back to the embedded baseline.");

            var secondArchive = await CreateCompatibilityArchiveAsync(root, sourceAssets, new Version(3, 0, 8));
            var secondHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(secondArchive)));
            var secondInstall = await store.InstallAsync(secondArchive, secondHash);
            Ensure(secondInstall.ActivePack.PackVersion == new Version(3, 0, 8) &&
                   secondInstall.PreviousPack.PackVersion == new Version(3, 0, 7),
                "Installing a newer compatibility pack must preserve the last known-good pack for rollback.");

            var rolledBack = store.Rollback();
            Ensure(!rolledBack.IsBuiltIn && rolledBack.PackVersion == new Version(3, 0, 7),
                "A failed active compatibility pack must roll back atomically to the previous verified pack.");

            await File.AppendAllTextAsync(rolledBack.RuntimeAssets.RuntimePath, "\n// corrupted by fixture");
            var repaired = store.Resolve();
            Ensure(repaired.IsBuiltIn && repaired.PackVersion == baseline.PackVersion,
                "A damaged installed pack must never be loaded and must fall back to the embedded baseline.");

            var rejected = false;
            try
            {
                await store.InstallAsync(secondArchive, new string('0', 64));
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Ensure(rejected && store.Resolve().IsBuiltIn,
                "An archive with a mismatched outer SHA-256 must be rejected without changing the active baseline.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreateCompatibilityArchiveAsync(
        string root,
        ThemeRuntimeAssets sourceAssets,
        Version version,
        int? runtimeContractVersion = null)
    {
        var contractVersion = runtimeContractVersion ?? ThemeRuntime.ContractVersion;
        var fixture = Path.Combine(root, $"pack-source-{version}");
        Directory.CreateDirectory(fixture);
        var runtimePath = Path.Combine(fixture, CompatibilityPackStore.RuntimeFileName);
        var profilePath = Path.Combine(fixture, CompatibilityPackStore.ProfileFileName);
        var runtime = await File.ReadAllTextAsync(sourceAssets.RuntimePath);
        await File.WriteAllTextAsync(runtimePath, $"{runtime}\n// compatibility fixture {version}");
        var profile = await File.ReadAllTextAsync(sourceAssets.CompatibilityProfilePath);
        using (var profileDocument = JsonDocument.Parse(profile))
        {
            var currentProfileVersion = profileDocument.RootElement
                .GetProperty("profileVersion")
                .GetString() ?? throw new InvalidDataException("Compatibility fixture profile version is missing.");
            profile = profile.Replace(
                $"\"profileVersion\": \"{currentProfileVersion}\"",
                $"\"profileVersion\": \"{version}\"",
                StringComparison.Ordinal);
            var currentContractVersion = profileDocument.RootElement
                .GetProperty("runtimeContractVersion")
                .GetInt32();
            profile = profile.Replace(
                $"\"runtimeContractVersion\": {currentContractVersion}",
                $"\"runtimeContractVersion\": {contractVersion}",
                StringComparison.Ordinal);
        }
        await File.WriteAllTextAsync(profilePath, profile);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CompatibilityPackStore.RuntimeFileName] = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(runtimePath))),
            [CompatibilityPackStore.ProfileFileName] = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(profilePath))),
        };
        var manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = CompatibilityPackStore.ManifestSchemaVersion,
            packVersion = version.ToString(),
            minimumAppVersion = "1.4.1",
            runtimeContractVersion = contractVersion,
            runtime = CompatibilityPackStore.RuntimeFileName,
            profile = CompatibilityPackStore.ProfileFileName,
            files,
        });
        await File.WriteAllTextAsync(
            Path.Combine(fixture, CompatibilityPackStore.ManifestFileName),
            manifest);

        var archivePath = Path.Combine(root, $"compatibility-{version}.zip");
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var fileName in new[]
                 {
                     CompatibilityPackStore.ManifestFileName,
                     CompatibilityPackStore.RuntimeFileName,
                     CompatibilityPackStore.ProfileFileName,
                 })
        {
            archive.CreateEntryFromFile(Path.Combine(fixture, fileName), fileName, CompressionLevel.Fastest);
        }
        return archivePath;
    }
}
