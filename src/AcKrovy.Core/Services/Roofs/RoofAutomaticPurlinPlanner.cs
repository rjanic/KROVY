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

    public static RoofAutomaticPurlinPlanResult Create(
        HipRoofGeometry? geometry,
        RoofBoundaryIdentityProvenanceResult? boundaryProvenance,
        RoofAutomaticPurlinLayout? layout,
        RoofAutomaticPurlinPlanningInput? planningInput) =>
        CreateCore(geometry, boundaryProvenance, layout, planningInput);

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

        if (!IsFinite(planningInput.PurlinHeightMm) || planningInput.PurlinHeightMm <= 0d ||
            !IsFinite(planningInput.RafterHeightMm) || planningInput.RafterHeightMm <= 0d)
        {
            return Invalid(RoofAutomaticPurlinPlanError.InvalidPhysicalSection);
        }

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
        if (normalizedItems.Any(item => item.Enabled && IsSeatingDriven(item.PlacementMode)))
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
        if (layout.RidgeEnabled)
        {
            var ridgeResult = AddHorizontalRidges(
                geometry,
                boundaryProvenance,
                planningInput,
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
            var placement = ResolvePlacementCore(
                geometry,
                boundaryProvenance,
                item,
                planningInput);
            if (!placement.IsValid || placement.RoofSurfaceLocalZMm is null)
            {
                return Invalid(placement.Error, item.LayoutItemId);
            }

            var sliceLocalZMm = placement.RoofSurfaceLocalZMm.Value;
            var centerLocalZMm = sliceLocalZMm;
            RoofPurlinElevationProfile elevationProfile;
            if (IsSeatingDriven(item.PlacementMode))
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
                    planningInput.RafterHeightMm,
                    planningInput.PurlinHeightMm,
                    seatingDepthMm,
                    planningInput.RelativeElevationDatum);
                if (!physical.IsValid || physical.Placement is null)
                {
                    return Invalid(
                        RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement,
                        item.LayoutItemId);
                }

                centerLocalZMm = physical.Placement.PurlinCenterLocalZMm;
                elevationProfile = CreateElevationProfile(
                    planningInput.RelativeElevationDatum,
                    centerLocalZMm,
                    planningInput.PurlinHeightMm,
                    seatingDepthMm);
            }
            else
            {
                elevationProfile = CreateElevationProfile(
                    planningInput.RelativeElevationDatum,
                    centerLocalZMm,
                    planningInput.PurlinHeightMm,
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
                planningInput,
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
            !IsFinite(item.PlacementValueMm) || item.PlacementValueMm <= 0d ||
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
                if (item.ReferenceRidgeKey is not null || item.SeatingDepth is not null)
                {
                    return PlacementInvalid(RoofAutomaticPurlinPlanError.InvalidLayout);
                }

                return new RoofAutomaticPurlinPlacementResolution(
                    true,
                    planningInput.RelativeElevationDatum.ReferenceLocalZMm +
                    item.PlacementValueMm + planningInput.PurlinHeightMm / 2d,
                    null,
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

                return new RoofAutomaticPurlinPlacementResolution(
                    true,
                    item.PlacementValueMm * Math.Tan(geometry.Topology.PitchDegrees * Math.PI / 180d),
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
        if (seating is null || !IsFinite(seating.Value) || seating.Value <= 0d)
        {
            return false;
        }

        if (seating.Mode == RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm)
        {
            if (!IsFinite(rafterHeightMm) || rafterHeightMm <= 0d || seating.Value >= rafterHeightMm)
            {
                return false;
            }

            depthMm = seating.Value;
            return true;
        }

        if (seating.Mode != RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight ||
            seating.Value >= 100d || !IsFinite(rafterHeightMm) || rafterHeightMm <= 0d)
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
        RoofAutomaticPurlinPlanningInput planningInput,
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

        foreach (var edge in resolution.Edges.Where(edge =>
                     edge.StructuralRole == RoofStructuralRole.Ridge &&
                     Math.Abs(edge.Segment3D.Start.Z - edge.Segment3D.End.Z) <=
                     CoordinateToleranceMm))
        {
            if (!IsFinite(edge.Length3dMm) || edge.Length3dMm <= CoordinateToleranceMm)
            {
                return Invalid(RoofAutomaticPurlinPlanError.ZeroLengthSegment);
            }

            items.Add(new RoofAutomaticPurlinPlanItem(
                new RoofAutomaticPurlinRidgeKey(edge.StructuralIdentity),
                TimberElementType.Purlin,
                edge.Segment3D,
                CreateElevationProfile(
                    planningInput.RelativeElevationDatum,
                    edge.Segment3D.Start.Z,
                    planningInput.PurlinHeightMm,
                    null)));
        }

        return null;
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

        if (elevationMm <= 0d || elevationMm >= riseMm)
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
                    layoutItem.LayoutItemId);
            }

            edgeLookup.Add(key, edge);
        }

        foreach (var face in topology.Faces)
        {
            if (!RoofBoundaryIdentityProvenanceResolver.TryResolveNormalizedBoundaryEdge(
                    boundaryProvenance,
                    face.SourceEdgeIndex,
                    out var sourceFace))
            {
                return Invalid(
                    RoofAutomaticPurlinPlanError.UnresolvedFaceBoundaryIdentity,
                    layoutItem.LayoutItemId);
            }

            var intersections = new List<SliceIntersection>();
            var cycle = face.BoundaryNodeIndices;
            if (cycle.Count < 3 || cycle.Any(index => index < 0 || index >= topology.Nodes.Count))
            {
                return Invalid(
                    RoofAutomaticPurlinPlanError.InvalidGeometry,
                    layoutItem.LayoutItemId);
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
                    layoutItem.LayoutItemId);
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
                        layoutItem.LayoutItemId);
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
                        layoutItem.LayoutItemId);
                }

                intersections.Add(new SliceIntersection(point, station, boundaryKey));
            }

            if (intersections.Count == 0)
            {
                continue;
            }

            if (intersections.Count % 2 != 0)
            {
                return Invalid(
                    RoofAutomaticPurlinPlanError.InvalidFaceIntersection,
                    layoutItem.LayoutItemId);
            }

            var ordered = intersections
                .OrderBy(intersection => intersection.StationMm)
                .ThenBy(intersection => intersection.BoundaryKey, BoundaryKeyComparer.Instance)
                .ToArray();
            for (var index = 0; index < ordered.Length; index += 2)
            {
                var first = ordered[index];
                var second = ordered[index + 1];
                var segment = new RoofSegment3D(first.Point, second.Point);
                if (!IsFinite(segment.LengthMm))
                {
                    return Invalid(
                        RoofAutomaticPurlinPlanError.InvalidCoordinate,
                        layoutItem.LayoutItemId);
                }

                if (segment.LengthMm <= CoordinateToleranceMm)
                {
                    return Invalid(
                        RoofAutomaticPurlinPlanError.ZeroLengthSegment,
                        layoutItem.LayoutItemId);
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
                            layoutItem.LayoutItemId);
                    }

                    var rafterCenter = new RoofPoint3D(
                        (first.Point.X + second.Point.X) / 2d,
                        (first.Point.Y + second.Point.Y) / 2d,
                        sliceLocalZMm);
                    var physical = RoofRafterPhysicalGeometry.CreatePurlinPlacement(
                        rafterCenter,
                        faceNormal,
                        planningInput.RafterHeightMm,
                        planningInput.PurlinHeightMm,
                        seating,
                        planningInput.RelativeElevationDatum);
                    if (!physical.IsValid || physical.Placement is null ||
                        Math.Abs(physical.Placement.PurlinCenterLocalZMm - centerLocalZMm) >
                        CoordinateToleranceMm)
                    {
                        return Invalid(
                            RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement,
                            layoutItem.LayoutItemId);
                    }

                    physicalPlacement = physical.Placement;
                }

                items.Add(new RoofAutomaticPurlinPlanItem(
                    new RoofAutomaticPurlinIntermediateKey(
                        layoutItem.LayoutItemId,
                        sourceFace.BoundaryEdgeId,
                        endpointKeys[0],
                        endpointKeys[1]),
                    TimberElementType.Purlin,
                    segment,
                    elevationProfile,
                    physicalPlacement));
            }
        }

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
