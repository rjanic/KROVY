using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Plan2D is the only user geometry-edit authority for an Ordinary member.</summary>
public static class RoofOrdinaryLogicalMoveRules
{
    private const double ToleranceMm = 0.0001d;

    public static bool TryPlan(
        RoofPoint3D beforeStart, RoofPoint3D beforeEnd,
        RoofPoint3D afterStart, RoofPoint3D afterEnd,
        RoofPoint3D? beforeSolidCenter, RoofPoint3D? afterSolidCenter,
        out RoofOrdinaryLogicalMovePlan? plan)
    {
        plan = null;
        if (!Finite(beforeStart) || !Finite(beforeEnd) || !Finite(afterStart) ||
            !Finite(afterEnd) || (beforeSolidCenter.HasValue && !Finite(beforeSolidCenter.Value)) ||
            (afterSolidCenter.HasValue && !Finite(afterSolidCenter.Value)) ||
            beforeSolidCenter.HasValue != afterSolidCenter.HasValue)
            return false;

        var lineDelta = Subtract(afterStart, beforeStart);
        if (Subtract(afterEnd, beforeEnd).DistanceTo(lineDelta) > ToleranceMm)
            return false;
        var solidDelta = beforeSolidCenter.HasValue
            ? Subtract(afterSolidCenter!.Value, beforeSolidCenter.Value)
            : new RoofPoint3D(0d, 0d, 0d);
        var lineMoved = Length(lineDelta) > ToleranceMm;
        var solidMoved = beforeSolidCenter.HasValue && Length(solidDelta) > ToleranceMm;
        if (!lineMoved && !solidMoved) return false;
        // Geometry, not ObjectModified callback order, establishes authority.
        // If the Plan moved, it wins even when AutoCAD also moved the Solid in Z.
        var planChanged = Math.Abs(lineDelta.X) > ToleranceMm ||
            Math.Abs(lineDelta.Y) > ToleranceMm;
        var initiator = planChanged
            ? RoofOrdinaryMoveInitiator.Plan2D
            : RoofOrdinaryMoveInitiator.Physical3D;
        var acceptedPlanDelta = planChanged
            ? new RoofPoint3D(lineDelta.X, lineDelta.Y, 0d)
            : new RoofPoint3D(0d, 0d, 0d);
        var nativeDelta = planChanged ? acceptedPlanDelta : solidDelta;
        // A native Z move of the plan Line violates its Z=0 representation.
        var lineTranslation = new RoofPoint3D(0d, 0d, -lineDelta.Z);
        // Correct only the native Solid displacement that differs from the
        // accepted Plan placement; direct Solid edits return to the baseline.
        var solidTranslation = beforeSolidCenter.HasValue
            ? Subtract(acceptedPlanDelta, solidDelta)
            : new RoofPoint3D(0d, 0d, 0d);
        plan = new RoofOrdinaryLogicalMovePlan(
            initiator, nativeDelta, lineTranslation, solidTranslation,
            acceptedPlanDelta, lineMoved, solidMoved);
        return true;
    }

    private static RoofPoint3D Subtract(RoofPoint3D a, RoofPoint3D b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static double Length(RoofPoint3D value) =>
        Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);

    private static bool Finite(RoofPoint3D point) =>
        !double.IsNaN(point.X) && !double.IsInfinity(point.X) &&
        !double.IsNaN(point.Y) && !double.IsInfinity(point.Y) &&
        !double.IsNaN(point.Z) && !double.IsInfinity(point.Z);
}

public enum RoofOrdinaryMoveInitiator { Plan2D, Physical3D }

public sealed record RoofOrdinaryLogicalMovePlan(
    RoofOrdinaryMoveInitiator Initiator,
    RoofPoint3D NativeDelta,
    RoofPoint3D LineTranslation,
    RoofPoint3D SolidTranslation,
    RoofPoint3D AnnotationTranslation,
    bool LineNativeMoved,
    bool SolidNativeMoved);
