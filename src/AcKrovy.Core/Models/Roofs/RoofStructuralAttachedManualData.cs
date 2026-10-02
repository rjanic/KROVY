namespace AcKrovy.Core.Models.Roofs;

public static class RoofStructuralAttachedManualDataSchema
{
    public const int IdentityVersion = 1;
    public const int PlacementVersion = 2;
    public const int CurrentVersion = IdentityVersion;
}

/// <summary>
/// How a manual Structural Hip/Valley child was created from a native CAD command.
/// </summary>
public enum RoofStructuralAttachedManualCreationKind
{
    Copy = 0,
    Mirror = 1,
    Array = 2,
    Offset = 3,
}

/// <summary>
/// Independent roof-owned manual Structural timber (COPY of Generated Hip/Valley).
/// Not a topology LogicalKey and not ordinary AttachedManual.
/// Plan2D lives on the CAD Line (Z=0). Physical rebuild uses a snapshotted rigid
/// frame (Model C): source timber axis translated by the Plan XY copy, not the
/// automatic fold. Raw BREP is not stored.
/// </summary>
public sealed record RoofStructuralAttachedManualData(
    int SchemaVersion,
    string RoofOwnerReference,
    string ManualIdentity,
    RoofStructuralRole SourceRole,
    int SourceBoundaryEdgeIdA,
    int SourceBoundaryEdgeIdB,
    RoofStructuralAttachedManualCreationKind CreationKind,
    double WidthMm,
    RoofStructuralHeightMode HeightMode,
    double? ExplicitHeightMm = null,
    RoofStructuralManualPlacement? Placement = null)
{
    public RoofStructuralLogicalKey SourceLogicalKey => new(
        SourceRole, SourceBoundaryEdgeIdA, SourceBoundaryEdgeIdB);

    public bool HasExplicitPlacement => Placement is not null;
}

/// <summary>
/// CAD-neutral rigid frame for one manual structural timber.
/// Axis is the top-face centerline. Side and Up are unit vectors.
/// SectionHeightMm is the snapshot distance along Up, not a live roof default.
/// </summary>
public sealed record RoofStructuralManualPlacement(
    double AxisStartX,
    double AxisStartY,
    double AxisStartZ,
    double AxisEndX,
    double AxisEndY,
    double AxisEndZ,
    double SideX,
    double SideY,
    double SideZ,
    double UpX,
    double UpY,
    double UpZ,
    double SectionHeightMm);

public enum RoofStructuralAttachedManualDataError
{
    None = 0,
    Missing,
    IncompletePayload,
    MalformedValueType,
    UnexpectedTrailingValue,
    UnsupportedSchemaVersion,
    MissingOwnerReference,
    MalformedOwnerReference,
    MissingManualIdentity,
    MalformedManualIdentity,
    UnsupportedRole,
    NonPositiveBoundaryEdgeId,
    SameBoundaryEdgeId,
    NonCanonicalBoundaryPair,
    NonPositiveWidth,
    UnsupportedHeightMode,
    MissingExplicitHeight,
}

public sealed record RoofStructuralAttachedManualDataValidationResult(
    bool IsValid,
    RoofStructuralAttachedManualData? Data,
    RoofStructuralAttachedManualDataError Error);
