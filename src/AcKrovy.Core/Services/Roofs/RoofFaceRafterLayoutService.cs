using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Generates ordinary roof-face rafter centerlines from an indexed roof topology.
/// Compatible opposing faces on a phase-coupled Ridge family share one
/// family-centered station lattice along a canonical station axis; each face still
/// slices its complete boundary with that family.
/// Phase coupling is topology-driven: shared Ridge endpoints, or multiple compatible
/// Ridge boundaries of one ordinary face (collinear or perpendicularly offset).
/// Unrelated parallel Ridges that do not share such a relationship stay independent.
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

    /// <summary>
    /// Verifies that ordinary Ridge endpoints from opposite faces of each compatible
    /// shared Ridge edge occupy the same canonical station lattice. Extra one-sided
    /// hits near Hip/Valley/Ridge junctions are allowed; mismatched lattices are not.
    /// </summary>
    public static RoofRidgePairingReport EvaluateSharedRidgePairing(
        RoofTopology topology,
        RoofFaceRafterLayout layout)
    {
        if (topology is null)
        {
            throw new ArgumentNullException(nameof(topology));
        }
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        var edgeByNodes = topology.Edges.ToDictionary(
            edge => Key(edge.StartNodeIndex, edge.EndNodeIndex));
        if (!TryBuildFaceContexts(
                topology,
                edgeByNodes,
                out var facesByIndex,
                out _))
        {
            return RoofRidgePairingReport.Empty;
        }

        var candidates = CollectCompatibleRidgeEdges(topology, facesByIndex);
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

        var componentRoots = candidates
            .Select((_, index) => Find(index))
            .Distinct()
            .OrderBy(root => root)
            .ToArray();

        var leftCount = 0;
        var rightCount = 0;
        var matched = 0;
        var unmatchedLeft = 0;
        var unmatchedRight = 0;
        var maxGap = 0d;
        var pairedComponents = 0;
        var failures = new List<RoofRidgePairingFailure>();

        foreach (var root in componentRoots)
        {
            var component = candidates
                .Select((edge, index) => (Edge: edge, Index: index))
                .Where(item => Find(item.Index) == root)
                .Select(item => item.Edge)
                .OrderBy(edge => edge.EdgeIndex)
                .ToArray();
            if (component.Length == 0)
            {
                continue;
            }

            // Pair each edge's two incident faces independently; component-level
            // lattice identity is what Create already enforced.
            var componentHadBothSides = false;
            foreach (var edge in component)
            {
                if (edge.FaceIndices.Count != 2)
                {
                    continue;
                }

                var firstFace = edge.FaceIndices[0];
                var secondFace = edge.FaceIndices[1];
                var firstHits = RidgeHitsOnEdge(layout, firstFace, edge);
                var secondHits = RidgeHitsOnEdge(layout, secondFace, edge);
                if (firstHits.Count == 0 || secondHits.Count == 0)
                {
                    continue;
                }

                componentHadBothSides = true;
                leftCount += firstHits.Count;
                rightCount += secondHits.Count;

                var shorter = firstHits.Count <= secondHits.Count ? firstHits : secondHits;
                var longer = firstHits.Count <= secondHits.Count ? secondHits : firstHits;
                var shorterIsFirst = firstHits.Count <= secondHits.Count;
                var used = new bool[longer.Count];
                foreach (var hit in shorter)
                {
                    var bestIndex = -1;
                    var bestGap = double.PositiveInfinity;
                    for (var i = 0; i < longer.Count; i++)
                    {
                        if (used[i])
                        {
                            continue;
                        }

                        var gap = hit.Point.DistanceTo(longer[i].Point);
                        if (gap < bestGap)
                        {
                            bestGap = gap;
                            bestIndex = i;
                        }
                    }

                    if (bestIndex >= 0 && bestGap <= CoordinateToleranceMm)
                    {
                        used[bestIndex] = true;
                        matched++;
                        continue;
                    }

                    unmatchedLeft++;
                    if (bestIndex >= 0)
                    {
                        maxGap = Math.Max(maxGap, bestGap);
                        if (failures.Count < 8)
                        {
                            var other = longer[bestIndex];
                            var faceAIndex = shorterIsFirst ? firstFace : secondFace;
                            var faceBIndex = shorterIsFirst ? secondFace : firstFace;
                            failures.Add(new RoofRidgePairingFailure(
                                edge.EdgeIndex,
                                edge.Start,
                                edge.End,
                                faceAIndex,
                                faceBIndex,
                                hit.StationMm,
                                other.StationMm,
                                hit.Point,
                                other.Point,
                                bestGap,
                                hit.StartRole,
                                hit.EndRole,
                                other.StartRole,
                                other.EndRole));
                        }
                    }
                }

                for (var i = 0; i < used.Length; i++)
                {
                    if (!used[i])
                    {
                        // One-sided extras at Hip/Valley/Ridge junction extents are
                        // informational only; they do not fail the pairing invariant.
                        unmatchedRight++;
                    }
                }
            }

            if (componentHadBothSides)
            {
                pairedComponents++;
            }
        }

        return new RoofRidgePairingReport(
            componentRoots.Length,
            pairedComponents,
            leftCount,
            rightCount,
            matched,
            unmatchedLeft,
            unmatchedRight,
            maxGap,
            unmatchedLeft == 0,
            failures);
    }

    private static IReadOnlyList<RidgeHitSample> RidgeHitsOnEdge(
        RoofFaceRafterLayout layout,
        int faceIndex,
        CompatibleRidgeEdge edge)
    {
        var axis = CanonicalAxis(edge.RidgeDirection);
        var samples = new List<RidgeHitSample>();
        foreach (var segment in layout.Segments)
        {
            if (segment.SourceFaceIndex != faceIndex)
            {
                continue;
            }

            RoofPoint2D point;
            if (segment.EndBoundaryRole == RoofRafterBoundaryRole.Ridge)
            {
                point = segment.PlanEnd;
            }
            else if (segment.StartBoundaryRole == RoofRafterBoundaryRole.Ridge)
            {
                point = segment.PlanStart;
            }
            else
            {
                continue;
            }

            if (!IsPointOnSegment(point, edge.Start, edge.End))
            {
                continue;
            }

            samples.Add(new RidgeHitSample(
                point,
                Dot(point, axis),
                segment.StartBoundaryRole,
                segment.EndBoundaryRole));
        }

        return samples
            .OrderBy(sample => sample.StationMm)
            .ThenBy(sample => sample.Point.X)
            .ThenBy(sample => sample.Point.Y)
            .ToArray();
    }

    private static bool IsPointOnSegment(
        RoofPoint2D point,
        RoofPoint2D start,
        RoofPoint2D end)
    {
        var abx = end.X - start.X;
        var aby = end.Y - start.Y;
        var lengthSquared = abx * abx + aby * aby;
        if (lengthSquared <= CoordinateToleranceMm * CoordinateToleranceMm)
        {
            return false;
        }

        var t = ((point.X - start.X) * abx + (point.Y - start.Y) * aby) / lengthSquared;
        if (t < -1e-9 || t > 1d + 1e-9)
        {
            return false;
        }

        var projected = new RoofPoint2D(start.X + t * abx, start.Y + t * aby);
        var dx = point.X - projected.X;
        var dy = point.Y - projected.Y;
        return Math.Sqrt(dx * dx + dy * dy) <= CoordinateToleranceMm;
    }

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

    /// <summary>
    /// Describes the topology-driven Ridge phase-constraint resolution used by
    /// <see cref="Create"/> (for DEBUG diagnostics and tests).
    /// </summary>
    public static RoofRafterPhasePlan DescribePhasePlan(
        RoofTopology topology,
        double spacingMm)
    {
        if (topology is null)
        {
            throw new ArgumentNullException(nameof(topology));
        }

        var edgeByNodes = topology.Edges.ToDictionary(
            edge => Key(edge.StartNodeIndex, edge.EndNodeIndex));
        if (!TryBuildFaceContexts(
                topology,
                edgeByNodes,
                out var facesByIndex,
                out _) ||
            !RoofRafterSpacingRules.IsValidDimension(spacingMm))
        {
            return RoofRafterPhasePlan.Empty;
        }

        ResolveRidgeCoordinatedPhases(
            topology,
            facesByIndex,
            spacingMm,
            out var plan);
        return plan;
    }

    /// <summary>
    /// Evaluates complete-face station coverage for every ordinary face.
    /// Enumeration extent must equal the full face projection on the station axis;
    /// Ridge-family extent is reported only for diagnostics and must not truncate.
    /// </summary>
    public static IReadOnlyList<RoofFaceRafterFaceCoverageReport> EvaluateFaceCoverage(
        RoofTopology topology,
        RoofFaceRafterLayout layout)
    {
        if (topology is null)
        {
            throw new ArgumentNullException(nameof(topology));
        }
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        var edgeByNodes = topology.Edges.ToDictionary(
            edge => Key(edge.StartNodeIndex, edge.EndNodeIndex));
        if (!TryBuildFaceContexts(
                topology,
                edgeByNodes,
                out var facesByIndex,
                out _) ||
            !RoofRafterSpacingRules.IsValidDimension(layout.RequestedSpacingMm))
        {
            return Array.Empty<RoofFaceRafterFaceCoverageReport>();
        }

        var ridgePhases = ResolveRidgeCoordinatedPhases(
            topology,
            facesByIndex,
            layout.RequestedSpacingMm,
            out var phasePlan);
        var ridgeExtentByFace = new Dictionary<int, (double MinT, double MaxT)>();
        foreach (var component in phasePlan.Components)
        {
            if (component.RidgeEdgeIds.Count == 0)
            {
                continue;
            }

            var projections = new List<double>();
            foreach (var edgeId in component.RidgeEdgeIds)
            {
                if (edgeId < 0 || edgeId >= topology.Edges.Count)
                {
                    continue;
                }

                var edge = topology.Edges[edgeId];
                var axis = new Vector2(component.StationAxisX, component.StationAxisY);
                projections.Add(Dot(Plan(topology.Nodes[edge.StartNodeIndex]), axis));
                projections.Add(Dot(Plan(topology.Nodes[edge.EndNodeIndex]), axis));
            }

            if (projections.Count == 0)
            {
                continue;
            }

            var minT = projections.Min();
            var maxT = projections.Max();
            foreach (var faceId in component.FacesConsumingPhase)
            {
                ridgeExtentByFace[faceId] = (minT, maxT);
            }
        }

        var reports = new List<RoofFaceRafterFaceCoverageReport>();
        foreach (var face in facesByIndex.Values.OrderBy(item => item.FaceIndex))
        {
            ridgePhases.TryGetValue(face.FaceIndex, out var ridgePhase);
            if (!TryResolveLocalPhase(
                    face,
                    layout.RequestedSpacingMm,
                    ridgePhase,
                    out var phase))
            {
                reports.Add(EmptyCoverageFail(face, phase: 0d));
                continue;
            }

            var localProjections = face.Cycle
                .Select(index => Plan(topology.Nodes[index]))
                .Select(point =>
                    (point.X - face.EaveStart.X) * face.EaveDirection.X +
                    (point.Y - face.EaveStart.Y) * face.EaveDirection.Y)
                .ToArray();
            if (localProjections.Length == 0 || localProjections.Any(value => !IsFinite(value)))
            {
                reports.Add(EmptyCoverageFail(face, phase));
                continue;
            }

            var fullMin = localProjections.Min();
            var fullMax = localProjections.Max();
            if (!TryCreateFaceStations(
                    topology,
                    face,
                    layout.RequestedSpacingMm,
                    ridgePhase,
                    out var stations,
                    out _))
            {
                reports.Add(EmptyCoverageFail(face, phase, fullMin, fullMax));
                continue;
            }

            var faceSegments = layout.Segments
                .Where(segment => segment.SourceFaceIndex == face.FaceIndex)
                .OrderBy(segment => segment.StationDistanceMm)
                .ThenBy(segment => segment.StationIntervalIndex)
                .ToArray();
            var generatedTs = faceSegments
                .Select(segment => segment.StationDistanceMm)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();

            double? ridgeMin = null;
            double? ridgeMax = null;
            if (ridgeExtentByFace.TryGetValue(face.FaceIndex, out var ridgeExtent))
            {
                // Report Ridge-family extent in the same local eave scalar used by stations.
                var axisDot = ridgePhase is null
                    ? 1d
                    : Dot(face.EaveDirection, ridgePhase.StationAxis);
                if (Math.Abs(axisDot) > AngularTolerance && ridgePhase is not null)
                {
                    ridgeMin = (ridgeExtent.MinT - Dot(face.EaveStart, ridgePhase.StationAxis)) / axisDot;
                    ridgeMax = (ridgeExtent.MaxT - Dot(face.EaveStart, ridgePhase.StationAxis)) / axisDot;
                    if (ridgeMin > ridgeMax)
                    {
                        (ridgeMin, ridgeMax) = (ridgeMax, ridgeMin);
                    }
                }
            }

            var enumerationMin = stations.Count == 0 ? fullMin : stations[0].DistanceMm;
            var enumerationMax = stations.Count == 0
                ? fullMax
                : stations[stations.Count - 1].DistanceMm;
            var firstGenerated = generatedTs.Length == 0 ? (double?)null : generatedTs[0];
            var lastGenerated = generatedTs.Length == 0
                ? (double?)null
                : generatedTs[generatedTs.Length - 1];
            var uncoveredStart = firstGenerated is null
                ? Math.Max(0d, fullMax - fullMin)
                : Math.Max(0d, firstGenerated.Value - fullMin);
            var uncoveredEnd = lastGenerated is null
                ? Math.Max(0d, fullMax - fullMin)
                : Math.Max(0d, fullMax - lastGenerated.Value);

            var missingLattice = false;
            foreach (var station in stations)
            {
                var fraction = station.DistanceMm / face.EaveLengthMm;
                var stationOrigin = new RoofPoint2D(
                    face.EaveStart.X + face.EaveDirection.X * face.EaveLengthMm * fraction,
                    face.EaveStart.Y + face.EaveDirection.Y * face.EaveLengthMm * fraction);
                var intervals = FindFaceIntervals(
                    topology,
                    face.Cycle,
                    edgeByNodes,
                    stationOrigin,
                    face.RafterDirection);
                if (intervals.Count == 0)
                {
                    continue;
                }

                var emitted = faceSegments.Count(segment =>
                    Math.Abs(segment.StationDistanceMm - station.DistanceMm) <=
                    CoordinateToleranceMm);
                if (emitted == 0)
                {
                    missingLattice = true;
                    break;
                }
            }

            var result = RoofFaceRafterFaceCoverageResult.Pass;
            if (faceSegments.Length == 0 && stations.Count > 0)
            {
                result = RoofFaceRafterFaceCoverageResult.FailEmptyFace;
            }
            else if (missingLattice)
            {
                result = RoofFaceRafterFaceCoverageResult.FailMissingLatticeStation;
            }
            else if (uncoveredStart > layout.RequestedSpacingMm + CoordinateToleranceMm)
            {
                result = RoofFaceRafterFaceCoverageResult.FailUncoveredStart;
            }
            else if (uncoveredEnd > layout.RequestedSpacingMm + CoordinateToleranceMm)
            {
                result = RoofFaceRafterFaceCoverageResult.FailUncoveredEnd;
            }

            var firstRoles = faceSegments.Length == 0
                ? "-"
                : RolePair(faceSegments[0]);
            var lastRoles = faceSegments.Length == 0
                ? "-"
                : RolePair(faceSegments[faceSegments.Length - 1]);
            var lengths = faceSegments.Select(segment => segment.PlanLengthMm).ToArray();

            reports.Add(new RoofFaceRafterFaceCoverageReport(
                face.FaceIndex,
                face.EaveDirection.X,
                face.EaveDirection.Y,
                fullMin,
                fullMax,
                ridgeMin,
                ridgeMax,
                enumerationMin,
                enumerationMax,
                phase,
                firstGenerated,
                lastGenerated,
                stations.Count,
                faceSegments.Length,
                lengths.Length == 0 ? double.NaN : lengths.Min(),
                lengths.Length == 0 ? double.NaN : lengths.Max(),
                firstRoles,
                lastRoles,
                uncoveredStart,
                uncoveredEnd,
                result));
        }

        return Array.AsReadOnly(reports.ToArray());
    }

    /// <summary>
    /// Assert-style helper: every ordinary face must PASS complete-face coverage.
    /// </summary>
    public static bool HasCompleteFaceCoverage(
        RoofTopology topology,
        RoofFaceRafterLayout layout) =>
        EvaluateFaceCoverage(topology, layout)
            .All(report => report.Result == RoofFaceRafterFaceCoverageResult.Pass);

    private static RoofFaceRafterFaceCoverageReport EmptyCoverageFail(
        FaceContext face,
        double phase,
        double fullMin = double.NaN,
        double fullMax = double.NaN) =>
        new(
            face.FaceIndex,
            face.EaveDirection.X,
            face.EaveDirection.Y,
            fullMin,
            fullMax,
            null,
            null,
            fullMin,
            fullMax,
            phase,
            null,
            null,
            0,
            0,
            double.NaN,
            double.NaN,
            "-",
            "-",
            double.IsNaN(fullMin) || double.IsNaN(fullMax) ? double.NaN : Math.Max(0d, fullMax - fullMin),
            double.IsNaN(fullMin) || double.IsNaN(fullMax) ? double.NaN : Math.Max(0d, fullMax - fullMin),
            RoofFaceRafterFaceCoverageResult.FailEmptyFace);

    private static string RolePair(RoofFaceRafterSegment segment) =>
        $"{segment.StartBoundaryRole}->{segment.EndBoundaryRole}";

    private static IReadOnlyDictionary<int, SharedStationPhase> ResolveRidgeCoordinatedPhases(
        RoofTopology topology,
        IReadOnlyDictionary<int, FaceContext> facesByIndex,
        double spacingMm) =>
        ResolveRidgeCoordinatedPhases(
            topology,
            facesByIndex,
            spacingMm,
            out _);

    private static IReadOnlyDictionary<int, SharedStationPhase> ResolveRidgeCoordinatedPhases(
        RoofTopology topology,
        IReadOnlyDictionary<int, FaceContext> facesByIndex,
        double spacingMm,
        out RoofRafterPhasePlan plan)
    {
        plan = RoofRafterPhasePlan.Empty;
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

        var couplingReasons = new Dictionary<(int, int), string>();
        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                if (!AreConnectedRidgeComponentEdges(candidates[i], candidates[j]))
                {
                    continue;
                }

                Union(i, j);
                couplingReasons[(candidates[i].EdgeIndex, candidates[j].EdgeIndex)] =
                    DescribeCouplingReason(candidates[i], candidates[j]);
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
        var componentRecords = new List<RoofRafterPhaseComponent>();
        var faceCandidates = new Dictionary<int, List<(int ComponentId, SharedStationPhase Phase)>>();

        for (var componentId = 0; componentId < components.Length; componentId++)
        {
            var component = components[componentId];
            if (!TryCreateComponentPhase(component, spacingMm, out var phase))
            {
                continue;
            }

            var edgeIds = component.Select(edge => edge.EdgeIndex).ToArray();
            var faces = component
                .SelectMany(edge => edge.FaceIndices)
                .Distinct()
                .OrderBy(index => index)
                .ToArray();
            var reasons = couplingReasons
                .Where(pair =>
                    edgeIds.Contains(pair.Key.Item1) &&
                    edgeIds.Contains(pair.Key.Item2))
                .Select(pair => pair.Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var allCollinear = component.Length <= 1 ||
                component
                    .Skip(1)
                    .All(edge => AreCollinearEquivalentSegments(component[0], edge));
            var offsetMm = 0d;
            if (!allCollinear && component.Length > 1)
            {
                offsetMm = component
                    .Skip(1)
                    .Select(edge => PointLineDistanceMm(
                        Midpoint(edge.Start, edge.End),
                        component[0].Start,
                        CanonicalAxis(component[0].RidgeDirection)))
                    .DefaultIfEmpty(0d)
                    .Max();
            }

            componentRecords.Add(new RoofRafterPhaseComponent(
                componentId,
                edgeIds,
                phase.StationAxis.X,
                phase.StationAxis.Y,
                phase.AbsolutePhaseT,
                faces,
                reasons.Length == 0 ? ["singleton"] : reasons,
                allCollinear,
                offsetMm,
                faces.Where(faceIndex =>
                        facesByIndex.TryGetValue(faceIndex, out var face) &&
                        AreUnitDirectionsCompatible(face.EaveDirection, phase.StationAxis))
                    .ToArray()));

            foreach (var faceIndex in faces)
            {
                if (!facesByIndex.TryGetValue(faceIndex, out var face) ||
                    !AreUnitDirectionsCompatible(face.EaveDirection, phase.StationAxis))
                {
                    continue;
                }

                if (!faceCandidates.TryGetValue(faceIndex, out var list))
                {
                    list = [];
                    faceCandidates[faceIndex] = list;
                }

                list.Add((componentId, phase));

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

                conflictingFaces.Add(faceIndex);
                assigned.Remove(faceIndex);
            }
        }

        foreach (var faceIndex in conflictingFaces)
        {
            assigned.Remove(faceIndex);
        }

        // Refresh consuming faces after conflict removals.
        componentRecords = componentRecords
            .Select(component => component with
            {
                FacesConsumingPhase = component.FacesConsumingPhase
                    .Where(faceId => assigned.ContainsKey(faceId))
                    .ToArray(),
            })
            .ToList();

        var faceDecisions = facesByIndex.Values
            .OrderBy(face => face.FaceIndex)
            .Select(face =>
            {
                faceCandidates.TryGetValue(face.FaceIndex, out var candidatesForFace);
                candidatesForFace ??= [];
                var ridgeBoundaries = candidates
                    .Where(edge => edge.FaceIndices.Contains(face.FaceIndex))
                    .Select(edge => edge.EdgeIndex)
                    .OrderBy(id => id)
                    .ToArray();
                var selected = assigned.TryGetValue(face.FaceIndex, out var phase)
                    ? phase.AbsolutePhaseT
                    : (double?)null;
                var fallbackUsed = ridgeBoundaries.Length > 0 && selected is null;
                var reason = conflictingFaces.Contains(face.FaceIndex)
                    ? "conflicting-component-phases"
                    : selected is null
                        ? ridgeBoundaries.Length == 0
                            ? "no-compatible-ridge"
                            : "eave-local-fallback"
                        : "ridge-family";
                return new RoofRafterFacePhaseDecision(
                    face.FaceIndex,
                    face.EaveDirection.X,
                    face.EaveDirection.Y,
                    ridgeBoundaries,
                    candidatesForFace.Select(item => item.Phase.AbsolutePhaseT).ToArray(),
                    selected,
                    fallbackUsed,
                    reason);
            })
            .ToArray();

        plan = new RoofRafterPhasePlan(componentRecords, faceDecisions);
        return assigned;
    }

    private static RoofPoint2D Midpoint(RoofPoint2D first, RoofPoint2D second) =>
        new((first.X + second.X) / 2d, (first.Y + second.Y) / 2d);

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
        if (!AreUnitDirectionsCompatible(first.RidgeDirection, second.RidgeDirection) ||
            !AreUnitDirectionsCompatible(first.StationAxis, second.StationAxis))
        {
            return false;
        }

        var shareEndpoint =
            first.StartNodeIndex == second.StartNodeIndex ||
            first.StartNodeIndex == second.EndNodeIndex ||
            first.EndNodeIndex == second.StartNodeIndex ||
            first.EndNodeIndex == second.EndNodeIndex;
        if (shareEndpoint)
        {
            return true;
        }

        // Same-face multi-Ridge constraint: one ordinary face owns one eave-parallel
        // station lattice. Compatible Ridge boundaries of that face must share phase
        // even when they are perpendicularly offset (stepped Ridge), not only when
        // they remain collinear after a Valley/T split.
        return ShareAnyFace(first, second);
    }

    private static string DescribeCouplingReason(
        CompatibleRidgeEdge first,
        CompatibleRidgeEdge second)
    {
        var shareEndpoint =
            first.StartNodeIndex == second.StartNodeIndex ||
            first.StartNodeIndex == second.EndNodeIndex ||
            first.EndNodeIndex == second.StartNodeIndex ||
            first.EndNodeIndex == second.EndNodeIndex;
        if (shareEndpoint)
        {
            return "shared-endpoint";
        }

        if (!ShareAnyFace(first, second))
        {
            return "none";
        }

        return AreCollinearEquivalentSegments(first, second)
            ? "shared-face-collinear"
            : "shared-face-offset-parallel";
    }

    private static bool ShareAnyFace(
        CompatibleRidgeEdge first,
        CompatibleRidgeEdge second)
    {
        for (var i = 0; i < first.FaceIndices.Count; i++)
        {
            for (var j = 0; j < second.FaceIndices.Count; j++)
            {
                if (first.FaceIndices[i] == second.FaceIndices[j])
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool AreCollinearEquivalentSegments(
        CompatibleRidgeEdge first,
        CompatibleRidgeEdge second)
    {
        var axis = CanonicalAxis(first.RidgeDirection);
        return PointLineDistanceMm(second.Start, first.Start, axis) <=
                   CoordinateToleranceMm &&
               PointLineDistanceMm(second.End, first.Start, axis) <=
                   CoordinateToleranceMm;
    }

    private static double PointLineDistanceMm(
        RoofPoint2D point,
        RoofPoint2D origin,
        Vector2 unitAxis)
    {
        var dx = point.X - origin.X;
        var dy = point.Y - origin.Y;
        return Math.Abs(dx * unitAxis.Y - dy * unitAxis.X);
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
        var spacing = first.SpacingMm;
        if (spacing <= CoordinateToleranceMm)
        {
            return Math.Abs(firstT - secondT) <= CoordinateToleranceMm;
        }

        // Same lattice: AbsolutePhaseT may differ by an integer multiple of spacing.
        var delta = Math.Abs(firstT - secondT);
        var rem = delta % spacing;
        var latticeGap = Math.Min(rem, spacing - rem);
        return latticeGap <= CoordinateToleranceMm;
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
    private readonly record struct RidgeHitSample(
        RoofPoint2D Point,
        double StationMm,
        RoofRafterBoundaryRole StartRole,
        RoofRafterBoundaryRole EndRole);
}
