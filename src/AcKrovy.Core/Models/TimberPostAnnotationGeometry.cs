namespace AcKrovy.Core.Models;

public sealed record TimberPostAnnotationGeometry(
    TimberSlopeAnnotationPoint Anchor,
    TimberSlopeAnnotationPoint CapStart,
    TimberSlopeAnnotationPoint CapEnd,
    TimberSlopeAnnotationPoint StemEnd,
    TimberSlopeAnnotationPoint TextPosition,
    double RotationRadians);
