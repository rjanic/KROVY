namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Host-neutral footprint source contract. AutoCAD polylines and future
/// point-by-point input both map into this value before validation.
/// </summary>
public sealed record RoofFootprintInput(
    IReadOnlyList<RoofPoint2D>? Vertices,
    bool IsClosed,
    bool HasCurvedSegments = false,
    bool IsPlanar = true);
