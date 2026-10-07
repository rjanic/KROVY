using System.Text.Json;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Real metadata and physical builders; native event/annotation APIs still require HOST.</summary>
public sealed class RoofStructuralManualCopyPlacementTests
{
    [Theory]
    [InlineData(RoofStructuralRole.Hip, RoofStructuralHeightMode.Automatic)]
    [InlineData(RoofStructuralRole.Hip, RoofStructuralHeightMode.Explicit)]
    [InlineData(RoofStructuralRole.Valley, RoofStructuralHeightMode.Automatic)]
    [InlineData(RoofStructuralRole.Valley, RoofStructuralHeightMode.Explicit)]
    public void CopyAfterMove_UsesCurrentFrameAndRoundTripsIndependentPhysicalBody(
        RoofStructuralRole role, RoofStructuralHeightMode heightMode)
    {
        var generatedFrame = Frame();
        var moved = RoofStructuralManualPlacementRules.Translate(generatedFrame, 5000, -2700, 0);
        var source = Source(role, heightMode, moved);
        var sourceJson = JsonSerializer.Serialize(source);
        var plan = Translate(Plan(), 5000, -2700);
        var copiedPlan = Translate(plan, 1300, 2200);
        var clone = Assert.IsType<RoofStructuralAttachedManualData>(
            RoofStructuralAttachedManualDataRules.CreateCopiedClone(source, plan, copiedPlan).Data);
        Assert.NotEqual(source.ManualIdentity, clone.ManualIdentity);
        Assert.Equal(source with
        {
            ManualIdentity = clone.ManualIdentity,
            CreationKind = RoofStructuralAttachedManualCreationKind.Copy,
            Placement = RoofStructuralManualPlacementRules.Translate(moved, 1300, 2200, 0),
        }, clone);
        Assert.Equal(sourceJson, JsonSerializer.Serialize(source));
        Assert.Equal(0, copiedPlan.Start.Z);
        Assert.Equal(0, copiedPlan.End.Z);
        var reopened = JsonSerializer.Deserialize<RoofStructuralAttachedManualData>(JsonSerializer.Serialize(clone));
        Assert.Equal(clone, reopened);
        Assert.Equal(RoofStructuralAttachedManualDataSchema.PlacementVersion, reopened!.SchemaVersion);
        Assert.NotEqual(Key(source), Key(reopened));
        var sourceBody = Vertices(source);
        var cloneBody = Vertices(reopened);
        var wrongFoldBody = Vertices(source with
        {
            Placement = RoofStructuralManualPlacementRules.Translate(generatedFrame, 1300, 2200, 0),
        });
        for (var i = 0; i < 8; i++)
        {
            AssertPoint(new(sourceBody[i].X + 1300, sourceBody[i].Y + 2200, sourceBody[i].Z), cloneBody[i]);
            Assert.True(cloneBody[i].DistanceTo(wrongFoldBody[i]) > 5000);
        }
        Assert.Equal(sourceBody, Vertices(source));
    }

    [Fact]
    public void RepeatedCopy_AndCopyOfCopy_CreateIndependentKeysWithoutChangingProvenance()
    {
        var source = Source(RoofStructuralRole.Hip, RoofStructuralHeightMode.Explicit, Frame());
        var json = JsonSerializer.Serialize(source);
        var keys = new HashSet<string> { Key(source) };
        for (var i = 1; i <= 16; i++)
        {
            var plan = Translate(Plan(), 1000 * i, -200 * i);
            var clone = RoofStructuralAttachedManualDataRules.CreateCopiedClone(source, Plan(), plan).Data!;
            Assert.True(keys.Add(Key(clone)));
            Assert.Equal(source.SourceLogicalKey, clone.SourceLogicalKey);
            var second = RoofStructuralAttachedManualDataRules.CreateCopiedClone(clone, plan, Translate(plan, -500, 400)).Data!;
            Assert.True(keys.Add(Key(second)));
            Assert.Equal(RoofStructuralManualPlacementRules.Translate(clone.Placement!, -500, 400, 0), second.Placement);
        }
        Assert.Equal(33, keys.Count);
        Assert.Equal(json, JsonSerializer.Serialize(source));
    }

    [Fact]
    public void CopyOfMirroredManual_TranslatesReflectedOrientationAndEveryPhysicalVertex()
    {
        var source = Source(RoofStructuralRole.Hip, RoofStructuralHeightMode.Explicit, Frame());
        var reflectedPlan = new RoofSegment3D(Reflect(Plan().Start), Reflect(Plan().End));
        var mirrored = RoofStructuralAttachedManualDataRules.CreateMirroredClone(source, Plan(), reflectedPlan).Data!;
        var clone = RoofStructuralAttachedManualDataRules.CreateCopiedClone(mirrored, reflectedPlan,
            Translate(reflectedPlan, -800, 1900)).Data!;
        Assert.Equal(RoofStructuralAttachedManualCreationKind.Copy, clone.CreationKind);
        Assert.Equal(RoofStructuralManualPlacementRules.Translate(mirrored.Placement!, -800, 1900, 0), clone.Placement);
        Assert.NotEqual(source.Placement!.SideX, clone.Placement!.SideX);
        var before = Vertices(mirrored);
        var after = Vertices(clone);
        for (var i = 0; i < 8; i++)
            AssertPoint(new(before[i].X - 800, before[i].Y + 1900, before[i].Z), after[i]);
    }

    [Theory]
    [InlineData("missing-frame")]
    [InlineData("changed-end")]
    [InlineData("reversed")]
    [InlineData("nonplanar")]
    [InlineData("invalid-frame")]
    [InlineData("nonfinite-delta")]
    [InlineData("nonfinite-z")]
    public void InvalidCopy_FailsClosedWithoutCreatingGeneratedFallback(string invalid)
    {
        var source = Source(RoofStructuralRole.Hip, RoofStructuralHeightMode.Explicit, Frame());
        var plan = Translate(Plan(), 1000, 2000);
        if (invalid == "missing-frame") source = source with { SchemaVersion = 1, Placement = null };
        if (invalid == "invalid-frame") source = source with { Placement = Frame() with { SectionHeightMm = -1 } };
        if (invalid == "changed-end") plan = plan with { End = new(plan.End.X + 20, plan.End.Y, 0) };
        if (invalid == "reversed") plan = new(plan.End, plan.Start);
        if (invalid == "nonplanar") plan = plan with { Start = new(plan.Start.X, plan.Start.Y, 20) };
        if (invalid == "nonfinite-delta") plan = plan with { Start = new(double.NaN, plan.Start.Y, 0) };
        if (invalid == "nonfinite-z") plan = plan with { End = new(plan.End.X, plan.End.Y, double.NaN) };
        var json = JsonSerializer.Serialize(source);
        var result = RoofStructuralAttachedManualDataRules.CreateCopiedClone(source, Plan(), plan);
        Assert.False(result.IsValid);
        Assert.Null(result.Data);
        Assert.Equal(json, JsonSerializer.Serialize(source));
    }

    [Fact]
    public void CopyAnnotations_ExactHandleOwnershipPreservesSourceAndRepeatedUpsertDoesNotDuplicate()
    {
        var timber = TimberElementDefaults.For(TimberElementType.HipRafter) with
        {
            ElementId = "N1", WidthMm = 120, HeightMm = 160, SlopeDegrees = 30,
            AnnotationMode = TimberAnnotationMode.DimensionsLeader,
        };
        var refresh = TimberAnnotationRefreshPlanner.Create(timber);
        Assert.True(refresh.EnsureLabel && refresh.ShouldSlopeArrowExist && refresh.ShouldSlopeAngleTextExist);
        Assert.Equal("120x160", TimberElementLabelFormatter.FormatDimensions(timber));
        var roles = TimberCompositeAnnotationLifecycleRules.RequiredRoles(timber.AnnotationMode, timber.ItemNumberLeaderStyle);
        var sourceLabels = roles.Select(role => Label("A-" + role, "A1", role)).ToArray();
        var labels = sourceLabels.ToList();
        foreach (var handle in new[] { "B1", "C1" })
        {
            foreach (var role in roles)
            {
                var first = Select(handle, labels.Where(label => label.ComponentRole == role).ToArray());
                Assert.Null(first.LabelKeyToUpdate);
                Assert.Empty(first.LabelKeysToDelete);
                labels.Add(Label(handle + "-" + role, handle, role));
                var repeat = Select(handle, labels.Where(label => label.ComponentRole == role).ToArray());
                Assert.Equal(handle + "-" + role, repeat.LabelKeyToUpdate);
                Assert.Empty(repeat.LabelKeysToDelete);
            }
        }
        Assert.Equal(sourceLabels, labels.Where(label => label.SourceHandle == "A1").ToArray());
        Assert.Empty(TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(labels, new[] { "A1", "B1", "C1" }));
        Assert.Empty(TimberElementLabelCleanupRules.SelectLabelsWithoutExistingSourceHandleToDelete(labels, new[] { "A1", "B1", "C1" }));
        Assert.All(new[] { "A1", "B1", "C1" }, handle => Assert.Equal(roles.Count,
            labels.Count(label => label.SourceHandle == handle)));
    }

    [Fact]
    public void Adapter_CopyPersistsCurrentFrameAndOwnAnnotationsBeforeReconcile()
    {
        var router = RoofUxSourceContractText.Read("src", "AcKrovy.AutoCAD", "Infrastructure", "RoofStructuralNativeEditService.cs");
        var remint = RoofUxSourceContractText.Member(router, "private static bool TryEnsureManualCloneIdentity",
            "private static bool TryConvertCloneToAttachedManual");
        Assert.Contains("RoofStructuralAttachedManualDataRules.CreateCopiedClone", remint);
        Assert.Contains("sourceManual != existing", remint);
        Assert.Contains("snapshot.TimberLines", remint);
        Assert.Contains("sourceHandle = source.EntityHandle", remint);
        Assert.Contains("RoofStructuralAttachedManualStore.WriteReplacingGenerated", remint);
        var appended = RoofUxSourceContractText.Member(router, "if (candidate.Manual)",
            "if (!expected.TryGetValue(candidate.Key, out var item)");
        Assert.Contains("IsSameDwgCopyOwnershipCommand(commandName)", appended);
        Assert.Contains("mirrorCloneAnnotations[candidate.Id] = cloneTimber", appended);
        Assert.Contains("DeleteStructuralMirrorCloneAnnotations", appended);
        Assert.Contains("ROOF_STRUCT_MANUAL_COPY_CLONE", appended);
        Assert.Contains("copySourcePreservation: true", router);
        Assert.True(router.IndexOf("TryEnsureManualCloneIdentity(", StringComparison.Ordinal) <
            router.IndexOf("TryReconcileInTransaction(", StringComparison.Ordinal));
    }

    private static RoofStructuralAttachedManualData Source(RoofStructuralRole role,
        RoofStructuralHeightMode mode, RoofStructuralManualPlacement frame) =>
        RoofStructuralAttachedManualDataRules.Create("2912", "444903c3d3884978a6b19bf97039cd50",
            new(role, 1, 4), RoofStructuralAttachedManualCreationKind.Mirror, 120, mode,
            mode == RoofStructuralHeightMode.Explicit ? 160 : null, frame).Data!;
    private static RoofStructuralManualPlacement Frame() => new(
        100, 200, 80, 3100, 3200, 1700, -1 / Math.Sqrt(2), 1 / Math.Sqrt(2), 0, 0, 0, 1, 160);
    private static RoofSegment3D Plan() => new(new(100, 200, 0), new(3100, 3200, 0));
    private static RoofSegment3D Translate(RoofSegment3D plan, double dx, double dy) => new(
        new(plan.Start.X + dx, plan.Start.Y + dy, 0), new(plan.End.X + dx, plan.End.Y + dy, 0));
    private static RoofPoint3D Reflect(RoofPoint3D point) => new(10000 - point.X, point.Y, point.Z);
    private static string Key(RoofStructuralAttachedManualData data) =>
        RoofStructuralAttachedManualIdentityRules.PhysicalKey(data.ManualIdentity);
    private static TimberElementLabelCandidate Label(string key, string handle, TimberMainAnnotationComponentRole role) =>
        new() { LabelKey = key, SourceHandle = handle, ElementId = "N1", ComponentRole = role };
    private static TimberElementLabelSelection Select(string handle, IReadOnlyList<TimberElementLabelCandidate> labels) =>
        TimberElementLabelMatchRules.SelectLabelForUpsert(handle, "N1", null, labels,
            currentElementOwnerCount: 3, previousElementOwnerCount: 0, allowElementIdFallback: false);
    private static RoofPoint3D[] Vertices(RoofStructuralAttachedManualData data)
    {
        Assert.True(RoofStructuralManualPlacementRules.TryBuildPrism(data.SourceRole, data.WidthMm,
            data.Placement!, out var body, out var failure), failure);
        return body!.ConvexHalves[0].SourcePrismVertices.ToArray();
    }
    private static void AssertPoint(RoofPoint3D expected, RoofPoint3D actual) =>
        Assert.InRange(actual.DistanceTo(expected), 0, 1e-6);
}
