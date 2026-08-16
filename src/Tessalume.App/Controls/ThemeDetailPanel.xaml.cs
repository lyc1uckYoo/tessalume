using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tessalume.App.Models;

namespace Tessalume.App.Controls;

public partial class ThemeDetailPanel : UserControl
{
    private const string CompanionThemeId = "aemeath.star-voyage";

    internal ThemeCardModel? Theme { get; private set; }

    public event EventHandler? CloseRequested;

    public event EventHandler? ApplyRequested;

    public event EventHandler? OpenFolderRequested;

    public event EventHandler? CompanionPetRequested;

    public ThemeDetailPanel()
    {
        InitializeComponent();
    }

    internal void Present(ThemeCardModel theme)
    {
        Theme = theme;
        ThemeTypeText.Text = theme.TypeLabel;
        ThemeNameText.Text = theme.Name;
        ThemeVersionText.Text = $"v{theme.Version}";
        ThemeDescriptionText.Text = string.IsNullOrWhiteSpace(theme.Description)
            ? "这个主题没有提供说明。"
            : theme.Description;
        CapabilityText.Text = theme.CapabilityLabel;
        TemplateText.Text = $"Template {theme.TemplateLabel}";
        AuthorText.Text = theme.Author;
        ThemeIdText.Text = theme.ThemeId ?? "无效主题";
        StorageText.Text = theme.StorageSummary;
        DirectoryText.Text = theme.DirectoryPath;
        DirectoryText.ToolTip = theme.DirectoryPath;
        AppliedBadge.Visibility = theme.IsApplied ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.IsEnabled = theme.IsValid;
        ApplyButton.Content = theme.IsApplied ? "重新应用主题" : "应用到 Codex";
        CompanionPetButton.Visibility = string.Equals(
            theme.ThemeId,
            CompanionThemeId,
            StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

        SetPreview(LightPreviewImage, LightFallback, theme.LightPreview, theme.PreviewAlignmentX);
        SetPreview(DarkPreviewImage, DarkFallback, theme.DarkPreview, theme.PreviewAlignmentX);
        Focus();
    }

    private static void SetPreview(
        System.Windows.Shapes.Rectangle target,
        FrameworkElement fallback,
        ImageSource? source,
        AlignmentX alignment)
    {
        target.Fill = source is null
            ? null
            : new ImageBrush(source)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = alignment,
                AlignmentY = AlignmentY.Center,
            };
        target.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        fallback.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void ApplyButton_Click(object sender, RoutedEventArgs e) =>
        ApplyRequested?.Invoke(this, EventArgs.Empty);

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e) =>
        OpenFolderRequested?.Invoke(this, EventArgs.Empty);

    private void CompanionPetButton_Click(object sender, RoutedEventArgs e) =>
        CompanionPetRequested?.Invoke(this, EventArgs.Empty);
}
