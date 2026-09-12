using AcKrovy.Core.Models;

namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// One desired automatic Hip/Valley rafter. Segment3D is the single geometric
/// authority for both persisted Line endpoints and source length.
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
    DuplicateLogicalKey,
}

public sealed record RoofAutomaticStructuralRafterPlanResult(
    bool IsValid,
    IReadOnlyList<RoofAutomaticStructuralRafterPlanItem> Items,
    RoofAutomaticStructuralRafterPlanError Error);
