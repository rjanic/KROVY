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

        if (selected is Polyline polyline)
        {
            if (RoofDefinitionStore.Read(polyline).Data is not null)
            {
                return RoofOwnerSelectionResult.Success(selectedId, selectedThroughDisplayChild: false);
            }
        }

        if (TryResolveUnlockIndicatorOwner(
                database,
                transaction,
                selected,
                out var indicatorOwnerId))
        {
            return RoofOwnerSelectionResult.Success(indicatorOwnerId, selectedThroughDisplayChild: true);
        }

        if (selected is Polyline)
        {
            return RoofOwnerSelectionResult.Success(selectedId, selectedThroughDisplayChild: false);
        }

        if (TryResolveGeneratedOrAnnotationOwner(
                database,
                transaction,
                selected,
                out var generatedOwnerId))
        {
#if DEBUG
            RoofDisplayGroupSelectabilityService.WriteGroupMembershipDiagnostics(
                database,
                transaction,
                generatedOwnerId);
#endif
            return RoofOwnerSelectionResult.Success(generatedOwnerId, selectedThroughDisplayChild: true);
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

    internal static bool TryResolveGeneratedOrAnnotationOwner(
        Database database,
        Transaction transaction,
        Entity selected,
        out ObjectId ownerId)
    {
        ownerId = ObjectId.Null;
        var attached = RoofAttachedManualTimberStore.Read(selected);
        if (attached.Data is not null &&
            TryResolveHandleToPolyline(
                database,
                transaction,
                attached.Data.RoofOwnerReference,
                out ownerId,
                out _))
        {
            return true;
        }

        var timber = RoofGeneratedTimberStore.Read(selected);
        if (timber.Data is not null &&
            TryResolveHandleToPolyline(
                database,
                transaction,
                timber.Data.RoofOwnerReference,
                out ownerId,
                out _))
        {
            return true;
        }

        if (!TryReadAnnotationSourceHandle(selected, out var sourceHandle) ||
            !TryResolveHandleToEntity(database, transaction, sourceHandle, out var sourceEntity) ||
            sourceEntity is null)
        {
            return false;
        }

        var sourceAttached = RoofAttachedManualTimberStore.Read(sourceEntity);
        if (sourceAttached.Data is not null &&
            TryResolveHandleToPolyline(
                database,
                transaction,
                sourceAttached.Data.RoofOwnerReference,
                out ownerId,
                out _))
        {
            return true;
        }

        var sourceTimber = RoofGeneratedTimberStore.Read(sourceEntity);
        return sourceTimber.Data is not null &&
               TryResolveHandleToPolyline(
                   database,
                   transaction,
                   sourceTimber.Data.RoofOwnerReference,
                   out ownerId,
                   out _);
    }

    private static bool TryResolveUnlockIndicatorOwner(
        Database database,
        Transaction transaction,
        Entity selected,
        out ObjectId ownerId)
    {
        ownerId = ObjectId.Null;
        var indicatorOwner = RoofUnlockIndicatorStore.TryReadOwnerReference(selected);
        return !string.IsNullOrWhiteSpace(indicatorOwner) &&
               TryResolveHandleToPolyline(
                   database,
                   transaction,
                   indicatorOwner,
                   out ownerId,
                   out _) &&
               AutoCadObjectIdAccess.TryGetObject<Polyline>(
                   transaction,
                   ownerId,
                   OpenMode.ForRead,
                   out var ownerPolyline,
                   database) &&
               ownerPolyline is not null &&
               RoofDefinitionStore.Read(ownerPolyline).Data is not null;
    }

    private static bool TryReadAnnotationSourceHandle(Entity entity, out string sourceHandle)
    {
        sourceHandle = string.Empty;
        if (ElementLabelStore.TryRead(entity, out var label) &&
            label is not null &&
            !string.IsNullOrWhiteSpace(label.SourceHandle))
        {
            sourceHandle = label.SourceHandle;
            return true;
        }

        if (SlopeArrowStore.TryRead(entity, out var arrow) &&
            arrow is not null &&
            !string.IsNullOrWhiteSpace(arrow.SourceHandle))
        {
            sourceHandle = arrow.SourceHandle;
            return true;
        }

        if (SlopeAngleTextStore.TryRead(entity, out var angle) &&
            angle is not null &&
            !string.IsNullOrWhiteSpace(angle.SourceHandle))
        {
            sourceHandle = angle.SourceHandle;
            return true;
        }

        if (PostFootprintPerpendicularAnnotationStore.TryRead(entity, out var post) &&
            post is not null &&
            !string.IsNullOrWhiteSpace(post.SourceHandle))
        {
            sourceHandle = post.SourceHandle;
            return true;
        }

        return false;
    }

    private static bool TryResolveHandleToEntity(
        Database database,
        Transaction transaction,
        string handleText,
        out Entity? entity)
    {
        entity = null;
        if (!long.TryParse(
                handleText,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var handleValue) ||
            handleValue <= 0)
        {
            return false;
        }

        try
        {
            var id = database.GetObjectId(false, new Handle(handleValue), 0);
            return AutoCadObjectIdAccess.TryGetObject(
                transaction,
                id,
                OpenMode.ForRead,
                out entity,
                database) && entity is not null;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
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
