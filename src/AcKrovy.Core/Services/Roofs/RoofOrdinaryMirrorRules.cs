using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Member-owned reflection; final native Plan direction is authoritative.</summary>
public static class RoofOrdinaryMirrorRules
{
    public sealed record Reflection(double XX, double XY, double YY, RoofPoint3D Source, RoofPoint3D Target,
        bool EndpointsReversed)
    {
        public RoofPoint3D Point(RoofPoint3D p) => new(
            Target.X + XX * (p.X - Source.X) + XY * (p.Y - Source.Y),
            Target.Y + XY * (p.X - Source.X) + YY * (p.Y - Source.Y), p.Z);
    }

    public static bool TryResolve(RoofSegment3D source, RoofSegment3D native, out Reflection? reflection)
    {
        reflection = null;
        var plan = RoofOrdinaryGripLifecycleRules.Plan(native);
        if (new[] { source.Start, source.End, native.Start, native.End }.Any(p =>
                !Finite(p.X) || !Finite(p.Y) || !Finite(p.Z))) return false;
        var a = TryMatch(source, plan, false);
        var b = TryMatch(source, plan, true);
        // Coincident midpoints can describe two different mirror planes. Do not
        // invent a physical roof plane or swap eave/ridge roles on that ambiguity.
        if (a is not null && b is not null) return false;
        reflection = a ?? b;
        return reflection is not null;
    }

    private static Reflection? TryMatch(RoofSegment3D source, RoofSegment3D plan, bool reversed)
    {
        var target = reversed ? new RoofSegment3D(plan.End, plan.Start) : plan;
        var sx = source.End.X - source.Start.X; var sy = source.End.Y - source.Start.Y;
        var tx = target.End.X - target.Start.X; var ty = target.End.Y - target.Start.Y;
        var s = Math.Sqrt(sx * sx + sy * sy); var t = Math.Sqrt(tx * tx + ty * ty);
        if (!Finite(s) || !Finite(t) || s <= 1e-4 || Math.Abs(s - t) > 1e-4) return null;
        sx /= s; sy /= s; tx /= t; ty /= t;
        var xx = sx * tx - sy * ty; var xy = sx * ty + sy * tx; var yy = -xx;
        var dx = (target.Start.X - source.Start.X + target.End.X - source.End.X) / 2;
        var dy = (target.Start.Y - source.Start.Y + target.End.Y - source.End.Y) / 2;
        if (Math.Abs(xx * dx + xy * dy + dx) > 1e-4 ||
            Math.Abs(xy * dx + yy * dy + dy) > 1e-4) return null;
        return new(xx, xy, yy, source.Start, target.Start, reversed);
    }

    public static bool TryPreparePhysicalMirror(RoofOrdinaryPhysicalBuildState state, RoofSegment3D native,
        out RoofOrdinaryPhysicalBuildState? mirrored)
    {
        mirrored = null;
        if (!RoofOrdinaryPhysicalBuildStateRules.IsValid(state) ||
            !TryResolve(state.AcceptedPlanAxis, native, out var reflection)) return false;
        var r = reflection!;
        RoofPoint2D Point2(RoofPoint2D p) { var q = r.Point(new(p.X, p.Y, 0)); return new(q.X, q.Y); }
        var plan = RoofOrdinaryGripLifecycleRules.Plan(native);
        var transformed = state with
        {
            Nodes = state.Nodes.Select(r.Point).ToArray(),
            Anchor = state.Anchor with { PlanStart = Point2(state.Anchor.PlanStart), PlanEnd = Point2(state.Anchor.PlanEnd) },
            StructuralSources = state.StructuralSources.Select(s => s with
                { Axis = new(r.Point(s.Axis.Start), r.Point(s.Axis.End)) }).ToArray(),
            AcceptedPlanAxis = plan, SectionFrame = null,
        };
        if (!RoofOrdinaryPhysicalFrameRules.TryCreate(transformed, plan, state.Anchor.SourceFaceIndex, out var frame) ||
            !RoofOrdinaryHorizontalSectionFrameRules.TryCreate(frame!.LongitudinalAxis, out var section)) return false;
        mirrored = transformed with { SectionFrame = section };
        return true;
    }

    public static RoofIndependentOrdinaryTimberData CreateIdentity(RoofIndependentOrdinaryTimberData? source,
        RoofGeneratedTimberData? generated, string newMemberId) =>
        RoofOrdinaryCopyCloneRules.CreateIdentity(source, generated, newMemberId) with
        { OriginKind = source is null ? RoofIndependentOrdinaryOriginKind.MirroredFromAuto :
            RoofIndependentOrdinaryOriginKind.MirroredFromIndependent };

    public static bool ReplacesSource(bool inPlace, bool sourceErased) => inPlace || sourceErased;
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
