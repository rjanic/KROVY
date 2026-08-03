using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AutoCadFramedItemLeaderRendererPolicy
{
    public static bool UsesImmutableVariant(
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style,
        TimberMainAnnotationComponentRole componentRole) =>
        TimberAnnotationModeRules.IsFramedItemLeader(mode, style) &&
        (TimberAnnotationModeRules.Normalize(mode) ==
            TimberAnnotationMode.ItemNumberLeader &&
         componentRole == TimberMainAnnotationComponentRole.Primary ||
         TimberAnnotationModeRules.Normalize(mode) ==
            TimberAnnotationMode.DimensionsWithItemNumber &&
         componentRole == TimberMainAnnotationComponentRole.FramedItem);

    public static double CalculateBlockScale(
        TimberAnnotationScaleContext annotationScaleContext)
    {
        ArgumentNullException.ThrowIfNull(annotationScaleContext);
        return annotationScaleContext.ScaleFactor;
    }
}

internal sealed record AutoCadFramedItemLeaderMutationPlan(
    bool ShouldOpenExistingForWrite,
    bool ShouldReplaceBlockContent,
    bool ShouldSetBlockScale,
    bool ShouldSetItemNumberToken,
    bool PreserveExistingAnnotation);

internal static class AutoCadFramedItemLeaderMutationPolicy
{
    public static AutoCadFramedItemLeaderMutationPlan Create(
        bool variantEnsureSucceeded,
        bool hasExistingAnnotation,
        bool blockContentMatches,
        bool blockScaleMatches,
        bool itemNumberTokenMatches,
        bool attributePresentationMatches = true)
    {
        if (!variantEnsureSucceeded)
        {
            return new AutoCadFramedItemLeaderMutationPlan(
                ShouldOpenExistingForWrite: false,
                ShouldReplaceBlockContent: false,
                ShouldSetBlockScale: false,
                ShouldSetItemNumberToken: false,
                PreserveExistingAnnotation: hasExistingAnnotation);
        }

        var replaceContent = !blockContentMatches;
        var setAttribute = replaceContent ||
            !itemNumberTokenMatches ||
            !attributePresentationMatches;
        return new AutoCadFramedItemLeaderMutationPlan(
            ShouldOpenExistingForWrite: hasExistingAnnotation &&
                (replaceContent || !blockScaleMatches || setAttribute),
            ShouldReplaceBlockContent: replaceContent,
            ShouldSetBlockScale: !blockScaleMatches,
            ShouldSetItemNumberToken: setAttribute,
            PreserveExistingAnnotation: false);
    }
}

internal sealed record AutoCadFramedItemLeaderPreparation(
    AutoCadItemLeaderBlockVariantResult VariantResult,
    ObjectId AttributeDefinitionId,
    double BlockScale,
    double EffectiveTextHeight,
    ObjectId TextStyleId,
    double AttributeHeightMm)
{
    public ObjectId BlockTableRecordId =>
        VariantResult.BlockTableRecordId ?? ObjectId.Null;
}
