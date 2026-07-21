using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class SlopeAnnotationService
{
    public static void EnsureForElement(
        Database database,
        Transaction transaction,
        Entity sourceEntity,
        TimberElementData data)
    {
        var preferredGeometry = SlopeAnnotationGeometry.CalculatePreferred(sourceEntity);
        TimberSlopeAnnotationLongitudinalInterval? labelInterval = null;
        if (ElementLabelService.TryGetLongitudinalInterval(
                database,
                transaction,
                sourceEntity,
                out var interval))
        {
            labelInterval = interval;
        }

        var glyphKind = TimberSlopeAnnotationRules.ResolveGlyphKind(
            data.ElementType,
            data.SlopeDegrees);
        var annotationHalfExtentMm = glyphKind == TimberSlopeGlyphKind.PostPerpendicularMarker
            ? TimberPostAnnotationGeometryCalculator.CollisionHalfExtentMm
            : TimberSlopeAnnotationPlacementCalculator.SlopeAnnotationHalfExtentMm;
        var placement = TimberSlopeAnnotationPlacementCalculator.Calculate(
            preferredGeometry.LengthMm,
            labelInterval,
            annotationHalfExtentMm);
        var geometry = SlopeAnnotationGeometry.Calculate(sourceEntity, placement.AnchorDistanceMm);

        SlopeArrowService.UpsertForElement(database, transaction, sourceEntity, data, geometry);
        SlopeAngleTextService.UpsertForElement(database, transaction, sourceEntity, data, geometry);
    }

    public static void DeleteForMissingSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<string> sourceHandles)
    {
        SlopeArrowService.DeleteArrowsForMissingSourceHandles(database, transaction, sourceHandles);
        SlopeAngleTextService.DeleteTextsForMissingSourceHandles(database, transaction, sourceHandles);
    }

    public static void DeleteForSourceHandle(
        Database database,
        Transaction transaction,
        string sourceHandle)
    {
        SlopeArrowService.DeleteForSourceHandle(database, transaction, sourceHandle);
        SlopeAngleTextService.DeleteForSourceHandle(database, transaction, sourceHandle);
    }

    public static void DeleteInsertedWithoutCurrentSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> arrowIds,
        IReadOnlyCollection<ObjectId> angleTextIds)
    {
        SlopeArrowService.DeleteInsertedArrowsWithoutCurrentSourceHandles(database, transaction, arrowIds);
        SlopeAngleTextService.DeleteInsertedTextsWithoutCurrentSourceHandles(database, transaction, angleTextIds);
    }

    public static void DeleteDuplicatesForExistingSourceHandles(
        Database database,
        Transaction transaction)
    {
        SlopeArrowService.DeleteDuplicateArrowsForExistingSourceHandles(database, transaction);
        SlopeAngleTextService.DeleteDuplicateTextsForExistingSourceHandles(database, transaction);
    }
}
