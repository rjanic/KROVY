using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberItemLeaderLayoutCalculator
{
    public const double TextHeightMm = TimberMainAnnotationTextRules.TextHeightMm;
    public const double FramePaddingMm =
        TimberItemLeaderBlockDefinitionRules.HorizontalPaddingMm;
    public const double MinimumCircleDiameterMm =
        TimberItemLeaderBlockDefinitionRules.CircleDiameterMm;
    public const double MinimumSlotWidthMm =
        TimberItemLeaderBlockDefinitionRules.SmallFrameWidthMm;
    public const double MinimumEnvelopeHeightMm =
        TimberItemLeaderBlockDefinitionRules.FrameHeightMm;
    public const double EnvelopeSizeStepMm = 20d;
    public const double EstimatedCharacterWidthFactor = 0.62d;
    public const double TextClearanceMm = 240d;
    public const double MinimumLeaderRunMm = 180d;
    public const double FirstSegmentAngleRadians = Math.PI / 3d;
    public const double FirstSegmentLengthMm = 360d;
    /// <summary>
    /// Standalone Plain / DimensionsLeader only: first leader segment length at
    /// annotation scale 1:50. Other scales multiply by
    /// <c>presentationScaleFactor</c> =
    /// <see cref="TimberAnnotationScaleRules.GetScaleFactor"/> (denominator / 50).
    /// Must not replace <see cref="FirstSegmentLengthMm"/> on Combined paths.
    /// Framed ItemOnly uses the canonical framed length minus
    /// <see cref="StandaloneNativeFramedItemOnlyLeaderReductionAtScale50Mm"/>.
    /// </summary>
    public const double StandaloneNativeFirstSegmentLengthMm = 250d;
    /// <summary>
    /// Standalone framed ItemOnly (Circle/Rectangle/Slot) only: model-space
    /// shortening of the straight Source→frame leader at annotation scale 1:50.
    /// Other scales multiply by <c>presentationScaleFactor</c>. Applied after the
    /// original canonical length
    /// (<see cref="FirstSegmentLengthMm"/> +
    /// <see cref="FramedLeaderAdditionalOffsetMm"/>) × scale — never an absolute
    /// 250 mm length. Must not affect Plain, Dimensions, or Combined.
    /// </summary>
    public const double StandaloneNativeFramedItemOnlyLeaderReductionAtScale50Mm = 250d;
    /// <summary>
    /// Standalone framed ItemOnly only: clamp after reduction so leader length
    /// never becomes zero or negative.
    /// </summary>
    public const double StandaloneNativeFramedItemOnlyLeaderMinimumLengthMm = 1e-6d;
    /// <summary>
    /// Standalone Plain ItemOnly only: tiny margin from the knee to the near
    /// text edge. Landing DoglegLength reaches MiddleCenter TextLocation at
    /// half envelope + this padding — keep it near-zero so the visible landing
    /// stays only slightly past the text (card-like), not a clearance overhang.
    /// Not TextClearance / MinimumLeaderRun / PlainTextClearance.
    /// </summary>
    public const double StandaloneNativeLandingPaddingMm = 1d;
    /// <summary>
    /// Standalone DimensionsLeader only: flush knee→near-edge pad (no extra
    /// overhang). Stacked digit landings use this instead of
    /// <see cref="StandaloneNativeLandingPaddingMm"/>.
    /// </summary>
    public const double StandaloneNativeDimensionsLandingPaddingMm = 0d;
    /// <summary>
    /// Standalone DimensionsLeader only: fraction of estimated envelope width
    /// for Knee→MiddleCenter. Slightly under 1/2 so AttachmentBottomLine
    /// landings stay tight when the digit-width estimate runs wide.
    /// Plain ItemOnly keeps the exact half-envelope factor (1/2).
    /// </summary>
    public const double StandaloneNativeDimensionsLandingEnvelopeFactor = 0.45d;
    /// <summary>
    /// Standalone DimensionsLeader only: model-space shortening of the second
    /// (landing) segment at annotation scale 1:50. Other scales multiply by
    /// <c>presentationScaleFactor</c> =
    /// <see cref="TimberAnnotationScaleRules.GetScaleFactor"/> (denominator / 50).
    /// Plain ItemOnly must not apply this reduction.
    /// </summary>
    public const double StandaloneNativeDimensionsLandingReductionAtScale50Mm = 250d;
    /// <summary>
    /// Standalone DimensionsLeader only: clamp after reduction so DoglegLength
    /// never becomes zero or negative.
    /// </summary>
    public const double StandaloneNativeDimensionsLandingMinimumLengthMm = 1e-6d;
    /// <summary>
    /// Standalone Plain / DimensionsLeader only: interior elbow angle when
    /// segment A is 60° from source axis T and segment B is parallel to T
    /// (180° − 60° = 120°). Not a rotate(dirA) construction angle.
    /// </summary>
    public const double SecondSegmentBendRadians = 2d * Math.PI / 3d;
    public const double FramedLeaderAdditionalOffsetMm = 350d;
    public const double FramedFirstSegmentAngleRadians = Math.PI / 3d;
    public const double FramedItemLandingDistanceMm = 0d;
    public const double CombinedFramedLandingDistanceMm = 350d;
    public const double AngleToleranceRadians = 1e-8d;

    public static TimberItemLeaderLayout Calculate(
        TimberLeaderPlacement placement,
        string itemText,
        ItemNumberLeaderStyle style,
        TimberLeaderHorizontalSide? preferredSide = null,
        double presentationScaleFactor = 1d)
    {
        if (placement is null)
        {
            throw new ArgumentNullException(nameof(placement));
        }
        if (presentationScaleFactor <= 0d ||
            double.IsNaN(presentationScaleFactor) ||
            double.IsInfinity(presentationScaleFactor))
        {
            throw new ArgumentOutOfRangeException(nameof(presentationScaleFactor));
        }

        var normalizedText = itemText?.Trim() ?? string.Empty;
        var normalizedStyle = ItemNumberLeaderStyleRules.Normalize(style);
        if (normalizedStyle is not ItemNumberLeaderStyle.Plain)
        {
            var definition =
                TimberItemLeaderBlockDefinitionRules.Resolve(
                    normalizedStyle,
                    normalizedText);
            var scaledEnvelopeWidth =
                definition.WidthMm * presentationScaleFactor;
            var scaledEnvelopeHeight =
                definition.HeightMm * presentationScaleFactor;
            var axisX = Math.Cos(placement.RotationRadians);
            var axisY = Math.Sin(placement.RotationRadians);
            var existingOffset =
                scaledEnvelopeWidth / 2d +
                TextClearanceMm * presentationScaleFactor +
                MinimumLeaderRunMm * presentationScaleFactor;
            var existingContentX = placement.TextX + axisX * existingOffset;
            var existingContentY = placement.TextY + axisY * existingOffset;
            var existingSide = existingContentX < placement.AnchorX
                ? TimberLeaderHorizontalSide.Left
                : TimberLeaderHorizontalSide.Right;
            return new TimberItemLeaderLayout(
                placement.AnchorX,
                placement.AnchorY,
                existingContentX,
                existingContentY,
                existingContentX,
                existingContentY,
                existingSide,
                scaledEnvelopeWidth,
                scaledEnvelopeHeight);
        }

        var effectiveTextHeight = normalizedStyle == ItemNumberLeaderStyle.Plain
            ? TextHeightMm * presentationScaleFactor
            : TextHeightMm;
        var estimatedTextWidth = Math.Max(
            effectiveTextHeight,
            normalizedText.Length * effectiveTextHeight * EstimatedCharacterWidthFactor);
        var paddedWidth = QuantizeUp(estimatedTextWidth + 2d * FramePaddingMm);
        var envelopeWidth = normalizedStyle switch
        {
            ItemNumberLeaderStyle.Circle => Math.Max(MinimumCircleDiameterMm, paddedWidth),
            ItemNumberLeaderStyle.Slot => Math.Max(MinimumSlotWidthMm, paddedWidth),
            _ => estimatedTextWidth,
        };
        var envelopeHeight = normalizedStyle == ItemNumberLeaderStyle.Plain
            ? effectiveTextHeight
            : Math.Max(MinimumEnvelopeHeightMm, TextHeightMm + 2d * FramePaddingMm);
        if (normalizedStyle == ItemNumberLeaderStyle.Circle)
        {
            envelopeHeight = envelopeWidth;
        }

        var side = preferredSide ?? (Math.Cos(placement.RotationRadians) < 0d
            ? TimberLeaderHorizontalSide.Left
            : TimberLeaderHorizontalSide.Right);
        var knee = CalculateKnee(
            placement.AnchorX,
            placement.AnchorY,
            side,
            FirstSegmentLengthMm * presentationScaleFactor,
            TimberLeaderPlaneBasis.WorldXY,
            TimberLeaderVerticalSide.Up);
        var horizontalDirection = side == TimberLeaderHorizontalSide.Left ? -1d : 1d;
        var alongAxisOffset =
            envelopeWidth / 2d +
            TextClearanceMm * presentationScaleFactor +
            MinimumLeaderRunMm * presentationScaleFactor;
        var contentX = placement.AnchorX + horizontalDirection * alongAxisOffset;
        var contentY = knee.Y + effectiveTextHeight / 2d;

        return new TimberItemLeaderLayout(
            placement.AnchorX,
            placement.AnchorY,
            knee.X,
            knee.Y,
            contentX,
            contentY,
            side,
            envelopeWidth,
            envelopeHeight);
    }

    public static TimberItemLeaderLayout CalculatePlainItemNumber(
        TimberLeaderPlacement placement,
        string itemText,
        TimberLeaderHorizontalSide? preferredSide = null,
        double presentationScaleFactor = 1d)
    {
        if (placement is null)
        {
            throw new ArgumentNullException(nameof(placement));
        }
        if (presentationScaleFactor <= 0d ||
            double.IsNaN(presentationScaleFactor) ||
            double.IsInfinity(presentationScaleFactor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(presentationScaleFactor));
        }

        var effectiveTextHeight =
            TimberItemNumberTypographyRules.CalculateTextHeightMm(
                presentationScaleFactor);
        var normalizedText = itemText?.Trim() ?? string.Empty;
        var envelopeWidth = Math.Max(
            effectiveTextHeight,
            normalizedText.Length *
            effectiveTextHeight *
            EstimatedCharacterWidthFactor);
        var side = preferredSide ??
            (Math.Cos(placement.RotationRadians) < 0d
                ? TimberLeaderHorizontalSide.Left
                : TimberLeaderHorizontalSide.Right);
        var knee = CalculateKnee(
            placement.AnchorX,
            placement.AnchorY,
            side,
            FirstSegmentLengthMm * presentationScaleFactor,
            TimberLeaderPlaneBasis.WorldXY,
            TimberLeaderVerticalSide.Up);

        var normalX = placement.TextX - placement.AnchorX;
        var normalY = placement.TextY - placement.AnchorY;
        var normalLength = Math.Sqrt(
            normalX * normalX +
            normalY * normalY);
        if (normalLength <= AngleToleranceRadians)
        {
            normalX = -Math.Sin(placement.RotationRadians);
            normalY = Math.Cos(placement.RotationRadians);
            normalLength = 1d;
        }
        normalX /= normalLength;
        normalY /= normalLength;

        // Native Plain item text remains horizontal. Use its projected
        // half-envelope along the source normal so every orientation keeps
        // the same edge clearance from the source axis.
        // Combined Plain keeps this pre-baked world layout (no TransformBy).
        var projectedHalfEnvelope =
            Math.Abs(normalX) * envelopeWidth / 2d +
            Math.Abs(normalY) * effectiveTextHeight / 2d;
        var centerOffset =
            projectedHalfEnvelope +
            TimberItemNumberTypographyRules.CalculatePlainTextClearanceMm(
                presentationScaleFactor);
        var contentX = placement.AnchorX + normalX * centerOffset;
        var contentY = placement.AnchorY + normalY * centerOffset;

        return new TimberItemLeaderLayout(
            placement.AnchorX,
            placement.AnchorY,
            knee.X,
            knee.Y,
            contentX,
            contentY,
            side,
            envelopeWidth,
            effectiveTextHeight);
    }

    /// <summary>
    /// Standalone DimensionsLeader (Iba rozmery) only: canonical WorldXY layout
    /// (T=+X, N=+Y). Segment A is exactly 60° from +T; segment B continues from
    /// the knee parallel to ±T (interior elbow 120°). Host finishes with absolute
    /// <see cref="OrientAroundAnchor"/> using
    /// <see cref="ResolveNativeLeaderTransformRadians"/> (physical Start→End) and
    /// absolute MText rotation — not cumulative <c>TransformBy</c>.
    /// Must not be used by Combined dimensions / R3 paths.
    /// </summary>
    public static TimberItemLeaderLayout CalculateStandaloneNativeDimensionsLeader(
        TimberLeaderPlacement placement,
        string dimensionText,
        TimberLeaderHorizontalSide? preferredSide = null,
        double presentationScaleFactor = 1d)
    {
        if (placement is null)
        {
            throw new ArgumentNullException(nameof(placement));
        }
        if (presentationScaleFactor <= 0d ||
            double.IsNaN(presentationScaleFactor) ||
            double.IsInfinity(presentationScaleFactor))
        {
            throw new ArgumentOutOfRangeException(nameof(presentationScaleFactor));
        }

        var effectiveTextHeight = TextHeightMm * presentationScaleFactor;
        var normalizedText = dimensionText?.Trim() ?? string.Empty;
        // Stacked "W\PH" uses the longest line — not the raw string with \P.
        var envelopeWidth = EstimateStandaloneTextEnvelopeWidthMm(
            normalizedText,
            effectiveTextHeight);
        var side = preferredSide ?? TimberLeaderHorizontalSide.Right;
        var firstLength =
            StandaloneNativeFirstSegmentLengthMm * presentationScaleFactor;
        var knee = CalculateKnee(
            placement.AnchorX,
            placement.AnchorY,
            side,
            firstLength,
            TimberLeaderPlaneBasis.WorldXY,
            TimberLeaderVerticalSide.Up);
        var secondLength = CalculateStandaloneNativeDimensionsLandingLengthMm(
            envelopeWidth,
            presentationScaleFactor);
        var content = CalculateStandaloneSecondSegmentContent(
            knee.X,
            knee.Y,
            placement.AnchorX,
            placement.AnchorY,
            side,
            secondLength);

        return new TimberItemLeaderLayout(
            placement.AnchorX,
            placement.AnchorY,
            knee.X,
            knee.Y,
            content.X,
            content.Y,
            side,
            envelopeWidth,
            effectiveTextHeight);
    }

    /// <summary>
    /// Standalone Plain ItemOnly only: canonical WorldXY layout (T=+X, N=+Y).
    /// Segment A is exactly 60° from +T; segment B continues from the knee
    /// parallel to ±T (interior elbow 120°). Host finishes with absolute
    /// <see cref="OrientAroundAnchor"/> using
    /// <see cref="ResolveNativeLeaderTransformRadians"/> (physical Start→End) and
    /// absolute MText rotation — not cumulative <c>TransformBy</c>.
    /// Must not be used by Combined Plain / R3 framed paths.
    /// </summary>
    public static TimberItemLeaderLayout CalculateStandaloneNativePlainItemNumber(
        TimberLeaderPlacement placement,
        string itemText,
        TimberLeaderHorizontalSide? preferredSide = null,
        double presentationScaleFactor = 1d)
    {
        if (placement is null)
        {
            throw new ArgumentNullException(nameof(placement));
        }
        if (presentationScaleFactor <= 0d ||
            double.IsNaN(presentationScaleFactor) ||
            double.IsInfinity(presentationScaleFactor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(presentationScaleFactor));
        }

        var effectiveTextHeight =
            TimberItemNumberTypographyRules.CalculateTextHeightMm(
                presentationScaleFactor);
        var normalizedText = itemText?.Trim() ?? string.Empty;
        var envelopeWidth = EstimateStandaloneTextEnvelopeWidthMm(
            normalizedText,
            effectiveTextHeight);
        var side = preferredSide ?? TimberLeaderHorizontalSide.Right;
        var firstLength =
            StandaloneNativeFirstSegmentLengthMm * presentationScaleFactor;
        var knee = CalculateKnee(
            placement.AnchorX,
            placement.AnchorY,
            side,
            firstLength,
            TimberLeaderPlaneBasis.WorldXY,
            TimberLeaderVerticalSide.Up);

        // Landing B ‖ source axis (±T from knee). Never rotate(dirA, ±120°) —
        // that puts B at −60° to T. Never Combined CalculatePlainItemNumber's
        // anchor-normal content placement. Length = half text envelope + tiny
        // near-edge pad (MiddleCenter TextLocation), not PlainTextClearance.
        var secondLength = CalculateStandaloneNativeLandingLengthMm(
            envelopeWidth,
            presentationScaleFactor);
        var content = CalculateStandaloneSecondSegmentContent(
            knee.X,
            knee.Y,
            placement.AnchorX,
            placement.AnchorY,
            side,
            secondLength);

        return new TimberItemLeaderLayout(
            placement.AnchorX,
            placement.AnchorY,
            knee.X,
            knee.Y,
            content.X,
            content.Y,
            side,
            envelopeWidth,
            effectiveTextHeight);
    }

    /// <summary>
    /// Standalone DimensionsLeader only: Knee→MiddleCenter DoglegLength.
    /// Starts from the shared envelope-factor landing, then shortens by
    /// <see cref="StandaloneNativeDimensionsLandingReductionAtScale50Mm"/> ×
    /// <paramref name="presentationScaleFactor"/> (1.0 at 1:50). Clamped to
    /// <see cref="StandaloneNativeDimensionsLandingMinimumLengthMm"/>.
    /// Must not be used by Plain ItemOnly.
    /// </summary>
    public static double CalculateStandaloneNativeDimensionsLandingLengthMm(
        double textEnvelopeWidthMm,
        double presentationScaleFactor = 1d)
    {
        var baseLanding = CalculateStandaloneNativeLandingLengthMm(
            textEnvelopeWidthMm,
            presentationScaleFactor,
            StandaloneNativeDimensionsLandingPaddingMm,
            StandaloneNativeDimensionsLandingEnvelopeFactor);

        if (presentationScaleFactor <= 0d ||
            double.IsNaN(presentationScaleFactor) ||
            double.IsInfinity(presentationScaleFactor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(presentationScaleFactor));
        }

        var reduction =
            StandaloneNativeDimensionsLandingReductionAtScale50Mm *
            presentationScaleFactor;
        return Math.Max(
            StandaloneNativeDimensionsLandingMinimumLengthMm,
            baseLanding - reduction);
    }

    /// <summary>
    /// Standalone Plain / DimensionsLeader only: DoglegLength / |Knee→Content|
    /// for MiddleCenter text on AttachmentBottomLine. Equals
    /// <paramref name="envelopeFactor"/> × text envelope plus
    /// <paramref name="landingPaddingMm"/> (knee→near-edge pad only — do not
    /// add a second half-envelope; MiddleCenter already accounts for center
    /// placement). Plain defaults: half envelope +
    /// <see cref="StandaloneNativeLandingPaddingMm"/>. Dimensions uses
    /// <see cref="StandaloneNativeDimensionsLandingEnvelopeFactor"/> +
    /// <see cref="StandaloneNativeDimensionsLandingPaddingMm"/>, then applies
    /// <see cref="CalculateStandaloneNativeDimensionsLandingLengthMm"/>
    /// reduction. Not the legacy TextClearance / MinimumLeaderRun /
    /// PlainTextClearance stack.
    /// </summary>
    public static double CalculateStandaloneNativeLandingLengthMm(
        double textEnvelopeWidthMm,
        double presentationScaleFactor = 1d,
        double landingPaddingMm = StandaloneNativeLandingPaddingMm,
        double envelopeFactor = 0.5d)
    {
        if (textEnvelopeWidthMm <= 0d ||
            double.IsNaN(textEnvelopeWidthMm) ||
            double.IsInfinity(textEnvelopeWidthMm))
        {
            throw new ArgumentOutOfRangeException(nameof(textEnvelopeWidthMm));
        }

        if (presentationScaleFactor <= 0d ||
            double.IsNaN(presentationScaleFactor) ||
            double.IsInfinity(presentationScaleFactor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(presentationScaleFactor));
        }

        if (landingPaddingMm < 0d ||
            double.IsNaN(landingPaddingMm) ||
            double.IsInfinity(landingPaddingMm))
        {
            throw new ArgumentOutOfRangeException(nameof(landingPaddingMm));
        }

        if (envelopeFactor <= 0d ||
            envelopeFactor > 0.5d ||
            double.IsNaN(envelopeFactor) ||
            double.IsInfinity(envelopeFactor))
        {
            throw new ArgumentOutOfRangeException(nameof(envelopeFactor));
        }

        // Knee → MiddleCenter: envelope factor (≤ half) + tiny near-edge pad.
        return (textEnvelopeWidthMm * envelopeFactor) +
            (landingPaddingMm * presentationScaleFactor);
    }

    /// <summary>
    /// Standalone Plain / DimensionsLeader only: place content at
    /// Knee + (±T) × length so segment B is parallel to the source axis.
    /// Right uses +T, Left uses −T. With A at 60° from T this yields interior
    /// elbow <see cref="SecondSegmentBendRadians"/> (120°). Must not rotate
    /// dirA by ±120° (that leaves B at −60° to T).
    /// </summary>
    public static (double X, double Y) CalculateStandaloneSecondSegmentContent(
        double kneeX,
        double kneeY,
        double anchorX,
        double anchorY,
        TimberLeaderHorizontalSide side,
        double secondSegmentLengthMm)
    {
        if (secondSegmentLengthMm <= 0d ||
            double.IsNaN(secondSegmentLengthMm) ||
            double.IsInfinity(secondSegmentLengthMm))
        {
            throw new ArgumentOutOfRangeException(nameof(secondSegmentLengthMm));
        }

        var firstDx = kneeX - anchorX;
        var firstDy = kneeY - anchorY;
        var firstLength = Math.Sqrt((firstDx * firstDx) + (firstDy * firstDy));
        if (firstLength <= AngleToleranceRadians ||
            double.IsNaN(firstLength) ||
            double.IsInfinity(firstLength))
        {
            throw new ArgumentOutOfRangeException(nameof(kneeX));
        }

        // Canonical WorldXY authoring: T = +X. Landing stays parallel to T —
        // never rotate(dirA, ±120°).
        var alongT = side == TimberLeaderHorizontalSide.Left ? -1d : 1d;
        return (
            kneeX + (secondSegmentLengthMm * alongT),
            kneeY);
    }

    /// <summary>
    /// Standalone Plain / DimensionsLeader only: measurable segment B for the
    /// AutoCAD MLeader dogleg — direction Knee→Content (‖ ±T after
    /// <see cref="OrientAroundAnchor"/>) and length |Content−Knee|.
    /// Host must set DoglegLength to this length and call SetDogleg; style
    /// LandingDistance remains 0 and must not be treated as the landing.
    /// </summary>
    public static (double DirX, double DirY, double LengthMm)
        ResolveStandaloneNativeLanding(
            double kneeX,
            double kneeY,
            double contentX,
            double contentY)
    {
        if (double.IsNaN(kneeX) || double.IsNaN(kneeY) ||
            double.IsNaN(contentX) || double.IsNaN(contentY) ||
            double.IsInfinity(kneeX) || double.IsInfinity(kneeY) ||
            double.IsInfinity(contentX) || double.IsInfinity(contentY))
        {
            throw new ArgumentOutOfRangeException(nameof(contentX));
        }

        var dx = contentX - kneeX;
        var dy = contentY - kneeY;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= AngleToleranceRadians)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentX),
                "Standalone landing requires Content distinct from Knee.");
        }

        return (dx / length, dy / length, length);
    }

    /// <summary>
    /// Rigid world orientation of a canonical-horizontal native leader layout
    /// around the attachment. Absolute CREATE / source-sync contract for
    /// standalone Plain ItemOnly and DimensionsLeader (not cumulative TransformBy).
    /// </summary>
    public static TimberItemLeaderLayout OrientAroundAnchor(
        TimberItemLeaderLayout layout,
        double rotationRadians)
    {
        if (layout is null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        if (double.IsNaN(rotationRadians) || double.IsInfinity(rotationRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(rotationRadians));
        }

        if (Math.Abs(rotationRadians) <= AngleToleranceRadians)
        {
            return layout;
        }

        var cos = Math.Cos(rotationRadians);
        var sin = Math.Sin(rotationRadians);
        return new TimberItemLeaderLayout(
            layout.AnchorX,
            layout.AnchorY,
            RotateX(layout.KneeX, layout.KneeY, layout.AnchorX, layout.AnchorY, cos, sin),
            RotateY(layout.KneeX, layout.KneeY, layout.AnchorX, layout.AnchorY, cos, sin),
            RotateX(layout.ContentX, layout.ContentY, layout.AnchorX, layout.AnchorY, cos, sin),
            RotateY(layout.ContentX, layout.ContentY, layout.AnchorX, layout.AnchorY, cos, sin),
            layout.Side,
            layout.EnvelopeWidthMm,
            layout.EnvelopeHeightMm);
    }

    /// <summary>
    /// Whole-annotation transform angle for standalone native leaders laid out
    /// in canonical horizontal space. Delegates to
    /// <see cref="TimberStandaloneNativeLeaderOrientationRules"/> —
    /// physical Start→End only; never feed an already-readable angle.
    /// </summary>
    public static double ResolveNativeLeaderTransformRadians(
        double physicalSourceAxisRadians) =>
        TimberStandaloneNativeLeaderOrientationRules.ResolveTransformRadians(
            physicalSourceAxisRadians);

    private static double RotateX(
        double x,
        double y,
        double pivotX,
        double pivotY,
        double cos,
        double sin)
    {
        var dx = x - pivotX;
        var dy = y - pivotY;
        return pivotX + (dx * cos) - (dy * sin);
    }

    private static double RotateY(
        double x,
        double y,
        double pivotX,
        double pivotY,
        double cos,
        double sin)
    {
        var dx = x - pivotX;
        var dy = y - pivotY;
        return pivotY + (dx * sin) + (dy * cos);
    }

    public static TimberItemLeaderLayout CalculateBlock(
        TimberLeaderPlacement placement,
        string itemText,
        ItemNumberLeaderStyle style,
        TimberLeaderHorizontalSide? preferredSide = null,
        double presentationScaleFactor = 1d)
    {
        if (placement is null)
        {
            throw new ArgumentNullException(nameof(placement));
        }
        if (presentationScaleFactor <= 0d ||
            double.IsNaN(presentationScaleFactor) ||
            double.IsInfinity(presentationScaleFactor))
        {
            throw new ArgumentOutOfRangeException(nameof(presentationScaleFactor));
        }

        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(style, itemText);

        var side = preferredSide ?? (Math.Cos(placement.RotationRadians) < 0d
            ? TimberLeaderHorizontalSide.Left
            : TimberLeaderHorizontalSide.Right);
        var planeBasis = TimberLeaderPlaneBasis.FromRotationRadians(
            placement.RotationRadians);
        var knee = CalculateKnee(
            placement.AnchorX,
            placement.AnchorY,
            side,
            (FirstSegmentLengthMm + FramedLeaderAdditionalOffsetMm) * presentationScaleFactor,
            planeBasis,
            TimberLeaderVerticalSide.Up,
            FramedFirstSegmentAngleRadians);
        return new TimberItemLeaderLayout(
            placement.AnchorX,
            placement.AnchorY,
            knee.X,
            knee.Y,
            knee.X,
            knee.Y,
            side,
            definition.WidthMm * presentationScaleFactor,
            definition.HeightMm * presentationScaleFactor);
    }

    /// <summary>
    /// Standalone framed ItemOnly only: canonical WorldXY layout (T=+X, N=+Y),
    /// Content==Knee (frame at terminal vertex). Host finishes with absolute
    /// <see cref="OrientAroundAnchor"/> (same CREATE contract as Plain /
    /// Dimensions). Must not be used by R3 Combined.
    /// </summary>
    public static TimberItemLeaderLayout CalculateStandaloneNativeFramedItem(
        TimberLeaderPlacement placement,
        string itemText,
        ItemNumberLeaderStyle style,
        double presentationScaleFactor = 1d)
    {
        if (placement is null)
        {
            throw new ArgumentNullException(nameof(placement));
        }
        if (presentationScaleFactor <= 0d ||
            double.IsNaN(presentationScaleFactor) ||
            double.IsInfinity(presentationScaleFactor))
        {
            throw new ArgumentOutOfRangeException(nameof(presentationScaleFactor));
        }

        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(style, itemText);
        var side = TimberLeaderHorizontalSide.Right;
        var originalCanonicalLength =
            (FirstSegmentLengthMm + FramedLeaderAdditionalOffsetMm) *
            presentationScaleFactor;
        var reduction =
            StandaloneNativeFramedItemOnlyLeaderReductionAtScale50Mm *
            presentationScaleFactor;
        var firstLength = Math.Max(
            StandaloneNativeFramedItemOnlyLeaderMinimumLengthMm,
            originalCanonicalLength - reduction);
        var knee = CalculateKnee(
            placement.AnchorX,
            placement.AnchorY,
            side,
            firstLength,
            TimberLeaderPlaneBasis.WorldXY,
            TimberLeaderVerticalSide.Up,
            FramedFirstSegmentAngleRadians);
        return new TimberItemLeaderLayout(
            placement.AnchorX,
            placement.AnchorY,
            knee.X,
            knee.Y,
            knee.X,
            knee.Y,
            side,
            definition.WidthMm * presentationScaleFactor,
            definition.HeightMm * presentationScaleFactor);
    }

    /// <summary>
    /// Combined landing/dogleg direction in the element-aligned plane.
    /// Uses an existing content delta when present; otherwise ±local H from
    /// <paramref name="rotationRadians"/> and <paramref name="side"/> —
    /// never world +X/−X.
    /// </summary>
    public static (double X, double Y) ResolveCombinedLandingDirection(
        double rotationRadians,
        TimberLeaderHorizontalSide side,
        double contentDeltaX = 0d,
        double contentDeltaY = 0d)
    {
        if (double.IsNaN(rotationRadians) || double.IsInfinity(rotationRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(rotationRadians));
        }

        var contentLength = Math.Sqrt(
            contentDeltaX * contentDeltaX +
            contentDeltaY * contentDeltaY);
        if (contentLength > AngleToleranceRadians)
        {
            return (
                contentDeltaX / contentLength,
                contentDeltaY / contentLength);
        }

        var basis = NormalizeBasis(
            TimberLeaderPlaneBasis.FromRotationRadians(rotationRadians));
        var horizontalDirection = side == TimberLeaderHorizontalSide.Left ? -1d : 1d;
        return (
            horizontalDirection * basis.HorizontalX,
            horizontalDirection * basis.HorizontalY);
    }

    public static (double X, double Y) CalculateKnee(
        double anchorX,
        double anchorY,
        TimberLeaderHorizontalSide side,
        double segmentLengthMm,
        TimberLeaderPlaneBasis? planeBasis = null,
        TimberLeaderVerticalSide verticalSide = TimberLeaderVerticalSide.Up,
        double firstSegmentAngleRadians = FirstSegmentAngleRadians)
    {
        if (segmentLengthMm <= 0d ||
            double.IsNaN(segmentLengthMm) ||
            double.IsInfinity(segmentLengthMm))
        {
            throw new ArgumentOutOfRangeException(nameof(segmentLengthMm));
        }

        var basis = NormalizeBasis(planeBasis ?? TimberLeaderPlaneBasis.WorldXY);
        var horizontalDirection = side == TimberLeaderHorizontalSide.Left ? -1d : 1d;
        var verticalDirection = verticalSide == TimberLeaderVerticalSide.Down ? -1d : 1d;
        var directionX =
            horizontalDirection * Math.Cos(firstSegmentAngleRadians) * basis.HorizontalX +
            verticalDirection * Math.Sin(firstSegmentAngleRadians) * basis.VerticalX;
        var directionY =
            horizontalDirection * Math.Cos(firstSegmentAngleRadians) * basis.HorizontalY +
            verticalDirection * Math.Sin(firstSegmentAngleRadians) * basis.VerticalY;
        return (
            anchorX + segmentLengthMm * directionX,
            anchorY + segmentLengthMm * directionY);
    }

    public static (double X, double Y) CalculateSegmentMidpoint(
        double startX,
        double startY,
        double endX,
        double endY) =>
        (
            startX + (endX - startX) / 2d,
            startY + (endY - startY) / 2d);

    public static double MeasureAcuteAngleRadians(
        double segmentX,
        double segmentY,
        double localHorizontalX,
        double localHorizontalY)
    {
        var segmentLength = Math.Sqrt(segmentX * segmentX + segmentY * segmentY);
        var horizontalLength = Math.Sqrt(
            localHorizontalX * localHorizontalX +
            localHorizontalY * localHorizontalY);
        if (segmentLength <= 0d ||
            horizontalLength <= 0d ||
            double.IsNaN(segmentLength) ||
            double.IsNaN(horizontalLength) ||
            double.IsInfinity(segmentLength) ||
            double.IsInfinity(horizontalLength))
        {
            throw new ArgumentOutOfRangeException(nameof(segmentX));
        }

        var normalizedDot =
            (segmentX * localHorizontalX + segmentY * localHorizontalY) /
            (segmentLength * horizontalLength);
        return Math.Acos(Math.Min(1d, Math.Max(-1d, Math.Abs(normalizedDot))));
    }

    private static TimberLeaderPlaneBasis NormalizeBasis(TimberLeaderPlaneBasis basis)
    {
        var horizontalLength = Math.Sqrt(
            basis.HorizontalX * basis.HorizontalX +
            basis.HorizontalY * basis.HorizontalY);
        var verticalLength = Math.Sqrt(
            basis.VerticalX * basis.VerticalX +
            basis.VerticalY * basis.VerticalY);
        if (horizontalLength <= 0d || verticalLength <= 0d)
        {
            throw new ArgumentException("Annotation plane basis must contain non-zero axes.", nameof(basis));
        }

        var horizontalX = basis.HorizontalX / horizontalLength;
        var horizontalY = basis.HorizontalY / horizontalLength;
        var verticalX = basis.VerticalX / verticalLength;
        var verticalY = basis.VerticalY / verticalLength;
        if (Math.Abs(horizontalX * verticalX + horizontalY * verticalY) > AngleToleranceRadians)
        {
            throw new ArgumentException("Annotation plane axes must be perpendicular.", nameof(basis));
        }

        return new TimberLeaderPlaneBasis(horizontalX, horizontalY, verticalX, verticalY);
    }

    /// <summary>
    /// Estimated text envelope width for standalone native landings. Stacked
    /// dimension lines (<c>\P</c> / newlines) use the longest line only.
    /// </summary>
    private static double EstimateStandaloneTextEnvelopeWidthMm(
        string normalizedText,
        double effectiveTextHeightMm)
    {
        var maximumLineLength = 0;
        foreach (var line in normalizedText
            .Replace("\r\n", "\n")
            .Split(["\\P", "\n"], StringSplitOptions.None))
        {
            if (line.Length > maximumLineLength)
            {
                maximumLineLength = line.Length;
            }
        }

        return Math.Max(
            effectiveTextHeightMm,
            maximumLineLength *
            effectiveTextHeightMm *
            EstimatedCharacterWidthFactor);
    }

    private static double QuantizeUp(double value) =>
        Math.Ceiling(value / EnvelopeSizeStepMm) * EnvelopeSizeStepMm;
}
