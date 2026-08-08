using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Production G5 Combined create/refresh placement contract.
/// Create and geometry-rebuild share one canonical layout calculator; refresh
/// must not invent a second placement math or move Anchor via manual offset.
/// Default local Right applies only to new create / legitimate recreate — never
/// to an existing placement that refresh preserves in place.
/// </summary>
public static class TimberFramedCombinedG5RefreshPlacementRules
{
    public const double AttachmentToleranceMm = 0.5d;

    public const double ScaleTolerance = 1e-6d;

    public const double PlacementToleranceMm = 1e-6d;

    /// <summary>
    /// New G5 Combined create (and legitimate recreate) always places the
    /// annotation on the local Right of the readable source axis. World ±X /
    /// Start→End screen direction must not choose Left vs Right.
    /// </summary>
    public static TimberLeaderHorizontalSide DefaultCreateSide =>
        TimberLeaderHorizontalSide.Right;

    /// <summary>
    /// Manual offset may adjust TextLocation/Knee bookkeeping only.
    /// Attachment/Anchor is owned by source geometry + canonical Create.
    /// </summary>
    public static bool ManualOffsetMayMoveAnchor => false;

    /// <summary>
    /// When live attachment and block scale still match the canonical create
    /// inputs, refresh must keep the existing MLeader and update content only.
    /// </summary>
    public static bool ShouldPreserveExistingPlacement(
        double liveAttachmentX,
        double liveAttachmentY,
        double canonicalAttachmentX,
        double canonicalAttachmentY,
        double liveBlockScale,
        double canonicalBlockScale,
        double attachmentToleranceMm = AttachmentToleranceMm,
        double scaleTolerance = ScaleTolerance)
    {
        var dx = liveAttachmentX - canonicalAttachmentX;
        var dy = liveAttachmentY - canonicalAttachmentY;
        var attachmentDrift = Math.Sqrt((dx * dx) + (dy * dy));
        if (attachmentDrift > attachmentToleranceMm)
        {
            return false;
        }

        return Math.Abs(liveBlockScale - canonicalBlockScale) <= scaleTolerance;
    }

    /// <summary>
    /// Same canonical layout inputs must stay geometrically idempotent across
    /// create → refresh → refresh (no attachment/knee/landing drift).
    /// </summary>
    public static bool LayoutsMatch(
        TimberFramedBlockContentLayout first,
        TimberFramedBlockContentLayout second,
        double toleranceMm = PlacementToleranceMm)
    {
        if (first is null)
        {
            throw new ArgumentNullException(nameof(first));
        }

        if (second is null)
        {
            throw new ArgumentNullException(nameof(second));
        }

        return PointsMatch(first.AttachmentLocal, second.AttachmentLocal, toleranceMm) &&
               PointsMatch(first.KneeLocal, second.KneeLocal, toleranceMm) &&
               PointsMatch(first.LandingEndLocal, second.LandingEndLocal, toleranceMm) &&
               Math.Abs(first.ReadableAngleRadians - second.ReadableAngleRadians) <=
                   1e-12d &&
               first.Side == second.Side;
    }

    public static TimberFramedBlockContentLayout CalculateCanonical(
        double attachmentX,
        double attachmentY,
        double elementAxisRadians,
        TimberLeaderHorizontalSide side,
        TimberFramedBlockContentKind contentKind,
        double frameWidthMm,
        double frameHeightMm,
        int annotationScaleDenominator,
        double itemPaperHeightMm,
        double dimensionPaperHeightMm,
        double firstSegmentLengthModelMm,
        double landingLengthModelMm,
        double dimensionColumnEnvelopeWidthMm,
        TimberFramedBlockContentDimensionColumnSide dimensionColumnSide) =>
        TimberFramedBlockContentLayoutCalculator.Calculate(
            new TimberFramedBlockContentLayoutRequest(
                attachmentX,
                attachmentY,
                elementAxisRadians,
                side,
                contentKind,
                frameWidthMm,
                frameHeightMm,
                annotationScaleDenominator,
                itemPaperHeightMm,
                dimensionPaperHeightMm,
                firstSegmentLengthModelMm,
                landingLengthModelMm,
                dimensionColumnEnvelopeWidthMm,
             TimberFramedBlockContentPresentation.Combined,
             dimensionColumnSide));

    /// <summary>
    /// Resolve the relative BlockRotation correction after an in-place content
    /// refresh. World presentation is measured before and after BTR/AttrRef
    /// updates because BlockRotation is relative to the live CREATE TransformBy
    /// basis. With no source-axis edit, desired world presentation is exactly
    /// the pre-refresh value and <see cref="R3RefreshPresentationDecision.PresentationRefreshDelta"/>
    /// is zero. A true source rotation remains delegated to
    /// <see cref="TimberFramedCombinedG5SourceRotationRules"/>.
    /// </summary>
    public static R3RefreshPresentationDecision ResolveContentOnlyRefreshPresentation(
        double sourceRotationBeforeRadians,
        double sourceRotationAfterRadians,
        double presentationBeforeRefreshRadians,
        double presentationAfterContentUpdateRadians,
        double blockRotationAfterContentUpdateRadians)
    {
        ValidateFinite(sourceRotationBeforeRadians, nameof(sourceRotationBeforeRadians));
        ValidateFinite(sourceRotationAfterRadians, nameof(sourceRotationAfterRadians));
        ValidateFinite(
            presentationBeforeRefreshRadians,
            nameof(presentationBeforeRefreshRadians));
        ValidateFinite(
            presentationAfterContentUpdateRadians,
            nameof(presentationAfterContentUpdateRadians));
        ValidateFinite(
            blockRotationAfterContentUpdateRadians,
            nameof(blockRotationAfterContentUpdateRadians));

        var before = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            presentationBeforeRefreshRadians);
        var afterContent = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            presentationAfterContentUpdateRadians);
        var currentBlock = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            blockRotationAfterContentUpdateRadians);
        var sourceRotationChanged =
            TimberFramedCombinedG5SourceRotationRules.RotationChanged(
                sourceRotationBeforeRadians,
                sourceRotationAfterRadians);
        var desired =
            TimberFramedCombinedG5SourceRotationRules
                .ResolveRefreshPresentationRadians(
                    sourceRotationBeforeRadians,
                    sourceRotationAfterRadians,
                    before);
        var correction = TimberAnnotationReadabilityRules.NormalizeAngleDelta(
            desired - afterContent);
        var targetBlock = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            currentBlock + correction);
        var finalWorld = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            afterContent + correction);
        var refreshDelta = TimberAnnotationReadabilityRules.NormalizeAngleDelta(
            finalWorld - before);

        return new R3RefreshPresentationDecision(
            PresentationBeforeRefresh: before,
            PresentationAfterContentUpdate: afterContent,
            DesiredWorldPresentation: desired,
            BlockRotationAfterContentUpdate: currentBlock,
            BlockRotationCorrection: correction,
            TargetBlockRotation: targetBlock,
            PresentationAfterRefresh: finalWorld,
            PresentationRefreshDelta: refreshDelta,
            SourceRotationChanged: sourceRotationChanged);
    }

    private static bool PointsMatch(
        TimberPlanarPoint a,
        TimberPlanarPoint b,
        double toleranceMm)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy)) <= toleranceMm;
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

/// <summary>
/// World-space before/after proof plus the relative BlockRotation required to
/// preserve R3 presentation through a content-only refresh.
/// </summary>
public sealed record R3RefreshPresentationDecision(
    double PresentationBeforeRefresh,
    double PresentationAfterContentUpdate,
    double DesiredWorldPresentation,
    double BlockRotationAfterContentUpdate,
    double BlockRotationCorrection,
    double TargetBlockRotation,
    double PresentationAfterRefresh,
    double PresentationRefreshDelta,
    bool SourceRotationChanged);
