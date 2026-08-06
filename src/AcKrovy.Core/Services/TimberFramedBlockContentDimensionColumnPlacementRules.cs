using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// CAD-neutral world-space Combined WIDTH/HEIGHT column placement.
/// Visual authority: knee K → dimension column center D → ITEM_NO center I.
/// DIMNX/DIMPX names are implementation variants only.
/// </summary>
public static class TimberFramedBlockContentDimensionColumnPlacementRules
{
    /// <summary>
    /// Minimum inset along K→I so D is not treated as coincident with an endpoint.
    /// Matches host placement noise used by create-verify (~1 mm model).
    /// </summary>
    public const double DefaultEndpointInsetMm = 1.0d;

    /// <summary>
    /// Allowed perpendicular offset of D from the K→I line. Frozen WIDTH/HEIGHT
    /// average should lie on the landing axis; 2 mm covers host AttrRef noise
    /// without accepting the mirrored far-side column.
    /// </summary>
    public const double DefaultColumnPerpendicularToleranceMm = 2.0d;

    /// <summary>
    /// Evaluate whether D lies between K and I along the knee→frame segment.
    /// </summary>
    public static TimberFramedBlockContentDimensionColumnPlacementEvaluation
        EvaluateDimensionColumnPlacement(
            TimberPlanarPoint knee,
            TimberPlanarPoint itemCenter,
            TimberPlanarPoint widthAlignment,
            TimberPlanarPoint heightAlignment,
            double endpointInsetMm = DefaultEndpointInsetMm,
            double columnPerpendicularToleranceMm =
                DefaultColumnPerpendicularToleranceMm)
    {
        var dimensionColumnCenter = new TimberPlanarPoint(
            (widthAlignment.X + heightAlignment.X) * 0.5d,
            (widthAlignment.Y + heightAlignment.Y) * 0.5d);
        return EvaluateDimensionColumnPlacement(
            knee,
            itemCenter,
            dimensionColumnCenter,
            endpointInsetMm,
            columnPerpendicularToleranceMm);
    }

    public static TimberFramedBlockContentDimensionColumnPlacementEvaluation
        EvaluateDimensionColumnPlacement(
            TimberPlanarPoint knee,
            TimberPlanarPoint itemCenter,
            TimberPlanarPoint dimensionColumnCenter,
            double endpointInsetMm = DefaultEndpointInsetMm,
            double columnPerpendicularToleranceMm =
                DefaultColumnPerpendicularToleranceMm)
    {
        if (!IsFinitePoint(knee) ||
            !IsFinitePoint(itemCenter) ||
            !IsFinitePoint(dimensionColumnCenter) ||
            !IsFiniteNonNegative(endpointInsetMm) ||
            !IsFiniteNonNegative(columnPerpendicularToleranceMm))
        {
            return Fail(
                knee,
                itemCenter,
                dimensionColumnCenter,
                parameterT: double.NaN,
                perpendicularDistance: double.NaN,
                "Non-finite placement inputs.");
        }

        var kiX = itemCenter.X - knee.X;
        var kiY = itemCenter.Y - knee.Y;
        var kiLengthSquared = (kiX * kiX) + (kiY * kiY);
        var kiLength = Math.Sqrt(kiLengthSquared);
        if (kiLength <= TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            return Fail(
                knee,
                itemCenter,
                dimensionColumnCenter,
                parameterT: double.NaN,
                perpendicularDistance: double.NaN,
                "Degenerate knee-to-frame (ITEM_NO) segment.");
        }

        var kdX = dimensionColumnCenter.X - knee.X;
        var kdY = dimensionColumnCenter.Y - knee.Y;
        var parameterT = ((kdX * kiX) + (kdY * kiY)) / kiLengthSquared;
        var projectedX = knee.X + (parameterT * kiX);
        var projectedY = knee.Y + (parameterT * kiY);
        var perpDx = dimensionColumnCenter.X - projectedX;
        var perpDy = dimensionColumnCenter.Y - projectedY;
        var perpendicularDistance = Math.Sqrt((perpDx * perpDx) + (perpDy * perpDy));

        var lowerT = endpointInsetMm / kiLength;
        var upperT = 1d - (endpointInsetMm / kiLength);
        if (upperT <= lowerT)
        {
            return Fail(
                knee,
                itemCenter,
                dimensionColumnCenter,
                parameterT,
                perpendicularDistance,
                "Endpoint inset exceeds knee-to-frame length.");
        }

        if (parameterT <= lowerT)
        {
            return new TimberFramedBlockContentDimensionColumnPlacementEvaluation(
                IsCorrect: false,
                parameterT,
                perpendicularDistance,
                knee,
                itemCenter,
                dimensionColumnCenter,
                "Dimension column is not past the knee toward the frame.");
        }

        if (parameterT >= upperT)
        {
            return new TimberFramedBlockContentDimensionColumnPlacementEvaluation(
                IsCorrect: false,
                parameterT,
                perpendicularDistance,
                knee,
                itemCenter,
                dimensionColumnCenter,
                "Dimension column is not between knee and ITEM_NO (past frame or on it).");
        }

        if (perpendicularDistance > columnPerpendicularToleranceMm)
        {
            return new TimberFramedBlockContentDimensionColumnPlacementEvaluation(
                IsCorrect: false,
                parameterT,
                perpendicularDistance,
                knee,
                itemCenter,
                dimensionColumnCenter,
                "Dimension column is too far off the knee→ITEM_NO line.");
        }

        return new TimberFramedBlockContentDimensionColumnPlacementEvaluation(
            IsCorrect: true,
            parameterT,
            perpendicularDistance,
            knee,
            itemCenter,
            dimensionColumnCenter,
            "K→D→I satisfied.");
    }

    /// <summary>
    /// Mirror D about ITEM_NO (frame center): mirroredD = I + (I − D).
    /// Choose no-op / swap / fail from current vs mirrored correctness.
    /// </summary>
    public static TimberFramedBlockContentDimensionColumnMirrorEvaluation
        EvaluateMirroredDimensionColumnPlacement(
            TimberPlanarPoint knee,
            TimberPlanarPoint itemCenter,
            TimberPlanarPoint widthAlignment,
            TimberPlanarPoint heightAlignment,
            double endpointInsetMm = DefaultEndpointInsetMm,
            double columnPerpendicularToleranceMm =
                DefaultColumnPerpendicularToleranceMm)
    {
        var currentCenter = new TimberPlanarPoint(
            (widthAlignment.X + heightAlignment.X) * 0.5d,
            (widthAlignment.Y + heightAlignment.Y) * 0.5d);
        var mirroredCenter = MirrorAboutItem(itemCenter, currentCenter);

        var current = EvaluateDimensionColumnPlacement(
            knee,
            itemCenter,
            currentCenter,
            endpointInsetMm,
            columnPerpendicularToleranceMm);
        var mirrored = EvaluateDimensionColumnPlacement(
            knee,
            itemCenter,
            mirroredCenter,
            endpointInsetMm,
            columnPerpendicularToleranceMm);

        TimberFramedBlockContentDimensionColumnMirrorDecision decision;
        if (!current.IsCorrect &&
            string.Equals(
                current.Reason,
                "Degenerate knee-to-frame (ITEM_NO) segment.",
                StringComparison.Ordinal))
        {
            decision = TimberFramedBlockContentDimensionColumnMirrorDecision
                .FailDegenerate;
        }
        else if (current.IsCorrect && !mirrored.IsCorrect)
        {
            decision = TimberFramedBlockContentDimensionColumnMirrorDecision.NoOp;
        }
        else if (!current.IsCorrect && mirrored.IsCorrect)
        {
            decision = TimberFramedBlockContentDimensionColumnMirrorDecision.Swap;
        }
        else if (current.IsCorrect && mirrored.IsCorrect)
        {
            decision = TimberFramedBlockContentDimensionColumnMirrorDecision
                .FailAmbiguous;
        }
        else
        {
            decision = TimberFramedBlockContentDimensionColumnMirrorDecision
                .FailUnresolved;
        }

        return new TimberFramedBlockContentDimensionColumnMirrorEvaluation(
            decision,
            current,
            mirrored,
            mirroredCenter);
    }

    public static TimberPlanarPoint MirrorAboutItem(
        TimberPlanarPoint itemCenter,
        TimberPlanarPoint dimensionColumnCenter) =>
        new(
            itemCenter.X + (itemCenter.X - dimensionColumnCenter.X),
            itemCenter.Y + (itemCenter.Y - dimensionColumnCenter.Y));

    public static string DescribeDecision(
        TimberFramedBlockContentDimensionColumnMirrorDecision decision) =>
        decision switch
        {
            TimberFramedBlockContentDimensionColumnMirrorDecision.NoOp => "no-op",
            TimberFramedBlockContentDimensionColumnMirrorDecision.Swap => "swap",
            TimberFramedBlockContentDimensionColumnMirrorDecision.FailAmbiguous =>
                "fail-ambiguous",
            TimberFramedBlockContentDimensionColumnMirrorDecision.FailUnresolved =>
                "fail-unresolved",
            TimberFramedBlockContentDimensionColumnMirrorDecision.FailDegenerate =>
                "fail-degenerate",
            _ => "fail",
        };

    private static TimberFramedBlockContentDimensionColumnPlacementEvaluation Fail(
        TimberPlanarPoint knee,
        TimberPlanarPoint itemCenter,
        TimberPlanarPoint dimensionColumnCenter,
        double parameterT,
        double perpendicularDistance,
        string reason) =>
        new(
            IsCorrect: false,
            parameterT,
            perpendicularDistance,
            knee,
            itemCenter,
            dimensionColumnCenter,
            reason);

    private static bool IsFinitePoint(TimberPlanarPoint point) =>
        !(double.IsNaN(point.X) ||
          double.IsInfinity(point.X) ||
          double.IsNaN(point.Y) ||
          double.IsInfinity(point.Y));

    private static bool IsFiniteNonNegative(double value) =>
        !(double.IsNaN(value) || double.IsInfinity(value) || value < 0d);
}
