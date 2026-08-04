using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Threading;
using System.Globalization;
using Tessalume.App.Infrastructure;
using Tessalume.App.Models;

namespace Tessalume.App;

public partial class ThemeQuickSwitchWindow
{
    private async void Previous_Click(object sender, RoutedEventArgs e) => await ApplyRelativeAsync(-1);

    private async void Next_Click(object sender, RoutedEventArgs e) => await ApplyRelativeAsync(1);

    private async Task ApplyRelativeAsync(int offset)
    {
        if (_switchCandidates.Count == 0)
        {
            CurrentThemeText.Text = "还没有可切换主题";
            return;
        }

        var currentIndex = _switchCandidates.Select((theme, index) => (theme, index))
            .FirstOrDefault(item => string.Equals(item.theme.ThemeId, _currentThemeId, StringComparison.OrdinalIgnoreCase)).index;
        if (!string.Equals(_switchCandidates[currentIndex].ThemeId, _currentThemeId, StringComparison.OrdinalIgnoreCase))
        {
            currentIndex = offset > 0 ? -1 : 0;
        }

        var nextIndex = (currentIndex + offset + _switchCandidates.Count) % _switchCandidates.Count;
        var theme = _switchCandidates[nextIndex];
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

}
