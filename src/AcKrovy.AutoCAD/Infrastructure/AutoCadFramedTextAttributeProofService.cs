#if DEBUG
using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class AutoCadFramedTextAttributeProofService
{
    internal const string ProofRegAppName = "AK23_TEXTATTR_PROOF";

    public static void Create(Document document) =>
        CreateCore(
            document,
            AutoCadFramedTextAttributeProofPolicy.Cases,
            verifyStyleVariation: true,
            successMessage: "\nAK23 text-attribute proof set created atomically. " +
                "Running a fresh read-transaction verification...",
            verifyAfterCreate: Verify);

    public static void CreateHeight(Document document)
    {
        var heightCases = AutoCadFramedTextAttributeProofPolicy.HeightCases;
        if (heightCases.Count != 2 ||
            heightCases.Any(proofCase =>
                !AutoCadFramedTextAttributeProofPolicy.IsHeightCapabilityCase(proofCase)))
        {
            throw new InvalidOperationException(
                "Height proof requires exactly AK23_HEIGHT_A (2.7→135) and " +
                "AK23_HEIGHT_B (3.5→175); do not use AK23_PROOF_* style cases.");
        }

        CreateCore(
            document,
            heightCases,
            verifyStyleVariation: false,
            successMessage:
                "\nAK_DEV_FRAMED_ITEM_HEIGHT_CREATE: height proof set created " +
                "atomically (AK23_HEIGHT_A base=135, AK23_HEIGHT_B base=175). " +
                "Running a fresh read-transaction verification...",
            verifyAfterCreate: VerifyHeight);
    }

    public static void Verify(Document document) =>
        VerifyCore(
            document,
            AutoCadFramedTextAttributeProofPolicy.Cases,
            verifyStyleVariation: true,
            passMessage: "\nAK23 host proof automated checks: PASS. " +
                "Complete the documented visual and SAVE/CLOSE/REOPEN checks.",
            passWithNotTestedMessage:
                "\nAK23 host proof automated checks: PASS WITH NOT TESTED " +
                "(A/B style variation). Complete the documented visual and " +
                "SAVE/CLOSE/REOPEN checks.",
            failMessage: "\nAK23 host proof automated checks: FAIL.");

    public static void VerifyHeight(Document document) =>
        VerifyCore(
            document,
            AutoCadFramedTextAttributeProofPolicy.HeightCases,
            verifyStyleVariation: false,
            passMessage:
                "\nAK_DEV_FRAMED_ITEM_HEIGHT_VERIFY: PASS " +
                "(A base=135, B base=175, shared BlockContentId, AttrDef unchanged).",
            passWithNotTestedMessage:
                "\nAK_DEV_FRAMED_ITEM_HEIGHT_VERIFY: PASS.",
            failMessage: "\nAK_DEV_FRAMED_ITEM_HEIGHT_VERIFY: FAIL.");

    public static void Clean(Document document) =>
        CleanCore(
            document,
            heightCapabilityOnly: false,
            successPrefix: "AK_DEV_FRAMED_ITEM_HEIGHT_CLEAN");

    public static void CleanHeight(Document document) =>
        CleanCore(
            document,
            heightCapabilityOnly: true,
            successPrefix: "AK_DEV_FRAMED_ITEM_HEIGHT_CLEAN");

    private static void CreateCore(
        Document document,
        IReadOnlyList<AutoCadFramedTextAttributeProofCase> cases,
        bool verifyStyleVariation,
        string successMessage,
        Action<Document> verifyAfterCreate)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(verifyAfterCreate);
        if (cases.Count == 0)
        {
            throw new ArgumentException("At least one proof case is required.", nameof(cases));
        }

        var database = document.Database;
        var editor = document.Editor;
        _ = verifyStyleVariation;

        try
        {
            ProofStyleSelection styles;
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var catalogRead = AutoCadTextStyleResolver.ReadCatalogWithDiagnostics(
                    database,
                    transaction);
                WriteTextStyleDiagnostics(editor, catalogRead);
                if (!catalogRead.TableReadSucceeded)
                {
                    editor.WriteMessage(
                        "\nAK23 proof creation aborted because the TextStyleTable " +
                        "could not be read. No drawing data was changed.");
                    return;
                }
                if (!TryValidateEmptyModelSpace(database, transaction, editor))
                {
                    return;
                }
                if (!TrySelectStyles(catalogRead.Catalog, editor, out styles))
                {
                    return;
                }
            }

            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var block = AcKrovyItemLeaderBlockService.Ensure(
                    database,
                    transaction,
                    ItemNumberLeaderStyle.Circle,
                    cases[0].Token,
                    preserveExistingDefinition: true);
                var blockRecord = (BlockTableRecord)transaction.GetObject(
                    block.BlockId,
                    OpenMode.ForRead);
                var definition = (AttributeDefinition)transaction.GetObject(
                    block.AttributeDefinitionId,
                    OpenMode.ForRead);
                var snapshot = CaptureSnapshot(definition);
                var definitionStyleName = ReadCanonicalStyleName(
                    database,
                    transaction,
                    definition.TextStyleId);
                WriteDefinitionSnapshot(
                    editor,
                    definition,
                    definitionStyleName);

                EnsureProofRegApp(database, transaction);
                var modelSpace = (BlockTableRecord)transaction.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(database),
                    OpenMode.ForWrite);

                foreach (var proofCase in cases)
                {
                    var style = proofCase.StyleSlot ==
                        AutoCadFramedTextAttributeProofStyleSlot.StyleA
                            ? styles.StyleA
                            : styles.StyleB;
                    CreateLeader(
                        database,
                        transaction,
                        modelSpace,
                        blockRecord,
                        definition,
                        snapshot,
                        proofCase,
                        style,
                        styles.HasDistinctStyleB,
                        definitionStyleName,
                        editor);
                }

                if (!AutoCadFramedTextAttributeProofPolicy.SnapshotsMatch(
                        snapshot,
                        CaptureSnapshot(definition)))
                {
                    throw new InvalidOperationException(
                        "The shared AttributeDefinition changed during proof creation.");
                }

                transaction.Commit();
            }

            editor.WriteMessage(successMessage);
            verifyAfterCreate(document);
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK23 proof creation aborted; transaction rolled back: {exception.Message}");
        }
    }

    private static void VerifyCore(
        Document document,
        IReadOnlyList<AutoCadFramedTextAttributeProofCase> cases,
        bool verifyStyleVariation,
        string passMessage,
        string passWithNotTestedMessage,
        string failMessage)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(cases);
        var database = document.Database;
        var editor = document.Editor;

        try
        {
            using var transaction = database.TransactionManager.StartTransaction();
            var catalog = AutoCadTextStyleResolver.ReadCatalog(database, transaction);
            var references = ReadProofReferences(
                database,
                transaction,
                out var corruptPayloadCount);

            if (corruptPayloadCount > 0)
            {
                WriteStatus(
                    editor,
                    AutoCadFramedTextAttributeProofCheckResult.InvalidEnvironment(
                        "proof payload",
                        $"{corruptPayloadCount} proof MLeader(s) have corrupt XData."));
                return;
            }

            var scopedReferences = references
                .Where(reference => cases.Any(proofCase =>
                    string.Equals(
                        proofCase.Token,
                        reference.Payload.CaseToken,
                        StringComparison.Ordinal)))
                .ToArray();

            var duplicate = scopedReferences
                .GroupBy(reference => reference.Payload.CaseToken, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() != 1);
            if (duplicate is not null)
            {
                WriteStatus(
                    editor,
                    AutoCadFramedTextAttributeProofCheckResult.InvalidEnvironment(
                        "proof set",
                        $"Duplicate token: {duplicate.Key}."));
                return;
            }

            var allPassed = true;
            var styleVariationNotTested = false;
            string? commonBlockHandle = null;
            AutoCadFramedTextAttributeProofPayload? payloadA = null;
            AutoCadFramedTextAttributeProofPayload? payloadB = null;

            foreach (var proofCase in cases)
            {
                var reference = scopedReferences.SingleOrDefault(candidate =>
                    string.Equals(
                        candidate.Payload.CaseToken,
                        proofCase.Token,
                        StringComparison.Ordinal));
                if (reference is null)
                {
                    WriteStatus(
                        editor,
                        AutoCadFramedTextAttributeProofCheckResult.Evaluated(
                            proofCase.Token,
                            false,
                            "one persisted MLeader",
                            "missing"));
                    allPassed = false;
                    continue;
                }

                if (proofCase.Token == "AK23_PROOF_A")
                {
                    payloadA = reference.Payload;
                }
                else if (proofCase.Token == "AK23_PROOF_B")
                {
                    payloadB = reference.Payload;
                }

                var expectedStyle = catalog.FindCompatible(
                    reference.Payload.ExpectedStyleName);
                if (expectedStyle is null ||
                    !string.Equals(
                        expectedStyle.TextStyleId.Handle.ToString(),
                        reference.Payload.ExpectedStyleHandle,
                        StringComparison.OrdinalIgnoreCase))
                {
                    WriteStatus(
                        editor,
                        AutoCadFramedTextAttributeProofCheckResult.InvalidEnvironment(
                            $"{proofCase.Token} style",
                            "The expected compatible text style was removed or replaced."));
                    return;
                }

                var casePassed = VerifyLeader(
                    database,
                    transaction,
                    reference,
                    expectedStyle,
                    ref commonBlockHandle,
                    editor);
                allPassed &= casePassed;
            }

            if (verifyStyleVariation && payloadA is not null && payloadB is not null)
            {
                if (!payloadB.DistinctStyleComparisonExpected)
                {
                    styleVariationNotTested = true;
                    WriteStatus(
                        editor,
                        AutoCadFramedTextAttributeProofCheckResult.NotTested(
                            "A/B style variation",
                            "Only one compatible text style was available at creation."));
                }
                else
                {
                    var distinct = !string.Equals(
                        payloadA.ExpectedStyleHandle,
                        payloadB.ExpectedStyleHandle,
                        StringComparison.OrdinalIgnoreCase);
                    WriteStatus(
                        editor,
                        AutoCadFramedTextAttributeProofCheckResult.Evaluated(
                            "A/B style variation",
                            distinct,
                            "different TextStyleId handles",
                            distinct ? "different" : "same"));
                    allPassed &= distinct;
                }
            }

            editor.WriteMessage(
                allPassed
                    ? styleVariationNotTested
                        ? passWithNotTestedMessage
                        : passMessage
                    : failMessage);
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK23 proof verification INVALID ENVIRONMENT: {exception.Message}");
        }
    }

    private static void CleanCore(
        Document document,
        bool heightCapabilityOnly,
        string successPrefix)
    {
        ArgumentNullException.ThrowIfNull(document);
        var database = document.Database;
        var editor = document.Editor;
        try
        {
            using var transaction = database.TransactionManager.StartTransaction();
            var modelSpace = (BlockTableRecord)transaction.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(database),
                OpenMode.ForRead);
            var erased = 0;
            foreach (ObjectId id in modelSpace)
            {
                if (transaction.GetObject(id, OpenMode.ForRead, false) is not
                        MLeader leader)
                {
                    continue;
                }

                using var xdata = leader.GetXDataForApplication(ProofRegAppName);
                if (xdata is null)
                {
                    continue;
                }

                if (heightCapabilityOnly)
                {
                    var chunks = xdata.AsArray()
                        .Where(value => value.TypeCode ==
                            (int)DxfCode.ExtendedDataAsciiString)
                        .Select(value => value.Value as string ?? string.Empty)
                        .ToArray();
                    if (!AutoCadFramedTextAttributeProofPolicy.TryDeserializePayload(
                            chunks,
                            out var payload) ||
                        payload is null ||
                        !payload.CaseToken.StartsWith(
                            "AK23_HEIGHT_",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                leader.UpgradeOpen();
                leader.Erase();
                erased++;
            }

            transaction.Commit();
            editor.WriteMessage(
                $"\n{successPrefix}: erased {erased} proof MLeader(s).");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\n{successPrefix}: FAIL - {exception.Message}");
        }
    }

    public static void DiagnoseTextStyles(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var database = document.Database;
        var editor = document.Editor;
        try
        {
            using var transaction = database.TransactionManager.StartTransaction();
            var result = AutoCadTextStyleResolver.ReadCatalogWithDiagnostics(
                database,
                transaction);
            WriteTextStyleDiagnostics(editor, result);
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage(
                $"\nAK23 text-style diagnostics failed read-only: {exception.Message}");
        }
    }

    private static bool TryValidateEmptyModelSpace(
        Database database,
        Transaction transaction,
        Editor editor)
    {
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database),
            OpenMode.ForRead);
        if (modelSpace.Cast<ObjectId>().Any())
        {
            editor.WriteMessage(
                "\nAK23 proof creation requires a new disposable drawing with empty model space. " +
                "No drawing data was changed.");
            return false;
        }

        return true;
    }

    private static void WriteTextStyleDiagnostics(
        Editor editor,
        AutoCadTextStyleCatalogReadResult result)
    {
        editor.WriteMessage("\nAK23 TextStyle catalog diagnostics (read-only):");
        foreach (var entry in result.Entries)
        {
            var textSize = entry.TextSize.HasValue
                ? entry.TextSize.Value.ToString("G17", CultureInfo.InvariantCulture)
                : "<unavailable>";
            var annotativeValue = entry.AnnotativeStateValue.HasValue
                ? entry.AnnotativeStateValue.Value.ToString(
                    CultureInfo.InvariantCulture)
                : "<unavailable>";
            editor.WriteMessage(
                $"\n  {entry.CanonicalName}: handle={entry.Handle}, " +
                $"ObjectId.IsValid={entry.ObjectIdIsValid}, " +
                $"IsErased={entry.IsErased}, TextSize={textSize}, " +
                $"Annotative={entry.AnnotativeStateName} ({annotativeValue}), " +
                $"ExpectedDbNative={entry.ExpectedDatabaseNativeIdentity}, " +
                $"ActualDbNative={entry.ActualDatabaseNativeIdentity}, " +
                $"ManagedReferenceEquals={entry.ManagedReferenceEquals}, " +
                $"HostDatabaseIdentity={entry.HostDatabaseIdentityMatches}, " +
                $"{(entry.Accepted ? "ACCEPTED" : "REJECTED")}: {entry.Reason}");
        }

        if (!result.TableReadSucceeded)
        {
            editor.WriteMessage(
                $"\n  TABLE READ FAILED: {result.TableFailureReason}");
        }
        else
        {
            editor.WriteMessage(
                $"\n  Catalog accepted {result.Catalog.CompatibleStyles.Count} " +
                $"of {result.Entries.Count} record(s).");
        }
    }

    private static bool TrySelectStyles(
        AutoCadTextStyleCatalog catalog,
        Editor editor,
        out ProofStyleSelection selection)
    {
        var styleA = catalog.CompatibleStyles.FirstOrDefault(style => style.IsCurrent)
            ?? catalog.FindCompatible(TimberAnnotationTextSettingsRules.DefaultTextStyleName)
            ?? catalog.CompatibleStyles.FirstOrDefault();
        if (styleA is null)
        {
            editor.WriteMessage(
                "\nAK23 proof creation requires at least one non-annotative text style " +
                "with fixed height 0. No drawing data was changed.");
            selection = null!;
            return false;
        }

        var styleB = catalog.CompatibleStyles.FirstOrDefault(style =>
                style.TextStyleId != styleA.TextStyleId) ?? styleA;
        selection = new ProofStyleSelection(
            styleA,
            styleB,
            styleB.TextStyleId != styleA.TextStyleId);
        return true;
    }

    private static void CreateLeader(
        Database database,
        Transaction transaction,
        BlockTableRecord modelSpace,
        BlockTableRecord sharedBlock,
        AttributeDefinition sharedDefinition,
        AutoCadFramedTextAttributeDefinitionSnapshot snapshot,
        AutoCadFramedTextAttributeProofCase proofCase,
        AutoCadTextStyleCatalogEntry style,
        bool hasDistinctStyleB,
        string definitionStyleName,
        Editor editor)
    {
        if (!AutoCadDatabaseIdentity.IsSame(database, style.TextStyleId))
        {
            throw new InvalidOperationException(
                "The selected text style belongs to another database.");
        }

        if (!AttributeDefinitionBelongsToBlock(sharedBlock, sharedDefinition.ObjectId))
        {
            throw new InvalidOperationException(
                "AttrDef ObjectId does not belong to BlockContentId " +
                $"{sharedBlock.ObjectId}.");
        }

        using var leader = new MLeader();
        leader.SetDatabaseDefaults(database);
        leader.EnableAnnotationScale = false;
        leader.Scale = 1d;
        leader.ContentType = ContentType.BlockContent;
        leader.BlockContentId = sharedBlock.ObjectId;
        leader.BlockConnectionType = BlockConnectionType.ConnectBase;
        leader.BlockScale = new Scale3d(proofCase.BlockScale);
        leader.BlockRotation = 0d;

        var blockPosition = new Point3d(proofCase.BlockPositionX, 0d, 0d);
        leader.BlockPosition = blockPosition;
        var leaderIndex = leader.AddLeader();
        var leaderLineIndex = leader.AddLeaderLine(leaderIndex);
        leader.AddFirstVertex(
            leaderLineIndex,
            new Point3d(proofCase.BlockPositionX - 300d, -250d, 0d));
        leader.AddLastVertex(
            leaderLineIndex,
            new Point3d(proofCase.BlockPositionX - 100d, 0d, 0d));

        modelSpace.AppendEntity(leader);
        transaction.AddNewlyCreatedDBObject(leader, true);

        using (var attribute = new AttributeReference())
        {
            attribute.SetAttributeFromBlock(
                sharedDefinition,
                Matrix3d.Identity);
            attribute.TextString = proofCase.Token;
            attribute.TextStyleId = style.TextStyleId;
            attribute.Height = proofCase.BaseAttributeHeight;
            leader.SetBlockAttribute(sharedDefinition.ObjectId, attribute);
        }

        using (var immediate = leader.GetBlockAttribute(sharedDefinition.ObjectId))
        {
            WriteAttributeValues(
                editor,
                "CREATE",
                proofCase,
                style,
                immediate,
                leader.BlockScale.X,
                snapshot.Height,
                definitionStyleName,
                snapshot.TextStyleHandle,
                leaderHandle: leader.ObjectId.ToString(),
                blockHandle: leader.BlockContentId.ToString(),
                actualStyleName: ReadCanonicalStyleName(
                    database,
                    transaction,
                    immediate.TextStyleId));
            // Height capability: AttrRef.TextStyleId is NOT SUPPORTED on MLeader
            // block content (reverts to AttrDef). Match token/heights/scale only.
            var requireStyleMatch =
                !AutoCadFramedTextAttributeProofPolicy.IsHeightCapabilityCase(
                    proofCase);
            if (!TryMatchAttributeValues(
                    proofCase,
                    style,
                    immediate,
                    leader.BlockScale,
                    requireStyleMatch,
                    out var failedCondition))
            {
                WriteImmediateReadbackFailure(
                    editor,
                    proofCase,
                    style,
                    leader,
                    sharedDefinition,
                    immediate,
                    snapshot.Height,
                    attrDefBelongsToBlock: true,
                    failedCondition);
                throw new InvalidOperationException(
                    $"Immediate readback failed for {proofCase.Token}: {failedCondition}");
            }
        }

        var payload = AutoCadFramedTextAttributeProofPolicy.CreatePayload(
            proofCase,
            style.CanonicalName,
            style.TextStyleId.Handle.ToString(),
            sharedBlock.Handle.ToString(),
            hasDistinctStyleB,
            snapshot);
        SetProofXData(leader, payload);
    }

    private static bool VerifyLeader(
        Database database,
        Transaction transaction,
        ProofEntityReference reference,
        AutoCadTextStyleCatalogEntry expectedStyle,
        ref string? commonBlockHandle,
        Editor editor)
    {
        var leader = (MLeader)transaction.GetObject(
            reference.LeaderId,
            OpenMode.ForRead);
        var payload = reference.Payload;
        var structuralMatch = leader.ContentType == ContentType.BlockContent &&
            AutoCadDatabaseIdentity.IsSame(database, leader.ObjectId) &&
            !leader.BlockContentId.IsNull &&
            AutoCadDatabaseIdentity.IsSame(database, leader.BlockContentId) &&
            string.Equals(
                leader.BlockContentId.Handle.ToString(),
                payload.BlockDefinitionHandle,
                StringComparison.OrdinalIgnoreCase);

        var currentBlockHandle = leader.BlockContentId.Handle.ToString();
        commonBlockHandle ??= currentBlockHandle;
        structuralMatch &= string.Equals(
            commonBlockHandle,
            currentBlockHandle,
            StringComparison.OrdinalIgnoreCase);

        var block = (BlockTableRecord)transaction.GetObject(
            leader.BlockContentId,
            OpenMode.ForRead);
        var definition = FindAttributeDefinition(block, transaction);
        if (definition is null)
        {
            WriteStatus(
                editor,
                AutoCadFramedTextAttributeProofCheckResult.Evaluated(
                    $"{payload.CaseToken} shared attribute definition",
                    false,
                    "ITEM_NO",
                    "missing"));
            return false;
        }

        var snapshotMatch = AutoCadFramedTextAttributeProofPolicy.SnapshotsMatch(
            payload.AttributeDefinitionSnapshot,
            CaptureSnapshot(definition));
        using var attribute = leader.GetBlockAttribute(definition.ObjectId);
        var actualStyleName = ReadCanonicalStyleName(
            database,
            transaction,
            attribute.TextStyleId);
        var proofCase = AutoCadFramedTextAttributeProofPolicy.Cases.Single(candidate =>
            string.Equals(candidate.Token, payload.CaseToken, StringComparison.Ordinal));
        var requireStyleMatch =
            !AutoCadFramedTextAttributeProofPolicy.IsHeightCapabilityCase(proofCase);
        var attributeMatch = TryMatchAttributeValues(
            proofCase,
            expectedStyle,
            attribute,
            leader.BlockScale,
            requireStyleMatch,
            out var failedCondition);
        var passed = structuralMatch && snapshotMatch && attributeMatch;

        WriteAttributeValues(
            editor,
            "VERIFY",
            proofCase,
            expectedStyle,
            attribute,
            leader.BlockScale.X,
            definition.Height,
            ReadCanonicalStyleName(
                database,
                transaction,
                definition.TextStyleId),
            definition.TextStyleId.Handle.ToString(),
            leader.ObjectId.ToString(),
            currentBlockHandle,
            actualStyleName);
        if (!attributeMatch)
        {
            editor.WriteMessage(
                $"\nVERIFY {payload.CaseToken} match failure: {failedCondition}");
        }
        WriteStatus(
            editor,
            AutoCadFramedTextAttributeProofCheckResult.Evaluated(
                payload.CaseToken,
                passed,
                $"token={proofCase.Token}, style={expectedStyle.CanonicalName}, " +
                    $"height={Format(proofCase.BaseAttributeHeight)}, " +
                    $"scale={Format(proofCase.BlockScale)}, " +
                    $"effective={Format(proofCase.EffectiveModelHeight)}, unchanged definition",
                $"token={attribute.TextString}, style={actualStyleName} " +
                    $"({attribute.TextStyleId.Handle}), " +
                    $"rawHeight={Format(attribute.Height)}, " +
                    $"normalizedBase={Format(new AutoCadFramedTextAttributeHeightObservation(attribute.Height, leader.BlockScale.X).NormalizedBaseHeight)}, " +
                    $"scale={Format(leader.BlockScale.X)}, " +
                    $"effective={Format(new AutoCadFramedTextAttributeHeightObservation(attribute.Height, leader.BlockScale.X).ActualEffectiveHeight)}, " +
                    $"definition={(snapshotMatch ? "unchanged" : "changed")}"));
        return passed;
    }

    private static bool TryMatchAttributeValues(
        AutoCadFramedTextAttributeProofCase proofCase,
        AutoCadTextStyleCatalogEntry expectedStyle,
        AttributeReference attribute,
        Scale3d blockScale,
        bool requireStyleMatch,
        out string failedCondition)
    {
        var height = new AutoCadFramedTextAttributeHeightObservation(
            attribute.Height,
            blockScale.X);

        if (!string.Equals(
                attribute.TextString,
                proofCase.Token,
                StringComparison.Ordinal))
        {
            failedCondition =
                $"token expected={proofCase.Token} actual={attribute.TextString}";
            return false;
        }

        if (requireStyleMatch &&
            attribute.TextStyleId != expectedStyle.TextStyleId)
        {
            failedCondition =
                $"TextStyleId expected={expectedStyle.TextStyleId} " +
                $"({expectedStyle.CanonicalName}) actual={attribute.TextStyleId}";
            return false;
        }

        if (height.NormalizedBaseHeight is not double normalizedBaseHeight ||
            !AutoCadFramedTextAttributeProofPolicy.AreClose(
                proofCase.BaseAttributeHeight,
                normalizedBaseHeight))
        {
            failedCondition =
                $"AttrRef.Height/normalizedBase expected={Format(proofCase.BaseAttributeHeight)} " +
                $"(paper={Format(proofCase.ItemNumberPaperHeightMm)}×" +
                $"{TimberAnnotationScaleRules.DefaultDenominator}; " +
                $"caseDenom={proofCase.AnnotationScaleDenominator}) " +
                $"actual raw={Format(attribute.Height)} " +
                $"normalized={Format(height.NormalizedBaseHeight)}";
            return false;
        }

        if (height.ActualEffectiveHeight is not double actualEffectiveHeight ||
            !AutoCadFramedTextAttributeProofPolicy.AreClose(
                proofCase.EffectiveModelHeight,
                actualEffectiveHeight))
        {
            failedCondition =
                $"effective height expected={Format(proofCase.EffectiveModelHeight)} " +
                $"actual={Format(height.ActualEffectiveHeight)}";
            return false;
        }

        if (!AutoCadFramedTextAttributeProofPolicy.AreClose(
                proofCase.BlockScale,
                blockScale.X))
        {
            failedCondition =
                $"BlockScale.X expected={Format(proofCase.BlockScale)} " +
                $"actual={Format(blockScale.X)}";
            return false;
        }

        if (!AutoCadFramedTextAttributeProofPolicy.AreClose(
                blockScale.X,
                blockScale.Y) ||
            !AutoCadFramedTextAttributeProofPolicy.AreClose(
                blockScale.X,
                blockScale.Z))
        {
            failedCondition =
                $"BlockScale non-uniform actual=({Format(blockScale.X)}, " +
                $"{Format(blockScale.Y)}, {Format(blockScale.Z)})";
            return false;
        }

        failedCondition = string.Empty;
        return true;
    }

    private static void WriteImmediateReadbackFailure(
        Editor editor,
        AutoCadFramedTextAttributeProofCase proofCase,
        AutoCadTextStyleCatalogEntry expectedStyle,
        MLeader leader,
        AttributeDefinition definition,
        AttributeReference attribute,
        double definitionHeight,
        bool attrDefBelongsToBlock,
        string failedCondition)
    {
        var height = new AutoCadFramedTextAttributeHeightObservation(
            attribute.Height,
            leader.BlockScale.X);
        editor.WriteMessage(
            $"\nImmediate readback FAILED for {proofCase.Token}:" +
            $"\n  failedCondition={failedCondition}" +
            $"\n  MLeader ObjectId={leader.ObjectId}" +
            $"\n  BlockContentId={leader.BlockContentId}" +
            $"\n  AttrDef ObjectId={definition.ObjectId}" +
            $"\n  AttrDef belongs to BlockContentId={attrDefBelongsToBlock}" +
            $"\n  token expected={proofCase.Token} actual={attribute.TextString}" +
            $"\n  AttrRef.Height expected={Format(proofCase.BaseAttributeHeight)} " +
            $"actual={Format(attribute.Height)}" +
            $"\n  AttrDef.Height={Format(definitionHeight)}" +
            $"\n  BlockScale expected={Format(proofCase.BlockScale)} " +
            $"actual={Format(leader.BlockScale.X)}" +
            $"\n  effective height expected={Format(proofCase.EffectiveModelHeight)} " +
            $"actual={Format(height.ActualEffectiveHeight)}" +
            $"\n  TextStyleId expected={expectedStyle.TextStyleId} " +
            $"actual={attribute.TextStyleId}" +
            $"\n  paperHeightMm={Format(proofCase.ItemNumberPaperHeightMm)} " +
            $"denominator={proofCase.AnnotationScaleDenominator} " +
            $"(expectedBase=paper×denom={Format(proofCase.BaseAttributeHeight)})" +
            $"\n  after AppendEntity+AddNewlyCreatedDBObject+SetBlockAttribute " +
            $"in current write transaction");
    }

    private static bool AttributeDefinitionBelongsToBlock(
        BlockTableRecord block,
        ObjectId attributeDefinitionId)
    {
        foreach (ObjectId id in block)
        {
            if (id == attributeDefinitionId)
            {
                return true;
            }
        }

        return false;
    }

    private static AttributeDefinition? FindAttributeDefinition(
        BlockTableRecord block,
        Transaction transaction)
    {
        foreach (ObjectId id in block)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is
                    AttributeDefinition definition &&
                string.Equals(
                    definition.Tag,
                    TimberItemLeaderBlockDefinitionRules.AttributeTag,
                    StringComparison.OrdinalIgnoreCase))
            {
                return definition;
            }
        }

        return null;
    }

    private static AutoCadFramedTextAttributeDefinitionSnapshot CaptureSnapshot(
        AttributeDefinition definition) =>
        new(
            definition.Handle.ToString(),
            definition.TextStyleId.Handle.ToString(),
            definition.Tag,
            definition.Prompt,
            definition.Height,
            definition.TextString,
            definition.Position.X,
            definition.Position.Y,
            definition.Position.Z,
            definition.AlignmentPoint.X,
            definition.AlignmentPoint.Y,
            definition.AlignmentPoint.Z,
            definition.Rotation,
            (int)definition.HorizontalMode,
            (int)definition.VerticalMode,
            definition.Invisible,
            definition.Constant,
            definition.LockPositionInBlock);

    private static string ReadCanonicalStyleName(
        Database database,
        Transaction transaction,
        ObjectId textStyleId)
    {
        if (!AutoCadDatabaseIdentity.IsSame(database, textStyleId) ||
            !AutoCadObjectIdAccess.TryGetObject<TextStyleTableRecord>(
                transaction,
                textStyleId,
                OpenMode.ForRead,
                out var record,
                database))
        {
            throw new InvalidOperationException(
                "A proof AttributeReference has no readable text style in this database.");
        }

        return record!.Name;
    }

    private static void EnsureProofRegApp(
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
        using var record = new RegAppTableRecord { Name = ProofRegAppName };
        table.Add(record);
        transaction.AddNewlyCreatedDBObject(record, true);
    }

    private static void SetProofXData(
        MLeader leader,
        AutoCadFramedTextAttributeProofPayload payload)
    {
        var values = new List<TypedValue>
        {
            new((int)DxfCode.ExtendedDataRegAppName, ProofRegAppName),
        };
        values.AddRange(
            AutoCadFramedTextAttributeProofPolicy.SerializePayload(payload)
                .Select(chunk => new TypedValue(
                    (int)DxfCode.ExtendedDataAsciiString,
                    chunk)));
        using var buffer = new ResultBuffer(values.ToArray());
        leader.XData = buffer;
    }

    private static IReadOnlyList<ProofEntityReference> ReadProofReferences(
        Database database,
        Transaction transaction,
        out int corruptPayloadCount)
    {
        corruptPayloadCount = 0;
        var result = new List<ProofEntityReference>();
        var modelSpace = (BlockTableRecord)transaction.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(database),
            OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (transaction.GetObject(id, OpenMode.ForRead, false) is not MLeader leader)
            {
                continue;
            }

            using var xdata = leader.GetXDataForApplication(ProofRegAppName);
            if (xdata is null)
            {
                continue;
            }

            var chunks = xdata.AsArray()
                .Where(value => value.TypeCode ==
                    (int)DxfCode.ExtendedDataAsciiString)
                .Select(value => value.Value as string ?? string.Empty)
                .ToArray();
            if (!AutoCadFramedTextAttributeProofPolicy.TryDeserializePayload(
                    chunks,
                    out var payload))
            {
                corruptPayloadCount++;
                continue;
            }

            result.Add(new ProofEntityReference(id, payload!));
        }

        return result.AsReadOnly();
    }

    private static void WriteAttributeValues(
        Editor editor,
        string phase,
        AutoCadFramedTextAttributeProofCase proofCase,
        AutoCadTextStyleCatalogEntry style,
        AttributeReference attribute,
        double blockScale,
        double definitionHeight,
        string definitionStyleName,
        string definitionStyleHandle,
        string? leaderHandle = null,
        string? blockHandle = null,
        string? actualStyleName = null)
    {
        var height = new AutoCadFramedTextAttributeHeightObservation(
            attribute.Height,
            blockScale);
        editor.WriteMessage(
            $"\n{phase} {proofCase.Token}: " +
            $"MLeader={leaderHandle ?? "pending"}, Block={blockHandle ?? "pending"}, " +
            $"token={attribute.TextString}, expectedStyle={style.CanonicalName} " +
            $"({style.TextStyleId.Handle}), actualStyle={actualStyleName ?? "unresolved"} " +
            $"({attribute.TextStyleId.Handle}), definitionStyle={definitionStyleName} " +
            $"({definitionStyleHandle}), expectedBase={Format(proofCase.BaseAttributeHeight)}, " +
            $"rawAttributeHeight={Format(attribute.Height)}, " +
            $"normalizedBaseHeight={Format(height.NormalizedBaseHeight)}, " +
            $"definitionBase={Format(definitionHeight)}, " +
            $"scale={Format(blockScale)}, " +
            $"actualEffectiveHeight={Format(height.ActualEffectiveHeight)}");
    }

    private static void WriteDefinitionSnapshot(
        Editor editor,
        AttributeDefinition definition,
        string styleName) =>
        editor.WriteMessage(
            "\nAK23 shared AttributeDefinition snapshot (read-only):" +
            $"\n  ObjectId={definition.ObjectId}, handle={definition.Handle}" +
            $"\n  TextStyleId={definition.TextStyleId.Handle}, style={styleName}" +
            $"\n  Height={Format(definition.Height)}, " +
            $"TextString={definition.TextString}" +
            $"\n  Position=({Format(definition.Position.X)}, " +
            $"{Format(definition.Position.Y)}, {Format(definition.Position.Z)}), " +
            $"Rotation={Format(definition.Rotation)}");

    private static void WriteStatus(
        Editor editor,
        AutoCadFramedTextAttributeProofCheckResult result) =>
        editor.WriteMessage(
            $"\n[{result.Status.ToString().ToUpperInvariant()}] {result.CheckName}: " +
            $"{result.Message}" +
            (result.Expected is null ? string.Empty : $" Expected: {result.Expected}.") +
            (result.Actual is null ? string.Empty : $" Actual: {result.Actual}."));

    private static string Format(double value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);

    private static string Format(double? value) =>
        value.HasValue ? Format(value.Value) : "<invalid>";

    private sealed record ProofStyleSelection(
        AutoCadTextStyleCatalogEntry StyleA,
        AutoCadTextStyleCatalogEntry StyleB,
        bool HasDistinctStyleB);

    private sealed record ProofEntityReference(
        ObjectId LeaderId,
        AutoCadFramedTextAttributeProofPayload Payload);
}
#endif
