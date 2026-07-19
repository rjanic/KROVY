using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class TimberAnnotationService
{
    public static bool EnsureForElement(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data,
        string? previousElementId = null,
        double roundingStepMm = TimberCuttingLengthCalculator.DefaultRoundingStepMm)
    {
        var plan = TimberAnnotationRefreshPlanner.Create(data);
        var labelCreated = plan.EnsureLabel && ElementLabelService.UpsertForElement(
                database,
                transaction,
                sourceEntity,
                data,
                previousElementId,
                roundingStepMm);
        if (plan.ReconcileSlopeArrow && plan.ReconcileSlopeAngleText)
        {
            SlopeAnnotationService.EnsureForElement(database, transaction, sourceEntity, data);
        }

        return labelCreated;
    }

    public static void DeleteForMissingSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<string> sourceHandles)
    {
        ElementLabelService.DeleteLabelsForMissingSourceHandles(database, transaction, sourceHandles);
        SlopeAnnotationService.DeleteForMissingSourceHandles(database, transaction, sourceHandles);
    }

    public static void DeleteInsertedWithoutCurrentSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> labelIds,
        IReadOnlyCollection<ObjectId> slopeArrowIds,
        IReadOnlyCollection<ObjectId> slopeAngleTextIds)
    {
        ElementLabelService.DeleteInsertedLabelsWithoutCurrentSourceHandles(database, transaction, labelIds);
        SlopeAnnotationService.DeleteInsertedWithoutCurrentSourceHandles(
            database,
            transaction,
            slopeArrowIds,
            slopeAngleTextIds);
    }

    public static void DeleteDuplicatesForExistingSourceHandles(
        Database database,
        Transaction transaction)
    {
        ElementLabelService.DeleteDuplicateLabelsForExistingSourceHandles(database, transaction);
        SlopeAnnotationService.DeleteDuplicatesForExistingSourceHandles(database, transaction);
    }
}
