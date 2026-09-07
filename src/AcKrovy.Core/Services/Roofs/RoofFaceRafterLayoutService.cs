using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Generates ordinary roof-face rafter centerlines from an indexed roof topology.
/// Compatible opposing faces on a connected collinear Ridge component share one
/// component-centered station lattice; each face still slices its complete
/// boundary with that family.
/// </summary>
public static class RoofFaceRafterLayoutService
{
    public const double CoordinateToleranceMm =
        SimpleGableRoofGeometryTolerance.CoordinateToleranceMm;
    private const double AngularTolerance =
        SimpleGableRoofGeometryTolerance.AngularTolerance;
    private const int MaximumStationCount = 1_000_000;

    public static RoofFaceRafterLayoutResult Create(
        RoofTopology topology,
        double spacingMm)
    {
        if (topology is null)
        {
            throw new ArgumentNullException(nameof(topology));
        }
        if (!RoofRafterSpacingRules.IsValidDimension(spacingMm))
        {
            return Invalid(RoofFaceRafterLayoutError.InvalidSpacing);
        }
        if (topology.Faces.Count == 0 ||
            topology.Edges.Count == 0 ||
            topology.Nodes.Count == 0)
        {
            return Invalid(RoofFaceRafterLayoutError.InvalidTopology);
        }

        var edgeByNodes = topology.Edges.ToDictionary(
            edge => Key(edge.StartNodeIndex, edge.EndNodeIndex));
        if (!TryBuildFaceContexts(
                topology,
                edgeByNodes,
                out var facesByIndex,
                out var error))
        {
            return Invalid(error);
        }

        var ridgePhases = ResolveRidgeCoordinatedPhases(
            topology,
            facesByIndex,
            spacingMm);
        var segments = new List<RoofFaceRafterSegment>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var face in facesByIndex.Values.OrderBy(item => item.FaceIndex))
        {
            if (!TryCreateFaceSegments(
                    topology,
                    face,
                    edgeByNodes,
                    spacingMm,
                    ridgePhases.TryGetValue(face.FaceIndex, out var phase)
                        ? phase
                        : null,
                    segments,
                    seen,
                    out error))
            {
                return Invalid(error);
            }
        }

        var ordered = segments
            .OrderBy(segment => segment.SourceEaveEdgeIndex)
            .ThenBy(segment => segment.StationDistanceMm)
            .ThenBy(segment => segment.StationIntervalIndex)
            .ThenBy(segment => segment.StartBoundaryRole)
            .ThenBy(segment => segment.EndBoundaryRole)
            .ToArray();
        var signature = BuildSignature(topology, spacingMm, ordered);
        return new RoofFaceRafterLayoutResult(
            true,
            new RoofFaceRafterLayout(spacingMm, ordered, signature),
            RoofFaceRafterLayoutError.None);
    }

    /// <summary>
    /// Uses exact center-to-center spacing. For interval length L and spacing S, the
    /// count is max(1, floor(L/S)); the symmetric end margin is
    /// (L - (count - 1)S) / 2. An interval shorter than S therefore has one center.
    /// </summary>
    public static IReadOnlyList<double> CreateStationDistances(
        double eaveLengthMm,
        double spacingMm)
    {
        if (!IsFinite(eaveLengthMm) ||
            eaveLengthMm <= CoordinateToleranceMm ||
            !RoofRafterSpacingRules.IsValidDimension(spacingMm))
        {
            return Array.Empty<double>();
        }

        var rawCount = Math.Floor(eaveLengthMm / spacingMm);
        if (!IsFinite(rawCount) || rawCount > MaximumStationCount)
        {
            return Array.Empty<double>();
        }

        var count = Math.Max(1, (int)rawCount);
        var endMargin = (eaveLengthMm - (count - 1d) * spacingMm) / 2d;
        return Array.AsReadOnly(Enumerable.Range(0, count)
            .Select(index => endMargin + index * spacingMm)
            .ToArray());
    }

    /// <summary>
    /// Opposing faces may share one station lattice when their ordinary-rafter
    /// directions are parallel or antiparallel within Core angular tolerance and
    /// their station axes are therefore equivalent after canonical orientation.
    /// </summary>
    public static bool AreCompatibleOpposingRafterFamilies(
        RoofPoint2D firstRafterDirection,
        RoofPoint2D secondRafterDirection) =>
        AreUnitDirectionsCompatible(
            new Vector2(firstRafterDirection.X, firstRafterDirection.Y),
            new Vector2(secondRafterDirection.X, secondRafterDirection.Y));

    private static bool TryBuildFaceContexts(
        RoofTopology topology,
        IReadOnlyDictionary<(int Start, int End), RoofTopologyEdge> edgeByNodes,
        out Dictionary<int, FaceContext> facesByIndex,
        out RoofFaceRafterLayoutError error)
    {
        facesByIndex = new Dictionary<int, FaceContext>();
        error = RoofFaceRafterLayoutError.InvalidTopology;
        foreach (var face in topology.Faces)
        {
            var cycle = face.BoundaryNodeIndices;
            if (cycle.Count < 3 ||
                cycle[0] != face.SourceEdgeIndex ||
                !edgeByNodes.TryGetValue(Key(cycle[0], cycle[1]), out var eave) ||
                eave.Kind != RoofTopologyEdgeKind.Eave)
            {
                return false;
            }

            var eaveStart = Plan(topology.Nodes[cycle[0]]);
            var eaveEnd = Plan(topology.Nodes[cycle[1]]);
            var deltaX = eaveEnd.X - eaveStart.X;
            var deltaY = eaveEnd.Y - eaveStart.Y;
            var eaveLength = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (!IsFinite(eaveLength) || eaveLength <= CoordinateToleranceMm)
            {
                return false;
            }

            var eaveDirection = new Vector2(
                deltaX / eaveLength,
                deltaY / eaveLength);
            // Topology faces are upward-facing and their boundary cycles are CCW in plan,
            // so the normalized left perpendicular of the eave points into its face.
            var rafterDirection = new Vector2(
                -deltaY / eaveLength,
                deltaX / eaveLength);
            facesByIndex[face.SourceEdgeIndex] = new FaceContext(
                face.SourceEdgeIndex,
                cycle,
                eaveStart,
                eaveDirection,
                rafterDirection,
                eaveLength);
        }

        error = RoofFaceRafterLayoutError.None;
        return true;
    }

    private static IReadOnlyDictionary<int, SharedStationPhase> ResolveRidgeCoordinatedPhases(
        RoofTopology topology,
        IReadOnlyDictionary<int, FaceContext> facesByIndex,
        double spacingMm)
    {
        var candidates = CollectCompatibleRidgeEdges(topology, facesByIndex);
        if (candidates.Count == 0)
        {
            return new Dictionary<int, SharedStationPhase>();
        }

        var parent = Enumerable.Range(0, candidates.Count).ToArray();
        int Find(int index)
        {
            while (parent[index] != index)
            {
                parent[index] = parent[parent[index]];
                index = parent[index];
            }
            return index;
        }

        void Union(int first, int second)
        {
            var a = Find(first);
            var b = Find(second);
            if (a == b)
            {
                return;
            }

            if (a < b)
            {
                parent[b] = a;
            }
            else
            {
                parent[a] = b;
            }
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                if (AreConnectedRidgeComponentEdges(candidates[i], candidates[j]))
                {
                    Union(i, j);
                }
            }
        }

        var components = candidates
            .Select((edge, index) => (Edge: edge, Root: Find(index)))
            .GroupBy(item => item.Root)
            .OrderBy(group => group.Min(item => item.Edge.EdgeIndex))
            .Select(group => group
                .Select(item => item.Edge)
                .OrderBy(edge => edge.EdgeIndex)
                .ToArray())
            .ToArray();

        var assigned = new Dictionary<int, SharedStationPhase>();
        var conflictingFaces = new HashSet<int>();
        foreach (var component in components)
        {
            if (!TryCreateComponentPhase(component, spacingMm, out var phase))
            {
                continue;
            }

            var faces = component
                .SelectMany(edge => edge.FaceIndices)
                .Distinct()
                .OrderBy(index => index)
                .ToArray();
            foreach (var faceIndex in faces)
            {
                if (conflictingFaces.Contains(faceIndex))
                {
                    continue;
                }

                if (!assigned.TryGetValue(faceIndex, out var existing))
                {
                    assigned[faceIndex] = phase;
                    continue;
                }

                if (AreIdenticalPhases(existing, phase))
                {
                    continue;
                }

                // Genuinely incompatible component phases on one face: fall back.
                conflictingFaces.Add(faceIndex);
                assigned.Remove(faceIndex);
            }
        }

        foreach (var faceIndex in conflictingFaces)
        {
            assigned.Remove(faceIndex);
        }

        return assigned;
    }

    private static IReadOnlyList<CompatibleRidgeEdge> CollectCompatibleRidgeEdges(
        RoofTopology topology,
        IReadOnlyDictionary<int, FaceContext> facesByIndex)
    {
        var candidates = new List<CompatibleRidgeEdge>();
        for (var index = 0; index < topology.Edges.Count; index++)
        {
            var edge = topology.Edges[index];
            if (edge.Kind != RoofTopologyEdgeKind.Ridge ||
                edge.FaceIndices.Count != 2 ||
                !facesByIndex.TryGetValue(edge.FaceIndices[0], out var first) ||
                !facesByIndex.TryGetValue(edge.FaceIndices[1], out var second) ||
                !TryCreatePairStationAxis(first, second, out var stationAxis))
            {
                continue;
            }

            var start = Plan(topology.Nodes[edge.StartNodeIndex]);
            var end = Plan(topology.Nodes[edge.EndNodeIndex]);
            var delta = new Vector2(end.X - start.X, end.Y - start.Y);
            var length = Length(delta);
            if (length <= CoordinateToleranceMm)
            {
                continue;
            }

            candidates.Add(new CompatibleRidgeEdge(
                index,
                edge.StartNodeIndex,
                edge.EndNodeIndex,
                start,
                end,
                CanonicalAxis(delta),
                stationAxis,
                [edge.FaceIndices[0], edge.FaceIndices[1]]));
        }

        return candidates
            .OrderBy(edge => edge.EdgeIndex)
            .ToArray();
    }

    private static bool TryCreatePairStationAxis(
        FaceContext first,
        FaceContext second,
        out Vector2 stationAxis)
    {
        stationAxis = default;
        if (!AreUnitDirectionsCompatible(first.RafterDirection, second.RafterDirection) ||
            !AreUnitDirectionsCompatible(first.EaveDirection, second.EaveDirection))
        {
            return false;
        }

        stationAxis = CanonicalAxis(first.EaveDirection);
        return AreUnitDirectionsCompatible(stationAxis, second.EaveDirection);
    }

    private static bool AreConnectedRidgeComponentEdges(
        CompatibleRidgeEdge first,
        CompatibleRidgeEdge second)
    {
        var shareEndpoint =
            first.StartNodeIndex == second.StartNodeIndex ||
            first.StartNodeIndex == second.EndNodeIndex ||
            first.EndNodeIndex == second.StartNodeIndex ||
            first.EndNodeIndex == second.EndNodeIndex;
        return shareEndpoint &&
               AreUnitDirectionsCompatible(first.RidgeDirection, second.RidgeDirection) &&
               AreUnitDirectionsCompatible(first.StationAxis, second.StationAxis);
    }

    private static bool TryCreateComponentPhase(
        IReadOnlyList<CompatibleRidgeEdge> component,
        double spacingMm,
        out SharedStationPhase phase)
    {
        phase = default!;
        if (component.Count == 0)
        {
            return false;
        }

        var stationAxis = CanonicalAxis(component[0].StationAxis);
        if (component.Any(edge =>
                !AreUnitDirectionsCompatible(edge.StationAxis, stationAxis)))
        {
            return false;
        }

        var projections = component
            .SelectMany(edge => new[]
            {
                Dot(edge.Start, stationAxis),
                Dot(edge.End, stationAxis),
            })
            .ToArray();
        if (projections.Length == 0 || projections.Any(value => !IsFinite(value)))
        {
            return false;
        }

        var tMin = projections.Min();
        var tMax = projections.Max();
        var projectedLength = tMax - tMin;
        var ridgeStations = CreateStationDistances(projectedLength, spacingMm);
        if (ridgeStations.Count == 0)
        {
            return false;
        }

        phase = new SharedStationPhase(
            stationAxis,
            tMin + ridgeStations[0],
            spacingMm);
        return true;
    }

    private static bool AreIdenticalPhases(
        SharedStationPhase first,
        SharedStationPhase second)
    {
        if (!AreUnitDirectionsCompatible(first.StationAxis, second.StationAxis) ||
            Math.Abs(first.SpacingMm - second.SpacingMm) > CoordinateToleranceMm)
        {
            return false;
        }

        var axis = CanonicalAxis(first.StationAxis);
        var firstT = first.AbsolutePhaseT * Dot(first.StationAxis, axis);
        var secondT = second.AbsolutePhaseT * Dot(second.StationAxis, axis);
        return Math.Abs(firstT - secondT) <= CoordinateToleranceMm;
    }

    private static bool TryCreateFaceSegments(
        RoofTopology topology,
        FaceContext face,
        IReadOnlyDictionary<(int Start, int End), RoofTopologyEdge> edgeByNodes,
        double spacingMm,
        SharedStationPhase? ridgePhase,
        ICollection<RoofFaceRafterSegment> output,
        ISet<string> seen,
        out RoofFaceRafterLayoutError error)
    {
        error = RoofFaceRafterLayoutError.InvalidTopology;
        if (!TryCreateFaceStations(
                topology,
                face,
                spacingMm,
                ridgePhase,
                out var stations,
                out var tooManyStations))
        {
            error = tooManyStations
                ? RoofFaceRafterLayoutError.TooManyStations
                : RoofFaceRafterLayoutError.InvalidTopology;
            return false;
        }

        foreach (var faceStation in stations)
        {
            var station = faceStation.DistanceMm;
            var fraction = station / face.EaveLengthMm;
            var stationOrigin = new RoofPoint2D(
                face.EaveStart.X + face.EaveDirection.X * face.EaveLengthMm * fraction,
                face.EaveStart.Y + face.EaveDirection.Y * face.EaveLengthMm * fraction);

            var intervals = FindFaceIntervals(
                topology,
                face.Cycle,
                edgeByNodes,
                stationOrigin,
                face.RafterDirection);
            for (var intervalIndex = 0; intervalIndex < intervals.Count; intervalIndex++)
            {
                var interval = intervals[intervalIndex];
                var segment = new RoofFaceRafterSegment(
                    face.FaceIndex,
                    face.FaceIndex,
                    faceStation.Index,
                    intervalIndex,
                    station,
                    interval.Start,
                    interval.End,
                    interval.StartRole,
                    interval.EndRole,
                    interval.LengthMm);
                if (seen.Add(SegmentKey(segment)))
                {
                    output.Add(segment);
                }
            }
        }

        error = RoofFaceRafterLayoutError.None;
        return true;
    }

    private static bool TryCreateFaceStations(
        RoofTopology topology,
        FaceContext face,
        double spacingMm,
        SharedStationPhase? ridgePhase,
        out IReadOnlyList<FaceStation> stations,
        out bool tooManyStations)
    {
        stations = Array.Empty<FaceStation>();
        tooManyStations = false;
        if (!TryResolveLocalPhase(
                face,
                spacingMm,
                ridgePhase,
                out var phase))
        {
            tooManyStations = face.EaveLengthMm / spacingMm > MaximumStationCount;
            return false;
        }

        // Keep the accepted phase exactly, then extend that same parallel
        // station family over the complete owning-face projection. Concave-corner
        // faces can project beyond either end of their finite source Eave.
        var projections = face.Cycle
            .Select(index => Plan(topology.Nodes[index]))
            .Select(point =>
                (point.X - face.EaveStart.X) * face.EaveDirection.X +
                (point.Y - face.EaveStart.Y) * face.EaveDirection.Y)
            .ToArray();
        if (projections.Length == 0 || projections.Any(value => !IsFinite(value)))
        {
            return false;
        }

        var firstIndexValue = Math.Ceiling(
            (projections.Min() - phase) / spacingMm);
        var lastIndexValue = Math.Floor(
            (projections.Max() - phase) / spacingMm);
        var stationCount = lastIndexValue - firstIndexValue + 1d;
        if (!IsFinite(firstIndexValue) ||
            !IsFinite(lastIndexValue) ||
            stationCount <= 0d)
        {
            return false;
        }
        if (stationCount > MaximumStationCount ||
            firstIndexValue < int.MinValue ||
            lastIndexValue > int.MaxValue)
        {
            tooManyStations = true;
            return false;
        }

        var firstIndex = (int)firstIndexValue;
        var lastIndex = (int)lastIndexValue;
        stations = Array.AsReadOnly(Enumerable
            .Range(firstIndex, lastIndex - firstIndex + 1)
            .Select(index => new FaceStation(
                index,
                phase + index * spacingMm))
            .ToArray());
        return true;
    }

    private static bool TryResolveLocalPhase(
        FaceContext face,
        double spacingMm,
        SharedStationPhase? ridgePhase,
        out double phase)
    {
        phase = 0d;
        if (ridgePhase is null)
        {
            var eaveStations = CreateStationDistances(face.EaveLengthMm, spacingMm);
            if (eaveStations.Count == 0)
            {
                return false;
            }

            phase = eaveStations[0];
            return true;
        }

        var axisDot = Dot(face.EaveDirection, ridgePhase.StationAxis);
        if (Math.Abs(axisDot) <= AngularTolerance)
        {
            return false;
        }

        // Absolute station coordinate T = Dot(eaveStart, s) + local * Dot(eaveDir, s).
        phase = (ridgePhase.AbsolutePhaseT - Dot(face.EaveStart, ridgePhase.StationAxis)) /
            axisDot;
        return IsFinite(phase);
    }

    private static IReadOnlyList<FaceInterval> FindFaceIntervals(
        RoofTopology topology,
        IReadOnlyList<int> cycle,
        IReadOnlyDictionary<(int Start, int End), RoofTopologyEdge> edgeByNodes,
        RoofPoint2D stationOrigin,
        Vector2 direction)
    {
        var intersections = new List<BoundaryIntersection>();
        for (var index = 0; index < cycle.Count; index++)
        {
            var first = cycle[index];
            var second = cycle[(index + 1) % cycle.Count];
            if (!edgeByNodes.TryGetValue(Key(first, second), out var edge))
            {
                continue;
            }

            var a = Plan(topology.Nodes[first]);
            var b = Plan(topology.Nodes[second]);
            if (TryIntersectLineSegment(
                    stationOrigin,
                    direction,
                    a,
                    b,
                    out var distance))
            {
                intersections.Add(new BoundaryIntersection(distance, edge.Kind));
            }
        }

        var hits = MergeCoincidentIntersections(intersections);
        if (hits.Count < 2)
        {
            return Array.Empty<FaceInterval>();
        }

        var polygon = cycle
            .Select(index => Plan(topology.Nodes[index]))
            .ToArray();
        var intervals = new List<FaceInterval>();
        for (var index = 0; index + 1 < hits.Count; index++)
        {
            var startHit = hits[index];
            var endHit = hits[index + 1];
            var length = endHit.DistanceMm - startHit.DistanceMm;
            if (length <= CoordinateToleranceMm)
            {
                continue;
            }

            var midpointDistance =
                startHit.DistanceMm / 2d + endHit.DistanceMm / 2d;
            var midpoint = PointAt(stationOrigin, direction, midpointDistance);
            if (!IsStrictlyInside(midpoint, polygon) ||
                !TryResolveBoundaryRole(startHit.Kinds, out var startRole) ||
                !TryResolveBoundaryRole(endHit.Kinds, out var endRole))
            {
                // A CoplanarSeam remains an ownership boundary, not a physical
                // ordinary-rafter endpoint. Never bridge across or relabel it.
                continue;
            }

            var start = PointAt(stationOrigin, direction, startHit.DistanceMm);
            var end = PointAt(stationOrigin, direction, endHit.DistanceMm);
            if (!IsFinite(start.X) || !IsFinite(start.Y) ||
                !IsFinite(end.X) || !IsFinite(end.Y))
            {
                continue;
            }

            intervals.Add(new FaceInterval(
                start,
                end,
                startRole,
                endRole,
                length));
        }

        return intervals;
    }

    private static bool TryIntersectLineSegment(
        RoofPoint2D origin,
        Vector2 direction,
        RoofPoint2D a,
        RoofPoint2D b,
        out double distanceMm)
    {
        distanceMm = 0d;
        var edge = new Vector2(b.X - a.X, b.Y - a.Y);
        var offset = new Vector2(a.X - origin.X, a.Y - origin.Y);
        var denominator = Cross(direction, edge);
        if (Math.Abs(denominator) <= CoordinateToleranceMm)
        {
            return false;
        }

        var lineDistance = Cross(offset, edge) / denominator;
        var edgeFraction = Cross(offset, direction) / denominator;
        if (!IsFinite(lineDistance) ||
            edgeFraction < -CoordinateToleranceMm ||
            edgeFraction > 1d + CoordinateToleranceMm)
        {
            return false;
        }

        distanceMm = lineDistance;
        return true;
    }

    private static IReadOnlyList<MergedBoundaryIntersection> MergeCoincidentIntersections(
        IEnumerable<BoundaryIntersection> intersections)
    {
        var ordered = intersections
            .OrderBy(item => item.DistanceMm)
            .ThenBy(item => item.Kind)
            .ToArray();
        var merged = new List<MergedBoundaryIntersection>();
        foreach (var intersection in ordered)
        {
            if (merged.Count == 0 ||
                Math.Abs(
                    intersection.DistanceMm -
                    merged[merged.Count - 1].DistanceMm) >
                CoordinateToleranceMm)
            {
                merged.Add(new MergedBoundaryIntersection(
                    intersection.DistanceMm,
                    [intersection.Kind]));
                continue;
            }

            var previous = merged[merged.Count - 1];
            merged[merged.Count - 1] = previous with
            {
                Kinds = previous.Kinds
                    .Append(intersection.Kind)
                    .Distinct()
                    .OrderBy(kind => kind)
                    .ToArray(),
            };
        }
        return merged;
    }

    private static bool TryResolveBoundaryRole(
        IReadOnlyList<RoofTopologyEdgeKind> kinds,
        out RoofRafterBoundaryRole role)
    {
        role = default;
        if (kinds.Contains(RoofTopologyEdgeKind.CoplanarSeam))
        {
            return false;
        }

        var roles = kinds
            .Select(MapBoundaryRole)
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .OrderBy(item => item)
            .ToArray();
        if (roles.Length == 0)
        {
            return false;
        }

        // Stable vertex tie-break: Eave, Ridge, Hip, Valley. This preserves the
        // previous Ridge-before-Hip-before-Valley physical-boundary ordering.
        role = roles[0];
        return true;
    }

    private static RoofRafterBoundaryRole? MapBoundaryRole(
        RoofTopologyEdgeKind kind) => kind switch
    {
        RoofTopologyEdgeKind.Eave => RoofRafterBoundaryRole.Eave,
        RoofTopologyEdgeKind.Ridge => RoofRafterBoundaryRole.Ridge,
        RoofTopologyEdgeKind.Hip => RoofRafterBoundaryRole.Hip,
        RoofTopologyEdgeKind.Valley => RoofRafterBoundaryRole.Valley,
        _ => null,
    };

    private static bool IsStrictlyInside(
        RoofPoint2D point,
        IReadOnlyList<RoofPoint2D> polygon)
    {
        var inside = false;
        for (var index = 0; index < polygon.Count; index++)
        {
            var a = polygon[index];
            var b = polygon[(index + 1) % polygon.Count];
            if (OnSegment(point, a, b))
            {
                return false;
            }
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X <
                a.X + (point.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static bool OnSegment(
        RoofPoint2D point,
        RoofPoint2D a,
        RoofPoint2D b)
    {
        var edge = new Vector2(b.X - a.X, b.Y - a.Y);
        var offset = new Vector2(point.X - a.X, point.Y - a.Y);
        var length = Math.Sqrt(edge.X * edge.X + edge.Y * edge.Y);
        return length > CoordinateToleranceMm &&
               Math.Abs(Cross(edge, offset)) / length <= CoordinateToleranceMm &&
               point.X >= Math.Min(a.X, b.X) - CoordinateToleranceMm &&
               point.X <= Math.Max(a.X, b.X) + CoordinateToleranceMm &&
               point.Y >= Math.Min(a.Y, b.Y) - CoordinateToleranceMm &&
               point.Y <= Math.Max(a.Y, b.Y) + CoordinateToleranceMm;
    }

    private static RoofPoint2D PointAt(
        RoofPoint2D origin,
        Vector2 direction,
        double distance) =>
        new(
            origin.X + direction.X * distance,
            origin.Y + direction.Y * distance);

    private static string SegmentKey(RoofFaceRafterSegment segment)
    {
        // Face identity keeps opposing rafters that only share a Ridge endpoint.
        var first = PointKey(segment.PlanStart);
        var second = PointKey(segment.PlanEnd);
        var geometry = string.CompareOrdinal(first, second) <= 0
            ? string.Join(",", first, second)
            : string.Join(",", second, first);
        return string.Join(
            "|",
            segment.SourceFaceIndex.ToString(CultureInfo.InvariantCulture),
            geometry);
    }

    private static string PointKey(RoofPoint2D point) =>
        string.Join(":", Format(point.X), Format(point.Y));

    private static string BuildSignature(
        RoofTopology topology,
        double spacingMm,
        IReadOnlyList<RoofFaceRafterSegment> segments) => string.Join(
            ";",
            new[]
            {
                "ROOF_FACE_RAFTER_LAYOUT_V2",
                topology.Signature,
                spacingMm.ToString("R", CultureInfo.InvariantCulture),
            }.Concat(segments.Select(segment => string.Join(",",
                segment.SourceFaceIndex.ToString(CultureInfo.InvariantCulture),
                segment.SourceEaveEdgeIndex.ToString(CultureInfo.InvariantCulture),
                segment.StationIndex.ToString(CultureInfo.InvariantCulture),
                segment.StationIntervalIndex.ToString(CultureInfo.InvariantCulture),
                segment.StationDistanceMm.ToString("R", CultureInfo.InvariantCulture),
                PointKey(segment.PlanStart),
                PointKey(segment.PlanEnd),
                segment.StartBoundaryRole.ToString(),
                segment.EndBoundaryRole.ToString(),
                segment.PlanLengthMm.ToString("R", CultureInfo.InvariantCulture)))));

    private static bool AreUnitDirectionsCompatible(Vector2 first, Vector2 second)
    {
        var firstLength = Length(first);
        var secondLength = Length(second);
        if (firstLength <= CoordinateToleranceMm ||
            secondLength <= CoordinateToleranceMm)
        {
            return false;
        }

        var a = new Vector2(first.X / firstLength, first.Y / firstLength);
        var b = new Vector2(second.X / secondLength, second.Y / secondLength);
        // Parallel or antiparallel within Core angular tolerance.
        return Math.Abs(Cross(a, b)) <= AngularTolerance &&
               Math.Abs(Dot(a, b)) >= 1d - 1e-9;
    }

    private static Vector2 CanonicalAxis(Vector2 axis)
    {
        var length = Length(axis);
        if (length <= CoordinateToleranceMm)
        {
            return axis;
        }

        var unit = new Vector2(axis.X / length, axis.Y / length);
        if (unit.X < -AngularTolerance ||
            (Math.Abs(unit.X) <= AngularTolerance && unit.Y < -AngularTolerance))
        {
            return new Vector2(-unit.X, -unit.Y);
        }

        return unit;
    }

    private static string Format(double value)
    {
        var rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
        return (rounded == 0d ? 0d : rounded).ToString("R", CultureInfo.InvariantCulture);
    }

    private static RoofPoint2D Plan(RoofPoint3D point) => new(point.X, point.Y);
    private static (int Start, int End) Key(int first, int second) =>
        (Math.Min(first, second), Math.Max(first, second));
    private static double Cross(Vector2 first, Vector2 second) =>
        first.X * second.Y - first.Y * second.X;
    private static double Dot(Vector2 first, Vector2 second) =>
        first.X * second.X + first.Y * second.Y;
    private static double Dot(RoofPoint2D point, Vector2 axis) =>
        point.X * axis.X + point.Y * axis.Y;
    private static double Length(Vector2 value) =>
        Math.Sqrt(value.X * value.X + value.Y * value.Y);
    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
    private static RoofFaceRafterLayoutResult Invalid(RoofFaceRafterLayoutError error) =>
        new(false, null, error);

    private readonly record struct Vector2(double X, double Y);
    private readonly record struct BoundaryIntersection(
        double DistanceMm,
        RoofTopologyEdgeKind Kind);
    private sealed record MergedBoundaryIntersection(
        double DistanceMm,
        IReadOnlyList<RoofTopologyEdgeKind> Kinds);
    private sealed record FaceInterval(
        RoofPoint2D Start,
        RoofPoint2D End,
        RoofRafterBoundaryRole StartRole,
        RoofRafterBoundaryRole EndRole,
        double LengthMm);
    private readonly record struct FaceStation(
        int Index,
        double DistanceMm);
    private sealed record FaceContext(
        int FaceIndex,
        IReadOnlyList<int> Cycle,
        RoofPoint2D EaveStart,
        Vector2 EaveDirection,
        Vector2 RafterDirection,
        double EaveLengthMm);
    private sealed record SharedStationPhase(
        Vector2 StationAxis,
        double AbsolutePhaseT,
        double SpacingMm);
    private sealed record CompatibleRidgeEdge(
        int EdgeIndex,
        int StartNodeIndex,
        int EndNodeIndex,
        RoofPoint2D Start,
        RoofPoint2D End,
        Vector2 RidgeDirection,
        Vector2 StationAxis,
        IReadOnlyList<int> FaceIndices);
}
