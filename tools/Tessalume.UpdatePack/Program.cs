using System.Text;
using System.Text.Json;
using Tessalume.Core.Updates.Delta;

return await UpdatePackProgram.RunAsync(args);

internal static class UpdatePackProgram
{
    private const long MaximumExecutableBytes = 512L * 1024L * 1024L;
    private const double MaximumUsefulDeltaRatio = 0.85d;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 5)
        {
            Console.Error.WriteLine(
                "Usage: Tessalume.UpdatePack <basis-exe> <basis-version> <target-exe> <target-version> <output-directory>");
            return 2;
        }

        try
        {
            var basisPath = RequireExecutable(args[0], "basis");
            var basisVersion = NormalizeVersion(args[1]);
            var targetPath = RequireExecutable(args[2], "target");
            var targetVersion = NormalizeVersion(args[3]);
            var outputDirectory = Path.GetFullPath(args[4]);
            if (string.Equals(basisVersion, targetVersion, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The basis and target versions must differ.");
            }

            Directory.CreateDirectory(outputDirectory);
            var deltaName = $"Tessalume-{basisVersion}-to-{targetVersion}.delta";
            var deltaPath = Path.Combine(outputDirectory, deltaName);
            var manifestPath = Path.Combine(outputDirectory, UpdateDeltaManifest.FileName);
            var verificationPath = Path.Combine(outputDirectory, $".{deltaName}.verification");
            File.Delete(deltaPath);
            File.Delete(manifestPath);
            File.Delete(verificationPath);
            try
            {
                await BinaryDeltaCodec.CreateAsync(basisPath, targetPath, deltaPath);
                var targetInfo = new FileInfo(targetPath);
                var deltaInfo = new FileInfo(deltaPath);
                var ratio = (double)deltaInfo.Length / targetInfo.Length;
                if (ratio >= MaximumUsefulDeltaRatio)
                {
                    File.Delete(deltaPath);
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        published = false,
                        reason = "delta-not-smaller-enough",
                        ratio,
                    }));
                    return 0;
                }

                await BinaryDeltaCodec.ApplyAsync(basisPath, deltaPath, verificationPath);
                var basisSha256 = await BinaryDeltaCodec.ComputeSha256Async(basisPath);
                var targetSha256 = await BinaryDeltaCodec.ComputeSha256Async(targetPath);
                var verificationSha256 = await BinaryDeltaCodec.ComputeSha256Async(verificationPath);
                if (new FileInfo(verificationPath).Length != targetInfo.Length ||
                    !string.Equals(verificationSha256, targetSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The generated delta did not reconstruct the exact target executable.");
                }

                var deltaSha256 = await BinaryDeltaCodec.ComputeSha256Async(deltaPath);
                var manifest = new UpdateDeltaManifest
                {
                    SchemaVersion = UpdateDeltaManifest.CurrentSchemaVersion,
                    TargetVersion = targetVersion,
                    TargetFileName = UpdateDeltaManifest.TargetExecutableName,
                    TargetSize = targetInfo.Length,
                    TargetSha256 = targetSha256.ToLowerInvariant(),
                    Deltas =
                    [
                        new UpdateDeltaEntry
                        {
                            FromVersion = basisVersion,
                            FromSha256 = basisSha256.ToLowerInvariant(),
                            Algorithm = UpdateDeltaEntry.SupportedAlgorithm,
                            AssetName = deltaName,
                            AssetSize = deltaInfo.Length,
                            AssetSha256 = deltaSha256.ToLowerInvariant(),
                        },
                    ],
                };
                await WriteUtf8AtomicallyAsync(
                    manifestPath,
                    JsonSerializer.Serialize(manifest, UpdateDeltaManifest.JsonOptions) + Environment.NewLine);
                var manifestSha256 = await BinaryDeltaCodec.ComputeSha256Async(manifestPath);
                var checksumPath = Path.Combine(outputDirectory, "SHA256SUMS.txt");
                var checksumLines = new[]
                {
                    $"{targetSha256.ToLowerInvariant()} *{UpdateDeltaManifest.TargetExecutableName}",
                    $"{deltaSha256.ToLowerInvariant()} *{deltaName}",
                    $"{manifestSha256.ToLowerInvariant()} *{UpdateDeltaManifest.FileName}",
                };
                await WriteUtf8AtomicallyAsync(
                    checksumPath,
                    string.Join(Environment.NewLine, checksumLines) + Environment.NewLine);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    published = true,
                    basisVersion,
                    targetVersion,
                    delta = deltaPath,
                    manifest = manifestPath,
                    fullSize = targetInfo.Length,
                    deltaSize = deltaInfo.Length,
                    ratio,
                    savings = targetInfo.Length - deltaInfo.Length,
                    targetSha256,
                    deltaSha256,
                }));
                return 0;
            }
            finally
            {
                File.Delete(verificationPath);
                File.Delete(verificationPath + ".partial");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string RequireExecutable(string path, string role)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) ||
            !string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase) ||
            (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"The {role} executable is missing, linked, or invalid: {fullPath}");
        }
        var length = new FileInfo(fullPath).Length;
        if (length <= 0 || length > MaximumExecutableBytes)
        {
            throw new InvalidDataException($"The {role} executable has an invalid size.");
        }
        return fullPath;
    }

    private static string NormalizeVersion(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(normalized, out var version) || version.Build < 0 || version.Revision >= 0)
        {
            throw new InvalidDataException($"Invalid three-part release version: {value}");
        }
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static async Task WriteUtf8AtomicallyAsync(string path, string content)
    {
        var temporaryPath = path + ".partial";
        File.Delete(temporaryPath);
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }
}
