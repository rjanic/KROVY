using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Portable AttrDef/frame contract for immutable G5 BlockContent BTRs (R3).
/// Geometry and AttrDef heights are baked at the default 1:50 baseline;
/// MLeader BlockScale applies the per-element annotation ScaleFactor once.
/// Production Combined uses R3_RIGHT / R3_LEFT immutable BTR variants keyed by
/// knee-vs-frame in block-local space (not world/source Left/Right).
/// Literal bake: NegativeLocalX = −offset, PositiveLocalX = +offset.
/// R3_RIGHT = NegativeLocalX (PASS); R3_LEFT = PositiveLocalX. Pick the
/// variant so after BlockPosition placement, world D lies toward K
/// (dot(D−F, K−F) &gt; 0). Text is never mirrored (no negative BlockScaleX).
/// </summary>
public static class TimberFramedBlockContentDefinitionRules
{
    public const string ItemNoTag = "ITEM_NO";
    public const string WidthTag = "WIDTH";
    public const string HeightTag = "HEIGHT";

    /// <summary>
    /// Default Combined create column = R3_RIGHT (−offset): create landing
    /// along +local X keeps the knee on −X of the frame.
    /// </summary>
    public static TimberFramedBlockContentDimensionColumnSide
        DefaultCombinedDimensionColumnSide =>
        TimberFramedCombinedG5ContentVariantRules.RightColumnSide;

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
    /// Literal enum bake: NegativeLocalX → −offset, PositiveLocalX → +offset.
    /// R3 token names do not override this sign.
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
        return ResolveDimensionColumnLocalXSign(dimensionColumnSide) * offset;
    }

    /// <summary>
    /// Literal AttrDef column sign from the enum (not from R3 token names).
    /// </summary>
    public static double ResolveDimensionColumnLocalXSign(
        TimberFramedBlockContentDimensionColumnSide dimensionColumnSide) =>
        dimensionColumnSide ==
        TimberFramedBlockContentDimensionColumnSide.NegativeLocalX
            ? -1d
            : 1d;

    /// <summary>
    /// World-space invariant: D is on the knee side of F when
    /// <c>dot(D − F, K − F) &gt; 0</c>.
    /// </summary>
    public static bool TryEvaluateDimensionsTowardKneeDot(
        TimberPlanarPoint frameCenter,
        TimberPlanarPoint knee,
        TimberPlanarPoint dimensionsAnchor,
        out double towardKneeDot)
    {
        towardKneeDot =
            ((dimensionsAnchor.X - frameCenter.X) * (knee.X - frameCenter.X)) +
            ((dimensionsAnchor.Y - frameCenter.Y) * (knee.Y - frameCenter.Y));
        if (double.IsNaN(towardKneeDot) || double.IsInfinity(towardKneeDot))
        {
            towardKneeDot = double.NaN;
            return false;
        }

        return true;
    }

    public static bool AreDimensionsTowardKnee(
        TimberPlanarPoint frameCenter,
        TimberPlanarPoint knee,
        TimberPlanarPoint dimensionsAnchor) =>
        TryEvaluateDimensionsTowardKneeDot(
            frameCenter,
            knee,
            dimensionsAnchor,
            out var dot) &&
        dot > 0d;

    /// <summary>
    /// Map block-local AttrDef point to world with BlockRotation about BlockPosition
    /// (BlockScale = 1). Matches typical CREATE BlockRotation=0 + BlockPosition
    /// placement after readable TransformBy has already oriented geometry.
    /// </summary>
    public static TimberPlanarPoint ToWorldFromBlockLocal(
        TimberPlanarPoint blockLocal,
        TimberPlanarPoint blockPosition,
        double blockRotationRadians)
    {
        if (double.IsNaN(blockRotationRadians) ||
            double.IsInfinity(blockRotationRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(blockRotationRadians));
        }

        var cos = Math.Cos(blockRotationRadians);
        var sin = Math.Sin(blockRotationRadians);
        return new TimberPlanarPoint(
            blockPosition.X + (blockLocal.X * cos) - (blockLocal.Y * sin),
            blockPosition.Y + (blockLocal.X * sin) + (blockLocal.Y * cos));
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

    /// <summary>
    /// Shared R3 Combined BTR layout. Variants differ only by WIDTH/HEIGHT
    /// local-X sign (literal enum). Frame, ITEM_NO, spacing, heights and
    /// readable rotation (0) are identical — no text mirror / negative BlockScaleX.
    /// Caller must pick the side from knee-vs-frame, not from world L/R alone.
    /// </summary>
    public static TimberFramedBlockContentR3Layout CreateR3Layout(
        TimberFramedBlockContentDimensionColumnSide side,
        TimberFramedBlockContentKind contentKind,
        double frameWidthMm,
        double dimensionPaperHeightMm)
    {
        if (!Enum.IsDefined(typeof(TimberFramedBlockContentDimensionColumnSide), side))
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        if (!Enum.IsDefined(typeof(TimberFramedBlockContentKind), contentKind))
        {
            throw new ArgumentOutOfRangeException(nameof(contentKind));
        }

        if (contentKind != TimberFramedBlockContentKind.Plain &&
            (frameWidthMm <= 0d ||
             double.IsNaN(frameWidthMm) ||
             double.IsInfinity(frameWidthMm)))
        {
            throw new ArgumentOutOfRangeException(nameof(frameWidthMm));
        }

        if (contentKind == TimberFramedBlockContentKind.Plain && frameWidthMm != 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameWidthMm),
                "Plain R3 layout requires zero frame width.");
        }

        var columnX = CalculateDimensionColumnLocalX(
            contentKind,
            frameWidthMm,
            dimensionPaperHeightMm,
            side);
        var widthY = CalculateWidthLocalY(dimensionPaperHeightMm);
        var heightY = CalculateHeightLocalY(dimensionPaperHeightMm);
        var frameCenter = ItemAttributeLocalPoint;
        return new TimberFramedBlockContentR3Layout(
            side,
            frameCenter,
            frameCenter,
            new TimberPlanarPoint(columnX, widthY),
            new TimberPlanarPoint(columnX, heightY),
            columnX,
            widthY,
            heightY,
            TextRotationRadians: 0d);
    }

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

    public static TimberFramedBlockContentKind FromItemNumberLeaderStyle(
        ItemNumberLeaderStyle style) =>
        ItemNumberLeaderStyleRules.Normalize(style) switch
        {
            ItemNumberLeaderStyle.Circle => TimberFramedBlockContentKind.Circle,
            ItemNumberLeaderStyle.Rectangle =>
                TimberFramedBlockContentKind.Rectangle,
            ItemNumberLeaderStyle.Slot => TimberFramedBlockContentKind.Slot,
            ItemNumberLeaderStyle.Plain =>
                throw new ArgumentOutOfRangeException(
                    nameof(style),
                    "Plain Combined stays on the native MText MLeader path."),
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };
}
