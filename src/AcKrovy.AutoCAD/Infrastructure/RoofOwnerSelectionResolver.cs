using System.Globalization;
using AcKrovy.Core.Models.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>Resolves either a source Polyline or a tagged display proxy to the semantic owner.</summary>
internal static class RoofOwnerSelectionResolver
{
    public static RoofOwnerSelectionResult Resolve(
        Database database,
        Transaction transaction,
        ObjectId selectedId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                selectedId,
                OpenMode.ForRead,
                out var selected,
                database) || selected is null)
        {
            return RoofOwnerSelectionResult.Failure(RoofOwnerSelectionError.UnrelatedObject);
        }

        if (selected is Polyline)
        {
            return RoofOwnerSelectionResult.Success(selectedId, selectedThroughDisplayChild: false);
        }

        var display = RoofDisplayStore.Read(selected);
        if (!display.Exists)
        {
            return RoofOwnerSelectionResult.Failure(RoofOwnerSelectionError.UnrelatedObject);
        }
        if (display.Data is null)
        {
            return RoofOwnerSelectionResult.Failure(
                display.Error == RoofDisplayDataDecodeError.UnsupportedFutureSchema
                    ? RoofOwnerSelectionError.UnsupportedFutureDisplaySchema
                    : RoofOwnerSelectionError.MalformedDisplayMetadata);
        }
        ObjectId ownerId;
        var handleError = RoofOwnerSelectionError.MissingOwner;
        if (RoofDisplayGroupService.TryResolveLegacyCopiedOwner(
                database,
                transaction,
                selectedId,
                display.Data.OwnerReference,
                out var copiedOwnerId))
        {
            ownerId = copiedOwnerId;
        }
        else if (display.OwnerReferenceFromCloneHandle &&
                 TryResolveHandleToPolyline(
                     database,
                     transaction,
                     display.Data.OwnerReference,
                     out var cloneOwnerId,
                     out handleError))
        {
            ownerId = cloneOwnerId;
        }
        else if (RoofDisplayService.TryResolveTransferredOwner(
                     database,
                     transaction,
                     selectedId,
                     display.Data.OwnerReference,
                     out var transferredOwnerId))
        {
            ownerId = transferredOwnerId;
        }
        else if (TryResolveHandleToPolyline(
                     database,
                     transaction,
                     display.Data.OwnerReference,
                     out var handleOwnerId,
                     out handleError))
        {
            ownerId = handleOwnerId;
        }
        else
        {
            return RoofOwnerSelectionResult.Failure(handleError);
        }

        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                database) || owner is null)
        {
            return RoofOwnerSelectionResult.Failure(RoofOwnerSelectionError.MissingOwner);
        }
        if (owner is not Polyline)
        {
            return RoofOwnerSelectionResult.Failure(RoofOwnerSelectionError.OwnerIsNotPolyline);
        }

        return RoofOwnerSelectionResult.Success(ownerId, selectedThroughDisplayChild: true);
    }

    private static bool TryResolveHandleToPolyline(
        Database database,
        Transaction transaction,
        string ownerReference,
        out ObjectId ownerId,
        out RoofOwnerSelectionError error)
    {
        ownerId = ObjectId.Null;
        if (!long.TryParse(
                ownerReference,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var handleValue) || handleValue <= 0)
        {
            error = RoofOwnerSelectionError.InvalidOwnerReference;
            return false;
        }

        try
        {
            ownerId = database.GetObjectId(false, new Handle(handleValue), 0);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            error = RoofOwnerSelectionError.MissingOwner;
            return false;
        }

        if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                database) || owner is null)
        {
            error = RoofOwnerSelectionError.MissingOwner;
            return false;
        }
        if (owner is not Polyline)
        {
            error = RoofOwnerSelectionError.OwnerIsNotPolyline;
            return false;
        }

        error = RoofOwnerSelectionError.None;
        return true;
    }
}

internal enum RoofOwnerSelectionError
{
    None = 0,
    UnrelatedObject = 1,
    MalformedDisplayMetadata = 2,
    UnsupportedFutureDisplaySchema = 3,
    InvalidOwnerReference = 4,
    MissingOwner = 5,
    OwnerIsNotPolyline = 6,
}

internal sealed record RoofOwnerSelectionResult(
    bool IsResolved,
    ObjectId OwnerId,
    bool SelectedThroughDisplayChild,
    RoofOwnerSelectionError Error)
{
    public static RoofOwnerSelectionResult Success(
        ObjectId ownerId,
        bool selectedThroughDisplayChild) =>
        new(true, ownerId, selectedThroughDisplayChild, RoofOwnerSelectionError.None);

    public static RoofOwnerSelectionResult Failure(RoofOwnerSelectionError error) =>
        new(false, ObjectId.Null, false, error);
}
