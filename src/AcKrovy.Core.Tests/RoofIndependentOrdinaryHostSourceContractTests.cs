using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Host routing guard. Interactive timing, native BRep and undo need AutoCAD retest.</summary>
public sealed class RoofIndependentOrdinaryHostSourceContractTests
{
    private static readonly string Edits = Read("Infrastructure", "RoofGeneratedMemberManualEditService.cs");
    private static readonly string Detach = Read("Infrastructure", "RoofIndependentOrdinaryDetachService.cs");
    private static readonly string Store = Read("Infrastructure", "RoofIndependentOrdinaryTimberStore.cs");
    private static readonly string Collector = Read("Infrastructure", "RoofAssemblyGroupMemberCollector.cs");
    private static readonly string Resize = Read("Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Lifecycle = Read("Infrastructure", "LiveGeometrySynchronizationService.cs");

    [Fact]
    public void ConfirmedMove_UsesIndependentTransition_GripHasSeparateFirstClaim()
    {
        var start = Edits.IndexOf("private static bool TryHandleFirstIndependentOrdinaryEdit", StringComparison.Ordinal);
        var end = Edits.IndexOf("private static bool ConfirmIndependentOrdinaryDetach", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var path = Edits[start..end];
        Assert.Contains("ConfirmIndependentOrdinaryDetach()", path);
        Assert.Contains("TryRestoreCancelledOrdinaryMove", Edits);
        var cancelStart = Edits.IndexOf("if (!ConfirmIndependentOrdinaryDetach())", start, StringComparison.Ordinal);
        var cancelEnd = Edits.IndexOf("var liveDefinition =", cancelStart, StringComparison.Ordinal);
        Assert.True(cancelStart >= 0 && cancelEnd > cancelStart);
        var cancelPath = Edits[cancelStart..cancelEnd];
        Assert.DoesNotContain("TryRecoverGeneratedMembersOnly", cancelPath);
        Assert.Contains("TryRestoreCancelledOrdinaryMove", cancelPath);
        Assert.Contains("RoofIndependentOrdinaryDetachService.TryDetach", path);
        Assert.DoesNotContain("TryAcceptUnlockedEdits", path);
        Assert.Contains("replay.Remove(member.Key)", path);
        Assert.Contains("RoofOrdinaryGripLifecycleService.Process(", Lifecycle);
        Assert.DoesNotContain("RoofAttachedManualTimberStore.Write", path);
        Assert.DoesNotContain("replay.Upsert", path);
        Assert.DoesNotContain("RoofGeneratedMemberOverrideMath.ComposeTranslation", path);
    }

    [Fact]
    public void Detach_PreservesLineAndElementId_TransfersPhysicalAndRemovesActiveOwnership()
    {
        Assert.Contains("line.ObjectId", Detach);
        Assert.Contains("timber.ElementId", Detach);
        Assert.Contains("Guid.NewGuid().ToString(\"N\")", Detach);
        Assert.Contains("RoofAssemblyGroupSyncService.DetachMembersBeforeErase", Detach);
        Assert.Contains("RoofGeneratedTimberStore.RegAppName", Detach);
        Assert.Contains("RoofPhysical3DGeneratedStore.RegAppName", Detach);
        Assert.Contains("RoofIndependentOrdinaryTimberStore.TransferFromRoof", Detach);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", Detach);
        Assert.Contains("RoofGeneratedTimberStore.FindByOwner", Collector);
        Assert.DoesNotContain("RoofIndependentOrdinaryTimberStore", Collector);
    }

    [Fact]
    public void OwnershipTransfer_ClearsRoofSectionsIndividually_ThenWritesMemberIdentity()
    {
        var start = Store.IndexOf("public static void TransferFromRoof", StringComparison.Ordinal);
        var end = Store.IndexOf("private static IEnumerable<TypedValue> BuildSection", start, StringComparison.Ordinal);
        var transfer = Store[start..end];
        Assert.Contains("entity.XData = erase", transfer);
        Assert.Contains("if (HasSection(entity, name))", transfer);
        Assert.Contains("Write(entity, transaction, data)", transfer);
        Assert.DoesNotContain("entity.XData = buffer", transfer);
        Assert.Contains("roofRegApps", transfer);
        Assert.Contains("RequiresGroupedUndoMark", Lifecycle);
    }

    [Fact]
    public void FailedMoveTransfer_UsesSnapshotRecovery_NotOuterExceptionSwallow()
    {
        Assert.Contains("catch (Exception ex)", Edits);
        Assert.Contains("throw new OrdinaryPhysicalReconcileException(\"Independent Ordinary detach failed.\")", Edits);
        Assert.Contains("RecoverFailedOrdinaryPhysicalReconcile(document, ownerId, ex, appendedTimberIds, modifiedIds)", Edits);
        Assert.Contains("TryRecoverGeneratedMembersOnly", Edits);
    }

    [Fact]
    public void MoveDetach_TransfersAnnotationsAndChecksGroupAndElementId()
    {
        Assert.Contains("FindAnnotations(document.Database, transaction, line.Handle.ToString())", Detach);
        Assert.Contains("RoofIndependentOrdinaryEntityRole.Annotation", Detach);
        Assert.Contains("lineTimber?.ElementId != elementIdBefore", Detach);
        Assert.Contains("!group.GetAllEntityIds().Intersect(ownedIds.Concat(annotations)).Any()", Detach);
        Assert.Contains("ROOF_INDEPENDENT_DETACH_MOVE", Detach);
        Assert.DoesNotContain("RoofAttachedManualTimberStore.Write", Detach);
    }

    [Fact]
    public void DetachedPhysical_IsExcludedFromRoofMoveAndStretchRecovery()
    {
        Assert.Contains("IsIndependentOrdinaryPhysical(document.Database, id)", Resize);
        Assert.Contains("RoofIndependentOrdinaryEntityRole.PhysicalSolid", Resize);
    }

    private static string Read(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName, "src", "AcKrovy.AutoCAD" }
            .Concat(path).ToArray()));
    }
}
