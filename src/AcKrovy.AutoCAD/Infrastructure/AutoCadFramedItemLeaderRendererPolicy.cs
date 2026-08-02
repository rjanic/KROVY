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
        bool itemNumberTokenMatches)
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
        return new AutoCadFramedItemLeaderMutationPlan(
            ShouldOpenExistingForWrite: hasExistingAnnotation,
            ShouldReplaceBlockContent: replaceContent,
            ShouldSetBlockScale: !blockScaleMatches,
            ShouldSetItemNumberToken:
                replaceContent || !itemNumberTokenMatches,
            PreserveExistingAnnotation: false);
    }
}

internal sealed record AutoCadFramedItemLeaderPreparation(
    AutoCadItemLeaderBlockVariantResult VariantResult,
    ObjectId AttributeDefinitionId,
    double BlockScale,
    double EffectiveTextHeight)
{
    public ObjectId BlockTableRecordId =>
        VariantResult.BlockTableRecordId ?? ObjectId.Null;
}
