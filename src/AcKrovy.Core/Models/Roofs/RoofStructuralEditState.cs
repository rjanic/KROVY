namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Owner-local semantic overrides. Missing absolute Plan endpoints means the member
/// uses planar OffsetXmm/OffsetYmm translation of the canonical fold (MOVE).
/// Absolute PlanStart/PlanEnd (Z forced to 0 on apply) represent user geometry edits
/// such as STRETCH/TRIM/EXTEND/ROTATE. Suppression survives reference deletion.
/// No entity identity is stored.
/// </summary>
public sealed record RoofStructuralMemberEdit(
    RoofStructuralLogicalKey LogicalKey,
    double OffsetXmm,
    double OffsetYmm,
    bool Suppressed,
    double? PlanStartXmm = null,
    double? PlanStartYmm = null,
    double? PlanEndXmm = null,
    double? PlanEndYmm = null)
{
    public bool HasAbsolutePlan =>
        PlanStartXmm is not null && PlanStartYmm is not null &&
        PlanEndXmm is not null && PlanEndYmm is not null;
}

public sealed record RoofStructuralEditState(int SchemaVersion, IReadOnlyList<RoofStructuralMemberEdit> Members)
{
    public static RoofStructuralEditState Empty => new(1, Array.Empty<RoofStructuralMemberEdit>());
}
