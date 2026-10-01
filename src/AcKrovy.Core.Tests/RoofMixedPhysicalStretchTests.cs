using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Semantic/model regressions; native crossing selection still needs HOST execution.</summary>
public sealed class RoofMixedPhysicalStretchTests
{
    [Theory]
    [InlineData(1, "STRETCH")]
    [InlineData(4, "STRETCH")]
    [InlineData(1, "GRIP_STRETCH")]
    [InlineData(4, "GRIP_STRETCH")]
    public void MixedStretch_RebuildsUniqueKeys_OnlyPlan2DChangesLogicalGeometry(int physicalCount, string command)
    {
        Assert.True(RoofPhysicalStretchRules.ShouldRecover(command, sourceModified: false));
        var (geometry, faceLayout, layout, before) = CreateModel();
        var edited = layout.Rafters.First(rafter =>
            faceLayout.Segments[rafter.StationIndex].EndBoundaryRole == RoofRafterBoundaryRole.Ridge);
        var overrides = new[] { new RoofGeneratedMemberOverride(edited.LogicalKey, false, 0, 0, 0, 0, -125) };
        var collateral = new[] { edited.LogicalKey }.Concat(before.Members
            .Select(member => member.MemberKey).Where(key => key != edited.LogicalKey)
            .Take(physicalCount - 1)).ToArray();
        // Duplicate selection callbacks and the matching Plan2D+Physical key
        // must still create exactly one physical body per key.
        Assert.True(RoofPhysicalStretchRules.TrySelectRebuildKeys(
            before.Members.Select(member => member.MemberKey).ToArray(),
            [edited.LogicalKey], collateral.Concat(collateral)
                .Select(RoofPhysicalStretchRules.PhysicalMemberId).ToArray(), out var selected));
        Assert.Equal(physicalCount, selected.Count);
        Assert.Single(selected, key => key == edited.LogicalKey);
        Assert.False(RoofPhysicalStretchRules.ShouldRejectDirectEdit(acceptedPlanEdit: true));

        var replay = RoofGeneratedMemberReplayPlanner.Create(layout, 0,
            RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, overrides);
        Assert.True(replay.IsValid);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", geometry.Topology,
            faceLayout, layout, 3000, 80, 125, new RoofAutomaticRafterPhysicalSettings(),
            replay, out var after));
        Assert.Single(overrides); // Physical identities never enter override composition.
        var rebuilt = before.Members.ToDictionary(member => member.MemberKey);
        foreach (var key in selected)
            rebuilt[key] = after!.Members.Single(member => member.MemberKey == key);
        Assert.Equal(before.Members.Count, rebuilt.Count);
        foreach (var (key, member) in rebuilt)
        {
            var old = before.Members.Single(item => item.MemberKey == key);
            if (key == edited.LogicalKey)
            {
                Assert.NotEqual(old.PlanAxis, member.PlanAxis);
                Assert.True(member.PhysicalLengthMm < old.PhysicalLengthMm);
            }
            else
            {
                Assert.Equal(old.PlanAxis, member.PlanAxis);
                Assert.Equal(old.SolidVertices, member.SolidVertices);
            }
        }
        var physicalKeys = rebuilt.Keys.Select(RoofPhysicalStretchRules.PhysicalMemberId).ToArray();
        Assert.Equal(physicalKeys.Length, physicalKeys.Distinct().Count());
        var expectedGroup = new[] { "source", "annotation", "structural" }.Concat(physicalKeys).ToArray();
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(expectedGroup, expectedGroup));
        Assert.False(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(
            expectedGroup.Concat([physicalKeys[0]]).ToArray(), expectedGroup));
    }

    [Fact]
    public void PhysicalOnlyStretch_RecoversBaselineWithoutAnyLogicalEdit()
    {
        var (_, _, _, model) = CreateModel();
        var key = model.Members[0].MemberKey;
        Assert.True(RoofPhysicalStretchRules.ShouldRecover("STRETCH", sourceModified: false));
        Assert.True(RoofPhysicalStretchRules.ShouldRejectDirectEdit(acceptedPlanEdit: false));
        Assert.True(RoofPhysicalStretchRules.TrySelectRebuildKeys(
            model.Members.Select(member => member.MemberKey).ToArray(), [],
            [RoofPhysicalStretchRules.PhysicalMemberId(key)], out var selected));
        Assert.Equal(key, Assert.Single(selected));
        // Recovery selects the original model member, with no geometry from a solid.
        Assert.Equal(model.Members[0], model.Members.Single(member => member.MemberKey == selected.Single()));
    }

    [Theory]
    [InlineData("STRETCH", true, false)]
    [InlineData("_.STRETCH", true, false)]
    [InlineData("STRETCH", false, true)]
    [InlineData("_.STRETCH", false, true)]
    [InlineData("GRIP_STRETCH", false, true)]
    [InlineData("_.grip_stretch", false, true)]
    [InlineData("'GRIP_STRETCH", false, true)]
    [InlineData("GRIP_STRETCH", true, false)]
    [InlineData("MOVE", false, false)]
    [InlineData("TRIM", false, false)]
    [InlineData("ERASE", false, false)]
    [InlineData("MIRROR", true, false)]
    [InlineData("UNDO", false, false)]
    [InlineData("REDO", false, false)]
    public void SourceRoofAndOtherLifecycles_RetainExistingRouting(string command, bool sourceModified, bool recover) =>
        Assert.Equal(recover, RoofPhysicalStretchRules.ShouldRecover(command, sourceModified));

    [Fact]
    public void UnknownPhysicalKeyOrDuplicateAuthority_FailsWithoutInventingPlanIdentity()
    {
        var (_, _, _, model) = CreateModel();
        var keys = model.Members.Select(member => member.MemberKey).ToArray();
        Assert.False(RoofPhysicalStretchRules.TrySelectRebuildKeys(keys, [], ["foreign-key"], out _));
        Assert.False(RoofPhysicalStretchRules.TrySelectRebuildKeys(
            keys.Concat([keys[0]]).ToArray(), [], [], out _));
    }

    [Fact]
    public void Adapter_ReconcilesBeforeCommit_AndNeverReadsNativePhysicalGeometry()
    {
        var manual = Read("RoofGeneratedMemberManualEditService.cs");
        var ordinary = Read("RoofOrdinaryRafterSolidMaterializationService.cs");
        var lifecycle = Read("RoofPhysical3DLifecycleService.cs");
        var resize = Read("RoofLiveResizeService.cs");
        Assert.Contains("restoreStretchCollateral: RoofPhysicalStretchRules.ShouldRecover(", manual);
        Assert.Contains("globalCommandName, RoofLiveResizeService.HasSourceGeometryChanged(", manual);
        Assert.Contains("acceptedPlanIds: acceptedPlanIds", manual);
        Assert.Contains(".IsClassicStretch(globalCommandName)", manual);
        Assert.Contains("RoofOrdinaryPhysicalReconciliationRules.TryPlan", ordinary);
        Assert.Contains("changedKeys.Add(data.StructuralId)", ordinary);
        var restore = lifecycle[lifecycle.IndexOf("public static bool TryRestoreStretchPhysicalInTransaction(", StringComparison.Ordinal)..
            lifecycle.IndexOf("public static void CleanupStillErasedSourceInTransaction(", StringComparison.Ordinal)];
        Assert.Contains("TryRestoreMovedPhysicalMembersInTransaction(", restore);
        Assert.Contains("RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction(", restore);
        Assert.Contains("ReconcileOwnerInTransaction(", restore);
        Assert.Contains("RoofDefinitionPersistence.Restore(", restore);
        Assert.DoesNotContain("TransformBy(", restore);
        Assert.DoesNotContain("GeometricExtents", restore);
        Assert.DoesNotContain("RoofDefinitionStore.Write(", restore);
        var accepted = manual[manual.IndexOf("// Hip/Valley are derived", StringComparison.Ordinal)..
            manual.IndexOf("return OwnerEditOutcome.Accepted;", StringComparison.Ordinal)];
        Assert.True(accepted.IndexOf("TryRestoreStructuralHipValleyMembersOnly", StringComparison.Ordinal) <
            accepted.IndexOf("TryRestoreStretchPhysicalInTransaction", StringComparison.Ordinal));
        Assert.True(accepted.IndexOf("TryRestoreStretchPhysicalInTransaction", StringComparison.Ordinal) <
            accepted.LastIndexOf("transaction.Commit()", StringComparison.Ordinal));
        Assert.Contains("TryVerifyStretchPhysicalState(document.Database, transaction, pair.Key)", resize);
        Assert.Contains("TryFinalizeRestoredPhysicalGroup(document, pair.Key", resize);
        Assert.Contains("SourceHandledOwnersThisCommand.Contains(ownerId)", resize);
        Assert.Contains("ShouldRejectDirectEdit(acceptedPlanEdit)", resize);
        Assert.Contains("acceptedOrdinaryStretchOwnerIds.Count > 0", resize);
        Assert.Contains("ordinaryPlanChanged |= generated.Data.MemberKind == RoofGeneratedTimberKind.Rafter", manual);
        Assert.Contains("transaction.Commit();\n            ordinaryStretchAccepted = ordinaryPlanChanged",
            manual.Replace("\r\n", "\n"));
    }

    [Fact]
    public void StructuralMetadataDiagnosis_IsDebugOnlyAndDoesNotChangeWritePolicy()
    {
        var materialize = Read("RoofAutomaticStructuralRafterMaterializationService.cs");
        var trace = Read("RoofAutomaticStructuralRafterTrace.cs");
        Assert.Contains("if (geometryChanged || metadataChanged)", materialize);
        Assert.Contains("#if DEBUG\n                if (metadataChanged)", materialize.Replace("\r\n", "\n"));
        Assert.StartsWith("#if DEBUG", trace);
        var diagnosis = trace[trace.IndexOf("public static void WriteMetadataDifference(", StringComparison.Ordinal)..
            trace.IndexOf("public static void WriteRejectedExistingAction(", StringComparison.Ordinal)];
        Assert.Contains("JsonSerializer.Serialize(existing)", diagnosis);
        Assert.Contains("JsonSerializer.Serialize(desired)", diagnosis);
        Assert.DoesNotContain("UpgradeOpen(", diagnosis);
        Assert.DoesNotContain("OpenMode.ForWrite", diagnosis);
    }

    private static (HipRoofGeometry Geometry, RoofFaceRafterLayout FaceLayout,
        RoofRafterLayout Layout, RoofAutomaticRafterPhysicalModel Model) CreateModel()
    {
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)], true));
        var geometry = Assert.IsType<HipRoofGeometry>(RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!, new RoofParameters(35), RoofKind.Hip)).Geometry);
        var face = RoofFaceRafterLayoutService.Create(geometry.Topology, 500).Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry, face, 80, out var layout));
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", geometry.Topology,
            face, layout, 3000, 80, 125, new RoofAutomaticRafterPhysicalSettings(), out var model));
        return (geometry, face, layout, model!);
    }

    private static string Read(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }
}
