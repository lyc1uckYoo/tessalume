using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tessalume.App.Infrastructure;
using Tessalume.Core.Themes;

namespace Tessalume.App.Models;

internal sealed class ThemeCardModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isFavorite;
    private bool _isApplied;
    private BitmapImage? _preview;
    private readonly BitmapImage? _lightPreview;
    private readonly BitmapImage? _darkPreview;

    public ThemeCardModel(
        ThemeCatalogItem item,
        bool isFavorite = false,
        bool loadPreview = true)
    {
        CatalogItem = item;
        Name = item.Package?.Manifest.Name ?? Path.GetFileName(item.Directory);
        Description = item.Package?.Manifest.Description ?? "主题包存在错误，请打开诊断查看。";
        Version = item.Package?.Manifest.Version ?? "无效";
        Author = item.Package?.Manifest.Author ?? "未知作者";
        IsValid = item.Validation.IsValid;
        SupportsLight = item.Package?.Manifest.Capabilities.Light == true;
        SupportsDark = item.Package?.Manifest.Capabilities.Dark == true;
        PreviewAlignmentX = ResolvePreviewAlignment(item.Package?.Manifest.Config);
        if (loadPreview)
        {
            _lightPreview = CreatePreview(item.Package?.PreviewLightPath);
            _darkPreview = CreatePreview(item.Package?.PreviewDarkPath);
            _preview = _lightPreview ?? _darkPreview;
        }
        (FallbackBackground, FallbackAccent, FallbackPanel) = CreateFallbackPalette(
            item.Package?.Manifest.Id ?? Name);
        _isFavorite = isFavorite;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ThemeCatalogItem CatalogItem { get; }

    public string Name { get; }

    public string Description { get; }

    public string Version { get; }

    public string Author { get; }

    public bool IsValid { get; }

    public bool SupportsLight { get; }

    public bool SupportsDark { get; }

    public bool IsAdvanced => CatalogItem.Package?.IsAdvanced == true;

    public bool IsBuiltIn => BuiltInAssetInstaller.IsBuiltInTheme(CatalogItem.Package?.Manifest.Id);

    public bool CanDelete => Directory.Exists(CatalogItem.Directory);

    public string TypeLabel => IsBuiltIn ? "内置主题" : "本地主题";

    public bool HasPreview => Preview is not null;

    public AlignmentX PreviewAlignmentX { get; }

    public string? ThemeId => CatalogItem.Package?.Manifest.Id;

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
            {
                return;
            }

            _isFavorite = value;
            OnPropertyChanged();
        }
    }

    public Brush FallbackBackground { get; }

    public Brush FallbackAccent { get; }

    public Brush FallbackPanel { get; }

    public BitmapImage? Preview
    {
        get => _preview;
        private set
        {
            if (ReferenceEquals(_preview, value))
            {
                return;
            }

            _preview = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPreview));
        }
    }

    public void SetDarkMode(bool dark) =>
        Preview = dark
            ? _darkPreview ?? _lightPreview
            : _lightPreview ?? _darkPreview;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsApplied
    {
        get => _isApplied;
        set
        {
            if (_isApplied == value)
            {
                return;
            }

            _isApplied = value;
            OnPropertyChanged();
        }
    }

    private static BitmapImage? CreatePreview(string? previewPath)
    {
        if (previewPath is null || !File.Exists(previewPath))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            // Cards are about 400 device-independent pixels wide. Decoding a
            // 2K/4K source at 900 px kept tens of megabytes of unused pixels
            // alive for the lifetime of the main window.
            image.DecodePixelWidth = 480;
            image.UriSource = new System.Uri(previewPath, System.UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is NotSupportedException or IOException or FileFormatException)
        {
            // A third-party theme must never prevent the application from starting.
            // Unsupported or damaged previews simply fall back to the generated card artwork.
            return null;
        }
    }

    private static AlignmentX ResolvePreviewAlignment(
        Dictionary<string, JsonElement>? config)
    {
        if (config is null ||
            !config.TryGetValue("previewFocusX", out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return AlignmentX.Center;
        }

        return value.GetString()?.Trim().ToLowerInvariant() switch
        {
            "left" => AlignmentX.Left,
            "right" => AlignmentX.Right,
            _ => AlignmentX.Center,
        };
    }

    private static (Brush Background, Brush Accent, Brush Panel) CreateFallbackPalette(string id)
    {
        var ocean = id.Contains("ocean", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("terminal", StringComparison.OrdinalIgnoreCase);
        var background = new LinearGradientBrush(
            ocean ? Color.FromRgb(214, 235, 238) : Color.FromRgb(232, 223, 234),
            ocean ? Color.FromRgb(229, 235, 240) : Color.FromRgb(240, 232, 237),
            new Point(0, 0),
            new Point(1, 1));
        var accent = new SolidColorBrush(
            ocean ? Color.FromRgb(47, 133, 152) : Color.FromRgb(139, 107, 156));
        var panel = new SolidColorBrush(
            ocean ? Color.FromRgb(244, 251, 252) : Color.FromRgb(255, 249, 252));
        background.Freeze();
        accent.Freeze();
        panel.Freeze();
        return (background, accent, panel);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
