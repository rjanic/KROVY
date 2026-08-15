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
        if (RoofDisplayGroupService.TryResolveLegacyCopiedOwner(
                database,
                transaction,
                selectedId,
                display.Data.OwnerReference,
                out var copiedOwnerId))
        {
            ownerId = copiedOwnerId;
        }
        else if (!long.TryParse(
                     display.Data.OwnerReference,
                     NumberStyles.AllowHexSpecifier,
                     CultureInfo.InvariantCulture,
                     out var handleValue) || handleValue <= 0)
        {
            return RoofOwnerSelectionResult.Failure(RoofOwnerSelectionError.InvalidOwnerReference);
        }
        else
        {
            try
            {
                ownerId = database.GetObjectId(false, new Handle(handleValue), 0);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return RoofOwnerSelectionResult.Failure(RoofOwnerSelectionError.MissingOwner);
            }
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
