using AcKrovy.Core.Models;

namespace AcKrovy.Core.Models;

/// <summary>
/// Canonical local (pre-host-rotation) G5 BlockContent layout.
/// Host applies <see cref="ReadableAngleRadians"/> around attachment.
/// </summary>
public sealed record TimberFramedBlockContentLayout(
    TimberPlanarPoint AttachmentLocal,
    TimberPlanarPoint KneeLocal,
    TimberPlanarPoint LandingStartLocal,
    TimberPlanarPoint LandingEndLocal,
    TimberPlanarPoint ItemCenterLocal,
    TimberPlanarPoint? WidthCenterLocal,
    TimberPlanarPoint? HeightCenterLocal,
    TimberPlanarPoint? FrameCenterLocal,
    double RawAngleRadians,
    double ReadableAngleRadians,
    bool ReadabilityFlipped,
    TimberLeaderHorizontalSide Side,
    TimberFramedBlockContentKind ContentKind,
    TimberFramedBlockContentPresentation Presentation,
    double SideSign,
    double FirstSegmentLengthModelMm,
    double FirstSegmentAngleRadians,
    double LandingLengthModelMm,
    double RowClearGapModelMm,
    double RowCenterDistanceModelMm,
    double DimensionTextModelHeightMm,
    double ItemTextModelHeightMm,
    double DimensionColumnLocalX,
    double FrameWidthMm,
    double FrameHeightMm);
