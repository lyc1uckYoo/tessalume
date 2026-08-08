internal static partial class TestSuite
{
    static async Task ThemeAssetsUseBlobUrlsAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var package = await LoadRepresentativePackageAsync(repositoryRoot);

        var payload = await BuildPayloadAsync(repositoryRoot, package);
        Ensure(payload.Contains("URL.createObjectURL", StringComparison.Ordinal),
            "Large theme assets must be converted to short blob URLs before entering CSS values.");
        Ensure(payload.Contains("URL.revokeObjectURL", StringComparison.Ordinal),
            "Theme blob URLs must be released when the theme is removed.");
        Ensure(!payload.Contains("setProperty(variable, `url(\"${dataUrl}\")`)", StringComparison.Ordinal),
            "Raw data URLs must not be assigned directly to CSS custom properties.");
    }

    static async Task RuntimePayloadStagesLargeAssetsSeparatelyAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var package = await LoadRepresentativePackageAsync(repositoryRoot);
        var builder = new ThemePayloadBuilder(new Dictionary<string, string>
        {
            [ThemePayloadBuilder.OpenRuntimeAdapterKey] = GetSourceRuntimeAssets(repositoryRoot).RuntimePath,
        });
        var payload = await builder.BuildRuntimeAsync(package);
        Ensure(payload.Contains("__TESSALUME_STAGED_ASSETS__", StringComparison.Ordinal),
            "Runtime payload must consume separately staged assets.");
        Ensure(payload.Length < 512 * 1024,
            $"Runtime payload unexpectedly embeds large assets ({payload.Length} characters).");
    }

    static async Task SkippedPetOverlaysRetainProcessedMarkerAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtime = await ReadCompatibilityRuntimeSourceAsync(repositoryRoot);
        var skippedBranchStart = runtime.IndexOf(
            "if (isPetOverlay && !allowPetOverlay)",
            StringComparison.Ordinal);
        Ensure(skippedBranchStart >= 0, "The pet-overlay skip branch is missing.");
        var skippedBranch = runtime.Substring(
            skippedBranchStart,
            Math.Min(600, runtime.Length - skippedBranchStart));
        Ensure(
            skippedBranch.Contains("window.__TESSALUME_THEME_ID__ = themeId", StringComparison.Ordinal),
            "Skipped pet overlays must be marked as processed to prevent repeated large-payload injection.");
    }

    static async Task RuntimeDisposesCompatiblePredecessorInjectionAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtime = await ReadCompatibilityRuntimeSourceAsync(repositoryRoot);
        Ensure(runtime.Contains("Object.getOwnPropertyNames(window)", StringComparison.Ordinal),
            "The runtime must discover an already injected compatible predecessor without retaining its brand key.");
        Ensure(runtime.Contains("typeof candidate.context.mountCanonicalTheme === \"function\"", StringComparison.Ordinal),
            "Predecessor discovery must require the canonical Tessalume runtime shape.");
        Ensure(runtime.Contains("await candidate.dispose()", StringComparison.Ordinal),
            "A compatible predecessor runtime must be disposed before the renamed runtime mounts.");
    }

    static async Task RuntimePreflightsAssetsBeforeReplacementAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtime = await ReadCompatibilityRuntimeSourceAsync(repositoryRoot);
        var preflightIndex = runtime.IndexOf(
            "assetAssignments.push([variable, createAssetObjectUrl(dataUrl)]);",
            StringComparison.Ordinal);
        var replacementIndex = preflightIndex < 0
            ? -1
            : runtime.IndexOf(
                "if (!(await disposeCompatibleRuntime())",
                preflightIndex,
                StringComparison.Ordinal);
        var attachIndex = replacementIndex < 0
            ? -1
            : runtime.IndexOf(
                "appendChild(style);",
                replacementIndex,
                StringComparison.Ordinal);
        Ensure(preflightIndex >= 0 && replacementIndex > preflightIndex && attachIndex > replacementIndex,
            "Theme assets must be decoded before the active runtime is disposed, then attached after replacement.");
        Ensure(runtime.Contains(
                "for (const objectUrl of assetObjectUrls) URL.revokeObjectURL(objectUrl);",
                StringComparison.Ordinal),
            "Failed theme preflight must release every prepared object URL.");
    }

    static async Task RuntimeFailuresAreClassifiedAndRolledBackAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtimeAdapterPath = GetSourceRuntimeAssets(repositoryRoot).RuntimePath;
        var runtimeSource = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.Core",
            "Runtime",
            "ThemeRuntime.cs"));
        var adapterSource = await File.ReadAllTextAsync(runtimeAdapterPath);
        Ensure(ThemeRuntime.ContractVersion == 3 &&
               adapterSource.Contains("TESSALUME_THEME_SCRIPT:", StringComparison.Ordinal),
            "The compatibility contract and theme-script failure marker must be explicit.");
        Ensure(runtimeSource.Contains("await CleanupTargetsAsync(targets);", StringComparison.Ordinal) &&
               runtimeSource.Contains("ThemeRuntimeFailureStage.ThemeScriptFailed", StringComparison.Ordinal),
            "Any partial multi-page application must roll every target back with a classified failure.");

        using var fixture = await ThemeFixture.CreateAsync();
        var package = (await new ThemePackageLoader().LoadAsync(fixture.Root)).Package
            ?? throw new InvalidOperationException("Runtime fixture did not load.");
        File.Delete(package.AssetPaths["hero"]);
        await using var runtime = new ThemeRuntime(
            new LoopbackCdpDiscovery(),
            new ThemePayloadBuilder(new Dictionary<string, string>
            {
                [ThemePayloadBuilder.OpenRuntimeAdapterKey] = runtimeAdapterPath,
            }));
        ThemeRuntimeException? resourceFailure = null;
        try
        {
            await runtime.PreflightAsync(CodexPackageLauncher.FindFreePort(), package);
        }
        catch (ThemeRuntimeException exception)
        {
            resourceFailure = exception;
        }
        Ensure(resourceFailure?.Stage == ThemeRuntimeFailureStage.ResourcePreflightFailed,
            "Unreadable local assets must fail before any Codex page is changed.");

        using var validFixture = await ThemeFixture.CreateAsync();
        var validPackage = (await new ThemePackageLoader().LoadAsync(validFixture.Root)).Package
            ?? throw new InvalidOperationException("Valid runtime fixture did not load.");
        ThemeRuntimeException? pageFailure = null;
        try
        {
            await runtime.PreflightAsync(CodexPackageLauncher.FindFreePort(), validPackage);
        }
        catch (ThemeRuntimeException exception)
        {
            pageFailure = exception;
        }
        Ensure(pageFailure?.Stage == ThemeRuntimeFailureStage.PageTargetsMissing,
            "A reachable test port without Codex pages must report the page-discovery stage.");
    }

    static async Task RestoreRemovesPredecessorRuntimeBrandsAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.Core",
            "Runtime",
            "ThemeRuntime.cs"));
        Ensure(source.Contains("window.__CODEX_THEME_STUDIO_RUNTIME__", StringComparison.Ordinal) &&
               source.Contains("delete window.__CODEX_THEME_STUDIO_THEME_ID__", StringComparison.Ordinal),
            "Restore must remove the predecessor runtime and marker after a brand migration.");
        Ensure(source.Contains("Object.getOwnPropertyNames(window)", StringComparison.Ordinal) &&
               source.Contains("candidate.context.mountCanonicalTheme", StringComparison.Ordinal),
            "Restore must also discover compatible runtime brands by canonical shape.");
        Ensure(source.Contains("await _applyLock.WaitAsync(cancellationToken);", StringComparison.Ordinal),
            "Restore must serialize with live theme application.");
    }

    static async Task RuntimeDiagnosticsUseTessalumeMarkersAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "tests",
            "Tessalume.Tests",
            "RuntimeProbeCommands.cs"));
        Ensure(source.Contains(
                "window.__TESSALUME_RUNTIME__ && document.documentElement.classList.contains('tessalume-theme-active')",
                StringComparison.Ordinal),
            "Runtime mode diagnostics must select the active Tessalume runtime.");
        var predecessorClassCheck =
            "document.documentElement.classList.contains('codex-" + "dream-skin')";
        Ensure(!source.Contains(predecessorClassCheck, StringComparison.Ordinal),
            "Runtime mode diagnostics must not depend on the predecessor skin class.");
    }


    static async Task RuntimeRemovesNativeComposerFadeAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var package = await LoadRepresentativePackageAsync(repositoryRoot);

        var payload = await BuildPayloadAsync(repositoryRoot, package);
        Ensure(payload.Contains("from-token-main-surface-primary", StringComparison.Ordinal),
            "The runtime must neutralize Codex's native bottom composer fade for every active theme.");
        Ensure(payload.Contains("background:transparent!important", StringComparison.Ordinal),
            "The native composer fade override must remain transparent.");
        Ensure(payload.Contains(":has(.composer-surface-chrome) .sticky.bottom-0", StringComparison.Ordinal),
            "The runtime must keep the sticky composer visible on Codex home layout changes.");
        Ensure(payload.Contains("min-height:64px!important", StringComparison.Ordinal),
            "The composer surface must keep a visible minimum hit area.");
        Ensure(payload.Contains("findComposerSurface", StringComparison.Ordinal) &&
               payload.Contains("[data-codex-composer=\"true\"]", StringComparison.Ordinal) &&
               payload.Contains("ComposerLayoutRoot", StringComparison.Ordinal) &&
               payload.Contains("mark(surface, \"composer-surface-chrome\")", StringComparison.Ordinal),
            "The runtime must alias Codex's current composer root so existing theme skins survive native DOM changes.");
        Ensure(payload.Contains("ComposerLayoutFooter", StringComparison.Ordinal) &&
               payload.Contains("mark(footer, \"_footer_\")", StringComparison.Ordinal),
            "The runtime must alias Codex's current composer footer so existing control skins survive native DOM changes.");
        Ensure(payload.Contains("tessalume-code-review-open", StringComparison.Ordinal),
            "The runtime must track Codex's code-review diff state.");
        Ensure(payload.Contains("data-tessalume-side-panel-overlay", StringComparison.Ordinal),
            "The runtime must hide theme overlays while the native sidebar is open.");
        Ensure(payload.Contains("data-tessalume-auto-hidden", StringComparison.Ordinal),
            "The runtime must fade theme widgets that do not fit beside the real chat content.");
        Ensure(payload.Contains("data-tessalume-left-rail", StringComparison.Ordinal) &&
               payload.Contains("data-tessalume-right-rail", StringComparison.Ordinal),
            "The runtime must expose geometry-based left and right task rail capacity.");
        Ensure(payload.Contains("settings-surface", StringComparison.Ordinal) &&
               payload.Contains("is-settings", StringComparison.Ordinal),
            "The runtime must expose a stable semantic state for settings surfaces.");
        Ensure(payload.Contains("new ResizeObserver", StringComparison.Ordinal),
            "Adaptive task rails must react to workspace and composer resizing.");
        Ensure(payload.Contains("syncStageGeometry", StringComparison.Ordinal) &&
               payload.Contains("startLayoutTracking", StringComparison.Ordinal),
            "The runtime must keep its fixed theme stage aligned throughout native layout transitions.");
        Ensure(payload.Contains("validateTemplateStructure", StringComparison.Ordinal) &&
               payload.Contains("data-tessalume-template-version", StringComparison.Ordinal),
            "The runtime must validate and expose the declared flagship template version.");
        Ensure(payload.Contains(
                "Template 1.0 requires one primary and one secondary task-right card",
                StringComparison.Ordinal) &&
               payload.Contains(
                "Template 1.0 sync-panel must hide with the secondary task card",
                StringComparison.Ordinal),
            "Template 1.0 must enforce its paired-card and sync-panel visibility contract.");
        var resizeObserverIndex = payload.IndexOf("layoutResizeObserver = new ResizeObserver", StringComparison.Ordinal);
        var immediateLayoutIndex = resizeObserverIndex < 0
            ? -1
            : payload.IndexOf("syncLiveLayout();", resizeObserverIndex, StringComparison.Ordinal);
        var delayedRepairIndex = resizeObserverIndex < 0
            ? -1
            : payload.IndexOf("schedule();", resizeObserverIndex, StringComparison.Ordinal);
        Ensure(resizeObserverIndex >= 0 &&
               immediateLayoutIndex > resizeObserverIndex &&
               delayedRepairIndex > immediateLayoutIndex,
            "ResizeObserver must align live geometry before scheduling the debounced DOM repair.");
        Ensure(!payload.Contains("min-height: 0 !important", StringComparison.Ordinal),
            "The runtime must not flatten themed home hero containers.");
        Ensure(payload.Contains("home-banners:empty", StringComparison.Ordinal),
            "The runtime must repair Codex's empty home-banners wrapper before themes inspect the home DOM.");
        Ensure(payload.Contains(".group\\\\/home-suggestions", StringComparison.Ordinal),
            "The runtime must hide Codex's native home suggestion cards for themed home pages.");
        Ensure(payload.Contains(".group\\\\/title", StringComparison.Ordinal),
            "The runtime must hide Codex's native home prompt title for themed home pages.");
    }

    static async Task DisplayPreferencesChangeEffectiveRuntimeStylesAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtime = await ReadCompatibilityRuntimeSourceAsync(repositoryRoot);
        var sharedCss = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Compatibility",
            ThemePayloadBuilder.SharedTemplateStyleFileName));

        Ensure(!sharedCss.Contains("animation-duration:.8s!important", StringComparison.Ordinal) &&
               runtime.Contains("MotionReductionFactor = .55", StringComparison.Ordinal) &&
               runtime.Contains("removeLegacyReducedMotionRule", StringComparison.Ordinal) &&
               runtime.Contains("sheet.deleteRule(index)", StringComparison.Ordinal) &&
               runtime.Contains("root.getAnimations({ subtree:true })", StringComparison.Ordinal) &&
               runtime.Contains("softenKeyframes", StringComparison.Ordinal) &&
               runtime.Contains("new DOMMatrixReadOnly", StringComparison.Ordinal) &&
               runtime.Contains("effect.setKeyframes(reduced ? softenKeyframes(frames) : frames)", StringComparison.Ordinal) &&
               runtime.Contains("animation.updatePlaybackRate(targetRate)", StringComparison.Ordinal),
            "Reduced motion must slow and soften the theme's real animations instead of forcing every animation to 0.8 seconds.");
        Ensure(runtime.Contains("collectTextTargets", StringComparison.Ordinal) &&
               runtime.Contains("getComputedStyle(node)", StringComparison.Ordinal) &&
               runtime.Contains("setManagedStyle(textStyles, node, \"font-size\"", StringComparison.Ordinal) &&
               runtime.Contains("setManagedStyle(textStyles, node, \"line-height\"", StringComparison.Ordinal),
            "Text scale must change computed native text sizes, including fixed-pixel descendants.");
        Ensure(runtime.Contains("[data-tessalume-message]", StringComparison.Ordinal) &&
               runtime.Contains("data-app-action-sidebar-thread-row", StringComparison.Ordinal) &&
               runtime.Contains("setManagedStyle(densityStyles, node, \"padding-top\"", StringComparison.Ordinal) &&
               runtime.Contains("setManagedStyle(densityStyles, node, \"height\"", StringComparison.Ordinal) &&
               runtime.Contains("setManagedStyle(densityStyles, node, \"min-height\"", StringComparison.Ordinal),
            "Density must affect both message rhythm and native sidebar rows.");
        Ensure(runtime.Contains("new MutationObserver(scheduleDisplayPreferences)", StringComparison.Ordinal) &&
               runtime.Contains("restoreManagedStyles(textStyles)", StringComparison.Ordinal) &&
               runtime.Contains("restoreManagedStyles(densityStyles)", StringComparison.Ordinal),
            "Display preferences must follow React DOM updates and restore every managed inline style on cleanup.");
        Ensure(sharedCss.Contains("data-tessalume-text-scale=\"large\"", StringComparison.Ordinal) &&
               sharedCss.Contains("data-tessalume-density=\"spacious\"", StringComparison.Ordinal),
            "Display preferences must retain immediate CSS fallbacks while semantic surfaces are discovered.");
    }

    static async Task RuntimeDecoratesTaskSurfacesBeforeDeferredRepairAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtime = await ReadCompatibilityRuntimeSourceAsync(repositoryRoot);
        const string criticalSignature = "const decorateTaskCriticalSurfaces = (mutations) => {";
        const string sharedSignature = "const decorateSharedSurfaces = (main, aside, home) => {";
        var criticalStart = runtime.IndexOf(criticalSignature, StringComparison.Ordinal);
        var sharedStart = runtime.IndexOf(sharedSignature, StringComparison.Ordinal);
        Ensure(criticalStart >= 0 && sharedStart > criticalStart,
            "The canonical runtime must expose a lightweight task-surface decorator.");
        var criticalBlock = runtime[criticalStart..sharedStart];

        foreach (var role in new[]
                 {
                     "markdown",
                     "message-assistant",
                     "message-user",
                     "chat-paper",
                     "task-header",
                     "task-title",
                 })
        {
            Ensure(criticalBlock.Contains($"roleClass(\"{role}\")", StringComparison.Ordinal) ||
                   runtime[..criticalStart].Contains($"roleClass(\"{role}\")", StringComparison.Ordinal),
                $"The immediate task decorator must cover the {role} semantic role.");
        }
        Ensure(criticalBlock.Contains("mutation.addedNodes", StringComparison.Ordinal) &&
               criticalBlock.Contains("mutation.target", StringComparison.Ordinal),
            "The immediate task decorator must cover inserted task trees and streamed child updates.");
        Ensure(runtime.Contains("[class*=\"_MarkdownRoot_\"]", StringComparison.Ordinal) &&
               runtime.Contains("/^msg_/i.test(unitId)", StringComparison.Ordinal),
            "The runtime must recognize current MarkdownRoot and msg_ assistant units while retaining legacy selectors.");
        Ensure(!criticalBlock.Contains("getBoundingClientRect", StringComparison.Ordinal) &&
               !criticalBlock.Contains("requestAnimationFrame", StringComparison.Ordinal) &&
               !criticalBlock.Contains("setTimeout", StringComparison.Ordinal) &&
               !criticalBlock.Contains("document.querySelectorAll('[class*=\"_markdownContent_\"]')", StringComparison.Ordinal),
            "The immediate task decorator must not perform layout work, defer itself, or scan every message.");

        const string mutationSignature = "const onDocumentMutations = (mutations) => {";
        const string cleanupSignature = "const cleanup = () => {";
        var mutationStart = runtime.IndexOf(mutationSignature, StringComparison.Ordinal);
        var cleanupStart = runtime.IndexOf(cleanupSignature, StringComparison.Ordinal);
        Ensure(mutationStart >= 0 && cleanupStart > mutationStart,
            "The runtime must use a dedicated document-mutation fast path.");
        var mutationBlock = runtime[mutationStart..cleanupStart];
        var immediateIndex = mutationBlock.IndexOf(
            "decorateTaskCriticalSurfaces(mutations);",
            StringComparison.Ordinal);
        var deferredIndex = mutationBlock.IndexOf("schedule(true);", StringComparison.Ordinal);
        Ensure(immediateIndex >= 0 && deferredIndex > immediateIndex,
            "Task surfaces must be decorated before the debounced full repair is scheduled.");
        Ensure(runtime.Contains(
                "context.observe(document.documentElement, { childList:true, subtree:true }, onDocumentMutations);",
                StringComparison.Ordinal),
            "The document observer must use the immediate task-surface callback.");
    }


    static async Task BundledAdapterBuildsPayloadAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var package = await LoadRepresentativePackageAsync(repositoryRoot);
        var payload = await new ThemePayloadBuilder(new Dictionary<string, string>
        {
            [ThemePayloadBuilder.OpenRuntimeAdapterKey] = GetSourceRuntimeAssets(repositoryRoot).RuntimePath,
        }).BuildAsync(package);
        Ensure(!payload.Contains("__DREAM_", StringComparison.Ordinal), "A dream placeholder remained in the payload.");
        Ensure(!payload.Contains("__TESSALUME_PAYLOAD_", StringComparison.Ordinal), "A Tessalume payload placeholder remained unresolved.");
        Ensure(package.Manifest.Config.TryGetValue("title", out var titleElement), "Theme title config is missing.");
        var title = titleElement.GetString();
        Ensure(!string.IsNullOrWhiteSpace(title), "Theme title config is empty.");
        var encodedTitle = JsonSerializer.Serialize(title).Trim('"');
        Ensure(package.IsAdvanced, "The representative theme should use the open advanced lifecycle.");
        Ensure(payload.Contains("registerTheme", StringComparison.Ordinal), "Advanced theme lifecycle is missing.");
        Ensure(payload.Contains(encodedTitle, StringComparison.Ordinal), "Theme config is missing.");
    }

}
