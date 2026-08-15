using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;

internal enum ArtworkPresetUpsertResult
{
    Added,
    Replaced,
    CapacityReached,
}

internal static class ArtworkPresetLibraryService
{
    public const int MaximumPresetCount = 24;
    public const int MaximumNameLength = 32;

    public static string NormalizeName(string? requestedName, int currentCount)
    {
        var name = requestedName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)) name = $"我的方案 {currentCount + 1}";
        return name.Length <= MaximumNameLength ? name : name[..MaximumNameLength];
    }

    public static int FindIndex(IList<ThemeArtworkPreset> presets, string name)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        for (var index = 0; index < presets.Count; index++)
        {
            if (string.Equals(
                    presets[index].Name,
                    name,
                    StringComparison.OrdinalIgnoreCase)) return index;
        }
        return -1;
    }

    public static ArtworkPresetUpsertResult Upsert(
        IList<ThemeArtworkPreset> presets,
        ThemeArtworkPreset preset)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(preset);
        var normalized = preset.Normalize();
        var existingIndex = FindIndex(presets, normalized.Name);
        if (existingIndex >= 0)
        {
            presets[existingIndex] = normalized;
            return ArtworkPresetUpsertResult.Replaced;
        }
        if (presets.Count >= MaximumPresetCount)
        {
            return ArtworkPresetUpsertResult.CapacityReached;
        }
        presets.Add(normalized);
        return ArtworkPresetUpsertResult.Added;
    }
}
