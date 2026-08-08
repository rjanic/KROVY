#if DEBUG
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Form B only: ITEM_NO + WIDTH + HEIGHT as three single-line AttrDefs.
/// Placement strategy: build one canonical horizontal MLeader (Left or Right),
/// then rigid-rotate the whole entity around the leader attachment pivot.
/// </summary>
internal enum AutoCadFramedG5CombinedStyleMode
{
    DistinctAttrDefStyles,
    SharedStyleSeparateHeights,
}

/// <summary>Horizontal landing side chosen before world rotation.</summary>
internal enum AutoCadFramedG5CombinedSide
{
    /// <summary>WIDTH/HEIGHT column on −X of frame (canonical 0° left column).</summary>
    Left,

    /// <summary>WIDTH/HEIGHT column on +X of frame; text stays upright (not mirrored).</summary>
    Right,
}

internal sealed record AutoCadFramedG5CombinedProofCase(
    string Token,
    ItemNumberLeaderStyle FrameKind,
    string ItemText,
    string WidthText,
    string HeightText,
    AutoCadFramedG5CombinedStyleMode StyleMode,
    AutoCadFramedG5CombinedSide Side,
    /// <summary>Source element axis Start→End in radians (before readability).</summary>
    double ElementAxisRadians,
    double ItemPaperHeightMm,
    double DimensionPaperHeightMm,
    int Denominator);

internal static class AutoCadFramedG5CombinedLandingProofPolicy
{
    public const int SchemaVersion = 8;
    public const string SuiteIdentifier = "AK_DEV_FRAMED_G5_COMBINED_CANONICAL_ROTATE";
    public const string RegAppName = "AK_DEV_FRAMED_G5C";
    /// <summary>DEBUG-only marker for 2000 mm reference source lines.</summary>
    public const string SourceLineRegAppName = "AK_DEV_FRAMED_G5C_SRC";
    public const string ReportFileName = "g5-combined-landing-proof-report.json";

    /// <summary>
    /// Host PDF mark (manual rectangle, not geometry): F-SLT-L-90-D50.
    /// Vertical source @ 90°, Slot/S1, Left, SharedStyle fallback.
    /// </summary>
    public const string PdfMarkedCaseToken = "F-SLT-L-90-D50";

    /// <summary>
    /// Rotate-equivalent post-create rebuild around attachment (±1°).
    /// AutoCAD MLeader BlockContent can leave stale display/layout after TransformBy
    /// until a native ROTATE rebuilds dogleg/content orientation.
    /// </summary>
    public const double StabilizationEpsilonRadians = Math.PI / 180d;

    public const string ItemNoTag = "ITEM_NO";
    public const string WidthTag = "WIDTH";
    public const string HeightTag = "HEIGHT";
    public const string DimFormName = "WidthHeightPair";

    public const double GeometryToleranceMm = 0.5d;
    public const double PlacementToleranceMm = 2.0d;
    public const double HeightToleranceMm = 0.05d;
    public const double PivotToleranceMm = 1e-6d;
    public const double BaselineInvariantToleranceMm = 0.75d;
    public const double FirstSegmentAngleToleranceDeg = 0.1d;
    public const double GripReseatToleranceMm = 0.5d;
    public const double RowSpacingToleranceMm = 0.75d;

    /// <summary>
    /// Legacy G5C stack (WRONG vs UX): StackGapFactor × dimModelHeight
    /// ≈ 1.15 × 125 = 143.75 mm at 1:50.
    /// </summary>
    public const double LegacyStackGapFactor = 1.15d;

    /// <summary>
    /// Paper-space half-gap from landing centerline to WIDTH or HEIGHT center.
    /// Model = 1.0 × denominator (1:50 → ±50 mm). Landing stays exactly between.
    /// </summary>
    public const double HalfRowSpacingPaperMm = 1.0d;

    /// <summary>
    /// Paper-space WIDTH ↔ HEIGHT center distance = 2 × <see cref="HalfRowSpacingPaperMm"/>.
    /// </summary>
    public const double DimensionRowSpacingPaperMm = 2.0d;

    /// <summary>
    /// Proof-only ITEM_NO paper height. Production Text Settings default is 2.7 mm;
    /// this harness uses 3.0 mm so ITEM_NO model height @ 1:50 = 150 mm.
    /// Does not change production Text Settings / kartu Texty.
    /// </summary>
    public const double ProofItemNumberPaperHeightMm = 3.0d;

    /// <summary>
    /// Multiplier for attachment → knee length vs legacy stub baseline.
    /// Direction is explicit 60° in local T/N — not a scaled legacy vector.
    /// </summary>
    public const double FirstLeaderSegmentLengthMultiplier = 3.0d;

    /// <summary>Absolute angle of attachment→knee vs element axis T.</summary>
    public const double FirstLeaderSegmentAngleDeg = 60d;

    public const double ProofSourceLineLengthMm = 2000d;

    /// <summary>
    /// Legacy stub factors — used only to derive the 1× base length that is then ×3.
    /// </summary>
    public const double AttachmentToKneeStubFactor = 0.25d;

    public const double KneeDropFrameHeightFactor = 0.35d;
    public const double KneeDropMinimumMm = 120d;

    public static double SideSign(AutoCadFramedG5CombinedSide side) =>
        side == AutoCadFramedG5CombinedSide.Right ? 1d : -1d;

    /// <summary>
    /// Circle/Rectangle/Slot × Left/Right × element axes, distinct styles.
    /// Angle is NOT part of the shared BTR variant key.
    /// </summary>
    public static IReadOnlyList<AutoCadFramedG5CombinedProofCase> Cases { get; } =
        BuildCases();

    private static IReadOnlyList<AutoCadFramedG5CombinedProofCase> BuildCases()
    {
        var axes = new (string Tag, double Degrees)[]
        {
            ("0", 0d),
            ("35", 35d),
            ("90", 90d),
            ("135", 135d),
            ("OPP", 180d), // opposite Start→End
            ("215", 215d),
            ("270", 270d),
        };

        var frames = new (ItemNumberLeaderStyle Kind, string Item, string Abbr)[]
        {
            (ItemNumberLeaderStyle.Circle, "K1", "CIR"),
            (ItemNumberLeaderStyle.Rectangle, "P8", "REC"),
            (ItemNumberLeaderStyle.Slot, "S1", "SLT"),
        };

        var denominators = new[] { 25, 50, 100 };

        var list = new List<AutoCadFramedG5CombinedProofCase>();
        foreach (var denominator in denominators)
        {
            foreach (var frame in frames)
            {
                foreach (var side in new[]
                         {
                             AutoCadFramedG5CombinedSide.Left,
                             AutoCadFramedG5CombinedSide.Right,
                         })
                {
                    var sideTag = side == AutoCadFramedG5CombinedSide.Left ? "L" : "R";
                    foreach (var axis in axes)
                    {
                        // Full Left×axes for all frames; Right covers 0/35/90/OPP.
                        // Non-50 scales keep the same angle set for scale proof.
                        if (side == AutoCadFramedG5CombinedSide.Right &&
                            axis.Tag is not ("0" or "35" or "90" or "OPP"))
                        {
                            continue;
                        }

                        list.Add(Case(
                            $"B-{frame.Abbr}-{sideTag}-{axis.Tag}-D{denominator}",
                            frame.Kind,
                            frame.Item,
                            AutoCadFramedG5CombinedStyleMode.DistinctAttrDefStyles,
                            side,
                            axis.Degrees * Math.PI / 180d,
                            denominator));
                    }
                }
            }
        }

        // Shared-style fallback samples (angle still via rigid rotate).
        list.Add(Case(
            "F-CIR-L-0-D50",
            ItemNumberLeaderStyle.Circle,
            "K1",
            AutoCadFramedG5CombinedStyleMode.SharedStyleSeparateHeights,
            AutoCadFramedG5CombinedSide.Left,
            0d,
            50));
        list.Add(Case(
            "F-REC-R-35-D50",
            ItemNumberLeaderStyle.Rectangle,
            "P8",
            AutoCadFramedG5CombinedStyleMode.SharedStyleSeparateHeights,
            AutoCadFramedG5CombinedSide.Right,
            35d * Math.PI / 180d,
            50));
        list.Add(Case(
            "F-SLT-L-90-D50",
            ItemNumberLeaderStyle.Slot,
            "S1",
            AutoCadFramedG5CombinedStyleMode.SharedStyleSeparateHeights,
            AutoCadFramedG5CombinedSide.Left,
            Math.PI / 2d,
            50));

        return list;
    }

    /// <summary>
    /// Same readability normalization as FullLabel / TimberElementLabelPlacementCalculator.
    /// </summary>
    public static double NormalizeReadableRotation(double rotationRadians)
    {
        // Keep DEBUG proof aligned with Core [−90°, +90°] (270° → −90°).
        return TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
            rotationRadians);
    }

    public static bool ReadabilityFlipped(double elementAxisRadians) =>
        Math.Abs(
            NormalizeAngleDelta(
                NormalizeReadableRotation(elementAxisRadians) - elementAxisRadians)) >
        1e-9;

    public static double ItemModelHeightMm(AutoCadFramedG5CombinedProofCase proofCase) =>
        TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            proofCase.ItemPaperHeightMm,
            proofCase.Denominator);

    public static double DimensionModelHeightMm(
        AutoCadFramedG5CombinedProofCase proofCase) =>
        TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            proofCase.DimensionPaperHeightMm,
            proofCase.Denominator);

    public static double PresentationScaleFactor(int denominator) =>
        TimberAnnotationScaleRules.GetScaleFactor(denominator);

    public static double LandingDistanceMm(int denominator) =>
        TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
        PresentationScaleFactor(denominator);

    public static double MinimumFrameGapMm(int denominator) =>
        TimberCombinedDimensionTypographyRules.CalculateMinimumFrameGapMm(
            PresentationScaleFactor(denominator));

    /// <summary>Landing centerline local Y in block space (frame origin).</summary>
    public static double LandingLocalY => 0d;

    /// <summary>
    /// Desired paper clearance between the visible DIM glyph rows (WIDTH/HEIGHT)
    /// measured as: (center-to-center distance - text height).
    /// </summary>
    public const double DesiredClearGapPaperMm = 2.0d;

    public static double DesiredClearGapModelMm(int denominator) =>
        DesiredClearGapPaperMm * TimberAnnotationScaleRules.NormalizeDenominator(denominator);

    /// <summary>
    /// Row center-to-center distance so that glyph-clear gap equals the contract:
    /// ActualGlyphClearGap = ActualCenterDistance - DimensionTextModelHeight.
    /// </summary>
    public static double RowCenterDistanceModelMm(
        double dimensionTextModelHeightMm,
        int denominator) =>
        dimensionTextModelHeightMm + DesiredClearGapModelMm(denominator);

    public static double HalfRowCenterDistanceModelMm(
        double dimensionTextModelHeightMm,
        int denominator) =>
        RowCenterDistanceModelMm(dimensionTextModelHeightMm, denominator) / 2d;

    /// <summary>
    /// WIDTH center above landing so that landing is exactly centered between.
    /// </summary>
    public static double WidthLocalY(
        int denominator,
        double dimensionTextModelHeightMm) =>
        LandingLocalY +
        HalfRowCenterDistanceModelMm(dimensionTextModelHeightMm, denominator);

    /// <summary>
    /// HEIGHT center below landing so that landing is exactly centered between.
    /// </summary>
    public static double HeightLocalY(
        int denominator,
        double dimensionTextModelHeightMm) =>
        LandingLocalY -
        HalfRowCenterDistanceModelMm(dimensionTextModelHeightMm, denominator);

    /// <summary>Legacy helper — prefer <see cref="RowSpacingModelMm"/>.</summary>
    public static double StackLineGapMm(double dimModelHeightMm) =>
        dimModelHeightMm * LegacyStackGapFactor;

    public static double LegacyRowSpacingModelMm(double dimModelHeightMm) =>
        StackLineGapMm(dimModelHeightMm);

    public static double DimensionEnvelopeWidthMm(
        AutoCadFramedG5CombinedProofCase proofCase) =>
        TimberCombinedDimensionTypographyRules.CalculateEnvelopeWidthMm(
            $"{proofCase.WidthText}\n{proofCase.HeightText}",
            PresentationScaleFactor(proofCase.Denominator));

    public static TimberItemLeaderBlockDefinition ResolveFrame(
        AutoCadFramedG5CombinedProofCase proofCase) =>
        TimberItemLeaderBlockDefinitionRules.Resolve(
            proofCase.FrameKind,
            proofCase.ItemText);

    /// <summary>
    /// Local X of WIDTH/HEIGHT centers relative to frame center.
    /// Always toward the attachment (−T in canonical horizontal). Side is expressed
    /// only by knee sideSign on ±N — text/frame are not mirrored.
    /// </summary>
    public static double ExpectedDimCenterLocalX(
        TimberItemLeaderBlockDefinition frame,
        double dimensionEnvelopeWidthMm,
        int denominator,
        AutoCadFramedG5CombinedSide side)
    {
        _ = side;
        return -(frame.WidthMm / 2d +
                 MinimumFrameGapMm(denominator) +
                 dimensionEnvelopeWidthMm / 2d);
    }

    public static double ExpectedDimOuterLocalX(
        double dimCenterLocalX,
        double dimensionEnvelopeWidthMm)
    {
        var side = Math.Sign(dimCenterLocalX) == 0 ? -1d : Math.Sign(dimCenterLocalX);
        return dimCenterLocalX + side * (dimensionEnvelopeWidthMm / 2d);
    }

    /// <summary>
    /// Shared immutable BTR key — style/frame/size/heights/side only. No angle.
    /// </summary>
    public static string CreateSharedVariantBlockName(
        AutoCadFramedG5CombinedProofCase proofCase,
        TimberItemLeaderBlockDefinition frame)
    {
        var side = proofCase.Side == AutoCadFramedG5CombinedSide.Left ? "L" : "R";
        return "AK_G5C_R4_" + Sanitize(
            $"{DimFormName}_{proofCase.StyleMode}_{proofCase.FrameKind}_" +
            $"{frame.Size}_D{proofCase.Denominator}_" +
            $"I{Format(proofCase.ItemPaperHeightMm)}_" +
            $"M{Format(proofCase.DimensionPaperHeightMm)}_{side}");
    }

    public static double NormalizeAngleDelta(double radians)
    {
        var value = radians;
        while (value > Math.PI)
        {
            value -= 2d * Math.PI;
        }

        while (value <= -Math.PI)
        {
            value += 2d * Math.PI;
        }

        return value;
    }

    private static AutoCadFramedG5CombinedProofCase Case(
        string token,
        ItemNumberLeaderStyle frameKind,
        string itemText,
        AutoCadFramedG5CombinedStyleMode styleMode,
        AutoCadFramedG5CombinedSide side,
        double elementAxisRadians,
        int denominator = TimberAnnotationScaleRules.DefaultDenominator) =>
        new(
            token,
            frameKind,
            itemText,
            WidthText: "80",
            HeightText: "160",
            styleMode,
            side,
            elementAxisRadians,
            ItemPaperHeightMm: ProofItemNumberPaperHeightMm,
            DimensionPaperHeightMm:
                TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
            Denominator: TimberAnnotationScaleRules.NormalizeDenominator(denominator));

    private static string Format(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string Sanitize(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var text = new string(chars);
        return text.Length <= 56 ? text : text[..56];
    }
}
#endif
