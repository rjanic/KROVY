#if DEBUG
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed record AutoCadPlainItemLeaderProofCase(
    string Token,
    int StyleSlot,
    TimberAnnotationTextSettings? TextSettings,
    int? DenominatorOverride,
    bool ExpectRefreshSameObjectId,
    bool IsFailurePreservationCase);

internal sealed record AutoCadPlainItemLeaderProofExpectedCase(
    string Token,
    string StyleName,
    double PaperHeightMm,
    int Denominator,
    double ModelHeightMm,
    string ResolutionKind,
    bool IsFallback,
    bool ExpectRefreshSameObjectId,
    bool IsFailurePreservationCase,
    string FailureOutcome);

internal sealed record AutoCadPlainItemLeaderProofManifest(
    int SchemaVersion,
    string SuiteIdentifier,
    IReadOnlyList<AutoCadPlainItemLeaderProofExpectedCase> Cases);

internal static class AutoCadPlainItemLeaderProofPolicy
{
    public const int SchemaVersion = 1;
    public const string SuiteIdentifier = "AK_DEV_PLAIN_ITEM_TEXT_PROOF";
    public const string RegAppName = "AK_DEV_PLAIN_ITEM_TEXT";
    public const string ManifestDictionaryKey =
        "AK_DEV_PLAIN_ITEM_TEXT_PROOF_MANIFEST";
    public const string FailureCaseNotTested = "NOT_TESTED";
    public const string FailureCasePreserved = "PRESERVED";
    public const string RefreshToken = "D";

    public static IReadOnlyList<AutoCadPlainItemLeaderProofCase> Cases { get; } =
    [
        new(
            "A",
            StyleSlot: -1,
            TextSettings: null,
            DenominatorOverride: 50,
            ExpectRefreshSameObjectId: true,
            IsFailurePreservationCase: false),
        new(
            "B",
            StyleSlot: 0,
            TextSettings: new TimberAnnotationTextSettings(
                "PLACEHOLDER",
                TimberAnnotationTextSettingsRules
                    .DefaultLabelAndDimensionPaperHeightMm,
                3d,
                TimberAnnotationTextSettingsRules.DefaultSlopeAnglePaperHeightMm),
            DenominatorOverride: 50,
            ExpectRefreshSameObjectId: false,
            IsFailurePreservationCase: false),
        new(
            "C",
            StyleSlot: 0,
            TextSettings: new TimberAnnotationTextSettings(
                "PLACEHOLDER",
                TimberAnnotationTextSettingsRules
                    .DefaultLabelAndDimensionPaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultItemNumberPaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultSlopeAnglePaperHeightMm),
            DenominatorOverride: 100,
            ExpectRefreshSameObjectId: false,
            IsFailurePreservationCase: false),
        new(
            "E",
            StyleSlot: -1,
            TextSettings: null,
            DenominatorOverride: 50,
            ExpectRefreshSameObjectId: false,
            IsFailurePreservationCase: true),
    ];

    public static AutoCadPlainItemLeaderProofExpectedCase ToExpected(
        AutoCadPlainItemLeaderProofCase proofCase,
        string styleName,
        double paperHeightMm,
        int denominator,
        string resolutionKind,
        bool isFallback,
        string failureOutcome = "") =>
        new(
            proofCase.Token,
            styleName,
            paperHeightMm,
            denominator,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                paperHeightMm,
                denominator),
            resolutionKind,
            isFallback,
            proofCase.ExpectRefreshSameObjectId,
            proofCase.IsFailurePreservationCase,
            failureOutcome);
}
#endif
