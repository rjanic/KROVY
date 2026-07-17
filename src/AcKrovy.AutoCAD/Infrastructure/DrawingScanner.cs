using AcKrovy.Core.Models;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class DrawingScanner
{
    public static IReadOnlyList<ObjectId> FindAllTimberElements(Database database, Transaction transaction)
    {
        var result = new List<ObjectId>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity)
            {
                continue;
            }

            if (!AutoCadEntityHelpers.IsSupportedTimberGeometry(entity))
            {
                continue;
            }

            if (ElementDataStore.TryRead(entity, transaction, out TimberElementData? _))
            {
                result.Add(id);
            }
        }

        return result;
    }
}
