using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedOverrideReplayDomainTests
{
    private const double RafterWidthMm = 125d;
    private const double MaximumSpacingMm = 500d;
    private static readonly RoofPoint3D ZUp = RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal;

    [Fact]
    public void ExactKeyAndValidLateralOverride_ReplaysGeometry()
    {
        var layout = Layout(Monopitch(5625d));
        var target = Find(layout, 1);
        var stored = Override(target.LogicalKey, lateralMm: 500d);

        var plan = Plan(layout, [stored]);

        Assert.True(plan.IsValid);
        Assert.Equal(1, plan.StoredOverrideCount);
        Assert.Equal(1, plan.ResolvedOverrideCount);
        Assert.Equal(1, plan.GeometryReplayCount);
        Assert.Equal(0, plan.DormantCount);
        Assert.Equal(RoofGeneratedMemberReplayDisposition.GeometryReplayed, Find(plan, target.LogicalKey).Disposition);
    }

    [Fact]
    public void ExactKeyAndOutsideLateralOverride_StaysStoredDormantAndMaterializesCanonicalMember()
    {
        var layout = Layout(Monopitch(2625.1d));
        var target = Find(layout, 1);
        var stored = Override(target.LogicalKey, lateralMm: 500d, reservedElementId: "K2");

        var plan = Plan(layout, [stored]);
        var item = Find(plan, target.LogicalKey);

        Assert.True(plan.IsValid);
        Assert.Equal(1, plan.StoredOverrideCount);
        Assert.Equal(1, plan.ResolvedOverrideCount);
        Assert.Equal(0, plan.GeometryReplayCount);
        Assert.Equal(1, plan.DormantInvalidDomainCount);
        Assert.Equal(0, plan.DormantMissingKeyCount);
        Assert.Equal(layout.Rafters.Count, plan.MaterializedCount);
        Assert.Equal(RoofGeneratedMemberReplayDisposition.DormantInvalidDomain, item.Disposition);
        Assert.Equal(stored, item.Override);
        Assert.Equal(Canonical(target), item.Geometry);
        Assert.All(plan.Items.Where(candidate => candidate.Geometry is not null), candidate =>
            Assert.True(RoofGeneratedMemberDomainRules.OverlapsBoundedPlane(
                layout,
                candidate.Rafter,
                candidate.Geometry!.Value)));
    }

    [Fact]
    public void HostNegativeFiveHundredOverride_ValidThenDomainDormantWithExactKeyStillPresent()
    {
        var expanded = Layout(Monopitch(5625d, mirrored: true));
        var shrunk = Layout(Monopitch(2625.1d, mirrored: true));
        var stored = Override(Find(expanded, 1).LogicalKey, lateralMm: -500d);

        var active = Plan(expanded, [stored]);
        var dormant = Plan(shrunk, [stored]);

        Assert.Equal(1, active.GeometryReplayCount);
        Assert.Equal(1, dormant.ResolvedOverrideCount);
        Assert.Equal(0, dormant.GeometryReplayCount);
        Assert.Equal(1, dormant.DormantInvalidDomainCount);
        Assert.Equal(Canonical(Find(shrunk, 1)), Find(dormant, stored.Key).Geometry);
        Assert.Equal(-500d, Assert.Single(
            dormant.Items,
            item => item.Override is not null).Override!.LateralMm);
    }

    [Fact]
    public void DomainDormantOverride_ReactivatesOnSameExactKeyWithoutNumericChange()
    {
        var expanded = Layout(Monopitch(5625d));
        var shrunk = Layout(Monopitch(2625.1d));
        var stored = Override(Find(expanded, 1).LogicalKey, lateralMm: 500d);

        var dormant = Plan(shrunk, [stored]);
        var reactivated = Plan(expanded, [stored]);

        Assert.Equal(1, dormant.DormantInvalidDomainCount);
        Assert.Equal(1, reactivated.GeometryReplayCount);
        Assert.Equal(stored, Find(reactivated, stored.Key).Override);
        Assert.NotEqual(Canonical(Find(expanded, 1)), Find(reactivated, stored.Key).Geometry);
    }

    [Fact]
    public void RepeatedDormantReactivationCycles_DoNotAccumulateOverrideOrGeometry()
    {
        var expanded = Layout(Monopitch(5625d));
        var shrunk = Layout(Monopitch(2625.1d));
        var stored = Override(Find(expanded, 1).LogicalKey, lateralMm: 500d);

        var firstActive = Find(Plan(expanded, [stored]), stored.Key).Geometry;
        var firstDormant = Plan(shrunk, [stored]);
        var secondActive = Find(Plan(expanded, [stored]), stored.Key).Geometry;
        var secondDormant = Plan(shrunk, [stored]);
        var thirdActive = Find(Plan(expanded, [stored]), stored.Key).Geometry;

        Assert.Equal(1, firstDormant.DormantInvalidDomainCount);
        Assert.Equal(1, secondDormant.DormantInvalidDomainCount);
        Assert.Equal(firstActive, secondActive);
        Assert.Equal(secondActive, thirdActive);
        Assert.Equal(500d, stored.LateralMm);
    }

    [Fact]
    public void MissingExactKey_RemainsSeparateDormantCauseAndCanonicalSetStaysComplete()
    {
        var expanded = Layout(Monopitch(5625d));
        var shrunk = Layout(Monopitch(2625.1d));
        var stored = Override(Find(expanded, 10).LogicalKey, lateralMm: 100d);

        var plan = Plan(shrunk, [stored]);

        Assert.Equal(0, plan.ResolvedOverrideCount);
        Assert.Equal(1, plan.DormantMissingKeyCount);
        Assert.Equal(0, plan.DormantInvalidDomainCount);
        Assert.Equal(shrunk.Rafters.Count, plan.MaterializedCount);
        Assert.All(plan.Items, item => Assert.Equal(RoofGeneratedMemberReplayDisposition.Canonical, item.Disposition));
    }

    [Fact]
    public void NewStationNeverInheritsAnotherKeysDormantOverride()
    {
        var shrunk = Layout(Monopitch(2625.1d));
        var expanded = Layout(Monopitch(5625d));
        var stored = Override(Find(shrunk, 1).LogicalKey, lateralMm: 500d);

        var plan = Plan(expanded, [stored]);
        var newStation = expanded.Rafters.OrderBy(item => item.StationIndex).Last();

        Assert.NotEqual(stored.Key, newStation.LogicalKey);
        Assert.Equal(RoofGeneratedMemberReplayDisposition.Canonical, Find(plan, newStation.LogicalKey).Disposition);
        Assert.Null(Find(plan, newStation.LogicalKey).Override);
    }

    [Theory]
    [InlineData(1000d, 0d)]
    [InlineData(0d, 1500d)]
    public void EndpointBeyondEave_RemainsActiveWhenSegmentOverlapsBoundedPlane(
        double startOffsetMm,
        double endOffsetMm)
    {
        var layout = Layout(Monopitch(5625d));
        var target = Find(layout, 4);
        var stored = Override(
            target.LogicalKey,
            startOffsetMm: startOffsetMm,
            endOffsetMm: endOffsetMm);

        var plan = Plan(layout, [stored]);

        Assert.Equal(1, plan.GeometryReplayCount);
        Assert.Equal(0, plan.DormantInvalidDomainCount);
    }

    [Fact]
    public void CombinedHostLateralAndEndpointOverride_RemainsActive()
    {
        var layout = Layout(Monopitch(10000d));
        var target = Find(layout, 3);
        var stored = Override(
            target.LogicalKey,
            lateralMm: 1216.877d,
            startOffsetMm: -1000d);

        var plan = Plan(layout, [stored]);

        Assert.Equal(1, plan.GeometryReplayCount);
        Assert.Equal(RoofGeneratedMemberReplayDisposition.GeometryReplayed, Find(plan, stored.Key).Disposition);
    }

    [Fact]
    public void RotationUsesSameSharedOverlapPolicy()
    {
        var layout = Layout(Monopitch(5625d));
        var validTarget = Find(layout, 5);
        var invalidTarget = Find(layout, 1);
        var valid = Override(validTarget.LogicalKey, rotationRadians: 20d * Math.PI / 180d);
        var invalid = Override(
            invalidTarget.LogicalKey,
            lateralMm: 10000d,
            rotationRadians: 20d * Math.PI / 180d);

        var plan = Plan(layout, [valid, invalid]);

        Assert.Equal(RoofGeneratedMemberReplayDisposition.GeometryReplayed, Find(plan, valid.Key).Disposition);
        Assert.Equal(RoofGeneratedMemberReplayDisposition.DormantInvalidDomain, Find(plan, invalid.Key).Disposition);
        Assert.Equal(1, plan.GeometryReplayCount);
        Assert.Equal(1, plan.DormantInvalidDomainCount);
    }

    [Fact]
    public void SemanticMirrorAndMirrorTwice_PreserveDomainDecisionAndPhysicalGeometry()
    {
        var originalGeometry = Monopitch(5625d);
        var mirroredGeometry = Monopitch(5625d, mirrored: true);
        var original = Layout(originalGeometry);
        var mirrored = Layout(mirroredGeometry);
        var target = Find(original, 4);
        var stored = Override(target.LogicalKey, lateralMm: 500d, startOffsetMm: -1000d);
        var onceData = MonopitchRoofDefinitionRules
            .PreserveGeneratedMemberOverridesAcrossSemanticMirror(
                DefinitionData([stored]),
                originalGeometry,
                mirroredGeometry);
        var mirroredStored = Assert.Single(onceData.Overrides);
        var twiceData = MonopitchRoofDefinitionRules
            .PreserveGeneratedMemberOverridesAcrossSemanticMirror(
                onceData,
                mirroredGeometry,
                originalGeometry);

        var before = Find(Plan(original, [stored]), stored.Key);
        var after = Find(Plan(mirrored, [mirroredStored]), stored.Key);
        var twice = Find(Plan(original, twiceData.Overrides), stored.Key);

        Assert.Equal(RoofGeneratedMemberReplayDisposition.GeometryReplayed, before.Disposition);
        Assert.Equal(before.Disposition, after.Disposition);
        AssertUndirectedXyEqual(before.Geometry!.Value, after.Geometry!.Value);
        Assert.Equal(before.Geometry, twice.Geometry);
        Assert.Equal(stored, Assert.Single(twiceData.Overrides));
        Assert.Equal(stored, twice.Override);
    }

    [Fact]
    public void RotatedThirtyDegrees_ValidDormantReactivatedUsesLocalDomainCoordinates()
    {
        var expanded = Layout(Monopitch(5625d, rotationDegrees: 30d));
        var shrunk = Layout(Monopitch(2625.1d, rotationDegrees: 30d));
        var stored = Override(Find(expanded, 1).LogicalKey, lateralMm: 500d);

        var active = Plan(expanded, [stored]);
        var dormant = Plan(shrunk, [stored]);
        var reactivated = Plan(expanded, [stored]);

        Assert.Equal(1, active.GeometryReplayCount);
        Assert.Equal(1, dormant.DormantInvalidDomainCount);
        Assert.Equal(1, reactivated.GeometryReplayCount);
        Assert.Equal(Find(active, stored.Key).Geometry, Find(reactivated, stored.Key).Geometry);
    }

    [Fact]
    public void TwoIndependentOverrides_OneCanDormantWithoutCrossKeyMigration()
    {
        var layout = Layout(Monopitch(2625.1d));
        var invalid = Override(Find(layout, 1).LogicalKey, lateralMm: 500d, reservedElementId: "K1");
        var valid = Override(Find(layout, 4).LogicalKey, lateralMm: -100d, reservedElementId: "K4");

        var plan = Plan(layout, [invalid, valid]);

        Assert.Equal(2, plan.StoredOverrideCount);
        Assert.Equal(2, plan.ResolvedOverrideCount);
        Assert.Equal(1, plan.GeometryReplayCount);
        Assert.Equal(1, plan.DormantInvalidDomainCount);
        Assert.Equal(invalid, Find(plan, invalid.Key).Override);
        Assert.Equal(valid, Find(plan, valid.Key).Override);
        Assert.Equal(layout.Rafters.Count, plan.MaterializedCount);
    }

    [Theory]
    [InlineData(RoofKind.SimpleGable)]
    [InlineData(RoofKind.AsymmetricGable)]
    public void GableKinds_UseTheSameExactKeyDomainPolicy(RoofKind kind)
    {
        var expanded = Layout(Gable(kind, 5625d));
        var shrunk = Layout(Gable(kind, 2625.1d));
        var key = expanded.Rafters.Single(item =>
            item.Face == RafterRoofFace.Face0 && item.StationIndex == 1).LogicalKey;
        var stored = Override(key, lateralMm: 500d);

        var active = Plan(expanded, [stored]);
        var dormant = Plan(shrunk, [stored]);

        Assert.Equal(1, active.GeometryReplayCount);
        Assert.Equal(1, dormant.ResolvedOverrideCount);
        Assert.Equal(1, dormant.DormantInvalidDomainCount);
        Assert.Equal(shrunk.Rafters.Count, dormant.MaterializedCount);
    }

    [Fact]
    public void InvalidOverrideMath_FailsTheWholeReplayPlanBeforePartialMaterialization()
    {
        var layout = Layout(Monopitch(5625d));
        var invalid = Override(Find(layout, 1).LogicalKey, lateralMm: double.NaN);

        var plan = Plan(layout, [invalid]);

        Assert.False(plan.IsValid);
        Assert.Empty(plan.Items);
        Assert.Equal("override-apply-failed", plan.FailureReason);
    }

    private static RoofGeneratedMemberReplayPlan Plan(
        RoofRafterLayout layout,
        IReadOnlyList<RoofGeneratedMemberOverride> overrides) =>
        RoofGeneratedMemberReplayPlanner.Create(layout, 0d, ZUp, overrides);

    private static RoofGeneratedMemberReplayItem Find(
        RoofGeneratedMemberReplayPlan plan,
        RoofGeneratedMemberKey key) =>
        plan.Items.Single(item => item.Rafter.LogicalKey == key);

    private static RoofRafterGeometry Find(RoofRafterLayout layout, int stationIndex) =>
        layout.Rafters.Single(item =>
            item.Face == RafterRoofFace.Face0 && item.StationIndex == stationIndex);

    private static RoofGeneratedMemberGeometry Canonical(RoofRafterGeometry rafter) =>
        RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0d);

    private static RoofGeneratedMemberOverride Override(
        RoofGeneratedMemberKey key,
        double alongMm = 0d,
        double lateralMm = 0d,
        double rotationRadians = 0d,
        double startOffsetMm = 0d,
        double endOffsetMm = 0d,
        string? reservedElementId = null) =>
        new(
            key,
            false,
            alongMm,
            lateralMm,
            rotationRadians,
            startOffsetMm,
            endOffsetMm,
            reservedElementId);

    private static RoofRafterLayout Layout(IRoofGeometry geometry)
    {
        var result = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(MaximumSpacingMm, RafterWidthMm));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Layout!;
    }

    private static MonopitchRoofGeometry Monopitch(
        double stationSpanMm,
        double runMm = 6000d,
        double rotationDegrees = 0d,
        bool mirrored = false)
    {
        var definition = MonopitchDefinition(stationSpanMm, runMm, rotationDegrees);
        if (mirrored)
        {
            definition = MonopitchRoofDefinitionRules.Mirror(definition);
        }
        var result = RoofGeometrySolver.Solve(definition);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<MonopitchRoofGeometry>(result.Geometry);
    }

    private static RoofDefinition MonopitchDefinition(
        double stationSpanMm,
        double runMm,
        double rotationDegrees)
    {
        var radians = rotationDegrees * Math.PI / 180d;
        var station = Direction(Math.Cos(radians), Math.Sin(radians));
        var run = Direction(-Math.Sin(radians), Math.Cos(radians));
        RoofPoint2D Point(double u, double v) => new(
            350d + u * station.X + v * run.X,
            -725d + u * station.Y + v * run.Y);
        return new RoofDefinition(
            Validate([
                Point(0d, 0d),
                Point(stationSpanMm, 0d),
                Point(stationSpanMm, runMm),
                Point(0d, runMm),
            ]),
            new RoofParameters(30d, SlopeDirection: run),
            RoofKind.Monopitch);
    }

    private static SimpleGableRoofGeometry Gable(RoofKind kind, double stationSpanMm)
    {
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            Validate([
                new(0d, 0d),
                new(stationSpanMm, 0d),
                new(stationSpanMm, 6000d),
                new(0d, 6000d),
            ]),
            kind == RoofKind.AsymmetricGable
                ? new RoofParameters(20d, Direction(1d, 0d), Face1SlopeDegrees: 35d)
                : new RoofParameters(30d, Direction(1d, 0d)),
            kind));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<SimpleGableRoofGeometry>(result.Geometry);
    }

    private static RoofFootprint Validate(IReadOnlyList<RoofPoint2D> points)
    {
        var result = RoofFootprintValidator.Validate(new RoofFootprintInput(points, true));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Footprint!;
    }

    private static RoofDefinitionData DefinitionData(
        IReadOnlyList<RoofGeneratedMemberOverride> overrides) =>
        new(
            RoofDefinitionDataSchema.CurrentVersion,
            RoofKind.Monopitch,
            30d,
            RidgeEdgeFamily: RoofRidgeEdgeFamily.SourceEdge01,
            RigidFootprint: new RoofRigidFootprintDescriptor(
                4,
                RoofPolygonOrientation.CounterClockwise,
                5625d,
                6000d),
            EditState: RoofEditState.Unlocked,
            ManualOverrides: overrides,
            Face1SlopeDegrees: 30d,
            EaveHeightDifferenceMm: 3464.1016151377544d);

    private static RoofDirection2D Direction(double x, double y)
    {
        Assert.True(RoofDirection2D.TryCreate(x, y, out var direction));
        return direction;
    }

    private static void AssertUndirectedXyEqual(
        RoofGeneratedMemberGeometry expected,
        RoofGeneratedMemberGeometry actual)
    {
        var direct = Distance(expected.Start, actual.Start) <= 0.01d &&
                     Distance(expected.End, actual.End) <= 0.01d;
        var reversed = Distance(expected.Start, actual.End) <= 0.01d &&
                       Distance(expected.End, actual.Start) <= 0.01d;
        Assert.True(direct || reversed);
    }

    private static double Distance(RoofPoint3D first, RoofPoint3D second) =>
        Math.Sqrt(
            Math.Pow(first.X - second.X, 2d) +
            Math.Pow(first.Y - second.Y, 2d));
}
