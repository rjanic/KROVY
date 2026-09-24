using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// When roof pitch changes, WallPlate / Intermediate plan stations stay authoritative.
/// <see cref="RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference"/> placement
/// values are recomputed for the new pitch; distance modes keep their plan values.
/// </summary>
public static class RoofAutomaticPurlinPitchAdaptationRules
{
    private const double PitchToleranceDegrees = 1e-9d;
    private const double StationToleranceMm = 1e-6d;

    public sealed record Result(
        bool IsValid,
        RoofAutomaticPurlinLayout? AdaptedLayout,
        bool LayoutChanged,
        RoofAutomaticPurlinPlanError Error,
        string? FailedLayoutItemId)
    {
        public static Result Success(RoofAutomaticPurlinLayout layout, bool changed) =>
            new(true, layout, changed, RoofAutomaticPurlinPlanError.None, null);

        public static Result Fail(
            RoofAutomaticPurlinPlanError error,
            string? failedLayoutItemId = null) =>
            new(false, null, false, error, failedLayoutItemId);
    }

    /// <summary>
    /// Adapts BottomEdge placement values so each member keeps the plan station it had
    /// under <paramref name="previousGeometry"/>. PlanDistance modes are left unchanged.
    /// Placement modes are never switched. Returns the original layout when pitch is
    /// unchanged. Does not clamp invalid stations under the new roof.
    /// </summary>
    public static Result TryAdaptLayoutPreservingPlanStations(
        HipRoofGeometry? previousGeometry,
        HipRoofGeometry? newGeometry,
        RoofBoundaryIdentityProvenanceResult? boundaryProvenance,
        RoofAutomaticPurlinLayout? layout,
        RoofAutomaticPurlinPlanningInput? planningInput)
    {
        if (previousGeometry is null ||
            newGeometry is null ||
            boundaryProvenance is null ||
            !boundaryProvenance.IsValid ||
            layout is null ||
            planningInput?.RelativeElevationDatum is null)
        {
            return Result.Fail(RoofAutomaticPurlinPlanError.InvalidGeometry);
        }

        if (!IsFinite(previousGeometry.Topology.PitchDegrees) ||
            !IsFinite(newGeometry.Topology.PitchDegrees))
        {
            return Result.Fail(RoofAutomaticPurlinPlanError.InvalidGeometry);
        }

        if (Math.Abs(
                previousGeometry.Topology.PitchDegrees -
                newGeometry.Topology.PitchDegrees) <= PitchToleranceDegrees)
        {
            return Result.Success(layout, changed: false);
        }

        // Product layouts under WallPlateBottom may persist WP BottomEdge Place=0 with
        // the SourceEave-absolute bottom stashed in WallPlateLowerEdgeHeightMm.
        var previousPlanningLayout = PrepareWallPlateBottomEdgeZeroForBootstrapPlanning(layout);
        var previousPlan = RoofAutomaticPurlinPlanner.Create(
            previousGeometry,
            boundaryProvenance,
            previousPlanningLayout,
            planningInput);
        if (!previousPlan.IsValid || previousPlan.Plan is null)
        {
            return Result.Fail(previousPlan.Error, previousPlan.FailedLayoutItemId);
        }

        if (!TryBuildStationMap(
                previousPlan.Plan,
                previousGeometry.Topology.PitchDegrees,
                out var stationsByLayoutId,
                out var stationError,
                out var stationFailedId))
        {
            return Result.Fail(stationError, stationFailedId);
        }

        var wallPlatePlacement =
            RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(layout);
        RoofAutomaticPurlinLayoutItem? adaptedWallPlate = null;
        if (layout.WallPlateEnabled)
        {
            if (!TryAdaptItem(
                    wallPlatePlacement,
                    stationsByLayoutId,
                    newGeometry.Topology.PitchDegrees,
                    planningInput,
                    isWallPlate: true,
                    out adaptedWallPlate,
                    out var wpError))
            {
                return Result.Fail(wpError, wallPlatePlacement.LayoutItemId);
            }
        }

        var adaptedIntermediates = new List<RoofAutomaticPurlinLayoutItem>(
            layout.IntermediateItems.Count);
        foreach (var item in layout.IntermediateItems)
        {
            if (!TryAdaptItem(
                    item,
                    stationsByLayoutId,
                    newGeometry.Topology.PitchDegrees,
                    planningInput,
                    isWallPlate: false,
                    out var adaptedItem,
                    out var itemError))
            {
                return Result.Fail(itemError, item.LayoutItemId);
            }

            adaptedIntermediates.Add(adaptedItem!);
        }

        // WallPlateBottom: WP bottom defines zero. Persist datum-relative BottomEdge
        // offsets (WP = 0, intermediates = physBottom − wpBottom). Core SourceEave
        // bootstrap still needs the absolute WP bottom — stash it in
        // WallPlateLowerEdgeHeightMm and expand via
        // PrepareWallPlateBottomEdgeZeroForBootstrapPlanning before Planner.Create.
        var datum = planningInput.RelativeElevationDatum;
        double? wallPlateBootstrapAbsoluteBottomMm = null;
        if (datum.ReferenceKind == RoofRelativeElevationReferenceKind.WallPlateBottom &&
            layout.WallPlateEnabled &&
            adaptedWallPlate is not null &&
            stationsByLayoutId.TryGetValue(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                out var wpStation) &&
            TryComputeBottomLocalZMm(
                wpStation,
                newGeometry.Topology.PitchDegrees,
                ResolveSectionWidthMm(adaptedWallPlate, planningInput, isWallPlate: true),
                ResolveSectionHeightMm(adaptedWallPlate, planningInput, isWallPlate: true),
                planningInput.RafterHeightMm,
                adaptedWallPlate.SeatingDepth,
                out var newWpBottom,
                out _))
        {
            wallPlateBootstrapAbsoluteBottomMm = newWpBottom;
            if (adaptedWallPlate.PlacementMode ==
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference)
            {
                adaptedWallPlate = adaptedWallPlate with { PlacementValueMm = 0d };
            }

            for (var i = 0; i < adaptedIntermediates.Count; i++)
            {
                var item = adaptedIntermediates[i];
                if (!item.Enabled ||
                    item.PlacementMode !=
                        RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference ||
                    !stationsByLayoutId.TryGetValue(item.LayoutItemId, out var station))
                {
                    continue;
                }

                if (!TryComputeBottomLocalZMm(
                        station,
                        newGeometry.Topology.PitchDegrees,
                        ResolveSectionWidthMm(item, planningInput, isWallPlate: false),
                        ResolveSectionHeightMm(item, planningInput, isWallPlate: false),
                        planningInput.RafterHeightMm,
                        item.SeatingDepth,
                        out var bottomLocalZMm,
                        out _))
                {
                    return Result.Fail(
                        RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement,
                        item.LayoutItemId);
                }

                adaptedIntermediates[i] = item with
                {
                    PlacementValueMm = bottomLocalZMm - newWpBottom,
                };
            }
        }

        var wallPlateLowerEdgeHeightMm =
            adaptedWallPlate?.PlacementMode ==
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference
                ? (wallPlateBootstrapAbsoluteBottomMm ?? adaptedWallPlate.PlacementValueMm)
                : layout.WallPlateLowerEdgeHeightMm;

        var adaptedLayout = layout with
        {
            IntermediateItems = adaptedIntermediates.AsReadOnly(),
            WallPlatePlacement = adaptedWallPlate is null
                ? layout.WallPlatePlacement
                : adaptedWallPlate with
                {
                    LayoutItemId = RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                    Enabled = true,
                },
            WallPlateLowerEdgeHeightMm = wallPlateLowerEdgeHeightMm,
        };

        var planningLayout = PrepareWallPlateBottomEdgeZeroForBootstrapPlanning(adaptedLayout);
        var validationPlan = RoofAutomaticPurlinPlanner.Create(
            newGeometry,
            boundaryProvenance,
            planningLayout,
            planningInput);
        if (!validationPlan.IsValid || validationPlan.Plan is null)
        {
            return Result.Fail(validationPlan.Error, validationPlan.FailedLayoutItemId);
        }

        // Guard: plan stations must match the captured previous stations exactly.
        if (!TryBuildStationMap(
                validationPlan.Plan,
                newGeometry.Topology.PitchDegrees,
                out var newStations,
                out var newStationError,
                out var newFailedId))
        {
            return Result.Fail(newStationError, newFailedId);
        }

        foreach (var pair in stationsByLayoutId)
        {
            if (!newStations.TryGetValue(pair.Key, out var newStation) ||
                Math.Abs(newStation - pair.Value) > StationToleranceMm)
            {
                return Result.Fail(
                    RoofAutomaticPurlinPlanError.ElevationOutsideRoof,
                    pair.Key);
            }
        }

        var changed = !LayoutPlacementEquals(layout, adaptedLayout);
        return Result.Success(adaptedLayout, changed);
    }

    /// <summary>
    /// Under WallPlateBottom, product WP BottomEdge Place=0 means relative zero
    /// (WP bottom defines the datum). Core bootstrap still places against SourceEave,
    /// so expand Place to the absolute bottom stashed in
    /// <see cref="RoofAutomaticPurlinLayout.WallPlateLowerEdgeHeightMm"/> before
    /// <see cref="RoofAutomaticPurlinPlanner.Create"/> / materialization. Does not
    /// change seating formulas or accepted PlanDistance↔BottomEdge conversion.
    /// </summary>
    public static RoofAutomaticPurlinLayout PrepareWallPlateBottomEdgeZeroForBootstrapPlanning(
        RoofAutomaticPurlinLayout layout)
    {
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        if (!layout.WallPlateEnabled)
        {
            return layout;
        }

        var wallPlate = RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(layout);
        if (wallPlate.PlacementMode !=
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference ||
            Math.Abs(wallPlate.PlacementValueMm) > 1e-9 ||
            !IsFinite(layout.WallPlateLowerEdgeHeightMm) ||
            Math.Abs(layout.WallPlateLowerEdgeHeightMm) <= 1e-9)
        {
            return layout;
        }

        return layout with
        {
            WallPlatePlacement = wallPlate with
            {
                LayoutItemId = RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                Enabled = true,
                PlacementValueMm = layout.WallPlateLowerEdgeHeightMm,
            },
        };
    }

    /// <summary>
    /// Closed-form BottomLocalZ at a fixed plan station for seated members.
    /// Matches <see cref="RoofRafterPhysicalGeometry.CreatePurlinPlacement"/> /
    /// <see cref="RoofRafterPhysicalGeometry.TryResolveUpperFaceLocalZFromSeatedBottom"/>.
    /// </summary>
    public static bool TryComputeBottomLocalZMm(
        double planDistanceFromEaveMm,
        double pitchDegrees,
        double purlinWidthMm,
        double purlinHeightMm,
        double rafterHeightMm,
        RoofAutomaticPurlinSeatingDepth? seating,
        out double bottomLocalZMm,
        out double roofSurfaceLocalZMm)
    {
        bottomLocalZMm = 0d;
        roofSurfaceLocalZMm = 0d;
        if (!IsFinite(planDistanceFromEaveMm) ||
            planDistanceFromEaveMm < 0d ||
            !IsFinite(pitchDegrees) ||
            pitchDegrees <= 0d ||
            pitchDegrees >= 90d ||
            !IsFinite(purlinWidthMm) ||
            purlinWidthMm < 0d ||
            !IsFinite(purlinHeightMm) ||
            purlinHeightMm <= 0d ||
            !IsFinite(rafterHeightMm) ||
            rafterHeightMm <= 0d)
        {
            return false;
        }

        var pitchRad = pitchDegrees * Math.PI / 180d;
        var tanPitch = Math.Tan(pitchRad);
        var cosPitch = Math.Cos(pitchRad);
        if (!IsFinite(tanPitch) || tanPitch <= 0d ||
            !IsFinite(cosPitch) || cosPitch <= RoofRafterPhysicalGeometry.UnitTolerance)
        {
            return false;
        }

        roofSurfaceLocalZMm = planDistanceFromEaveMm * tanPitch;
        if (!IsFinite(roofSurfaceLocalZMm))
        {
            return false;
        }

        if (seating is null)
        {
            // Historical no-seating BottomEdge: slice at timber center.
            bottomLocalZMm = roofSurfaceLocalZMm - purlinHeightMm / 2d;
            return IsFinite(bottomLocalZMm);
        }

        if (!TryResolveSeatingDepthMm(seating, rafterHeightMm, out var seatingDepthMm))
        {
            return false;
        }

        // Invert CreatePurlinPlacement: surfaceZ is axis upper face.
        bottomLocalZMm =
            roofSurfaceLocalZMm -
            purlinHeightMm -
            (rafterHeightMm - seatingDepthMm) / cosPitch -
            (purlinWidthMm / 2d) * tanPitch;
        return IsFinite(bottomLocalZMm);
    }

    public static bool TryResolvePlanDistanceFromEaveMm(
        double roofSurfaceLocalZMm,
        double pitchDegrees,
        out double planDistanceMm)
    {
        planDistanceMm = 0d;
        if (!IsFinite(roofSurfaceLocalZMm) ||
            !IsFinite(pitchDegrees) ||
            pitchDegrees <= 0d ||
            pitchDegrees >= 90d)
        {
            return false;
        }

        var tanPitch = Math.Tan(pitchDegrees * Math.PI / 180d);
        if (!IsFinite(tanPitch) || tanPitch <= 0d)
        {
            return false;
        }

        planDistanceMm = roofSurfaceLocalZMm / tanPitch;
        return IsFinite(planDistanceMm) && planDistanceMm >= 0d;
    }

    private static bool TryAdaptItem(
        RoofAutomaticPurlinLayoutItem item,
        IReadOnlyDictionary<string, double> stationsByLayoutId,
        double newPitchDegrees,
        RoofAutomaticPurlinPlanningInput planningInput,
        bool isWallPlate,
        out RoofAutomaticPurlinLayoutItem? adapted,
        out RoofAutomaticPurlinPlanError error)
    {
        adapted = item;
        error = RoofAutomaticPurlinPlanError.None;
        if (!item.Enabled ||
            item.PlacementMode !=
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference)
        {
            return true;
        }

        var layoutId = isWallPlate
            ? RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId
            : item.LayoutItemId;
        if (!stationsByLayoutId.TryGetValue(layoutId, out var stationMm))
        {
            error = RoofAutomaticPurlinPlanError.InvalidLayout;
            return false;
        }

        var datum = planningInput.RelativeElevationDatum;
        // Non-WallPlateBottom: offset vs unchanged datum LocalZ.
        // WallPlateBottom BottomEdge values are finalized in the caller rebase pass.
        if (datum.ReferenceKind == RoofRelativeElevationReferenceKind.WallPlateBottom)
        {
            adapted = item;
            return true;
        }

        if (!TryComputeBottomLocalZMm(
                stationMm,
                newPitchDegrees,
                ResolveSectionWidthMm(item, planningInput, isWallPlate),
                ResolveSectionHeightMm(item, planningInput, isWallPlate),
                planningInput.RafterHeightMm,
                item.SeatingDepth,
                out var bottomLocalZMm,
                out _))
        {
            error = RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement;
            return false;
        }

        var referenceLocalZMm = datum.ReferenceKind switch
        {
            RoofRelativeElevationReferenceKind.SourceEavePlane => 0d,
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane => datum.ReferenceLocalZMm,
            _ => datum.ReferenceLocalZMm,
        };
        adapted = item with { PlacementValueMm = bottomLocalZMm - referenceLocalZMm };
        return true;
    }

    private static bool TryBuildStationMap(
        RoofAutomaticPurlinPlan plan,
        double pitchDegrees,
        out Dictionary<string, double> stationsByLayoutId,
        out RoofAutomaticPurlinPlanError error,
        out string? failedLayoutItemId)
    {
        stationsByLayoutId = new Dictionary<string, double>(StringComparer.Ordinal);
        error = RoofAutomaticPurlinPlanError.None;
        failedLayoutItemId = null;

        foreach (var group in plan.Items
                     .Where(item =>
                         item.GeneratorRole is
                             RoofAutomaticPurlinGeneratorRole.WallPlate or
                             RoofAutomaticPurlinGeneratorRole.Intermediate)
                     .GroupBy(ResolveLayoutKey, StringComparer.Ordinal))
        {
            double? station = null;
            foreach (var item in group)
            {
                if (!TryResolvePlanDistanceFromPlanItem(
                        item,
                        pitchDegrees,
                        out var itemStation))
                {
                    error = RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement;
                    failedLayoutItemId = group.Key;
                    return false;
                }

                if (station is null)
                {
                    station = itemStation;
                }
                else if (Math.Abs(station.Value - itemStation) > StationToleranceMm)
                {
                    error = RoofAutomaticPurlinPlanError.InconsistentFaceNormalVerticalComponent;
                    failedLayoutItemId = group.Key;
                    return false;
                }
            }

            if (station is null)
            {
                continue;
            }

            stationsByLayoutId[group.Key] = station.Value;
        }

        return true;
    }

    private static bool TryResolvePlanDistanceFromPlanItem(
        RoofAutomaticPurlinPlanItem item,
        double pitchDegrees,
        out double planDistanceMm)
    {
        planDistanceMm = 0d;
        if (item.PhysicalPlacement is not null &&
            TryResolvePlanDistanceFromEaveMm(
                item.PhysicalPlacement.RafterUpperSurfaceLocalZMm,
                pitchDegrees,
                out planDistanceMm))
        {
            return true;
        }

        // No-seating BottomEdge: planner slice LocalZ is the timber center.
        if (item.ElevationProfile is not null &&
            TryResolvePlanDistanceFromEaveMm(
                item.ElevationProfile.CenterLocalZMm,
                pitchDegrees,
                out planDistanceMm))
        {
            return true;
        }

        return false;
    }

    private static string ResolveLayoutKey(RoofAutomaticPurlinPlanItem item) =>
        item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate
            ? RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId
            : item.LayoutItemId ?? string.Empty;

    private static double ResolveSectionWidthMm(
        RoofAutomaticPurlinLayoutItem item,
        RoofAutomaticPurlinPlanningInput planningInput,
        bool isWallPlate) =>
        item.WidthMm is > 0d
            ? item.WidthMm.Value
            : isWallPlate
                ? planningInput.WallPlateWidthMm
                : planningInput.PurlinWidthMm;

    private static double ResolveSectionHeightMm(
        RoofAutomaticPurlinLayoutItem item,
        RoofAutomaticPurlinPlanningInput planningInput,
        bool isWallPlate) =>
        item.HeightMm is > 0d
            ? item.HeightMm.Value
            : isWallPlate
                ? planningInput.WallPlateHeightMm
                : planningInput.PurlinHeightMm;

    private static bool TryResolveSeatingDepthMm(
        RoofAutomaticPurlinSeatingDepth seating,
        double rafterHeightMm,
        out double depthMm)
    {
        depthMm = 0d;
        if (!IsFinite(seating.Value) || seating.Value < 0d)
        {
            return false;
        }

        if (seating.Mode == RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm)
        {
            if (!IsFinite(rafterHeightMm) || rafterHeightMm <= 0d || seating.Value > rafterHeightMm)
            {
                return false;
            }

            depthMm = seating.Value;
            return true;
        }

        if (seating.Mode != RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight ||
            seating.Value > 100d ||
            !IsFinite(rafterHeightMm) ||
            rafterHeightMm <= 0d)
        {
            return false;
        }

        depthMm = rafterHeightMm * seating.Value / 100d;
        return IsFinite(depthMm);
    }

    private static bool LayoutPlacementEquals(
        RoofAutomaticPurlinLayout left,
        RoofAutomaticPurlinLayout right)
    {
        if (left.IntermediateItems.Count != right.IntermediateItems.Count)
        {
            return false;
        }

        for (var i = 0; i < left.IntermediateItems.Count; i++)
        {
            if (!ItemPlacementEquals(left.IntermediateItems[i], right.IntermediateItems[i]))
            {
                return false;
            }
        }

        var leftWp = RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(left);
        var rightWp = RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(right);
        return ItemPlacementEquals(leftWp, rightWp);
    }

    private static bool ItemPlacementEquals(
        RoofAutomaticPurlinLayoutItem left,
        RoofAutomaticPurlinLayoutItem right) =>
        left.PlacementMode == right.PlacementMode &&
        Math.Abs(left.PlacementValueMm - right.PlacementValueMm) <= StationToleranceMm;

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
