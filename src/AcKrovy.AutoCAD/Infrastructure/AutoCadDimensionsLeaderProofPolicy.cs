#if DEBUG
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum AutoCadDimensionsLeaderProofKind
{
    DimensionsLeader,
    StandalonePlainItemRegression,
}

internal sealed record AutoCadDimensionsLeaderProofCase(
    string Token,
    int StyleSlot,
    TimberAnnotationTextSettings? TextSettings,
    int? DenominatorOverride,
    bool ExpectRefreshSameObjectId,
    bool IsFailurePreservationCase,
    AutoCadDimensionsLeaderProofKind Kind);

internal sealed record AutoCadDimensionsLeaderProofExpectedCase(
    string Token,
    string StyleName,
    double PaperHeightMm,
    int Denominator,
    double ModelHeightMm,
    string ResolutionKind,
    bool IsFallback,
    bool ExpectRefreshSameObjectId,
    bool IsFailurePreservationCase,
    AutoCadDimensionsLeaderProofKind Kind,
    string FailureOutcome);

internal sealed record AutoCadDimensionsLeaderProofManifest(
    int SchemaVersion,
    string SuiteIdentifier,
    IReadOnlyList<AutoCadDimensionsLeaderProofExpectedCase> Cases);

internal static class AutoCadDimensionsLeaderProofPolicy
{
    public const int SchemaVersion = 1;
    public const string SuiteIdentifier = "AK_DEV_DIMENSIONS_LEADER_TEXT_PROOF";
    public const string RegAppName = "AK_DEV_DIMENSIONS_LEADER_TEXT";
    public const string ManifestDictionaryKey =
        "AK_DEV_DIMENSIONS_LEADER_TEXT_PROOF_MANIFEST";
    public const string FailureCaseNotTested = "NOT_TESTED";
    public const string FailureCasePreserved = "PRESERVED";
    public const string RefreshToken = "A";

    public static IReadOnlyList<AutoCadDimensionsLeaderProofCase> Cases { get; } =
    [
        new(
            "A",
            StyleSlot: -1,
            TextSettings: null,
            DenominatorOverride: 50,
            ExpectRefreshSameObjectId: true,
            IsFailurePreservationCase: false,
            Kind: AutoCadDimensionsLeaderProofKind.DimensionsLeader),
        new(
            "B",
            StyleSlot: 0,
            TextSettings: new TimberAnnotationTextSettings(
                "PLACEHOLDER",
                3d,
                TimberAnnotationTextSettingsRules.DefaultItemNumberPaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultSlopeAnglePaperHeightMm),
            DenominatorOverride: 50,
            ExpectRefreshSameObjectId: false,
            IsFailurePreservationCase: false,
            Kind: AutoCadDimensionsLeaderProofKind.DimensionsLeader),
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
            IsFailurePreservationCase: false,
            Kind: AutoCadDimensionsLeaderProofKind.DimensionsLeader),
        new(
            "E",
            StyleSlot: -1,
            TextSettings: null,
            DenominatorOverride: 50,
            ExpectRefreshSameObjectId: false,
            IsFailurePreservationCase: true,
            Kind: AutoCadDimensionsLeaderProofKind.DimensionsLeader),
        new(
            "F",
            StyleSlot: -1,
            TextSettings: null,
            DenominatorOverride: 50,
            ExpectRefreshSameObjectId: false,
            IsFailurePreservationCase: false,
            Kind: AutoCadDimensionsLeaderProofKind.StandalonePlainItemRegression),
    ];

    public static AutoCadDimensionsLeaderProofExpectedCase ToExpected(
        AutoCadDimensionsLeaderProofCase proofCase,
        string styleName,
        double paperHeightMm,
        int denominator,
        string resolutionKind,
        bool isFallback,
        string failureOutcome = "")
    {
        return new(
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
            proofCase.Kind,
            failureOutcome);
    }
}
#endif
