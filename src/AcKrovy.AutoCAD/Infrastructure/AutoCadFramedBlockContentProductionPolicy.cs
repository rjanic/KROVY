using AcKrovy.Core.Models;
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Production routing for framed Combined → one G5 BlockContent MLeader.
/// ItemNumberLeader framed (Iba popis) and Combined Plain stay off this path.
/// </summary>
internal static class AutoCadFramedBlockContentProductionPolicy
{
    public const int RendererGeneration =
        TimberMainAnnotationOwnershipRules.G5RendererGeneration;

    public const int LabelMetadataSchemaVersion = 5;

    public static TimberMainAnnotationComponentRole CombinedRole { get; } =
        TimberMainAnnotationComponentRole.FramedItem;

    public static bool UsesG5CombinedFramed(
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style) =>
        TimberAnnotationModeRules.Normalize(mode) ==
            TimberAnnotationMode.DimensionsWithItemNumber &&
        TimberAnnotationModeRules.IsFramedItemLeader(mode, style);

    public static bool IsG5CombinedMetadata(ElementLabelData data) =>
        data.ComponentRole == CombinedRole &&
        data.RendererGeneration == RendererGeneration &&
        TimberAnnotationModeRules.Normalize(data.AnnotationMode) ==
            TimberAnnotationMode.DimensionsWithItemNumber &&
        TimberAnnotationModeRules.IsFramedItemLeader(
            data.AnnotationMode,
            data.ItemNumberLeaderStyle);
}
