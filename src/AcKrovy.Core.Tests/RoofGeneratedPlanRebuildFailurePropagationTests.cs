using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedPlanRebuildFailurePropagationTests
{
    private static readonly string GeneratedSet = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterSetService.cs");
    private static readonly string Resize = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofLiveResizeService.cs");
    private static readonly string OrdinaryPhysical = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofOrdinaryRafterSolidMaterializationService.cs");
    private static readonly string Recovery = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofUnsupportedStretchRecoveryService.cs");

    [Fact]
    public void A_ExceptionAfterPostAtomic_SurfacesSubstageAndExceptionInHardFailureContract()
    {
        Assert.Contains("LastFailureDetail", GeneratedSet);
        Assert.Contains("ROOF_GENERATED_PLAN_REBUILD_FAILURE", GeneratedSet);
        Assert.Contains("InjectPostCreateOrdinaryPhysicalFailureOnce", GeneratedSet);
        Assert.Contains("RoofGeneratedPlanRebuildFailureRules.FromException", GeneratedSet);
        Assert.Contains(
            "RoofGeneratedPlanRebuildFailureRules.SubstageOrdinaryPhysicalReconcile",
            GeneratedSet);
        Assert.Contains("substage: detail?.Substage", Resize);
        Assert.Contains("RoofGeneratedRafterSetService.LastFailureDetail", Resize);

        var detail = RoofGeneratedPlanRebuildFailureRules.FromException(
            "2912",
            new InvalidOperationException(
                "Ordinary physical rafter model is inconsistent: StructuralCut:OrdinaryCutNotOnStructuralSide:Rafter:Face0:2."),
            RoofGeneratedPlanRebuildFailureRules.SubstageOrdinaryPhysicalReconcile);
        Assert.Equal("2912", detail.OwnerHandle);
        Assert.Equal(RoofGeneratedPlanRebuildFailureRules.SubstageOrdinaryPhysicalReconcile, detail.Substage);
        Assert.Contains("StructuralCut", detail.Result, StringComparison.Ordinal);
        Assert.Equal(nameof(InvalidOperationException), detail.ExceptionType);
        Assert.Contains("substage=ordinary-physical-reconcile", detail.ToMarkerLine(), StringComparison.Ordinal);
        Assert.StartsWith("InvalidOperationException:", detail.ToHardFailureExceptionToken(), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSolid_FallsBackToClippedBody_AndIncludesMemberInFailure()
    {
        Assert.Contains("CreateExtrudedSolidFromPrism", OrdinaryPhysical);
        Assert.Contains("member.SolidVertices", OrdinaryPhysical);
        Assert.Contains("CreateSolid failed member=", OrdinaryPhysical);
    }

    [Fact]
    public void B_ElementIdSeriesMismatch_IsRecoverableWhenHandleMatches()
    {
        Assert.True(RoofUnsupportedStretchRecoveryRules.IsRecoverableGeneratedElementIdSeriesMismatch(
            "29F7", "29F7", "NK5", "NK4", isLineEntity: true, hasTimberMetadata: true));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsRecoverableGeneratedElementIdSeriesMismatch(
            "29F7", "29F8", "NK5", "NK4", isLineEntity: true, hasTimberMetadata: true));
        Assert.False(RoofUnsupportedStretchRecoveryRules.IsRecoverableGeneratedElementIdSeriesMismatch(
            "29F7", "29F7", "NK5", "NK5", isLineEntity: true, hasTimberMetadata: true));
        Assert.Contains("IsRecoverableGeneratedElementIdSeriesMismatch", Recovery);
        Assert.Contains("generated-timber-elementid-mismatch-recoverable", Recovery);
        Assert.Contains("ElementId = timber.ElementId", Recovery);
    }

    [Fact]
    public void C_D_E_F_RecoveryEqualityAndPostFailureLivenessRemainRequired()
    {
        Assert.True(RoofSupportedResizeFailureRecoveryRules.IsRecoveryVerdictOk(
            dbRollback: true,
            runtimeReset: true,
            pendingResize: false,
            suppressionDepth: 0,
            hasActiveOwner: false,
            groupCanonical: true,
            sourceGeometryRestored: true,
            roofDefinitionRestored: true,
            physicalInventoryRestored: true,
            annotationsRestored: true,
            nextCommandReady: true));

        var session = new RoofSupportedResizeFailureRecoveryRules.Session(
            new RoofSupportedResizeFailureRecoveryRules.AggregateSnapshot(
                "source-v1", "plan-v1", "ordinary-phys-v1", "structural-phys-v1",
                "overrides-v1", "annotations-v1", GroupCanonical: true));
        session.InjectedFailureAtStructuralPhysical =
            RoofSupportedResizeFailureRecoveryRules.OrdinaryCutNotOnStructuralSideFailure;
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.SourceDetected,
            session.PreCommand with
            {
                SourceGeometryToken = "source-stretched",
                GroupCanonical = false,
                RigidFootprintEdge12Mm = 7138.038790322185d,
            }));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.OrdinaryRebuilt));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.OverrideReplayed));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.GroupTouched));
        Assert.False(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.StructuralPhysical));
        Assert.True(session.TryRecoverFromHardFailure());
        Assert.True(session.CompareToPreCommand().IsExactRestore);
        Assert.True(session.Runtime.NextCommandReady);
        Assert.True(session.TryExecuteAfterRecovery(RoofSupportedResizeFailureRecoveryRules.LifecycleOperation.OrdinaryMove));
        Assert.True(session.TryExecuteAfterRecovery(RoofSupportedResizeFailureRecoveryRules.LifecycleOperation.OrdinaryGripStretch));
        Assert.True(session.TryExecuteAfterRecovery(RoofSupportedResizeFailureRecoveryRules.LifecycleOperation.SourceGripStretch));

        // F: success -> forced failure -> success -> forced failure -> success
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.SourceDetected,
            session.PreCommand));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.OrdinaryRebuilt));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.OverrideReplayed));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.GroupTouched));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.StructuralPhysical));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.Commit));
        session.InjectedFailureAtStructuralPhysical =
            RoofSupportedResizeFailureRecoveryRules.OrdinaryCutNotOnStructuralSideFailure;
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.SourceDetected,
            session.PreCommand with { SourceGeometryToken = "source-stretched-2" }));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.OrdinaryRebuilt));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.OverrideReplayed));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.GroupTouched));
        Assert.False(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.StructuralPhysical));
        Assert.True(session.TryRecoverFromHardFailure());
        Assert.True(session.TryExecuteAfterRecovery(RoofSupportedResizeFailureRecoveryRules.LifecycleOperation.SourceGripStretch));
    }
}
