namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// One persistent intermediate-purlin layout row. LayoutItemId is an opaque logical
/// identity and is deliberately independent of row order and placement value.
/// </summary>
public enum RoofAutomaticPurlinPlacementMode
{
    BottomEdgeHeightAboveReference = 1,
    PlanDistanceFromEave = 2,
    PlanDistanceFromRidge = 3,
}

public enum RoofAutomaticPurlinSeatingDepthMode
{
    None = 0,
    PercentOfRafterHeight = 1,
    AbsoluteMm = 2,
}

public sealed record RoofAutomaticPurlinSeatingDepth(
    RoofAutomaticPurlinSeatingDepthMode Mode,
    double Value);

public sealed record RoofAutomaticPurlinLayoutItem(
    string LayoutItemId,
    bool Enabled,
    RoofAutomaticPurlinPlacementMode PlacementMode,
    double PlacementValueMm,
    RoofStructuralLogicalKey? ReferenceRidgeKey = null,
    RoofAutomaticPurlinSeatingDepth? SeatingDepth = null);

/// <summary>CAD-neutral desired automatic-purlin layout owned by one roof.</summary>
public sealed record RoofAutomaticPurlinLayout(
    bool RidgeEnabled,
    IReadOnlyList<RoofAutomaticPurlinLayoutItem> IntermediateItems)
{
    /// <summary>
    /// In-memory meaning of a missing owner layout section. Reading this value never
    /// implies that it should be persisted.
    /// </summary>
    public static RoofAutomaticPurlinLayout Empty { get; } = new(
        false,
        Array.Empty<RoofAutomaticPurlinLayoutItem>());
}
