using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Produces the complete CAD-neutral desired centerline plan for automatic purlins.
/// It consumes the existing roof topology and persisted boundary provenance; it does
/// not solve a second roof, apply manufacturing defaults, or create host entities.
/// </summary>
public static class RoofAutomaticPurlinPlanner
{
    public const double CoordinateToleranceMm =
        SimpleGableRoofGeometryTolerance.CoordinateToleranceMm;

    /// <summary>
    /// Product seating for WallPlate matches Intermediate Purlin plan-distance:
    /// 25% of rafter height into the rafter section.
    /// </summary>
    public const double DefaultWallPlateSeatingPercent = 25d;

    public static RoofAutomaticPurlinPlanResult Create(
        HipRoofGeometry? geometry,
        RoofBoundaryIdentityProvenanceResult? boundaryProvenance,
        RoofAutomaticPurlinLayout? layout,
        RoofAutomaticPurlinPlanningInput? planningInput) =>
        CreateCore(geometry, boundaryProvenance, layout, planningInput);

    /// <summary>
    /// Resolves the planning datum. When reference kind is WallPlateBottom and wall
    /// plates are enabled, WallPlate geometry is placed against a bootstrap eave
    /// datum first; the resulting lower-edge local Z becomes ReferenceLocalZMm.
    /// </summary>
    public static RoofAutomaticPurlinEffectiveDatumResult ResolveEffectiveDatum(
        HipRoofGeometry? geometry,
        RoofBoundaryIdentityProvenanceResult? boundaryProvenance,
        RoofAutomaticPurlinLayout? layout,
        RoofAutomaticPurlinPlanningInput? planningInput)
    {
        if (geometry is null ||
            boundaryProvenance is null ||
            !boundaryProvenance.IsValid ||
            layout is null ||
            planningInput?.RelativeElevationDatum is null)
        {
            return new RoofAutomaticPurlinEffectiveDatumResult(
                false,
                null,
                null,
                RoofAutomaticPurlinPlanError.InvalidRelativeElevationDatum);
        }

        var requested = planningInput.RelativeElevationDatum;
        if (requested.ReferenceKind != RoofRelativeElevationReferenceKind.WallPlateBottom ||
            !planningInput.WallPlatesEnabled)
        {
            var validated = RoofRelativeElevationDatumRules.Validate(
                RoofRelativeElevationDatumSchema.CurrentVersion,
                requested.ReferenceKind,
                requested.ReferenceRelativeElevationMm,
                requested.ReferenceLocalZMm);
            if (!validated.IsValid || validated.Datum is null)
            {
                return new RoofAutomaticPurlinEffectiveDatumResult(
                    false,
                    null,
                    null,
                    RoofAutomaticPurlinPlanError.InvalidRelativeElevationDatum);
            }

            return new RoofAutomaticPurlinEffectiveDatumResult(
                true,
                validated.Datum,
                null,
                RoofAutomaticPurlinPlanError.None);
        }

        var anchor = TryResolveWallPlateBottomLocalZMm(
            geometry,
            boundaryProvenance,
            layout,
            planningInput,
            out var bottomLocalZMm,
            out var error);
        if (!anchor)
        {
            return new RoofAutomaticPurlinEffectiveDatumResult(false, null, null, error);
        }

        return new RoofAutomaticPurlinEffectiveDatumResult(
            true,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.WallPlateBottom,
                requested.ReferenceRelativeElevationMm,
                bottomLocalZMm),
            bottomLocalZMm,
            RoofAutomaticPurlinPlanError.None);
    }

    private static RoofAutomaticPurlinPlanResult CreateCore(
        HipRoofGeometry? geometry,
        RoofBoundaryIdentityProvenanceResult? boundaryProvenance,
        RoofAutomaticPurlinLayout? layout,
        RoofAutomaticPurlinPlanningInput? planningInput)
    {
        if (geometry is null)
        {
            return Invalid(RoofAutomaticPurlinPlanError.InvalidGeometry);
        }

        if (boundaryProvenance is null || !boundaryProvenance.IsValid)
        {
            return Invalid(RoofAutomaticPurlinPlanError.InvalidBoundaryProvenance);
        }

        if (layout is null || layout.IntermediateItems is null)
        {
            return Invalid(RoofAutomaticPurlinPlanError.InvalidLayout);
        }

        if (planningInput is null || planningInput.RelativeElevationDatum is null ||
            RoofRelativeElevationDatumRules.Validate(
                RoofRelativeElevationDatumSchema.CurrentVersion,
                planningInput.RelativeElevationDatum.ReferenceKind,
                planningInput.RelativeElevationDatum.ReferenceRelativeElevationMm,
                planningInput.RelativeElevationDatum.ReferenceLocalZMm).Datum is null)
        {
            return Invalid(RoofAutomaticPurlinPlanError.InvalidRelativeElevationDatum);
        }

        if (!IsFinite(planningInput.PurlinWidthMm) || planningInput.PurlinWidthMm <= 0d ||
            !IsFinite(planningInput.PurlinHeightMm) || planningInput.PurlinHeightMm <= 0d ||
            !IsFinite(planningInput.RafterHeightMm) || planningInput.RafterHeightMm <= 0d)
        {
            return Invalid(RoofAutomaticPurlinPlanError.InvalidPhysicalSection);
        }

        if (planningInput.WallPlatesEnabled &&
            (!IsFinite(planningInput.WallPlateWidthMm) || planningInput.WallPlateWidthMm <= 0d ||
             !IsFinite(planningInput.WallPlateHeightMm) || planningInput.WallPlateHeightMm <= 0d))
        {
            return Invalid(RoofAutomaticPurlinPlanError.InvalidPhysicalSection);
        }

        // WallPlateBottom product may persist WP BottomEdge Place=0 (relative zero)
        // with SourceEave-absolute bottom in WallPlateLowerEdgeHeightMm. Expand before
        // ResolveEffectiveDatum / bootstrap so Core seating formulas stay unchanged.
        if (planningInput.RelativeElevationDatum.ReferenceKind ==
            RoofRelativeElevationReferenceKind.WallPlateBottom)
        {
            layout = RoofAutomaticPurlinPitchAdaptationRules
                .PrepareWallPlateBottomEdgeZeroForBootstrapPlanning(layout);
        }

        var effectiveDatumResult = ResolveEffectiveDatum(
            geometry,
            boundaryProvenance,
            layout,
            planningInput);
        if (!effectiveDatumResult.IsValid || effectiveDatumResult.Datum is null)
        {
            return Invalid(effectiveDatumResult.Error);
        }

        planningInput = planningInput with
        {
            RelativeElevationDatum = effectiveDatumResult.Datum,
        };

        var topology = geometry.Topology;
        if (!HasValidCoordinates(topology))
        {
            return Invalid(RoofAutomaticPurlinPlanError.InvalidCoordinate);
        }

        if (!HasCompleteBoundaryProvenance(topology, boundaryProvenance))
        {
            return Invalid(RoofAutomaticPurlinPlanError.BoundaryProvenanceCountMismatch);
        }

        var normalizedItems = new List<RoofAutomaticPurlinLayoutItem>(layout.IntermediateItems.Count);
        var layoutIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in layout.IntermediateItems)
        {
            if (item is null)
            {
                return Invalid(RoofAutomaticPurlinPlanError.InvalidLayout);
            }

            if (string.IsNullOrWhiteSpace(item.LayoutItemId))
            {
                return Invalid(
                    RoofAutomaticPurlinPlanError.EmptyLayoutItemId,
                    item.LayoutItemId);
            }

            if (!RoofAutomaticPurlinLayoutItemIdentity.TryNormalize(
                    item.LayoutItemId,
                    out var normalizedId))
            {
                return Invalid(
                    RoofAutomaticPurlinPlanError.MalformedLayoutItemId,
                    item.LayoutItemId);
            }

            if (!layoutIds.Add(normalizedId))
            {
                return Invalid(
                    RoofAutomaticPurlinPlanError.DuplicateLayoutItemId,
                    normalizedId);
            }

            normalizedItems.Add(item with { LayoutItemId = normalizedId });
        }

        IReadOnlyDictionary<int, RoofFaceUnitNormal>? faceNormals = null;
        var commonNormalZ = 0d;
        var wallPlatePlacement = planningInput.WallPlatesEnabled
            ? RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(layout)
            : null;
        if (wallPlatePlacement is not null)
        {
            planningInput = ApplyWallPlateSectionOverrides(planningInput, wallPlatePlacement);
        }

        var needsFaceNormals =
            (wallPlatePlacement is not null &&
             (IsSeatingDriven(wallPlatePlacement.PlacementMode) ||
              wallPlatePlacement.SeatingDepth is not null)) ||
            normalizedItems.Any(item =>
                item.Enabled &&
                (IsSeatingDriven(item.PlacementMode) || item.SeatingDepth is not null)) ||
            layout.RidgeEnabled;
        if (needsFaceNormals)
        {
            // Current Hip topology has one uniform pitch, so every upward face normal
            // has the same Z component and one plan setback yields one horizontal
            // purlin elevation. Future unequal-pitch roofs require an explicit policy.
            var normalsError = TryCreateFaceNormals(topology, out faceNormals, out commonNormalZ);
            if (normalsError != RoofAutomaticPurlinPlanError.None)
            {
                return Invalid(normalsError);
            }
        }

        var items = new List<RoofAutomaticPurlinPlanItem>();
        if (planningInput.WallPlatesEnabled)
        {
            // When the architectural datum is anchored to WallPlate bottom, physical
            // WallPlate placement must use the bootstrap eave datum to avoid a cycle.
            // Relative elevations are then rebased onto the resolved WallPlateBottom datum.
            var wallPlatePlacementInput =
                planningInput.RelativeElevationDatum.ReferenceKind ==
                RoofRelativeElevationReferenceKind.WallPlateBottom
                    ? planningInput with
                    {
                        RelativeElevationDatum = CreateWallPlateBootstrapDatum(),
                    }
                    : planningInput;
            var wallPlateResult = AddWallPlates(
                geometry,
                boundaryProvenance,
                wallPlatePlacement!,
                wallPlatePlacementInput,
                faceNormals,
                commonNormalZ,
                items);
            if (wallPlateResult is not null)
            {
                return wallPlateResult;
            }

            if (planningInput.RelativeElevationDatum.ReferenceKind ==
                RoofRelativeElevationReferenceKind.WallPlateBottom)
            {
                var rebaseError = RebaseWallPlateRelativeElevations(
                    items,
                    planningInput.RelativeElevationDatum);
                if (rebaseError != RoofAutomaticPurlinPlanError.None)
                {
                    return Invalid(rebaseError);
                }
            }
        }

        if (layout.RidgeEnabled)
        {
            var ridgeResult = AddHorizontalRidges(
                geometry,
                boundaryProvenance,
                layout,
                planningInput,
                commonNormalZ,
                items);
            if (ridgeResult is not null)
            {
                return ridgeResult;
            }
        }

        foreach (var item in normalizedItems
                     .Where(candidate => candidate.Enabled)
                     .OrderBy(candidate => candidate.LayoutItemId, StringComparer.Ordinal))
        {
            var itemPlanningInput = ApplyPurlinSectionOverrides(planningInput, item);
            var placement = ResolvePlacementCore(
                geometry,
                boundaryProvenance,
                item,
                itemPlanningInput);
            if (!placement.IsValid || placement.RoofSurfaceLocalZMm is null)
            {
                return Invalid(placement.Error, item.LayoutItemId);
            }

            var sliceLocalZMm = placement.RoofSurfaceLocalZMm.Value;
            var centerLocalZMm = sliceLocalZMm;
            RoofPurlinElevationProfile elevationProfile;
            if (IsSeatingDriven(item.PlacementMode) || placement.SeatingDepthMm is not null)
            {
                if (faceNormals is null || faceNormals.Count == 0 ||
                    placement.SeatingDepthMm is not { } seatingDepthMm)
                {
                    return Invalid(RoofAutomaticPurlinPlanError.MissingFaceNormal, item.LayoutItemId);
                }

                var representativeNormal = new RoofFaceUnitNormal(0d, 0d, commonNormalZ);
                var horizontalLength = Math.Sqrt(Math.Max(0d, 1d - commonNormalZ * commonNormalZ));
                representativeNormal = new RoofFaceUnitNormal(horizontalLength, 0d, commonNormalZ);
                var physical = RoofRafterPhysicalGeometry.CreatePurlinPlacement(
                    new RoofPoint3D(0d, 0d, sliceLocalZMm),
                    representativeNormal,
                    itemPlanningInput.RafterHeightMm,
                    itemPlanningInput.PurlinHeightMm,
                    seatingDepthMm,
                    itemPlanningInput.RelativeElevationDatum,
                    itemPlanningInput.PurlinWidthMm);
                if (!physical.IsValid || physical.Placement is null)
                {
                    return Invalid(
                        RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement,
                        item.LayoutItemId);
                }

                centerLocalZMm = physical.Placement.PurlinCenterLocalZMm;
                elevationProfile = CreateElevationProfile(
                    itemPlanningInput.RelativeElevationDatum,
                    centerLocalZMm,
                    itemPlanningInput.PurlinHeightMm,
                    seatingDepthMm);
            }
            else
            {
                elevationProfile = CreateElevationProfile(
                    itemPlanningInput.RelativeElevationDatum,
                    centerLocalZMm,
                    itemPlanningInput.PurlinHeightMm,
                    null);
            }

            var validationError = ValidateElevation(topology, geometry.RiseMm, sliceLocalZMm);
            if (validationError != RoofAutomaticPurlinPlanError.None)
            {
                return Invalid(validationError, item.LayoutItemId);
            }

            var sliceResult = AddIntermediateSlices(
                topology,
                boundaryProvenance,
                item,
                sliceLocalZMm,
                centerLocalZMm,
                elevationProfile,
                faceNormals,
                itemPlanningInput,
                placement.SeatingDepthMm,
                items);
            if (sliceResult is not null)
            {
                return sliceResult;
            }
        }

        var duplicate = items
            .GroupBy(item => item.GeneratedKey)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
        {
            return Invalid(
                RoofAutomaticPurlinPlanError.DuplicateGeneratedKey,
                duplicate is RoofAutomaticPurlinIntermediateKey intermediate
                    ? intermediate.LayoutItemId
                    : null,
                duplicate);
        }

        var ordered = items
            .OrderBy(item => item.GeneratorRole)
            .ThenBy(item => item.GeneratedKey.ToString(), StringComparer.Ordinal)
            .ToArray();
        return new RoofAutomaticPurlinPlanResult(
            true,
            new RoofAutomaticPurlinPlan(Array.AsReadOnly(ordered)),
            RoofAutomaticPurlinPlanError.None,
            null,
            null);
    }

    /// <summary>
    /// Resolves semantic placement to the mathematical roof-face elevation. That
    /// elevation is the rafter centroidal-axis elevation for plan-distance modes.
    /// </summary>
    public static RoofAutomaticPurlinPlacementResolution ResolvePlacement(
        HipRoofGeometry? geometry,
        RoofBoundaryIdentityProvenanceResult? boundaryProvenance,
        RoofAutomaticPurlinLayoutItem? item,
        RoofAutomaticPurlinPlanningInput? planningInput) =>
        ResolvePlacementCore(
            geometry,
            boundaryProvenance,
            item,
            planningInput);

    private static RoofAutomaticPurlinPlacementResolution ResolvePlacementCore(
        HipRoofGeometry? geometry,
        RoofBoundaryIdentityProvenanceResult? boundaryProvenance,
        RoofAutomaticPurlinLayoutItem? item,
        RoofAutomaticPurlinPlanningInput? planningInput)
    {
        if (geometry is null || item is null || planningInput is null ||
            planningInput.RelativeElevationDatum is null ||
            !RoofPurlinLayoutPersistenceRules.IsValidPlacementValueMm(
                item.PlacementMode,
                item.PlacementValueMm) ||
            !IsFinite(planningInput.PurlinHeightMm) || planningInput.PurlinHeightMm <= 0d ||
            !IsFinite(planningInput.RafterHeightMm) || planningInput.RafterHeightMm <= 0d)
        {
            return PlacementInvalid(RoofAutomaticPurlinPlanError.InvalidPlacementValue);
        }

        if (!IsFinite(geometry.Topology.PitchDegrees) ||
            geometry.Topology.PitchDegrees < 0d ||
            geometry.Topology.PitchDegrees >= 90d)
        {
            return PlacementInvalid(RoofAutomaticPurlinPlanError.InvalidGeometry);
        }

        if (!RoofRelativeElevationDatumRules.Validate(
                RoofRelativeElevationDatumSchema.CurrentVersion,
                planningInput.RelativeElevationDatum.ReferenceKind,
                planningInput.RelativeElevationDatum.ReferenceRelativeElevationMm,
                planningInput.RelativeElevationDatum.ReferenceLocalZMm).IsValid)
        {
            return PlacementInvalid(RoofAutomaticPurlinPlanError.InvalidRelativeElevationDatum);
        }

        switch (item.PlacementMode)
        {
            case RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference:
                if (item.ReferenceRidgeKey is not null)
                {
                    return PlacementInvalid(RoofAutomaticPurlinPlanError.InvalidReferenceRidge);
                }

                double? bottomSeat = null;
                if (item.SeatingDepth is not null)
                {
                    if (!TryResolveSeatingDepth(
                            item.SeatingDepth,
                            planningInput.RafterHeightMm,
                            out var resolvedBottomSeat))
                    {
                        return PlacementInvalid(RoofAutomaticPurlinPlanError.InvalidSeatingDepth);
                    }

                    bottomSeat = resolvedBottomSeat;
                }

                // Requested bottom edge is a signed offset in roof-local Z:
                // BottomLocal = ReferenceLocalZ + PlacementValueMm (negative = below datum).
                var bottomLocalZMm =
                    planningInput.RelativeElevationDatum.ReferenceLocalZMm +
                    item.PlacementValueMm;
                var centerFromBottomLocalZMm =
                    bottomLocalZMm + planningInput.PurlinHeightMm / 2d;

                if (bottomSeat is null)
                {
                    // No seating: historical contract — RoofSurfaceLocalZMm is the timber
                    // center elevation used as the horizontal roof-plane slice.
                    return new RoofAutomaticPurlinPlacementResolution(
                        true,
                        centerFromBottomLocalZMm,
                        null,
                        null,
                        RoofAutomaticPurlinPlanError.None);
                }

                // With seating: RoofSurfaceLocalZMm must be the mathematical UPPER face at
                // the member axis. Passing the timber center here previously made
                // CreatePurlinPlacement treat center as upper face and seat the wall plate
                // below the reference (HOST: Bottom ≈ −233 mm for requested 0).
                if (!RoofRafterPhysicalGeometry.TryResolveUpperFaceLocalZFromSeatedBottom(
                        bottomLocalZMm,
                        planningInput.PurlinHeightMm,
                        planningInput.PurlinWidthMm,
                        planningInput.RafterHeightMm,
                        bottomSeat.Value,
                        geometry.Topology.PitchDegrees,
                        out var upperFaceLocalZMm))
                {
                    return PlacementInvalid(
                        RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement);
                }

                return new RoofAutomaticPurlinPlacementResolution(
                    true,
                    upperFaceLocalZMm,
                    bottomSeat,
                    null,
                    RoofAutomaticPurlinPlanError.None);

            case RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave:
                if (item.ReferenceRidgeKey is not null)
                {
                    return PlacementInvalid(RoofAutomaticPurlinPlanError.InvalidReferenceRidge);
                }

                if (!TryResolveSeatingDepth(
                        item.SeatingDepth,
                        planningInput.RafterHeightMm,
                        out var eaveSeat))
                {
                    return PlacementInvalid(RoofAutomaticPurlinPlanError.InvalidSeatingDepth);
                }

                var eaveSurfaceLocalZMm =
                    item.PlacementValueMm *
                    Math.Tan(geometry.Topology.PitchDegrees * Math.PI / 180d);
                if (!TryResolvePlanDistanceFromEaveExclusiveMaxMm(
                        geometry,
                        out var maxPlanDistanceMm) ||
                    item.PlacementValueMm >= maxPlanDistanceMm ||
                    eaveSurfaceLocalZMm >= geometry.RiseMm)
                {
                    return PlacementInvalid(RoofAutomaticPurlinPlanError.ElevationOutsideRoof);
                }

                return new RoofAutomaticPurlinPlacementResolution(
                    true,
                    eaveSurfaceLocalZMm,
                    eaveSeat,
                    null,
                    RoofAutomaticPurlinPlanError.None);

            case RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge:
                if (!TryResolveSeatingDepth(
                        item.SeatingDepth,
                        planningInput.RafterHeightMm,
                        out var ridgeSeat))
                {
                    return PlacementInvalid(RoofAutomaticPurlinPlanError.InvalidSeatingDepth);
                }

                if (boundaryProvenance is null || !boundaryProvenance.IsValid)
                {
                    return PlacementInvalid(RoofAutomaticPurlinPlanError.InvalidBoundaryProvenance);
                }

                var structural = RoofStructuralEdgeIdentityResolver.Resolve(geometry, boundaryProvenance);
                if (!structural.IsValid)
                {
                    return PlacementInvalid(RoofAutomaticPurlinPlanError.InvalidGeometry);
                }

                var ridges = structural.Edges
                    .Where(edge => edge.StructuralRole == RoofStructuralRole.Ridge)
                    .ToArray();
                ResolvedRoofStructuralEdge? ridge;
                if (item.ReferenceRidgeKey is null)
                {
                    if (ridges.Length != 1)
                    {
                        return PlacementInvalid(
                            ridges.Length == 0
                                ? RoofAutomaticPurlinPlanError.ReferenceRidgeNotFound
                                : RoofAutomaticPurlinPlanError.ReferenceRidgeRequired);
                    }

                    ridge = ridges[0];
                }
                else
                {
                    if (item.ReferenceRidgeKey.Role != RoofStructuralRole.Ridge ||
                        item.ReferenceRidgeKey.BoundaryEdgeIdA <= 0 ||
                        item.ReferenceRidgeKey.BoundaryEdgeIdA >= item.ReferenceRidgeKey.BoundaryEdgeIdB)
                    {
                        return PlacementInvalid(RoofAutomaticPurlinPlanError.InvalidReferenceRidge);
                    }

                    ridge = ridges.SingleOrDefault(edge => edge.StructuralIdentity == item.ReferenceRidgeKey);
                    if (ridge is null)
                    {
                        return PlacementInvalid(RoofAutomaticPurlinPlanError.ReferenceRidgeNotFound);
                    }
                }

                if (Math.Abs(ridge.Segment3D.Start.Z - ridge.Segment3D.End.Z) > CoordinateToleranceMm)
                {
                    return PlacementInvalid(RoofAutomaticPurlinPlanError.InclinedReferenceRidge);
                }

                return new RoofAutomaticPurlinPlacementResolution(
                    true,
                    ridge.Segment3D.Start.Z -
                    item.PlacementValueMm * Math.Tan(geometry.Topology.PitchDegrees * Math.PI / 180d),
                    ridgeSeat,
                    ridge.StructuralIdentity,
                    RoofAutomaticPurlinPlanError.None);

            default:
                return PlacementInvalid(RoofAutomaticPurlinPlanError.InvalidPlacementMode);
        }
    }

    private static bool TryResolveSeatingDepth(
        RoofAutomaticPurlinSeatingDepth? seating,
        double rafterHeightMm,
        out double depthMm)
    {
        depthMm = 0d;
        // 0 is a valid zero-penetration seating; only negative / non-finite values fail.
        if (seating is null || !IsFinite(seating.Value) || seating.Value < 0d)
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

        // Percent: inclusive 0..100 → depth 0..rafterHeight.
        if (seating.Mode != RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight ||
            seating.Value > 100d || !IsFinite(rafterHeightMm) || rafterHeightMm <= 0d)
        {
            return false;
        }

        depthMm = rafterHeightMm * seating.Value / 100d;
        return true;
    }

    private static RoofPurlinElevationProfile CreateElevationProfile(
        RoofRelativeElevationDatum datum,
        double centerLocalZMm,
        double purlinHeightMm,
        double? seatingDepthMm)
    {
        var bottom = centerLocalZMm - purlinHeightMm / 2d;
        var top = centerLocalZMm + purlinHeightMm / 2d;
        return new RoofPurlinElevationProfile(
            bottom,
            centerLocalZMm,
            top,
            RoofRelativeElevationDatumRules.ToRelativeElevationMm(datum, bottom),
            RoofRelativeElevationDatumRules.ToRelativeElevationMm(datum, centerLocalZMm),
            RoofRelativeElevationDatumRules.ToRelativeElevationMm(datum, top),
            seatingDepthMm);
    }

    private static RoofAutomaticPurlinPlacementResolution PlacementInvalid(
        RoofAutomaticPurlinPlanError error) => new(false, null, null, null, error);

    private static RoofAutomaticPurlinPlanResult? AddHorizontalRidges(
        HipRoofGeometry geometry,
        RoofBoundaryIdentityProvenanceResult boundaryProvenance,
        RoofAutomaticPurlinLayout layout,
        RoofAutomaticPurlinPlanningInput planningInput,
        double commonNormalZ,
        ICollection<RoofAutomaticPurlinPlanItem> items)
    {
        var resolution = RoofStructuralEdgeIdentityResolver.Resolve(
            geometry,
            boundaryProvenance);
        if (!resolution.IsValid)
        {
            var error = resolution.Error switch
            {
                RoofStructuralEdgeResolutionError.InvalidBoundaryProvenance =>
                    RoofAutomaticPurlinPlanError.InvalidBoundaryProvenance,
                RoofStructuralEdgeResolutionError.BoundaryProvenanceCountMismatch =>
                    RoofAutomaticPurlinPlanError.BoundaryProvenanceCountMismatch,
                RoofStructuralEdgeResolutionError.UnresolvedFaceBoundaryIdentity =>
                    RoofAutomaticPurlinPlanError.UnresolvedFaceBoundaryIdentity,
                RoofStructuralEdgeResolutionError.DuplicateStructuralIdentity =>
                    RoofAutomaticPurlinPlanError.DuplicateGeneratedKey,
                RoofStructuralEdgeResolutionError.InvalidStructuralSegment =>
                    RoofAutomaticPurlinPlanError.ZeroLengthSegment,
                _ => RoofAutomaticPurlinPlanError.InvalidGeometry,
            };
            var duplicate = resolution.DuplicateIdentity is null
                ? null
                : new RoofAutomaticPurlinRidgeKey(resolution.DuplicateIdentity);
            return Invalid(error, duplicateGeneratedKey: duplicate);
        }

        var ridgeWidthMm = ResolvePositiveOrDefault(
            layout.RidgeWidthMm,
            planningInput.PurlinWidthMm);
        var ridgeHeightMm = ResolvePositiveOrDefault(
            layout.RidgeHeightMm,
            planningInput.PurlinHeightMm);
        double? ridgeSeatingDepthMm = null;
        if (layout.RidgeSeatingDepth is not null)
        {
            if (!TryResolveSeatingDepth(
                    layout.RidgeSeatingDepth,
                    planningInput.RafterHeightMm,
                    out var resolvedRidgeSeat))
            {
                return Invalid(RoofAutomaticPurlinPlanError.InvalidSeatingDepth);
            }

            ridgeSeatingDepthMm = resolvedRidgeSeat;
        }
        else
        {
            ridgeSeatingDepthMm =
                planningInput.RafterHeightMm * DefaultWallPlateSeatingPercent / 100d;
        }

        // Ridge contact uses the midpoint of the TOP edge: travel seatingDepth along the
        // rafter normal from the lower surface so 0%→lower edge and 100%→upper edge.
        var horizontalLength = Math.Sqrt(Math.Max(0d, 1d - commonNormalZ * commonNormalZ));
        var representativeNormal = new RoofFaceUnitNormal(horizontalLength, 0d, commonNormalZ);

        foreach (var edge in resolution.Edges.Where(edge =>
                     edge.StructuralRole == RoofStructuralRole.Ridge &&
                     Math.Abs(edge.Segment3D.Start.Z - edge.Segment3D.End.Z) <=
                     CoordinateToleranceMm))
        {
            if (!IsFinite(edge.Length3dMm) || edge.Length3dMm <= CoordinateToleranceMm)
            {
                return Invalid(RoofAutomaticPurlinPlanError.ZeroLengthSegment);
            }

            var ridgeMid = new RoofPoint3D(
                (edge.Segment3D.Start.X + edge.Segment3D.End.X) / 2d,
                (edge.Segment3D.Start.Y + edge.Segment3D.End.Y) / 2d,
                edge.Segment3D.Start.Z);
            var physical = RoofRafterPhysicalGeometry.CreatePurlinPlacement(
                ridgeMid,
                representativeNormal,
                planningInput.RafterHeightMm,
                ridgeHeightMm,
                ridgeSeatingDepthMm!.Value,
                planningInput.RelativeElevationDatum,
                ridgeWidthMm);
            if (!physical.IsValid || physical.Placement is null)
            {
                return Invalid(RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement);
            }

            var centerLocalZMm = physical.Placement.PurlinCenterLocalZMm;
            var elevationProfile = CreateElevationProfile(
                planningInput.RelativeElevationDatum,
                centerLocalZMm,
                ridgeHeightMm,
                ridgeSeatingDepthMm);
            var seatedSegment = new RoofSegment3D(
                new RoofPoint3D(edge.Segment3D.Start.X, edge.Segment3D.Start.Y, centerLocalZMm),
                new RoofPoint3D(edge.Segment3D.End.X, edge.Segment3D.End.Y, centerLocalZMm));

            items.Add(new RoofAutomaticPurlinPlanItem(
                new RoofAutomaticPurlinRidgeKey(edge.StructuralIdentity),
                TimberElementType.Purlin,
                seatedSegment,
                ridgeWidthMm,
                ridgeHeightMm,
                elevationProfile,
                physical.Placement));
        }

        return null;
    }

    private static RoofRelativeElevationDatum CreateWallPlateBootstrapDatum() =>
        new(
            RoofRelativeElevationReferenceKind.SourceEavePlane,
            0d,
            0d);

    private static RoofAutomaticPurlinPlanningInput ApplyWallPlateSectionOverrides(
        RoofAutomaticPurlinPlanningInput planningInput,
        RoofAutomaticPurlinLayoutItem wallPlatePlacement) =>
        planningInput with
        {
            WallPlateWidthMm = ResolvePositiveOrDefault(
                wallPlatePlacement.WidthMm,
                planningInput.WallPlateWidthMm),
            WallPlateHeightMm = ResolvePositiveOrDefault(
                wallPlatePlacement.HeightMm,
                planningInput.WallPlateHeightMm),
        };

    private static RoofAutomaticPurlinPlanningInput ApplyPurlinSectionOverrides(
        RoofAutomaticPurlinPlanningInput planningInput,
        RoofAutomaticPurlinLayoutItem item) =>
        planningInput with
        {
            PurlinWidthMm = ResolvePositiveOrDefault(item.WidthMm, planningInput.PurlinWidthMm),
            PurlinHeightMm = ResolvePositiveOrDefault(item.HeightMm, planningInput.PurlinHeightMm),
        };

    private static double ResolvePositiveOrDefault(double? overrideMm, double defaultMm) =>
        overrideMm is > 0d ? overrideMm.Value : defaultMm;

    private static bool TryResolveWallPlateBottomLocalZMm(
        HipRoofGeometry geometry,
        RoofBoundaryIdentityProvenanceResult boundaryProvenance,
        RoofAutomaticPurlinLayout layout,
        RoofAutomaticPurlinPlanningInput planningInput,
        out double bottomLocalZMm,
        out RoofAutomaticPurlinPlanError error)
    {
        bottomLocalZMm = 0d;
        error = RoofAutomaticPurlinPlanError.None;
        if (!planningInput.WallPlatesEnabled ||
            !IsFinite(planningInput.WallPlateWidthMm) || planningInput.WallPlateWidthMm <= 0d ||
            !IsFinite(planningInput.WallPlateHeightMm) || planningInput.WallPlateHeightMm <= 0d ||
            !IsFinite(planningInput.RafterHeightMm) || planningInput.RafterHeightMm <= 0d)
        {
            error = RoofAutomaticPurlinPlanError.InvalidPhysicalSection;
            return false;
        }

        var topology = geometry.Topology;
        if (!HasValidCoordinates(topology) ||
            !HasCompleteBoundaryProvenance(topology, boundaryProvenance))
        {
            error = RoofAutomaticPurlinPlanError.InvalidGeometry;
            return false;
        }

        var wallPlatePlacement = RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(layout);
        planningInput = ApplyWallPlateSectionOverrides(planningInput, wallPlatePlacement);
        IReadOnlyDictionary<int, RoofFaceUnitNormal>? faceNormals = null;
        var commonNormalZ = 0d;
        if (IsSeatingDriven(wallPlatePlacement.PlacementMode) ||
            wallPlatePlacement.SeatingDepth is not null)
        {
            error = TryCreateFaceNormals(topology, out faceNormals, out commonNormalZ);
            if (error != RoofAutomaticPurlinPlanError.None)
            {
                return false;
            }
        }

        var bootstrapInput = planningInput with
        {
            RelativeElevationDatum = CreateWallPlateBootstrapDatum(),
        };
        var items = new List<RoofAutomaticPurlinPlanItem>();
        var wallPlateResult = AddWallPlates(
            geometry,
            boundaryProvenance,
            wallPlatePlacement,
            bootstrapInput,
            faceNormals,
            commonNormalZ,
            items);
        if (wallPlateResult is not null)
        {
            error = wallPlateResult.Error;
            return false;
        }

        if (items.Count == 0)
        {
            error = RoofAutomaticPurlinPlanError.InvalidRelativeElevationDatum;
            return false;
        }

        bottomLocalZMm = items[0].ElevationProfile!.BottomLocalZMm;
        foreach (var item in items)
        {
            var bottom = item.ElevationProfile!.BottomLocalZMm;
            if (Math.Abs(bottom - bottomLocalZMm) > CoordinateToleranceMm)
            {
                error = RoofAutomaticPurlinPlanError.InconsistentFaceNormalVerticalComponent;
                return false;
            }
        }

        return true;
    }

    private static RoofAutomaticPurlinPlanError RebaseWallPlateRelativeElevations(
        IList<RoofAutomaticPurlinPlanItem> items,
        RoofRelativeElevationDatum datum)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.GeneratorRole != RoofAutomaticPurlinGeneratorRole.WallPlate ||
                item.ElevationProfile is null)
            {
                continue;
            }

            var profile = item.ElevationProfile;
            var rebuiltProfile = CreateElevationProfile(
                datum,
                profile.CenterLocalZMm,
                item.HeightMm,
                profile.SeatingDepthMm);
            RoofPurlinPhysicalPlacement? rebuiltPhysical = null;
            if (item.PhysicalPlacement is { } physical)
            {
                // CreatePurlinPlacement expects the mathematical UPPER rafter face at the
                // member axis — never the centroid section point.
                var physicalResult = RoofRafterPhysicalGeometry.CreatePurlinPlacement(
                    physical.RafterSection.UpperSurfacePoint,
                    physical.RafterSection.FaceNormal,
                    physical.RafterSection.HeightMm,
                    item.HeightMm,
                    physical.SeatingDepthMm,
                    datum,
                    item.WidthMm);
                if (!physicalResult.IsValid || physicalResult.Placement is null)
                {
                    return RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement;
                }

                rebuiltPhysical = physicalResult.Placement;
            }

            items[index] = item with
            {
                ElevationProfile = rebuiltProfile,
                PhysicalPlacement = rebuiltPhysical,
            };
        }

        return RoofAutomaticPurlinPlanError.None;
    }

    /// <summary>
    /// Emits one wall plate per canonical source-eave face using the same
    /// <see cref="ResolvePlacement"/> + roof-plane slice path as Intermediate Purlin.
    /// </summary>
    private static RoofAutomaticPurlinPlanResult? AddWallPlates(
        HipRoofGeometry geometry,
        RoofBoundaryIdentityProvenanceResult boundaryProvenance,
        RoofAutomaticPurlinLayoutItem wallPlatePlacement,
        RoofAutomaticPurlinPlanningInput planningInput,
        IReadOnlyDictionary<int, RoofFaceUnitNormal>? faceNormals,
        double commonNormalZ,
        ICollection<RoofAutomaticPurlinPlanItem> items)
    {
        var topology = geometry.Topology;
        var resolveInput = planningInput with
        {
            // Bottom-edge center/width must use WallPlate section, not Purlin defaults.
            PurlinHeightMm = planningInput.WallPlateHeightMm,
            PurlinWidthMm = planningInput.WallPlateWidthMm,
        };
        var placement = ResolvePlacementCore(
            geometry,
            boundaryProvenance,
            wallPlatePlacement,
            resolveInput);
        if (!placement.IsValid || placement.RoofSurfaceLocalZMm is null)
        {
            return Invalid(
                placement.Error,
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId);
        }

        if (!RoofAutomaticPurlinWallPlatePlanDistanceRules.TryResolveAxisPlanDistanceFromEaveMm(
                wallPlatePlacement.PlacementMode,
                wallPlatePlacement.PlacementValueMm,
                placement.RoofSurfaceLocalZMm,
                geometry.Topology.PitchDegrees,
                out var axisPlanDistanceMm) ||
            !RoofAutomaticPurlinWallPlatePlanDistanceRules.IsPlanDistanceAtOrAboveMinimum(
                axisPlanDistanceMm,
                planningInput.WallPlateWidthMm))
        {
            return Invalid(
                RoofAutomaticPurlinPlanError.WallPlatePlanDistanceBelowMinimum,
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId);
        }

        var sliceLocalZMm = placement.RoofSurfaceLocalZMm.Value;
        var centerLocalZMm = sliceLocalZMm;
        RoofPurlinElevationProfile elevationProfile;
        double? seatingDepthMm = placement.SeatingDepthMm;
        if (IsSeatingDriven(wallPlatePlacement.PlacementMode) || seatingDepthMm is not null)
        {
            if (faceNormals is null || faceNormals.Count == 0 ||
                seatingDepthMm is not { } seating)
            {
                return Invalid(RoofAutomaticPurlinPlanError.MissingFaceNormal);
            }

            var representativeNormal = new RoofFaceUnitNormal(
                Math.Sqrt(Math.Max(0d, 1d - commonNormalZ * commonNormalZ)),
                0d,
                commonNormalZ);
            var physical = RoofRafterPhysicalGeometry.CreatePurlinPlacement(
                new RoofPoint3D(0d, 0d, sliceLocalZMm),
                representativeNormal,
                planningInput.RafterHeightMm,
                planningInput.WallPlateHeightMm,
                seating,
                planningInput.RelativeElevationDatum,
                planningInput.WallPlateWidthMm);
            if (!physical.IsValid || physical.Placement is null)
            {
                return Invalid(RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement);
            }

            centerLocalZMm = physical.Placement.PurlinCenterLocalZMm;
            elevationProfile = CreateElevationProfile(
                planningInput.RelativeElevationDatum,
                centerLocalZMm,
                planningInput.WallPlateHeightMm,
                seating);
        }
        else
        {
            seatingDepthMm = null;
            elevationProfile = CreateElevationProfile(
                planningInput.RelativeElevationDatum,
                centerLocalZMm,
                planningInput.WallPlateHeightMm,
                null);
        }

        var validationError = ValidateElevation(topology, geometry.RiseMm, sliceLocalZMm);
        if (validationError != RoofAutomaticPurlinPlanError.None)
        {
            return Invalid(validationError);
        }

        return AddHorizontalRoofPlaneSlices(
            topology,
            boundaryProvenance,
            failedLayoutItemId: null,
            sliceLocalZMm,
            centerLocalZMm,
            elevationProfile,
            faceNormals,
            planningInput.WallPlateWidthMm,
            planningInput.WallPlateHeightMm,
            TimberElementType.WallPlate,
            planningInput.RafterHeightMm,
            planningInput.RelativeElevationDatum,
            seatingDepthMm,
            requireSegmentOnEveryFace: true,
            oneSegmentPerFace: true,
            (sourceFaceBoundaryEdgeId, _, _) =>
                new RoofAutomaticPurlinWallPlateKey(sourceFaceBoundaryEdgeId),
            items);
    }

    /// <summary>
    /// Exclusive upper bound for <see cref="RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave"/>:
    /// axis elevation Z = d·tan(pitch) must stay strictly below roof rise. Returns false when the
    /// pitch/rise cannot form a finite positive interval.
    /// </summary>
    public static bool TryResolvePlanDistanceFromEaveExclusiveMaxMm(
        HipRoofGeometry? geometry,
        out double maxExclusivePlanDistanceMm)
    {
        maxExclusivePlanDistanceMm = 0d;
        if (geometry is null ||
            !IsFinite(geometry.RiseMm) ||
            geometry.RiseMm <= 0d ||
            !IsFinite(geometry.Topology.PitchDegrees) ||
            geometry.Topology.PitchDegrees <= 0d ||
            geometry.Topology.PitchDegrees >= 90d)
        {
            return false;
        }

        var tanPitch = Math.Tan(geometry.Topology.PitchDegrees * Math.PI / 180d);
        if (!IsFinite(tanPitch) || tanPitch <= 0d)
        {
            return false;
        }

        maxExclusivePlanDistanceMm = geometry.RiseMm / tanPitch;
        return IsFinite(maxExclusivePlanDistanceMm) && maxExclusivePlanDistanceMm > 0d;
    }

    private static RoofAutomaticPurlinPlanError ValidateElevation(
        RoofTopology topology,
        double riseMm,
        double elevationMm)
    {
        if (!IsFinite(elevationMm))
        {
            return RoofAutomaticPurlinPlanError.InvalidElevation;
        }

        if (elevationMm < 0d || elevationMm >= riseMm)
        {
            return RoofAutomaticPurlinPlanError.ElevationOutsideRoof;
        }

        return topology.Nodes.Skip(topology.BoundaryVertexCount).Any(node =>
                Math.Abs(node.Z - elevationMm) <= CoordinateToleranceMm)
            ? RoofAutomaticPurlinPlanError.CriticalEventElevation
            : RoofAutomaticPurlinPlanError.None;
    }

    private static RoofAutomaticPurlinPlanResult? AddIntermediateSlices(
        RoofTopology topology,
        RoofBoundaryIdentityProvenanceResult boundaryProvenance,
        RoofAutomaticPurlinLayoutItem layoutItem,
        double sliceLocalZMm,
        double centerLocalZMm,
        RoofPurlinElevationProfile elevationProfile,
        IReadOnlyDictionary<int, RoofFaceUnitNormal>? faceNormals,
        RoofAutomaticPurlinPlanningInput planningInput,
        double? seatingDepthMm,
        ICollection<RoofAutomaticPurlinPlanItem> items) =>
        AddHorizontalRoofPlaneSlices(
            topology,
            boundaryProvenance,
            layoutItem.LayoutItemId,
            sliceLocalZMm,
            centerLocalZMm,
            elevationProfile,
            faceNormals,
            planningInput.PurlinWidthMm,
            planningInput.PurlinHeightMm,
            TimberElementType.Purlin,
            planningInput.RafterHeightMm,
            planningInput.RelativeElevationDatum,
            seatingDepthMm,
            requireSegmentOnEveryFace: false,
            oneSegmentPerFace: false,
            (sourceFaceBoundaryEdgeId, endpointA, endpointB) =>
                new RoofAutomaticPurlinIntermediateKey(
                    layoutItem.LayoutItemId,
                    sourceFaceBoundaryEdgeId,
                    endpointA,
                    endpointB),
            items);

    /// <summary>
    /// Shared roof-plane longitudinal-member placement: horizontal Z-slice of each face
    /// clipped by Hip/Valley/Ridge boundaries, optional 25%-class seating via
    /// <see cref="RoofRafterPhysicalGeometry.CreatePurlinPlacement"/>.
    /// </summary>
    private static RoofAutomaticPurlinPlanResult? AddHorizontalRoofPlaneSlices(
        RoofTopology topology,
        RoofBoundaryIdentityProvenanceResult boundaryProvenance,
        string? failedLayoutItemId,
        double sliceLocalZMm,
        double centerLocalZMm,
        RoofPurlinElevationProfile elevationProfile,
        IReadOnlyDictionary<int, RoofFaceUnitNormal>? faceNormals,
        double memberWidthMm,
        double memberHeightMm,
        TimberElementType elementType,
        double rafterHeightMm,
        RoofRelativeElevationDatum datum,
        double? seatingDepthMm,
        bool requireSegmentOnEveryFace,
        bool oneSegmentPerFace,
        Func<int, RoofAutomaticPurlinBoundaryKey, RoofAutomaticPurlinBoundaryKey, RoofAutomaticPurlinGeneratedKey> createKey,
        ICollection<RoofAutomaticPurlinPlanItem> items)
    {
        var edgeLookup = new Dictionary<(int A, int B), RoofTopologyEdge>();
        foreach (var edge in topology.Edges)
        {
            var key = NodePair(edge.StartNodeIndex, edge.EndNodeIndex);
            if (edgeLookup.ContainsKey(key))
            {
                return Invalid(
                    RoofAutomaticPurlinPlanError.InvalidGeometry,
                    failedLayoutItemId);
            }

            edgeLookup.Add(key, edge);
        }

        foreach (var face in topology.Faces.OrderBy(candidate => candidate.SourceEdgeIndex))
        {
            if (!RoofBoundaryIdentityProvenanceResolver.TryResolveNormalizedBoundaryEdge(
                    boundaryProvenance,
                    face.SourceEdgeIndex,
                    out var sourceFace))
            {
                return Invalid(
                    RoofAutomaticPurlinPlanError.UnresolvedFaceBoundaryIdentity,
                    failedLayoutItemId);
            }

            var intersections = new List<SliceIntersection>();
            var cycle = face.BoundaryNodeIndices;
            if (cycle.Count < 3 || cycle.Any(index => index < 0 || index >= topology.Nodes.Count))
            {
                return Invalid(
                    RoofAutomaticPurlinPlanError.InvalidGeometry,
                    failedLayoutItemId);
            }

            var eaveStart = topology.Nodes[cycle[0]];
            var eaveEnd = topology.Nodes[cycle[1]];
            var eaveDx = eaveEnd.X - eaveStart.X;
            var eaveDy = eaveEnd.Y - eaveStart.Y;
            var eaveLength = Math.Sqrt(eaveDx * eaveDx + eaveDy * eaveDy);
            if (!IsFinite(eaveLength) || eaveLength <= CoordinateToleranceMm)
            {
                return Invalid(
                    RoofAutomaticPurlinPlanError.InvalidGeometry,
                    failedLayoutItemId);
            }

            var unitX = eaveDx / eaveLength;
            var unitY = eaveDy / eaveLength;
            for (var index = 0; index < cycle.Count; index++)
            {
                var startIndex = cycle[index];
                var endIndex = cycle[(index + 1) % cycle.Count];
                var start = topology.Nodes[startIndex];
                var end = topology.Nodes[endIndex];
                var belowToAbove = start.Z < sliceLocalZMm && end.Z > sliceLocalZMm;
                var aboveToBelow = start.Z > sliceLocalZMm && end.Z < sliceLocalZMm;
                if (!belowToAbove && !aboveToBelow)
                {
                    continue;
                }

                if (!edgeLookup.TryGetValue(NodePair(startIndex, endIndex), out var boundaryEdge) ||
                    !TryCreateBoundaryKey(
                        topology,
                        boundaryProvenance,
                        boundaryEdge,
                        out var boundaryKey))
                {
                    return Invalid(
                        RoofAutomaticPurlinPlanError.UnresolvedFaceBoundaryIdentity,
                        failedLayoutItemId);
                }

                var fraction = (sliceLocalZMm - start.Z) / (end.Z - start.Z);
                var point = new RoofPoint3D(
                    start.X + (end.X - start.X) * fraction,
                    start.Y + (end.Y - start.Y) * fraction,
                    centerLocalZMm);
                var station = (point.X - eaveStart.X) * unitX +
                              (point.Y - eaveStart.Y) * unitY;
                if (!IsFinite(point.X) || !IsFinite(point.Y) || !IsFinite(station))
                {
                    return Invalid(
                        RoofAutomaticPurlinPlanError.InvalidCoordinate,
                        failedLayoutItemId);
                }

                intersections.Add(new SliceIntersection(point, station, boundaryKey));
            }

            if (intersections.Count == 0)
            {
                // Plan distance 0 → axis elevation at the source eave (Z≈0). Horizontal
                // face slices need a strict crossing of Z, so place along the source-eave
                // edge instead of failing closed.
                if (sliceLocalZMm <= CoordinateToleranceMm)
                {
                    var eaveResult = TryAddSourceEaveSegment(
                        topology,
                        boundaryProvenance,
                        edgeLookup,
                        face,
                        cycle,
                        eaveStart,
                        eaveEnd,
                        failedLayoutItemId,
                        sliceLocalZMm,
                        centerLocalZMm,
                        elevationProfile,
                        faceNormals,
                        memberWidthMm,
                        memberHeightMm,
                        elementType,
                        rafterHeightMm,
                        datum,
                        seatingDepthMm,
                        createKey,
                        items);
                    if (eaveResult is not null)
                    {
                        return eaveResult;
                    }

                    continue;
                }

                if (requireSegmentOnEveryFace)
                {
                    return Invalid(
                        RoofAutomaticPurlinPlanError.ElevationOutsideRoof,
                        failedLayoutItemId);
                }

                continue;
            }

            if (intersections.Count % 2 != 0)
            {
                return Invalid(
                    RoofAutomaticPurlinPlanError.InvalidFaceIntersection,
                    failedLayoutItemId);
            }

            var ordered = intersections
                .OrderBy(intersection => intersection.StationMm)
                .ThenBy(intersection => intersection.BoundaryKey, BoundaryKeyComparer.Instance)
                .ToArray();
            var intervalStarts = Enumerable.Range(0, ordered.Length / 2)
                .Select(pairIndex => pairIndex * 2)
                .ToArray();
            if (oneSegmentPerFace && intervalStarts.Length > 1)
            {
                intervalStarts =
                [
                    intervalStarts
                        .OrderBy(pairIndex =>
                            (ordered[pairIndex].StationMm + ordered[pairIndex + 1].StationMm) / 2d)
                        .ThenBy(pairIndex => ordered[pairIndex].BoundaryKey, BoundaryKeyComparer.Instance)
                        .First()
                ];
            }

            foreach (var pairIndex in intervalStarts)
            {
                var first = ordered[pairIndex];
                var second = ordered[pairIndex + 1];
                var segment = new RoofSegment3D(first.Point, second.Point);
                if (!IsFinite(segment.LengthMm))
                {
                    return Invalid(
                        RoofAutomaticPurlinPlanError.InvalidCoordinate,
                        failedLayoutItemId);
                }

                if (segment.LengthMm <= CoordinateToleranceMm)
                {
                    return Invalid(
                        RoofAutomaticPurlinPlanError.ZeroLengthSegment,
                        failedLayoutItemId);
                }

                var endpointKeys = new[] { first.BoundaryKey, second.BoundaryKey }
                    .OrderBy(key => key, BoundaryKeyComparer.Instance)
                    .ToArray();
                RoofPurlinPhysicalPlacement? physicalPlacement = null;
                if (seatingDepthMm is { } seating)
                {
                    if (faceNormals is null ||
                        !faceNormals.TryGetValue(face.SourceEdgeIndex, out var faceNormal))
                    {
                        return Invalid(
                            RoofAutomaticPurlinPlanError.MissingFaceNormal,
                            failedLayoutItemId);
                    }

                    // sliceLocalZMm is the mathematical UPPER roof-face elevation at the
                    // member plan station (SourceEave / topology face), not the centroid.
                    var upperFaceAtAxis = new RoofPoint3D(
                        (first.Point.X + second.Point.X) / 2d,
                        (first.Point.Y + second.Point.Y) / 2d,
                        sliceLocalZMm);
                    var physical = RoofRafterPhysicalGeometry.CreatePurlinPlacement(
                        upperFaceAtAxis,
                        faceNormal,
                        rafterHeightMm,
                        memberHeightMm,
                        seating,
                        datum,
                        memberWidthMm);
                    if (!physical.IsValid || physical.Placement is null ||
                        Math.Abs(physical.Placement.PurlinCenterLocalZMm - centerLocalZMm) >
                        CoordinateToleranceMm * (1d + Math.Abs(rafterHeightMm)))
                    {
                        return Invalid(
                            RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement,
                            failedLayoutItemId);
                    }

                    physicalPlacement = physical.Placement;
                }

                items.Add(new RoofAutomaticPurlinPlanItem(
                    createKey(sourceFace.BoundaryEdgeId, endpointKeys[0], endpointKeys[1]),
                    elementType,
                    segment,
                    memberWidthMm,
                    memberHeightMm,
                    elevationProfile,
                    physicalPlacement));
            }
        }

        return null;
    }

    /// <summary>
    /// Places one longitudinal member along the face source-eave edge when the plan
    /// station is at the eave (axis elevation Z≈0). Used by plan-distance 0 so the
    /// outer seating corner may overhang the eave by half the member width.
    /// </summary>
    private static RoofAutomaticPurlinPlanResult? TryAddSourceEaveSegment(
        RoofTopology topology,
        RoofBoundaryIdentityProvenanceResult boundaryProvenance,
        IReadOnlyDictionary<(int A, int B), RoofTopologyEdge> edgeLookup,
        RoofTopologyFace face,
        IReadOnlyList<int> cycle,
        RoofPoint3D eaveStart,
        RoofPoint3D eaveEnd,
        string? failedLayoutItemId,
        double sliceLocalZMm,
        double centerLocalZMm,
        RoofPurlinElevationProfile elevationProfile,
        IReadOnlyDictionary<int, RoofFaceUnitNormal>? faceNormals,
        double memberWidthMm,
        double memberHeightMm,
        TimberElementType elementType,
        double rafterHeightMm,
        RoofRelativeElevationDatum datum,
        double? seatingDepthMm,
        Func<int, RoofAutomaticPurlinBoundaryKey, RoofAutomaticPurlinBoundaryKey, RoofAutomaticPurlinGeneratedKey> createKey,
        ICollection<RoofAutomaticPurlinPlanItem> items)
    {
        if (!RoofBoundaryIdentityProvenanceResolver.TryResolveNormalizedBoundaryEdge(
                boundaryProvenance,
                face.SourceEdgeIndex,
                out var sourceFace))
        {
            return Invalid(
                RoofAutomaticPurlinPlanError.UnresolvedFaceBoundaryIdentity,
                failedLayoutItemId);
        }

        var startIndex = cycle[0];
        var endIndex = cycle[1];
        if (!edgeLookup.TryGetValue(NodePair(startIndex, endIndex), out var eaveEdge) ||
            !TryCreateBoundaryKey(topology, boundaryProvenance, eaveEdge, out var eaveKey))
        {
            return Invalid(
                RoofAutomaticPurlinPlanError.UnresolvedFaceBoundaryIdentity,
                failedLayoutItemId);
        }

        var start = new RoofPoint3D(eaveStart.X, eaveStart.Y, centerLocalZMm);
        var end = new RoofPoint3D(eaveEnd.X, eaveEnd.Y, centerLocalZMm);
        var segment = new RoofSegment3D(start, end);
        if (!IsFinite(segment.LengthMm))
        {
            return Invalid(
                RoofAutomaticPurlinPlanError.InvalidCoordinate,
                failedLayoutItemId);
        }

        if (segment.LengthMm <= CoordinateToleranceMm)
        {
            return Invalid(
                RoofAutomaticPurlinPlanError.ZeroLengthSegment,
                failedLayoutItemId);
        }

        RoofPurlinPhysicalPlacement? physicalPlacement = null;
        var profile = elevationProfile;
        if (seatingDepthMm is { } seating)
        {
            if (faceNormals is null ||
                !faceNormals.TryGetValue(face.SourceEdgeIndex, out var faceNormal))
            {
                return Invalid(
                    RoofAutomaticPurlinPlanError.MissingFaceNormal,
                    failedLayoutItemId);
            }

            var upperFaceAtAxis = new RoofPoint3D(
                (eaveStart.X + eaveEnd.X) / 2d,
                (eaveStart.Y + eaveEnd.Y) / 2d,
                sliceLocalZMm);
            var physical = RoofRafterPhysicalGeometry.CreatePurlinPlacement(
                upperFaceAtAxis,
                faceNormal,
                rafterHeightMm,
                memberHeightMm,
                seating,
                datum,
                memberWidthMm);
            if (!physical.IsValid || physical.Placement is null)
            {
                return Invalid(
                    RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement,
                    failedLayoutItemId);
            }

            physicalPlacement = physical.Placement;
            var seatedCenterZ = physical.Placement.PurlinCenterLocalZMm;
            profile = CreateElevationProfile(datum, seatedCenterZ, memberHeightMm, seating);
            segment = new RoofSegment3D(
                new RoofPoint3D(eaveStart.X, eaveStart.Y, seatedCenterZ),
                new RoofPoint3D(eaveEnd.X, eaveEnd.Y, seatedCenterZ));
        }

        // Eave endpoints share the same eave boundary key; intermediate keys still need
        // two ordered endpoint slots, so reuse the single eave identity for both.
        items.Add(new RoofAutomaticPurlinPlanItem(
            createKey(sourceFace.BoundaryEdgeId, eaveKey, eaveKey),
            elementType,
            segment,
            memberWidthMm,
            memberHeightMm,
            profile,
            physicalPlacement));
        return null;
    }

    private static bool TryCreateBoundaryKey(
        RoofTopology topology,
        RoofBoundaryIdentityProvenanceResult boundaryProvenance,
        RoofTopologyEdge edge,
        out RoofAutomaticPurlinBoundaryKey key)
    {
        key = null!;
        if (edge.Kind == RoofTopologyEdgeKind.Eave)
        {
            if (edge.FaceIndices.Count != 1 ||
                edge.FaceIndices[0] < 0 ||
                edge.FaceIndices[0] >= topology.Faces.Count ||
                !RoofBoundaryIdentityProvenanceResolver.TryResolveNormalizedBoundaryEdge(
                    boundaryProvenance,
                    topology.Faces[edge.FaceIndices[0]].SourceEdgeIndex,
                    out var eave))
            {
                return false;
            }

            key = new RoofAutomaticPurlinBoundaryKey(
                RoofTopologyEdgeKind.Eave,
                eave.BoundaryEdgeId,
                0);
            return eave.BoundaryEdgeId > 0;
        }

        if (edge.Kind is not (RoofTopologyEdgeKind.Hip or
            RoofTopologyEdgeKind.Valley or
            RoofTopologyEdgeKind.Ridge or
            RoofTopologyEdgeKind.CoplanarSeam) ||
            edge.FaceIndices.Count != 2 ||
            edge.FaceIndices.Any(index => index < 0 || index >= topology.Faces.Count))
        {
            return false;
        }

        var firstSource = topology.Faces[edge.FaceIndices[0]].SourceEdgeIndex;
        var secondSource = topology.Faces[edge.FaceIndices[1]].SourceEdgeIndex;
        if (!RoofBoundaryIdentityProvenanceResolver.TryResolveBoundaryPair(
                boundaryProvenance,
                firstSource,
                secondSource,
                out var pair))
        {
            return false;
        }

        key = new RoofAutomaticPurlinBoundaryKey(
            edge.Kind,
            pair.LowerBoundaryEdgeId,
            pair.UpperBoundaryEdgeId);
        return true;
    }

    private static bool HasValidCoordinates(RoofTopology topology)
    {
        if (topology.Nodes.Count == 0 ||
            topology.BoundaryVertexCount <= 0 ||
            topology.BoundaryVertexCount > topology.Nodes.Count ||
            topology.Faces.Count == 0 ||
            !IsFinite(topology.PitchDegrees))
        {
            return false;
        }

        return topology.Nodes.All(point =>
            IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z)) &&
            topology.Edges.All(edge =>
                edge.StartNodeIndex >= 0 &&
                edge.StartNodeIndex < topology.Nodes.Count &&
                edge.EndNodeIndex >= 0 &&
                edge.EndNodeIndex < topology.Nodes.Count);
    }

    private static bool HasCompleteBoundaryProvenance(
        RoofTopology topology,
        RoofBoundaryIdentityProvenanceResult boundaryProvenance) =>
        boundaryProvenance.EdgeProvenance.Count == topology.BoundaryVertexCount &&
        boundaryProvenance.EdgeProvenance
            .Select(edge => edge.NormalizedBoundaryEdgeIndex)
            .OrderBy(index => index)
            .SequenceEqual(Enumerable.Range(0, topology.BoundaryVertexCount));

    private static RoofAutomaticPurlinPlanError TryCreateFaceNormals(
        RoofTopology topology,
        out IReadOnlyDictionary<int, RoofFaceUnitNormal>? normals,
        out double commonNormalZ)
    {
        normals = null;
        commonNormalZ = 0d;
        if (topology.Faces.Count == 0)
        {
            return RoofAutomaticPurlinPlanError.MissingFaceNormal;
        }

        var bySourceEdge = new Dictionary<int, RoofFaceUnitNormal>();
        foreach (var face in topology.Faces)
        {
            if (!RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                    topology,
                    face,
                    out var normal) ||
                !RoofRafterPhysicalGeometry.IsValidUnitNormal(normal) ||
                bySourceEdge.ContainsKey(face.SourceEdgeIndex))
            {
                return RoofAutomaticPurlinPlanError.InvalidFaceNormal;
            }

            bySourceEdge.Add(face.SourceEdgeIndex, normal);

            if (bySourceEdge.Count == 1)
            {
                commonNormalZ = normal.Z;
            }
            else if (Math.Abs(normal.Z - commonNormalZ) > CoordinateToleranceMm)
            {
                return RoofAutomaticPurlinPlanError.InconsistentFaceNormalVerticalComponent;
            }
        }

        normals = bySourceEdge;
        return RoofAutomaticPurlinPlanError.None;
    }

    private static bool IsSeatingDriven(RoofAutomaticPurlinPlacementMode mode) =>
        mode is RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave or
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;

    private static (int A, int B) NodePair(int first, int second) =>
        (Math.Min(first, second), Math.Max(first, second));

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static RoofAutomaticPurlinPlanResult Invalid(
        RoofAutomaticPurlinPlanError error,
        string? failedLayoutItemId = null,
        RoofAutomaticPurlinGeneratedKey? duplicateGeneratedKey = null) => new(
            false,
            null,
            error,
            failedLayoutItemId,
            duplicateGeneratedKey);

    private sealed record SliceIntersection(
        RoofPoint3D Point,
        double StationMm,
        RoofAutomaticPurlinBoundaryKey BoundaryKey);

    private sealed class BoundaryKeyComparer : IComparer<RoofAutomaticPurlinBoundaryKey>
    {
        internal static readonly BoundaryKeyComparer Instance = new();

        public int Compare(
            RoofAutomaticPurlinBoundaryKey? first,
            RoofAutomaticPurlinBoundaryKey? second)
        {
            if (ReferenceEquals(first, second)) return 0;
            if (first is null) return -1;
            if (second is null) return 1;
            var kind = first.Kind.CompareTo(second.Kind);
            if (kind != 0) return kind;
            var firstId = first.BoundaryEdgeIdA.CompareTo(second.BoundaryEdgeIdA);
            return firstId != 0
                ? firstId
                : first.BoundaryEdgeIdB.CompareTo(second.BoundaryEdgeIdB);
        }
    }
}
