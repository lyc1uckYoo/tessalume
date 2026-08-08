using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tessalume.App.Infrastructure;

internal static class CompatibilityRuntimeComposer
{
    public const string BundleManifestRelativePath = "Runtime/runtime-bundle.json";
    public const string RuntimeFileName = "theme-runtime-v2.js";

    private const int MaximumFragmentCount = 8;
    private const int MaximumRuntimeCharacters = 2 * 1024 * 1024;
    private const string StandaloneEnvelopeMarker = "TESSALUME_STANDALONE_ENVELOPE";
    private static readonly Regex StandaloneEnvelope = new(
        @"(?ms)^[ \t]*// TESSALUME_STANDALONE_ENVELOPE_START\s*\r?\n.*?^[ \t]*// TESSALUME_STANDALONE_ENVELOPE_END\s*(?:\r?\n)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string ComposeSource(string compatibilityDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compatibilityDirectory);
        compatibilityDirectory = Path.GetFullPath(compatibilityDirectory);
        var manifestPath = Path.Combine(
            compatibilityDirectory,
            BundleManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var manifest = JsonSerializer.Deserialize<RuntimeBundleManifest>(
            File.ReadAllText(manifestPath),
            ManifestSerializerOptions) ?? throw new InvalidDataException("兼容运行时模块清单为空。");
        ValidateManifest(manifest);

        var runtimeDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidDataException("兼容运行时模块目录无效。");
        var fragments = manifest.Fragments.Select(fileName =>
        {
            var path = Path.GetFullPath(Path.Combine(runtimeDirectory, fileName));
            if (!string.Equals(Path.GetDirectoryName(path), runtimeDirectory, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path))
            {
                throw new InvalidDataException($"兼容运行时模块不存在或路径无效：{fileName}");
            }
            return StripStandaloneEnvelope(File.ReadAllText(path)).TrimEnd('\r', '\n');
        });
        var source = string.Join('\n', fragments) + "\n";
        ValidateSource(source);
        return source;
    }

    public static void EnsureComposed(string compatibilityDirectory)
    {
        var source = ComposeSource(compatibilityDirectory);
        var outputPath = Path.Combine(Path.GetFullPath(compatibilityDirectory), RuntimeFileName);
        if (File.Exists(outputPath) && string.Equals(
                File.ReadAllText(outputPath),
                source,
                StringComparison.Ordinal)) return;

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(outputPath)!,
            $".{RuntimeFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void ValidateManifest(RuntimeBundleManifest manifest)
    {
        if (manifest.SchemaVersion != 1 ||
            !string.Equals(manifest.Output, RuntimeFileName, StringComparison.Ordinal) ||
            manifest.Fragments.Count is 0 or > MaximumFragmentCount ||
            manifest.Fragments.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Fragments.Count ||
            manifest.Fragments.Any(fileName =>
                string.IsNullOrWhiteSpace(fileName) ||
                !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(fileName), ".js", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("兼容运行时模块清单无效。");
        }
    }

    private static string StripStandaloneEnvelope(string source)
    {
        var composed = StandaloneEnvelope.Replace(source, string.Empty);
        if (composed.Contains(StandaloneEnvelopeMarker, StringComparison.Ordinal))
        {
            throw new InvalidDataException("兼容运行时模块的独立语法校验边界不完整。");
        }
        return composed;
    }

    private static void ValidateSource(string source)
    {
        if (source.Length is 0 or > MaximumRuntimeCharacters ||
            !source.Contains("TESSALUME_RUNTIME_FRAGMENT", StringComparison.Ordinal) ||
            !source.Contains("mountCanonicalTheme", StringComparison.Ordinal) ||
            !source.Contains("syncRouteState", StringComparison.Ordinal) ||
            !source.Contains("syncAdaptiveVisibility", StringComparison.Ordinal) ||
            !source.Contains("decorateSharedSurfaces", StringComparison.Ordinal) ||
            !source.Contains("const dispose = async () =>", StringComparison.Ordinal) ||
            !source.TrimEnd().EndsWith("})()", StringComparison.Ordinal))
        {
            throw new InvalidDataException("兼容运行时模块组装结果不完整。");
        }
    }

    private sealed record RuntimeBundleManifest(
        int SchemaVersion,
        string Output,
        IReadOnlyList<string> Fragments);
}
