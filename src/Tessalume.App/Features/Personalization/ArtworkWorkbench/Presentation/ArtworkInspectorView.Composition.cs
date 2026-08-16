using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

public partial class ArtworkInspectorView
{
    private readonly Dictionary<ComboBox, (string RawToken, string DisplayText)> _placementEditorStates = [];
    private bool _placementEditing;

    internal event EventHandler? PlacementEditingStarted;

    internal event EventHandler? PlacementEditingCompleted;

    internal bool IsPlacementEditing => _placementEditing;

    internal void SetPlacement(
        ThemeArtworkPlacementSpec placement,
        ThemeArtworkCompositionMode compositionMode)
    {
        _placement = (placement ?? new ThemeArtworkPlacementSpec()).Normalize();
        _updating = true;
        try
        {
            if (!_placementEditing) RenderPlacementEditors(_placement);
            PlacementKindText.Text = compositionMode switch
            {
                ThemeArtworkCompositionMode.Legacy => "旧版兼容",
                ThemeArtworkCompositionMode.Custom => "用户构图",
                _ => "主题推荐",
            };
            ParameterOriginText.Text = compositionMode switch
            {
                ThemeArtworkCompositionMode.Legacy => "当前构图来自 schema 5 兼容值；首次取景将等价转换",
                ThemeArtworkCompositionMode.Custom => "当前构图来自个人覆盖",
                _ => "当前构图跟随主题推荐值",
            };
            LegacyCompositionPanel.Visibility = compositionMode == ThemeArtworkCompositionMode.Legacy
                ? Visibility.Visible
                : Visibility.Collapsed;
            SetPlacementValidationError(null);
        }
        finally
        {
            _updating = false;
        }
    }

    internal void SetProvenance(
        IReadOnlyDictionary<string, ThemeArtworkValueSource>? provenance)
    {
        _provenance = provenance ??
            new Dictionary<string, ThemeArtworkValueSource>(StringComparer.Ordinal);
        RenderParameterOrigin();
    }

    internal void SetPlacementValidationError(string? message)
    {
        PlacementErrorText.Text = message ?? string.Empty;
        PlacementErrorText.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void PlacementValue_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitPlacementEditors();
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void PlacementValue_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        SelectParameter(sender switch
        {
            ComboBox box when ReferenceEquals(box, PositionXValue) =>
                ArtworkParameter.PlacementX,
            ComboBox box when ReferenceEquals(box, PositionYValue) =>
                ArtworkParameter.PlacementY,
            _ => ArtworkParameter.PlacementSize,
        });
        if (_placementEditing) return;
        _placementEditing = true;
        PlacementEditingStarted?.Invoke(this, EventArgs.Empty);
    }

    private void PlacementValue_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitPlacementEditors();
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(CompletePlacementEditingWhenFocusLeaves));
    }

    private void PlacementComboBox_DropDownClosed(object sender, EventArgs e) =>
        CommitPlacementEditors();

    private void PlacementComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updating || !IsLoaded) return;
        CommitPlacementEditors();
    }

    private void CompletePlacementEditingWhenFocusLeaves()
    {
        if (!_placementEditing ||
            SizeWidthValue.IsKeyboardFocusWithin ||
            SizeHeightValue.IsKeyboardFocusWithin ||
            PositionXValue.IsKeyboardFocusWithin ||
            PositionYValue.IsKeyboardFocusWithin)
        {
            return;
        }
        _placementEditing = false;
        PlacementEditingCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void CommitPlacementEditors()
    {
        if (_updating) return;
        try
        {
            var widthToken = GetPlacementToken(SizeWidthValue);
            var heightToken = GetPlacementToken(SizeHeightValue);
            var sizeMode = widthToken switch
            {
                "cover" => ThemeArtworkSizeMode.Cover,
                "contain" => ThemeArtworkSizeMode.Contain,
                _ => ThemeArtworkSizeMode.Explicit,
            };
            var placement = new ThemeArtworkPlacementSpec
            {
                SizeMode = sizeMode,
                Width = sizeMode == ThemeArtworkSizeMode.Explicit
                    ? ThemeArtworkPlacementParser.ParseLength(widthToken)
                    : ThemeArtworkLength.Auto,
                Height = sizeMode == ThemeArtworkSizeMode.Explicit
                    ? ThemeArtworkPlacementParser.ParseLength(heightToken)
                    : ThemeArtworkLength.Auto,
                PositionX = ThemeArtworkPlacementParser.ParsePosition(
                    GetPlacementToken(PositionXValue),
                    horizontal: true),
                PositionY = ThemeArtworkPlacementParser.ParsePosition(
                    GetPlacementToken(PositionYValue),
                    horizontal: false),
                // Theme/Custom values retain their typed geometry so changing one
                // token never rewrites px/%/auto siblings. Legacy is folded before
                // display and therefore already carries identity geometry here.
                Geometry = _placement.Geometry,
            }.Normalize();
            SetPlacementValidationError(null);
            _updating = true;
            try
            {
                RenderPlacementEditors(placement);
            }
            finally
            {
                _updating = false;
            }
            if (placement == _placement) return;
            _placement = placement;
            PlacementChanged?.Invoke(this, new ArtworkPlacementChangedEventArgs(placement));
        }
        catch (FormatException exception)
        {
            SetPlacementValidationError(exception.Message);
        }
    }

    private void RenderPlacementEditors(ThemeArtworkPlacementSpec placement)
    {
        var widthToken = _responsiveCover
            ? placement.SizeMode == ThemeArtworkSizeMode.Contain ? "contain" : "cover"
            : placement.SizeMode switch
            {
                ThemeArtworkSizeMode.Contain => "contain",
                ThemeArtworkSizeMode.Explicit => ArtworkPresentationFormatter.ExactCss(placement.Width),
                _ => "cover",
            };
        var heightToken = !_responsiveCover && placement.SizeMode == ThemeArtworkSizeMode.Explicit
            ? ArtworkPresentationFormatter.ExactCss(placement.Height)
            : "auto";
        var xToken = ArtworkPresentationFormatter.ExactCss(
            placement.PositionX,
            horizontal: true);
        var yToken = ArtworkPresentationFormatter.ExactCss(
            placement.PositionY,
            horizontal: false);
        SetPlacementToken(SizeWidthValue, widthToken);
        SetPlacementToken(SizeHeightValue, heightToken);
        SetPlacementToken(PositionXValue, xToken);
        SetPlacementToken(PositionYValue, yToken);
        var widthDisplay = ArtworkPresentationFormatter.CssToken(widthToken);
        var heightDisplay = ArtworkPresentationFormatter.CssToken(heightToken);
        var xDisplay = ArtworkPresentationFormatter.CssToken(xToken);
        var yDisplay = ArtworkPresentationFormatter.CssToken(yToken);
        PlacementSummaryText.Text = _responsiveCover
            ? $"当前：等比填满 · 缩放 {ArtworkPresentationFormatter.Percent(placement.Geometry.Scale * 100d)} · " +
              $"焦点 {xDisplay} {yDisplay}"
            : $"当前：size {widthDisplay} {heightDisplay} · position {xDisplay} {yDisplay}";
    }

    private string GetPlacementToken(ComboBox comboBox)
    {
        var text = comboBox.Text.Trim();
        if (comboBox.SelectedItem is ComboBoxItem { Tag: string selectedToken } selected &&
            string.Equals(
                text,
                selected.Content?.ToString(),
                StringComparison.Ordinal))
        {
            return selectedToken.Trim().ToLowerInvariant();
        }
        if (_placementEditorStates.TryGetValue(comboBox, out var state) &&
            string.Equals(text, state.DisplayText, StringComparison.Ordinal))
        {
            return state.RawToken;
        }
        return text.ToLowerInvariant();
    }

    private void SetPlacementToken(ComboBox comboBox, string token)
    {
        var match = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag as string,
                token,
                StringComparison.OrdinalIgnoreCase));
        comboBox.SelectedItem = match;
        var display = match?.Content?.ToString() ?? ArtworkPresentationFormatter.CssToken(token);
        comboBox.Text = display;
        _placementEditorStates[comboBox] = (token.Trim().ToLowerInvariant(), display);
    }

    private void RenderParameterOrigin()
    {
        if (ParameterOriginText is null) return;
        var field = _group == ArtworkParameterGroup.Composition
            ? nameof(ThemeArtworkAdjustment.Placement)
            : GetAdjustmentFieldName(_selectedParameter);
        var source = field is not null && _provenance.TryGetValue(field, out var value)
            ? value
            : ThemeArtworkValueSource.ThemeDefault;
        ParameterOriginText.Text = source switch
        {
            ThemeArtworkValueSource.UserOverride => "当前值来自个人覆盖",
            ThemeArtworkValueSource.LegacyMigration => "当前值来自 schema 5 兼容迁移",
            ThemeArtworkValueSource.OriginalAsset => "当前使用主题原始资源",
            _ => "当前值跟随主题推荐",
        };
        ParameterOriginText.Foreground = TryFindResource(source switch
        {
            ThemeArtworkValueSource.UserOverride => "Accent",
            ThemeArtworkValueSource.LegacyMigration => "Amber",
            _ => "SubtleText",
        }) as Brush;
    }

    private static string? GetAdjustmentFieldName(ArtworkParameter parameter) => parameter switch
    {
        ArtworkParameter.Brightness => nameof(ThemeArtworkAdjustment.Brightness),
        ArtworkParameter.Contrast => nameof(ThemeArtworkAdjustment.Contrast),
        ArtworkParameter.Saturation => nameof(ThemeArtworkAdjustment.Saturation),
        ArtworkParameter.Opacity => nameof(ThemeArtworkAdjustment.Opacity),
        ArtworkParameter.Zoom or ArtworkParameter.OffsetX or ArtworkParameter.OffsetY or
        ArtworkParameter.PlacementSize or ArtworkParameter.PlacementX or
        ArtworkParameter.PlacementY =>
            nameof(ThemeArtworkAdjustment.Placement),
        ArtworkParameter.Grayscale => nameof(ThemeArtworkAdjustment.Grayscale),
        ArtworkParameter.HueRotation => nameof(ThemeArtworkAdjustment.HueRotation),
        ArtworkParameter.Blur => nameof(ThemeArtworkAdjustment.Blur),
        ArtworkParameter.OverlayColor => nameof(ThemeArtworkAdjustment.OverlayColor),
        ArtworkParameter.OverlayOpacity => nameof(ThemeArtworkAdjustment.OverlayOpacity),
        ArtworkParameter.GradientStrength => nameof(ThemeArtworkAdjustment.GradientVeil),
        ArtworkParameter.Vignette => nameof(ThemeArtworkAdjustment.Vignette),
        ArtworkParameter.BlendMode => nameof(ThemeArtworkAdjustment.BlendMode),
        ArtworkParameter.ReadabilityProtection => nameof(ThemeArtworkAdjustment.ReadabilityVeil),
        _ => null,
    };
}
