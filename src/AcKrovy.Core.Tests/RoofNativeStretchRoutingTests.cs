using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Source authority, persisted replay and adapter boundaries; not native HOST proof.</summary>
public sealed class RoofNativeStretchRoutingTests
{
    private static readonly RoofPoint3D Normal = RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal;

    [Fact]
    public void EndpointStretch_WithUnchangedSource_PersistsSameKeyAndRebuildsOnlyTarget()
    {
        var fixture = CreateFixture();
        var baseline = RoofGeneratedMemberOverrideRules.CanonicalGeometry(fixture.Target, 0);
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(baseline, Normal, out var basis));
        var observed = new RoofGeneratedMemberGeometry(baseline.Start, new RoofPoint3D(
            baseline.End.X - 125 * basis.AxisU.X, baseline.End.Y - 125 * basis.AxisU.Y, 0));
        Assert.True(RoofUnsupportedStretchRecoveryRules.SourceGeometryMatchesSnapshot(
            fixture.Source, 0, Normal, Snapshot(fixture.Source)));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            baseline, observed, Normal, out var start, out var end, out var accepted, out _));
        var edit = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, fixture.Target.LogicalKey, "K12", start, end);
        Assert.NotNull(edit);
        Assert.Equal(-125, edit.EndOffsetMm, 6);
        var persisted = Decode(RoofDefinitionDataCodec.Encode(
            RoofGeneratedMemberOverrideRules.WithEditState(fixture.Definition, RoofEditState.Unlocked, [edit])));
        Assert.Equal(fixture.Target.LogicalKey, Assert.Single(persisted.Overrides).Key);
        Assert.Equal("K12", persisted.Overrides[0].ReservedElementId);
        var before = Build(fixture, fixture.Definition);
        var after = Build(fixture, persisted);
        Assert.True(RoofPhysicalStretchRules.TrySelectRebuildKeys(
            before.Members.Select(member => member.MemberKey).ToArray(), [fixture.Target.LogicalKey], [], out var keys));
        Assert.Equal(fixture.Target.LogicalKey, Assert.Single(keys));
        // Actual adapter uses these keys to detach/build; unrelated bodies retain identity.
        var bodies = before.Members.ToDictionary(member => member.MemberKey);
        foreach (var key in keys) bodies[key] = after.Members.Single(member => member.MemberKey == key);
        foreach (var old in before.Members)
        {
            var current = bodies[old.MemberKey];
            if (old.MemberKey == fixture.Target.LogicalKey)
            {
                Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(accepted,
                    new RoofGeneratedMemberGeometry(current.PlanAxis.Start, current.PlanAxis.End)));
                Assert.Equal(0, current.PlanAxis.Start.Z);
                Assert.Equal(0, current.PlanAxis.End.Z);
                Assert.NotEqual(old.SolidVertices, current.SolidVertices);
            }
            else Assert.Same(old, current);
        }
        var expected = before.Members.Select(member => RoofPhysicalStretchRules.PhysicalMemberId(member.MemberKey)).ToArray();
        var actual = bodies.Keys.Select(RoofPhysicalStretchRules.PhysicalMemberId).ToArray();
        Assert.Equal(expected.Length, actual.Distinct().Count());
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
    }

    [Fact]
    public void ChangedSourcePolyline_StillClassifiesAsSupportedRoofResize()
    {
        var fixture = CreateFixture();
        var resized = new RoofFootprintInput(
            [new(0, 0), new(11000, 0), new(11000, 6000), new(0, 6000)], true);
        Assert.False(RoofUnsupportedStretchRecoveryRules.SourceGeometryMatchesSnapshot(
            resized, 0, Normal, Snapshot(fixture.Source)));
        var validation = RoofFootprintValidator.Validate(resized);
        Assert.Equal(RoofSourceChangeKind.SupportedResize,
            RoofDefinitionPersistence.Classify(resized, validation.Footprint!, fixture.Definition).Kind);
    }

    [Theory]
    [InlineData("translation")]
    [InlineData("elevation")]
    [InlineData("normal")]
    [InlineData("closed")]
    [InlineData("curve")]
    [InlineData("off-plane")]
    [InlineData("nan")]
    public void SourceComparison_DetectsRawAuthoritativeChanges(string change)
    {
        var fixture = CreateFixture();
        var vertices = fixture.Source.Vertices!.ToArray();
        if (change == "translation") vertices = vertices.Select(point => new RoofPoint2D(point.X + 100, point.Y)).ToArray();
        if (change == "nan") vertices[0] = new RoofPoint2D(double.NaN, 0);
        var live = new RoofFootprintInput(vertices, change != "closed", change == "curve", change != "off-plane");
        Assert.False(RoofUnsupportedStretchRecoveryRules.SourceGeometryMatchesSnapshot(live,
            change == "elevation" ? 10 : 0, change == "normal" ? new RoofPoint3D(0, 1, 0) : Normal,
            Snapshot(fixture.Source)));
    }

    [Fact]
    public void NoOp_DoesNotCreateAnOverrideOrSelectAPhysicalRebuild()
    {
        var fixture = CreateFixture();
        var baseline = RoofGeneratedMemberOverrideRules.CanonicalGeometry(fixture.Target, 0);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            baseline, baseline, Normal, out var start, out var end, out _, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.NeitherEndpointChanged, reason);
        Assert.Null(RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, fixture.Target.LogicalKey, null, start, end));
        Assert.True(RoofPhysicalStretchRules.TrySelectRebuildKeys(
            fixture.Layout.Rafters.Select(member => member.LogicalKey).ToArray(), [], [], out var keys));
        Assert.Empty(keys);
        Assert.Equal(RoofDefinitionDataCodec.Encode(fixture.Definition),
            RoofDefinitionDataCodec.Encode(Decode(RoofDefinitionDataCodec.Encode(fixture.Definition))));
    }

    [Fact]
    public void UndoRedoPayloadReplay_RestoresBothPlanAndDerivedStateWithoutChangingKeys()
    {
        var fixture = CreateFixture();
        var beforePayload = RoofDefinitionDataCodec.Encode(fixture.Definition);
        var afterPayload = RoofDefinitionDataCodec.Encode(RoofGeneratedMemberOverrideRules.WithEditState(
            fixture.Definition, RoofEditState.Unlocked,
            [new(fixture.Target.LogicalKey, false, 0, 0, 0, 0, -125)]));
        var before = Build(fixture, Decode(beforePayload));
        var edited = Build(fixture, Decode(afterPayload));
        var undo = Build(fixture, Decode(beforePayload));
        var redo = Build(fixture, Decode(afterPayload));
        foreach (var original in before.Members)
        {
            var restored = undo.Members.Single(member => member.MemberKey == original.MemberKey);
            var changed = edited.Members.Single(member => member.MemberKey == original.MemberKey);
            var replayed = redo.Members.Single(member => member.MemberKey == original.MemberKey);
            Assert.Equal(original.PlanAxis, restored.PlanAxis);
            Assert.Equal(original.SolidVertices, restored.SolidVertices);
            Assert.Equal(changed.PlanAxis, replayed.PlanAxis);
            Assert.Equal(changed.SolidVertices, replayed.SolidVertices);
            if (original.MemberKey == fixture.Target.LogicalKey) Assert.NotEqual(original.PlanAxis, changed.PlanAxis);
            else Assert.Equal(original.PlanAxis, changed.PlanAxis);
        }
    }

    [Fact]
    public void Adapter_RoutesOnSourceSnapshotBeforeChildDrift_AndFiltersUnchangedMembers()
    {
        var resize = Read("RoofLiveResizeService.cs");
        var inspect = Between(resize, "private static InspectionPlan Inspect", "private static bool HasErasedGeneratedTimber");
        Assert.True(inspect.IndexOf("!HasSourceGeometryChanged(source", StringComparison.Ordinal) <
            inspect.IndexOf("treatHipDisplayDriftAsResize: true", StringComparison.Ordinal));
        Assert.Contains("sourceCandidates.Add(generatedOwnerId)", inspect);
        Assert.Contains("IsUnchangedStretchMemberNotification(entity", inspect);
        Assert.Contains("RoofGeneratedMemberOverrideMath.GeometryEquals(", inspect);
        Assert.Contains("!plan.UnchangedGeneratedMemberIds.Contains(id)", resize);
        var manual = Read("RoofGeneratedMemberManualEditService.cs");
        Assert.Contains("var sourceModified = RoofLiveResizeService.HasSourceGeometryChanged(", manual);
        Assert.Contains("acceptedPlanIds.Add(id)", manual);
        Assert.Contains("acceptedPlanIds: acceptedPlanIds", manual);
        Assert.Contains("acceptedPlanIds ?? modifiedIds", Read("RoofOrdinaryRafterSolidMaterializationService.cs"));
    }

    [Fact]
    public void CancelAndUndo_KeepExistingNoMaintenanceBoundaries()
    {
        var live = Read("LiveGeometrySynchronizationService.cs");
        var cancel = Between(live, "private void CommandCancelled", "private void CommandFailed");
        Assert.Contains("ClearPendingLiveGeometryState()", cancel);
        Assert.DoesNotContain("RefreshCandidates(", cancel);
        var resize = Read("RoofLiveResizeService.cs");
        var process = Between(resize, "public static IReadOnlyCollection<ObjectId> Process(", "private static InspectionPlan Inspect");
        Assert.True(process.IndexOf("IsUndoRedoCommand(globalCommandName)", StringComparison.Ordinal) <
            process.IndexOf("Inspect(", StringComparison.Ordinal));
        Assert.Contains("RoofUnsupportedStretchRecoverySnapshotService.Clear(\"CommandCancelled\"", cancel);
    }

    private sealed record Fixture(RoofFootprintInput Source, HipRoofGeometry Geometry,
        RoofFaceRafterLayout Face, RoofRafterLayout Layout, RoofRafterGeometry Target, RoofDefinitionData Definition);

    private static Fixture CreateFixture()
    {
        var source = new RoofFootprintInput([new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)], true);
        var footprint = RoofFootprintValidator.Validate(source).Footprint!;
        var geometry = Assert.IsType<HipRoofGeometry>(RoofGeometrySolver.Solve(new RoofDefinition(
            footprint, new RoofParameters(35), RoofKind.Hip)).Geometry);
        var face = RoofFaceRafterLayoutService.Create(geometry.Topology, 500).Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(geometry, face, 80, out var layout));
        var target = layout.Rafters.First(rafter => face.Segments[rafter.StationIndex].EndBoundaryRole == RoofRafterBoundaryRole.Ridge);
        return new(source, geometry, face, layout, target, RoofGeneratedMemberOverrideRules.WithEditState(
            RoofDefinitionPersistence.Create(source, footprint, geometry), RoofEditState.Unlocked, null));
    }

    private static RoofUnsupportedStretchSourceSnapshotData Snapshot(RoofFootprintInput source) =>
        new("AB", source.Vertices!.ToArray(), source.IsClosed, 0, Normal.X, Normal.Y, Normal.Z);

    private static RoofDefinitionData Decode(string payload)
    {
        Assert.True(RoofDefinitionDataCodec.TryDecode(payload, out var decoded, out _));
        return decoded!;
    }

    private static RoofAutomaticRafterPhysicalModel Build(Fixture fixture, RoofDefinitionData definition)
    {
        var replay = RoofGeneratedMemberReplayPlanner.Create(fixture.Layout, 0, Normal, definition.Overrides);
        Assert.True(replay.IsValid);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", fixture.Geometry.Topology,
            fixture.Face, fixture.Layout, 3000, 80, 125, new RoofAutomaticRafterPhysicalSettings(), replay, out var model));
        return model!;
    }

    private static string Between(string source, string start, string end) =>
        source[source.IndexOf(start, StringComparison.Ordinal)..source.IndexOf(end, StringComparison.Ordinal)];

    private static string Read(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }
}
