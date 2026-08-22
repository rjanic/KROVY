namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// One bounded rectangular gable face. Boundary points are ordered so the
/// implied face normal has a positive Z component.
/// </summary>
public sealed class SimpleGableRoofFace
{
    internal SimpleGableRoofFace(
        int index,
        SimpleGableRoofFaceSide side,
        RoofSegment3D eave,
        IReadOnlyList<RoofPoint3D> boundaryPoints,
        double runMm,
        double slopeDegrees)
    {
        Index = index;
        Side = side;
        Eave = eave;
        BoundaryPoints = boundaryPoints.ToArray();
        RunMm = runMm;
        SlopeDegrees = slopeDegrees;
    }

    public int Index { get; }

    public SimpleGableRoofFaceSide Side { get; }

    public RoofSegment3D Eave { get; }

    public IReadOnlyList<RoofPoint3D> BoundaryPoints { get; }

    public double RunMm { get; }

    public double SlopeDegrees { get; }
}
