namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// How owned Hip display wireframe maps topology Z into WCS for presentation only.
/// Does not change canonical <see cref="RoofTopology"/> solver output.
/// </summary>
public enum RoofDisplayProjectionKind
{
    /// <summary>Legacy spatial wireframe: WCS.Z = topologyLocalZ + drawingPlaneZ.</summary>
    SpatialLocalZ = 0,

    /// <summary>
    /// True flattened XY plan: every display point uses the same WCS.Z = drawingPlaneZ
    /// (source polyline elevation). Used when Physical3DEnabled is on.
    /// </summary>
    FlattenedDrawingPlane = 1,
}
