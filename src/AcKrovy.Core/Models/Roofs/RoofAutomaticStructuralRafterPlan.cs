using AcKrovy.Core.Models;

namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// One desired automatic Hip/Valley rafter. Segment3D is topology-derived
/// physical geometry and true length; the persisted reference Line uses only
/// its XY projection at Z=0, independently of physical elevation.
/// </summary>
public sealed record RoofAutomaticStructuralRafterPlanItem(
    RoofStructuralLogicalKey LogicalKey,
    TimberElementType ElementType,
    RoofSegment3D Segment3D,
    TimberElementData TimberData)
{
    public double True3DLengthMm => Segment3D.LengthMm;
}

public enum RoofAutomaticStructuralRafterPlanError
{
    None = 0,
    InvalidStructuralResolution,
    UnsupportedStructuralRole,
    InvalidStructuralLength,
    InvalidStructuralWidth,
    DuplicateLogicalKey,
}

public sealed record RoofAutomaticStructuralRafterPlanResult(
    bool IsValid,
    IReadOnlyList<RoofAutomaticStructuralRafterPlanItem> Items,
    RoofAutomaticStructuralRafterPlanError Error);
