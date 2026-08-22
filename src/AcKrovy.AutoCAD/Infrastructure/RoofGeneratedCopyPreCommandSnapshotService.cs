using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Pre-command generated-timber handles for native COPY (individual-member detach)
/// plus the command-scoped whole-roof assembly snapshot: existing roof owner handles,
/// per-owner generated/AttachedManual/display member handles, and the transient set
/// of appended clone handles consumed by the whole-roof COPY branch.
/// </summary>
internal static class RoofGeneratedCopyPreCommandSnapshotService
{
    private static readonly object Gate = new();
    private static HashSet<string> _generatedHandles = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, HashSet<string>> _logicalKeysByOwner = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _ownerHandles = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, HashSet<string>> _generatedHandlesByOwner = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, HashSet<string>> _attachedManualHandlesByOwner = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, HashSet<string>> _displayHandlesByOwner = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _consumedWholeRoofCloneHandles = new(StringComparer.OrdinalIgnoreCase);

    public static void Clear()
    {
        lock (Gate)
        {
            _generatedHandles.Clear();
            _logicalKeysByOwner.Clear();
            _ownerHandles.Clear();
            _generatedHandlesByOwner.Clear();
            _attachedManualHandlesByOwner.Clear();
            _displayHandlesByOwner.Clear();
            _consumedWholeRoofCloneHandles.Clear();
        }
    }

    public static void CaptureForCopy(Document document)
    {
        Clear();
        ArgumentNullException.ThrowIfNull(document);
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var blockTable = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            OpenMode.ForRead);

        var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keysByOwner = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var ownerHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var generatedByOwner = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var attachedByOwner = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var displayByOwner = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // Display lines are owned metadata (RoofDisplayStore) — collect them in one
        // modelspace pass grouped by their owner reference.
        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                transaction.GetObject(id, OpenMode.ForRead, false) is not Line line ||
                line.IsErased)
            {
                continue;
            }

            var display = RoofDisplayStore.Read(line);
            if (display.Data is null ||
                string.IsNullOrWhiteSpace(display.Data.OwnerReference))
            {
                continue;
            }

            if (!displayByOwner.TryGetValue(display.Data.OwnerReference, out var displaySet))
            {
                displaySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                displayByOwner[display.Data.OwnerReference] = displaySet;
            }

            displaySet.Add(line.Handle.ToString());
        }

        foreach (ObjectId id in modelSpace)
        {
            if (id.IsErased ||
                transaction.GetObject(id, OpenMode.ForRead, false) is not Polyline owner ||
                owner.IsErased ||
                RoofDefinitionStore.Read(owner).Data is null)
            {
                continue;
            }

            var ownerHandle = owner.Handle.ToString();
            ownerHandles.Add(ownerHandle);
            var generatedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var attachedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var timberId in RoofGeneratedTimberStore.FindByOwner(
                         document.Database,
                         transaction,
                         ownerHandle))
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                        transaction,
                        timberId,
                        OpenMode.ForRead,
                        out var line,
                        document.Database) ||
                    line is null)
                {
                    continue;
                }

                var generated = RoofGeneratedTimberStore.Read(line);
                if (generated.Data is null ||
                    generated.Data.MemberKind != RoofGeneratedTimberKind.Rafter ||
                    !metadataStore.TryRead(line, out var timber) ||
                    timber is null ||
                    timber.ElementType != TimberElementType.Rafter)
                {
                    continue;
                }

                var handle = line.Handle.ToString();
                handles.Add(handle);
                generatedSet.Add(handle);
                if (!keysByOwner.TryGetValue(ownerHandle, out var keys))
                {
                    keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    keysByOwner[ownerHandle] = keys;
                }

                keys.Add(RoofGeneratedRafterCopyDetachRules.FormatLogicalKey(
                    generated.Data.RoofFace,
                    generated.Data.StationIndex));
            }

            foreach (var attachedId in RoofAttachedManualTimberStore.FindByOwner(
                         document.Database,
                         transaction,
                         ownerHandle))
            {
                if (AutoCadObjectIdAccess.TryGetObject<Line>(
                        transaction,
                        attachedId,
                        OpenMode.ForRead,
                        out var line,
                        document.Database) &&
                    line is not null)
                {
                    attachedSet.Add(line.Handle.ToString());
                }
            }

            generatedByOwner[ownerHandle] = generatedSet;
            attachedByOwner[ownerHandle] = attachedSet;
        }

        transaction.Commit();
        lock (Gate)
        {
            _generatedHandles = handles;
            _logicalKeysByOwner = keysByOwner;
            _ownerHandles = ownerHandles;
            _generatedHandlesByOwner = generatedByOwner;
            _attachedManualHandlesByOwner = attachedByOwner;
            _displayHandlesByOwner = displayByOwner;
        }
    }

    public static IReadOnlyCollection<string> GetPreCommandGeneratedHandles()
    {
        lock (Gate)
        {
            return _generatedHandles.ToArray();
        }
    }

    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> GetPreCommandLogicalKeysByOwner()
    {
        lock (Gate)
        {
            return _logicalKeysByOwner.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyCollection<string>)pair.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public static IReadOnlyCollection<string> GetPreCommandOwnerHandles()
    {
        lock (Gate)
        {
            return _ownerHandles.ToArray();
        }
    }

    public static IReadOnlyCollection<string> GetPreCommandGeneratedHandlesByOwner(string ownerHandle)
    {
        lock (Gate)
        {
            return _generatedHandlesByOwner.TryGetValue(ownerHandle, out var set)
                ? set.ToArray()
                : Array.Empty<string>();
        }
    }

    public static IReadOnlyCollection<string> GetPreCommandAttachedManualHandlesByOwner(string ownerHandle)
    {
        lock (Gate)
        {
            return _attachedManualHandlesByOwner.TryGetValue(ownerHandle, out var set)
                ? set.ToArray()
                : Array.Empty<string>();
        }
    }

    public static IReadOnlyCollection<string> GetPreCommandDisplayHandlesByOwner(string ownerHandle)
    {
        lock (Gate)
        {
            return _displayHandlesByOwner.TryGetValue(ownerHandle, out var set)
                ? set.ToArray()
                : Array.Empty<string>();
        }
    }

    /// <summary>
    /// Registers appended clone handles already consumed by the whole-roof COPY branch.
    /// The per-rafter services MUST skip them — a consumed clone must never be detached
    /// to AttachedManual under the old owner, even when the whole-roof rebind failed.
    /// </summary>
    public static void RegisterConsumedWholeRoofClones(IEnumerable<string> cloneHandles)
    {
        if (cloneHandles is null)
        {
            return;
        }

        lock (Gate)
        {
            foreach (var handle in cloneHandles)
            {
                if (!string.IsNullOrWhiteSpace(handle))
                {
                    _consumedWholeRoofCloneHandles.Add(handle);
                }
            }
        }
    }

    public static bool IsConsumedWholeRoofClone(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return false;
        }

        lock (Gate)
        {
            return _consumedWholeRoofCloneHandles.Contains(handle);
        }
    }
}
