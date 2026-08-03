using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Tessalume.App;

public partial class FirstRunWindow : Window
{
    private FirstRunWindow(bool darkMode, bool codexInstalled)
    {
        InitializeComponent();
        ApplyTheme(darkMode);
        if (codexInstalled)
        {
            CodexStatusText.Text = "已检测到 Windows 版 Codex Desktop";
            CodexStatusHintText.Text = "选择主题后即可建立本机连接并应用。";
            CodexStatusDot.Fill = Brush(darkMode ? "#55D6A6" : "#24B987");
        }
        else
        {
            CodexStatusText.Text = "暂未检测到 Windows 版 Codex Desktop";
            CodexStatusHintText.Text = "仍可先浏览主题；安装 Codex 后再应用即可。";
            CodexStatusDot.Fill = Brush(darkMode ? "#F1B85B" : "#D88A24");
        }

        Loaded += (_, _) => ContinueButton.Focus();
    }

    public static bool Show(Window owner, bool darkMode, bool codexInstalled)
    {
        var window = new FirstRunWindow(darkMode, codexInstalled) { Owner = owner };
        return window.ShowDialog() == true;
    }

    private void ApplyTheme(bool darkMode)
    {
        Resources["WelcomeSurface"] = Brush(darkMode ? "#202732" : "#FFFFFF");
        Resources["WelcomeSurfaceAlt"] = Brush(darkMode ? "#2A323E" : "#F6F7FB");
        Resources["WelcomeBorder"] = Brush(darkMode ? "#3A4557" : "#DDE2EC");
        Resources["WelcomeText"] = Brush(darkMode ? "#EFF2F8" : "#171927");
        Resources["WelcomeMuted"] = Brush(darkMode ? "#ADB6C6" : "#62697A");
        Resources["WelcomeSubtle"] = Brush(darkMode ? "#858FA1" : "#9299AA");
        Resources["WelcomeAccent"] = Brush(darkMode ? "#978BFF" : "#675CF0");
        Resources["WelcomeAccentSoft"] = Brush(darkMode ? "#332F58" : "#EFEDFF");
        Resources["WelcomePositive"] = Brush(darkMode ? "#55D6A6" : "#24B987");
        Resources["WelcomePositiveSoft"] = Brush(darkMode ? "#203B34" : "#EAF9F4");
        Resources["WelcomeAmber"] = Brush(darkMode ? "#F1B85B" : "#D88A24");
        Resources["WelcomeAmberSoft"] = Brush(darkMode ? "#3A3020" : "#FFF5E7");
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

    private void Continue_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Exit_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
