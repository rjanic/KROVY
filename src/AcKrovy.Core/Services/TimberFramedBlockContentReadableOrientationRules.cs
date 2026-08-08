using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// R3 Combined content presentation/orientation (layer C). Distinct from
/// leader geometry (TransformBy / 60° / straight landing) and from
/// WIDTH/HEIGHT toward-knee R3_RIGHT/LEFT selection.
/// <para>
/// WHITE DOBRÉ contract: Knee→Dimensions→Frame outward; frame outermost;
/// text readable. Presentation may fold the physical axis by π for upright
/// glyphs, but that fold must not be applied as a naive post-hoc
/// <c>BlockRotation = NormalizeReadable</c> after R3 classify — the host
/// proved that mixes AttrDef local X with landing and puts the frame between
/// knee and dimensions (ZLÉ).
/// </para>
/// Canonical presentation half-plane is <c>[-π/2, π/2]</c>. At the exact
/// vertical boundary both directed source axes use <c>−π/2</c>. A separate
/// CREATE content-only correction may turn specific host references by 180°
/// reference. Neighbouring 89°/91° values still use the ordinary half-plane
/// fold; this is one deterministic boundary rule, not a quadrant override.
/// </summary>
public static class TimberFramedBlockContentReadableOrientationRules
{
    public const double AngleToleranceRadians = 1e-9d;

    public const double ConstructionDrawingVerticalPresentationRadians =
        -Math.PI / 2d;

    public const double ReferenceContentHalfTurnRadians = Math.PI;

    public const int ReferencePresentationRevision = 2;

    public static double SourcePhysicalAngleRadians(
        double startX,
        double startY,
        double endX,
        double endY)
    {
        var dx = endX - startX;
        var dy = endY - startY;
        if (Math.Sqrt((dx * dx) + (dy * dy)) <=
            TimberFramedBlockContentDefinitionRules.GeometryToleranceMm)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endX),
                "Source Start→End must be a non-degenerate segment.");
        }

        return Math.Atan2(dy, dx);
    }

    /// <summary>
    /// Single shared presentation decision from source physical axis
    /// (Start→End). Reverse Start/End differs by π before fold and yields the
    /// same <see cref="R3ContentOrientationDecision.PresentationAngle"/>.
    /// </summary>
    public static R3ContentOrientationDecision Decide(
        double sourcePhysicalAngleRadians)
    {
        var physical =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                sourcePhysicalAngleRadians);
        var halfPlanePresentation =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
                physical);
        var presentation = CanonicalizeVerticalBoundary(halfPlanePresentation);
        var flipped = Math.Abs(
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                presentation - physical)) > AngleToleranceRadians;
        // When presentation folds by π, landing along the physical axis points
        // opposite content local +X → R3_LEFT (+offset). Otherwise R3_RIGHT.
        var incoming = flipped
            ? TimberFramedCombinedG5ContentVariantRules.LeftColumnSide
            : TimberFramedCombinedG5ContentVariantRules.RightColumnSide;
        return new R3ContentOrientationDecision(
            PhysicalAxisAngle: physical,
            PresentationAngle: presentation,
            ReadableFlip: flipped,
            IncomingLandingSide: incoming);
    }

    public static TimberFramedBlockContentReadableOrientationSnapshot Inspect(
        double sourcePhysicalAngleRadians,
        double? rawContentRotationRadians = null)
    {
        var decision = Decide(sourcePhysicalAngleRadians);
        var rawContent = rawContentRotationRadians is double raw
            ? TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(raw)
            : decision.PhysicalAxisAngle;
        var rawDecision = Decide(rawContent);
        var presentationFromRaw = rawDecision.PresentationAngle;
        var flippedFromRaw = rawDecision.ReadableFlip;
        return new TimberFramedBlockContentReadableOrientationSnapshot(
            SourcePhysicalAngleDeg: decision.PhysicalAxisAngle * 180d / Math.PI,
            RawContentRotationDeg: rawContent * 180d / Math.PI,
            ReadableContentRotationDeg: presentationFromRaw * 180d / Math.PI,
            ReadableFlipApplied: flippedFromRaw,
            ItemTextWorldAngleDeg: presentationFromRaw * 180d / Math.PI,
            WidthTextWorldAngleDeg: presentationFromRaw * 180d / Math.PI,
            HeightTextWorldAngleDeg: presentationFromRaw * 180d / Math.PI,
            ReadableContentRotationRadians: presentationFromRaw,
            PhysicalAxisAngleDeg: decision.PhysicalAxisAngle * 180d / Math.PI,
            PresentationAngleDeg: decision.PresentationAngle * 180d / Math.PI,
            IncomingLandingSide: decision.IncomingLandingSide);
    }

    /// <summary>
    /// True when <paramref name="worldAngleDegrees"/> already lies in the
    /// canonical presentation half-plane [−90°, +90°] (physical wrap first).
    /// </summary>
    public static bool IsReadableTextAngleDegrees(double worldAngleDegrees)
    {
        if (double.IsNaN(worldAngleDegrees) ||
            double.IsInfinity(worldAngleDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(worldAngleDegrees));
        }

        var physicalDeg =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                worldAngleDegrees * Math.PI / 180d) * 180d / Math.PI;
        return physicalDeg >= -90d - 1e-9d && physicalDeg <= 90d + 1e-9d;
    }

    public static bool TextAnglesAreCoherent(
        double itemWorldAngleDeg,
        double widthWorldAngleDeg,
        double heightWorldAngleDeg,
        double toleranceDeg = 1e-6d)
    {
        var item = TimberAnnotationReadabilityRules.NormalizeAngleDelta(
            itemWorldAngleDeg * Math.PI / 180d);
        var width = TimberAnnotationReadabilityRules.NormalizeAngleDelta(
            widthWorldAngleDeg * Math.PI / 180d);
        var height = TimberAnnotationReadabilityRules.NormalizeAngleDelta(
            heightWorldAngleDeg * Math.PI / 180d);
        var tol = toleranceDeg * Math.PI / 180d;
        return Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                   width - item)) <= tol &&
               Math.Abs(TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                   height - item)) <= tol;
    }

    /// <summary>
    /// Exact R3 verticals share one construction-drawing reading direction:
    /// +90° and −90° both present at −90°. Only the exact FP-tolerant boundary
    /// is canonicalized; 89°/91° and 269°/271° retain the normal readable fold.
    /// </summary>
    public static double CanonicalizeVerticalBoundary(
        double readablePresentationRadians)
    {
        var presentation =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                readablePresentationRadians);
        if (Math.Abs(Math.Abs(presentation) - (Math.PI / 2d)) <=
            AngleToleranceRadians)
        {
            return ConstructionDrawingVerticalPresentationRadians;
        }

        return presentation;
    }

    /// <summary>
    /// Narrow host correction for CREATE content only. Exact physical +90°,
    /// -90° and 180° references rotate frame/ITEM_NO/WIDTH/HEIGHT by 180°
    /// relative to the existing R3 presentation. It must never drive TransformBy, grip
    /// presentation, side selection, leader geometry, knee or landing.
    /// </summary>
    public static R3CreateReferencePresentationDecision
        ResolveCreateReferenceFinalWorldPresentation(
            double sourcePhysicalAngleRadians,
            double currentWorldPresentationRadians,
            double currentBlockRotationRadians)
    {
        var physical =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                sourcePhysicalAngleRadians);
        var currentWorld =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                currentWorldPresentationRadians);
        var currentBlock =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                currentBlockRotationRadians);
        var appliesReferenceRule = IsReferencePresentationSource(physical);
        // Directed vertical host contract is owned by Source Start→End, not by
        // landing/readable canonicalization: +90° reads at +90°, while -90°
        // reads at -90°. The existing 180° reference retains its measured
        // one-shot half-turn behavior. Other angles remain untouched.
        var desiredWorld = IsPositiveVerticalSource(physical)
            ? -ConstructionDrawingVerticalPresentationRadians
            : IsNegativeVerticalSource(physical)
                ? ConstructionDrawingVerticalPresentationRadians
                : IsOneEightySource(physical)
                    ? TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                        currentWorld + ReferenceContentHalfTurnRadians)
                    : currentWorld;
        var correction =
            TimberAnnotationReadabilityRules.NormalizeAngleDelta(
                desiredWorld - currentWorld);
        var appliesHalfTurn = appliesReferenceRule &&
            Math.Abs(Math.Abs(correction) - ReferenceContentHalfTurnRadians) <=
                AngleToleranceRadians;
        var targetBlock =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                currentBlock + correction);
        var finalWorld =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                currentWorld + correction);

        return new R3CreateReferencePresentationDecision(
            SourcePhysicalAngle: physical,
            VerticalRuleInput: currentWorld,
            VerticalRuleOutput: desiredWorld,
            CurrentBlockRotation: currentBlock,
            BlockRotationCorrection: correction,
            TargetBlockRotation: targetBlock,
            FinalWorldPresentation: finalWorld,
            AppliesReferenceRule: appliesReferenceRule,
            AppliesHalfTurn: appliesHalfTurn);
    }

    public static bool IsReferencePresentationSource(
        double sourcePhysicalAngleRadians)
    {
        var physical =
            TimberAnnotationReadabilityRules.WrapPhysicalAngleRadians(
                sourcePhysicalAngleRadians);
        return IsPositiveVerticalSource(physical) ||
            IsNegativeVerticalSource(physical) ||
            IsOneEightySource(physical);
    }

    public static bool ShouldAdoptReferencePresentation(
        double sourcePhysicalAngleRadians,
        int currentRevision) =>
        currentRevision < ReferencePresentationRevision &&
        IsReferencePresentationSource(sourcePhysicalAngleRadians);

    private static bool IsPositiveVerticalSource(double physical) =>
        Math.Abs(physical - (Math.PI / 2d)) <= AngleToleranceRadians;

    private static bool IsNegativeVerticalSource(double physical) =>
        Math.Abs(physical + (Math.PI / 2d)) <= AngleToleranceRadians;

    private static bool IsOneEightySource(double physical) =>
        Math.Abs(Math.Abs(physical) - Math.PI) <= AngleToleranceRadians;

    /// <summary>
    /// Host-reference presentation table (degrees). Exact 90° and 270° use the
    /// R3 orientation engine; CREATE-only 90°/180° visual correction is separate.
    /// </summary>
    public static bool TryGetWhiteReferencePresentationDeg(
        double sourceAngleDeg,
        out double expectedPresentationDeg,
        out bool expectedFlip,
        out TimberFramedBlockContentDimensionColumnSide expectedIncomingSide)
    {
        expectedPresentationDeg = double.NaN;
        expectedFlip = false;
        expectedIncomingSide =
            TimberFramedCombinedG5ContentVariantRules.RightColumnSide;
        if (double.IsNaN(sourceAngleDeg) || double.IsInfinity(sourceAngleDeg))
        {
            return false;
        }

        var decision = Decide(sourceAngleDeg * Math.PI / 180d);
        expectedPresentationDeg = decision.PresentationAngle * 180d / Math.PI;
        expectedFlip = decision.ReadableFlip;
        expectedIncomingSide = decision.IncomingLandingSide;
        return true;
    }

    /// <summary>
    /// World WIDTH/HEIGHT column center after readable TransformBy, used to
    /// prove DimensionsTowardKneeDot stays &gt; 0 when readability folds by π.
    /// </summary>
    public static bool TryEvaluateCreateWorldDimensionsTowardKnee(
        TimberFramedBlockContentLayout layout,
        out double towardKneeDot,
        out TimberPlanarPoint worldKnee,
        out TimberPlanarPoint worldFrame,
        out TimberPlanarPoint worldDims)
    {
        towardKneeDot = double.NaN;
        worldKnee = default;
        worldFrame = default;
        worldDims = default;
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        if (layout.Presentation != TimberFramedBlockContentPresentation.Combined ||
            layout.WidthCenterLocal is null ||
            layout.HeightCenterLocal is null ||
            layout.FrameCenterLocal is null)
        {
            return false;
        }

        worldKnee =
            TimberFramedCombinedG5CreatePlacementRules.WorldKnee(layout);
        worldFrame =
            TimberFramedCombinedG5CreatePlacementRules.WorldBlockPosition(layout);
        var worldWidth =
            TimberFramedCombinedG5CreatePlacementRules.ToWorldAfterReadableTransform(
                layout,
                layout.WidthCenterLocal.Value);
        var worldHeight =
            TimberFramedCombinedG5CreatePlacementRules.ToWorldAfterReadableTransform(
                layout,
                layout.HeightCenterLocal.Value);
        worldDims = new TimberPlanarPoint(
            (worldWidth.X + worldHeight.X) * 0.5d,
            (worldWidth.Y + worldHeight.Y) * 0.5d);
        return TimberFramedBlockContentDefinitionRules
            .TryEvaluateDimensionsTowardKneeDot(
                worldFrame,
                worldKnee,
                worldDims,
                out towardKneeDot);
    }
}

/// <summary>
/// CREATE-only final-world presentation decision for the two host reference
/// cases. BlockRotation is deliberately a relative actuator on top of the
/// already-installed TransformBy/BTR basis, never an absolute world angle.
/// </summary>
public sealed record R3CreateReferencePresentationDecision(
    double SourcePhysicalAngle,
    double VerticalRuleInput,
    double VerticalRuleOutput,
    double CurrentBlockRotation,
    double BlockRotationCorrection,
    double TargetBlockRotation,
    double FinalWorldPresentation,
    bool AppliesReferenceRule,
    bool AppliesHalfTurn);

/// <summary>
/// Shared R3 Combined presentation decision (layer C only).
/// </summary>
public sealed record R3ContentOrientationDecision(
    double PhysicalAxisAngle,
    double PresentationAngle,
    bool ReadableFlip,
    TimberFramedBlockContentDimensionColumnSide IncomingLandingSide);

/// <summary>
/// Inspect payload for R3 Combined readable text orientation (degrees).
/// </summary>
public sealed record TimberFramedBlockContentReadableOrientationSnapshot(
    double SourcePhysicalAngleDeg,
    double RawContentRotationDeg,
    double ReadableContentRotationDeg,
    bool ReadableFlipApplied,
    double ItemTextWorldAngleDeg,
    double WidthTextWorldAngleDeg,
    double HeightTextWorldAngleDeg,
    double ReadableContentRotationRadians,
    double PhysicalAxisAngleDeg = 0d,
    double PresentationAngleDeg = 0d,
    TimberFramedBlockContentDimensionColumnSide IncomingLandingSide =
        TimberFramedBlockContentDimensionColumnSide.NegativeLocalX);
