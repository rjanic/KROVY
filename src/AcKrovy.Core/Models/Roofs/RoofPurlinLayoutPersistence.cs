namespace AcKrovy.Core.Models.Roofs;

/// <summary>Independent owner-scoped automatic-purlin layout schema.</summary>
public static class RoofPurlinLayoutSchema
{
    /// <summary>Legacy layouts without manual rafter profile trailer.</summary>
    public const int Version1 = 1;

    /// <summary>
    /// Manual rafter W×H + source policy without acknowledged-actual snapshot.
    /// </summary>
    public const int Version2 = 2;

    /// <summary>
    /// Current write version. Extends schema-2 with durable acknowledged-actual
    /// rafter profile for conflict tracking. WallPlate LowerEdge stash uses an
    /// extended optional trailer length within this version (not a schema bump).
    /// </summary>
    public const int CurrentVersion = 3;

    public static bool IsSupported(int schemaVersion) =>
        schemaVersion is Version1 or Version2 or CurrentVersion;
}

/// <summary>
/// Durable policy for which rafter cross-section drives automatic-purlin planning.
/// Never stores AutoCAD ObjectIds.
/// </summary>
public enum RoofAutomaticPurlinRafterSourcePolicy
{
    /// <summary>No durable choice. Legacy schema-1 default.</summary>
    Unset = 0,

    /// <summary>Use persisted manual W×H when valid.</summary>
    PreferManual = 1,

    /// <summary>Use recovered/selected actual roof rafter W×H when available.</summary>
    PreferActual = 2,
}

/// <summary>
/// Durable snapshot of the actual rafter profile acknowledged by the most recent
/// explicit source decision. Never invents a snapshot for legacy schema-2 data.
/// </summary>
public enum RoofAutomaticPurlinAcknowledgedActualKind
{
    /// <summary>No acknowledgement exists (legacy schema-2 / unread).</summary>
    Unspecified = 0,

    /// <summary>Decision made when no actual roof rafters existed.</summary>
    NonePresent = 1,

    /// <summary>User explicitly acknowledged this actual W×H.</summary>
    ExplicitProfile = 2,
}

/// <summary>Typed semantic fields decoded from one repeated layout-row payload.</summary>
public sealed record RoofPurlinLayoutStoredItem(
    string LayoutItemId,
    int EnabledValue,
    string PlacementToken,
    double PlacementValueMm,
    string ReferenceRidgeRoleToken,
    int ReferenceRidgeBoundaryEdgeIdA,
    int ReferenceRidgeBoundaryEdgeIdB,
    string SeatingDepthToken,
    double SeatingDepthValue,
    double WidthMm = 0d,
    double HeightMm = 0d);

/// <summary>
/// Optional section-dimensions trailer. Zero values mean "use defaults".
/// </summary>
public sealed record RoofPurlinLayoutStoredSectionDimensions(
    double WallPlateWidthMm,
    double WallPlateHeightMm,
    double RidgeWidthMm,
    double RidgeHeightMm);

/// <summary>
/// Optional rafter-profile trailer: durable manual W×H, source policy, and
/// acknowledged-actual snapshot (schema-3). Schema-2 payloads omit acknowledgement
/// fields (kind Unspecified, dimensions zero).
/// </summary>
public sealed record RoofPurlinLayoutStoredRafterProfile(
    int SourcePolicyValue,
    double ManualWidthMm,
    double ManualHeightMm,
    int AcknowledgedActualKindValue = 0,
    double AcknowledgedActualWidthMm = 0d,
    double AcknowledgedActualHeightMm = 0d);

public enum RoofPurlinLayoutPersistenceError
{
    None = 0,
    Missing,
    NotAuthoritativeSource,
    IncompletePayload,
    MalformedValueType,
    UnexpectedTrailingValue,
    UnsupportedSchemaVersion,
    InvalidRidgeEnabled,
    InvalidWallPlateEnabled,
    InvalidWallPlateLowerEdgeHeight,
    InvalidItemCount,
    InvalidEnabled,
    EmptyLayoutItemId,
    MalformedLayoutItemId,
    NonCanonicalLayoutItemId,
    DuplicateLayoutItemId,
    InvalidPlacementToken,
    InvalidPlacementValue,
    InvalidReferenceRidge,
    InvalidSeatingDepthToken,
    InvalidSeatingDepth,
    InvalidSectionDimension,
    InvalidManualRafterProfile,
    InvalidRafterSourcePolicy,
    InvalidAcknowledgedActualProfile,
}

public sealed record RoofPurlinLayoutValidationResult(
    bool IsValid,
    RoofAutomaticPurlinLayout? Layout,
    RoofPurlinLayoutPersistenceError Error);
