using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Threading;
using System.Globalization;
using Tessalume.App.Infrastructure;
using Tessalume.App.Models;

namespace Tessalume.App;

public partial class ThemeQuickSwitchWindow : Window
{
    private readonly Func<ThemeCardModel, Task<bool>> _applyTheme;
    private readonly Func<Task<bool>> _toggleRestore;
    private readonly Func<Task<bool?>> _toggleColorScheme;
    private readonly Func<Task<bool?>> _readColorScheme;
    private readonly Action _showHome;
    private readonly Func<Task<CodexUsageSnapshot?>> _readUsage;
    private readonly DispatcherTimer _usageTimer;
    private IReadOnlyList<ThemeCardModel> _switchCandidates = [];
    private string? _currentThemeId;
    private bool _isDefaultAppearance;
    private bool _readingUsage;

    internal ThemeQuickSwitchWindow(
        Func<ThemeCardModel, Task<bool>> applyTheme,
        Func<Task<bool>> toggleRestore,
        Func<Task<bool?>> toggleColorScheme,
        Func<Task<bool?>> readColorScheme,
        Action showHome,
        Func<Task<CodexUsageSnapshot?>> readUsage)
    {
        _applyTheme = applyTheme;
        _toggleRestore = toggleRestore;
        _toggleColorScheme = toggleColorScheme;
        _readColorScheme = readColorScheme;
        _showHome = showHome;
        _readUsage = readUsage;
        _usageTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _usageTimer.Tick += UsageTimer_Tick;
        InitializeComponent();
        Loaded += ThemeQuickSwitchWindow_Loaded;
        Closed += (_, _) => _usageTimer.Stop();
    }

    internal void Refresh(
        string currentThemeId,
        string currentThemeName,
        bool isDefaultAppearance,
        IReadOnlyList<ThemeCardModel> switchCandidates)
    {
        _currentThemeId = currentThemeId;
        _isDefaultAppearance = isDefaultAppearance;
        _switchCandidates = switchCandidates;
        if (IsLoaded && !string.Equals(CurrentThemeText.Text, currentThemeName, StringComparison.Ordinal))
        {
            AnimateThemeChange(currentThemeName, 0);
        }
        else
        {
            CurrentThemeText.Text = currentThemeName;
        }
        if (IsLoaded)
        {
            _ = Dispatcher.InvokeAsync(PositionAtTopCenter, DispatcherPriority.Loaded);
            _ = RefreshColorModeAsync();
        }
        RestoreIconPath.Data = System.Windows.Media.Geometry.Parse(_isDefaultAppearance
            ? "M 17,8 L 20.5,8 L 20.5,4.5 M 20,8 C 18,4 14,2.5 10,3.5 C 5.5,4.5 3,9 4,13.5 C 5,18 9.5,21 14,20 C 17,19.4 19.2,17.5 20.3,15"
            : "M 7,8 L 3.5,8 L 3.5,4.5 M 4,8 C 6,4 10,2.5 14,3.5 C 18.5,4.5 21,9 20,13.5 C 19,18 14.5,21 10,20 C 7,19.4 4.8,17.5 3.7,15");
        RestoreThemeButton.ToolTip = _isDefaultAppearance ? "恢复刚刚使用的主题" : "恢复 Codex 默认外观";
        RestoreThemeButton.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter()
            .ConvertFromString(_isDefaultAppearance ? "#2C8B72D8" : "#267E67D0")!;
    }

    internal void SetShellTheme(bool dark)
    {
        var shell = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        shell.GradientStops.Add(new GradientStop(
            (Color)ColorConverter.ConvertFromString(dark ? "#F4211827" : "#FFF0F2F8"),
            0));
        shell.GradientStops.Add(new GradientStop(
            (Color)ColorConverter.ConvertFromString(dark ? "#F435243E" : "#FFE7E9F3"),
            0.52));
        shell.GradientStops.Add(new GradientStop(
            (Color)ColorConverter.ConvertFromString(dark ? "#F4241B32" : "#FFEEECF6"),
            1));
        QuickBarRoot.Background = shell;
        QuickBarRoot.BorderBrush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(dark ? "#61815B8C" : "#BAC4D0E7"));
        QuickBarTopSheen.Background = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(dark ? "#38FFFFFF" : "#B8FFFFFF"));
        QuickBarRoot.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = (Color)ColorConverter.ConvertFromString(dark ? "#120B18" : "#59637A"),
            BlurRadius = dark ? 28 : 24,
            ShadowDepth = dark ? 8 : 7,
            Opacity = dark ? 0.44 : 0.22,
        };

        SetShellBrush("QuickPrimaryText", dark ? "#FFF7FA" : "#25293B");
        SetShellBrush("QuickSecondaryText", dark ? "#B8DFD4E5" : "#697087");
        SetShellBrush("QuickIconBrush", dark ? "#FFF7FA" : "#34394F");
        SetShellBrush("QuickButtonSurface", dark ? "#0DFFFFFF" : "#F9FBFE");
        SetShellBrush("QuickButtonBorder", dark ? "#18FFFFFF" : "#C7CDDE");
        SetShellBrush("QuickButtonHover", dark ? "#29FFFFFF" : "#FFFFFF");
        SetShellBrush("QuickButtonHoverBorder", dark ? "#46FFFFFF" : "#9EA8D7");
        SetShellBrush("QuickButtonPressed", dark ? "#477E67D0" : "#D7D4F2");
        SetShellBrush("QuickPlayerSurface", dark ? "#18FFFFFF" : "#F7F8FC");
        SetShellBrush("QuickPlayerBorder", dark ? "#2AFFFFFF" : "#C2C9DD");
        SetShellBrush("QuickAccentSurface", dark ? "#287D66CF" : "#EEEAFB");
        SetShellBrush("QuickAccentBorder", dark ? "#397F70D6" : "#C8C0ED");
        SetShellBrush("QuickColorSurface", dark ? "#2A9B6AD1" : "#E8EAFB");
        SetShellBrush("QuickColorBorder", dark ? "#4AAF86E4" : "#BFC5EE");
        SetShellBrush("QuickHomeSurface", dark ? "#245E6FCA" : "#EAF1FA");
        SetShellBrush("QuickHomeBorder", dark ? "#3E8192E5" : "#B9CBE2");
        SetShellBrush("QuickCloseSurface", dark ? "#36D65B7E" : "#FFF0F3");
        SetShellBrush("QuickCloseBorder", dark ? "#66E47B9B" : "#F0A3B4");
        SetShellBrush("QuickCloseIcon", dark ? "#FFD9E4" : "#D04466");
        SetShellBrush("QuickRingSurface", dark ? "#0DFFFFFF" : "#F9FAFD");
        SetShellBrush("QuickRingBorder", dark ? "#22FFFFFF" : "#C3CADC");
        SetShellBrush("QuickRingTrack", dark ? "#30FFFFFF" : "#9EA7BD");
        SetShellBrush("QuickSeparator", dark ? "#30FFFFFF" : "#9DA5B8");
        SetShellBrush("QuickPanelBorder", dark ? "#3AFFFFFF" : "#B5BDD5");
        SetShellBrush("QuickBadgeSurface", dark ? "#18FFFFFF" : "#F8FAFD");
        SetShellBrush("QuickBadgeBorder", dark ? "#2FFFFFFF" : "#C3CADB");
        SetShellBrush("QuickTooltipSurface", dark ? "#F4291F33" : "#FAF8F9FE");
        SetShellBrush("QuickTooltipBorder", dark ? "#685273" : "#B8C0D8");
        Resources["QuickPanelStop0"] = (Color)ColorConverter.ConvertFromString(dark ? "#24FFFFFF" : "#F2FFFFFF");
        Resources["QuickPanelStop1"] = (Color)ColorConverter.ConvertFromString(dark ? "#32E05DA4" : "#B7DAD4F5");
        Resources["QuickPanelStop2"] = (Color)ColorConverter.ConvertFromString(dark ? "#28826AD8" : "#A5C8CFF4");
        Resources["QuickPanelStop3"] = (Color)ColorConverter.ConvertFromString(dark ? "#14FFFFFF" : "#DEFFFFFF");
    }

    private void SetShellBrush(string key, string color) =>
        Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

}
