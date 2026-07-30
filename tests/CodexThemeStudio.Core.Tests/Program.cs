using System.Text.Json;
using CodexThemeStudio.Core.Themes;
using CodexThemeStudio.Core.Runtime;
using CodexThemeStudio.Core.Security;

if (args is ["--probe", var portText] && int.TryParse(portText, out var probePort))
{
    return await ProbeRuntimeAsync(probePort);
}

if (args is ["--remove", var removePortText] && int.TryParse(removePortText, out var removePort))
{
    return await RemoveRuntimeAsync(removePort);
}

if (args is ["--apply", var applyPortText] && int.TryParse(applyPortText, out var applyPort))
{
    return await ApplyRuntimeAsync(applyPort);
}

if (args is ["--theme-modes", var modePortText] && int.TryParse(modePortText, out var modePort))
{
    return await ProbeThemeModesAsync(modePort);
}

if (args is ["--toggle-color-scheme", var togglePortText] && int.TryParse(togglePortText, out var togglePort))
{
    return await ToggleColorSchemeAsync(togglePort);
}

if (args is ["--appearance-state", var appearancePortText] && int.TryParse(appearancePortText, out var appearancePort))
{
    return await ProbeAppearanceStateAsync(appearancePort);
}

if (args is ["--appearance-bundles", var bundlePortText] && int.TryParse(bundlePortText, out var bundlePort))
{
    return await ProbeAppearanceBundlesAsync(bundlePort);
}

if (args is ["--query-clients", var queryPortText] && int.TryParse(queryPortText, out var queryPort))
{
    return await ProbeQueryClientsAsync(queryPort);
}

if (args is ["--apply-package", var packagePortText, var packagePath] &&
    int.TryParse(packagePortText, out var packagePort))
{
    return await ApplyPackageRuntimeAsync(packagePort, packagePath);
}

if (args is ["--trust-package", var trustDataPath, var trustedPackagePath])
{
    return await TrustPackageAsync(trustDataPath, trustedPackagePath);
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("valid package loads", ValidPackageLoadsAsync),
    ("path traversal is rejected", PathTraversalIsRejectedAsync),
    ("remote CSS is rejected", RemoteCssIsRejectedAsync),
    ("catalog keeps invalid packages visible", CatalogIncludesInvalidPackagesAsync),
    ("representative open theme loads", RepresentativeOpenThemeLoadsAsync),
    ("published theme library loads and builds", PublishedThemeLibraryLoadsAndBuildsAsync),
    ("theme assets use disposable blob URLs", ThemeAssetsUseBlobUrlsAsync),
    ("runtime payload stages large assets separately", RuntimePayloadStagesLargeAssetsSeparatelyAsync),
    ("skipped pet overlays retain the processed marker", SkippedPetOverlaysRetainProcessedMarkerAsync),
    ("runtime removes native composer fade", RuntimeRemovesNativeComposerFadeAsync),
    ("runtime decorates task surfaces before deferred repair", RuntimeDecoratesTaskSurfacesBeforeDeferredRepairAsync),
    ("published themes use canonical injection contract", PublishedThemesUseCanonicalInjectionContractAsync),
    ("flagship template v1 freezes shared structure", FlagshipTemplateV1FreezesSharedStructureAsync),
    ("local importer copies a validated package", LocalImporterCopiesPackageAsync),
    ("bundled adapter builds a complete payload", BundledAdapterBuildsPayloadAsync),
    ("open advanced template loads and fingerprints", OpenAdvancedTemplateLoadsAndFingerprintsAsync),
    ("advanced import keeps script and trust follows fingerprint", AdvancedImportAndTrustFollowFingerprintAsync),
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception exception)
    {
        failures.Add(name);
        Console.Error.WriteLine($"FAIL  {name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} checks passed.");
return failures.Count == 0 ? 0 : 1;

static async Task ValidPackageLoadsAsync()
{
    using var fixture = await ThemeFixture.CreateAsync();
    var result = await new ThemePackageLoader().LoadAsync(fixture.Root);
    Ensure(result.Validation.IsValid, FormatIssues(result.Validation));
    var package = result.Package ?? throw new InvalidOperationException("Expected the sample theme to load.");
    Ensure(package.Manifest.Id == "sample.theme", "Expected the sample theme to load.");
    Ensure(package.AssetPaths.ContainsKey("hero"), "Expected hero asset mapping.");
}

static async Task PathTraversalIsRejectedAsync()
{
    using var fixture = await ThemeFixture.CreateAsync(cssPath: "../outside.css");
    var outsidePath = Path.Combine(Path.GetDirectoryName(fixture.Root)!, "outside.css");
    try
    {
        await File.WriteAllTextAsync(outsidePath, "body {}");
        var result = await new ThemePackageLoader().LoadAsync(fixture.Root);
        Ensure(!result.Validation.IsValid, "Traversal package must be invalid.");
        Ensure(result.Validation.Issues.Any(issue => issue.Code == "path.outside-package"), "Traversal issue was not reported.");
    }
    finally
    {
        File.Delete(outsidePath);
    }
}

static async Task RemoteCssIsRejectedAsync()
{
    using var fixture = await ThemeFixture.CreateAsync(css: "@import url('https://example.com/theme.css');");
    var result = await new ThemePackageLoader().LoadAsync(fixture.Root);
    Ensure(!result.Validation.IsValid, "Remote CSS package must be invalid.");
    Ensure(result.Validation.Issues.Any(issue => issue.Code == "css.import.forbidden"), "Remote import issue was not reported.");
}

static async Task CatalogIncludesInvalidPackagesAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"cts-catalog-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        using var valid = await ThemeFixture.CreateAsync(Path.Combine(root, "valid"));
        Directory.CreateDirectory(Path.Combine(root, "broken"));
        var catalog = await new ThemeCatalog(new ThemePackageLoader()).ScanAsync(root);
        Ensure(catalog.Count == 2, "Catalog should report both valid and invalid directories.");
        Ensure(catalog.Count(item => item.Validation.IsValid) == 1, "Expected one valid package.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task RepresentativeOpenThemeLoadsAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var package = await LoadRepresentativePackageAsync(repositoryRoot);
    Ensure(package.IsAdvanced, "The representative theme must use the open advanced lifecycle.");
}

static async Task PublishedThemeLibraryLoadsAndBuildsAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var themesRoot = Path.Combine(repositoryRoot, "themes");
    if (!Directory.Exists(themesRoot))
    {
        return;
    }

    var discoveredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var catalog = await new ThemeCatalog(new ThemePackageLoader()).ScanAsync(themesRoot);
    foreach (var item in catalog)
    {
        Ensure(item.Validation.IsValid, $"{Path.GetFileName(item.Directory)}: {FormatIssues(item.Validation)}");
        var package = item.Package
            ?? throw new InvalidOperationException($"{Path.GetFileName(item.Directory)} did not load.");
        Ensure(discoveredIds.Add(package.Manifest.Id), $"Duplicate bundled theme id: {package.Manifest.Id}");
        var payload = await BuildPayloadAsync(repositoryRoot, package);
        Ensure(payload.Contains(package.Manifest.Id, StringComparison.Ordinal),
            $"Payload is missing {package.Manifest.Id} metadata.");
    }
}

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
        [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
            repositoryRoot,
            "src",
            "CodexThemeStudio.App",
            "Compatibility",
            "theme-runtime-v2.js"),
    });
    var payload = await builder.BuildRuntimeAsync(package);
    Ensure(payload.Contains("__CODEX_THEME_STUDIO_STAGED_ASSETS__", StringComparison.Ordinal),
        "Runtime payload must consume separately staged assets.");
    Ensure(payload.Length < 512 * 1024,
        $"Runtime payload unexpectedly embeds large assets ({payload.Length} characters).");
}

static async Task SkippedPetOverlaysRetainProcessedMarkerAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var runtimePath = Path.Combine(
        repositoryRoot,
        "src",
        "CodexThemeStudio.App",
        "Compatibility",
        "theme-runtime-v2.js");
    var runtime = await File.ReadAllTextAsync(runtimePath);
    var skippedBranchStart = runtime.IndexOf(
        "if (isPetOverlay && !allowPetOverlay)",
        StringComparison.Ordinal);
    Ensure(skippedBranchStart >= 0, "The pet-overlay skip branch is missing.");
    var skippedBranch = runtime.Substring(
        skippedBranchStart,
        Math.Min(600, runtime.Length - skippedBranchStart));
    Ensure(
        skippedBranch.Contains("window.__CODEX_THEME_STUDIO_THEME_ID__ = themeId", StringComparison.Ordinal),
        "Skipped pet overlays must be marked as processed to prevent repeated large-payload injection.");
}

static async Task RuntimeRemovesNativeComposerFadeAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var package = await LoadRepresentativePackageAsync(repositoryRoot);

    var payload = await BuildPayloadAsync(repositoryRoot, package);
    Ensure(payload.Contains("from-token-main-surface-primary", StringComparison.Ordinal),
        "The runtime must neutralize Codex's native bottom composer fade for every active theme.");
    Ensure(payload.Contains("background: transparent !important", StringComparison.Ordinal),
        "The native composer fade override must remain transparent.");
    Ensure(payload.Contains(":has(.composer-surface-chrome) .sticky.bottom-0", StringComparison.Ordinal),
        "The runtime must keep the sticky composer visible on Codex home layout changes.");
    Ensure(payload.Contains("min-height: 64px !important", StringComparison.Ordinal),
        "The composer surface must keep a visible minimum hit area.");
    Ensure(payload.Contains("cts-code-review-open", StringComparison.Ordinal),
        "The runtime must track Codex's code-review diff state.");
    Ensure(payload.Contains("data-cts-side-panel-overlay", StringComparison.Ordinal),
        "The runtime must hide theme overlays while the native sidebar is open.");
    Ensure(payload.Contains("data-cts-auto-hidden", StringComparison.Ordinal),
        "The runtime must fade theme widgets that do not fit beside the real chat content.");
    Ensure(payload.Contains("data-cts-left-rail", StringComparison.Ordinal) &&
           payload.Contains("data-cts-right-rail", StringComparison.Ordinal),
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
           payload.Contains("data-cts-template-version", StringComparison.Ordinal),
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

static async Task RuntimeDecoratesTaskSurfacesBeforeDeferredRepairAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var runtimePath = Path.Combine(
        repositoryRoot,
        "src",
        "CodexThemeStudio.App",
        "Compatibility",
        "theme-runtime-v2.js");
    var runtime = await File.ReadAllTextAsync(runtimePath);

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

static async Task PublishedThemesUseCanonicalInjectionContractAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var runtimePath = Path.Combine(
        repositoryRoot,
        "src",
        "CodexThemeStudio.App",
        "Compatibility",
        "theme-runtime-v2.js");
    var runtime = await File.ReadAllTextAsync(runtimePath);
    Ensure(runtime.Contains("mountCanonicalTheme", StringComparison.Ordinal),
        "The open runtime must expose the canonical theme host.");
    Ensure(runtime.Contains("syncRouteState();", StringComparison.Ordinal),
        "The canonical host must synchronize route state before its debounced repair.");

    var themes = new[]
    {
        (Directory: "xin.moonfox-sovereign", Namespace: "xmf"),
        (Directory: "aemeath-star-voyage", Namespace: "ae3"),
        (Directory: "danya.bubble-void-duality", Namespace: "dny"),
    };
    foreach (var (directory, themeNamespace) in themes)
    {
        var themeRoot = Path.Combine(repositoryRoot, "themes", directory);
        var script = await File.ReadAllTextAsync(Path.Combine(themeRoot, "theme.js"));
        var css = await File.ReadAllTextAsync(Path.Combine(themeRoot, "theme.css"));
        Ensure(script.Contains("context.mountCanonicalTheme(", StringComparison.Ordinal),
            $"{directory} must use the canonical theme host.");
        Ensure(!script.Contains("context.observe(", StringComparison.Ordinal) &&
               !script.Contains("MutationObserver", StringComparison.Ordinal),
            $"{directory} must not own route observers.");
        Ensure(script.Contains("data-theme-stage", StringComparison.Ordinal),
            $"{directory} must expose the canonical stage role.");
        foreach (var role in new[] { "hero", "identity", "task-left", "task-right", "memory", "composer-accessory" })
        {
            Ensure(script.Contains($"data-theme-role=\"{role}\"", StringComparison.Ordinal),
                $"{directory} is missing canonical role {role}.");
        }
        Ensure(css.Contains("chat-paper::before{content:none!important}", StringComparison.Ordinal),
            $"{directory} must not paint chat art on a replaceable chat-paper pseudo-element.");
        Ensure(css.Contains("-is-task main.", StringComparison.Ordinal) &&
               css.Contains("-chat-art)", StringComparison.Ordinal),
            $"{directory} must paint chat art on the stable task main.");
        Ensure(!css.Contains($"main.{themeNamespace}-main>*{{position:relative", StringComparison.Ordinal),
            $"{directory} must not override every direct main child; doing so breaks Codex fixed headers.");
        Ensure(css.Contains("z-index:-2", StringComparison.Ordinal) &&
               css.Contains("z-index:-1", StringComparison.Ordinal),
            $"{directory} must stack artwork behind native Codex layout without repositioning native children.");
        if (directory == "xin.moonfox-sovereign")
        {
            Ensure(script.Contains("adaptiveLayout: true", StringComparison.Ordinal),
                "The flagship candidate must opt into geometry-based task widget visibility.");
            Ensure(script.Contains("data-theme-priority=\"primary\"", StringComparison.Ordinal) &&
                   script.Contains("data-theme-priority=\"secondary\"", StringComparison.Ordinal),
                "The flagship candidate must declare which right task card survives reduced layouts.");
            Ensure(css.Contains("--xmf-home-hero-height", StringComparison.Ordinal) &&
                   css.Contains("100cqh", StringComparison.Ordinal) &&
                   css.Contains("100cqw", StringComparison.Ordinal),
                "The flagship candidate home hero must respond to both available height and width.");
            Ensure(!css.Contains("height:502px!important", StringComparison.Ordinal) &&
                   !css.Contains("min-height:502px!important", StringComparison.Ordinal),
                "The flagship candidate home hero must not regress to its fixed-height crop.");
            Ensure(css.Contains(".xmf-is-settings .xmf-settings-surface", StringComparison.Ordinal) &&
                   css.Contains("electron-dark.xmf-is-settings", StringComparison.Ordinal),
                "The flagship candidate must reveal its light and dark chat artwork behind settings.");
        }
    }
}

static async Task FlagshipTemplateV1FreezesSharedStructureAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var skillRoot = Path.Combine(
        repositoryRoot,
        ".agents",
        "skills",
        "author-tessalume-theme");
    var templateRoot = Path.Combine(skillRoot, "assets", "theme-template");
    var templateScript = await File.ReadAllTextAsync(Path.Combine(templateRoot, "theme.js"));
    var templateCss = await File.ReadAllTextAsync(Path.Combine(templateRoot, "theme.css"));
    var templateManifest = await File.ReadAllTextAsync(Path.Combine(templateRoot, "manifest.json"));
    var validator = await File.ReadAllTextAsync(
        Path.Combine(skillRoot, "scripts", "validate_theme_contract.py"));
    var geometrySync = await File.ReadAllTextAsync(
        Path.Combine(skillRoot, "scripts", "sync_template_geometry.py"));
    var exampleSync = await File.ReadAllTextAsync(
        Path.Combine(skillRoot, "scripts", "sync_template_example.py"));

    const string geometryStart = "/* TESSALUME_TEMPLATE_V1_GEOMETRY_START */";
    const string geometryEnd = "/* TESSALUME_TEMPLATE_V1_GEOMETRY_END */";
    static string ExtractGeometry(string css, string startMarker, string endMarker)
    {
        var start = css.IndexOf(startMarker, StringComparison.Ordinal);
        var end = css.IndexOf(endMarker, StringComparison.Ordinal);
        Ensure(start >= 0 && end > start, "Template 1.0 geometry markers are missing.");
        end += endMarker.Length;
        Ensure(string.IsNullOrWhiteSpace(css[end..]),
            "The frozen Template 1.0 geometry must be the final CSS section.");
        return css[start..end].ReplaceLineEndings("\n");
    }

    Ensure(templateScript.Contains("templateVersion: \"1.0\"", StringComparison.Ordinal) &&
           templateScript.Contains("adaptiveLayout: true", StringComparison.Ordinal),
        "The reusable template must opt into Template 1.0 and adaptive layout.");
    Ensure(templateManifest.Contains("\"version\": \"1.0\"", StringComparison.Ordinal) &&
           templateManifest.Contains("assets/placeholder.svg", StringComparison.Ordinal),
        "The reusable template must be valid before custom artwork is added.");
    Ensure(validator.Contains("TEMPLATE_V1_PARTS", StringComparison.Ordinal) &&
           validator.Contains("canonical_geometry", StringComparison.Ordinal) &&
           geometrySync.Contains("--check", StringComparison.Ordinal) &&
           exampleSync.Contains("repo_root / \"examples\"", StringComparison.Ordinal) &&
           !Directory.Exists(Path.Combine(repositoryRoot, "examples", "advanced-theme")),
        "The authoring skill must validate structure parts and frozen geometry.");

    var requiredParts = new[]
    {
        "hero-copy",
        "hero-kicker",
        "hero-title-light",
        "hero-title-dark",
        "hero-motion",
        "hero-note",
        "identity",
        "identity-emblem",
        "identity-copy",
        "identity-status",
        "task-card-left",
        "task-card-right-secondary",
        "task-card-right-primary",
        "task-card-art",
        "task-card-caption",
        "memory-card",
        "memory-meter",
        "sync-panel",
        "sync-copy",
        "sync-core",
        "sync-meter",
        "sync-state",
        "composer-accessory",
    };
    foreach (var part in requiredParts)
    {
        Ensure(templateScript.Contains($"data-theme-part=\"{part}\"", StringComparison.Ordinal),
            $"The reusable template is missing structure part {part}.");
    }

    var canonicalGeometry = ExtractGeometry(templateCss, geometryStart, geometryEnd);
    Ensure(canonicalGeometry.Contains("width:146px;", StringComparison.Ordinal) &&
           canonicalGeometry.Contains("height:234px;", StringComparison.Ordinal) &&
           canonicalGeometry.Contains("top:334px;", StringComparison.Ordinal) &&
           canonicalGeometry.Contains("width:320px;", StringComparison.Ordinal) &&
           canonicalGeometry.Contains("height:56px;", StringComparison.Ordinal),
        "Template 1.0 geometry must preserve the accepted Xin layout.");

    var implementations = new[]
    {
        (Root: Path.Combine(repositoryRoot, "themes", "xin.moonfox-sovereign"), Namespace: "xmf"),
        (Root: Path.Combine(repositoryRoot, "examples"), Namespace: "example"),
    };
    foreach (var (root, themeNamespace) in implementations)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(root, "theme.js"));
        var css = await File.ReadAllTextAsync(Path.Combine(root, "theme.css"));
        Ensure(script.Contains("templateVersion: \"1.0\"", StringComparison.Ordinal),
            $"{Path.GetFileName(root)} must declare Template 1.0.");
        Ensure(!script.Contains('\0'),
            $"{Path.GetFileName(root)} contains an invalid null character.");
        foreach (var part in requiredParts)
        {
            Ensure(script.Contains($"data-theme-part=\"{part}\"", StringComparison.Ordinal),
                $"{Path.GetFileName(root)} is missing Template 1.0 part {part}.");
        }
        var expected = canonicalGeometry.Replace("__NS__", themeNamespace, StringComparison.Ordinal);
        var actual = ExtractGeometry(css, geometryStart, geometryEnd);
        Ensure(string.Equals(expected, actual, StringComparison.Ordinal),
            $"{Path.GetFileName(root)} has drifted from frozen Template 1.0 geometry.");
    }
}

static async Task LocalImporterCopiesPackageAsync()
{
    using var fixture = await ThemeFixture.CreateAsync();
    var library = Path.Combine(Path.GetTempPath(), $"cts-library-{Guid.NewGuid():N}");
    try
    {
        var package = await new ThemeImporter(new ThemePackageLoader()).ImportAsync(
            fixture.Root,
            library,
            overwrite: false);
        Ensure(package.Manifest.Id == "sample.theme", "Imported theme id did not match.");
        Ensure(File.Exists(Path.Combine(library, "sample.theme", "manifest.json")), "Imported manifest is missing.");
        Ensure(File.Exists(Path.Combine(library, "sample.theme", "assets", "hero.png")), "Imported asset is missing.");
    }
    finally
    {
        if (Directory.Exists(library))
        {
            Directory.Delete(library, recursive: true);
        }
    }
}

static async Task BundledAdapterBuildsPayloadAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var package = await LoadRepresentativePackageAsync(repositoryRoot);
    var payload = await new ThemePayloadBuilder(new Dictionary<string, string>
    {
        [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
            repositoryRoot,
            "src",
            "CodexThemeStudio.App",
            "Compatibility",
            "theme-runtime-v2.js"),
    }).BuildAsync(package);
    Ensure(!payload.Contains("__DREAM_", StringComparison.Ordinal), "A dream placeholder remained in the payload.");
    Ensure(!payload.Contains("__CTS_", StringComparison.Ordinal), "A Studio placeholder remained in the payload.");
    Ensure(package.Manifest.Config.TryGetValue("title", out var titleElement), "Theme title config is missing.");
    var title = titleElement.GetString();
    Ensure(!string.IsNullOrWhiteSpace(title), "Theme title config is empty.");
    var encodedTitle = JsonSerializer.Serialize(title).Trim('"');
    Ensure(package.IsAdvanced, "The representative theme should use the open advanced lifecycle.");
    Ensure(payload.Contains("registerTheme", StringComparison.Ordinal), "Advanced theme lifecycle is missing.");
    Ensure(payload.Contains(encodedTitle, StringComparison.Ordinal), "Theme config is missing.");
}

static async Task OpenAdvancedTemplateLoadsAndFingerprintsAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var package = (await new ThemePackageLoader().LoadAsync(
        Path.Combine(repositoryRoot, "examples"))).Package
        ?? throw new InvalidOperationException("Open advanced template could not be loaded.");
    var payload = await BuildPayloadAsync(repositoryRoot, package);
    var first = await ThemeFingerprintCalculator.CalculateAsync(package);
    var second = await ThemeFingerprintCalculator.CalculateAsync(package);
    Ensure(package.IsAdvanced, "Advanced template must require script trust.");
    Ensure(package.Manifest.Id == "example.template-v1",
        "The root example package must be the Flagship Template 1.0 example.");
    Ensure(payload.Contains("registerTheme", StringComparison.Ordinal), "Advanced lifecycle is missing.");
    Ensure(first.Length == 64 && first == second, "Theme fingerprint must be stable SHA-256.");
}

static async Task AdvancedImportAndTrustFollowFingerprintAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var library = Path.Combine(Path.GetTempPath(), $"cts-advanced-library-{Guid.NewGuid():N}");
    var trustData = Path.Combine(Path.GetTempPath(), $"cts-trust-{Guid.NewGuid():N}");
    try
    {
        var imported = await new ThemeImporter(new ThemePackageLoader()).ImportAsync(
            Path.Combine(repositoryRoot, "examples"),
            library,
            overwrite: false);
        var scriptPath = imported.ScriptPath ?? throw new InvalidOperationException("Advanced script was not imported.");
        Ensure(File.Exists(scriptPath), "Advanced script was not imported.");

        Directory.CreateDirectory(trustData);
        using var trustStore = new ThemeTrustStore(trustData);
        Ensure(!await trustStore.IsTrustedAsync(imported), "Advanced theme must start untrusted.");
        await trustStore.TrustAsync(imported);
        Ensure(await trustStore.IsTrustedAsync(imported), "Trusted fingerprint was not remembered.");

        await File.AppendAllTextAsync(scriptPath, "\n// fingerprint change");
        var changed = (await new ThemePackageLoader().LoadAsync(imported.RootDirectory)).Package
            ?? throw new InvalidOperationException("Changed advanced theme did not reload.");
        Ensure(!await trustStore.IsTrustedAsync(changed), "Changed script must invalidate prior trust.");
    }
    finally
    {
        if (Directory.Exists(library)) Directory.Delete(library, recursive: true);
        if (Directory.Exists(trustData)) Directory.Delete(trustData, recursive: true);
    }
}

static string FindRepositoryRoot()
{
    foreach (var startingPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(startingPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
    }

    throw new DirectoryNotFoundException("Could not find the Tessalume repository root.");
}

static async Task<ThemePackage> LoadRepresentativePackageAsync(string repositoryRoot)
{
    var loader = new ThemePackageLoader();
    var themesRoot = Path.Combine(repositoryRoot, "themes");
    if (Directory.Exists(themesRoot))
    {
        var catalog = await new ThemeCatalog(loader).ScanAsync(themesRoot);
        var published = catalog.FirstOrDefault(item => item.Validation.IsValid && item.Package is not null);
        if (published?.Package is not null)
        {
            return published.Package;
        }
    }

    var templateRoot = Path.Combine(repositoryRoot, "examples");
    var template = await loader.LoadAsync(templateRoot);
    Ensure(template.Validation.IsValid, FormatIssues(template.Validation));
    return template.Package
        ?? throw new InvalidOperationException("No published theme or open theme template could be loaded.");
}

static async Task<int> ProbeRuntimeAsync(int port)
{
    using var discovery = new LoopbackCdpDiscovery();
    var targets = await discovery.DiscoverAsync(port);
    if (targets.Count == 0)
    {
        Console.Error.WriteLine($"No Codex targets found on {port}.");
        return 2;
    }

    foreach (var target in targets.OrderBy(target =>
                 target.Url.Contains("initialRoute=", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
    {
        await using var session = new CdpSession();
        await session.ConnectAsync(target.WebSocketDebuggerUrl);
        var result = await session.EvaluateAsync(
            "({ themeId: window.__CODEX_THEME_STUDIO_THEME_ID__ || null, installed: !!window.__CODEX_THEME_STUDIO_THEME_ID__, runtime: !!window.__CODEX_THEME_STUDIO_RUNTIME__, root: !!document.getElementById('cts-theme-root'), style: !!document.getElementById('cts-theme-style') || !!document.getElementById('codex-dream-skin-style'), chrome: !!document.getElementById('codex-dream-skin-chrome'), title: document.querySelector('.dream-brand b')?.textContent || document.querySelector('.example-theme-widget b')?.textContent || null, exampleMounted: document.documentElement.getAttribute('data-example-theme-mounted') })");
        Console.WriteLine(result);
    }

    return 0;
}

static async Task<int> RemoveRuntimeAsync(int port)
{
    await using var runtime = new ThemeRuntime(
        new LoopbackCdpDiscovery(),
        new ThemePayloadBuilder(new Dictionary<string, string>()));
    await runtime.RemoveAsync(port);
    Console.WriteLine("Theme removed.");
    return 0;
}

static async Task<int> ApplyRuntimeAsync(int port)
{
    var repositoryRoot = FindRepositoryRoot();
    var package = await LoadRepresentativePackageAsync(repositoryRoot);
    await using var runtime = new ThemeRuntime(
        new LoopbackCdpDiscovery(),
        new ThemePayloadBuilder(new Dictionary<string, string>
        {
            [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
                repositoryRoot,
                "src",
                "CodexThemeStudio.App",
                "Compatibility",
                "theme-runtime-v2.js"),
        }));
    await runtime.StartAsync(port, package);
    await runtime.StopAsync();
    Console.WriteLine("Theme applied.");
    return 0;
}

static async Task<int> ApplyPackageRuntimeAsync(int port, string packagePath)
{
    var repositoryRoot = FindRepositoryRoot();
    var package = (await new ThemePackageLoader().LoadAsync(packagePath)).Package
        ?? throw new InvalidOperationException("The requested theme package could not be loaded.");
    await using var runtime = new ThemeRuntime(
        new LoopbackCdpDiscovery(),
        new ThemePayloadBuilder(new Dictionary<string, string>
        {
            [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
                repositoryRoot,
                "src",
                "CodexThemeStudio.App",
                "Compatibility",
                "theme-runtime-v2.js"),
        }));
    await runtime.StartAsync(port, package);
    await runtime.StopAsync();
    Console.WriteLine($"Theme applied: {package.Manifest.Id}");
    return 0;
}

static async Task<int> TrustPackageAsync(string dataPath, string packagePath)
{
    var package = (await new ThemePackageLoader().LoadAsync(packagePath)).Package
        ?? throw new InvalidOperationException("The requested theme package could not be loaded.");
    Directory.CreateDirectory(dataPath);
    using var trustStore = new ThemeTrustStore(dataPath);
    await trustStore.TrustAsync(package);
    Console.WriteLine($"Theme trusted for local diagnostics: {package.Manifest.Id}");
    return 0;
}

static async Task<string> BuildPayloadAsync(string repositoryRoot, ThemePackage package) =>
    await new ThemePayloadBuilder(new Dictionary<string, string>
    {
        [ThemePayloadBuilder.OpenRuntimeAdapterKey] = Path.Combine(
            repositoryRoot,
            "src",
            "CodexThemeStudio.App",
            "Compatibility",
            "theme-runtime-v2.js"),
    }).BuildAsync(package);

static async Task<int> ProbeThemeModesAsync(int port)
{
    using var discovery = new LoopbackCdpDiscovery();
    var targets = await discovery.DiscoverAsync(port);
    foreach (var target in targets)
    {
        await using var session = new CdpSession();
        await session.ConnectAsync(target.WebSocketDebuggerUrl);
        var installed = await session.EvaluateAsync(
            "document.documentElement.classList.contains('codex-dream-skin')");
        if (installed.ValueKind != JsonValueKind.True)
        {
            continue;
        }

        var result = await session.EvaluateAsync(
            """
            (() => {
              const root = document.documentElement;
              const wasDark = root.classList.contains('electron-dark');
              const sample = () => ({
                colorScheme: getComputedStyle(root).colorScheme,
                textColor: getComputedStyle(document.body).color,
                composerBackground: getComputedStyle(document.querySelector('.composer-surface-chrome') || document.body).backgroundColor
              });
              root.classList.remove('electron-dark');
              const light = sample();
              root.classList.add('electron-dark');
              const dark = sample();
              root.classList.toggle('electron-dark', wasDark);
              return { light, dark, different: JSON.stringify(light) !== JSON.stringify(dark) };
            })()
            """);
        Console.WriteLine(result);
        return result.GetProperty("different").GetBoolean() ? 0 : 3;
    }

    Console.Error.WriteLine("No themed Codex target found.");
    return 2;
}

static async Task<int> ToggleColorSchemeAsync(int port)
{
    await using var runtime = new ThemeRuntime(
        new LoopbackCdpDiscovery(),
        new ThemePayloadBuilder(new Dictionary<string, string>()));
    var dark = await runtime.ToggleColorSchemeAsync(port);
    Console.WriteLine(dark ? "dark" : "light");
    return 0;
}

static async Task<int> ProbeAppearanceStateAsync(int port)
{
    using var discovery = new LoopbackCdpDiscovery();
    var targets = await discovery.DiscoverAsync(port);
    foreach (var target in targets.OrderBy(target =>
                 target.Url.Contains("initialRoute=", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
    {
        await using var session = new CdpSession();
        await session.ConnectAsync(target.WebSocketDebuggerUrl);
        var result = await session.EvaluateAsync(
            """
            (() => {
              const root = document.documentElement;
              const interesting = ([key]) => /theme|appearance|color|dark|light|scheme/i.test(key);
              const storage = store => Object.entries(store).filter(interesting);
              return {
                isMain: !!document.querySelector('main') &&
                  !root.classList.contains('compact-window') &&
                  !new URLSearchParams(location.search).has('initialRoute'),
                url: location.href,
                rootClass: root.className,
                rootAttributes: Object.fromEntries([...root.attributes].map(x => [x.name, x.value])),
                bodyAttributes: Object.fromEntries([...document.body.attributes].map(x => [x.name, x.value])),
                colorScheme: getComputedStyle(root).colorScheme,
                prefersDark: matchMedia('(prefers-color-scheme: dark)').matches,
                localStorage: storage(localStorage),
                sessionStorage: storage(sessionStorage),
                globalKeys: Object.keys(window).filter(key => /theme|appearance|color|dark|light|scheme/i.test(key)).slice(0, 100)
              };
            })()
            """);
        if (result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("isMain", out var isMain) && isMain.GetBoolean())
        {
            Console.WriteLine(result);
            return 0;
        }
    }

    Console.Error.WriteLine("No main Codex target found.");
    return 2;
}

static async Task<int> ProbeAppearanceBundlesAsync(int port)
{
    using var discovery = new LoopbackCdpDiscovery();
    var targets = await discovery.DiscoverAsync(port);
    foreach (var target in targets)
    {
        await using var session = new CdpSession();
        await session.ConnectAsync(target.WebSocketDebuggerUrl);
        var result = await session.EvaluateAsync(
            """
            (async () => {
              if (!document.querySelector('main')) return null;
              const urls = [...new Set([
                ...[...document.scripts].map(x => x.src),
                ...performance.getEntriesByType('resource').map(x => x.name)
              ].filter(url => /\.m?js(?:\?|$)/i.test(url)))];
              const needles = ['electron-dark', 'color-scheme', 'appearance', 'setTheme', 'themePreference'];
              const matches = [];
              for (const url of urls) {
                let text;
                try { text = await fetch(url).then(response => response.text()); } catch { continue; }
                for (const needle of needles) {
                  let offset = 0;
                  for (let count = 0; count < 4; count++) {
                    const index = text.indexOf(needle, offset);
                    if (index < 0) break;
                    matches.push({ url, needle, index, snippet: text.slice(Math.max(0,index-500),index+900) });
                    offset = index + needle.length;
                  }
                }
              }
              return matches.slice(0, 60);
            })()
            """);
        if (result.ValueKind == JsonValueKind.Array)
        {
            Console.WriteLine(result);
            return 0;
        }
    }

    Console.Error.WriteLine("No main Codex target found.");
    return 2;
}

static async Task<int> ProbeQueryClientsAsync(int port)
{
    using var discovery = new LoopbackCdpDiscovery();
    var targets = await discovery.DiscoverAsync(port);
    foreach (var target in targets)
    {
        await using var session = new CdpSession();
        await session.ConnectAsync(target.WebSocketDebuggerUrl);
        var result = await session.EvaluateAsync(
            """
            (() => {
              if (!document.querySelector('main')) return null;
              const rootNode = document.querySelector('#root') || document.body;
              const fiberKey = Object.keys(rootNode).find(key => key.startsWith('__reactContainer$') || key.startsWith('__reactFiber$'));
              let root = fiberKey ? rootNode[fiberKey] : null;
              const seenFibers = new Set();
              const seenObjects = new WeakSet();
              const clients = [];
              const inspect = (value, path, depth = 0) => {
                if (!value || (typeof value !== 'object' && typeof value !== 'function') || seenObjects.has(value) || depth > 5) return;
                seenObjects.add(value);
                try {
                  if (typeof value.getQueryCache === 'function' && typeof value.invalidateQueries === 'function') {
                    const queries = value.getQueryCache().getAll().map(q => ({ hash: q.queryHash, key: q.queryKey, state: q.state?.status }));
                    clients.push({ path, queries: queries.filter(q => JSON.stringify(q).includes('settings')).slice(0, 20), total: queries.length });
                    return;
                  }
                } catch {}
                let entries;
                try { entries = Object.entries(value); } catch { return; }
                for (const [key, child] of entries.slice(0, 80)) {
                  if (/^(return|child|sibling|stateNode|alternate|_owner)$/.test(key)) continue;
                  inspect(child, `${path}.${key}`, depth + 1);
                }
              };
              const queue = root ? [root] : [];
              while (queue.length && seenFibers.size < 12000 && clients.length < 10) {
                const fiber = queue.shift();
                if (!fiber || seenFibers.has(fiber)) continue;
                seenFibers.add(fiber);
                inspect(fiber.memoizedProps, 'fiber.memoizedProps');
                inspect(fiber.memoizedState, 'fiber.memoizedState');
                inspect(fiber.dependencies, 'fiber.dependencies');
                if (fiber.child) queue.push(fiber.child);
                if (fiber.sibling) queue.push(fiber.sibling);
              }
              return { fiberKey, fibers: seenFibers.size, clients };
            })()
            """);
        if (result.ValueKind == JsonValueKind.Object)
        {
            Console.WriteLine(result);
            return 0;
        }
    }

    Console.Error.WriteLine("No main Codex target found.");
    return 2;
}

static void Ensure(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static string FormatIssues(ThemeValidationResult validation) =>
    string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}"));

file sealed class ThemeFixture : IDisposable
{
    private ThemeFixture(string root) => Root = root;

    public string Root { get; }

    public static async Task<ThemeFixture> CreateAsync(
        string? root = null,
        string cssPath = "theme.css",
        string css = ":root { --accent: #ff79c6; }")
    {
        root ??= Path.Combine(Path.GetTempPath(), $"cts-theme-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "assets"));

        if (!cssPath.StartsWith("..", StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(Path.Combine(root, cssPath), css);
        }
        await File.WriteAllTextAsync(
            Path.Combine(root, "theme.js"),
            "window.codexThemeStudio.registerTheme({ mount() {}, unmount() {} });");

        await File.WriteAllBytesAsync(Path.Combine(root, "assets", "hero.png"), [0x89, 0x50, 0x4e, 0x47]);
        var manifest = new
        {
            schemaVersion = 2,
            id = "sample.theme",
            name = "Sample Theme",
            version = "1.0.0",
            author = "Tests",
            engineVersion = 2,
            type = "advanced",
            capabilities = new { light = true, dark = true },
            entryPoints = new { css = cssPath, script = "theme.js" },
            assets = new { hero = "assets/hero.png" },
        };
        await File.WriteAllTextAsync(
            Path.Combine(root, ThemePackageLoader.ManifestFileName),
            JsonSerializer.Serialize(manifest));
        return new ThemeFixture(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
