#if DEBUG
using System.Globalization;
using System.IO;
using System.Text;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG Stage E: GripOverrule pass-through + in-callback shared dogleg →
/// content-side normalize after base.MoveGripPointsAt. Uses the write-open
/// callback MLeader directly (never reopens via ObjectId mid-drag). Mid-drag
/// preview callbacks may normalize whenever K→D→I is wrong (no write budget).
/// No SendStringToExecute, no cached GripData, no CommandEnded queue normalize.
/// Production OFF. UNDO_PROOF stays hard-disabled. REDO is unresolved.
/// </summary>
internal static class AutoCadFramedBlockContentGripNormalizeProofService
{
    internal const string DebugRegAppName = "AK_DEV_FBC_GRIP_NORMALIZE";
    private const string ProofLayerName = "AK_DEV_FBC_GRIP_NORMALIZE";
    private const string MarkerToken =
        TimberFramedBlockContentGripStageProofRules.DebugNormalizeMarkerToken;
    private const string CommandBanner = "AK_DEV_FBC_GRIP_NORMALIZE";

    private static readonly Dictionary<Document, DocumentProofState> States = new();
    private static readonly HashSet<string> LoggedDistinctExceptionKeys = new(StringComparer.Ordinal);
    private static FramedBlockContentGripNormalizeOverrule? _overrule;
    private static bool _overruleAdded;
    private static bool _overrulingWasEnabled;
    private static ObjectId _trackedLeaderId = ObjectId.Null;
    private static ObjectId _setupDimnxBlockId = ObjectId.Null;
    private static ObjectId _setupDimpxBlockId = ObjectId.Null;
    private static bool _armed;

    public static bool IsOverruleRegistered => _overruleAdded;

    public static string OverruleInstanceIdentity =>
        AutoCadFramedBlockContentGripRegistrationSnapshot.FormatInstanceIdentity(_overrule);

    public static TimberFramedBlockContentGripStageProofSession GetOrCreateSession(
        Document document) =>
        GetOrCreateState(document).Session;

    public static void RemoveSession(Document document)
    {
        if (States.TryGetValue(document, out var state) && state.Session.ProofEnabled)
        {
            DisableKeepEntities(document);
        }

        States.Remove(document);
        if (!States.Values.Any(s => s.Session.ProofEnabled))
        {
            ForceUnregisterOverrule();
        }
    }

    public static void ForceUnregisterAll()
    {
        foreach (var pair in States)
        {
            pair.Value.Session.ProofEnabled = false;
            pair.Value.Session.MarkUnregistered();
            pair.Value.Session.ForceReleaseProcessingGuard();
        }

        ForceUnregisterOverrule();
        _armed = false;
        LoggedDistinctExceptionKeys.Clear();
    }

    public static void Setup()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        var readiness = AutoCadFramedBlockContentGripReadonlyProofService.StageDReadiness;
        var armDecision = TimberFramedBlockContentGripStageProofRules.DecideStageEArm(
            readiness,
            out var armReason);

        editor.WriteMessage($"\n=== {CommandBanner}_SETUP ===");
        editor.WriteMessage(
            $"\nStageDReadiness=" +
            TimberFramedBlockContentGripStageProofRules.FormatStageDReadiness(readiness));
        editor.WriteMessage(
            $"\nStageEArmDecision=" +
            TimberFramedBlockContentGripStageProofRules.FormatStageEArmDecision(armDecision));
        editor.WriteMessage($"\nStageEArmReason={armReason}");
        editor.WriteMessage(
            $"\nStageEImplementationMode=" +
            TimberFramedBlockContentGripStageProofRules.StageEImplementationMode);

        if (armDecision == TimberFramedBlockContentGripStageEArmDecision.Blocked)
        {
            editor.WriteMessage("\n" + armReason);
            var storedFailure =
                AutoCadFramedBlockContentGripReadonlyProofService.LastStageDFailureReason;
            if (!string.IsNullOrWhiteSpace(storedFailure))
            {
                editor.WriteMessage("\n" + storedFailure);
            }

            editor.WriteMessage(
                "\nHost: Stage E setup blocked only while StageDReadiness=Failed. " +
                "Rerun AK_DEV_FBC_GRIP_READONLY_SETUP → knee drag → STATUS to clear, " +
                "or NETLOAD for StageDReadiness=NotRun (Allowed).");
            return;
        }

        // Atomic arming: fully succeed (one marked MLeader + overrule) or fully abort.
        ObjectId leaderId = ObjectId.Null;
        var createdCommitted = false;
        try
        {
            AutoCadFramedBlockContentGripPassthroughProofService.ForceUnregisterAll();
            AutoCadFramedBlockContentGripUndoProofService.ForceUnregisterAll();
            AutoCadFramedBlockContentGripReadonlyProofService.ForceUnregisterAll();
            ForceUnregisterAll();
            LoggedDistinctExceptionKeys.Clear();

            using (document.LockDocument())
            {
                var database = document.Database;
                using var transaction = database.TransactionManager.StartTransaction();

                ForceP4aLifecycleOff(document);
                var (_, erased) = EraseMarkedProofEntities(database, transaction);

                var textStyleId = database.Textstyle;
                var textStyle = (TextStyleTableRecord)transaction.GetObject(
                    textStyleId,
                    OpenMode.ForRead);
                var styleName = string.IsNullOrWhiteSpace(textStyle.Name)
                    ? "Standard"
                    : textStyle.Name;
                var layerId = EnsureProofLayer(database, transaction);
                var request = BuildRepresentativeRequest(styleName, textStyleId, layerId);
                var created = AutoCadFramedBlockContentAnnotationService.Create(
                    database,
                    transaction,
                    request);
                if (!created.Succeeded ||
                    created.LeaderId is not ObjectId createdId ||
                    createdId.IsNull)
                {
                    transaction.Abort();
                    AbortPartialSetup(document, ObjectId.Null);
                    editor.WriteMessage(
                        $"\n{CommandBanner}_SETUP FAIL create: {created.DiagnosticReason}");
                    editor.WriteMessage(
                        "\nAtomic abort: no entity, no overrule, guards cleared.");
                    return;
                }

                MarkProofEntity(database, transaction, createdId);
                leaderId = createdId;
                PreResolveSetupVariantBlockIds(database, transaction, createdId);
                var session = GetOrCreateSession(document);
                session.ClearProofRuntime();
                LoggedDistinctExceptionKeys.Clear();
                session.TrackedHandle = createdId.Handle.ToString();
                transaction.Commit();
                createdCommitted = true;
                editor.WriteMessage(
                    $"\n{CommandBanner}_SETUP created marked MLeader " +
                    $"handle={createdId.Handle} erasedOld={erased} " +
                    $"dimnx={FormatObjectId(_setupDimnxBlockId)} " +
                    $"dimpx={FormatObjectId(_setupDimpxBlockId)}");
            }

            RegisterOverrule(document, leaderId);
            _armed = true;
            editor.WriteMessage(
                $"\n{CommandBanner} armed (normalize after base move).");
            editor.WriteMessage(
                $"\nStageDReadiness=" +
                TimberFramedBlockContentGripStageProofRules.FormatStageDReadiness(
                    AutoCadFramedBlockContentGripReadonlyProofService.StageDReadiness));
            editor.WriteMessage(
                $"\nStageEArmDecision=" +
                TimberFramedBlockContentGripStageProofRules.FormatStageEArmDecision(
                    TimberFramedBlockContentGripStageEArmDecision.Allowed));
            editor.WriteMessage($"\nStageEArmReason={armReason}");
            editor.WriteMessage(
                $"\nStageEImplementationMode=" +
                TimberFramedBlockContentGripStageProofRules.StageEImplementationMode);
            editor.WriteMessage(
                "\nHost: select → grips → knee cross → expect auto-correct " +
                "visual, K→D→I correct, same handle, STATUS STATE_POST_GRIP_CORRECT.");
            editor.WriteMessage(
                "\nDo NOT test UNDO/REDO yet. " +
                TimberFramedBlockContentGripStageProofRules
                    .SameUndoHostSequenceDeferredDocumentation);
        }
        catch (System.Exception exception)
        {
            AbortPartialSetup(
                document,
                createdCommitted ? leaderId : ObjectId.Null);
            editor.WriteMessage(
                $"\n{CommandBanner}_SETUP FAIL: {exception.Message}");
            editor.WriteMessage(
                "\nAtomic abort: no entity, no overrule, guards cleared.");
        }
        finally
        {
            if (!_armed)
            {
                // Defense in depth if a path forgot AbortPartialSetup.
                ForceUnregisterOverrule();
            }
        }
    }

    private static void AbortPartialSetup(Document document, ObjectId createdLeaderId)
    {
        try
        {
            ForceUnregisterOverrule();
        }
        catch (System.Exception)
        {
            // Best-effort disarm.
        }

        _armed = false;
        LoggedDistinctExceptionKeys.Clear();
        _trackedLeaderId = ObjectId.Null;
        _setupDimnxBlockId = ObjectId.Null;
        _setupDimpxBlockId = ObjectId.Null;

        try
        {
            var session = GetOrCreateSession(document);
            session.ProofEnabled = false;
            session.MarkUnregistered();
            session.ForceReleaseProcessingGuard();
            session.ClearProofRuntime();
            ForceP4aLifecycleOff(document);
        }
        catch (System.Exception)
        {
            // Session cleanup best-effort.
        }

        if (createdLeaderId.IsNull)
        {
            return;
        }

        try
        {
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                EraseMarkedProofEntities(document.Database, transaction);
                transaction.Commit();
            }
        }
        catch (System.Exception)
        {
            // Entity erase best-effort on abort.
        }
    }

    public static void WriteStatus()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        var session = GetOrCreateSession(document);
        ForceP4aLifecycleOff(document);
        var p4a = AutoCadFramedBlockContentStretchNormalizeLifecycleService
            .GetOrCreateSession(document);

        bool? liveKdi = null;
        string? liveHandle = null;
        using (document.LockDocument())
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            if (!string.IsNullOrWhiteSpace(session.TrackedHandle) &&
                TryFindHandle(document.Database, transaction, session.TrackedHandle, out var id) &&
                transaction.GetObject(id, OpenMode.ForRead, true) is MLeader leader &&
                !leader.IsErased)
            {
                liveHandle = leader.ObjectId.Handle.ToString();
                if (AutoCadFramedBlockContentDimensionColumnPlacementService.TryEvaluate(
                        transaction,
                        leader,
                        out var evaluation,
                        out _,
                        out _))
                {
                    liveKdi = evaluation.Current.IsCorrect;
                }
            }

            transaction.Commit();
        }

        var state = session.ClassifyNormalizeCurrent(
            liveHandle,
            liveKdi ?? session.LastCurrentPlacementCorrect);
        session.LastNormalizeState = state;

        editor.WriteMessage($"\n=== {CommandBanner}_STATUS ===");
        editor.WriteMessage(
            $"\nSTATE={TimberFramedBlockContentGripStageProofRules.FormatNormalizeState(state)}");
        var readiness = AutoCadFramedBlockContentGripReadonlyProofService.StageDReadiness;
        var armDecision = TimberFramedBlockContentGripStageProofRules.DecideStageEArm(
            readiness,
            out var armReason);
        editor.WriteMessage(
            $"\nStageDReadiness=" +
            TimberFramedBlockContentGripStageProofRules.FormatStageDReadiness(readiness));
        editor.WriteMessage(
            $"\nStageEArmDecision=" +
            TimberFramedBlockContentGripStageProofRules.FormatStageEArmDecision(armDecision));
        editor.WriteMessage($"\nStageEArmReason={armReason}");
        editor.WriteMessage(
            $"\nStageEImplementationMode=" +
            TimberFramedBlockContentGripStageProofRules.StageEImplementationMode);
        editor.WriteMessage($"\nProofEnabled={session.ProofEnabled}");
        editor.WriteMessage($"\nOverruleRegistered={session.OverruleRegistered}");
        editor.WriteMessage($"\nTrackedHandle={session.TrackedHandle}");
        editor.WriteMessage($"\nLiveHandle={liveHandle ?? "n/a"}");
        editor.WriteMessage($"\nSameHandle={string.Equals(session.TrackedHandle, liveHandle, StringComparison.OrdinalIgnoreCase)}");
        editor.WriteMessage($"\nNativeMoveCompleted={session.LastNativeMoveCompleted}");
        editor.WriteMessage(
            $"\ncurrentPlacementCorrect=" +
            ((liveKdi ?? session.LastCurrentPlacementCorrect)?.ToString() ?? "n/a"));
        editor.WriteMessage($"\nLastDoglegChanged={session.LastDoglegChanged}");
        editor.WriteMessage($"\nLastDoglegReason={session.LastDoglegReason}");
        editor.WriteMessage($"\nLastContentSideChanged={session.LastContentSideChanged}");
        editor.WriteMessage($"\nLastContentSideReason={session.LastContentSideReason}");
        editor.WriteMessage($"\nCallbackCount={session.CallbackCount}");
        editor.WriteMessage($"\nBaseMoveCompletedCount={session.BaseMoveCompletedCount}");
        editor.WriteMessage($"\nNormalizeAttemptCount={session.NormalizeAttemptCount}");
        editor.WriteMessage($"\nNormalizeChangedCount={session.NormalizeChangedCount}");
        editor.WriteMessage($"\nNormalizeNoOpCount={session.NormalizeNoOpCount}");
        editor.WriteMessage($"\nTransientSkipCount={session.TransientSkipCount}");
        editor.WriteMessage($"\nExceptionCount={session.ExceptionCount}");
        editor.WriteMessage(
            $"\nLastOutcome=" +
            (session.LastNormalizeOutcome is TimberFramedBlockContentGripNormalizeOutcome outcome
                ? TimberFramedBlockContentGripStageProofRules.FormatNormalizeOutcome(outcome)
                : "n/a"));
        editor.WriteMessage($"\nLastReason={session.LastNormalizeReason}");
        editor.WriteMessage($"\nLastCallbackFailed={session.LastCallbackFailed}");
        editor.WriteMessage($"\nLastFailingOperation={session.LastFailingOperation}");
        editor.WriteMessage($"\nLastExceptionType={session.LastExceptionType}");
        editor.WriteMessage($"\nLastExceptionMessage={session.LastExceptionMessage}");
        editor.WriteMessage(
            $"\nFirstExceptionCallbackIndex=" +
            (session.FirstExceptionCallbackIndex?.ToString(CultureInfo.InvariantCulture) ?? "n/a"));
        editor.WriteMessage($"\nGuard.IsProcessing={session.IsProcessing}");
        editor.WriteMessage($"\nP4A.ProofEnabled={p4a.ProofEnabled}");
        editor.WriteMessage($"\nP4A.TraceEnabled={p4a.TraceEnabled}");
        editor.WriteMessage($"\nP4A.QueuedCount={p4a.QueuedCount}");
        editor.WriteMessage(
            $"\nCallbackOrder={string.Join("→", TimberFramedBlockContentGripStageProofRules.NormalizeCallbackOrder)}");
        editor.WriteMessage(
            "\nWriteModel=after base.MoveGripPointsAt: inspect write-open " +
            "callback MLeader; when K→D→I wrong → shared dogleg then " +
            "content-side (multi-preview normalize allowed); no GetObject reopen; " +
            "TransientSkip is not an exception.");
    }

    public static void DisableKeepEntities()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is not null)
        {
            DisableKeepEntities(document);
        }
        else
        {
            ForceUnregisterAll();
        }
    }

    public static void Clean()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        DisableKeepEntities(document);
        using var documentLock = document.LockDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var (found, erased) = EraseMarkedProofEntities(document.Database, transaction);
        transaction.Commit();
        GetOrCreateSession(document).ClearProofRuntime();
        document.Editor.WriteMessage($"\n=== {CommandBanner}_CLEAN ===");
        document.Editor.WriteMessage($"\noldProofEntitiesFound={found}");
        document.Editor.WriteMessage($"\noldProofEntitiesErased={erased}");
    }

    private static void DisableKeepEntities(Document document)
    {
        var state = GetOrCreateState(document);
        var session = state.Session;
        session.ProofEnabled = false;
        session.ForceReleaseProcessingGuard();
        ForceP4aLifecycleOff(document);
        session.MarkUnregistered();
        if (!States.Values.Any(s => s.Session.ProofEnabled))
        {
            ForceUnregisterOverrule();
        }

        _armed = false;
        document.Editor.WriteMessage($"\n{CommandBanner}_OFF (overrule removed)");
    }

    private static void RegisterOverrule(Document document, ObjectId leaderId)
    {
        if (leaderId.IsNull)
        {
            throw new InvalidOperationException(
                "Normalize setup cannot register without a leader ObjectId.");
        }

        ForceUnregisterOverrule();
        _trackedLeaderId = leaderId;
        var state = GetOrCreateState(document);
        var session = state.Session;
        session.ProofEnabled = true;
        _overrule ??= new FramedBlockContentGripNormalizeOverrule();
        _overrulingWasEnabled = Overrule.Overruling;
        Overrule.Overruling = true;
        Overrule.AddOverrule(
            RXClass.GetClass(typeof(MLeader)),
            _overrule,
            false);
        _overruleAdded = true;
        session.TryRegisterOnce();
        AutoCadRedoDiagService.OnOverruleRegister(
            "Normalize",
            OverruleInstanceIdentity,
            _overrulingWasEnabled);
        AutoCadRedoDiagService.OnProofEnableDisable("Normalize", "ENABLE", true);
    }

    private static void ForceUnregisterOverrule()
    {
        if (_overruleAdded && _overrule is not null)
        {
            var identity = OverruleInstanceIdentity;
            var ownedWasEnabled = _overrulingWasEnabled;
            try
            {
                Overrule.RemoveOverrule(
                    RXClass.GetClass(typeof(MLeader)),
                    _overrule);
            }
            catch (AcadException)
            {
                // Already removed.
            }

            _overruleAdded = false;
            if (!_overrulingWasEnabled)
            {
                Overrule.Overruling = false;
            }

            AutoCadRedoDiagService.OnOverruleUnregister(
                "Normalize",
                identity,
                removed: true,
                overrulingRestoredTo: ownedWasEnabled,
                ownedWasEnabled: ownedWasEnabled);
            AutoCadRedoDiagService.OnProofEnableDisable("Normalize", "DISABLE", false);
        }

        _trackedLeaderId = ObjectId.Null;
        _setupDimnxBlockId = ObjectId.Null;
        _setupDimpxBlockId = ObjectId.Null;
        _armed = false;
    }

    private static void ForceP4aLifecycleOff(Document document)
    {
        var p4a = AutoCadFramedBlockContentStretchNormalizeLifecycleService
            .GetOrCreateSession(document);
        p4a.ForceAutotestIsolation();
        p4a.ClearConfirmedCommands();
        p4a.ClearObservedCommands();
        p4a.ClearQueue();
        if (p4a.IsProcessing)
        {
            p4a.ForceReleaseProcessingGuard();
        }
    }

    private static DocumentProofState GetOrCreateState(Document document)
    {
        if (!States.TryGetValue(document, out var state))
        {
            state = new DocumentProofState();
            States[document] = state;
        }

        return state;
    }

    private static AutoCadFramedBlockContentAnnotationRequest BuildRepresentativeRequest(
        string styleName,
        ObjectId styleId,
        ObjectId layerId)
    {
        const int denom = 50;
        var scale = TimberAnnotationScaleRules.GetScaleFactor(denom);
        var frame = TimberItemLeaderBlockDefinitionRules.Resolve(
            ItemNumberLeaderStyle.Circle,
            "12");
        var frameWidth = frame.WidthMm * scale;
        var frameHeight = frame.HeightMm * scale;
        var dimPaper = TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm;
        var envelope =
            TimberFramedBlockContentDefinitionRules
                .CalculateReferenceDimensionEnvelopeWidthMm(dimPaper) * scale;
        var firstSegment =
            TimberItemLeaderLayoutCalculator.FirstSegmentLengthMm * scale;
        var landing =
            TimberItemLeaderLayoutCalculator.CombinedFramedLandingDistanceMm *
            scale;

        return new AutoCadFramedBlockContentAnnotationRequest(
            AttachmentX: 16000d,
            AttachmentY: 16000d,
            ElementAxisRadians: Math.PI / 2d,
            Side: TimberLeaderHorizontalSide.Right,
            ContentKind: TimberFramedBlockContentKind.Circle,
            Presentation: TimberFramedBlockContentPresentation.Combined,
            FrameWidthMm: frameWidth,
            FrameHeightMm: frameHeight,
            DimensionColumnEnvelopeWidthMm: envelope,
            AnnotationScaleDenominator: denom,
            ItemPaperHeightMm: TimberFramedBlockContentAutotestRules.DefaultItemPaperHeightMm,
            DimensionPaperHeightMm: dimPaper,
            ItemTextStyleName: styleName,
            DimensionTextStyleName: styleName,
            ItemTextStyleId: styleId,
            DimensionTextStyleId: styleId,
            ItemNoText: "12",
            WidthText: "120",
            HeightText: "60",
            FirstSegmentLengthModelMm: firstSegment,
            LandingLengthModelMm: landing,
            LayerId: layerId,
            StabilizationMode: AutoCadFramedBlockContentStabilizationMode.RecordGraphicsRefresh);
    }

    /// <summary>
    /// After base.MoveGripPointsAt: inspect the write-open callback MLeader
    /// directly (never GetObject reopen). Applicability / transient gates,
    /// then shared dogleg → content-side only when K→D→I is wrong. One handle.
    /// Multi-preview normalize is allowed. No CommandEnded queue.
    /// </summary>
    private static TimberFramedBlockContentGripNormalizeOutcome
        TryNormalizeAfterNativeMove(
            Document document,
            MLeader writeOpenLeader,
            out string reason)
    {
        reason = string.Empty;
        var session = GetOrCreateSession(document);
        session.NormalizeAttemptCount++;
        session.LastFailingOperation = "TryNormalize.entry";
        session.LastEntityOpenState = SnapshotEntityOpenState(writeOpenLeader);
        session.LastCallbackFailed = false;

        ForceP4aLifecycleOff(document);

        if (writeOpenLeader.IsDisposed)
        {
            reason = "callback MLeader disposed";
            session.LastFailingOperation = "entity lifetime";
            return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
        }

        if (writeOpenLeader.IsErased)
        {
            reason = "callback MLeader erased";
            session.LastFailingOperation = "entity lifetime";
            return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
        }

        var database = writeOpenLeader.Database ?? document.Database;
        if (database is null || database.IsDisposed)
        {
            reason = "Database null/disposed";
            session.LastFailingOperation = "database";
            return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
        }

        // ObjectId may be Null mid-drag — still use the write-open callback entity.
        // Do NOT GetObject(ObjectId.Null) / reopen the callback MLeader.
        var handle = writeOpenLeader.ObjectId.IsNull
            ? session.TrackedHandle
            : writeOpenLeader.ObjectId.Handle.ToString();
        if (!string.IsNullOrWhiteSpace(handle))
        {
            if (!string.IsNullOrWhiteSpace(session.TrackedHandle) &&
                !string.Equals(
                    session.TrackedHandle,
                    handle,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "handle mismatch";
                session.LastNormalizeState =
                    TimberFramedBlockContentGripNormalizeProofState.Unknown;
                return TimberFramedBlockContentGripNormalizeOutcome.NotApplicable;
            }

            session.TrackedHandle = handle;
        }

        if (writeOpenLeader.ContentType != ContentType.BlockContent)
        {
            reason = "not BlockContent";
            session.LastFailingOperation = "applicability";
            return TimberFramedBlockContentGripNormalizeOutcome.NotApplicable;
        }

        if (writeOpenLeader.BlockContentId.IsNull)
        {
            reason = "BlockContentId Null (transient)";
            session.LastFailingOperation = "BlockContentId";
            return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
        }

        using (AutoCadFramedBlockContentStretchNormalizeLifecycleService
                   .GetOrCreateSession(document)
                   .SuppressQueue())
        {
            Transaction? ownedOpenClose = null;
            Transaction transaction;
            var top = database.TransactionManager.TopTransaction;
            if (top is not null)
            {
                transaction = top;
            }
            else
            {
                try
                {
                    session.LastFailingOperation =
                        "StartOpenCloseTransaction (related only)";
                    ownedOpenClose =
                        database.TransactionManager.StartOpenCloseTransaction();
                    transaction = ownedOpenClose;
                }
                catch (AcadException exception) when (
                    IsTransientAcadStatus(exception.ErrorStatus))
                {
                    reason = "OpenClose unavailable:" + exception.ErrorStatus;
                    return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
                }
            }

            try
            {
                session.LastFailingOperation = "applicability BTR";
                if (!TryIsApplicableLeader(database, transaction, writeOpenLeader))
                {
                    reason = "not applicable (no-op)";
                    session.LastDoglegChanged = false;
                    session.LastContentSideChanged = false;
                    session.LastDoglegReason = reason;
                    session.LastContentSideReason = reason;
                    TryCapturePlacementCorrect(session, transaction, writeOpenLeader);
                    session.LastNormalizeState = session.ClassifyNormalizeCurrent(
                        handle,
                        session.LastCurrentPlacementCorrect);
                    return TimberFramedBlockContentGripNormalizeOutcome.NotApplicable;
                }

                session.LastFailingOperation = "K→D→I TryEvaluate";
                bool placementCorrect;
                TimberFramedBlockContentDimensionColumnMirrorEvaluation evaluation;
                try
                {
                    if (!AutoCadFramedBlockContentDimensionColumnPlacementService.TryEvaluate(
                            transaction,
                            writeOpenLeader,
                            out evaluation,
                            out _,
                            out var evaluateNote))
                    {
                        if (IsTransientEvaluateNote(evaluateNote))
                        {
                            reason = string.IsNullOrWhiteSpace(evaluateNote)
                                ? "TryEvaluate failed (transient AttrRef?)"
                                : evaluateNote;
                            return TimberFramedBlockContentGripNormalizeOutcome
                                .TransientSkip;
                        }

                        reason = evaluateNote;
                        return TimberFramedBlockContentGripNormalizeOutcome.Failed;
                    }

                    placementCorrect = evaluation.Current.IsCorrect;
                }
                catch (AcadException exception) when (
                    IsTransientAcadStatus(exception.ErrorStatus))
                {
                    reason = "TryEvaluate:" + exception.ErrorStatus;
                    session.LastFailingOperation = "K→D→I TryEvaluate";
                    return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
                }

                session.LastCurrentPlacementCorrect = placementCorrect;
                if (placementCorrect)
                {
                    // Already correct after native move — prefer SuccessNoOp;
                    // do not repeatedly swap while the cursor is still moving.
                    reason = "K→D→I already correct";
                    session.LastDoglegChanged = false;
                    session.LastContentSideChanged = false;
                    session.LastDoglegReason = reason;
                    session.LastContentSideReason = reason;
                    session.LastNormalizeState = session.ClassifyNormalizeCurrent(
                        handle,
                        true);
                    return TimberFramedBlockContentGripNormalizeOutcome.SuccessNoOp;
                }

                var preferredOpposite = ResolvePreferredOppositeBlockId(
                    writeOpenLeader.BlockContentId,
                    transaction);

                session.LastFailingOperation = "SharedDoglegNormalize";
                AutoCadFramedBlockContentNormalizeResult dogleg;
                try
                {
                    dogleg =
                        AutoCadFramedBlockContentNormalizeDoglegService.TryNormalizeDogleg(
                            writeOpenLeader,
                            database);
                }
                catch (AcadException exception) when (
                    IsTransientAcadStatus(exception.ErrorStatus))
                {
                    reason = "dogleg:" + exception.ErrorStatus;
                    return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
                }
                catch (InvalidOperationException exception)
                {
                    reason = "dogleg geometry unstable:" + exception.Message;
                    return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
                }

                session.LastDoglegChanged = dogleg.Changed;
                session.LastDoglegReason = dogleg.Reason;

                session.LastFailingOperation = "SharedContentSideNormalize";
                AutoCadFramedBlockContentNormalizeResult contentSide;
                try
                {
                    contentSide =
                        AutoCadFramedBlockContentNormalizeContentSideService
                            .TryNormalizeContentSide(
                                writeOpenLeader,
                                database,
                                preferredOpposite);
                }
                catch (AcadException exception) when (
                    IsTransientAcadStatus(exception.ErrorStatus))
                {
                    reason = "content-side:" + exception.ErrorStatus;
                    return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
                }
                catch (InvalidOperationException exception)
                {
                    reason = "content-side geometry unstable:" + exception.Message;
                    return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
                }

                session.LastContentSideChanged = contentSide.Changed;
                session.LastContentSideReason = contentSide.Reason;

                if (!dogleg.Applied && IsTransientNormalizeReason(dogleg.Reason))
                {
                    reason = "dogleg transient: " + dogleg.Reason;
                    return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
                }

                if (!contentSide.Applied &&
                    IsTransientNormalizeReason(contentSide.Reason))
                {
                    reason = "content-side transient: " + contentSide.Reason;
                    return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
                }

                session.LastFailingOperation = "post-normalize K→D→I verify";
                try
                {
                    if (AutoCadFramedBlockContentDimensionColumnPlacementService.TryEvaluate(
                            transaction,
                            writeOpenLeader,
                            out var afterEval,
                            out _,
                            out _))
                    {
                        session.LastCurrentPlacementCorrect = afterEval.Current.IsCorrect;
                    }
                }
                catch (AcadException exception) when (
                    IsTransientAcadStatus(exception.ErrorStatus))
                {
                    reason = "post-verify:" + exception.ErrorStatus;
                    return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
                }

                session.LastNormalizeState = session.ClassifyNormalizeCurrent(
                    handle,
                    session.LastCurrentPlacementCorrect);

                var anyChanged = dogleg.Changed || contentSide.Changed;
                if (anyChanged)
                {
                    reason =
                        "dogleg=" + dogleg.Reason +
                        "; content-side=" + contentSide.Reason;
                    return TimberFramedBlockContentGripNormalizeOutcome.SuccessChanged;
                }

                if (!dogleg.Applied || !contentSide.Applied)
                {
                    reason =
                        "normalize incomplete: dogleg=" + dogleg.Reason +
                        "; content-side=" + contentSide.Reason;
                    return TimberFramedBlockContentGripNormalizeOutcome.Failed;
                }

                reason = "normalize applied but no geometry/content change";
                return TimberFramedBlockContentGripNormalizeOutcome.SuccessNoOp;
            }
            finally
            {
                if (ownedOpenClose is not null)
                {
                    try
                    {
                        ownedOpenClose.Commit();
                    }
                    catch (System.Exception)
                    {
                        try
                        {
                            ownedOpenClose.Abort();
                        }
                        catch (System.Exception)
                        {
                            // OpenClose cleanup best-effort.
                        }
                    }

                    ownedOpenClose.Dispose();
                }
            }
        }
    }

    private static void TryCapturePlacementCorrect(
        TimberFramedBlockContentGripStageProofSession session,
        Transaction transaction,
        MLeader leader)
    {
        try
        {
            if (AutoCadFramedBlockContentDimensionColumnPlacementService.TryEvaluate(
                    transaction,
                    leader,
                    out var evaluation,
                    out _,
                    out _))
            {
                session.LastCurrentPlacementCorrect = evaluation.Current.IsCorrect;
            }
        }
        catch (AcadException)
        {
            // Best-effort STATUS scalar only.
        }
    }

    private static ObjectId ResolvePreferredOppositeBlockId(
        ObjectId currentBlockId,
        Transaction transaction)
    {
        if (currentBlockId.IsNull)
        {
            return ObjectId.Null;
        }

        if (!_setupDimnxBlockId.IsNull &&
            !_setupDimpxBlockId.IsNull)
        {
            if (currentBlockId == _setupDimnxBlockId)
            {
                return _setupDimpxBlockId;
            }

            if (currentBlockId == _setupDimpxBlockId)
            {
                return _setupDimnxBlockId;
            }
        }

        // Fallback: parse current name; prefer pre-resolved scalar when sides match.
        try
        {
            if (transaction.GetObject(currentBlockId, OpenMode.ForRead, true) is
                    BlockTableRecord block &&
                TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                    block.Name,
                    out var parse) &&
                parse.DimensionColumnSide is TimberFramedBlockContentDimensionColumnSide side)
            {
                return side == TimberFramedBlockContentDimensionColumnSide.NegativeLocalX
                    ? _setupDimpxBlockId
                    : _setupDimnxBlockId;
            }
        }
        catch (AcadException)
        {
            return ObjectId.Null;
        }

        return ObjectId.Null;
    }

    private static void PreResolveSetupVariantBlockIds(
        Database database,
        Transaction transaction,
        ObjectId leaderId)
    {
        _setupDimnxBlockId = ObjectId.Null;
        _setupDimpxBlockId = ObjectId.Null;
        if (leaderId.IsNull ||
            transaction.GetObject(leaderId, OpenMode.ForRead, true) is not MLeader leader ||
            leader.IsErased ||
            leader.BlockContentId.IsNull)
        {
            return;
        }

        var currentId = leader.BlockContentId;
        if (transaction.GetObject(currentId, OpenMode.ForRead, true) is not
                BlockTableRecord currentBlock ||
            currentBlock.IsErased ||
            !TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                currentBlock.Name,
                out var parse) ||
            parse.DimensionColumnSide is not
                TimberFramedBlockContentDimensionColumnSide currentSide)
        {
            return;
        }

        if (currentSide == TimberFramedBlockContentDimensionColumnSide.NegativeLocalX)
        {
            _setupDimnxBlockId = currentId;
        }
        else
        {
            _setupDimpxBlockId = currentId;
        }

        // Resolve opposite via shared helper (Ensure if missing) — scalars only.
        AttributeDefinition? itemDef = null;
        AttributeDefinition? widthDef = null;
        Entity? frame = null;
        foreach (ObjectId id in currentBlock)
        {
            if (id.IsNull)
            {
                continue;
            }

            if (transaction.GetObject(id, OpenMode.ForRead, true) is not Entity entity ||
                entity.IsErased)
            {
                continue;
            }

            if (entity is AttributeDefinition attribute)
            {
                if (string.Equals(
                        attribute.Tag,
                        TimberFramedBlockContentDefinitionRules.ItemNoTag,
                        StringComparison.OrdinalIgnoreCase))
                {
                    itemDef = attribute;
                }
                else if (string.Equals(
                             attribute.Tag,
                             TimberFramedBlockContentDefinitionRules.WidthTag,
                             StringComparison.OrdinalIgnoreCase))
                {
                    widthDef = attribute;
                }
            }
            else
            {
                frame ??= entity;
            }
        }

        if (itemDef is null || widthDef is null)
        {
            return;
        }

        var contentKind = TimberFramedBlockContentKind.Circle;
        if (frame is Circle)
        {
            contentKind = TimberFramedBlockContentKind.Circle;
        }
        else if (frame is DBPoint)
        {
            contentKind = TimberFramedBlockContentKind.Plain;
        }
        else if (frame is Polyline)
        {
            contentKind = TimberFramedBlockContentKind.Rectangle;
        }

        var itemStyle = (TextStyleTableRecord)transaction.GetObject(
            itemDef.TextStyleId,
            OpenMode.ForRead);
        var dimStyle = (TextStyleTableRecord)transaction.GetObject(
            widthDef.TextStyleId,
            OpenMode.ForRead);
        var itemStyleName = string.IsNullOrWhiteSpace(itemStyle.Name)
            ? "Standard"
            : itemStyle.Name;
        var dimStyleName = string.IsNullOrWhiteSpace(dimStyle.Name)
            ? "Standard"
            : dimStyle.Name;
        var itemPaper =
            itemDef.Height /
            TimberFramedBlockContentDefinitionRules.BaselineDenominator;
        var dimPaper =
            widthDef.Height /
            TimberFramedBlockContentDefinitionRules.BaselineDenominator;
        string itemText = "12";
        using (var attr = leader.GetBlockAttribute(itemDef.ObjectId))
        {
            if (attr is not null)
            {
                itemText = attr.TextString ?? "12";
            }
        }

        if (AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveOppositeVariantId(
                    database,
                    transaction,
                    currentId,
                    contentKind,
                    itemStyleName,
                    dimStyleName,
                    itemDef.TextStyleId,
                    widthDef.TextStyleId,
                    itemPaper,
                    dimPaper,
                    itemText,
                    out var oppositeId,
                    out _) &&
            !oppositeId.IsNull)
        {
            if (currentSide == TimberFramedBlockContentDimensionColumnSide.NegativeLocalX)
            {
                _setupDimpxBlockId = oppositeId;
            }
            else
            {
                _setupDimnxBlockId = oppositeId;
            }
        }
    }

    private static bool IsTransientEvaluateNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ||
        note.Contains("Missing AttrRef", StringComparison.OrdinalIgnoreCase) ||
        note.Contains("Null", StringComparison.OrdinalIgnoreCase) ||
        note.Contains("unavailable", StringComparison.OrdinalIgnoreCase);

    private static bool IsTransientNormalizeReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) &&
        (reason.Contains("Null", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("transient", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("Missing AttrRef", StringComparison.OrdinalIgnoreCase));

    private static bool IsTransientAcadStatus(ErrorStatus status) =>
        status is ErrorStatus.WasOpenForWrite
            or ErrorStatus.LockViolation
            or ErrorStatus.WasOpenForNotify
            or ErrorStatus.NotOpenForRead
            or ErrorStatus.NotOpenForWrite
            or ErrorStatus.InvalidContext
            or ErrorStatus.NoDatabase
            or ErrorStatus.NullObjectId
            or ErrorStatus.NullObjectPointer;

    private static string SnapshotEntityOpenState(MLeader leader)
    {
        try
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"IsWriteEnabled={leader.IsWriteEnabled};IsNotifyEnabled={leader.IsNotifyEnabled};IsReadEnabled={leader.IsReadEnabled};IsErased={leader.IsErased};IsDisposed={leader.IsDisposed};ObjectIdNull={leader.ObjectId.IsNull}");
        }
        catch (System.Exception exception)
        {
            return "open-state-unavailable:" + exception.GetType().Name;
        }
    }

    private static string FormatObjectId(ObjectId id) =>
        id.IsNull ? "Null" : id.Handle.ToString();

    private static void RecordCaughtException(
        Document document,
        TimberFramedBlockContentGripStageProofSession session,
        System.Exception exception,
        string failingOperationFallback)
    {
        var operation = string.IsNullOrWhiteSpace(session.LastFailingOperation)
            ? failingOperationFallback
            : session.LastFailingOperation;
        var typeName = exception.GetType().FullName ?? exception.GetType().Name;
        var message = exception.Message ?? string.Empty;
        if (exception is AcadException acad)
        {
            message = acad.ErrorStatus + ": " + message;
        }

        var stack = exception.ToString();
        session.RecordExceptionDiagnostics(
            session.CallbackCount,
            typeName,
            message,
            stack,
            operation);
        session.LastCallbackFailed = true;
        session.LastNormalizeState =
            TimberFramedBlockContentGripNormalizeProofState.CallbackFailed;
        session.RecordNormalizeOutcome(
            TimberFramedBlockContentGripNormalizeOutcome.Failed,
            operation + ": " + message);
        LogFirstDistinctException(document, session, exception, operation);
    }

    private static void LogFirstDistinctException(
        Document document,
        TimberFramedBlockContentGripStageProofSession session,
        System.Exception exception,
        string operation)
    {
        var typeName = exception.GetType().FullName ?? exception.GetType().Name;
        var message = exception.Message ?? string.Empty;
        if (exception is AcadException acad)
        {
            message = acad.ErrorStatus + ": " + message;
        }

        var key = typeName + "|" + message + "|" + operation;
        if (!LoggedDistinctExceptionKeys.Add(key))
        {
            return;
        }

        try
        {
            var scratch = TryFindScratchDirectory();
            var directory = scratch ??
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AcKrovy");
            Directory.CreateDirectory(directory);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            var path = Path.Combine(
                directory,
                "ak_dev_fbc_grip_normalize_exception_" + stamp + ".txt");
            var body = new StringBuilder();
            body.AppendLine("AK_DEV_FBC_GRIP_NORMALIZE first distinct callback exception");
            body.AppendLine("Timestamp=" + stamp);
            body.AppendLine(
                "CallbackIndex=" +
                session.CallbackCount.ToString(CultureInfo.InvariantCulture));
            body.AppendLine("FailingOperation=" + operation);
            body.AppendLine("ExceptionType=" + typeName);
            body.AppendLine("ExceptionMessage=" + message);
            body.AppendLine("TrackedHandle=" + session.TrackedHandle);
            body.AppendLine("EntityOpenState=" + session.LastEntityOpenState);
            body.AppendLine("MLeader.ObjectId=" + FormatObjectId(_trackedLeaderId));
            body.AppendLine(
                "SetupDimnxBlockId=" + FormatObjectId(_setupDimnxBlockId));
            body.AppendLine(
                "SetupDimpxBlockId=" + FormatObjectId(_setupDimpxBlockId));
            try
            {
                body.AppendLine(
                    "Database.IsDisposed=" +
                    (document.Database?.IsDisposed.ToString() ?? "n/a"));
                body.AppendLine(
                    "TransactionManager.TopTransactionNull=" +
                    (document.Database?.TransactionManager.TopTransaction is null));
            }
            catch (System.Exception)
            {
                body.AppendLine("Database/TransactionManager=unavailable");
            }

            body.AppendLine("--- stack ---");
            body.AppendLine(exception.ToString());
            File.WriteAllText(path, body.ToString(), Encoding.UTF8);
        }
        catch (System.Exception)
        {
            // Scratch logging must never throw into the grip callback.
        }
    }

    private static string? TryFindScratchDirectory()
    {
        try
        {
            var dir = new DirectoryInfo(
                Path.GetDirectoryName(
                    typeof(AutoCadFramedBlockContentGripNormalizeProofService)
                        .Assembly.Location) ??
                AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AcKrovy.sln")))
                {
                    var scratch = Path.Combine(dir.FullName, "_scratch");
                    Directory.CreateDirectory(scratch);
                    return scratch;
                }

                dir = dir.Parent;
            }
        }
        catch (System.Exception)
        {
            // Fall back to LocalAppData.
        }

        return null;
    }

    private static bool TryIsApplicableLeader(
        Database database,
        Transaction transaction,
        MLeader leader)
    {
        if (leader.IsErased ||
            leader.ContentType != ContentType.BlockContent ||
            leader.BlockContentId.IsNull)
        {
            return false;
        }

        if (!TryReadBlockNameAndCombinedAttrs(
                database,
                transaction,
                leader,
                out var blockName,
                out var hasItemNo,
                out var hasWidth,
                out var hasHeight))
        {
            return false;
        }

        return TimberFramedBlockContentGripStageProofRules.IsApplicableBlockContent(
            blockName,
            hasItemNo,
            hasWidth,
            hasHeight);
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

    private static bool TryFindHandle(
        Database database,
        Transaction transaction,
        string handleText,
        out ObjectId objectId)
    {
        objectId = ObjectId.Null;
        if (!long.TryParse(
                handleText,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return false;
        }

        try
        {
            objectId = database.GetObjectId(
                false,
                new Handle(value),
                0);
            return !objectId.IsNull &&
                transaction.GetObject(objectId, OpenMode.ForRead, true) is not null;
        }
        catch (AcadException)
        {
            return false;
        }
    }

    private static (int Found, int Erased) EraseMarkedProofEntities(
        Database database,
        Transaction transaction)
    {
        var modelSpace = OpenModelSpace(database, transaction, OpenMode.ForRead);
        var candidates = new List<ObjectId>();
        foreach (ObjectId id in modelSpace)
        {
            candidates.Add(id);
        }

        var found = 0;
        var erased = 0;
        foreach (var id in candidates)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not Entity entity ||
                entity.IsErased ||
                !HasProofMarker(entity))
            {
                continue;
            }

            found++;
            if (!entity.IsWriteEnabled)
            {
                entity.UpgradeOpen();
            }

            entity.Erase();
            erased++;
        }

        return (found, erased);
    }

    private static void MarkProofEntity(
        Database database,
        Transaction transaction,
        ObjectId entityId)
    {
        if (entityId.IsNull ||
            transaction.GetObject(entityId, OpenMode.ForWrite, true) is not Entity entity ||
            entity.IsErased)
        {
            return;
        }

        EnsureDebugRegApp(database, transaction);
        var retained = ReadForeignXData(entity);
        retained.Add(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName, DebugRegAppName));
        retained.Add(
            new TypedValue(
                (int)DxfCode.ExtendedDataAsciiString,
                $"{MarkerToken}|" +
                $"{TimberFramedBlockContentGripStageProofRules.RepresentativeCaseKey}"));
        entity.XData = new ResultBuffer(retained.ToArray());
    }

    private static bool HasProofMarker(Entity entity)
    {
        using var buffer = entity.GetXDataForApplication(DebugRegAppName);
        if (buffer is null)
        {
            return false;
        }

        foreach (var value in buffer)
        {
            if (value.TypeCode == (int)DxfCode.ExtendedDataAsciiString &&
                Convert.ToString(value.Value) is string payload &&
                payload.StartsWith(MarkerToken, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static List<TypedValue> ReadForeignXData(Entity entity)
    {
        var retained = new List<TypedValue>();
        var xdata = entity.XData;
        if (xdata is null)
        {
            return retained;
        }

        using (xdata)
        {
            var skip = false;
            foreach (var value in xdata.AsArray())
            {
                if (value.TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                {
                    skip = string.Equals(
                        Convert.ToString(value.Value),
                        DebugRegAppName,
                        StringComparison.OrdinalIgnoreCase);
                }

                if (!skip)
                {
                    retained.Add(value);
                }
            }
        }

        return retained;
    }

    private static void EnsureDebugRegApp(Database database, Transaction transaction)
    {
        var regApps = (RegAppTable)transaction.GetObject(
            database.RegAppTableId,
            OpenMode.ForRead);
        if (regApps.Has(DebugRegAppName))
        {
            return;
        }

        regApps.UpgradeOpen();
        var record = new RegAppTableRecord { Name = DebugRegAppName };
        regApps.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static ObjectId EnsureProofLayer(Database database, Transaction transaction)
    {
        var layers = (LayerTable)transaction.GetObject(
            database.LayerTableId,
            OpenMode.ForRead);
        if (layers.Has(ProofLayerName))
        {
            return layers[ProofLayerName];
        }

        layers.UpgradeOpen();
        var layer = new LayerTableRecord { Name = ProofLayerName };
        var id = layers.Add(layer);
        transaction.AddNewlyCreatedDBObject(layer, true);
        return id;
    }

    private static BlockTableRecord OpenModelSpace(
        Database database,
        Transaction transaction,
        OpenMode mode)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        return (BlockTableRecord)transaction.GetObject(
            blockTable[BlockTableRecord.ModelSpace],
            mode);
    }

    private sealed class DocumentProofState
    {
        public TimberFramedBlockContentGripStageProofSession Session { get; } = new();
    }

    /// <summary>
    /// Stage E: base grips + normalize on write-open callback MLeader.
    /// ObjectId IsApplicable only. Never reopens callback via GetObject.
    /// </summary>
    private sealed class FramedBlockContentGripNormalizeOverrule : GripOverrule
    {
        public override bool IsApplicable(RXObject overruledSubject)
        {
            if (_trackedLeaderId.IsNull)
            {
                return false;
            }

            if (overruledSubject is not DBObject dbObject || dbObject.IsErased)
            {
                return false;
            }

            return dbObject.ObjectId == _trackedLeaderId;
        }

        public override void GetGripPoints(
            Entity entity,
            Point3dCollection gripPoints,
            IntegerCollection osnapModes,
            IntegerCollection geomIds)
        {
            base.GetGripPoints(entity, gripPoints, osnapModes, geomIds);
        }

        public override void GetGripPoints(
            Entity entity,
            GripDataCollection grips,
            double curViewUnitSize,
            int gripSize,
            Vector3d curViewDir,
            GetGripPointsFlags bitFlags)
        {
            base.GetGripPoints(
                entity,
                grips,
                curViewUnitSize,
                gripSize,
                curViewDir,
                bitFlags);
        }

        public override void MoveGripPointsAt(
            Entity entity,
            IntegerCollection indices,
            Vector3d offset)
        {
            RunNormalizeCallback(
                entity,
                () => base.MoveGripPointsAt(entity, indices, offset));
        }

        public override void MoveGripPointsAt(
            Entity entity,
            GripDataCollection grips,
            Vector3d offset,
            MoveGripPointsFlags bitFlags)
        {
            RunNormalizeCallback(
                entity,
                () => base.MoveGripPointsAt(entity, grips, offset, bitFlags));
        }

        private static void RunNormalizeCallback(Entity entity, Action baseMove)
        {
            var document = AcApplication.DocumentManager.MdiActiveDocument;
            if (document is null || entity is not MLeader leader)
            {
                baseMove();
                return;
            }

            var session = GetOrCreateSession(document);
            if (!session.ProofEnabled || session.IsProcessing)
            {
                baseMove();
                return;
            }

            // Outer safety catch only — TransientSkip must not reach here.
            // Real exceptions increment ExceptionCount and fail Stage E.
            try
            {
                // 1 enter guard → 2 base once → 3–6 inspect/normalize → 7 exit
                using (session.BeginProcessing())
                {
                    session.CallbackCount++;
                    session.LastFailingOperation = "base.MoveGripPointsAt";
                    baseMove();
                    session.BaseMoveCompletedCount++;
                    session.LastNativeMoveCompleted = true;
                    session.LastFailingOperation = "TryNormalizeAfterNativeMove";
                    session.LastEntityOpenState = SnapshotEntityOpenState(leader);

                    var outcome = TryNormalizeAfterNativeMove(
                        document,
                        leader,
                        out var reason);
                    session.RecordNormalizeOutcome(outcome, reason);
                    // Soft Failed does not set LastCallbackFailed — only caught
                    // exceptions do (via RecordCaughtException).
                    ForceP4aLifecycleOff(document);
                }
            }
            catch (System.Exception exception)
            {
                RecordCaughtException(
                    document,
                    session,
                    exception,
                    "outer MoveGripPointsAt catch");
                session.ForceReleaseProcessingGuard();
            }
        }
    }
}
#endif
