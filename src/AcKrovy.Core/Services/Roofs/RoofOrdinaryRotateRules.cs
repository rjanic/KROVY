using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Checks the final native planar rotation, without reconstructing the user's base point.</summary>
public static class RoofOrdinaryRotateRules
{
    public static bool IsRigidPlanRotation(RoofSegment3D before, RoofSegment3D after)
    {
        before = RoofOrdinaryGripLifecycleRules.Plan(before);
        after = RoofOrdinaryGripLifecycleRules.Plan(after);
        var originalLength = before.Start.DistanceTo(before.End);
        var finalLength = after.Start.DistanceTo(after.End);
        return !double.IsNaN(originalLength) && !double.IsInfinity(originalLength) &&
            !double.IsNaN(finalLength) && !double.IsInfinity(finalLength) &&
            originalLength > 0.0001d && Math.Abs(finalLength - originalLength) <= 0.0001d;
    }

    public static bool RotationDetected(RoofSegment3D before, RoofSegment3D after)
    {
        if (!IsRigidPlanRotation(before, after)) return false;
        var a = RoofOrdinaryGripLifecycleRules.Plan(before);
        var b = RoofOrdinaryGripLifecycleRules.Plan(after);
        var oldDirection = RoofOrdinaryGripLifecycleRules.Delta(a.Start, a.End);
        var newDirection = RoofOrdinaryGripLifecycleRules.Delta(b.Start, b.End);
        return oldDirection.DistanceTo(newDirection) > 0.0001d;
    }
}
