using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class TimberElementItemIdentityService
{
    public static IReadOnlyDictionary<ObjectId, TimberElementData> SynchronizeElementIds(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        IReadOnlyCollection<ObjectId> targetIds)
    {
        var targetSet = targetIds.Distinct().ToHashSet();
        var entries = ReadCurrentMeasurements(database, transaction, metadataStore);
        var assignments = TimberElementItemNumbering.AssignElementIds(entries.Select(entry =>
            new TimberElementItemNumberingCandidate(
                entry.Measurement,
                IsChanged: targetSet.Contains(entry.Id))));
        var result = new Dictionary<ObjectId, TimberElementData>();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var assignment = assignments[index];
            var updatedData = entry.Measurement.Data with { ElementId = assignment.ElementId };

            if (targetSet.Contains(entry.Id))
            {
                if (!string.Equals(
                        entry.Measurement.Data.ElementId,
                        updatedData.ElementId,
                        StringComparison.OrdinalIgnoreCase) &&
                    AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        entry.Id,
                        OpenMode.ForWrite,
                        out var writableEntity,
                        database) &&
                    writableEntity is not null)
                {
                    metadataStore.Write(writableEntity, updatedData);
                }
            }

            result[entry.Id] = updatedData;
        }

        return result;
    }

    private static List<TimberElementMeasurementEntry> ReadCurrentMeasurements(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore)
    {
        var entries = new List<TimberElementMeasurementEntry>();

        foreach (var id in DrawingScanner.FindAllTimberElements(database, transaction, metadataStore))
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

            entries.Add(new TimberElementMeasurementEntry(
                id,
                TimberElementMeasurer.Measure(snapshot)));
        }

        return entries;
    }

    private sealed record TimberElementMeasurementEntry(
        ObjectId Id,
        TimberElementMeasurement Measurement);
}
