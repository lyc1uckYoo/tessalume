internal static partial class TestSuite
{
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

    static async Task NullManifestSectionsProduceValidationAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-null-manifest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, ThemePackageLoader.ManifestFileName),
                """
                {
                  "schemaVersion": 2,
                  "id": "null.sections",
                  "name": "Null Sections",
                  "version": "1.0",
                  "engineVersion": 2,
                  "type": "advanced",
                  "capabilities": null,
                  "entryPoints": null,
                  "previews": null,
                  "assets": null,
                  "config": null,
                  "compatibility": null
                }
                """);
            var result = await new ThemePackageLoader().LoadAsync(root);
            Ensure(!result.Validation.IsValid && result.Package is null,
                "Null manifest sections must produce a rejected package instead of an exception.");
            Ensure(result.Validation.Issues.Any(issue => issue.Code == "manifest.capabilities.missing") &&
                   result.Validation.Issues.Any(issue => issue.Code == "manifest.entry-points.missing") &&
                   result.Validation.Issues.Any(issue => issue.Code == "manifest.assets.missing"),
                "Null manifest sections must produce actionable validation codes.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static async Task CatalogIncludesInvalidPackagesAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-catalog-{Guid.NewGuid():N}");
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


    static async Task LocalImporterCopiesPackageAsync()
    {
        using var fixture = await ThemeFixture.CreateAsync();
        var library = Path.Combine(Path.GetTempPath(), $"tessalume-library-{Guid.NewGuid():N}");
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

    static async Task ZipThemeImportIsBoundedAsync()
    {
        using var fixture = await ThemeFixture.CreateAsync();
        var token = Guid.NewGuid().ToString("N");
        var archivePath = Path.Combine(Path.GetTempPath(), $"tessalume-theme-{token}.zip");
        var maliciousPath = Path.Combine(Path.GetTempPath(), $"tessalume-malicious-{token}.zip");
        var library = Path.Combine(Path.GetTempPath(), $"tessalume-zip-library-{token}");
        string? extractedThemeDirectory = null;
        try
        {
            ZipFile.CreateFromDirectory(fixture.Root, archivePath);
            using (var extraction = await ThemeArchiveExtractor.ExtractAsync(archivePath))
            {
                extractedThemeDirectory = extraction.ThemeDirectory;
                Ensure(File.Exists(Path.Combine(extraction.ThemeDirectory, "manifest.json")),
                    "The extracted ZIP theme manifest is missing.");
                var imported = await new ThemeImporter(new ThemePackageLoader()).ImportAsync(
                    extraction.ThemeDirectory,
                    library,
                    overwrite: false);
                Ensure(imported.Manifest.Id == "sample.theme", "ZIP import changed the theme identity.");
            }
            Ensure(extractedThemeDirectory is not null && !Directory.Exists(extractedThemeDirectory),
                "Temporary ZIP extraction was not cleaned after import.");

            using (var archive = ZipFile.Open(maliciousPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../escaped.txt");
                await using var stream = entry.Open();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync("must not escape");
            }

            var traversalRejected = false;
            try
            {
                using var extraction = await ThemeArchiveExtractor.ExtractAsync(maliciousPath);
            }
            catch (InvalidDataException)
            {
                traversalRejected = true;
            }
            Ensure(traversalRejected, "ZIP path traversal was not rejected.");
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (File.Exists(maliciousPath)) File.Delete(maliciousPath);
            if (Directory.Exists(library)) Directory.Delete(library, recursive: true);
        }
    }


    static async Task OpenAdvancedTemplateLoadsWithStableRevisionHashAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var package = (await new ThemePackageLoader().LoadAsync(
            Path.Combine(repositoryRoot, "examples"))).Package
            ?? throw new InvalidOperationException("Open advanced template could not be loaded.");
        var payload = await BuildPayloadAsync(repositoryRoot, package);
        var first = await ThemeFingerprintCalculator.CalculateAsync(package);
        var second = await ThemeFingerprintCalculator.CalculateAsync(package);
        var sharedTemplatePath = Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Compatibility",
            ThemePayloadBuilder.SharedTemplateStyleFileName);
        var effective = await ThemeFingerprintCalculator.CalculateEffectiveAsync(package, sharedTemplatePath);
        Ensure(package.IsAdvanced, "Advanced template must use the scripted lifecycle.");
        Ensure(package.Manifest.Id == "example.template-v1",
            "The root example package must be the Flagship Template 1.0 example.");
        Ensure(payload.Contains("registerTheme", StringComparison.Ordinal), "Advanced lifecycle is missing.");
        Ensure(first.Length == 64 && first == second, "Theme revision hash must be stable SHA-256.");
        Ensure(effective.Length == 64 && effective != first,
            "Shared themes must include the runtime template stylesheet in their effective revision hash.");
    }

    static async Task AdvancedImportKeepsScriptAndTracksChangesAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var library = Path.Combine(Path.GetTempPath(), $"tessalume-advanced-library-{Guid.NewGuid():N}");
        try
        {
            var imported = await new ThemeImporter(new ThemePackageLoader()).ImportAsync(
                Path.Combine(repositoryRoot, "examples"),
                library,
                overwrite: false);
            var scriptPath = imported.ScriptPath ?? throw new InvalidOperationException("Advanced script was not imported.");
            Ensure(File.Exists(scriptPath), "Advanced script was not imported.");
            var initialHash = await ThemeFingerprintCalculator.CalculateAsync(imported);

            await File.AppendAllTextAsync(scriptPath, "\n// fingerprint change");
            var changed = (await new ThemePackageLoader().LoadAsync(imported.RootDirectory)).Package
                ?? throw new InvalidOperationException("Changed advanced theme did not reload.");
            var changedHash = await ThemeFingerprintCalculator.CalculateAsync(changed);
            Ensure(!string.Equals(initialHash, changedHash, StringComparison.Ordinal),
                "Changing the imported script must update its runtime revision hash.");
        }
        finally
        {
            if (Directory.Exists(library)) Directory.Delete(library, recursive: true);
        }
    }

    static async Task<string> ReadMainWindowSourceAsync(string appRoot)
    {
        var sources = Directory
            .EnumerateFiles(appRoot, "MainWindow*.cs", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => File.ReadAllTextAsync(path));
        return string.Join("\n", await Task.WhenAll(sources));
    }

    static async Task<string> ReadMainWindowXamlAsync(string appRoot)
    {
        var mainWindow = await File.ReadAllTextAsync(Path.Combine(appRoot, "MainWindow.xaml"));
        var resources = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Styles",
            "MainWindowResources.xaml"));
        var creatorViews = await Task.WhenAll(Directory
            .EnumerateFiles(Path.Combine(appRoot, "Creator"), "*.xaml", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => File.ReadAllTextAsync(path)));
        var artworkAdjustmentEditor = await File.ReadAllTextAsync(Path.Combine(
            appRoot,
            "Controls",
            "ArtworkAdjustmentEditor.xaml"));
        var featureViews = Directory.Exists(Path.Combine(appRoot, "Features"))
            ? await Task.WhenAll(Directory
                .EnumerateFiles(Path.Combine(appRoot, "Features"), "*.xaml", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(path => File.ReadAllTextAsync(path)))
            : [];
        return string.Join(
            "\n",
            new[] { mainWindow, resources, artworkAdjustmentEditor }
                .Concat(creatorViews)
                .Concat(featureViews));
    }

    static async Task<string> ReadUiPreferencesSourceAsync(string appRoot)
    {
        var infrastructureRoot = Path.Combine(appRoot, "Infrastructure");
        var model = await File.ReadAllTextAsync(Path.Combine(infrastructureRoot, "UiPreferences.cs"));
        var store = await File.ReadAllTextAsync(Path.Combine(infrastructureRoot, "UiPreferencesStore.cs"));
        return model + "\n" + store;
    }

}
