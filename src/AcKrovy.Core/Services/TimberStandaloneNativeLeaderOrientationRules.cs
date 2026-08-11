namespace AcKrovy.Core.Services;

/// <summary>
/// Standalone Plain / DimensionsLeader / framed ItemOnly orientation only.
/// Must not be used by R3 Combined BlockContent production.
/// <para>
/// Two related angles (CREATE, source ROTATE, GRIP-STRETCH, COPY→ROTATE):
/// 1) <see cref="ResolveTransformRadians"/> — leader/frame geometry via
///    <c>OrientAroundAnchor</c>. Readable half-plane fold only; exact ±90°
///    stay directed so 90° vs 270° geometry can differ.
/// 2) <see cref="ResolveTextPresentationRadians"/> — Plain / Dimensions
///    <c>MText.Rotation</c>. Exact 90° and 270° share one BOTTOM→TOP
///    presentation (<c>+π/2</c>). Non-vertical angles match geometry transform
///    unchanged.
/// 3) <see cref="ResolveFramedItemOnlyBlockRotationRadians"/> — framed ItemOnly
///    absolute <c>BlockRotation</c> only (= text presentation + π base) so
///    ITEM_NO matches Plain. Host source ROTATE/STRETCH must erase+CREATE with
///    this absolute value (not in-place rewrite after AutoCAD TransformBy).
///    Not used by R3 Combined TransformBy path.
/// </para>
/// </summary>
public static class TimberStandaloneNativeLeaderOrientationRules
{
    public const double AngleToleranceRadians =
        TimberItemLeaderLayoutCalculator.AngleToleranceRadians;

    /// <summary>
    /// Canonical vertical text presentation: BOTTOM→TOP (CREATE 90° PASS).
    /// Exact 270°/−90° must converge here too so readability does not flip.
    /// </summary>
    public const double CanonicalVerticalTextPresentationRadians = Math.PI / 2d;

    /// <summary>
    /// Leader/frame geometry angle from physical Start→End.
    /// Non-vertical behavior is the shared readable fold only — do not alter it.
    /// Exact ±90° remain directed (90°≠270° geometry).
    /// </summary>
    public static double ResolveTransformRadians(double physicalSourceAxisRadians)
    {
        if (double.IsNaN(physicalSourceAxisRadians) ||
            double.IsInfinity(physicalSourceAxisRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(physicalSourceAxisRadians));
        }

        return TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
            physicalSourceAxisRadians);
    }

    /// <summary>
    /// Absolute MText.Rotation presentation from physical Start→End
    /// (Plain ItemOnly / DimensionsOnly). Vertical-only rule: 90° and 270°
    /// both present at <see cref="CanonicalVerticalTextPresentationRadians"/>
    /// (BOTTOM→TOP). All non-vertical angles equal
    /// <see cref="ResolveTransformRadians"/>. Framed ItemOnly BlockRotation
    /// uses <see cref="ResolveFramedItemOnlyBlockRotationRadians"/> instead.
    /// </summary>
    public static double ResolveTextPresentationRadians(
        double physicalSourceAxisRadians)
    {
        if (double.IsNaN(physicalSourceAxisRadians) ||
            double.IsInfinity(physicalSourceAxisRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(physicalSourceAxisRadians));
        }

        var physical = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            physicalSourceAxisRadians);
        if (IsExactVertical(physical))
        {
            return CanonicalVerticalTextPresentationRadians;
        }

        return ResolveTransformRadians(physical);
    }

    /// <summary>
    /// Framed ItemOnly BlockContent only. ITEM_NO AttrDefs are authored at
    /// AttrDef.Rotation = 0 in block-local space; absolute
    /// <c>MLeader.BlockRotation</c> then reads π opposite native MText baseline
    /// used by Plain ItemOnly. Constant base correction aligns framed ITEM_NO
    /// with Plain for the same source. Must not be used by Plain, DimensionsOnly,
    /// or R3 Combined (TransformBy path).
    /// </summary>
    public const double FramedItemOnlyBlockContentBaseCorrectionRadians = Math.PI;

    /// <summary>
    /// Absolute <c>BlockRotation</c> for standalone framed ItemOnly Circle /
    /// Rectangle / Slot so ITEM_NO matches Plain ItemOnly directed text.
    /// </summary>
    public static double ResolveFramedItemOnlyBlockRotationRadians(
        double physicalSourceAxisRadians) =>
        TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            ResolveTextPresentationRadians(physicalSourceAxisRadians) +
            FramedItemOnlyBlockContentBaseCorrectionRadians);

    /// <summary>
    /// True when framed BlockRotation matches Plain text presentation + π base.
    /// </summary>
    public static bool FramedItemOnlyMatchesPlainTextOrientation(
        double physicalSourceAxisRadians,
        double framedBlockRotationRadians,
        double plainMTextRotationRadians)
    {
        var expectedFramed =
            ResolveFramedItemOnlyBlockRotationRadians(physicalSourceAxisRadians);
        var expectedPlain =
            ResolveTextPresentationRadians(physicalSourceAxisRadians);
        var framed = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            framedBlockRotationRadians);
        var plain = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            plainMTextRotationRadians);
        return Math.Abs(
                   TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                       framed - expectedFramed)) <=
               AngleToleranceRadians &&
               Math.Abs(
                   TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                       plain - expectedPlain)) <=
               AngleToleranceRadians &&
               Math.Abs(
                   TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                       framed - plain - FramedItemOnlyBlockContentBaseCorrectionRadians)) <=
               AngleToleranceRadians;
    }

    public static bool IsExactVertical(double physicalSourceAxisRadians)
    {
        var physical = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            physicalSourceAxisRadians);
        return Math.Abs(Math.Abs(physical) - (Math.PI / 2d)) <= AngleToleranceRadians;
    }

    public static bool IsExactOneEighty(double physicalSourceAxisRadians)
    {
        var physical = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            physicalSourceAxisRadians);
        return Math.Abs(Math.Abs(physical) - Math.PI) <= AngleToleranceRadians;
    }

    /// <summary>
    /// True when text presentation already matches the canonical readable pose.
    /// </summary>
    public static bool IsCanonicalTextPresentation(
        double physicalSourceAxisRadians,
        double presentationRadians)
    {
        var expected = ResolveTextPresentationRadians(physicalSourceAxisRadians);
        var actual = TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
            presentationRadians);
        return Math.Abs(
                   TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                       actual - expected)) <=
               AngleToleranceRadians;
    }

    /// <summary>
    /// Backward-compatible alias for text presentation checks.
    /// </summary>
    public static bool IsCanonicalPresentation(
        double physicalSourceAxisRadians,
        double presentationRadians) =>
        IsCanonicalTextPresentation(
            physicalSourceAxisRadians,
            presentationRadians);
}
