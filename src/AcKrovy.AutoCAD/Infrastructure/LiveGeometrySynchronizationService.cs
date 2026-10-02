using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.Diagnostics;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class LiveGeometrySynchronizationService
{
    private static readonly Dictionary<Document, DocumentTracker> Trackers = new();
    private static bool _isStarted;

    public static void Start()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        var documents = AcApp.DocumentManager;
        documents.DocumentCreated += DocumentCreated;
        documents.DocumentToBeDestroyed += DocumentToBeDestroyed;

        foreach (Document document in documents)
        {
            Attach(document);
        }
    }

    public static void Stop()
    {
        if (!_isStarted)
        {
            return;
        }

        _isStarted = false;
        var documents = AcApp.DocumentManager;
        documents.DocumentCreated -= DocumentCreated;
        documents.DocumentToBeDestroyed -= DocumentToBeDestroyed;

        foreach (var tracker in Trackers.Values.ToList())
        {
            tracker.Dispose();
        }

        Trackers.Clear();
    }

    private static void DocumentCreated(object? sender, DocumentCollectionEventArgs e)
    {
        if (e.Document is not null)
        {
            Attach(e.Document);
        }
    }

    private static void DocumentToBeDestroyed(object? sender, DocumentCollectionEventArgs e)
    {
        if (e.Document is not null && Trackers.TryGetValue(e.Document, out var tracker))
        {
            tracker.Dispose();
            Trackers.Remove(e.Document);
        }
    }

    private static void Attach(Document document)
    {
        if (Trackers.ContainsKey(document))
        {
            return;
        }

        Trackers[document] = new DocumentTracker(document);
    }

    private sealed class DocumentTracker : IDisposable
    {
        private readonly Document _document;
        private readonly RoofNativeCloneSnapshot _nativeRoofClones = new();
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _modifiedIds = new();
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _appendedTimberIds = new();
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _appendedRoofOwnerIds = new();
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _modifiedFramedLabelIds = new();
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _appendedLabelIds = new();
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _appendedSlopeArrowIds = new();
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _appendedSlopeAngleTextIds = new();
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _appendedPasteEntityIds = new();
        private readonly LiveGeometryRefreshCoordinator<string> _erasedSourceHandles = new();
        private bool _ignoreCurrentCommand;
        private bool _refreshAllTimberAnnotationsAfterCommand;
        private bool _preserveCopySourcesForCurrentCommand;
        private bool _sameDwgClipboardPasteForCurrentCommand;
        private RoofClipboardPasteProvenanceDecision? _clipboardPasteProvenanceForCurrentCommand;
        private bool _stretchUndoMarkOpen;
        private bool _groupSelectabilityReconciled;
        private bool _isDisposed;
        private string? _currentGlobalCommandName;

        public DocumentTracker(Document document)
        {
            _document = document;
            _document.Database.ObjectAppended += ObjectAppended;
            _document.Database.BeginDeepCloneTranslation += NativeRoofCloneMapping;
            _document.Database.ObjectModified += ObjectModified;
            _document.Database.ObjectErased += ObjectErased;
            _document.CommandWillStart += CommandWillStart;
            _document.CommandEnded += CommandEnded;
            _document.CommandCancelled += CommandCancelled;
            _document.CommandFailed += CommandFailed;
            TryReconcileRoofGroupSelectabilityOnce();
        }

        private void TryReconcileRoofGroupSelectabilityOnce()
        {
            if (_groupSelectabilityReconciled)
            {
                return;
            }

            _groupSelectabilityReconciled = true;
            try
            {
                using (_document.LockDocument())
                using (var transaction = _document.Database.TransactionManager.StartTransaction())
                {
                    if (RoofDisplayGroupSelectabilityService.ReconcileAllRoofOwners(
                            _document.Database,
                            transaction))
                    {
                        transaction.Commit();
                    }
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _document.Database.ObjectAppended -= ObjectAppended;
            _document.Database.BeginDeepCloneTranslation -= NativeRoofCloneMapping;
            _document.Database.ObjectModified -= ObjectModified;
            _document.Database.ObjectErased -= ObjectErased;
            _document.CommandWillStart -= CommandWillStart;
            _document.CommandEnded -= CommandEnded;
            _document.CommandCancelled -= CommandCancelled;
            _document.CommandFailed -= CommandFailed;
            _modifiedIds.Clear();
            _appendedTimberIds.Clear();
            _appendedRoofOwnerIds.Clear();
            _modifiedFramedLabelIds.Clear();
            _appendedLabelIds.Clear();
            _appendedSlopeArrowIds.Clear();
            _appendedSlopeAngleTextIds.Clear();
            _appendedPasteEntityIds.Clear();
            _erasedSourceHandles.Clear();
            _refreshAllTimberAnnotationsAfterCommand = false;
            _preserveCopySourcesForCurrentCommand = false;
            _sameDwgClipboardPasteForCurrentCommand = false;
            _clipboardPasteProvenanceForCurrentCommand = null;
            _currentGlobalCommandName = null;
            RoofGeneratedCopyPreCommandSnapshotService.ClearForDocument(_document);
            EndStretchUndoMark();
            RoofLiveResizeService.EndStretchCommandScope();
            RoofGroupGripGeometrySnapshotService.EndCommandScope("dispose");
            RoofGroupGripPreCommandBaselineService.Clear("dispose");
            RoofUnsupportedStretchRecoverySnapshotService.Clear("dispose");
            RoofDisplayErasePreCommandMapService.Clear("dispose");
#if DEBUG
            AutoCadFramedBlockContentStretchNormalizeLifecycleService.RemoveSession(_document);
            AutoCadFramedBlockContentGripUndoProofService.RemoveSession(_document);
            AutoCadFramedBlockContentGripPassthroughProofService.RemoveSession(_document);
            AutoCadFramedBlockContentGripReadonlyProofService.RemoveSession(_document);
            AutoCadFramedBlockContentGripNormalizeProofService.RemoveSession(_document);
#endif
            AutoCadFramedBlockContentProductionGripNormalizeService
                .ForceReleaseProcessingGuard();
        }

        private void ObjectAppended(object? sender, ObjectEventArgs e)
        {
            try
            {
                if (_ignoreCurrentCommand ||
                    _modifiedIds.IsSuppressed ||
                    e.DBObject is not Entity entity ||
                    entity.ObjectId.IsNull ||
                    entity.IsErased)
                {
                    return;
                }

                // Per-Document command-scope evidence for native/clipboard clones. Capture every Entity
                // before any observation branch can return. Deep-cloned metadata may not
                // be final until CommandEnded, where these exact ids are reopened.
                if (!_appendedPasteEntityIds.IsSuppressed &&
                    (LiveGeometryCommandRules.IsClipboardPasteCommand(_currentGlobalCommandName) ||
                     LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(_currentGlobalCommandName) ||
                     RoofGeneratedMemberEditCommandRules.IsMirrorCommand(_currentGlobalCommandName)))
                {
                    _appendedPasteEntityIds.TryAdd(entity.ObjectId);
                }

                if (!_appendedSlopeArrowIds.IsSuppressed &&
                    (SlopeArrowStore.TryRead(entity, out _) ||
                     PostFootprintPerpendicularAnnotationStore.TryRead(entity, out _)))
                {
                    _appendedSlopeArrowIds.TryAdd(entity.ObjectId);
                    return;
                }

                if (!_appendedSlopeAngleTextIds.IsSuppressed && SlopeAngleTextStore.TryRead(entity, out _))
                {
                    _appendedSlopeAngleTextIds.TryAdd(entity.ObjectId);
                    return;
                }

                if (!_appendedLabelIds.IsSuppressed &&
                    ElementLabelStore.TryRead(entity, out _))
                {
                    _appendedLabelIds.TryAdd(entity.ObjectId);
                    return;
                }

                if (!_appendedRoofOwnerIds.IsSuppressed &&
                    entity is Polyline &&
                    RoofDefinitionStore.Read(entity).Data is not null)
                {
                    _appendedRoofOwnerIds.TryAdd(entity.ObjectId);
                    return;
                }

                if (AutoCadEntityHelpers.IsSupportedTimberGeometry(entity))
                {
                    _appendedTimberIds.TryAdd(entity.ObjectId);
                    _modifiedIds.TryAdd(entity.ObjectId);
                    return;
                }

                if (!_appendedLabelIds.IsSuppressed &&
                    entity is MText or MLeader or BlockReference or DBText)
                {
                    _appendedLabelIds.TryAdd(entity.ObjectId);
                }
            }
            catch
            {
                // Observation must never abort other Database.ObjectAppended subscribers
                // (import session append evidence depends on the remaining multicast).
            }
        }

        private void ObjectModified(object? sender, ObjectEventArgs e)
        {
            if (_ignoreCurrentCommand ||
                e.DBObject is not Entity entity ||
                entity.ObjectId.IsNull ||
                entity.IsErased)
            {
                return;
            }

            // Native GRIP/STRETCH geometry snapshot MUST run before suppress early-out
            // is irrelevant for plugin writes: when suppressed, capture is skipped inside.
            RoofGroupGripGeometrySnapshotService.TryCaptureNativeObjectModified(
                entity,
                _currentGlobalCommandName,
                _modifiedIds.IsSuppressed);

            if (_modifiedIds.IsSuppressed)
            {
                return;
            }

            // Framed MLeader presentation tracking remains for Unlocked
            // PersistFramedManualOffsets. Do NOT early-return: Locked roofs need
            // the same ObjectId in _modifiedIds so RoofLiveResizeService Inspect
            // can resolve SourceHandle → owner → LockedAnnotationTamper recovery.
            // Slope/post annotations with readable XData must also enter _modifiedIds
            // (previously they fell out of both queues when TryRead succeeded).
            if (entity is MLeader)
            {
                _modifiedFramedLabelIds.TryAdd(entity.ObjectId);
#if DEBUG
                AutoCadFramedBlockContentStretchNormalizeLifecycleService.TraceQueueMLeader(
                    _document,
                    entity.ObjectId);
#endif
            }

            _modifiedIds.TryAdd(entity.ObjectId);
        }

        private void ObjectErased(object? sender, ObjectErasedEventArgs e)
        {
            if (_ignoreCurrentCommand ||
                _erasedSourceHandles.IsSuppressed)
            {
                return;
            }

            if (e.DBObject is not Entity entity)
            {
                return;
            }

            var handle = entity.Handle.ToString();
            var mapped = RoofDisplayErasePreCommandMapService.TryResolve(handle, out var mappedEntity);
            var mappedKind = mapped ? mappedEntity.Kind.ToString() : "-";
            var mappedOwner = mapped ? mappedEntity.OwnerHandle : "-";
            var shouldQueue =
                e.Erased &&
                (mapped || AutoCadEntityHelpers.IsSupportedTimberGeometry(entity));
            var action = !e.Erased
                ? "ignore-unerase"
                : shouldQueue
                    ? mapped
                        ? "queued-mapped"
                        : "queued-geometry"
                    : "ignore-unmapped";

#if DEBUG
            RoofGeneratedMemberManualEditDiag.WriteObjectErased(
                _document.Editor,
                entity.ObjectId.ToString(),
                handle,
                e.Erased,
                unerased: !e.Erased,
                _currentGlobalCommandName,
                _document.Name,
                _document.Database.Filename,
                mappedKind,
                mappedOwner,
                action);
#endif

            if (!shouldQueue)
            {
                return;
            }

            _erasedSourceHandles.TryAdd(handle);
        }

        private void NativeRoofCloneMapping(object? sender, IdMappingEventArgs e)
        {
            if (_ignoreCurrentCommand || LiveGeometryCommandRules.IsUndoRedoCommand(_currentGlobalCommandName) ||
                !(LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(_currentGlobalCommandName) ||
                  RoofGeneratedMemberEditCommandRules.IsMirrorCommand(_currentGlobalCommandName))) return;
            // Copy IDs only. Metadata was captured before the native command; no
            // writes or nested transaction in the native translation callback.
            _nativeRoofClones.Observe(e.IdMapping);
        }

        private void CommandWillStart(object? sender, CommandEventArgs e)
        {
            _nativeRoofClones.Clear();
            var isUndoRedo = LiveGeometryCommandRules.IsUndoRedoCommand(e.GlobalCommandName);
            _ignoreCurrentCommand = IsAcKrovyCommand(e.GlobalCommandName) || isUndoRedo;
            _currentGlobalCommandName = e.GlobalCommandName;
            _refreshAllTimberAnnotationsAfterCommand =
                !isUndoRedo &&
                LiveGeometryCommandRules.RequiresFullTimberAnnotationRefresh(e.GlobalCommandName);
            _preserveCopySourcesForCurrentCommand =
                !isUndoRedo &&
                LiveGeometryCommandRules.IsCopySourcePreservingCommand(e.GlobalCommandName);
            _sameDwgClipboardPasteForCurrentCommand = false;
            _clipboardPasteProvenanceForCurrentCommand = null;
            if (!isUndoRedo &&
                !_ignoreCurrentCommand &&
                LiveGeometryCommandRules.IsClipboardCopySourceCommand(e.GlobalCommandName))
            {
                RoofGeneratedCopyPreCommandSnapshotService.CaptureForClipboardCopy(
                    _document,
                    e.GlobalCommandName);
            }
            else if (!isUndoRedo &&
                     !_ignoreCurrentCommand &&
                     LiveGeometryCommandRules.IsClipboardPasteCommand(e.GlobalCommandName))
            {
                _sameDwgClipboardPasteForCurrentCommand =
                    RoofGeneratedCopyPreCommandSnapshotService.TryActivateForClipboardPaste(
                        _document,
                        e.GlobalCommandName,
                        out var clipboardPasteProvenance);
                _clipboardPasteProvenanceForCurrentCommand = clipboardPasteProvenance;
            }
            // Clipboard provenance represents the current clipboard payload, not the
            // immediately preceding command. PAN/ZOOM/view/internal commands therefore
            // do not invalidate it; the OS clipboard revision does that authoritatively.
            // Always clear SOURCE-handled owner suppression at the boundary of a new
            // native command so a later genuine display-only GRIP_STRETCH is not masked.
            RoofLiveResizeService.BeginStretchCommandScope();
            RoofGroupGripGeometrySnapshotService.BeginCommandScope(e.GlobalCommandName);
            // True pre-command baseline MUST be captured here, before any native
            // ObjectModified can mutate DB geometry. Do not clear implied selection.
            // Whole-roof COPY and whole-roof MIRROR (Erase source = No) share the same
            // pre-command ownership snapshot so CommandEnded can detect a complete
            // assembly clone before any per-member detach/rehydration runs.
            if (!isUndoRedo &&
                !_ignoreCurrentCommand &&
                (LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(e.GlobalCommandName) ||
                 RoofGeneratedMemberEditCommandRules.IsMirrorCommand(e.GlobalCommandName)))
            {
                RoofGeneratedCopyPreCommandSnapshotService.CaptureForCopy(_document);
                try { _nativeRoofClones.Capture(_document.Database); }
                catch (System.Exception) { _nativeRoofClones.Clear(); }
            }

            if (!isUndoRedo &&
                !_ignoreCurrentCommand &&
                LiveGeometryCommandRules.IsGripStretchCommand(e.GlobalCommandName))
            {
                RoofGroupGripPreCommandBaselineService.CaptureFromImpliedSelection(
                    _document,
                    e.GlobalCommandName);
            }
            else
            {
                RoofGroupGripPreCommandBaselineService.Clear("non-grip-command");
            }

            // Generated-member lifecycle recovery: capture the exact roof assembly
            // before any supported or known-unsupported native edit can mutate it.
            // Structural first-claim for ARRAY/FILLET/etc. reuses the same capture
            // without widening ordinary IsAssemblySnapshotCommand routing.
            if (!isUndoRedo &&
                !_ignoreCurrentCommand &&
                (RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand(e.GlobalCommandName) ||
                 RoofStructuralEditRules.RequiresAssemblySnapshotCapture(e.GlobalCommandName)))
            {
                RoofUnsupportedStretchRecoverySnapshotService.CaptureForCommand(
                    _document,
                    e.GlobalCommandName);
            }
            else
            {
                RoofUnsupportedStretchRecoverySnapshotService.Clear("non-recovery-command", e.GlobalCommandName);
            }

            // Read-only display→owner map for native ERASE. ObjectErased resolves
            // through this map; never rely on post-erase XData from erased DBObjects.
            if (!isUndoRedo &&
                !_ignoreCurrentCommand &&
                RoofGeneratedMemberEditCommandRules.IsEraseCommand(e.GlobalCommandName))
            {
                RoofDisplayErasePreCommandMapService.CaptureForErase(
                    _document,
                    e.GlobalCommandName);
            }
            else
            {
                RoofDisplayErasePreCommandMapService.Clear("non-erase-command");
            }

            if (!isUndoRedo &&
                !_ignoreCurrentCommand &&
                (LiveGeometryCommandRules.RequiresGroupedUndoMark(e.GlobalCommandName) ||
                 _sameDwgClipboardPasteForCurrentCommand))
            {
                _stretchUndoMarkOpen = RoofLiveResizeService.TryBeginGroupedUndo(_document);
            }
#if DEBUG
            AutoCadRedoDiagService.OnCommandWillStart(e.GlobalCommandName);
            AutoCadFramedBlockContentStretchNormalizeLifecycleService.TraceWillStart(
                _document,
                e.GlobalCommandName);
#endif
            if (_ignoreCurrentCommand)
            {
                ClearPendingLiveGeometryState();
            }
        }

        private void CommandEnded(object? sender, CommandEventArgs e)
        {
            var isUndoRedo = LiveGeometryCommandRules.IsUndoRedoCommand(e.GlobalCommandName);
            var shouldIgnore =
                _ignoreCurrentCommand ||
                isUndoRedo ||
                IsAcKrovyCommand(e.GlobalCommandName);
            var refreshAllTimberAnnotations = _refreshAllTimberAnnotationsAfterCommand;
            var preserveCopySources = _preserveCopySourcesForCurrentCommand;
            var sameDwgClipboardPaste = _sameDwgClipboardPasteForCurrentCommand;
            var clipboardPasteProvenance = _clipboardPasteProvenanceForCurrentCommand;
            _refreshAllTimberAnnotationsAfterCommand = false;
            _preserveCopySourcesForCurrentCommand = false;
            _sameDwgClipboardPasteForCurrentCommand = false;
            _clipboardPasteProvenanceForCurrentCommand = null;
#if DEBUG
            AutoCadRedoDiagService.OnCommandEnded(e.GlobalCommandName);
#endif
            if (LiveGeometryCommandRules.IsClipboardCopySourceCommand(e.GlobalCommandName))
            {
                RoofGeneratedCopyPreCommandSnapshotService.CompleteClipboardCopy(
                    _document,
                    e.GlobalCommandName);
            }
            try
            {
                if (shouldIgnore)
                {
                    // Undo/Redo (and AK_): clear queued dirty state only — never
                    // LockDocument / StartTransaction / RefreshTimberElements.
                    ClearPendingLiveGeometryState();
                    // Keep ignore armed after U/UNDO/REDO/MREDO so deferred
                    // ObjectModified/Appended from annotation restore cannot
                    // re-queue work that a later non-undo CommandEnded would
                    // refresh (that write txn clears the native REDO stack).
                    // CommandWillStart of the next real edit disarms this.
                    _ignoreCurrentCommand = isUndoRedo;
#if DEBUG
                    AutoCadFramedBlockContentStretchNormalizeLifecycleService
                        .TraceCancelledOrFailed(_document, "CommandIgnored", e.GlobalCommandName);
                    if (isUndoRedo)
                    {
                        AutoCadRedoDiagService.OnLiveGeometryRefreshSkippedUndoRedo(e.GlobalCommandName);
                    }
                    else
                    {
                        AutoCadRedoDiagService.OnLiveGeometryRefreshSkippedEmpty(e.GlobalCommandName);
                    }
#endif
                    return;
                }

                _ignoreCurrentCommand = false;
                RefreshCandidates(
                    e.GlobalCommandName,
                    refreshAllTimberAnnotations,
                    preserveCopySources,
                    sameDwgClipboardPaste,
                    clipboardPasteProvenance);
            }
            finally
            {
                if (LiveGeometryCommandRules.IsClipboardPasteCommand(e.GlobalCommandName))
                {
                    RoofGeneratedCopyPreCommandSnapshotService.CompleteClipboardPaste();
                }

                EndStretchUndoMark();
                RoofLiveResizeService.EndStretchCommandScope();
                RoofGroupGripGeometrySnapshotService.EndCommandScope("CommandEnded");
                RoofGroupGripPreCommandBaselineService.Clear("CommandEnded");
                RoofUnsupportedStretchRecoverySnapshotService.Clear("CommandEnded", e.GlobalCommandName);
                RoofDisplayErasePreCommandMapService.Clear("CommandEnded");
                _currentGlobalCommandName = null;
#if DEBUG
                if (!isUndoRedo && !shouldIgnore)
                    RoofPhysical3DHostDiagnostics.MaintenanceComplete(_document, e.GlobalCommandName);
#endif
            }
        }

        private void CommandCancelled(object? sender, CommandEventArgs e)
        {
            var isUndoRedo = LiveGeometryCommandRules.IsUndoRedoCommand(e.GlobalCommandName);
            ClearPendingLiveGeometryState();
            _refreshAllTimberAnnotationsAfterCommand = false;
            _preserveCopySourcesForCurrentCommand = false;
            _sameDwgClipboardPasteForCurrentCommand = false;
            _clipboardPasteProvenanceForCurrentCommand = null;
            if (LiveGeometryCommandRules.IsClipboardPasteCommand(e.GlobalCommandName))
            {
                RoofGeneratedCopyPreCommandSnapshotService.CancelOrFailClipboardPaste();
            }
            else
            {
                RoofGeneratedCopyPreCommandSnapshotService.CancelOrFailClipboardSource(
                    _document,
                    e.GlobalCommandName);
            }
            if (LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(e.GlobalCommandName))
            {
                RoofGeneratedCopyPreCommandSnapshotService.Clear();
            }
            _ignoreCurrentCommand = isUndoRedo;
            EndStretchUndoMark();
            RoofLiveResizeService.EndStretchCommandScope();
            RoofGroupGripGeometrySnapshotService.EndCommandScope("CommandCancelled");
            RoofGroupGripPreCommandBaselineService.Clear("CommandCancelled");
            RoofUnsupportedStretchRecoverySnapshotService.Clear("CommandCancelled", e.GlobalCommandName);
            RoofDisplayErasePreCommandMapService.Clear("CommandCancelled");
            _currentGlobalCommandName = null;
#if DEBUG
            AutoCadRedoDiagService.OnCommandCancelledOrFailed(
                "CommandCancelled",
                e.GlobalCommandName);
            AutoCadFramedBlockContentStretchNormalizeLifecycleService.TraceCancelledOrFailed(
                _document,
                "CommandCancelled",
                e.GlobalCommandName);
#endif
        }

        private void CommandFailed(object? sender, CommandEventArgs e)
        {
            var isUndoRedo = LiveGeometryCommandRules.IsUndoRedoCommand(e.GlobalCommandName);
            ClearPendingLiveGeometryState();
            _refreshAllTimberAnnotationsAfterCommand = false;
            _preserveCopySourcesForCurrentCommand = false;
            _sameDwgClipboardPasteForCurrentCommand = false;
            _clipboardPasteProvenanceForCurrentCommand = null;
            if (LiveGeometryCommandRules.IsClipboardPasteCommand(e.GlobalCommandName))
            {
                RoofGeneratedCopyPreCommandSnapshotService.CancelOrFailClipboardPaste();
            }
            else
            {
                RoofGeneratedCopyPreCommandSnapshotService.CancelOrFailClipboardSource(
                    _document,
                    e.GlobalCommandName);
            }
            if (LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(e.GlobalCommandName))
            {
                RoofGeneratedCopyPreCommandSnapshotService.Clear();
            }
            _ignoreCurrentCommand = isUndoRedo;
            EndStretchUndoMark();
            RoofLiveResizeService.EndStretchCommandScope();
            RoofGroupGripGeometrySnapshotService.EndCommandScope("CommandFailed");
            RoofGroupGripPreCommandBaselineService.Clear("CommandFailed");
            RoofUnsupportedStretchRecoverySnapshotService.Clear("CommandFailed", e.GlobalCommandName);
            RoofDisplayErasePreCommandMapService.Clear("CommandFailed");
            _currentGlobalCommandName = null;
#if DEBUG
            AutoCadRedoDiagService.OnCommandCancelledOrFailed(
                "CommandFailed",
                e.GlobalCommandName);
            AutoCadFramedBlockContentStretchNormalizeLifecycleService.TraceCancelledOrFailed(
                _document,
                "CommandFailed",
                e.GlobalCommandName);
#endif
        }

        private void ClearPendingLiveGeometryState()
        {
            _nativeRoofClones.Clear();
            _modifiedIds.Clear();
            _appendedTimberIds.Clear();
            _appendedRoofOwnerIds.Clear();
            _modifiedFramedLabelIds.Clear();
            _appendedLabelIds.Clear();
            _appendedSlopeArrowIds.Clear();
            _appendedSlopeAngleTextIds.Clear();
            _appendedPasteEntityIds.Clear();
            _erasedSourceHandles.Clear();
        }

        private void EndStretchUndoMark()
        {
            if (!_stretchUndoMarkOpen)
            {
                return;
            }

            RoofLiveResizeService.TryEndGroupedUndo(_document, true);
            _stretchUndoMarkOpen = false;
        }

        private void RefreshCandidates(
            string? globalCommandName,
            bool refreshAllTimberAnnotations,
            bool preserveCopySources,
            bool sameDwgClipboardPaste,
            RoofClipboardPasteProvenanceDecision? clipboardPasteProvenance)
        {
            // Belt-and-suspenders: never open a write transaction after Undo/Redo.
            if (LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName))
            {
                ClearPendingLiveGeometryState();
#if DEBUG
                AutoCadRedoDiagService.OnLiveGeometryRefreshSkippedUndoRedo(globalCommandName);
#endif
                return;
            }

#if DEBUG
            var commandCompletionWatch = System.Diagnostics.Stopwatch.StartNew();
#endif
            var ids = _modifiedIds.Drain();
            var appendedTimberIds = _appendedTimberIds.Drain();
            var appendedRoofOwnerIds = _appendedRoofOwnerIds.Drain();
            var modifiedFramedLabelIds = _modifiedFramedLabelIds.Drain();
            var appendedLabelIds = _appendedLabelIds.Drain();
            var appendedSlopeArrowIds = _appendedSlopeArrowIds.Drain();
            var appendedSlopeAngleTextIds = _appendedSlopeAngleTextIds.Drain();
            var appendedPasteEntityIds = _appendedPasteEntityIds.Drain();
            var erasedSourceHandles = _erasedSourceHandles.Drain();
            var appendedIntelligentTimberCount =
                LiveGeometryCommandRules.IsClipboardPasteCommand(globalCommandName)
                    ? CountAppendedIntelligentRoofTimbers(_document, appendedTimberIds)
                    : 0;
            RoofClipboardPastePayloadSnapshot? finalPastePayload = null;
            RoofClipboardPasteOwnershipDecision clipboardDecision;
            if (LiveGeometryCommandRules.IsClipboardPasteCommand(globalCommandName))
            {
                finalPastePayload = RoofClipboardPastePayloadInspectionService.Inspect(
                    _document,
                    appendedPasteEntityIds);
                clipboardDecision = RoofClipboardPasteOwnershipRules.Classify(
                    globalCommandName,
                    clipboardPasteProvenance?.ProvenanceKind ??
                        RoofClipboardPasteProvenanceKind.Unknown,
                    finalPastePayload.IntelligentTimberIds.Count,
                    finalPastePayload.RoofOwnerIds.Count > 0,
                    finalPastePayload.GenericTimberIds.Count > 0);
#if DEBUG
                TraceClipboardClassification(
                    _document,
                    globalCommandName,
                    clipboardPasteProvenance?.ProvenanceKind ??
                        RoofClipboardPasteProvenanceKind.Unknown,
                    finalPastePayload.AppendedEntityIds.Count,
                    finalPastePayload.IntelligentTimberIds.Count,
                    finalPastePayload.RoofOwnerIds.Count,
                    appendedIntelligentTimberCount,
                    clipboardDecision);
#endif
                if (clipboardDecision.Action ==
                    RoofClipboardPasteOwnershipAction.SkipWholeRoof)
                {
                    return;
                }

                if (clipboardDecision.Action == RoofClipboardPasteOwnershipAction.NoOp)
                {
                    return;
                }

                if (clipboardDecision.Action is
                    RoofClipboardPasteOwnershipAction.DegradeForeignIndividual or
                    RoofClipboardPasteOwnershipAction.DegradeForeignBatch or
                    RoofClipboardPasteOwnershipAction.DegradeUnknownBatch)
                {
                    using (_modifiedIds.Suppress())
                    using (_appendedTimberIds.Suppress())
                    using (_appendedRoofOwnerIds.Suppress())
                    using (_modifiedFramedLabelIds.Suppress())
                    using (_appendedLabelIds.Suppress())
                    using (_appendedSlopeArrowIds.Suppress())
                    using (_appendedSlopeAngleTextIds.Suppress())
                    using (_appendedPasteEntityIds.Suppress())
                    using (_erasedSourceHandles.Suppress())
                    {
                        var degradation = RoofForeignClipboardDegradationService.Process(
                            _document,
                            globalCommandName,
                            finalPastePayload);
                        if (!degradation.Success)
                        {
                            throw new InvalidOperationException(
                                "Foreign clipboard fail-closed cleanup failed: " +
                                degradation.DiagnosticResult);
                        }
                    }

                    return;
                }
            }
            else
            {
                clipboardDecision = RoofClipboardPasteOwnershipRules.Classify(
                    globalCommandName,
                    sameDwgClipboardPaste,
                    appendedIntelligentTimberCount,
                    appendedRoofOwnerIds.Count > 0);
            }
            // MIRROR Yes: AutoCAD modifies the selected Generated member IN PLACE (no
            // ObjectAppended clone, no ObjectErased source). Preserve the raw modified
            // timber ids BEFORE RoofLiveResizeService/roof-related filtering drops them,
            // so the MIRROR service can convert the same entity. Appended clones (MIRROR
            // No) are excluded here — they are handled by the clone branch and must not
            // be double-processed as in-place conversions.
            IReadOnlyCollection<ObjectId>? mirrorModifiedTimberIds = null;
            if (RoofGeneratedMemberEditCommandRules.IsMirrorCommand(globalCommandName))
            {
                var appendedSet = new HashSet<ObjectId>(appendedTimberIds);
                mirrorModifiedTimberIds = ids.Where(id => !appendedSet.Contains(id)).ToArray();
            }

            // Native mirrored/copied annotation clones observed during this command
            // (before any plugin RefreshTimberElements upsert). Whole-roof rebind must
            // consume these by command-lifecycle identity.
            var appendedAnnotationIds = appendedLabelIds
                .Concat(appendedSlopeArrowIds)
                .Concat(appendedSlopeAngleTextIds)
                .Distinct()
                .ToArray();
            var nativeCopy =
                LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(globalCommandName);
            var nativeMirror =
                RoofGeneratedMemberEditCommandRules.IsMirrorCommand(globalCommandName);

            // Consume the exact native full-roof clones before resize or per-member
            // ownership maintenance can reinterpret their inherited owner metadata.
            if (nativeCopy || nativeMirror)
            {
                var nativeRoofClones = _nativeRoofClones.GetCompleteClones();
                using (_modifiedIds.Suppress())
                using (_appendedTimberIds.Suppress())
                using (_appendedRoofOwnerIds.Suppress())
                using (_appendedPasteEntityIds.Suppress())
                using (_appendedLabelIds.Suppress())
                using (_appendedSlopeArrowIds.Suppress())
                using (_appendedSlopeAngleTextIds.Suppress())
                using (_erasedSourceHandles.Suppress())
                {
                    RoofWholeRoofCopyRebindService.Process(
                        _document,
                        globalCommandName,
                        appendedTimberIds,
                        appendedAnnotationIds,
                        nativeRoofClones);
                }
                // This set was handled atomically by whole-roof rebind. A rollback
                // must not be followed by independent resize/tamper writes for its
                // native clones; success must not regenerate it a second time.
                var nativeHandledIds = nativeRoofClones.SelectMany(clone => clone.Mapping.Values).ToHashSet();
                ids = ids.Where(id => !nativeHandledIds.Contains(id)).ToArray();
            }

            // Structural native events receive first semantic claim after full-roof
            // ownership, before ordinary clone handling and generic tamper recovery.
            using (_modifiedIds.Suppress())
            using (_appendedTimberIds.Suppress())
            using (_appendedRoofOwnerIds.Suppress())
            using (_appendedPasteEntityIds.Suppress())
            using (_appendedLabelIds.Suppress())
            using (_appendedSlopeArrowIds.Suppress())
            using (_appendedSlopeAngleTextIds.Suppress())
            using (_erasedSourceHandles.Suppress())
            {
                var structuralClaimedIds = RoofStructuralNativeEditService.Process(
                    _document, globalCommandName, ids, erasedSourceHandles, appendedTimberIds, appendedAnnotationIds);
                ids = ids.Where(id => !structuralClaimedIds.Contains(id)).ToArray();
                appendedTimberIds = appendedTimberIds.Where(id => !structuralClaimedIds.Contains(id)).ToArray();
                modifiedFramedLabelIds = modifiedFramedLabelIds.Where(id => !structuralClaimedIds.Contains(id)).ToArray();
                var claimedHandles = structuralClaimedIds.Select(id => id.Handle.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                erasedSourceHandles = erasedSourceHandles.Where(handle => !claimedHandles.Contains(handle)).ToArray();
            }

            // COPY/MIRROR clones temporarily inherit Generated/AttachedManual identity. Let
            // the existing semantic clone transaction consume them before generic
            // generated-tamper recovery can interpret that intermediate state.
            // Whole-roof rebind retains first priority. Only claimed member results
            // are removed from the subsequent generic resize/tamper candidates.
            if (nativeCopy || nativeMirror)
            {
                var handledNativeMemberIds = new HashSet<ObjectId>();
                using (_modifiedIds.Suppress())
                using (_appendedTimberIds.Suppress())
                using (_appendedRoofOwnerIds.Suppress())
                using (_appendedPasteEntityIds.Suppress())
                using (_appendedLabelIds.Suppress())
                using (_appendedSlopeArrowIds.Suppress())
                using (_appendedSlopeAngleTextIds.Suppress())
                using (_erasedSourceHandles.Suppress())
                {
                    ProcessNativeMemberClones(globalCommandName, appendedTimberIds, appendedPasteEntityIds,
                        erasedSourceHandles, mirrorModifiedTimberIds ?? Array.Empty<ObjectId>(), appendedAnnotationIds,
                        _nativeRoofClones, copy: nativeCopy, handledNativeMemberIds);
                }
                ids = ids.Where(id => !handledNativeMemberIds.Contains(id)).ToArray();
                appendedTimberIds = appendedTimberIds.Where(id => !handledNativeMemberIds.Contains(id)).ToArray();
                var handledSourceHandles = handledNativeMemberIds.Select(id => id.Handle.ToString())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                erasedSourceHandles = erasedSourceHandles.Where(handle => !handledSourceHandles.Contains(handle)).ToArray();
            }

            // Suppress ObjectModified while SOURCE resize rebuilds display / regenerates
            // rafters. Otherwise GRIP_STRETCH display rebuild events re-queue and a later
            // pass misclassifies them as independent display-only tamper.
            // Freeze native grip geometry snapshot before any plugin Rebuild can overwrite it.
            RoofGroupGripGeometrySnapshotService.FreezeAll();
            IReadOnlyCollection<ObjectId> roofRelatedIds;
            using (_modifiedIds.Suppress())
            using (_appendedTimberIds.Suppress())
            using (_appendedRoofOwnerIds.Suppress())
            using (_appendedPasteEntityIds.Suppress())
            using (_erasedSourceHandles.Suppress())
            {
                roofRelatedIds = RoofLiveResizeService.Process(
                    _document,
                    globalCommandName,
                    ids,
                    erasedSourceHandles,
                    appendedTimberIds);
            }
            if (roofRelatedIds.Count > 0)
            {
                ids = ids.Where(id => !roofRelatedIds.Contains(id)).ToArray();
                // LockedAnnotationTamper restores annotations from the assembly
                // snapshot. Drop those ids from framed-label persistence so
                // PersistFramedManualOffsets cannot re-bake the native MOVE.
                modifiedFramedLabelIds = modifiedFramedLabelIds
                    .Where(id => !roofRelatedIds.Contains(id))
                    .ToArray();
            }

            var hasLiveGeometryWork =
                ids.Count > 0 ||
                appendedTimberIds.Count > 0 ||
                modifiedFramedLabelIds.Count > 0 ||
                appendedLabelIds.Count > 0 ||
                appendedSlopeArrowIds.Count > 0 ||
                appendedSlopeAngleTextIds.Count > 0 ||
                appendedRoofOwnerIds.Count > 0 ||
                erasedSourceHandles.Count > 0 ||
                refreshAllTimberAnnotations;

            using (_modifiedIds.Suppress())
            using (_appendedTimberIds.Suppress())
            using (_appendedRoofOwnerIds.Suppress())
            using (_modifiedFramedLabelIds.Suppress())
            using (_appendedLabelIds.Suppress())
            using (_appendedSlopeArrowIds.Suppress())
            using (_appendedSlopeAngleTextIds.Suppress())
            using (_appendedPasteEntityIds.Suppress())
            using (_erasedSourceHandles.Suppress())
            {
                if (hasLiveGeometryWork)
                {
                    RefreshTimberElements(
                        _document,
                        globalCommandName,
                        ids,
                        appendedTimberIds,
                        modifiedFramedLabelIds,
                        appendedLabelIds,
                        appendedSlopeArrowIds,
                        appendedSlopeAngleTextIds,
                        erasedSourceHandles,
                        refreshAllTimberAnnotations,
                        preserveCopySources);
                }
#if DEBUG
                else
                {
                    AutoCadRedoDiagService.OnLiveGeometryRefreshSkippedEmpty(globalCommandName);
                }
#endif

                // Same-DWG COPY or proven same-DWG individual clipboard paste:
                // AutoCAD clones roof ownership metadata verbatim. Existing geometry
                // association rehydrates the appended member after timber copy init.
                // Never runs during U/UNDO/REDO/MREDO.
                // Whole-roof rebind already ran above (before LiveResize) for COPY/MIRROR.
                // NOTE: AttachedManual COPY-of-COPY clones are re-initialized BEFORE the
                // per-rafter rehydration service so a Generated→AttachedManual promotion
                // there is not re-classified as an already-manual clone.
                if (clipboardDecision.ShouldProcessIndividualTimber)
                {
                    RoofAttachedManualCopyCloneReinitializeService.Process(_document, globalCommandName,
                        appendedTimberIds, sameDwgClipboardPaste);
                    RoofGeneratedRafterCopyOwnershipRehydrationService.Process(_document, globalCommandName,
                        appendedTimberIds, sameDwgClipboardPaste);
                }
                if (nativeCopy || nativeMirror)
                {
                    RoofGeneratedCopyPreCommandSnapshotService.Clear();
                    _nativeRoofClones.Clear();
                    using (_document.LockDocument())
                    using (var transaction = _document.Database.TransactionManager.StartTransaction())
                    {
                        if (RoofUnlockIndicatorService.RebuildUnlockedOwners(
                                _document.Database,
                                transaction))
                        {
                            transaction.Commit();
                        }
                    }
                }
#if DEBUG
                TraceLiveGeometryTiming(
                    globalCommandName,
                    "command_completion_handler",
                    commandCompletionWatch.ElapsedMilliseconds,
                    $"hasWork={hasLiveGeometryWork} refreshAll={refreshAllTimberAnnotations} " +
                    $"modified={ids.Count} framedLabels={modifiedFramedLabelIds.Count}");

                // P4A DEBUG proof runs under the same reentrancy suppress scopes so
                // normalize writes do not re-queue LiveGeometry candidates.
                AutoCadFramedBlockContentStretchNormalizeLifecycleService.ProcessCommandEnded(
                    _document,
                    globalCommandName);
#endif
            }
        }

        private void ProcessNativeMemberClones(string? command, IReadOnlyCollection<ObjectId> appendedIds,
            IReadOnlyCollection<ObjectId> appendedEntities,
            IReadOnlyCollection<string> erasedHandles, IReadOnlyCollection<ObjectId> modifiedIds,
            IReadOnlyCollection<ObjectId> annotations, RoofNativeCloneSnapshot snapshot, bool copy,
            ISet<ObjectId>? handledIds = null)
        {
            var affectedOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ownedNativeAdded = new HashSet<ObjectId>();
            var ownedModifiedIds = new HashSet<ObjectId>();
            var unchangedCloneSources = new HashSet<ObjectId>();
            var claimedModifiedIds = new HashSet<ObjectId>();
            var ordinaryPlans = new Dictionary<ObjectId, (RoofGeneratedTimberData? Generated, RoofAttachedManualTimberData? Attached, bool Retain)>();
            var sourcesByClone = snapshot.GetMemberSourcesByClone();
            var changedKeys = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var nativeAdded = appendedIds.Concat(appendedEntities).Concat(sourcesByClone.Keys).Concat(annotations).Concat(snapshot.GetDerivedClones())
                .Where(id => !id.IsNull && !snapshot.IsPreExisting(id) && !snapshot.IsMemberCloneConsumed(id) &&
                    !RoofGeneratedCopyPreCommandSnapshotService.IsConsumedWholeRoofClone(id.Handle.ToString()))
                .Distinct().ToArray();
            using (_document.LockDocument())
            {
                using (var probe = _document.Database.TransactionManager.StartTransaction())
                {
                    foreach (var id in nativeAdded.Concat(modifiedIds))
                    {
                        if (id.IsNull || id.IsErased || probe.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
                        // Structural AttachedManual was claimed earlier; never route through
                        // ordinary native-member clone recovery (that path restores/erases
                        // Generated Hip/Valley snapshots and would destroy Manual children).
                        if (entity is Line && RoofStructuralAttachedManualStore.Read(entity).Data is not null)
                            continue;
                        if (entity is Solid3d &&
                            RoofPhysical3DGeneratedStore.Read(entity).Data is { Role: RoofPhysical3DGeneratedRole.StructuralRafterSolid } manualPhysical &&
                            RoofStructuralAttachedManualDataRules.IsManualPhysicalKey(manualPhysical.StructuralId))
                            continue;
                        var owner = RoofGeneratedTimberStore.Read(entity).Data?.RoofOwnerReference ??
                            RoofAttachedManualTimberStore.Read(entity).Data?.RoofOwnerReference ??
                            RoofPhysical3DGeneratedStore.Read(entity).Data?.RoofOwnerReference ??
                            RoofStructuralGeneratedStore.Read(entity).Data?.RoofOwnerReference;
                        if (owner is null || !TryOpenNativeMemberOwner(probe, owner, snapshot, out _)) continue;
                        if (modifiedIds.Contains(id)) ownedModifiedIds.Add(id);
                        if (nativeAdded.Contains(id))
                        {
                            affectedOwners.Add(owner);
                            ownedNativeAdded.Add(id);
                        }
                        if (entity is Line)
                        {
                            var generated = RoofGeneratedTimberStore.Read(entity).Data;
                            var attached = RoofAttachedManualTimberStore.Read(entity).Data;
                            if (generated?.MemberKind == RoofGeneratedTimberKind.Rafter ||
                                attached?.AnchorGeneratedMemberKey?.MemberKind == RoofGeneratedTimberKind.Rafter)
                            {
                                var retain = snapshot.IsPreExisting(id);
                                if (sourcesByClone.TryGetValue(id, out var sourceId) && snapshot.Members.TryGetValue(sourceId, out var sourceMember))
                                {
                                    generated = sourceMember.Generated;
                                    attached = sourceMember.Attached;
                                    retain = !copy && sourceId.IsErased;
                                }
                                ordinaryPlans[id] = (generated, attached, retain);
                            }
                        }
                        if (entity is Line line && snapshot.Members.TryGetValue(id, out var before))
                        {
                            var after = new RoofGeneratedMemberGeometry(new RoofPoint3D(line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z),
                                new RoofPoint3D(line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z));
                            if (!RoofGeneratedMemberOverrideMath.GeometryEquals(before.Binding.Geometry, after))
                            {
                                affectedOwners.Add(owner);
                                if (ordinaryPlans.ContainsKey(id)) claimedModifiedIds.Add(id);
                                if (!changedKeys.TryGetValue(owner, out var keys)) changedKeys[owner] = keys = new(StringComparer.Ordinal);
                                if (RoofOrdinaryRafterSolidMaterializationService.TryGetPlanPhysicalIdentity(line, owner, out var key)) keys.Add(key);
                            }
                        }
                        else if (!copy && entity is Solid3d && snapshot.IsPreExisting(id) &&
                                 RoofPhysical3DGeneratedStore.Read(entity).Data is { Role: RoofPhysical3DGeneratedRole.OrdinaryRafterSolid } physical)
                        {
                            affectedOwners.Add(owner);
                            claimedModifiedIds.Add(id);
                            if (!changedKeys.TryGetValue(owner, out var keys)) changedKeys[owner] = keys = new(StringComparer.Ordinal);
                            keys.Add(physical.StructuralId);
                        }
                    }
                    foreach (var pair in sourcesByClone)
                    {
                        if (ownedNativeAdded.Contains(pair.Key) &&
                            snapshot.Members.TryGetValue(pair.Value, out var source) &&
                            !pair.Value.IsErased && probe.GetObject(pair.Value, OpenMode.ForRead) is Line sourceLine &&
                            RoofGeneratedMemberOverrideMath.GeometryEquals(source.Binding.Geometry,
                                new RoofGeneratedMemberGeometry(
                                    new RoofPoint3D(sourceLine.StartPoint.X, sourceLine.StartPoint.Y, sourceLine.StartPoint.Z),
                                    new RoofPoint3D(sourceLine.EndPoint.X, sourceLine.EndPoint.Y, sourceLine.EndPoint.Z))))
                            unchangedCloneSources.Add(pair.Value);
                        if (ownedNativeAdded.Contains(pair.Key) && pair.Value.IsErased &&
                            snapshot.Members.TryGetValue(pair.Value, out var before) &&
                            !RoofGeneratedCopyPreCommandSnapshotService.IsConsumedWholeRoofClone(pair.Key.Handle.ToString()))
                        {
                            var reference = before.Generated?.RoofOwnerReference ?? before.Attached?.RoofOwnerReference;
                            if (reference is null || !TryOpenNativeMemberOwner(probe, reference, snapshot, out _)) continue;
                            affectedOwners.Add(reference);
                            claimedModifiedIds.Add(pair.Value);
                            if (!changedKeys.TryGetValue(reference, out var keys)) changedKeys[reference] = keys = new(StringComparer.Ordinal);
                            if (before.Generated is { } generated)
                                keys.Add(RoofPhysicalStretchRules.PhysicalMemberId(RoofGeneratedMemberKey.From(generated)));
                            else if (before.Attached is { } attached)
                                keys.Add(RoofAttachedManualIdentityRules.PhysicalKey(attached));
                        }
                    }
                    // MIRROR Yes can erase a selected derived source body as well.
                    // Its semantic replacement key is already driven by Plan2D above.
                    foreach (var id in snapshot.PreExistingPhysical.Where(id => id.IsErased))
                        if (AutoCadObjectIdAccess.TryGetObjectAllowErased<Entity>(probe, id, OpenMode.ForRead,
                                out var entity, _document.Database) && entity is not null &&
                            RoofPhysical3DGeneratedStore.Read(entity).Data is { Role: RoofPhysical3DGeneratedRole.OrdinaryRafterSolid } physical &&
                            changedKeys.TryGetValue(physical.RoofOwnerReference, out var keys) && keys.Contains(physical.StructuralId))
                            claimedModifiedIds.Add(id);
                }
                if (affectedOwners.Count == 0) return;
                try
                {
                    // Nested service commits remain part of this outer AutoCAD transaction.
                    using var transaction = _document.Database.TransactionManager.StartTransaction();
                    // Restore structural geometry before clone presentation can perform
                    // drawing-wide item numbering; those numbers are not semantic keys.
                    foreach (var reference in affectedOwners)
                        if (TryOpenNativeMemberOwner(transaction, reference, snapshot, out var structuralOwner) &&
                            structuralOwner is not null &&
                            !RoofUnsupportedStretchRecoveryService.TryRestoreStructuralHipValleyMembersOnly(
                                _document.Database, transaction, structuralOwner.ObjectId, _document.Editor))
                            throw new InvalidOperationException("Native member structural snapshot restoration failed.");
                    if (copy)
                    {
                        var sourceHandles = snapshot.Members.Values.Where(member =>
                                affectedOwners.Contains(member.Generated?.RoofOwnerReference ?? member.Attached?.RoofOwnerReference ?? string.Empty))
                            .Select(member => member.Binding.Id.Handle.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var copiedAnnotations = annotations.Where(id => !id.IsNull && !id.IsErased &&
                            transaction.GetObject(id, OpenMode.ForRead) is Entity annotation &&
                            RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(annotation, out var sourceHandle) &&
                            sourceHandles.Contains(sourceHandle)).ToArray();
                        EraseAppendedAnnotationCopies(transaction, copiedAnnotations, Array.Empty<ObjectId>(), Array.Empty<ObjectId>());
                        var copyCandidates = ownedNativeAdded.ToArray();
                        RoofAttachedManualCopyCloneReinitializeService.Process(_document, command, copyCandidates, propagateFailure: true);
                        RoofGeneratedRafterCopyOwnershipRehydrationService.Process(_document, command, copyCandidates, propagateFailure: true);
                        // Reuse the existing copy-preserving numbering/annotation refresh
                        // inside this transaction, including AttachedManual COPY-of-COPY.
                        RefreshTimberElements(_document, command, Array.Empty<ObjectId>(), ordinaryPlans.Keys.ToArray(),
                            Array.Empty<ObjectId>(), Array.Empty<ObjectId>(), Array.Empty<ObjectId>(), Array.Empty<ObjectId>(),
                            Array.Empty<string>(), refreshAllTimberAnnotations: false, preserveCopySources: true, propagateFailure: true);
                        ownedNativeAdded.UnionWith(copiedAnnotations);
                    }
                    else
                        RoofMirrorCloneDetachService.Process(_document, command, ownedNativeAdded.ToArray(),
                            erasedHandles.Where(handle => claimedModifiedIds.Any(id =>
                                string.Equals(id.Handle.ToString(), handle, StringComparison.OrdinalIgnoreCase))).ToArray(),
                            ownedModifiedIds.ToArray(),
                            annotations, snapshot, propagateFailure: true);
                    TimberAnnotationService.DeleteForMissingSourceHandles(_document.Database, transaction,
                        claimedModifiedIds.Where(id => id.IsErased).Select(id => id.Handle.ToString()).ToArray());
                    foreach (var pair in ordinaryPlans)
                    {
                        if (pair.Key.IsErased || transaction.GetObject(pair.Key, OpenMode.ForRead) is not Line line)
                            throw new InvalidOperationException("Native ordinary Plan2D result is missing.");
                        var resultGenerated = RoofGeneratedTimberStore.Read(line).Data;
                        var resultAttached = RoofAttachedManualTimberStore.Read(line).Data;
                        var expected = pair.Value;
                        if (expected.Retain && expected.Generated is { } originalGenerated)
                        {
                            if (resultGenerated is null || resultAttached is not null ||
                                RoofGeneratedMemberKey.From(resultGenerated) != RoofGeneratedMemberKey.From(originalGenerated))
                                throw new InvalidOperationException("Native replacement lost its Generated logical key.");
                        }
                        else if (resultGenerated is not null || resultAttached is null ||
                                 resultAttached.AnchorGeneratedMemberKey?.MemberKind != RoofGeneratedTimberKind.Rafter ||
                                 (expected.Retain && expected.Attached is { } originalAttached &&
                                  RoofAttachedManualIdentityRules.Resolve(resultAttached) != RoofAttachedManualIdentityRules.Resolve(originalAttached)))
                            throw new InvalidOperationException("Native ordinary result has no canonical AttachedManual semantic identity.");
                    }
                    foreach (var id in unchangedCloneSources)
                    {
                        var before = snapshot.Members[id];
                        if (id.IsErased || transaction.GetObject(id, OpenMode.ForRead) is not Line source ||
                            !RoofGeneratedMemberOverrideMath.GeometryEquals(before.Binding.Geometry,
                                new RoofGeneratedMemberGeometry(
                                    new RoofPoint3D(source.StartPoint.X, source.StartPoint.Y, source.StartPoint.Z),
                                    new RoofPoint3D(source.EndPoint.X, source.EndPoint.Y, source.EndPoint.Z))) ||
                            (before.Generated is { } original &&
                             (RoofGeneratedTimberStore.Read(source).Data is not { } liveGenerated ||
                              RoofGeneratedMemberKey.From(liveGenerated) != RoofGeneratedMemberKey.From(original) ||
                              (copy && liveGenerated != original))) ||
                            (before.Attached is { } originalAttached &&
                             RoofAttachedManualIdentityRules.Resolve(RoofAttachedManualTimberStore.Read(source).Data!) !=
                             RoofAttachedManualIdentityRules.Resolve(originalAttached)))
                            throw new InvalidOperationException("Native clone changed its retained semantic source.");
                    }
                    foreach (var reference in affectedOwners)
                    {
                        if (!TryOpenNativeMemberOwner(transaction, reference, snapshot, out var owner) || owner is null) continue;
                        if (modifiedIds.Contains(owner.ObjectId)) claimedModifiedIds.Add(owner.ObjectId);
                        foreach (var id in modifiedIds.Where(id => !id.IsNull && !id.IsErased))
                            if (transaction.GetObject(id, OpenMode.ForRead) is Entity structuralEntity &&
                                RoofStructuralGeneratedStore.Read(structuralEntity).Data is { } structural &&
                                string.Equals(structural.RoofOwnerReference, reference, StringComparison.OrdinalIgnoreCase) &&
                                RoofStructuralGeneratedLockRules.IsLockProtectedRole(structural.StructuralRole))
                                claimedModifiedIds.Add(id); // Restored before clone numbering above.
                        var input = RoofPolylineExtractor.Extract(owner);
                        var footprint = RoofFootprintValidator.Validate(input);
                        var definition = RoofDefinitionStore.Read(owner).Data;
                        var geometry = footprint.Footprint is null || definition is null ? null :
                            RoofDefinitionPersistence.Restore(input, footprint.Footprint, definition).Geometry;
                        if (geometry is null) throw new InvalidOperationException("Native member roof geometry is unavailable.");
                        var collateral = RoofPhysical3DGeneratedStore.FindByOwner(_document.Database, transaction, reference)
                            .Where(id => !snapshot.IsPreExisting(id)).ToArray();
                        if (!RoofOrdinaryRafterSolidMaterializationService.TryReconcileSemanticMembersInTransaction(
                                _document.Database, transaction, owner, geometry,
                                changedKeys.GetValueOrDefault(reference)?.ToArray() ?? Array.Empty<string>(), collateral))
                            throw new InvalidOperationException("Native member physical reconciliation failed.");
                        var disposable = snapshot.GetDerivedClones().Concat(collateral).Concat(nativeAdded).Distinct().Where(id =>
                        {
                            if (id.IsErased ||
                                RoofGeneratedCopyPreCommandSnapshotService.IsConsumedWholeRoofClone(id.Handle.ToString()) ||
                                transaction.GetObject(id, OpenMode.ForRead) is not Entity entity)
                                return false;
                            // Structural AttachedManual Plans/solids are claimed earlier and
                            // must not be erased as ordinary native-member disposable clones.
                            if (entity is Line && RoofStructuralAttachedManualStore.Read(entity).Data is not null)
                                return false;
                            var physical = RoofPhysical3DGeneratedStore.Read(entity).Data;
                            if (physical is not null &&
                                physical.Role == RoofPhysical3DGeneratedRole.StructuralRafterSolid &&
                                RoofStructuralAttachedManualDataRules.IsManualPhysicalKey(physical.StructuralId))
                                return false;
                            var ownerReference = physical?.RoofOwnerReference ??
                                RoofStructuralGeneratedStore.Read(entity).Data?.RoofOwnerReference ??
                                RoofDisplayStore.Read(entity).OwnerReference;
                            return ownerReference == reference;
                        }).ToArray();
                        _ = RoofAssemblyGroupSyncService.DetachMembersBeforeErase(_document.Database, transaction, owner.ObjectId, disposable);
                        foreach (var id in disposable) ((Entity)transaction.GetObject(id, OpenMode.ForWrite)).Erase();
                        var otherPhysical = modifiedIds.Where(id => !id.IsNull && !id.IsErased &&
                            snapshot.IsPreExisting(id) && transaction.GetObject(id, OpenMode.ForRead) is Entity entity &&
                            RoofPhysical3DGeneratedStore.Read(entity).Data is { } physical &&
                            physical.Role != RoofPhysical3DGeneratedRole.OrdinaryRafterSolid &&
                            string.Equals(physical.RoofOwnerReference, reference, StringComparison.OrdinalIgnoreCase)).ToArray();
                        if (!RoofPhysical3DLifecycleService.TryRestoreStretchPhysicalInTransaction(_document, transaction, owner, otherPhysical) ||
                            !RoofAssemblyGroupSyncService.TrySyncForOwner(_document, transaction, owner.ObjectId) ||
                            !RoofLiveResizeService.TryVerifyStretchPhysicalState(_document.Database, transaction, owner.ObjectId))
                            throw new InvalidOperationException("Native member physical/GROUP canonicality failed.");
                        claimedModifiedIds.UnionWith(otherPhysical);
                    }
                    transaction.Commit();
                    var summary = $"ROOF_MEMBER_CLONE_ACCEPT command={command}" +
                        $" classification={(ordinaryPlans.Count > 0 ? "semantic-members" : "derived-recovery")}" +
                        $" plans={ordinaryPlans.Count} owners={affectedOwners.Count}" +
                        $" claimedNew={ownedNativeAdded.Count} claimedModified={claimedModifiedIds.Count} result=ok";
                    _document.Editor.WriteMessage("\n" + summary);
                    AcKrovyDiagnostics.Info("ROOF_MEMBER_CLONE_ACCEPT", summary);
                }
                catch (System.Exception ex)
                {
                    // The failed outer transaction is disposed before this fresh recovery.
                    RecoverNativeMemberFailure(snapshot, nativeAdded, ownedNativeAdded, affectedOwners, command, ex);
                }
                snapshot.ConsumeMemberClones(ownedNativeAdded);
                handledIds?.UnionWith(ownedNativeAdded);
                handledIds?.UnionWith(unchangedCloneSources);
                handledIds?.UnionWith(claimedModifiedIds);
            }
        }

        private bool TryOpenNativeMemberOwner(Transaction transaction, string reference,
            RoofNativeCloneSnapshot snapshot, out Polyline? owner)
        {
            owner = null;
            if (!long.TryParse(reference, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var handle)) return false;
            ObjectId id;
            try { id = _document.Database.GetObjectId(false, new Handle(handle), 0); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return false; }
            return AutoCadObjectIdAccess.TryGetObject<Polyline>(transaction, id, OpenMode.ForRead,
                out owner, _document.Database) && owner is not null && !snapshot.IsSourceChanged(owner);
        }

        private void RecoverNativeMemberFailure(RoofNativeCloneSnapshot snapshot, IReadOnlyCollection<ObjectId> nativeAdded,
            IReadOnlyCollection<ObjectId> ownedNativeAdded,
            IReadOnlyCollection<string> owners, string? command, System.Exception failure)
        {
            using var transaction = _document.Database.TransactionManager.StartTransaction();
            var ownedSourceHandles = snapshot.Members.Values.Where(member =>
                    owners.Contains(member.Generated?.RoofOwnerReference ?? member.Attached?.RoofOwnerReference ?? string.Empty))
                .Select(member => member.Binding.Id.Handle.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in nativeAdded)
            {
                if (id.IsNull || id.IsErased || transaction.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
                if (entity is Line && RoofStructuralAttachedManualStore.Read(entity).Data is not null)
                    continue;
                if (entity is Solid3d &&
                    RoofPhysical3DGeneratedStore.Read(entity).Data is { Role: RoofPhysical3DGeneratedRole.StructuralRafterSolid } manualPhysical &&
                    RoofStructuralAttachedManualDataRules.IsManualPhysicalKey(manualPhysical.StructuralId))
                    continue;
                var reference = RoofGeneratedTimberStore.Read(entity).Data?.RoofOwnerReference ??
                    RoofAttachedManualTimberStore.Read(entity).Data?.RoofOwnerReference ??
                    RoofPhysical3DGeneratedStore.Read(entity).Data?.RoofOwnerReference ??
                    RoofStructuralGeneratedStore.Read(entity).Data?.RoofOwnerReference ?? RoofDisplayStore.Read(entity).OwnerReference;
                if (ownedNativeAdded.Contains(id) || (reference is not null && owners.Contains(reference)) ||
                    (RoofOwnedAnnotationSourceResolver.TryResolveSourceHandle(entity, out var source) && ownedSourceHandles.Contains(source)))
                {
                    entity.UpgradeOpen();
                    entity.Erase();
                }
            }
            foreach (var member in snapshot.Members.Values)
            {
                var reference = member.Generated?.RoofOwnerReference ?? member.Attached?.RoofOwnerReference;
                if (reference is null || !owners.Contains(reference) ||
                    !AutoCadObjectIdAccess.TryGetObjectAllowErased<Line>(transaction, member.Binding.Id,
                        OpenMode.ForWrite, out var line, _document.Database) || line is null) continue;
                if (line.IsErased) line.Erase(false);
                line.StartPoint = new Point3d(member.Binding.Geometry.Start.X, member.Binding.Geometry.Start.Y, member.Binding.Geometry.Start.Z);
                line.EndPoint = new Point3d(member.Binding.Geometry.End.X, member.Binding.Geometry.End.Y, member.Binding.Geometry.End.Z);
                if (member.Generated is not null && member.Timber is not null)
                    RoofGeneratedTimberStore.WriteAtomic(line, transaction, member.Timber,
                        RoofGeneratedTimberStore.BuildSection(line, transaction, member.Generated));
                if (member.Attached is not null) RoofAttachedManualLifecycleService.WriteAnchored(line, transaction, member.Attached);
            }
            foreach (var reference in owners)
            {
                if (!TryOpenNativeMemberOwner(transaction, reference, snapshot, out var owner) || owner is null) continue;
                if (RoofUnsupportedStretchRecoveryService.TryRecoverGeneratedMembersOnly(_document.Database, transaction,
                        owner.ObjectId, _document.Editor) != RoofUnsupportedStretchRecoveryOutcome.Recovered)
                    throw new InvalidOperationException("Native member snapshot recovery failed.", failure);
                var input = RoofPolylineExtractor.Extract(owner);
                var footprint = RoofFootprintValidator.Validate(input);
                var definition = RoofDefinitionStore.Read(owner).Data;
                var geometry = footprint.Footprint is null || definition is null ? null :
                    RoofDefinitionPersistence.Restore(input, footprint.Footprint, definition).Geometry;
                var keys = snapshot.Members.Values.Where(member =>
                        (member.Generated?.RoofOwnerReference ?? member.Attached?.RoofOwnerReference) == reference)
                    .Select(member => member.Generated is { } generated ? RoofPhysicalStretchRules.PhysicalMemberId(
                        RoofGeneratedMemberKey.From(generated)) : RoofAttachedManualIdentityRules.PhysicalKey(member.Attached!)).ToArray();
                if (geometry is null || !RoofOrdinaryRafterSolidMaterializationService.TryReconcileSemanticMembersInTransaction(
                        _document.Database, transaction, owner, geometry, keys, Array.Empty<ObjectId>()) ||
                    !RoofPhysical3DLifecycleService.TryRestoreStretchPhysicalInTransaction(
                        _document, transaction, owner, snapshot.PreExistingPhysical.Where(id => !id.IsErased &&
                            RoofPhysical3DGeneratedStore.Read((Entity)transaction.GetObject(id, OpenMode.ForRead)).Data is { } data &&
                            data.Role != RoofPhysical3DGeneratedRole.OrdinaryRafterSolid &&
                            string.Equals(data.RoofOwnerReference, reference, StringComparison.OrdinalIgnoreCase)).ToArray()) ||
                    !RoofAssemblyGroupSyncService.TrySyncForOwner(_document, transaction, owner.ObjectId) ||
                    !RoofLiveResizeService.TryVerifyStretchPhysicalState(_document.Database, transaction, owner.ObjectId))
                    throw new InvalidOperationException("Native member physical rollback failed.", failure);
            }
            transaction.Commit();
            AcKrovyDiagnostics.Info("ROOF_MEMBER_ROLLBACK", $"command={command} action=rollback result=restored error={failure.GetType().Name}");
        }

        private static void RefreshTimberElements(
            Document document,
            string? globalCommandName,
            IReadOnlyList<ObjectId> ids,
            IReadOnlyList<ObjectId> appendedTimberIds,
            IReadOnlyCollection<ObjectId> modifiedFramedLabelIds,
            IReadOnlyCollection<ObjectId> appendedLabelIds,
            IReadOnlyCollection<ObjectId> appendedSlopeArrowIds,
            IReadOnlyCollection<ObjectId> appendedSlopeAngleTextIds,
            IReadOnlyCollection<string> erasedSourceHandles,
            bool refreshAllTimberAnnotations,
            bool preserveCopySources,
            bool propagateFailure = false)
        {
            // Final guard: Undo/Redo must never LockDocument / StartTransaction.
            if (LiveGeometryCommandRules.IsUndoRedoCommand(globalCommandName))
            {
#if DEBUG
                AutoCadRedoDiagService.OnLiveGeometryRefreshSkippedUndoRedo(globalCommandName);
#endif
                return;
            }

            var editor = document.Editor;

            try
            {
#if DEBUG
                AutoCadRedoDiagService.OnLiveGeometryRefreshBegin(
                    globalCommandName,
                    ids.Count,
                    erasedSourceHandles.Count,
                    modifiedFramedLabelIds.Count);
                var committed = false;
                var totalWatch = System.Diagnostics.Stopwatch.StartNew();
#else
                _ = globalCommandName;
#endif
                using (document.LockDocument())
                using (var transaction = document.Database.TransactionManager.StartTransaction())
                {
#if DEBUG
                    var classifyWatch = System.Diagnostics.Stopwatch.StartNew();
#endif
                    var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
                    var modifiedTimberIds = FilterTimberElementIds(
                        document.Database,
                        transaction,
                        metadataStore,
                        ids);
                    var annotationPresentationCount = CountOwnedAnnotationPresentationIds(
                        document.Database,
                        transaction,
                        ids,
                        modifiedFramedLabelIds,
                        appendedLabelIds,
                        appendedSlopeArrowIds,
                        appendedSlopeAngleTextIds);
                    var modificationKind = LiveGeometryModificationClassifier.Classify(
                        modifiedTimberSourceCount: modifiedTimberIds.Count,
                        modifiedAnnotationPresentationCount: annotationPresentationCount,
                        appendedTimberCount: appendedTimberIds.Count,
                        erasedSourceHandleCount: erasedSourceHandles.Count,
                        requiresFullTimberAnnotationRefresh: refreshAllTimberAnnotations);
#if DEBUG
                    TraceLiveGeometryTiming(
                        globalCommandName,
                        "modified_object_classification",
                        classifyWatch.ElapsedMilliseconds,
                        $"kind={modificationKind} timber={modifiedTimberIds.Count} " +
                        $"annotationPresentation={annotationPresentationCount} " +
                        $"refreshAllFlag={refreshAllTimberAnnotations}");
#endif

                    if (LiveGeometryModificationClassifier.ShouldPreserveAnnotationPresentationOnly(
                            modificationKind) &&
                        !preserveCopySources)
                    {
                        // Classic annotation-only MOVE/ROTATE: keep the native
                        // presentation edit. Do not mark the source dirty, do not
                        // EnsureForElement, and do not run whole-drawing scans.
#if DEBUG
                        var presentationWatch = System.Diagnostics.Stopwatch.StartNew();
#endif
                        ElementLabelService.PersistFramedManualOffsets(
                            document.Database,
                            transaction,
                            modifiedFramedLabelIds);
#if DEBUG
                        TraceLiveGeometryTiming(
                            globalCommandName,
                            "annotation_presentation_only",
                            presentationWatch.ElapsedMilliseconds,
                            "PersistFramedManualOffsets only; skipped EnsureForElement/" +
                            "FindAllTimberElements/SynchronizeElementIds/duplicate+orphan scans");
#endif
                        transaction.Commit();
#if DEBUG
                        committed = true;
                        TraceLiveGeometryTiming(
                            globalCommandName,
                            "transaction_commit_presentation_only",
                            totalWatch.ElapsedMilliseconds,
                            "ok");
#endif
                    }
                    else
                    {
                        if (preserveCopySources)
                        {
                            EraseAppendedAnnotationCopies(
                                transaction,
                                appendedLabelIds,
                                appendedSlopeArrowIds,
                                appendedSlopeAngleTextIds);
                        }
                        else
                        {
                            ElementLabelService.PersistFramedManualOffsets(
                                document.Database,
                                transaction,
                                modifiedFramedLabelIds);
                        }

                        TimberAnnotationService.DeleteForMissingSourceHandles(
                            document.Database,
                            transaction,
                            erasedSourceHandles);

                        foreach (var erasedOwnerHandle in erasedSourceHandles)
                        {
                            RoofPhysical3DLifecycleService.CleanupStillErasedSourceInTransaction(
                                document.Database,
                                transaction,
                                erasedOwnerHandle);
                        }

                        var defaultProfile = TimberElementDefaultProfileStore.Load();
                        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
                        var presentationBatchContext =
                            AutoCadAnnotationPresentationBatchContext.Create(
                            document.Database,
                            transaction,
                            defaultProfile);
#if DEBUG
                        var scanWatch = System.Diagnostics.Stopwatch.StartNew();
#endif
                        // Prefer ObjectModified timber sources over historical
                        // ROTATE FindAll. 1 rotated source → 1 EnsureForElement.
                        var candidateIds = LiveGeometryCommandRules.SelectSourceRefreshCandidates(
                            preserveCopySources,
                            refreshAllTimberAnnotations,
                            ids,
                            appendedTimberIds,
                            modifiedTimberIds,
                            () => DrawingScanner.FindAllTimberElements(
                                document.Database,
                                transaction,
                                metadataStore));
#if DEBUG
                        TraceLiveGeometryTiming(
                            globalCommandName,
                            "timber_candidate_resolution",
                            scanWatch.ElapsedMilliseconds,
                            $"refreshAllFlag={refreshAllTimberAnnotations} " +
                            $"modifiedTimber={modifiedTimberIds.Count} " +
                            $"candidates={candidateIds.Count} " +
                            $"usedFindAllFallback=" +
                            $"{refreshAllTimberAnnotations && !preserveCopySources && modifiedTimberIds.Count == 0}");
#endif
                        // COPY/PASTE init only — pure ROTATE/MOVE of existing
                        // timber does not need ModelSpace handle re-scan here.
                        if (preserveCopySources || appendedTimberIds.Count > 0)
                        {
                            TimberElementCopyInitializationService.InitializeLocalCopies(
                                document.Database,
                                transaction,
                                metadataStore,
                                candidateIds,
                                defaultProfile);
                        }

                        var previousElementIdById = ReadElementIds(
                            document.Database,
                            transaction,
                            metadataStore,
                            candidateIds);
                        var timberIds = FilterTimberElementIds(
                            document.Database,
                            transaction,
                            metadataStore,
                            candidateIds);
                        if (timberIds.Count > 0)
                        {
#if DEBUG
                            var syncWatch = System.Diagnostics.Stopwatch.StartNew();
#endif
                            var synchronizedDataById =
                                TimberElementItemIdentityService.SynchronizeElementIds(
                                    document.Database,
                                    transaction,
                                    metadataStore,
                                    timberIds,
                                    roundingStepMm);
#if DEBUG
                            TraceLiveGeometryTiming(
                                globalCommandName,
                                "SynchronizeElementIds",
                                syncWatch.ElapsedMilliseconds,
                                $"ensureTargets={timberIds.Count} " +
                                $"drawingTimberMeasured={synchronizedDataById.Count}");
                            var ensureWatch = System.Diagnostics.Stopwatch.StartNew();
                            var ensureCalls = 0;
#endif

                            foreach (var id in timberIds)
                            {
                                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                                        transaction,
                                        id,
                                        OpenMode.ForRead,
                                        out var entity,
                                        document.Database) ||
                                    entity is null ||
                                    !synchronizedDataById.TryGetValue(id, out var data))
                                {
                                    continue;
                                }

                                previousElementIdById.TryGetValue(id, out var previousElementId);
                                TimberAnnotationService.EnsureForElement(
                                    document.Database,
                                    transaction,
                                    entity,
                                    data,
                                    presentationBatchContext,
                                    previousElementId,
                                    roundingStepMm,
                                    copySourcePreservation: preserveCopySources);
#if DEBUG
                                ensureCalls++;
#endif
                            }
#if DEBUG
                            TraceLiveGeometryTiming(
                                globalCommandName,
                                "EnsureForElement_batch",
                                ensureWatch.ElapsedMilliseconds,
                                $"calls={ensureCalls} modifiedTimber={modifiedTimberIds.Count}");
#endif
                        }

                        if (!preserveCopySources)
                        {
#if DEBUG
                            var cleanupWatch = System.Diagnostics.Stopwatch.StartNew();
#endif
                            TimberAnnotationService.DeleteInsertedWithoutCurrentSourceHandles(
                                document.Database,
                                transaction,
                                appendedLabelIds,
                                appendedSlopeArrowIds,
                                appendedSlopeAngleTextIds);
                            TimberAnnotationService.DeleteDuplicatesForExistingSourceHandles(
                                document.Database,
                                transaction);
#if DEBUG
                            TraceLiveGeometryTiming(
                                globalCommandName,
                                "duplicate_orphan_cleanup",
                                cleanupWatch.ElapsedMilliseconds,
                                "DeleteInserted+DeleteDuplicates");
#endif
                        }

                        transaction.Commit();
#if DEBUG
                        committed = true;
                        TraceLiveGeometryTiming(
                            globalCommandName,
                            "transaction_commit_source_refresh",
                            totalWatch.ElapsedMilliseconds,
                            $"kind={modificationKind}");
#endif
                    }
                }
#if DEBUG
                AutoCadRedoDiagService.OnLiveGeometryRefreshEnd(globalCommandName, committed);
#endif
            }
            catch (System.Exception ex)
            {
#if DEBUG
                AutoCadRedoDiagService.OnException(
                    "LiveGeometrySynchronizationService.RefreshTimberElements",
                    ex);
                AutoCadRedoDiagService.OnLiveGeometryRefreshEnd(
                    globalCommandName,
                    committed: false);
#endif
                editor.WriteMessage(UiStrings.Format(UiStrings.WarningLiveRefreshSkippedFormat, ex.Message));
                if (propagateFailure) throw;
            }
        }

#if DEBUG
        private static void TraceClipboardClassification(
            Document document,
            string? globalCommandName,
            RoofClipboardPasteProvenanceKind provenanceKind,
            int pastedCount,
            int intelligentTimberCount,
            int appendedRoofOwnerCount,
            int objectAppendedObservationCount,
            RoofClipboardPasteOwnershipDecision decision)
        {
            if (!LiveGeometryCommandRules.IsClipboardPasteCommand(globalCommandName))
            {
                return;
            }

            var message =
                "ROOF_CLIPBOARD_CLASSIFY " +
                $"command={LiveGeometryCommandRules.NormalizeCommandName(globalCommandName)} " +
                $"provenance={FormatProvenance(provenanceKind)} " +
                $"pastedCount={pastedCount} " +
                $"intelligentTimberCount={intelligentTimberCount} " +
                $"objectAppendedObservationCount={objectAppendedObservationCount} " +
                $"roofSourceCount={appendedRoofOwnerCount} " +
                $"wholeRoofDetected={(appendedRoofOwnerCount > 0 ? "true" : "false")} " +
                $"eligibleIndividualTimber={(decision.ShouldProcessIndividualTimber ? "true" : "false")} " +
                $"action={decision.Action} " +
                $"result={decision.DiagnosticResult}";
            document.Editor.WriteMessage("\n" + message);
            Diagnostics.AcKrovyDiagnostics.Info(
                "RoofClipboardClassification",
                message,
                LiveGeometryCommandRules.NormalizeCommandName(globalCommandName));
        }

        private static string FormatProvenance(
            RoofClipboardPasteProvenanceKind provenanceKind) => provenanceKind switch
        {
            RoofClipboardPasteProvenanceKind.KnownSameDocument => "same-dwg",
            RoofClipboardPasteProvenanceKind.KnownForeignDocument => "foreign",
            _ => "unknown",
        };

        private static void TraceLiveGeometryTiming(
            string? globalCommandName,
            string stage,
            long elapsedMilliseconds,
            string detail)
        {
            Diagnostics.AcKrovyDiagnostics.Info(
                "LiveGeometryTiming",
                $"stage={stage}; elapsedMs={elapsedMilliseconds}; {detail}",
                LiveGeometryCommandRules.NormalizeCommandName(globalCommandName));
        }
#endif

        private static int CountAppendedIntelligentRoofTimbers(
            Document document,
            IReadOnlyList<ObjectId> appendedTimberIds)
        {
            if (appendedTimberIds.Count == 0)
            {
                return 0;
            }

            using var transaction = document.Database.TransactionManager.StartTransaction();
            var count = 0;
            foreach (var id in appendedTimberIds.Distinct())
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        document.Database) ||
                    entity is null)
                {
                    continue;
                }

                if (RoofGeneratedTimberStore.Read(entity).Data is not null ||
                    RoofAttachedManualTimberStore.Read(entity).Data is not null)
                {
                    count++;
                }
            }

            transaction.Commit();
            return count;
        }

        private static int CountOwnedAnnotationPresentationIds(
            Database database,
            Transaction transaction,
            IReadOnlyList<ObjectId> modifiedIds,
            IReadOnlyCollection<ObjectId> modifiedFramedLabelIds,
            IReadOnlyCollection<ObjectId> appendedLabelIds,
            IReadOnlyCollection<ObjectId> appendedSlopeArrowIds,
            IReadOnlyCollection<ObjectId> appendedSlopeAngleTextIds)
        {
            var counted = new HashSet<ObjectId>();
            foreach (var id in modifiedFramedLabelIds
                         .Concat(appendedLabelIds)
                         .Concat(appendedSlopeArrowIds)
                         .Concat(appendedSlopeAngleTextIds))
            {
                if (!id.IsNull && !id.IsErased)
                {
                    counted.Add(id);
                }
            }

            foreach (var id in modifiedIds.Distinct())
            {
                if (id.IsNull ||
                    id.IsErased ||
                    counted.Contains(id) ||
                    !AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        database) ||
                    entity is null)
                {
                    continue;
                }

                if (ElementLabelStore.TryRead(entity, out _) ||
                    SlopeArrowStore.TryRead(entity, out _) ||
                    SlopeAngleTextStore.TryRead(entity, out _) ||
                    PostFootprintPerpendicularAnnotationStore.TryRead(entity, out _))
                {
                    counted.Add(id);
                }
            }

            return counted.Count;
        }

        private static void EraseAppendedAnnotationCopies(
            Transaction transaction,
            IReadOnlyCollection<ObjectId> appendedLabelIds,
            IReadOnlyCollection<ObjectId> appendedSlopeArrowIds,
            IReadOnlyCollection<ObjectId> appendedSlopeAngleTextIds)
        {
            foreach (var id in appendedLabelIds
                         .Concat(appendedSlopeArrowIds)
                         .Concat(appendedSlopeAngleTextIds)
                         .Distinct())
            {
                if (id.IsNull ||
                    id.IsErased ||
                    transaction.GetObject(id, OpenMode.ForRead, false) is not Entity entity)
                {
                    continue;
                }

                var isAcKrovyAnnotation =
                    ElementLabelStore.TryRead(entity, out _) ||
                    SlopeArrowStore.TryRead(entity, out _) ||
                    SlopeAngleTextStore.TryRead(entity, out _) ||
                    PostFootprintPerpendicularAnnotationStore.TryRead(entity, out _);
                if (!isAcKrovyAnnotation)
                {
                    continue;
                }

                entity.UpgradeOpen();
                entity.Erase();
            }
        }

        private static IReadOnlyDictionary<ObjectId, string> ReadElementIds(
            Database database,
            Transaction transaction,
            AutoCadTimberElementMetadataStore metadataStore,
            IReadOnlyList<ObjectId> ids)
        {
            var result = new Dictionary<ObjectId, string>();

            foreach (var id in ids.Distinct())
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        database) ||
                    entity is null ||
                    !metadataStore.TryRead(entity, out var data) ||
                    data is null)
                {
                    continue;
                }

                result[id] = data.ElementId;
            }

            return result;
        }

        private static IReadOnlyList<ObjectId> FilterTimberElementIds(
            Database database,
            Transaction transaction,
            AutoCadTimberElementMetadataStore metadataStore,
            IReadOnlyList<ObjectId> ids)
        {
            var result = new List<ObjectId>();

            foreach (var id in ids.Distinct())
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        database) ||
                    entity is null ||
                    !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                    !metadataStore.TryRead(entity, out var data) ||
                    data is null)
                {
                    continue;
                }

                result.Add(id);
            }

            return result;
        }

        private static bool IsAcKrovyCommand(string? commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return false;
            }

            return commandName.Trim().StartsWith("AK_", StringComparison.OrdinalIgnoreCase);
        }
    }
}
