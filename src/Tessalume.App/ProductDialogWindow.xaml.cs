using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Tessalume.App;

public enum ProductDialogKind
{
    Information,
    Warning,
    Error,
    Confirmation,
}

public partial class ProductDialogWindow : Window
{
    private ProductDialogWindow(
        string title,
        string message,
        ProductDialogKind kind,
        bool darkMode,
        string confirmText,
        string? cancelText,
        bool dangerous)
    {
        InitializeComponent();
        ApplyTheme(darkMode);
        Title = title;
        DialogTitleText.Text = title;
        DialogMessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText ?? "取消";
        CancelButton.Visibility = cancelText is null ? Visibility.Collapsed : Visibility.Visible;

        var (icon, accent, accentSoft) = kind switch
        {
            ProductDialogKind.Error => ("!", darkMode ? "#FF829E" : "#D94C70", darkMode ? "#38232B" : "#FFF0F4"),
            ProductDialogKind.Warning => ("!", darkMode ? "#F1B85B" : "#D88A24", darkMode ? "#3A3020" : "#FFF5E7"),
            ProductDialogKind.Information => ("i", darkMode ? "#55D4D1" : "#159A9C", darkMode ? "#203B3D" : "#EAF9F7"),
            _ => ("?", darkMode ? "#978BFF" : "#675CF0", darkMode ? "#332F58" : "#EFEDFF"),
        };
        DialogIconText.Text = icon;
        DialogIconText.Foreground = Brush(accent);
        DialogIconSurface.Background = Brush(accentSoft);

        if (dangerous)
        {
            ConfirmButton.Background = Brush(darkMode ? "#D95775" : "#C83E61");
            ConfirmButton.IsDefault = false;
            CancelButton.IsDefault = true;
            Loaded += (_, _) => CancelButton.Focus();
        }
    }

    public static bool Confirm(
        Window owner,
        string title,
        string message,
        string confirmText = "确认",
        string cancelText = "取消",
        bool dangerous = false,
        bool darkMode = false)
    {
        var dialog = new ProductDialogWindow(
            title,
            message,
            ProductDialogKind.Confirmation,
            darkMode,
            confirmText,
            cancelText,
            dangerous)
        {
            Owner = owner,
        };
        return dialog.ShowDialog() == true;
    }

    public static void ShowMessage(
        Window owner,
        string title,
        string message,
        ProductDialogKind kind = ProductDialogKind.Information,
        bool darkMode = false)
    {
        var dialog = new ProductDialogWindow(
            title,
            message,
            kind,
            darkMode,
            "知道了",
            null,
            false)
        {
            Owner = owner,
        };
        dialog.ShowDialog();
    }

    private void ApplyTheme(bool darkMode)
    {
        Resources["DialogSurfaceBrush"] = Brush(darkMode ? "#202732" : "#FFFFFF");
        Resources["DialogSurfaceAltBrush"] = Brush(darkMode ? "#2A323E" : "#F3F5F9");
        Resources["DialogBorderBrush"] = Brush(darkMode ? "#3A4557" : "#DDE2EC");
        Resources["DialogTextBrush"] = Brush(darkMode ? "#EFF2F8" : "#171927");
        Resources["DialogMutedBrush"] = Brush(darkMode ? "#ADB6C6" : "#62697A");
        Resources["DialogAccentBrush"] = Brush(darkMode ? "#978BFF" : "#675CF0");
        Resources["DialogAccentSoftBrush"] = Brush(darkMode ? "#332F58" : "#EFEDFF");
    }

    private static SolidColorBrush Brush(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
