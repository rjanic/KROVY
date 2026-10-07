using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Conservative adoption of a measured member package into the existing v2 model.
/// A context is evidence only when the normal builder reproduces the current body.</summary>
public static class RoofOrdinaryPhysicalBuildStateMigrationRules
{
    public const double GeometryToleranceMm = 0.01d;

    public static bool TryComplete(RoofOrdinaryPhysicalBuildState state, RoofSegment3D plan,
        out RoofOrdinaryPhysicalBuildState? complete)
    {
        complete = null;
        if (!RoofOrdinaryPhysicalBuildStateRules.IsValid(state) ||
            state.AcceptedPlanAxis.Start.DistanceTo(plan.Start) > 0.0001d ||
            state.AcceptedPlanAxis.End.DistanceTo(plan.End) > 0.0001d) return false;
        var section = state.SectionFrame;
        if (section is null)
        {
            if (!RoofOrdinaryPhysicalFrameRules.TryCreate(state, plan, state.Anchor.SourceFaceIndex, out var frame)) return false;
            section = new(frame!.LongitudinalAxis, frame.SectionWidthAxis, frame.SectionHeightAxis);
        }
        complete = state with { Version = RoofOrdinaryPhysicalBuildState.CurrentVersion, SectionFrame = section };
        return true;
    }

    public static bool TryRecover(RoofSegment3D plan, double widthMm, double heightMm,
        RoofOrdinarySectionFrame frame, RoofPoint3D upperAxisPoint,
        IReadOnlyList<RoofPoint3D> topVertices, IReadOnlyList<RoofPoint3D> bodyVertices,
        RoofGeneratedMemberKey key, RoofOrdinaryPhysicalBuildState? retainedContext,
        out RoofOrdinaryPhysicalBuildState? state, out string reason)
    {
        state = null;
        reason = "InvalidIndependentPackage";
        if (!Finite(plan.Start) || !Finite(plan.End) || !Finite(upperAxisPoint) ||
            Math.Abs(plan.Start.Z) > 1e-7 || Math.Abs(plan.End.Z) > 1e-7 ||
            plan.Start.DistanceTo(plan.End) <= 0.0001d ||
            !Finite(widthMm) || widthMm <= 0 || !Finite(heightMm) || heightMm <= 0 ||
            !RoofOrdinarySectionTransportRules.IsValid(frame) || frame.HeightAxis.Z <= 1e-9 ||
            Math.Abs(frame.WidthAxis.Z) > 1e-7 ||
            topVertices is null || topVertices.Count < 3 || topVertices.Any(p => !Finite(p)) ||
            bodyVertices is null || bodyVertices.Count < 6 || bodyVertices.Any(p => !Finite(p))) return false;
        var run = Sub(plan.End, plan.Start);
        var planLength = Length(run);
        var projected = new RoofPoint3D(frame.LongitudinalAxis.X, frame.LongitudinalAxis.Y, 0);
        var projectedLength = Length(projected);
        if (projectedLength <= 1e-9 || Dot(run, projected) / (planLength * projectedLength) < 1 - 1e-9)
        { reason = "MeasuredPhysicalAxisDoesNotProjectOntoPlan"; return false; }

        if (RoofOrdinaryPhysicalBuildStateRules.IsValid(retainedContext))
        {
            // No AUTO station lookup and no provenance TryRebase. Fit only a
            // candidate plane/cut context to the member's measured current axis.
            var context = retainedContext!;
            if (RoofOrdinaryPhysicalFrameRules.TryCreate(context, plan, context.Anchor.SourceFaceIndex, out var plane) &&
                Dot(plane!.LongitudinalAxis, frame.LongitudinalAxis) >= 1 - 1e-9)
            {
                var origin = plane.RoofPlaneOrigin;
                var normal = plane.RoofNormal;
                var predictedZ = origin.Z - (normal.X * (upperAxisPoint.X - origin.X) +
                    normal.Y * (upperAxisPoint.Y - origin.Y)) / normal.Z;
                var candidate = context with { Version = RoofOrdinaryPhysicalBuildState.CurrentVersion,
                    AcceptedPlanAxis = plan, WidthMm = widthMm, HeightMm = heightMm, SectionFrame = frame,
                    EaveElevationMm = context.EaveElevationMm + upperAxisPoint.Z - predictedZ };
                if (MatchesCurrentBody(candidate, plan, bodyVertices))
                { state = candidate; reason = "RetainedContextVerifiedAgainstCurrentPackage"; return true; }
            }
        }

        // Member-local plane and measured end boundaries. This is still the
        // existing topology/cut v2 model, with no live roof ownership dependency.
        var top = Unique(topVertices);
        if (top.Count != 4)
        { reason = "CurrentCutsRequireUnavailableVerifiedTopologyOrStructuralContext"; return false; }
        var ordered = top.OrderBy(p => Dot(Sub(p, upperAxisPoint), frame.LongitudinalAxis)).ToArray();
        var start = ordered.Take(2).OrderBy(p => Dot(p, frame.WidthAxis)).ToArray();
        var end = ordered.Skip(2).OrderByDescending(p => Dot(p, frame.WidthAxis)).ToArray();
        var nodes = start.Concat(end).ToArray();
        var pitch = Math.Atan2(Math.Sqrt(frame.HeightAxis.X * frame.HeightAxis.X +
            frame.HeightAxis.Y * frame.HeightAxis.Y), frame.HeightAxis.Z) * 180 / Math.PI;
        var roles = new[]
        {
            (RoofTopologyEdgeKind.CoplanarSeam, RoofTopologyEdgeKind.CoplanarSeam, LowerEndCutMode.Perpendicular),
            (RoofTopologyEdgeKind.Eave, RoofTopologyEdgeKind.Eave, LowerEndCutMode.Vertical),
            (RoofTopologyEdgeKind.Eave, RoofTopologyEdgeKind.CoplanarSeam, LowerEndCutMode.Vertical),
            (RoofTopologyEdgeKind.CoplanarSeam, RoofTopologyEdgeKind.Eave, LowerEndCutMode.Vertical),
            (RoofTopologyEdgeKind.CoplanarSeam, RoofTopologyEdgeKind.Ridge, LowerEndCutMode.Perpendicular),
            (RoofTopologyEdgeKind.Ridge, RoofTopologyEdgeKind.CoplanarSeam, LowerEndCutMode.Perpendicular),
            (RoofTopologyEdgeKind.Eave, RoofTopologyEdgeKind.Ridge, LowerEndCutMode.Vertical),
            (RoofTopologyEdgeKind.Ridge, RoofTopologyEdgeKind.Eave, LowerEndCutMode.Vertical),
            (RoofTopologyEdgeKind.Eave, RoofTopologyEdgeKind.Ridge, LowerEndCutMode.Perpendicular),
            (RoofTopologyEdgeKind.Ridge, RoofTopologyEdgeKind.Eave, LowerEndCutMode.Perpendicular),
        };
        foreach (var (startKind, endKind, cut) in roles)
        {
            var anchor = new RoofFaceRafterSegment(0, 0, key.StationIndex, 0, 0,
                new(plan.Start.X, plan.Start.Y), new(plan.End.X, plan.End.Y),
                RoofRafterBoundaryRole.Free, RoofRafterBoundaryRole.Free, planLength);
            var candidate = new RoofOrdinaryPhysicalBuildState(RoofOrdinaryPhysicalBuildState.CurrentVersion,
                nodes, new[] { new RoofOrdinaryTopologyEdgeState(0, 1, startKind, new[] { 0 }),
                    new RoofOrdinaryTopologyEdgeState(2, 3, endKind, new[] { 0 }) },
                new[] { new RoofOrdinaryTopologyFaceState(0, new[] { 0, 1, 2, 3 }) }, 4, pitch,
                anchor, key, 0, widthMm, heightMm, new(cut, RidgeJoinMode.Meet),
                Array.Empty<RoofStructuralRafterTrimSource>(), plan) { SectionFrame = frame };
            if (!MatchesCurrentBody(candidate, plan, bodyVertices)) continue;
            state = candidate;
            reason = "MeasuredMemberPlaneAndCutsVerified";
            return true;
        }
        // A detached Hip/Valley-style side cut is offset from the logical Plan
        // endpoint. Its measured plane cannot be represented by a Free/Ridge
        // edge through the physical top corners. Recover that offset into the
        // existing topology + structural-side-plane inputs, never from AUTO.
        if (TryMeasuredSideCut(plan, widthMm, heightMm, frame, upperAxisPoint,
                nodes, bodyVertices, key, pitch, out state))
        { reason = "MeasuredMemberStructuralCutsVerified"; return true; }
        reason = "CurrentPhysicalCutsOrPlanPlacementCannotBeReproduced";
        return false;
    }

    private static bool TryMeasuredSideCut(RoofSegment3D plan, double widthMm, double heightMm,
        RoofOrdinarySectionFrame frame, RoofPoint3D upperAxisPoint, RoofPoint3D[] top,
        IReadOnlyList<RoofPoint3D> body, RoofGeneratedMemberKey key, double pitch,
        out RoofOrdinaryPhysicalBuildState? recovered)
    {
        recovered = null;
        foreach (var atStart in new[] { false, true })
        {
            var first = atStart ? 0 : 2;
            var endpoint = atStart ? plan.Start : plan.End;
            var interior = atStart ? plan.End : plan.Start;
            var edge = Sub(top[first + 1], top[first]);
            var edgeLength = Math.Sqrt(edge.X*edge.X + edge.Y*edge.Y);
            if (edgeLength <= GeometryToleranceMm) continue;
            var normal = new RoofPoint3D(-edge.Y/edgeLength, edge.X/edgeLength, 0);
            if (Dot(Sub(interior, top[first]), normal) < 0)
                normal = new(-normal.X, -normal.Y, 0);
            var halfWidth = -Dot(Sub(endpoint, top[first]), normal);
            // Only a current, inward-facing plumb cut before the logical end.
            // Other placements/cuts must not acquire guessed structural state.
            if (!Finite(halfWidth) || halfWidth <= GeometryToleranceMm ||
                Dot(Sub(interior, endpoint), normal) <= halfWidth + GeometryToleranceMm) continue;
            var cutFace = body.Where(p => Math.Abs(Dot(Sub(p, top[first]), normal)) <= GeometryToleranceMm).ToArray();
            if (cutFace.Length < 3 ||
                cutFace.All(p => Math.Abs(Dot(Sub(p, upperAxisPoint), frame.HeightAxis)) <= GeometryToleranceMm))
                continue;
            var nodes = top.ToArray();
            // A plane boundary is not limited to the physical width of the timber.
            // Center its member-local support edge on the canonical Plan endpoint;
            // a normal-only shift of the short top edge can miss that endpoint.
            var endpointZ = upperAxisPoint.Z -
                (frame.HeightAxis.X*(endpoint.X-upperAxisPoint.X) +
                 frame.HeightAxis.Y*(endpoint.Y-upperAxisPoint.Y))/frame.HeightAxis.Z;
            var extent = Math.Max(edgeLength,widthMm*2);
            var tangent = new RoofPoint3D(edge.X/edgeLength,edge.Y/edgeLength,edge.Z/edgeLength);
            nodes[first] = new(endpoint.X-tangent.X*extent/2,endpoint.Y-tangent.Y*extent/2,
                endpointZ-tangent.Z*extent/2);
            nodes[first+1] = new(endpoint.X+tangent.X*extent/2,endpoint.Y+tangent.Y*extent/2,
                endpointZ+tangent.Z*extent/2);
            var structuralIndex = atStart ? 0 : 1;
            foreach (var otherKind in new[] { RoofTopologyEdgeKind.CoplanarSeam, RoofTopologyEdgeKind.Eave })
            {
                var edges = new[]
                {
                    new RoofOrdinaryTopologyEdgeState(0,1,atStart ? RoofTopologyEdgeKind.Hip : otherKind,new[] { 0 }),
                    new RoofOrdinaryTopologyEdgeState(2,3,atStart ? otherKind : RoofTopologyEdgeKind.Hip,new[] { 0 }),
                };
                var anchor = new RoofFaceRafterSegment(0,0,key.StationIndex,0,0,
                    new(plan.Start.X,plan.Start.Y),new(plan.End.X,plan.End.Y),
                    RoofRafterBoundaryRole.Free,RoofRafterBoundaryRole.Free,plan.Start.DistanceTo(plan.End));
                var source = new RoofStructuralRafterTrimSource(structuralIndex,RoofRafterBoundaryRole.Hip,
                    new(nodes[first],nodes[first+1]),2*halfWidth);
                var candidate = new RoofOrdinaryPhysicalBuildState(RoofOrdinaryPhysicalBuildState.CurrentVersion,
                    nodes,edges,new[] { new RoofOrdinaryTopologyFaceState(0,new[] { 0,1,2,3 }) },
                    4,pitch,anchor,key,0,widthMm,heightMm,new(LowerEndCutMode.Vertical,RidgeJoinMode.Meet),
                    new[] { source },plan) { SectionFrame=frame };
                if (!MatchesCurrentBody(candidate,plan,body)) continue;
                // Store the resolved current boundary roles, like every accepted v2 state.
                if (!RoofOrdinaryPhysicalBuildStateRules.TryBuild(candidate,plan,out _,out recovered,out _,
                        useIndependentHorizontalFrame:true)) continue;
                return true;
            }
        }
        return false;
    }

    public static bool MatchesCurrentBody(RoofOrdinaryPhysicalBuildState candidate, RoofSegment3D plan,
        IReadOnlyList<RoofPoint3D> body) =>
        RoofOrdinaryPhysicalBuildStateRules.TryBuild(candidate, plan, out var rebuilt, out _, out _,
            useIndependentHorizontalFrame: true) && rebuilt is not null &&
        body.All(p => rebuilt.SolidVertices.Any(q => p.DistanceTo(q) <= GeometryToleranceMm)) &&
        rebuilt.SolidVertices.All(p => body.Any(q => p.DistanceTo(q) <= GeometryToleranceMm));

    private static List<RoofPoint3D> Unique(IEnumerable<RoofPoint3D> points)
    {
        var unique = new List<RoofPoint3D>();
        foreach (var point in points)
            if (!unique.Any(p => p.DistanceTo(point) <= 1e-7)) unique.Add(point);
        return unique;
    }
    private static RoofPoint3D Sub(RoofPoint3D a, RoofPoint3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static double Dot(RoofPoint3D a, RoofPoint3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static double Length(RoofPoint3D p) => Math.Sqrt(Dot(p, p));
    private static bool Finite(double p) => !double.IsNaN(p) && !double.IsInfinity(p);
    private static bool Finite(RoofPoint3D p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
}
