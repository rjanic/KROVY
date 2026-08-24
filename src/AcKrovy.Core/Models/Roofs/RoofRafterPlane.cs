namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// One bounded rectangular roof plane for rafter layout. Stations progress along
/// the two boundary segments; rafters run from RunStartBoundary to RunEndBoundary.
/// </summary>
public sealed record RoofRafterPlane(
    RafterRoofFace Face,
    RoofSegment3D RunStartBoundary,
    RoofSegment3D RunEndBoundary,
    double SlopeDegrees)
{
    public IReadOnlyList<RoofPoint3D> BoundaryPoints =>
        [
            RunStartBoundary.Start,
            RunStartBoundary.End,
            RunEndBoundary.End,
            RunEndBoundary.Start,
        ];
}
