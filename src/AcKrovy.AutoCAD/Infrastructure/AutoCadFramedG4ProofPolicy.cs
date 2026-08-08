#if DEBUG
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed record AutoCadFramedG4ProofCase(
    string Token,
    ItemNumberLeaderStyle FrameKind,
    string ItemText,
    string StyleName,
    double PaperHeightMm,
    int Denominator,
    bool Combined);

internal sealed record AutoCadFramedG4ProofManifest(
    int SchemaVersion,
    string SuiteIdentifier,
    IReadOnlyList<AutoCadFramedG4ProofCase> Cases);

internal static class AutoCadFramedG4ProofPolicy
{
    public const int SchemaVersion = 1;
    public const string SuiteIdentifier = "AK_DEV_FRAMED_G4_PROOF";
    public const string RegAppName = "AK_DEV_FRAMED_G4";
    public const string ManifestDictionaryKey = "AK_DEV_FRAMED_G4_PROOF_MANIFEST";

    public static IReadOnlyList<AutoCadFramedG4ProofCase> Cases { get; } =
    [
        new("A", ItemNumberLeaderStyle.Circle, "K1",
            TimberAnnotationTextStylePresetRules.ClassicStyleName, 2.7d, 50, false),
        new("B", ItemNumberLeaderStyle.Circle, "K2",
            TimberAnnotationTextStylePresetRules.ClassicStyleName, 3.5d, 50, false),
        new("C", ItemNumberLeaderStyle.Rectangle, "P8",
            TimberAnnotationTextStylePresetRules.ArchitecturalStyleName, 3.0d, 50, false),
        new("D", ItemNumberLeaderStyle.Slot, "S1",
            TimberAnnotationTextStylePresetRules.TechnicalStyleName, 2.5d, 100, false),
        new("E", ItemNumberLeaderStyle.Rectangle, "W3",
            TimberAnnotationTextStylePresetRules.BuildAutoCadTextStyleName("PROOF"), 3.5d, 50, false),
        // Unique ItemText/ElementId remains a defense-in-depth for the proof suite.
        // Production matching is SourceHandle-first via TimberFramedG4CompositeMatchRules.
        new("F", ItemNumberLeaderStyle.Circle, "KF",
            TimberAnnotationTextStylePresetRules.ClassicStyleName, 3.5d, 50, true),
    ];

    public static AutoCadFramedG4ProofManifest CreateManifest() =>
        new(SchemaVersion, SuiteIdentifier, Cases);

    public static double ExpectedModelHeightMm(AutoCadFramedG4ProofCase proofCase) =>
        TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            proofCase.PaperHeightMm,
            proofCase.Denominator);
}
#endif
