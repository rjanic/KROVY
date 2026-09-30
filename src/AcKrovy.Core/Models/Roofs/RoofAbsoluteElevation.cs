namespace AcKrovy.Core.Models.Roofs;

/// <summary>Which architectural elevation the user is editing for a physical roof.</summary>
public enum RoofAbsoluteElevationInputMode
{
    Eave = 0,
    Ridge = 1,
}

/// <summary>
/// Per-roof visibility of owned flattened 2D display vs physical 3D entities.
/// Independent of shared AutoCAD layers so other roofs are unaffected.
/// </summary>
public enum RoofPhysicalDisplayVisibility
{
    /// <summary>Show owned 2D plan display and physical 3D model.</summary>
    Both = 0,
    /// <summary>Show only the flattened 2D plan display entities.</summary>
    Plan2D = 1,
    /// <summary>Show only the physical 3D model entities.</summary>
    Model3D = 2,
}

/// <summary>
/// CAD-neutral absolute roof elevation state relative to the drawing/project datum.
/// Physical 3D WCS Z uses <see cref="ResolvedEaveRelativeElevationMm"/> + topology local Z
/// (decision 2A). Display formatting is derived; never round into this state.
/// </summary>
public sealed record RoofAbsoluteElevationState(
    RoofAbsoluteElevationInputMode InputMode,
    double EnteredRelativeElevationMm,
    double ResolvedEaveRelativeElevationMm,
    double ResolvedRidgeRelativeElevationMm,
    double RiseMm,
    bool Physical3DEnabled,
    RoofPhysicalDisplayVisibility DisplayVisibility = RoofPhysicalDisplayVisibility.Both,
    LowerEndCutMode LowerEndCutMode = LowerEndCutMode.Vertical,
    RidgeJoinMode RidgeJoinMode = RidgeJoinMode.Meet,
    double StructuralWidthMm = RoofStructuralPhysicalSettings.DefaultWidthMm,
    RoofStructuralHeightMode StructuralHeightMode = RoofStructuralHeightMode.Automatic,
    double StructuralExplicitHeightMm = 0d);
