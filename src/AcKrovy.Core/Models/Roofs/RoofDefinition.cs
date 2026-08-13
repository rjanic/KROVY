namespace AcKrovy.Core.Models.Roofs;

/// <summary>The S1 roof aggregate: one validated footprint and neutral parameters.</summary>
public sealed class RoofDefinition
{
    public RoofDefinition(RoofFootprint footprint, RoofParameters? parameters = null)
    {
        Footprint = footprint ?? throw new ArgumentNullException(nameof(footprint));
        Parameters = parameters ?? RoofParameters.Unspecified;
    }

    public RoofFootprint Footprint { get; }

    public RoofParameters Parameters { get; }
}
