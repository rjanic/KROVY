using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticPurlinMaterializationRulesTests
{
    [Fact]
    public void CreateTimberData_UsesCanonicalPurlinManufacturingDefaults()
    {
        var data = RoofAutomaticPurlinMaterializationRules.CreateTimberData(
            TimberElementDefaultProfile.CreateDefault(),
            "V7");

        Assert.Equal(TimberElementDataSchema.CurrentVersion, data.SchemaVersion);
        Assert.Equal("V7", data.ElementId);
        Assert.Equal(TimberElementType.Purlin, data.ElementType);
        Assert.Equal(160d, data.WidthMm);
        Assert.Equal(220d, data.HeightMm);
        Assert.Equal(0d, data.SlopeDegrees);
        Assert.Equal("Smrek C24", data.Material);
        Assert.Equal(200d, data.CuttingAllowanceMm);
        Assert.Equal(TimberAnnotationMode.NoAnnotations, data.AnnotationMode);
        Assert.Equal(LengthCalculationMode.PlanLength, data.LengthCalculationMode);
        Assert.Null(data.ManualLengthMm);
        Assert.Equal(100d, TimberElementDefaultProfile.CreateDefault().GetCuttingLengthRoundingStepMm());
    }

    [Fact]
    public void Reconcile_SecondIdenticalRun_ReusesEveryMember()
    {
        var desired = new[] { Item(RidgeKey(1, 2), 0d), Item(IntermediateKey("row-1"), 800d) };
        var existing = new[]
        {
            Existing("10", desired[0].GeneratedKey, "V1"),
            Existing("11", desired[1].GeneratedKey, "V2"),
        };

        var result = RoofAutomaticPurlinMaterializationRules.Reconcile(desired, existing);

        Assert.True(result.IsValid);
        Assert.Empty(result.Plan!.Create);
        Assert.Empty(result.Plan.Stale);
        Assert.Equal(2, result.Plan.Reuse.Count);
        Assert.Equal(new[] { "V1", "V2" }, result.Plan.Reuse.Select(item => item.Existing.ElementId));
    }

    [Fact]
    public void Reconcile_ElevationEdit_ReusesStableKeyAndElementId()
    {
        var key = IntermediateKey("row-1");
        var desired = new[] { Item(key, 1200d) };
        var existing = new[] { Existing("A2", key, "V9") };

        var result = RoofAutomaticPurlinMaterializationRules.Reconcile(desired, existing);

        var reuse = Assert.Single(result.Plan!.Reuse);
        Assert.Equal("A2", reuse.Existing.EntityToken);
        Assert.Equal("V9", reuse.Existing.ElementId);
        Assert.Equal(1200d, reuse.Desired.Segment3D.Start.Z);
    }

    [Fact]
    public void Reconcile_RemovesStaleAndCreatesMissing()
    {
        var desired = new[] { Item(RidgeKey(1, 2), 0d) };
        var existing = new[] { Existing("20", IntermediateKey("old-row"), "V3") };

        var result = RoofAutomaticPurlinMaterializationRules.Reconcile(desired, existing);

        Assert.Single(result.Plan!.Create);
        Assert.Single(result.Plan.Stale);
        Assert.Empty(result.Plan.Reuse);
    }

    [Fact]
    public void Reconcile_RidgeToggleOff_RemovesOnlyRidgeMember()
    {
        var intermediateA = Item(IntermediateKey("row-1", 1), 800d);
        var intermediateB = Item(IntermediateKey("row-1", 2), 800d);
        var ridge = RidgeKey(1, 2);
        var result = RoofAutomaticPurlinMaterializationRules.Reconcile(
            new[] { intermediateA, intermediateB },
            new[]
            {
                Existing("20", ridge, "V1"),
                Existing("21", intermediateA.GeneratedKey, "V2"),
                Existing("22", intermediateB.GeneratedKey, "V3"),
            });

        Assert.True(result.IsValid);
        Assert.Empty(result.Plan!.Create);
        Assert.Equal(2, result.Plan.Reuse.Count);
        Assert.Equal(ridge, Assert.Single(result.Plan.Stale).GeneratedKey);
    }

    [Fact]
    public void Reconcile_DisabledIntermediateRow_RemovesOnlyThatRowsChildren()
    {
        var retained = Item(IntermediateKey("row-a"), 800d);
        var disabled = IntermediateKey("row-b");
        var result = RoofAutomaticPurlinMaterializationRules.Reconcile(
            new[] { retained },
            new[]
            {
                Existing("30", retained.GeneratedKey, "V4"),
                Existing("31", disabled, "V5"),
            });

        Assert.True(result.IsValid);
        Assert.Empty(result.Plan!.Create);
        Assert.Equal("V4", Assert.Single(result.Plan.Reuse).Existing.ElementId);
        Assert.Equal(disabled, Assert.Single(result.Plan.Stale).GeneratedKey);
    }

    [Fact]
    public void Reconcile_AllOffExistingConfiguration_RemovesEveryAutomaticMember()
    {
        var existing = new[]
        {
            Existing("40", RidgeKey(1, 2), "V6"),
            Existing("41", IntermediateKey("row-a"), "V7"),
        };

        var result = RoofAutomaticPurlinMaterializationRules.Reconcile(
            Array.Empty<RoofAutomaticPurlinPlanItem>(),
            existing);

        Assert.True(result.IsValid);
        Assert.Empty(result.Plan!.Create);
        Assert.Empty(result.Plan.Reuse);
        Assert.Equal(existing, result.Plan.Stale);
    }

    [Fact]
    public void Reconcile_DuplicateExistingKey_FailsClosedWithoutPlan()
    {
        var key = RidgeKey(1, 2);
        var result = RoofAutomaticPurlinMaterializationRules.Reconcile(
            new[] { Item(key, 0d) },
            new[] { Existing("30", key, "V1"), Existing("31", key, "V2") });

        Assert.False(result.IsValid);
        Assert.Equal(RoofAutomaticPurlinReconciliationError.DuplicateExistingKey, result.Error);
        Assert.Equal(key, result.DuplicateGeneratedKey);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void Reconcile_MalformedExistingMetadata_FailsClosedWithoutPlan()
    {
        var result = RoofAutomaticPurlinMaterializationRules.Reconcile(
            new[] { Item(RidgeKey(1, 2), 0d) },
            new[] { Existing("40", null, "V1") });

        Assert.False(result.IsValid);
        Assert.Equal(RoofAutomaticPurlinReconciliationError.InvalidExistingMember, result.Error);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void IsValidDesiredItem_RejectsSlopedOrNonPurlinSegments()
    {
        var key = RidgeKey(1, 2);
        Assert.False(RoofAutomaticPurlinMaterializationRules.IsValidDesiredItem(
            new RoofAutomaticPurlinPlanItem(
                key,
                TimberElementType.Purlin,
                new RoofSegment3D(new RoofPoint3D(0, 0, 1), new RoofPoint3D(100, 0, 2)))));
        Assert.False(RoofAutomaticPurlinMaterializationRules.IsValidDesiredItem(
            new RoofAutomaticPurlinPlanItem(
                key,
                TimberElementType.Rafter,
                new RoofSegment3D(new RoofPoint3D(0, 0, 1), new RoofPoint3D(100, 0, 1)))));
    }

    private static RoofAutomaticPurlinPlanItem Item(
        RoofAutomaticPurlinGeneratedKey key,
        double elevation) => new(
            key,
            TimberElementType.Purlin,
            new RoofSegment3D(
                new RoofPoint3D(0d, 0d, elevation),
                new RoofPoint3D(1000d, 0d, elevation)));

    private static RoofAutomaticPurlinExistingMember Existing(
        string token,
        RoofAutomaticPurlinGeneratedKey? key,
        string elementId) => new(token, key, elementId);

    private static RoofAutomaticPurlinRidgeKey RidgeKey(int first, int second) => new(
        new RoofStructuralLogicalKey(RoofStructuralRole.Ridge, first, second));

    private static RoofAutomaticPurlinIntermediateKey IntermediateKey(
        string id,
        int sourceFaceBoundaryEdgeId = 1) => new(
        id,
        sourceFaceBoundaryEdgeId,
        new RoofAutomaticPurlinBoundaryKey(RoofTopologyEdgeKind.Hip, 1, 2),
        new RoofAutomaticPurlinBoundaryKey(RoofTopologyEdgeKind.Hip, 1, 3));
}
