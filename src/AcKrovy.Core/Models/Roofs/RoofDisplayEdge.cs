namespace AcKrovy.Core.Models.Roofs;

/// <summary>CAD-neutral line required by the permanent simple-gable display.</summary>
public sealed record RoofDisplayEdge(
    RoofDisplayEdgeRole Role,
    RoofSegment3D Segment);
