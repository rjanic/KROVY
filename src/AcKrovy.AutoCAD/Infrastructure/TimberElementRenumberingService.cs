using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal sealed record TimberElementRenumberingResult(
    int ProcessedElements,
    int UniqueItems,
    int RenumberedElementTypes,
    int ChangedElements);

internal static class TimberElementRenumberingService
{
    public static TimberElementRenumberingResult RenumberAll(
        Database database,
        TimberElementDefaultProfile defaultProfile,
        double roundingStepMm = TimberCuttingLengthCalculator.DefaultRoundingStepMm)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(defaultProfile);

        using var transaction = database.TransactionManager.StartTransaction();
        var presentationBatchContext =
            AutoCadAnnotationPresentationBatchContext.Create(
            database,
            transaction,
            defaultProfile);
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var entries = ReadValidEntries(database, transaction, metadataStore, roundingStepMm);
        var assignments = TimberElementItemNumbering.RenumberElementIdsByCuttingLength(
            entries.Select(entry => entry.Measurement));
        var circleNormalizationIds =
            ElementLabelService.FindCircleNormalizationSourceIds(
                database,
                transaction,
                entries.Select(entry => entry.Id).ToList(),
                presentationBatchContext.AnnotationScaleService);
        var changedEntries = new List<ChangedEntry>();
        var annotationEntries = new List<ChangedEntry>();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var assignment = assignments[index];
            var updatedData = entry.Measurement.Data with
            {
                ElementId = assignment.ElementId,
            };
            if (assignment.IsChanged || circleNormalizationIds.Contains(entry.Id))
            {
                annotationEntries.Add(new ChangedEntry(
                    entry.Id,
                    assignment.PreviousElementId,
                    updatedData));
            }
            if (!assignment.IsChanged)
            {
                continue;
            }

            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    entry.Id,
                    OpenMode.ForWrite,
                    out var entity,
                    database) ||
                entity is null)
            {
                throw new InvalidOperationException(UiStrings.ErrorRenumberEntityUnavailable);
            }

            metadataStore.Write(entity, updatedData);
            changedEntries.Add(new ChangedEntry(
                entry.Id,
                assignment.PreviousElementId,
                updatedData));
        }

        foreach (var entry in annotationEntries)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    entry.Id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null)
            {
                throw new InvalidOperationException(UiStrings.ErrorRenumberEntityUnavailable);
            }

            TimberAnnotationService.EnsureForElement(
                database,
                transaction,
                entity,
                entry.Data,
                presentationBatchContext,
                entry.PreviousElementId,
                roundingStepMm);
        }

        transaction.Commit();

        return new TimberElementRenumberingResult(
            entries.Count,
            assignments.Select(assignment => assignment.Signature).Distinct().Count(),
            assignments
                .Select(assignment => TimberElementSeriesRules.GetKey(assignment.Signature))
                .Distinct()
                .Count(),
            changedEntries.Count);
    }

    private static List<MeasurementEntry> ReadValidEntries(
        Database database,
        Transaction transaction,
        AutoCadTimberElementMetadataStore metadataStore,
        double roundingStepMm)
    {
        var entries = new List<MeasurementEntry>();

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

            try
            {
                entries.Add(new MeasurementEntry(
                    id,
                    TimberElementMeasurer.Measure(snapshot, roundingStepMm)));
            }
            catch (ArgumentException)
            {
                // Invalid timber data are outside the command scope and remain untouched.
            }
            catch (InvalidOperationException)
            {
                // Unsupported/invalid geometry is ignored; unexpected CAD failures still roll back.
            }
        }

        return entries;
    }

    private sealed record MeasurementEntry(ObjectId Id, TimberElementMeasurement Measurement);

    private sealed record ChangedEntry(
        ObjectId Id,
        string PreviousElementId,
        TimberElementData Data);
}
