using AcKrovy.Cad.Abstractions.Metadata;
using AcKrovy.Core.Models;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class DrawingScanner
{
    public sealed record TimberElementScanItem(
        ObjectId ObjectId,
        TimberElementSnapshot Snapshot);

    public static IReadOnlyList<ObjectId> FindAllTimberElements(
        Database database,
        Transaction transaction,
        ITimberElementMetadataStore<Entity> metadataStore)
    {
        var result = new List<ObjectId>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
                continue;
            }

            if (!AutoCadEntityHelpers.IsSupportedTimberGeometry(entity))
            {
                continue;
            }

            if (metadataStore.TryRead(entity, out TimberElementData? _))
            {
                result.Add(id);
            }
        }

        return result;
    }

    public static IReadOnlyList<TimberElementScanItem> ReadAllTimberElements(
        Database database,
        Transaction transaction,
        ITimberElementMetadataStore<Entity> metadataStore)
    {
        var result = new List<TimberElementScanItem>();
        foreach (var id in FindAllTimberElements(database, transaction, metadataStore))
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                !AutoCadEntityReader.TryReadTimberElement(entity, metadataStore, out var snapshot) ||
                snapshot is null)
            {
                continue;
            }

            result.Add(new TimberElementScanItem(id, snapshot));
        }

        return result;
    }
}
