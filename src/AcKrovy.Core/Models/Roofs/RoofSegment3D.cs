namespace AcKrovy.Core.Models.Roofs;

/// <summary>A deterministic CAD-neutral segment in the local roof datum.</summary>
public sealed record RoofSegment3D(RoofPoint3D Start, RoofPoint3D End)
{
    public double LengthMm => Start.DistanceTo(End);
}
