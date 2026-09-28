using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

public partial class ArtworkCanvasControl
{
    /// <summary>
    /// Representative page chrome for the library's offline scene preview. The
    /// artwork keeps the exact placement mapper and target viewport underneath;
    /// this independent overlay never modifies the saved image or composition.
    /// Existing workbench instances retain their original behavior by default.
    /// </summary>
    internal void SetScenePreview(bool enabled)
    {
        ScenePreviewLayer.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        RuntimeMockLayer.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        CanvasStatusBadge.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        if (!enabled)
        {
            Checkerboard.Visibility = Visibility.Visible;
            ViewportBorder.SetResourceReference(BackgroundProperty, "SettingsControlSurface");
            return;
        }
        var dark = _mode == ArtworkColorMode.Dark;
        Brush foreground = new SolidColorBrush(dark ? Color.FromRgb(248, 249, 252) : Color.FromRgb(12, 20, 31));
        Brush border = new SolidColorBrush(dark ? Color.FromArgb(175, 225, 231, 246) : Color.FromArgb(155, 29, 40, 58));
        Brush accent = new SolidColorBrush(dark ? Color.FromRgb(205, 184, 255) : Color.FromRgb(79, 54, 130));
        Brush rowSurface = new SolidColorBrush(dark ? Color.FromArgb(195, 20, 24, 36) : Color.FromArgb(215, 255, 255, 255));
        var width = _region switch
        {
            ArtworkRegion.Sidebar => 140d,
            ArtworkRegion.TaskLeft or ArtworkRegion.Memory => 146d,
            ArtworkRegion.TaskRightSecondary or ArtworkRegion.TaskRightPrimary => 156d,
            _ => 480d,
        };
        var height = width * _targetViewport.Height / _targetViewport.Width;
        ScenePreviewSurface.Width = width;
        ScenePreviewSurface.Height = height;
        ScenePreviewSurface.Children.Clear();
        ViewportBorder.Background = new SolidColorBrush(dark ? Color.FromRgb(23, 24, 31) : Color.FromRgb(246, 246, 249));
        Checkerboard.Visibility = Visibility.Collapsed;
        ScenePreviewSurface.Children.Add(_region switch
        {
            ArtworkRegion.Sidebar => CreateSidebarScene(foreground, border, accent, rowSurface),
            ArtworkRegion.Chat => CreateChatScene(foreground, border, accent),
            ArtworkRegion.TaskLeft or ArtworkRegion.Memory or ArtworkRegion.TaskRightSecondary or ArtworkRegion.TaskRightPrimary =>
                CreateCardScene(_region, foreground, border, accent, rowSurface),
            _ => CreateHomeScene(foreground, border, accent),
        });
    }

    private static Grid CreateCardScene(ArtworkRegion region, Brush foreground, Brush border, Brush accent, Brush surface)
    {
        var scene = new Grid();
        var caption = new StackPanel();
        caption.Children.Add(SceneText(region switch
        {
            ArtworkRegion.TaskLeft => "角色陪伴",
            ArtworkRegion.Memory => "记忆片段",
            ArtworkRegion.TaskRightSecondary => "灵感时刻",
            _ => "与你同行",
        }, foreground, 12, FontWeights.SemiBold));
        caption.Children.Add(SceneText(region == ArtworkRegion.Memory ? "珍藏每一次创作" : "TESSALUME", accent, 8,
            FontWeights.Normal, new Thickness(0, 4, 0, 0)));
        scene.Children.Add(new Border
        {
            Child = caption,
            Background = surface,
            BorderBrush = border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(11, 8, 9, 9),
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        return scene;
    }

    private static StackPanel CreateHomeScene(Brush foreground, Brush border, Brush accent)
    {
        var content = new StackPanel { Width = 282, Margin = new Thickness(26, 20, 0, 14), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(SceneText("TESSALUME", accent, 9, FontWeights.Bold));
        content.Children.Add(SceneText("今天，一起创造些什么？", foreground, 19, FontWeights.Bold, new Thickness(0, 5, 0, 0)));
        content.Children.Add(SceneText("打开灵感，让喜欢的角色陪伴每一次创作。", foreground, 10, FontWeights.Normal, new Thickness(0, 5, 0, 0)));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(SceneOutline("＋ 新任务", foreground, border, 10, new Thickness(9, 4, 9, 4)));
        var recent = SceneOutline("继续最近的任务", foreground, border, 10, new Thickness(9, 4, 9, 4));
        recent.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(recent);
        content.Children.Add(actions);
        return content;
    }

    private static StackPanel CreateSidebarScene(Brush foreground, Brush border, Brush accent, Brush surface)
    {
        var content = new StackPanel { Margin = new Thickness(12, 17, 12, 0) };
        content.Children.Add(SidebarSceneLabel("TESSALUME", foreground, surface, 12, FontWeights.Bold));
        var create = SceneOutline("＋ 新任务", foreground, border, 11, new Thickness(9, 8, 9, 8));
        create.Margin = new Thickness(0, 18, 0, 0);
        create.Background = surface;
        content.Children.Add(create);
        content.Children.Add(SidebarSceneLabel("收藏", accent, surface, 9, FontWeights.Bold, new Thickness(0, 20, 0, 5)));
        content.Children.Add(SidebarSceneLabel("☆ 灵感收集", foreground, surface, 11, FontWeights.Normal));
        content.Children.Add(SidebarSceneLabel("最近的任务", accent, surface, 9, FontWeights.Bold, new Thickness(0, 15, 0, 5)));
        foreach (var label in new[] { "今天的创作", "整理设计思路", "我的角色空间" })
            content.Children.Add(SidebarSceneLabel(label, foreground, surface, 11, FontWeights.Normal, new Thickness(0, 0, 0, 5)));
        return content;
    }

    private static Border SidebarSceneLabel(string text, Brush foreground, Brush surface, double size,
        FontWeight weight, Thickness margin = default) => new()
        {
            Child = SceneText(text, foreground, size, weight),
            Background = surface,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 5, 6, 5),
            Margin = margin,
        };

    private static Grid CreateChatScene(Brush foreground, Brush border, Brush accent)
    {
        var grid = new Grid { Margin = new Thickness(30, 17, 30, 19) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(SceneText("新的创作  /  场景示例", foreground, 10, FontWeights.SemiBold));
        var messages = new StackPanel { Margin = new Thickness(0, 24, 0, 16) };
        Grid.SetRow(messages, 1);
        var user = SceneOutline("把今天的灵感，变成喜欢的样子。", foreground, accent, 11, new Thickness(13, 11, 13, 11));
        user.Width = 270;
        user.HorizontalAlignment = HorizontalAlignment.Right;
        user.BorderThickness = new Thickness(1, 1, 3, 1);
        messages.Children.Add(user);
        var assistant = SceneOutline("我们可以从一个小想法开始。\n一步一步，让创作更有趣。", foreground, border, 11, new Thickness(13, 11, 13, 11));
        assistant.Width = 290;
        assistant.HorizontalAlignment = HorizontalAlignment.Left;
        assistant.BorderThickness = new Thickness(3, 1, 1, 1);
        assistant.Margin = new Thickness(0, 17, 0, 0);
        messages.Children.Add(assistant);
        grid.Children.Add(messages);
        var composer = SceneOutline("＋   描述你的想法…                                      ↑", foreground, border, 10, new Thickness(12, 12, 12, 12));
        Grid.SetRow(composer, 2);
        grid.Children.Add(composer);
        return grid;
    }

    private static TextBlock SceneText(string text, Brush foreground, double size, FontWeight weight, Thickness margin = default) => new()
    {
        Text = text,
        Foreground = foreground,
        FontSize = size,
        FontWeight = weight,
        TextWrapping = TextWrapping.Wrap,
        Margin = margin,
        FontFamily = new FontFamily("Microsoft YaHei UI"),
    };

    private static Border SceneOutline(string text, Brush foreground, Brush border, double size, Thickness padding) => new()
    {
        Child = SceneText(text, foreground, size, FontWeights.Normal),
        Background = Brushes.Transparent,
        BorderBrush = border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(7),
        Padding = padding,
    };
}
