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
        Ensure(runtime.Contains(
                "await candidate.dispose({ preserveSharedAppearance });",
                StringComparison.Ordinal),
            "A compatible predecessor runtime must preserve the prepared successor shell during handoff.");
    }

    static async Task RuntimePreflightsAssetsBeforeReplacementAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtime = await ReadCompatibilityRuntimeSourceAsync(repositoryRoot);
        var scratchIndex = runtime.IndexOf(
            "let visualSettingsTarget = document.createElement(\"div\");",
            StringComparison.Ordinal);
        var prepareIndex = scratchIndex < 0
            ? -1
            : runtime.IndexOf(
                "await setVisualSettings(stagedVisualSettings || {}, stagedVisualImages || Object.create(null));",
                scratchIndex,
                StringComparison.Ordinal);
        var replacementIndex = prepareIndex < 0
            ? -1
            : runtime.IndexOf(
                "if (!(await disposeCompatibleRuntime())",
                prepareIndex,
                StringComparison.Ordinal);
        var attachIndex = replacementIndex < 0
            ? -1
            : runtime.IndexOf(
                "appendChild(style);",
                replacementIndex,
                StringComparison.Ordinal);
        var commitIndex = attachIndex < 0
            ? -1
            : runtime.IndexOf(
                "appearanceCommitted = true;",
                attachIndex,
                StringComparison.Ordinal);
        Ensure(scratchIndex >= 0 &&
               prepareIndex > scratchIndex &&
               replacementIndex > prepareIndex &&
               attachIndex > replacementIndex &&
               commitIndex > attachIndex,
            "The successor theme must prepare on a detached target before replacing and atomically committing its visible shell.");
        Ensure(!runtime.Contains("preloadAssetObjectUrl", StringComparison.Ordinal),
            "Theme switching must not synchronously decode every package asset before committing the visible shell.");
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
        Ensure(ThemeRuntime.ContractVersion == 4 &&
               adapterSource.Contains("TESSALUME_THEME_SCRIPT:", StringComparison.Ordinal) &&
               adapterSource.Contains("__TESSALUME_PAYLOAD_SCRIPT_BODY__", StringComparison.Ordinal) &&
               !adapterSource.Contains("eval(scriptText)", StringComparison.Ordinal),
            "The compatibility contract must inline theme lifecycle code without requiring CSP-blocked unsafe-eval, while retaining classified failures.");
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
        var sharedTemplate = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Compatibility",
            "theme-template-v1.css"));
        Ensure(payload.Contains("from-token-main-surface-primary", StringComparison.Ordinal),
            "The runtime must neutralize Codex's native bottom composer fade for every active theme.");
        Ensure(payload.Contains("bg-gradient-to-t", StringComparison.Ordinal) &&
               payload.Contains("from-surface", StringComparison.Ordinal),
            "The runtime must neutralize the current Codex composer fade utility names.");
        Ensure(payload.Contains("tessalume-composer-native-fade", StringComparison.Ordinal) &&
               payload.Contains("style.pointerEvents !== \"none\"", StringComparison.Ordinal) &&
               payload.Contains("style.backgroundImage.includes(\"linear-gradient\")", StringComparison.Ordinal) &&
               payload.Contains("carrierStyle.position === \"absolute\"", StringComparison.Ordinal) &&
               payload.Contains("carrierStyle.position === \"fixed\"", StringComparison.Ordinal) &&
               payload.Contains("const fadeSearchRoot = bottomCarrier.parentElement || bottomCarrier", StringComparison.Ordinal) &&
               payload.Contains("mark(nativeFadeCarrier, \"tessalume-composer-fade-carrier\")", StringComparison.Ordinal),
            "The runtime must semantically alias native composer fades in both legacy sticky and current absolute bottom carriers.");
        Ensure(payload.Contains("tessalume-runtime-compatibility-style", StringComparison.Ordinal) &&
               payload.Contains(".sticky.bottom-0.tessalume-composer-fade-carrier", StringComparison.Ordinal) &&
               payload.Contains("pointer-events:none!important", StringComparison.Ordinal) &&
               payload.Contains("z-index:0!important", StringComparison.Ordinal) &&
               payload.Contains("addCleanup(() => compatibilityStyle.remove())", StringComparison.Ordinal),
            "Compatibility packs must override an older executable's embedded composer CSS and restore it during disposal.");
        Ensure(payload.Contains("background:transparent!important", StringComparison.Ordinal),
            "The native composer fade override must remain transparent.");
        Ensure(payload.Contains("background-image:none!important", StringComparison.Ordinal),
            "The native composer fade image must be fully removed so it cannot obscure chat artwork.");
        Ensure(payload.Contains(
                ":has(.composer-surface-chrome) .sticky.bottom-0:has(.composer-surface-chrome)",
                StringComparison.Ordinal) &&
               !sharedTemplate.Contains(
                   ":has(.composer-surface-chrome) .sticky.bottom-0 {",
                   StringComparison.Ordinal),
            "Only a legacy sticky carrier that actually contains the composer may be elevated or receive pointer events.");
        Ensure(sharedTemplate.Contains(
                ":is(.sticky,.absolute,.fixed).bottom-0:has(.composer-surface-chrome)",
                StringComparison.Ordinal),
            "The compatibility template must neutralize the current full-width absolute composer fade before semantic decoration runs.");
        Ensure(sharedTemplate.Contains(
                ".thread-scroll-container:has(.composer-surface-chrome) .sticky.bottom-0",
                StringComparison.Ordinal),
            "The compatibility template must neutralize Codex's sibling sticky fade while preserving the centered composer surface.");
        Ensure(payload.Contains("min-height:64px!important", StringComparison.Ordinal),
            "The composer surface must keep a visible minimum hit area.");
        Ensure(sharedTemplate.Contains(
                   "[data-in-progress-fixed-content=\"true\"] > * > [class*=\"bg-gradient-to-t\"][class*=\"from-surface\"]",
                   StringComparison.Ordinal) &&
               sharedTemplate.Contains(
                   "[data-tessalume-progress-fade=\"true\"]",
                   StringComparison.Ordinal) &&
               sharedTemplate.Contains("background-image:none!important;", StringComparison.Ordinal) &&
               payload.Contains("decorateComposerProgressFade", StringComparison.Ordinal) &&
               payload.Contains("data-tessalume-progress-fade", StringComparison.Ordinal),
            "Native progress fades must stay transparent without depending on localized step text.");
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
               payload.Contains("is-settings", StringComparison.Ordinal) &&
               payload.Contains("settingsScrollChild?.parentElement", StringComparison.Ordinal) &&
               payload.Contains("markSurface(settingsSurface, \"settings\")", StringComparison.Ordinal) &&
               sharedTemplate.Contains(
                   "tessalume-is-settings [data-tessalume-surface=\"settings\"]",
                   StringComparison.Ordinal) &&
               sharedTemplate.Contains("background-color:transparent!important;", StringComparison.Ordinal),
            "Settings recognition must follow the stable scroll viewport and reveal the active chat artwork through only its native carrier.");
        Ensure(payload.Contains("new ResizeObserver", StringComparison.Ordinal),
            "Adaptive task rails must react to workspace and composer resizing.");
        Ensure(payload.Contains("syncStageGeometry", StringComparison.Ordinal) &&
               payload.Contains("startLayoutTracking", StringComparison.Ordinal),
            "The runtime must keep its fixed theme stage aligned throughout native layout transitions.");
        var runtime = await ReadCompatibilityRuntimeSourceAsync(repositoryRoot);
        Ensure(runtime.Contains("main[data-app-shell-main-surface=\"default\"]", StringComparison.Ordinal) &&
               runtime.Contains("main[class*=\"MainContentSurface\"]", StringComparison.Ordinal) &&
               runtime.IndexOf("main[data-app-shell-main-surface=\"default\"]", StringComparison.Ordinal) <
               runtime.IndexOf("main.main-surface", StringComparison.Ordinal),
            "The runtime must prefer Codex's visible content main over the hidden full-window main.");
        using (var profileDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
                   repositoryRoot,
                   "src",
                   "Tessalume.App",
                   "Compatibility",
                   "compatibility-profile-v3.json"))))
        {
            var mainSelectors = profileDocument.RootElement
                .GetProperty("selectors")
                .GetProperty("main")
                .EnumerateArray()
                .Select(selector => selector.GetString())
                .ToArray();
            var settingsScrollSelectors = profileDocument.RootElement
                .GetProperty("selectors")
                .GetProperty("settingsScrollChild")
                .EnumerateArray()
                .Select(selector => selector.GetString())
                .ToArray();
            Ensure(mainSelectors.Length >= 4 &&
                   mainSelectors[0] == "main[data-app-shell-main-surface=\"default\"]" &&
                   mainSelectors[1] == "main[class*=\"MainContentSurface\"]" &&
                   mainSelectors[2] == "main.main-surface" &&
                   mainSelectors[3] == "main",
                "The compatibility profile must keep visible semantic main selectors ahead of generic fallbacks.");
            Ensure(settingsScrollSelectors.Length >= 2 &&
                   settingsScrollSelectors[0] ==
                       ":scope > .scrollbar-stable.flex-1.overflow-y-auto.p-panel" &&
                   settingsScrollSelectors[1] ==
                       ".scrollbar-stable.flex-1.overflow-y-auto.p-panel",
                "Settings discovery must support both the legacy direct child and Codex's current nested scroll viewport.");
        }
        var acceptanceProbe = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.Core",
            "Runtime",
            "ThemeRuntimeAcceptanceProbe.cs"));
        Ensure(acceptanceProbe.Contains(
                "document.querySelector('main[data-app-shell-main-surface=\"default\"]') ||",
                StringComparison.Ordinal) &&
               acceptanceProbe.Contains(
                "document.querySelector('main[class*=\"MainContentSurface\"]') ||",
                StringComparison.Ordinal),
            "Runtime acceptance must preserve selector priority instead of using document order across a selector list.");
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

    static async Task RuntimePreservesWideAssistantContentAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runtime = await ReadCompatibilityRuntimeSourceAsync(repositoryRoot);
        var sharedCss = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Compatibility",
            ThemePayloadBuilder.SharedTemplateStyleFileName));

        Ensure(runtime.Contains(
                   "table,[role=\"table\"],pre,.katex-display",
                   StringComparison.Ordinal) &&
               runtime.Contains("data-tessalume-wide-content", StringComparison.Ordinal) &&
               runtime.Contains(
                   "surfaced.push([content, previousWideContent, wideContentAttribute])",
                   StringComparison.Ordinal),
            "The runtime must mark streamed structured assistant content and restore the marker on cleanup.");
        Ensure(sharedCss.Contains(
                   "[data-tessalume-wide-content=\"true\"]",
                   StringComparison.Ordinal) &&
               sharedCss.Contains(
                   ":has(:is(table,[role=\"table\"],pre,.katex-display))",
                   StringComparison.Ordinal) &&
               sharedCss.Contains("width:100%!important;", StringComparison.Ordinal) &&
               sharedCss.Contains("max-width:100%!important;", StringComparison.Ordinal) &&
               sharedCss.Contains("overflow:visible!important;", StringComparison.Ordinal),
            "Wide assistant structures must use the complete message lane without being clipped by theme frames.");
        Ensure(sharedCss.Contains("max-width:min(88%,820px)!important;", StringComparison.Ordinal),
            "Narrative assistant replies must retain the compact Template 1.0 frame.");
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
        Ensure(runtime.Contains("const syncTaskTitleWidth = () => {", StringComparison.Ordinal) &&
               runtime.Contains("--tessalume-task-title-primary-width", StringComparison.Ordinal) &&
               runtime.Contains("syncTaskTitleWidth();", StringComparison.Ordinal),
            "The canonical runtime must size the primary task title from the live header region.");
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
