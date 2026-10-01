using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public static class RoofAttachedManualRelativeGeometryRules
{
    /// <summary>
    /// Re-expresses the same physical points after an anchor reverses direction.
    /// For U'=-U, V'=-V, W'=W and the new origin at the old anchor end:
    /// u'=L-u, v'=-v, w'=w.
    /// </summary>
    public static RoofAttachedManualRelativeSegment RebaseForReversedAnchorDirection(
        RoofAttachedManualRelativeSegment relative,
        double anchorLengthMm)
    {
        if (relative is null)
        {
            throw new ArgumentNullException(nameof(relative));
        }
        if (double.IsNaN(anchorLengthMm) ||
            double.IsInfinity(anchorLengthMm) ||
            anchorLengthMm <= RoofGeneratedMemberOverrideMath.LengthToleranceMm)
        {
            throw new ArgumentOutOfRangeException(nameof(anchorLengthMm));
        }

        return new RoofAttachedManualRelativeSegment(
            anchorLengthMm - relative.U0Mm,
            -relative.V0Mm,
            relative.W0Mm,
            anchorLengthMm - relative.U1Mm,
            -relative.V1Mm,
            relative.W1Mm);
    }

    public static bool TryCapture(
        RoofPoint3D anchorStart,
        RoofPoint3D anchorEnd,
        RoofPoint3D childStart,
        RoofPoint3D childEnd,
        out RoofAttachedManualRelativeSegment relative)
    {
        relative = default!;
        if (!TryBuildAxes(anchorStart, anchorEnd, out var origin, out var axisU, out var axisV, out var axisW))
        {
            return false;
        }

        Decompose(childStart, origin, axisU, axisV, axisW, out var u0, out var v0, out var w0);
        Decompose(childEnd, origin, axisU, axisV, axisW, out var u1, out var v1, out var w1);
        relative = new RoofAttachedManualRelativeSegment(u0, v0, w0, u1, v1, w1);
        return true;
    }

    public static bool TryReplay(
        RoofPoint3D anchorStart,
        RoofPoint3D anchorEnd,
        RoofAttachedManualRelativeSegment relative,
        out RoofPoint3D childStart,
        out RoofPoint3D childEnd)
    {
        childStart = default;
        childEnd = default;
        if (!TryBuildAxes(anchorStart, anchorEnd, out var origin, out var axisU, out var axisV, out var axisW))
        {
            return false;
        }

        childStart = Compose(origin, axisU, axisV, axisW, relative.U0Mm, relative.V0Mm, relative.W0Mm);
        childEnd = Compose(origin, axisU, axisV, axisW, relative.U1Mm, relative.V1Mm, relative.W1Mm);
        return true;
    }

    /// <summary>Recover the unchanged anchor basis from a persisted relative segment
    /// and its matching pre-command Plan2D pose when the native anchor is absent.
    /// The returned end defines the basis only; it does not invent a generated slot.</summary>
    public static bool TryRecoverAnchorBasis(RoofAttachedManualRelativeSegment relative,
        RoofPoint3D priorStart, RoofPoint3D priorEnd, out RoofPoint3D anchorStart, out RoofPoint3D anchorEnd)
    {
        anchorStart = anchorEnd = default;
        var values = new[] { relative.U0Mm, relative.V0Mm, relative.W0Mm, relative.U1Mm,
            relative.V1Mm, relative.W1Mm, priorStart.X, priorStart.Y, priorStart.Z, priorEnd.X, priorEnd.Y, priorEnd.Z };
        if (values.Any(value => double.IsNaN(value) || double.IsInfinity(value))) return false;
        var du = relative.U1Mm - relative.U0Mm;
        var dv = relative.V1Mm - relative.V0Mm;
        var dx = priorEnd.X - priorStart.X;
        var dy = priorEnd.Y - priorStart.Y;
        var localLength = Math.Sqrt(du * du + dv * dv);
        var worldLength = Math.Sqrt(dx * dx + dy * dy);
        var tolerance = RoofGeneratedMemberOverrideMath.LengthToleranceMm;
        if (localLength <= tolerance || worldLength <= tolerance ||
            Math.Abs(localLength - worldLength) > tolerance ||
            Math.Abs(priorStart.Z - relative.W0Mm - priorEnd.Z + relative.W1Mm) > tolerance) return false;
        var cosine = (du * dx + dv * dy) / (localLength * worldLength);
        var sine = (du * dy - dv * dx) / (localLength * worldLength);
        anchorStart = new(priorStart.X - relative.U0Mm * cosine + relative.V0Mm * sine,
            priorStart.Y - relative.U0Mm * sine - relative.V0Mm * cosine, priorStart.Z - relative.W0Mm);
        anchorEnd = new(anchorStart.X + cosine * localLength, anchorStart.Y + sine * localLength, anchorStart.Z);
        return true;
    }

    public static string FormatAnchorKey(RoofGeneratedMemberKey key) =>
        $"{key.MemberKind}:{key.RoofFace}:s{key.StationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static bool TryBuildAxes(
        RoofPoint3D anchorStart,
        RoofPoint3D anchorEnd,
        out RoofPoint3D origin,
        out RoofPoint3D axisU,
        out RoofPoint3D axisV,
        out RoofPoint3D axisW)
    {
        origin = anchorStart;
        axisU = default;
        axisV = default;
        axisW = RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal;
        if (!TryNormalize(Subtract(anchorEnd, anchorStart), out axisU))
        {
            return false;
        }

        axisV = Cross(axisW, axisU);
        if (!TryNormalize(axisV, out axisV))
        {
            return false;
        }

        axisU = Cross(axisV, axisW);
        return TryNormalize(axisU, out axisU);
    }

    private static void Decompose(
        RoofPoint3D point,
        RoofPoint3D origin,
        RoofPoint3D axisU,
        RoofPoint3D axisV,
        RoofPoint3D axisW,
        out double alongU,
        out double alongV,
        out double alongW)
    {
        var delta = Subtract(point, origin);
        alongU = Dot(delta, axisU);
        alongV = Dot(delta, axisV);
        alongW = Dot(delta, axisW);
    }

    private static RoofPoint3D Compose(
        RoofPoint3D origin,
        RoofPoint3D axisU,
        RoofPoint3D axisV,
        RoofPoint3D axisW,
        double alongU,
        double alongV,
        double alongW) =>
        Add(
            origin,
            Add(
                Scale(axisU, alongU),
                Add(Scale(axisV, alongV), Scale(axisW, alongW))));

    private static RoofPoint3D Subtract(RoofPoint3D left, RoofPoint3D right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static RoofPoint3D Add(RoofPoint3D left, RoofPoint3D right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static RoofPoint3D Scale(RoofPoint3D vector, double scalar) =>
        new(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);

    private static double Dot(RoofPoint3D left, RoofPoint3D right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static RoofPoint3D Cross(RoofPoint3D left, RoofPoint3D right) =>
        new(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);

    private static bool TryNormalize(RoofPoint3D vector, out RoofPoint3D normalized)
    {
        var length = Math.Sqrt(Dot(vector, vector));
        if (length <= RoofGeneratedMemberOverrideMath.LengthToleranceMm)
        {
            normalized = default;
            return false;
        }

        normalized = Scale(vector, 1d / length);
        return true;
    }
}
