using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class SlopeAnnotationSourceResolver
{
    public static bool TryResolveSourceId(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        Entity selectedEntity,
        out ObjectId sourceId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(metadataStore);
        ArgumentNullException.ThrowIfNull(selectedEntity);

        sourceId = ObjectId.Null;
        if (AutoCadEntityHelpers.IsSupportedTimberGeometry(selectedEntity) &&
            metadataStore.TryRead(selectedEntity, out var selectedData) &&
            selectedData is not null)
        {
            sourceId = selectedEntity.ObjectId;
            return true;
        }

        var sourceHandle = ReadAnnotationSourceHandle(selectedEntity);
        if (string.IsNullOrWhiteSpace(sourceHandle))
        {
            return false;
        }

        foreach (var id in DrawingScanner.FindAllTimberElements(database, transaction, metadataStore))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                !TimberSlopeAnnotationRules.HasSameSourceHandle(
                    sourceHandle,
                    entity.Handle.ToString()))
            {
                continue;
            }

            sourceId = id;
            return true;
        }

        return false;
    }

    private static string? ReadAnnotationSourceHandle(Entity entity)
    {
        if (SlopeArrowStore.TryRead(entity, out var arrowData) && arrowData is not null)
        {
            return arrowData.SourceHandle;
        }

        return SlopeAngleTextStore.TryRead(entity, out var textData) && textData is not null
            ? textData.SourceHandle
            : null;
    }
}
