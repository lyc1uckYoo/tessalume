using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Application;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

public partial class ArtworkWorkbenchView
{
    private void Inspector_NumericValueChanged(
        object? sender,
        ArtworkParameterValueChangedEventArgs e)
    {
        if (!CanEdit()) return;
        ApplyParameterMutation(
            settings => ArtworkSettingsReducer.SetParameter(
                settings,
                _mode,
                _region,
                e.Parameter,
                e.Value),
            $"调整{GetParameterDisplayName(e.Parameter)}",
            e.Parameter);
    }

    private void Inspector_TextValueChanged(
        object? sender,
        ArtworkParameterTextChangedEventArgs e)
    {
        if (!CanEdit()) return;
        ApplyDiscrete(
            settings => ArtworkSettingsReducer.SetParameter(
                settings,
                _mode,
                _region,
                e.Parameter,
                e.Value),
            $"调整{GetParameterDisplayName(e.Parameter)}");
    }

    private void Inspector_PlacementChanged(
        object? sender,
        ArtworkPlacementChangedEventArgs e)
    {
        if (!CanEdit()) return;
        if (CurrentAdjustment.CompositionMode == ThemeArtworkCompositionMode.Legacy &&
            (CurrentSlotResolution?.ThemeDefaultAdjustment.Placement is null ||
             !PreviewCanvas.SourcePixelSize.IsValid ||
             !PreviewCanvas.TargetSize.IsValid))
        {
            RenderPlacementEditor(CurrentAdjustment);
            Inspector.SetPlacementValidationError("完整原图尚未就绪，请加载后再精确编辑最终构图。");
            return;
        }
        if (!ApplyDiscrete(
                settings => ArtworkSettingsReducer.SetCustomPlacement(
                    settings,
                    _mode,
                    _region,
                    _region == ArtworkRegion.Sidebar
                        ? PreviewCanvas.SourcePixelSize.IsValid && PreviewCanvas.TargetSize.IsValid
                            ? ArtworkPlacementMapper.AdaptFixedWidthSidebar(
                                e.Placement,
                                PreviewCanvas.SourcePixelSize,
                                PreviewCanvas.TargetSize)
                            : ArtworkPlacementMapper.AdaptFixedWidthSidebar(e.Placement)
                        : e.Placement),
                "精确输入最终构图"))
        {
            return;
        }
        Notify("已按输入值更新最终 background-size / position · 可撤销");
    }

    private void Inspector_InteractionStarted(object? sender, ArtworkParameterEventArgs e)
    {
        if (!CanEdit()) return;
        EndWheelGesture();
        _session.BeginGesture(_themeId!, _settings);
    }

    private void Inspector_InteractionCompleted(object? sender, ArtworkParameterEventArgs e)
    {
        if (!CanEdit()) return;
        _session.EndGesture(_themeId!, _settings);
        UpdateHistoryActions();
    }

    private void Inspector_GroupChanged(object? sender, ArtworkParameterGroupEventArgs e)
    {
        Inspector.SetGroup(e.Group);
        if (_showOriginal) return;
        // Composition work needs the complete source and crop frame. Brightness,
        // chat masking, and effects need the final surface so every visible control
        // produces immediate visual feedback without asking users to change views.
        _canvasViewMode = e.Group == ArtworkParameterGroup.Composition
            ? ArtworkCanvasViewMode.FullSource
            : ArtworkCanvasViewMode.Result;
        RenderCanvasViewMode();
    }

    private void Inspector_ResetParameterRequested(object? sender, EventArgs e)
    {
        if (!CanEdit()) return;
        var parameter = Inspector.SelectedParameter;
        if (CurrentSlotResolution?.ThemeDefaultAdjustment is not { } themeDefault)
        {
            Notify("当前主题推荐值尚未加载，未修改任何参数");
            return;
        }
        var composition = Inspector.SelectedGroup == ArtworkParameterGroup.Composition;
        var themePlacement = themeDefault.Placement ?? new ThemeArtworkPlacementSpec();
        var editablePlacement = CurrentAdjustment.CompositionMode ==
                                ThemeArtworkCompositionMode.Legacy
            ? ResolveLegacyPlacementForEditing(CurrentAdjustment) ?? themePlacement
            : CurrentAdjustment.Placement ?? themePlacement;
        var restoredPlacement = RestorePlacementParameter(
            editablePlacement,
            themePlacement,
            parameter);
        var changed = ApplyDiscrete(
            settings => composition
                ? restoredPlacement == themePlacement.Normalize()
                    ? ArtworkSettingsReducer.RestoreGroupToTheme(
                        settings,
                        _mode,
                        _region,
                        ArtworkParameterGroup.Composition,
                        themeDefault)
                    : ArtworkSettingsReducer.SetCustomPlacement(
                        settings,
                        _mode,
                        _region,
                        restoredPlacement)
                : ArtworkSettingsReducer.RestoreParameterToTheme(
                    settings,
                    _mode,
                    _region,
                    parameter,
                    themeDefault),
            composition
                ? $"恢复{GetParameterDisplayName(parameter)}到主题推荐值"
                : $"恢复{GetParameterDisplayName(parameter)}到主题推荐值");
        if (!changed)
        {
            Notify(composition
                ? $"{GetParameterDisplayName(parameter)}已经跟随主题推荐值"
                : $"{GetParameterDisplayName(parameter)}已经跟随主题推荐值");
            return;
        }
        if (RequiresPixelProcessing(parameter)) ScheduleEffectProcessing();
        Notify(composition
            ? $"已恢复{GetParameterDisplayName(parameter)}到主题推荐值 · 可撤销"
            : $"已恢复{GetParameterDisplayName(parameter)}到主题推荐值 · 可撤销");
    }

    private static ThemeArtworkPlacementSpec RestorePlacementParameter(
        ThemeArtworkPlacementSpec current,
        ThemeArtworkPlacementSpec themeDefault,
        ArtworkParameter parameter)
    {
        var value = (current ?? new ThemeArtworkPlacementSpec()).Normalize();
        var baseline = (themeDefault ?? new ThemeArtworkPlacementSpec()).Normalize();
        return (parameter switch
        {
            ArtworkParameter.PlacementX => value with
            {
                PositionX = baseline.PositionX,
            },
            ArtworkParameter.PlacementY => value with
            {
                PositionY = baseline.PositionY,
            },
            _ => value with
            {
                SizeMode = baseline.SizeMode,
                Width = baseline.Width,
                Height = baseline.Height,
            },
        }).Normalize();
    }

    private void Inspector_ResetGroupRequested(object? sender, EventArgs e)
    {
        if (!CanEdit()) return;
        var group = Inspector.SelectedGroup;
        if (CurrentSlotResolution?.ThemeDefaultAdjustment is not { } themeDefault)
        {
            Notify("当前主题推荐值尚未加载，未修改任何参数");
            return;
        }
        if (!ApplyDiscrete(
                settings => ArtworkSettingsReducer.RestoreGroupToTheme(
                    settings,
                    _mode,
                    _region,
                    group,
                    themeDefault),
                $"恢复当前{GetGroupDisplayName(group)}到主题推荐值"))
        {
            Notify($"当前{GetGroupDisplayName(group)}已经跟随主题推荐值");
            return;
        }
        if (group is ArtworkParameterGroup.Basic or ArtworkParameterGroup.Effects)
        {
            ScheduleEffectProcessing();
        }
        Notify($"已恢复当前{GetGroupDisplayName(group)}到主题推荐值 · 可撤销");
    }

    private void Inspector_ResetRegionRequested(object? sender, EventArgs e)
    {
        if (!CanEdit()) return;
        if (CurrentSlotResolution?.ThemeDefaultAdjustment is not { } themeDefault)
        {
            Notify("当前主题推荐值尚未加载，未修改任何参数");
            return;
        }
        if (!ApplyDiscrete(
                settings => ArtworkSettingsReducer.RestoreSlotToTheme(
                    settings,
                    _mode,
                    _region,
                    themeDefault),
                "恢复当前槽位到主题推荐值"))
        {
            Notify("当前槽位已经跟随主题推荐值");
            return;
        }
        ScheduleEffectProcessing();
        Notify(
            $"已恢复{GetRegionDisplayName(_region)} · {GetModeDisplayName(_mode)}到主题推荐值" +
            " · 图片来源保持不变 · 可撤销");
    }

    private void Inspector_RestoreOriginalBaselineRequested(object? sender, EventArgs e)
    {
        if (!CanEdit()) return;
        if (!ApplyDiscrete(
            settings => ArtworkSettingsReducer.RestoreSlotToOriginalBaseline(
                settings,
                _mode,
                _region),
                "恢复当前槽位到原图基线"))
        {
            Notify("当前槽位已经使用原图基线");
            return;
        }
        ScheduleEffectProcessing();
        Notify("已切换主题原始资产，并使用完整图片、居中构图与中性效果 · 可撤销");
    }

    private void Inspector_ChooseImageRequested(object? sender, EventArgs e)
    {
        if (!CanEdit()) return;
        ChooseImageRequested?.Invoke(
            this,
            new ArtworkChooseImageEventArgs(_themeId!, _mode, _region));
    }

    private void Inspector_ClearImageRequested(object? sender, EventArgs e)
    {
        if (!CanEdit()) return;
        var current = CurrentAdjustment;
        if (string.IsNullOrWhiteSpace(current.CustomImagePath))
        {
            Notify("当前已经在使用主题原图");
            return;
        }
        _ = TrySetCustomImagePath(_themeId!, _mode, _region, null);
        Notify($"{GetRegionDisplayName(_region)}已使用主题原图 · 参数保持不变");
    }

    private void Region_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string tag }) return;
        var region = tag switch
        {
            "sidebar" => ArtworkRegion.Sidebar,
            "chat" => ArtworkRegion.Chat,
            _ => ArtworkRegion.Hero,
        };
        if (_region == region) return;
        EndWheelGesture();
        if (region != ArtworkRegion.Chat && Inspector.SelectedGroup == ArtworkParameterGroup.Mask)
        {
            _canvasViewMode = ArtworkCanvasViewMode.FullSource;
        }
        _region = region;
        UpdateResponsiveLayout(ActualWidth);
        RenderAll();
        QueuePreviewReload();
    }

    private void EditingMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string tag }) return;
        var mode = string.Equals(tag, "dark", StringComparison.Ordinal)
            ? ArtworkColorMode.Dark
            : ArtworkColorMode.Light;
        if (_mode == mode) return;
        EndWheelGesture();
        _mode = mode;
        RenderAll();
        QueuePreviewReload();
        EditingModeChanged?.Invoke(this, new ArtworkEditingModeChangedEventArgs(mode));
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private void ApplyParameterMutation(
        Func<ThemeVisualSettings, ThemeVisualSettings> mutation,
        string description,
        ArtworkParameter parameter)
    {
        var before = _settings;
        var gestureActive = _session.History.GetStatus(_themeId!).GestureActive;
        _settings = gestureActive
            ? ArtworkWorkbenchSession.UpdateGesture(_settings, mutation)
            : _session.Mutate(_themeId!, _settings, mutation);
        if (ThemeVisualSettingsSemanticComparer.Instance.Equals(_settings, before)) return;
        CompleteSettingsChange(description, recordAlreadyCreated: gestureActive);
        if (RequiresPixelProcessing(parameter))
        {
            ScheduleEffectProcessing();
        }
    }

    private bool ApplyDiscrete(
        Func<ThemeVisualSettings, ThemeVisualSettings> mutation,
        string description,
        bool reloadSource = false)
    {
        if (!CanEdit()) return false;
        EndWheelGesture();
        var before = _settings;
        _settings = _session.Mutate(_themeId!, _settings, mutation);
        if (ThemeVisualSettingsSemanticComparer.Instance.Equals(_settings, before)) return false;
        CompleteSettingsChange(description, reloadSource);
        return true;
    }

    private void CompleteSettingsChange(
        string description,
        bool reloadSource = false,
        bool recordAlreadyCreated = false)
    {
        _ = recordAlreadyCreated;
        RenderAll();
        if (reloadSource)
        {
            QueuePreviewReload();
        }
        UpdateHistoryActions();
        SetApplyState(
            ArtworkApplyState.Pending,
            _isApplied && _isCodexConnected ? "等待安全应用" : "等待写入本机配置");
        _knownThemeSettings[_themeId!] = _settings;
        SettingsChanged?.Invoke(
            this,
            new ArtworkWorkbenchSettingsChangedEventArgs(_themeId!, _settings, description));
    }

    private ThemeArtworkAdjustment CurrentAdjustment =>
        ArtworkSettingsAccessor.GetAdjustment(_settings, _mode, _region);

    private static bool RequiresPixelProcessing(ArtworkParameter parameter) => parameter is
        ArtworkParameter.Brightness or
        ArtworkParameter.Contrast or
        ArtworkParameter.Saturation or
        ArtworkParameter.Grayscale or
        ArtworkParameter.HueRotation;

    private void Notify(string message) => NotificationRequested?.Invoke(message);
}
