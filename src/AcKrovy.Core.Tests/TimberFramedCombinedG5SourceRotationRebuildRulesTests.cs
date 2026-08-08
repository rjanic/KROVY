using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberFramedCombinedG5SourceRotationRebuildRulesTests
{
    [Theory]
    [InlineData(0d, 5d, 5d)]
    [InlineData(45d, 56d, 11d)]
    [InlineData(84d, 95d, 11d)]
    [InlineData(95d, 84d, -11d)]
    [InlineData(170d, 179d, 9d)]
    [InlineData(179d, -179d, 2d)]
    [InlineData(-84d, -95d, -11d)]
    [InlineData(-95d, -84d, 11d)]
    public void PhysicalSourceRotationMatrix_ReportsMeaningfulDeltaAndRecreatesOnce(
        double beforeDeg,
        double afterDeg,
        double expectedDeltaDeg)
    {
        var decision = Decide(beforeDeg, afterDeg);

        Assert.True(decision.SourceRotationDetected);
        Assert.True(decision.AnnotationRebuildRequired);
        Assert.Equal(
            expectedDeltaDeg,
            ToDegrees(decision.SourceAxisDeltaRadians),
            9);
        Assert.Equal(
            TimberFramedCombinedG5SourceRotationRebuildRules.SourceAxisChangedReason,
            decision.RebuildReason);
    }

    [Fact]
    public void HostCrossingReadableBoundary_UsesPhysicalMinusElevenDegreeDelta()
    {
        var decision =
            TimberFramedCombinedG5SourceRotationRebuildRules
                .DecideFromPersistedMetadata(
                    persistedSourcePhysicalAxisRadians: ToRadians(-84.3835d),
                    persistedPlacementReadableAxisRadians: ToRadians(-84.3835d),
                    currentSourcePhysicalAxisRadians: ToRadians(-95.4691d));

        Assert.True(decision.AnnotationRebuildRequired);
        Assert.Equal(-84.3835d, ToDegrees(decision.SourceAxisBeforeRadians), 4);
        Assert.Equal(-95.4691d, ToDegrees(decision.SourceAxisAfterRadians), 4);
        Assert.Equal(-11.0856d, ToDegrees(decision.SourceAxisDeltaRadians), 4);
    }

    [Fact]
    public void LegacyReadableMetadata_UnchangedPhysicalAxisIsAdoptedWithoutRebuild()
    {
        var decision =
            TimberFramedCombinedG5SourceRotationRebuildRules
                .DecideFromPersistedMetadata(
                    persistedSourcePhysicalAxisRadians: ToRadians(84.5309d),
                    persistedPlacementReadableAxisRadians: ToRadians(84.5309d),
                    currentSourcePhysicalAxisRadians: ToRadians(-95.4691d));

        Assert.False(decision.SourceRotationDetected);
        Assert.False(decision.AnnotationRebuildRequired);
        Assert.Equal(0d, ToDegrees(decision.SourceAxisDeltaRadians), 9);
    }

    [Fact]
    public void PersistedPhysicalAndReadableAngles_AreComparedLikeForLike()
    {
        var decision =
            TimberFramedCombinedG5SourceRotationRebuildRules
                .DecideFromPersistedMetadata(
                    persistedSourcePhysicalAxisRadians: ToRadians(-95d),
                    persistedPlacementReadableAxisRadians: ToRadians(85d),
                    currentSourcePhysicalAxisRadians: ToRadians(-84d));

        Assert.True(decision.AnnotationRebuildRequired);
        Assert.Equal(11d, ToDegrees(decision.SourceAxisDeltaRadians), 9);
    }

    [Fact]
    public void ExactPositiveToNegativeVertical_KeepsDirectedCreateFamilySemantics()
    {
        var decision =
            TimberFramedCombinedG5SourceRotationRebuildRules
                .DecideFromPersistedMetadata(
                    persistedSourcePhysicalAxisRadians: ToRadians(90d),
                    persistedPlacementReadableAxisRadians: ToRadians(90d),
                    currentSourcePhysicalAxisRadians: ToRadians(-90d));

        Assert.True(decision.AnnotationRebuildRequired);
        Assert.Equal(180d, ToDegrees(decision.SourceAxisDeltaRadians), 9);
    }

    [Fact]
    public void FullTurnBoundary_UsesShortestDirectedDelta()
    {
        var decision = Decide(359d, 1d);

        Assert.True(decision.AnnotationRebuildRequired);
        Assert.Equal(2d, ToDegrees(decision.SourceAxisDeltaRadians), 9);
    }

    [Fact]
    public void NumericalJitterWithinTolerance_DoesNotRecreate()
    {
        var tolerance =
            TimberFramedCombinedG5SourceRotationRules.RotationToleranceRadians;
        var decision =
            TimberFramedCombinedG5SourceRotationRebuildRules.Decide(
                35d * Math.PI / 180d,
                (35d * Math.PI / 180d) + (tolerance / 2d));

        Assert.False(decision.SourceRotationDetected);
        Assert.False(decision.AnnotationRebuildRequired);
        Assert.Equal(
            TimberFramedCombinedG5SourceRotationRebuildRules.SourceAxisUnchangedReason,
            decision.RebuildReason);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(45d)]
    [InlineData(90d)]
    [InlineData(-90d)]
    public void LengthStretchRefreshAndAnnotationGrip_DoNotRecreate(
        double sourceDeg)
    {
        var lengthStretch = Decide(sourceDeg, sourceDeg);
        var annotationGrip = Decide(sourceDeg, sourceDeg);

        Assert.False(lengthStretch.AnnotationRebuildRequired);
        Assert.False(annotationGrip.AnnotationRebuildRequired);
    }

    [Fact]
    public void RefreshTwiceAfterRebuild_KeepsNewFamilyUntilNextRotation()
    {
        Assert.True(Decide(0d, 10d).AnnotationRebuildRequired);
        Assert.False(Decide(10d, 10d).AnnotationRebuildRequired);
        Assert.False(Decide(10d, 10d).AnnotationRebuildRequired);
        Assert.True(Decide(10d, 20d).AnnotationRebuildRequired);
    }

    [Fact]
    public void BatchTwelveRotatedSources_ProducesTwelveIndependentRebuilds()
    {
        var sourceHandles = Enumerable.Range(1, 12)
            .Select(index => $"SOURCE-{index}")
            .ToArray();
        var decisions = sourceHandles.ToDictionary(
            handle => handle,
            _ => Decide(15d, 22.5d),
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal(12, decisions.Count);
        Assert.Equal(
            12,
            decisions.Values.Count(decision => decision.AnnotationRebuildRequired));
    }

    [Theory]
    [InlineData(85d, 90d, true)]
    [InlineData(90d, 100d, false)]
    [InlineData(-80d, -90d, true)]
    [InlineData(-90d, -80d, false)]
    public void VerticalTransitions_DelegateFinalStateToFreshCreate(
        double beforeDeg,
        double afterDeg,
        bool expectedWholeHalfTurn)
    {
        Assert.True(Decide(beforeDeg, afterDeg).AnnotationRebuildRequired);
        Assert.Equal(
            expectedWholeHalfTurn,
            TimberFramedBlockContentWholeAnnotationHalfTurnRules
                .RequiresWholeAnnotationHalfTurn(afterDeg * Math.PI / 180d));
    }

    [Fact]
    public void RebuildDecision_DoesNotDependOnCommandName()
    {
        var method = typeof(TimberFramedCombinedG5SourceRotationRebuildRules)
            .GetMethod(nameof(TimberFramedCombinedG5SourceRotationRebuildRules.Decide));

        Assert.NotNull(method);
        Assert.DoesNotContain(
            method!.GetParameters(),
            parameter => parameter.Name?.Contains("command", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static TimberFramedCombinedG5SourceRotationRebuildDecision Decide(
        double beforeDeg,
        double afterDeg) =>
        TimberFramedCombinedG5SourceRotationRebuildRules.Decide(
            beforeDeg * Math.PI / 180d,
            afterDeg * Math.PI / 180d);

    private static double ToDegrees(double radians) => radians * 180d / Math.PI;

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
