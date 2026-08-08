using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// CREATE → source STRETCH / live-refresh orientation invariants (layer C).
/// Host CREATE keeps BR=0 + AttrRef after TransformBy; in-place refresh must
/// preserve that world presentation when the readable axis is unchanged.
/// </summary>
public sealed class TimberFramedCombinedG5SourceStretchOrientationRulesTests
{
    public static IEnumerable<object[]> StretchAngles()
    {
        foreach (var deg in TimberFramedCombinedG5SourceRotationRules
                     .StretchOrientationAnglesDegrees)
        {
            yield return [deg];
        }
    }

    [Theory]
    [MemberData(nameof(StretchAngles))]
    public void LengthOnlyStretch_SameReadable_PreservesLivePresentation(
        double sourceDeg)
    {
        var physical = sourceDeg * Math.PI / 180d;
        var readable =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
                physical);
        var createPresentation =
            TimberFramedBlockContentReadableOrientationRules
                .Decide(physical)
                .PresentationAngle;

        // CREATE TransformBy path: BR=0, AttrRef carries presentation.
        var liveFromCreate =
            TimberFramedBlockContentGripPresentationRules
                .ResolvePreservedPresentationRadians(
                    preGripBlockRotationRadians: 0d,
                    preGripItemAttributeRotationRadians: createPresentation);

        var after =
            TimberFramedCombinedG5SourceRotationRules
                .ResolveRefreshPresentationRadians(
                    oldRotationRadians: readable,
                    newRotationRadians: readable,
                    livePresentationRadians: liveFromCreate);

        Assert.True(
            TimberFramedBlockContentGripPresentationRules.PresentationPreserved(
                createPresentation,
                after),
            $"Length-only refresh must keep CREATE presentation at {sourceDeg}°.");
        Assert.Equal(createPresentation, after, 12);
    }

    [Theory]
    [MemberData(nameof(StretchAngles))]
    public void ReverseStartEnd_UndirectedAxisUnchanged_PreservesLivePresentation(
        double sourceDeg)
    {
        var physical = sourceDeg * Math.PI / 180d;
        var reversed = physical + Math.PI;
        var oldReadable =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
                physical);
        var newReadable =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
                reversed);
        Assert.False(
            TimberFramedCombinedG5SourceRotationRules.RotationChanged(
                oldReadable,
                newReadable),
            $"Reverse Start/End at {sourceDeg}° must keep undirected axis.");

        var createPresentation =
            TimberFramedBlockContentReadableOrientationRules
                .Decide(physical)
                .PresentationAngle;
        var after =
            TimberFramedCombinedG5SourceRotationRules
                .ResolveRefreshPresentationRadians(
                    oldReadable,
                    newReadable,
                    createPresentation);

        Assert.True(
            TimberFramedBlockContentGripPresentationRules.PresentationPreserved(
                createPresentation,
                after));
    }

    [Theory]
    [MemberData(nameof(StretchAngles))]
    public void TrueSourceAngleChange_MatchesFreshCreatePresentation(
        double oldDeg)
    {
        var newDeg = oldDeg + 35d;
        var oldPhysical = oldDeg * Math.PI / 180d;
        var newPhysical = newDeg * Math.PI / 180d;
        var oldReadable =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
                oldPhysical);
        var newReadable =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(
                newPhysical);
        Assert.True(
            TimberFramedCombinedG5SourceRotationRules.RotationChanged(
                oldReadable,
                newReadable));

        var liveOld =
            TimberFramedBlockContentReadableOrientationRules
                .Decide(oldPhysical)
                .PresentationAngle;
        var expectedFresh =
            TimberFramedBlockContentReadableOrientationRules
                .Decide(newPhysical)
                .PresentationAngle;

        var after =
            TimberFramedCombinedG5SourceRotationRules
                .ResolveRefreshPresentationRadians(
                    oldReadable,
                    newReadable,
                    liveOld);

        Assert.True(
            TimberFramedBlockContentGripPresentationRules.PresentationPreserved(
                expectedFresh,
                after),
            $"Refresh at {newDeg}° must match fresh CREATE presentation.");
    }

    [Theory]
    [MemberData(nameof(StretchAngles))]
    public void AttrRefWipeWithoutPreserve_WouldLoseCreateOrientation(
        double sourceDeg)
    {
        // Documents the defect class: CREATE BR=0+AttrRef, then Identity AttrRef
        // reapply with BR left at 0 → presentation collapses to 0.
        var physical = sourceDeg * Math.PI / 180d;
        var createPresentation =
            TimberFramedBlockContentReadableOrientationRules
                .Decide(physical)
                .PresentationAngle;
        if (Math.Abs(createPresentation) <= 1e-9d)
        {
            return;
        }

        var afterWipeWithoutPreserve =
            TimberFramedBlockContentGripPresentationRules
                .ResolvePreservedPresentationRadians(
                    preGripBlockRotationRadians: 0d,
                    preGripItemAttributeRotationRadians: 0d);
        Assert.False(
            TimberFramedBlockContentGripPresentationRules.PresentationPreserved(
                createPresentation,
                afterWipeWithoutPreserve),
            "Unpreserved AttrRef wipe must be detected as orientation loss.");

        var restored =
            TimberFramedCombinedG5SourceRotationRules
                .ResolveRefreshPresentationRadians(
                    oldRotationRadians: createPresentation,
                    newRotationRadians: createPresentation,
                    livePresentationRadians: createPresentation);
        Assert.True(
            TimberFramedBlockContentGripPresentationRules.PresentationPreserved(
                createPresentation,
                restored));
    }
}
