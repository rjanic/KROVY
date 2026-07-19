using AcKrovy.Core.Models;
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
        SlopeArrowService.UpsertForElement(database, transaction, sourceEntity, data);
        SlopeAngleTextService.UpsertForElement(database, transaction, sourceEntity, data);
    }

    public static void DeleteForMissingSourceHandles(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<string> sourceHandles)
    {
        SlopeArrowService.DeleteArrowsForMissingSourceHandles(database, transaction, sourceHandles);
        SlopeAngleTextService.DeleteTextsForMissingSourceHandles(database, transaction, sourceHandles);
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
