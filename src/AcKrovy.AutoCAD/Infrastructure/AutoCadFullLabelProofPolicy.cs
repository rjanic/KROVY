#if DEBUG
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed record AutoCadFullLabelProofCase(
    string Token,
    bool IsPostFootprint,
    int StyleSlot,
    TimberAnnotationTextSettings? TextSettings,
    int? DenominatorOverride,
    bool ExpectRefreshSameObjectId);

internal sealed record AutoCadFullLabelProofExpectedCase(
    string Token,
    bool IsPostFootprint,
    string StyleName,
    double PaperHeightMm,
    int Denominator,
    double ModelHeightMm,
    string ResolutionKind,
    bool IsFallback,
    bool ExpectRefreshSameObjectId);

internal sealed record AutoCadFullLabelProofManifest(
    int SchemaVersion,
    string SuiteIdentifier,
    IReadOnlyList<AutoCadFullLabelProofExpectedCase> Cases);

internal static class AutoCadFullLabelProofPolicy
{
    public const int SchemaVersion = 1;
    public const string SuiteIdentifier = "AK_DEV_FULLLABEL_TEXT_PROOF";
    public const string RegAppName = "AK_DEV_FULLLABEL_TEXT";
    public const string ManifestDictionaryKey =
        "AK_DEV_FULLLABEL_TEXT_PROOF_MANIFEST";
    public const string FailureCaseNotTested = "NOT_TESTED";

    /// <summary>
    /// Matches the rectangular polyline created for case D and the production
    /// <see cref="TimberPostFootprintAssignmentRules.CreateMetadata"/> contract.
    /// </summary>
    public const double PostFootprintSizeMm = 300d;
    public const int PostFootprintWidthEdgeIndex = 0;

    public static IReadOnlyList<AutoCadFullLabelProofCase> Cases { get; } =
    [
        new(
            "A",
            IsPostFootprint: false,
            StyleSlot: -1,
            TextSettings: null,
            DenominatorOverride: null,
            ExpectRefreshSameObjectId: true),
        new(
            "B",
            IsPostFootprint: false,
            StyleSlot: 0,
            TextSettings: TimberAnnotationTextSettings.Shared(
                "PLACEHOLDER",
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
                3d,
                TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm),
            DenominatorOverride: null,
            ExpectRefreshSameObjectId: false),
        new(
            "C",
            IsPostFootprint: false,
            StyleSlot: 0,
            TextSettings: TimberAnnotationTextSettings.Shared(
                "PLACEHOLDER",
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm),
            DenominatorOverride: 100,
            ExpectRefreshSameObjectId: false),
        new(
            "D",
            IsPostFootprint: true,
            StyleSlot: 1,
            TextSettings: TimberAnnotationTextSettings.Shared(
                "PLACEHOLDER",
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
                2.5d,
                TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm),
            DenominatorOverride: null,
            ExpectRefreshSameObjectId: false),
    ];

    public static TimberRectangularFootprintGeometry CreatePostFootprintGeometry()
    {
        var size = PostFootprintSizeMm;
        var points = new[]
        {
            new TimberRectangularFootprintPoint(0d, 0d),
            new TimberRectangularFootprintPoint(size, 0d),
            new TimberRectangularFootprintPoint(size, size),
            new TimberRectangularFootprintPoint(0d, size),
        };
        var validation = TimberRectangularFootprintValidator.Validate(points);
        return validation.Geometry ??
            throw new InvalidOperationException(
                "FullLabel proof post-footprint geometry is invalid.");
    }

    public static TimberRectangularFootprintDimensions CreatePostFootprintDimensions() =>
        TimberRectangularFootprintEdgeRules.ResolveDimensions(
            CreatePostFootprintGeometry(),
            PostFootprintWidthEdgeIndex);

    /// <summary>
    /// Builds the same metadata contract as
    /// <c>PostFootprintAssignmentWorkflow</c>: explicit ManualLength mode with
    /// the production default vertical length and footprint edge dimensions.
    /// </summary>
    public static TimberElementData CreatePostFootprintElementData(
        string elementId,
        TimberAnnotationTextSettings? textSettings,
        int? annotationScaleDenominatorOverride,
        TimberElementDefaultProfile? defaultProfile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);

        var profile = defaultProfile ?? TimberElementDefaultProfile.CreateDefault();
        var source = TimberElementDefaults.For(TimberElementType.Post, profile) with
        {
            ElementId = elementId,
            AnnotationMode = TimberAnnotationMode.FullLabel,
            ItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain,
            AnnotationTextSettings = textSettings,
            AnnotationScaleDenominatorOverride =
                annotationScaleDenominatorOverride,
            LengthCalculationMode = LengthCalculationMode.ManualLength,
            ManualLengthMm = null,
            RoofPlaneId = "AK_DEV",
        };

        return TimberPostFootprintAssignmentRules.CreateMetadata(
            source,
            CreatePostFootprintDimensions());
    }

    public static AutoCadFullLabelProofExpectedCase ToExpected(
        AutoCadFullLabelProofCase proofCase,
        string styleName,
        double paperHeightMm,
        int denominator,
        string resolutionKind,
        bool isFallback) =>
        new(
            proofCase.Token,
            proofCase.IsPostFootprint,
            styleName,
            paperHeightMm,
            denominator,
            TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
                paperHeightMm,
                denominator),
            resolutionKind,
            isFallback,
            proofCase.ExpectRefreshSameObjectId);
}
#endif
