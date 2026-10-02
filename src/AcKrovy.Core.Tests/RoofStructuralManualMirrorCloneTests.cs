using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using System.Text.Json;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Semantic clone behavior and adapter wiring; not AutoCAD HOST validation.</summary>
public sealed class RoofStructuralManualMirrorCloneTests
{
    [Fact]
    public void AppendedManualMirror_ReachesRemintAndPersistsBeforePhysicalReconcile()
    {
        var router = Read("RoofStructuralNativeEditService");
        var guard = RoofUxSourceContractText.Member(router,
            "var before = snapshot.Assembly.TimberLines", "if (!AutoCadObjectIdAccess.TryGetObjectAllowErased<Line>");
        Assert.DoesNotContain("(before is not null || candidate.Manual)", guard);
        Assert.Contains("before is not null", guard);
        Assert.Contains("candidate.Manual", guard);
        var remint = RoofUxSourceContractText.Member(router,
            "private static bool TryEnsureManualCloneIdentity", "private static bool TryConvertCloneToAttachedManual");
        Assert.Contains("RoofStructuralAttachedManualDataRules.CreateMirroredClone", remint);
        Assert.Contains("snapshot.TimberLines", remint);
        Assert.Contains("existing.ManualIdentity", remint);
        Assert.Contains("RoofStructuralAttachedManualStore.WriteReplacingGenerated", remint);
        var appended = RoofUxSourceContractText.Member(router,
            "if (candidate.Manual)", "if (!expected.TryGetValue(candidate.Key, out var item)");
        Assert.Contains("RoofMirrorCloneDetachService.DeleteStructuralMirrorCloneAnnotations", appended);
        Assert.Contains("mirrorCloneAnnotations[candidate.Id]", appended);
        Assert.Contains("copySourcePreservation: true", router);
        Assert.True(router.IndexOf("TryEnsureManualCloneIdentity(", StringComparison.Ordinal) <
            router.IndexOf("RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction", StringComparison.Ordinal));
        var live = Read("LiveGeometrySynchronizationService");
        Assert.Contains("appendedTimberIds, appendedAnnotationIds", live);
        Assert.Contains("ROOF_STRUCT_MANUAL_MIRROR_CLONE", router);
    }

    [Theory]
    [InlineData(RoofStructuralRole.Hip, RoofStructuralAttachedManualCreationKind.Copy, RoofStructuralHeightMode.Automatic)]
    [InlineData(RoofStructuralRole.Hip, RoofStructuralAttachedManualCreationKind.Copy, RoofStructuralHeightMode.Explicit)]
    [InlineData(RoofStructuralRole.Hip, RoofStructuralAttachedManualCreationKind.Mirror, RoofStructuralHeightMode.Automatic)]
    [InlineData(RoofStructuralRole.Hip, RoofStructuralAttachedManualCreationKind.Mirror, RoofStructuralHeightMode.Explicit)]
    [InlineData(RoofStructuralRole.Valley, RoofStructuralAttachedManualCreationKind.Copy, RoofStructuralHeightMode.Automatic)]
    [InlineData(RoofStructuralRole.Valley, RoofStructuralAttachedManualCreationKind.Copy, RoofStructuralHeightMode.Explicit)]
    [InlineData(RoofStructuralRole.Valley, RoofStructuralAttachedManualCreationKind.Mirror, RoofStructuralHeightMode.Automatic)]
    [InlineData(RoofStructuralRole.Valley, RoofStructuralAttachedManualCreationKind.Mirror, RoofStructuralHeightMode.Explicit)]
    public void MirrorNo_CreatesSecondIdentityAndReflectedBodyWithoutMutatingSource(
        RoofStructuralRole role, RoofStructuralAttachedManualCreationKind creationKind, RoofStructuralHeightMode heightMode)
    {
        var source = Source(role, creationKind, heightMode);
        var originalJson = JsonSerializer.Serialize(source);
        var result = RoofStructuralAttachedManualDataRules.CreateMirroredClone(source, SourcePlan(), ClonePlan());
        Assert.True(result.IsValid);
        var clone = Assert.IsType<RoofStructuralAttachedManualData>(result.Data);
        Assert.NotEqual(source.ManualIdentity, clone.ManualIdentity);
        Assert.True(RoofStructuralAttachedManualIdentityRules.TryNormalize(clone.ManualIdentity, out _));
        Assert.Equal(originalJson, JsonSerializer.Serialize(source));
        Assert.Equal(source.RoofOwnerReference, clone.RoofOwnerReference);
        Assert.Equal(source.SourceLogicalKey, clone.SourceLogicalKey);
        Assert.Equal(source.SourceRole, clone.SourceRole);
        Assert.Equal(source.SourceBoundaryEdgeIdA, clone.SourceBoundaryEdgeIdA);
        Assert.Equal(source.SourceBoundaryEdgeIdB, clone.SourceBoundaryEdgeIdB);
        Assert.Equal(source.WidthMm, clone.WidthMm);
        Assert.Equal(source.HeightMode, clone.HeightMode);
        Assert.Equal(source.ExplicitHeightMm, clone.ExplicitHeightMm);
        Assert.Equal(RoofStructuralAttachedManualCreationKind.Mirror, clone.CreationKind);
        Assert.Equal(RoofStructuralAttachedManualDataSchema.PlacementVersion, clone.SchemaVersion);
        Assert.True(clone.HasExplicitPlacement);
        Assert.NotEqual(source.Placement, clone.Placement);
        Assert.Equal(source with
        {
            ManualIdentity = clone.ManualIdentity, CreationKind = RoofStructuralAttachedManualCreationKind.Mirror,
            Placement = clone.Placement,
        }, clone);
        Assert.Equal(0d, ClonePlan().Start.Z);
        Assert.Equal(0d, ClonePlan().End.Z);
        AssertPoint(Reflect(SourcePlan().Start), ClonePlan().Start);
        AssertPoint(Reflect(SourcePlan().End), ClonePlan().End);
        var reopened = JsonSerializer.Deserialize<RoofStructuralAttachedManualData>(JsonSerializer.Serialize(clone));
        Assert.Equal(clone, reopened);
        Assert.True(RoofStructuralManualPlacementRules.TryBuildPrism(
            role, source.WidthMm, source.Placement!, out var originalBody, out var failure), failure);
        Assert.True(RoofStructuralManualPlacementRules.TryBuildPrism(
            role, reopened!.WidthMm, reopened.Placement!, out var cloneBody, out failure), failure);
        for (var i = 0; i < 8; i++)
            AssertPoint(Reflect(originalBody!.ConvexHalves[0].SourcePrismVertices[i]),
                cloneBody!.ConvexHalves[0].SourcePrismVertices[i]);
        Assert.True(originalBody!.ConvexHalves[0].SourcePrismVertices[0].DistanceTo(
            cloneBody!.ConvexHalves[0].SourcePrismVertices[0]) > 1000);
        var keys = new[] { source, reopened }.Select(member =>
            RoofStructuralAttachedManualIdentityRules.PhysicalKey(member.ManualIdentity)).ToArray();
        Assert.Equal(2, keys.Distinct().Count());
        Assert.Contains("ManualStructural:444903c3d3884978a6b19bf97039cd50", keys);
        Assert.Contains("ManualStructural:" + clone.ManualIdentity, keys);
    }

    [Fact]
    public void RepeatedMirrorNo_MintsIndependentClonesFromUnchangedSource()
    {
        var source = Source(RoofStructuralRole.Hip, RoofStructuralAttachedManualCreationKind.Copy,
            RoofStructuralHeightMode.Explicit);
        var original = JsonSerializer.Serialize(source);
        var ids = new HashSet<string> { source.ManualIdentity };
        RoofStructuralManualPlacement? first = null;
        for (var i = 0; i < 16; i++)
        {
            var result = RoofStructuralAttachedManualDataRules.CreateMirroredClone(source, SourcePlan(), ClonePlan());
            Assert.True(result.IsValid);
            Assert.True(ids.Add(result.Data!.ManualIdentity));
            first ??= result.Data.Placement;
            Assert.Equal(first, result.Data.Placement);
            Assert.Equal(original, JsonSerializer.Serialize(source));
        }
    }

    [Fact]
    public void MirrorNo_MissingFrameCannotCreateCloneFromGeneratedProvenance()
    {
        var source = Source(RoofStructuralRole.Hip, RoofStructuralAttachedManualCreationKind.Copy,
            RoofStructuralHeightMode.Explicit) with { SchemaVersion = 1, Placement = null };
        var result = RoofStructuralAttachedManualDataRules.CreateMirroredClone(source, SourcePlan(), ClonePlan());
        Assert.False(result.IsValid);
        Assert.Null(result.Data);
        Assert.Equal(RoofStructuralAttachedManualDataError.IncompletePayload, result.Error);
    }

    [Fact]
    public void MirrorNo_ChangedLengthCannotCreateCloneWithStaleFrame()
    {
        var source = Source(RoofStructuralRole.Hip, RoofStructuralAttachedManualCreationKind.Copy,
            RoofStructuralHeightMode.Explicit);
        var plan = ClonePlan();
        var invalid = new RoofSegment3D(plan.Start, new(plan.End.X + 100, plan.End.Y, 0));
        var result = RoofStructuralAttachedManualDataRules.CreateMirroredClone(source, SourcePlan(), invalid);
        Assert.False(result.IsValid);
        Assert.Null(result.Data);
        Assert.Equal(RoofStructuralAttachedManualDataError.MalformedValueType, result.Error);
    }

    [Fact]
    public void MirrorNo_NonplanarInputIsRejected()
    {
        var source = Source(RoofStructuralRole.Hip, RoofStructuralAttachedManualCreationKind.Copy,
            RoofStructuralHeightMode.Explicit);
        var plan = ClonePlan();
        var result = RoofStructuralAttachedManualDataRules.CreateMirroredClone(source, SourcePlan(),
            new(new(plan.Start.X, plan.Start.Y, 10), plan.End));
        Assert.False(result.IsValid);
        Assert.Null(result.Data);
    }

    private static RoofStructuralAttachedManualData Source(RoofStructuralRole role,
        RoofStructuralAttachedManualCreationKind kind, RoofStructuralHeightMode heightMode)
    {
        var result = RoofStructuralAttachedManualDataRules.Create("2912", "444903c3d3884978a6b19bf97039cd50",
            new(role, 1, 4), kind, 120, heightMode,
            heightMode == RoofStructuralHeightMode.Explicit ? 160 : null,
            new(34035.12031771148, 10908.504361157415, -49.567145124853056,
                30948.56039718791, 13995.064281680987, 1732.4590558593404,
                -1 / Math.Sqrt(2), -1 / Math.Sqrt(2), 0, 0, 0, 1, 160));
        Assert.True(result.IsValid);
        return result.Data!;
    }

    private static RoofSegment3D SourcePlan() => new(
        new(33991.69391084029, 10951.930768028607, 0),
        new(30991.693910840288, 13951.930768028607, 0));
    private static RoofSegment3D ClonePlan() => new(
        new(32224.873978734307, 10951.930768028607, 0),
        new(35224.87397873431, 13951.930768028607, 0));
    private static RoofPoint3D Reflect(RoofPoint3D point) => new(66216.5678895746 - point.X, point.Y, point.Z);
    private static void AssertPoint(RoofPoint3D expected, RoofPoint3D actual) =>
        Assert.InRange(actual.DistanceTo(expected), 0d, 1e-6);

    private static string Read(string name) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", name + ".cs");
}
