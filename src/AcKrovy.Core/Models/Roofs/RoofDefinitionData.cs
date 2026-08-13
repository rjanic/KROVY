namespace AcKrovy.Core.Models.Roofs;

/// <summary>Minimum CAD-neutral data needed to reconstruct a persisted roof.</summary>
public sealed record RoofDefinitionData(
    int SchemaVersion,
    RoofKind Kind,
    double SlopeDegrees,
    double RidgeDirectionX,
    double RidgeDirectionY,
    string FootprintSignature);
