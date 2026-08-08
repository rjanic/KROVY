#if DEBUG
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum AutoCadFramedG5CachingMode
{
    SharedVariant,
    PerInstance,
}

internal enum AutoCadFramedG5StyleKind
{
    ArialPreset,
    TimesNewRoman,
    ClassicShx,
}

internal sealed record AutoCadFramedG5ProofCase(
    string Token,
    AutoCadFramedG5CachingMode CachingMode,
    ItemNumberLeaderStyle FrameKind,
    string ItemText,
    AutoCadFramedG5StyleKind StyleKind,
    double PaperHeightMm,
    int Denominator);

internal static class AutoCadFramedG5ProofPolicy
{
    public const int SchemaVersion = 1;
    public const string SuiteIdentifier = "AK_DEV_FRAMED_G5_PROOF";
    public const string RegAppName = "AK_DEV_FRAMED_G5";
    public const string ReportFileName = "g5-framed-host-proof-report.json";
    public const string TimesNewRomanStyleName = "AK_G5_PROOF_TIMESNR";
    public const string TimesNewRomanFontFile = "Times New Roman";
    public const double GeometryToleranceMm = 0.05d;
    public const double HeightToleranceMm = 0.05d;

    /// <summary>
    /// Representative matrix covering frames, styles, scales and both caching
    /// strategies without a full cartesian explosion.
    /// </summary>
    public static IReadOnlyList<AutoCadFramedG5ProofCase> Cases { get; } =
    [
        // Shared variant — frame kinds
        new("S-CIR-AR50", AutoCadFramedG5CachingMode.SharedVariant,
            ItemNumberLeaderStyle.Circle, "K1",
            AutoCadFramedG5StyleKind.ArialPreset, 2.7d, 50),
        new("S-REC-AR50", AutoCadFramedG5CachingMode.SharedVariant,
            ItemNumberLeaderStyle.Rectangle, "P8",
            AutoCadFramedG5StyleKind.ArialPreset, 2.7d, 50),
        new("S-SLT-AR50", AutoCadFramedG5CachingMode.SharedVariant,
            ItemNumberLeaderStyle.Slot, "S1",
            AutoCadFramedG5StyleKind.ArialPreset, 2.7d, 50),
        // Shared — styles
        new("S-CIR-TN50", AutoCadFramedG5CachingMode.SharedVariant,
            ItemNumberLeaderStyle.Circle, "K2",
            AutoCadFramedG5StyleKind.TimesNewRoman, 2.7d, 50),
        new("S-CIR-SH50", AutoCadFramedG5CachingMode.SharedVariant,
            ItemNumberLeaderStyle.Circle, "K3",
            AutoCadFramedG5StyleKind.ClassicShx, 2.7d, 50),
        // Shared — heights / denominators
        new("S-CIR-AR25", AutoCadFramedG5CachingMode.SharedVariant,
            ItemNumberLeaderStyle.Circle, "H25",
            AutoCadFramedG5StyleKind.ArialPreset, 2.7d, 25),
        new("S-CIR-AR100", AutoCadFramedG5CachingMode.SharedVariant,
            ItemNumberLeaderStyle.Circle, "H100",
            AutoCadFramedG5StyleKind.ArialPreset, 2.7d, 100),
        // Per-instance cohort (same coverage axes)
        new("P-CIR-AR50", AutoCadFramedG5CachingMode.PerInstance,
            ItemNumberLeaderStyle.Circle, "K1",
            AutoCadFramedG5StyleKind.ArialPreset, 2.7d, 50),
        new("P-REC-AR50", AutoCadFramedG5CachingMode.PerInstance,
            ItemNumberLeaderStyle.Rectangle, "P8",
            AutoCadFramedG5StyleKind.ArialPreset, 2.7d, 50),
        new("P-SLT-AR50", AutoCadFramedG5CachingMode.PerInstance,
            ItemNumberLeaderStyle.Slot, "S1",
            AutoCadFramedG5StyleKind.ArialPreset, 2.7d, 50),
        new("P-CIR-TN50", AutoCadFramedG5CachingMode.PerInstance,
            ItemNumberLeaderStyle.Circle, "K2",
            AutoCadFramedG5StyleKind.TimesNewRoman, 2.7d, 50),
        new("P-CIR-SH50", AutoCadFramedG5CachingMode.PerInstance,
            ItemNumberLeaderStyle.Circle, "K3",
            AutoCadFramedG5StyleKind.ClassicShx, 2.7d, 50),
        new("P-CIR-AR25", AutoCadFramedG5CachingMode.PerInstance,
            ItemNumberLeaderStyle.Circle, "H25",
            AutoCadFramedG5StyleKind.ArialPreset, 2.7d, 25),
        new("P-CIR-AR100", AutoCadFramedG5CachingMode.PerInstance,
            ItemNumberLeaderStyle.Circle, "H100",
            AutoCadFramedG5StyleKind.ArialPreset, 2.7d, 100),
    ];

    public static double ExpectedModelHeightMm(AutoCadFramedG5ProofCase proofCase) =>
        TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            proofCase.PaperHeightMm,
            proofCase.Denominator);

    public static TimberItemLeaderBlockDefinition ResolveFrame(
        AutoCadFramedG5ProofCase proofCase) =>
        TimberItemLeaderBlockDefinitionRules.Resolve(
            proofCase.FrameKind,
            proofCase.ItemText);

    public static string CreateSharedVariantBlockName(
        AutoCadFramedG5ProofCase proofCase,
        TimberItemLeaderBlockDefinition definition,
        string textStyleStableKey) =>
        "AK_G5_V_" +
        Sanitize(
            $"{proofCase.FrameKind}_{definition.Size}_" +
            $"{textStyleStableKey}_P{Format(proofCase.PaperHeightMm)}_" +
            $"D{proofCase.Denominator}");

    public static string CreatePerInstanceBlockName(string token) =>
        "AK_G5_I_" + Sanitize(token) + "_" + Guid.NewGuid().ToString("N")[..8];

    private static string Format(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string Sanitize(string value)
    {
        var chars = value
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        var sanitized = new string(chars);
        return sanitized.Length <= 48 ? sanitized : sanitized[..48];
    }
}
#endif
