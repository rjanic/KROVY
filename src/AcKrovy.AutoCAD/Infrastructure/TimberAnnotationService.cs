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
        var isRectangularFootprintPost =
            TimberPostFootprintMetadataRules.IsValidNewFootprintPost(data);
        var hasResolvedFootprintGeometry = PostFootprintRuntimeGeometryResolver.TryResolve(
            sourceEntity,
            data,
            out var footprintGeometry,
            out var footprintDimensions);
        if (hasResolvedFootprintGeometry &&
            sourceEntity is Polyline footprintPolyline &&
            footprintGeometry is not null &&
            footprintDimensions is not null)
        {
            var effectiveData = data with
            {
                WidthMm = footprintDimensions.WidthMm,
                HeightMm = footprintDimensions.HeightMm,
            };
            var footprintLabelCreated = ElementLabelService.UpsertForPostFootprint(
                database,
                transaction,
                footprintPolyline,
                effectiveData,
                footprintGeometry,
                previousElementId,
                roundingStepMm);
            SlopeAnnotationService.DeleteForSourceHandle(
                database,
                transaction,
                sourceEntity.Handle.ToString());
            PostFootprintPerpendicularAnnotationService.UpsertForFootprint(
                database,
                transaction,
                footprintPolyline,
                footprintGeometry);
            return footprintLabelCreated;
        }

        PostFootprintPerpendicularAnnotationService.DeleteForSourceHandle(
            database,
            transaction,
            sourceEntity.Handle.ToString());
        var plan = TimberAnnotationRefreshPlanner.Create(data, isRectangularFootprintPost);
        if (!plan.EnsureLabel && !plan.ReconcileSlopeArrow && !plan.ReconcileSlopeAngleText)
        {
            var sourceHandle = sourceEntity.Handle.ToString();
            ElementLabelService.DeleteForSourceHandle(database, transaction, sourceHandle);
            SlopeAnnotationService.DeleteForSourceHandle(database, transaction, sourceHandle);
            return false;
        }

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
        PostFootprintPerpendicularAnnotationService.DeleteForMissingSourceHandles(
            database,
            transaction,
            sourceHandles);
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
        PostFootprintPerpendicularAnnotationService.DeleteInsertedWithoutCurrentSourceHandles(
            database,
            transaction,
            slopeArrowIds);
    }

    public static void DeleteDuplicatesForExistingSourceHandles(
        Database database,
        Transaction transaction)
    {
        ElementLabelService.DeleteDuplicateLabelsForExistingSourceHandles(database, transaction);
        SlopeAnnotationService.DeleteDuplicatesForExistingSourceHandles(database, transaction);
        PostFootprintPerpendicularAnnotationService.DeleteDuplicatesForExistingSourceHandles(
            database,
            transaction);
    }
}
