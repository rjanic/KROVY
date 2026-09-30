namespace AcKrovy.Core.Models.Roofs;

/// <summary>Independent schema for owner-scoped physical roof elevation + 3D enablement.</summary>
public static class RoofPhysicalElevationSchema
{
    public const int Version1 = 1;
    public const int Version2 = 2;
    public const int Version3 = 3;
    public const int CurrentVersion = 4;
}

public enum RoofStructuralHeightMode
{
    Automatic = 0,
    Explicit = 1,
}

public static class RoofStructuralPhysicalSettings
{
    public const double DefaultWidthMm = 120d;
}

/// <summary>
/// Canonical persisted absolute-elevation input for a roof owner. Derived ridge elevation
/// is recomputed from rise at restore; it is not a competing source of truth.
/// </summary>
public sealed record RoofPhysicalElevationData(
    int SchemaVersion,
    RoofAbsoluteElevationInputMode InputMode,
    double EnteredRelativeElevationMm,
    double ResolvedEaveRelativeElevationMm,
    bool Physical3DEnabled,
    RoofPhysicalDisplayVisibility DisplayVisibility = RoofPhysicalDisplayVisibility.Both,
    LowerEndCutMode LowerEndCutMode = LowerEndCutMode.Vertical,
    RidgeJoinMode RidgeJoinMode = RidgeJoinMode.Meet,
    double StructuralWidthMm = RoofStructuralPhysicalSettings.DefaultWidthMm,
    RoofStructuralHeightMode StructuralHeightMode = RoofStructuralHeightMode.Automatic,
    double StructuralExplicitHeightMm = 0d);

public enum RoofPhysicalElevationError
{
    None = 0,
    Missing,
    NotAuthoritativeSource,
    IncompletePayload,
    MalformedValueType,
    UnexpectedTrailingValue,
    UnsupportedSchemaVersion,
    UnsupportedInputMode,
    InvalidEnteredRelativeElevation,
    InvalidResolvedEaveRelativeElevation,
    InconsistentResolvedEave,
    UnsupportedDisplayVisibility,
    UnsupportedLowerEndCutMode,
    UnsupportedRidgeJoinMode,
    InvalidStructuralWidth,
    UnsupportedStructuralHeightMode,
    InvalidStructuralExplicitHeight,
}

public sealed record RoofPhysicalElevationValidationResult(
    bool IsValid,
    RoofPhysicalElevationData? Data,
    RoofPhysicalElevationError Error);
