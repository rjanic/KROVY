using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class MonopitchRafterStage2D4BClipboardTests
{
    private static readonly string Live = ReadAutoCad("LiveGeometrySynchronizationService.cs");
    private static readonly string Snapshot = ReadAutoCad(
        "RoofGeneratedCopyPreCommandSnapshotService.cs");
    private static readonly string Inspection = ReadAutoCad(
        "RoofClipboardPastePayloadInspectionService.cs");
    private static readonly string Degradation = ReadAutoCad(
        "RoofForeignClipboardDegradationService.cs");
    private static readonly string ElementStore = ReadAutoCad("ElementDataStore.cs");
    private static readonly string CommandRules = ReadCore("Services", "LiveGeometryCommandRules.cs");
    private static readonly string OwnershipRules = ReadCore(
        "Services", "Roofs", "RoofClipboardPasteOwnershipRules.cs");
    private static readonly string ProvenanceLifecycle = ReadCore(
        "Services", "Roofs", "RoofClipboardProvenanceLifecycle.cs");

    [Fact]
    public void FinalPayloadRescan_UsesExactPerDocumentPasteIdsAtCommandEnded()
    {
        var appended = Member(Live, "private void ObjectAppended(", "private void ObjectModified(");
        var refresh = Member(Live, "private void RefreshCandidates(", "private static void RefreshTimberElements(");
        var capture = Index(appended, "_appendedPasteEntityIds.TryAdd(entity.ObjectId)");
        var slopeArrowObservation = Index(appended, "SlopeArrowStore.TryRead(entity");
        var slopeAngleObservation = Index(appended, "SlopeAngleTextStore.TryRead(entity");
        var labelObservation = Index(appended, "ElementLabelStore.TryRead(entity");

        Assert.Contains("_appendedPasteEntityIds", Live);
        Assert.Contains("IsClipboardPasteCommand(_currentGlobalCommandName)", appended);
        Assert.Contains("_appendedPasteEntityIds.TryAdd(entity.ObjectId)", appended);
        Assert.True(capture < slopeArrowObservation);
        Assert.True(capture < slopeAngleObservation);
        Assert.True(capture < labelObservation);
        Assert.Contains("var appendedPasteEntityIds = _appendedPasteEntityIds.Drain();", refresh);
        Assert.Contains("RoofClipboardPastePayloadInspectionService.Inspect(", refresh);
        Assert.Contains("appendedEntityIds", Inspection);
        Assert.DoesNotContain("ModelSpace", Inspection);
        Assert.DoesNotContain("GetClosestPointTo", Inspection);
    }

    [Fact]
    public void FinalStateReadsAllRequiredKrovyPayloadKinds()
    {
        Assert.Contains("RoofDefinitionStore.Read(entity).Exists", Inspection);
        Assert.Contains("RoofGeneratedTimberStore.Read(entity).Exists", Inspection);
        Assert.Contains("RoofAttachedManualTimberStore.Read(entity).Exists", Inspection);
        Assert.Contains("ElementDataStore.TryRead(entity, transaction", Inspection);
        Assert.Contains("ElementLabelStore.TryRead(entity", Inspection);
        Assert.Contains("SlopeArrowStore.TryRead(entity", Inspection);
        Assert.Contains("SlopeAngleTextStore.TryRead(entity", Inspection);
        Assert.Contains("PostFootprintPerpendicularAnnotationStore.TryRead(entity", Inspection);
    }

    [Fact]
    public void ClassificationAndForeignFences_PrecedeEveryMutableLifecycle()
    {
        var refresh = Member(Live, "private void RefreshCandidates(", "private static void RefreshTimberElements(");
        var inspect = Index(refresh, "RoofClipboardPastePayloadInspectionService.Inspect(");
        var classify = Index(refresh, "clipboardPasteProvenance?.ProvenanceKind");
        var wholeRoof = Index(refresh, "RoofClipboardPasteOwnershipAction.SkipWholeRoof");
        var degrade = Index(refresh, "RoofForeignClipboardDegradationService.Process(");
        var resize = Index(refresh, "RoofLiveResizeService.Process(");
        var genericRefresh = Index(refresh, "RefreshTimberElements(");
        var attached = Index(refresh, "RoofAttachedManualCopyCloneReinitializeService.Process(");
        var generated = Index(refresh, "RoofGeneratedRafterCopyOwnershipRehydrationService.Process(");

        Assert.True(inspect < classify);
        Assert.True(classify < wholeRoof && wholeRoof < degrade);
        Assert.True(degrade < resize && resize < genericRefresh);
        Assert.True(genericRefresh < attached && attached < generated);
    }

    [Fact]
    public void WholeRoofPriority_IsProvenanceIndependentAndReturnsBeforeMutation()
    {
        var classification = Member(
            OwnershipRules,
            "RoofClipboardPasteProvenanceKind provenanceKind",
            "bool sameDrawingProvenanceProven");
        Assert.True(
            Index(classification, "if (appendedRoofOwnerDetected)") <
            Index(classification, "if (intelligentTimberCount <= 0)"));
        Assert.True(
            Index(classification, "if (appendedRoofOwnerDetected)") <
            Index(classification, "provenanceKind =="));
        Assert.Contains("RoofClipboardPasteOwnershipAction.SkipWholeRoof", classification);

        var refresh = Member(Live, "private void RefreshCandidates(", "private static void RefreshTimberElements(");
        Assert.Contains(
            "clipboardDecision.Action ==\r\n                    RoofClipboardPasteOwnershipAction.SkipWholeRoof",
            NormalizeCrLf(refresh));
        Assert.Contains("return;", refresh);
    }

    [Fact]
    public void ForeignGeneratedAndAttachedMetadata_DegradesToVisiblePlainLine()
    {
        Assert.Contains("timber.Visible = true", Degradation);
        Assert.Contains("RoofGeneratedTimberStore.TryClear(", Degradation);
        Assert.Contains("RoofAttachedManualTimberStore.TryClear(", Degradation);
        Assert.Contains("ElementDataStore.TryClear(", Degradation);
        Assert.Contains("RoofGeneratedTimberStore.Read(timber).Exists", Degradation);
        Assert.Contains("RoofAttachedManualTimberStore.Read(timber).Exists", Degradation);
        Assert.Contains("ElementDataStore.TryRead(timber, transaction", Degradation);
        Assert.DoesNotContain("StartPoint =", Degradation);
        Assert.DoesNotContain("EndPoint =", Degradation);
        Assert.DoesNotContain("Layer =", Degradation);
        Assert.DoesNotContain("Color =", Degradation);
    }

    [Fact]
    public void GenericAndLegacyIdentityAreRemovedWithoutTouchingForeignXData()
    {
        var clear = Member(ElementStore, "public static bool TryClear(", "public static IReadOnlyList<TypedValue> BuildSection(");
        Assert.Contains("HasApplicationSection(entity, RegAppName)", clear);
        Assert.Contains("TryClearLegacyExtensionDictionary", clear);
        Assert.Contains("LegacyApplicationDictionaryName", ElementStore);
        Assert.Contains("LegacyElementDataRecordName", ElementStore);
        Assert.Contains("appDictionary.Remove(LegacyElementDataRecordName)", ElementStore);
        Assert.DoesNotContain("entity.XData = null", clear);
        Assert.DoesNotContain("root.Remove", clear);
    }

    [Fact]
    public void AnnotationCleanup_IsLimitedToFinalCurrentPayloadAndCreatesNoReplacement()
    {
        Assert.Contains("SelectCurrentPasteAnnotations(", Degradation);
        Assert.Contains("payload.AnnotationIds.Distinct()", Degradation);
        Assert.Contains("annotation.Erase()", Degradation);
        Assert.DoesNotContain("FindBySource", Degradation);
        Assert.DoesNotContain("EnsureForElement", Degradation);
        Assert.DoesNotContain("TimberAnnotationService", Degradation);
        Assert.DoesNotContain("ElementLabelService", Degradation);
    }

    [Fact]
    public void GroupDetach_RequiresActualRoofOwnerAndExactCanonicalIdentity()
    {
        Assert.Contains("database.GroupDictionaryId", Degradation);
        Assert.Contains("RoofDefinitionStore.Read(owner).Data is null", Degradation);
        Assert.Contains("RoofDisplayGroupService.BuildCanonicalGroupName", Degradation);
        Assert.Contains("members.Where(targets.Contains)", Degradation);
        Assert.DoesNotContain("StartsWith", Degradation);
        Assert.DoesNotContain("GroupNamePrefix", Degradation);
    }

    [Fact]
    public void ForeignPathNeverResolvesUntrustedOwnerOrRunsRoofLifecycle()
    {
        Assert.DoesNotContain("GetObjectId", Degradation);
        Assert.DoesNotContain("FindByOwner", Degradation);
        Assert.DoesNotContain("OwnerSelectionResolver", Degradation);
        Assert.DoesNotContain("Nearest", Degradation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RoofLiveResizeService", Degradation);
        Assert.DoesNotContain("RoofGeneratedRafterSetService", Degradation);
        Assert.DoesNotContain("SkippedAmbiguousRecipe", Degradation);
    }

    [Fact]
    public void BatchCleanup_IsOneTransactionWithFailClosedCloneEraseFallback()
    {
        Assert.Contains("ExecuteCleanup(document, cleanupPayload, eraseTimbers: false)", Degradation);
        Assert.Contains("ExecuteCleanup(document, cleanupPayload, eraseTimbers: true)", Degradation);
        Assert.Contains("StartTransaction()", Degradation);
        Assert.Contains("transaction.Commit()", Degradation);
        Assert.Contains("timber.Erase()", Degradation);
        Assert.Contains("fail-closed:", Degradation);
    }

    [Fact]
    public void PasteCleanupSharesNativeGroupedUndoAndUndoRedoRemainZeroDb()
    {
        Assert.Contains("IsClipboardPasteCommand(globalCommandName)", CommandRules);
        Assert.Contains("RequiresGroupedUndoMark(e.GlobalCommandName)", Live);
        var ended = Member(Live, "private void CommandEnded(", "private void CommandCancelled(");
        Assert.True(Index(ended, "RefreshCandidates(") < Index(ended, "EndStretchUndoMark()"));
        Assert.Contains("ClearPendingLiveGeometryState();", ended);
        Assert.DoesNotContain("Application.Idle", Live + Degradation);
        Assert.DoesNotContain("Timer", Degradation);
    }

    [Fact]
    public void MultiDocumentPasteStateIsTrackerLocalAndTargetDestructionClearsActiveScope()
    {
        Assert.Contains("private readonly LiveGeometryRefreshCoordinator<ObjectId> _appendedPasteEntityIds", Live);
        Assert.Contains("RoofClipboardPasteProvenanceDecision? _clipboardPasteProvenanceForCurrentCommand", Live);
        Assert.Contains("TargetDocument", ProvenanceLifecycle);
        Assert.Contains("TargetCommand", ProvenanceLifecycle);
        Assert.Contains("ClipboardRevision", ProvenanceLifecycle);
        Assert.Contains("ReferenceEquals(active.TargetDocument, document)", ProvenanceLifecycle);
        Assert.Contains("ReferenceEquals(active.Provenance?.SourceDocument, document)", ProvenanceLifecycle);
        Assert.Contains("ClipboardLifecycle.ClearForDocument(document)", Snapshot);
        Assert.Contains("_activeSnapshot = ClipboardLifecycle.ActivePayload", Snapshot);
    }

    [Fact]
    public void CancelFailAndDocumentDispose_ClearThePerCommandPasteQueue()
    {
        var cancelled = Member(Live, "private void CommandCancelled(", "private void CommandFailed(");
        var failed = Member(Live, "private void CommandFailed(", "private void ClearPendingLiveGeometryState(");
        var clear = Member(Live, "private void ClearPendingLiveGeometryState(", "private void EndStretchUndoMark(");
        var dispose = Member(Live, "public void Dispose()", "private void ObjectAppended(");

        Assert.Contains("ClearPendingLiveGeometryState();", cancelled);
        Assert.Contains("ClearPendingLiveGeometryState();", failed);
        Assert.Contains("_appendedPasteEntityIds.Clear();", clear);
        Assert.Contains("_appendedPasteEntityIds.Clear();", dispose);
        Assert.Contains("RoofGeneratedCopyPreCommandSnapshotService.ClearForDocument(_document)", dispose);
    }

    [Fact]
    public void DiagnosticsReportProvenancePayloadActionAndAtomicOutcome()
    {
        Assert.Contains("ROOF_CLIPBOARD_CLASSIFY", Live);
        Assert.Contains("provenance=", Live);
        Assert.Contains("wholeRoofDetected=", Live);
        Assert.Contains("action=", Live);
        Assert.Contains("ROOF_FOREIGN_CLIPBOARD_DEGRADE", Degradation);
        Assert.Contains("metadataCleared=", Degradation);
        Assert.Contains("groupDetached=", Degradation);
    }

    [Theory]
    [InlineData("PASTECLIP")]
    [InlineData("PASTEORIG")]
    public void SameDwgClipboardAdoptionAndNativeCopyRemainPublished(string pasteCommand)
    {
        var same = RoofClipboardPasteOwnershipRules.Classify(
            pasteCommand,
            RoofClipboardPasteProvenanceKind.KnownSameDocument,
            1,
            false);

        Assert.Equal(RoofClipboardPasteOwnershipAction.UseStage2D4AAdoption, same.Action);
        Assert.True(same.ShouldProcessIndividualTimber);
        Assert.Contains("IsSameDwgCopyOwnershipCommand", Live);
        Assert.Contains("if (nativeCopy || clipboardDecision.ShouldProcessIndividualTimber)", Live);
    }

    [Fact]
    public void WholeRoofWithAnnotations_ReturnsBeforeForeignDegradation()
    {
        var decision = RoofClipboardPasteOwnershipRules.Classify(
            "PASTECLIP",
            RoofClipboardPasteProvenanceKind.KnownForeignDocument,
            intelligentTimberCount: 4,
            appendedRoofOwnerDetected: true);
        var refresh = Member(Live, "private void RefreshCandidates(", "private static void RefreshTimberElements(");

        Assert.Equal(RoofClipboardPasteOwnershipAction.SkipWholeRoof, decision.Action);
        Assert.True(
            Index(refresh, "RoofClipboardPasteOwnershipAction.SkipWholeRoof") <
            Index(refresh, "RoofForeignClipboardDegradationService.Process("));
    }

    [Theory]
    [InlineData(RoofClipboardPasteProvenanceKind.KnownForeignDocument, 1,
        RoofClipboardPasteOwnershipAction.DegradeForeignIndividual)]
    [InlineData(RoofClipboardPasteProvenanceKind.KnownForeignDocument, 2,
        RoofClipboardPasteOwnershipAction.DegradeForeignBatch)]
    [InlineData(RoofClipboardPasteProvenanceKind.Unknown, 1,
        RoofClipboardPasteOwnershipAction.DegradeUnknownBatch)]
    [InlineData(RoofClipboardPasteProvenanceKind.Unknown, 2,
        RoofClipboardPasteOwnershipAction.DegradeUnknownBatch)]
    public void IntelligentPayloadWithAnnotations_KeepsExpectedAtomicAction(
        RoofClipboardPasteProvenanceKind provenance,
        int intelligentTimberCount,
        RoofClipboardPasteOwnershipAction expectedAction)
    {
        var decision = RoofClipboardPasteOwnershipRules.Classify(
            "PASTECLIP",
            provenance,
            intelligentTimberCount,
            appendedRoofOwnerDetected: false);

        Assert.Equal(expectedAction, decision.Action);
    }

    [Fact]
    public void CopyBasePasteOrigForeignPayload_UsesTheSameDegradationRoute()
    {
        Assert.True(LiveGeometryCommandRules.IsClipboardCopySourceCommand("COPYBASE"));
        var decision = RoofClipboardPasteOwnershipRules.Classify(
            "PASTEORIG",
            RoofClipboardPasteProvenanceKind.KnownForeignDocument,
            intelligentTimberCount: 1,
            appendedRoofOwnerDetected: false);

        Assert.Equal(
            RoofClipboardPasteOwnershipAction.DegradeForeignIndividual,
            decision.Action);
    }

    [Fact]
    public void AToBToA_PreservesDurableSourceClipboardAuthority()
    {
        var lifecycle = new RoofClipboardProvenanceLifecycle<object, object, object>();
        var documentA = new object();
        var databaseA = new object();
        lifecycle.BeginCopy(documentA, databaseA, "COPYCLIP", new object());
        Assert.True(lifecycle.CompleteCopy(101));

        var inB = lifecycle.BeginPaste(new object(), new object(), 101, "PASTECLIP");
        Assert.Equal(RoofClipboardPasteProvenanceKind.KnownForeignDocument, inB.ProvenanceKind);
        lifecycle.CompletePaste();
        var backInA = lifecycle.BeginPaste(documentA, databaseA, 101, "PASTECLIP");

        Assert.Equal(RoofClipboardPasteProvenanceKind.KnownSameDocument, backInA.ProvenanceKind);
        Assert.True(backInA.IsValid);
    }

    [Fact]
    public void SameFilenameReopenCannotReestablishDocumentIdentity()
    {
        var lifecycle = new RoofClipboardProvenanceLifecycle<NamedDocument, object, object>();
        var original = new NamedDocument("A.dwg");
        var reopened = new NamedDocument("A.dwg");
        var sharedWrapper = new object();
        lifecycle.BeginCopy(original, sharedWrapper, "COPYCLIP", new object());
        Assert.True(lifecycle.CompleteCopy(101));

        lifecycle.ClearForDocument(original);
        var decision = lifecycle.BeginPaste(reopened, sharedWrapper, 101, "PASTECLIP");

        Assert.Equal(original.Path, reopened.Path);
        Assert.Equal(RoofClipboardPasteProvenanceKind.Unknown, decision.ProvenanceKind);
        Assert.False(decision.IsValid);
    }

    [Fact]
    public void VersionAndSchemaFreezeRemainPublished()
    {
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Equal(3, RoofAttachedManualTimberDataSchema.CurrentVersion);
        Assert.Equal(3, (int)RoofKind.Monopitch);
        var props = File.ReadAllText(Path.Combine(RepositoryRoot(), "Directory.Build.props"));
        Assert.Contains("<AcKrovyVersion>0.23.0</AcKrovyVersion>", props);
    }

    private sealed record NamedDocument(string Path);

    private static string ReadAutoCad(string fileName) => File.ReadAllText(Path.Combine(
        RepositoryRoot(), "src", "AcKrovy.AutoCAD", "Infrastructure", fileName));

    private static string ReadCore(params string[] parts) => File.ReadAllText(Path.Combine(
        new[] { RepositoryRoot(), "src", "AcKrovy.Core" }.Concat(parts).ToArray()));

    private static string Member(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);

    private static int Index(string source, string token)
    {
        var index = source.IndexOf(token, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Missing token: {token}");
        return index;
    }

    private static string NormalizeCrLf(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
