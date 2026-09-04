using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofExternalImportC2SourceContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string Command = Read("src", "AcKrovy.AutoCAD", "Commands", "AutoCadRoofExternalImportCommand.cs");
    private static readonly string C2Proof = Read("src", "AcKrovy.AutoCAD", "Commands", "RoofExternalImportC2SuccessProof.cs");
    private static readonly string SuccessRules = Read("src", "AcKrovy.Core", "Services", "Roofs", "RoofExternalImportSuccessRules.cs");
    private static readonly string RollbackRules = Read("src", "AcKrovy.Core", "Services", "Roofs", "RoofExternalImportRollbackRules.cs");
    private static readonly string Policy = Read("src", "AcKrovy.Core", "Services", "Roofs", "RoofExternalImportPolicy.cs");
    private static readonly string Session = Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofExternalImportSession.cs");
    private static readonly string LiveGeometry = Read("src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string Sanitation = Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofExternalImportSanitizationService.cs");
    private static readonly string ProjectContext = Read("ACAD_KROVY_PROJECT_CONTEXT.md");

    [Fact]
    public void C2_01_SuccessPath_ExistsAndCommits()
    {
        Assert.Contains("AK_DEBUG_C2_SUCCESSFUL_IMPORT", Command);
        Assert.Contains("RoofExternalImportWorkflow.Execute(RoofExternalImportFaultMode.None)", Command);
        Assert.Contains("transaction.Commit();", Command);
        Assert.True(Index(Command, "ValidateFinalState") < Index(Command, "transaction.Commit();"));
    }

    [Fact]
    public void C2_02_DirectDatabaseInsert_RemainsTheArchitecture()
    {
        Assert.Contains("Database.Insert(destinationName, sourceDatabase, true)", Command);
        Assert.DoesNotContain("WblockCloneObjects", Command);
        Assert.DoesNotContain("Editor.Command", Command);
        Assert.DoesNotContain("SendStringToExecute", Command);
    }

    [Fact]
    public void C2_03_C1AbortPath_RemainsUntouched()
    {
        Assert.Contains("AK_DEBUG_C1_ABORT_AFTER_INSERT", Command);
        Assert.Contains("AK_DEBUG_C1_ABORT_AFTER_SANITATION", Command);
        Assert.Contains("InjectFault(editor, faultMode, RoofExternalImportFaultMode.AfterMappingValidation)", Command);
        Assert.Contains("ROOF_IMPORT_C1_ABORT_FAILPOINT", Command);
        Assert.True(
            Index(Command, "ValidateReturnedRoot(transaction, operation);") <
            Index(Command, "InjectFault(editor, faultMode, RoofExternalImportFaultMode.AfterMappingValidation)"));
    }

    [Fact]
    public void C2_04_TopLevelReference_UsesExactReturnedRootInModelSpaceOnce()
    {
        Assert.Contains("GetBlockModelSpaceId(document.Database)", Command);
        Assert.Contains("new BlockReference(Point3d.Origin, rootId)", Command);
        Assert.Equal(1, Count(Command, "new BlockReference("));
        Assert.Contains("reference.BlockTableRecord != operation.ReturnedRootId", Command);
        Assert.Contains("reference.OwnerId != modelSpaceId", Command);
        Assert.Contains("explicitReference.ReferencedRootId != operation.ReturnedRootId", Command);
        Assert.Contains("explicitReference.OwnerId != modelSpaceId", Command);
    }

    [Fact]
    public void C2_05_ReturnedRoot_UsesNativeDatabaseIdentityNotWrapperReferenceEquals()
    {
        var validation = Command[
            Index(Command, "private static void ValidateReturnedRoot")..
            Index(Command, "private static void ValidateFinalState")];
        Assert.Contains("operation.Document.Database != operation.TargetDatabase", validation);
        Assert.Contains("operation.ReturnedRootId.Database != operation.TargetDatabase", validation);
        Assert.DoesNotContain("ReferenceEquals", validation);
    }

    [Fact]
    public void C2_06_SuccessProof_RequiresCommittedAliveContent()
    {
        Assert.Contains("ROOF_IMPORT_C2_SUCCESS_PROOF", C2Proof);
        Assert.Contains("transactionCommitted=", C2Proof);
        Assert.Contains("mappingValidation=", C2Proof);
        Assert.Contains("returnedRootValid=", C2Proof);
        Assert.Contains("rootPresent=", C2Proof);
        Assert.Contains("importedObjectsPresent=", C2Proof);
        Assert.Contains("topLevelReferencePresent=", C2Proof);
        Assert.Contains("topLevelReferenceTargetsReturnedRoot=", C2Proof);
        Assert.Contains("supportBtrsValid=", C2Proof);
        Assert.Contains("sanitizationValid=", C2Proof);
        Assert.Contains("dbmodEquivalent=", C2Proof);
        Assert.Contains("result=", C2Proof);
        Assert.Contains("proof.TransactionCommitted &&", C2Proof);
    }

    [Fact]
    public void C2_07_Dbmod_IsDiagnosticOnly_NotSuccessEquivalence()
    {
        Assert.Contains("dbmodEquivalent={Bool(dbmodEquivalent)}", C2Proof);
        Assert.DoesNotContain("&& dbmodEquivalent", C2Proof);
        Assert.DoesNotContain("dbmodEquivalent &&", C2Proof);
        Assert.DoesNotContain("pass = dbmodEquivalent", C2Proof);
        Assert.Contains("proof.SanitizationValid;", C2Proof);
        Assert.True(
            Index(C2Proof, "var pass = proof.TransactionCommitted") <
            Index(C2Proof, "dbmodBefore={proof.DbmodBefore}"));
    }

    [Fact]
    public void C2_08_RealContentPresence_ExcludesStructuralSentinelsAndReusedSupport()
    {
        Assert.Contains("IsRealImportedObjectPresent", SuccessRules);
        Assert.Contains("isStructuralBlockTableRecordSentinel", SuccessRules);
        Assert.Contains("IsNewImportedContentCandidate", SuccessRules);
        Assert.Contains("isSupportBlockTableRecord && !isCloned", SuccessRules);
        Assert.Contains("BlockBegin or BlockEnd", C2Proof);
        Assert.Contains("structuralSentinelsSkipped", C2Proof);
        Assert.Contains("supportReused", C2Proof);
        Assert.Contains("erasedAnnotations.Contains(pair.DestinationId)", C2Proof);
    }

    [Fact]
    public void C2_09_CollisionStillRejectsBeforeMutation_AndC14Unsupported()
    {
        Assert.True(Index(Command, "EnsureNoCollision(document.Database, manifest);") <
                    Index(Command, "Database.Insert"));
        Assert.Contains("root-btr-collision", Command);
        Assert.Contains("C14 redefine zostáva nepodporovaný", ProjectContext);
        Assert.DoesNotContain("redefine", Command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C14", Command);
    }

    [Fact]
    public void C2_10_NoSyntheticSourceRootMapping_AndFirstCallbackWins()
    {
        Assert.DoesNotContain("SourceRootBlockId", Session);
        Assert.Contains("if (_mappingCaptured || mapping.DeepCloneContext != DeepCloneType.InsertCopy)", Session);
        Assert.Contains("ExpectedCloneObjectIds", Command);
        Assert.DoesNotContain("SourceRootBlockId -> ReturnedRootId", Command + Session);
    }

    [Fact]
    public void C2_11_Sanitization_IsReusedNotBypassed()
    {
        Assert.True(Index(Command, "SanitizationService.Apply") < Index(Command, "AppendEntity"));
        Assert.True(Index(Command, "SanitizationService.Verify") < Index(Command, "transaction.Commit();"));
        Assert.Contains("committedSanitation", Command);
        Assert.Contains("sanitizationValid: sanitizationValidationPassed", Command);
        Assert.DoesNotContain("bypass", Sanitation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void C2_12_C2DebugMarkersAndCommand_AreReleaseGuarded()
    {
        Assert.StartsWith("#if DEBUG", C2Proof.TrimStart(), StringComparison.Ordinal);
        Assert.EndsWith("#endif", C2Proof.TrimEnd(), StringComparison.Ordinal);
        Assert.Contains("AK_DEBUG_C2_SUCCESSFUL_IMPORT", Command);
        var release = WithoutDebugBlocks(Command + "\n" + C2Proof);
        foreach (var token in new[]
                 {
                     "AK_DEBUG_C2_SUCCESSFUL_IMPORT",
                     "ROOF_IMPORT_C2_SUCCESS_PROOF",
                     "ROOF_IMPORT_C2_OBJECT_SUMMARY",
                     "RoofExternalImportC2SuccessProof",
                 })
        {
            Assert.DoesNotContain(token, release);
        }
    }

    [Fact]
    public void C2_13_SuccessRules_LiveInCadNeutralCore()
    {
        Assert.DoesNotContain("Autodesk", SuccessRules);
        Assert.DoesNotContain("ObjectId", SuccessRules);
        Assert.DoesNotContain("DatabaseServices", SuccessRules);
        Assert.Contains("RoofExternalImportSuccessRules", C2Proof);
        Assert.Contains("IsAppendedObjectAbsent", RollbackRules);
    }

    [Fact]
    public void C2_14_C2Proof_UsesOperationBoundReferenceNotStaleLookup()
    {
        Assert.Contains("operation.ExplicitReference", C2Proof);
        Assert.Contains("explicitReference.ConstructedByCurrentOperation", C2Proof);
        Assert.Contains("reference.Handle == explicitReference.Handle", C2Proof);
        Assert.Contains("reference.BlockTableRecord == operation.ReturnedRootId", C2Proof);
        Assert.Contains("reference.OwnerId == modelSpaceId", C2Proof);
        Assert.DoesNotContain("GetBlockReferenceIds", C2Proof);
    }

    [Fact]
    public void C2_18_NativeInsertEvidence_AndExplicitReferenceEvidence_AreSeparate()
    {
        var returnedRoot = Command[
            Index(Command, "private static void ValidateReturnedRoot")..
            Index(Command, "private static void ValidateFinalState")];
        var finalState = Command[
            Index(Command, "private static void ValidateFinalState")..
            Index(Command, "private static void EnsureNoCollision(Database database")];

        // Native Database.Insert append evidence proves only the returned root.
        Assert.Contains("operation.AppendedIds.Contains(operation.ReturnedRootId)", returnedRoot);

        // The post-Insert reference must never be proven from the native append stream.
        Assert.DoesNotContain("operation.AppendedIds", finalState);
        Assert.DoesNotContain("object-appended-cross-check-failed", Command);
        Assert.Contains("is native <c>Database.Insert</c> append evidence only", Session);
        Assert.Contains("RoofExternalImportExplicitReference", Session);
    }

    [Fact]
    public void C2_19_ExplicitReference_IsProvenNewlyCreatedByCurrentWorkflow()
    {
        Assert.Contains("var wasUnresidentBeforeAppend = reference.ObjectId.IsNull;", Command);
        Assert.True(
            Index(Command, "var wasUnresidentBeforeAppend") <
            Index(Command, "modelSpace.AppendEntity(reference)"));
        Assert.True(
            Index(Command, "AddNewlyCreatedDBObject(reference, true)") <
            Index(Command, "RecordExplicitReference(reference, wasUnresidentBeforeAppend)"));
        Assert.Contains("wasUnresidentBeforeAppend && !reference.ObjectId.IsNull", Session);
        Assert.Contains("explicitReference.ConstructedByCurrentOperation", Command);
        Assert.Contains("explicit-reference-creation-evidence-failed", Command);
    }

    [Fact]
    public void C2_20_ExactlyOneExplicitReference_IsRecordablePerOperation()
    {
        Assert.Contains("_explicitReferenceRecorded", Session);
        Assert.Contains("explicit-top-level-reference-already-recorded", Session);
        Assert.Equal(1, Count(Command, "RecordExplicitReference("));
        Assert.Equal(1, Count(Command, "new BlockReference("));
    }

    [Fact]
    public void C2_21_ExplicitReference_ResidentIdentityIsRevalidatedInSameTransaction()
    {
        var finalState = Command[
            Index(Command, "private static void ValidateFinalState")..
            Index(Command, "private static void EnsureNoCollision(Database database")];
        foreach (var token in new[]
                 {
                     "transaction.GetObject(explicitReference.Id, OpenMode.ForRead)",
                     "reference.ObjectId != explicitReference.Id",
                     "reference.Handle != explicitReference.Handle",
                     "reference.IsErased",
                     "reference.Database != operation.TargetDatabase",
                     "top-level-reference-invariant-failed",
                 })
        {
            Assert.Contains(token, finalState);
        }

        // No table-wide or space-wide search may substitute for explicit creation evidence.
        Assert.DoesNotContain("GetBlockReferenceIds", Command);
        Assert.DoesNotContain("BlockTableRecord.ModelSpace]", Command);
    }

    [Fact]
    public void C2_22_LiveGeometryObjectAppended_NeverAbortsOtherSubscribers()
    {
        var method = LiveGeometry[
            Index(LiveGeometry, "private void ObjectAppended")..
            Index(LiveGeometry, "private void ObjectModified")];
        Assert.Contains("catch", method);
        Assert.Contains("must never abort other Database.ObjectAppended subscribers", method);
    }

    [Fact]
    public void C2_15_ProjectContext_DocumentsImplementedHostPending()
    {
        Assert.Contains("Stage 2D4-C2", ProjectContext);
        Assert.Contains("IMPLEMENTED, HOST ACCEPTANCE PENDING", ProjectContext);
        Assert.DoesNotContain("Stage 2D4-C2: HOST PASS", ProjectContext);
        Assert.Contains("AK_DEBUG_C2_SUCCESSFUL_IMPORT", ProjectContext);
    }

    [Fact]
    public void C2_16_C2Command_DoesNotArmC1Failpoints()
    {
        var successCommand = Command[
            Index(Command, "class AutoCadRoofExternalImportSuccessProofCommand")..
            Index(Command, "internal enum RoofExternalImportFaultMode")];
        Assert.Contains("RoofExternalImportC2SuccessProof.Arm", successCommand);
        Assert.Contains("FaultMode.None", successCommand);
        Assert.DoesNotContain("C1DbmodTimeline.Arm", successCommand);
        Assert.DoesNotContain("AfterMappingValidation", successCommand);
        Assert.DoesNotContain("AfterMappedNewSanitation", successCommand);
    }

    [Fact]
    public void C2_17_PolicyBoundary_RemainsFailClosedForUnsafePayloads()
    {
        Assert.Contains("whole-roof-out-of-scope", Policy);
        Assert.Contains("MayMutate", Policy);
        Assert.Contains("IsMappedNew", Policy);
    }

    private static string WithoutDebugBlocks(string source)
    {
        var lines = new List<string>();
        var debugDepth = 0;
        foreach (var line in source.Split('\n'))
        {
            var directive = line.Trim();
            if (directive == "#if DEBUG")
            {
                debugDepth++;
                continue;
            }

            if (directive == "#endif")
            {
                Assert.True(debugDepth > 0);
                debugDepth--;
                continue;
            }

            Assert.False(
                directive.StartsWith("#if", StringComparison.Ordinal) ||
                directive.StartsWith("#else", StringComparison.Ordinal));
            if (debugDepth == 0)
            {
                lines.Add(line);
            }
        }

        Assert.Equal(0, debugDepth);
        return string.Join('\n', lines);
    }

    private static int Index(string source, string token)
    {
        var index = source.IndexOf(token, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Missing token: {token}");
        return index;
    }

    private static int Count(string source, string token)
    {
        var count = 0;
        for (var index = 0;
             (index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0;
             index += token.Length)
        {
            count++;
        }

        return count;
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { Root }.Concat(path).ToArray()));

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
