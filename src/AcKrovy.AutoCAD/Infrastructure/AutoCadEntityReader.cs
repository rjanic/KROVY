using AcKrovy.Core.Models;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AutoCadEntityReader
{
    public static bool TryReadTimberElement(
        Entity entity,
        Transaction transaction,
        out TimberElementSnapshot? snapshot)
    {
        snapshot = null;

        if (!AutoCadEntityHelpers.IsSupportedTimberGeometry(entity))
        {
            return false;
        }

        if (!ElementDataStore.TryRead(entity, transaction, out var data) || data is null)
        {
            return false;
        }

        snapshot = new TimberElementSnapshot(
            data,
            AutoCadEntityHelpers.GetPlanLengthMm(entity));
        return true;
    }
}
