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
    private IReadOnlyList<ThemeCardModel> _favorites = [];
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
        IReadOnlyList<ThemeCardModel> favorites)
    {
        _currentThemeId = currentThemeId;
        _isDefaultAppearance = isDefaultAppearance;
        _favorites = favorites;
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

    private async void Previous_Click(object sender, RoutedEventArgs e) => await ApplyRelativeAsync(-1);

    private async void Next_Click(object sender, RoutedEventArgs e) => await ApplyRelativeAsync(1);

    private async Task ApplyRelativeAsync(int offset)
    {
        if (_favorites.Count == 0)
        {
            CurrentThemeText.Text = "还没有收藏主题";
            return;
        }

        var currentIndex = _favorites.Select((theme, index) => (theme, index))
            .FirstOrDefault(item => string.Equals(item.theme.ThemeId, _currentThemeId, StringComparison.OrdinalIgnoreCase)).index;
        if (!string.Equals(_favorites[currentIndex].ThemeId, _currentThemeId, StringComparison.OrdinalIgnoreCase))
        {
            currentIndex = offset > 0 ? -1 : 0;
        }

        var nextIndex = (currentIndex + offset + _favorites.Count) % _favorites.Count;
        var theme = _favorites[nextIndex];
        if (await _applyTheme(theme))
        {
            _currentThemeId = theme.ThemeId;
            AnimateThemeChange(theme.Name, offset);
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        AnimateIconRotation(RestoreIconViewbox, -360);
        await _toggleRestore();
    }

    private async void ToggleColor_Click(object sender, RoutedEventArgs e)
    {
        AnimateIconRotation(ColorIconViewbox, 180);
        var dark = await _toggleColorScheme();
        if (dark is not null)
        {
            UpdateColorMode(dark.Value);
        }
    }

    private async void ThemeQuickSwitchWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PositionAtTopCenter();
        Opacity = 0;
        if (QuickBarRoot.RenderTransform is TranslateTransform entranceTransform)
        {
            entranceTransform.Y = -12;
            entranceTransform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
        }
        var entrance = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(OpacityProperty, entrance);
        await Task.WhenAll(RefreshUsageAsync(), RefreshColorModeAsync());
        _usageTimer.Start();
    }

    private async Task RefreshColorModeAsync()
    {
        var dark = await _readColorScheme();
        if (dark is not null)
        {
            UpdateColorMode(dark.Value);
        }
    }

    private void UpdateColorMode(bool dark)
    {
        QuickMoonIcon.Visibility = dark ? Visibility.Visible : Visibility.Collapsed;
        QuickSunIcon.Visibility = dark ? Visibility.Collapsed : Visibility.Visible;
        QuickColorUnknownText.Visibility = Visibility.Collapsed;
        ColorModeButton.Background = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(dark ? "#3A8067D8" : "#385F76E4"));
        ColorModeButton.BorderBrush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(dark ? "#66A18CF0" : "#66899AF2"));
        ColorModeButton.ToolTip = dark
            ? "Codex 当前为暗色，点击切换到亮色"
            : "Codex 当前为亮色，点击切换到暗色";
    }

    private void AnimateThemeChange(string themeName, int direction)
    {
        CurrentThemeText.Text = themeName;
        var offset = direction == 0 ? 8d : Math.Sign(direction) * 16d;
        if (CurrentThemeText.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
        }

        CurrentThemeText.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.18, 1, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        CurrentThemePanel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.62, 1, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        _ = Dispatcher.InvokeAsync(PositionAtTopCenter, DispatcherPriority.Loaded);
    }

    private static void AnimateIconRotation(FrameworkElement icon, double angle)
    {
        if (icon.RenderTransform is not RotateTransform rotation)
        {
            return;
        }

        rotation.BeginAnimation(
            RotateTransform.AngleProperty,
            new DoubleAnimation(0, angle, TimeSpan.FromMilliseconds(360))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            });
    }

    private async void UsageTimer_Tick(object? sender, EventArgs e) => await RefreshUsageAsync();

    private async Task RefreshUsageAsync()
    {
        if (_readingUsage) return;
        _readingUsage = true;
        try
        {
            var usage = await _readUsage();
            if (usage is null)
            {
                ClearUsageRings();
                return;
            }

            var fiveHour = usage.Windows.FirstOrDefault(window => window.WindowDurationMinutes == 300);
            var longWindow = usage.Windows
                .Where(window => window.WindowDurationMinutes != 300)
                .OrderByDescending(window => window.WindowDurationMinutes)
                .FirstOrDefault();

            UpdateUsageRing(
                fiveHour,
                FiveHourUsagePercentText,
                FiveHourUsageArcPath,
                FiveHourUsageToolTip,
                "5 小时额度暂时不可用");
            UpdateUsageRing(
                longWindow,
                LongUsagePercentText,
                LongUsageArcPath,
                LongUsageToolTip,
                "长周期额度暂时不可用");
            LongUsageLabelText.Text = FormatCompactWindowLabel(longWindow?.WindowDurationMinutes);
        }
        catch
        {
            ClearUsageRings();
        }
        finally
        {
            _readingUsage = false;
        }
    }

    private void ClearUsageRings()
    {
        UpdateUsageRing(null, FiveHourUsagePercentText, FiveHourUsageArcPath, FiveHourUsageToolTip, "暂时无法读取 Codex 5 小时额度");
        UpdateUsageRing(null, LongUsagePercentText, LongUsageArcPath, LongUsageToolTip, "暂时无法读取 Codex 长周期额度");
        LongUsageLabelText.Text = "LONG";
    }

    private static void UpdateUsageRing(
        CodexUsageWindow? usageWindow,
        System.Windows.Controls.TextBlock percentText,
        System.Windows.Shapes.Path arcPath,
        System.Windows.Controls.ToolTip toolTip,
        string unavailableText)
    {
        if (usageWindow is null)
        {
            percentText.Text = "--";
            percentText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "QuickSecondaryText");
            arcPath.Data = Geometry.Empty;
            toolTip.Content = unavailableText;
            return;
        }

        percentText.Text = Math.Round(usageWindow.RemainingPercent)
            .ToString("0", CultureInfo.InvariantCulture);
        var stateColor = usageWindow.RemainingPercent switch
        {
            >= 80 => Color.FromRgb(92, 218, 165),
            >= 60 => Color.FromRgb(78, 205, 205),
            >= 40 => Color.FromRgb(105, 148, 244),
            >= 20 => Color.FromRgb(242, 178, 84),
            _ => Color.FromRgb(245, 92, 117),
        };
        arcPath.Stroke = new SolidColorBrush(stateColor);
        percentText.Foreground = new SolidColorBrush(stateColor);
        arcPath.Data = BuildUsageArc(usageWindow.RemainingPercent);
        arcPath.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(360))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        toolTip.Content = string.Join(
            Environment.NewLine,
            usageWindow.Label,
            $"剩余：{usageWindow.RemainingPercent:0}%",
            $"重置：{FormatResetTime(usageWindow.ResetsAt)}",
            "每分钟自动刷新");
    }

    private static string FormatCompactWindowLabel(int? durationMinutes) => durationMinutes switch
    {
        1440 => "1D",
        10080 => "7D",
        > 0 when durationMinutes % 1440 == 0 => $"{durationMinutes / 1440}D",
        > 0 when durationMinutes % 60 == 0 => $"{durationMinutes / 60}H",
        _ => "LONG",
    };

    private static Geometry BuildUsageArc(double remainingPercent)
    {
        remainingPercent = Math.Clamp(remainingPercent, 0d, 100d);
        if (remainingPercent <= 0d) return Geometry.Empty;

        const double center = 19d;
        const double radius = 16d;
        var angle = remainingPercent / 100d * 359.999d;
        var radians = angle * Math.PI / 180d;
        var endPoint = new Point(
            center + radius * Math.Sin(radians),
            center - radius * Math.Cos(radians));
        var figure = new PathFigure
        {
            StartPoint = new Point(center, center - radius),
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new ArcSegment(
            endPoint,
            new Size(radius, radius),
            0,
            angle >= 180d,
            SweepDirection.Clockwise,
            true));
        return new PathGeometry([figure]);
    }

    private static string FormatResetTime(DateTimeOffset? resetAt)
    {
        if (resetAt is null) return "时间未知";
        var remaining = resetAt.Value - DateTimeOffset.Now;
        var countdown = remaining <= TimeSpan.Zero
            ? "即将重置"
            : remaining.TotalDays >= 1
                ? $"{(int)remaining.TotalDays} 天 {remaining.Hours} 小时后"
                : remaining.TotalHours >= 1
                    ? $"{(int)remaining.TotalHours} 小时 {remaining.Minutes} 分后"
                    : $"{Math.Max(1, remaining.Minutes)} 分钟后";
        return $"{countdown}（{resetAt.Value:MM-dd HH:mm}）";
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        _showHome();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _showHome();
        Close();
    }

    private void WindowDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null)
        {
            if (source is System.Windows.Controls.Button)
            {
                return;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void PositionAtTopCenter()
    {
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - ActualWidth) / 2;
        Top = SystemParameters.WorkArea.Top + 18;
    }
}
