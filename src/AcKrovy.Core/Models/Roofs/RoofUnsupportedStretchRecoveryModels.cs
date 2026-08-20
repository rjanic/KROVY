namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// CAD-neutral pre-command assembly snapshot for Unsupported STRETCH Auto-Recovery.
/// Transient runtime state only — never persisted.
/// </summary>
public sealed record RoofUnsupportedStretchSourceSnapshotData(
    string OwnerHandle,
    IReadOnlyList<RoofPoint2D> Vertices,
    bool IsClosed,
    double ElevationMm,
    double NormalX,
    double NormalY,
    double NormalZ);

public sealed record RoofUnsupportedStretchTimberLineSnapshotData(
    string EntityHandle,
    string ElementId,
    string SourceHandle,
    RoofPoint3D Start,
    RoofPoint3D End);

public enum RoofUnsupportedStretchAnnotationKind
{
    Unknown = 0,
    Line = 1,
    Polyline = 2,
    MText = 3,
    DBText = 4,
    MLeader = 5,
    BlockReference = 6,
    Circle = 7,
}

public enum RoofUnsupportedStretchMLeaderContentKind
{
    Unknown = 0,
    NoneContent = 1,
    MTextContent = 2,
    BlockContent = 3,
}

public sealed record RoofUnsupportedStretchAnnotationSnapshotData(
    string EntityHandle,
    string SourceHandle,
    RoofUnsupportedStretchAnnotationKind Kind,
    RoofPoint3D? Position,
    double? Rotation,
    RoofPoint3D? SecondaryPoint,
    RoofPoint3D? TertiaryPoint,
    RoofPoint3D? QuaternaryPoint,
    double? SecondaryScalar,
    IReadOnlyList<RoofPoint2D>? PolylineVertices,
    IReadOnlyList<double>? PolylineBulges,
    bool? PolylineClosed,
    double? ElevationMm,
    int? MLeaderLeaderIndex = null,
    int? MLeaderLeaderLineIndex = null,
    RoofUnsupportedStretchMLeaderContentKind MLeaderContentKind =
        RoofUnsupportedStretchMLeaderContentKind.Unknown,
    bool? MLeaderEnableDogleg = null);

public sealed record RoofUnsupportedStretchAssemblySnapshotData(
    RoofUnsupportedStretchSourceSnapshotData RoofSource,
    IReadOnlyList<RoofUnsupportedStretchTimberLineSnapshotData> TimberLines,
    IReadOnlyList<RoofUnsupportedStretchAnnotationSnapshotData> Annotations);

public enum RoofUnsupportedStretchRecoveryOutcome
{
    NotApplicable = 0,
    Recovered = 1,
    Unavailable = 2,
    HardFailure = 3,
}
