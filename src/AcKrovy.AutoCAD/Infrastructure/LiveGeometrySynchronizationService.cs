using AcKrovy.AutoCAD.Settings;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
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
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _modifiedIds = new();
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _appendedTimberIds = new();
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _modifiedFramedLabelIds = new();
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _appendedLabelIds = new();
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _appendedSlopeArrowIds = new();
        private readonly LiveGeometryRefreshCoordinator<ObjectId> _appendedSlopeAngleTextIds = new();
        private readonly LiveGeometryRefreshCoordinator<string> _erasedSourceHandles = new();
        private bool _ignoreCurrentCommand;
        private bool _refreshAllTimberAnnotationsAfterCommand;
        private bool _preserveCopySourcesForCurrentCommand;
        private bool _stretchUndoMarkOpen;
        private bool _isDisposed;
        private string? _currentGlobalCommandName;

        public DocumentTracker(Document document)
        {
            _document = document;
            _document.Database.ObjectAppended += ObjectAppended;
            _document.Database.ObjectModified += ObjectModified;
            _document.Database.ObjectErased += ObjectErased;
            _document.CommandWillStart += CommandWillStart;
            _document.CommandEnded += CommandEnded;
            _document.CommandCancelled += CommandCancelled;
            _document.CommandFailed += CommandFailed;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _document.Database.ObjectAppended -= ObjectAppended;
            _document.Database.ObjectModified -= ObjectModified;
            _document.Database.ObjectErased -= ObjectErased;
            _document.CommandWillStart -= CommandWillStart;
            _document.CommandEnded -= CommandEnded;
            _document.CommandCancelled -= CommandCancelled;
            _document.CommandFailed -= CommandFailed;
            _modifiedIds.Clear();
            _appendedTimberIds.Clear();
            _modifiedFramedLabelIds.Clear();
            _appendedLabelIds.Clear();
            _appendedSlopeArrowIds.Clear();
            _appendedSlopeAngleTextIds.Clear();
            _erasedSourceHandles.Clear();
            _refreshAllTimberAnnotationsAfterCommand = false;
            _preserveCopySourcesForCurrentCommand = false;
            _currentGlobalCommandName = null;
            EndStretchUndoMark();
            RoofLiveResizeService.EndStretchCommandScope();
            RoofGroupGripGeometrySnapshotService.EndCommandScope("dispose");
            RoofGroupGripPreCommandBaselineService.Clear("dispose");
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
            if (_ignoreCurrentCommand ||
                _modifiedIds.IsSuppressed ||
                e.DBObject is not Entity entity ||
                entity.ObjectId.IsNull ||
                entity.IsErased)
            {
                return;
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

            if (!SlopeArrowStore.TryRead(entity, out _) &&
                !PostFootprintPerpendicularAnnotationStore.TryRead(entity, out _) &&
                !SlopeAngleTextStore.TryRead(entity, out _))
            {
                // Do not inspect XData from ObjectModified. During native
                // STRETCH AutoCAD can raise this event while the MLeader is in
                // an evaluation/open state in which XData is unavailable.
                // Classification is performed safely in the CommandEnded
                // transaction by PersistFramedManualOffsets.
                if (entity is MLeader)
                {
                    _modifiedFramedLabelIds.TryAdd(entity.ObjectId);
#if DEBUG
                    AutoCadFramedBlockContentStretchNormalizeLifecycleService.TraceQueueMLeader(
                        _document,
                        entity.ObjectId);
#endif
                    return;
                }

                _modifiedIds.TryAdd(entity.ObjectId);
            }
        }

        private void ObjectErased(object? sender, ObjectErasedEventArgs e)
        {
            if (_ignoreCurrentCommand ||
                _erasedSourceHandles.IsSuppressed ||
                !e.Erased ||
                e.DBObject is not Entity entity ||
                !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity))
            {
                return;
            }

            _erasedSourceHandles.TryAdd(entity.Handle.ToString());
        }

        private void CommandWillStart(object? sender, CommandEventArgs e)
        {
            var isUndoRedo = LiveGeometryCommandRules.IsUndoRedoCommand(e.GlobalCommandName);
            _ignoreCurrentCommand = IsAcKrovyCommand(e.GlobalCommandName) || isUndoRedo;
            _currentGlobalCommandName = e.GlobalCommandName;
            _refreshAllTimberAnnotationsAfterCommand =
                !isUndoRedo &&
                LiveGeometryCommandRules.RequiresFullTimberAnnotationRefresh(e.GlobalCommandName);
            _preserveCopySourcesForCurrentCommand =
                !isUndoRedo &&
                LiveGeometryCommandRules.IsCopySourcePreservingCommand(e.GlobalCommandName);
            // Always clear SOURCE-handled owner suppression at the boundary of a new
            // native command so a later genuine display-only GRIP_STRETCH is not masked.
            RoofLiveResizeService.BeginStretchCommandScope();
            RoofGroupGripGeometrySnapshotService.BeginCommandScope(e.GlobalCommandName);
            // True pre-command baseline MUST be captured here, before any native
            // ObjectModified can mutate DB geometry. Do not clear implied selection.
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

            if (!isUndoRedo &&
                !_ignoreCurrentCommand &&
                LiveGeometryCommandRules.RequiresGroupedUndoMark(e.GlobalCommandName))
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
            _refreshAllTimberAnnotationsAfterCommand = false;
            _preserveCopySourcesForCurrentCommand = false;
#if DEBUG
            AutoCadRedoDiagService.OnCommandEnded(e.GlobalCommandName);
#endif
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
                    preserveCopySources);
            }
            finally
            {
                EndStretchUndoMark();
                RoofLiveResizeService.EndStretchCommandScope();
                RoofGroupGripGeometrySnapshotService.EndCommandScope("CommandEnded");
                RoofGroupGripPreCommandBaselineService.Clear("CommandEnded");
                _currentGlobalCommandName = null;
            }
        }

        private void CommandCancelled(object? sender, CommandEventArgs e)
        {
            var isUndoRedo = LiveGeometryCommandRules.IsUndoRedoCommand(e.GlobalCommandName);
            ClearPendingLiveGeometryState();
            _refreshAllTimberAnnotationsAfterCommand = false;
            _preserveCopySourcesForCurrentCommand = false;
            _ignoreCurrentCommand = isUndoRedo;
            EndStretchUndoMark();
            RoofLiveResizeService.EndStretchCommandScope();
            RoofGroupGripGeometrySnapshotService.EndCommandScope("CommandCancelled");
            RoofGroupGripPreCommandBaselineService.Clear("CommandCancelled");
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
            _ignoreCurrentCommand = isUndoRedo;
            EndStretchUndoMark();
            RoofLiveResizeService.EndStretchCommandScope();
            RoofGroupGripGeometrySnapshotService.EndCommandScope("CommandFailed");
            RoofGroupGripPreCommandBaselineService.Clear("CommandFailed");
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
            _modifiedIds.Clear();
            _appendedTimberIds.Clear();
            _modifiedFramedLabelIds.Clear();
            _appendedLabelIds.Clear();
            _appendedSlopeArrowIds.Clear();
            _appendedSlopeAngleTextIds.Clear();
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
            bool preserveCopySources)
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
            // Suppress ObjectModified while SOURCE resize rebuilds display / regenerates
            // rafters. Otherwise GRIP_STRETCH display rebuild events re-queue and a later
            // pass misclassifies them as independent display-only tamper.
            // Freeze native grip geometry snapshot before any plugin Rebuild can overwrite it.
            RoofGroupGripGeometrySnapshotService.FreezeAll();
            IReadOnlyCollection<ObjectId> roofRelatedIds;
            using (_modifiedIds.Suppress())
            using (_appendedTimberIds.Suppress())
            using (_erasedSourceHandles.Suppress())
            {
                roofRelatedIds = RoofLiveResizeService.Process(
                    _document,
                    globalCommandName,
                    ids);
            }
            if (roofRelatedIds.Count > 0)
            {
                ids = ids.Where(id => !roofRelatedIds.Contains(id)).ToArray();
            }

            var appendedTimberIds = _appendedTimberIds.Drain();
            var modifiedFramedLabelIds = _modifiedFramedLabelIds.Drain();
            var appendedLabelIds = _appendedLabelIds.Drain();
            var appendedSlopeArrowIds = _appendedSlopeArrowIds.Drain();
            var appendedSlopeAngleTextIds = _appendedSlopeAngleTextIds.Drain();
            var erasedSourceHandles = _erasedSourceHandles.Drain();
            var hasLiveGeometryWork =
                ids.Count > 0 ||
                appendedTimberIds.Count > 0 ||
                modifiedFramedLabelIds.Count > 0 ||
                appendedLabelIds.Count > 0 ||
                appendedSlopeArrowIds.Count > 0 ||
                appendedSlopeAngleTextIds.Count > 0 ||
                erasedSourceHandles.Count > 0 ||
                refreshAllTimberAnnotations;

            using (_modifiedIds.Suppress())
            using (_appendedTimberIds.Suppress())
            using (_modifiedFramedLabelIds.Suppress())
            using (_appendedLabelIds.Suppress())
            using (_appendedSlopeArrowIds.Suppress())
            using (_appendedSlopeAngleTextIds.Suppress())
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

                // Same-DWG COPY: AutoCAD does not remap generated-rafter 1005.
                // Geometry association rebinds copied members after timber copy init.
                // Runs only for native COPY; never during U/UNDO/REDO/MREDO.
                RoofGeneratedRafterCopyOwnershipRehydrationService.Process(
                    _document,
                    globalCommandName);
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
            bool preserveCopySources)
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
            }
        }

#if DEBUG
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
