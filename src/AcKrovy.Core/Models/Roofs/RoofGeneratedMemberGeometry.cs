namespace AcKrovy.Core.Models.Roofs;

/// <summary>CAD-neutral start/end of one generated member in millimetres.</summary>
public readonly record struct RoofGeneratedMemberGeometry(RoofPoint3D Start, RoofPoint3D End)
{
    public double LengthMm => Start.DistanceTo(End);
}
