namespace AcKrovy.Core.Models.Roofs;

/// <summary>An edge in canonical footprint order. Indices are zero-based.</summary>
public sealed record RoofEdge(int Index, RoofPoint2D Start, RoofPoint2D End)
{
    public double LengthMm => Start.DistanceTo(End);
}
