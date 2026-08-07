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
/// DEBUG Stage D: GripOverrule pass-through + read-only inspection after
/// base.MoveGripPointsAt. No dogleg/content-side writes, no BlockContentId
/// swap, no lifecycle queue, no cached GripData. Production OFF.
/// </summary>
internal static class AutoCadFramedBlockContentGripReadonlyProofService
{
    internal const string DebugRegAppName = "AK_DEV_FBC_GRIP_READONLY";
    private const string ProofLayerName = "AK_DEV_FBC_GRIP_READONLY";
    private const string MarkerToken =
        TimberFramedBlockContentGripStageProofRules.DebugReadOnlyMarkerToken;
    private const string CommandBanner = "AK_DEV_FBC_GRIP_READONLY";

    private static readonly Dictionary<Document, DocumentProofState> States = new();
    private static readonly HashSet<string> LoggedDistinctExceptionKeys = new(
        StringComparer.Ordinal);
    private static FramedBlockContentGripReadonlyOverrule? _overrule;
    private static bool _overruleAdded;
    private static bool _overrulingWasEnabled;
    private static ObjectId _trackedLeaderId = ObjectId.Null;
    private static bool _armed;

    public static bool IsOverruleRegistered => _overruleAdded;

    public static string OverruleInstanceIdentity =>
        AutoCadFramedBlockContentGripRegistrationSnapshot.FormatInstanceIdentity(_overrule);

    /// <summary>
    /// Sticky in-process Stage D readiness for Stage E arming.
    /// Fresh NETLOAD / new READONLY_SETUP → NotRun (not Failed).
    /// ExceptionCount&gt;0 → Failed. Proven zero-exception inspection → Passed.
    /// Never persisted to workstation JSON/registry.
    /// </summary>
    private static TimberFramedBlockContentGripStageDReadiness _stageDReadiness =
        TimberFramedBlockContentGripStageDReadiness.NotRun;

    private static string _lastStageDFailureReason = string.Empty;

    public static TimberFramedBlockContentGripStageDReadiness StageDReadiness =>
        _stageDReadiness;

    public static string LastStageDFailureReason => _lastStageDFailureReason;

    /// <summary>
    /// True only when readiness is Passed (not NotRun).
    /// </summary>
    public static bool IsStageDZeroExceptionReady =>
        _stageDReadiness == TimberFramedBlockContentGripStageDReadiness.Passed;

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
        foreach (var state in States.Values)
        {
            state.Session.ProofEnabled = false;
            state.Session.MarkUnregistered();
            state.Session.ForceReleaseProcessingGuard();
        }

        ForceUnregisterOverrule();
        _armed = false;
    }

    public static void Setup()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        // Mutual exclusion: never stack grip overrules.
        AutoCadFramedBlockContentGripPassthroughProofService.ForceUnregisterAll();
        AutoCadFramedBlockContentGripUndoProofService.ForceUnregisterAll();
        AutoCadFramedBlockContentGripNormalizeProofService.ForceUnregisterAll();
        ForceUnregisterOverrule();
        ClearStageDZeroExceptionReady();
        LoggedDistinctExceptionKeys.Clear();

        ObjectId leaderId = ObjectId.Null;
        try
        {
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
                    editor.WriteMessage(
                        $"\n{CommandBanner}_SETUP FAIL create: {created.DiagnosticReason}");
                    transaction.Commit();
                    return;
                }

                MarkProofEntity(database, transaction, createdId);
                leaderId = createdId;
                var session = GetOrCreateSession(document);
                session.ClearProofRuntime();
                session.TrackedHandle = createdId.Handle.ToString();
                transaction.Commit();
                editor.WriteMessage(
                    $"\n{CommandBanner}_SETUP created marked MLeader " +
                    $"handle={createdId.Handle} erasedOld={erased}");
            }

            RegisterOverrule(document, leaderId);
            _armed = true;
            editor.WriteMessage($"\n{CommandBanner} armed (read-only after base move).");
            editor.WriteMessage(
                "\nHost: select → native grips → knee cross WIDTH/HEIGHT → " +
                "STATUS expects ExceptionCount=0, InspectionSuccessCount>=1, " +
                "currentPlacementCorrect=False, WouldNormalizeContentSide=True.");
        }
        catch (System.Exception exception)
        {
            ForceUnregisterOverrule();
            _armed = false;
            ClearStageDZeroExceptionReady();
            var session = GetOrCreateSession(document);
            session.ProofEnabled = false;
            session.MarkUnregistered();
            editor.WriteMessage(
                $"\n{CommandBanner}_SETUP FAIL: {exception.Message}");
            editor.WriteMessage("\nOverrule force-unregistered in catch.");
        }
        finally
        {
            if (!_armed)
            {
                ForceUnregisterOverrule();
            }
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

        RefreshStageDZeroExceptionReady(session);

        editor.WriteMessage($"\n=== {CommandBanner}_STATUS ===");
        editor.WriteMessage($"\nProofEnabled={session.ProofEnabled}");
        editor.WriteMessage($"\nOverruleRegistered={session.OverruleRegistered}");
        editor.WriteMessage($"\nTrackedHandle={session.TrackedHandle}");
        editor.WriteMessage($"\nSameHandle={session.TrackedHandle}");
        editor.WriteMessage($"\nNativeMoveCompleted={session.LastNativeMoveCompleted}");
        editor.WriteMessage(
            $"\ncurrentPlacementCorrect=" +
            (session.LastCurrentPlacementCorrect?.ToString() ?? "n/a"));
        editor.WriteMessage(
            $"\nWouldNormalizeDogleg={session.LastWouldNormalizeDogleg}");
        editor.WriteMessage(
            $"\nWouldNormalizeContentSide={session.LastWouldNormalizeContentSide}");
        editor.WriteMessage($"\nCallbackCount={session.CallbackCount}");
        editor.WriteMessage(
            $"\nBaseMoveCompletedCount={session.BaseMoveCompletedCount}");
        editor.WriteMessage(
            $"\nInspectionSuccessCount={session.InspectionSuccessCount}");
        editor.WriteMessage(
            $"\nInspectionTransientSkipCount={session.InspectionTransientSkipCount}");
        editor.WriteMessage(
            $"\nInspectionNotApplicableCount={session.InspectionNotApplicableCount}");
        editor.WriteMessage($"\nExceptionCount={session.ExceptionCount}");
        editor.WriteMessage(
            $"\nStageDReadiness=" +
            TimberFramedBlockContentGripStageProofRules.FormatStageDReadiness(
                _stageDReadiness));
        editor.WriteMessage(
            $"\nStageDZeroExceptionReady={IsStageDZeroExceptionReady}");
        var armDecision = TimberFramedBlockContentGripStageProofRules.DecideStageEArm(
            _stageDReadiness,
            out var armReason);
        editor.WriteMessage(
            $"\nStageEArmDecision=" +
            TimberFramedBlockContentGripStageProofRules.FormatStageEArmDecision(
                armDecision));
        editor.WriteMessage($"\nStageEArmReason={armReason}");
        editor.WriteMessage(
            $"\nStageEImplementationMode=" +
            TimberFramedBlockContentGripStageProofRules.StageEImplementationMode);
        editor.WriteMessage($"\nGuard.IsProcessing={session.IsProcessing}");
        editor.WriteMessage(
            $"\nLastInspectionOutcome=" +
            (session.LastInspectionOutcome is { } outcome
                ? TimberFramedBlockContentGripStageProofRules.FormatInspectionOutcome(
                    outcome)
                : "n/a"));
        editor.WriteMessage(
            $"\nLastInspectionReason={session.LastInspectionReason}");
        editor.WriteMessage(
            $"\nLastFailingOperation={session.LastFailingOperation}");
        editor.WriteMessage(
            $"\nLastExceptionType={session.LastExceptionType}");
        editor.WriteMessage(
            $"\nLastExceptionMessage={session.LastExceptionMessage}");
        editor.WriteMessage(
            $"\nLastExceptionStack={Truncate(session.LastExceptionStack, 1200)}");
        editor.WriteMessage(
            $"\nFirstExceptionCallbackIndex=" +
            (session.FirstExceptionCallbackIndex?.ToString(
                 CultureInfo.InvariantCulture) ??
             "n/a"));
        editor.WriteMessage(
            $"\nLastExceptionCallbackIndex=" +
            (session.LastExceptionCallbackIndex?.ToString(
                 CultureInfo.InvariantCulture) ??
             "n/a"));
        editor.WriteMessage(
            $"\nLastCallbackOffset=(" +
            $"{Fmt(session.LastCallbackOffsetX)}," +
            $"{Fmt(session.LastCallbackOffsetY)}," +
            $"{Fmt(session.LastCallbackOffsetZ)})");
        editor.WriteMessage(
            $"\nLastCallbackGripIndices={session.LastCallbackGripIndices}");
        editor.WriteMessage(
            $"\nLastEntityOpenState={session.LastEntityOpenState}");
        editor.WriteMessage($"\nP4A.ProofEnabled={p4a.ProofEnabled}");
        editor.WriteMessage($"\nP4A.TraceEnabled={p4a.TraceEnabled}");
        editor.WriteMessage($"\nP4A.QueuedCount={p4a.QueuedCount}");
        if (session.LastInspection is { } inspection)
        {
            editor.WriteMessage(
                $"\nLastInspection: handle={inspection.Handle} " +
                $"kdi={inspection.CurrentPlacementCorrect} " +
                $"block='{inspection.BlockContentName}' " +
                $"DIM={inspection.DimensionColumnSideToken} " +
                $"att=({Fmt(inspection.AttachmentX)},{Fmt(inspection.AttachmentY)}) " +
                $"knee=({Fmt(inspection.KneeX)},{Fmt(inspection.KneeY)}) " +
                $"bp=({Fmt(inspection.BlockPositionX)},{Fmt(inspection.BlockPositionY)})");
        }

        editor.WriteMessage(
            $"\nCallbackOrder={string.Join("→", TimberFramedBlockContentGripStageProofRules.ReadOnlyCallbackOrder)}");
        editor.WriteMessage("\nWrites=NONE (read-only inspection after base).");
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
        ClearStageDZeroExceptionReady();
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
        var session = GetOrCreateSession(document);
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
                "Read-only setup cannot register without a leader ObjectId.");
        }

        ForceUnregisterOverrule();
        _trackedLeaderId = leaderId;
        var session = GetOrCreateSession(document);
        session.ProofEnabled = true;
        _overrule ??= new FramedBlockContentGripReadonlyOverrule();
        _overrulingWasEnabled = Overrule.Overruling;
        Overrule.Overruling = true;
        Overrule.AddOverrule(
            RXClass.GetClass(typeof(MLeader)),
            _overrule,
            false);
        _overruleAdded = true;
        session.TryRegisterOnce();
        AutoCadRedoDiagService.OnOverruleRegister(
            "Readonly",
            OverruleInstanceIdentity,
            _overrulingWasEnabled);
        AutoCadRedoDiagService.OnProofEnableDisable("Readonly", "ENABLE", true);
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
                "Readonly",
                identity,
                removed: true,
                overrulingRestoredTo: ownedWasEnabled,
                ownedWasEnabled: ownedWasEnabled);
            AutoCadRedoDiagService.OnProofEnableDisable("Readonly", "DISABLE", false);
        }

        _trackedLeaderId = ObjectId.Null;
        _armed = false;
        // No GripData / MLeader instance cache.
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

    private static void ClearStageDZeroExceptionReady()
    {
        _stageDReadiness = TimberFramedBlockContentGripStageDReadiness.NotRun;
        _lastStageDFailureReason = string.Empty;
    }

    private static void MarkStageDFailed(string failureReason)
    {
        _stageDReadiness = TimberFramedBlockContentGripStageDReadiness.Failed;
        if (!string.IsNullOrWhiteSpace(failureReason))
        {
            _lastStageDFailureReason = failureReason;
        }
    }

    private static void RefreshStageDZeroExceptionReady(
        TimberFramedBlockContentGripStageProofSession session)
    {
        var classified = TimberFramedBlockContentGripStageProofRules.ClassifyStageDReadiness(
            session.ExceptionCount,
            session.InspectionSuccessCount);
        switch (classified)
        {
            case TimberFramedBlockContentGripStageDReadiness.Failed:
                MarkStageDFailed(
                    FormatStoredFailureReason(
                        session.LastExceptionType,
                        session.LastExceptionMessage,
                        session.LastFailingOperation));
                break;
            case TimberFramedBlockContentGripStageDReadiness.Passed:
                _stageDReadiness = TimberFramedBlockContentGripStageDReadiness.Passed;
                break;
            default:
                // Counter silence is NotRun only when sticky state was not Failed.
                // Do not demote Passed → NotRun on a no-op refresh with cleared
                // counters; SETUP/CLEAN explicitly reset to NotRun.
                if (_stageDReadiness != TimberFramedBlockContentGripStageDReadiness.Failed &&
                    _stageDReadiness != TimberFramedBlockContentGripStageDReadiness.Passed)
                {
                    _stageDReadiness = TimberFramedBlockContentGripStageDReadiness.NotRun;
                }

                break;
        }
    }

    private static string FormatStoredFailureReason(
        string? exceptionType,
        string? exceptionMessage,
        string? failingOperation)
    {
        var type = string.IsNullOrWhiteSpace(exceptionType) ? "n/a" : exceptionType;
        var message = string.IsNullOrWhiteSpace(exceptionMessage) ? "n/a" : exceptionMessage;
        var operation = string.IsNullOrWhiteSpace(failingOperation) ? "n/a" : failingOperation;
        return $"StageDFailure: operation={operation} type={type} message={message}";
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
            AttachmentX: 15000d,
            AttachmentY: 15000d,
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

    private static string Fmt(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value ?? string.Empty;
        }

        return value[..maxChars] + "...";
    }

    private static string SnapshotEntityOpenState(MLeader leader)
    {
        try
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"IsWriteEnabled={leader.IsWriteEnabled};IsNotifyEnabled={leader.IsNotifyEnabled};IsReadEnabled={leader.IsReadEnabled};IsErased={leader.IsErased};IsDisposed={leader.IsDisposed}");
        }
        catch (System.Exception exception)
        {
            return "open-state-unavailable:" + exception.GetType().Name;
        }
    }

    private static bool TryReadAttachmentKnee(
        MLeader leader,
        out Point3d attachment,
        out Point3d knee,
        out string reason)
    {
        attachment = default;
        knee = default;
        reason = string.Empty;
        try
        {
            var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            if (leaderIndexes.Length == 0)
            {
                reason = "MLeader has no leaders (transient).";
                return false;
            }

            var lineIndexes = leader.GetLeaderLineIndexes(leaderIndexes[0]).Cast<int>().ToArray();
            if (lineIndexes.Length == 0)
            {
                reason = "MLeader has no leader lines (transient).";
                return false;
            }

            var lineIndex = lineIndexes[0];
            attachment = leader.GetFirstVertex(lineIndex);
            knee = leader.GetLastVertex(lineIndex);
            return true;
        }
        catch (AcadException exception) when (IsTransientAcadStatus(exception.ErrorStatus))
        {
            reason = "vertex-read:" + exception.ErrorStatus;
            return false;
        }
        catch (System.Exception exception)
        {
            reason = "vertex-read:" + exception.GetType().Name + ":" + exception.Message;
            return false;
        }
    }

    private static bool TryReadBlockPosition(
        MLeader leader,
        out Point3d blockPosition,
        out string reason)
    {
        blockPosition = default;
        reason = string.Empty;
        try
        {
            blockPosition = leader.BlockPosition;
            return true;
        }
        catch (AcadException exception) when (IsTransientAcadStatus(exception.ErrorStatus))
        {
            reason = "BlockPosition:" + exception.ErrorStatus;
            return false;
        }
        catch (System.Exception exception)
        {
            reason = "BlockPosition:" + exception.GetType().Name + ":" + exception.Message;
            return false;
        }
    }

    private static Vector3d? TryReadDogleg(MLeader leader)
    {
        try
        {
            var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            if (leaderIndexes.Length == 0)
            {
                return null;
            }

            return leader.GetDogleg(leaderIndexes[0]);
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    private static bool DoglegDirectionMatches(Vector3d? current, TimberPlanarVector expected)
    {
        if (current is not Vector3d vector)
        {
            return false;
        }

        var a = new Vector3d(vector.X, vector.Y, 0d);
        var b = new Vector3d(expected.X, expected.Y, 0d);
        if (a.Length <= 1e-9d || b.Length <= 1e-9d)
        {
            return false;
        }

        return a.GetNormal().IsEqualTo(b.GetNormal(), new Tolerance(1e-6d, 1e-6d));
    }

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

    /// <summary>
    /// Read-only inspection after base move. Uses the callback MLeader directly
    /// (already open in grip context). Never StartTransaction + GetObject on
    /// that MLeader. Related BTR/AttrDef reads use TopTransaction or a short
    /// OpenClose transaction that does not reopen the leader. Expected
    /// transient states return outcomes — they do not throw.
    /// </summary>
    private static TimberFramedBlockContentGripReadOnlyInspectionOutcome
        TryInspectAfterNativeGripMove(
            Document document,
            MLeader leader,
            out string reason)
    {
        reason = string.Empty;
        var session = GetOrCreateSession(document);
        session.LastFailingOperation = "TryInspect.entry";
        session.LastEntityOpenState = SnapshotEntityOpenState(leader);

        if (leader.IsDisposed)
        {
            reason = "callback MLeader disposed";
            session.LastFailingOperation = "entity lifetime";
            return TimberFramedBlockContentGripReadOnlyInspectionOutcome.ObjectUnavailable;
        }

        if (leader.IsErased)
        {
            reason = "callback MLeader erased";
            session.LastFailingOperation = "entity lifetime";
            return TimberFramedBlockContentGripReadOnlyInspectionOutcome.ObjectUnavailable;
        }

        if (leader.ContentType != ContentType.BlockContent ||
            leader.BlockContentId.IsNull)
        {
            reason = "not BlockContent / missing BlockContentId";
            session.LastFailingOperation = "applicability";
            return TimberFramedBlockContentGripReadOnlyInspectionOutcome.NotApplicable;
        }

        session.LastFailingOperation = "vertex read";
        if (!TryReadAttachmentKnee(leader, out var attachment, out var knee, out reason))
        {
            return TimberFramedBlockContentGripReadOnlyInspectionOutcome
                .TransientGeometryNotReady;
        }

        session.LastFailingOperation = "BlockPosition";
        if (!TryReadBlockPosition(leader, out var blockPos, out reason))
        {
            return TimberFramedBlockContentGripReadOnlyInspectionOutcome
                .TransientGeometryNotReady;
        }

        var dogleg = TryReadDogleg(leader);
        var handle = leader.ObjectId.IsNull
            ? session.TrackedHandle
            : leader.ObjectId.Handle.ToString();
        if (!string.IsNullOrWhiteSpace(handle))
        {
            session.TrackedHandle = handle;
        }

        var database = leader.Database;
        if (database is null)
        {
            reason = "leader.Database null";
            session.LastFailingOperation = "database";
            return TimberFramedBlockContentGripReadOnlyInspectionOutcome.ObjectUnavailable;
        }

        // Prefer AutoCAD's existing top transaction. Otherwise OpenClose for
        // related objects only — never reopen the callback MLeader via GetObject.
        var topTransaction = database.TransactionManager.TopTransaction;
        Transaction? ownedOpenClose = null;
        Transaction transaction;
        if (topTransaction is not null)
        {
            transaction = topTransaction;
        }
        else
        {
            try
            {
                session.LastFailingOperation = "StartOpenCloseTransaction (related only)";
                ownedOpenClose = database.TransactionManager.StartOpenCloseTransaction();
                transaction = ownedOpenClose;
            }
            catch (AcadException exception) when (IsTransientAcadStatus(exception.ErrorStatus))
            {
                reason = "OpenClose unavailable:" + exception.ErrorStatus;
                return TimberFramedBlockContentGripReadOnlyInspectionOutcome
                    .TransientGeometryNotReady;
            }
        }

        try
        {
            session.LastFailingOperation = "BlockTableRecord ForRead";
            string blockName;
            TimberFramedBlockContentDimensionColumnSide? side = null;
            try
            {
                if (transaction.GetObject(leader.BlockContentId, OpenMode.ForRead, true) is not
                        BlockTableRecord block ||
                    block.IsErased)
                {
                    reason = "BlockTableRecord unavailable";
                    return TimberFramedBlockContentGripReadOnlyInspectionOutcome
                        .ObjectUnavailable;
                }

                blockName = block.Name ?? string.Empty;
                if (TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                        blockName,
                        out var parse))
                {
                    side = parse.DimensionColumnSide;
                    if (parse.IsItemOnly)
                    {
                        reason = "ItemOnly no-op";
                        session.LastFailingOperation = "applicability";
                        return TimberFramedBlockContentGripReadOnlyInspectionOutcome
                            .NotApplicable;
                    }
                }
            }
            catch (AcadException exception) when (IsTransientAcadStatus(exception.ErrorStatus))
            {
                reason = "BTR open:" + exception.ErrorStatus;
                return TimberFramedBlockContentGripReadOnlyInspectionOutcome
                    .TransientGeometryNotReady;
            }

            session.LastFailingOperation = "K→D→I TryEvaluate / AttrRef";
            var kdiCorrect = false;
            var wouldContent = false;
            try
            {
                if (!AutoCadFramedBlockContentDimensionColumnPlacementService.TryEvaluate(
                        transaction,
                        leader,
                        out var evaluation,
                        out _,
                        out var evaluateNote))
                {
                    if (string.IsNullOrWhiteSpace(evaluateNote) ||
                        evaluateNote.Contains("Missing AttrRef", StringComparison.OrdinalIgnoreCase))
                    {
                        reason = string.IsNullOrWhiteSpace(evaluateNote)
                            ? "TryEvaluate failed (transient AttrRef?)"
                            : evaluateNote;
                        return TimberFramedBlockContentGripReadOnlyInspectionOutcome
                            .TransientGeometryNotReady;
                    }

                    reason = evaluateNote;
                    return TimberFramedBlockContentGripReadOnlyInspectionOutcome
                        .InvalidContract;
                }

                kdiCorrect = evaluation.Current.IsCorrect;
                wouldContent =
                    TimberFramedBlockContentGripStageProofRules.WouldNormalizeContentSide(
                        evaluation.Decision);
            }
            catch (AcadException exception) when (IsTransientAcadStatus(exception.ErrorStatus))
            {
                reason = "TryEvaluate:" + exception.ErrorStatus;
                return TimberFramedBlockContentGripReadOnlyInspectionOutcome
                    .TransientGeometryNotReady;
            }

            session.LastFailingOperation = "dogleg would-normalize";
            var geometryResolved =
                TimberFramedBlockContentDoglegRules.TryNormalizeDoglegGeometry(
                    new TimberPlanarPoint(attachment.X, attachment.Y),
                    new TimberPlanarPoint(knee.X, knee.Y),
                    new TimberPlanarPoint(blockPos.X, blockPos.Y),
                    out var expectedDirection,
                    out _,
                    out var mirrored);
            var wouldDogleg =
                TimberFramedBlockContentGripStageProofRules.WouldNormalizeDogleg(
                    geometryResolved,
                    mirrored,
                    DoglegDirectionMatches(dogleg, expectedDirection));

            session.LastCurrentPlacementCorrect = kdiCorrect;
            session.LastWouldNormalizeDogleg = wouldDogleg;
            session.LastWouldNormalizeContentSide = wouldContent;
            session.LastInspection = new TimberFramedBlockContentGripReadOnlyInspection(
                handle,
                NativeMoveCompleted: true,
                kdiCorrect,
                wouldDogleg,
                wouldContent,
                blockName,
                TimberFramedBlockContentGripStageProofRules.FormatDimensionColumnSideToken(side),
                attachment.X,
                attachment.Y,
                knee.X,
                knee.Y,
                blockPos.X,
                blockPos.Y);

            reason = "ok";
            session.LastFailingOperation = "none";
            return TimberFramedBlockContentGripReadOnlyInspectionOutcome.Success;
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

    private static void ApplyInspectionOutcome(
        TimberFramedBlockContentGripStageProofSession session,
        TimberFramedBlockContentGripReadOnlyInspectionOutcome outcome,
        string reason)
    {
        session.LastInspectionOutcome = outcome;
        session.LastInspectionReason = reason ?? string.Empty;
        switch (outcome)
        {
            case TimberFramedBlockContentGripReadOnlyInspectionOutcome.Success:
                session.InspectionSuccessCount++;
                RefreshStageDZeroExceptionReady(session);
                break;
            case TimberFramedBlockContentGripReadOnlyInspectionOutcome.TransientGeometryNotReady:
                session.InspectionTransientSkipCount++;
                break;
            case TimberFramedBlockContentGripReadOnlyInspectionOutcome.NotApplicable:
                session.InspectionNotApplicableCount++;
                break;
            case TimberFramedBlockContentGripReadOnlyInspectionOutcome.ObjectUnavailable:
            case TimberFramedBlockContentGripReadOnlyInspectionOutcome.InvalidContract:
            case TimberFramedBlockContentGripReadOnlyInspectionOutcome.Failed:
                // Counted outcomes, not exceptions — do not increment ExceptionCount.
                break;
        }
    }

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
        MarkStageDFailed(FormatStoredFailureReason(typeName, message, operation));
        LogFirstDistinctException(session, exception, operation);
        try
        {
            document.Editor.WriteMessage(
                $"\n{CommandBanner}: callback exception [{operation}] " +
                $"{typeName}: {message}");
        }
        catch (System.Exception)
        {
            // Editor may be unavailable mid-drag.
        }
    }

    private static void LogFirstDistinctException(
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
                "ak_dev_fbc_grip_readonly_exception_" + stamp + ".txt");
            var body = new StringBuilder();
            body.AppendLine("AK_DEV_FBC_GRIP_READONLY first distinct callback exception");
            body.AppendLine("Timestamp=" + stamp);
            body.AppendLine("CallbackIndex=" + session.CallbackCount.ToString(CultureInfo.InvariantCulture));
            body.AppendLine("FailingOperation=" + operation);
            body.AppendLine("ExceptionType=" + typeName);
            body.AppendLine("ExceptionMessage=" + message);
            body.AppendLine(
                "Offset=(" +
                Fmt(session.LastCallbackOffsetX) + "," +
                Fmt(session.LastCallbackOffsetY) + "," +
                Fmt(session.LastCallbackOffsetZ) + ")");
            body.AppendLine("GripIndices=" + session.LastCallbackGripIndices);
            body.AppendLine("EntityOpenState=" + session.LastEntityOpenState);
            body.AppendLine("TrackedHandle=" + session.TrackedHandle);
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
                    typeof(AutoCadFramedBlockContentGripReadonlyProofService)
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

    private static string FormatIntegerCollection(IntegerCollection indices)
    {
        if (indices is null || indices.Count == 0)
        {
            return "none";
        }

        var parts = new string[indices.Count];
        for (var i = 0; i < indices.Count; i++)
        {
            parts[i] = indices[i].ToString(CultureInfo.InvariantCulture);
        }

        return string.Join(",", parts);
    }

    private static string FormatGripDataCollection(GripDataCollection grips)
    {
        if (grips is null || grips.Count == 0)
        {
            return "grips:0";
        }

        return "grips:" + grips.Count.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class DocumentProofState
    {
        public TimberFramedBlockContentGripStageProofSession Session { get; } = new();
    }

    /// <summary>
    /// Stage D: base GetGripPoints + base MoveGripPointsAt, then read-only
    /// inspection. IsApplicable: ObjectId compare only.
    /// </summary>
    private sealed class FramedBlockContentGripReadonlyOverrule : GripOverrule
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
            AfterBaseMove(
                entity,
                offset,
                FormatIntegerCollection(indices),
                () => base.MoveGripPointsAt(entity, indices, offset));
        }

        public override void MoveGripPointsAt(
            Entity entity,
            GripDataCollection grips,
            Vector3d offset,
            MoveGripPointsFlags bitFlags)
        {
            AfterBaseMove(
                entity,
                offset,
                FormatGripDataCollection(grips),
                () => base.MoveGripPointsAt(entity, grips, offset, bitFlags));
        }

        private static void AfterBaseMove(
            Entity entity,
            Vector3d offset,
            string gripIndices,
            Action baseMove)
        {
            var document = AcApplication.DocumentManager.MdiActiveDocument;
            if (document is null || entity is not MLeader)
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

            // Outer safety catch only — any caught exception increments
            // ExceptionCount and fails the proof. Expected transients must
            // return TryInspect outcomes instead of throwing.
            try
            {
                using (session.BeginProcessing())
                {
                    session.CallbackCount++;
                    session.LastCallbackOffsetX = offset.X;
                    session.LastCallbackOffsetY = offset.Y;
                    session.LastCallbackOffsetZ = offset.Z;
                    session.LastCallbackGripIndices = gripIndices;
                    session.LastFailingOperation = "base.MoveGripPointsAt";

                    // 1 guard entered → 2 base exactly once → 3 safe inspect →
                    // 4 no nested command / GripData / writes → 5 finally releases.
                    baseMove();
                    session.BaseMoveCompletedCount++;
                    session.LastNativeMoveCompleted = true;
                    session.LastFailingOperation = "TryInspectAfterNativeGripMove";

                    if (entity is MLeader leader)
                    {
                        session.LastEntityOpenState = SnapshotEntityOpenState(leader);
                        var outcome = TryInspectAfterNativeGripMove(
                            document,
                            leader,
                            out var reason);
                        ApplyInspectionOutcome(session, outcome, reason);
                    }
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
