#if DEBUG
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
/// DEBUG Stage A/B GripOverrule pass-through proof. Registers exactly one
/// minimal overrule that filters one marked test MLeader and delegates
/// GetGripPoints / MoveGripPointsAt to base only. No normalize, dogleg,
/// content-side, transactions in callbacks, grip inventory, or queue.
/// Production OFF.
/// </summary>
internal static class AutoCadFramedBlockContentGripPassthroughProofService
{
    internal const string DebugRegAppName = "AK_DEV_FBC_GRIP_PASSTHROUGH";
    private const string ProofLayerName = "AK_DEV_FBC_GRIP_PASSTHROUGH";
    private const string MarkerToken = "FBC_GRIP_PASSTHROUGH";
    private const string CommandBanner = "AK_DEV_FBC_GRIP_PASSTHROUGH";

    private static FramedBlockContentGripPassthroughOverrule? _overrule;
    private static bool _overruleAdded;
    private static bool _overrulingWasEnabled;
    private static ObjectId _trackedLeaderId = ObjectId.Null;
    private static bool _armed;

    public static bool IsOverruleRegistered => _overruleAdded;

    public static string OverruleInstanceIdentity =>
        AutoCadFramedBlockContentGripRegistrationSnapshot.FormatInstanceIdentity(_overrule);

    public static void RemoveSession(Document _)
    {
        // Global overrule: any document teardown must not leave it registered.
        if (_armed || _overruleAdded)
        {
            ForceUnregisterAll();
        }
    }

    /// <summary>
    /// Unload/terminate safety: always remove overrule; clear tracked id.
    /// </summary>
    public static void ForceUnregisterAll()
    {
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
        // Never leave a normalizing undo-proof overrule armed alongside this.
        AutoCadFramedBlockContentGripUndoProofService.ForceUnregisterAll();
        ForceUnregisterOverrule();

        ObjectId leaderId = ObjectId.Null;
        try
        {
            using (document.LockDocument())
            {
                var database = document.Database;
                using var transaction = database.TransactionManager.StartTransaction();

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
                transaction.Commit();
                editor.WriteMessage(
                    $"\n{CommandBanner}_SETUP created marked MLeader " +
                    $"handle={createdId.Handle} erasedOld={erased}");
            }

            // Register ONLY after create+commit. Never query GetGripPoints here.
            RegisterOverrule(leaderId);
            _armed = true;
            editor.WriteMessage($"\n{CommandBanner} armed (pass-through only).");
            editor.WriteMessage(
                "\nHost acceptance: click annotation (no crash) → native grips " +
                "visible → move knee (no crash) → OFF → grips still work.");
            editor.WriteMessage(
                "\nIf this crashes, GripOverrule is NO-GO for this host/API.");
        }
        catch (System.Exception exception)
        {
            ForceUnregisterOverrule();
            _armed = false;
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

    public static void DisableKeepEntities()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        ForceUnregisterOverrule();
        _armed = false;
        document?.Editor.WriteMessage($"\n{CommandBanner}_OFF (overrule removed)");
    }

    public static void Clean()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        ForceUnregisterOverrule();
        _armed = false;
        using var documentLock = document.LockDocument();
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var (found, erased) = EraseMarkedProofEntities(document.Database, transaction);
        transaction.Commit();
        document.Editor.WriteMessage($"\n=== {CommandBanner}_CLEAN ===");
        document.Editor.WriteMessage($"\noldProofEntitiesFound={found}");
        document.Editor.WriteMessage($"\noldProofEntitiesErased={erased}");
    }

    private static void RegisterOverrule(ObjectId leaderId)
    {
        if (leaderId.IsNull)
        {
            throw new InvalidOperationException(
                "Pass-through setup cannot register without a leader ObjectId.");
        }

        // Idempotent: remove any prior registration before adding exactly once.
        ForceUnregisterOverrule();
        _trackedLeaderId = leaderId;
        _overrule ??= new FramedBlockContentGripPassthroughOverrule();
        _overrulingWasEnabled = Overrule.Overruling;
        Overrule.Overruling = true;
        Overrule.AddOverrule(
            RXClass.GetClass(typeof(MLeader)),
            _overrule,
            false);
        _overruleAdded = true;
        AutoCadRedoDiagService.OnOverruleRegister(
            "Passthrough",
            OverruleInstanceIdentity,
            _overrulingWasEnabled);
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
                "Passthrough",
                identity,
                removed: true,
                overrulingRestoredTo: ownedWasEnabled,
                ownedWasEnabled: ownedWasEnabled);
        }

        _trackedLeaderId = ObjectId.Null;
        // No GripData / MLeader instance cache — tracked id only.
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
            AttachmentX: 14000d,
            AttachmentY: 14000d,
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
                $"{MarkerToken}|PASSTHROUGH"));
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

    /// <summary>
    /// Stage A/B: base GetGripPoints + base MoveGripPointsAt only.
    /// IsApplicable: ObjectId compare only — no DB open, no transactions.
    /// </summary>
    private sealed class FramedBlockContentGripPassthroughOverrule : GripOverrule
    {
        public override bool IsApplicable(RXObject overruledSubject)
        {
            if (_trackedLeaderId.IsNull)
            {
                return false;
            }

            // ObjectId compare only. No Database open, no TopTransaction, no BTR.
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
            // Stage A: native via base only. Do not flip Overruling (crash-path
            // pattern from UNDO_PROOF). Do not clear or invent grips.
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
            // Stage A: forward once via base — no GripData cache/copy/reuse.
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
            // Stage B: base only — no normalize / dogleg / content-side.
            base.MoveGripPointsAt(entity, indices, offset);
        }

        public override void MoveGripPointsAt(
            Entity entity,
            GripDataCollection grips,
            Vector3d offset,
            MoveGripPointsFlags bitFlags)
        {
            base.MoveGripPointsAt(entity, grips, offset, bitFlags);
        }
    }
}
#endif
