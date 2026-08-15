using Tessalume.App.Features.Personalization.ArtworkWorkbench.Domain;
using Tessalume.Core.Runtime;

namespace Tessalume.App.Features.Personalization.ArtworkWorkbench.Presentation;

internal sealed class ArtworkParameterValueChangedEventArgs(
    ArtworkParameter parameter,
    double value) : EventArgs
{
    public ArtworkParameter Parameter { get; } = parameter;

    public double Value { get; } = value;
}

internal sealed class ArtworkParameterTextChangedEventArgs(
    ArtworkParameter parameter,
    string value) : EventArgs
{
    public ArtworkParameter Parameter { get; } = parameter;

    public string Value { get; } = value;
}

internal sealed class ArtworkParameterBooleanChangedEventArgs(
    ArtworkParameter parameter,
    bool value) : EventArgs
{
    public ArtworkParameter Parameter { get; } = parameter;

    public bool Value { get; } = value;
}

internal sealed class ArtworkParameterEventArgs(ArtworkParameter parameter) : EventArgs
{
    public ArtworkParameter Parameter { get; } = parameter;
}

internal sealed class ArtworkParameterGroupEventArgs(ArtworkParameterGroup group) : EventArgs
{
    public ArtworkParameterGroup Group { get; } = group;
}

internal sealed class ArtworkPlacementChangedEventArgs(
    ThemeArtworkPlacementSpec placement) : EventArgs
{
    public ThemeArtworkPlacementSpec Placement { get; } = placement;
}
