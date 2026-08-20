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
        IReadOnlyCollection<ObjectId> targetIds,
        double roundingStepMm = TimberCuttingLengthCalculator.DefaultRoundingStepMm) =>
        SynchronizeElementIdsDetailed(
            database,
            transaction,
            metadataStore,
            targetIds,
            roundingStepMm).DataById;

    public static TimberElementItemSyncResult SynchronizeElementIdsDetailed(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        IReadOnlyCollection<ObjectId> targetIds,
        double roundingStepMm = TimberCuttingLengthCalculator.DefaultRoundingStepMm)
    {
#if DEBUG
        RoofGeneratedPostAtomicWriteDiag.ResetBatch();
#endif
        var targetSet = targetIds.Distinct().ToHashSet();
        var entries = ReadCurrentMeasurements(database, transaction, metadataStore, roundingStepMm);
        var assignments = TimberElementItemNumbering.AssignElementIds(entries.Select(entry =>
            new TimberElementItemNumberingCandidate(
                entry.Measurement,
                IsChanged: targetSet.Contains(entry.Id))));
        var result = new Dictionary<ObjectId, TimberElementData>();
        var previousElementIdById = new Dictionary<ObjectId, string>();
        var writtenIds = new List<ObjectId>();
        var numberingChanges = new List<TimberElementNumberingChange>();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var assignment = assignments[index];
            var previousElementId = entry.Measurement.Data.ElementId;
            var updatedData = entry.Measurement.Data with { ElementId = assignment.ElementId };
            previousElementIdById[entry.Id] = previousElementId;
            result[entry.Id] = updatedData;

            if (string.Equals(
                    previousElementId,
                    updatedData.ElementId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            numberingChanges.Add(new TimberElementNumberingChange(
                entry.Id,
                entry.Id.Handle.ToString(),
                previousElementId,
                updatedData.ElementId));

            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    entry.Id,
                    OpenMode.ForWrite,
                    out var writableEntity,
                    database) ||
                writableEntity is null)
            {
                continue;
            }

#if DEBUG
            RoofGeneratedPostAtomicWriteDiag.TraceSyncWrite(writableEntity);
#endif
            metadataStore.Write(writableEntity, updatedData);
            writtenIds.Add(entry.Id);
        }

        return new TimberElementItemSyncResult(
            result,
            previousElementIdById,
            writtenIds,
            numberingChanges);
    }

    private static List<TimberElementMeasurementEntry> ReadCurrentMeasurements(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        double roundingStepMm)
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
                TimberElementMeasurer.Measure(snapshot, roundingStepMm)));
        }

        return entries;
    }

    /// <summary>
    /// Computes the final ElementId for not-yet-persisted timber snapshots WITHOUT writing
    /// XData, by running the same numbering over the existing drawing timber plus the new
    /// snapshots. The Generated creation path uses this so its single atomic Entity.XData
    /// assignment already carries the final ElementId, leaving
    /// <see cref="SynchronizeElementIds"/> with no change (and thus no second XData write).
    /// </summary>
    public static IReadOnlyList<string> ComputeFinalElementIds(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        IReadOnlyList<TimberElementSnapshot> newSnapshots,
        double roundingStepMm = TimberCuttingLengthCalculator.DefaultRoundingStepMm)
    {
        if (newSnapshots is null || newSnapshots.Count == 0)
        {
            return Array.Empty<string>();
        }

        var existingEntries = ReadCurrentMeasurements(database, transaction, metadataStore, roundingStepMm);
        var newMeasurements = newSnapshots
            .Select(snapshot => TimberElementMeasurer.Measure(snapshot, roundingStepMm))
            .ToList();

        var allCandidates = existingEntries
            .Select(entry => new TimberElementItemNumberingCandidate(entry.Measurement, IsChanged: false))
            .Concat(newMeasurements.Select(measurement =>
                new TimberElementItemNumberingCandidate(measurement, IsChanged: true)))
            .ToList();

        var assignments = TimberElementItemNumbering.AssignElementIds(allCandidates);

        var finalIds = new List<string>(newSnapshots.Count);
        for (var index = assignments.Count - newSnapshots.Count; index < assignments.Count; index++)
        {
            finalIds.Add(assignments[index].ElementId);
        }

        return finalIds;
    }

    private sealed record TimberElementMeasurementEntry(
        ObjectId Id,
        TimberElementMeasurement Measurement);
}

internal sealed record TimberElementNumberingChange(
    ObjectId Id,
    string Handle,
    string PreviousElementId,
    string ElementId);

internal sealed record TimberElementItemSyncResult(
    IReadOnlyDictionary<ObjectId, TimberElementData> DataById,
    IReadOnlyDictionary<ObjectId, string> PreviousElementIdById,
    IReadOnlyList<ObjectId> WrittenIds,
    IReadOnlyList<TimberElementNumberingChange> NumberingChanges);
