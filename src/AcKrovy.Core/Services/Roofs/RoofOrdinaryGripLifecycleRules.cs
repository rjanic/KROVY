using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public enum RoofOrdinaryGripChange { None, Start, End, Middle, Both }

/// <summary>Classifies the completed Plan edit, never the native grip index or event order.</summary>
public static class RoofOrdinaryGripLifecycleRules
{
    private const double Tolerance = 0.0001d;

    public static RoofOrdinaryGripChange Classify(RoofSegment3D before, RoofSegment3D after)
    {
        var start = before.Start.DistanceTo(after.Start) > Tolerance;
        var end = before.End.DistanceTo(after.End) > Tolerance;
        if (!start && !end) return RoofOrdinaryGripChange.None;
        if (!end) return RoofOrdinaryGripChange.Start;
        if (!start) return RoofOrdinaryGripChange.End;
        return Delta(before.Start, after.Start).DistanceTo(Delta(before.End, after.End)) <= Tolerance
            ? RoofOrdinaryGripChange.Middle : RoofOrdinaryGripChange.Both;
    }

    public static RoofSegment3D Plan(RoofSegment3D native) => new(
        new(native.Start.X, native.Start.Y, 0), new(native.End.X, native.End.Y, 0));

    internal static RoofPoint3D Delta(RoofPoint3D before, RoofPoint3D after) =>
        new(after.X - before.X, after.Y - before.Y, after.Z - before.Z);
}

/// <summary>Persisted member-owned builder inputs. Roof history is not a live dependency.</summary>
public sealed record RoofOrdinaryPhysicalBuildState(
    int Version, RoofPoint3D[] Nodes, RoofOrdinaryTopologyEdgeState[] Edges,
    RoofOrdinaryTopologyFaceState[] Faces, int BoundaryVertexCount, double PitchDegrees,
    RoofFaceRafterSegment Anchor, RoofGeneratedMemberKey MemberKey,
    double EaveElevationMm, double WidthMm, double HeightMm,
    RoofAutomaticRafterPhysicalSettings Settings, RoofStructuralRafterTrimSource[] StructuralSources,
    RoofSegment3D AcceptedPlanAxis)
{
    public const int CurrentVersion = 2;
    // Optional for legacy v1 and the initial roof-owned capture; populated from
    // the pre-edit physical frame before accepting an Independent geometry edit.
    public RoofOrdinarySectionFrame? SectionFrame { get; init; }
}

public sealed record RoofOrdinaryTopologyEdgeState(int Start, int End,
    RoofTopologyEdgeKind Kind, int[] Faces);
public sealed record RoofOrdinaryTopologyFaceState(int SourceEdge, int[] Nodes);

/// <summary>Shares the ordinary roof-plane, frame and cut builder with Generated members.</summary>
public static class RoofOrdinaryPhysicalBuildStateRules
{
    public static RoofOrdinaryPhysicalBuildState Capture(RoofTopology topology,
        RoofFaceRafterSegment anchor, RoofGeneratedMemberKey key, double eaveElevationMm,
        double widthMm, double heightMm, RoofAutomaticRafterPhysicalSettings settings,
        IReadOnlyList<RoofStructuralRafterTrimSource> structuralSources, RoofSegment3D acceptedPlan) =>
        new(RoofOrdinaryPhysicalBuildState.CurrentVersion, topology.Nodes.ToArray(),
            topology.Edges.Select(e => new RoofOrdinaryTopologyEdgeState(e.StartNodeIndex,
                e.EndNodeIndex, e.Kind, e.FaceIndices.ToArray())).ToArray(),
            topology.Faces.Select(f => new RoofOrdinaryTopologyFaceState(f.SourceEdgeIndex,
                f.BoundaryNodeIndices.ToArray())).ToArray(), topology.BoundaryVertexCount,
            topology.PitchDegrees, anchor, key, eaveElevationMm, widthMm, heightMm, settings,
            structuralSources.ToArray(), acceptedPlan);

    public static bool IsValid(RoofOrdinaryPhysicalBuildState? state) => state is not null &&
        state.Version is 1 or RoofOrdinaryPhysicalBuildState.CurrentVersion &&
        (state.SectionFrame is null || RoofOrdinarySectionTransportRules.IsValid(state.SectionFrame)) &&
        state.Nodes is { Length: >= 3 } && state.Nodes.All(Finite) &&
        state.Faces is { Length: > 0 } && state.Faces.All(f => f is not null &&
            f.Nodes is { Length: >= 3 } && f.Nodes.All(i => i >= 0 && i < state.Nodes.Length)) &&
        state.Edges is not null && state.Edges.All(e => e is not null &&
            e.Start >= 0 && e.Start < state.Nodes.Length && e.End >= 0 && e.End < state.Nodes.Length &&
            Enum.IsDefined(typeof(RoofTopologyEdgeKind), e.Kind) && e.Faces is not null &&
            e.Faces.All(i => state.Faces.Any(f => f.SourceEdge == i))) &&
        state.BoundaryVertexCount >= 3 && state.BoundaryVertexCount <= state.Nodes.Length &&
        state.Anchor is not null && state.Settings is not null && state.StructuralSources is not null &&
        Finite(state.EaveElevationMm) && Finite(state.PitchDegrees) &&
        Finite(state.WidthMm) && state.WidthMm > 0 && Finite(state.HeightMm) && state.HeightMm > 0 &&
        Finite(state.AcceptedPlanAxis.Start) && Finite(state.AcceptedPlanAxis.End) &&
        state.AcceptedPlanAxis.Start.DistanceTo(state.AcceptedPlanAxis.End) > 0.0001d;

    /// <summary>Rebase stored inputs after an intervening rigid MOVE, which already owns its lifecycle.</summary>
    public static bool TryRebase(RoofOrdinaryPhysicalBuildState state, RoofSegment3D commandStart,
        out RoofOrdinaryPhysicalBuildState? rebased)
    {
        rebased = null;
        if (!IsValid(state) || !Finite(commandStart.Start) || !Finite(commandStart.End)) return false;
        var delta = RoofOrdinaryGripLifecycleRules.Delta(state.AcceptedPlanAxis.Start, commandStart.Start);
        if (Math.Abs(delta.Z) > 0.0001d ||
            RoofOrdinaryGripLifecycleRules.Delta(state.AcceptedPlanAxis.End, commandStart.End)
                .DistanceTo(delta) > 0.0001d) return false;
        RoofPoint3D Shift(RoofPoint3D p) => new(p.X + delta.X, p.Y + delta.Y, p.Z);
        RoofPoint2D Shift2(RoofPoint2D p) => new(p.X + delta.X, p.Y + delta.Y);
        rebased = state with
        {
            Nodes = state.Nodes.Select(Shift).ToArray(),
            Anchor = state.Anchor with { PlanStart = Shift2(state.Anchor.PlanStart), PlanEnd = Shift2(state.Anchor.PlanEnd) },
            StructuralSources = state.StructuralSources.Select(s => s with
            { Axis = new(Shift(s.Axis.Start), Shift(s.Axis.End)) }).ToArray(),
            AcceptedPlanAxis = commandStart,
        };
        return true;
    }

    public static bool TryBuild(RoofOrdinaryPhysicalBuildState state, RoofSegment3D acceptedPlan,
        out RoofAutomaticRafterPhysicalMember? member, out RoofOrdinaryPhysicalBuildState? updated,
        out string reason, bool useIndependentHorizontalFrame = false)
    {
        member = null;
        updated = null;
        reason = "InvalidIndependentOrdinaryBuildState";
        if (!IsValid(state)) return false;
        var topology = new RoofTopology(state.Nodes,
            state.Edges.Select(e => new RoofTopologyEdge(e.Start, e.End, e.Kind, e.Faces)),
            state.Faces.Select(f => new RoofTopologyFace(f.SourceEdge, f.Nodes)),
            state.BoundaryVertexCount, state.PitchDegrees);
        var segment = RoofOrdinaryRafterSemanticGeometryRules.ResolveSegment(topology, state.Anchor, acceptedPlan);
        RoofOrdinarySectionOrientation? orientation = null;
        if (useIndependentHorizontalFrame)
        {
            var oldFace = topology.Faces.SingleOrDefault(f => f.SourceEdgeIndex == state.Anchor.SourceFaceIndex);
            var newFace = topology.Faces.SingleOrDefault(f => f.SourceEdgeIndex == segment.SourceFaceIndex);
            if (oldFace is null || newFace is null ||
                !RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(topology, oldFace, out var oldNormal) ||
                !RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(topology, newFace, out var newNormal)) return false;
            var oldOrigin = topology.Nodes[oldFace.BoundaryNodeIndices[0]];
            var newOrigin = topology.Nodes[newFace.BoundaryNodeIndices[0]];
            if (!RoofOrdinaryPhysicalFrameRules.TryCreate(state.AcceptedPlanAxis,
                    new(oldOrigin.X, oldOrigin.Y, oldOrigin.Z + state.EaveElevationMm), oldNormal, out var oldRoofFrame) ||
                !RoofOrdinaryPhysicalFrameRules.TryCreate(acceptedPlan,
                    new(newOrigin.X, newOrigin.Y, newOrigin.Z + state.EaveElevationMm), newNormal, out var newRoofFrame)) return false;
            // CAD adapters supply the measured old BRep frame. Roof seeding is
            // only for neutral/legacy state with no physical representation.
            var previous = state.SectionFrame ?? new RoofOrdinarySectionFrame(oldRoofFrame!.LongitudinalAxis,
                oldRoofFrame.SectionWidthAxis, oldRoofFrame.SectionHeightAxis);
            if (!RoofOrdinarySectionTransportRules.TryTransport(previous, newRoofFrame!.LongitudinalAxis, out var minimum))
            { reason = "InvalidIndependentSectionFrame"; return false; }
            var directionChanged = previous.LongitudinalAxis.DistanceTo(newRoofFrame.LongitudinalAxis) > 1e-7;
            var roofPitch = StructuralMemberElevationRules.PitchDegreesFromLongitudinalAxis(
                newRoofFrame.LongitudinalAxis);
            var previousPitch = StructuralMemberElevationRules.PitchDegreesFromLongitudinalAxis(
                previous.LongitudinalAxis);
            // Elevation Apply / Elevation-aware GRIP: SectionFrame already encodes the
            // member-owned pitch on the accepted plan XY, while roof nodes still encode
            // the source roof pitch. Preserve member pitch. Plain Independent yaw edits
            // (plan XY changes away from previous L) still adopt the horizontal roof frame.
            var memberOwnedPitchMismatch =
                state.SectionFrame is not null &&
                PlanProjectionAligned(previous, acceptedPlan) &&
                Math.Abs(previousPitch - state.PitchDegrees) <= 0.05d &&
                Math.Abs(previousPitch - roofPitch) > 0.5d;
            RoofOrdinarySectionFrame? next = previous;
            if (directionChanged)
            {
                if (memberOwnedPitchMismatch)
                {
                    if (!TryCreatePitchPreservingIndependentFrame(
                            previous, acceptedPlan, state.PitchDegrees, state.Anchor, out next) ||
                        next is null)
                    { reason = "IndependentPitchPreservingSectionFrameUnresolved"; return false; }
                }
                else if (!RoofOrdinaryHorizontalSectionFrameRules.TryCreate(
                             newRoofFrame.LongitudinalAxis, out next))
                { reason = "IndependentHorizontalSectionFrameUnresolved"; return false; }
            }
            orientation = new(previous, next!, minimum!.NewFrame, minimum.MinimumAngleDegrees, directionChanged);
        }
        if (!RoofAutomaticRafterPhysicalBuilder.TryBuildSemanticMember(topology, segment,
                state.MemberKey, acceptedPlan, state.EaveElevationMm, state.WidthMm, state.HeightMm,
                state.Settings, state.StructuralSources, out member, out reason,
                independentSectionFrame: orientation?.NewFrame)) return false;
        if (orientation is not null) member = member! with { SectionOrientation = orientation };
        updated = state with { Version = RoofOrdinaryPhysicalBuildState.CurrentVersion,
            Anchor = segment, AcceptedPlanAxis = acceptedPlan, SectionFrame = orientation?.NewFrame ?? state.SectionFrame };
        return true;
    }

    /// <summary>
    /// Builds an Independent section frame on the accepted Plan2D direction while
    /// preserving the member-owned pitch (Elevation / PitchDegrees).
    /// </summary>
    internal static bool TryCreatePitchPreservingIndependentFrame(
        RoofOrdinarySectionFrame previous,
        RoofSegment3D plan,
        double pitchDegrees,
        RoofFaceRafterSegment anchor,
        out RoofOrdinarySectionFrame? frame)
    {
        frame = null;
        if (!RoofOrdinarySectionTransportRules.IsValid(previous) ||
            !Finite(plan.Start) || !Finite(plan.End) ||
            Math.Abs(plan.Start.Z) > 1e-7 || Math.Abs(plan.End.Z) > 1e-7 ||
            !Finite(pitchDegrees) || pitchDegrees < 0d || pitchDegrees >= 90d)
            return false;
        var dx = plan.End.X - plan.Start.X;
        var dy = plan.End.Y - plan.Start.Y;
        var planLength = Math.Sqrt(dx * dx + dy * dy);
        if (planLength <= 1e-7) return false;
        var planDirX = dx / planLength;
        var planDirY = dy / planLength;
        var eaveAtStart = ResolveIndependentEaveAtStart(previous, planDirX, planDirY, anchor);
        var pitchRad = pitchDegrees * Math.PI / 180.0;
        var dirSign = eaveAtStart ? 1.0 : -1.0;
        var lx = planDirX * Math.Cos(pitchRad) * dirSign;
        var ly = planDirY * Math.Cos(pitchRad) * dirSign;
        var lz = Math.Sin(pitchRad);
        return RoofOrdinaryHorizontalSectionFrameRules.TryCreate(new RoofPoint3D(lx, ly, lz), out frame);
    }

    private static bool PlanProjectionAligned(RoofOrdinarySectionFrame previous, RoofSegment3D plan)
    {
        var dx = plan.End.X - plan.Start.X;
        var dy = plan.End.Y - plan.Start.Y;
        var planLength = Math.Sqrt(dx * dx + dy * dy);
        if (planLength <= 1e-7) return false;
        var px = previous.LongitudinalAxis.X;
        var py = previous.LongitudinalAxis.Y;
        var projectedLength = Math.Sqrt(px * px + py * py);
        if (projectedLength <= 1e-9) return false;
        var dot = Math.Abs(
            (px / projectedLength) * (dx / planLength) +
            (py / projectedLength) * (dy / planLength));
        return dot >= 1 - 1e-6;
    }

    private static bool ResolveIndependentEaveAtStart(
        RoofOrdinarySectionFrame previous,
        double planDirX,
        double planDirY,
        RoofFaceRafterSegment anchor)
    {
        if (anchor.StartBoundaryRole == RoofRafterBoundaryRole.Eave) return true;
        if (anchor.EndBoundaryRole == RoofRafterBoundaryRole.Eave) return false;
        if (anchor.StartBoundaryRole == RoofRafterBoundaryRole.Ridge) return false;
        if (anchor.EndBoundaryRole == RoofRafterBoundaryRole.Ridge) return true;
        var prevXyLen = Math.Sqrt(
            previous.LongitudinalAxis.X * previous.LongitudinalAxis.X +
            previous.LongitudinalAxis.Y * previous.LongitudinalAxis.Y);
        if (prevXyLen <= 1e-9) return previous.LongitudinalAxis.Z >= 0;
        var sameSense =
            (previous.LongitudinalAxis.X / prevXyLen) * planDirX +
            (previous.LongitudinalAxis.Y / prevXyLen) * planDirY >= 0;
        return sameSense ? previous.LongitudinalAxis.Z >= -1e-12 : previous.LongitudinalAxis.Z < 0;
    }

    private static bool Finite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
    private static bool Finite(RoofPoint3D p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
}
