using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedOverrideReplayDomainSourceContractTests
{
    private static readonly string Materialization = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterSetService.cs");
    private static readonly string Diagnostics = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditDiag.cs");
    private static readonly string SourcePolicy = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofSourceResizeChildPolicyService.cs");
    private static readonly string LiveResize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string LiveGeometry = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");

    [Fact]
    public void ActualCadMaterializationConsumesNeutralReplayPlanAndKeepsCanonicalItemForDormantOverride()
    {
        var materialize = RoofUxSourceContractText.Member(
            Materialization,
            "private static MaterializationResult MaterializeCore",
            "private sealed record MaterializationResult");

        Assert.Contains("RoofGeneratedMemberReplayPlanner.Create", materialize);
        Assert.Contains("foreach (var replayItem in replayPlan.Items)", materialize);
        Assert.Contains("replayItem.Geometry is not { } appliedGeometry", materialize);
        Assert.Contains("var rafter = replayItem.Rafter", materialize);
        Assert.Contains("TimberSourceLineCreationService.Create", materialize);
        Assert.Contains("TimberCreatedElementAnnotationService.EnsureForCreatedElements", materialize);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", materialize);
        Assert.DoesNotContain("TryApplyToLayout", materialize);
        Assert.True(
            materialize.IndexOf("RoofGeneratedMemberReplayPlanner.Create", StringComparison.Ordinal) <
            materialize.IndexOf("TimberSourceLineCreationService.Create", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceResizeBuildsCompleteReplayPlanBeforeErasingOldGeneratedSet()
    {
        var plan = Materialization.IndexOf(
            "replayPlan = RoofGeneratedMemberReplayPlanner.Create",
            StringComparison.Ordinal);
        var erase = Materialization.IndexOf(
            "EraseGeneratedSet(database, transaction, owner.ObjectId, existingIds)",
            plan,
            StringComparison.Ordinal);
        var create = Materialization.IndexOf("var materialized = MaterializeCore", erase, StringComparison.Ordinal);

        Assert.True(plan >= 0 && erase > plan && create > erase);
    }

    [Fact]
    public void ReplayDiagnosticDistinguishesMissingKeyFromInvalidDomainAndUsesActualReplayPlanCounts()
    {
        Assert.Contains("dormantMissingKey=", Diagnostics);
        Assert.Contains("dormantInvalidDomain=", Diagnostics);
        Assert.Contains("replayPlan.GeometryReplayCount", Materialization);
        Assert.Contains("replayPlan.DormantMissingKeyCount", Materialization);
        Assert.Contains("replayPlan.DormantInvalidDomainCount", Materialization);
    }

    [Fact]
    public void SourceResizeCounterReportsActualGeometryReplaysRatherThanStoredOverrideCount()
    {
        Assert.Contains("generatedReplayPlan?.GeometryReplayCount ?? 0", LiveResize);
        Assert.Contains("generatedOverridesGeometryReplayed", SourcePolicy);
        Assert.DoesNotContain("CountPersistedOverrides", SourcePolicy);
        Assert.Contains("generatedOverridesReplayed=", SourcePolicy);
    }

    [Fact]
    public void UndoRedoStillClearsPendingStateBeforeAnyDatabaseReplayWork()
    {
        var ended = RoofUxSourceContractText.Member(
            LiveGeometry,
            "private void CommandEnded",
            "private void CommandCancelled");

        Assert.Contains("IsUndoRedoCommand(e.GlobalCommandName)", ended);
        Assert.Contains("ClearPendingLiveGeometryState", ended);
        Assert.Contains("RoofUnsupportedStretchRecoverySnapshotService.Clear", ended);
        Assert.DoesNotContain("RoofGeneratedMemberReplayPlanner", ended);
    }
}
