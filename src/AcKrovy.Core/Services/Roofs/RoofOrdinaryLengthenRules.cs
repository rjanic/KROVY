using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Validates final native LINE geometry; does not implement LENGTHEN modes or select an endpoint.</summary>
public static class RoofOrdinaryLengthenRules
{
    private const double Tolerance = 0.0001d;

    public static RoofOrdinaryGripChange Endpoint(RoofSegment3D before, RoofSegment3D after) =>
        RoofOrdinaryGripLifecycleRules.Classify(
            RoofOrdinaryGripLifecycleRules.Plan(before), RoofOrdinaryGripLifecycleRules.Plan(after));

    public static bool IsCollinear(RoofSegment3D before, RoofSegment3D after)
    {
        before = RoofOrdinaryGripLifecycleRules.Plan(before);
        after = RoofOrdinaryGripLifecycleRules.Plan(after);
        if (!Finite(before.Start) || !Finite(before.End) || !Finite(after.Start) || !Finite(after.End)) return false;
        var dx = before.End.X - before.Start.X;
        var dy = before.End.Y - before.Start.Y;
        var length = before.Start.DistanceTo(before.End);
        if (length <= Tolerance || after.Start.DistanceTo(after.End) <= Tolerance) return false;
        double Distance(RoofPoint3D point) => Math.Abs(dx * (point.Y - before.Start.Y) -
            dy * (point.X - before.Start.X)) / length;
        return Distance(after.Start) <= Tolerance && Distance(after.End) <= Tolerance &&
            dx * (after.End.X - after.Start.X) + dy * (after.End.Y - after.Start.Y) > 0;
    }

    public static bool IsEndpointLengthEdit(RoofSegment3D before, RoofSegment3D after, out string reason)
    {
        var endpoint = Endpoint(before, after);
        reason = endpoint is RoofOrdinaryGripChange.Start or RoofOrdinaryGripChange.End
            ? "non_collinear_or_degenerate" : "expected_one_changed_endpoint:" + endpoint;
        if (endpoint is not (RoofOrdinaryGripChange.Start or RoofOrdinaryGripChange.End) ||
            !IsCollinear(before, after)) return false;
        reason = "none";
        return true;
    }

    private static bool Finite(RoofPoint3D point) =>
        !double.IsNaN(point.X) && !double.IsInfinity(point.X) &&
        !double.IsNaN(point.Y) && !double.IsInfinity(point.Y);
}

