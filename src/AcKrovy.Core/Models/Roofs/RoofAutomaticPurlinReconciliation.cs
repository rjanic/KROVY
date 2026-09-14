namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Host-neutral observation of one entity already owned by the automatic-purlin
/// subsystem. EntityToken is opaque and is never used as logical identity.
/// </summary>
public sealed record RoofAutomaticPurlinExistingMember(
    string EntityToken,
    RoofAutomaticPurlinGeneratedKey? GeneratedKey,
    string ElementId);

public sealed record RoofAutomaticPurlinReuse(
    RoofAutomaticPurlinPlanItem Desired,
    RoofAutomaticPurlinExistingMember Existing);

public enum RoofAutomaticPurlinReconciliationError
{
    None = 0,
    InvalidDesiredItem,
    DuplicateDesiredKey,
    InvalidExistingMember,
    DuplicateExistingKey,
}

/// <summary>Complete mutation-free reconciliation decision.</summary>
public sealed record RoofAutomaticPurlinReconciliationPlan(
    IReadOnlyList<RoofAutomaticPurlinPlanItem> Create,
    IReadOnlyList<RoofAutomaticPurlinReuse> Reuse,
    IReadOnlyList<RoofAutomaticPurlinExistingMember> Stale);

public sealed record RoofAutomaticPurlinReconciliationResult(
    bool IsValid,
    RoofAutomaticPurlinReconciliationPlan? Plan,
    RoofAutomaticPurlinReconciliationError Error,
    RoofAutomaticPurlinGeneratedKey? DuplicateGeneratedKey);
