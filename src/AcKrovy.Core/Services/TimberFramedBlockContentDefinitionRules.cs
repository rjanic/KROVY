using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Portable AttrDef/frame contract for immutable G5 BlockContent BTRs (R2).
/// Geometry and AttrDef heights are baked at the default 1:50 baseline;
/// MLeader BlockScale applies the per-element annotation ScaleFactor once.
/// Leader Side is intentionally absent — it is ModelSpace-only geometry.
/// Combined WIDTH/HEIGHT local X is signed by
/// <see cref="TimberFramedBlockContentDimensionColumnSide"/> so the column
/// stays on the frame side toward the knee.
/// </summary>
public static class TimberFramedBlockContentDefinitionRules
{
    public const string ItemNoTag = "ITEM_NO";
    public const string WidthTag = "WIDTH";
    public const string HeightTag = "HEIGHT";

    /// <summary>
    /// Stable reference WIDTH/HEIGHT token length for shared BTR column X.
    /// Content-agnostic; AttrRef text may be wider/narrower without mutating the BTR.
    /// </summary>
    public const string ReferenceDimensionEnvelopeToken = "0000";

    public const int BaselineDenominator =
        TimberAnnotationScaleRules.DefaultDenominator;

    public const double BaselinePresentationScaleFactor = 1d;

    public const double GeometryToleranceMm = 0.001d;
    public const double AttributeTolerance = 1e-9d;

    public static string GetFrameSizeToken(
        TimberFramedBlockContentKind contentKind,
        TimberItemLeaderBlockSize? frameSize)
    {
        if (contentKind == TimberFramedBlockContentKind.Plain)
        {
            return "NONE";
        }

        if (frameSize is null)
        {
            throw new ArgumentNullException(nameof(frameSize));
        }

        return frameSize.Value switch
        {
            TimberItemLeaderBlockSize.Small => "SMALL",
            TimberItemLeaderBlockSize.Medium => "MEDIUM",
            TimberItemLeaderBlockSize.Large => "LARGE",
            _ => throw new ArgumentOutOfRangeException(nameof(frameSize)),
        };
    }

    public static int ExpectedAttributeCount(
        TimberFramedBlockContentPresentation presentation) =>
        presentation == TimberFramedBlockContentPresentation.ItemOnly ? 1 : 3;

    public static int ExpectedFrameEntityCount(
        TimberFramedBlockContentKind contentKind)
    {
        if (!Enum.IsDefined(typeof(TimberFramedBlockContentKind), contentKind))
        {
            throw new ArgumentOutOfRangeException(nameof(contentKind));
        }

        // Plain: invisible origin connection marker (AttrDefs alone are rejected
        // by BlockContent MLeader with eInvalidContext). Framed: one frame.
        return 1;
    }

    public static IReadOnlyList<string> ExpectedAttributeTags(
        TimberFramedBlockContentPresentation presentation) =>
        presentation == TimberFramedBlockContentPresentation.ItemOnly
            ? [ItemNoTag]
            : [ItemNoTag, WidthTag, HeightTag];

    public static double CalculateBaselineItemModelHeightMm(
        double itemPaperHeightMm) =>
        TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            itemPaperHeightMm,
            BaselineDenominator);

    public static double CalculateBaselineDimensionModelHeightMm(
        double dimensionPaperHeightMm) =>
        TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            dimensionPaperHeightMm,
            BaselineDenominator);

    public static double CalculateReferenceDimensionEnvelopeWidthMm(
        double dimensionPaperHeightMm)
    {
        // Envelope scales with the configured dimension paper height so BTR
        // column X stays coherent with AttrDef HEIGHT baked into the same key.
        var modelHeight = CalculateBaselineDimensionModelHeightMm(
            dimensionPaperHeightMm);
        return Math.Max(
            modelHeight,
            ReferenceDimensionEnvelopeToken.Length *
            modelHeight *
            TimberCombinedDimensionTypographyRules.EstimatedCharacterWidthFactor);
    }

    /// <summary>
    /// Absolute Combined WIDTH/HEIGHT column offset from frame/origin.
    /// </summary>
    public static double CalculateDimensionColumnOffsetMm(
        TimberFramedBlockContentKind contentKind,
        double frameWidthMm,
        double dimensionPaperHeightMm)
    {
        var frameHalf = contentKind == TimberFramedBlockContentKind.Plain
            ? 0d
            : frameWidthMm / 2d;
        var gap = TimberCombinedDimensionTypographyRules.CalculateMinimumFrameGapMm(
            BaselinePresentationScaleFactor);
        var envelope = CalculateReferenceDimensionEnvelopeWidthMm(
            dimensionPaperHeightMm);
        return frameHalf + gap + envelope / 2d;
    }

    /// <summary>
    /// Signed Combined WIDTH/HEIGHT local X for the requested column side.
    /// </summary>
    public static double CalculateDimensionColumnLocalX(
        TimberFramedBlockContentKind contentKind,
        double frameWidthMm,
        double dimensionPaperHeightMm,
        TimberFramedBlockContentDimensionColumnSide dimensionColumnSide)
    {
        if (!Enum.IsDefined(
                typeof(TimberFramedBlockContentDimensionColumnSide),
                dimensionColumnSide))
        {
            throw new ArgumentOutOfRangeException(nameof(dimensionColumnSide));
        }

        var offset = CalculateDimensionColumnOffsetMm(
            contentKind,
            frameWidthMm,
            dimensionPaperHeightMm);
        return dimensionColumnSide ==
            TimberFramedBlockContentDimensionColumnSide.NegativeLocalX
            ? -offset
            : offset;
    }

    /// <summary>
    /// From block-local content vector (BlockPosition − knee): positive local X
    /// content needs NegativeLocalX dimensions (toward the knee); negative
    /// local X content needs PositiveLocalX dimensions.
    /// </summary>
    public static TimberFramedBlockContentDimensionColumnSide
        ResolveDimensionColumnSideFromContentLocalX(double contentDirectionLocalX)
    {
        if (double.IsNaN(contentDirectionLocalX) ||
            double.IsInfinity(contentDirectionLocalX) ||
            Math.Abs(contentDirectionLocalX) <= GeometryToleranceMm)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentDirectionLocalX),
                contentDirectionLocalX,
                "Content direction local X must be a non-zero finite value.");
        }

        return contentDirectionLocalX > 0d
            ? TimberFramedBlockContentDimensionColumnSide.NegativeLocalX
            : TimberFramedBlockContentDimensionColumnSide.PositiveLocalX;
    }

    /// <summary>
    /// Classify an existing AttrDef WIDTH/HEIGHT local X into a column side.
    /// </summary>
    public static bool TryClassifyDimensionColumnSide(
        double widthOrHeightLocalX,
        out TimberFramedBlockContentDimensionColumnSide side)
    {
        if (double.IsNaN(widthOrHeightLocalX) ||
            double.IsInfinity(widthOrHeightLocalX) ||
            Math.Abs(widthOrHeightLocalX) <= GeometryToleranceMm)
        {
            side = default;
            return false;
        }

        side = widthOrHeightLocalX < 0d
            ? TimberFramedBlockContentDimensionColumnSide.NegativeLocalX
            : TimberFramedBlockContentDimensionColumnSide.PositiveLocalX;
        return true;
    }

    public static double CalculateWidthLocalY(double dimensionPaperHeightMm) =>
        TimberDimensionRowClearGapRules.CalculateWidthLocalY(
            dimensionPaperHeightMm,
            BaselineDenominator);

    public static double CalculateHeightLocalY(double dimensionPaperHeightMm) =>
        TimberDimensionRowClearGapRules.CalculateHeightLocalY(
            dimensionPaperHeightMm,
            BaselineDenominator);

    public static TimberPlanarPoint ItemAttributeLocalPoint =>
        new(0d, 0d);

    public static TimberPlanarPoint WidthAttributeLocalPoint(
        TimberFramedBlockContentKind contentKind,
        double frameWidthMm,
        double dimensionPaperHeightMm,
        TimberFramedBlockContentDimensionColumnSide dimensionColumnSide) =>
        new(
            CalculateDimensionColumnLocalX(
                contentKind,
                frameWidthMm,
                dimensionPaperHeightMm,
                dimensionColumnSide),
            CalculateWidthLocalY(dimensionPaperHeightMm));

    public static TimberPlanarPoint HeightAttributeLocalPoint(
        TimberFramedBlockContentKind contentKind,
        double frameWidthMm,
        double dimensionPaperHeightMm,
        TimberFramedBlockContentDimensionColumnSide dimensionColumnSide) =>
        new(
            CalculateDimensionColumnLocalX(
                contentKind,
                frameWidthMm,
                dimensionPaperHeightMm,
                dimensionColumnSide),
            CalculateHeightLocalY(dimensionPaperHeightMm));

    public static void ValidateRequest(
        TimberFramedBlockContentKind contentKind,
        TimberFramedBlockContentPresentation presentation)
    {
        if (!Enum.IsDefined(typeof(TimberFramedBlockContentKind), contentKind))
        {
            throw new ArgumentOutOfRangeException(nameof(contentKind));
        }

        if (!Enum.IsDefined(
                typeof(TimberFramedBlockContentPresentation),
                presentation))
        {
            throw new ArgumentOutOfRangeException(nameof(presentation));
        }

        if (contentKind == TimberFramedBlockContentKind.Plain &&
            presentation == TimberFramedBlockContentPresentation.ItemOnly)
        {
            throw new ArgumentException(
                "Plain ItemOnly BlockContent is out of scope; use native MText MLeader.",
                nameof(presentation));
        }
    }

    public static ItemNumberLeaderStyle ToItemNumberLeaderStyle(
        TimberFramedBlockContentKind contentKind) =>
        contentKind switch
        {
            TimberFramedBlockContentKind.Circle => ItemNumberLeaderStyle.Circle,
            TimberFramedBlockContentKind.Rectangle =>
                ItemNumberLeaderStyle.Rectangle,
            TimberFramedBlockContentKind.Slot => ItemNumberLeaderStyle.Slot,
            TimberFramedBlockContentKind.Plain =>
                throw new ArgumentOutOfRangeException(
                    nameof(contentKind),
                    "Plain G5 content has no framed ITEM_NO block definition."),
            _ => throw new ArgumentOutOfRangeException(nameof(contentKind)),
        };
}
