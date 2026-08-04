internal static partial class TestSuite
{
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


    static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    static string FormatIssues(ThemeValidationResult validation) =>
        string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}"));
}

internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}

internal sealed class ThemeFixture : IDisposable
{
    private ThemeFixture(string root) => Root = root;

    public string Root { get; }

    public static async Task<ThemeFixture> CreateAsync(
        string? root = null,
        string cssPath = "theme.css",
        string css = ":root { --accent: #ff79c6; }")
    {
        root ??= Path.Combine(Path.GetTempPath(), $"tessalume-theme-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "assets"));

        if (!cssPath.StartsWith("..", StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(Path.Combine(root, cssPath), css);
        }
        await File.WriteAllTextAsync(
            Path.Combine(root, "theme.js"),
            "registerTheme({ mount() {}, unmount() {} });");

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
