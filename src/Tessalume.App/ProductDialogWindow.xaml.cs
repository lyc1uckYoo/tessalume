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
    internal ProductDialogWindow(
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
        var isLongMessage = message.Length > 360 || message.Count(character => character == '\n') > 7;
        Width = Math.Min(
            isLongMessage ? 520 : 440,
            Math.Max(360, SystemParameters.WorkArea.Width - 32));
        MaxHeight = Math.Min(620, Math.Max(320, SystemParameters.WorkArea.Height - 32));
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
        Resources["DialogSurfaceBrush"] = Brush(darkMode ? "#202732" : "#FBFCFF");
        Resources["DialogSurfaceAltBrush"] = Brush(darkMode ? "#2A323E" : "#F1F3FA");
        Resources["DialogBorderBrush"] = Brush(darkMode ? "#3A4557" : "#D7DDEA");
        Resources["DialogTextBrush"] = Brush(darkMode ? "#EFF2F8" : "#171B2E");
        Resources["DialogMutedBrush"] = Brush(darkMode ? "#ADB6C6" : "#5D667C");
        Resources["DialogAccentBrush"] = Brush(darkMode ? "#978BFF" : "#6558E8");
        Resources["DialogAccentSoftBrush"] = Brush(darkMode ? "#332F58" : "#EEEBFF");
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
