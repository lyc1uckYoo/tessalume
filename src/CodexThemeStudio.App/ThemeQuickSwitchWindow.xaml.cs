using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Threading;
using System.Globalization;
using CodexThemeStudio.App.Infrastructure;
using CodexThemeStudio.App.Models;

namespace CodexThemeStudio.App;

public partial class ThemeQuickSwitchWindow : Window
{
    private readonly Func<ThemeCardModel, Task<bool>> _applyTheme;
    private readonly Func<Task<bool>> _toggleRestore;
    private readonly Func<Task<bool?>> _toggleColorScheme;
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
        Action showHome,
        Func<Task<CodexUsageSnapshot?>> readUsage)
    {
        _applyTheme = applyTheme;
        _toggleRestore = toggleRestore;
        _toggleColorScheme = toggleColorScheme;
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
        CurrentThemeText.Text = currentThemeName;
        RestoreIconPath.Data = System.Windows.Media.Geometry.Parse(_isDefaultAppearance
            ? "M 17,8 L 20.5,8 L 20.5,4.5 M 20,8 C 18,4 14,2.5 10,3.5 C 5.5,4.5 3,9 4,13.5 C 5,18 9.5,21 14,20 C 17,19.4 19.2,17.5 20.3,15"
            : "M 7,8 L 3.5,8 L 3.5,4.5 M 4,8 C 6,4 10,2.5 14,3.5 C 18.5,4.5 21,9 20,13.5 C 19,18 14.5,21 10,20 C 7,19.4 4.8,17.5 3.7,15");
        RestoreThemeButton.ToolTip = _isDefaultAppearance ? "恢复刚刚使用的主题" : "恢复 Codex 默认外观";
        RestoreThemeButton.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter()
            .ConvertFromString(_isDefaultAppearance ? "#355A73E8" : "#2B526CE0")!;
    }

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
            CurrentThemeText.Text = theme.Name;
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        await _toggleRestore();
    }

    private async void ToggleColor_Click(object sender, RoutedEventArgs e) => await _toggleColorScheme();

    private async void ThemeQuickSwitchWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PositionAtTopCenter();
        Opacity = 0;
        var entrance = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(OpacityProperty, entrance);
        await RefreshUsageAsync();
        _usageTimer.Start();
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
            arcPath.Data = Geometry.Empty;
            toolTip.Content = unavailableText;
            return;
        }

        percentText.Text = Math.Round(usageWindow.RemainingPercent)
            .ToString("0", CultureInfo.InvariantCulture);
        arcPath.Stroke = new SolidColorBrush(usageWindow.RemainingPercent switch
        {
            >= 55 => Color.FromRgb(117, 231, 192),
            >= 25 => Color.FromRgb(255, 193, 109),
            _ => Color.FromRgb(255, 105, 145),
        });
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
