using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofMirrorAnnotationConsumeRulesTests
{
    [Fact]
    public void ShouldErase_WhenLivingNonAppendedPeerExists_True()
    {
        Assert.True(RoofMirrorAnnotationConsumeRules.ShouldEraseAppendedAnnotationClone(true));
        Assert.Equal("mirrored-clone", RoofMirrorAnnotationConsumeRules.ClassifyAppendedAnnotation(true));
        Assert.Equal("erase", RoofMirrorAnnotationConsumeRules.ActionForAppendedAnnotation(true));
    }

    [Fact]
    public void ShouldErase_WhenSoleAppendedSurvivor_False()
    {
        // AutoCAD MIRROR recreate of a source MLeader: appended, but no living peer.
        Assert.False(RoofMirrorAnnotationConsumeRules.ShouldEraseAppendedAnnotationClone(false));
        Assert.Equal("source-original", RoofMirrorAnnotationConsumeRules.ClassifyAppendedAnnotation(false));
        Assert.Equal("keep", RoofMirrorAnnotationConsumeRules.ActionForAppendedAnnotation(false));
    }

    [Fact]
    public void FormatMainLabelRole_IncludesComponentRole()
    {
        Assert.Equal(
            RoofMirrorAnnotationConsumeRules.RoleMainLabelPrefix + "FramedItem",
            RoofMirrorAnnotationConsumeRules.FormatMainLabelRole("FramedItem"));
    }

    [Fact]
    public void ShouldKeepSoleAppendedSurvivor_PrefersLowestHandle()
    {
        Assert.True(RoofMirrorAnnotationConsumeRules.ShouldKeepSoleAppendedSurvivor(10, 10));
        Assert.False(RoofMirrorAnnotationConsumeRules.ShouldKeepSoleAppendedSurvivor(20, 10));
    }

    [Fact]
    public void OriginBaseline_ExcludesMirrorAppendedClones_195Not390()
    {
        // HOST: 195 real origin + 195 native MIRROR clones sharing SourceHandles.
        // Invariant baseline must remain 195, never 390.
        var timber = new[] { "2A31", "2A34", "2A35" };
        var candidates = new List<RoofMirrorOriginAnnotationCandidate>();
        var appendedKeys = new List<string>();
        for (var i = 0; i < 195; i++)
        {
            var source = timber[i % timber.Length];
            candidates.Add(new RoofMirrorOriginAnnotationCandidate
            {
                AnnotationKey = $"ORIG{i:D3}",
                SourceHandle = source,
            });
            candidates.Add(new RoofMirrorOriginAnnotationCandidate
            {
                AnnotationKey = $"CLONE{i:D3}",
                SourceHandle = source,
            });
            appendedKeys.Add($"CLONE{i:D3}");
        }

        var originKeys = RoofMirrorAnnotationConsumeRules.SelectOriginAnnotationKeysExcludingAppended(
            candidates,
            timber,
            appendedKeys);

        Assert.Equal(195, originKeys.Count);
        Assert.All(originKeys, key => Assert.StartsWith("ORIG", key, StringComparison.Ordinal));
        Assert.DoesNotContain(originKeys, key => key.StartsWith("CLONE", StringComparison.Ordinal));
    }

    [Fact]
    public void OriginBaseline_AfterCloneCleanup_Same195KeysRemain()
    {
        var timber = new[] { "2912-T1" };
        var origin = Enumerable.Range(0, 195)
            .Select(i => new RoofMirrorOriginAnnotationCandidate
            {
                AnnotationKey = $"A{i:D3}",
                SourceHandle = "2912-T1",
            })
            .ToList();
        var before = RoofMirrorAnnotationConsumeRules.SelectOriginAnnotationKeysExcludingAppended(
            origin.Concat(Enumerable.Range(0, 195).Select(i => new RoofMirrorOriginAnnotationCandidate
            {
                AnnotationKey = $"M{i:D3}",
                SourceHandle = "2912-T1",
            })).ToList(),
            timber,
            Enumerable.Range(0, 195).Select(i => $"M{i:D3}").ToArray());
        // Cleanup erased clones; after snapshot has only origin keys, no appended exclude needed.
        var after = RoofMirrorAnnotationConsumeRules.SelectOriginAnnotationKeysExcludingAppended(
            origin,
            timber,
            Array.Empty<string>());

        Assert.Equal(195, before.Count);
        Assert.Equal(195, after.Count);
        Assert.Equal(
            before.OrderBy(key => key, StringComparer.OrdinalIgnoreCase),
            after.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));
    }
}
