using AcKrovy.Core.Models;

namespace AcKrovy.Core.Models;

/// <summary>
/// Immutable G5 BlockContent layout request. Host supplies scaled lengths;
/// Core does not read AutoCAD types or database state.
/// </summary>
public sealed record TimberFramedBlockContentLayoutRequest(
    double AttachmentX,
    double AttachmentY,
    double ElementAxisRadians,
    TimberLeaderHorizontalSide Side,
    TimberFramedBlockContentKind ContentKind,
    double FrameWidthMm,
    double FrameHeightMm,
    int AnnotationScaleDenominator,
    double ItemNumberPaperHeightMm,
    double DimensionPaperHeightMm,
    double FirstSegmentLengthModelMm,
    double LandingLengthModelMm,
    double DimensionColumnEnvelopeWidthMm,
    TimberFramedBlockContentPresentation Presentation =
        TimberFramedBlockContentPresentation.Combined);
