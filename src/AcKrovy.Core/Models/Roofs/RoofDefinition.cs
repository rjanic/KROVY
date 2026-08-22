namespace AcKrovy.Core.Models.Roofs;

/// <summary>The S1 roof aggregate: one validated footprint and neutral parameters.</summary>
public sealed class RoofDefinition
{
    public RoofDefinition(
        RoofFootprint footprint,
        RoofParameters? parameters = null,
        RoofKind kind = RoofKind.SimpleGable)
    {
        Footprint = footprint ?? throw new ArgumentNullException(nameof(footprint));
        Parameters = parameters ?? RoofParameters.Unspecified;
        Kind = kind;
    }

    public RoofFootprint Footprint { get; }

    public RoofParameters Parameters { get; }

    public RoofKind Kind { get; }
}
