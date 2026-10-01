using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public sealed record RoofAttachedManualPhysicalInput(
    RoofAttachedManualTimberData Metadata, RoofSegment3D AcceptedPlanAxis,
    double WidthMm, double HeightMm, bool Active = true);

/// <summary>Attached children reuse the ordinary prism/cut solver. Their anchor
/// supplies topology provenance, never the child's physical identity.</summary>
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
}
