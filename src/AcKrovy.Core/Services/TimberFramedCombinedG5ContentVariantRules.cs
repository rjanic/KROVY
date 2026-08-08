using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Production R3 Combined content-side variants: immutable R3_RIGHT / R3_LEFT
/// BTR layouts. WIDTH/HEIGHT must sit on the knee side of the frame
/// (<c>dot(D−F, K−F) &gt; 0</c>). Token names are compatibility labels for
/// knee-on-minus-X (RIGHT / −offset) vs knee-on-plus-X (LEFT / +offset) in
/// block-local AttrDef space — not world/source Left/Right. Required variant
/// is classified from final knee→frame landing projected onto the effective
/// block local +X. Text is never mirrored.
/// </summary>
public static class TimberFramedCombinedG5ContentVariantRules
{
    public const string RightToken = "RIGHT";
    public const string LeftToken = "LEFT";

    /// <summary>
    /// R3_RIGHT PASS: AttrDef column X = −offset (knee on −local X of frame).
    /// </summary>
    public static TimberFramedBlockContentDimensionColumnSide RightColumnSide =>
        TimberFramedBlockContentDimensionColumnSide.NegativeLocalX;

    /// <summary>
    /// R3_LEFT: AttrDef column X = +offset (knee on +local X of frame).
    /// </summary>
    public static TimberFramedBlockContentDimensionColumnSide LeftColumnSide =>
        TimberFramedBlockContentDimensionColumnSide.PositiveLocalX;

    public static string ToContentVariantToken(
        TimberFramedBlockContentDimensionColumnSide side) =>
        side == RightColumnSide ? RightToken : LeftToken;

    public static TimberFramedBlockContentDimensionColumnSide
        FromContentVariantToken(string? token)
    {
        if (string.Equals(token, RightToken, StringComparison.OrdinalIgnoreCase))
        {
            return RightColumnSide;
        }

        if (string.Equals(token, LeftToken, StringComparison.OrdinalIgnoreCase))
        {
            return LeftColumnSide;
        }

        throw new ArgumentOutOfRangeException(
            nameof(token),
            token,
            "Expected RIGHT or LEFT content-variant token.");
    }

    /// <summary>
    /// Provisional create default only. Final BTR selection must use
    /// <see cref="TryResolveRequiredContentVariant"/> from knee/frame landing —
    /// never treat this as layout authority.
    /// </summary>
    public static TimberFramedBlockContentDimensionColumnSide
        FromWorldSide(TimberLeaderHorizontalSide worldSide) =>
        worldSide == TimberLeaderHorizontalSide.Right
            ? RightColumnSide
            : LeftColumnSide;

    public static TimberLeaderHorizontalSide ToWorldSide(
        TimberFramedBlockContentDimensionColumnSide side) =>
        side == RightColumnSide
            ? TimberLeaderHorizontalSide.Right
            : TimberLeaderHorizontalSide.Left;

    public static TimberFramedBlockContentDimensionColumnSide Opposite(
        TimberFramedBlockContentDimensionColumnSide side) =>
        TimberFramedBlockContentVariantRules.OppositeDimensionColumnSide(side);

    /// <summary>
    /// Required R3 content variant from final knee→frame landing projected onto
    /// the effective block-local +X (AttrDef space). Independent of world L/R.
    /// </summary>
    public static bool TryResolveRequiredContentVariant(
        double kneeX,
        double kneeY,
        double frameCenterX,
        double frameCenterY,
        double effectiveLocalXAxisX,
        double effectiveLocalXAxisY,
        out TimberFramedBlockContentDimensionColumnSide requiredSide,
        out double contentLocalX,
        out double landingLength)
    {
        requiredSide = RightColumnSide;
        contentLocalX = 0d;
        landingLength = 0d;
        if (!TimberFramedBlockContentOrientationRules
                .TryClassifyRequiredDimensionColumnSide(
                    frameCenterX - kneeX,
                    frameCenterY - kneeY,
                    effectiveLocalXAxisX,
                    effectiveLocalXAxisY,
                    TimberFramedBlockContentDefinitionRules.GeometryToleranceMm,
                    out requiredSide,
                    out contentLocalX,
                    out landingLength,
                    out _))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Diagnostic world-side measure (source T/N). Does not select BTR layout.
    /// Prefer <see cref="TryResolveRequiredContentVariant"/> for WIDTH/HEIGHT.
    /// </summary>
    public static bool TryMeasureWorldSide(
        double attachmentX,
        double attachmentY,
        double contentCenterX,
        double contentCenterY,
        double startX,
        double startY,
        double endX,
        double endY,
        out TimberLeaderHorizontalSide worldSide,
        out double signedSide)
    {
        worldSide = TimberFramedCombinedG5CreatePlacementRules.DesiredWorldSide;
        signedSide = 0d;
        if (!TimberFramedCombinedG5CreatePlacementRules.TryMeasureSignedSide(
                attachmentX,
                attachmentY,
                contentCenterX,
                contentCenterY,
                startX,
                startY,
                endX,
                endY,
                out signedSide,
                out worldSide))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Obsolete world-side → token mapping kept for provisional CREATE insert.
    /// Final ensure/swap must use knee/frame overload.
    /// </summary>
    public static bool TryResolveRequiredContentVariant(
        double attachmentX,
        double attachmentY,
        double contentCenterX,
        double contentCenterY,
        double startX,
        double startY,
        double endX,
        double endY,
        out TimberFramedBlockContentDimensionColumnSide requiredSide,
        out TimberLeaderHorizontalSide worldSide,
        out double signedSide)
    {
        requiredSide = RightColumnSide;
        if (!TryMeasureWorldSide(
                attachmentX,
                attachmentY,
                contentCenterX,
                contentCenterY,
                startX,
                startY,
                endX,
                endY,
                out worldSide,
                out signedSide))
        {
            return false;
        }

        // Provisional only — same token as historic FromWorldSide mapping.
        requiredSide = FromWorldSide(worldSide);
        return true;
    }

    /// <summary>
    /// True when the live BTR content variant already matches the required side.
    /// </summary>
    public static bool IsContentVariantMatch(
        TimberFramedBlockContentDimensionColumnSide? currentSide,
        TimberFramedBlockContentDimensionColumnSide requiredSide) =>
        currentSide is TimberFramedBlockContentDimensionColumnSide live &&
        live == requiredSide;

    /// <summary>
    /// Grip side-crossing: swap only when required variant differs from current.
    /// </summary>
    public static bool ShouldSwapContentVariant(
        TimberFramedBlockContentDimensionColumnSide? currentSide,
        TimberFramedBlockContentDimensionColumnSide requiredSide) =>
        !IsContentVariantMatch(currentSide, requiredSide);

    /// <summary>
    /// Visual contract: WIDTH/HEIGHT column lies on the landing between knee
    /// and frame (K→D→I). Parameter t of dimension center along knee→item:
    /// 0 &lt; t &lt; 1. Legacy name kept; <paramref name="isOutsideBeyondFrame"/>
    /// is true when the column satisfies that on-landing contract (not past
    /// the frame).
    /// </summary>
    public static bool TryEvaluateOutsideDimensionColumn(
        TimberPlanarPoint knee,
        TimberPlanarPoint itemCenter,
        TimberPlanarPoint dimensionColumnCenter,
        out double parameterT,
        out bool isOutsideBeyondFrame) =>
        TryEvaluateLandingDimensionColumn(
            knee,
            itemCenter,
            dimensionColumnCenter,
            out parameterT,
            out isOutsideBeyondFrame);

    /// <summary>
    /// WIDTH/HEIGHT on the second landing segment between knee and ITEM_NO.
    /// </summary>
    public static bool TryEvaluateLandingDimensionColumn(
        TimberPlanarPoint knee,
        TimberPlanarPoint itemCenter,
        TimberPlanarPoint dimensionColumnCenter,
        out double parameterT,
        out bool isOnLandingBetweenKneeAndFrame)
    {
        parameterT = double.NaN;
        isOnLandingBetweenKneeAndFrame = false;
        var kiX = itemCenter.X - knee.X;
        var kiY = itemCenter.Y - knee.Y;
        var kiLengthSquared = (kiX * kiX) + (kiY * kiY);
        if (kiLengthSquared <=
            TimberFramedBlockContentDefinitionRules.GeometryToleranceMm *
            TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            return false;
        }

        var kdX = dimensionColumnCenter.X - knee.X;
        var kdY = dimensionColumnCenter.Y - knee.Y;
        parameterT = ((kdX * kiX) + (kdY * kiY)) / kiLengthSquared;
        isOnLandingBetweenKneeAndFrame = parameterT > 0d && parameterT < 1d;
        return true;
    }
}
