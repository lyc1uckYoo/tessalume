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

}
