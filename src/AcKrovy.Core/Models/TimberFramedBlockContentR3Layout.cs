namespace AcKrovy.Core.Models;

/// <summary>
/// Immutable R3 Combined BTR layout in block-local coordinates.
/// Frame and ITEM_NO share the origin; WIDTH/HEIGHT share one column X.
/// </summary>
public readonly record struct TimberFramedBlockContentR3Layout(
    TimberFramedBlockContentDimensionColumnSide Side,
    TimberPlanarPoint FrameCenter,
    TimberPlanarPoint ItemNo,
    TimberPlanarPoint Width,
    TimberPlanarPoint Height,
    double DimensionColumnLocalX,
    double WidthLocalY,
    double HeightLocalY,
    double TextRotationRadians);
