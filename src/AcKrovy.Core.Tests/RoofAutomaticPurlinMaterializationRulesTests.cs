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
    public void WallPlatePlanItem_MapsToExactTimberTypeDimensionsAndGeneratedFamily()
    {
        var item = new RoofAutomaticPurlinPlanItem(
            new RoofAutomaticPurlinWallPlateKey(17),
            TimberElementType.WallPlate,
            new RoofSegment3D(
                new RoofPoint3D(0d, 0d, 0d),
                new RoofPoint3D(5000d, 0d, 0d)),
            160d,
            180d);

        var timber = RoofAutomaticPurlinMaterializationRules.CreateTimberData(
            TimberElementDefaultProfile.CreateDefault(),
            item,
            "P7");
        var generated = RoofAutomaticPurlinGeneratedDataRules.Create(
            "AF",
            item.GeneratedKey);

        Assert.True(generated.IsValid, generated.Error.ToString());
        Assert.Equal(item.ElementType, timber.ElementType);
        Assert.Equal(item.WidthMm, timber.WidthMm);
        Assert.Equal(item.HeightMm, timber.HeightMm);
        Assert.Equal("P7", timber.ElementId);
        Assert.Equal(item.GeneratedKey, generated.Data!.GeneratedKey);
        Assert.Equal("AF", generated.Data.RoofOwnerReference);
        Assert.Equal(RoofAutomaticPurlinGeneratorRole.WallPlate, generated.Data.GeneratorRole);
        Assert.Equal(TimberAnnotationMode.NoAnnotations, timber.AnnotationMode);
    }

    [Fact]
    public void Reconcile_SecondIdenticalWallPlateRun_ReusesStableKeyAndElementId()
    {
        var desired = new[]
        {
            new RoofAutomaticPurlinPlanItem(
                new RoofAutomaticPurlinWallPlateKey(4),
                TimberElementType.WallPlate,
                new RoofSegment3D(
                    new RoofPoint3D(0d, 0d, 0d),
                    new RoofPoint3D(4000d, 0d, 0d)),
                140d,
                140d),
        };
        var existing = new[]
        {
            Existing("A1", desired[0].GeneratedKey, "P12"),
        };

        var result = RoofAutomaticPurlinMaterializationRules.Reconcile(desired, existing);

        Assert.True(result.IsValid);
        Assert.Empty(result.Plan!.Create);
        Assert.Empty(result.Plan.Stale);
        var reused = Assert.Single(result.Plan.Reuse);
        Assert.Equal("P12", reused.Existing.ElementId);
        Assert.Equal(desired[0].GeneratedKey, reused.Desired.GeneratedKey);
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
    public void Reconcile_WallPlateLowerEdgeChange_ReusesStableKeysAndElementIds()
    {
        var atZero = new[]
        {
            WallPlateItem(1, centerZMm: 70d),
            WallPlateItem(2, centerZMm: 70d),
            WallPlateItem(3, centerZMm: 70d),
            WallPlateItem(4, centerZMm: 70d),
        };
        var atThousand = new[]
        {
            WallPlateItem(1, centerZMm: 1070d),
            WallPlateItem(2, centerZMm: 1070d),
            WallPlateItem(3, centerZMm: 1070d),
            WallPlateItem(4, centerZMm: 1070d),
        };
        var existing = atZero
            .Select((item, index) => Existing(
                (10 + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.GeneratedKey,
                "P" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();

        var result = RoofAutomaticPurlinMaterializationRules.Reconcile(atThousand, existing);

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.Empty(result.Plan!.Create);
        Assert.Empty(result.Plan.Stale);
        Assert.Equal(4, result.Plan.Reuse.Count);
        Assert.Equal(
            existing.Select(member => member.ElementId),
            result.Plan.Reuse.Select(reuse => reuse.Existing.ElementId));
        Assert.Equal(
            atThousand.Select(item => item.GeneratedKey),
            result.Plan.Reuse.Select(reuse => reuse.Desired.GeneratedKey));
        Assert.All(result.Plan.Reuse, reuse =>
            Assert.Equal(1070d, reuse.Desired.Segment3D.Start.Z, 9));
    }

    [Fact]
    public void Reconcile_WallPlateOffToOn_CreatesOnlyMissingWallPlates()
    {
        var ridge = Item(RidgeKey(1, 2), 1000d);
        var wallPlateA = WallPlateItem(1);
        var wallPlateB = WallPlateItem(2);
        var result = RoofAutomaticPurlinMaterializationRules.Reconcile(
            new[] { wallPlateA, wallPlateB, ridge },
            new[] { Existing("10", ridge.GeneratedKey, "V1") });

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Plan!.Create.Count);
        Assert.All(result.Plan.Create, item =>
            Assert.IsType<RoofAutomaticPurlinWallPlateKey>(item.GeneratedKey));
        Assert.Equal("V1", Assert.Single(result.Plan.Reuse).Existing.ElementId);
        Assert.Empty(result.Plan.Stale);
    }

    [Fact]
    public void Reconcile_WallPlateOnToOff_RemovesOnlyStaleWallPlates()
    {
        var ridge = Item(RidgeKey(1, 2), 1000d);
        var wallPlate = WallPlateItem(1);
        var result = RoofAutomaticPurlinMaterializationRules.Reconcile(
            new[] { ridge },
            new[]
            {
                Existing("10", ridge.GeneratedKey, "V1"),
                Existing("11", wallPlate.GeneratedKey, "P1"),
            });

        Assert.True(result.IsValid);
        Assert.Empty(result.Plan!.Create);
        Assert.Equal("V1", Assert.Single(result.Plan.Reuse).Existing.ElementId);
        Assert.Equal("P1", Assert.Single(result.Plan.Stale).ElementId);
    }

    [Fact]
    public void Reconcile_RidgeOnToOff_PreservesWallPlateIdentityAndThenIsIdempotent()
    {
        var wallPlate = WallPlateItem(1);
        var ridge = Item(RidgeKey(1, 2), 1000d);
        var existing = new[]
        {
            Existing("10", wallPlate.GeneratedKey, "P7"),
            Existing("11", ridge.GeneratedKey, "V3"),
        };

        var toggle = RoofAutomaticPurlinMaterializationRules.Reconcile(
            new[] { wallPlate },
            existing);
        Assert.True(toggle.IsValid);
        Assert.Empty(toggle.Plan!.Create);
        Assert.Equal("P7", Assert.Single(toggle.Plan.Reuse).Existing.ElementId);
        Assert.Equal("V3", Assert.Single(toggle.Plan.Stale).ElementId);

        var identical = RoofAutomaticPurlinMaterializationRules.Reconcile(
            new[] { wallPlate },
            new[] { Existing("10", wallPlate.GeneratedKey, "P7") });
        Assert.True(identical.IsValid);
        Assert.Empty(identical.Plan!.Create);
        Assert.Single(identical.Plan.Reuse);
        Assert.Empty(identical.Plan.Stale);
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

    private static RoofAutomaticPurlinPlanItem WallPlateItem(
        int boundaryEdgeId,
        double centerZMm = 0d) => new(
        new RoofAutomaticPurlinWallPlateKey(boundaryEdgeId),
        TimberElementType.WallPlate,
        new RoofSegment3D(
            new RoofPoint3D(0d, boundaryEdgeId * 100d, centerZMm),
            new RoofPoint3D(1000d, boundaryEdgeId * 100d, centerZMm)),
        140d,
        140d);

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
