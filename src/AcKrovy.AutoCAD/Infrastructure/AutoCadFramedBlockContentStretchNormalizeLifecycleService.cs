#if DEBUG
using System.Globalization;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG P4A proof: deferred dogleg → content-side normalize after grip STRETCH.
/// Reuses LiveGeometrySynchronizationService event subscriptions — no parallel
/// DocumentCollection / Database event system.
/// </summary>
internal static class AutoCadFramedBlockContentStretchNormalizeLifecycleService
{
    public const string UndoBlockerReason =
        "CommandEnded StartTransaction cannot join the grip/STRETCH undo group; " +
        "normalize would be a separate Ctrl+Z step. Production wiring blocked until " +
        "a same-undo-record hook exists. No nested-command or string-execute undo tricks.";

    private static readonly Dictionary<Document, DocumentLifecycleState> States = new();
    private static int _autotestIsolationDepth;
    private static int _externalLifecycleMutationsDuringAutotest;

    public static TimberFramedBlockContentStretchNormalizeSession GetOrCreateSession(
        Document document) =>
        GetOrCreateState(document).Session;

    public static void RemoveSession(Document document) => States.Remove(document);

    /// <summary>
    /// Mutations that would have normalized while autotest isolation is active
    /// (Proof should be forced off — non-zero means external proof interfered).
    /// </summary>
    public static int ExternalLifecycleMutationsDuringAutotest =>
        _externalLifecycleMutationsDuringAutotest;

    public static bool IsAutotestIsolationActive => _autotestIsolationDepth > 0;

    public static void TraceWillStart(
        Document document,
        string? globalCommandName)
    {
        var state = GetOrCreateState(document);
        state.Session.BeginCommand(globalCommandName);
        state.QueuedIds.Clear();
        if (state.Session.TraceEnabled)
        {
            Write(
                document,
                $"LIFECYCLE TRACE CommandWillStart name='{state.Session.ActiveCommandName}'");
        }
    }

    public static void TraceQueueMLeader(
        Document document,
        ObjectId objectId)
    {
        if (objectId.IsNull)
        {
            return;
        }

        var state = GetOrCreateState(document);
        var key = objectId.Handle.ToString();
        var addedToSession = state.Session.TryQueueObjectKey(key);
        var addedToIds = state.QueuedIds.TryAdd(objectId);
        if (state.Session.TraceEnabled && (addedToSession || addedToIds))
        {
            Write(document, $"LIFECYCLE TRACE queued MLeader handle={key}");
        }
    }

    public static void TraceCancelledOrFailed(
        Document document,
        string phase,
        string? globalCommandName)
    {
        var state = GetOrCreateState(document);
        var queued = state.Session.QueuedCount;
        state.Session.CancelOrFailCommand();
        state.QueuedIds.Clear();
        if (state.Session.TraceEnabled)
        {
            Write(
                document,
                $"LIFECYCLE TRACE {phase} name='{TimberFramedBlockContentStretchNormalizeRules.NormalizeCommandName(globalCommandName)}' clearedQueue={queued}");
        }
    }

    public static void ProcessCommandEnded(
        Document document,
        string? globalCommandName)
    {
        var state = GetOrCreateState(document);
        var session = state.Session;
        session.RememberObservedCommandIfQueued();

        if (session.TraceEnabled)
        {
            Write(
                document,
                $"LIFECYCLE TRACE CommandEnded name='{TimberFramedBlockContentStretchNormalizeRules.NormalizeCommandName(globalCommandName)}' queued={session.QueuedCount} proof={session.ProofEnabled}");
        }

        if (!session.ShouldProcessEndedCommand(globalCommandName))
        {
            if (session.TraceEnabled && session.QueuedCount > 0)
            {
                Write(
                    document,
                    "LIFECYCLE TRACE skip normalize (proof off or command not confirmed)");
            }

            session.ClearQueue();
            state.QueuedIds.Clear();
            return;
        }

        if (IsAutotestIsolationActive)
        {
            // Proof was forced off for autotest; reaching here means external
            // proof/confirm leaked past isolation.
            _externalLifecycleMutationsDuringAutotest +=
                Math.Max(state.QueuedIds.Count, session.QueuedCount);
        }

        var ids = state.QueuedIds.Drain();
        session.DrainQueue();
        DrainNormalizeIds(document, state, session, ids);
    }

    public static void WriteStatus(Document document)
    {
        var session = GetOrCreateSession(document);
        var editor = document.Editor;
        editor.WriteMessage("\n=== AK_DEV_FBC_LIFECYCLE_STATUS ===");
        editor.WriteMessage($"\nTraceEnabled={session.TraceEnabled}");
        editor.WriteMessage($"\nProofEnabled={session.ProofEnabled}");
        editor.WriteMessage($"\nQueuedCount={session.QueuedCount}");
        editor.WriteMessage($"\nActiveCommand='{session.ActiveCommandName}'");
        editor.WriteMessage(
            $"\nObserved=[{string.Join(", ", session.ObservedCommandNames)}]");
        editor.WriteMessage(
            $"\nConfirmed=[{string.Join(", ", session.ConfirmedCommandNames)}]");
        editor.WriteMessage($"\nUNDO_BLOCKER={UndoBlockerReason}");
        editor.WriteMessage(
            "\nDefault after NETLOAD: Trace=OFF Proof=OFF Confirmed=empty. " +
            "Discover grip GlobalCommandName via TRACE, CONFIRM it, then PROOF_ON.");
    }

    /// <summary>
    /// Snapshot external DEBUG lifecycle state, force Trace/Proof off, clear
    /// queues and active synthetic command. Nestable; restore via
    /// <see cref="EndAutotestIsolation"/>.
    /// </summary>
    public static TimberFramedBlockContentStretchNormalizeExternalState
        BeginAutotestIsolation(Document document)
    {
        var state = GetOrCreateState(document);
        if (_autotestIsolationDepth == 0)
        {
            _externalLifecycleMutationsDuringAutotest = 0;
        }

        var snapshot = state.Session.CaptureExternalState();
        state.Session.ForceAutotestIsolation();
        state.Session.ClearConfirmedCommands();
        state.Session.ClearObservedCommands();
        state.QueuedIds.Clear();
        ReleaseProcessingGuardIfStuck(state.Session);
        _autotestIsolationDepth++;
        return snapshot;
    }

    /// <summary>
    /// Clear queues, release reentrancy guard, restore prior DEBUG lifecycle
    /// state exactly.
    /// </summary>
    public static void EndAutotestIsolation(
        Document document,
        TimberFramedBlockContentStretchNormalizeExternalState snapshot)
    {
        var state = GetOrCreateState(document);
        if (_autotestIsolationDepth > 0)
        {
            _autotestIsolationDepth--;
        }

        state.Session.ClearQueue();
        state.QueuedIds.Clear();
        ReleaseProcessingGuardIfStuck(state.Session);
        state.Session.RestoreExternalState(snapshot);
    }

    /// <summary>
    /// Isolated LifecycleProcessor drain: enqueue ObjectId and run the same
    /// dogleg→content-side processor without arming public Proof/Trace and
    /// without synthesizing CommandWillStart/Ended event pairs.
    /// </summary>
    public static void EnqueueAndDrainNormalizeForTest(
        Document document,
        ObjectId objectId)
    {
        if (objectId.IsNull)
        {
            return;
        }

        var state = GetOrCreateState(document);
        var session = state.Session;
        state.QueuedIds.Clear();
        session.ClearQueue();
        state.QueuedIds.TryAdd(objectId);
        session.TryQueueObjectKey(objectId.Handle.ToString());
        var ids = state.QueuedIds.Drain();
        session.DrainQueue();
        DrainNormalizeIds(document, state, session, ids);
    }

    /// <summary>
    /// Second-drain no-op probe (empty queues only).
    /// </summary>
    public static void DrainNormalizeForTestIfQueued(Document document)
    {
        var state = GetOrCreateState(document);
        var session = state.Session;
        var ids = state.QueuedIds.Drain();
        session.DrainQueue();
        if (ids.Count == 0)
        {
            return;
        }

        DrainNormalizeIds(document, state, session, ids);
    }

    /// <summary>
    /// DEBUG autotest hook retained for compatibility: enqueue + public
    /// ProcessCommandEnded path. Prefer
    /// <see cref="EnqueueAndDrainNormalizeForTest"/> from AUTOTEST isolation.
    /// </summary>
    public static void RunDeferredProcessorForTest(
        Document document,
        ObjectId objectId,
        string globalCommandName)
    {
        TraceWillStart(document, globalCommandName);
        TraceQueueMLeader(document, objectId);
        ProcessCommandEnded(document, globalCommandName);
    }

    public static void ArmLifecycleTest(Document document)
    {
        var session = GetOrCreateSession(document);
        session.ArmLifecycleTest(
            TimberFramedBlockContentAutotestRules.GripStretchCommandName);
        GetOrCreateState(document).QueuedIds.Clear();
    }

    public static void DisarmLifecycleTest(Document document)
    {
        var session = GetOrCreateSession(document);
        session.DisarmLifecycleTest();
        GetOrCreateState(document).QueuedIds.Clear();
    }

    private static void DrainNormalizeIds(
        Document document,
        DocumentLifecycleState state,
        TimberFramedBlockContentStretchNormalizeSession session,
        IReadOnlyList<ObjectId> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        try
        {
            using (session.BeginProcessing())
            using (session.SuppressQueue())
            using (state.QueuedIds.Suppress())
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                foreach (var objectId in ids)
                {
                    ProcessOne(document, transaction, session, objectId);
                }

                transaction.Commit();
            }
        }
        catch (System.Exception exception)
        {
            Write(
                document,
                $"LIFECYCLE TRACE normalize skipped: {exception.Message}");
            session.ClearQueue();
            state.QueuedIds.Clear();
            ReleaseProcessingGuardIfStuck(session);
        }
    }

    private static void ReleaseProcessingGuardIfStuck(
        TimberFramedBlockContentStretchNormalizeSession session)
    {
        if (!session.IsProcessing)
        {
            return;
        }

        session.ForceReleaseProcessingGuard();
        session.ClearQueue();
    }

    private static DocumentLifecycleState GetOrCreateState(Document document)
    {
        if (!States.TryGetValue(document, out var state))
        {
            state = new DocumentLifecycleState();
            States[document] = state;
        }

        return state;
    }

    private static void ProcessOne(
        Document document,
        Transaction transaction,
        TimberFramedBlockContentStretchNormalizeSession session,
        ObjectId objectId)
    {
        var editor = session.TraceEnabled ? document.Editor : null;
        var handleKey = objectId.Handle.ToString();
        if (objectId.IsNull ||
            objectId.IsErased ||
            transaction.GetObject(objectId, OpenMode.ForRead, true) is not MLeader leader ||
            leader.IsErased)
        {
            editor?.WriteMessage(
                $"\nLIFECYCLE TRACE handle={handleKey} not MLeader (filter reject)");
            return;
        }

        editor?.WriteMessage(
            $"\nLIFECYCLE TRACE handle={handleKey} entity=MLeader contentType={leader.ContentType}");

        if (leader.ContentType != ContentType.BlockContent ||
            leader.BlockContentId.IsNull)
        {
            editor?.WriteMessage(
                $"\nLIFECYCLE TRACE handle={handleKey} filter=reject (not BlockContent)");
            return;
        }

        if (!TryReadBlockNameAndCombinedAttrs(
                document.Database,
                transaction,
                leader,
                out var blockName,
                out var hasItemNo,
                out var hasWidth,
                out var hasHeight))
        {
            editor?.WriteMessage(
                $"\nLIFECYCLE TRACE handle={handleKey} filter=reject (BTR unavailable)");
            return;
        }

        var eligible = TimberFramedBlockContentStretchNormalizeRules.IsEligibleBlockContent(
            blockName,
            hasItemNo,
            hasWidth,
            hasHeight);
        editor?.WriteMessage(
            $"\nLIFECYCLE TRACE handle={handleKey} block='{blockName}' " +
            $"attrs ITEM_NO={hasItemNo} WIDTH={hasWidth} HEIGHT={hasHeight} " +
            $"filter={(eligible ? "P3R2Combined" : "reject")}");

        if (!eligible)
        {
            return;
        }

        leader.UpgradeOpen();

        editor?.WriteMessage(
            $"\nLIFECYCLE TRACE handle={handleKey} step={TimberFramedBlockContentStretchNormalizeRules.DoglegStep}");
        var dogleg = AutoCadFramedBlockContentNormalizeDoglegService.TryNormalizeDogleg(
            transaction,
            leader,
            editor);
        editor?.WriteMessage(
            $"\nLIFECYCLE TRACE handle={handleKey} dogleg applied={dogleg.Applied} " +
            $"changed={dogleg.Changed} reason={dogleg.Reason} " +
            $"attDrift={Format(dogleg.AttachmentDrift)} kneeDrift={Format(dogleg.KneeDrift)} " +
            $"bpDrift={Format(dogleg.BlockPositionDrift)}");

        editor?.WriteMessage(
            $"\nLIFECYCLE TRACE handle={handleKey} step={TimberFramedBlockContentStretchNormalizeRules.ContentSideStep}");
        var contentSide =
            AutoCadFramedBlockContentNormalizeContentSideService.TryNormalizeContentSide(
                document.Database,
                transaction,
                leader,
                editor);
        editor?.WriteMessage(
            $"\nLIFECYCLE TRACE handle={handleKey} contentSide applied={contentSide.Applied} " +
            $"changed={contentSide.Changed} reason={contentSide.Reason} " +
            $"before={contentSide.BeforeBlockContentId} after={contentSide.AfterBlockContentId} " +
            $"attDrift={Format(contentSide.AttachmentDrift)} kneeDrift={Format(contentSide.KneeDrift)} " +
            $"bpDrift={Format(contentSide.BlockPositionDrift)}");
    }

    private static bool TryReadBlockNameAndCombinedAttrs(
        Database database,
        Transaction transaction,
        MLeader leader,
        out string blockName,
        out bool hasItemNo,
        out bool hasWidth,
        out bool hasHeight)
    {
        blockName = string.Empty;
        hasItemNo = false;
        hasWidth = false;
        hasHeight = false;

        var blockId = leader.BlockContentId;
        if (blockId.IsNull || !AutoCadDatabaseIdentity.IsSame(database, blockId))
        {
            return false;
        }

        if (transaction.GetObject(blockId, OpenMode.ForRead, true) is not
                BlockTableRecord block ||
            block.IsErased)
        {
            return false;
        }

        blockName = block.Name ?? string.Empty;
        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                    AttributeDefinition attribute ||
                attribute.IsErased)
            {
                continue;
            }

            if (string.Equals(
                    attribute.Tag,
                    TimberFramedBlockContentDefinitionRules.ItemNoTag,
                    StringComparison.OrdinalIgnoreCase))
            {
                hasItemNo = true;
            }
            else if (string.Equals(
                         attribute.Tag,
                         TimberFramedBlockContentDefinitionRules.WidthTag,
                         StringComparison.OrdinalIgnoreCase))
            {
                hasWidth = true;
            }
            else if (string.Equals(
                         attribute.Tag,
                         TimberFramedBlockContentDefinitionRules.HeightTag,
                         StringComparison.OrdinalIgnoreCase))
            {
                hasHeight = true;
            }
        }

        return true;
    }

    private static void Write(Document document, string message) =>
        document.Editor.WriteMessage("\n" + message);

    private static string Format(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private sealed class DocumentLifecycleState
    {
        public TimberFramedBlockContentStretchNormalizeSession Session { get; } = new();

        public LiveGeometryRefreshCoordinator<ObjectId> QueuedIds { get; } = new();
    }
}
#endif
