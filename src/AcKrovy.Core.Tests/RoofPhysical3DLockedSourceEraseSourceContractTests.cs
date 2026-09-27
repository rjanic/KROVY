using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Integration guards, not proof of AutoCAD native event timing.</summary>
public sealed class RoofPhysical3DLockedSourceEraseSourceContractTests
{
    private static string Read(string name) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", name + ".cs");

    [Fact]
    public void PostRepairCleanup_CannotBlindlyEraseHistoricalOwnerHandles()
    {
        var live = Read("LiveGeometrySynchronizationService");
        Assert.Contains("RoofPhysical3DLifecycleService.CleanupStillErasedSourceInTransaction", live);
        Assert.DoesNotContain("RoofPhysical3DMaterializationService.EraseOwned", live);
        var lifecycle = Read("RoofPhysical3DLifecycleService");
        var cleanup = lifecycle[..lifecycle.IndexOf("public static bool EnsureRestoredOwnerInTransaction", StringComparison.Ordinal)];
        Assert.Contains("TryGetObjectAllowErased<Polyline>", cleanup);
        Assert.Contains("ShouldEraseForSourceState(resolved, isRoof, owner?.IsErased == true)", cleanup);
        Assert.Contains("if (erase) RoofPhysical3DMaterializationService.EraseOwned", cleanup);
        Assert.DoesNotContain("OpenMode.ForWrite", cleanup);
    }

    [Fact]
    public void LockedRepair_SourceDisplayPhysicalGroup_ShareOneCommit()
    {
        var source = Read("RoofLiveResizeService");
        var repair = source[source.IndexOf("private static bool ApplySourceEraseTampers", StringComparison.Ordinal)..
            source.IndexOf("private static bool TryUnEraseLockedSource", StringComparison.Ordinal)];
        var restore = repair.IndexOf("TryUnEraseLockedSource(", StringComparison.Ordinal);
        var display = repair.IndexOf("TryApplyDisplayTamper(", StringComparison.Ordinal);
        var physical = repair.IndexOf("EnsureRestoredOwnerInTransaction", StringComparison.Ordinal);
        var group = repair.IndexOf("RoofAssemblyGroupSyncService.TrySyncForOwner", StringComparison.Ordinal);
        var commit = repair.IndexOf("transaction.Commit();", StringComparison.Ordinal);
        Assert.True(restore >= 0 && display > restore && physical > display && group > physical && commit > group);
        Assert.Contains("return false;", repair[physical..commit]);
        Assert.Contains("TryValidateRestoredSourceGroup", repair);
        Assert.DoesNotContain("AppendEntity", repair);
    }

    [Fact]
    public void ExistingSet_IsPreservedOrRepairedUsingSharedMaterializer()
    {
        var source = Read("RoofPhysical3DLifecycleService");
        Assert.Contains("RoofPhysical3DSetRules.IsComplete(build.Model, children)", source);
        Assert.Contains("locked-source-physical-ensure-preserved", source);
        Assert.Contains("ApplyOwnedVisibility(database, transaction", source);
        Assert.Contains("ReadPhysicalChildren(database, transaction, owner.Handle.ToString())", source);
        Assert.Contains("ReconcileOwnerInTransaction(database, transaction, ownerId, owner", source);
        Assert.DoesNotContain("new Face(", source);
        Assert.DoesNotContain("new Line(", source);
        Assert.DoesNotContain("GeometricExtents", source);
    }

    [Fact]
    public void FocusedCounts_ExposeZeroSetsNativeRepairCleanupAndDistinctGroups()
    {
        var diagnostics = Read("RoofPhysical3DHostDiagnostics");
        Assert.Contains("OWNER_COUNTS phase=", diagnostics);
        Assert.Contains("OWNER_GROUP phase=", diagnostics);
        Assert.Contains("firstListing={firstListing}", diagnostics);
        Assert.Contains("sourceSlots=", diagnostics);
        var post = diagnostics[diagnostics.IndexOf("public static void MaintenanceComplete", StringComparison.Ordinal)..
            diagnostics.IndexOf("public static void OwnerCounts", StringComparison.Ordinal)];
        Assert.Contains("is not (\"COPY\" or \"MIRROR\" or \"ERASE\")", post);
        var live = Read("LiveGeometrySynchronizationService");
        Assert.Contains("if (!isUndoRedo && !shouldIgnore)", live);
        Assert.Contains("RoofPhysical3DHostDiagnostics.MaintenanceComplete", live);
    }

    [Fact]
    public void EmptyRafterInvariant_DoesNotWeakenFailedRebindOrMissingMemberChecks()
    {
        var source = Read("RoofGeneratedRafterCopyOwnershipRehydrationService");
        Assert.Contains("unique || (expectedKeys.Count == 0 && actualKeys.Count == 0)", source);
        Assert.Contains("wholeCopyRebindSucceeded &&", source);
        Assert.Contains("expectedKeys.Count == actualKeys.Count", source);
        Assert.Contains("missing.Length == 0", source);
        Assert.Contains("duplicate.Length == 0", source);
    }
}
