using AcKrovy.Core.Models.Roofs;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Builds the deterministic roof assembly member set for GROUP membership.
/// Structural display (owner + 7 lines) plus owned generated/attached timber and
/// their source-handle-bound annotations. Does not mutate the database.
/// </summary>
internal static class RoofAssemblyGroupMemberCollector
{
    public sealed record CollectResult(
        IReadOnlyList<ObjectId> MemberIds,
        int GeneratedCount,
        int AttachedManualCount,
        int AnnotationCount);

    public static bool TryCollect(
        Database database,
        Transaction transaction,
        ObjectId ownerId,
        IReadOnlyList<ObjectId> structuralDisplayChildIds,
        out CollectResult? result)
    {
        result = null;
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        if (ownerId.IsNull ||
            structuralDisplayChildIds.Count != RoofDisplayGroupService.ExpectedStructuralDisplayChildCount)
        {
            return false;
        }

        if (!AutoCadObjectIdAccess.TryGetObject<Polyline>(
                transaction,
                ownerId,
                OpenMode.ForRead,
                out var owner,
                database) ||
            owner is null ||
            RoofDefinitionStore.Read(owner).Data is null)
        {
            return false;
        }

        var ownerReference = owner.Handle.ToString();
        var members = new HashSet<ObjectId> { ownerId };
        foreach (var displayId in structuralDisplayChildIds)
        {
            if (displayId.IsNull || displayId == ownerId)
            {
                return false;
            }

            members.Add(displayId);
        }

        var timberSourceHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var generatedCount = 0;
        foreach (var id in RoofGeneratedTimberStore.FindByOwner(database, transaction, ownerReference))
        {
            if (!TryAddTimberLine(database, transaction, id, members, timberSourceHandles))
            {
                continue;
            }

            generatedCount++;
        }

        var attachedManualCount = 0;
        foreach (var id in RoofAttachedManualTimberStore.FindByOwner(database, transaction, ownerReference))
        {
            if (!TryAddTimberLine(database, transaction, id, members, timberSourceHandles))
            {
                continue;
            }

            attachedManualCount++;
        }

        var annotationCount = 0;
        if (timberSourceHandles.Count > 0)
        {
            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace],
                OpenMode.ForRead);
            foreach (ObjectId id in modelSpace)
            {
                if (id.IsErased || members.Contains(id))
                {
                    continue;
                }

                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        database) ||
                    entity is null ||
                    entity.IsErased ||
                    RoofDisplayStore.Read(entity).Exists)
                {
                    continue;
                }

                if (!RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(entity, out var sourceHandle) ||
                    !timberSourceHandles.Contains(sourceHandle))
                {
                    continue;
                }

                if (members.Add(id))
                {
                    annotationCount++;
                }
            }
        }

        var ordered = members
            .OrderBy(id => id == ownerId ? 0 : 1)
            .ThenBy(id => id.Handle.Value)
            .ToList();
        result = new CollectResult(ordered, generatedCount, attachedManualCount, annotationCount);
        return true;
    }

    private static bool TryAddTimberLine(
        Database database,
        Transaction transaction,
        ObjectId id,
        HashSet<ObjectId> members,
        HashSet<string> timberSourceHandles)
    {
        if (id.IsNull ||
            id.IsErased ||
            !AutoCadObjectIdAccess.TryGetObject<Line>(
                transaction,
                id,
                OpenMode.ForRead,
                out var line,
                database) ||
            line is null ||
            line.IsErased)
        {
            return false;
        }

        members.Add(id);
        timberSourceHandles.Add(line.Handle.ToString());
        return true;
    }
}

/// <summary>Shared source-handle resolution for owned timber annotations.</summary>
internal static class RoofOwnedAnnotationSourceResolver
{
    public static bool TryResolveSourceHandle(Entity entity, out string sourceHandle)
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
}
