using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofExternalImportC1SourceContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string Command = Read("src", "AcKrovy.AutoCAD", "Commands", "AutoCadRoofExternalImportCommand.cs");
    private static readonly string DbmodTimeline = Read("src", "AcKrovy.AutoCAD", "Commands", "RoofExternalImportC1DbmodTimeline.cs");
    private static readonly string RollbackObjects = Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofExternalImportRollbackObjectDiagnostics.cs");
    private static readonly string Preflight = Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofExternalImportPreflightService.cs");
    private static readonly string Manifest = Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofExternalImportManifest.cs");
    private static readonly string Session = Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofExternalImportSession.cs");
    private static readonly string Sanitation = Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofExternalImportSanitizationService.cs");
    private static readonly string Veto = Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofImportCommandEntryProtection.cs");
    private static readonly string Plugin = Read("src", "AcKrovy.AutoCAD", "PluginEntry.cs");
    private static readonly string Live = Read("src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string ProjectContext = Read("ACAD_KROVY_PROJECT_CONTEXT.md");

    [Fact] public void C1_01_NeutralGeometry_IsPartOfExpectedCloneClosure() =>
        Assert.Contains(".Concat(reachableEntities)", Preflight);

    [Fact] public void C1_02_Generated_IsSemanticallyDecoded() =>
        Assert.Contains("RoofGeneratedTimberStore.Read(entity)", Preflight);

    [Fact] public void C1_03_AttachedManualCopy_IsSemanticallyDecoded() =>
        Assert.Contains("RoofAttachedManualTimberStore.Read(entity)", Preflight);

    [Fact] public void C1_04_AttachedManualSplit_UsesTheSameValidatedRoleStore() =>
        Assert.Contains("RoofExternalImportObjectRole.AttachedManual", Preflight);

    [Fact] public void C1_05_AllFourAnnotationRoles_AreExplicitlyClassified()
    {
        Assert.Contains("ElementLabelStore.TryRead", Preflight);
        Assert.Contains("SlopeArrowStore.TryRead", Preflight);
        Assert.Contains("SlopeAngleTextStore.TryRead", Preflight);
        Assert.Contains("PostFootprintPerpendicularAnnotationStore.TryRead", Preflight);
    }

    [Fact] public void C1_06_MultiItemBatch_HasOneCommitAndNoPartialCommit() =>
        Assert.Equal(1, Count(Command, "transaction.Commit();"));

    [Fact] public void C1_07_StaleOwner_IsNeverResolvedDuringSanitation()
    {
        Assert.DoesNotContain("RoofOwnerReference", Sanitation);
        Assert.DoesNotContain("GetObjectId", Sanitation);
    }

    [Fact] public void C1_08_SourceHandle_IsOnlyCheckedAgainstSourceInventory()
    {
        Assert.Contains("timberHandles.Contains(item.SourceHandle)", Preflight);
        Assert.DoesNotContain("GetObjectId", Preflight);
    }

    [Fact] public void C1_09_WblockRoundTrip_IsNotClassifiedByPathOrHandleProvenance()
    {
        Assert.DoesNotContain("Wblock", Preflight);
        Assert.DoesNotContain("FileName", Preflight);
        Assert.DoesNotContain("FingerprintGuid", Preflight);
    }

    [Fact] public void C1_10_SourceIdentity_IsContentBasedAndSnapshotLeased()
    {
        Assert.Contains("SHA256.HashData", Command);
        Assert.Contains("FileShare.Read", Command);
        Assert.Contains("sha256:", Command);
    }

    [Fact] public void C1_11_RootCollision_IsCheckedBeforeAndInsideTransaction()
    {
        Assert.Contains("EnsureNoCollision(document.Database, manifest);", Command);
        Assert.Contains("EnsureNoCollision(transaction, document.Database, manifest);", Command);
        Assert.Contains("root-btr-collision", Command);
    }

    [Fact] public void C1_12_NestedCollision_UsesReachableSymbolNames() =>
        Assert.Contains("nested-user-btr-collision", Command);

    [Fact] public void C1_13_SystemSupport_UsesOnlyTheExactRelationshipPredicate()
    {
        Assert.Contains("styleReference &&", Preflight);
        Assert.Contains("containedBlockReferenceCount == 0", Preflight);
        Assert.Contains("directBlockReferenceCount == 0", Preflight);
        Assert.Contains("!reachability.Model", Preflight);
        Assert.Contains("!reachability.Paper", Preflight);
        Assert.DoesNotContain("_Open90", Preflight);
        Assert.DoesNotContain("_Oblique", Preflight);
    }

    [Fact] public void C1_14_WholeRoof_IsRejectedBeforeInsert() =>
        Assert.True(Index(Command, "manifest.Decision.IsAllowed") < Index(Command, "Database.Insert"));

    [Fact] public void C1_15_MalformedAndUnknownMetadata_IsFailClosed() =>
        Assert.Contains("IsUnknownKrovyRegApp", Preflight);

    [Fact] public void C1_16_FutureSchemas_AreRejected()
    {
        Assert.Contains("TimberElementDataVersioning.IsSupported", Preflight);
        Assert.Contains("LabelMetadataSchemaVersion", Preflight);
        Assert.Contains("SchemaVersion: 1", Preflight);
    }

    [Fact] public void C1_17_PortableAndLegacyGenericTimber_AreIndependentlyValidatedAndCleared()
    {
        Assert.Contains("TryReadPortableXData", Preflight);
        Assert.Contains("TryReadLegacyExtensionDictionary", Preflight);
        Assert.Contains("ElementDataStore.TryClear", Sanitation);
    }

    [Fact] public void C1_18_MappedReused_IsNeverOpenedForWrite() =>
        Assert.True(Index(Sanitation, "RequireSingleMappedNew") < Index(Sanitation, "OpenMode.ForWrite"));

    [Fact] public void C1_19_NeutralImport_PerformsNoMetadataWrite()
    {
        Assert.DoesNotContain(".Write(", Sanitation);
        Assert.DoesNotContain("SetXData", Sanitation);
    }

    [Fact] public void C1_20_AkImport_IsIgnoredByGenericLifecycle()
    {
        Assert.Contains("ImportDwg = \"AK_IMPORT_DWG\"", Read("src", "AcKrovy.Localization", "CommandUiCatalog.cs"));
        Assert.Contains("StartsWith(\"AK_\"", Live);
    }

    [Fact] public void C1_21_NoNestedNativeCommandInvocation()
    {
        Assert.DoesNotContain("Editor.Command", Command);
        Assert.DoesNotContain("SendStringToExecute", Command);
    }

    [Fact] public void C1_22_OneTargetTransaction_ContainsInsertSanitationReferenceAndCommit()
    {
        Assert.Equal(1, Count(Command, "StartTransaction()"));
        Assert.True(Index(Command, "StartTransaction()") < Index(Command, "Database.Insert"));
        Assert.True(Index(Command, "Database.Insert") < Index(Command, "SanitizationService.Apply"));
        Assert.True(Index(Command, "SanitizationService.Apply") < Index(Command, "AppendEntity"));
        Assert.True(Index(Command, "AppendEntity") < Index(Command, "transaction.Commit();"));
    }

    [Fact] public void C1_23_NoUndoCompensationOrDeferredRepair()
    {
        foreach (var forbidden in new[] { "StartUndoMark", "EndUndoMark", "SendStringToExecute", "Editor.Command", "Idle", "Timer", "UserBreak" })
            Assert.DoesNotContain(forbidden, Command + Session + Sanitation);
    }

    [Fact] public void C1_24_Manifest_FreezesRequiredAuthority()
    {
        foreach (var token in new[] { "SourceIdentity", "SourceRootBlockId", "SourceObjectIds", "DependencyGraph", "IndividualTimbers", "Annotations", "SupportBlockTableRecords", "ReachableUserBlockNames", "ExpectedCloneObjectIds", "Decision" })
            Assert.Contains(token, Manifest);
    }

    [Fact] public void C1_25_ExactSideDatabase_IsLoadedOnceAndPassedDirectlyToInsert()
    {
        Assert.Equal(1, Count(Command, "new Database(false, true)"));
        Assert.Contains("Database.Insert(destinationName, sourceDatabase, true)", Command);
    }

    [Fact] public void C1_26_Callbacks_CaptureFactsOnlyAndNeverThrowOrMutate()
    {
        Assert.Contains("captured.Add(new(pair.Key, pair.Value, pair.IsCloned, pair.IsPrimary))", Session);
        Assert.DoesNotContain("OpenMode.ForWrite", Session);
        Assert.DoesNotContain("throw;", Session);
    }

    [Fact] public void C1_27_SanitationAndAnnotationErase_AreVerifiedBeforeCommit()
    {
        Assert.Contains("annotation.Erase();", Sanitation);
        Assert.True(Index(Command, "SanitizationService.Verify") < Index(Command, "transaction.Commit();"));
    }

    [Fact] public void C1_28_DebugFaults_AreCommandBodyOnlyAndReleaseGuarded()
    {
        Assert.Contains("AK_DEBUG_C1_ABORT_AFTER_INSERT", Command);
        Assert.Contains("AK_DEBUG_C1_ABORT_AFTER_SANITATION", Command);
        Assert.Contains("transactionCommitted", DbmodTimeline);
        Assert.Contains("#if DEBUG", Command);
        var callbackCapture = Session[Index(Session, "private void Capture(")..Index(Session, "private void ObjectAppended(")];
        Assert.DoesNotContain("throw", callbackCapture);
    }

    [Fact] public void C1_29_ProductionVeto_IsExactAndDoesNotBlockCopy()
    {
        Assert.Contains("\"-INSERT\"", Veto);
        Assert.Contains("\"INSERT\"", Veto);
        Assert.Contains("\"CLASSICINSERT\"", Veto);
        Assert.DoesNotContain("COPY", Veto);
        Assert.Contains("RoofImportCommandEntryProtection.Start();", Plugin);
        Assert.Contains("RoofImportCommandEntryProtection.Stop();", Plugin);
    }

    [Fact] public void C1_30_DbmodTimeline_IsDebugOnlyNumericAndHasTheExactFivePhases()
    {
        Assert.StartsWith("#if DEBUG", DbmodTimeline.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("ROOF_IMPORT_C1_DBMOD_TIMELINE", DbmodTimeline);
        Assert.Contains("Convert.ToInt32(", DbmodTimeline);
        Assert.Contains("AcApplication.GetSystemVariable(\"DBMOD\")", DbmodTimeline);
        foreach (var phase in new[]
                 {
                     "before-target-transaction",
                     "after-database-insert-return",
                     "after-target-transaction-dispose",
                     "after-rollback-audit-dispose",
                     "command-ended",
                 })
        {
            Assert.Equal(1, Count(Command + DbmodTimeline, $"\"{phase}\""));
        }
    }

    [Fact] public void C1_31_DbmodTimeline_IsReadOnlyAndHasNoDeferredOrCommandWork()
    {
        foreach (var forbidden in new[]
                 {
                     "SetSystemVariable",
                     "SaveAs",
                     "OpenMode.ForWrite",
                     "StartTransaction",
                     "StartOpenCloseTransaction",
                     "AppendEntity",
                     "Erase(",
                     "XData",
                     "DBDictionary",
                     "Idle",
                     "Timer",
                     "SendStringToExecute",
                     "Editor.Command",
                 })
        {
            Assert.DoesNotContain(forbidden, DbmodTimeline);
        }
    }

    [Fact] public void C1_32_DbmodTimeline_CommandEnded_IsExactDocumentScopedAndOneShot()
    {
        Assert.Contains("document.CommandEnded += CommandEnded;", DbmodTimeline);
        Assert.Contains("ReferenceEquals(sender, _armedDocument)", DbmodTimeline);
        Assert.Contains("_armedCommand", DbmodTimeline);
        Assert.Contains("StringComparison.Ordinal", DbmodTimeline);
        var ended = DbmodTimeline[Index(DbmodTimeline, "private static void CommandEnded")..Index(DbmodTimeline, "private static void CommandCancelledOrFailed")];
        Assert.True(Index(ended, "Disarm();") < Index(ended, "WriteCommandEnded(document.Editor, rollbackProof);"));
    }

    [Fact] public void C1_33_SourceRoot_IsTheOnlyReachableBtrExcludedFromMappingCoverage()
    {
        var expected = Preflight[Index(Preflight, "var expected = reachable")..Index(Preflight, "return new(")];
        Assert.Contains(".Where(id => id != modelSpaceId)", expected);
        Assert.Contains(".Concat(reachableEntities)", expected);
        Assert.Equal(1, Count(expected, "id != modelSpaceId"));
    }

    [Fact] public void C1_34_ExpectedEntities_NestedObjectsSanitationAndAnnotationsRemainStrict()
    {
        Assert.Contains("CollectReachable(transaction, modelSpaceId, reachable, graph);", Preflight);
        Assert.Contains(".Concat(reachableEntities)", Preflight);
        Assert.Contains("operation.Manifest.ExpectedCloneObjectIds", Command);
        Assert.Contains("RequireSingleMappedNew(source.SourceId", Sanitation);
        Assert.Equal(2, Count(Sanitation, "RequireSingleMappedNew(source.SourceId"));
        Assert.Contains("annotation.Erase();", Sanitation);
    }

    [Fact] public void C1_35_MappedReusedIntelligentObjects_StillReject()
    {
        Assert.Contains("!support.Contains(sourceId) && !pair.IsCloned", Command);
        Assert.Contains("unexpected-mapped-reused-object", Command);
        Assert.Contains("Intelligent source object was mapped-reused", Sanitation);
    }

    [Fact] public void C1_36_ReturnedRoot_IsIndependentlyAndStrictlyValidated()
    {
        var validation = Command[Index(Command, "private static void ValidateReturnedRoot")..Index(Command, "private static void ValidateFinalState")];
        foreach (var token in new[]
                 {
                     "operation.ReturnedRootId.Database != operation.TargetDatabase",
                     "operation.Document.Database != operation.TargetDatabase",
                     "operation.Manifest.SourceRootBlockId.Database != operation.SourceDatabase",
                     "table[operation.Manifest.DestinationBlockName] != operation.ReturnedRootId",
                     "string.Equals(root.Name, operation.Manifest.DestinationBlockName, StringComparison.Ordinal)",
                     "root.IsLayout",
                     "root.IsAnonymous",
                     "root.IsDependent",
                     "root.IsFromExternalReference",
                     "root.IsFromOverlayReference",
                     "operation.AppendedIds.Contains(operation.ReturnedRootId)",
                 })
        {
            Assert.Contains(token, validation);
        }
    }

    [Fact] public void C1_37_SourceRoot_HasNoSyntheticOrRequiredIdPair()
    {
        var validation = Command[Index(Command, "private static void ValidateMapping")..Index(Command, "private static void ValidateReturnedRoot")];
        Assert.Contains("operation.Manifest.ExpectedCloneObjectIds", validation);
        Assert.Contains("incomplete-operation-mapping", validation);
        Assert.DoesNotContain("SourceRootBlockId", validation);
        Assert.DoesNotContain("new RoofExternalImportIdPair", Command + Session);
    }

    [Fact] public void C1_38_FirstInsertMappingCallback_RemainsAuthoritativeWithoutAggregation()
    {
        Assert.Contains("if (_mappingCaptured || mapping.DeepCloneContext != DeepCloneType.InsertCopy)", Session);
        Assert.Contains("_mappingCaptured = true;", Session);
        Assert.Equal(1, Count(Session, "_pairs.AddRange(captured);"));
    }

    [Fact] public void C1_39_C1AbortProof_TreatsDbmodAsDiagnosticOnly()
    {
        Assert.Contains("dbmodBefore={proof.DbmodBefore}", DbmodTimeline);
        Assert.Contains("dbmodCommandEnded={dbmodCommandEnded}", DbmodTimeline);
        Assert.Contains("dbmodEquivalent={Bool(dbmodEquivalent)}", DbmodTimeline);
        var pass = DbmodTimeline[Index(DbmodTimeline, "var objectRollbackPass")..Index(DbmodTimeline, "editor.WriteMessage(", Index(DbmodTimeline, "var objectRollbackPass"))];
        Assert.DoesNotContain("dbmod", pass, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void C1_40_C1AbortProof_FailsForEveryObjectOrSupportResidue()
    {
        var pass = DbmodTimeline[Index(DbmodTimeline, "var objectRollbackPass")..Index(DbmodTimeline, "editor.WriteMessage(", Index(DbmodTimeline, "var objectRollbackPass"))];
        foreach (var token in new[]
                 {
                     "!proof.TransactionCommitted",
                     "proof.RootAbsent",
                     "proof.ImportedObjectsAbsent",
                     "proof.TopLevelReferenceAbsent",
                     "proof.SupportBtrsUnchanged",
                 })
        {
            Assert.Contains(token, pass);
        }
    }

    [Fact] public void C1_41_AfterMappingFailpoint_IsLoggedOnlyAfterBothValidations()
    {
        var validateMapping = Index(Command, "ValidateMapping(operation);");
        var validateRoot = Index(Command, "ValidateReturnedRoot(transaction, operation);");
        var validationPass = Index(Command, "WriteMappingValidationSucceeded(editor, faultMode, operation);");
        var fault = Index(Command, "InjectFault(editor, faultMode, RoofExternalImportFaultMode.AfterMappingValidation);");
        Assert.True(validateMapping < validateRoot);
        Assert.True(validateRoot < validationPass);
        Assert.True(validationPass < fault);
        Assert.Contains("ROOF_IMPORT_C1_ABORT_FAILPOINT mode={checkpoint} reached=true", Command);
    }

    [Fact] public void C1_42_NoDbmodResetSaveCompensationManualMaterializationOrNativeHandoff()
    {
        foreach (var forbidden in new[]
                 {
                     "SetSystemVariable",
                     "SaveAs",
                     "WblockCloneObjects",
                     "DeepCloneObjects",
                     "SendStringToExecute",
                     "Editor.Command",
                     "StartUndoMark",
                     "EndUndoMark",
                 })
        {
            Assert.DoesNotContain(forbidden, Command + Session + Sanitation + DbmodTimeline);
        }

        Assert.Contains("Database.Insert(destinationName, sourceDatabase, true)", Command);
    }

    [Fact] public void C1_43_ProductContext_DocumentsAcceptedDbmodConsequenceWithoutCallingItCorruption()
    {
        Assert.Contains("Neúspešný externý DWG import", ProjectContext);
        Assert.Contains("výzvu na uloženie", ProjectContext);
        Assert.Contains("Nejde o poškodenie", ProjectContext);
        Assert.Contains("KROVY `DBMOD` neresetuje", ProjectContext);
    }

    [Fact] public void C1_44_DocumentDatabaseBinding_UsesNativeDatabaseEqualityNotWrapperIdentity()
    {
        var validation = Command[Index(Command, "private static void ValidateReturnedRoot")..Index(Command, "private static void ValidateFinalState")];
        Assert.Contains("operation.Document.Database != operation.TargetDatabase", validation);
        Assert.DoesNotContain("ReferenceEquals", validation);
        Assert.Contains("operation.ReturnedRootId.Database != operation.TargetDatabase", validation);
        Assert.Contains("operation.Manifest.SourceRootBlockId.Database != operation.SourceDatabase", validation);
    }

    [Fact] public void C1_45_RollbackObjectEvidence_AndAllCallSitesAreDebugOnly()
    {
        Assert.StartsWith("#if DEBUG", RollbackObjects.TrimStart(), StringComparison.Ordinal);
        Assert.EndsWith("#endif", RollbackObjects.TrimEnd(), StringComparison.Ordinal);
        var release = WithoutDebugBlocks(Command + "\n" + Session + "\n" + RollbackObjects);
        foreach (var token in new[] { "ROOF_IMPORT_C1_ROLLBACK_OBJECT", "RoofExternalImportRollbackObjectDiagnostics", "CaptureRollbackDiagnostics", "VerifyRollback", "rollbackStates" })
            Assert.DoesNotContain(token, release);
    }

    [Fact] public void C1_46_RollbackEvidence_IsC1FaultScopedAfterTargetDisposalInExistingReadAudit()
    {
        Assert.Contains("session.CaptureRollbackDiagnostics = faultMode != RoofExternalImportFaultMode.None;", Command);
        Assert.True(Index(Command, "session.CaptureRollbackDiagnostics =") < Index(Command, "Database.Insert(destinationName"));
        Assert.True(Index(Command, "\"after-target-transaction-dispose\"") < Index(Command, "targetBefore.VerifyRollback("));
        var faultCatch = Command[Index(Command, "catch (System.Exception exception)")..Index(Command, "private static void ValidateMapping")];
        Assert.True(Index(faultCatch, "faultMode != RoofExternalImportFaultMode.None && targetBefore is not null") < Index(faultCatch, "targetBefore.VerifyRollback("));
        Assert.Contains("editor, appendedDuringOperation, rollbackDiagnostics", faultCatch);
        Assert.Contains("audit.ImportedObjectsAbsent", faultCatch);
        var audit = Command[Index(Command, "public RollbackAudit VerifyRollback(")..Index(Command, "private sealed record BlockFingerprint")];
        Assert.Equal(1, Count(audit, "StartOpenCloseTransaction()"));
        Assert.Contains("editor, transaction, state.Id, captured, state.Absent", audit);
    }

    [Fact] public void C1_47_RollbackPredicate_UsesStructuralOwnerSentinelRuleAndSamplesEveryIdBeforeDiagnostics()
    {
        var predicate = Command[Index(Command, "private static bool IsRolledBackObject(")..Index(Command, "private sealed class RoofExternalImportInjectedAbortException")];
        Assert.Contains("RoofExternalImportRollbackRules.IsAppendedObjectAbsent(", predicate);
        Assert.Contains("TryClassifyStructuralBlockSentinel(", predicate);
        Assert.Contains("value is not (BlockBegin or BlockEnd)", predicate);
        Assert.Contains("ownerIsValid = owner.IsValid;", predicate);
        Assert.Contains("ownerIsErased = owner.IsErased;", predicate);
        Assert.DoesNotContain("ownerIsErased =>", predicate);
        Assert.DoesNotContain("if (owner.IsErased)", predicate);
        // Surviving non-sentinel ObjectIds fail closed; only sentinels consult owner state.
        Assert.Contains("return false;", predicate);
        var audit = Command[Index(Command, "var rollbackStates = appendedIds")..Index(Command, "private sealed record BlockFingerprint")];
        Assert.Contains(".Select(id => (Id: id, Absent: IsRolledBackObject(id, transaction))).ToArray();", audit);
        Assert.True(Index(audit, ".ToArray();") < Index(audit, "RoofExternalImportRollbackObjectDiagnostics.Write("));
        Assert.Contains("foreach (var state in rollbackStates)", audit);
        Assert.Contains("rollbackStates.All(state => state.Absent)", audit);
        Assert.DoesNotContain(".Where(", audit);
        Assert.DoesNotContain(".Distinct(", audit);
        Assert.DoesNotContain("continue;", audit);
        Assert.DoesNotContain("break;", audit);
    }

    [Fact] public void C1_51_RollbackSentinelRule_LivesInCadNeutralCoreAndIsHostMapped()
    {
        var rules = Read("src", "AcKrovy.Core", "Services", "Roofs", "RoofExternalImportRollbackRules.cs");
        Assert.Contains("IsAppendedObjectAbsent(", rules);
        Assert.Contains("isStructuralBlockTableRecordSentinel", rules);
        Assert.Contains("if (!isStructuralBlockTableRecordSentinel)", rules);
        Assert.Contains("return !ownerIsValid || ownerIsErased;", rules);
        Assert.DoesNotContain("Autodesk", rules);
        Assert.DoesNotContain("ObjectId", rules);
        Assert.DoesNotContain("BlockBegin", rules);
        Assert.DoesNotContain("BlockEnd", rules);
        Assert.Contains("RoofExternalImportRollbackRules.IsAppendedObjectAbsent(", Command);
        Assert.Contains("BlockBegin or BlockEnd", Command);
    }

    [Fact] public void C1_52_RollbackPredicate_DoesNotBroadlyIgnoreTypesOrOwnerErasedEntities()
    {
        var predicate = Command[Index(Command, "private static bool IsRolledBackObject(")..Index(Command, "private sealed class RoofExternalImportInjectedAbortException")];
        Assert.DoesNotContain("if (obj is BlockBegin", predicate);
        Assert.DoesNotContain("if (value is BlockBegin", predicate);
        Assert.DoesNotContain("|| id.IsErased || owner", predicate);
        Assert.Contains("isStructuralBlockTableRecordSentinel: isSentinel", predicate);
        Assert.Contains("isStructuralBlockTableRecordSentinel: false", predicate);
        // Classification open is verdict input for surviving IDs only; diagnostics stay supplemental.
        Assert.Contains("OpenMode.ForRead, openErased: true", predicate);
        Assert.True(Index(Command, "Absent: IsRolledBackObject(id, transaction)") <
                    Index(Command, "RoofExternalImportRollbackObjectDiagnostics.Write("));
    }

    [Fact] public void C1_48_RollbackObjectEvidence_ContainsEveryRequiredFieldAndVerdictCounts()
    {
        foreach (var field in new[] { "id=", "capturedHandle=", "capturedType=", "isValid=", "isErased=", "canOpen=", "openError=", "runtimeType=", "currentHandle=", "ownerId=", "ownerIsValid=", "ownerIsErased=", "contributesFail=" })
            Assert.Contains(field, RollbackObjects);
        Assert.Contains("contributesFail={Bool(!absent)}", RollbackObjects);
        Assert.Contains("ROOF_IMPORT_C1_ROLLBACK_OBJECT_SUMMARY", Command);
        Assert.Contains("rollbackStates.Count(state => !state.Absent)", Command);
        Assert.Contains("checked={rollbackStates.Length}", Command);
        Assert.Contains("absentPass={rollbackStates.Length - survivingCount}", Command);
        Assert.Contains("survivingFail={survivingCount}", Command);
    }

    [Fact] public void C1_49_RollbackObjectEvidence_IsSupplementalReadOnlyAndFailuresAreReported()
    {
        Assert.Contains("transaction.GetObject(id, OpenMode.ForRead, openErased: true)", RollbackObjects);
        Assert.Contains("openError = Error(exception);", RollbackObjects);
        Assert.Contains("return \"unavailable:\" + Error(exception);", RollbackObjects);
        Assert.Contains("catch { }", RollbackObjects);
        foreach (var forbidden in new[] { "OpenMode.ForWrite", "UpgradeOpen", "Commit(", "Erase(", "AppendEntity", "StartTransaction", "StartOpenCloseTransaction", "SetSystemVariable", "XData", "Idle", "Timer", "SendStringToExecute", "Editor.Command", "absent =" })
            Assert.DoesNotContain(forbidden, RollbackObjects);
    }

    [Fact] public void C1_50_OriginalMetadata_IsCapturedFromAppendedObjectWithoutAffectingSessionValidity()
    {
        Assert.Contains("internal bool CaptureRollbackDiagnostics { get; set; }", Session);
        var capture = Session[Index(Session, "if (CaptureRollbackDiagnostics)")..Index(Session, "internal readonly record struct RoofExternalImportIdPair")];
        Assert.Contains("RollbackDiagnostics[e.DBObject.ObjectId]", capture);
        Assert.Contains("RoofExternalImportRollbackObjectDiagnostics.Capture(e.DBObject)", capture);
        Assert.Contains("catch { }", capture);
        Assert.DoesNotContain("_captureFailure =", capture);
        Assert.DoesNotContain("GetObject(", capture);
        Assert.Contains("Read(() => value.Handle.ToString())", RollbackObjects);
        Assert.Contains("Read(() => value.GetType().FullName ?? value.GetType().Name)", RollbackObjects);
    }

    private static string WithoutDebugBlocks(string source)
    {
        var lines = new List<string>();
        var debugDepth = 0;
        foreach (var line in source.Split('\n'))
        {
            var directive = line.Trim();
            if (directive == "#if DEBUG") { debugDepth++; continue; }
            if (directive == "#endif") { Assert.True(debugDepth > 0); debugDepth--; continue; }
            Assert.False(directive.StartsWith("#if", StringComparison.Ordinal) || directive.StartsWith("#else", StringComparison.Ordinal));
            if (debugDepth == 0) lines.Add(line);
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

    private static int Index(string source, string token, int startIndex)
    {
        var index = source.IndexOf(token, startIndex, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Missing token: {token}");
        return index;
    }

    private static int Count(string source, string token)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0; index += token.Length)
            count++;
        return count;
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine(new[] { Root }.Concat(path).ToArray()));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
