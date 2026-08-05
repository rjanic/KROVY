using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Canonical local G5 BlockContent layout (pre-host TransformBy).
/// First segment: knee = attachment + T·(L·cos60) + sideSign·N·(L·sin60),
/// with sideSign = +1 Right / −1 Left (G5C proof contract).
/// Combined WIDTH/HEIGHT local X follows
/// <see cref="TimberFramedBlockContentDimensionColumnSide"/> so the column
/// stays on the frame side toward the knee (not screen Left/Right).
/// Landing stays along +T for both Left and Right at create; dogleg direction
/// after grip STRETCH is resolved separately by
/// <see cref="TimberFramedBlockContentDoglegRules"/>.
/// </summary>
public static class TimberFramedBlockContentLayoutCalculator
{
    public const double FirstSegmentAngleRadians =
        TimberItemLeaderLayoutCalculator.FramedFirstSegmentAngleRadians;

    public static double SideSign(TimberLeaderHorizontalSide side) =>
        TimberFramedBlockContentDoglegRules.SideSign(side);

    public static TimberFramedBlockContentLayout Calculate(
        TimberFramedBlockContentLayoutRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ValidateRequest(request);

        var rawAngle = request.ElementAxisRadians;
        var readable =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(rawAngle);
        var flipped = TimberAnnotationReadabilityRules.IsReadabilityFlipped(rawAngle);
        var sideSign = SideSign(request.Side);

        var attachment = new TimberPlanarPoint(
            request.AttachmentX,
            request.AttachmentY);

        // Canonical local axes before host rotation: T = +X, N = +Y.
        var t = new TimberPlanarVector(1d, 0d);
        var n = new TimberPlanarVector(0d, 1d);
        var angle = FirstSegmentAngleRadians;
        var segment = request.FirstSegmentLengthModelMm;
        var knee = attachment
            .Offset(t.Scale(segment * Math.Cos(angle)))
            .Offset(n.Scale(sideSign * segment * Math.Sin(angle)));

        var landingStart = knee;
        var landingEnd = knee.Offset(t.Scale(request.LandingLengthModelMm));

        var frameWidth = request.ContentKind == TimberFramedBlockContentKind.Plain
            ? 0d
            : request.FrameWidthMm;
        var frameHeight = request.ContentKind == TimberFramedBlockContentKind.Plain
            ? 0d
            : request.FrameHeightMm;

        var frameCenter = landingEnd;
        var itemCenter = frameCenter;

        var scaleFactor = TimberAnnotationScaleRules.GetScaleFactor(
            request.AnnotationScaleDenominator);
        var minimumFrameGap =
            TimberCombinedDimensionTypographyRules.CalculateMinimumFrameGapMm(
                scaleFactor);
        var dimensionColumnOffset =
            frameWidth / 2d +
            minimumFrameGap +
            request.DimensionColumnEnvelopeWidthMm / 2d;
        var dimensionColumnLocalX =
            request.Presentation == TimberFramedBlockContentPresentation.Combined
                ? (request.DimensionColumnSide ==
                    TimberFramedBlockContentDimensionColumnSide.NegativeLocalX
                    ? -dimensionColumnOffset
                    : dimensionColumnOffset)
                : -dimensionColumnOffset;

        var itemHeight =
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                request.ItemNumberPaperHeightMm,
                request.AnnotationScaleDenominator);
        var dimHeight =
            TimberDimensionRowClearGapRules.CalculateDimensionTextModelHeightMm(
                request.DimensionPaperHeightMm,
                request.AnnotationScaleDenominator);
        var clearGap =
            TimberDimensionRowClearGapRules.CalculateDesiredClearGapModelMm(
                request.AnnotationScaleDenominator);
        var rowCenter =
            TimberDimensionRowClearGapRules.CalculateRowCenterDistanceModelMm(
                request.DimensionPaperHeightMm,
                request.AnnotationScaleDenominator);

        TimberPlanarPoint? widthCenter = null;
        TimberPlanarPoint? heightCenter = null;
        if (request.Presentation == TimberFramedBlockContentPresentation.Combined)
        {
            var widthLocalY =
                TimberDimensionRowClearGapRules.CalculateWidthLocalY(
                    request.DimensionPaperHeightMm,
                    request.AnnotationScaleDenominator);
            var heightLocalY =
                TimberDimensionRowClearGapRules.CalculateHeightLocalY(
                    request.DimensionPaperHeightMm,
                    request.AnnotationScaleDenominator);
            widthCenter = frameCenter.Offset(dimensionColumnLocalX, widthLocalY);
            heightCenter = frameCenter.Offset(dimensionColumnLocalX, heightLocalY);
        }

        TimberPlanarPoint? frameCenterResult =
            request.ContentKind == TimberFramedBlockContentKind.Plain
                ? null
                : frameCenter;

        return new TimberFramedBlockContentLayout(
            attachment,
            knee,
            landingStart,
            landingEnd,
            itemCenter,
            widthCenter,
            heightCenter,
            frameCenterResult,
            rawAngle,
            readable,
            flipped,
            request.Side,
            request.ContentKind,
            request.Presentation,
            sideSign,
            segment,
            angle,
            request.LandingLengthModelMm,
            clearGap,
            rowCenter,
            dimHeight,
            itemHeight,
            dimensionColumnLocalX,
            frameWidth,
            frameHeight);
    }

    private static void ValidateRequest(TimberFramedBlockContentLayoutRequest request)
    {
        ValidateFinite(request.AttachmentX, nameof(request.AttachmentX));
        ValidateFinite(request.AttachmentY, nameof(request.AttachmentY));
        ValidateFinite(request.ElementAxisRadians, nameof(request.ElementAxisRadians));

        if (!TimberAnnotationScaleRules.IsValidDenominator(
                request.AnnotationScaleDenominator))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.AnnotationScaleDenominator),
                request.AnnotationScaleDenominator,
                $"Annotation scale denominator must be between {TimberAnnotationScaleRules.MinimumDenominator} and {TimberAnnotationScaleRules.MaximumDenominator}.");
        }

        if (!TimberAnnotationTextSettingsRules.IsValidItemCodePaperHeightMm(
                request.ItemNumberPaperHeightMm))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.ItemNumberPaperHeightMm),
                request.ItemNumberPaperHeightMm,
                "Item number paper height is outside the supported range.");
        }

        if (!TimberAnnotationTextSettingsRules.IsValidDimensionPaperHeightMm(
                request.DimensionPaperHeightMm))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.DimensionPaperHeightMm),
                request.DimensionPaperHeightMm,
                "Dimension paper height is outside the supported range.");
        }

        if (request.FirstSegmentLengthModelMm <= 0d ||
            double.IsNaN(request.FirstSegmentLengthModelMm) ||
            double.IsInfinity(request.FirstSegmentLengthModelMm))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.FirstSegmentLengthModelMm));
        }

        if (request.LandingLengthModelMm <= 0d ||
            double.IsNaN(request.LandingLengthModelMm) ||
            double.IsInfinity(request.LandingLengthModelMm))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.LandingLengthModelMm));
        }

        if (request.DimensionColumnEnvelopeWidthMm < 0d ||
            double.IsNaN(request.DimensionColumnEnvelopeWidthMm) ||
            double.IsInfinity(request.DimensionColumnEnvelopeWidthMm))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.DimensionColumnEnvelopeWidthMm));
        }

        if (request.ContentKind == TimberFramedBlockContentKind.Plain)
        {
            if (request.FrameWidthMm != 0d || request.FrameHeightMm != 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.FrameWidthMm),
                    "Plain G5 content requires zero frame width and height.");
            }
        }
        else
        {
            if (request.FrameWidthMm <= 0d ||
                double.IsNaN(request.FrameWidthMm) ||
                double.IsInfinity(request.FrameWidthMm))
            {
                throw new ArgumentOutOfRangeException(nameof(request.FrameWidthMm));
            }

            if (request.FrameHeightMm <= 0d ||
                double.IsNaN(request.FrameHeightMm) ||
                double.IsInfinity(request.FrameHeightMm))
            {
                throw new ArgumentOutOfRangeException(nameof(request.FrameHeightMm));
            }
        }

        if (!Enum.IsDefined(typeof(TimberFramedBlockContentKind), request.ContentKind))
        {
            throw new ArgumentOutOfRangeException(nameof(request.ContentKind));
        }

        if (!Enum.IsDefined(
                typeof(TimberFramedBlockContentPresentation),
                request.Presentation))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Presentation));
        }

        if (request.Presentation == TimberFramedBlockContentPresentation.Combined &&
            !Enum.IsDefined(
                typeof(TimberFramedBlockContentDimensionColumnSide),
                request.DimensionColumnSide))
        {
            throw new ArgumentOutOfRangeException(nameof(request.DimensionColumnSide));
        }
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
