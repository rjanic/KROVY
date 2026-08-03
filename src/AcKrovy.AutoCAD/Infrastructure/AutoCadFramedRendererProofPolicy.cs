#if DEBUG
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed record AutoCadFramedRendererProofCase(
    string Token,
    TimberAnnotationMode Mode,
    ItemNumberLeaderStyle ItemStyle,
    TimberMainAnnotationComponentRole FramedRole,
    int StyleSlot,
    double ItemNumberPaperHeightMm,
    int Denominator,
    string ItemText,
    TimberElementType ElementType = TimberElementType.Rafter,
    string? CustomElementPrefix = null);

internal sealed record AutoCadFramedRendererProofExpectedCase(
    string Token,
    TimberAnnotationMode Mode,
    ItemNumberLeaderStyle ItemStyle,
    TimberMainAnnotationComponentRole FramedRole,
    string StyleName,
    double ItemNumberPaperHeightMm,
    int Denominator,
    string ItemText,
    string BlockName,
    string ResultKind,
    TimberItemLeaderBlockSize FrameSize,
    double MeasuredTextWidthMm,
    double AvailableInnerWidthMm,
    double HorizontalPaddingMm);

internal sealed record AutoCadFramedRendererTokenCandidate(
    string ItemText,
    string Prefix);

internal sealed record AutoCadFramedRendererTokenAttempt(
    string ItemText,
    string Prefix,
    bool IsValidProductionToken,
    double? MeasuredTextWidthMm,
    bool MatchesRequestedRange,
    string DiagnosticReason);

internal sealed record AutoCadFramedRendererTokenSelection(
    AutoCadFramedRendererTokenCandidate? SelectedCandidate,
    double? MeasuredTextWidthMm,
    IReadOnlyList<AutoCadFramedRendererTokenAttempt> Attempts,
    string DiagnosticReason)
{
    public bool IsTested => SelectedCandidate is not null &&
        MeasuredTextWidthMm.HasValue;
}

internal sealed record AutoCadFramedRendererRectangleCaseManifest(
    string Token,
    string State,
    string? ItemText,
    string StyleName,
    double? MeasuredTextWidthMm,
    double MediumInnerWidthMm,
    double LargeInnerWidthMm,
    double HorizontalPaddingMm,
    string DiagnosticReason,
    IReadOnlyList<AutoCadFramedRendererTokenAttempt> Attempts);

internal sealed record AutoCadFramedRendererOverflowCaseManifest(
    string Token,
    string State,
    string? ItemText,
    string ExpectedResultKind,
    string StyleName,
    double? MeasuredTextWidthMm,
    double LargeInnerWidthMm,
    double HorizontalPaddingMm,
    int ExpectedCreatedEntityCount,
    int ModelSpaceEntityDelta,
    int BlockDefinitionDelta,
    int VariantCatalogDelta,
    string PreservationState,
    string DiagnosticReason,
    IReadOnlyList<AutoCadFramedRendererTokenAttempt> Attempts);

internal sealed record AutoCadFramedRendererProofManifest(
    int SchemaVersion,
    string SuiteIdentifier,
    string LegacyBlockName,
    int VariantCatalogCount,
    string FailureCaseState,
    AutoCadFramedRendererRectangleCaseManifest RectangleCaseE,
    AutoCadFramedRendererOverflowCaseManifest OverflowCaseJ,
    IReadOnlyList<AutoCadFramedRendererProofExpectedCase> Cases);

internal static class AutoCadFramedRendererProofPolicy
{
    public const int SchemaVersion = 3;
    public const string SuiteIdentifier = "AK_DEV_FRAMED_RENDERER_PROOF";
    public const string RegAppName = "AK_DEV_FRAMED_RENDERER";
    public const string ManifestDictionaryKey =
        "AK_DEV_FRAMED_RENDERER_PROOF_MANIFEST";
    public const string FailureCaseNotTested =
        "NOT TESTED - a normal Architecture DWG always exposes a compatible fallback style";
    public const string FitPass = "FIT PASS";
    public const string ExpectedOverflowPass = "EXPECTED OVERFLOW PASS";
    public const string NotTested = "NOT TESTED";
    public const string PreservationPass = "PRESERVATION PASS";
    public const string CircleInvariantPass = "CIRCLE INVARIANT PASS";

    // E: fixed Resolve-based Large token (Resolve(Rectangle, VT1234) → Large,
    //    font-independent because Resolve uses estimated character width).
    public const string RectangleLargeItemText = "VT1234";
    public const string RectangleLargeItemPrefix = "VT";

    // J: long Circle token — Resolve(Circle, *) always yields Small.
    //    Used to prove shared definition remains unchanged regardless of token.
    public const string CircleLongInvariantText = "WWWWWWWW2147483647";

    public static IReadOnlyList<AutoCadFramedRendererTokenCandidate>
        RectangleLargeFitCandidates { get; } =
    [
        new(RectangleLargeItemText, RectangleLargeItemPrefix),
    ];

    public static IReadOnlyList<AutoCadFramedRendererTokenCandidate>
        RectangleOverflowCandidates { get; } =
    [
        new("WWWWWWWW2147483647", "WWWWWWWW"),
    ];

    public static AutoCadFramedRendererProofCase RectangleLargeCaseTemplate {
        get;
    } = new(
        "E",
        TimberAnnotationMode.ItemNumberLeader,
        ItemNumberLeaderStyle.Rectangle,
        TimberMainAnnotationComponentRole.Primary,
        0,
        2d,
        50,
        RectangleLargeItemText,
        TimberElementType.Custom,
        RectangleLargeItemPrefix);

    public static IReadOnlyList<AutoCadFramedRendererProofCase> Cases { get; } =
    [
        new("A", TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle,
            TimberMainAnnotationComponentRole.Primary, 0, 2d, 50, "A"),
        new("B", TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle,
            TimberMainAnnotationComponentRole.Primary, 0, 2d, 100, "B"),
        new("C", TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle,
            TimberMainAnnotationComponentRole.Primary, 1, 3.2d, 50, "C"),
        new("D", TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Slot,
            TimberMainAnnotationComponentRole.Primary, 0, 2d, 50, "D"),
        new("F", TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Circle,
            TimberMainAnnotationComponentRole.FramedItem, 0, 2d, 50, "F"),
        new("G", TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle,
            TimberMainAnnotationComponentRole.Primary, 0, 2d, 50, "G"),
        new("H1", TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle,
            TimberMainAnnotationComponentRole.Primary, 0, 2d, 50, "H1"),
        new("H2", TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle,
            TimberMainAnnotationComponentRole.Primary, 0, 2d, 50, "H2"),
        new("H3", TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle,
            TimberMainAnnotationComponentRole.Primary, 0, 2d, 50, "H3"),
    ];

    public static AutoCadFramedRendererTokenSelection
        SelectRectangleLargeFitCandidate(
            Func<string, double?> measureTextWidth,
            double mediumInnerWidthMm,
            double largeInnerWidthMm) =>
        SelectCandidate(
            RectangleLargeFitCandidates,
            measureTextWidth,
            width => width > mediumInnerWidthMm &&
                width <= largeInnerWidthMm,
            "No valid deterministic token measured wider than Rectangle " +
            "Medium and no wider than Rectangle Large.");

    public static AutoCadFramedRendererTokenSelection
        SelectRectangleOverflowCandidate(
            Func<string, double?> measureTextWidth,
            double largeInnerWidthMm) =>
        SelectCandidate(
            RectangleOverflowCandidates,
            measureTextWidth,
            width => width > largeInnerWidthMm,
            "No valid deterministic token measured wider than Rectangle Large.");

    private static AutoCadFramedRendererTokenSelection SelectCandidate(
        IReadOnlyList<AutoCadFramedRendererTokenCandidate> candidates,
        Func<string, double?> measureTextWidth,
        Func<double, bool> matchesRequestedRange,
        string notTestedReason)
    {
        ArgumentNullException.ThrowIfNull(measureTextWidth);
        var attempts = new List<AutoCadFramedRendererTokenAttempt>();
        foreach (var candidate in candidates)
        {
            var parsedNumber = TimberElementIdentityRules
                .TryParseElementNumber(candidate.ItemText, candidate.Prefix);
            var isValid = parsedNumber is > 0 && string.Equals(
                TimberElementIdentityRules.CreateElementId(
                    candidate.Prefix,
                    parsedNumber.Value),
                candidate.ItemText,
                StringComparison.Ordinal);
            var measuredWidth = isValid
                ? measureTextWidth(candidate.ItemText)
                : null;
            var matches = measuredWidth.HasValue &&
                double.IsFinite(measuredWidth.Value) &&
                matchesRequestedRange(measuredWidth.Value);
            attempts.Add(new AutoCadFramedRendererTokenAttempt(
                candidate.ItemText,
                candidate.Prefix,
                isValid,
                measuredWidth,
                matches,
                !isValid
                    ? "Candidate is not a canonical production ITEM_NO token."
                    : !measuredWidth.HasValue ||
                        !double.IsFinite(measuredWidth.Value)
                        ? "Text measurement was unavailable."
                        : matches
                            ? "Candidate matches the requested measured range."
                            : "Measured width is outside the requested range."));
            if (matches)
            {
                return new AutoCadFramedRendererTokenSelection(
                    candidate,
                    measuredWidth,
                    attempts.AsReadOnly(),
                    "Selected the first valid deterministic candidate in the requested range.");
            }
        }

        return new AutoCadFramedRendererTokenSelection(
            null,
            null,
            attempts.AsReadOnly(),
            notTestedReason);
    }
}
#endif
