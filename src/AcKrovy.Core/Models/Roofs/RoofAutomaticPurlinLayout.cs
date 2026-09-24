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
    RoofAutomaticPurlinSeatingDepth? SeatingDepth = null,
    double? WidthMm = null,
    double? HeightMm = null);

/// <summary>CAD-neutral desired automatic-purlin layout owned by one roof.</summary>
public sealed record RoofAutomaticPurlinLayout(
    bool RidgeEnabled,
    IReadOnlyList<RoofAutomaticPurlinLayoutItem> IntermediateItems)
{
    /// <summary>
    /// Whether the desired automatic-purlin set includes one wall plate for every
    /// canonical eave. Missing legacy schema-1 data intentionally defaults to false.
    /// </summary>
    public bool WallPlateEnabled { get; init; }

    /// <summary>
    /// Compat projection of wall-plate bottom-edge placement. When
    /// <see cref="WallPlatePlacement"/> uses
    /// <see cref="RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference"/>,
    /// this equals that placement value. Legacy schema-1 payloads that stored only
    /// this field are upgraded to an equivalent BottomEdge placement on read.
    /// </summary>
    public double WallPlateLowerEdgeHeightMm { get; init; }

    /// <summary>
    /// Shared Intermediate-purlin placement configuration for automatic wall plates.
    /// Null means the legacy default: BottomEdgeHeightAboveReference at
    /// <see cref="WallPlateLowerEdgeHeightMm"/> (typically 0). LayoutItemId is a
    /// reserved sentinel and is never mixed into IntermediateItems.
    /// Optional WidthMm/HeightMm on this item override profile defaults when set.
    /// </summary>
    public RoofAutomaticPurlinLayoutItem? WallPlatePlacement { get; init; }

    /// <summary>
    /// Optional ridge purlin cross-section width. Null means use the active Purlin
    /// profile default at planning time.
    /// </summary>
    public double? RidgeWidthMm { get; init; }

    /// <summary>
    /// Optional ridge purlin cross-section height. Null means use the active Purlin
    /// profile default at planning time.
    /// </summary>
    public double? RidgeHeightMm { get; init; }

    /// <summary>
    /// Optional ridge purlin seating into the rafter section. Null means the planner
    /// applies the product default percent of rafter height when ridge is enabled.
    /// </summary>
    public RoofAutomaticPurlinSeatingDepth? RidgeSeatingDepth { get; init; }

    /// <summary>
    /// Optional durable manual rafter width (mm). Must be paired with
    /// <see cref="ManualRafterHeightMm"/> when either is set.
    /// </summary>
    public double? ManualRafterWidthMm { get; init; }

    /// <summary>
    /// Optional durable manual rafter height (mm). Must be paired with
    /// <see cref="ManualRafterWidthMm"/> when either is set.
    /// </summary>
    public double? ManualRafterHeightMm { get; init; }

    /// <summary>
    /// Durable source-selection policy for rafter W×H. Schema-1 layouts default to
    /// <see cref="RoofAutomaticPurlinRafterSourcePolicy.Unset"/>.
    /// </summary>
    public RoofAutomaticPurlinRafterSourcePolicy RafterSourcePolicy { get; init; }

    /// <summary>
    /// Actual-rafter acknowledgement for conflict tracking. Schema-2 defaults to
    /// <see cref="RoofAutomaticPurlinAcknowledgedActualKind.Unspecified"/>.
    /// </summary>
    public RoofAutomaticPurlinAcknowledgedActualKind AcknowledgedActualKind { get; init; }

    /// <summary>
    /// Acknowledged actual width (mm) when
    /// <see cref="AcknowledgedActualKind"/> is
    /// <see cref="RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile"/>.
    /// </summary>
    public double? AcknowledgedActualWidthMm { get; init; }

    /// <summary>
    /// Acknowledged actual height (mm) when
    /// <see cref="AcknowledgedActualKind"/> is
    /// <see cref="RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile"/>.
    /// </summary>
    public double? AcknowledgedActualHeightMm { get; init; }

    /// <summary>
    /// In-memory meaning of a missing owner layout section. Reading this value never
    /// implies that it should be persisted.
    /// </summary>
    public static RoofAutomaticPurlinLayout Empty { get; } = new(
        false,
        Array.Empty<RoofAutomaticPurlinLayoutItem>());
}
