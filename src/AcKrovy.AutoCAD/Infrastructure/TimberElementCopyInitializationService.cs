using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class TimberElementCopyInitializationService
{
    public static int InitializeLocalCopies(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        IReadOnlyCollection<ObjectId> targetIds,
        TimberElementDefaultProfile defaultProfile)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(metadataStore);
        ArgumentNullException.ThrowIfNull(targetIds);
        ArgumentNullException.ThrowIfNull(defaultProfile);

        var labelCandidates = ElementLabelService.ReadLabelCandidates(database, transaction);
        if (labelCandidates.Count == 0)
        {
            return 0;
        }

        var existingTimberHandles = ReadExistingTimberHandles(database, transaction, metadataStore);
        var initialized = 0;

        foreach (var id in targetIds.Distinct())
        {
            if (transaction.GetObject(id, OpenMode.ForWrite) is not Entity entity ||
                !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                !metadataStore.TryRead(entity, out var data) ||
                data is null)
            {
                continue;
            }

            var currentHandle = entity.Handle.ToString();
            if (!TimberElementCopyInitializationRules.ShouldInitializeAsNewPhysicalElement(
                    currentHandle,
                    data.ElementId,
                    labelCandidates,
                    existingTimberHandles))
            {
                continue;
            }

            initialized++;
        }

        return initialized;
    }

    private static IReadOnlyCollection<string> ReadExistingTimberHandles(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore)
    {
        var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in DrawingScanner.FindAllTimberElements(database, transaction, metadataStore))
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is Entity entity)
            {
                handles.Add(entity.Handle.ToString());
            }
        }

        return handles;
    }
}
