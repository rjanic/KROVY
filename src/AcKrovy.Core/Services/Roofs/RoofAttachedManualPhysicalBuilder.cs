using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public sealed record RoofAttachedManualPhysicalInput(
    RoofAttachedManualTimberData Metadata, RoofSegment3D AcceptedPlanAxis,
    double WidthMm, double HeightMm, bool Active = true);

/// <summary>Attached children reuse the ordinary prism/cut solver. Rigid COPY/MOVE
/// carries a source reference body; the displaced Plan must not be lifted again.
/// Anchor provenance never supplies the child's physical identity.</summary>
public static class RoofAttachedManualPhysicalBuilder
{
    public static bool TryAppend(RoofTopology topology, RoofFaceRafterLayout layout,
        RoofAutomaticRafterPhysicalModel generated,
        IReadOnlyList<RoofAttachedManualPhysicalInput> children,
        double eaveElevationMm, RoofAutomaticRafterPhysicalSettings settings,
        IReadOnlyList<RoofStructuralRafterTrimSource>? structuralSources,
        out RoofAutomaticRafterPhysicalModel? model, out string reason)
    {
        model = null;
        reason = "InvalidAttachedManualPhysicalInput";
        var members = generated.Members.ToList();
        var identities = new HashSet<string>(members.Select(member => member.PhysicalIdentity), StringComparer.Ordinal);
        foreach (var child in children)
        {
            var data = child.Metadata;
            if (!RoofAttachedManualTimberDataCodec.TryValidate(data, out _) ||
                !string.Equals(data.RoofOwnerReference, generated.RoofOwnerReference, StringComparison.OrdinalIgnoreCase) ||
                !identities.Add(RoofAttachedManualIdentityRules.PhysicalKey(data))) return false;
            if (!child.Active) continue;
            if (data.AnchorGeneratedMemberKey is not { MemberKind: RoofGeneratedTimberKind.Rafter } key ||
                key.RoofFace != RafterRoofFace.Face0 || key.StationIndex < 0 || data.RelativeSegment is not { } relative ||
                new[] { relative.U0Mm, relative.V0Mm, relative.W0Mm, relative.U1Mm, relative.V1Mm, relative.W1Mm }
                    .Any(value => double.IsNaN(value) || double.IsInfinity(value))) return false;
            var anchor = key.StationIndex < layout.Segments.Count ? layout.Segments[key.StationIndex] :
                new RoofFaceRafterSegment(-1, -1, key.StationIndex, 0, 0d,
                    new RoofPoint2D(child.AcceptedPlanAxis.Start.X, child.AcceptedPlanAxis.Start.Y),
                    new RoofPoint2D(child.AcceptedPlanAxis.End.X, child.AcceptedPlanAxis.End.Y),
                    RoofRafterBoundaryRole.Free, RoofRafterBoundaryRole.Free, 0d);
            var segment = RoofOrdinaryRafterSemanticGeometryRules.ResolveSegment(
                topology, anchor, child.AcceptedPlanAxis);
            var source = generated.Members.FirstOrDefault(member =>
                member.AttachedManualIdentity is null && member.MemberKey == key);
            if (data.PhysicalReferenceSegment is { } reference)
            {
                if (Math.Abs(child.AcceptedPlanAxis.Start.Z) > RoofGeneratedMemberOverrideMath.LengthToleranceMm ||
                    Math.Abs(child.AcceptedPlanAxis.End.Z) > RoofGeneratedMemberOverrideMath.LengthToleranceMm) return false;
                // The reference is captured before rigid COPY/MOVE, not from the
                // displaced world axis. Legacy/non-rigid/MIRROR paths have no reference.
                var basisStart = source?.PlanAxis.Start ?? new RoofPoint3D(anchor.PlanStart.X, anchor.PlanStart.Y, 0);
                var basisEnd = source?.PlanAxis.End ?? new RoofPoint3D(anchor.PlanEnd.X, anchor.PlanEnd.Y, 0);
                if (source is null && key.StationIndex >= layout.Segments.Count &&
                    !RoofAttachedManualRelativeGeometryRules.TryRecoverAnchorBasis(relative,
                        child.AcceptedPlanAxis.Start, child.AcceptedPlanAxis.End, out basisStart, out basisEnd)) return false;
                if (!RoofAttachedManualRelativeGeometryRules.TryReplay(basisStart, basisEnd, reference, out var start, out var end) ||
                    !RoofGeneratedMemberOverrideMath.TryClassifyPureTranslation(new(start, end),
                        new(child.AcceptedPlanAxis.Start, child.AcceptedPlanAxis.End),
                        RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, out var displacement, out _, out _))
                {
                    reason = "InvalidAttachedManualPhysicalReference";
                    return false;
                }
                var referenceAxis = new RoofSegment3D(start, end);
                var referenceBody = source;
                if (referenceBody is null || referenceBody.WidthMm != child.WidthMm || referenceBody.HeightMm != child.HeightMm ||
                    start.DistanceTo(referenceBody.PlanAxis.Start) > RoofGeneratedMemberOverrideMath.LengthToleranceMm ||
                    end.DistanceTo(referenceBody.PlanAxis.End) > RoofGeneratedMemberOverrideMath.LengthToleranceMm)
                {
                    var referenceSegment = RoofOrdinaryRafterSemanticGeometryRules.ResolveSegment(topology, anchor, referenceAxis);
                    if (!RoofAutomaticRafterPhysicalBuilder.TryBuildSemanticMember(topology, referenceSegment, key,
                            referenceAxis, eaveElevationMm, child.WidthMm, child.HeightMm, settings, structuralSources,
                            out referenceBody, out reason)) return false;
                }
                members.Add(TranslateCopy(referenceBody!, child.AcceptedPlanAxis, displacement) with
                { AttachedManualIdentity = RoofAttachedManualIdentityRules.Resolve(data) });
                continue;
            }
            if (!RoofAutomaticRafterPhysicalBuilder.TryBuildSemanticMember(
                    topology, segment, key, child.AcceptedPlanAxis, eaveElevationMm,
                    child.WidthMm, child.HeightMm, settings, structuralSources,
                    out var member, out reason)) return false;
            members.Add(member! with { AttachedManualIdentity = RoofAttachedManualIdentityRules.Resolve(data) });
        }
        model = generated with { Members = Array.AsReadOnly(members.ToArray()) };
        reason = string.Empty;
        return true;
    }

    private static RoofAutomaticRafterPhysicalMember TranslateCopy(
        RoofAutomaticRafterPhysicalMember source, RoofSegment3D plan, RoofPoint3D displacement)
    {
        RoofPoint3D Shift(RoofPoint3D point) => new(point.X + displacement.X, point.Y + displacement.Y, point.Z);
        IReadOnlyList<RoofPoint3D> ShiftAll(IReadOnlyList<RoofPoint3D> points) => Array.AsReadOnly(points.Select(Shift).ToArray());
        return source with
        {
            PlanAxis = plan,
            SolidVertices = ShiftAll(source.SolidVertices),
            HorizontalCut = source.HorizontalCut is { } h ? h with
            {
                SourcePrismVertices = ShiftAll(h.SourcePrismVertices), BodyVertices = ShiftAll(h.BodyVertices),
                CutFaceVertices = ShiftAll(h.CutFaceVertices), TopFaceVertices = ShiftAll(h.TopFaceVertices),
            } : null,
            StructuralCut = source.StructuralCut is { } s ? s with
            {
                PlanePoint = Shift(s.PlanePoint), SourcePrismVertices = ShiftAll(s.SourcePrismVertices),
                CutFaceVertices = ShiftAll(s.CutFaceVertices), TopFaceVertices = ShiftAll(s.TopFaceVertices),
                BottomFaceVertices = s.BottomFaceVertices is { } bottom ? ShiftAll(bottom) : null,
                LowerContactEdge = s.LowerContactEdge is { } edge ? new RoofSegment3D(Shift(edge.Start), Shift(edge.End)) : null,
            } : null,
            RidgeOverlapCut = source.RidgeOverlapCut is { } r ? r with
            {
                PlanePoint = Shift(r.PlanePoint), SourcePrismVertices = ShiftAll(r.SourcePrismVertices),
                BodyVertices = ShiftAll(r.BodyVertices), TopFaceVertices = ShiftAll(r.TopFaceVertices),
                BottomFaceVertices = ShiftAll(r.BottomFaceVertices), CutFaceVertices = ShiftAll(r.CutFaceVertices),
            } : null,
            RidgeMeetCut = source.RidgeMeetCut is { } meet ? meet with
            {
                PlanePoint = Shift(meet.PlanePoint),
                SourcePrismVertices = ShiftAll(meet.SourcePrismVertices),
                BodyVertices = ShiftAll(meet.BodyVertices),
                TopFaceVertices = ShiftAll(meet.TopFaceVertices),
                BottomFaceVertices = ShiftAll(meet.BottomFaceVertices),
                CutFaceVertices = ShiftAll(meet.CutFaceVertices),
            } : null,
        };
    }
}
