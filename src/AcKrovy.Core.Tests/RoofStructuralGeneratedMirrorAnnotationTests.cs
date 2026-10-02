using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Canonical annotation behavior and adapter wiring; not a live DWG annotation count.</summary>
public sealed class RoofStructuralGeneratedMirrorAnnotationTests
{
    [Fact]
    public void GeneratedMirror_QueuesConvertedCloneInWorkingCanonicalMirrorBatch()
    {
        var router = Read("RoofStructuralNativeEditService");
        var generated = RoofUxSourceContractText.Member(router,
            "// Appended structural clone carrying inherited Generated LogicalKey.",
            "if (action is not (RoofStructuralNativeAction.RejectClone or RoofStructuralNativeAction.RestorePlan");
        Assert.Contains("mirrorCloneAnnotations[candidate.Id] = cloneTimber", generated);
        var conversion = generated.IndexOf("TryConvertCloneToAttachedManual", StringComparison.Ordinal);
        var cleanup = generated.IndexOf("DeleteStructuralMirrorCloneAnnotations", StringComparison.Ordinal);
        var enqueue = generated.IndexOf("mirrorCloneAnnotations[candidate.Id]", StringComparison.Ordinal);
        Assert.True(conversion < cleanup && cleanup < enqueue);
        Assert.Contains("snapshotHandles.Contains", generated);
        Assert.Contains("candidate.Key", generated);
        Assert.Contains("metadata.TryRead(line, out var cloneTimber)", generated);
        Assert.Contains("RoofStructuralAttachedManualStore.Read(line)", generated);
        Assert.Contains("annotations=canonical result=prepared", generated);
        var ensure = router.IndexOf("document.Database, transaction, mirrorCloneAnnotations, defaultProfile", StringComparison.Ordinal);
        var physical = router.IndexOf("RoofStructuralRafterSolidMaterializationService.TryReconcileInTransaction", ensure, StringComparison.Ordinal);
        var group = router.IndexOf("TrySyncForOwner(document, transaction, ownerId)", physical, StringComparison.Ordinal);
        var commit = router.IndexOf("transaction.Commit()", group, StringComparison.Ordinal);
        Assert.True(ensure < physical && physical < group && group < commit);
        Assert.Contains("copySourcePreservation: true", router[ensure..physical]);
        Assert.Contains("sourceEntity.Handle.ToString()", Read("TimberAnnotationService"));
    }

    [Theory]
    [InlineData(TimberAnnotationMode.FullLabel, ItemNumberLeaderStyle.Plain, 3)]
    [InlineData(TimberAnnotationMode.DimensionsLeader, ItemNumberLeaderStyle.Plain, 3)]
    [InlineData(TimberAnnotationMode.DimensionsWithItemNumber, ItemNumberLeaderStyle.Plain, 4)]
    [InlineData(TimberAnnotationMode.DimensionsWithItemNumber, ItemNumberLeaderStyle.Rectangle, 3)]
    public void ConvertedGeneratedHip_CanonicalSetIncludesDimensionsAndMatchesOnlyCloneHandle(
        TimberAnnotationMode mode, ItemNumberLeaderStyle style, int expectedPartCount)
    {
        var inheritedTimber = TimberElementDefaults.For(TimberElementType.HipRafter) with
        {
            ElementId = "N1", WidthMm = 120, HeightMm = 160,
            SlopeDegrees = 30, AnnotationMode = mode, ItemNumberLeaderStyle = style,
        };
        var plan = TimberAnnotationRefreshPlanner.Create(inheritedTimber);
        Assert.True(plan.EnsureLabel);
        Assert.True(plan.ShouldSlopeArrowExist);
        Assert.True(plan.ShouldSlopeAngleTextExist);
        var roles = TimberCompositeAnnotationLifecycleRules.RequiredRoles(mode, style);
        Assert.Equal(expectedPartCount, roles.Count + 2);
        Assert.Equal("120x160", TimberElementLabelFormatter.FormatDimensions(inheritedTimber));
        var measurement = new TimberElementMeasurement(inheritedTimber, 3000, 3464, 3500, 0);
        Assert.Contains("120x160", TimberElementLabelFormatter.Format(inheritedTimber, measurement));
        Assert.Contains("3500 mm", TimberElementLabelFormatter.Format(inheritedTimber, measurement));

        // Both sources share the manufacturing item number. This must never authorize
        // adopting the Generated source's label for the newly converted Manual Plan.
        var sourceLabels = roles.Select(role => Label("source-" + role, "29F4", role)).ToArray();
        foreach (var role in roles)
        {
            var beforeCreation = Select("2A40", sourceLabels.Where(label => label.ComponentRole == role).ToArray());
            Assert.Null(beforeCreation.LabelKeyToUpdate);
            Assert.Empty(beforeCreation.LabelKeysToDelete);
        }

        var cloneLabels = roles.Select(role => Label("clone-" + role, "2A40", role)).ToArray();
        var complete = sourceLabels.Concat(cloneLabels).ToArray();
        Assert.Empty(TimberCompositeAnnotationLifecycleRules.SelectUnexpectedComponentKeys(mode, style, cloneLabels));
        Assert.Empty(TimberElementLabelCleanupRules.SelectLabelsWithoutExistingSourceHandleToDelete(
            complete, new[] { "29F4", "2A40" }));
        Assert.Empty(TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(
            complete, new[] { "29F4", "2A40" }));
        foreach (var role in roles)
        {
            var first = Select("2A40", complete.Where(label => label.ComponentRole == role).ToArray());
            var repeated = Select("2A40", complete.Where(label => label.ComponentRole == role).ToArray());
            Assert.Equal("clone-" + role, first.LabelKeyToUpdate);
            Assert.Equal(first.LabelKeyToUpdate, repeated.LabelKeyToUpdate);
            Assert.Empty(first.LabelKeysToDelete);
            Assert.Empty(repeated.LabelKeysToDelete);
            Assert.Single(cloneLabels, label => label.ComponentRole == role);
        }
    }

    [Theory]
    [InlineData(TimberElementType.HipRafter)]
    [InlineData(TimberElementType.ValleyRafter)]
    public void GeneratedAndLaterManualClones_KeepOneCompleteSetPerPlanHandleAfterDuplicateCleanup(TimberElementType type)
    {
        var data = TimberElementDefaults.For(type) with
        {
            ElementId = "N1", SlopeDegrees = 30, AnnotationMode = TimberAnnotationMode.DimensionsLeader,
        };
        var plan = TimberAnnotationRefreshPlanner.Create(data);
        Assert.True(plan.EnsureLabel && plan.ShouldSlopeArrowExist && plan.ShouldSlopeAngleTextExist);
        var roles = TimberCompositeAnnotationLifecycleRules.RequiredRoles(data.AnnotationMode, data.ItemNumberLeaderStyle);
        Assert.Single(roles);
        var source = Label("original", "29F4", roles.Single());
        var generatedToManual = Label("first-manual", "2A40", roles.Single());
        var manualToManual = Label("second-manual", "2A50", roles.Single());
        var candidates = new[]
        {
            source, generatedToManual, manualToManual,
            Label("native-old-binding", "29F4", roles.Single()),
            Label("repeated-first", "2A40", roles.Single()),
            Label("repeated-second", "2A50", roles.Single()),
        };
        var handles = new[] { "29F4", "2A40", "2A50" };
        var delete = TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(candidates, handles);
        Assert.Equal(new[] { "native-old-binding", "repeated-first", "repeated-second" }, delete);
        var canonical = candidates.Where(label => !delete.Contains(label.LabelKey)).ToArray();
        Assert.Equal(new[] { source, generatedToManual, manualToManual }, canonical);
        Assert.Empty(TimberElementLabelCleanupRules.SelectDuplicateLabelKeysToDelete(canonical, handles));
        Assert.Empty(TimberElementLabelCleanupRules.SelectLabelsWithoutExistingSourceHandleToDelete(canonical, handles));
        Assert.Equal("original", Select("29F4", canonical).LabelKeyToUpdate);
        Assert.Equal("first-manual", Select("2A40", canonical).LabelKeyToUpdate);
        Assert.Equal("second-manual", Select("2A50", canonical).LabelKeyToUpdate);
        // One dimension label plus arrow/angle per source: two independent Manual
        // sources require six planned entities, regardless of their creation origin.
        Assert.Equal(6, canonical.Count(label => label.SourceHandle != "29F4") * 3);
    }

    [Fact]
    public void ExplicitNoAnnotations_RemainsRespected()
    {
        var data = TimberElementDefaults.For(TimberElementType.HipRafter) with
        {
            AnnotationMode = TimberAnnotationMode.NoAnnotations,
        };
        var plan = TimberAnnotationRefreshPlanner.Create(data);
        Assert.False(plan.EnsureLabel);
        Assert.False(plan.ShouldSlopeArrowExist);
        Assert.False(plan.ShouldSlopeAngleTextExist);
    }

    private static TimberElementLabelCandidate Label(string key, string sourceHandle, TimberMainAnnotationComponentRole role) =>
        new() { LabelKey = key, SourceHandle = sourceHandle, ElementId = "N1", ComponentRole = role };

    private static TimberElementLabelSelection Select(string sourceHandle, IReadOnlyList<TimberElementLabelCandidate> labels) =>
        TimberElementLabelMatchRules.SelectLabelForUpsert(sourceHandle, "N1", null, labels,
            currentElementOwnerCount: 3, previousElementOwnerCount: 0, allowElementIdFallback: false);

    private static string Read(string name) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", name + ".cs");
}
