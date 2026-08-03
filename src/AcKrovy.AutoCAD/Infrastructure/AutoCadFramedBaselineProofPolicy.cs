#if DEBUG
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// One style/height/denominator combination tested in the framed-baseline
/// shared-definition matrix proof.
/// </summary>
internal sealed record AutoCadFramedBaselineSlot(
    string CanonicalStyleName,
    double PaperHeightMm,
    int Denominator)
{
    /// <summary>
    /// BlockScale = denominator / DefaultDenominator (50).
    /// </summary>
    public double BlockScale =>
        Denominator / (double)TimberAnnotationScaleRules.DefaultDenominator;

    /// <summary>
    /// Per-instance AttributeReference height = paperHeight × denominator.
    /// </summary>
    public double AttributeReferenceHeightMm => PaperHeightMm * Denominator;

    public string Description =>
        $"style={CanonicalStyleName}; paper={PaperHeightMm:R}; denom={Denominator}; " +
        $"blockScale={BlockScale:R}; attrH={AttributeReferenceHeightMm:R}";
}

/// <summary>
/// One persisted record in the framed-baseline manifest: maps a
/// (frame, token, slotIndex) triple to the shared block definition name
/// and the expected per-instance measurements.
/// </summary>
internal sealed record AutoCadFramedBaselineManifestEntry(
    string FrameStyleName,
    string ItemToken,
    int SlotIndex,
    string BlockName,
    double DefinitionHeightMm,
    double AttributeReferenceHeightMm,
    double BlockScale,
    string StyleName);

/// <summary>
/// Persisted manifest for AK_DEV_FRAMED_BASELINE_CREATE.
/// </summary>
internal sealed record AutoCadFramedBaselineManifest(
    int SchemaVersion,
    string SuiteIdentifier,
    IReadOnlyList<string> SlotDescriptions,
    IReadOnlyList<AutoCadFramedBaselineManifestEntry> Entries);

internal static class AutoCadFramedBaselineProofPolicy
{
    public const int SchemaVersion = 1;
    public const string SuiteIdentifier = "AK_DEV_FRAMED_BASELINE";
    public const string ManifestDictionaryKey = "AK_DEV_FRAMED_BASELINE_MANIFEST";
    public const string RegAppName = "AK_DEV_FRAMED_BASELINE";
    public const string SharedDefinitionPass = "SHARED DEFINITION PASS";
    public const string DenomOnlyBlockScalePass = "DENOM ONLY BLOCKSCALE PASS";

    /// <summary>
    /// The (frame-style, token) pairs that form the rows of the proof matrix.
    /// </summary>
    public static IReadOnlyList<(ItemNumberLeaderStyle Style, string Token)>
        MatrixKeys { get; } =
    [
        (ItemNumberLeaderStyle.Circle, "K1"),
        (ItemNumberLeaderStyle.Circle, "K10"),
        (ItemNumberLeaderStyle.Rectangle, "K1"),
        (ItemNumberLeaderStyle.Rectangle, "K10"),
        (ItemNumberLeaderStyle.Slot, "K1"),
        (ItemNumberLeaderStyle.Slot, "K10"),
    ];

    /// <summary>
    /// Paper heights probed per text style (mm at scale 1:1 / denominator).
    /// </summary>
    public static IReadOnlyList<double> PaperHeightsMm { get; } = [2.7d, 3.5d];

    /// <summary>
    /// Annotation denominators: 50 (default scale) and 100 (double scale).
    /// Used to prove that changing denominator changes only BlockScale,
    /// not the shared BlockContentId.
    /// </summary>
    public static IReadOnlyList<int> Denominators { get; } = [50, 100];

    /// <summary>
    /// Case-insensitive name substrings used to prefer specific styles
    /// when building slots. Falls back to the first distinct styles found.
    /// </summary>
    public static IReadOnlyList<string> PreferredStyleSubstrings { get; } =
        ["Arial", "TNR", "Times", "Courier"];

    public static string GetFrameStyleName(ItemNumberLeaderStyle style) =>
        style switch
        {
            ItemNumberLeaderStyle.Circle => "CIR",
            ItemNumberLeaderStyle.Rectangle => "RECT",
            ItemNumberLeaderStyle.Slot => "SLOT",
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };

    public static string GetRowKey(ItemNumberLeaderStyle style, string token) =>
        $"{GetFrameStyleName(style)}|{token}";
}
#endif
