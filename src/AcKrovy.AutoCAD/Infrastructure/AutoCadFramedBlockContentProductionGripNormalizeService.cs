using System.Runtime.CompilerServices;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Production GripOverrule for G5 Combined BlockContent annotations.
/// R3: native MoveGripPointsAt (leader geometry authority) → sync layer-C
/// presentation from FINAL knee→frame landing via Decide() → swap
/// R3_RIGHT↔R3_LEFT only when final knee-side crossed — no dogleg rewrite,
/// no forced 60°, no K→D→I normalize, no leader vertex rewrite. Legacy R2:
/// base.MoveGripPointsAt → dogleg → content-side when K→D→I wrong.
/// Registered once on plug-in init; independent of DEBUG proof commands.
/// </summary>
internal static class AutoCadFramedBlockContentProductionGripNormalizeService
{
    private static readonly object Gate = new();
    private static readonly TimberFramedBlockContentProductionGripNormalizeSession Session =
        new();

    private static FramedBlockContentProductionGripNormalizeOverrule? _overrule;
    private static bool _overruleAdded;
    private static bool _overrulingWasEnabled;
    private static bool _ownsOverrulingEnable;

    public static bool IsOverruleRegistered
    {
        get
        {
            lock (Gate)
            {
                return _overruleAdded;
            }
        }
    }

    public static bool GuardIsProcessing => Session.IsProcessing;

    public static TimberFramedBlockContentProductionGripNormalizeSession Diagnostics =>
        Session;

    public static string OverruleInstanceIdentity
    {
        get
        {
            var instance = _overrule;
            return instance is null
                ? "null"
                : $"{instance.GetType().Name}#{RuntimeHelpers.GetHashCode(instance)}";
        }
    }

    /// <summary>
    /// Idempotent: registers the production GripOverrule exactly once.
    /// Safe to call from Initialize repeatedly.
    /// </summary>
    public static void RegisterOnce()
    {
        lock (Gate)
        {
            if (_overruleAdded)
            {
                return;
            }

            _overrule ??= new FramedBlockContentProductionGripNormalizeOverrule();
            _overrulingWasEnabled = Overrule.Overruling;
            if (!Overrule.Overruling)
            {
                Overrule.Overruling = true;
                _ownsOverrulingEnable = true;
            }
            else
            {
                _ownsOverrulingEnable = false;
            }

            Overrule.AddOverrule(
                RXClass.GetClass(typeof(MLeader)),
                _overrule,
                false);
            _overruleAdded = true;
            if (!Session.OverruleRegistered)
            {
                Session.MarkRegistered();
            }
        }
    }

    /// <summary>
    /// Idempotent unregister for Terminate/Dispose. Restores Overruling only when
    /// this service owned enabling it and no other KROVY overrule still needs it.
    /// </summary>
    public static void Unregister()
    {
        lock (Gate)
        {
            ForceUnregisterOverruleLocked();
        }
    }

    /// <summary>
    /// Terminate safety: always remove production overrule.
    /// </summary>
    public static void ForceUnregisterAll() => Unregister();

    /// <summary>
    /// Document teardown: release reentrancy guard only — keep overrule registered.
    /// </summary>
    public static void ForceReleaseProcessingGuard()
    {
        lock (Gate)
        {
            Session.ForceReleaseProcessingGuard();
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
        editor.WriteMessage("\n=== AK_DEV_FBC_PRODUCTION_GRIP_STATUS ===");
        editor.WriteMessage($"\nProductionOverruleRegistered={IsOverruleRegistered}");
        editor.WriteMessage(
            $"\nApplicableProcessedCount={Session.ApplicableProcessedCount}");
        editor.WriteMessage($"\nIgnoredForeignCount={Session.IgnoredForeignCount}");
        editor.WriteMessage($"\nNormalizeChangedCount={Session.NormalizeChangedCount}");
        editor.WriteMessage($"\nNormalizeNoOpCount={Session.NormalizeNoOpCount}");
        editor.WriteMessage($"\nTransientSkipCount={Session.TransientSkipCount}");
        editor.WriteMessage($"\nExceptionCount={Session.ExceptionCount}");
        editor.WriteMessage($"\nLastHandle={Session.LastHandle}");
        editor.WriteMessage(
            $"\nLastOutcome=" +
            (Session.LastOutcome is TimberFramedBlockContentGripNormalizeOutcome outcome
                ? TimberFramedBlockContentProductionGripNormalizeRules.FormatNormalizeOutcome(
                    outcome)
                : "(none)"));
        editor.WriteMessage($"\nLastReason={Session.LastReason}");
        editor.WriteMessage($"\nGuardIsProcessing={Session.IsProcessing}");
        editor.WriteMessage($"\nRegisterCount={Session.RegisterCount}");
        editor.WriteMessage($"\nUnregisterCount={Session.UnregisterCount}");
        editor.WriteMessage($"\nOverrule.Overruling={Overrule.Overruling}");
        editor.WriteMessage($"\nOwnsOverrulingEnable={_ownsOverrulingEnable}");
        editor.WriteMessage($"\nOverruleInstanceIdentity={OverruleInstanceIdentity}");
    }

    private static void ForceUnregisterOverruleLocked()
    {
        if (_overruleAdded && _overrule is not null)
        {
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
            if (_ownsOverrulingEnable &&
                !_overrulingWasEnabled &&
                !OtherKrovyOverruleStillRegistered())
            {
                Overrule.Overruling = false;
            }

            _ownsOverrulingEnable = false;
        }

        Session.MarkUnregistered();
        Session.ForceReleaseProcessingGuard();
    }

    private static bool OtherKrovyOverruleStillRegistered()
    {
#if DEBUG
        return AutoCadFramedBlockContentGripNormalizeProofService.IsOverruleRegistered ||
            AutoCadFramedBlockContentGripPassthroughProofService.IsOverruleRegistered ||
            AutoCadFramedBlockContentGripReadonlyProofService.IsOverruleRegistered ||
            AutoCadFramedBlockContentGripUndoProofService.IsOverruleRegistered;
#else
        return false;
#endif
    }

    /// <summary>
    /// After base.MoveGripPointsAt inspect write-open callback MLeader.
    /// R3 Combined: sync content presentation to final landing, then
    /// content-variant swap only (RIGHT↔LEFT) from final knee/frame.
    /// Legacy R2: dogleg → content-side when K→D→I wrong.
    /// </summary>
    private static TimberFramedBlockContentGripNormalizeOutcome
        TryNormalizeAfterNativeMove(
            Document document,
            MLeader writeOpenLeader,
            out string reason)
    {
        reason = string.Empty;

        ForceP4aLifecycleOff(document);

        if (writeOpenLeader.IsDisposed)
        {
            reason = "callback MLeader disposed";
            return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
        }

        if (writeOpenLeader.IsErased)
        {
            reason = "callback MLeader erased";
            return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
        }

        var database = writeOpenLeader.Database ?? document.Database;
        if (database is null || database.IsDisposed)
        {
            reason = "Database null/disposed";
            return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
        }

        var handle = writeOpenLeader.ObjectId.IsNull
            ? Session.LastHandle
            : writeOpenLeader.ObjectId.Handle.ToString();

        if (writeOpenLeader.ContentType != ContentType.BlockContent)
        {
            reason = "not BlockContent";
            return TimberFramedBlockContentGripNormalizeOutcome.NotApplicable;
        }

        if (writeOpenLeader.BlockContentId.IsNull)
        {
            reason = "BlockContentId Null (transient)";
            return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
        }

        if (HasAnyDebugProofMarker(writeOpenLeader))
        {
            reason = "DEBUG proof marker (yield)";
            return TimberFramedBlockContentGripNormalizeOutcome.NotApplicable;
        }

#if DEBUG
        if (IsDebugProofExclusiveOwner(writeOpenLeader))
        {
            reason = "DEBUG proof exclusive owner";
            return TimberFramedBlockContentGripNormalizeOutcome.NotApplicable;
        }
#endif

        using (BeginP4aSuppressQueue(document))
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
                if (!TryIsProductionApplicableLeader(
                        database,
                        transaction,
                        writeOpenLeader))
                {
                    reason = "not production G5 Combined applicable";
                    return TimberFramedBlockContentGripNormalizeOutcome.NotApplicable;
                }

                if (!TryReadBlockNameAndCombinedAttrs(
                        database,
                        transaction,
                        writeOpenLeader,
                        out var blockName,
                        out _,
                        out _,
                        out _))
                {
                    reason = "unable to read production BTR attrs";
                    return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
                }

                if (TimberFramedBlockContentProductionGripNormalizeRules
                        .IsR3ContentVariantOnlyPath(blockName))
                {
                    return TryNormalizeR3ContentVariantOnly(
                        database,
                        transaction,
                        writeOpenLeader,
                        out reason);
                }

                return TryNormalizeLegacyR2Full(
                    database,
                    transaction,
                    writeOpenLeader,
                    out reason);
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

    /// <summary>
    /// R3 production: native grip already applied (leader geometry authority —
    /// no vertex rewrite here). Sync layer-C presentation from FINAL
    /// knee→frame landing via Decide(), then swap R3_RIGHT/LEFT when final
    /// knee-side crossed on that content axis. Never dogleg-normalize, never
    /// force 60°, never restore pre-grip CREATE presentation against a rotated
    /// landing.
    /// </summary>
    private static TimberFramedBlockContentGripNormalizeOutcome
        TryNormalizeR3ContentVariantOnly(
            Database database,
            Transaction transaction,
            MLeader writeOpenLeader,
            out string reason)
    {
        reason = string.Empty;
        if (!TryResolveSourceEndpoints(
                database,
                transaction,
                writeOpenLeader,
                out var start,
                out var end,
                out var sourceNote))
        {
            reason = sourceNote;
            return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
        }

        if (!TryReadR3SwapContext(
                database,
                transaction,
                writeOpenLeader,
                out var context,
                out var contextNote))
        {
            if (IsTransientEvaluateNote(contextNote))
            {
                reason = contextNote;
                return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
            }

            reason = contextNote;
            return TimberFramedBlockContentGripNormalizeOutcome.Failed;
        }

        var beforeAttachment = ReadLeaderAttachment(writeOpenLeader);
        var beforeKnee = ReadLeaderKnee(writeOpenLeader);
        var beforeBlockPosition = writeOpenLeader.BlockPosition;

        // Authority: final post-stretch landing (knee→BlockPosition). Derive
        // readable presentation BEFORE side classify so local +X matches the
        // leader second segment; re-assert AFTER optional BTR swap.
        var syncedPresentation = TrySyncPresentationFromFinalLanding(
            transaction,
            writeOpenLeader);

        bool changed;
        ObjectId afterBlockId;
        try
        {
            if (!AutoCadFramedBlockContentDimensionColumnPlacementService
                    .EnsureCorrectR3ContentVariantFromFinalGeometry(
                        database,
                        transaction,
                        writeOpenLeader,
                        start.X,
                        start.Y,
                        end.X,
                        end.Y,
                        context.ContentKind,
                        context.ItemTextStyleName,
                        context.DimensionTextStyleName,
                        context.ItemTextStyleId,
                        context.DimensionTextStyleId,
                        context.ItemPaperHeightMm,
                        context.DimensionPaperHeightMm,
                        context.ItemTextForFrameSizing,
                        context.AttributeValues,
                        out changed,
                        out _,
                        out afterBlockId,
                        out reason,
                        effectiveContentWorldAngleRadians:
                            syncedPresentation?.FinalWorldPresentationAngle))
            {
                if (IsTransientNormalizeReason(reason))
                {
                    return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
                }

                return TimberFramedBlockContentGripNormalizeOutcome.Failed;
            }
        }
        catch (AcadException exception) when (
            IsTransientAcadStatus(exception.ErrorStatus))
        {
            reason = "R3 content-variant:" + exception.ErrorStatus;
            return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
        }
        catch (InvalidOperationException exception)
        {
            reason = "R3 content-variant geometry unstable:" + exception.Message;
            return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
        }

        if (syncedPresentation is R3FinalContentPresentationDecision presentationAfter)
        {
            AutoCadFramedBlockContentDimensionColumnPlacementService
                .PreserveBlockContentPresentationRotation(
                    transaction,
                    writeOpenLeader,
                    presentationAfter.TargetBlockRotation);
        }
        else
        {
            TrySyncPresentationFromFinalLanding(transaction, writeOpenLeader);
        }

        if (changed)
        {
            // Leader vertices must remain identical after BTR swap.
            var afterAttachment = ReadLeaderAttachment(writeOpenLeader);
            var afterKnee = ReadLeaderKnee(writeOpenLeader);
            if (beforeAttachment.DistanceTo(afterAttachment) > 1e-6d ||
                beforeKnee.DistanceTo(afterKnee) > 1e-6d)
            {
                reason =
                    "R3 swap drifted leader vertices " +
                    $"(att={beforeAttachment.DistanceTo(afterAttachment):R}, " +
                    $"knee={beforeKnee.DistanceTo(afterKnee):R})";
                return TimberFramedBlockContentGripNormalizeOutcome.Failed;
            }

            _ = afterBlockId;
            _ = beforeBlockPosition;
            return TimberFramedBlockContentGripNormalizeOutcome.SuccessChanged;
        }

        return TimberFramedBlockContentGripNormalizeOutcome.SuccessNoOp;
    }

    /// <summary>
    /// Measure the live world content axis (including CREATE TransformBy), then
    /// install Decide(final knee→frame) as a relative BlockRotation correction.
    /// Returns both world and BlockRotation-space angles when geometry is stable.
    /// </summary>
    private static R3FinalContentPresentationDecision?
        TrySyncPresentationFromFinalLanding(
        Transaction transaction,
        MLeader leader)
    {
        var knee = ReadLeaderKnee(leader);
        var frame = leader.BlockPosition;
        if (!TimberFramedBlockContentGripPresentationRules
                .TryResolveLandingPhysicalAngleRadians(
                    knee.X,
                    knee.Y,
                    frame.X,
                    frame.Y,
                    out var landingPhysical) ||
            !AutoCadFramedBlockContentDimensionColumnPlacementService
                .TryResolveWorldContentXAxis(
                    transaction,
                    leader,
                    out var currentWorldPresentation,
                    out _))
        {
            return null;
        }

        var preserveAdoptedReferenceVerticalFamily =
            ElementLabelStore.TryRead(leader, out var labelData) &&
            (labelData?.R3ReferencePresentationRevision ?? 0) >=
                TimberFramedBlockContentReadableOrientationRules
                    .ReferencePresentationRevision;
        var presentation =
            TimberFramedBlockContentGripPresentationRules
                .ResolveFinalContentPresentation(
                    currentWorldPresentation,
                    leader.BlockRotation,
                    landingPhysical,
                    preserveAdoptedReferenceVerticalFamily);

        AutoCadFramedBlockContentDimensionColumnPlacementService
            .PreserveBlockContentPresentationRotation(
                transaction,
                leader,
                presentation.TargetBlockRotation);
        return presentation;
    }

#if DEBUG
    internal static TimberFramedBlockContentGripNormalizeOutcome
        TryNormalizeR3ContentVariantOnlyForAutotest(
            Database database,
            Transaction transaction,
            MLeader leader,
            out string reason) =>
        TryNormalizeR3ContentVariantOnly(
            database,
            transaction,
            leader,
            out reason);
#endif

    private static TimberFramedBlockContentGripNormalizeOutcome
        TryNormalizeLegacyR2Full(
            Database database,
            Transaction transaction,
            MLeader writeOpenLeader,
            out string reason)
    {
        reason = string.Empty;
        bool placementCorrect;
        try
        {
            if (!AutoCadFramedBlockContentDimensionColumnPlacementService.TryEvaluate(
                    transaction,
                    writeOpenLeader,
                    out var evaluation,
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
            return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
        }

        if (placementCorrect)
        {
            reason = "K→D→I already correct";
            return TimberFramedBlockContentGripNormalizeOutcome.SuccessNoOp;
        }

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

        AutoCadFramedBlockContentNormalizeResult contentSide;
        try
        {
            contentSide =
                AutoCadFramedBlockContentNormalizeContentSideService
                    .TryNormalizeContentSide(
                        writeOpenLeader,
                        database,
                        preferredOppositeBlockContentId: ObjectId.Null);
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

        try
        {
            _ = AutoCadFramedBlockContentDimensionColumnPlacementService.TryEvaluate(
                transaction,
                writeOpenLeader,
                out _,
                out _,
                out _);
        }
        catch (AcadException exception) when (
            IsTransientAcadStatus(exception.ErrorStatus))
        {
            reason = "post-verify:" + exception.ErrorStatus;
            return TimberFramedBlockContentGripNormalizeOutcome.TransientSkip;
        }

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

    private static bool TryResolveSourceEndpoints(
        Database database,
        Transaction transaction,
        MLeader leader,
        out Point3d start,
        out Point3d end,
        out string note)
    {
        start = Point3d.Origin;
        end = Point3d.Origin;
        note = string.Empty;
        if (!ElementLabelStore.TryRead(leader, out var data) ||
            data is null ||
            string.IsNullOrWhiteSpace(data.SourceHandle))
        {
            note = "R3 grip: missing SourceHandle";
            return false;
        }

        if (!TryParseHandleObjectId(database, data.SourceHandle, out var sourceId) ||
            sourceId.IsNull)
        {
            note = "R3 grip: SourceHandle unparsable";
            return false;
        }

        if (transaction.GetObject(sourceId, OpenMode.ForRead, true) is not Entity source ||
            source.IsErased)
        {
            note = "R3 grip: source entity missing";
            return false;
        }

        switch (source)
        {
            case Line line:
                start = line.StartPoint;
                end = line.EndPoint;
                return true;
            case Polyline polyline:
                start = polyline.StartPoint;
                end = polyline.EndPoint;
                return true;
            default:
                note = "R3 grip: unsupported source entity";
                return false;
        }
    }

    private static bool TryParseHandleObjectId(
        Database database,
        string handleText,
        out ObjectId objectId)
    {
        objectId = ObjectId.Null;
        if (string.IsNullOrWhiteSpace(handleText))
        {
            return false;
        }

        try
        {
            var hex = handleText.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hex = hex[2..];
            }

            if (!long.TryParse(
                    hex,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                return false;
            }

            objectId = database.GetObjectId(false, new Handle(value), 0);
            return !objectId.IsNull;
        }
        catch (System.Exception)
        {
            objectId = ObjectId.Null;
            return false;
        }
    }

    private static bool TryReadR3SwapContext(
        Database database,
        Transaction transaction,
        MLeader leader,
        out R3SwapContext context,
        out string note)
    {
        context = default!;
        note = string.Empty;
        _ = database;
        var blockId = leader.BlockContentId;
        if (blockId.IsNull)
        {
            note = "BlockContentId Null.";
            return false;
        }

        if (transaction.GetObject(blockId, OpenMode.ForRead, true) is not
                BlockTableRecord block ||
            block.IsErased)
        {
            note = "BlockTableRecord unavailable.";
            return false;
        }

        AttributeDefinition? itemDef = null;
        AttributeDefinition? widthDef = null;
        AttributeDefinition? heightDef = null;
        Entity? frame = null;
        foreach (ObjectId id in block)
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
                else if (string.Equals(
                             attribute.Tag,
                             TimberFramedBlockContentDefinitionRules.HeightTag,
                             StringComparison.OrdinalIgnoreCase))
                {
                    heightDef = attribute;
                }
            }
            else
            {
                frame = entity;
            }
        }

        if (itemDef is null || widthDef is null || heightDef is null)
        {
            note = "Combined BTR must expose ITEM_NO/WIDTH/HEIGHT AttrDefs.";
            return false;
        }

        if (!TryResolveContentKind(frame, out var contentKind, out note))
        {
            return false;
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

        var values = new List<(string Tag, string Text, double Height)>();
        foreach (var definition in new[] { itemDef, widthDef, heightDef })
        {
            using var attribute = leader.GetBlockAttribute(definition.ObjectId);
            if (attribute is null)
            {
                note = $"Missing AttrRef for {definition.Tag}.";
                return false;
            }

            values.Add((
                definition.Tag.ToUpperInvariant(),
                attribute.TextString ?? string.Empty,
                attribute.Height));
        }

        var itemText = values
            .First(v => v.Tag == TimberFramedBlockContentDefinitionRules.ItemNoTag)
            .Text;
        context = new R3SwapContext(
            contentKind,
            itemStyleName,
            dimStyleName,
            itemDef.TextStyleId,
            widthDef.TextStyleId,
            itemPaper,
            dimPaper,
            itemText,
            values);
        return true;
    }

    private static bool TryResolveContentKind(
        Entity? frame,
        out TimberFramedBlockContentKind kind,
        out string note)
    {
        kind = default;
        note = string.Empty;
        if (frame is null)
        {
            note = "Combined BTR missing frame/connection entity.";
            return false;
        }

        if (frame is DBPoint)
        {
            kind = TimberFramedBlockContentKind.Plain;
            return true;
        }

        if (frame is Circle)
        {
            kind = TimberFramedBlockContentKind.Circle;
            return true;
        }

        if (frame is Polyline polyline &&
            polyline.Closed &&
            polyline.NumberOfVertices == 4)
        {
            var hasBulge = Enumerable.Range(0, 4).Any(i =>
                Math.Abs(polyline.GetBulgeAt(i)) >
                TimberFramedBlockContentDefinitionRules.AttributeTolerance);
            kind = hasBulge
                ? TimberFramedBlockContentKind.Slot
                : TimberFramedBlockContentKind.Rectangle;
            return true;
        }

        note = "Unable to classify Combined frame geometry kind.";
        return false;
    }

    private static Point3d ReadLeaderAttachment(MLeader leader)
    {
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        var lineIndexes = leader.GetLeaderLineIndexes(leaderIndexes[0]).Cast<int>().ToArray();
        return leader.GetFirstVertex(lineIndexes[0]);
    }

    private static Point3d ReadLeaderKnee(MLeader leader)
    {
        var leaderIndexes = leader.GetLeaderIndexes().Cast<int>().ToArray();
        var lineIndexes = leader.GetLeaderLineIndexes(leaderIndexes[0]).Cast<int>().ToArray();
        return leader.GetLastVertex(lineIndexes[0]);
    }

    private sealed record R3SwapContext(
        TimberFramedBlockContentKind ContentKind,
        string ItemTextStyleName,
        string DimensionTextStyleName,
        ObjectId ItemTextStyleId,
        ObjectId DimensionTextStyleId,
        double ItemPaperHeightMm,
        double DimensionPaperHeightMm,
        string ItemTextForFrameSizing,
        IReadOnlyList<(string Tag, string Text, double Height)> AttributeValues);

    private static bool TryIsProductionApplicableLeader(
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

        return TimberFramedBlockContentProductionGripNormalizeRules
            .IsProductionApplicableBlockContent(
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

    private static bool HasAnyDebugProofMarker(Entity entity)
    {
        foreach (var regApp in TimberFramedBlockContentProductionGripNormalizeRules
                     .DebugProofRegAppNames)
        {
            using var buffer = entity.GetXDataForApplication(regApp);
            if (buffer is null)
            {
                continue;
            }

            foreach (var value in buffer)
            {
                if (value.TypeCode == (int)DxfCode.ExtendedDataAsciiString &&
                    Convert.ToString(value.Value) is string payload &&
                    TimberFramedBlockContentProductionGripNormalizeRules
                        .IsDebugProofMarkerToken(payload))
                {
                    return true;
                }
            }

            // RegApp present without token still counts as DEBUG ownership.
            return true;
        }

        return false;
    }

#if DEBUG
    private static bool IsDebugProofExclusiveOwner(Entity entity)
    {
        var debugArmed =
            AutoCadFramedBlockContentGripNormalizeProofService.IsOverruleRegistered ||
            AutoCadFramedBlockContentGripPassthroughProofService.IsOverruleRegistered ||
            AutoCadFramedBlockContentGripReadonlyProofService.IsOverruleRegistered ||
            AutoCadFramedBlockContentGripUndoProofService.IsOverruleRegistered;
        if (!debugArmed)
        {
            return false;
        }

        return HasAnyDebugProofMarker(entity);
    }
#endif

    private static void ForceP4aLifecycleOff(Document document)
    {
#if DEBUG
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
#else
        _ = document;
#endif
    }

    private static IDisposable BeginP4aSuppressQueue(Document document)
    {
#if DEBUG
        return AutoCadFramedBlockContentStretchNormalizeLifecycleService
            .GetOrCreateSession(document)
            .SuppressQueue();
#else
        _ = document;
        return NoopDisposable.Instance;
#endif
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

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Production: base grips + Stage E normalize on write-open callback MLeader.
    /// IsApplicable is fail-safe (no StartTransaction). Full BTR contract is
    /// evaluated after base move.
    /// </summary>
    private sealed class FramedBlockContentProductionGripNormalizeOverrule : GripOverrule
    {
        public override bool IsApplicable(RXObject overruledSubject)
        {
            if (!_overruleAdded)
            {
                return false;
            }

            if (overruledSubject is not MLeader leader || leader.IsErased)
            {
                return false;
            }

            if (leader.ContentType != ContentType.BlockContent ||
                leader.BlockContentId.IsNull)
            {
                return false;
            }

            if (HasAnyDebugProofMarker(leader))
            {
                return false;
            }

#if DEBUG
            if (IsDebugProofExclusiveOwner(leader))
            {
                return false;
            }
#endif

            return true;
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

            if (Session.IsProcessing)
            {
                baseMove();
                return;
            }

            try
            {
                using (Session.BeginProcessing())
                {
                    // Leader geometry: native AutoCAD MoveGripPointsAt only.
                    // Post-move: sync framed content presentation to final
                    // landing, then R3 side swap — no vertex rewrite.
                    baseMove();

                    var outcome = TryNormalizeAfterNativeMove(
                        document,
                        leader,
                        out var reason);
                    var handle = leader.ObjectId.IsNull
                        ? Session.LastHandle
                        : leader.ObjectId.Handle.ToString();
                    Session.RecordNormalizeOutcome(outcome, reason, handle);
                    ForceP4aLifecycleOff(document);
                }
            }
            catch (System.Exception exception)
            {
                var handle = leader.ObjectId.IsNull
                    ? string.Empty
                    : leader.ObjectId.Handle.ToString();
                Session.RecordCaughtException(
                    handle,
                    exception.GetType().Name + ": " + exception.Message);
                Session.ForceReleaseProcessingGuard();
            }
        }
    }
}
