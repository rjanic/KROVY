namespace AcKrovy.Core.Models.Roofs;

/// <summary>Read-only observation of a database child, suitable for neutral validation.</summary>
public sealed record RoofDisplayObservation(
    string? OwnerReference,
    RoofDisplayData? Data,
    RoofDisplayDataDecodeError MetadataError,
    RoofSegment3D Segment,
    bool IsNativeLine = true);
