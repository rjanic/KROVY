using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Finds one unbranched four-edge rectangular cycle around a selected CAD-neutral line.
/// Endpoint tolerance is used only for comparison; returned corners are existing endpoints.
/// </summary>
public static class TimberLineRectangleDiscoveryService
{
    public const double EndpointConnectivityToleranceMm = 0.01d;

    public static TimberLineRectangleDiscoveryResult Discover(
        string selectedEdgeKey,
        IReadOnlyList<TimberLineRectangleEdge>? candidateEdges)
    {
        if (string.IsNullOrWhiteSpace(selectedEdgeKey) || candidateEdges is null)
        {
            return TimberLineRectangleDiscoveryResult.Failure(
                TimberLineRectangleDiscoveryStatus.NotFound);
        }

        var edges = candidateEdges
            .Where(IsUsable)
            .GroupBy(edge => edge.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var selected = edges.SingleOrDefault(edge =>
            string.Equals(edge.Key, selectedEdgeKey, StringComparison.Ordinal));
        if (selected is null)
        {
            return TimberLineRectangleDiscoveryResult.Failure(
                TimberLineRectangleDiscoveryStatus.NotFound);
        }

        var connectedEdges = FindConnectedComponent(selected, edges);
        if (HasDuplicateGeometry(connectedEdges))
        {
            return TimberLineRectangleDiscoveryResult.Failure(
                TimberLineRectangleDiscoveryStatus.DuplicateEdge);
        }

        var closedCycles = new List<OrientedCycle>();
        Explore(connectedEdges, selected, selected.Start, selected.End, [selected], [selected.Start], closedCycles);
        Explore(connectedEdges, selected, selected.End, selected.Start, [selected], [selected.End], closedCycles);

        var distinctCycles = closedCycles
            .GroupBy(cycle => string.Join("\u001f", cycle.Edges
                .Select(edge => edge.Key)
                .OrderBy(key => key, StringComparer.Ordinal)), StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        var valid = distinctCycles
            .Select(cycle => (Cycle: cycle, Validation: TimberRectangularFootprintValidator.Validate(cycle.Vertices)))
            .Where(item => item.Validation.IsValid && item.Validation.Geometry is not null)
            .ToArray();

        if (valid.Length > 1)
        {
            return TimberLineRectangleDiscoveryResult.Failure(
                TimberLineRectangleDiscoveryStatus.Ambiguous);
        }

        if (valid.Length == 0)
        {
            return TimberLineRectangleDiscoveryResult.Failure(
                distinctCycles.Length == 0
                    ? TimberLineRectangleDiscoveryStatus.NotFound
                    : TimberLineRectangleDiscoveryStatus.InvalidRectangle);
        }

        var resolved = valid[0];
        if (HasBranchAtCycle(edges, resolved.Cycle))
        {
            return TimberLineRectangleDiscoveryResult.Failure(
                TimberLineRectangleDiscoveryStatus.Branching);
        }

        return new TimberLineRectangleDiscoveryResult(
            TimberLineRectangleDiscoveryStatus.Success,
            resolved.Validation.Geometry,
            resolved.Cycle.Edges.Select(edge => edge.Key).ToArray());
    }

    private static void Explore(
        IReadOnlyList<TimberLineRectangleEdge> allEdges,
        TimberLineRectangleEdge selected,
        TimberRectangularFootprintPoint cycleStart,
        TimberRectangularFootprintPoint current,
        IReadOnlyList<TimberLineRectangleEdge> path,
        IReadOnlyList<TimberRectangularFootprintPoint> vertices,
        ICollection<OrientedCycle> results)
    {
        if (path.Count == TimberRectangularFootprintGeometry.RequiredVertexCount)
        {
            if (AreConnected(current, cycleStart))
            {
                results.Add(new OrientedCycle(path.ToArray(), vertices.ToArray()));
            }

            return;
        }

        foreach (var edge in allEdges)
        {
            if (ReferenceEquals(edge, selected) || path.Any(item =>
                    string.Equals(item.Key, edge.Key, StringComparison.Ordinal)))
            {
                continue;
            }

            if (AreConnected(current, edge.Start))
            {
                Explore(
                    allEdges,
                    selected,
                    cycleStart,
                    edge.End,
                    [.. path, edge],
                    [.. vertices, current],
                    results);
            }

            if (AreConnected(current, edge.End))
            {
                Explore(
                    allEdges,
                    selected,
                    cycleStart,
                    edge.Start,
                    [.. path, edge],
                    [.. vertices, current],
                    results);
            }
        }
    }

    private static bool HasBranchAtCycle(
        IReadOnlyList<TimberLineRectangleEdge> allEdges,
        OrientedCycle cycle)
    {
        var cycleKeys = new HashSet<string>(
            cycle.Edges.Select(edge => edge.Key),
            StringComparer.Ordinal);
        foreach (var corner in cycle.Vertices)
        {
            var incidentCount = allEdges.Count(edge =>
                AreConnected(corner, edge.Start) || AreConnected(corner, edge.End));
            if (incidentCount != 2)
            {
                return true;
            }
        }

        var cycleGeometry = new TimberRectangularFootprintGeometry(cycle.Vertices);
        foreach (var edge in allEdges.Where(edge => !cycleKeys.Contains(edge.Key)))
        {
            if (cycleGeometry.Segments.Any(segment =>
                    IsPointOnSegmentInterior(edge.Start, segment) ||
                    IsPointOnSegmentInterior(edge.End, segment)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointOnSegmentInterior(
        TimberRectangularFootprintPoint point,
        TimberRectangularFootprintSegment segment)
    {
        var dx = segment.End.X - segment.Start.X;
        var dy = segment.End.Y - segment.Start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0d)
        {
            return false;
        }

        var projection = ((point.X - segment.Start.X) * dx +
            (point.Y - segment.Start.Y) * dy) / lengthSquared;
        if (projection <= 0d || projection >= 1d)
        {
            return false;
        }

        var projected = new TimberRectangularFootprintPoint(
            segment.Start.X + projection * dx,
            segment.Start.Y + projection * dy);
        return Distance(point, projected) <= EndpointConnectivityToleranceMm;
    }

    private static IReadOnlyList<TimberLineRectangleEdge> FindConnectedComponent(
        TimberLineRectangleEdge selected,
        IReadOnlyList<TimberLineRectangleEdge> allEdges)
    {
        var component = new List<TimberLineRectangleEdge> { selected };
        var keys = new HashSet<string>(StringComparer.Ordinal) { selected.Key };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var candidate in allEdges)
            {
                if (keys.Contains(candidate.Key) || !component.Any(edge => AreIncident(edge, candidate)))
                {
                    continue;
                }

                component.Add(candidate);
                keys.Add(candidate.Key);
                changed = true;
            }
        }

        return component;
    }

    private static bool AreIncident(
        TimberLineRectangleEdge first,
        TimberLineRectangleEdge second) =>
        AreConnected(first.Start, second.Start) ||
        AreConnected(first.Start, second.End) ||
        AreConnected(first.End, second.Start) ||
        AreConnected(first.End, second.End);

    private static bool HasDuplicateGeometry(IReadOnlyList<TimberLineRectangleEdge> edges)
    {
        for (var first = 0; first < edges.Count; first++)
        {
            for (var second = first + 1; second < edges.Count; second++)
            {
                if ((AreConnected(edges[first].Start, edges[second].Start) &&
                     AreConnected(edges[first].End, edges[second].End)) ||
                    (AreConnected(edges[first].Start, edges[second].End) &&
                     AreConnected(edges[first].End, edges[second].Start)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsUsable(TimberLineRectangleEdge edge) =>
        !string.IsNullOrWhiteSpace(edge.Key) &&
        IsFinite(edge.Start.X) && IsFinite(edge.Start.Y) &&
        IsFinite(edge.End.X) && IsFinite(edge.End.Y) &&
        Distance(edge.Start, edge.End) >= TimberRectangularFootprintValidator.MinimumEdgeLengthMm;

    private static bool AreConnected(
        TimberRectangularFootprintPoint first,
        TimberRectangularFootprintPoint second) =>
        Distance(first, second) <= EndpointConnectivityToleranceMm;

    private static double Distance(
        TimberRectangularFootprintPoint first,
        TimberRectangularFootprintPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private sealed record OrientedCycle(
        IReadOnlyList<TimberLineRectangleEdge> Edges,
        IReadOnlyList<TimberRectangularFootprintPoint> Vertices);
}
