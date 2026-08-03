#if DEBUG
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AutoCadItemLeaderBlockVariantProofService
{
    internal const string ProofRegAppName = "AK_DEV_BLOCKVARIANT_PROOF";
    internal const string ProofManifestDictionaryKey =
        "AK_DEV_BLOCKVARIANT_MANIFEST";

    private sealed record CreatedCase(
        AutoCadItemLeaderBlockVariantProofCase ProofCase,
        AutoCadItemLeaderBlockVariantResult EnsureResult,
        ObjectId LeaderId,
        ObjectId AttributeDefinitionId,
        string StyleName,
        ObjectId StyleId,
        double DefinitionHeight,
        AutoCadItemLeaderBlockVariantProofMarker Marker);

    private sealed record PersistedCase(
        AutoCadItemLeaderBlockVariantProofCase ProofCase,
        AutoCadItemLeaderBlockVariantProofMarker Marker,
        ObjectId LeaderId,
        ObjectId BlockId,
        ObjectId AttributeDefinitionId,
        string BlockName,
        string StyleName,
        ObjectId StyleId,
        double DefinitionHeight,
        double BlockScale);

    private sealed record ProofCandidateDiagnostic(
        string Handle,
        string ObjectId,
        string OwnerHandle,
        string OwnerName,
        string BlockContentHandle,
        string BlockContentName,
        string XDataRegAppNames,
        int? MarkerSchema,
        string? CaseToken,
        string DecisionReason);

    private sealed record ProofScanResult(
        AutoCadItemLeaderBlockVariantProofManifest? Manifest,
        IReadOnlyList<AutoCadItemLeaderBlockVariantObservedMarker> Observations,
        IReadOnlyDictionary<string, PersistedCase> PersistedCases,
        IReadOnlyList<ProofCandidateDiagnostic> Candidates,
        IReadOnlyList<string> ScanErrors,
        int TotalModelSpaceMLeaderCount,
        int ProofXDataMLeaderCount,
        int InvalidProofPayloadCount);

    public static void Create(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var database = document.Database;
        var editor = document.Editor;
        try
        {
            using var documentLock = document.LockDocument();
            Dictionary<string, CreatedCase> created;
            bool hasDistinctStyleB;
            using (var transaction =
                   database.TransactionManager.StartTransaction())
            {
                var preflight = ReadModelSpacePreflight(database, transaction);
                WriteModelSpacePreflight(editor, preflight);
                if (!preflight.Passed)
                {
                    editor.WriteMessage(
                        "\nAK_DEV_BLOCKVARIANT_CREATE requires a new disposable " +
                        "drawing with empty model space. No drawing data was changed.");
                    return;
                }

                var catalog = AutoCadTextStyleResolver.ReadCatalog(
                    database,
                    transaction);
                var styleA = catalog.CompatibleStyles.FirstOrDefault();
                if (styleA is null)
                {
                    editor.WriteMessage(
                        "\nAK_DEV_BLOCKVARIANT_CREATE: FAIL - no compatible " +
                        "variable-height nonannotative text style exists. " +
                        "No drawing data was changed.");
                    return;
                }
                var styleB = catalog.CompatibleStyles.FirstOrDefault(candidate =>
                    candidate.TextStyleId != styleA.TextStyleId);
                hasDistinctStyleB = styleB is not null;

                EnsureRegApp(database, transaction);
                var modelSpace = OpenModelSpace(
                    database,
                    transaction,
                    OpenMode.ForWrite);
                var batch =
                    new AutoCadItemLeaderBlockVariantBatchCatalog(database);
                created = new Dictionary<string, CreatedCase>(
                    StringComparer.Ordinal);
                var markers = new List<
                    AutoCadItemLeaderBlockVariantProofMarker>();

                foreach (var proofCase in
                         AutoCadItemLeaderBlockVariantProofPolicy.Cases)
                {
                    if (proofCase.StyleSlot ==
                            AutoCadItemLeaderBlockVariantProofStyleSlot.StyleB &&
                        styleB is null)
                    {
                        continue;
                    }

                    var style = proofCase.StyleSlot ==
                        AutoCadItemLeaderBlockVariantProofStyleSlot.StyleA
                            ? styleA
                            : styleB!;
                    var result =
                        AcKrovyItemLeaderBlockVariantService.EnsureResolved(
                            database,
                            transaction,
                            proofCase.ToItemNumberLeaderStyle(),
                            proofCase.Token,
                            batch);
                    if (!result.Succeeded ||
                        result.BlockTableRecordId is not ObjectId blockId ||
                        result.VariantKey is null ||
                        result.CanonicalBlockName is null)
                    {
                        throw new InvalidOperationException(
                            $"Ensure failed for case {proofCase.Token}: " +
                            result.DiagnosticReason);
                    }

                    var marker =
                        AutoCadItemLeaderBlockVariantProofPolicy.CreateMarker(
                            proofCase,
                            result.VariantKey,
                            result.CanonicalBlockName,
                            style.CanonicalName);
                    var attribute = ReadItemNumberAttribute(
                        transaction,
                        blockId,
                        out var attributeId);
                    var leaderId = CreateLeader(
                        database,
                        transaction,
                        modelSpace,
                        blockId,
                        attribute,
                        style,
                        proofCase,
                        marker);
                    markers.Add(marker);

                    created.Add(
                        proofCase.Token,
                        new CreatedCase(
                            proofCase,
                            result,
                            leaderId,
                            attributeId,
                            style.CanonicalName,
                            style.TextStyleId,
                            attribute.Height,
                            marker));
                }

                var manifest =
                    AutoCadItemLeaderBlockVariantProofPolicy.CreateManifest(
                        hasDistinctStyleB
                            ? AutoCadItemLeaderBlockVariantProofStyleBState.Tested
                            : AutoCadItemLeaderBlockVariantProofStyleBState
                                .NotTestedNoSecondCompatibleStyle,
                        styleA.CanonicalName,
                        styleB?.CanonicalName,
                        markers);
                WriteManifest(database, transaction, manifest);
                transaction.Commit();
            }

            ProofScanResult readback;
            AutoCadItemLeaderBlockVariantProofRecoveryResult recovery;
            var definitionValidations = new Dictionary<
                string,
                AutoCadItemLeaderBlockVariantDefinitionValidationResult>(
                StringComparer.Ordinal);
            using (var readTransaction =
                   database.TransactionManager.StartTransaction())
            {
                readback = ScanProofState(database, readTransaction);
                recovery = EvaluateScanRecovery(readback);
                if (recovery.Succeeded)
                {
                    foreach (var persisted in readback.PersistedCases.Values)
                    {
                        if (!ValidatePersistedCase(
                                database,
                                readTransaction,
                                persisted,
                                out var reason,
                                out var definitionValidation))
                        {
                            recovery = new(
                                false,
                                recovery.AcceptedCandidateByCase,
                                [
                                    .. recovery.Errors,
                                    $"Post-commit case {persisted.ProofCase.Token} failed: {reason}",
                                ]);
                        }
                        if (definitionValidation is not null)
                        {
                            definitionValidations[persisted.ProofCase.Token] =
                                definitionValidation;
                        }
                    }
                }
            }

            WriteScanDiagnostics(editor, readback);
            foreach (var validation in definitionValidations)
            {
                WriteDefinitionValidationDiagnostics(
                    editor,
                    validation.Key,
                    validation.Value);
            }
            if (!recovery.Succeeded)
            {
                WriteRecoveryFailures(editor, "CREATE post-commit readback", recovery);
                editor.WriteMessage(
                    "\nAK_DEV_BLOCKVARIANT_CREATE: FAIL - persisted proof is not ready for SAVE/REOPEN verification.");
                return;
            }

            foreach (var item in created.Values)
            {
                WriteCreatedCase(editor, item);
            }
            WriteCreateRelationships(editor, created, hasDistinctStyleB);
            if (!hasDistinctStyleB)
            {
                WriteNotTested(
                    editor,
                    "B",
                    "Persisted manifest records NotTestedNoSecondCompatibleStyle.");
            }
            editor.WriteMessage(
                "\nAK_DEV_BLOCKVARIANT_CREATE post-commit readback: PASS. " +
                "SAVE, CLOSE, REOPEN, " +
                "NETLOAD the Debug DLL, then run AK_DEV_BLOCKVARIANT_VERIFY.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_BLOCKVARIANT_CREATE: FAIL - {exception.Message}");
        }
    }

    public static void Verify(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var database = document.Database;
        var editor = document.Editor;
        try
        {
            using var transaction =
                database.TransactionManager.StartTransaction();
            var scan = ScanProofState(database, transaction);
            WriteScanDiagnostics(editor, scan);
            var recovery = EvaluateScanRecovery(scan);
            if (!recovery.Succeeded || scan.Manifest is null)
            {
                WriteRecoveryFailures(editor, "VERIFY", recovery);
                return;
            }
            var persisted = scan.PersistedCases;
            var distinctStyleB = scan.Manifest.StyleBState ==
                AutoCadItemLeaderBlockVariantProofStyleBState.Tested;

            foreach (var proofCase in
                     AutoCadItemLeaderBlockVariantProofPolicy.Cases)
            {
                if (proofCase.Token == "B" && !distinctStyleB)
                {
                    WriteNotTested(
                        editor,
                        "B",
                        "Persisted manifest records NotTestedNoSecondCompatibleStyle.");
                    continue;
                }
                if (!persisted.TryGetValue(proofCase.Token, out var item))
                {
                    editor.WriteMessage(
                        $"\n{proofCase.Token}: FAIL - persisted manifest expected exactly one ModelSpace proof MLeader.");
                    continue;
                }

                WriteVerifiedCase(database, transaction, editor, item);
            }

            WriteVerifyRelationships(
                editor,
                persisted,
                distinctStyleB);
            editor.WriteMessage(
                "\nAK_DEV_BLOCKVARIANT_VERIFY is read-only. Existing definitions " +
                "were opened ForRead and were not mutated.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_BLOCKVARIANT_VERIFY: FAIL - {exception.Message}");
        }
    }

    public static void Cleanup(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var database = document.Database;
        var editor = document.Editor;
        try
        {
            using var documentLock = document.LockDocument();
            using var transaction =
                database.TransactionManager.StartTransaction();
            var modelSpace = OpenModelSpace(
                database,
                transaction,
                OpenMode.ForRead);
            var markedIds = modelSpace.Cast<ObjectId>()
                .Where(id => transaction.GetObject(
                        id,
                        OpenMode.ForRead,
                        false) is MLeader leader &&
                    TryReadMarker(leader, out _))
                .ToArray();
            foreach (var id in markedIds)
            {
                var leader = (MLeader)transaction.GetObject(
                    id,
                    OpenMode.ForWrite);
                leader.Erase();
            }

            transaction.Commit();
            editor.WriteMessage(
                $"\nAK_DEV_BLOCKVARIANT_CLEAN removed {markedIds.Length} marked " +
                "proof MLeader(s). Text styles, legacy definitions, and variant " +
                "definitions were left untouched; unused variant definitions may " +
                "remain in this disposable proof drawing.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK_DEV_BLOCKVARIANT_CLEAN: FAIL - {exception.Message}");
        }
    }

    private static ObjectId CreateLeader(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        ObjectId blockId,
        AttributeDefinition definition,
        AutoCadTextStyleCatalogEntry style,
        AutoCadItemLeaderBlockVariantProofCase proofCase,
        AutoCadItemLeaderBlockVariantProofMarker marker)
    {
        var leader = new MLeader();
        leader.SetDatabaseDefaults(database);
        leader.EnableAnnotationScale = false;
        leader.Scale = 1d;
        leader.ContentType = ContentType.BlockContent;
        leader.BlockContentId = blockId;
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
        leader.BlockScale = new Scale3d(proofCase.BlockScale);
        leader.BlockRotation = 0d;
        leader.BlockPosition = new Point3d(proofCase.PositionX, 0d, 0d);
        var leaderIndex = leader.AddLeader();
        var lineIndex = leader.AddLeaderLine(leaderIndex);
        leader.AddFirstVertex(
            lineIndex,
            new Point3d(proofCase.PositionX - 300d, -250d, 0d));
        leader.AddLastVertex(
            lineIndex,
            new Point3d(proofCase.PositionX - 100d, 0d, 0d));

        using (var attribute = new AttributeReference())
        {
            attribute.SetAttributeFromBlock(definition, Matrix3d.Identity);
            attribute.TextString = proofCase.Token;
            attribute.TextStyleId = style.TextStyleId;
            attribute.Height = proofCase.DefinitionBaseHeight;
            leader.SetBlockAttribute(definition.ObjectId, attribute);
        }

        var leaderId = modelSpace.AppendEntity(leader);
        transaction.AddNewlyCreatedDBObject(leader, true);
        SetMarker(leader, marker);
        return leaderId;
    }

    private static AttributeDefinition ReadItemNumberAttribute(
        Transaction transaction,
        ObjectId blockId,
        out ObjectId attributeId)
    {
        var block = (BlockTableRecord)transaction.GetObject(
            blockId,
            OpenMode.ForRead);
        attributeId = AcKrovyItemLeaderBlockService.FindItemNumberAttribute(
            block,
            transaction);
        if (attributeId.IsNull ||
            transaction.GetObject(
                attributeId,
                OpenMode.ForRead) is not AttributeDefinition attribute)
        {
            throw new InvalidOperationException(
                $"Block {block.Name} has no ITEM_NO definition.");
        }
        return attribute;
    }

    private static ProofScanResult ScanProofState(
        Database database,
        Transaction transaction)
    {
        var manifest = ReadManifest(database, transaction);
        var observations = new List<
            AutoCadItemLeaderBlockVariantObservedMarker>();
        var persisted = new Dictionary<string, PersistedCase>(
            StringComparer.Ordinal);
        var candidates = new List<ProofCandidateDiagnostic>();
        var scanErrors = new List<string>();
        var totalMLeaders = 0;
        var proofXDataCount = 0;
        var invalidPayloadCount = 0;
        var modelSpace = OpenModelSpace(
            database,
            transaction,
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is not
                    MLeader leader)
            {
                continue;
            }
            totalMLeaders++;

            var regApps = ReadXDataRegAppNames(leader);
            var hasProofXData = regApps.Contains(
                ProofRegAppName,
                StringComparer.Ordinal);
            AutoCadItemLeaderBlockVariantProofMarker? marker = null;
            var decision = "Rejected: proof RegApp/XData is absent.";
            if (hasProofXData)
            {
                proofXDataCount++;
                if (!TryReadMarker(leader, out marker) || marker is null)
                {
                    invalidPayloadCount++;
                    decision = "Rejected: proof payload is invalid or unreadable.";
                }
                else if (marker.SuiteIdentifier !=
                         AutoCadItemLeaderBlockVariantProofPolicy.SuiteIdentifier)
                {
                    decision = "Rejected: marker belongs to another proof suite.";
                }
                else if (!TryCreatePersistedCase(
                             transaction,
                             id,
                             leader,
                             marker,
                             out var persistedCase,
                             out var rejectionReason))
                {
                    decision = "Rejected: " + rejectionReason;
                    scanErrors.Add(
                        $"ModelSpace proof candidate {ReadHandle(id)}: {rejectionReason}");
                }
                else if (!persisted.TryAdd(marker.CaseToken, persistedCase!))
                {
                    decision = "Rejected: duplicate case token.";
                }
                else
                {
                    decision = "Accepted: exact suite and readable marker.";
                }
            }

            observations.Add(
                new AutoCadItemLeaderBlockVariantObservedMarker(
                    AutoCadItemLeaderBlockVariantProofObjectSpace.ModelSpace,
                    hasProofXData,
                    marker,
                    ReadHandle(id)));
            ReadBlockContentDiagnostic(
                transaction,
                leader,
                out var blockHandle,
                out var blockName);
            candidates.Add(
                new ProofCandidateDiagnostic(
                    ReadHandle(id),
                    ReadObjectId(id),
                    ReadHandle(leader.OwnerId),
                    ReadOwnerName(transaction, leader.OwnerId),
                    blockHandle,
                    blockName,
                    regApps.Count == 0
                        ? "<none>"
                        : string.Join(",", regApps),
                    marker?.SchemaVersion,
                    marker?.CaseToken,
                    decision));
        }

        return new ProofScanResult(
            manifest,
            observations.AsReadOnly(),
            persisted,
            candidates.AsReadOnly(),
            scanErrors.AsReadOnly(),
            totalMLeaders,
            proofXDataCount,
            invalidPayloadCount);
    }

    private static bool TryCreatePersistedCase(
        Transaction transaction,
        ObjectId leaderId,
        MLeader leader,
        AutoCadItemLeaderBlockVariantProofMarker marker,
        out PersistedCase? persisted,
        out string reason)
    {
        persisted = null;
        reason = string.Empty;
        var proofCase = AutoCadItemLeaderBlockVariantProofPolicy.Cases
            .SingleOrDefault(candidate => candidate.Token == marker.CaseToken);
        if (proofCase is null)
        {
            reason = $"unknown case token '{marker.CaseToken}'.";
            return false;
        }
        try
        {
            var block = (BlockTableRecord)transaction.GetObject(
                leader.BlockContentId,
                OpenMode.ForRead);
            var attribute = ReadItemNumberAttribute(
                transaction,
                block.ObjectId,
                out var attributeId);
            var textStyle = (TextStyleTableRecord)transaction.GetObject(
                attribute.TextStyleId,
                OpenMode.ForRead);
            persisted = new PersistedCase(
                proofCase,
                marker,
                leaderId,
                block.ObjectId,
                attributeId,
                block.Name,
                textStyle.Name,
                attribute.TextStyleId,
                attribute.Height,
                leader.BlockScale.X);
            return true;
        }
        catch (System.Exception exception) when (
            exception is Autodesk.AutoCAD.Runtime.Exception or
                InvalidOperationException or ObjectDisposedException)
        {
            reason = exception.Message;
            return false;
        }
    }

    private static bool ValidatePersistedCase(
        Database database,
        Transaction transaction,
        PersistedCase item,
        out string reason,
        out AutoCadItemLeaderBlockVariantDefinitionValidationResult?
            definitionValidation)
    {
        definitionValidation = null;
        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(
            item.ProofCase.ToItemNumberLeaderStyle(),
            item.ProofCase.Token);
        var key = AutoCadItemLeaderBlockVariantKey.FromDefinition(definition);
        var expectedKeyPayload =
            AutoCadItemLeaderBlockVariantNamePolicy.CreateFingerprintPayload(key);
        var expectedBlockName =
            AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(key);
        if (item.Marker.VariantKeyPayload != expectedKeyPayload ||
            item.Marker.CanonicalBlockName != expectedBlockName ||
            item.BlockName != expectedBlockName ||
            item.StyleName != item.Marker.ExpectedCanonicalStyleName ||
            !AutoCadItemLeaderBlockVariantProofPolicy.AreClose(
                item.DefinitionHeight,
                TimberItemLeaderBlockDefinitionRules
                    .BaseFramedItemTextHeightAtScale50Mm) ||
            !AutoCadItemLeaderBlockVariantProofPolicy.AreClose(
                item.BlockScale,
                item.Marker.ExpectedBlockScale))
        {
            reason = "Marker, block, style, height, or scale differs from persisted expectations.";
            return false;
        }

        definitionValidation = AcKrovyItemLeaderBlockVariantService
            .ValidateExistingDefinitionDetailed(
            database,
            transaction,
            item.BlockId,
            definition,
            key);
        reason = definitionValidation.Reason;
        return definitionValidation.IsValid;
    }

    private static void WriteCreatedCase(Editor editor, CreatedCase item)
    {
        var key = item.EnsureResult.VariantKey!;
        var blockId = item.EnsureResult.BlockTableRecordId!.Value;
        var checks = new[]
        {
            AutoCadItemLeaderBlockVariantProofPolicy.Evaluate(
                "definition height",
                TimberItemLeaderBlockDefinitionRules
                    .BaseFramedItemTextHeightAtScale50Mm,
                item.DefinitionHeight),
            AutoCadItemLeaderBlockVariantProofPolicy.Evaluate(
                "effective height",
                item.ProofCase.EffectiveHeight,
                item.ProofCase.DefinitionBaseHeight * item.ProofCase.BlockScale),
        };
        WriteCaseHeader(
            editor,
            "CREATE",
            item.ProofCase,
            key,
            item.EnsureResult.CanonicalBlockName!,
            item.EnsureResult.ResolvedBlockName!,
            blockId,
            item.AttributeDefinitionId,
            item.StyleName,
            item.StyleId,
            item.DefinitionHeight,
            item.ProofCase.BlockScale,
            item.EnsureResult.Kind.ToString());
        WriteChecks(editor, checks);
    }

    private static void WriteVerifiedCase(
        Database database,
        Transaction transaction,
        Editor editor,
        PersistedCase item)
    {
        var definition = TimberItemLeaderBlockDefinitionRules.Resolve(
            item.ProofCase.ToItemNumberLeaderStyle(),
            item.ProofCase.Token);
        var key = AutoCadItemLeaderBlockVariantKey.FromDefinition(definition);
        var canonicalName =
            AutoCadItemLeaderBlockVariantNamePolicy.CreateCanonicalName(key);
        var contentValid = ValidatePersistedCase(
            database,
            transaction,
            item,
            out var contentReason,
            out var definitionValidation);
        var checks = new[]
        {
            AutoCadItemLeaderBlockVariantProofPolicy.Evaluate(
                "block name",
                canonicalName,
                item.BlockName),
            AutoCadItemLeaderBlockVariantProofPolicy.Evaluate(
                "style name",
                item.Marker.ExpectedCanonicalStyleName,
                item.StyleName),
            AutoCadItemLeaderBlockVariantProofPolicy.Evaluate(
                "definition height",
                item.ProofCase.DefinitionBaseHeight,
                item.DefinitionHeight),
            AutoCadItemLeaderBlockVariantProofPolicy.Evaluate(
                "BlockScale",
                item.ProofCase.BlockScale,
                item.BlockScale),
            AutoCadItemLeaderBlockVariantProofPolicy.Evaluate(
                "effective height",
                item.ProofCase.EffectiveHeight,
                item.DefinitionHeight * item.BlockScale),
            new AutoCadItemLeaderBlockVariantProofCheck(
                "immutable definition content",
                contentValid
                    ? AutoCadItemLeaderBlockVariantProofStatus.Pass
                    : AutoCadItemLeaderBlockVariantProofStatus.Fail,
                "complete key/content match",
                contentReason),
        };
        WriteCaseHeader(
            editor,
            "VERIFY",
            item.ProofCase,
            key,
            canonicalName,
            item.BlockName,
            item.BlockId,
            item.AttributeDefinitionId,
            item.StyleName,
            item.StyleId,
            item.DefinitionHeight,
            item.BlockScale,
            "ReusedExisting");
        if (definitionValidation is not null)
        {
            WriteDefinitionValidationDiagnostics(
                editor,
                item.ProofCase.Token,
                definitionValidation);
        }
        WriteChecks(editor, checks);
    }

    private static void WriteCreateRelationships(
        Editor editor,
        IReadOnlyDictionary<string, CreatedCase> cases,
        bool hasDistinctStyleB)
    {
        var a = cases["A"];
        var c = cases["C"];
        var d = cases["D"];
        var e = cases["E"];
        WriteRelationship(editor, "A/C same BlockTableRecord", a, c, true);
        WriteRelationship(editor, "A/E same BlockTableRecord", a, e, true);
        WriteRelationship(editor, "A/D different frame definition", a, d, false);
        if (hasDistinctStyleB)
        {
            WriteRelationship(
                editor,
                "A/B different style/height definition",
                a,
                cases["B"],
                false);
        }
        else
        {
            WriteNotTested(
                editor,
                "A/B",
                "Only one compatible style exists.");
        }
    }

    private static void WriteVerifyRelationships(
        Editor editor,
        IReadOnlyDictionary<string, PersistedCase> cases,
        bool hasDistinctStyleB)
    {
        if (!cases.TryGetValue("A", out var a) ||
            !cases.TryGetValue("C", out var c) ||
            !cases.TryGetValue("D", out var d) ||
            !cases.TryGetValue("E", out var e))
        {
            editor.WriteMessage(
                "\nRELATIONSHIPS: FAIL - A, C, D, or E proof leader is missing.");
            return;
        }
        WriteRelationship(editor, "A/C same BlockTableRecord", a, c, true);
        WriteRelationship(editor, "A/E same BlockTableRecord", a, e, true);
        WriteRelationship(editor, "A/D different frame definition", a, d, false);
        if (hasDistinctStyleB && cases.TryGetValue("B", out var b))
        {
            WriteRelationship(
                editor,
                "A/B different style/height definition",
                a,
                b,
                false);
        }
        else
        {
            WriteNotTested(editor, "A/B", "Style variation was not available.");
        }
    }

    private static void WriteRelationship(
        Editor editor,
        string name,
        CreatedCase left,
        CreatedCase right,
        bool shouldEqual)
    {
        var leftId = left.EnsureResult.BlockTableRecordId!.Value;
        var rightId = right.EnsureResult.BlockTableRecordId!.Value;
        WriteRelationship(editor, name, leftId, rightId, shouldEqual);
    }

    private static void WriteRelationship(
        Editor editor,
        string name,
        PersistedCase left,
        PersistedCase right,
        bool shouldEqual) =>
        WriteRelationship(editor, name, left.BlockId, right.BlockId, shouldEqual);

    private static void WriteRelationship(
        Editor editor,
        string name,
        ObjectId left,
        ObjectId right,
        bool shouldEqual)
    {
        var passed = (left == right) == shouldEqual;
        editor.WriteMessage(
            $"\n{name}: {(passed ? "PASS" : "FAIL")}; " +
            $"left={left.Handle}, right={right.Handle}");
    }

    private static void WriteCaseHeader(
        Editor editor,
        string phase,
        AutoCadItemLeaderBlockVariantProofCase proofCase,
        AutoCadItemLeaderBlockVariantKey key,
        string canonicalName,
        string resolvedName,
        ObjectId blockId,
        ObjectId attributeId,
        string styleName,
        ObjectId styleId,
        double definitionHeight,
        double blockScale,
        string resultKind)
    {
        editor.WriteMessage(
            $"\n{phase} {proofCase.Token}: key=[{AutoCadItemLeaderBlockVariantNamePolicy.CreateFingerprintPayload(key)}]" +
            $"\n  canonicalBlock={canonicalName}, resolvedBlock={resolvedName}, " +
            $"BlockTableRecord={blockId.Handle}, ITEM_NO={attributeId.Handle}" +
            $"\n  frame={key.FrameKind}, size={key.FrameSize}, " +
            $"geometryVersion={key.GeometryVersion}, style={styleName}, " +
            $"styleObjectId={styleId.Handle}" +
            $"\n  paperHeight={AutoCadItemLeaderBlockVariantProofPolicy.Format(proofCase.ItemNumberPaperHeightMm)}, " +
            $"definitionBaseHeight={AutoCadItemLeaderBlockVariantProofPolicy.Format(definitionHeight)}, " +
            $"BlockScale={AutoCadItemLeaderBlockVariantProofPolicy.Format(blockScale)}, " +
            $"effectiveHeight={AutoCadItemLeaderBlockVariantProofPolicy.Format(definitionHeight * blockScale)}, " +
            $"resultKind={resultKind}");
    }

    private static void WriteChecks(
        Editor editor,
        IEnumerable<AutoCadItemLeaderBlockVariantProofCheck> checks)
    {
        foreach (var check in checks)
        {
            editor.WriteMessage(
                $"\n  {check.Name}: {check.Status}; expected={check.Expected}; " +
                $"actual={check.Actual}");
        }
    }

    private static void WriteDefinitionValidationDiagnostics(
        Editor editor,
        string caseToken,
        AutoCadItemLeaderBlockVariantDefinitionValidationResult result)
    {
        var diagnostic = result.Diagnostic;
        editor.WriteMessage(
            $"\nDEFINITION VALIDATION {caseToken}: " +
            $"{(result.IsValid ? "PASS" : "FAIL")}; " +
            $"reasonCode={result.ReasonCode}; reason={result.Reason}" +
            $"\n  block name={diagnostic.BlockName}, " +
            $"handle={diagnostic.BlockHandle}, " +
            $"ObjectId={diagnostic.BlockObjectId}, " +
            $"databaseIdentity={diagnostic.DatabaseIdentity}" +
            $"\n  entityCount={diagnostic.EntityCount}, " +
            $"AttributeDefinitionCount={diagnostic.AttributeDefinitionCount}, " +
            $"frameSignature={diagnostic.FrameSignature}");
        if (diagnostic.Attribute is not { } attribute)
        {
            editor.WriteMessage("\n  ITEM_NO snapshot: <unavailable>");
        }
        else
        {
            editor.WriteMessage(
                $"\n  ITEM_NO handle={diagnostic.AttributeHandle}, " +
                $"ObjectId={diagnostic.AttributeObjectId}, " +
                $"ownerHandle={diagnostic.AttributeOwnerHandle}, " +
                $"ownerName={diagnostic.AttributeOwnerName}, " +
                $"ownerMatchesBlock={attribute.OwnerMatchesBlock}" +
                $"\n  Tag={attribute.Tag}, Prompt={attribute.Prompt}, " +
                $"TextString/default={attribute.DefaultText}" +
                $"\n  Height={AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Format(attribute.Height)}, " +
                $"TextStyleId={attribute.TextStyleObjectId}, " +
                $"styleIdValid={attribute.TextStyleIdIsValid}, " +
                $"styleDatabaseMatch={attribute.TextStyleBelongsToDatabase}, " +
                $"styleRuntimeIdMatch={attribute.TextStyleMatchesResolvedRuntimeId}" +
                $"\n  canonicalStyle={attribute.CanonicalTextStyleName}, " +
                $"styleFixedHeight=" +
                $"{AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Format(attribute.TextStyleFixedHeight)}, " +
                $"styleAnnotative={attribute.TextStyleAnnotativeState}" +
                $"\n  Position=({AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Format(attribute.PositionX)}, " +
                $"{AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Format(attribute.PositionY)}, " +
                $"{AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Format(attribute.PositionZ)}) " +
                "[host-derived diagnostic; not variant identity]" +
                $"\n  AlignmentPoint=({AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Format(attribute.AlignmentX)}, " +
                $"{AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Format(attribute.AlignmentY)}, " +
                $"{AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Format(attribute.AlignmentZ)}), " +
                $"HorizontalMode={attribute.HorizontalMode}, " +
                $"VerticalMode={attribute.VerticalMode}, " +
                $"Rotation={AutoCadItemLeaderBlockVariantAttributeValidationPolicy.Format(attribute.Rotation)}" +
                $"\n  LockPositionInBlock={attribute.LockPositionInBlock}, " +
                $"Constant={attribute.Constant}, Invisible={attribute.Invisible}, " +
                $"Preset={attribute.Preset}, Verifiable={attribute.Verifiable}, " +
                $"IsMTextAttributeDefinition=" +
                $"{attribute.IsMTextAttributeDefinition}, " +
                $"IsErased={attribute.IsErased}, " +
                $"ByBlockAppearance={attribute.HasByBlockAppearance}");
        }

        foreach (var field in result.FieldChecks)
        {
            editor.WriteMessage(
                $"\n    {field.PropertyName}: " +
                $"{(field.Passed ? "PASS" : "FAIL")}; " +
                $"expected={field.Expected}; actual={field.Actual}; " +
                $"tolerance={field.Tolerance}");
        }
    }

    private static void WriteNotTested(
        Editor editor,
        string token,
        string reason) =>
        editor.WriteMessage($"\n{token}: NOT TESTED - {reason}");

    private static AutoCadItemLeaderBlockVariantProofPreflightResult
        ReadModelSpacePreflight(
        Database database,
        Transaction transaction)
    {
        var blockTable = (BlockTable)transaction.GetObject(
            database.BlockTableId,
            OpenMode.ForRead);
        var modelSpaceId = blockTable[BlockTableRecord.ModelSpace];
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            modelSpaceId,
            OpenMode.ForRead);
        var snapshots = new List<
            AutoCadItemLeaderBlockVariantProofObjectSnapshot>();
        foreach (ObjectId id in modelSpace)
        {
            snapshots.Add(ReadModelSpaceObjectSnapshot(
                transaction,
                modelSpace,
                id));
        }

        return AutoCadItemLeaderBlockVariantProofPreflightPolicy.Evaluate(
            snapshots,
            currentSpaceIsModelSpace: database.CurrentSpaceId == modelSpaceId);
    }

    private static AutoCadItemLeaderBlockVariantProofObjectSnapshot
        ReadModelSpaceObjectSnapshot(
        Transaction transaction,
        BlockTableRecord modelSpace,
        ObjectId id)
    {
        var objectIdText = ReadObjectId(id);
        var handle = ReadHandle(id);
        try
        {
            if (!id.IsValid)
            {
                return InvalidModelSpaceObject(handle, objectIdText);
            }

            var dbObject = transaction.GetObject(
                id,
                OpenMode.ForRead,
                true);
            if (dbObject is not Entity entity)
            {
                return new AutoCadItemLeaderBlockVariantProofObjectSnapshot(
                    AutoCadItemLeaderBlockVariantProofObjectSpace.ModelSpace,
                    true,
                    false,
                    dbObject.IsErased,
                    dbObject.OwnerId == modelSpace.ObjectId,
                    handle,
                    objectIdText,
                    dbObject.GetRXClass()?.DxfName ?? "<none>",
                    dbObject.GetRXClass()?.Name ?? "<unknown>",
                    "<not-an-entity>",
                    ReadHandle(dbObject.OwnerId),
                    ReadOwnerName(transaction, dbObject.OwnerId));
            }

            return new AutoCadItemLeaderBlockVariantProofObjectSnapshot(
                AutoCadItemLeaderBlockVariantProofObjectSpace.ModelSpace,
                true,
                true,
                entity.IsErased,
                entity.OwnerId == modelSpace.ObjectId,
                handle,
                objectIdText,
                entity.GetRXClass()?.DxfName ?? "<none>",
                entity.GetRXClass()?.Name ?? "<unknown>",
                entity.Layer,
                ReadHandle(entity.OwnerId),
                ReadOwnerName(transaction, entity.OwnerId));
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return InvalidModelSpaceObject(handle, objectIdText);
        }
        catch (ObjectDisposedException)
        {
            return InvalidModelSpaceObject(handle, objectIdText);
        }
    }

    private static AutoCadItemLeaderBlockVariantProofObjectSnapshot
        InvalidModelSpaceObject(string handle, string objectId) =>
        new(
            AutoCadItemLeaderBlockVariantProofObjectSpace.ModelSpace,
            false,
            false,
            false,
            false,
            handle,
            objectId,
            "<unavailable>",
            "<unavailable>",
            "<unavailable>",
            "<unavailable>",
            BlockTableRecord.ModelSpace);

    private static string ReadOwnerName(
        Transaction transaction,
        ObjectId ownerId)
    {
        try
        {
            return transaction.GetObject(
                    ownerId,
                    OpenMode.ForRead,
                    true) is BlockTableRecord owner
                ? owner.Name
                : "<not-a-block-table-record>";
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return "<unavailable>";
        }
        catch (ObjectDisposedException)
        {
            return "<unavailable>";
        }
    }

    private static string ReadHandle(ObjectId id)
    {
        try
        {
            return id.IsNull ? "<null>" : id.Handle.ToString();
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return "<unavailable>";
        }
        catch (ObjectDisposedException)
        {
            return "<unavailable>";
        }
    }

    private static string ReadObjectId(ObjectId id)
    {
        try
        {
            return id.ToString();
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return "<unavailable>";
        }
        catch (ObjectDisposedException)
        {
            return "<unavailable>";
        }
    }

    private static void WriteModelSpacePreflight(
        Editor editor,
        AutoCadItemLeaderBlockVariantProofPreflightResult result)
    {
        if (result.Passed)
        {
            editor.WriteMessage(
                "\nModel space preflight: PASS, entity count = 0." +
                $" CurrentSpaceIsModelSpace={result.CurrentSpaceIsModelSpace}." +
                $" Ignored invalid model-space ObjectIds=" +
                $"{result.InvalidModelSpaceObjectCount}.");
            return;
        }

        editor.WriteMessage(
            $"\nModel space preflight: FAIL, entity count = " +
            $"{result.BlockingModelSpaceEntities.Count}. " +
            $"CurrentSpaceIsModelSpace={result.CurrentSpaceIsModelSpace}. " +
            $"Ignored invalid model-space ObjectIds=" +
            $"{result.InvalidModelSpaceObjectCount}.");
        foreach (var entity in result.BlockingModelSpaceEntities.Take(20))
        {
            editor.WriteMessage(
                $"\n  handle={entity.Handle}, ObjectId={entity.ObjectId}, " +
                $"DXF={entity.DxfName}, RXClass={entity.RxClassName}, " +
                $"layer={entity.Layer}, ownerHandle={entity.OwnerHandle}, " +
                $"ownerName={entity.OwnerName}, IsErased={entity.IsErased}, " +
                $"OwnerIsModelSpace={entity.OwnerIsModelSpace}");
        }
        if (result.BlockingModelSpaceEntities.Count > 20)
        {
            editor.WriteMessage(
                $"\n  ... {result.BlockingModelSpaceEntities.Count - 20} " +
                "additional blocking model-space entities omitted.");
        }
    }

    private static void EnsureRegApp(
        Database database,
        Transaction transaction)
    {
        var table = (RegAppTable)transaction.GetObject(
            database.RegAppTableId,
            OpenMode.ForRead);
        if (table.Has(ProofRegAppName))
        {
            return;
        }
        table.UpgradeOpen();
        var record = new RegAppTableRecord { Name = ProofRegAppName };
        table.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
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

    private static void WriteManifest(
        Database database,
        Transaction transaction,
        AutoCadItemLeaderBlockVariantProofManifest manifest)
    {
        var values = AutoCadItemLeaderBlockVariantProofPolicy
            .SerializeManifest(manifest)
            .Select(chunk => new TypedValue((int)DxfCode.Text, chunk))
            .ToArray();
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        Xrecord record;
        if (namedObjects.Contains(ProofManifestDictionaryKey))
        {
            record = (Xrecord)transaction.GetObject(
                namedObjects.GetAt(ProofManifestDictionaryKey),
                OpenMode.ForWrite);
        }
        else
        {
            namedObjects.UpgradeOpen();
            record = new Xrecord();
            namedObjects.SetAt(ProofManifestDictionaryKey, record);
            transaction.AddNewlyCreatedDBObject(record, true);
        }
        using var data = new ResultBuffer(values);
        record.Data = data;
    }

    private static AutoCadItemLeaderBlockVariantProofManifest? ReadManifest(
        Database database,
        Transaction transaction)
    {
        var namedObjects = (DBDictionary)transaction.GetObject(
            database.NamedObjectsDictionaryId,
            OpenMode.ForRead);
        if (!namedObjects.Contains(ProofManifestDictionaryKey) ||
            transaction.GetObject(
                namedObjects.GetAt(ProofManifestDictionaryKey),
                OpenMode.ForRead) is not Xrecord record)
        {
            return null;
        }
        using var data = record.Data;
        var chunks = data?.AsArray()
            .Where(value => value.TypeCode == (int)DxfCode.Text)
            .Select(value => value.Value as string ?? string.Empty)
            .ToArray() ?? [];
        return AutoCadItemLeaderBlockVariantProofPolicy.TryDeserializeManifest(
                chunks,
                out var manifest)
            ? manifest
            : null;
    }

    private static void SetMarker(
        MLeader leader,
        AutoCadItemLeaderBlockVariantProofMarker marker)
    {
        var values = new List<TypedValue>
        {
            new(
                (int)DxfCode.ExtendedDataRegAppName,
                ProofRegAppName),
        };
        values.AddRange(
            AutoCadItemLeaderBlockVariantProofPolicy.SerializeMarker(marker)
                .Select(chunk => new TypedValue(
                    (int)DxfCode.ExtendedDataAsciiString,
                    chunk)));
        using var buffer = new ResultBuffer(values.ToArray());
        leader.XData = buffer;
    }

    private static bool TryReadMarker(
        MLeader leader,
        out AutoCadItemLeaderBlockVariantProofMarker? marker)
    {
        using var buffer = leader.GetXDataForApplication(ProofRegAppName);
        var chunks = buffer?.AsArray()
            .Where(item => item.TypeCode ==
                (int)DxfCode.ExtendedDataAsciiString)
            .Select(item => item.Value as string ?? string.Empty)
            .ToArray() ?? [];
        return AutoCadItemLeaderBlockVariantProofPolicy.TryDeserializeMarker(
            chunks,
            out marker);
    }

    private static IReadOnlyList<string> ReadXDataRegAppNames(MLeader leader)
    {
        try
        {
            using var buffer = leader.XData;
            return Array.AsReadOnly(
                buffer?.AsArray()
                    .Where(value => value.TypeCode ==
                        (int)DxfCode.ExtendedDataRegAppName)
                    .Select(value => value.Value as string ?? string.Empty)
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray() ?? []);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static void ReadBlockContentDiagnostic(
        Transaction transaction,
        MLeader leader,
        out string handle,
        out string name)
    {
        handle = ReadHandle(leader.BlockContentId);
        name = "<unavailable>";
        try
        {
            if (!leader.BlockContentId.IsNull &&
                transaction.GetObject(
                    leader.BlockContentId,
                    OpenMode.ForRead,
                    true) is BlockTableRecord block)
            {
                name = block.Name;
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            name = "<unavailable>";
        }
    }

    private static AutoCadItemLeaderBlockVariantProofRecoveryResult
        EvaluateScanRecovery(ProofScanResult scan)
    {
        var result = AutoCadItemLeaderBlockVariantProofPolicy.EvaluateRecovery(
            scan.Manifest,
            scan.Observations);
        var errors = result.Errors.Concat(scan.ScanErrors).ToList();
        if (result.Succeeded &&
            scan.PersistedCases.Count !=
                result.AcceptedCandidateByCase.Count)
        {
            errors.Add(
                "One or more accepted marker entities could not be read as complete block-content MLeaders.");
        }
        return new AutoCadItemLeaderBlockVariantProofRecoveryResult(
            errors.Count == 0,
            result.AcceptedCandidateByCase,
            errors.AsReadOnly());
    }

    private static void WriteScanDiagnostics(
        Editor editor,
        ProofScanResult scan)
    {
        editor.WriteMessage(
            $"\nProof recovery scan: ModelSpace MLeaders=" +
            $"{scan.TotalModelSpaceMLeaderCount}, proof XData=" +
            $"{scan.ProofXDataMLeaderCount}, invalid payloads=" +
            $"{scan.InvalidProofPayloadCount}, manifest=" +
            $"{(scan.Manifest is null ? "MISSING/INVALID" : "VALID")}.");
        if (scan.TotalModelSpaceMLeaderCount == 0)
        {
            editor.WriteMessage(
                "\n  Situation A: proof MLeader entities are physically absent from ModelSpace.");
        }
        else if (scan.ProofXDataMLeaderCount == 0)
        {
            editor.WriteMessage(
                "\n  Situation B: ModelSpace MLeaders exist, but none has the exact proof RegApp/XData.");
        }
        else if (scan.InvalidProofPayloadCount > 0)
        {
            editor.WriteMessage(
                "\n  Situation B: proof XData exists, but at least one payload is unreadable.");
        }

        foreach (var candidate in scan.Candidates.Take(20))
        {
            editor.WriteMessage(
                $"\n  MLeader handle={candidate.Handle}, ObjectId={candidate.ObjectId}, " +
                $"ownerHandle={candidate.OwnerHandle}, ownerName={candidate.OwnerName}, " +
                $"blockHandle={candidate.BlockContentHandle}, " +
                $"blockName={candidate.BlockContentName}, RegApps=" +
                $"{candidate.XDataRegAppNames}, markerSchema=" +
                $"{candidate.MarkerSchema?.ToString() ?? "<none>"}, case=" +
                $"{candidate.CaseToken ?? "<none>"}, {candidate.DecisionReason}");
        }
        if (scan.Candidates.Count > 20)
        {
            editor.WriteMessage(
                $"\n  ... {scan.Candidates.Count - 20} additional ModelSpace MLeaders omitted.");
        }
    }

    private static void WriteRecoveryFailures(
        Editor editor,
        string phase,
        AutoCadItemLeaderBlockVariantProofRecoveryResult recovery)
    {
        foreach (var error in recovery.Errors)
        {
            editor.WriteMessage($"\n{phase}: FAIL - {error}");
        }
    }
}
#endif
