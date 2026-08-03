#if DEBUG
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum AutoCadTextSettingsProofKind
{
    ItemPlain,
    ItemCircle,
    ItemRectangle,
    ItemSlot,
    CombinedFramed,
    FullLabel,
    DimensionsLeader,
    SlopeNumeric,
    HorizontalMarker,
    PostPerpendicular,
    ItemUserPreset,
    RoleIsolation,
}

internal sealed record AutoCadTextSettingsProofCase(
    string Token,
    AutoCadTextSettingsProofKind Kind,
    TimberAnnotationMode AnnotationMode,
    ItemNumberLeaderStyle ItemStyle,
    TimberElementType ElementType,
    double SlopeDegrees,
    int Denominator,
    TimberAnnotationTextSettings TextSettings,
    bool UsesUserPreset,
    bool IsRoleIsolation);

internal sealed record AutoCadTextSettingsProofRoleExpectation(
    string Role,
    string RequestedStyleName,
    string ResolvedStyleName,
    double PaperHeightMm,
    int Denominator,
    double ModelHeightMm,
    string EntityType,
    string? BlockName,
    string? FrameSize,
    double? BlockScale,
    string? ObjectIdHandle,
    bool ExpectUnchangedObjectId);

internal sealed record AutoCadTextSettingsProofExpectedCase(
    string Token,
    AutoCadTextSettingsProofKind Kind,
    IReadOnlyList<AutoCadTextSettingsProofRoleExpectation> Roles);

internal sealed record AutoCadTextSettingsProofStandardSnapshot(
    string Name,
    string FontFileName,
    double TextSize,
    double XScale,
    double ObliquingAngle);

internal sealed record AutoCadTextSettingsProofManifest(
    int SchemaVersion,
    string SuiteIdentifier,
    string UserPresetStyleName,
    string UserPresetFontFile,
    AutoCadTextSettingsProofStandardSnapshot? StandardBefore,
    IReadOnlyList<AutoCadTextSettingsProofExpectedCase> Cases,
    string? UserPresetLibrarySnapshotJson = null,
    bool ProofOwnedUserPresetLibraryMutation = false,
    bool ProofCreatedUserTextStyle = false,
    string? SharedUserFramedBlockContentHandle = null,
    string? UserPresetStableId = null);

/// <summary>
/// Compact but complete CREATE matrix for three-role Text Settings host proof.
/// </summary>
internal static class AutoCadTextSettingsProofPolicy
{
    public const int SchemaVersion = 1;
    public const string SuiteIdentifier = "AK_DEV_TEXT_SETTINGS_PROOF";
    public const string RegAppName = "AK_DEV_TEXT_SETTINGS";
    public const string ManifestDictionaryKey =
        "AK_DEV_TEXT_SETTINGS_PROOF_MANIFEST";
    public const string FailureCaseNotTested = "NOT_TESTED";
    public const string ElementIdPrefix = "TS-";
    /// <summary>
    /// Stable id for the temporary DEBUG user preset used by IU / IUFR / IUFR2.
    /// Display name is never part of G3 identity.
    /// </summary>
    public const string UserPresetStableId = "g3_host_user";
    public const string UserPresetDisplayName = "G3 Host User";
    public const string PreferredUserPresetFont = "Calibri";
    public const string UserFramedToken = "IUFR";
    public const string UserFramedTwinToken = "IUFR2";
    public const string HorizontalMarkerBlockName =
        "DECORAIR_ACADKROVY_HORIZONTAL_SLOPE_MARKER";
    public const string PostPerpendicularMarkerBlockName =
        "DECORAIR_ACADKROVY_POST_90_MARKER_V3";
    public const string RoleIsolationToken = "RI";
    /// <summary>
    /// Dimension paper height applied by RI after baseline CREATE.
    /// ItemCode stays at <see cref="TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm"/>
    /// (2.7 → model/attrH 135 @ 1:50).
    /// </summary>
    public const double RoleIsolationPatchedDimensionHeightMm = 3.0d;
    /// <summary>
    /// Slope paper height applied by RI after baseline CREATE
    /// (independent of Dimension and of untouched ItemCode).
    /// </summary>
    public const double RoleIsolationPatchedSlopeHeightMm = 2.5d;
    public const double SlopeArchTallerHeightMm = 2.5d;

    public static IReadOnlyList<AutoCadTextSettingsProofCase> Cases { get; } =
    [
        new(
            "IP",
            AutoCadTextSettingsProofKind.ItemPlain,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain,
            TimberElementType.Rafter,
            SlopeDegrees: 35d,
            Denominator: 50,
            TextSettings: SharedClassic(
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm),
            UsesUserPreset: false,
            IsRoleIsolation: false),
        new(
            "IC",
            AutoCadTextSettingsProofKind.ItemCircle,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Circle,
            TimberElementType.Rafter,
            SlopeDegrees: 35d,
            Denominator: 50,
            TextSettings: SharedClassic(
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm),
            UsesUserPreset: false,
            IsRoleIsolation: false),
        new(
            "IR",
            AutoCadTextSettingsProofKind.ItemRectangle,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Rectangle,
            TimberElementType.Rafter,
            SlopeDegrees: 35d,
            Denominator: 50,
            TextSettings: SharedArch(
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm),
            UsesUserPreset: false,
            IsRoleIsolation: false),
        new(
            "IS",
            AutoCadTextSettingsProofKind.ItemSlot,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Slot,
            TimberElementType.Rafter,
            SlopeDegrees: 35d,
            Denominator: 100,
            TextSettings: SharedClassic(
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm),
            UsesUserPreset: false,
            IsRoleIsolation: false),
        new(
            "CF",
            AutoCadTextSettingsProofKind.CombinedFramed,
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Circle,
            TimberElementType.Rafter,
            SlopeDegrees: 35d,
            Denominator: 50,
            TextSettings: new TimberAnnotationTextSettings(
                TimberAnnotationTextStylePresetRules.ClassicStyleName,
                TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
                TimberAnnotationTextStylePresetRules.ClassicStyleName,
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm),
            UsesUserPreset: false,
            IsRoleIsolation: false),
        new(
            "FL",
            AutoCadTextSettingsProofKind.FullLabel,
            TimberAnnotationMode.FullLabel,
            ItemNumberLeaderStyle.Plain,
            TimberElementType.Rafter,
            SlopeDegrees: 35d,
            Denominator: 50,
            TextSettings: SharedClassic(
                TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm),
            UsesUserPreset: false,
            IsRoleIsolation: false),
        new(
            "DL",
            AutoCadTextSettingsProofKind.DimensionsLeader,
            TimberAnnotationMode.DimensionsLeader,
            ItemNumberLeaderStyle.Plain,
            TimberElementType.Rafter,
            SlopeDegrees: 35d,
            Denominator: 50,
            TextSettings: SharedArch(
                TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm),
            UsesUserPreset: false,
            IsRoleIsolation: false),
        new(
            "SC",
            AutoCadTextSettingsProofKind.SlopeNumeric,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain,
            TimberElementType.Rafter,
            SlopeDegrees: 35d,
            Denominator: 50,
            TextSettings: SharedClassic(
                TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm),
            UsesUserPreset: false,
            IsRoleIsolation: false),
        new(
            "SA",
            AutoCadTextSettingsProofKind.SlopeNumeric,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain,
            TimberElementType.Rafter,
            SlopeDegrees: 35d,
            Denominator: 100,
            TextSettings: TimberAnnotationTextSettings.Shared(
                TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
                SlopeArchTallerHeightMm),
            UsesUserPreset: false,
            IsRoleIsolation: false),
        new(
            "IU",
            AutoCadTextSettingsProofKind.ItemUserPreset,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain,
            TimberElementType.Rafter,
            SlopeDegrees: 35d,
            Denominator: 50,
            TextSettings: TimberAnnotationTextSettings.Shared(
                "PLACEHOLDER_USER",
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm),
            UsesUserPreset: true,
            IsRoleIsolation: false),
        new(
            UserFramedToken,
            AutoCadTextSettingsProofKind.ItemRectangle,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Rectangle,
            TimberElementType.Rafter,
            SlopeDegrees: 35d,
            Denominator: 50,
            TextSettings: TimberAnnotationTextSettings.Shared(
                "PLACEHOLDER_USER",
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm),
            UsesUserPreset: true,
            IsRoleIsolation: false),
        new(
            UserFramedTwinToken,
            AutoCadTextSettingsProofKind.ItemRectangle,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Rectangle,
            TimberElementType.Rafter,
            SlopeDegrees: 35d,
            Denominator: 50,
            TextSettings: TimberAnnotationTextSettings.Shared(
                "PLACEHOLDER_USER",
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
                TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm),
            UsesUserPreset: true,
            IsRoleIsolation: false),
        new(
            "HZ",
            AutoCadTextSettingsProofKind.HorizontalMarker,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain,
            TimberElementType.Rafter,
            SlopeDegrees: 0d,
            Denominator: 50,
            TextSettings: SharedClassic(
                TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm),
            UsesUserPreset: false,
            IsRoleIsolation: false),
        new(
            "PP",
            AutoCadTextSettingsProofKind.PostPerpendicular,
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain,
            TimberElementType.Post,
            SlopeDegrees: 35d,
            Denominator: 50,
            TextSettings: SharedClassic(
                TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm),
            UsesUserPreset: false,
            IsRoleIsolation: false),
        new(
            RoleIsolationToken,
            AutoCadTextSettingsProofKind.RoleIsolation,
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Circle,
            TimberElementType.Rafter,
            SlopeDegrees: 35d,
            Denominator: 50,
            TextSettings: SharedClassic(
                TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm),
            UsesUserPreset: false,
            IsRoleIsolation: true),
    ];

    public static TimberAnnotationUserTextStylePreset CreateUserPreset(
        string fontFile,
        IEnumerable<TimberAnnotationUserTextStylePreset>? existingPresets = null) =>
        TimberAnnotationTextStylePresetRules.ValidateAndNormalizeUserPreset(
            new TimberAnnotationUserTextStylePreset
            {
                StableId = UserPresetStableId,
                DisplayName = UserPresetDisplayName,
                FontFile = fontFile,
                AutoCadTextStyleName =
                    TimberAnnotationTextStylePresetRules.GenerateUserAutoCadTextStyleName(
                        UserPresetStableId),
                WidthFactor =
                    TimberAnnotationTextStylePresetRules.DefaultWidthFactor,
                ObliqueAngleDegrees =
                    TimberAnnotationTextStylePresetRules.DefaultObliqueAngleDegrees,
            },
            existingPresets);

    public static bool IsUserFramedToken(string? token) =>
        string.Equals(token, UserFramedToken, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(token, UserFramedTwinToken, StringComparison.OrdinalIgnoreCase);

    public static string ResolvePreferredUserFont(
        Func<string, bool> isFontAvailable)
    {
        ArgumentNullException.ThrowIfNull(isFontAvailable);
        foreach (var candidate in new[]
                 {
                     PreferredUserPresetFont,
                     "Consolas",
                     "Segoe UI",
                     "Arial Narrow",
                 })
        {
            if (isFontAvailable(candidate) &&
                !string.Equals(
                    candidate,
                    TimberAnnotationTextStylePresetRules.ClassicFontFile,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    candidate,
                    TimberAnnotationTextStylePresetRules.ArchitecturalFontFile,
                    StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "No host font distinct from Classic/Arch is available for the " +
            "G3 USER framed proof preset.");
    }

    public static TimberAnnotationTextSettings ResolveTextSettings(
        AutoCadTextSettingsProofCase proofCase,
        string userPresetStyleName)
    {
        if (proofCase.UsesUserPreset)
        {
            return proofCase.TextSettings with
            {
                TextStyleName = userPresetStyleName,
            };
        }

        return proofCase.TextSettings;
    }

    /// <summary>
    /// Patches Dimension and Slope only. ItemCode style/height stay baseline
    /// so RI proves other-role edits do not disturb framed Item geometry.
    /// </summary>
    public static TimberAnnotationTextSettings CreateRoleIsolationPatchedSettings(
        TimberAnnotationTextSettings baseline) =>
        baseline
            .WithRole(
                TimberAnnotationTextRole.Dimension,
                TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
                RoleIsolationPatchedDimensionHeightMm)
            .WithRole(
                TimberAnnotationTextRole.Slope,
                TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
                RoleIsolationPatchedSlopeHeightMm);

    /// <summary>
    /// Shared AttributeDefinition.Height remains the frozen 1:50 baseline.
    /// </summary>
    public static double ExpectedFramedDefinitionHeightMm =>
        TimberItemLeaderBlockDefinitionRules.BaseFramedItemTextHeightAtScale50Mm;

    /// <summary>
    /// Per-instance AttributeReference.Height equals role model height
    /// (paper × annotation denominator), matching AK_DEV_FRAMED_BASELINE.
    /// BlockScale carries denom/50 and must not be multiplied again.
    /// </summary>
    public static double ExpectedFramedAttributeHeightMm(
        double paperHeightMm,
        int denominator) =>
        TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            paperHeightMm,
            denominator);

    public static double ExpectedBlockScale(int denominator) =>
        TimberAnnotationScaleRules.GetScaleFactor(denominator);

    public static AutoCadTextSettingsProofExpectedCase ToExpected(
        AutoCadTextSettingsProofCase proofCase,
        IReadOnlyList<AutoCadTextSettingsProofRoleExpectation> roles) =>
        new(proofCase.Token, proofCase.Kind, roles);

    private static TimberAnnotationTextSettings SharedClassic(double primaryHeight) =>
        TimberAnnotationTextSettings.Shared(
            TimberAnnotationTextStylePresetRules.ClassicStyleName,
            primaryHeight,
            TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
            TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm);

    private static TimberAnnotationTextSettings SharedArch(double primaryHeight) =>
        TimberAnnotationTextSettings.Shared(
            TimberAnnotationTextStylePresetRules.ArchitecturalStyleName,
            primaryHeight,
            TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
            TimberAnnotationTextSettingsRules.DefaultSlopePaperHeightMm);
}
#endif
