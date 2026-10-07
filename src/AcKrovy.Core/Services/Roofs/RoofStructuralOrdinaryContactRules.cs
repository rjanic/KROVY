using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Classifies whether an ordinary member's current StructuralCut is a real physical
/// contact for Hip/Valley height/profile. Logical membership alone is never enough:
/// a manually translated ordinary may keep TopologyEdgeIndex while no longer lying
/// on the structural side plane.
/// </summary>
public static class RoofStructuralOrdinaryContactRules
{
    public const double ToleranceMm = 1e-5;

    public enum Classification
    {
        Contact = 0,
        NonContact = 1,
        Corrupt = 2,
        Mismatch = 3,
    }

    public static Classification Classify(
        RoofAutomaticRafterPhysicalMember? member,
        RoofSegment3D structuralAxis,
        int topologyEdgeIndex,
        RoofRafterBoundaryRole expectedRole,
        double structuralWidthMm,
        out string detail)
    {
        detail = "no-structural-cut";
        if (member?.StructuralCut is not { } cut ||
            cut.TopologyEdgeIndex != topologyEdgeIndex)
        {
            return Classification.NonContact;
        }

        if (cut.Role != expectedRole ||
            !Finite(cut.StructuralWidthMm) ||
            Math.Abs(cut.StructuralWidthMm - structuralWidthMm) > ToleranceMm ||
            cut.CutFaceVertices is not { Count: >= 3 } vertices ||
            vertices.Any(point => !Finite(point)) ||
            !Finite(cut.PlanePoint) ||
            !Finite(cut.PlaneNormal))
        {
            detail = "OrdinaryStructuralCutMismatch";
            return Classification.Mismatch;
        }

        var normalX = -(structuralAxis.End.Y - structuralAxis.Start.Y);
        var normalY = structuralAxis.End.X - structuralAxis.Start.X;
        var normalLength = Math.Sqrt(normalX * normalX + normalY * normalY);
        if (!Finite(normalLength) || normalLength <= ToleranceMm)
        {
            detail = "StructuralPlanDirectionDegenerate";
            return Classification.Corrupt;
        }

        normalX /= normalLength;
        normalY /= normalLength;
        var offset = (cut.PlanePoint.X - structuralAxis.Start.X) * normalX +
            (cut.PlanePoint.Y - structuralAxis.Start.Y) * normalY;
        var onSide =
            Finite(offset) &&
            Math.Abs(Math.Abs(offset) - structuralWidthMm / 2d) <= ToleranceMm &&
            Math.Abs(cut.PlaneNormal.Z) <= ToleranceMm &&
            Math.Abs(cut.PlaneNormal.X * normalX +
                cut.PlaneNormal.Y * normalY - Math.Sign(offset)) <= ToleranceMm &&
            vertices.All(point => Math.Abs(
                (point.X - structuralAxis.Start.X) * normalX +
                (point.Y - structuralAxis.Start.Y) * normalY - offset) <= ToleranceMm);

        if (onSide)
        {
            detail = "contact";
            return Classification.Contact;
        }

        var planTouches = PlanTouchesStructuralEdge(member.PlanAxis, structuralAxis);
        detail = planTouches
            ? $"OrdinaryCutNotOnStructuralSide;signedOffset={offset.ToString(System.Globalization.CultureInfo.InvariantCulture)};halfWidth={(structuralWidthMm / 2d).ToString(System.Globalization.CultureInfo.InvariantCulture)};planTouches=true"
            : $"non-contact;signedOffset={offset.ToString(System.Globalization.CultureInfo.InvariantCulture)};halfWidth={(structuralWidthMm / 2d).ToString(System.Globalization.CultureInfo.InvariantCulture)};planTouches=false";
        return planTouches ? Classification.Corrupt : Classification.NonContact;
    }

    /// <summary>
    /// Selects only geometric contacts for one structural edge. Returns false when a
    /// corrupt on-edge ordinary cut fails side classification.
    /// </summary>
    public static bool TrySelectContacts(
        IEnumerable<RoofAutomaticRafterPhysicalMember>? ordinaryMembers,
        RoofSegment3D structuralAxis,
        int topologyEdgeIndex,
        RoofRafterBoundaryRole expectedRole,
        double structuralWidthMm,
        out IReadOnlyList<RoofAutomaticRafterPhysicalMember> contacts,
        out string failureReason)
    {
        contacts = Array.Empty<RoofAutomaticRafterPhysicalMember>();
        failureReason = string.Empty;
        if (ordinaryMembers is null)
            return true;

        var selected = new List<RoofAutomaticRafterPhysicalMember>();
        foreach (var member in ordinaryMembers)
        {
            if (member?.StructuralCut?.TopologyEdgeIndex != topologyEdgeIndex)
                continue;

            var kind = Classify(
                member,
                structuralAxis,
                topologyEdgeIndex,
                expectedRole,
                structuralWidthMm,
                out var detail);
            switch (kind)
            {
                case Classification.Contact:
                    selected.Add(member);
                    break;
                case Classification.NonContact:
                    break;
                case Classification.Mismatch:
                    failureReason = detail;
                    return false;
                case Classification.Corrupt:
                    failureReason = "OrdinaryCutNotOnStructuralSide";
                    return false;
                default:
                    failureReason = "OrdinaryStructuralCutMismatch";
                    return false;
            }
        }

        contacts = selected;
        return true;
    }

    public static bool PlanTouchesStructuralEdge(
        RoofSegment3D planAxis,
        RoofSegment3D structuralAxis)
    {
        if (!Finite(planAxis.Start) || !Finite(planAxis.End) ||
            !Finite(structuralAxis.Start) || !Finite(structuralAxis.End))
            return false;

        return OnEdge(structuralAxis, new RoofPoint2D(planAxis.Start.X, planAxis.Start.Y)) ||
               OnEdge(structuralAxis, new RoofPoint2D(planAxis.End.X, planAxis.End.Y));
    }

    private static bool OnEdge(RoofSegment3D edge, RoofPoint2D point)
    {
        var dx = edge.End.X - edge.Start.X;
        var dy = edge.End.Y - edge.Start.Y;
        var squared = dx * dx + dy * dy;
        if (!Finite(squared) || squared <= ToleranceMm * ToleranceMm)
            return false;

        var fraction = ((point.X - edge.Start.X) * dx +
            (point.Y - edge.Start.Y) * dy) / squared;
        var missX = point.X - (edge.Start.X + fraction * dx);
        var missY = point.Y - (edge.Start.Y + fraction * dy);
        return fraction >= -ToleranceMm && fraction <= 1d + ToleranceMm &&
            Math.Sqrt(missX * missX + missY * missY) <= ToleranceMm;
    }

    private static bool Finite(RoofPoint3D point) =>
        Finite(point.X) && Finite(point.Y) && Finite(point.Z);

    private static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
