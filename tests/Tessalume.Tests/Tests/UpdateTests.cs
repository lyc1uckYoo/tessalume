internal static partial class TestSuite
{
    static async Task ReleaseUpdaterChecksAndDownloadsAsync()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"tessalume-update-client-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);
        try
        {
            var executableBytes = Encoding.UTF8.GetBytes("Tessalume test executable v1.3.0");
            var sha256 = Convert.ToHexString(SHA256.HashData(executableBytes));
            var requestedUris = new List<Uri>();
            using var httpClient = new HttpClient(new StubHttpHandler(request =>
            {
                requestedUris.Add(request.RequestUri!);
                if (request.RequestUri!.Host == "api.github.com")
                {
                    var json = JsonSerializer.Serialize(new
                    {
                        tag_name = "v1.3.0",
                        html_url = "https://github.com/lyc1uckYoo/tessalume/releases/tag/v1.3.0",
                        body = "Update test release",
                        draft = false,
                        prerelease = false,
                        assets = new[]
                        {
                            new
                            {
                                name = "Tessalume.exe",
                                browser_download_url = "https://downloads.example.test/Tessalume.exe",
                                size = executableBytes.Length,
                                digest = $"sha256:{sha256.ToLowerInvariant()}",
                            },
                        },
                    });
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json"),
                    };
                }

                if (request.RequestUri.Host == "downloads.example.test")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(executableBytes),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }));
            using var client = new ReleaseUpdateClient(
                httpClient,
                "lyc1uckYoo",
                "tessalume",
                dataDirectory,
                new Version(1, 2, 0));
            var release = await client.CheckLatestAsync();
            Ensure(release is not null && release.Version == new Version(1, 3, 0) && release.Sha256 == sha256,
                "The updater must accept a newer stable GitHub Release and its asset digest.");
            UpdateDownloadProgress? lastProgress = null;
            var downloaded = await client.DownloadAsync(
                release!,
                new Progress<UpdateDownloadProgress>(value => lastProgress = value));
            Ensure(File.ReadAllBytes(downloaded).SequenceEqual(executableBytes),
                "The updater must persist the verified release asset without modifying its bytes.");
            Ensure(requestedUris.Any(uri => uri.Host == "api.github.com") &&
                   requestedUris.Any(uri => uri.Host == "downloads.example.test"),
                "The updater must use the release metadata endpoint and the declared asset URL.");
            Ensure(lastProgress is null || lastProgress.BytesReceived == executableBytes.Length,
                "Download progress must never report an invalid final byte count.");
        }
        finally
        {
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }
    }

    static async Task PortableUpdaterReplacesAndBacksUpAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tessalume-update-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var destination = Path.Combine(root, "Tessalume.exe");
            var source = Path.Combine(root, "Tessalume.exe.download");
            var helper = Path.Combine(root, "Tessalume.UpdateHelper.exe");
            var resultPath = Path.Combine(root, "update-result.json");
            var preferencesPath = Path.Combine(root, "data", "ui-settings.json");
            var oldBytes = Encoding.UTF8.GetBytes("old version");
            var newBytes = Encoding.UTF8.GetBytes("new version");
            var preferencesBytes = Encoding.UTF8.GetBytes("{\"DarkMode\":true,\"FavoriteThemeIds\":[\"kept-theme\"]}");
            await File.WriteAllBytesAsync(destination, oldBytes);
            await File.WriteAllBytesAsync(source, newBytes);
            await File.WriteAllTextAsync(helper, "helper");
            Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
            await File.WriteAllBytesAsync(preferencesPath, preferencesBytes);
            var request = new PortableUpdateRequest(
                0,
                source,
                destination,
                Convert.ToHexString(SHA256.HashData(newBytes)),
                "v1.3.0",
                resultPath,
                helper);
            var result = await PortableUpdateInstaller.ApplyAndWriteResultAsync(request);
            Ensure(result.Success && File.ReadAllBytes(destination).SequenceEqual(newBytes),
                "The portable installer must replace the destination with the verified release.");
            Ensure(result.BackupPath is not null && File.ReadAllBytes(result.BackupPath).SequenceEqual(oldBytes),
                "The portable installer must keep the previous executable as a rollback backup.");
            Ensure(File.ReadAllBytes(preferencesPath).SequenceEqual(preferencesBytes),
                "Replacing the executable must not modify portable user settings or other data files.");
            var persisted = PortableUpdateInstaller.ReadResult(resultPath);
            Ensure(persisted is { Success: true, VersionLabel: "v1.3.0" },
                "The update result must survive the updater process restart boundary.");

            var rejectedSource = Path.Combine(root, "tampered.exe.download");
            await File.WriteAllTextAsync(rejectedSource, "tampered");
            var rejected = await PortableUpdateInstaller.ApplyAndWriteResultAsync(request with
            {
                SourcePath = rejectedSource,
                ExpectedSha256 = new string('0', 64),
                ResultPath = Path.Combine(root, "rejected-result.json"),
            });
            Ensure(!rejected.Success && File.ReadAllBytes(destination).SequenceEqual(newBytes),
                "A checksum mismatch must leave the currently installed executable untouched.");
            Ensure(File.ReadAllBytes(preferencesPath).SequenceEqual(preferencesBytes),
                "A rejected update must also leave portable user settings untouched.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

}
