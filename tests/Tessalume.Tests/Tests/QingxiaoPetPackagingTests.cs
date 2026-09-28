using System.Text.Json.Nodes;
using Tessalume.App.Features.Pets;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Pets;

internal static partial class TestSuite
{
    static async Task QingxiaoPetPublishesAndInstallsFromReleasePackageAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "pets", "qingxiao");
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-qingxiao-release-{Guid.NewGuid():N}");
        try
        {
            // Only the published package is copied. No ignored development
            // outputs are available to this package/installer verification.
            var packageRoot = Path.Combine(root, "pets", "qingxiao");
            PetCopyDirectory(sourceRoot, packageRoot);
            var loaded = await new PetPackageLoader().LoadAsync(packageRoot);
            Ensure(loaded.Validation.IsValid && loaded.Package is not null,
                "The independently published Qingxiao package must pass strict Core validation.");
            var package = loaded.Package!;
            Ensure(package.Manifest is
            {
                Id: "qingxiao", DisplayName: "清宵", SpriteVersionNumber: 2,
                SpritesheetPath: "spritesheet.webp"
            } &&
                   package.Catalog.SchemaVersion == 2 && package.Catalog.ProductVersion == "1.0.0" &&
                   package.SpritesheetInfo is { Width: 1536, Height: 2288, HasAlpha: true, Encoding: "VP8L" } &&
                   package.Catalog.Protocol.States.SequenceEqual(PetPackageContract.RequiredStates) &&
                   package.Catalog.Protocol.UsedFrameCount == 74,
                "Qingxiao must retain its own version and the complete 74-cell V2 desktop protocol.");
            Ensure(package.Catalog.Rights.Kind == "fan-work" &&
                   package.Catalog.Rights.Notice.Contains("非官方同人", StringComparison.Ordinal) &&
                   !string.IsNullOrWhiteSpace(package.Catalog.Author.Name) &&
                   package.Catalog.License.Kind == "all-rights-reserved" &&
                   package.Catalog.RecommendedThemeIds.SequenceEqual(["qingxiao.cloudsword-gate"]) &&
                   File.Exists(Path.Combine(repositoryRoot, "themes", "qingxiao.cloudsword-gate", "manifest.json")) &&
                   !package.Catalog.Description.Contains("待验收", StringComparison.Ordinal),
                "The published pet must expose release wording, authorship, rights and its available paired theme.");
            var expectedFiles = package.Catalog.Files.Select(file => file.Path).Append("catalog.json")
                .Order(StringComparer.Ordinal).ToArray();
            var actualFiles = Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(packageRoot, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal).ToArray();
            Ensure(expectedFiles.Length == 14 && actualFiles.SequenceEqual(expectedFiles) &&
                   package.InstallFiles.Select(file => file.Path).Order(StringComparer.Ordinal)
                       .SequenceEqual(["pet.json", "spritesheet.webp"]),
                "Qingxiao release resources must contain only the catalog, two runtime files and eleven GIFs.");
            Ensure(package.Catalog.Previews.Select(preview => preview.ActionKey).SequenceEqual(
                   ["idle", "move-right", "move-left", "wave-touch", "jump", "blocked",
                    "needs-input", "running", "ready", "gaze-clockwise", "showcase"]) &&
                   package.Catalog.Previews.Select(preview => preview.ExpectedFrameCount)
                       .SequenceEqual([6, 8, 8, 4, 5, 8, 6, 6, 6, 16, 24]) &&
                   package.Catalog.Previews.All(preview => preview.Width == 576 && preview.Height == 624 && preview.Loop) &&
                   package.PreviewInfos.Count == 11 && package.PreviewInfos.All(pair =>
                       pair.Value.Width == 576 && pair.Value.Height == 624 &&
                       pair.Value.FrameCount == package.Catalog.Previews.Single(preview => preview.Path == pair.Key).ExpectedFrameCount),
                "All accepted Qingxiao animation previews must retain their truthful compact dimensions and frame counts.");
            foreach (var file in package.Catalog.Files)
            {
                var path = Path.Combine(packageRoot, file.Path);
                Ensure(new FileInfo(path).Length == file.Size && HashBuiltInPetReleaseFile(path) == file.Sha256,
                    $"Published Qingxiao file failed its exact catalog hash: {file.Path}.");
            }

            var layout = new PortableLayout(root, Path.Combine(root, "themes"), Path.Combine(root, "data"));
            Directory.CreateDirectory(layout.DataDirectory);
            using var gallery = new PetGalleryService(layout);
            var snapshot = await gallery.ScanAsync();
            var entry = snapshot.Entries.Single(candidate => candidate.PetId == "qingxiao");
            Ensure(entry.IsValid && entry.SourceBadge == "正式宠物" && entry.PreviewFrames.Count == 11 &&
                   entry.RecommendedThemeId == "qingxiao.cloudsword-gate",
                "Qingxiao must enter the existing gallery and paired-theme flow as a formal package.");
            var options = new PetApplicationServiceOptions(Path.Combine(root, "isolated-codex-pets"),
                Path.Combine(root, "pet-backups"), Path.Combine(layout.DataDirectory, "pet-state.json"));
            using (var service = new PetApplicationService(layout, options))
            {
                service.SelectEntry(entry);
                var initial = await service.RefreshAsync();
                Ensure(initial.Status == PetCenterStatus.NotInstalled && initial.PrimaryActionEnabled &&
                       service.CurrentRecommendedThemeId == "qingxiao.cloudsword-gate",
                    "The Qingxiao detail must be installable and preserve its own theme recommendation.");
                var installed = await service.InstallAsync(PetInstallIntent.Install);
                var installedRoot = Path.Combine(options.CodexPetsRoot, "qingxiao");
                Ensure(installed.Status == PetCenterStatus.AwaitingCodexSelection && installed.CanUninstall &&
                       Directory.EnumerateFiles(installedRoot, "*", SearchOption.AllDirectories)
                           .Select(Path.GetFileName).Order(StringComparer.Ordinal)
                           .SequenceEqual(["pet.json", "spritesheet.webp"]),
                    "Installing Qingxiao must write only its two declared Codex runtime files.");
                Ensure(HashBuiltInPetReleaseFile(Path.Combine(installedRoot, "spritesheet.webp")) ==
                       HashBuiltInPetReleaseFile(Path.Combine(sourceRoot, "spritesheet.webp")),
                    "The installed Qingxiao atlas must preserve the accepted image bytes.");
                await service.UninstallAsync(PetUninstallIntent.Safe);
                Ensure(!File.Exists(Path.Combine(installedRoot, "pet.json")) &&
                       File.Exists(Path.Combine(packageRoot, "catalog.json")),
                    "Uninstalling Qingxiao must preserve its formal gallery source.");
            }

            var extractedLayout = new PortableLayout(Path.Combine(root, "extracted"),
                Path.Combine(root, "extracted", "themes"), Path.Combine(root, "extracted", "data"));
            BuiltInAssetInstaller.EnsurePetsInstalled(extractedLayout);
            foreach (var file in expectedFiles)
            {
                var extracted = Path.Combine(extractedLayout.PetsDirectory, "qingxiao", file);
                Ensure(File.Exists(extracted) && HashBuiltInPetReleaseFile(extracted) ==
                       HashBuiltInPetReleaseFile(Path.Combine(sourceRoot, file)),
                    $"Embedded Qingxiao extraction must preserve every formal package resource: {file}.");
            }
            await VerifyQingxiaoPublishGateAsync(repositoryRoot, root, packageRoot);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task VerifyQingxiaoPublishGateAsync(string repositoryRoot, string projectRoot, string packageRoot)
    {
        var harnessPath = Path.Combine(projectRoot, "qingxiao-publish-gate.ps1");
        await File.WriteAllTextAsync(harnessPath, """
            param([string]$BuildScript, [string]$ProjectRoot, [string]$PetsRoot)
            $ErrorActionPreference = 'Stop'
            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($BuildScript, [ref]$tokens, [ref]$errors)
            if ($errors.Count -gt 0) { throw $errors[0] }
            foreach ($function in $ast.FindAll(
                { param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) {
                Invoke-Expression $function.Extent.Text
            }
            $script:root = [IO.Path]::GetFullPath($ProjectRoot)
            Assert-BuiltInPetPackages $PetsRoot @('qingxiao') | Out-Null
            """);
        var buildScript = Path.Combine(repositoryRoot, "一键构建EXE.ps1");
        var petsRoot = Path.GetDirectoryName(packageRoot)!;
        var valid = await RunBuiltInPetBuildGateAsync(harnessPath, buildScript, projectRoot, petsRoot);
        Ensure(valid.ExitCode == 0, $"The accepted Qingxiao package must pass the real publish gate: {valid.Output}");
        var catalogPath = Path.Combine(packageRoot, "catalog.json");
        var catalog = JsonNode.Parse(await File.ReadAllTextAsync(catalogPath))!;
        catalog["previews"]!.AsArray().Single(preview =>
            preview!["actionKey"]!.GetValue<string>() == "gaze-clockwise")!["height"] = 684;
        await File.WriteAllTextAsync(catalogPath, catalog.ToJsonString());
        var drift = await RunBuiltInPetBuildGateAsync(harnessPath, buildScript, projectRoot, petsRoot);
        Ensure(drift.ExitCode != 0 && drift.Output.Contains("preview", StringComparison.OrdinalIgnoreCase),
            "Qingxiao's preview profile must reject another pet's dimensions instead of weakening the gate.");
    }
}
