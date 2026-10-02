using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofStructuralLockedLifecycleTests
{
    [Theory]
    [InlineData("MOVE", RoofStructuralNativeAction.RestorePlan)]
    [InlineData("ERASE", RoofStructuralNativeAction.RestorePlan)]
    [InlineData("COPY", RoofStructuralNativeAction.RejectClone)]
    [InlineData("MIRROR", RoofStructuralNativeAction.RejectClone)]
    [InlineData("STRETCH", RoofStructuralNativeAction.Unclaimed)]
    [InlineData("GRIP_STRETCH", RoofStructuralNativeAction.Unclaimed)]
    [InlineData("TRIM", RoofStructuralNativeAction.Unclaimed)]
    [InlineData("EXTEND", RoofStructuralNativeAction.Unclaimed)]
    public void LockedPlan_KeepsOneRestoreOwner(string command, RoofStructuralNativeAction expected)
    {
        Assert.Equal(expected, RoofStructuralEditRules.Classify(command, false, RoofEditState.Locked));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand(command));
        Assert.Equal(command is "COPY" or "MIRROR" ? RoofStructuralNativeAction.RejectClone : RoofStructuralNativeAction.RebuildPhysical,
            RoofStructuralEditRules.Classify(command, true, RoofEditState.Locked));
    }

    [Theory]
    [InlineData("MOVE", RoofStructuralNativeAction.AcceptPlan)]
    [InlineData("ERASE", RoofStructuralNativeAction.AcceptPlan)]
    [InlineData("COPY", RoofStructuralNativeAction.AcceptManualClone)]
    [InlineData("MIRROR", RoofStructuralNativeAction.AcceptManualClone)]
    [InlineData("STRETCH", RoofStructuralNativeAction.AcceptPlan)]
    [InlineData("GRIP_STRETCH", RoofStructuralNativeAction.AcceptPlan)]
    [InlineData("TRIM", RoofStructuralNativeAction.AcceptPlan)]
    [InlineData("EXTEND", RoofStructuralNativeAction.AcceptPlan)]
    public void UnlockedPlan_RetainsAcceptedMatrix(string command, RoofStructuralNativeAction expected) =>
        Assert.Equal(expected, RoofStructuralEditRules.Classify(command, false, RoofEditState.Unlocked));

    [Fact]
    public void LockedRecovery_WiresStructuralCompletionAndManualOwnership()
    {
        // Adapter wiring only; service behavior is tested in WPF without live CAD objects.
        var recovery = Read("RoofUnsupportedStretchRecoveryService");
        var generatedOnly = RoofUxSourceContractText.Member(recovery,
            "public static RoofUnsupportedStretchRecoveryOutcome TryRecoverGeneratedMembersOnly(",
            "public static bool TryNormalizeRigidTranslation(");
        Assert.Contains("TryCompleteStructuralRecovery", generatedOnly);
        Assert.Contains("TryReconcileInTransaction", generatedOnly);
        Assert.Contains("HasUnclaimedStructuralPlanChanges", generatedOnly);
        Assert.Contains("var structuralRecovery = sourceUnchanged", generatedOnly);
        Assert.Contains("includeManual: true", generatedOnly);
        Assert.Contains("allowErased: structuralRecovery", generatedOnly);
        var live = Read("RoofLiveResizeService");
        var resolve = RoofUxSourceContractText.Member(live,
            "private static bool TryResolveGeneratedAssemblyOwner(", "private static bool TryResolveHandleToOwnerPolyline(");
        Assert.Contains("TryResolveLockedStructuralManualOwner", resolve);
        var classify = RoofUxSourceContractText.Member(live,
            "private static void ClassifyModifiedGeneratedChildren(", "private static bool TryResolveGeneratedAssemblyOwner(");
        Assert.Contains("RoofStructuralAttachedManualStore.FindByOwner", classify);
    }

    private static string Read(string name) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", name + ".cs");
}
