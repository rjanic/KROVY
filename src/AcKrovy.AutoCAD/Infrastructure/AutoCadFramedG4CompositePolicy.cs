using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// G4 framed composite roles reuse the historical Circle* component roles for
/// all frame kinds (Circle/Rectangle/Slot). Erase/upsert already treat those
/// three roles as one logical group.
/// </summary>
internal static class AutoCadFramedG4CompositePolicy
{
    public const int RendererGeneration = 4;
    public const int LabelMetadataSchemaVersion = 4;

    public static TimberMainAnnotationComponentRole LeaderRole { get; } =
        TimberMainAnnotationComponentRole.CircleLeaderLine;

    public static TimberMainAnnotationComponentRole FrameRole { get; } =
        TimberMainAnnotationComponentRole.CircleFrame;

    public static TimberMainAnnotationComponentRole ItemCodeRole { get; } =
        TimberMainAnnotationComponentRole.CircleText;

    public static bool IsG4CompositeRole(TimberMainAnnotationComponentRole role) =>
        role is
            TimberMainAnnotationComponentRole.CircleText or
            TimberMainAnnotationComponentRole.CircleFrame or
            TimberMainAnnotationComponentRole.CircleLeaderLine;

    public static bool UsesG4Composite(
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style,
        TimberMainAnnotationComponentRole componentRole)
    {
        // CREATE for ItemNumberLeader framed migrated to one BlockContent MLeader
        // (AutoCadStandaloneFramedItemOnly*). G4 remains for role classification /
        // legacy erase only — never for new composite CREATE.
        _ = mode;
        _ = style;
        _ = componentRole;
        return false;
    }
    public static bool IsLegacyG2G3BlockLeaderRole(
        TimberMainAnnotationComponentRole role) =>
        role is
            TimberMainAnnotationComponentRole.Primary or
            TimberMainAnnotationComponentRole.FramedItem;

    public static string CreateAnnotationGroupId() =>
        Guid.NewGuid().ToString("N");

    public static double CalculateFrameBlockScale(
        TimberAnnotationScaleContext annotationScaleContext)
    {
        ArgumentNullException.ThrowIfNull(annotationScaleContext);
        return annotationScaleContext.ScaleFactor;
    }

    public static double CalculateItemCodeModelHeightMm(
        double paperHeightMm,
        int effectiveDenominator) =>
        TimberAnnotationTextSettingsRules.CalculateModelHeightMm(
            paperHeightMm,
            effectiveDenominator);
}

internal sealed record AutoCadFramedG4Preparation(
    AutoCadItemLeaderFrameOnlyBlockResult FrameResult,
    TimberItemLeaderBlockDefinition Definition,
    double FrameBlockScale,
    double ItemCodeModelHeightMm,
    double ItemCodePaperHeightMm,
    ObjectId TextStyleId,
    string TextStyleName,
    AutoCadItemLeaderTextStyleIdentity TextStyleIdentity,
    string AnnotationGroupId,
    bool CombinedFramed)
{
    public ObjectId FrameBlockTableRecordId =>
        FrameResult.BlockTableRecordId ?? ObjectId.Null;

    public string FrameBlockName =>
        FrameResult.ResolvedBlockName ??
        AutoCadItemLeaderFrameOnlyBlockNamePolicy.CreateCanonicalName(
            FrameResult.VariantKey!);
}
