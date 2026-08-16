namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// CAD-neutral observation of one generated rafter Line used for same-DWG COPY
/// ownership rehydration. MemberKey is an opaque host identifier (e.g. handle).
/// </summary>
public sealed record RoofGeneratedRafterGeometryObservation(
    string MemberKey,
    string EffectiveOwnerReference,
    RoofRafterGenerationRecipe Recipe,
    RafterRoofFace Face,
    int StationIndex,
    int StationCount,
    RoofPoint2D PlanStart,
    RoofPoint2D PlanEnd,
    string LayoutSignature);

/// <summary>One persisted SimpleGable roof that may need generated-set rebinding.</summary>
public sealed record RoofGeneratedRafterCopyOwnerTarget(
    string OwnerReference,
    SimpleGableRoofGeometry Geometry);

/// <summary>One uniquely matched generated set that should belong to a roof owner.</summary>
public sealed record RoofGeneratedRafterCopyAssociation(
    string OwnerReference,
    RoofRafterGenerationRecipe Recipe,
    SimpleGableRafterLayout ExpectedLayout,
    IReadOnlyList<RoofGeneratedRafterGeometryObservation> Members,
    bool RequiresMetadataRewrite);

/// <summary>Deterministic multi-roof association result. Ambiguous roofs are omitted.</summary>
public sealed record RoofGeneratedRafterCopyAssociationPlan(
    IReadOnlyList<RoofGeneratedRafterCopyAssociation> Associations);
