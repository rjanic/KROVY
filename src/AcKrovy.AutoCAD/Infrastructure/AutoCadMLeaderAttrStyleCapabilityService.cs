#if DEBUG
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;
using AcApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using ErrorStatus = Autodesk.AutoCAD.Runtime.ErrorStatus;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// DEBUG-only host probe: can MLeader block-content AttributeReference.TextStyleId
/// persist independently per instance, or does it always derive from AttrDef?
/// Approaches A–H are exercised independently on disposable MLeaders sharing one
/// local block definition. One variant failure must not abort the matrix.
/// </summary>
internal static class AutoCadMLeaderAttrStyleCapabilityService
{
    private const string RegAppName = "AK_DEV_MLEADER_ATTR_STYLE_CAP";
    private const string BlockName = "AK_DEV_MLEADER_ATTR_STYLE_CAP_BLOCK";
    private const string AttributeTag = "ITEM_NO";
    private const string NodDictionaryName = "AK_DEV_MLEADER_ATTR_STYLE_CAP";
    private const string ManifestRecordName = "MANIFEST";
    private const double AttributeHeightMm = 135d;
    private const int XDataRegAppCode = 1001;
    private const int XDataStringCode = 1000;
    private const int XRecordStringCode = 1;
    private const string OutcomePersisted = "PERSISTED";
    private const string OutcomeReverted = "REVERTED_TO_DEFINITION";
    private const string OutcomeNotApplicable = "NOT_APPLICABLE";
    private const string OutcomeApiError = "API_ERROR";
    private const string SingleLineHReason =
        "block attribute is a single-line AttributeDefinition";

    // Setup-only gate: when true, stop after disposable AttrDef creation/audit
    // so the host can confirm STEP logs before A–H. Flip to false after the
    // first failing setup line is confirmed on host.
    private static bool SetupDiagnosticsOnly = false;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private static readonly string[] Approaches =
    [
        "A", "B", "C", "D", "E", "F", "G", "H",
    ];

    public static void Run()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var database = document.Database;
        var editor = document.Editor;
        var step = new ProbeStepTracker(editor);
        try
        {
            ObjectId definitionId = ObjectId.Null;
            ObjectId blockContentId = ObjectId.Null;
            AutoCadTextStyleCatalogEntry baseline;
            AutoCadTextStyleCatalogEntry requested;
            bool isMTextAttributeDefinition;
            string attrDefAudit;

            using (document.LockDocument())
            using (var setup = database.TransactionManager.StartTransaction())
            {
                try
                {
                    step.Begin(
                        "01",
                        "OpenModelSpace(BlockTable/ModelSpace ForRead)");
                    var modelSpace = OpenModelSpace(
                        database,
                        setup,
                        OpenMode.ForRead);

                    step.Begin(
                        "02",
                        "CountModelSpaceEntities(transaction.GetObject)");
                    var existing = CountModelSpaceEntities(modelSpace, setup);
                    if (existing != 0)
                    {
                        editor.WriteMessage(
                            $"\nAK_DEV_MLEADER_ATTR_STYLE_CAPABILITY: FAIL - " +
                            $"model space contains {existing} entities. " +
                            "Use a new empty drawing, then rerun.");
                        setup.Abort();
                        return;
                    }

                    step.Begin(
                        "03",
                        "AutoCadTextStyleResolver.ReadCatalog");
                    var catalog = AutoCadTextStyleResolver.ReadCatalog(
                        database,
                        setup);

                    step.Begin("04", "TryPickStyles (no AutoCAD mutate)");
                    if (!TryPickStyles(
                            catalog,
                            database,
                            out baseline,
                            out requested,
                            out var styleDiagnostic))
                    {
                        editor.WriteMessage(
                            "\nAK_DEV_MLEADER_ATTR_STYLE_CAPABILITY: FAIL - " +
                            "need two distinct compatible text styles.\n" +
                            styleDiagnostic);
                        setup.Abort();
                        return;
                    }

                    editor.WriteMessage(
                        "\nAK_DEV_MLEADER_ATTR_STYLE_CAPABILITY");
                    editor.WriteMessage(
                        "\n  API surface (Architecture 2027 AcDbMgd): " +
                        "MLeader.GetBlockAttribute(ObjectId), " +
                        "MLeader.SetBlockAttribute(ObjectId, AttributeReference). " +
                        "No dedicated per-instance AttrRef style override API. " +
                        "MLeader.TextStyleId / MLeaderStyle.TextStyleId apply to " +
                        "MText content, not block AttrRef. Approach H probes " +
                        "IsMTextAttribute + MText.TextStyleId only when " +
                        "AttrDef.IsMTextAttributeDefinition is true.");
                    editor.WriteMessage(styleDiagnostic);

                    step.Begin("05", "EnsureRegApp(RegAppTable)");
                    EnsureRegApp(database, setup);

                    step.Begin(
                        "06",
                        "EnsureDisposableBlock (create AttrDef; see STEPs 06.xx)");
                    var definition = EnsureDisposableBlock(
                        database,
                        setup,
                        baseline.TextStyleId,
                        step);
                    definitionId = definition.ObjectId;
                    blockContentId = definition.OwnerId;

                    step.Begin(
                        "07",
                        "AttributeDefinition.IsMTextAttributeDefinition get",
                        definitionId: definitionId,
                        blockContentId: blockContentId,
                        attrDefId: definitionId);
                    isMTextAttributeDefinition = TryReadIsMTextAttributeDefinition(
                        definition,
                        step);

                    step.Begin(
                        "08",
                        "AuditAttributeDefinition (property reads)",
                        definitionId: definitionId,
                        blockContentId: blockContentId,
                        attrDefId: definitionId);
                    attrDefAudit = AuditAttributeDefinition(definition);
                    editor.WriteMessage(attrDefAudit);

                    step.Begin("09", "setup.Transaction.Commit()");
                    setup.Commit();
                    step.Begin("10", "SETUP COMPLETE (ready for A–H)");
                }
                catch (System.Exception setupException)
                {
                    ReportSetupFailure(
                        editor,
                        step,
                        setupException,
                        definitionId,
                        blockContentId);
                    try
                    {
                        setup.Abort();
                    }
                    catch (System.Exception)
                    {
                        // Best-effort abort after diagnostic report.
                    }

                    return;
                }
            }

            if (SetupDiagnosticsOnly)
            {
                editor.WriteMessage(
                    "\nAK_DEV_MLEADER_ATTR_STYLE_CAPABILITY: SETUP OK - " +
                    "SetupDiagnosticsOnly=true; A–H NOT run. " +
                    "Set SetupDiagnosticsOnly=false after host confirms STEP log, " +
                    "then rerun for matrix.");
                return;
            }

            var entries = new List<CapabilityManifestEntry>();
            using (document.LockDocument())
            {
                foreach (var approach in Approaches)
                {
                    CapabilityManifestEntry entry;
                    try
                    {
                        entry = ExecuteApproachIsolated(
                            database,
                            editor,
                            definitionId,
                            blockContentId,
                            baseline,
                            requested,
                            approach,
                            index: entries.Count,
                            isMTextAttributeDefinition);
                    }
                    catch (System.Exception exception)
                    {
                        entry = CreateFailureEntry(
                            database,
                            editor,
                            approach,
                            definitionId,
                            ObjectId.Null,
                            operation: "ExecuteApproachIsolated(unhandled)",
                            exception,
                            baseline,
                            requested,
                            isMTextAttributeDefinition);
                    }

                    entries.Add(entry);
                }

                using (var manifestTx =
                    database.TransactionManager.StartTransaction())
                {
                    WriteManifest(database, manifestTx, entries);
                    manifestTx.Commit();
                }
            }

            WriteMatrixSummary(editor, entries, baseline, requested);
            var provisional = ComputeProvisionalVerdict(entries);
            editor.WriteMessage(
                $"\n\nPROVISIONAL VERDICT (CREATE matrix only): {provisional}");
            editor.WriteMessage(
                "\nFinal SUPPORTED requires SAVE/CLOSE/REOPEN + VERIFY.");
            editor.WriteMessage(
                "\n\nSAVE/CLOSE/REOPEN guidance:");
            editor.WriteMessage(
                "\n  1. SAVEAS a disposable DWG (keep this test drawing).");
            editor.WriteMessage(
                "\n  2. CLOSE the drawing.");
            editor.WriteMessage(
                "\n  3. REOPEN the saved DWG.");
            editor.WriteMessage(
                "\n  4. Run AK_DEV_MLEADER_ATTR_STYLE_CAPABILITY_VERIFY");
            editor.WriteMessage(
                "\n  5. Compare post-commit vs reopen TextStyleId for A–H.");
            editor.WriteMessage(
                "\n  6. Clean with AK_DEV_MLEADER_ATTR_STYLE_CAPABILITY_CLEAN");
            editor.WriteMessage(
                "\n\nAK_DEV_MLEADER_ATTR_STYLE_CAPABILITY: PASS - " +
                "capability matrix completed.");
        }
        catch (System.Exception exception)
        {
            ReportSetupFailure(
                editor,
                step,
                exception,
                ObjectId.Null,
                ObjectId.Null);
        }
    }

    public static void Verify()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var database = document.Database;
        var editor = document.Editor;
        var dbmodBefore = Convert.ToInt32(
            AcApplication.GetSystemVariable("DBMOD"),
            CultureInfo.InvariantCulture);
        try
        {
            using (document.LockDocument())
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var manifest = ReadManifest(database, transaction);
                if (manifest is null || manifest.Count == 0)
                {
                    editor.WriteMessage(
                        "\nAK_DEV_MLEADER_ATTR_STYLE_CAPABILITY_VERIFY: FAIL - " +
                        "manifest missing. Run the capability command first.");
                    transaction.Abort();
                    return;
                }

                editor.WriteMessage(
                    "\nAK_DEV_MLEADER_ATTR_STYLE_CAPABILITY_VERIFY " +
                    $"(read-only; DBMOD before={dbmodBefore})");

                foreach (var entry in manifest.OrderBy(item => item.Approach))
                {
                    if (string.Equals(
                            entry.Outcome,
                            OutcomeNotApplicable,
                            StringComparison.Ordinal) ||
                        string.Equals(
                            entry.Outcome,
                            OutcomeApiError,
                            StringComparison.Ordinal) ||
                        string.IsNullOrWhiteSpace(entry.PrimaryHandle))
                    {
                        editor.WriteMessage(
                            $"\n  [{entry.Approach}] skip reopen readback - " +
                            $"CREATE outcome={entry.Outcome}; " +
                            $"notes={entry.Notes}");
                        continue;
                    }

                    if (!TryGetObjectId(
                            database,
                            entry.PrimaryHandle,
                            out var primaryId) ||
                        transaction.GetObject(
                            primaryId,
                            OpenMode.ForRead,
                            false) is not MLeader primary)
                    {
                        editor.WriteMessage(
                            $"\n  [{entry.Approach}] FAIL - primary missing " +
                            $"(handle={entry.PrimaryHandle}).");
                        continue;
                    }

                    if (!TryGetObjectId(
                            database,
                            entry.ControlHandle,
                            out var controlId) ||
                        transaction.GetObject(
                            controlId,
                            OpenMode.ForRead,
                            false) is not MLeader control)
                    {
                        editor.WriteMessage(
                            $"\n  [{entry.Approach}] FAIL - control missing " +
                            $"(handle={entry.ControlHandle}).");
                        continue;
                    }

                    if (!TryGetObjectId(
                            database,
                            entry.AttributeDefinitionHandle,
                            out var defId) ||
                        transaction.GetObject(
                            defId,
                            OpenMode.ForRead,
                            false) is not AttributeDefinition definition)
                    {
                        editor.WriteMessage(
                            $"\n  [{entry.Approach}] FAIL - AttrDef missing.");
                        continue;
                    }

                    using var primaryAttr =
                        primary.GetBlockAttribute(definition.ObjectId);
                    using var controlAttr =
                        control.GetBlockAttribute(definition.ObjectId);
                    var primaryStyle = ResolveStyleName(
                        database,
                        transaction,
                        primaryAttr.TextStyleId);
                    var controlStyle = ResolveStyleName(
                        database,
                        transaction,
                        controlAttr.TextStyleId);
                    var defStyle = ResolveStyleName(
                        database,
                        transaction,
                        definition.TextStyleId);
                    var requestedStyleId = ParseHandleId(
                        database,
                        entry.RequestedTextStyleHandle);
                    var controlExpectedStyleId = ParseHandleId(
                        database,
                        entry.ControlExpectedStyleHandle);
                    var stylePersisted =
                        !requestedStyleId.IsNull &&
                        primaryAttr.TextStyleId == requestedStyleId;
                    var crosstalk =
                        controlExpectedStyleId.IsNull ||
                        controlAttr.TextStyleId != controlExpectedStyleId ||
                        !string.Equals(
                            controlAttr.TextString,
                            "CTRL",
                            StringComparison.Ordinal);

                    editor.WriteMessage(
                        $"\n  [{entry.Approach}] reopen readback: " +
                        $"CREATE outcome={entry.Outcome}; " +
                        $"requested={entry.RequestedStyleName} " +
                        $"({entry.RequestedTextStyleHandle}); " +
                        $"AttrDef={defStyle} ({definition.TextStyleId.Handle}); " +
                        $"primaryAttr={primaryStyle} " +
                        $"({primaryAttr.TextStyleId.Handle}); " +
                        $"token={primaryAttr.TextString}; " +
                        $"height={primaryAttr.Height:R}; " +
                        $"stylePersisted={stylePersisted}; " +
                        $"controlAttr={controlStyle} " +
                        $"({controlAttr.TextStyleId.Handle}); " +
                        $"controlCrosstalk={crosstalk}; " +
                        $"postCommitWas={entry.PostCommitStyleName} " +
                        $"({entry.PostCommitTextStyleHandle}).");
                }

                transaction.Commit();
            }

            var dbmodAfter = Convert.ToInt32(
                AcApplication.GetSystemVariable("DBMOD"),
                CultureInfo.InvariantCulture);
            editor.WriteMessage(
                $"\n  DBMOD after verify: before={dbmodBefore}, " +
                $"after={dbmodAfter} " +
                (dbmodBefore == dbmodAfter
                    ? "(unchanged)."
                    : "(CHANGED - investigate writes)."));
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                "\nAK_DEV_MLEADER_ATTR_STYLE_CAPABILITY_VERIFY: FAIL - " +
                FormatException(exception));
        }
    }

    public static void Clean()
    {
        var document = AcApplication.DocumentManager.MdiActiveDocument;
        if (document is null)
        {
            return;
        }

        var database = document.Database;
        var editor = document.Editor;
        try
        {
            using (document.LockDocument())
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var modelSpace = OpenModelSpace(
                    database,
                    transaction,
                    OpenMode.ForWrite);
                var erased = 0;
                foreach (ObjectId id in modelSpace)
                {
                    if (transaction.GetObject(id, OpenMode.ForRead, false)
                            is not Entity entity ||
                        entity.IsErased)
                    {
                        continue;
                    }

                    if (!HasCapabilityMarker(entity))
                    {
                        continue;
                    }

                    entity.UpgradeOpen();
                    entity.Erase();
                    erased++;
                }

                EraseDisposableBlock(database, transaction);
                EraseManifest(database, transaction);
                transaction.Commit();
                editor.WriteMessage(
                    $"\nAK_DEV_MLEADER_ATTR_STYLE_CAPABILITY_CLEAN: " +
                    $"erased {erased} marked entities + disposable block.");
            }
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                "\nAK_DEV_MLEADER_ATTR_STYLE_CAPABILITY_CLEAN: FAIL - " +
                FormatException(exception));
        }
    }

    private static CapabilityManifestEntry ExecuteApproachIsolated(
        Database database,
        Editor editor,
        ObjectId definitionId,
        ObjectId blockContentId,
        AutoCadTextStyleCatalogEntry baseline,
        AutoCadTextStyleCatalogEntry requested,
        string approach,
        int index,
        bool isMTextAttributeDefinition)
    {
        var posX = index * 800d;
        var notes = ApproachNotes(approach);

        if (approach == "H" && !isMTextAttributeDefinition)
        {
            editor.WriteMessage(
                $"\n  [H] operation=skip MText AttrRef APIs; " +
                $"AttrDef={definitionId}; " +
                $"IsMTextAttributeDefinition=false; " +
                $"outcome={OutcomeNotApplicable}; reason={SingleLineHReason}");
            return new CapabilityManifestEntry(
                approach,
                PrimaryHandle: string.Empty,
                ControlHandle: string.Empty,
                AttributeDefinitionHandle: definitionId.Handle.ToString(),
                RequestedTextStyleHandle: requested.TextStyleId.Handle.ToString(),
                RequestedStyleName: requested.CanonicalName,
                ControlExpectedStyleHandle: baseline.TextStyleId.Handle.ToString(),
                PostCommitTextStyleHandle: string.Empty,
                PostCommitStyleName: string.Empty,
                Outcome: OutcomeNotApplicable,
                Notes: SingleLineHReason);
        }

        ObjectId primaryId = ObjectId.Null;
        ObjectId controlId = ObjectId.Null;
        ObjectId styleBeforeWrite = ObjectId.Null;
        string? styleBeforeWriteName = null;
        string operation = "create+approach";

        try
        {
            if (approach == "D")
            {
                using (var createTx =
                    database.TransactionManager.StartTransaction())
                {
                    var modelSpace = OpenModelSpace(
                        database,
                        createTx,
                        OpenMode.ForWrite);
                    var definition = (AttributeDefinition)createTx.GetObject(
                        definitionId,
                        OpenMode.ForRead);
                    AssertDefinitionBelongsToBlock(
                        definition,
                        blockContentId);
                    controlId = CreateControlLeader(
                        database,
                        createTx,
                        modelSpace,
                        definition,
                        baseline,
                        approach,
                        posX,
                        editor,
                        ref operation);
                    primaryId = CreatePrimaryBareLeader(
                        database,
                        createTx,
                        modelSpace,
                        definition,
                        blockContentId,
                        approach,
                        posX,
                        editor,
                        ref operation);
                    var primary = (MLeader)createTx.GetObject(
                        primaryId,
                        OpenMode.ForWrite);
                    operation = "D:initial SetBlockAttribute";
                    using (var attribute = new AttributeReference())
                    {
                        attribute.SetAttributeFromBlock(
                            definition,
                            Matrix3d.Identity);
                        styleBeforeWrite = attribute.TextStyleId;
                        styleBeforeWriteName = ResolveStyleName(
                            database,
                            createTx,
                            styleBeforeWrite);
                        attribute.TextString = "D";
                        attribute.Height = AttributeHeightMm;
                        attribute.TextStyleId = requested.TextStyleId;
                        WritePreSetState(
                            editor,
                            "D",
                            operation,
                            primary,
                            definition,
                            isMTextAttribute: null);
                        primary.SetBlockAttribute(
                            definition.ObjectId,
                            attribute);
                    }

                    createTx.Commit();
                }

                using (var modifyTx =
                    database.TransactionManager.StartTransaction())
                {
                    var primary = (MLeader)modifyTx.GetObject(
                        primaryId,
                        OpenMode.ForWrite);
                    var definition = (AttributeDefinition)modifyTx.GetObject(
                        definitionId,
                        OpenMode.ForRead);
                    operation = "D:new-tx Get/modify/Set TextStyleId";
                    using var nestedAttr = primary.GetBlockAttribute(
                        definition.ObjectId);
                    nestedAttr.TextStyleId = requested.TextStyleId;
                    nestedAttr.TextString = "D";
                    nestedAttr.Height = AttributeHeightMm;
                    WritePreSetState(
                        editor,
                        "D",
                        operation,
                        primary,
                        definition,
                        TryReadIsMTextAttribute(nestedAttr));
                    primary.SetBlockAttribute(
                        definition.ObjectId,
                        nestedAttr);
                    modifyTx.Commit();
                }
            }
            else if (approach == "G")
            {
                using var tx = database.TransactionManager.StartTransaction();
                var modelSpace = OpenModelSpace(
                    database,
                    tx,
                    OpenMode.ForWrite);
                var definition = (AttributeDefinition)tx.GetObject(
                    definitionId,
                    OpenMode.ForWrite);
                AssertDefinitionBelongsToBlock(definition, blockContentId);
                var baselineStyleId = definition.TextStyleId;
                operation = "G:set AttrDef.TextStyleId before MLeader create";
                definition.TextStyleId = requested.TextStyleId;

                try
                {
                    controlId = CreateControlLeader(
                        database,
                        tx,
                        modelSpace,
                        definition,
                        baseline,
                        approach,
                        posX,
                        editor,
                        ref operation);
                    primaryId = CreatePrimaryBareLeader(
                        database,
                        tx,
                        modelSpace,
                        definition,
                        blockContentId,
                        approach,
                        posX,
                        editor,
                        ref operation);
                    var primary = (MLeader)tx.GetObject(
                        primaryId,
                        OpenMode.ForWrite);
                    operation = "G:SetBlockAttribute while AttrDef=requested";
                    using (var attribute = new AttributeReference())
                    {
                        attribute.SetAttributeFromBlock(
                            definition,
                            Matrix3d.Identity);
                        styleBeforeWrite = attribute.TextStyleId;
                        styleBeforeWriteName = ResolveStyleName(
                            database,
                            tx,
                            styleBeforeWrite);
                        attribute.TextString = "G";
                        attribute.Height = AttributeHeightMm;
                        attribute.TextStyleId = requested.TextStyleId;
                        WritePreSetState(
                            editor,
                            "G",
                            operation,
                            primary,
                            definition,
                            isMTextAttribute: null);
                        primary.SetBlockAttribute(
                            definition.ObjectId,
                            attribute);
                    }
                }
                finally
                {
                    operation = "G:restore AttrDef.TextStyleId";
                    definition.TextStyleId = baselineStyleId;
                }

                tx.Commit();
            }
            else
            {
                using var tx = database.TransactionManager.StartTransaction();
                var modelSpace = OpenModelSpace(
                    database,
                    tx,
                    OpenMode.ForWrite);
                var definition = (AttributeDefinition)tx.GetObject(
                    definitionId,
                    OpenMode.ForRead);
                AssertDefinitionBelongsToBlock(definition, blockContentId);
                controlId = CreateControlLeader(
                    database,
                    tx,
                    modelSpace,
                    definition,
                    baseline,
                    approach,
                    posX,
                    editor,
                    ref operation);
                primaryId = CreatePrimaryBareLeader(
                    database,
                    tx,
                    modelSpace,
                    definition,
                    blockContentId,
                    approach,
                    posX,
                    editor,
                    ref operation);
                var primary = (MLeader)tx.GetObject(
                    primaryId,
                    OpenMode.ForWrite);

                switch (approach)
                {
                    case "A":
                        operation = "A:AttrRef.TextStyleId before SetBlockAttribute";
                        using (var attribute = new AttributeReference())
                        {
                            attribute.SetAttributeFromBlock(
                                definition,
                                Matrix3d.Identity);
                            styleBeforeWrite = attribute.TextStyleId;
                            styleBeforeWriteName = ResolveStyleName(
                                database,
                                tx,
                                styleBeforeWrite);
                            attribute.TextString = "A";
                            attribute.Height = AttributeHeightMm;
                            attribute.TextStyleId = requested.TextStyleId;
                            WritePreSetState(
                                editor,
                                "A",
                                operation,
                                primary,
                                definition,
                                isMTextAttribute: null);
                            primary.SetBlockAttribute(
                                definition.ObjectId,
                                attribute);
                        }

                        break;

                    case "B":
                        operation = "B:initial SetBlockAttribute";
                        using (var attribute = new AttributeReference())
                        {
                            attribute.SetAttributeFromBlock(
                                definition,
                                Matrix3d.Identity);
                            styleBeforeWrite = attribute.TextStyleId;
                            styleBeforeWriteName = ResolveStyleName(
                                database,
                                tx,
                                styleBeforeWrite);
                            attribute.TextString = "B";
                            attribute.Height = AttributeHeightMm;
                            WritePreSetState(
                                editor,
                                "B",
                                operation,
                                primary,
                                definition,
                                isMTextAttribute: null);
                            primary.SetBlockAttribute(
                                definition.ObjectId,
                                attribute);
                            operation =
                                "B:modify local AttrRef.TextStyleId + Set again";
                            attribute.TextStyleId = requested.TextStyleId;
                            WritePreSetState(
                                editor,
                                "B",
                                operation,
                                primary,
                                definition,
                                isMTextAttribute: null);
                            primary.SetBlockAttribute(
                                definition.ObjectId,
                                attribute);
                        }

                        break;

                    case "C":
                        operation =
                            "C:GetBlockAttribute after append (no prior Set)";
                        using (var written =
                            primary.GetBlockAttribute(definition.ObjectId))
                        {
                            styleBeforeWrite = written.TextStyleId;
                            styleBeforeWriteName = ResolveStyleName(
                                database,
                                tx,
                                styleBeforeWrite);
                            written.TextStyleId = requested.TextStyleId;
                            written.TextString = "C";
                            written.Height = AttributeHeightMm;
                            operation = "C:SetBlockAttribute after Get/modify";
                            WritePreSetState(
                                editor,
                                "C",
                                operation,
                                primary,
                                definition,
                                TryReadIsMTextAttribute(written));
                            primary.SetBlockAttribute(
                                definition.ObjectId,
                                written);
                        }

                        break;

                    case "E":
                        operation = "E:initial SetBlockAttribute";
                        using (var attribute = new AttributeReference())
                        {
                            attribute.SetAttributeFromBlock(
                                definition,
                                Matrix3d.Identity);
                            styleBeforeWrite = attribute.TextStyleId;
                            styleBeforeWriteName = ResolveStyleName(
                                database,
                                tx,
                                styleBeforeWrite);
                            attribute.TextString = "E";
                            attribute.Height = AttributeHeightMm;
                            WritePreSetState(
                                editor,
                                "E",
                                operation,
                                primary,
                                definition,
                                isMTextAttribute: null);
                            primary.SetBlockAttribute(
                                definition.ObjectId,
                                attribute);
                        }

                        operation =
                            "E:Get/modify TextStyleId without SetBlockAttribute";
                        using (var written =
                            primary.GetBlockAttribute(definition.ObjectId))
                        {
                            written.TextStyleId = requested.TextStyleId;
                            // Intentionally no SetBlockAttribute.
                        }

                        break;

                    case "F":
                        operation = "F:initial SetBlockAttribute";
                        using (var attribute = new AttributeReference())
                        {
                            attribute.SetAttributeFromBlock(
                                definition,
                                Matrix3d.Identity);
                            styleBeforeWrite = attribute.TextStyleId;
                            styleBeforeWriteName = ResolveStyleName(
                                database,
                                tx,
                                styleBeforeWrite);
                            attribute.TextString = "F";
                            attribute.Height = AttributeHeightMm;
                            WritePreSetState(
                                editor,
                                "F",
                                operation,
                                primary,
                                definition,
                                isMTextAttribute: null);
                            primary.SetBlockAttribute(
                                definition.ObjectId,
                                attribute);
                        }

                        operation = "F:Get/modify/Set TextStyleId";
                        using (var written =
                            primary.GetBlockAttribute(definition.ObjectId))
                        {
                            written.TextStyleId = requested.TextStyleId;
                            written.TextString = "F";
                            written.Height = AttributeHeightMm;
                            WritePreSetState(
                                editor,
                                "F",
                                operation,
                                primary,
                                definition,
                                TryReadIsMTextAttribute(written));
                            primary.SetBlockAttribute(
                                definition.ObjectId,
                                written);
                        }

                        break;

                    case "H":
                        operation =
                            "H:IsMTextAttribute + MText.TextStyleId " +
                            "(MText AttrDef only)";
                        using (var attribute = new AttributeReference())
                        {
                            attribute.SetAttributeFromBlock(
                                definition,
                                Matrix3d.Identity);
                            styleBeforeWrite = attribute.TextStyleId;
                            styleBeforeWriteName = ResolveStyleName(
                                database,
                                tx,
                                styleBeforeWrite);
                            attribute.TextString = "H";
                            attribute.Height = AttributeHeightMm;
                            attribute.IsMTextAttribute = true;
                            using var mtext = new MText();
                            mtext.SetDatabaseDefaults(database);
                            mtext.Contents = "H";
                            mtext.TextHeight = AttributeHeightMm;
                            mtext.TextStyleId = requested.TextStyleId;
                            attribute.MTextAttribute = mtext;
                            attribute.TextStyleId = requested.TextStyleId;
                            WritePreSetState(
                                editor,
                                "H",
                                operation,
                                primary,
                                definition,
                                TryReadIsMTextAttribute(attribute));
                            primary.SetBlockAttribute(
                                definition.ObjectId,
                                attribute);
                        }

                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unknown approach '{approach}'.");
                }

                tx.Commit();
            }
        }
        catch (System.Exception exception)
        {
            return CreateFailureEntry(
                database,
                editor,
                approach,
                definitionId,
                primaryId,
                operation,
                exception,
                baseline,
                requested,
                isMTextAttributeDefinition,
                controlId,
                notes);
        }

        return ReadbackAndClassify(
            database,
            editor,
            approach,
            notes,
            primaryId,
            controlId,
            definitionId,
            baseline,
            requested,
            styleBeforeWrite,
            styleBeforeWriteName);
    }

    private static CapabilityManifestEntry ReadbackAndClassify(
        Database database,
        Editor editor,
        string approach,
        string notes,
        ObjectId primaryId,
        ObjectId controlId,
        ObjectId definitionId,
        AutoCadTextStyleCatalogEntry baseline,
        AutoCadTextStyleCatalogEntry requested,
        ObjectId styleBeforeWrite,
        string? styleBeforeWriteName)
    {
        using var tx = database.TransactionManager.StartTransaction();
        var primary = (MLeader)tx.GetObject(primaryId, OpenMode.ForRead);
        var control = (MLeader)tx.GetObject(controlId, OpenMode.ForRead);
        var definition = (AttributeDefinition)tx.GetObject(
            definitionId,
            OpenMode.ForRead);

        ObjectId styleAfterWrite;
        string styleAfterWriteName;
        ObjectId stylePostCommit;
        string stylePostCommitName;
        string token;
        double height;
        bool? isMTextAttribute;

        using (var afterWrite =
            primary.GetBlockAttribute(definition.ObjectId))
        {
            styleAfterWrite = afterWrite.TextStyleId;
            styleAfterWriteName = ResolveStyleName(
                database,
                tx,
                styleAfterWrite);
            stylePostCommit = styleAfterWrite;
            stylePostCommitName = styleAfterWriteName;
            token = afterWrite.TextString;
            height = afterWrite.Height;
            isMTextAttribute = TryReadIsMTextAttribute(afterWrite);
        }

        // Same-session re-read after graphics touch (post-commit in CREATE session).
        primary.UpgradeOpen();
        primary.RecordGraphicsModified(true);
        using (var post = primary.GetBlockAttribute(definition.ObjectId))
        {
            stylePostCommit = post.TextStyleId;
            stylePostCommitName = ResolveStyleName(
                database,
                tx,
                stylePostCommit);
            token = post.TextString;
            height = post.Height;
            isMTextAttribute = TryReadIsMTextAttribute(post);
        }

        using var controlAttr = control.GetBlockAttribute(definition.ObjectId);
        var controlStyle = ResolveStyleName(
            database,
            tx,
            controlAttr.TextStyleId);
        var defStyle = ResolveStyleName(
            database,
            tx,
            definition.TextStyleId);
        var stylePersisted = stylePostCommit == requested.TextStyleId;
        var heightPersisted =
            Math.Abs(height - AttributeHeightMm) <= 1e-6;
        var expectedToken = approach;
        var textPersisted =
            string.Equals(token, expectedToken, StringComparison.Ordinal) ||
            (approach == "H" && !string.IsNullOrWhiteSpace(token));
        var defUnchanged = definition.TextStyleId == baseline.TextStyleId;
        var crosstalk =
            controlAttr.TextStyleId != baseline.TextStyleId ||
            !string.Equals(
                controlAttr.TextString,
                "CTRL",
                StringComparison.Ordinal);
        // Approach G writes control while AttrDef temporarily = requested;
        // after restore, control may still show requested from that write.
        if (approach == "G")
        {
            crosstalk = !string.Equals(
                controlAttr.TextString,
                "CTRL",
                StringComparison.Ordinal);
        }

        var outcome = ClassifyOutcome(
            stylePersisted,
            defUnchanged,
            crosstalk,
            stylePostCommit,
            definition.TextStyleId,
            baseline.TextStyleId);

        editor.WriteMessage(
            $"\n  [{approach}] {notes}");
        editor.WriteMessage(
            $"\n    outcome={outcome}; " +
            $"requested={requested.CanonicalName} " +
            $"({requested.TextStyleId.Handle}); " +
            $"AttrDef after={defStyle} ({definition.TextStyleId.Handle}); " +
            $"AttrDefUnchanged={defUnchanged}; " +
            $"AttrRef before write={styleBeforeWriteName ?? "<n/a>"} " +
            $"({(styleBeforeWrite.IsNull ? "null" : styleBeforeWrite.Handle.ToString())}); " +
            $"after write={styleAfterWriteName} ({styleAfterWrite.Handle}); " +
            $"post-commit readback={stylePostCommitName} " +
            $"({stylePostCommit.Handle}); " +
            $"token={token}; height={height:R}; " +
            $"stylePersisted={stylePersisted}; " +
            $"heightPersisted={heightPersisted}; " +
            $"textPersisted={textPersisted}; " +
            $"controlStyle={controlStyle} " +
            $"({controlAttr.TextStyleId.Handle}); " +
            $"sharedDefinitionCrosstalk={crosstalk}" +
            FormatOptionalIsMText(isMTextAttribute) +
            ".");
        editor.WriteMessage(
            "\n    SAVE/CLOSE/REOPEN: run VERIFY after reopen.");

        tx.Commit();
        return new CapabilityManifestEntry(
            approach,
            primary.Handle.ToString(),
            control.Handle.ToString(),
            definition.Handle.ToString(),
            requested.TextStyleId.Handle.ToString(),
            requested.CanonicalName,
            baseline.TextStyleId.Handle.ToString(),
            stylePostCommit.Handle.ToString(),
            stylePostCommitName,
            outcome,
            notes);
    }

    private static string ClassifyOutcome(
        bool stylePersisted,
        bool defUnchanged,
        bool crosstalk,
        ObjectId postCommitStyleId,
        ObjectId definitionStyleId,
        ObjectId baselineStyleId)
    {
        if (stylePersisted && defUnchanged && !crosstalk)
        {
            return OutcomePersisted;
        }

        if (!stylePersisted &&
            (postCommitStyleId == definitionStyleId ||
             postCommitStyleId == baselineStyleId))
        {
            return OutcomeReverted;
        }

        if (!stylePersisted)
        {
            return OutcomeReverted;
        }

        // Style matched requested but AttrDef changed or control crosstalk.
        return OutcomeApiError;
    }

    private static string ComputeProvisionalVerdict(
        IReadOnlyList<CapabilityManifestEntry> entries)
    {
        var applicable = entries
            .Where(entry =>
                !string.Equals(
                    entry.Outcome,
                    OutcomeNotApplicable,
                    StringComparison.Ordinal))
            .ToArray();
        if (applicable.Length == 0)
        {
            return "INCONCLUSIVE";
        }

        if (applicable.Any(entry =>
                string.Equals(
                    entry.Outcome,
                    OutcomePersisted,
                    StringComparison.Ordinal)))
        {
            return "SUPPORTED (provisional; VERIFY required)";
        }

        var usable = applicable
            .Where(entry =>
                string.Equals(
                    entry.Outcome,
                    OutcomeReverted,
                    StringComparison.Ordinal) ||
                string.Equals(
                    entry.Outcome,
                    OutcomePersisted,
                    StringComparison.Ordinal))
            .ToArray();
        if (usable.Length > 0 &&
            usable.All(entry =>
                string.Equals(
                    entry.Outcome,
                    OutcomeReverted,
                    StringComparison.Ordinal)))
        {
            return "NOT SUPPORTED (provisional; VERIFY still required)";
        }

        return "INCONCLUSIVE";
    }

    private static void WriteMatrixSummary(
        Editor editor,
        IReadOnlyList<CapabilityManifestEntry> entries,
        AutoCadTextStyleCatalogEntry baseline,
        AutoCadTextStyleCatalogEntry requested)
    {
        editor.WriteMessage("\n\nA–H SUMMARY");
        editor.WriteMessage(
            $"\n  baselineAttrDefStyle={baseline.CanonicalName}; " +
            $"requestedStyle={requested.CanonicalName}");
        foreach (var entry in entries.OrderBy(item => item.Approach))
        {
            editor.WriteMessage(
                $"\n  [{entry.Approach}] outcome={entry.Outcome}; " +
                $"postCommit={entry.PostCommitStyleName}; " +
                $"notes={entry.Notes}");
        }
    }

    private static CapabilityManifestEntry CreateFailureEntry(
        Database database,
        Editor editor,
        string approach,
        ObjectId definitionId,
        ObjectId primaryId,
        string operation,
        System.Exception exception,
        AutoCadTextStyleCatalogEntry baseline,
        AutoCadTextStyleCatalogEntry requested,
        bool isMTextAttributeDefinition,
        ObjectId controlId = default,
        string? notes = null)
    {
        var outcome =
            exception is AcadException acad &&
            acad.ErrorStatus == ErrorStatus.NotApplicable
                ? OutcomeNotApplicable
                : OutcomeApiError;
        var detail = FormatException(exception);
        editor.WriteMessage(
            $"\n  [{approach}] DIAGNOSTIC FAILURE");
        editor.WriteMessage(
            $"\n    operation={operation}");
        editor.WriteMessage(
            $"\n    MLeader ObjectId=" +
            $"{(primaryId.IsNull ? "<null>" : primaryId.ToString())}");
        editor.WriteMessage(
            $"\n    AttrDef ObjectId={definitionId}");
        editor.WriteMessage(
            $"\n    IsMTextAttributeDefinition={isMTextAttributeDefinition}");
        editor.WriteMessage(
            $"\n    exceptionType={exception.GetType().FullName}");
        editor.WriteMessage(
            $"\n    message={exception.Message}");
        if (exception is AcadException acadException)
        {
            editor.WriteMessage(
                $"\n    ErrorStatus={acadException.ErrorStatus}");
        }

        // Exception.ToString() includes file:line when Debug PDBs are loaded.
        editor.WriteMessage(
            $"\n    stack={exception}");
        editor.WriteMessage(
            $"\n    outcome={outcome}; continuing next variant.");

        TryRollbackStrayLeaders(database, primaryId, controlId);

        return new CapabilityManifestEntry(
            approach,
            primaryId.IsNull ? string.Empty : primaryId.Handle.ToString(),
            controlId.IsNull ? string.Empty : controlId.Handle.ToString(),
            definitionId.Handle.ToString(),
            requested.TextStyleId.Handle.ToString(),
            requested.CanonicalName,
            baseline.TextStyleId.Handle.ToString(),
            PostCommitTextStyleHandle: string.Empty,
            PostCommitStyleName: string.Empty,
            Outcome: outcome,
            Notes: $"{notes ?? ApproachNotes(approach)} | {operation} | {detail}");
    }

    private static void TryRollbackStrayLeaders(
        Database database,
        ObjectId primaryId,
        ObjectId controlId)
    {
        try
        {
            using var tx = database.TransactionManager.StartTransaction();
            foreach (var id in new[] { primaryId, controlId })
            {
                if (id.IsNull)
                {
                    continue;
                }

                if (tx.GetObject(id, OpenMode.ForWrite, false) is Entity entity &&
                    !entity.IsErased)
                {
                    entity.Erase();
                }
            }

            tx.Commit();
        }
        catch (System.Exception)
        {
            // Best-effort local rollback only.
        }
    }

    private static ObjectId CreateControlLeader(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        AttributeDefinition definition,
        AutoCadTextStyleCatalogEntry baseline,
        string approach,
        double posX,
        Editor editor,
        ref string operation)
    {
        operation = $"{approach}:create control MLeader";
        var control = CreateBareLeader(
            database,
            transaction,
            modelSpace,
            definition.OwnerId,
            posX,
            y: -400d);
        WriteMarker(control, approach, isControl: true);
        operation = $"{approach}:SetBlockAttribute control CTRL";
        // Control token must remain CTRL; style follows baseline explicitly
        // (including during G while AttrDef is temporarily requested).
        using var attribute = new AttributeReference();
        attribute.SetAttributeFromBlock(definition, Matrix3d.Identity);
        attribute.TextString = "CTRL";
        attribute.Height = AttributeHeightMm;
        attribute.TextStyleId = baseline.TextStyleId;
        WritePreSetState(
            editor,
            approach,
            operation,
            control,
            definition,
            isMTextAttribute: null);
        control.SetBlockAttribute(definition.ObjectId, attribute);
        return control.ObjectId;
    }

    private static ObjectId CreatePrimaryBareLeader(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        AttributeDefinition definition,
        ObjectId blockContentId,
        string approach,
        double posX,
        Editor editor,
        ref string operation)
    {
        operation = $"{approach}:create primary MLeader";
        AssertDefinitionBelongsToBlock(definition, blockContentId);
        var primary = CreateBareLeader(
            database,
            transaction,
            modelSpace,
            blockContentId,
            posX,
            y: 0d);
        WriteMarker(primary, approach, isControl: false);
        editor.WriteMessage(
            $"\n  [{approach}] created primary MLeader={primary.ObjectId}; " +
            $"AttrDef={definition.ObjectId}; " +
            $"ContentType={primary.ContentType}; " +
            $"BlockContentId={primary.BlockContentId}; " +
            $"AttrDefOwner={definition.OwnerId}; " +
            $"inModelSpace+transaction=true.");
        return primary.ObjectId;
    }

    private static void WritePreSetState(
        Editor editor,
        string approach,
        string operation,
        MLeader leader,
        AttributeDefinition definition,
        bool? isMTextAttribute)
    {
        editor.WriteMessage(
            $"\n  [{approach}] pre-SetBlockAttribute: " +
            $"operation={operation}; " +
            $"MLeader={leader.ObjectId}; " +
            $"AttrDef={definition.ObjectId}; " +
            $"ContentType={leader.ContentType}; " +
            $"BlockContentId={leader.BlockContentId}; " +
            $"AttrDefOwner={definition.OwnerId}; " +
            $"AttrDefBelongsToBlockContent=" +
            $"{definition.OwnerId == leader.BlockContentId}" +
            FormatOptionalIsMText(isMTextAttribute));
    }

    private static string FormatOptionalIsMText(bool? isMTextAttribute) =>
        isMTextAttribute is null
            ? string.Empty
            : $"; IsMTextAttribute={isMTextAttribute.Value}";

    private static bool? TryReadIsMTextAttribute(AttributeReference attribute)
    {
        try
        {
            return attribute.IsMTextAttribute;
        }
        catch (AcadException)
        {
            return null;
        }
    }

    private static void AssertDefinitionBelongsToBlock(
        AttributeDefinition definition,
        ObjectId blockContentId)
    {
        if (definition.OwnerId != blockContentId)
        {
            throw new InvalidOperationException(
                "AttributeDefinition ObjectId does not belong to " +
                $"current BlockContentId ({blockContentId}).");
        }
    }

    private static string AuditAttributeDefinition(AttributeDefinition definition)
    {
        string? mtextDefNote = null;
        try
        {
            // Architecture 2027: MTextAttributeDefinition may be unavailable
            // on classic AttributeDefinition; probe only reports presence.
            var property = definition.GetType().GetProperty(
                "MTextAttributeDefinition");
            if (property is not null)
            {
                var value = property.GetValue(definition);
                mtextDefNote = value is null ? "null" : value.GetType().Name;
            }
            else
            {
                mtextDefNote = "<property absent>";
            }
        }
        catch (System.Exception exception)
        {
            mtextDefNote = $"<error:{exception.GetType().Name}>";
        }

        bool? isMTextDef = null;
        try
        {
            isMTextDef = definition.IsMTextAttributeDefinition;
        }
        catch (AcadException exception)
            when (exception.ErrorStatus == ErrorStatus.NotApplicable)
        {
            isMTextDef = null;
        }

        return
            $"\n  AttrDef audit: ObjectId={definition.ObjectId}; " +
            $"Tag={definition.Tag}; " +
            $"IsMTextAttributeDefinition=" +
            $"{(isMTextDef is null ? "<eNotApplicable>" : isMTextDef.Value.ToString())}; " +
            $"MTextAttributeDefinition={mtextDefNote}; " +
            $"TextStyleId={definition.TextStyleId.Handle}; " +
            $"Height={definition.Height:R}; " +
            $"Constant={definition.Constant}; " +
            $"Invisible={definition.Invisible}; " +
            $"Preset={definition.Preset}; " +
            $"Verifiable={definition.Verifiable}.";
    }

    private static string ApproachNotes(string approach) =>
        approach switch
        {
            "A" => "Set AttrRef.TextStyleId before SetBlockAttribute",
            "B" => "SetBlockAttribute, then change local AttrRef, Set again",
            "C" => "append, Get, change TextStyleId, Set",
            "D" => "commit create, new transaction Get/modify/Set",
            "E" => "Get, modify without SetBlockAttribute",
            "F" => "Get, modify, SetBlockAttribute",
            "G" => "set AttrDef style before MLeader create, write, restore " +
                "(probe-only; unsafe for production)",
            "H" => "API-specific: IsMTextAttribute + MText.TextStyleId",
            _ => approach,
        };

    private static string FormatException(System.Exception exception)
    {
        if (exception is AcadException acad)
        {
            return
                $"ErrorStatus={acad.ErrorStatus}; Message={acad.Message}; " +
                $"ToString={exception}";
        }

        return exception.ToString();
    }

    private static void ReportSetupFailure(
        Editor editor,
        ProbeStepTracker step,
        System.Exception exception,
        ObjectId definitionId,
        ObjectId blockContentId)
    {
        editor.WriteMessage(
            "\nAK_DEV_MLEADER_ATTR_STYLE_CAPABILITY: FAIL - setup aborted");
        editor.WriteMessage(
            $"\n  lastSTEP={step.LastStepId} ({step.LastApi})");
        editor.WriteMessage(
            $"\n  method={step.LastCallerMember}");
        editor.WriteMessage(
            $"\n  file={step.LastCallerFile}:{step.LastCallerLine}");
        editor.WriteMessage(
            $"\n  AttrDef ObjectId=" +
            $"{(definitionId.IsNull ? "<null>" : definitionId.ToString())}");
        editor.WriteMessage(
            $"\n  BlockContentId=" +
            $"{(blockContentId.IsNull ? "<null>" : blockContentId.ToString())}");
        editor.WriteMessage(
            $"\n  ContentType=<n/a during setup>");
        editor.WriteMessage(
            $"\n  MLeader ObjectId=<n/a during setup>");
        if (exception is AcadException acad)
        {
            editor.WriteMessage(
                $"\n  ErrorStatus={acad.ErrorStatus}");
        }

        editor.WriteMessage(
            $"\n  exceptionType={exception.GetType().FullName}");
        editor.WriteMessage(
            $"\n  message={exception.Message}");
        editor.WriteMessage(
            $"\n  stack={exception}");
        editor.WriteMessage(
            "\n  A–H NOT entered (setup failure).");
    }

    private static bool TryReadIsMTextAttributeDefinition(
        AttributeDefinition definition,
        ProbeStepTracker step)
    {
        try
        {
            return definition.IsMTextAttributeDefinition;
        }
        catch (AcadException exception)
            when (exception.ErrorStatus == ErrorStatus.NotApplicable)
        {
            step.Begin(
                "07b",
                "AttributeDefinition.IsMTextAttributeDefinition " +
                "threw eNotApplicable -> treat as false",
                attrDefId: definition.ObjectId,
                blockContentId: definition.OwnerId,
                definitionId: definition.ObjectId);
            WriteCaughtNotApplicable(
                step.Editor,
                "07b",
                "AttributeDefinition.get_IsMTextAttributeDefinition",
                exception,
                mleaderId: ObjectId.Null,
                contentType: null,
                blockContentId: definition.OwnerId,
                attrDefId: definition.ObjectId);
            return false;
        }
    }

    private static void WriteCaughtNotApplicable(
        Editor editor,
        string stepId,
        string method,
        AcadException exception,
        ObjectId mleaderId,
        ContentType? contentType,
        ObjectId blockContentId,
        ObjectId attrDefId)
    {
        editor.WriteMessage(
            $"\n  [CAUGHT eNotApplicable] STEP {stepId}; method={method}");
        editor.WriteMessage(
            $"\n    MLeader ObjectId=" +
            $"{(mleaderId.IsNull ? "<null>" : mleaderId.ToString())}");
        editor.WriteMessage(
            $"\n    ContentType=" +
            $"{(contentType is null ? "<n/a>" : contentType.Value.ToString())}");
        editor.WriteMessage(
            $"\n    BlockContentId=" +
            $"{(blockContentId.IsNull ? "<null>" : blockContentId.ToString())}");
        editor.WriteMessage(
            $"\n    AttrDef ObjectId=" +
            $"{(attrDefId.IsNull ? "<null>" : attrDefId.ToString())}");
        editor.WriteMessage(
            $"\n    ErrorStatus={exception.ErrorStatus}");
        editor.WriteMessage(
            $"\n    stack={exception}");
    }

    private sealed class ProbeStepTracker
    {
        public ProbeStepTracker(Editor editor)
        {
            Editor = editor;
        }

        public Editor Editor { get; }

        public string LastStepId { get; private set; } = "<none>";

        public string LastApi { get; private set; } = "<none>";

        public string LastCallerMember { get; private set; } = "<none>";

        public string LastCallerFile { get; private set; } = "<none>";

        public int LastCallerLine { get; private set; }

        public void Begin(
            string stepId,
            string apiAboutToCall,
            ObjectId definitionId = default,
            ObjectId mleaderId = default,
            ObjectId blockContentId = default,
            ObjectId attrDefId = default,
            ContentType? contentType = null,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            LastStepId = stepId;
            LastApi = apiAboutToCall;
            LastCallerMember = member;
            LastCallerFile = file;
            LastCallerLine = line;
            var message =
                $"\n  STEP {stepId}: about to call {apiAboutToCall}" +
                $" | method={member}" +
                $" | {System.IO.Path.GetFileName(file)}:{line}" +
                (mleaderId.IsNull
                    ? string.Empty
                    : $" | MLeader={mleaderId}") +
                (contentType is null
                    ? string.Empty
                    : $" | ContentType={contentType}") +
                (blockContentId.IsNull
                    ? string.Empty
                    : $" | BlockContentId={blockContentId}") +
                (attrDefId.IsNull && definitionId.IsNull
                    ? string.Empty
                    : $" | AttrDef=" +
                      $"{(attrDefId.IsNull ? definitionId : attrDefId)}");
            Editor.WriteMessage(message);
            System.Diagnostics.Trace.WriteLine(
                "AK_DEV_MLEADER_ATTR_STYLE_CAPABILITY" + message);
        }
    }

    private static MLeader CreateBareLeader(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        ObjectId blockContentId,
        double posX,
        double y)
    {
        var leader = new MLeader();
        leader.SetDatabaseDefaults(database);
        leader.EnableAnnotationScale = false;
        leader.Scale = 1d;
        leader.ContentType = ContentType.BlockContent;
        leader.BlockContentId = blockContentId;
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
        leader.BlockScale = new Scale3d(1d);
        leader.BlockRotation = 0d;
        leader.BlockPosition = new Point3d(posX, y, 0d);
        var leaderIndex = leader.AddLeader();
        var lineIndex = leader.AddLeaderLine(leaderIndex);
        leader.AddFirstVertex(
            lineIndex,
            new Point3d(posX - 300d, y - 250d, 0d));
        leader.AddLastVertex(
            lineIndex,
            new Point3d(posX - 100d, y, 0d));
        modelSpace.AppendEntity(leader);
        transaction.AddNewlyCreatedDBObject(leader, true);
        return leader;
    }

    private static AttributeDefinition EnsureDisposableBlock(
        Database database,
        Transaction transaction,
        ObjectId baselineStyleId,
        ProbeStepTracker step)
    {
        step.Begin("06.01", "BlockTable GetObject ForRead");
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        if (blockTable.Has(BlockName))
        {
            step.Begin(
                "06.02",
                "existing BlockTableRecord ForWrite + Erase entities");
            var existing = (BlockTableRecord)transaction.GetObject(
                blockTable[BlockName],
                OpenMode.ForWrite);
            foreach (ObjectId id in existing)
            {
                var entity = transaction.GetObject(id, OpenMode.ForWrite, false);
                entity?.Erase();
            }
        }
        else
        {
            step.Begin(
                "06.02",
                "BlockTable.UpgradeOpen + Add BlockTableRecord");
            blockTable.UpgradeOpen();
            var created = new BlockTableRecord
            {
                Name = BlockName,
                Origin = Point3d.Origin,
            };
            blockTable.Add(created);
            transaction.AddNewlyCreatedDBObject(created, true);
        }

        step.Begin("06.03", "re-open BlockTableRecord ForWrite");
        var record = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockName],
            OpenMode.ForWrite);

        step.Begin("06.04", "Circle AppendEntity + AddNewlyCreatedDBObject");
        var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 80d);
        record.AppendEntity(circle);
        transaction.AddNewlyCreatedDBObject(circle, true);

        step.Begin("06.05", "new AttributeDefinition()");
        var attribute = new AttributeDefinition();

        step.Begin("06.06", "AttributeDefinition.SetDatabaseDefaults");
        attribute.SetDatabaseDefaults(database);

        step.Begin("06.07", "AttributeDefinition.Position set");
        attribute.Position = Point3d.Origin;

        // Capability probe AttrDef needs TextStyleId persistence only — not
        // visual centering. Leave default HorizontalMode/VerticalMode.
        // Do NOT set AlignmentPoint, Justify, or alignment modes:
        // AlignmentPoint throws eNotApplicable on classic left-default AttrDef.
        step.Begin("06.08", "AttributeDefinition.Tag/Prompt/TextString/Height");
        attribute.Tag = AttributeTag;
        attribute.Prompt = AttributeTag;
        attribute.TextString = "0";
        attribute.Height = 50d;

        step.Begin(
            "06.09",
            "AttributeDefinition.TextStyleId = baselineStyleId");
        attribute.TextStyleId = baselineStyleId;

        step.Begin(
            "06.10",
            "AttributeDefinition Invisible/Constant/Verifiable/Preset flags");
        attribute.Invisible = false;
        attribute.Constant = false;
        attribute.Verifiable = false;
        attribute.Preset = false;

        // Classic single-line AttributeDefinition (not MText attribute).
        step.Begin(
            "06.11",
            "BlockTableRecord.AppendEntity(AttributeDefinition)");
        record.AppendEntity(attribute);

        step.Begin(
            "06.12",
            "Transaction.AddNewlyCreatedDBObject(AttributeDefinition)");
        transaction.AddNewlyCreatedDBObject(attribute, true);

        step.Begin(
            "06.13",
            "EnsureDisposableBlock return",
            definitionId: attribute.ObjectId,
            blockContentId: attribute.OwnerId,
            attrDefId: attribute.ObjectId);
        return attribute;
    }

    private static void EraseDisposableBlock(
        Database database,
        Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        if (!blockTable.Has(BlockName))
        {
            return;
        }

        var record = (BlockTableRecord)transaction.GetObject(
            blockTable[BlockName],
            OpenMode.ForWrite);
        foreach (ObjectId id in record)
        {
            var entity = transaction.GetObject(id, OpenMode.ForWrite, false);
            entity?.Erase();
        }

        record.Erase();
    }

    private static bool TryPickStyles(
        AutoCadTextStyleCatalog catalog,
        Database database,
        out AutoCadTextStyleCatalogEntry baseline,
        out AutoCadTextStyleCatalogEntry requested,
        out string diagnostic)
    {
        baseline = null!;
        requested = null!;
        var styles = catalog.CompatibleStyles.ToArray();
        if (styles.Length < 2)
        {
            diagnostic =
                $"CompatibleStyles={styles.Length}; database.Textstyle=" +
                $"{database.Textstyle.Handle}.";
            return false;
        }

        var classic = catalog.FindCompatible(
            TimberAnnotationTextStylePresetRules.ClassicStyleName);
        var arch = catalog.FindCompatible(
            TimberAnnotationTextStylePresetRules.ArchitecturalStyleName);
        if (classic is not null &&
            arch is not null &&
            classic.TextStyleId != arch.TextStyleId)
        {
            baseline = classic;
            requested = arch;
        }
        else
        {
            var first = styles[0];
            var second = styles.First(style =>
                style.TextStyleId != first.TextStyleId);
            baseline = first;
            requested = second;
        }

        diagnostic =
            $"\n  baselineAttrDefStyle={baseline.CanonicalName} " +
            $"({baseline.TextStyleId.Handle}); " +
            $"requestedStyle={requested.CanonicalName} " +
            $"({requested.TextStyleId.Handle}); " +
            $"compatibleCount={styles.Length}.";
        return true;
    }

    private static void EnsureRegApp(
        Database database,
        Transaction transaction)
    {
        var regApps = (RegAppTable)transaction.GetObject(
            database.RegAppTableId,
            OpenMode.ForRead);
        if (regApps.Has(RegAppName))
        {
            return;
        }

        regApps.UpgradeOpen();
        var record = new RegAppTableRecord
        {
            Name = RegAppName,
        };
        regApps.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static void WriteMarker(
        Entity entity,
        string approach,
        bool isControl)
    {
        entity.XData = new ResultBuffer(
            new TypedValue(XDataRegAppCode, RegAppName),
            new TypedValue(
                XDataStringCode,
                isControl ? $"CTRL:{approach}" : $"PRIMARY:{approach}"));
    }

    private static bool HasCapabilityMarker(Entity entity)
    {
        var buffer = entity.XData;
        if (buffer is null)
        {
            return false;
        }

        foreach (TypedValue value in buffer)
        {
            if (value.TypeCode == XDataRegAppCode &&
                value.Value is string name &&
                string.Equals(name, RegAppName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteManifest(
        Database database,
        Transaction transaction,
        IReadOnlyList<CapabilityManifestEntry> entries)
    {
        var nod = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        DBDictionary root;
        if (nod.Contains(NodDictionaryName))
        {
            root = (DBDictionary)transaction.GetObject(
                nod.GetAt(NodDictionaryName),
                OpenMode.ForWrite);
        }
        else
        {
            nod.UpgradeOpen();
            root = new DBDictionary();
            nod.SetAt(NodDictionaryName, root);
            transaction.AddNewlyCreatedDBObject(root, true);
        }

        if (root.Contains(ManifestRecordName))
        {
            var old = transaction.GetObject(
                root.GetAt(ManifestRecordName),
                OpenMode.ForWrite);
            old.Erase();
        }

        var json = JsonSerializer.Serialize(entries, JsonOptions);
        var record = new Xrecord
        {
            Data = new ResultBuffer(
                new TypedValue(XRecordStringCode, json)),
        };
        root.SetAt(ManifestRecordName, record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static List<CapabilityManifestEntry>? ReadManifest(
        Database database,
        Transaction transaction)
    {
        var nod = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!nod.Contains(NodDictionaryName))
        {
            return null;
        }

        var root = (DBDictionary)transaction.GetObject(
            nod.GetAt(NodDictionaryName),
            OpenMode.ForRead);
        if (!root.Contains(ManifestRecordName))
        {
            return null;
        }

        var record = (Xrecord)transaction.GetObject(
            root.GetAt(ManifestRecordName),
            OpenMode.ForRead);
        var json = record.Data?
            .Cast<TypedValue>()
            .Where(value => value.TypeCode == XRecordStringCode)
            .Select(value => value.Value?.ToString() ?? string.Empty)
            .Aggregate(new StringBuilder(), (sb, part) => sb.Append(part))
            .ToString();
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<List<CapabilityManifestEntry>>(
            json,
            JsonOptions);
    }

    private static void EraseManifest(
        Database database,
        Transaction transaction)
    {
        var nod = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!nod.Contains(NodDictionaryName))
        {
            return;
        }

        var root = (DBDictionary)transaction.GetObject(
            nod.GetAt(NodDictionaryName),
            OpenMode.ForWrite);
        root.Erase();
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

    private static int CountModelSpaceEntities(
        BlockTableRecord modelSpace,
        Transaction transaction)
    {
        var count = 0;
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false)
                    is Entity { IsErased: false })
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryGetObjectId(
        Database database,
        string handleText,
        out ObjectId objectId)
    {
        objectId = ParseHandleId(database, handleText);
        return !objectId.IsNull;
    }

    private static ObjectId ParseHandleId(
        Database database,
        string? handleText)
    {
        if (string.IsNullOrWhiteSpace(handleText))
        {
            return ObjectId.Null;
        }

        var normalized = handleText.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        if (!long.TryParse(
                normalized,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var handleValue))
        {
            return ObjectId.Null;
        }

        try
        {
            return database.GetObjectId(
                false,
                new Handle(handleValue),
                0);
        }
        catch (AcadException)
        {
            return ObjectId.Null;
        }
    }

    private static string ResolveStyleName(
        Database database,
        Transaction transaction,
        ObjectId styleId)
    {
        if (styleId.IsNull)
        {
            return "<null>";
        }

        try
        {
            if (transaction.GetObject(styleId, OpenMode.ForRead, false)
                is TextStyleTableRecord style)
            {
                return style.Name;
            }
        }
        catch (AcadException)
        {
            // Fall through.
        }

        return styleId.Handle.ToString();
    }

    private sealed record CapabilityManifestEntry(
        string Approach,
        string PrimaryHandle,
        string ControlHandle,
        string AttributeDefinitionHandle,
        string RequestedTextStyleHandle,
        string RequestedStyleName,
        string ControlExpectedStyleHandle,
        string PostCommitTextStyleHandle,
        string PostCommitStyleName,
        string Outcome = "",
        string Notes = "");
}
#endif
