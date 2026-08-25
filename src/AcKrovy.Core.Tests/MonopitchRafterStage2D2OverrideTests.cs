using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class MonopitchRafterStage2D2OverrideTests
{
    private const double SpacingMm = 900d;
    private const double WidthMm = 80d;
    private static readonly RoofPoint3D ZUp = RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal;

    [Theory]
    [InlineData("MOVE")]
    [InlineData("ROTATE")]
    [InlineData("TRIM")]
    [InlineData("EXTEND")]
    [InlineData("STRETCH")]
    [InlineData("GRIP_STRETCH")]
    [InlineData("ERASE")]
    public void ExistingGeometryOverrideCommands_AreEnabledForUnlockedMonopitch(string command)
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(
            command,
            RoofKind.Monopitch));
    }

    [Fact]
    public void Break_IsEnabledForMonopitchByAttachedManualStage2D3()
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(
            "BREAK",
            RoofKind.SimpleGable));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(
            "BREAK",
            RoofKind.AsymmetricGable));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(
            "BREAK",
            RoofKind.Monopitch));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(
            "SCALE",
            RoofKind.Monopitch));
    }

    [Fact]
    public void MonopitchCanonicalMember_SupportsExistingMoveRotateAndEndpointRepresentations()
    {
        var rafter = Layout(10000d, 6000d, 30d).Rafters[5];
        var canonical = Canonical(rafter);

        var moved = MoveInLocalBasis(canonical, 85d, 165d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, moved, ZUp, rafter.LogicalKey, "K5", out var moveOverride));
        AssertReplay(canonical, moved, moveOverride);

        var rotated = RotateAround(canonical, canonical.Start, 0.12d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, rotated, ZUp, rafter.LogicalKey, "K5", out var rotateOverride));
        AssertReplay(canonical, rotated, rotateOverride);

        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical, ZUp, out var basis));
        var trimmed = new RoofGeneratedMemberGeometry(
            canonical.Start,
            Add(canonical.End, Scale(basis.AxisU, -275d)));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical,
            trimmed,
            ZUp,
            out var trimStart,
            out var trimEnd,
            out _,
            out var trimReason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, trimReason);
        var trimOverride = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, rafter.LogicalKey, "K5", trimStart, trimEnd);
        AssertReplay(canonical, trimmed, trimOverride);

        var extended = new RoofGeneratedMemberGeometry(
            canonical.Start,
            Add(canonical.End, Scale(basis.AxisU, 325d)));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical,
            extended,
            ZUp,
            out var extendStart,
            out var extendEnd,
            out _,
            out var extendReason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, extendReason);
        var extendOverride = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, rafter.LogicalKey, "K5", extendStart, extendEnd);
        AssertReplay(canonical, extended, extendOverride);
    }

    [Fact]
    public void MonopitchCanonicalMember_SupportsExistingRepresentableGripAndClassicStretchShapes()
    {
        var rafter = Layout(10000d, 6000d, 30d).Rafters[5];
        var canonical = Canonical(rafter);
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical, ZUp, out var basis));
        var observed = new RoofGeneratedMemberGeometry(
            canonical.Start,
            Add(
                Add(canonical.End, Scale(basis.AxisU, -220d)),
                Scale(basis.AxisV, 310d)));

        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical,
            observed,
            ZUp,
            rafter.LogicalKey,
            "K5",
            out var overrideData));
        AssertReplay(canonical, observed, overrideData);
    }

    [Fact]
    public void Face0StationOverride_CapturesAndRoundTripsWithoutObjectIdentity()
    {
        var layout = Layout(10000d, 6000d, 30d);
        var rafter = layout.Rafters[5];
        var canonical = Canonical(rafter);
        var observed = MoveInLocalBasis(canonical, alongMm: 125d, lateralMm: -210d);

        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical,
            observed,
            ZUp,
            rafter.LogicalKey,
            "K42",
            out var captured));
        Assert.NotNull(captured);
        Assert.Equal(RafterRoofFace.Face0, captured!.Key.RoofFace);
        Assert.Equal(rafter.StationIndex, captured.Key.StationIndex);
        Assert.Equal(125d, captured.AlongMm, 6);
        Assert.Equal(-210d, captured.LateralMm, 6);
        Assert.Equal("K42", captured.ReservedElementId);

        var data = DefinitionData([captured]);
        var payload = RoofDefinitionDataCodec.Encode(data);
        Assert.True(RoofDefinitionDataCodec.TryDecode(payload, out var decoded, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.None, error);
        Assert.Equal(RoofKind.Monopitch, decoded!.Kind);
        Assert.Equal(RoofEditState.Unlocked, decoded.EditState);
        Assert.Equal(captured, Assert.Single(decoded.Overrides));
        Assert.DoesNotContain("ObjectId", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunResize_ReplaysStoredOverrideOnceAcrossRepeatedCanonicalRebuilds()
    {
        var before = Layout(10000d, 6000d, 30d);
        var rebuiltB = Layout(10000d, 7500d, 30d);
        var rebuiltC = Layout(10000d, 8200d, 30d);
        var key = before.Rafters[5].LogicalKey;
        var captured = CaptureLocalMove(before.Rafters[5], 90d, 175d);
        var overrides = new RoofManualOverrideSet([captured]);

        var appliedB = Apply(Find(rebuiltB, key), overrides);
        var appliedCA = Apply(Find(rebuiltC, key), overrides);
        var appliedCB = Apply(Find(rebuiltC, key), overrides);

        Assert.Equal(7500d, Find(rebuiltB, key).PlanLengthMm, 6);
        Assert.Equal(8200d, Find(rebuiltC, key).PlanLengthMm, 6);
        AssertGeometry(appliedCA, appliedCB);
        AssertLocalDelta(Canonical(Find(rebuiltC, key)), appliedCA, 90d, 175d);
        Assert.NotEqual(0d, appliedB.LengthMm);
    }

    [Fact]
    public void PerpendicularResize_AddsCanonicalStationsWithoutMigratingOverride()
    {
        var before = Layout(10000d, 6000d, 30d);
        var after = Layout(13000d, 6000d, 30d);
        var targetKey = before.Rafters[5].LogicalKey;
        var overrides = new RoofManualOverrideSet([
            CaptureLocalMove(before.Rafters[5], 0d, 240d),
        ]);

        Assert.True(after.StationCount > before.StationCount);
        var appliedTarget = Apply(Find(after, targetKey), overrides);
        AssertLocalDelta(Canonical(Find(after, targetKey)), appliedTarget, 0d, 240d);

        var newRafter = after.Rafters[^1];
        Assert.True(newRafter.StationIndex >= before.StationCount);
        AssertGeometry(Canonical(newRafter), Apply(newRafter, overrides));
        Assert.Equal(after.Rafters.Count, after.Rafters.Select(item => item.LogicalKey).Distinct().Count());
    }

    [Fact]
    public void StationDisappearance_UsesExistingDormantExactKeyPolicyAndReactivatesExactly()
    {
        var wide = Layout(14000d, 6000d, 30d);
        var narrow = Layout(8000d, 6000d, 30d);
        var target = wide.Rafters[^2];
        var overrides = new RoofManualOverrideSet([
            CaptureLocalMove(target, 45d, -160d),
        ]);

        Assert.True(target.StationIndex >= narrow.StationCount);
        Assert.Null(overrides.FindMapped(target.LogicalKey, narrow.StationCount));
        Assert.Equal(target.LogicalKey, Assert.Single(overrides.FindDormant(narrow.StationCount)).Key);
        Assert.All(narrow.Rafters, item => AssertGeometry(Canonical(item), Apply(item, overrides)));

        var returned = Find(wide, target.LogicalKey);
        AssertLocalDelta(Canonical(returned), Apply(returned, overrides), 45d, -160d);
    }

    [Fact]
    public void SlopeEdit_RecomputesCanonicalManufacturingLengthThenReplaysPlanOverride()
    {
        var before = Layout(10000d, 6000d, 30d);
        var after = Layout(10000d, 6000d, 40d);
        var key = before.Rafters[5].LogicalKey;
        var beforeCanonical = Canonical(before.Rafters[5]);
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(beforeCanonical, ZUp, out var basis));
        var shortened = new RoofGeneratedMemberGeometry(
            beforeCanonical.Start,
            Add(beforeCanonical.End, Scale(basis.AxisU, -300d)));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            beforeCanonical,
            shortened,
            ZUp,
            key,
            "K5",
            out var captured));
        var overrides = new RoofManualOverrideSet([captured!]);
        var rebuilt = Find(after, key);
        var applied = Apply(rebuilt, overrides);

        Assert.Equal(40d, rebuilt.SlopeDegrees);
        Assert.Equal(6000d / Math.Cos(40d * Math.PI / 180d), rebuilt.TrueLengthMm, 8);
        Assert.NotEqual(Find(before, key).TrueLengthMm, rebuilt.TrueLengthMm);
        Assert.Equal(5700d, applied.LengthMm, 6);
        Assert.Equal(5700d / Math.Cos(40d * Math.PI / 180d),
            applied.LengthMm / Math.Cos(rebuilt.SlopeDegrees * Math.PI / 180d),
            8);
    }

    [Fact]
    public void SemanticMirrorWithoutOverrides_PreservesEveryPhysicalPlanSegment()
    {
        var definition = Definition(10000d, 6000d, 30d, 0d);
        var original = Layout(definition);
        var mirrored = Layout(MonopitchRoofDefinitionRules.Mirror(definition));

        Assert.Equal(original.StationCount, mirrored.StationCount);
        foreach (var before in original.Rafters)
        {
            var after = Find(mirrored, before.LogicalKey);
            Assert.Equal(before.StationPositionMm, after.StationPositionMm, 9);
            Assert.Equal(before.PlanLengthMm, after.PlanLengthMm, 9);
            Assert.Equal(before.TrueLengthMm, after.TrueLengthMm, 9);
            AssertUndirectedXyGeometry(Canonical(before), Canonical(after));
        }
    }

    [Theory]
    [InlineData(0d, 1216.877d, 0d, 0d, 0d)]
    [InlineData(0d, 0d, 0d, -1000d, 0d)]
    [InlineData(0d, 1216.877d, 0d, -1000d, 0d)]
    [InlineData(175d, -240d, 0.12d, 325d, -180d)]
    public void SemanticMirror_RebasesEveryOverrideComponentToPreservePhysicalPlan(
        double alongMm,
        double lateralMm,
        double rotationRadians,
        double startOffsetMm,
        double endOffsetMm)
    {
        var definition = Definition(10000d, 6000d, 30d, 0d);
        var mirroredDefinition = MonopitchRoofDefinitionRules.Mirror(definition);
        var original = Layout(definition);
        var mirrored = Layout(mirroredDefinition);
        var target = original.Rafters[5];
        var stored = new RoofManualOverrideSet([
            Override(
                target.LogicalKey,
                alongMm,
                lateralMm,
                rotationRadians,
                startOffsetMm,
                endOffsetMm,
                "K5"),
        ]);
        var rebased = RebaseForMirror(definition, mirroredDefinition, stored);

        Assert.Equal(target.LogicalKey, Assert.Single(rebased.Items).Key);
        AssertUndirectedXyGeometry(
            Apply(target, stored),
            Apply(Find(mirrored, target.LogicalKey), rebased));
    }

    [Fact]
    public void SemanticMirror_TwoIndependentHostOverridesStayOnTheirPhysicalStations()
    {
        var definition = Definition(10000d, 6000d, 35d, 0d);
        var mirroredDefinition = MonopitchRoofDefinitionRules.Mirror(definition);
        var original = Layout(definition);
        var mirrored = Layout(mirroredDefinition);
        var first = original.Rafters[4];
        var second = original.Rafters[7];
        var stored = new RoofManualOverrideSet([
            Override(first.LogicalKey, 0d, 1216.877d, 0d, -1000d, 0d, "KA"),
            Override(second.LogicalKey, 0d, 150d, 0d, 0d, 0d, "KB"),
        ]);
        var rebased = RebaseForMirror(definition, mirroredDefinition, stored);

        Assert.Equal(2, rebased.Count);
        Assert.Equal(0d, rebased.Items[0].AlongMm);
        Assert.Equal(-1216.877d, rebased.Items[0].LateralMm, 6);
        Assert.Equal(0d, rebased.Items[0].StartOffsetMm);
        Assert.Equal(-1000d, rebased.Items[0].EndOffsetMm);
        Assert.Equal(-150d, rebased.Items[1].LateralMm, 6);
        AssertUndirectedXyGeometry(
            Apply(first, stored),
            Apply(Find(mirrored, first.LogicalKey), rebased));
        AssertUndirectedXyGeometry(
            Apply(second, stored),
            Apply(Find(mirrored, second.LogicalKey), rebased));
        Assert.Equal(first.LogicalKey, rebased.Items[0].Key);
        Assert.Equal(second.LogicalKey, rebased.Items[1].Key);
    }

    [Fact]
    public void SemanticMirrorTwice_RestoresCanonicalAndStoredOverrideStateWithoutDrift()
    {
        var originalDefinition = Definition(10000d, 6000d, 30d, 0d);
        var mirroredDefinition = MonopitchRoofDefinitionRules.Mirror(originalDefinition);
        var twiceDefinition = MonopitchRoofDefinitionRules.Mirror(mirroredDefinition);
        var original = Layout(originalDefinition);
        var twice = Layout(twiceDefinition);
        var target = original.Rafters[5];
        var stored = new RoofManualOverrideSet([
            Override(target.LogicalKey, 110d, 1216.877d, 0d, -1000d, 225d, "K5"),
        ]);
        var once = RebaseForMirror(originalDefinition, mirroredDefinition, stored);
        var twiceOverrides = RebaseForMirror(mirroredDefinition, twiceDefinition, once);

        Assert.Equal(Layout(originalDefinition).Signature, twice.Signature);
        Assert.Equal(Assert.Single(stored.Items), Assert.Single(twiceOverrides.Items));
        AssertUndirectedXyGeometry(
            Apply(target, stored),
            Apply(Find(twice, target.LogicalKey), twiceOverrides));
        Assert.Equal(twice.Rafters.Count, twice.Rafters.Select(item => item.LogicalKey).Distinct().Count());
    }

    [Fact]
    public void SemanticMirror_RotatedThirtyDegreesPreservesOverriddenPhysicalPlan()
    {
        var definition = Definition(10000d, 6000d, 30d, 30d);
        var mirroredDefinition = MonopitchRoofDefinitionRules.Mirror(definition);
        var original = Layout(definition);
        var mirrored = Layout(mirroredDefinition);
        var target = original.Rafters[5];
        var stored = new RoofManualOverrideSet([
            Override(target.LogicalKey, 140d, 1216.877d, 0d, -1000d, 0d, "K5"),
        ]);
        var rebased = RebaseForMirror(definition, mirroredDefinition, stored);

        AssertUndirectedXyGeometry(
            Apply(target, stored),
            Apply(Find(mirrored, target.LogicalKey), rebased));
    }

    [Fact]
    public void SemanticMirrorOverrideRebase_DoesNotAlterGableData()
    {
        var monopitch = SolveGeometry(Definition(10000d, 6000d, 30d, 0d));
        var footprint = ValidateFootprint(RotatedRectangle(10000d, 6000d, 0d));
        Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var ridgeDirection));
        var gableResult = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(30d, ridgeDirection),
            RoofKind.SimpleGable));
        Assert.True(gableResult.IsValid);
        var data = DefinitionData([
            Override(
                new RoofGeneratedMemberKey(
                    RoofGeneratedTimberKind.Rafter,
                    RafterRoofFace.Face0,
                    2),
                0d,
                150d,
                0d,
                0d,
                0d,
                "K2"),
        ]);

        var unchanged = MonopitchRoofDefinitionRules
            .PreserveGeneratedMemberOverridesAcrossSemanticMirror(
                data,
                gableResult.Geometry!,
                monopitch);

        Assert.Same(data, unchanged);
    }

    [Fact]
    public void RotatedThirtyDegrees_ReplaysInCanonicalLocalBasis()
    {
        var before = Layout(Definition(10000d, 6000d, 30d, 30d));
        var after = Layout(Definition(10000d, 7800d, 30d, 30d));
        var key = before.Rafters[5].LogicalKey;
        var overrides = new RoofManualOverrideSet([
            CaptureLocalMove(Find(before, key), 140d, 225d),
        ]);
        var rebuiltCanonical = Canonical(Find(after, key));
        var rebuiltApplied = Apply(Find(after, key), overrides);

        AssertLocalDelta(rebuiltCanonical, rebuiltApplied, 140d, 225d);
        Assert.NotEqual(rebuiltCanonical.Start.X, rebuiltApplied.Start.X);
        Assert.NotEqual(rebuiltCanonical.Start.Y, rebuiltApplied.Start.Y);
    }

    [Fact]
    public void TwoOverridesRemainIndependentAndNeighborsStayCanonical()
    {
        var before = Layout(10000d, 6000d, 30d);
        var after = Layout(10000d, 7200d, 30d);
        var first = before.Rafters[4];
        var second = before.Rafters[7];
        var overrides = new RoofManualOverrideSet([
            CaptureLocalMove(first, 60d, 180d),
            CaptureLocalMove(second, -95d, -260d),
        ]);

        Assert.Equal(2, overrides.Count);
        AssertLocalDelta(Canonical(Find(after, first.LogicalKey)), Apply(Find(after, first.LogicalKey), overrides), 60d, 180d);
        AssertLocalDelta(Canonical(Find(after, second.LogicalKey)), Apply(Find(after, second.LogicalKey), overrides), -95d, -260d);
        AssertGeometry(Canonical(after.Rafters[3]), Apply(after.Rafters[3], overrides));
        AssertGeometry(Canonical(after.Rafters[5]), Apply(after.Rafters[5], overrides));
        AssertGeometry(Canonical(after.Rafters[6]), Apply(after.Rafters[6], overrides));
        AssertGeometry(Canonical(after.Rafters[8]), Apply(after.Rafters[8], overrides));
    }

    [Fact]
    public void SuppressedMonopitchStation_RemainsExactKeyAndDoesNotProduceGeometry()
    {
        var layout = Layout(10000d, 6000d, 30d);
        var rafter = layout.Rafters[5];
        var overrides = new RoofManualOverrideSet([
            RoofGeneratedMemberOverride.Suppress(rafter.LogicalKey, "K99"),
        ]);

        Assert.True(RoofGeneratedMemberOverrideRules.TryApplyToLayout(
            rafter,
            0d,
            ZUp,
            overrides,
            out var geometry,
            out var suppressed));
        Assert.True(suppressed);
        Assert.Null(geometry);
        Assert.Equal("K99", Assert.Single(overrides.Items).ReservedElementId);
    }

    private static RoofGeneratedMemberOverride CaptureLocalMove(
        RoofRafterGeometry rafter,
        double alongMm,
        double lateralMm)
    {
        var canonical = Canonical(rafter);
        var observed = MoveInLocalBasis(canonical, alongMm, lateralMm);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical,
            observed,
            ZUp,
            rafter.LogicalKey,
            $"K{rafter.StationIndex}",
            out var captured));
        return captured!;
    }

    private static RoofGeneratedMemberOverride Override(
        RoofGeneratedMemberKey key,
        double alongMm,
        double lateralMm,
        double rotationRadians,
        double startOffsetMm,
        double endOffsetMm,
        string reservedElementId) =>
        new(
            key,
            false,
            alongMm,
            lateralMm,
            rotationRadians,
            startOffsetMm,
            endOffsetMm,
            reservedElementId);

    private static RoofManualOverrideSet RebaseForMirror(
        RoofDefinition beforeDefinition,
        RoofDefinition afterDefinition,
        RoofManualOverrideSet stored)
    {
        var updated = DefinitionData(stored.Items);
        var rebased = MonopitchRoofDefinitionRules
            .PreserveGeneratedMemberOverridesAcrossSemanticMirror(
                updated,
                SolveGeometry(beforeDefinition),
                SolveGeometry(afterDefinition));
        var payload = RoofDefinitionDataCodec.Encode(rebased);
        Assert.True(RoofDefinitionDataCodec.TryDecode(
            payload,
            out var roundTripped,
            out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.None, error);
        return new RoofManualOverrideSet(roundTripped!.Overrides);
    }

    private static RoofGeneratedMemberGeometry Apply(
        RoofRafterGeometry rafter,
        RoofManualOverrideSet overrides)
    {
        Assert.True(RoofGeneratedMemberOverrideRules.TryApplyToLayout(
            rafter,
            0d,
            ZUp,
            overrides,
            out var geometry,
            out var suppressed));
        Assert.False(suppressed);
        return geometry!.Value;
    }

    private static RoofGeneratedMemberGeometry Canonical(RoofRafterGeometry rafter) =>
        RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0d);

    private static RoofGeneratedMemberGeometry MoveInLocalBasis(
        RoofGeneratedMemberGeometry canonical,
        double alongMm,
        double lateralMm)
    {
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical, ZUp, out var basis));
        var delta = Add(Scale(basis.AxisU, alongMm), Scale(basis.AxisV, lateralMm));
        return new RoofGeneratedMemberGeometry(
            Add(canonical.Start, delta),
            Add(canonical.End, delta));
    }

    private static void AssertLocalDelta(
        RoofGeneratedMemberGeometry canonical,
        RoofGeneratedMemberGeometry applied,
        double alongMm,
        double lateralMm)
    {
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical, ZUp, out var basis));
        var delta = Subtract(applied.Start, canonical.Start);
        Assert.Equal(alongMm, Dot(delta, basis.AxisU), 6);
        Assert.Equal(lateralMm, Dot(delta, basis.AxisV), 6);
    }

    private static void AssertGeometry(
        RoofGeneratedMemberGeometry expected,
        RoofGeneratedMemberGeometry actual) =>
        Assert.True(
            RoofGeneratedMemberOverrideMath.GeometryEquals(expected, actual),
            $"Expected {expected}; actual {actual}");

    private static void AssertUndirectedXyGeometry(
        RoofGeneratedMemberGeometry expected,
        RoofGeneratedMemberGeometry actual)
    {
        var direct = XyDistance(expected.Start, actual.Start) <=
                         RoofGeneratedMemberOverrideMath.LengthToleranceMm &&
                     XyDistance(expected.End, actual.End) <=
                         RoofGeneratedMemberOverrideMath.LengthToleranceMm;
        var reversed = XyDistance(expected.Start, actual.End) <=
                           RoofGeneratedMemberOverrideMath.LengthToleranceMm &&
                       XyDistance(expected.End, actual.Start) <=
                           RoofGeneratedMemberOverrideMath.LengthToleranceMm;
        Assert.True(direct || reversed, $"Expected physical XY {expected}; actual {actual}");
    }

    private static double XyDistance(RoofPoint3D left, RoofPoint3D right) =>
        Math.Sqrt(Math.Pow(left.X - right.X, 2d) + Math.Pow(left.Y - right.Y, 2d));

    private static void AssertReplay(
        RoofGeneratedMemberGeometry canonical,
        RoofGeneratedMemberGeometry expected,
        RoofGeneratedMemberOverride? overrideData)
    {
        Assert.NotNull(overrideData);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(
            canonical,
            ZUp,
            overrideData,
            out var replayed));
        AssertGeometry(expected, replayed);
    }

    private static RoofGeneratedMemberGeometry RotateAround(
        RoofGeneratedMemberGeometry geometry,
        RoofPoint3D origin,
        double radians) =>
        new(RotatePoint(geometry.Start, origin, radians), RotatePoint(geometry.End, origin, radians));

    private static RoofPoint3D RotatePoint(RoofPoint3D point, RoofPoint3D origin, double radians)
    {
        var dx = point.X - origin.X;
        var dy = point.Y - origin.Y;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new RoofPoint3D(
            origin.X + dx * cos - dy * sin,
            origin.Y + dx * sin + dy * cos,
            point.Z);
    }

    private static RoofRafterGeometry Find(RoofRafterLayout layout, RoofGeneratedMemberKey key) =>
        Assert.Single(layout.Rafters, item => item.LogicalKey == key);

    private static RoofRafterLayout Layout(
        double widthMm,
        double runMm,
        double slopeDegrees) =>
        Layout(Definition(widthMm, runMm, slopeDegrees, 0d));

    private static RoofRafterLayout Layout(RoofDefinition definition)
    {
        var geometry = SolveGeometry(definition);
        var layout = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(SpacingMm, WidthMm));
        Assert.True(layout.IsValid, layout.Error.ToString());
        Assert.All(layout.Layout!.Rafters, item => Assert.Equal(RafterRoofFace.Face0, item.Face));
        return layout.Layout;
    }

    private static IRoofGeometry SolveGeometry(RoofDefinition definition)
    {
        var solved = RoofGeometrySolver.Solve(definition);
        Assert.True(solved.IsValid, solved.Error.ToString());
        return solved.Geometry!;
    }

    private static RoofDefinition Definition(
        double widthMm,
        double runMm,
        double slopeDegrees,
        double rotationDegrees)
    {
        var input = RotatedRectangle(widthMm, runMm, rotationDegrees);
        var validated = RoofFootprintValidator.Validate(input);
        Assert.True(validated.IsValid, validated.Error.ToString());
        var radians = rotationDegrees * Math.PI / 180d;
        Assert.True(RoofDirection2D.TryCreate(-Math.Sin(radians), Math.Cos(radians), out var direction));
        return new RoofDefinition(
            validated.Footprint!,
            new RoofParameters(slopeDegrees, SlopeDirection: direction),
            RoofKind.Monopitch);
    }

    private static RoofFootprint ValidateFootprint(RoofFootprintInput input)
    {
        var validated = RoofFootprintValidator.Validate(input);
        Assert.True(validated.IsValid, validated.Error.ToString());
        return validated.Footprint!;
    }

    private static RoofFootprintInput RotatedRectangle(
        double widthMm,
        double runMm,
        double rotationDegrees)
    {
        var radians = rotationDegrees * Math.PI / 180d;
        RoofPoint2D Rotate(double x, double y) => new(
            x * Math.Cos(radians) - y * Math.Sin(radians),
            x * Math.Sin(radians) + y * Math.Cos(radians));
        return new RoofFootprintInput(
            [Rotate(0d, 0d), Rotate(widthMm, 0d), Rotate(widthMm, runMm), Rotate(0d, runMm)],
            true);
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
                10000d,
                6000d),
            EditState: RoofEditState.Unlocked,
            ManualOverrides: overrides,
            Face1SlopeDegrees: 30d,
            EaveHeightDifferenceMm: 3464.1016151377544d);

    private static RoofPoint3D Add(RoofPoint3D left, RoofPoint3D right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static RoofPoint3D Subtract(RoofPoint3D left, RoofPoint3D right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static RoofPoint3D Scale(RoofPoint3D vector, double scalar) =>
        new(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);

    private static double Dot(RoofPoint3D left, RoofPoint3D right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;
}
