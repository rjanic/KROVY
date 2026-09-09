using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Read-only CommandWillStart map of live roof entity handles to their owner for
/// native ERASE. ObjectErased must resolve through this map instead of reading
/// XData / dictionaries from already-erased DBObjects. Source rows also retain
/// pre-command editState so Locked source ERASE recovery does not depend on
/// reading definition from an erased DBObject.
/// </summary>
internal static class RoofDisplayErasePreCommandMapService
{
    private static readonly object Gate = new();
    private static Dictionary<string, MappedEntity> _byHandle =
        new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, ObjectId> _ownerIdsByHandle =
        new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<ObjectId, SourcePreCommandState> _sourcesByOwnerId = new();
    private static HashSet<string> _sourceHandles =
        new(StringComparer.OrdinalIgnoreCase);

    internal readonly record struct MappedEntity(
        string OwnerHandle,
        ObjectId OwnerId,
        RoofEraseMappedKind Kind);

    internal sealed record SourcePreCommandState(
        ObjectId OwnerId,
        string OwnerHandle,
        RoofEditState EditState,
        RoofKind Kind,
        bool DefinitionAvailable,
        int SourceVertexCount);

    public static void Clear(string reason)
    {
        _ = reason;
        lock (Gate)
        {
            _byHandle = new Dictionary<string, MappedEntity>(StringComparer.OrdinalIgnoreCase);
            _ownerIdsByHandle = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            _sourcesByOwnerId = new Dictionary<ObjectId, SourcePreCommandState>();
            _sourceHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void CaptureForErase(Document document, string? globalCommandName)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!RoofGeneratedMemberEditCommandRules.IsEraseCommand(globalCommandName))
        {
            Clear("non-erase-command");
            return;
        }

        var byHandle = new Dictionary<string, MappedEntity>(StringComparer.OrdinalIgnoreCase);
        var ownerIdsByHandle = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
        var sourcesByOwnerId = new Dictionary<ObjectId, SourcePreCommandState>();
        var sourceHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var transaction = document.Database.TransactionManager.StartTransaction();
        try
        {
            var blockTable = (BlockTable)transaction.GetObject(
                document.Database.BlockTableId,
                OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                blockTable[BlockTableRecord.ModelSpace],
                OpenMode.ForRead);

            foreach (ObjectId id in modelSpace)
            {
                if (id.IsErased ||
                    !AutoCadObjectIdAccess.TryGetObject<Polyline>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var polyline,
                        document.Database) ||
                    polyline is null ||
                    polyline.IsErased)
                {
                    continue;
                }

                var definition = RoofDefinitionStore.Read(polyline).Data;
                if (definition is null)
                {
                    continue;
                }

                var ownerHandle = polyline.Handle.ToString();
                ownerIdsByHandle[ownerHandle] = id;
                sourceHandles.Add(ownerHandle);
                byHandle[ownerHandle] = new MappedEntity(
                    ownerHandle,
                    id,
                    RoofEraseMappedKind.Source);
                sourcesByOwnerId[id] = new SourcePreCommandState(
                    id,
                    ownerHandle,
                    definition.EditState,
                    definition.Kind,
                    DefinitionAvailable: true,
                    SourceVertexCount: polyline.NumberOfVertices);
            }

            foreach (ObjectId id in modelSpace)
            {
                if (id.IsErased ||
                    !AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        document.Database) ||
                    entity is null ||
                    entity.IsErased)
                {
                    continue;
                }

                var handle = entity.Handle.ToString();
                if (byHandle.ContainsKey(handle))
                {
                    continue;
                }

                var display = RoofDisplayStore.Read(entity);
                if (display.Exists &&
                    !string.IsNullOrWhiteSpace(display.OwnerReference) &&
                    ownerIdsByHandle.TryGetValue(display.OwnerReference, out var displayOwnerId))
                {
                    byHandle[handle] = new MappedEntity(
                        display.OwnerReference,
                        displayOwnerId,
                        RoofEraseMappedKind.Display);
                    continue;
                }

                if (entity is not Line line)
                {
                    continue;
                }

                var generated = RoofGeneratedTimberStore.Read(line);
                if (generated.Data is not null &&
                    !string.IsNullOrWhiteSpace(generated.Data.RoofOwnerReference) &&
                    ownerIdsByHandle.TryGetValue(
                        generated.Data.RoofOwnerReference,
                        out var timberOwnerId))
                {
                    byHandle[handle] = new MappedEntity(
                        generated.Data.RoofOwnerReference,
                        timberOwnerId,
                        RoofEraseMappedKind.GeneratedTimber);
                    continue;
                }

                var attached = RoofAttachedManualTimberStore.Read(line);
                if (attached.Data is not null &&
                    !string.IsNullOrWhiteSpace(attached.Data.RoofOwnerReference) &&
                    ownerIdsByHandle.TryGetValue(
                        attached.Data.RoofOwnerReference,
                        out var attachedOwnerId))
                {
                    byHandle[handle] = new MappedEntity(
                        attached.Data.RoofOwnerReference,
                        attachedOwnerId,
                        RoofEraseMappedKind.GeneratedTimber);
                }
            }

            foreach (ObjectId id in modelSpace)
            {
                if (id.IsErased ||
                    !AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        document.Database) ||
                    entity is null ||
                    entity.IsErased)
                {
                    continue;
                }

                var handle = entity.Handle.ToString();
                if (byHandle.ContainsKey(handle) ||
                    !TryResolveAnnotationSourceHandle(entity, out var annotationSourceHandle) ||
                    !byHandle.TryGetValue(annotationSourceHandle, out var timberMapped) ||
                    timberMapped.Kind != RoofEraseMappedKind.GeneratedTimber)
                {
                    continue;
                }

                byHandle[handle] = new MappedEntity(
                    timberMapped.OwnerHandle,
                    timberMapped.OwnerId,
                    RoofEraseMappedKind.GeneratedAnnotation);
            }

            lock (Gate)
            {
                _byHandle = byHandle;
                _ownerIdsByHandle = ownerIdsByHandle;
                _sourcesByOwnerId = sourcesByOwnerId;
                _sourceHandles = sourceHandles;
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            Clear("capture-exception");
        }
        // Read-only capture — do not Commit (avoid DBMOD).
    }

    public static bool TryResolve(string? handle, out MappedEntity entry)
    {
        entry = default;
        if (string.IsNullOrWhiteSpace(handle))
        {
            return false;
        }

        lock (Gate)
        {
            return _byHandle.TryGetValue(handle, out entry);
        }
    }

    public static bool TryGetSourceState(ObjectId ownerId, out SourcePreCommandState state)
    {
        lock (Gate)
        {
            return _sourcesByOwnerId.TryGetValue(ownerId, out state!);
        }
    }

    public static bool IsSourceHandle(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return false;
        }

        lock (Gate)
        {
            return _sourceHandles.Contains(handle);
        }
    }

    public static bool IsOwnerSourceErased(
        ObjectId ownerId,
        IReadOnlyCollection<string> erasedHandles)
    {
        if (ownerId.IsNull || erasedHandles.Count == 0)
        {
            return false;
        }

        lock (Gate)
        {
            foreach (var pair in _ownerIdsByHandle)
            {
                if (pair.Value != ownerId)
                {
                    continue;
                }

                foreach (var erased in erasedHandles)
                {
                    if (string.Equals(pair.Key, erased, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        return false;
    }

    public static IReadOnlyCollection<ObjectId> CollectLockedSourceEraseOwners(
        IReadOnlyCollection<string> erasedHandles,
        string? globalCommandName)
    {
        if (erasedHandles.Count == 0)
        {
            return Array.Empty<ObjectId>();
        }

        var owners = new HashSet<ObjectId>();
        lock (Gate)
        {
            foreach (var handle in erasedHandles)
            {
                if (!_byHandle.TryGetValue(handle, out var mapped) ||
                    mapped.Kind != RoofEraseMappedKind.Source ||
                    !_sourcesByOwnerId.TryGetValue(mapped.OwnerId, out var state) ||
                    !RoofDisplayErasePreCommandMapRules.ShouldRestoreLockedSourceErase(
                        state.EditState,
                        globalCommandName))
                {
                    continue;
                }

                owners.Add(mapped.OwnerId);
            }
        }

        return owners.Count == 0 ? Array.Empty<ObjectId>() : owners.ToArray();
    }

    public static IReadOnlyCollection<ObjectId> CollectDisplayOwners(
        IReadOnlyCollection<string> erasedHandles)
    {
        if (erasedHandles.Count == 0)
        {
            return Array.Empty<ObjectId>();
        }

        var owners = new HashSet<ObjectId>();
        lock (Gate)
        {
            foreach (var handle in erasedHandles)
            {
                if (_byHandle.TryGetValue(handle, out var mapped) &&
                    mapped.Kind == RoofEraseMappedKind.Display)
                {
                    owners.Add(mapped.OwnerId);
                }
            }
        }

        return owners.Count == 0 ? Array.Empty<ObjectId>() : owners.ToArray();
    }

    public static int CountErasedDisplaysForOwner(
        ObjectId ownerId,
        IReadOnlyCollection<string> erasedHandles)
    {
        if (ownerId.IsNull || erasedHandles.Count == 0)
        {
            return 0;
        }

        var count = 0;
        lock (Gate)
        {
            foreach (var handle in erasedHandles)
            {
                if (_byHandle.TryGetValue(handle, out var mapped) &&
                    mapped.Kind == RoofEraseMappedKind.Display &&
                    mapped.OwnerId == ownerId)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool TryResolveAnnotationSourceHandle(
        Entity entity,
        out string sourceHandle)
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
