namespace AcKrovy.Core.Models.Roofs;

public sealed record RoofBoundingBox2D(
    double MinX,
    double MinY,
    double MaxX,
    double MaxY);
