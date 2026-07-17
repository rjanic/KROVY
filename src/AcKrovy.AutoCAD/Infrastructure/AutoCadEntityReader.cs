using AcKrovy.Cad.Abstractions.Metadata;
using AcKrovy.Core.Models;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AutoCadEntityReader
{
    public static bool TryReadTimberElement(
        Entity entity,
        ITimberElementMetadataStore<Entity> metadataStore,
        out TimberElementSnapshot? snapshot)
    {
        snapshot = null;

        if (!AutoCadEntityHelpers.IsSupportedTimberGeometry(entity))
        {
            return false;
        }

        if (!metadataStore.TryRead(entity, out var data) || data is null)
        {
            return false;
        }

        snapshot = new TimberElementSnapshot(
            data,
            AutoCadEntityHelpers.GetPlanLengthMm(entity));
        return true;
    }
}
