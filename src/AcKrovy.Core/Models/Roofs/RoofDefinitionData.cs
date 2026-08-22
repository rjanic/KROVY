namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// CAD-neutral persisted roof data. Schema 1 uses the legacy absolute WCS
/// direction/signature fields; schema 2 uses source-topology fields instead.
/// Schema 3 adds lock/edit state and relative generated-member overrides.
/// Schema 4 adds the stable roof kind and second face slope.
/// Schema 5 adds signed eave height difference, zB - zA.
/// </summary>
public sealed record RoofDefinitionData(
    int SchemaVersion,
    RoofKind Kind,
    double SlopeDegrees,
    double? RidgeDirectionX = null,
    double? RidgeDirectionY = null,
    string? FootprintSignature = null,
    RoofRidgeEdgeFamily? RidgeEdgeFamily = null,
    RoofRigidFootprintDescriptor? RigidFootprint = null,
    RoofEditState EditState = RoofEditState.Locked,
    IReadOnlyList<RoofGeneratedMemberOverride>? ManualOverrides = null,
    double? Face1SlopeDegrees = null,
    double EaveHeightDifferenceMm = 0d)
{
    public double Face0SlopeDegrees => SlopeDegrees;

    public double EffectiveFace1SlopeDegrees => Face1SlopeDegrees ?? SlopeDegrees;

    public IReadOnlyList<RoofGeneratedMemberOverride> Overrides =>
        ManualOverrides ?? Array.Empty<RoofGeneratedMemberOverride>();
}
