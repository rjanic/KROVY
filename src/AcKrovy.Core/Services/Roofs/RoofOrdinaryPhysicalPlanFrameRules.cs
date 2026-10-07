using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Compares an ordinary Plan2D axis with the Physical3D prism longitudinal frame.
/// Used to distinguish legitimate Plan rotation from Physical3D frame corruption.
/// </summary>
public static class RoofOrdinaryPhysicalPlanFrameRules
{
    public const double DirectionTolerance = 1e-5;

    public sealed record FrameCorrespondence(
        double PlanDirectionX,
        double PlanDirectionY,
        double PhysicalDirectionX,
        double PhysicalDirectionY,
        double SideX,
        double SideY,
        double PitchDegrees,
        bool AxesAligned,
        bool SectionOrthogonalToPlan,
        bool ExtraYawRelativeToPlan)
    {
        public bool IsFaithful => AxesAligned && SectionOrthogonalToPlan && !ExtraYawRelativeToPlan;
    }

    public static bool TryDescribe(
        RoofSegment3D planAxis,
        IReadOnlyList<RoofPoint3D> solidVertices,
        double widthMm,
        out FrameCorrespondence? correspondence,
        out string failureReason)
    {
        correspondence = null;
        failureReason = "InvalidFrameInput";
        if (solidVertices is not { Count: >= 4 } ||
            !Finite(planAxis.Start) || !Finite(planAxis.End) ||
            !Finite(widthMm) || widthMm <= DirectionTolerance)
            return false;

        var planDx = planAxis.End.X - planAxis.Start.X;
        var planDy = planAxis.End.Y - planAxis.Start.Y;
        var planLength = Math.Sqrt(planDx * planDx + planDy * planDy);
        if (!Finite(planLength) || planLength <= DirectionTolerance)
        {
            failureReason = "PlanDirectionDegenerate";
            return false;
        }

        planDx /= planLength;
        planDy /= planLength;

        // Upper face longitudinal edges: 0->2 and 1->3 in the shared prism convention.
        var physicalDx = ((solidVertices[2].X - solidVertices[0].X) +
                          (solidVertices[3].X - solidVertices[1].X)) / 2d;
        var physicalDy = ((solidVertices[2].Y - solidVertices[0].Y) +
                          (solidVertices[3].Y - solidVertices[1].Y)) / 2d;
        var physicalDz = ((solidVertices[2].Z - solidVertices[0].Z) +
                          (solidVertices[3].Z - solidVertices[1].Z)) / 2d;
        var physicalPlanLength = Math.Sqrt(physicalDx * physicalDx + physicalDy * physicalDy);
        if (!Finite(physicalPlanLength) || physicalPlanLength <= DirectionTolerance)
        {
            failureReason = "PhysicalDirectionDegenerate";
            return false;
        }

        var physX = physicalDx / physicalPlanLength;
        var physY = physicalDy / physicalPlanLength;
        var aligned = Math.Abs(planDx * physX + planDy * physY) >= 1d - DirectionTolerance ||
                      Math.Abs(planDx * -physX + planDy * -physY) >= 1d - DirectionTolerance;

        var sideX = ((solidVertices[1].X - solidVertices[0].X) +
                     (solidVertices[3].X - solidVertices[2].X)) / 2d;
        var sideY = ((solidVertices[1].Y - solidVertices[0].Y) +
                     (solidVertices[3].Y - solidVertices[2].Y)) / 2d;
        var sideZ = ((solidVertices[1].Z - solidVertices[0].Z) +
                     (solidVertices[3].Z - solidVertices[2].Z)) / 2d;
        var sideLength = Math.Sqrt(sideX * sideX + sideY * sideY);
        if (!Finite(sideLength) || sideLength <= DirectionTolerance)
        {
            failureReason = "SectionSideDegenerate";
            return false;
        }

        sideX /= sideLength;
        sideY /= sideLength;
        // Clipped structural ends can shrink one transverse edge; compare direction only.
        // Roof-plane cross-section perpendicularity is a 3D condition. Its XY
        // projection need not be perpendicular to Plan after a freeform yaw.
        var trueLength = Math.Sqrt(physicalPlanLength * physicalPlanLength + physicalDz * physicalDz);
        var trueWidth = Math.Sqrt(sideLength * sideLength + sideZ * sideZ);
        var sectionOrthogonal = Math.Abs((physicalDx * sideX * sideLength + physicalDy * sideY * sideLength +
            physicalDz * sideZ) / (trueLength * trueWidth)) <= 1e-3;
        var pitchDegrees = Math.Atan2(physicalDz, physicalPlanLength) * 180d / Math.PI;
        var extraYaw = !aligned;
        correspondence = new FrameCorrespondence(
            planDx, planDy, physX, physY, sideX, sideY, pitchDegrees,
            aligned, sectionOrthogonal, extraYaw);
        failureReason = string.Empty;
        return true;
    }

    private static bool Finite(RoofPoint3D point) =>
        Finite(point.X) && Finite(point.Y) && Finite(point.Z);

    private static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
