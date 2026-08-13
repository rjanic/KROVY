namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// CAD-neutral persisted roof data. Schema 1 uses the legacy absolute WCS
/// direction/signature fields; schema 2 uses source-topology fields instead.
/// </summary>
public sealed record RoofDefinitionData(
    int SchemaVersion,
    RoofKind Kind,
    double SlopeDegrees,
    double? RidgeDirectionX = null,
    double? RidgeDirectionY = null,
    string? FootprintSignature = null,
    RoofRidgeEdgeFamily? RidgeEdgeFamily = null,
    RoofRigidFootprintDescriptor? RigidFootprint = null);
