using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOrdinaryMoveCancelRollbackSourceContractTests
{
    [Fact]
    public void ExplicitNoUsesSnapshotRollbackAndNeverGenericRecoveryOrRebuild()
    {
        var manual = Read("RoofGeneratedMemberManualEditService.cs");
        var start = manual.IndexOf("if (!ConfirmIndependentOrdinaryDetach())", StringComparison.Ordinal);
        var end = manual.IndexOf("var liveDefinition =", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var cancel = manual[start..end];
        Assert.Contains("TryRestoreCancelledOrdinaryMove", cancel);
        Assert.DoesNotContain("TryRecoverGeneratedMembersOnly", cancel);
        Assert.DoesNotContain("TryReconcileSemanticMembersInTransaction", cancel);

        var recovery = Read("RoofUnsupportedStretchRecoveryService.cs");
        var rollbackStart = recovery.IndexOf("TryRestoreCancelledOrdinaryMove", StringComparison.Ordinal);
        var rollbackEnd = recovery.IndexOf("private static bool PhysicalInventoryMatches", rollbackStart,
            StringComparison.Ordinal);
        Assert.True(rollbackStart >= 0 && rollbackEnd > rollbackStart);
        var rollback = recovery[rollbackStart..rollbackEnd];
        Assert.Contains("TryRestoreTimberLines", rollback);
        Assert.Contains("TryRestoreAnnotations", rollback);
        Assert.Contains("PhysicalInventoryMatches", rollback);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", rollback);
        Assert.Contains("MemberKind: RoofGeneratedTimberKind.Rafter", rollback);
        Assert.Contains("classification=StructuralGenerated", rollback);
        Assert.Contains("skip-ordinary-identity-restore", rollback);
        Assert.Contains("affectedOrdinaryLineId", rollback);
        Assert.Contains("ROOF_ORDINARY_ROLLBACK_VERIFY", rollback);
        Assert.Contains("Physical3D geometry/placement", rollback);
        Assert.Contains("Physical3D snapshot", rollback);
        Assert.DoesNotContain("TryVerifyStretchPhysicalState", rollback);
        Assert.Contains("TryVerifyCanonicalGroupState", rollback);
        Assert.Contains("generated Ordinary inventory", Read("RoofLiveResizeService.cs"));
        Assert.Contains("GROUP exact membership", Read("RoofLiveResizeService.cs"));
        Assert.DoesNotContain("RoofDisplayService.Rebuild", rollback);
        Assert.DoesNotContain("TryEraseUnsnapshotGeneratedDuplicates", rollback);
        Assert.DoesNotContain("TryEraseUnsnapshotStructuralDuplicates", rollback);

        Assert.Contains("RoofCommandLifecycleTerminalState.MarkHandled", cancel);
        Assert.Contains("terminalHandled=true", cancel);
        Assert.Contains("OrdinaryMoveRollbackFailureException", manual);
        Assert.Contains("RetryCancelledOrdinaryRollback", manual);
    }

    [Fact]
    public void MultiMemberPlan2DMoveUsesOneDecisionAndDetachesOnlyAffectedOrdinaryLines()
    {
        var manual = Read("RoofGeneratedMemberManualEditService.cs");
        var start = manual.IndexOf("private static bool TryHandleFirstIndependentOrdinaryEdit", StringComparison.Ordinal);
        var end = manual.IndexOf("private static bool ConfirmIndependentOrdinaryDetach", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = manual[start..end];
        Assert.Contains("DistinctBy(line => line.ObjectId)", method);
        Assert.Contains("var affected = new List", method);
        Assert.Contains("var affectedIds = affected.Select", method);
        Assert.Equal(1, Count(method, "ConfirmIndependentOrdinaryDetach()"));
        Assert.Contains("foreach (var member in affected)", method);
        Assert.Contains("TryDetach(document, transaction, owner", method);
        Assert.Contains("ROOF_ORDINARY_MULTI_MOVE", method);
        Assert.Contains("manualOverrideWritten=false", method);
        Assert.DoesNotContain("TryAcceptUnlockedEdits", method[..method.IndexOf("if (!ConfirmIndependentOrdinaryDetach()", StringComparison.Ordinal)]);
    }

    [Fact]
    public void DerivedPhysicalMoveSkipsOwnersAlreadyHandledBySourceLifecycle()
    {
        var liveResize = Read("RoofLiveResizeService.cs");
        var start = liveResize.IndexOf("private static void ApplyDerivedPhysicalMoveTampers", StringComparison.Ordinal);
        var end = liveResize.IndexOf("private static ", start + "private static ".Length,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = liveResize[start..end];
        Assert.Contains("SourceHandledOwnersThisCommand.Contains(pair.Key)", method);
        Assert.Contains("continue;", method);

        var logicalMove = Read("RoofOrdinaryLogicalMoveService.cs");
        Assert.Contains("RoofCommandLifecycleTerminalState.IsHandled(member.OwnerId)", logicalMove);
        var sync = Read("LiveGeometrySynchronizationService.cs");
        Assert.Contains("RoofCommandLifecycleTerminalState.IsHandled(owner.ObjectId)", sync);
    }

    private static string Read(string filename)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            directory = directory.Parent;
        var folder = filename.StartsWith("RoofUnsupported", StringComparison.Ordinal)
            ? "Infrastructure"
            : "Infrastructure";
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "AcKrovy.AutoCAD", folder, filename));
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }
}
