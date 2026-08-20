using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Deterministic TRIM/BREAK fragment identity: the handle present in the
/// pre-command snapshot remains the generated child; any other live fragment
/// of the same logical key becomes standalone timber.
/// </summary>
public static class RoofGeneratedMemberSplitRules
{
    public static bool IsSnapshotGeneratedHandle(
        string handle,
        IReadOnlyCollection<string> snapshotHandles) =>
        !string.IsNullOrWhiteSpace(handle) &&
        snapshotHandles is not null &&
        snapshotHandles.Contains(handle, StringComparer.OrdinalIgnoreCase);

    public static bool IsCollinearFragment(
        RoofGeneratedMemberGeometry parent,
        RoofGeneratedMemberGeometry fragment,
        double toleranceMm)
    {
        if (fragment.LengthMm <= toleranceMm ||
            parent.LengthMm <= toleranceMm ||
            fragment.LengthMm >= parent.LengthMm - toleranceMm)
        {
            return false;
        }

        return PointOnSegment(parent, fragment.Start, toleranceMm) &&
               PointOnSegment(parent, fragment.End, toleranceMm);
    }

    public static bool PointOnSegment(
        RoofGeneratedMemberGeometry segment,
        RoofPoint3D point,
        double toleranceMm)
    {
        var along = Subtract(segment.End, segment.Start);
        var length = Length(along);
        if (length <= toleranceMm)
        {
            return false;
        }

        var axisU = Scale(along, 1d / length);
        var delta = Subtract(point, segment.Start);
        var alongMm = Dot(delta, axisU);
        var rejection = Subtract(delta, Scale(axisU, alongMm));
        return Length(rejection) <= toleranceMm &&
               alongMm >= -toleranceMm &&
               alongMm <= length + toleranceMm;
    }

    private static RoofPoint3D Subtract(RoofPoint3D left, RoofPoint3D right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static RoofPoint3D Scale(RoofPoint3D vector, double scalar) =>
        new(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);

    private static double Dot(RoofPoint3D left, RoofPoint3D right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static double Length(RoofPoint3D vector) =>
        Math.Sqrt(Dot(vector, vector));
}
