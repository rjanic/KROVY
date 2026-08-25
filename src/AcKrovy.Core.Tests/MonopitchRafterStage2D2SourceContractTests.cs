using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class MonopitchRafterStage2D2SourceContractTests
{
    private static readonly string Manual = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditService.cs");
    private static readonly string Replacement = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterSetService.cs");
    private static readonly string Resize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string Live = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "LiveGeometrySynchronizationService.cs");
    private static readonly string Diag = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedMemberManualEditDiag.cs");
    private static readonly string Edit = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofEditCommandWorkflow.cs");

    [Fact]
    public void CapturePath_UsesNeutralRestoredGeometryAndSharedLayoutSolver()
    {
        var accept = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryAcceptUnlockedEdits",
            "private static RoofGeneratedAnchorResolutionContext? CreateSplitAnchorResolutionContext");
        var canonical = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryCanonicalGeometry",
            "private static void ApplyAcceptedLineGeometry");

        Assert.Contains("var roofGeometry = restored.Geometry", accept);
        Assert.DoesNotContain("restored.Geometry is not SimpleGableRoofGeometry", accept);
        Assert.Contains("TryCanonicalGeometry(", accept);
        Assert.Contains("roofGeometry", accept);
        Assert.Contains("IRoofGeometry geometry", canonical);
        Assert.Contains("RoofRafterLayoutSolver.Solve", canonical);
        Assert.Contains("item.LogicalKey == key", canonical);
        Assert.DoesNotContain("SimpleGableRafterLayoutSolver.Solve", canonical);
    }

    [Fact]
    public void MonopitchSplitOperations_ReachSharedStage2D3Promotion()
    {
        var accept = RoofUxSourceContractText.Member(
            Manual,
            "private static bool TryAcceptUnlockedEdits",
            "private static RoofGeneratedAnchorResolutionContext? CreateSplitAnchorResolutionContext");
        var promote = accept.IndexOf("TryPromoteSplitFragments", StringComparison.Ordinal);
        var persist = accept.IndexOf("RoofDefinitionStore.Write", StringComparison.Ordinal);

        Assert.DoesNotContain("attached-manual-not-supported", accept);
        Assert.True(promote >= 0);
        Assert.True(persist >= 0);
        Assert.DoesNotContain("roofGeometry.Kind == RoofKind.Monopitch", accept);
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(
            "BREAK",
            RoofKind.Monopitch));
    }

    [Fact]
    public void ExistingGableSplitPath_UsesSharedNeutralGeometryBoundary()
    {
        Assert.Contains("IRoofGeometry geometry", Manual);
        Assert.Contains("RoofRafterLayoutSolver.Solve", Manual);
        Assert.DoesNotContain("roofGeometry is SimpleGableRoofGeometry", Manual);
        Assert.Contains("CreateSplitAnchorResolutionContext", Manual);
        Assert.Contains("TryPromoteSplitFragments", Manual);
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(
            "BREAK",
            RoofKind.SimpleGable));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(
            "BREAK",
            RoofKind.AsymmetricGable));
    }

    [Fact]
    public void LockedOrUnsupportedPath_RecoversBeforeAnyOverrideCapture()
    {
        var process = RoofUxSourceContractText.Member(
            Manual,
            "private static OwnerEditOutcome ProcessOwner",
            "private static bool TryAcceptLockedRigidTranslation");
        var locked = process.IndexOf("if (!supportedUnlocked)", StringComparison.Ordinal);
        var recovery = process.IndexOf("TryRecoverGeneratedMembersOnly", locked, StringComparison.Ordinal);
        var capture = process.IndexOf("TryAcceptUnlockedEdits", StringComparison.Ordinal);

        Assert.True(locked >= 0 && recovery > locked && capture > recovery);
        Assert.Contains("stored.Data.Kind", process);
        Assert.DoesNotContain("RoofDefinitionStore.Write", process[..capture]);
    }

    [Fact]
    public void SourceRebuild_PreservesOverridesAndReplaysFreshNeutralLayout()
    {
        Assert.Contains("PreserveEditState", Resize);
        Assert.Contains("forceRegenerateOnSourceResize: true", Resize);
        Assert.Contains("RoofRafterLayoutSolver.Solve", Replacement);
        Assert.Contains("RoofGeneratedMemberReplayPlanner.Create", Replacement);
        Assert.Contains("foreach (var replayItem in replayPlan.Items)", Replacement);
        Assert.Contains("storedOverrides", Replacement);
        Assert.DoesNotContain("Nearest", Replacement, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("replayAttachedManualChildren: true", Resize);
    }

    [Fact]
    public void SharedDebugReplaySummary_ReportsExactKeyResolutionWithoutMutation()
    {
        Assert.Contains("ROOF_GENERATED_OVERRIDE_REPLAY", Diag);
        Assert.Contains("resolved=", Diag);
        Assert.Contains("geometryReplayed=", Diag);
        Assert.Contains("suppressed=", Diag);
        Assert.Contains("dormant=", Diag);
        Assert.Contains("dormantMissingKey=", Diag);
        Assert.Contains("dormantInvalidDomain=", Diag);
        Assert.Contains("duplicateKeyCount=", Diag);
        Assert.Contains("transaction=caller-owned-pending", Diag);
        Assert.Contains("RoofGeneratedMemberManualEditDiag.WriteReplay", Replacement);
        Assert.Contains("replayPlan.ResolvedOverrideCount", Replacement);
        Assert.Contains("replayPlan.GeometryReplayCount", Replacement);
        Assert.DoesNotContain("RoofDefinitionStore.Write", Diag);
        Assert.DoesNotContain("StartTransaction", Diag);
    }

    [Fact]
    public void SemanticMirror_RebasesOverridesBeforeRestoreAndGeneratedReplacement()
    {
        var apply = RoofUxSourceContractText.Member(
            Edit,
            "private static RoofGeneratedRafterSetService.ReplacementOutcome? TryApply",
            "private static string GetSoftReplacementMessage");
        var update = apply.IndexOf("RoofDefinitionPersistence.UpdateGeometry", StringComparison.Ordinal);
        var rebase = apply.IndexOf(
            "PreserveGeneratedMemberOverridesAcrossSemanticMirror",
            StringComparison.Ordinal);
        var restore = apply.IndexOf("RoofDefinitionPersistence.Restore", StringComparison.Ordinal);
        var replace = apply.IndexOf("TryReplaceForSupportedResize", StringComparison.Ordinal);

        Assert.True(update >= 0 && rebase > update && restore > rebase && replace > restore);
        Assert.Contains("selectionGeometry", apply);
        Assert.Contains("newGeometry", apply);
    }

    [Fact]
    public void AcceptedEdit_RemainsOneCommandEndedTransactionWithCanonicalPresentation()
    {
        var process = RoofUxSourceContractText.Member(
            Manual,
            "private static OwnerEditOutcome ProcessOwner",
            "private static bool TryAcceptLockedRigidTranslation");
        Assert.Contains("StartTransaction", process);
        Assert.Contains("TryRefreshAcceptedMemberAnnotations", Manual);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", process);
        Assert.Contains("transaction.Commit()", process);
        Assert.DoesNotContain("new Timer", Manual);
        Assert.DoesNotContain("Application.Idle", Manual);
        Assert.DoesNotContain("SendStringToExecute", Manual);
    }

    [Fact]
    public void UndoRedoBoundary_RemainsZeroDatabaseWork()
    {
        Assert.Contains("IsUndoRedoCommand(globalCommandName)", Resize);
        Assert.Contains("IsUndoRedoCommand(e.GlobalCommandName)", Live);
        Assert.Contains("ClearPendingLiveGeometryState", Live);
        Assert.Contains("never", Live, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MonopitchGeneratedEditObserver", Manual + Live);
        Assert.DoesNotContain("MonopitchManualOverrideManager", Manual + Live);
        Assert.Equal(5, RoofDefinitionDataSchema.CurrentVersion);
    }
}
