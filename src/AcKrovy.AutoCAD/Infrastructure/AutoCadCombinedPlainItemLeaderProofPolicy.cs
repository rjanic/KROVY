#if DEBUG
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed record AutoCadCombinedPlainItemLeaderProofCase(
    string Token,
    int StyleSlot,
    TimberAnnotationTextSettings? TextSettings,
    int? DenominatorOverride,
    bool ExpectRefreshSameObjectId,
    bool IsFailurePreservationCase,
    bool IsStandaloneRegressionCase);

internal sealed record AutoCadCombinedPlainItemLeaderProofExpectedCase(
    string Token,
    string StyleName,
    double ItemPaperHeightMm,
    int Denominator,
    double ItemModelHeightMm,
    double DimensionsModelHeightMm,
    string ResolutionKind,
    bool IsFallback,
    bool ExpectRefreshSameObjectId,
    bool IsFailurePreservationCase,
    bool IsStandaloneRegressionCase,
    string FailureOutcome);

internal sealed record AutoCadCombinedPlainItemLeaderProofManifest(
    int SchemaVersion,
    string SuiteIdentifier,
    IReadOnlyList<AutoCadCombinedPlainItemLeaderProofExpectedCase> Cases);

internal static class AutoCadCombinedPlainItemLeaderProofPolicy
{
    public const int SchemaVersion = 1;
    public const string SuiteIdentifier =
        "AK_DEV_COMBINED_PLAIN_ITEM_TEXT_PROOF";
    public const string RegAppName = "AK_DEV_COMBINED_PLAIN_ITEM_TEXT";
    public const string ManifestDictionaryKey =
        "AK_DEV_COMBINED_PLAIN_ITEM_TEXT_PROOF_MANIFEST";
    public const string FailureCaseNotTested = "NOT_TESTED";
    public const string FailureCasePreserved = "PRESERVED";
    public const string RefreshToken = "D";

    public static IReadOnlyList<AutoCadCombinedPlainItemLeaderProofCase> Cases { get; } =
    [
        new(
            "A",
            StyleSlot: -1,
            TextSettings: null,
            DenominatorOverride: 50,
            ExpectRefreshSameObjectId: true,
            IsFailurePreservationCase: false,
            IsStandaloneRegressionCase: false),
        new(
            "B",
            StyleSlot: 0,
            TextSettings: TimberAnnotationTextSettings.Shared(
                "PLACEHOLDER",
                3d,
                TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm),
            DenominatorOverride: 50,
            ExpectRefreshSameObjectId: false,
            IsFailurePreservationCase: false,
            IsStandaloneRegressionCase: false),
        new(
            "C",
            StyleSlot: 0,
            TextSettings: TimberAnnotationTextSettings.Shared(
                "PLACEHOLDER",
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm),
            DenominatorOverride: 100,
            ExpectRefreshSameObjectId: false,
            IsFailurePreservationCase: false,
            IsStandaloneRegressionCase: false),
        new(
            "E",
            StyleSlot: -1,
            TextSettings: null,
            DenominatorOverride: 50,
            ExpectRefreshSameObjectId: false,
            IsFailurePreservationCase: true,
            IsStandaloneRegressionCase: false),
        new(
            "F",
            StyleSlot: -1,
            TextSettings: null,
            DenominatorOverride: 50,
            ExpectRefreshSameObjectId: false,
            IsFailurePreservationCase: false,
            IsStandaloneRegressionCase: true),
    ];

    public static AutoCadCombinedPlainItemLeaderProofExpectedCase ToExpected(
        AutoCadCombinedPlainItemLeaderProofCase proofCase,
        string styleName,
        double itemPaperHeightMm,
        int denominator,
        string resolutionKind,
        bool isFallback,
        string failureOutcome = "") =>
        new(
            proofCase.Token,
            styleName,
            itemPaperHeightMm,
            denominator,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                itemPaperHeightMm,
                denominator),
            TimberCombinedDimensionTypographyRules.CalculateTextHeightMm(
                TimberAnnotationScaleRules.GetScaleFactor(denominator)),
            resolutionKind,
            isFallback,
            proofCase.ExpectRefreshSameObjectId,
            proofCase.IsFailurePreservationCase,
            proofCase.IsStandaloneRegressionCase,
            failureOutcome);
}
#endif
