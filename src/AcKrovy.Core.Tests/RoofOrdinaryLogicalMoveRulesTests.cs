using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOrdinaryLogicalMoveRulesTests
{
    private static readonly RoofPoint3D Start = new(10, 20, 0);
    private static readonly RoofPoint3D End = new(10, 120, 0);
    private static readonly RoofPoint3D Center = new(10, 70, 450);

    [Fact]
    public void PlanInitiatedMove_TranslatesSolidOnce()
    {
        Assert.True(Plan(new(100, -50, 0), new(0, 0, 0), out var result));
        Assert.Equal(RoofOrdinaryMoveInitiator.Plan2D, result!.Initiator);
        Assert.Equal(new RoofPoint3D(0, 0, 0), result.LineTranslation);
        Assert.Equal(new RoofPoint3D(100, -50, 0), result.SolidTranslation);
    }

    [Fact]
    public void DirectSolidDiagonalMove_RestoresSolidWithoutChangingPlan()
    {
        Assert.True(Plan(new(0, 0, 0), new(12000, -9000, 350), out var result));
        Assert.Equal(RoofOrdinaryMoveInitiator.Physical3D, result!.Initiator);
        Assert.Equal(new RoofPoint3D(0, 0, 0), result.LineTranslation);
        Assert.Equal(new RoofPoint3D(-12000, 9000, -350), result.SolidTranslation);
        Assert.Equal(new RoofPoint3D(0, 0, 0), result.AnnotationTranslation);
        Assert.Equal(350, result.NativeDelta.Z);
    }

    [Fact]
    public void SolidVerticalMove_KeepsPlanZAndXy()
    {
        Assert.True(Plan(new(0, 0, 0), new(0, 0, 750), out var result));
        Assert.Equal(new RoofPoint3D(0, 0, -750), result!.SolidTranslation);
        Assert.Equal(new RoofPoint3D(0, 0, 0), result.LineTranslation);
        Assert.Equal(new RoofPoint3D(0, 0, 0), result.AnnotationTranslation);
        Assert.Equal(750, result.NativeDelta.Z);
    }

    [Fact]
    public void BothNativeRepresentationsMoved_DoesNotTranslateEitherAgain()
    {
        Assert.True(Plan(new(50, 25, 0), new(50, 25, 0), out var result));
        Assert.Equal(new RoofPoint3D(0, 0, 0), result!.LineTranslation);
        Assert.Equal(new RoofPoint3D(0, 0, 0), result.SolidTranslation);
    }

    [Fact]
    public void BothNativeRepresentationsMovedWithPhysicalZ_PlanWinsAndPhysicalZIsRemoved()
    {
        Assert.True(Plan(new(50, 25, 0), new(50, 25, 700), out var result));
        Assert.Equal(RoofOrdinaryMoveInitiator.Plan2D, result!.Initiator);
        Assert.Equal(new RoofPoint3D(50, 25, 0), result.NativeDelta);
        Assert.Equal(new RoofPoint3D(0, 0, 0), result.LineTranslation);
        Assert.Equal(new RoofPoint3D(0, 0, -700), result.SolidTranslation);
    }

    [Fact]
    public void VeryLargeSolidMove_IsRejectedWithoutProjectingOntoPlan()
    {
        Assert.True(Plan(new(0, 0, 0), new(900000, -700000, 95000), out var result));
        Assert.Equal(new RoofPoint3D(0, 0, 0), result!.LineTranslation);
        Assert.Equal(new RoofPoint3D(-900000, 700000, -95000), result.SolidTranslation);
    }

    [Fact]
    public void ConflictingNativeDisplacements_AreReconciledFromPlan()
    {
        Assert.True(Plan(new(50, 25, 0), new(60, 25, 0), out var result));
        Assert.Equal(RoofOrdinaryMoveInitiator.Plan2D, result!.Initiator);
        Assert.Equal(new RoofPoint3D(-10, 0, 0), result.SolidTranslation);
    }

    [Fact]
    public void MixedSelection_SolidZCannotOverrideChangedPlan()
    {
        Assert.True(Plan(new(50, 25, 0), new(50, 25, 400), out var result));
        Assert.Equal(RoofOrdinaryMoveInitiator.Plan2D, result!.Initiator);
        Assert.Equal(new RoofPoint3D(0, 0, -400), result.SolidTranslation);
    }

    [Fact]
    public void PlanMoveWithNativeZ_KeepsPlanAtZeroAndMovesPhysicalOnlyInXy()
    {
        Assert.True(Plan(new(50, 25, 300), new(0, 0, 0), out var result));
        Assert.Equal(RoofOrdinaryMoveInitiator.Plan2D, result!.Initiator);
        Assert.Equal(new RoofPoint3D(0, 0, -300), result.LineTranslation);
        Assert.Equal(new RoofPoint3D(50, 25, 0), result.SolidTranslation);
    }

    [Fact]
    public void EndpointOnlyEdit_IsNotRigidMove() =>
        Assert.False(RoofOrdinaryLogicalMoveRules.TryPlan(Start, End,
            Start, Add(End, new(1, 0, 0)), Center, Center, out _));

    [Fact]
    public void NoGeometryChange_DoesNotCreateMovePlan() =>
        Assert.False(Plan(new(0, 0, 0), new(0, 0, 0), out _));

    private static bool Plan(RoofPoint3D lineDelta, RoofPoint3D solidDelta,
        out RoofOrdinaryLogicalMovePlan? result) =>
        RoofOrdinaryLogicalMoveRules.TryPlan(Start, End,
            Add(Start, lineDelta), Add(End, lineDelta),
            Center, Add(Center, solidDelta), out result);

    private static RoofPoint3D Add(RoofPoint3D point, RoofPoint3D delta) =>
        new(point.X + delta.X, point.Y + delta.Y, point.Z + delta.Z);
}
