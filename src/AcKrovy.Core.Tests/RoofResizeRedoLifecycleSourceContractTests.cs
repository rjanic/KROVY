using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofResizeRedoLifecycleSourceContractTests
{
    private static readonly string Resize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string RafterSet = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterSetService.cs");
    private static readonly string RedoDiag = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofRedoStateDiag.cs");
    private static readonly string Sync = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string GeneratedStore = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedTimberStore.cs");
    private static readonly string SourceLine = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "TimberSourceLineCreationService.cs");
    private static readonly string ElementStore = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "ElementDataStore.cs");
    private static readonly string IdentityService = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "TimberElementItemIdentityService.cs");
    private static readonly string PostAtomicDiag = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedPostAtomicWriteDiag.cs");
    private static readonly string GroupService = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayGroupService.cs");

    [Fact]
    public void Resize_IsASingleWriteTransaction()
    {
        var apply = Member(Resize, "private static void ApplyResizes", "private static ResizeApplyResult TryApplyResize");
        Assert.Equal(1, Count(apply, "StartTransaction"));
        Assert.Equal(1, Count(apply, "transaction.Commit()"));
    }

    [Fact]
    public void RelocateUnlockIndicators_SkipsAlreadyResizedOwners()
    {
        var relocate = Member(Resize, "private static void RelocateUnlockIndicators", "private static InspectionPlan Inspect");
        Assert.Contains("ownerIds.ExceptWith(plan.ResizeOwnerIds)", relocate);
        Assert.DoesNotContain("ownerIds.UnionWith(plan.ResizeOwnerIds)", relocate);
    }

    [Fact]
    public void RedoStateDiagnostics_AreReadOnlyAndPresent()
    {
        Assert.Contains("ROOF_REDO_STATE", RedoDiag);
        Assert.Contains("ROOF_RESIZE_TXN", RedoDiag);
        // Uses only the caller's transaction; never opens its own, never writes.
        Assert.DoesNotContain("StartTransaction", RedoDiag);
        Assert.DoesNotContain("StartOpenCloseTransaction", RedoDiag);
        Assert.DoesNotContain("Commit()", RedoDiag);
        Assert.DoesNotContain("LockDocument", RedoDiag);
        Assert.DoesNotContain("UpgradeOpen", RedoDiag);
        Assert.DoesNotContain("OpenMode.ForWrite", RedoDiag);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", RedoDiag);
    }

    [Fact]
    public void UndoRedo_IgnoreStaysArmedThroughDeferredEvents()
    {
        // CommandEnded re-arms ignore for undo/redo so deferred ObjectModified /
        // ObjectAppended raised by native U stay suppressed until the next real command.
        Assert.Contains("_ignoreCurrentCommand = isUndoRedo", Sync);
        Assert.Contains("IsUndoRedoCommand(e.GlobalCommandName)", Sync);
        Assert.Contains("_ignoreCurrentCommand", Sync);
    }

    [Fact]
    public void UndoRedoCommandEnded_NeverAccessesDatabaseOrDiagnostics()
    {
        // The live-geometry synchronization service owns the undo/redo lifecycle and must
        // never reference the DB-accessing roof state diagnostic on that boundary.
        Assert.DoesNotContain("RoofRedoStateDiag", Sync);
        var ended = Member(Sync, "private void CommandEnded", "private void CommandCancelled");
        Assert.DoesNotContain("StartTransaction(", ended);
        Assert.DoesNotContain("StartOpenCloseTransaction(", ended);
        Assert.DoesNotContain("LockDocument(", ended);
        Assert.DoesNotContain("GetObject(", ended);
        Assert.Contains("_ignoreCurrentCommand = isUndoRedo", ended);
    }

    [Fact]
    public void GeneratedReplacement_DetachesBeforeEraseAndNeverClearsXData()
    {
        var detach = RafterSet.IndexOf("DetachMembersBeforeErase", System.StringComparison.Ordinal);
        var eraseCall = RafterSet.IndexOf("entity.Erase()", System.StringComparison.Ordinal);
        Assert.True(detach >= 0 && eraseCall > detach, "Detach must precede entity.Erase().");
        Assert.DoesNotContain("TryClear", RafterSet);
    }

    [Fact]
    public void UndoRedo_NoWriteGuardRemainsOnProcess()
    {
        var process = Member(Resize, "public static IReadOnlyCollection<ObjectId> Process", "public static bool TryBeginGroupedUndo");
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", process);
        Assert.Contains("return Array.Empty<ObjectId>()", process);
        Assert.DoesNotContain("StartTransaction", process);
    }

    [Fact]
    public void RoofGeneratedWrite_CanonicalIdentityHasNoSoftPointer()
    {
        // The canonical Entity.XData ResultBuffer must contain NO 1005 and no LINK
        // RegApp — native U/REDO replays Entity.XData as one property assignment. The
        // write path (Write + BuildSection) builds pure ASCII canonical identity.
        var write = Member(
            GeneratedStore,
            "public static void Write",
            "public static IReadOnlyList<TypedValue> BuildSection");
        var build = Member(
            GeneratedStore,
            "public static IReadOnlyList<TypedValue> BuildSection",
            "public static void WriteAtomic");
        Assert.Contains("RoofGeneratedTimberDataCodec.Encode", build);
        Assert.DoesNotContain("DxfOwnerHandleCode", write + build);
        Assert.DoesNotContain("LinkRegAppName", write + build);
    }

    [Fact]
    public void RoofGeneratedRead_FallsBackToAsciiWhenLinkMissing()
    {
        // Canonical ASCII owner is the fallback authority; the LINK 1005 only overrides
        // when present and valid (clone remap). Legacy combined payloads stay readable.
        Assert.Contains("ReadLinkOwner(entity) ?? legacyOwnerReference", GeneratedStore);
        Assert.Contains("ReadLinkOwner", GeneratedStore);
    }

    [Fact]
    public void RoofGeneratedClear_RemovesBothSections()
    {
        // TryClear clears both the canonical identity and the clone LINK sections.
        Assert.Contains("eraseLink", GeneratedStore);
        Assert.Contains("eraseCanonical", GeneratedStore);
        Assert.Contains("ReadLinkOwner(entity) is not null", GeneratedStore);
    }

    [Fact]
    public void OwnershipInvariant_EmittedAtResizeStart()
    {
        Assert.Contains("ROOF_GENERATED_OWNERSHIP_INVARIANT", RedoDiag);
        Assert.Contains("CaptureOwnershipInvariant", RedoDiag);
        Assert.Contains("missingGeneratedMetadata", RedoDiag);
        Assert.Contains("CaptureOwnershipInvariant", Resize);
    }

    [Fact]
    public void SourceResizePrecedence_SuppressesGeneratedChildTamper()
    {
        // For a source SupportedResize, the owner is excluded from generated-member
        // tamper handling, so incidental child ObjectModified cannot be processed
        // independently before the parent resize classification.
        Assert.Contains("resizeOwners.Contains(ownerId)", Resize);
        Assert.Contains("generatedMemberTamperCandidates", Resize);
        Assert.Contains("generatedMemberTamperOwners", Resize);
    }

    [Fact]
    public void ObjectModifiedHandler_NeverClearsOrWritesGeneratedIdentity()
    {
        var objModified = Member(Sync, "private void ObjectModified", "private void ObjectErased");
        Assert.DoesNotContain("TryClear", objModified);
        Assert.DoesNotContain("RoofGeneratedTimberStore.Clear", objModified);
        Assert.DoesNotContain("RoofGeneratedTimberStore.Write", objModified);
        Assert.DoesNotContain("entity.XData", objModified);
        Assert.DoesNotContain("OpenMode.ForWrite", objModified);
        Assert.DoesNotContain("UpgradeOpen", objModified);
    }

    [Fact]
    public void GeneratedCreation_UsesSingleAtomicXDataAssignment()
    {
        // The generated creation path must route through the atomic writer, not a
        // metadataStore.Write followed by a secondary RoofGeneratedTimberStore.Write.
        Assert.Contains("RoofGeneratedTimberStore.WriteAtomic", SourceLine);
        Assert.Contains(
            "Func<Line, Transaction, int, IReadOnlyList<TypedValue>?>",
            SourceLine);
        Assert.Contains("return RoofGeneratedTimberStore.BuildSection", RafterSet);
        Assert.DoesNotContain("RoofGeneratedTimberStore.Write(", RafterSet);
    }

    [Fact]
    public void AtomicWriter_IncludesGenericAndGeneratedSections_NoSoftPointer()
    {
        var writeAtomic = Member(
            GeneratedStore,
            "public static void WriteAtomic",
            "public static IReadOnlyList<ObjectId> FindByOwner");
        Assert.Contains("ElementDataStore.BuildSection", writeAtomic);
        Assert.Contains("generatedSection", writeAtomic);
        Assert.Contains("entity.XData = buffer", writeAtomic);
        Assert.DoesNotContain("DxfOwnerHandleCode", writeAtomic);
    }

    [Fact]
    public void GeneratedBuildSection_IsCanonicalAsciiOnly()
    {
        var buildSection = Member(
            GeneratedStore,
            "public static IReadOnlyList<TypedValue> BuildSection",
            "public static void WriteAtomic");
        Assert.Contains("RoofGeneratedTimberDataCodec.Encode", buildSection);
        Assert.Contains("DxfRegAppNameCode, RegAppName", buildSection);
        Assert.Contains("DxfAsciiStringCode, chunk", buildSection);
        Assert.DoesNotContain("DxfOwnerHandleCode", buildSection);
    }

    [Fact]
    public void GenericElementStore_RetainsBuildSectionRefactor()
    {
        // ElementDataStore.Write still assigns once via BuildSection; the generic path is
        // unchanged for ordinary (non-generated) timber.
        Assert.Contains("public static IReadOnlyList<TypedValue> BuildSection", ElementStore);
        Assert.Contains("values.AddRange(BuildSection(entity, transaction, data))", ElementStore);
        Assert.Contains("entity.XData = newXData", ElementStore);
    }

    [Fact]
    public void GeneratedCreation_ComputesFinalElementIdBeforeSingleAtomicWrite()
    {
        // The final ElementId is pre-numbered before the atomic write so the sync pass has
        // nothing to rewrite; WriteAtomic uses the effective (final) data.
        Assert.Contains("ComputeFinalElementIds", IdentityService);
        Assert.Contains("ComputeFinalElementIds", SourceLine);
        Assert.Contains("finalElementIds[index]", SourceLine);
        Assert.Contains("RoofGeneratedTimberStore.WriteAtomic", SourceLine);
        Assert.Contains("ElementId = finalElementIds[index]", SourceLine);
    }

    [Fact]
    public void GeneratedCreation_UsesT2Order_WriteAtomicBeforeAddNewlyCreatedDBObject()
    {
        // Generated branch must be AppendEntity -> WriteAtomic -> AddNewlyCreatedDBObject
        // (probe timing T2), NOT AddNewlyCreatedDBObject -> WriteAtomic (failing T3).
        var appendIndex = SourceLine.IndexOf("AppendEntity(line)", StringComparison.Ordinal);
        var writeAtomicIndex = SourceLine.IndexOf(
            "RoofGeneratedTimberStore.WriteAtomic",
            StringComparison.Ordinal);
        var addNewlyAfterWriteAtomic = SourceLine.IndexOf(
            "AddNewlyCreatedDBObject",
            writeAtomicIndex,
            StringComparison.Ordinal);
        Assert.True(appendIndex >= 0, "AppendEntity(line) missing.");
        Assert.True(writeAtomicIndex > appendIndex, "WriteAtomic must follow AppendEntity.");
        Assert.True(addNewlyAfterWriteAtomic > writeAtomicIndex,
            "AddNewlyCreatedDBObject must follow WriteAtomic (T2 order).");
    }

    [Fact]
    public void FinalElementId_ComputedWithoutWritingXData()
    {
        var compute = Member(
            IdentityService,
            "public static IReadOnlyList<string> ComputeFinalElementIds",
            "private sealed record TimberElementMeasurementEntry");
        Assert.Contains("TimberElementItemNumbering.AssignElementIds", compute);
        Assert.DoesNotContain("metadataStore.Write", compute);
        Assert.DoesNotContain("entity.XData", compute);
    }

    [Fact]
    public void PostAtomicWriteDiagnostic_IsCompactSummaryOnly()
    {
        // Per-member ROOF_GENERATED_POST_ATOMIC_WRITE line removed; only the compact
        // batch summary remains (and TraceSyncWrite is now a silent counter).
        Assert.DoesNotContain("ROOF_GENERATED_POST_ATOMIC_WRITE", PostAtomicDiag);
        Assert.Contains("ROOF_GENERATED_POST_ATOMIC_SUMMARY", PostAtomicDiag);
        Assert.Contains("TraceSyncWrite", PostAtomicDiag);
    }

    [Fact]
    public void Materialize_UsesSingleCreationPath_ForInitialAndRebuild()
    {
        // Materialize is the single creation path (initial generation + rebuild); it routes
        // through TimberSourceLineCreationService.Create which pre-numbers and writes once.
        Assert.Contains("TimberSourceLineCreationService.Create", RafterSet);
        Assert.Contains("return RoofGeneratedTimberStore.BuildSection", RafterSet);
    }

    [Fact]
    public void TemporaryDiagnostics_Removed()
    {
        // The temporary STRETCH trace, XData probe, and per-member state hooks are gone.
        Assert.DoesNotContain("RoofStretchMetadataTraceService", Sync);
        Assert.DoesNotContain("RoofXDataRedoProbeService", Resize);
        Assert.DoesNotContain("CaptureMemberState", Resize);
        Assert.DoesNotContain("CaptureMemberState", RedoDiag);
        Assert.DoesNotContain("ROOF_GENERATED_MEMBER_STATE", RedoDiag);
    }

    [Fact]
    public void RedoStateCapture_IsCompactSummary()
    {
        // No full key->handle map on success; compact duplicateKeyCount instead.
        Assert.DoesNotContain("generatedKeys", RedoDiag);
        Assert.DoesNotContain("generatedFindByOwnerCount", RedoDiag);
        Assert.Contains("generatedCount", RedoDiag);
        Assert.Contains("duplicateKeyCount", RedoDiag);
    }

    [Fact]
    public void GroupMutationDiag_SuppressedPerEntity()
    {
        // Per-entity ROOF_GROUP_MUTATION spam suppressed; compact undo invariant retained.
        Assert.DoesNotContain("ROOF_GROUP_MUTATION", GroupService);
        Assert.Contains("ROOF_GROUP_UNDO_INVARIANT", GroupService);
    }

    private static string Member(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);

    private static int Count(string source, string token) =>
        source.Split(token, System.StringSplitOptions.None).Length - 1;
}
