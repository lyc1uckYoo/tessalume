using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using Tessalume.Core.Runtime;
using Tessalume.Core.Themes;

namespace Tessalume.App;

public partial class MainWindow
{
    private async Task ResolveThemeArtworkDefaultsAsync(CancellationToken cancellationToken = default)
    {
        SnapshotVisualOverrides();
        var packages = _themes
            .Select(theme => theme.CatalogItem.Package)
            .Where(package => package is not null)
            .Cast<ThemePackage>()
            .ToArray();
        var loaded = await Task.WhenAll(packages.Select(async package =>
            (Package: package, Result: await _artworkDefaultsStore.LoadAsync(
                package,
                cancellationToken))));

        _themeArtworkDefaults.Clear();
        _themeArtworkDefaultLoads.Clear();
        _themeVisualResolutions.Clear();
        _themeVisualSettings.Clear();
        foreach (var (package, result) in loaded)
        {
            var themeId = package.Manifest.Id;
            _themeArtworkDefaults[themeId] = result.Defaults;
            _themeArtworkDefaultLoads[themeId] = result;
            _themeVisualOverrides.TryGetValue(themeId, out var userOverrides);
            var resolution = AddDefaultsLoadStatus(
                themeId,
                ThemeArtworkSettingsResolver.Resolve(result.Defaults, userOverrides));
            _themeVisualResolutions[themeId] = resolution;
            _themeVisualSettings[themeId] = resolution.Settings;
        }
    }

    private Dictionary<string, ThemeVisualSettingsOverride> SnapshotVisualOverrides()
    {
        foreach (var (themeId, settings) in _themeVisualSettings)
        {
            _themeArtworkDefaults.TryGetValue(themeId, out var defaults);
            var sparse = ThemeArtworkSettingsResolver.CreateSparseOverride(
                defaults ?? CreateStandardArtworkDefaults(themeId),
                settings);
            if (sparse.IsEmpty)
            {
                _themeVisualOverrides.Remove(themeId);
            }
            else
            {
                _themeVisualOverrides[themeId] = sparse;
            }
        }
        return _themeVisualOverrides;
    }

    private ThemeVisualSettingsResolution ResolveVisualSettings(
        string themeId,
        ThemeVisualSettingsOverride? userOverrides = null)
    {
        _themeArtworkDefaults.TryGetValue(themeId, out var defaults);
        if (userOverrides is null)
        {
            _themeVisualOverrides.TryGetValue(themeId, out userOverrides);
        }
        var resolution = AddDefaultsLoadStatus(
            themeId,
            ThemeArtworkSettingsResolver.Resolve(
                defaults ?? CreateStandardArtworkDefaults(themeId),
                userOverrides));
        _themeVisualResolutions[themeId] = resolution;
        _themeVisualSettings[themeId] = resolution.Settings;
        return resolution;
    }

    private void SetResolvedVisualSettings(string themeId, ThemeVisualSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);
        ArgumentNullException.ThrowIfNull(settings);
        _themeArtworkDefaults.TryGetValue(themeId, out var defaults);
        var sparse = ThemeArtworkSettingsResolver.CreateSparseOverride(
            defaults ?? CreateStandardArtworkDefaults(themeId),
            settings);
        if (sparse.IsEmpty)
        {
            _themeVisualOverrides.Remove(themeId);
        }
        else
        {
            _themeVisualOverrides[themeId] = sparse;
        }
        ResolveVisualSettings(themeId, sparse);
    }

    private ThemeVisualSettingsResolution GetVisualSettingsResolution(string themeId)
    {
        if (_themeVisualResolutions.TryGetValue(themeId, out var resolution)) return resolution;
        return ResolveVisualSettings(themeId);
    }

    private ThemeVisualSettingsResolution AddDefaultsLoadStatus(
        string themeId,
        ThemeVisualSettingsResolution resolution)
    {
        if (_themeArtworkDefaultLoads.TryGetValue(themeId, out var load))
        {
            return resolution with
            {
                DefaultsAreExact = load.IsExact,
                DefaultsDiagnostic = load.Diagnostic,
            };
        }
        return resolution with
        {
            DefaultsAreExact = false,
            DefaultsDiagnostic = "标准预览，需要在线校准。",
        };
    }

    private static ThemeArtworkDefaultsDocument CreateStandardArtworkDefaults(string themeId) =>
        ArtworkThemeDefaultsLoadResult.StandardFallback(
            themeId,
            "标准预览，需要在线校准。").Defaults;
}
