using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public sealed record RoofOrdinaryJoinSource(RoofSegment3D Plan,
    RoofOrdinaryPhysicalBuildState? State, string Material);
public sealed record RoofOrdinaryJoinOrder(int StartOwner, int EndOwner, int GapCount, int OverlapCount);

/// <summary>Validates native extents and composes existing v2 outer-end inputs.
/// Does not join CAD geometry, bridge gaps, migrate state or extrude a solid.</summary>
public static class RoofOrdinaryJoinRules
{
    private const double Tol = RoofFaceRafterLayoutService.CoordinateToleranceMm;
    private const double Angle = SimpleGableRoofGeometryTolerance.AngularTolerance;

    public static bool TryOrder(IReadOnlyList<RoofSegment3D> sources, RoofSegment3D native,
        out RoofOrdinaryJoinOrder? order, out string reason)
    {
        order = null; reason = "InvalidNativeJoinGeometry";
        if (sources.Count < 2 || !Valid(native) || sources.Any(s => !Valid(s))) return false;
        var length = native.Start.DistanceTo(native.End);
        var unit = Scale(Sub(native.End,native.Start),1/length);
        double Project(RoofPoint3D p) => Dot(Sub(p,native.Start),unit);
        bool OnAxis(RoofPoint3D p) => Sub(Sub(p,native.Start),Scale(unit,Project(p))).DistanceTo(new(0,0,0)) <= Tol;
        if (sources.Any(s => !OnAxis(s.Start) || !OnAxis(s.End)))
        { reason = "NonCollinearSources"; return false; }
        var ranges = sources.Select((s,i) => (Index:i,
            Min:Math.Min(Project(s.Start),Project(s.End)),
            Max:Math.Max(Project(s.Start),Project(s.End)))).OrderBy(s => s.Min).ThenBy(s => s.Index).ToArray();
        var last = ranges.OrderByDescending(s => s.Max).ThenBy(s => s.Index).First();
        if (Math.Abs(ranges[0].Min) > Tol || Math.Abs(last.Max-length) > Tol)
        { reason = "NativeOuterExtentsDifferFromSources"; return false; }
        var maximum = ranges[0].Max; var gaps = 0; var overlaps = 0;
        foreach (var range in ranges.Skip(1))
        {
            if (range.Min > maximum + Tol) gaps++;
            else if (range.Min < maximum - Tol) overlaps++;
            maximum = Math.Max(maximum,range.Max);
        }
        order = new(ranges[0].Index,last.Index,gaps,overlaps); reason = "ValidNativeStraightJoin"; return true;
    }

    public static bool TryPrepare(IReadOnlyList<RoofOrdinaryJoinSource> sources, RoofSegment3D native,
        out RoofOrdinaryPhysicalBuildState? state, out RoofOrdinaryJoinOrder? order, out string reason)
    {
        state = null;
        if (!TryOrder(sources.Select(s => s.Plan).ToArray(),native,out order,out reason)) return false;
        if (sources.Any(s => !RoofOrdinaryPhysicalBuildStateRules.IsValid(s.State)))
        { reason = "SourcePhysicalBuildStateUnavailable"; return false; }
        var first = sources[order!.StartOwner].State!;
        var material = sources[order.StartOwner].Material;
        if (sources.Any(s => Math.Abs(s.State!.WidthMm-first.WidthMm) > Tol ||
                Math.Abs(s.State.HeightMm-first.HeightMm) > Tol ||
                !StringComparer.Ordinal.Equals(s.Material,material)))
        { reason = "IncompatibleSectionOrMaterial"; return false; }
        var bodies = new List<RoofAutomaticRafterPhysicalMember>();
        var resolvedStates = new List<RoofOrdinaryPhysicalBuildState>();
        if (!RoofOrdinaryPhysicalFrameRules.TryCreate(first,native,first.Anchor.SourceFaceIndex,out var common))
        { reason = "JoinedPhysicalAxisUnavailable"; return false; }
        foreach (var source in sources)
        {
            var item = source.State!;
            if (item.AcceptedPlanAxis.Start.DistanceTo(source.Plan.Start) > Tol ||
                item.AcceptedPlanAxis.End.DistanceTo(source.Plan.End) > Tol ||
                !RoofOrdinaryPhysicalFrameRules.TryCreate(item,native,item.Anchor.SourceFaceIndex,out var frame) ||
                frame!.UpperAxis.Start.DistanceTo(common!.UpperAxis.Start) > Tol ||
                frame.UpperAxis.End.DistanceTo(common.UpperAxis.End) > Tol ||
                !RoofOrdinaryPhysicalBuildStateRules.TryBuild(item,source.Plan,out var body,out var resolved,out _,true))
            { reason = "IncompatiblePhysicalAxisOrSourceReplay"; return false; }
            var section = item.SectionFrame ?? body!.SectionOrientation!.NewFrame;
            var sign = Dot(section.LongitudinalAxis,common!.LongitudinalAxis) >= 0 ? 1d : -1d;
            if (Math.Abs(section.WidthAxis.Z) > Angle || section.HeightAxis.Z <= 0 ||
                Dot(Scale(section.LongitudinalAxis,sign),common.LongitudinalAxis) < 1-Angle ||
                !RoofOrdinaryHorizontalSectionFrameRules.TryCreate(common.LongitudinalAxis,out var expected) ||
                Dot(Scale(section.WidthAxis,sign),expected!.WidthAxis) < 1-Angle ||
                Dot(section.HeightAxis,expected.HeightAxis) < 1-Angle)
            { reason = "IncompatiblePhysicalSectionFrame"; return false; }
            bodies.Add(body!);
            resolvedStates.Add(resolved!);
        }
        RoofRafterBoundaryRole Role(int owner, RoofPoint3D endpoint) =>
            sources[owner].Plan.Start.DistanceTo(endpoint) <= Tol ? resolvedStates[owner].Anchor.StartBoundaryRole :
                resolvedStates[owner].Anchor.EndBoundaryRole;
        var outer = new[] { (Owner:order.StartOwner,Role:Role(order.StartOwner,native.Start)),
            (Owner:order.EndOwner,Role:Role(order.EndOwner,native.End)) };
        var eaveModes = outer.Where(p => p.Role==RoofRafterBoundaryRole.Eave)
            .Select(p => sources[p.Owner].State!.Settings.LowerEndCutMode).Distinct().ToArray();
        var ridgeModes = outer.Where(p => p.Role==RoofRafterBoundaryRole.Ridge)
            .Select(p => sources[p.Owner].State!.Settings.RidgeJoinMode).Distinct().ToArray();
        if (eaveModes.Length>1 || ridgeModes.Length>1)
        { reason="IncompatibleOuterCutSettings"; return false; }
        var settings = new RoofAutomaticRafterPhysicalSettings(eaveModes.Length==0 ? first.Settings.LowerEndCutMode : eaveModes[0],
            ridgeModes.Length==0 ? first.Settings.RidgeJoinMode : ridgeModes[0]);
        // Keep only the two outer boundary contexts on the active face. No
        // internal BREAK seam/cut can survive into the joined member state.
        var active = first.Anchor.SourceFaceIndex;
        var nodes = first.Nodes.ToList();
        var faces = first.Faces.ToList();
        var edges = first.Edges.Where(e => !e.Faces.Contains(active)).ToList();
        var structural = new List<RoofStructuralRafterTrimSource>();
        void Outer(int owner, RoofPoint3D endpoint)
        {
            var source = sources[owner].State!;
            var offset = nodes.Count;
            nodes.AddRange(source.Nodes.Select(p => new RoofPoint3D(p.X,p.Y,p.Z+source.EaveElevationMm-first.EaveElevationMm)));
            var mapping = new Dictionary<int,int> { [source.Anchor.SourceFaceIndex] = active };
            foreach (var face in source.Faces.Where(f => f.SourceEdge != source.Anchor.SourceFaceIndex))
            {
                var index = faces.Max(f => f.SourceEdge)+1;
                mapping[face.SourceEdge] = index;
                faces.Add(new(index,face.Nodes.Select(i => i+offset).ToArray()));
            }
            foreach (var pair in source.Edges.Select((edge,index) => (edge,index)))
            {
                var e = pair.edge;
                if (!e.Faces.Contains(source.Anchor.SourceFaceIndex) ||
                    !OnSegment(endpoint,source.Nodes[e.Start],source.Nodes[e.End])) continue;
                var index = edges.Count;
                edges.Add(new(e.Start+offset,e.End+offset,e.Kind,e.Faces.Select(f => mapping[f]).ToArray()));
                structural.AddRange(source.StructuralSources.Where(s => s.TopologyEdgeIndex == pair.index)
                    .Select(s => s with { TopologyEdgeIndex = index,
                        Axis = new(new(s.Axis.Start.X,s.Axis.Start.Y,s.Axis.Start.Z+source.EaveElevationMm-first.EaveElevationMm),
                            new(s.Axis.End.X,s.Axis.End.Y,s.Axis.End.Z+source.EaveElevationMm-first.EaveElevationMm)) }));
            }
        }
        Outer(order.StartOwner,native.Start); Outer(order.EndOwner,native.End);
        if (!RoofOrdinaryHorizontalSectionFrameRules.TryCreate(common!.LongitudinalAxis,out var joinedFrame))
        { reason = "JoinedHorizontalFrameUnavailable"; return false; }
        var candidate = first with { Nodes = nodes.ToArray(), Edges = edges.ToArray(), Faces = faces.ToArray(),
            StructuralSources = structural.ToArray(), Settings = settings, AcceptedPlanAxis = native, SectionFrame = joinedFrame,
            Anchor = first.Anchor with { PlanStart = new(native.Start.X,native.Start.Y),
                PlanEnd = new(native.End.X,native.End.Y), PlanLengthMm = native.Start.DistanceTo(native.End) } };
        if (!RoofOrdinaryPhysicalBuildStateRules.TryBuild(candidate,native,out var joined,out var complete,out reason,true)) return false;
        bool MatchesOuter(int owner, bool start)
        {
            var endpoint = start ? native.Start : native.End;
            var other = start ? native.End : native.Start;
            var source = sources[owner].Plan;
            var sourceOther = source.Start.DistanceTo(endpoint) <= Tol ? source.End : source.Start;
            var old = bodies[owner].SolidVertices.Where(p => PlanDistance(p,endpoint) < PlanDistance(p,sourceOther)).ToArray();
            var current = joined!.SolidVertices.Where(p => PlanDistance(p,endpoint) < PlanDistance(p,other)).ToArray();
            var tolerance = RoofOrdinaryPhysicalBuildStateMigrationRules.GeometryToleranceMm;
            return old.Length >= 3 && current.Length >= 3 &&
                old.All(p => current.Any(q => p.DistanceTo(q) <= tolerance)) &&
                current.All(p => old.Any(q => p.DistanceTo(q) <= tolerance));
        }
        if (!MatchesOuter(order.StartOwner,true) || !MatchesOuter(order.EndOwner,false))
        { reason = "OuterPhysicalCutContextNotPreserved"; return false; }
        state = complete; reason = "JoinedOuterContextsVerified"; return true;
    }

    public static bool IsStraightPolyline(IReadOnlyList<RoofPoint3D> points, IReadOnlyList<double> bulges,
        bool closed, out RoofSegment3D? axis)
    {
        axis = null;
        if (closed || points.Count < 2 || bulges.Count != points.Count || bulges.Any(b => b != 0)) return false;
        var native = new RoofSegment3D(points[0],points[points.Count-1]);
        if (!Valid(native)) return false;
        var length = native.Start.DistanceTo(native.End); var unit = Scale(Sub(native.End,native.Start),1/length);
        var previous = 0d;
        foreach (var point in points)
        {
            if (!Finite(point) || Math.Abs(point.Z) > Tol) return false;
            var t = Dot(Sub(point,native.Start),unit);
            if (t < previous-Tol || t > length+Tol ||
                Sub(Sub(point,native.Start),Scale(unit,t)).DistanceTo(new(0,0,0)) > Tol) return false;
            previous = t;
        }
        axis = RoofOrdinaryGripLifecycleRules.Plan(native); return true;
    }
    private static bool Valid(RoofSegment3D axis) => Finite(axis.Start) && Finite(axis.End) &&
        Math.Abs(axis.Start.Z) <= Tol && Math.Abs(axis.End.Z) <= Tol && axis.Start.DistanceTo(axis.End) > Tol;
    private static bool Finite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
    private static bool Finite(RoofPoint3D p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
    private static bool OnSegment(RoofPoint3D p, RoofPoint3D a, RoofPoint3D b)
    {
        var dx = b.X-a.X; var dy = b.Y-a.Y; var length = Math.Sqrt(dx*dx+dy*dy);
        var t = (p.X-a.X)*dx+(p.Y-a.Y)*dy;
        return length > Tol && Math.Abs((p.X-a.X)*dy-(p.Y-a.Y)*dx) <= Tol*length &&
            t >= -Tol*length && t <= length*length+Tol*length;
    }
    private static double PlanDistance(RoofPoint3D a, RoofPoint3D b) => Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
    private static RoofPoint3D Sub(RoofPoint3D a, RoofPoint3D b) => new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    private static RoofPoint3D Scale(RoofPoint3D p,double s) => new(p.X*s,p.Y*s,p.Z*s);
    private static double Dot(RoofPoint3D a, RoofPoint3D b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
}
