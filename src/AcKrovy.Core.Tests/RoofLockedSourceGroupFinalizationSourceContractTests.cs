using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofLockedSourceGroupFinalizationSourceContractTests
{
    private static string Read(string name) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", name + ".cs");

    [Fact]
    public void FinalizationRunsAfterSourceTransactionDisposalInsideExistingLock()
    {
        var repair = RoofUxSourceContractText.Member(Read("RoofLiveResizeService"),
            "private static bool ApplySourceEraseTampers", "private static bool TryUnEraseLockedSource");
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", repair);
        Assert.Contains("restoredOwners.Add(ownerId)", repair);
        var finalize = repair.IndexOf("return wrote && RoofAssemblyGroupSyncService.TryFinalizeRestoredSources", StringComparison.Ordinal);
        var committed = repair.IndexOf("transaction.Commit();", StringComparison.Ordinal);
        Assert.True(committed >= 0 && finalize > committed);
        Assert.Contains("\n            }\n", repair[committed..finalize]);
        Assert.DoesNotContain("Idle", Read("RoofAssemblyGroupSyncService"));
    }

    [Fact]
    public void FinalizerChangesOnlyMembershipAndValidatesClosedCommittedState()
    {
        var finalizer = RoofUxSourceContractText.Member(Read("RoofAssemblyGroupSyncService"),
            "public static bool TryFinalizeRestoredSources", "public static bool TrySyncForOwner(");
        var guard = finalizer.IndexOf("IsUndoRedoCommand(globalCommandName)", StringComparison.Ordinal);
        var transaction = finalizer.IndexOf("StartTransaction()", StringComparison.Ordinal);
        Assert.True(guard >= 0 && guard < transaction);
        Assert.Contains("IsEraseCommand(globalCommandName)", finalizer);
        Assert.Contains("ownerIds.Distinct()", finalizer);
        Assert.Contains("if (!IsCurrent", finalizer);
        Assert.Contains("TryRemoveSurplusSourceSlots(document.Database, transaction, ownerId, group)", finalizer);
        Assert.Contains("group.RemoveAt(index)", finalizer);
        Assert.Contains("!uniqueBefore.SetEquals(after)", finalizer);
        Assert.Contains("!uniqueBefore.SetEquals(collected.MemberIds)", finalizer);
        Assert.DoesNotContain("group.Append", finalizer);
        Assert.Contains("using var verify", finalizer);
        Assert.Contains("IsCurrent(document.Database, verify, ownerId)", finalizer);
        Assert.Contains("group.ObjectId != groupIds[ownerId]", finalizer);
        Assert.Contains("if (changed) transaction.Commit();", finalizer);
        Assert.True(finalizer.IndexOf("result: \"Recovered|ok\"", StringComparison.Ordinal) >
                    finalizer.IndexOf("using var verify", StringComparison.Ordinal));
        Assert.DoesNotContain("RoofPhysical3DMaterializationService", finalizer);
        Assert.DoesNotContain("Erase(false)", finalizer);
        Assert.DoesNotContain("AppendEntity", finalizer);
    }

    [Fact]
    public void SourceSlotCorrection_LeavesExistingGeneralGroupSyncIntact()
    {
        var group = Read("RoofDisplayGroupService");
        Assert.DoesNotContain("plan = RoofAssemblyGroupMembershipRules.PlanCanonicalization(group.GetAllEntityIds(), expected);", group);
        Assert.Contains("group.Append(addId)", group);
        Assert.Contains("group-sync-before-append", group);
        Assert.Contains("RoofPhysical3DHostDiagnostics.GroupMutation", group);
    }

    [Fact]
    public void DiagnosticsIdentifyNativeDatabaseAndCapturePostCommitMembership()
    {
        var diagnostics = Read("RoofPhysical3DHostDiagnostics");
        Assert.Contains("item.Document.Database.UnmanagedObject == database.UnmanagedObject", diagnostics);
        Assert.DoesNotContain("ReferenceEquals(item.Document.Database, database)", diagnostics);
        Assert.Contains("GROUP_MUTATION owner=", diagnostics);
        Assert.Contains("GROUP_MEMBERS phase=", diagnostics);
        Assert.Contains("members.Distinct().Select", diagnostics);
        Assert.Contains("if (!canonicalName && !members.Any", diagnostics);
        var sync = Read("RoofAssemblyGroupSyncService");
        Assert.Contains("locked-source-group-finalize-before", sync);
        Assert.Contains("locked-source-group-finalize-after:provisional", sync);
        Assert.Contains("locked-source-group-finalize-committed", sync);
    }
}
