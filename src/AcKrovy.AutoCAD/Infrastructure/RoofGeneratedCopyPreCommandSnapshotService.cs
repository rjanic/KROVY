using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Pre-command roof ownership snapshot reused by native COPY and by repeated proven
/// same-DWG individual clipboard pastes. Clipboard provenance is bound to the
/// exact tracked live source Document. Database wrapper identity is diagnostic only;
/// owner handles are payload data, never provenance. Native COPY also uses the
/// command-scoped whole-roof assembly fields.
/// </summary>
internal static class RoofGeneratedCopyPreCommandSnapshotService
{
    private static readonly object Gate = new();
    private static SnapshotState? _activeSnapshot;
    private static readonly RoofClipboardProvenanceLifecycle<Document, Database, SnapshotState>
        ClipboardLifecycle = new();

    private sealed record SnapshotState(
        Document SourceDocument,
        Database SourceDatabase,
        HashSet<string> GeneratedHandles,
        Dictionary<string, HashSet<string>> LogicalKeysByOwner,
        HashSet<string> OwnerHandles,
        Dictionary<string, HashSet<string>> GeneratedHandlesByOwner,
        Dictionary<string, HashSet<string>> AttachedManualHandlesByOwner,
        Dictionary<string, HashSet<string>> DisplayHandlesByOwner,
        HashSet<string> ConsumedWholeRoofCloneHandles);

    public static void Clear()
    {
        lock (Gate)
        {
            _activeSnapshot = null;
        }
    }

    public static void CaptureForCopy(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var snapshot = Capture(document);
        lock (Gate)
        {
            _activeSnapshot = snapshot;
        }
    }

    public static void CaptureForClipboardCopy(Document document, string? globalCommandName)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!LiveGeometryCommandRules.IsClipboardCopySourceCommand(globalCommandName))
        {
            return;
        }

        var snapshot = Capture(document);
        lock (Gate)
        {
            _activeSnapshot = null;
            ClipboardLifecycle.BeginCopy(
                document,
                snapshot.SourceDatabase,
                LiveGeometryCommandRules.NormalizeCommandName(globalCommandName),
                snapshot);
        }
    }

    /// <summary>
    /// Publishes the pending source snapshot only after COPYCLIP/COPYBASE succeeds.
    /// The OS clipboard revision binds the durable token to the actual current
    /// clipboard payload, so unrelated clipboard replacement cannot reuse it.
    /// </summary>
    public static void CompleteClipboardCopy(
        Document document,
        string? globalCommandName)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!LiveGeometryCommandRules.IsClipboardCopySourceCommand(globalCommandName))
        {
            return;
        }

        var clipboardRevision = ReadClipboardRevision();
        bool stored;
        SnapshotState? durableSnapshot;
        lock (Gate)
        {
            stored = ClipboardLifecycle.CompleteCopy(clipboardRevision);
            durableSnapshot = ClipboardLifecycle.DurablePayload;
        }

#if DEBUG
        TraceClipboardProvenance(
            document,
            "phase=capture " +
            $"command={LiveGeometryCommandRules.NormalizeCommandName(globalCommandName)} " +
            $"clipboardRevision={clipboardRevision} " +
            $"sourceDocument={DebugIdentity(document)} " +
            $"sourceDatabase={DebugIdentity(durableSnapshot?.SourceDatabase)} " +
            $"result={(stored ? "stored" : "not-stored")}",
            globalCommandName);
#endif
    }

    /// <summary>
    /// Activates the captured clipboard ownership snapshot only when the paste
    /// target is the exact same tracked live Document. Database wrapper identity and
    /// inherited owner handles are deliberately not provenance proof.
    /// </summary>
    public static bool TryActivateForClipboardPaste(
        Document targetDocument,
        string? globalCommandName)
    {
        ArgumentNullException.ThrowIfNull(targetDocument);
        if (!LiveGeometryCommandRules.IsClipboardPasteCommand(globalCommandName))
        {
            return false;
        }

        var targetDatabase = targetDocument.Database;
        var clipboardRevision = ReadClipboardRevision();
        RoofClipboardPasteProvenanceDecision decision;
        lock (Gate)
        {
            decision = ClipboardLifecycle.BeginPaste(
                targetDocument,
                targetDatabase,
                clipboardRevision);
            _activeSnapshot = decision.IsValid ? ClipboardLifecycle.ActivePayload : null;
        }

#if DEBUG
        TraceClipboardProvenance(
            targetDocument,
            "phase=paste-start " +
            $"command={LiveGeometryCommandRules.NormalizeCommandName(globalCommandName)} " +
            $"clipboardRevision={clipboardRevision} " +
            $"targetDocument={DebugIdentity(targetDocument)} " +
            $"targetDatabase={DebugIdentity(targetDatabase)} " +
            $"sameDocument={Lower(decision.SameDocument)} " +
            $"databaseReferenceEqual={Lower(decision.DatabaseReferenceEqual)} " +
            $"sameDrawing={Lower(decision.SameDrawing)} " +
            $"valid={Lower(decision.IsValid)} " +
            $"result={decision.DiagnosticResult}",
            globalCommandName);
#endif
        return decision.IsValid;
    }

    /// <summary>
    /// Ends the current paste activation while retaining provenance for repeated paste
    /// commands because the clipboard payload itself did not change.
    /// </summary>
    public static void CompleteClipboardPaste()
    {
        lock (Gate)
        {
            _activeSnapshot = null;
            ClipboardLifecycle.CompletePaste();
        }
    }

    public static void CancelOrFailClipboardPaste()
    {
        lock (Gate)
        {
            _activeSnapshot = null;
            ClipboardLifecycle.CancelOrFailPaste();
        }
    }

    public static void CancelOrFailClipboardSource(
        Document document,
        string? globalCommandName)
    {
        if (!LiveGeometryCommandRules.IsClipboardCopySourceCommand(globalCommandName))
        {
            return;
        }

        lock (Gate)
        {
            ClipboardLifecycle.CancelOrFailCopy(document);
            _activeSnapshot = null;
        }
    }

    public static void ClearForDocument(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (Gate)
        {
            if (_activeSnapshot is { } active &&
                ReferenceEquals(active.SourceDocument, document))
            {
                _activeSnapshot = null;
            }

            ClipboardLifecycle.ClearForDocument(document);
        }
    }

    private static uint ReadClipboardRevision()
    {
        try
        {
            return NativeMethods.GetClipboardSequenceNumber();
        }
        catch (Exception)
        {
            return 0;
        }
    }

#if DEBUG
    private static void TraceClipboardProvenance(
        Document document,
        string detail,
        string? globalCommandName)
    {
        var message = $"ROOF_CLIPBOARD_PROVENANCE {detail}";
        document.Editor.WriteMessage("\n" + message);
        Diagnostics.AcKrovyDiagnostics.Info(
            "RoofClipboardProvenance",
            message,
            LiveGeometryCommandRules.NormalizeCommandName(globalCommandName));
    }

    private static string DebugIdentity(object? value) => value is null
        ? "none"
        : RuntimeHelpers.GetHashCode(value).ToString("X8", CultureInfo.InvariantCulture);

    private static string Lower(bool value) => value ? "true" : "false";
#endif

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern uint GetClipboardSequenceNumber();
    }

    private static SnapshotState Capture(Document document)
    {
        var database = document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
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
                         database,
                         transaction,
                         ownerHandle))
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                        transaction,
                        timberId,
                        OpenMode.ForRead,
                        out var line,
                        database) ||
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
                         database,
                         transaction,
                         ownerHandle))
            {
                if (AutoCadObjectIdAccess.TryGetObject<Line>(
                        transaction,
                        attachedId,
                        OpenMode.ForRead,
                        out var line,
                        database) &&
                    line is not null)
                {
                    attachedSet.Add(line.Handle.ToString());
                }
            }

            generatedByOwner[ownerHandle] = generatedSet;
            attachedByOwner[ownerHandle] = attachedSet;
        }

        transaction.Commit();
        return new SnapshotState(
            document,
            database,
            handles,
            keysByOwner,
            ownerHandles,
            generatedByOwner,
            attachedByOwner,
            displayByOwner,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    public static IReadOnlyCollection<string> GetPreCommandGeneratedHandles()
    {
        lock (Gate)
        {
            return _activeSnapshot?.GeneratedHandles.ToArray() ?? Array.Empty<string>();
        }
    }

    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> GetPreCommandLogicalKeysByOwner()
    {
        lock (Gate)
        {
            return (_activeSnapshot?.LogicalKeysByOwner ??
                    new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase))
                .ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyCollection<string>)pair.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public static IReadOnlyCollection<string> GetPreCommandOwnerHandles()
    {
        lock (Gate)
        {
            return _activeSnapshot?.OwnerHandles.ToArray() ?? Array.Empty<string>();
        }
    }

    public static IReadOnlyCollection<string> GetPreCommandGeneratedHandlesByOwner(string ownerHandle)
    {
        lock (Gate)
        {
            return _activeSnapshot is { } snapshot &&
                   snapshot.GeneratedHandlesByOwner.TryGetValue(ownerHandle, out var set)
                ? set.ToArray()
                : Array.Empty<string>();
        }
    }

    public static IReadOnlyCollection<string> GetPreCommandAttachedManualHandlesByOwner(string ownerHandle)
    {
        lock (Gate)
        {
            return _activeSnapshot is { } snapshot &&
                   snapshot.AttachedManualHandlesByOwner.TryGetValue(ownerHandle, out var set)
                ? set.ToArray()
                : Array.Empty<string>();
        }
    }

    public static IReadOnlyCollection<string> GetPreCommandDisplayHandlesByOwner(string ownerHandle)
    {
        lock (Gate)
        {
            return _activeSnapshot is { } snapshot &&
                   snapshot.DisplayHandlesByOwner.TryGetValue(ownerHandle, out var set)
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
            if (_activeSnapshot is not { } snapshot)
            {
                return;
            }

            foreach (var handle in cloneHandles)
            {
                if (!string.IsNullOrWhiteSpace(handle))
                {
                    snapshot.ConsumedWholeRoofCloneHandles.Add(handle);
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
            return _activeSnapshot?.ConsumedWholeRoofCloneHandles.Contains(handle) == true;
        }
    }
}
