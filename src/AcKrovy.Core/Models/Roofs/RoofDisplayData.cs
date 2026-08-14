namespace AcKrovy.Core.Models.Roofs;

/// <summary>Portable ownership and regeneration data attached to one display line.</summary>
public sealed record RoofDisplayData(
    int SchemaVersion,
    string OwnerReference,
    RoofDisplayEdgeRole Role,
    string GenerationSignature);
