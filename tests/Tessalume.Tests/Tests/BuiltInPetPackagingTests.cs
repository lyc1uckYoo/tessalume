using System.Security.Cryptography;
using System.Text.Json;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Pets;

internal static partial class TestSuite
{
    static async Task BuiltInPetPackageIsPublishedAndExtractedSafelyAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageRoot = Path.Combine(repositoryRoot, "pets", "flying-snowfluff");
        var catalogPath = Path.Combine(packageRoot, "catalog.json");
        var manifestPath = Path.Combine(packageRoot, "pet.json");
        var sheetPath = Path.Combine(packageRoot, "spritesheet.webp");
        Ensure(File.Exists(catalogPath) && File.Exists(manifestPath) && File.Exists(sheetPath),
            "The built-in Flying Snowfluff release package is incomplete.");

        using var catalog = JsonDocument.Parse(await File.ReadAllTextAsync(catalogPath));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var root = catalog.RootElement;
        var protocol = root.GetProperty("protocol");
        var states = protocol.GetProperty("states").EnumerateArray().ToArray();
        var frameTotal = states.Sum(state => state.GetProperty("frames").GetInt32());
        Ensure(root.GetProperty("schemaVersion").GetInt32() == 2 &&
               root.GetProperty("id").GetString() == "flying-snowfluff" &&
               root.GetProperty("id").GetString() == manifest.RootElement.GetProperty("id").GetString() &&
               root.GetProperty("productVersion").GetString() == "1.0.0" &&
               !string.IsNullOrWhiteSpace(root.GetProperty("author").GetProperty("name").GetString()) &&
               !string.IsNullOrWhiteSpace(root.GetProperty("license").GetProperty("kind").GetString()) &&
               !string.IsNullOrWhiteSpace(root.GetProperty("license").GetProperty("spdx").GetString()) &&
               !string.IsNullOrWhiteSpace(root.GetProperty("license").GetProperty("name").GetString()) &&
               !string.IsNullOrWhiteSpace(root.GetProperty("rights").GetProperty("kind").GetString()) &&
               !string.IsNullOrWhiteSpace(root.GetProperty("rights").GetProperty("notice").GetString()) &&
               root.GetProperty("recommendedThemeIds").EnumerateArray()
                   .Select(theme => theme.GetString()).SequenceEqual(["aemeath.star-voyage"]) &&
               protocol.GetProperty("spriteVersionNumber").GetInt32() == 2 &&
               protocol.GetProperty("atlasWidth").GetInt32() == 1536 &&
               protocol.GetProperty("atlasHeight").GetInt32() == 2288 &&
               protocol.GetProperty("usedFrameCount").GetInt32() == 74 &&
               frameTotal == 74 &&
               states.Select(state => state.GetProperty("frames").GetInt32())
                   .SequenceEqual([7, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8]),
            "The built-in pet catalog must retain its independent product version and exact desktop V2 atlas layout.");

        var declaredFiles = root.GetProperty("files").EnumerateArray()
            .ToDictionary(
                file => file.GetProperty("path").GetString()!,
                file => file,
                StringComparer.Ordinal);
        var actualRelativePaths = Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(packageRoot, path).Replace('\\', '/'))
            .Where(path => path != "catalog.json")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Ensure(actualRelativePaths.SequenceEqual(declaredFiles.Keys.Order(StringComparer.Ordinal)),
            "Every built-in pet release asset must be declared, and design sources must stay out of the package.");
        foreach (var (relativePath, descriptor) in declaredFiles)
        {
            var filePath = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
            Ensure(filePath.StartsWith(packageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                   File.Exists(filePath) &&
                   new FileInfo(filePath).Length == descriptor.GetProperty("size").GetInt64() &&
                   HashBuiltInPetReleaseFile(filePath).Equals(descriptor.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase),
                $"Built-in pet asset failed release hash validation: {relativePath}.");
        }

        var previews = root.GetProperty("previews").EnumerateArray().ToArray();
        var expectedPreviews = new Dictionary<string, (string Kind, string Label, int Width, int Height, int Frames)>(
            StringComparer.Ordinal)
        {
            ["idle"] = ("action", "待机", 576, 624, 6),
            ["move-right"] = ("action", "向右移动", 576, 624, 8),
            ["move-left"] = ("action", "向左移动", 576, 624, 8),
            ["wave-touch"] = ("action", "挥手互动", 576, 624, 4),
            ["jump"] = ("action", "跳跃", 576, 624, 5),
            ["blocked"] = ("action", "遇到阻塞", 576, 624, 8),
            ["needs-input"] = ("action", "等待输入", 576, 624, 6),
            ["running"] = ("action", "正在工作", 576, 624, 6),
            ["ready"] = ("action", "完成待看", 576, 624, 6),
            ["gaze-clockwise"] = ("direction", "16 向转身", 576, 684, 16),
            ["showcase"] = ("showcase", "动态九宫格", 1152, 1248, 8),
        };
        Ensure(previews.Length == expectedPreviews.Count && previews.All(preview =>
                   declaredFiles[preview.GetProperty("path").GetString()!].GetProperty("role").GetString() == "preview"),
            "The WPF pet center needs all eleven declared animated previews.");
        var loaded = await new PetPackageLoader().LoadAsync(packageRoot);
        Ensure(loaded.Validation.IsValid && loaded.Package is not null &&
               loaded.Package.InstallFiles.Count() == 2 &&
               loaded.Package.PreviewInfos.Count == expectedPreviews.Count,
            "The release package must validate eleven GIFs while keeping only two Codex install files.");
        var package = loaded.Package!;
        foreach (var preview in previews)
        {
            var path = preview.GetProperty("path").GetString()!;
            var key = preview.GetProperty("actionKey").GetString()!;
            var expected = expectedPreviews[key];
            var info = package.PreviewInfos[path];
            Ensure(preview.GetProperty("mediaType").GetString() == "image/gif" &&
                   preview.GetProperty("stateKey").GetString() == key &&
                   preview.GetProperty("kind").GetString() == expected.Kind &&
                   preview.GetProperty("label").GetString() == expected.Label &&
                   preview.GetProperty("expectedFrameCount").GetInt32() == expected.Frames &&
                   preview.GetProperty("width").GetInt32() == expected.Width &&
                   preview.GetProperty("height").GetInt32() == expected.Height &&
                   preview.GetProperty("representativeFrame").GetInt32() == 0 &&
                   preview.GetProperty("loop").GetBoolean() &&
                   info.Width == expected.Width &&
                   info.Height == expected.Height &&
                   info.FrameCount == expected.Frames,
                $"Animated preview metadata does not match its GIF: {path}.");
        }
        Ensure(!actualRelativePaths.Any(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)),
            "Static PNG substitutes must not remain in the animated preview release package.");

        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"tessalume-built-in-pets-{Guid.NewGuid():N}");
        try
        {
            var layout = new PortableLayout(
                temporaryRoot,
                Path.Combine(temporaryRoot, "themes"),
                Path.Combine(temporaryRoot, "data"));
            BuiltInAssetInstaller.EnsurePetsInstalled(layout);
            var extractedRoot = Path.Combine(layout.PetsDirectory, "flying-snowfluff");
            foreach (var sourcePath in Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(packageRoot, sourcePath);
                var extractedPath = Path.Combine(extractedRoot, relativePath);
                Ensure(File.Exists(extractedPath) &&
                       HashBuiltInPetReleaseFile(sourcePath) == HashBuiltInPetReleaseFile(extractedPath),
                    $"Embedded pet extraction differs from source: {relativePath}.");
            }

            var unchangedPath = Path.Combine(extractedRoot, "pet.json");
            var unchangedWriteTime = File.GetLastWriteTimeUtc(unchangedPath);
            BuiltInAssetInstaller.EnsurePetsInstalled(layout);
            Ensure(File.GetLastWriteTimeUtc(unchangedPath) == unchangedWriteTime,
                "Repeated built-in pet extraction must leave matching files untouched.");

            var damagedPreview = Path.Combine(extractedRoot, "previews", "05-blocked.gif");
            await File.WriteAllBytesAsync(damagedPreview, [0x00]);
            BuiltInAssetInstaller.EnsurePetsInstalled(layout);
            Ensure(HashBuiltInPetReleaseFile(damagedPreview) ==
                   HashBuiltInPetReleaseFile(Path.Combine(packageRoot, "previews", "05-blocked.gif")),
                "Built-in pet extraction must repair a damaged portable catalog asset.");
            Ensure(!Directory.EnumerateFileSystemEntries(
                    Path.GetDirectoryName(damagedPreview)!,
                    "*.tmp",
                    SearchOption.TopDirectoryOnly).Any(),
                "Successful built-in pet extraction must not leave temporary files behind.");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }

        VerifyBuiltInPetReparseDefense();
        await VerifyBuiltInPetBuildGateAsync(repositoryRoot, packageRoot);

        var projectSource = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Tessalume.App.csproj"));
        var installerSource = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "Tessalume.App",
            "Infrastructure",
            "BuiltInAssetInstaller.cs"));
        var buildSource = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "一键构建EXE.ps1"));
        Ensure(projectSource.Contains("Tessalume.BuiltInPets/", StringComparison.Ordinal) &&
               installerSource.Contains("EnsurePetPathHasNoReparsePoints", StringComparison.Ordinal) &&
               installerSource.Contains("RandomNumberGenerator.GetHexString", StringComparison.Ordinal) &&
               installerSource.Contains("FileMode.CreateNew", StringComparison.Ordinal) &&
               buildSource.Contains("$builtInPetPackageNames = @('flying-snowfluff')", StringComparison.Ordinal) &&
               buildSource.Contains("Assert-SafeBuiltInPetTree", StringComparison.Ordinal) &&
               buildSource.Contains("Get-PetWebPMetadata", StringComparison.Ordinal) &&
               buildSource.Contains("Get-PetGifMetadata", StringComparison.Ordinal) &&
               buildSource.Contains("Kind = 'showcase'", StringComparison.Ordinal) &&
               buildSource.Contains("Assert-BuiltInPetPackages", StringComparison.Ordinal) &&
               buildSource.Contains("-p:BuiltInPetsRoot=$sourcePets", StringComparison.Ordinal) &&
               buildSource.Contains("$stagedPets = Join-Path $staging 'pets'", StringComparison.Ordinal) &&
               buildSource.Contains("Published pet asset does not match its source", StringComparison.Ordinal),
            "Publishing must embed, stage, and hash-check the complete built-in pet package.");
    }

    private static void VerifyBuiltInPetReparseDefense()
    {
        var securityRoot = Path.Combine(Path.GetTempPath(), $"tessalume-pet-reparse-{Guid.NewGuid():N}");
        var links = new List<string>();
        Directory.CreateDirectory(securityRoot);
        try
        {
            var probeTarget = Path.Combine(securityRoot, "probe-target");
            var probeLink = Path.Combine(securityRoot, "probe-link");
            Directory.CreateDirectory(probeTarget);
            if (!TryCreateBuiltInPetDirectoryLink(probeLink, probeTarget))
            {
                return;
            }
            links.Add(probeLink);
            DeleteBuiltInPetDirectoryLink(probeLink);
            links.Remove(probeLink);

            var portableParentTarget = Path.Combine(securityRoot, "portable-parent-target");
            var portableParentLink = Path.Combine(securityRoot, "portable-parent-link");
            Directory.CreateDirectory(portableParentTarget);
            Directory.CreateSymbolicLink(portableParentLink, portableParentTarget);
            links.Add(portableParentLink);
            Ensure(BuiltInPetExtractionRejectsReparsePoint(new PortableLayout(
                       portableParentLink,
                       Path.Combine(portableParentLink, "themes"),
                       Path.Combine(portableParentLink, "data"))),
                "Built-in pet extraction must reject a reparse-point portable parent.");

            var petsRoot = Path.Combine(securityRoot, "pets-root-case");
            var petsExternal = Path.Combine(securityRoot, "pets-root-external");
            Directory.CreateDirectory(petsRoot);
            Directory.CreateDirectory(petsExternal);
            var petsLink = Path.Combine(petsRoot, "pets");
            Directory.CreateSymbolicLink(petsLink, petsExternal);
            links.Add(petsLink);
            Ensure(BuiltInPetExtractionRejectsReparsePoint(new PortableLayout(
                       petsRoot,
                       Path.Combine(petsRoot, "themes"),
                       Path.Combine(petsRoot, "data"))) &&
                   !Directory.EnumerateFileSystemEntries(petsExternal).Any(),
                "Built-in pet extraction must reject the pets root before writing through it.");

            var packageRoot = Path.Combine(securityRoot, "package-case");
            var packageExternal = Path.Combine(securityRoot, "package-external");
            Directory.CreateDirectory(Path.Combine(packageRoot, "pets"));
            Directory.CreateDirectory(packageExternal);
            var packageLink = Path.Combine(packageRoot, "pets", "flying-snowfluff");
            Directory.CreateSymbolicLink(packageLink, packageExternal);
            links.Add(packageLink);
            Ensure(BuiltInPetExtractionRejectsReparsePoint(new PortableLayout(
                       packageRoot,
                       Path.Combine(packageRoot, "themes"),
                       Path.Combine(packageRoot, "data"))) &&
                   !Directory.EnumerateFileSystemEntries(packageExternal).Any(),
                "Built-in pet extraction must reject a linked package directory before writing through it.");

            var previewRoot = Path.Combine(securityRoot, "preview-case");
            var previewExternal = Path.Combine(securityRoot, "preview-external");
            Directory.CreateDirectory(Path.Combine(previewRoot, "pets", "flying-snowfluff"));
            Directory.CreateDirectory(previewExternal);
            var previewLink = Path.Combine(previewRoot, "pets", "flying-snowfluff", "previews");
            Directory.CreateSymbolicLink(previewLink, previewExternal);
            links.Add(previewLink);
            Ensure(BuiltInPetExtractionRejectsReparsePoint(new PortableLayout(
                       previewRoot,
                       Path.Combine(previewRoot, "themes"),
                       Path.Combine(previewRoot, "data"))) &&
                   !Directory.EnumerateFileSystemEntries(previewExternal).Any(),
                "Built-in pet extraction must reject a linked destination parent before writing previews.");

            var temporaryRoot = Path.Combine(securityRoot, "temporary-case");
            var temporaryLayout = new PortableLayout(
                temporaryRoot,
                Path.Combine(temporaryRoot, "themes"),
                Path.Combine(temporaryRoot, "data"));
            BuiltInAssetInstaller.EnsurePetsInstalled(temporaryLayout);
            var temporaryExternal = Path.Combine(securityRoot, "temporary-external");
            Directory.CreateDirectory(temporaryExternal);
            var blockedPreview = Path.Combine(
                temporaryLayout.PetsDirectory,
                "flying-snowfluff",
                "previews",
                "05-blocked.gif");
            File.WriteAllBytes(blockedPreview, [0x00]);
            var legacyTemporaryLink = blockedPreview + ".tmp";
            Directory.CreateSymbolicLink(legacyTemporaryLink, temporaryExternal);
            links.Add(legacyTemporaryLink);
            Ensure(BuiltInPetExtractionRejectsReparsePoint(temporaryLayout) &&
                   !Directory.EnumerateFileSystemEntries(temporaryExternal).Any(),
                "Built-in pet extraction must reject a reparse-point temporary path.");
        }
        finally
        {
            for (var index = links.Count - 1; index >= 0; index--)
            {
                DeleteBuiltInPetDirectoryLink(links[index]);
            }
            if (Directory.Exists(securityRoot)) Directory.Delete(securityRoot, recursive: true);
        }
    }

    private static async Task VerifyBuiltInPetBuildGateAsync(
        string repositoryRoot,
        string sourcePackageRoot)
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"tessalume-pet-build-gate-{Guid.NewGuid():N}");
        var projectRoot = Path.Combine(testRoot, "project");
        var petsRoot = Path.Combine(projectRoot, "pets");
        var packageRoot = Path.Combine(petsRoot, "flying-snowfluff");
        var harnessPath = Path.Combine(testRoot, "invoke-pet-gate.ps1");
        string? reparseLink = null;
        try
        {
            CopyBuiltInPetTestDirectory(sourcePackageRoot, packageRoot);
            var harnessSource = """
                param(
                    [Parameter(Mandatory=$true)][string]$BuildScript,
                    [Parameter(Mandatory=$true)][string]$ProjectRoot,
                    [Parameter(Mandatory=$true)][string]$PetsRoot
                )
                $ErrorActionPreference = 'Stop'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                    $BuildScript,
                    [ref]$tokens,
                    [ref]$errors)
                if ($errors.Count -gt 0) { throw $errors[0] }
                foreach ($function in $ast.FindAll(
                    { param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] },
                    $true)) {
                    Invoke-Expression $function.Extent.Text
                }
                $script:root = [IO.Path]::GetFullPath($ProjectRoot)
                Assert-BuiltInPetPackages $PetsRoot @('flying-snowfluff') | Out-Null
                """;
            await File.WriteAllTextAsync(harnessPath, harnessSource);
            var buildScript = Path.Combine(repositoryRoot, "一键构建EXE.ps1");

            var valid = await RunBuiltInPetBuildGateAsync(harnessPath, buildScript, projectRoot, petsRoot);
            Ensure(valid.ExitCode == 0, $"The real built-in pet package must pass the publish gate. {valid.Output}");

            var extraPackage = Path.Combine(petsRoot, "unexpected-pet");
            Directory.CreateDirectory(extraPackage);
            var extra = await RunBuiltInPetBuildGateAsync(harnessPath, buildScript, projectRoot, petsRoot);
            Ensure(extra.ExitCode != 0 && extra.Output.Contains("allowlist", StringComparison.OrdinalIgnoreCase),
                "The publish gate must reject any package outside the explicit built-in allowlist.");
            Directory.Delete(extraPackage);

            var strayFile = Path.Combine(packageRoot, "undeclared.bin");
            await File.WriteAllBytesAsync(strayFile, [0x01]);
            var stray = await RunBuiltInPetBuildGateAsync(harnessPath, buildScript, projectRoot, petsRoot);
            Ensure(stray.ExitCode != 0 && stray.Output.Contains("undeclared", StringComparison.OrdinalIgnoreCase),
                "The publish gate must reject undeclared release files.");
            File.Delete(strayFile);

            var catalogPath = Path.Combine(packageRoot, "catalog.json");
            var catalogSource = await File.ReadAllTextAsync(catalogPath);
            await File.WriteAllTextAsync(catalogPath, catalogSource.Replace(
                "\"usedFrameCount\": 74",
                "\"usedFrameCount\": 73",
                StringComparison.Ordinal));
            var protocol = await RunBuiltInPetBuildGateAsync(harnessPath, buildScript, projectRoot, petsRoot);
            Ensure(protocol.ExitCode != 0 && protocol.Output.Contains("protocol", StringComparison.OrdinalIgnoreCase),
                "The publish gate must reject protocol geometry or frame-count drift.");

            await File.WriteAllTextAsync(catalogPath, catalogSource.Replace(
                "\"role\": \"codex-manifest\"",
                "\"role\": \"preview\"",
                StringComparison.Ordinal));
            var role = await RunBuiltInPetBuildGateAsync(harnessPath, buildScript, projectRoot, petsRoot);
            Ensure(role.ExitCode != 0 && role.Output.Contains("role", StringComparison.OrdinalIgnoreCase),
                "The publish gate must reject manifest, runtime, or preview role confusion.");

            await File.WriteAllTextAsync(catalogPath, catalogSource.Replace(
                "\"kind\": \"showcase\"",
                "\"kind\": \"state\"",
                StringComparison.Ordinal));
            var productPreview = await RunBuiltInPetBuildGateAsync(
                harnessPath,
                buildScript,
                projectRoot,
                petsRoot);
            Ensure(productPreview.ExitCode != 0 &&
                   productPreview.Output.Contains("preview", StringComparison.OrdinalIgnoreCase),
                "The animated showcase must retain its dedicated preview kind.");
            await File.WriteAllTextAsync(catalogPath, catalogSource);

            var externalDirectory = Path.Combine(projectRoot, "external-pet-package");
            Directory.CreateDirectory(externalDirectory);
            reparseLink = Path.Combine(petsRoot, "linked-pet");
            if (TryCreateBuiltInPetDirectoryLink(reparseLink, externalDirectory))
            {
                var reparse = await RunBuiltInPetBuildGateAsync(
                    harnessPath,
                    buildScript,
                    projectRoot,
                    petsRoot);
                Ensure(reparse.ExitCode != 0 &&
                       reparse.Output.Contains("reparse", StringComparison.OrdinalIgnoreCase),
                    "The publish gate must reject reparse points before package enumeration.");
            }
        }
        finally
        {
            if (reparseLink is not null) DeleteBuiltInPetDirectoryLink(reparseLink);
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunBuiltInPetBuildGateAsync(
        string harnessPath,
        string buildScript,
        string projectRoot,
        string petsRoot)
    {
        var powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var startInfo = new System.Diagnostics.ProcessStartInfo(powerShell)
        {
            WorkingDirectory = projectRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile",
                     "-NonInteractive",
                     "-ExecutionPolicy",
                     "Bypass",
                     "-File",
                     harnessPath,
                     "-BuildScript",
                     buildScript,
                     "-ProjectRoot",
                     projectRoot,
                     "-PetsRoot",
                     petsRoot,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["PSModulePath"] = string.Empty;
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the built-in pet publish-gate test.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        return (process.ExitCode, $"{output} {error}".Trim());
    }

    private static void CopyBuiltInPetTestDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static bool BuiltInPetExtractionRejectsReparsePoint(PortableLayout layout)
    {
        try
        {
            BuiltInAssetInstaller.EnsurePetsInstalled(layout);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static bool TryCreateBuiltInPetDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return Directory.Exists(link) &&
                   (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void DeleteBuiltInPetDirectoryLink(string path)
    {
        if (Directory.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path);
        }
    }

    private static string HashBuiltInPetReleaseFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

}
