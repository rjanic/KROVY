#if DEBUG
using System.Globalization;
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
/// DEBUG P4B proof (full normalize GripOverrule). HARD-DISABLED pending
/// Stage A/B pass-through validation — see
/// <c>AK_DEV_FBC_GRIP_PASSTHROUGH_*</c>. Production OFF. Shared normalize
/// services retained for later re-arm; registration of the normalizing
/// overrule is gated off.
/// </summary>
internal static class AutoCadFramedBlockContentGripUndoProofService
{
    internal const string DebugRegAppName = "AK_DEV_FBC_UNDO_PROOF";
    private const string ProofLayerName = "AK_DEV_FBC_UNDO_PROOF";
    private const string CommandBanner = "AK_DEV_FBC_UNDO_PROOF";

    /// <summary>
    /// Full normalize GripOverrule must stay OFF until host pass-through proves
    /// GripOverrule selection is safe. Do not flip to true without Stage A/B PASS.
    /// Runtime flag (not const) so the gated re-arm path stays compilable.
    /// </summary>
    private static readonly bool FullNormalizeGripOverruleAllowed = false;

    private static readonly Dictionary<Document, DocumentProofState> States = new();
    private static FramedBlockContentGripUndoOverrule? _overrule;
    private static bool _overruleAdded;
    private static bool _overrulingWasEnabled;

    public static bool IsOverruleRegistered => _overruleAdded;

    public static string OverruleInstanceIdentity =>
        AutoCadFramedBlockContentGripRegistrationSnapshot.FormatInstanceIdentity(_overrule);

    public static TimberFramedBlockContentGripUndoProofSession GetOrCreateSession(
        Document document) =>
        GetOrCreateState(document).Session;

    public static void RemoveSession(Document document)
    {
        if (States.TryGetValue(document, out var state) && state.Session.ProofEnabled)
        {
            DisableProof(document, eraseEntities: false);
        }

        States.Remove(document);
        if (!States.Values.Any(s => s.Session.ProofEnabled))
        {
            ForceUnregisterOverrule();
        }
    }

    /// <summary>
    /// Unload/terminate / safety: always remove overrule; clear session arms.
    /// Also clears Stage D/E so PASSTHROUGH_SETUP (which calls this) cannot
    /// leave a normalizing/read-only overrule stacked — passthrough source
    /// stays unmodified.
    /// </summary>
    public static void ForceUnregisterAll()
    {
        foreach (var state in States.Values)
        {
            state.Session.ProofEnabled = false;
            state.Session.MarkUnregistered();
            state.Session.ForceReleaseProcessingGuard();
        }

        ForceUnregisterOverrule();
        AutoCadFramedBlockContentGripReadonlyProofService.ForceUnregisterAll();
        AutoCadFramedBlockContentGripNormalizeProofService.ForceUnregisterAll();
    }

    public static void Setup()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var editor = document.Editor;
        // Safety first: never leave a prior normalizing overrule registered.
        ForceUnregisterAll();
        AutoCadFramedBlockContentGripPassthroughProofService.ForceUnregisterAll();

        try
        {
            using var documentLock = document.LockDocument();
            var database = document.Database;
            using var transaction = database.TransactionManager.StartTransaction();

            // 1) P4A lifecycle proof/trace OFF — CommandEnded must not second-fix.
            ForceP4aLifecycleOff(document);

            // 2) Clean only old P4B-marked entities.
            var (_, erased) = EraseMarkedProofEntities(database, transaction);

            // 3) Create one representative R2 Combined (Circle, exact 90°, 1:50).
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
                created.LeaderId is not ObjectId leaderId ||
                leaderId.IsNull)
            {
                editor.WriteMessage(
                    $"\n{CommandBanner}_SETUP FAIL create: {created.DiagnosticReason}");
                transaction.Commit();
                return;
            }

            MarkProofEntity(database, transaction, leaderId);
            var leader = (MLeader)transaction.GetObject(leaderId, OpenMode.ForWrite);
            var session = GetOrCreateSession(document);
            session.ClearProofRuntime();
            session.TrackedHandle = leaderId.Handle.ToString();
            var pre = CaptureSnapshot(database, transaction, leader);
            if (pre is null || !pre.KdiCorrect)
            {
                editor.WriteMessage(
                    $"\n{CommandBanner}_SETUP FAIL: PRE snapshot not K→D→I correct " +
                    $"(kdi={pre?.KdiCorrect}). erasedOld={erased}");
                transaction.Commit();
                return;
            }

            session.PreGripSnapshot = pre;
            session.PostGripSnapshot = null;
            session.LastNormalizeChangedContentOrDogleg = false;
            transaction.Commit();

            // 5) HARD-DISABLED: do NOT register normalizing GripOverrule.
            // Never query GetGripPoints / grip inventory after (or before) register.
            EnableProof(document);
            editor.WriteMessage(
                $"\n{CommandBanner}_SETUP: marked entity handle={leaderId.Handle} " +
                $"erasedOld={erased}");
            editor.WriteMessage(
                "\nHARD-DISABLED: full GripOverrule normalize proof is OFF pending " +
                "pass-through. Overrule was NOT registered.");
            editor.WriteMessage(
                "\nNext host smoke-test ONLY: AK_DEV_FBC_GRIP_PASSTHROUGH_SETUP");
            editor.WriteMessage(
                "\nDo NOT use UNDO_PROOF_SETUP for grip selection until pass-through PASS.");
        }
        catch (System.Exception exception)
        {
            ForceUnregisterAll();
            editor.WriteMessage(
                $"\n{CommandBanner}_SETUP FAIL: {exception.Message}");
            editor.WriteMessage("\nOverrule force-unregistered in catch.");
        }
        finally
        {
            // Setup failure / hard-disable: never leave normalizing overrule armed.
            if (!FullNormalizeGripOverruleAllowed)
            {
                ForceUnregisterOverrule();
                var session = GetOrCreateSession(document);
                session.ProofEnabled = false;
                session.MarkUnregistered();
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

        editor.WriteMessage($"\n=== {CommandBanner}_STATUS ===");
        editor.WriteMessage($"\nProofEnabled={session.ProofEnabled}");
        editor.WriteMessage($"\nOverruleRegistered={session.OverruleRegistered}");
        editor.WriteMessage($"\nTrackedHandle={session.TrackedHandle}");
        editor.WriteMessage($"\nP4A.ProofEnabled={p4a.ProofEnabled}");
        editor.WriteMessage($"\nP4A.TraceEnabled={p4a.TraceEnabled}");
        editor.WriteMessage($"\nP4A.QueuedCount={p4a.QueuedCount}");
        editor.WriteMessage($"\nGuard.IsProcessing={session.IsProcessing}");
        editor.WriteMessage(
            $"\nExternalLifecycleMutations={session.ExternalLifecycleMutations}");
        editor.WriteMessage(
            $"\nExternalLifecycleQueuedCount={session.ExternalLifecycleQueuedCount}");
        editor.WriteMessage(
            $"\nLastDogleg applied={session.LastDoglegApplied} " +
            $"changed={session.LastDoglegChanged} reason={session.LastDoglegReason}");
        editor.WriteMessage(
            $"\nLastContentSide applied={session.LastContentSideApplied} " +
            $"changed={session.LastContentSideChanged} " +
            $"reason={session.LastContentSideReason}");

        using var documentLock = document.LockDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var current = TryCaptureTrackedSnapshot(document.Database, transaction, session);
        transaction.Commit();

        WriteSnapshot(editor, "PRE", session.PreGripSnapshot);
        WriteSnapshot(editor, "POST", session.PostGripSnapshot);
        WriteSnapshot(editor, "CURRENT", current);

        var state = session.ClassifyCurrent(current);
        editor.WriteMessage(
            $"\nSTATE={TimberFramedBlockContentGripUndoProofRules.FormatState(state)}");
        if (current is not null)
        {
            editor.WriteMessage(
                $"\nSameHandle={string.Equals(session.TrackedHandle, current.Handle, StringComparison.OrdinalIgnoreCase)}");
            editor.WriteMessage(
                $"\nKdiCorrect={current.KdiCorrect} DIM={current.DimensionColumnSideToken} " +
                $"block='{current.BlockContentName}'");
        }
    }

    public static void DisableProofKeepEntities()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        DisableProof(document, eraseEntities: false);
        document.Editor.WriteMessage($"\n{CommandBanner}_OFF");
    }

    public static void Clean()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        DisableProof(document, eraseEntities: false);
        using var documentLock = document.LockDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var (found, erased) = EraseMarkedProofEntities(document.Database, transaction);
        transaction.Commit();
        GetOrCreateSession(document).ClearProofRuntime();
        document.Editor.WriteMessage($"\n=== {CommandBanner}_CLEAN ===");
        document.Editor.WriteMessage($"\noldProofEntitiesFound={found}");
        document.Editor.WriteMessage($"\noldProofEntitiesErased={erased}");
    }

    internal static void NormalizeAfterNativeGrip(
        Document document,
        MLeader leader,
        TimberFramedBlockContentGripKind gripKind)
    {
        var state = GetOrCreateState(document);
        var session = state.Session;
        if (!session.ProofEnabled ||
            !TimberFramedBlockContentGripUndoProofRules.ShouldRunSharedNormalize(
                session.ProofEnabled,
                gripKind))
        {
            return;
        }

        if (session.IsProcessing)
        {
            return;
        }

        // Keep P4A from second-fixing via CommandEnded while we write.
        ForceP4aLifecycleOff(document);

        try
        {
            using (session.BeginProcessing())
            using (AutoCadFramedBlockContentStretchNormalizeLifecycleService
                       .GetOrCreateSession(document)
                       .SuppressQueue())
            {
                var database = leader.Database;
                var transaction = database.TransactionManager.TopTransaction;
                var ownsTransaction = false;
                if (transaction is null)
                {
                    transaction = database.TransactionManager.StartTransaction();
                    ownsTransaction = true;
                }

                try
                {
                    if (!leader.IsWriteEnabled)
                    {
                        leader.UpgradeOpen();
                    }

                    if (!TryIsApplicableLeader(database, transaction, leader))
                    {
                        return;
                    }

                    var dogleg =
                        AutoCadFramedBlockContentNormalizeDoglegService.TryNormalizeDogleg(
                            transaction,
                            leader);
                    session.LastDoglegApplied = dogleg.Applied;
                    session.LastDoglegChanged = dogleg.Changed;
                    session.LastDoglegReason = dogleg.Reason;

                    var contentSide =
                        AutoCadFramedBlockContentNormalizeContentSideService
                            .TryNormalizeContentSide(
                                database,
                                transaction,
                                leader);
                    session.LastContentSideApplied = contentSide.Applied;
                    session.LastContentSideChanged = contentSide.Changed;
                    session.LastContentSideReason = contentSide.Reason;
                    session.LastNormalizeChangedContentOrDogleg =
                        dogleg.Changed || contentSide.Changed;

                    var post = CaptureSnapshot(database, transaction, leader);
                    if (post is not null)
                    {
                        session.PostGripSnapshot = post;
                        session.TrackedHandle = post.Handle;
                    }

                    if (ownsTransaction)
                    {
                        transaction.Commit();
                    }
                }
                catch
                {
                    if (ownsTransaction)
                    {
                        transaction.Abort();
                    }

                    throw;
                }
            }

            // Queue must be empty after op; P4A proof stays OFF.
            var p4a = AutoCadFramedBlockContentStretchNormalizeLifecycleService
                .GetOrCreateSession(document);
            session.ExternalLifecycleQueuedCount = p4a.QueuedCount;
            if (p4a.QueuedCount > 0)
            {
                session.ExternalLifecycleMutations += p4a.QueuedCount;
            }

            p4a.ClearQueue();
            ForceP4aLifecycleOff(document);
        }
        catch (System.Exception)
        {
            session.ForceReleaseProcessingGuard();
            throw;
        }
    }

    internal static bool TryIsApplicableLeader(
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

        return TimberFramedBlockContentGripUndoProofRules.IsApplicableBlockContent(
            blockName,
            hasItemNo,
            hasWidth,
            hasHeight);
    }

    private static void EnableProof(Document document)
    {
        var session = GetOrCreateSession(document);
        // Always strip any stale registration first (idempotent; no duplicates).
        ForceUnregisterOverrule();

        if (!FullNormalizeGripOverruleAllowed)
        {
            session.ProofEnabled = false;
            session.MarkUnregistered();
            document.Editor.WriteMessage(
                $"\n{CommandBanner} HARD-DISABLED: GripOverrule full normalize " +
                "proof is disabled pending AK_DEV_FBC_GRIP_PASSTHROUGH. " +
                "Overrule NOT registered.");
            return;
        }

        // Dead path until FullNormalizeGripOverruleAllowed is re-enabled after
        // Stage A/B host PASS. Retained for Stage C–F re-arm only.
        session.ProofEnabled = true;
        _overrule ??= new FramedBlockContentGripUndoOverrule();
        if (!_overruleAdded)
        {
            _overrulingWasEnabled = Overrule.Overruling;
            Overrule.Overruling = true;
            Overrule.AddOverrule(
                RXClass.GetClass(typeof(MLeader)),
                _overrule,
                false);
            _overruleAdded = true;
            AutoCadRedoDiagService.OnOverruleRegister(
                "UnsafeUndoProof",
                OverruleInstanceIdentity,
                _overrulingWasEnabled);
        }

        session.TryRegisterOnce();
        AutoCadRedoDiagService.OnProofEnableDisable("UnsafeUndoProof", "ENABLE", true);
    }

    private static void DisableProof(Document document, bool eraseEntities)
    {
        var session = GetOrCreateSession(document);
        session.ProofEnabled = false;
        session.ForceReleaseProcessingGuard();
        ForceP4aLifecycleOff(document);
        session.MarkUnregistered();

        // Unregister globally only when no document still has proof armed.
        if (!States.Values.Any(s => s.Session.ProofEnabled))
        {
            ForceUnregisterOverrule();
        }

        if (eraseEntities)
        {
            using var documentLock = document.LockDocument();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            EraseMarkedProofEntities(document.Database, transaction);
            transaction.Commit();
        }

        // Keep PreGrip/PostGrip for STATUS after OFF; CLEAN clears via ClearProofRuntime.
        session.ExternalLifecycleQueuedCount = 0;
        AutoCadRedoDiagService.OnProofEnableDisable("UnsafeUndoProof", "DISABLE", false);
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
                "UnsafeUndoProof",
                identity,
                removed: true,
                overrulingRestoredTo: ownedWasEnabled,
                ownedWasEnabled: ownedWasEnabled);
        }

        // No static GripData / MLeader instance cache to clear — overrule holds none.
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
            AttachmentX: 12000d,
            AttachmentY: 12000d,
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

    private static TimberFramedBlockContentGripUndoProofSnapshot? TryCaptureTrackedSnapshot(
        Database database,
        Transaction transaction,
        TimberFramedBlockContentGripUndoProofSession session)
    {
        if (string.IsNullOrWhiteSpace(session.TrackedHandle))
        {
            return null;
        }

        if (!TryFindHandle(database, transaction, session.TrackedHandle, out var id) ||
            transaction.GetObject(id, OpenMode.ForRead, true) is not MLeader leader ||
            leader.IsErased)
        {
            return null;
        }

        return CaptureSnapshot(database, transaction, leader);
    }

    private static TimberFramedBlockContentGripUndoProofSnapshot? CaptureSnapshot(
        Database database,
        Transaction transaction,
        MLeader leader)
    {
        if (!TryReadBlockNameAndCombinedAttrs(
                database,
                transaction,
                leader,
                out var blockName,
                out _,
                out _,
                out _))
        {
            return null;
        }

        var attachment = ReadAttachment(leader);
        var knee = ReadKnee(leader);
        var blockPos = leader.BlockPosition;
        var dogleg = TryReadDogleg(leader) ?? new Vector3d(0, 0, 0);
        TimberFramedBlockContentDimensionColumnSide? side = null;
        if (TimberFramedBlockContentVariantRules.TryParseR2VariantKey(
                blockName,
                out var parse))
        {
            side = parse.DimensionColumnSide;
        }

        var kdiCorrect = false;
        if (AutoCadFramedBlockContentDimensionColumnPlacementService.TryEvaluate(
                transaction,
                leader,
                out var evaluation,
                out _,
                out _))
        {
            kdiCorrect = evaluation.Current.IsCorrect;
        }

        TryReadAttr(
            transaction,
            leader,
            TimberFramedBlockContentDefinitionRules.ItemNoTag,
            out var itemText,
            out var itemHeight);
        TryReadAttr(
            transaction,
            leader,
            TimberFramedBlockContentDefinitionRules.WidthTag,
            out var widthText,
            out var widthHeight);
        TryReadAttr(
            transaction,
            leader,
            TimberFramedBlockContentDefinitionRules.HeightTag,
            out var heightText,
            out var heightHeight);

        return new TimberFramedBlockContentGripUndoProofSnapshot(
            leader.ObjectId.Handle.ToString(),
            attachment.X,
            attachment.Y,
            knee.X,
            knee.Y,
            blockPos.X,
            blockPos.Y,
            dogleg.X,
            dogleg.Y,
            leader.DoglegLength,
            blockName,
            TimberFramedBlockContentGripUndoProofRules.FormatDimensionColumnSideToken(side),
            kdiCorrect,
            itemText,
            widthText,
            heightText,
            itemHeight,
            widthHeight,
            heightHeight);
    }

    private static void WriteSnapshot(
        Editor editor,
        string label,
        TimberFramedBlockContentGripUndoProofSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            editor.WriteMessage($"\n{label}=null");
            return;
        }

        editor.WriteMessage(
            $"\n{label}: handle={snapshot.Handle} " +
            $"att=({Fmt(snapshot.AttachmentX)},{Fmt(snapshot.AttachmentY)}) " +
            $"knee=({Fmt(snapshot.KneeX)},{Fmt(snapshot.KneeY)}) " +
            $"bp=({Fmt(snapshot.BlockPositionX)},{Fmt(snapshot.BlockPositionY)}) " +
            $"dogleg=({Fmt(snapshot.DoglegDirectionX)},{Fmt(snapshot.DoglegDirectionY)}) " +
            $"len={Fmt(snapshot.DoglegLength)} " +
            $"block='{snapshot.BlockContentName}' DIM={snapshot.DimensionColumnSideToken} " +
            $"kdi={snapshot.KdiCorrect} " +
            $"ITEM='{snapshot.ItemNoText}'/{Fmt(snapshot.ItemNoHeight)} " +
            $"WIDTH='{snapshot.WidthText}'/{Fmt(snapshot.WidthHeight)} " +
            $"HEIGHT='{snapshot.HeightText}'/{Fmt(snapshot.HeightHeight)}");
    }

    private static string Fmt(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

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

    private static bool TryReadAttr(
        Transaction transaction,
        MLeader leader,
        string tag,
        out string text,
        out double height)
    {
        text = string.Empty;
        height = 0d;
        var blockId = leader.BlockContentId;
        if (blockId.IsNull ||
            transaction.GetObject(blockId, OpenMode.ForRead, true) is not
                BlockTableRecord block)
        {
            return false;
        }

        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, true) is not
                    AttributeDefinition definition ||
                definition.IsErased ||
                !string.Equals(definition.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var attribute = leader.GetBlockAttribute(definition.ObjectId);
            if (attribute is null)
            {
                return false;
            }

            text = attribute.TextString ?? string.Empty;
            height = attribute.Height;
            return true;
        }

        return false;
    }

    private static Point3d ReadAttachment(MLeader leader) =>
        leader.GetFirstVertex(GetPrimaryLeaderLineIndex(leader));

    private static Point3d ReadKnee(MLeader leader) =>
        leader.GetLastVertex(GetPrimaryLeaderLineIndex(leader));

    private static int GetPrimaryLeaderLineIndex(MLeader leader)
    {
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        if (leaderIndexes.Length == 0)
        {
            throw new InvalidOperationException("MLeader has no leaders.");
        }

        var lineIndexes = leader.GetLeaderLineIndexes(leaderIndexes[0]).Cast<int>().ToArray();
        if (lineIndexes.Length == 0)
        {
            throw new InvalidOperationException("MLeader has no leader lines.");
        }

        return lineIndexes[0];
    }

    private static Vector3d? TryReadDogleg(MLeader leader)
    {
        try
        {
            var indexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
            return indexes.Length == 0 ? null : leader.GetDogleg(indexes[0]);
        }
        catch (AcadException)
        {
            return null;
        }
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
                $"{TimberFramedBlockContentGripUndoProofRules.DebugMarkerToken}|" +
                $"{TimberFramedBlockContentGripUndoProofRules.RepresentativeCaseKey}"));
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
                payload.StartsWith(
                    TimberFramedBlockContentGripUndoProofRules.DebugMarkerToken,
                    StringComparison.OrdinalIgnoreCase))
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
        public TimberFramedBlockContentGripUndoProofSession Session { get; } = new();
    }

    /// <summary>
    /// HARD-DISABLED registration path. Retained for Stage C–F after
    /// pass-through PASS. Suspected crash contributors (do not re-arm as-is):
    /// IsApplicable opened BTR / AttrDefs via TopTransaction; soft filter
    /// applied to ALL BlockContent; GetGripPoints flipped global Overruling
    /// and called entity.GetGripPoints during selection.
    /// Host NO-GO: applicable GripOverrule without GripData forward = empty grips.
    /// </summary>
    private sealed class FramedBlockContentGripUndoOverrule : GripOverrule
    {
        public override bool IsApplicable(RXObject overruledSubject)
        {
            // Defense: never applicable while full normalize overrule is gated off.
            if (!FullNormalizeGripOverruleAllowed)
            {
                return false;
            }

            if (overruledSubject is not MLeader leader || leader.IsErased)
            {
                return false;
            }

            var document = AcApplication.DocumentManager.MdiActiveDocument;
            if (document is null)
            {
                return false;
            }

            var session = GetOrCreateSession(document);
            if (!session.ProofEnabled)
            {
                return false;
            }

            // Side-effect-free soft filter only — no TopTransaction / BTR / AttrDef.
            // (Previous TryIsApplicableLeader-in-IsApplicable was a crash suspect.)
            return leader.ContentType == ContentType.BlockContent &&
                !leader.BlockContentId.IsNull;
        }

        /// <summary>
        /// Classic grip path: forward native points only (no custom set).
        /// </summary>
        public override void GetGripPoints(
            Entity entity,
            Point3dCollection gripPoints,
            IntegerCollection osnapModes,
            IntegerCollection geomIds)
        {
            AppendNativeGripPoints(entity, gripPoints, osnapModes, geomIds);
        }

        /// <summary>
        /// MLeader uses GripData. Must forward native grips — GripOverrule
        /// default leaves the collection empty when IsApplicable is true.
        /// Never replace with custom GripData. Never cache/clone GripData.
        /// </summary>
        public override void GetGripPoints(
            Entity entity,
            GripDataCollection grips,
            double curViewUnitSize,
            int gripSize,
            Vector3d curViewDir,
            GetGripPointsFlags bitFlags)
        {
            AppendNativeGripData(
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
            var before = CaptureBeforeGrip(entity);
            base.MoveGripPointsAt(entity, indices, offset);
            AfterNativeGripMove(entity, before);
        }

        public override void MoveGripPointsAt(
            Entity entity,
            GripDataCollection grips,
            Vector3d offset,
            MoveGripPointsFlags bitFlags)
        {
            var before = CaptureBeforeGrip(entity);
            base.MoveGripPointsAt(entity, grips, offset, bitFlags);
            AfterNativeGripMove(entity, before);
        }

        private static void AppendNativeGripPoints(
            Entity entity,
            Point3dCollection gripPoints,
            IntegerCollection osnapModes,
            IntegerCollection geomIds)
        {
            // CRASH-SUSPECT pattern retained only behind FullNormalizeGripOverruleAllowed.
            // Prefer base.GetGripPoints (see pass-through) — do not re-arm until proven.
            var previous = Overruling;
            Overruling = false;
            try
            {
                entity.GetGripPoints(gripPoints, osnapModes, geomIds);
            }
            finally
            {
                Overruling = previous;
            }
        }

        private static void AppendNativeGripData(
            Entity entity,
            GripDataCollection grips,
            double curViewUnitSize,
            int gripSize,
            Vector3d curViewDir,
            GetGripPointsFlags bitFlags)
        {
            // CRASH-SUSPECT: global Overruling flip during selection GetGripPoints.
            var previous = Overruling;
            Overruling = false;
            try
            {
                entity.GetGripPoints(
                    grips,
                    curViewUnitSize,
                    gripSize,
                    curViewDir,
                    bitFlags);
            }
            finally
            {
                Overruling = previous;
            }
        }

        private static (
            Point3d? Attachment,
            Point3d? Knee,
            Point3d? BlockPosition)? CaptureBeforeGrip(Entity entity)
        {
            if (entity is not MLeader leader)
            {
                return null;
            }

            try
            {
                return (
                    ReadAttachment(leader),
                    ReadKnee(leader),
                    leader.BlockPosition);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static void AfterNativeGripMove(
            Entity entity,
            (Point3d? Attachment, Point3d? Knee, Point3d? BlockPosition)? before)
        {
            var document = AcApplication.DocumentManager.MdiActiveDocument;
            if (document is null || entity is not MLeader leader)
            {
                return;
            }

            var session = GetOrCreateSession(document);
            if (!session.ProofEnabled || session.IsProcessing)
            {
                return;
            }

            var gripKind = ClassifyAfterMove(leader, before);
            try
            {
                NormalizeAfterNativeGrip(document, leader, gripKind);
            }
            catch (System.Exception exception)
            {
                session.ForceReleaseProcessingGuard();
                document.Editor.WriteMessage(
                    $"\n{CommandBanner}: normalize skipped: {exception.Message}");
            }
        }

        private static TimberFramedBlockContentGripKind ClassifyAfterMove(
            MLeader leader,
            (Point3d? Attachment, Point3d? Knee, Point3d? BlockPosition)? before)
        {
            if (before is not { } snap ||
                snap.Attachment is not Point3d att0 ||
                snap.Knee is not Point3d knee0 ||
                snap.BlockPosition is not Point3d bp0)
            {
                return TimberFramedBlockContentGripKind.Unknown;
            }

            try
            {
                var att1 = ReadAttachment(leader);
                var knee1 = ReadKnee(leader);
                var bp1 = leader.BlockPosition;
                var dAtt = att1 - att0;
                var dKnee = knee1 - knee0;
                var dBp = bp1 - bp0;
                var tol = TimberFramedBlockContentGripUndoProofRules
                    .GeometryMatchToleranceMm;
                return TimberFramedBlockContentGripUndoProofRules.ClassifyGripKind(
                    dAtt.Length > tol,
                    dKnee.Length > tol,
                    dBp.Length > tol,
                    (dAtt - dKnee).Length <= tol,
                    (dKnee - dBp).Length <= tol);
            }
            catch (System.Exception)
            {
                return TimberFramedBlockContentGripKind.Unknown;
            }
        }
    }
}
#endif
