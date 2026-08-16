using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Infrastructure;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

public partial class ArtworkWorkbenchView
{
    private void RenderAll()
    {
        if (PreviewCanvas is null || Inspector is null) return;
        var available = CanEdit();
        HeroRegionButton.IsEnabled = available;
        SidebarRegionButton.IsEnabled = available;
        ChatRegionButton.IsEnabled = available;
        LightModeButton.IsEnabled = available;
        DarkModeButton.IsEnabled = available;
        CanvasCard.IsEnabled = available;
        InspectorScroller.IsEnabled = available;

        SetSelectionButton(HeroRegionButton, _region == ArtworkRegion.Hero);
        SetSelectionButton(SidebarRegionButton, _region == ArtworkRegion.Sidebar);
        SetSelectionButton(ChatRegionButton, _region == ArtworkRegion.Chat);
        SetSelectionButton(LightModeButton, _mode == ArtworkColorMode.Light);
        SetSelectionButton(DarkModeButton, _mode == ArtworkColorMode.Dark);

        var adjustment = CurrentAdjustment;
        PreviewCanvas.SetRegion(_region);
        PreviewCanvas.SetColorMode(_mode);
        RenderSurfaceMetrics();
        PreviewCanvas.SetComposition(
            adjustment,
            CurrentSlotResolution?.ThemeDefaultAdjustment.Placement ??
                adjustment.Placement ??
                new ThemeArtworkPlacementSpec());
        PreviewCanvas.SetGuidesVisible(GuideToggleButton.IsChecked == true);
        RenderCanvasViewMode();
        Inspector.SetRegion(_region);
        Inspector.SetAdjustment(adjustment);
        Inspector.SetFixedWidthComposition(
            _region == ArtworkRegion.Sidebar,
            responsiveCover: _region is ArtworkRegion.Hero or ArtworkRegion.Chat);
        RenderPlacementEditor(adjustment);
        Inspector.SetProvenance(CurrentSlotResolution?.Provenance);
        Inspector.SetTargetSummary(
            GetRegionDisplayName(_region),
            GetModeDisplayName(_mode));
        CanvasTitleText.Text = $"{GetRegionDisplayName(_region)} · {GetModeDisplayName(_mode)}";
        CanvasHintText.Text = _region switch
        {
            ArtworkRegion.Sidebar => "左栏固定宽度缩放；窗口高度变化时只调整纵向取景",
            ArtworkRegion.Chat => "拖动调整人物焦点，滚轮等比缩放；窗口变化自动保持原图比例",
            _ => "拖动调整人物焦点，滚轮等比缩放；窗口变化自动保持原图比例",
        };
        RenderMappingHint();
        RenderSourceSummary();
        RenderCompositionSource(adjustment);
        UpdateHistoryActions();
    }

    private void RenderCompositionSource(ThemeArtworkAdjustment adjustment)
    {
        var standardFallback = _settingsResolution is { DefaultsAreExact: false };
        CompositionSourceText.Text = standardFallback
            ? "标准预览 · 需要在线校准"
            : adjustment.CompositionMode switch
            {
                ThemeArtworkCompositionMode.Legacy => "旧版兼容构图",
                ThemeArtworkCompositionMode.Custom => "用户自定义构图",
                _ => "主题推荐构图",
            };
        CompositionSourceBadge.Background = FindBrush(
            standardFallback || adjustment.CompositionMode == ThemeArtworkCompositionMode.Legacy
                ? "AmberSoft"
                : adjustment.CompositionMode == ThemeArtworkCompositionMode.Custom
                    ? "AccentSoft"
                    : "TealSoft",
            Brushes.Transparent);
        CompositionSourceText.Foreground = FindBrush(
            standardFallback || adjustment.CompositionMode == ThemeArtworkCompositionMode.Legacy
                ? "Amber"
                : adjustment.CompositionMode == ThemeArtworkCompositionMode.Custom
                    ? "Accent"
                    : "Teal",
            Brushes.Gray);
        AutomationProperties.SetName(
            CompositionSourceBadge,
            $"当前构图来源：{CompositionSourceText.Text}" +
            (standardFallback && !string.IsNullOrWhiteSpace(_settingsResolution?.DefaultsDiagnostic)
                ? $"。{_settingsResolution.DefaultsDiagnostic}"
                : string.Empty));
        CompositionSourceBadge.ToolTip = standardFallback
            ? _settingsResolution?.DefaultsDiagnostic ??
              "主题推荐构图不可精确读取；当前使用完整图片居中的标准预览。"
            : null;
    }

    private void RenderSurfaceMetrics()
    {
        var hasMeasuredMetrics = _surfaceMetrics.TryGetValue(_region, out var measured);
        var hasLiveMetrics = hasMeasuredMetrics && measured!.IsLive;
        var metrics = hasLiveMetrics
            ? measured!
            : CreateStandardMetrics(_region);
        PreviewCanvas.SetTargetViewport(new Size(metrics.Width, metrics.Height));
        SurfaceMetricsText.Text = hasLiveMetrics
            ? $"在线实测 {metrics.Width:0.#}×{metrics.Height:0.#} · DPR {metrics.DeviceScaleFactor:0.##}"
            : _isCodexConnected
                ? $"在线待校准 · 标准预览 {metrics.Width:0.#}×{metrics.Height:0.#}"
                : $"离线标准预览 {metrics.Width:0.#}×{metrics.Height:0.#}";
        SurfaceMetricsBadge.Background = FindBrush(
            hasLiveMetrics ? "TealSoft" : "AmberSoft",
            Brushes.Transparent);
        SurfaceMetricsBadge.BorderBrush = FindBrush(
            hasLiveMetrics ? "Teal" : "Amber",
            Brushes.Gray);
        SurfaceMetricsText.Foreground = FindBrush(
            hasLiveMetrics ? "Teal" : "Amber",
            Brushes.Gray);
        var detail = hasLiveMetrics
            ? metrics.Detail ?? "来自当前 Codex 目标 surface 的 getBoundingClientRect"
            : measured?.Detail ?? (_isCodexConnected
                ? "Codex 已连接，但当前路由或注入协议无法精确测量此区域；使用标注的标准比例"
                : "Codex 未连接；使用标注的标准比例，不代表实际窗口");
        if (_settingsResolution is { DefaultsAreExact: false })
        {
            detail += "；主题构图数据为标准回退，需要在线校准。";
        }
        SurfaceMetricsBadge.ToolTip = detail;
        AutomationProperties.SetName(
            SurfaceMetricsBadge,
            $"预览区域尺寸来源：{SurfaceMetricsText.Text}。{detail}");
    }

    private static ArtworkSurfacePreviewMetrics CreateStandardMetrics(ArtworkRegion region)
    {
        var size = region switch
        {
            ArtworkRegion.Sidebar => new ArtworkSize(260d, 800d),
            ArtworkRegion.Chat => new ArtworkSize(1440d, 900d),
            _ => new ArtworkSize(1440d, 420d),
        };
        return new ArtworkSurfacePreviewMetrics(
            size.Width,
            size.Height,
            1d,
            IsLive: false,
            "离线标准预览");
    }

    private void RenderPlacementEditor(ThemeArtworkAdjustment adjustment)
    {
        var placement = adjustment.Placement ?? new ThemeArtworkPlacementSpec();
        if (adjustment.CompositionMode == ThemeArtworkCompositionMode.Legacy)
        {
            placement = ResolveLegacyPlacementForEditing(adjustment) ?? placement;
        }
        if (_region == ArtworkRegion.Sidebar)
        {
            placement = PreviewCanvas.SourcePixelSize.IsValid && PreviewCanvas.TargetSize.IsValid
                ? ArtworkPlacementMapper.AdaptFixedWidthSidebar(
                    placement,
                    PreviewCanvas.SourcePixelSize,
                    PreviewCanvas.TargetSize)
                : ArtworkPlacementMapper.AdaptFixedWidthSidebar(placement);
        }
        Inspector.SetPlacement(placement, adjustment.CompositionMode);
    }

    private ThemeArtworkPlacementSpec? ResolveLegacyPlacementForEditing(
        ThemeArtworkAdjustment adjustment)
    {
        if (CurrentSlotResolution?.ThemeDefaultAdjustment.Placement is not { } themeDefault ||
            !PreviewCanvas.SourcePixelSize.IsValid ||
            !PreviewCanvas.TargetSize.IsValid)
        {
            return null;
        }
        // Schema-five Zoom/Offset has no typed CSS representation. Only this
        // compatibility mode needs a one-time fold before exact editing. Theme
        // and Custom specs remain token-stable (%/px/auto + geometry).
        return ArtworkPlacementMapper.ConvertToCustomEquivalent(
                adjustment,
                themeDefault,
                PreviewCanvas.SourcePixelSize,
                PreviewCanvas.TargetSize,
                fixedWidthSurface: _region == ArtworkRegion.Sidebar)
            .Placement;
    }

    private ThemeArtworkSlotResolution? CurrentSlotResolution =>
        GetSlotResolution(_mode, _region);

    private ThemeArtworkSlotResolution? GetSlotResolution(
        ArtworkColorMode colorMode,
        ArtworkRegion region)
    {
        if (_settingsResolution is null) return null;
        var mode = colorMode == ArtworkColorMode.Dark
            ? _settingsResolution.Dark
            : _settingsResolution.Light;
        return region switch
        {
            ArtworkRegion.Sidebar => mode.Sidebar,
            ArtworkRegion.Chat => mode.Chat,
            _ => mode.Hero,
        };
    }

    private void RenderSourceSummary()
    {
        if (CanvasSourceText is null || Inspector is null) return;
        var adjustment = CurrentAdjustment;
        var hasStoredLocal = !string.IsNullOrWhiteSpace(adjustment.CustomImagePath);
        var sourceKind = _resolvedSource?.SourceKind;
        var summary = sourceKind switch
        {
            ArtworkImageSourceKind.LocalReplacement => "本地图片",
            ArtworkImageSourceKind.ThemeOriginal when hasStoredLocal => "主题原图 · 本地图片不可用",
            ArtworkImageSourceKind.ThemeOriginal => "主题原图",
            _ when hasStoredLocal => "本地图片 · 正在解析",
            _ => "主题原图",
        };
        CanvasSourceText.Text = summary;
        Inspector.SetSourceSummary(summary, hasStoredLocal);
    }

    private void RenderMappingHint()
    {
        if (MappingHintText is null) return;
        var adjustment = CurrentAdjustment;
        var placement = adjustment.CompositionMode == ThemeArtworkCompositionMode.Legacy
            ? ResolveLegacyPlacementForEditing(adjustment)
            : adjustment.Placement;
        var projection = PreviewCanvas.PlacementProjection;
        var hasCompositeEffects =
            !adjustment.BlendMode.Equals("normal", StringComparison.OrdinalIgnoreCase) ||
            adjustment.OverlayOpacity > 0.01d ||
            adjustment.GradientStrength > 0.01d ||
            adjustment.GradientVeil is { Enabled: true, Strength: > 0.01d } ||
            adjustment.ReadabilityVeil is { Enabled: true, Opacity: > 0.01d } ||
            adjustment.Vignette > 0.01d;
        var browserOnly = hasCompositeEffects
            ? " · 滤镜与叠层组合为本地近似，需在 Codex 中最终确认"
            : string.Empty;
        MappingHintText.Text = placement is null || projection is null
            ? "完整原图尚未加载；最终 size / position 将在同一坐标系校准后显示。"
            : $"输入 size: {ArtworkPresentationFormatter.CssValue(placement.SizeCss)} · " +
              $"position: {ArtworkPresentationFormatter.CssValue(placement.PositionCss)}；" +
              $"最终渲染 {ArtworkPresentationFormatter.Number(projection.RenderedImage.Width)}×" +
              $"{ArtworkPresentationFormatter.Number(projection.RenderedImage.Height)}px @ " +
              $"{ArtworkPresentationFormatter.Number(projection.RenderedImage.X)}, " +
              $"{ArtworkPresentationFormatter.Number(projection.RenderedImage.Y)}px" +
              $"{browserOnly}。";
    }

    private void UpdateHistoryActions()
    {
        var status = _themeId is null
            ? default
            : _session.History.GetStatus(_themeId);
        UndoButton.IsEnabled = status.CanUndo;
        RedoButton.IsEnabled = status.CanRedo;
    }

    private void RenderApplyState(ArtworkApplyState state, string detail)
    {
        if (SyncStatusText is null) return;
        _applyState = state;
        (SyncStatusText.Text, SyncStatusDetailText.Text) = state switch
        {
            ArtworkApplyState.Loading => ("正在加载", detail),
            ArtworkApplyState.Connected => ("已连接", detail),
            ArtworkApplyState.Saving => ("正在保存", detail),
            ArtworkApplyState.Pending => ("等待应用", detail),
            ArtworkApplyState.Applying => ("正在应用", detail),
            ArtworkApplyState.Applied => ("应用成功", detail),
            ArtworkApplyState.SaveFailed => ("保存失败", detail),
            ArtworkApplyState.PreviewFailed => ("预览失败", detail),
            ArtworkApplyState.ImportFailed => ("导入失败", detail),
            ArtworkApplyState.Failed => ("应用失败", detail),
            _ => ("未连接", detail),
        };
        AutomationProperties.SetName(
            SyncStatusBadge,
            $"工作台应用状态：{SyncStatusText.Text}，{SyncStatusDetailText.Text}");
        var resource = state switch
        {
            ArtworkApplyState.Applied or ArtworkApplyState.Connected => "Positive",
            ArtworkApplyState.Failed or ArtworkApplyState.SaveFailed or ArtworkApplyState.PreviewFailed or ArtworkApplyState.ImportFailed => "Danger",
            ArtworkApplyState.Loading or ArtworkApplyState.Saving or ArtworkApplyState.Pending or ArtworkApplyState.Applying => "Amber",
            _ => "SubtleText",
        };
        SyncStatusDot.Fill = FindBrush(resource, Brushes.Gray);
        SyncStatusBadge.BorderBrush = FindBrush(
            state is ArtworkApplyState.Failed or ArtworkApplyState.SaveFailed or ArtworkApplyState.PreviewFailed or ArtworkApplyState.ImportFailed
                ? "Danger"
                : "SettingsControlBorder",
            Brushes.Gray);
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateResponsiveLayout(e.NewSize.Width);

    private void UpdateResponsiveLayout(double width)
    {
        if (WorkspaceGrid is null) return;
        var stackWorkspace = width < 800;
        if (stackWorkspace)
        {
            CanvasColumn.Width = new GridLength(1, GridUnitType.Star);
            WorkspaceGapColumn.Width = new GridLength(0);
            InspectorColumn.Width = new GridLength(0);
            WorkspaceGapRow.Height = new GridLength(12);
            Grid.SetRow(InspectorScroller, 2);
            Grid.SetColumn(InspectorScroller, 0);
            PreviewCanvas.MinHeight = _region == ArtworkRegion.Sidebar ? 360 : 330;
            InspectorScroller.MaxHeight = double.PositiveInfinity;
        }
        else
        {
            CanvasColumn.Width = new GridLength(1.5, GridUnitType.Star);
            WorkspaceGapColumn.Width = new GridLength(12);
            InspectorColumn.Width = new GridLength(1, GridUnitType.Star);
            WorkspaceGapRow.Height = new GridLength(0);
            Grid.SetRow(InspectorScroller, 0);
            Grid.SetColumn(InspectorScroller, 2);
            PreviewCanvas.MinHeight = _region == ArtworkRegion.Sidebar ? 360 : 340;
            InspectorScroller.MaxHeight = double.PositiveInfinity;
        }

        SyncStatusDetailText.Visibility = width < 560
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void SetSelectionButton(Button button, bool active)
    {
        button.Tag = active ? "active" : "inactive";
        AutomationProperties.SetItemStatus(button, active ? "当前选中" : "未选中");
        button.Background = active ? FindBrush("AccentSoft", Brushes.Transparent) : Brushes.Transparent;
        button.BorderBrush = active ? FindBrush("Accent", Brushes.Transparent) : Brushes.Transparent;
        button.Foreground = FindBrush(active ? "Accent" : "MutedText", Brushes.Gray);
    }

    private Brush FindBrush(string resourceName, Brush fallback) =>
        TryFindResource(resourceName) as Brush ?? fallback;

    private static string GetRegionDisplayName(ArtworkRegion region) => region switch
    {
        ArtworkRegion.Sidebar => "左栏图片",
        ArtworkRegion.Chat => "聊天背景",
        _ => "首页横幅",
    };

    private static string GetModeDisplayName(ArtworkColorMode mode) =>
        mode == ArtworkColorMode.Dark ? "暗色" : "亮色";

    private static string GetGroupDisplayName(ArtworkParameterGroup group) => group switch
    {
        ArtworkParameterGroup.Composition => "构图",
        ArtworkParameterGroup.Effects => "效果",
        ArtworkParameterGroup.Mask => "遮罩",
        _ => "基础",
    };

    private static string GetParameterDisplayName(ArtworkParameter parameter) => parameter switch
    {
        ArtworkParameter.Brightness => "亮度",
        ArtworkParameter.Contrast => "对比度",
        ArtworkParameter.Saturation => "饱和度",
        ArtworkParameter.Opacity => "透明度",
        ArtworkParameter.Zoom => "缩放",
        ArtworkParameter.OffsetX => "水平位置",
        ArtworkParameter.OffsetY => "垂直位置",
        ArtworkParameter.PlacementSize => "图片大小",
        ArtworkParameter.PlacementX => "水平位置",
        ArtworkParameter.PlacementY => "垂直位置",
        ArtworkParameter.Grayscale => "灰度",
        ArtworkParameter.HueRotation => "色相",
        ArtworkParameter.Blur => "柔化",
        ArtworkParameter.OverlayColor => "叠色颜色",
        ArtworkParameter.OverlayOpacity => "叠色强度",
        ArtworkParameter.GradientStrength => "聊天遮罩",
        ArtworkParameter.Vignette => "暗角",
        ArtworkParameter.BlendMode => "混合模式",
        ArtworkParameter.ReadabilityProtection => "文字可读性",
        _ => "参数",
    };
}
