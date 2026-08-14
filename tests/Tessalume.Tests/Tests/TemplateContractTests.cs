internal static partial class TestSuite
{
    static async Task PublishedThemesUseCanonicalInjectionContractAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sharedCss = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Compatibility",
            "theme-template-v1.css"));
        var runtime = await ReadCompatibilityRuntimeSourceAsync(repositoryRoot);
        Ensure(runtime.Contains("mountCanonicalTheme", StringComparison.Ordinal),
            "The open runtime must expose the canonical theme host.");
        Ensure(runtime.Contains("renderTemplateV1", StringComparison.Ordinal) &&
               runtime.Contains("appendDecorations", StringComparison.Ordinal) &&
               runtime.Contains("data-tessalume-surface", StringComparison.Ordinal),
            "The open runtime must own Template 1.0 outer DOM and generic surface markers.");
        Ensure(runtime.Contains("syncRouteState();", StringComparison.Ordinal),
            "The canonical host must synchronize route state before its debounced repair.");
        Ensure(runtime.Contains("const decorateOutputPanels = () => {", StringComparison.Ordinal) &&
               runtime.Contains("[data-slot=\"thread-summary-panel-item-button\"]", StringComparison.Ordinal),
            "The canonical host must bind environment panels by their stable item slot, not only visible labels.");

        var themes = new[]
        {
            (Directory: "xin.moonfox-sovereign", Namespace: "xmf"),
            (Directory: "aemeath-star-voyage", Namespace: "ae3"),
            (Directory: "danya.bubble-void-duality", Namespace: "dny"),
            (Directory: "qingxiao.cloudsword-gate", Namespace: "qxo"),
        };
        foreach (var (directory, themeNamespace) in themes)
        {
            var themeRoot = Path.Combine(repositoryRoot, "themes", directory);
            var script = await File.ReadAllTextAsync(Path.Combine(themeRoot, "theme.js"));
            var css = await File.ReadAllTextAsync(Path.Combine(themeRoot, "skin.css"));
            Ensure(script.Contains("context.mountCanonicalTheme(", StringComparison.Ordinal),
                $"{directory} must use the canonical theme host.");
            Ensure(script.Contains("context.renderTemplateV1(", StringComparison.Ordinal),
                $"{directory} must use the shared Template 1.0 renderer.");
            Ensure(!script.Contains("context.observe(", StringComparison.Ordinal) &&
                   !script.Contains("MutationObserver", StringComparison.Ordinal),
                $"{directory} must not own route observers.");
            Ensure(!script.Contains("data-theme-role=", StringComparison.Ordinal) &&
                   !script.Contains("data-theme-stage", StringComparison.Ordinal),
                $"{directory} must not duplicate runtime-owned outer roles.");
            Ensure(!css.Contains("TESSALUME_TEMPLATE_V1_", StringComparison.Ordinal) &&
                   !css.Contains("[data-theme-role=", StringComparison.Ordinal),
                $"{directory} skin must not duplicate shared surfaces or geometry.");
            Ensure(css.Contains("-is-task main.", StringComparison.Ordinal) &&
                   css.Contains("-chat-art)", StringComparison.Ordinal),
                $"{directory} must paint chat art on the stable task main.");
            Ensure(!css.Contains($"main.{themeNamespace}-main>*{{position:relative", StringComparison.Ordinal),
                $"{directory} must not override every direct main child; doing so breaks Codex fixed headers.");
            Ensure(!css.Contains($"main.{themeNamespace}-main::before {{\n  content:\"\";\n  position:", StringComparison.Ordinal) &&
                   !css.Contains($"main.{themeNamespace}-main::after {{\n  content:\"\";\n  position:", StringComparison.Ordinal) &&
                   sharedCss.Contains("[data-tessalume-surface=\"main\"]::before { z-index:-2; }", StringComparison.Ordinal) &&
                   sharedCss.Contains("[data-tessalume-surface=\"main\"]::after { z-index:-1; }", StringComparison.Ordinal),
                $"{directory} must inherit task-canvas stacking from the shared template stylesheet.");
            if (directory == "aemeath-star-voyage")
            {
                Ensure(script.Contains("stageDecorations:", StringComparison.Ordinal) &&
                       script.Contains("ae3-orbit", StringComparison.Ordinal),
                    "Aemeath's character-specific stage orbit must survive shared-DOM migration.");
            }
            if (directory == "danya.bubble-void-duality")
            {
                Ensure(script.Contains("stageDecorations:", StringComparison.Ordinal) &&
                       script.Contains("data-dny-home-fx=\"bubble-prism-v2\"", StringComparison.Ordinal) &&
                       script.Contains("data-dny-home-fx=\"void-lattice-v2\"", StringComparison.Ordinal) &&
                       script.Contains("dny-main-frame", StringComparison.Ordinal) &&
                       script.Contains("class=\"dny-domain-line\" data-theme-part=\"hero-motion\"", StringComparison.Ordinal) &&
                       !script.Contains("homeEffects", StringComparison.Ordinal) &&
                       css.Contains(".dny-domain-phases-light", StringComparison.Ordinal) &&
                       css.Contains(".dny-domain-phases-dark", StringComparison.Ordinal) &&
                       script.Contains("data-dny-sync-fx=\"duality-chamber-v2\"", StringComparison.Ordinal) &&
                       css.Contains(".dny-sync-core", StringComparison.Ordinal) &&
                       css.Contains(".dny-sync-state", StringComparison.Ordinal),
                    "Danya's light/dark home effects must live in the canonical hero-motion slot.");
            }
            if (directory == "qingxiao.cloudsword-gate")
            {
                Ensure(script.Contains("class=\"qxo-score\" data-theme-part=\"hero-motion\"", StringComparison.Ordinal) &&
                       script.Contains("data-qxo-home-fx=\"cloud-heart-sword-v2\"", StringComparison.Ordinal) &&
                       script.Contains("data-qxo-home-fx=\"moon-sword-array-v2\"", StringComparison.Ordinal) &&
                       !script.Contains("qxo-banner-fx", StringComparison.Ordinal) &&
                       css.Contains(".qxo-score-form-light", StringComparison.Ordinal) &&
                       css.Contains(".qxo-score-form-dark", StringComparison.Ordinal),
                    "Qingxiao's light/dark sword arrays must live in the canonical hero-motion slot.");
            }
            if (directory == "shorekeeper.tethys-reverie")
            {
                Ensure(script.Contains("class=\"sk3-tide\" data-theme-part=\"hero-motion\"", StringComparison.Ordinal) &&
                       script.Contains("data-sk3-home-fx=\"shoreline-butterfly-v2\"", StringComparison.Ordinal) &&
                       script.Contains("data-sk3-home-fx=\"tethys-probability-v2\"", StringComparison.Ordinal) &&
                       css.Contains(".sk3-tide-form-light", StringComparison.Ordinal) &&
                       css.Contains(".sk3-tide-form-dark", StringComparison.Ordinal) &&
                       !css.Contains("sk3-route-scan", StringComparison.Ordinal),
                    "Shorekeeper's light/dark home effects must live in the canonical hero-motion slot.");
            }
            if (directory == "suisui.inkscape-dawn")
            {
                Ensure(script.Contains("class=\"sui-river\" data-theme-part=\"hero-motion\"", StringComparison.Ordinal) &&
                       script.Contains("data-sui-home-fx=\"dawn-fan-scroll-v2\"", StringComparison.Ordinal) &&
                       script.Contains("data-sui-home-fx=\"moonlit-chongming-v2\"", StringComparison.Ordinal) &&
                       !script.Contains("sui-banner-fx", StringComparison.Ordinal) &&
                       css.Contains(".sui-river-form-light", StringComparison.Ordinal) &&
                       css.Contains(".sui-river-form-dark", StringComparison.Ordinal) &&
                       script.Contains("data-sui-sync-fx=\"shanhe-fan-v2\"", StringComparison.Ordinal) &&
                       css.Contains(".sui-sync-core", StringComparison.Ordinal) &&
                       css.Contains(".sui-sync-state", StringComparison.Ordinal),
                    "Suisui's light/dark home effects must live in the canonical hero-motion slot.");
            }
            if (directory == "xin.moonfox-sovereign")
            {
                Ensure(script.Contains("adaptiveLayout: true", StringComparison.Ordinal),
                    "The flagship candidate must opt into geometry-based task widget visibility.");
                Ensure(script.Contains("taskSecondary:", StringComparison.Ordinal) &&
                       script.Contains("taskPrimary:", StringComparison.Ordinal),
                    "The flagship candidate must fill both canonical right-card slots.");
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
        var templateCss = await File.ReadAllTextAsync(Path.Combine(templateRoot, "skin.css"));
        var sharedCss = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Compatibility",
            "theme-template-v1.css"));
        var templateManifest = await File.ReadAllTextAsync(Path.Combine(templateRoot, "manifest.json"));
        var validator = await File.ReadAllTextAsync(
            Path.Combine(skillRoot, "scripts", "validate_theme_contract.py"));
        var geometrySync = await File.ReadAllTextAsync(
            Path.Combine(skillRoot, "scripts", "sync_template_geometry.py"));
        var exampleSync = await File.ReadAllTextAsync(
            Path.Combine(skillRoot, "scripts", "sync_template_example.py"));

        Ensure(templateScript.Contains("templateVersion: \"1.0\"", StringComparison.Ordinal) &&
               templateScript.Contains("adaptiveLayout: true", StringComparison.Ordinal) &&
               templateScript.Contains("context.renderTemplateV1(", StringComparison.Ordinal) &&
               templateScript.Contains("data-theme-draft=", StringComparison.Ordinal),
            "The reusable template must opt into Template 1.0 and adaptive layout.");
        Ensure(templateManifest.Contains("\"version\": \"1.0\"", StringComparison.Ordinal) &&
               templateManifest.Contains("\"style\": \"shared\"", StringComparison.Ordinal) &&
               templateManifest.Contains("\"qualityGate\": \"flagship-complete-1\"", StringComparison.Ordinal) &&
               templateManifest.Contains("assets/placeholder.svg", StringComparison.Ordinal),
            "The reusable template must be valid before custom artwork is added.");
        Ensure(validator.Contains("REQUIRED_SLOTS", StringComparison.Ordinal) &&
               validator.Contains("DRAFT_TOKENS", StringComparison.Ordinal) &&
               validator.Contains("flagship visual coverage missing", StringComparison.Ordinal) &&
               templateCss.Contains("aside.app-shell-left-panel::after", StringComparison.Ordinal) &&
               templateCss.Contains("-task-title", StringComparison.Ordinal) &&
               templateCss.Contains("thread-summary-panel-item-button", StringComparison.Ordinal) &&
               templateCss.Contains("_footer_", StringComparison.Ordinal) &&
               validator.Contains("skin.css", StringComparison.Ordinal) &&
               geometrySync.Contains("--check", StringComparison.Ordinal) &&
               exampleSync.Contains("repo_root / \"examples\"", StringComparison.Ordinal) &&
               !Directory.Exists(Path.Combine(repositoryRoot, "examples", "advanced-theme")),
            "The authoring skill must validate shared structure and skin isolation.");

        var requiredParts = new[]
        {
            "hero-kicker",
            "hero-title-light",
            "hero-title-dark",
            "hero-motion",
            "hero-note",
            "identity-emblem",
            "identity-copy",
            "identity-status",
            "task-card-art",
            "task-card-caption",
            "memory-meter",
            "sync-copy",
            "sync-core",
            "sync-meter",
            "sync-state",
        };
        foreach (var part in requiredParts)
        {
            Ensure(templateScript.Contains($"data-theme-part=\"{part}\"", StringComparison.Ordinal),
                $"The reusable template is missing structure part {part}.");
        }

        Ensure(sharedCss.Contains("width:146px;", StringComparison.Ordinal) &&
               sharedCss.Contains("height:234px;", StringComparison.Ordinal) &&
               sharedCss.Contains("top:334px;", StringComparison.Ordinal) &&
               sharedCss.Contains("width:320px;", StringComparison.Ordinal) &&
               sharedCss.Contains("height:56px;", StringComparison.Ordinal) &&
               sharedCss.Contains("--tessalume-v1-home-composer-reserve:240px;", StringComparison.Ordinal) &&
               sharedCss.Contains("calc(100cqh - var(--tessalume-v1-home-composer-reserve))", StringComparison.Ordinal) &&
               sharedCss.Contains("data-tessalume-surface=\"chat-paper\"", StringComparison.Ordinal),
            "Runtime-owned Template 1.0 geometry must preserve the accepted Xin layout.");
        Ensure(sharedCss.Contains("--tessalume-task-title-primary-width", StringComparison.Ordinal) &&
               sharedCss.Contains(":has(button.truncate)", StringComparison.Ordinal) &&
               sharedCss.Contains("margin-inline-start:0!important", StringComparison.Ordinal) &&
               !sharedCss.Contains("padding-right:145px", StringComparison.Ordinal),
            "Template 1.0 task-title frames must grow with their content before falling back to ellipsis.");
        Ensure(!templateCss.Contains("[data-theme-role=", StringComparison.Ordinal) &&
               !templateCss.Contains("TESSALUME_TEMPLATE_V1_", StringComparison.Ordinal),
            "The reusable skin must not contain shared geometry.");

        var implementations = new[]
        {
            (Root: Path.Combine(repositoryRoot, "themes", "xin.moonfox-sovereign"), Namespace: "xmf"),
            (Root: Path.Combine(repositoryRoot, "examples"), Namespace: "example"),
        };
        foreach (var (root, themeNamespace) in implementations)
        {
            var script = await File.ReadAllTextAsync(Path.Combine(root, "theme.js"));
            var css = await File.ReadAllTextAsync(Path.Combine(root, "skin.css"));
            Ensure(script.Contains("templateVersion: \"1.0\"", StringComparison.Ordinal),
                $"{Path.GetFileName(root)} must declare Template 1.0.");
            Ensure(!script.Contains('\0'),
                $"{Path.GetFileName(root)} contains an invalid null character.");
            foreach (var part in requiredParts)
            {
                Ensure(script.Contains($"data-theme-part=\"{part}\"", StringComparison.Ordinal),
                    $"{Path.GetFileName(root)} is missing Template 1.0 part {part}.");
            }
            Ensure(script.Contains("context.renderTemplateV1(", StringComparison.Ordinal) &&
                   !script.Contains("data-theme-role=", StringComparison.Ordinal) &&
                   !css.Contains("[data-theme-role=", StringComparison.Ordinal) &&
                   !css.Contains("home-hero-height", StringComparison.Ordinal) &&
                   !css.Contains("height:502px!important", StringComparison.Ordinal) &&
                   !css.Contains("flex:0 0 526px!important", StringComparison.Ordinal),
                $"{Path.GetFileName(root)} has duplicated runtime-owned Template 1.0 structure.");
        }
    }

}
