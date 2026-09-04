using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofImportDirectInsertProofSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string Command = ReadAutoCad(
        "Commands",
        "AutoCadRoofImportDirectInsertProofCommand.cs");
    private static readonly string Session = ReadAutoCad(
        "Infrastructure",
        "RoofImportDirectInsertProofSession.cs");
    private static readonly string Diagnostics = ReadAutoCad(
        "Infrastructure",
        "RoofImportRuntimeDiscoveryDiagnostics.cs");
    private static readonly string Veto = ReadAutoCad(
        "Infrastructure",
        "RoofImportDocumentLockVetoProbe.cs");

    [Fact]
    public void ProofCommandAndSession_AreStrictlyDebugOnly()
    {
        AssertDebugOnly(Command);
        AssertDebugOnly(Session);
        AssertDebugOnly(Diagnostics);
        Assert.Contains("AK_DEBUG_DIRECT_INSERT_PROOF", Session);
        Assert.DoesNotContain("AK_DEBUG_DIRECT_INSERT_PROOF", ReadAutoCad("PluginEntry.cs"));
    }

    [Fact]
    public void SameImmutableSideDatabase_IsPreflightedAndPassedToDatabaseInsert()
    {
        Assert.Contains("File.Copy(selectedSource, snapshotPath, overwrite: false);", Command);
        Assert.Contains("FileShare.Read", Command);
        Assert.Contains("SHA256.HashData(snapshotLease)", Command);
        Assert.Contains("using var sourceDatabase = new Database(", Command);
        Assert.Contains("var preflight = PreflightSourceDatabase(sourceDatabase, editor);", Command);
        Assert.Contains("document.Database.Insert(\n                    destinationBlockName,\n                    sourceDatabase,\n                    preserveSourceDatabase: true)", Normalize(Command));
        Assert.DoesNotContain("PreflightSourceDatabase(selectedSource)", Command);
        Assert.DoesNotContain("new Database(\n                    buildDefaultDrawing: false", Normalize(ExtractMethod(Command, "private static PreflightResult PreflightSourceDatabase(")));
    }

    [Fact]
    public void NeutralPreflightAndAbsentDestination_PrecedeAnyTargetMutation()
    {
        var preflight = Command.IndexOf(
            "PreflightSourceDatabase(sourceDatabase, editor)",
            StringComparison.Ordinal);
        var absent = Command.IndexOf("EnsureDestinationDoesNotExist(", preflight, StringComparison.Ordinal);
        var markOk = Command.IndexOf("MarkPreflightOk(", absent, StringComparison.Ordinal);
        var begin = Command.IndexOf("BeginInsert(sessionId.Value)", markOk, StringComparison.Ordinal);
        var transaction = Command.IndexOf("StartTransaction()", begin, StringComparison.Ordinal);
        var insert = Command.IndexOf("document.Database.Insert(", transaction, StringComparison.Ordinal);

        Assert.True(preflight >= 0 && preflight < absent);
        Assert.True(absent < markOk && markOk < begin);
        Assert.True(begin < transaction && transaction < insert);
        Assert.Contains("blockTable.Has(destinationBlockName)", Command);
        Assert.Contains("The proof destination block name already exists", Command);
    }

    [Fact]
    public void Preflight_RejectsKrovyNestedBlocksAndNonNeutralModelSpace()
    {
        Assert.Contains("The proof source contains a KROVY RegApp marker.", Command);
        Assert.Contains("The proof source contains KROVY XData.", Command);
        Assert.Contains("The proof source contains a KROVY dictionary marker.", Command);
        Assert.Contains("if (!block.IsLayout)", Command);
        Assert.Contains("nested or user-defined block", Command);
        Assert.Contains("entity is not Line && entity is not Polyline", Command);
        Assert.Contains("modelSpaceEntityCount != 1", Command);
        Assert.Contains("nestedBlocks=false krovyMetadata=false", Command);
    }

    [Fact]
    public void NestedBlockDiagnostic_PrecedesTheUnchangedGuardThrowAndIsReadOnly()
    {
        var preflight = ExtractMethod(
            Command,
            "private static PreflightResult PreflightSourceDatabase(");
        var diagnostic = ExtractMethod(
            Command,
            "private static bool WriteNonLayoutBlockTableRecordAudit(");
        var guard = preflight.IndexOf("if (!block.IsLayout)", StringComparison.Ordinal);
        var log = preflight.IndexOf(
            "WriteNonLayoutBlockTableRecordAudit(",
            guard,
            StringComparison.Ordinal);
        var message = preflight.IndexOf(
            "The proof source contains a nested or user-defined block.",
            log,
            StringComparison.Ordinal);

        Assert.Equal(1, CountOccurrences(Command, "if (!block.IsLayout)"));
        Assert.True(guard >= 0 && guard < log && log < message);
        Assert.Contains("ROOF_IMPORT_DIRECT_INSERT_PREFLIGHT_BTR", diagnostic);
        Assert.Contains("name={Quoted(name)}", diagnostic);
        Assert.Contains("entityCount={block.Cast<ObjectId>().Count()}", diagnostic);
        Assert.Contains("isLayout={Bool(block.IsLayout)}", diagnostic);
        Assert.Contains("isAnonymous={Bool(block.IsAnonymous)}", diagnostic);
        Assert.Contains(
            "isFromExternalReference={Bool(block.IsFromExternalReference)}",
            diagnostic);
        Assert.Contains("isFromOverlayReference={Bool(block.IsFromOverlayReference)}", diagnostic);
        Assert.Contains("isDependent={Bool(block.IsDependent)}", diagnostic);
        Assert.Contains("exactSystemName={ExactSystemName(name)}", diagnostic);
        Assert.Contains("rejectReason=nested-or-user-defined-block", diagnostic);

        var diagnosticPath = preflight + diagnostic;
        Assert.Contains("OpenMode.ForRead", preflight);
        Assert.DoesNotContain("OpenMode.ForWrite", diagnosticPath);
        Assert.DoesNotContain("Commit", diagnosticPath);
        Assert.DoesNotContain("AppendEntity", diagnosticPath);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", diagnosticPath);
        Assert.DoesNotContain(".Erase(", diagnosticPath);
        Assert.DoesNotContain("UpgradeOpen", diagnosticPath);
    }

    [Fact]
    public void BtrRelationshipAudit_UsesExactObjectIdsAndReadOnlyCycleSafeTraversal()
    {
        var audit = ExtractRegion(
            Command,
            "private static bool WriteNonLayoutBlockTableRecordAudit(",
            "private static string ExactSystemName(");

        Assert.Contains("ROOF_IMPORT_DIRECT_INSERT_BTR_AUDIT", audit);
        Assert.Contains("entityTypes={ListToken(audit.EntityTypes)}", audit);
        Assert.Contains("nestedBlockReferenceCount={audit.NestedReferences.Count}", audit);
        Assert.Contains("nestedReferencedBtrNames=", audit);
        Assert.Contains("nestedReferencedBtrHandles=", audit);
        Assert.Contains("record.Dimblk == offendingBlockId", audit);
        Assert.Contains("record.Dimblk1 == offendingBlockId", audit);
        Assert.Contains("record.Dimblk2 == offendingBlockId", audit);
        Assert.Contains("record.Dimldrblk == offendingBlockId", audit);
        Assert.Contains("style.ArrowSymbolId == offendingBlockId", audit);
        Assert.Contains("sourceDatabase.MLeaderStyleDictionaryId", audit);
        Assert.Contains("offendingBlock.GetBlockReferenceIds(true, false)", audit);
        Assert.Contains("var referencedBlockId = reference.BlockTableRecord;", audit);
        Assert.Contains("referencedBlockId == targetBlockId", audit);
        Assert.Contains("SymbolUtilityServices.GetBlockModelSpaceId(sourceDatabase)", audit);
        Assert.Contains("space.IsLayout", audit);
        Assert.Contains("new HashSet<ObjectId> { space.ObjectId }", audit);
        Assert.Contains("!visitedBlockTableRecords.Add(referencedBlockId)", audit);
        Assert.Contains("referencedFromModelSpace=", audit);
        Assert.Contains("referencedFromPaperSpace=", audit);
        Assert.Contains("reachableFromSourceGeometry=", audit);
        Assert.Contains("semanticClassification=", audit);

        Assert.DoesNotContain("_Open90", Command);
        Assert.DoesNotContain("OpenMode.ForWrite", audit);
        Assert.DoesNotContain("Commit", audit);
        Assert.DoesNotContain("Database.Insert", audit);
        Assert.DoesNotContain("WblockCloneObjects", audit);
        Assert.DoesNotContain("AppendEntity", audit);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", audit);
        Assert.DoesNotContain(".Erase(", audit);
        Assert.DoesNotContain("UpgradeOpen", audit);
        Assert.DoesNotContain("Editor.Command", audit);
        Assert.DoesNotContain("SendStringToExecute", audit);
        Assert.DoesNotContain("Idle", audit);
        Assert.DoesNotContain("Timer", audit);
    }

    [Fact]
    public void SystemSupportPredicate_IsExactRelationshipBasedAndFailClosed()
    {
        var preflight = ExtractMethod(
            Command,
            "private static PreflightResult PreflightSourceDatabase(");
        var predicate = ExtractRegion(
            Command,
            "private readonly record struct SystemSupportRelationshipResult(",
            "private readonly record struct NestedReferenceInfo(");

        Assert.Contains("HasStyleObjectIdReference &&", predicate);
        Assert.Contains("ContainedBlockReferenceCount == 0 &&", predicate);
        Assert.Contains("DirectBlockReferenceCount == 0 &&", predicate);
        Assert.Contains("!ReferencedFromModelSpace &&", predicate);
        Assert.Contains("!ReferencedFromPaperSpace &&", predicate);
        Assert.Contains("!ReachableFromSourceGeometry;", predicate);
        Assert.Contains("var isConfirmedSystemSupport = WriteNonLayoutBlockTableRecordAudit(", preflight);
        Assert.Contains("if (!isConfirmedSystemSupport)", preflight);
        Assert.Contains(
            "The proof source contains a nested or user-defined block.",
            preflight);
        Assert.Contains("action=allow-system-support", Command);
        Assert.Contains("return false;", ExtractMethod(
            Command,
            "private static bool WriteNonLayoutBlockTableRecordAudit("));

        foreach (var forbiddenName in new[] { "_Open90", "_Open30", "_Closed" })
        {
            Assert.DoesNotContain(forbiddenName, Command);
        }
    }

    [Fact]
    public void Mutation_IsDirectManagedInsertAndOneExplicitTargetTransaction()
    {
        Assert.Equal(1, CountOccurrences(Command, "document.Database.Insert("));
        Assert.Equal(1, CountOccurrences(Command, "StartTransaction()"));
        Assert.Contains("preserveSourceDatabase: true", Command);
        Assert.Contains("new BlockReference(Point3d.Origin, rootBlockId)", Command);
        Assert.Contains("SymbolUtilityServices.GetBlockModelSpaceId(document.Database)", Command);
        Assert.Contains("ScaleFactors = new Scale3d(1.0, 1.0, 1.0)", Command);
        Assert.Contains("Rotation = 0.0", Command);
        Assert.Contains("currentSpace.AppendEntity(reference)", Command);
        Assert.Contains("transaction.AddNewlyCreatedDBObject(reference, add: true)", Command);
        Assert.Contains("transaction.Commit();", Command);
        Assert.DoesNotContain("editor.Command(", Command);
        Assert.DoesNotContain("CommandAsync", Command);
        Assert.DoesNotContain("SendStringToExecute", Command);
        Assert.DoesNotContain("StartUndoMark", Command);
        Assert.DoesNotContain("EndUndoMark", Command);
        Assert.DoesNotContain(".Erase(", Command);
    }

    [Fact]
    public void MappingEvidence_IsExactSessionDocumentPhaseAndInsertContextBound()
    {
        Assert.Contains("candidate.Phase is SessionPhase.Inserting or SessionPhase.MappingSeen", Session);
        Assert.Contains("ReferenceEquals(document, candidate.Document)", Session);
        Assert.Contains("mapping.DeepCloneContext is not DeepCloneType.Insert", Session);
        Assert.Contains("not DeepCloneType.InsertCopy", Session);
        Assert.Contains("ROOF_IMPORT_DIRECT_INSERT_MAPPING", Session);
        Assert.Contains("MappingSeen = true", Session);
        Assert.Contains("RoofImportDirectInsertProofSession.ObserveMapping(", Diagnostics);
        Assert.Contains("RoofImportDirectInsertProofSession.ObserveObjectAppended(", Diagnostics);
    }

    [Fact]
    public void ExistingDiagnostics_CaptureDirectInsertLifecycleAndOuterCommandBoundary()
    {
        Assert.Contains("RoofImportDirectInsertProofSession.CommandName", Diagnostics);
        foreach (var callback in new[]
                 {
                     "CommandWillStart",
                     "BeginInsert",
                     "InsertMappingAvailable",
                     "BeginDeepClone",
                     "BeginDeepCloneTranslation",
                     "ObjectAppended",
                     "DeepCloneEnded",
                     "InsertEnded",
                     "CommandEnded",
                 })
        {
            Assert.Contains(callback, Diagnostics);
        }
    }

    [Fact]
    public void Completion_RequiresMappingOneBtrAndOneReferenceThenArmsRawDashInsertVeto()
    {
        var mappingRequired = Command.IndexOf("if (!session.MappingSeen", StringComparison.Ordinal);
        var oneBtr = Command.IndexOf("result.TargetBlockCount - targetBlockCountBefore != 1", mappingRequired, StringComparison.Ordinal);
        var oneReference = Command.IndexOf("result.TopLevelReferenceCount != 1", oneBtr, StringComparison.Ordinal);
        var complete = Command.IndexOf("RoofImportDirectInsertProofSession.Complete", oneReference, StringComparison.Ordinal);
        var result = Command.IndexOf("ROOF_IMPORT_DIRECT_INSERT_RESULT", complete, StringComparison.Ordinal);
        var arm = Command.IndexOf("RoofImportDocumentLockVetoProbe.ArmDashInsert(document);", result, StringComparison.Ordinal);

        Assert.True(mappingRequired >= 0 && mappingRequired < oneBtr);
        Assert.True(oneBtr < oneReference && oneReference < complete);
        Assert.True(complete < result && result < arm);
        Assert.Contains("$\"sourceIdentity={sourceIdentity} \"", Command);
        Assert.Contains("DashInsertGlobalCommandName = \"-INSERT\"", Veto);
        Assert.Contains("e.Veto();", Veto);
    }

    [Fact]
    public void EveryExitPath_ClearsSessionAndSnapshotWithoutRepair()
    {
        Assert.Contains("finally", Command);
        Assert.Contains("RoofImportDirectInsertProofSession.Clear(", Command);
        Assert.Contains("failed-or-cancelled", Command);
        Assert.Contains("snapshotLease?.Dispose();", Command);
        Assert.Contains("DeleteSnapshotDirectory(snapshotDirectory, editor);", Command);
        Assert.Contains("RoofImportDirectInsertProofSession.ClearAll(\"plugin-stop\")", Veto);
        Assert.Contains("RoofImportDirectInsertProofSession.ClearDocument(", Veto);
        Assert.DoesNotContain("Editor.Command", Command);
        Assert.DoesNotContain("UserBreak", Command);
        Assert.DoesNotContain("Idle", Command);
        Assert.DoesNotContain("Timer", Command);
    }

    private static void AssertDebugOnly(string source)
    {
        var trimmed = source.Trim();
        Assert.StartsWith("#if DEBUG", trimmed, StringComparison.Ordinal);
        Assert.EndsWith("#endif", trimmed, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method signature not found: {signature}");
        var next = source.IndexOf("\n    private static ", start + signature.Length, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }

    private static string ExtractRegion(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker not found: {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker not found: {endMarker}");
        return source[start..end];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string Normalize(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string ReadAutoCad(params string[] path) =>
        File.ReadAllText(Path.Combine(
            new[] { RepositoryRoot, "src", "AcKrovy.AutoCAD" }
                .Concat(path)
                .ToArray()));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
