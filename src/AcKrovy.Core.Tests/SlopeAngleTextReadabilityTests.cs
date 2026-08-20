using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class SlopeAngleTextReadabilityTests
{
    private static readonly string SlopeTextService = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "SlopeAngleTextService.cs");
    private static readonly string RafterWorkflow = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofRafterCommandWorkflow.cs");
    private static readonly string RafterReplacement = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofGeneratedRafterSetService.cs");
    private static readonly string ArrowRenderer = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "SlopeArrowService.cs");

    [Theory]
    [InlineData(90d)]
    [InlineData(-90d)]
    [InlineData(270d)]
    public void OpposingGableFaces_UseBottomToTopSlopeText(double sourceDegrees)
    {
        var rotation = TextRotation(sourceDegrees);

        Assert.Equal(Math.PI / 2d, rotation, 10);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(1d, 1d)]
    [InlineData(179d, -1d)]
    [InlineData(180d, 0d)]
    [InlineData(359d, -1d)]
    public void HorizontalAndNearHorizontalText_ReadsLeftToRight(
        double sourceDegrees,
        double expectedDegrees)
    {
        Assert.Equal(expectedDegrees, ToDegrees(TextRotation(sourceDegrees)), 10);
    }

    [Theory]
    [InlineData(15d)]
    [InlineData(46d)]
    [InlineData(89d)]
    [InlineData(90d)]
    [InlineData(135d)]
    [InlineData(270d)]
    [InlineData(315d)]
    public void OppositeSourceDirections_HaveSameReadableTextPresentation(
        double sourceDegrees)
    {
        Assert.Equal(
            TextRotation(sourceDegrees),
            TextRotation(sourceDegrees + 180d),
            10);
    }

    [Fact]
    public void MoveRefresh_DoesNotChangeReadableTextOrientation()
    {
        const double beforeMoveAxisDegrees = -90d;
        const double afterMoveAxisDegrees = -90d;

        Assert.Equal(
            TextRotation(beforeMoveAxisDegrees),
            TextRotation(afterMoveAxisDegrees),
            12);
        Assert.Equal(Math.PI / 2d, TextRotation(afterMoveAxisDegrees), 12);
    }

    [Theory]
    [InlineData(-90d, 37d)]
    [InlineData(90d, 37d)]
    [InlineData(170d, 25d)]
    public void RotateRefresh_FollowsSourceThenNormalizesReadable(
        double sourceDegrees,
        double rotationDegrees)
    {
        var rotated = TextRotation(sourceDegrees + rotationDegrees);
        var oppositeFace = TextRotation(sourceDegrees + rotationDegrees + 180d);

        Assert.Equal(rotated, oppositeFace, 10);
        Assert.InRange(rotated, -Math.PI / 2d, Math.PI / 2d);
    }

    [Fact]
    public void ArrowDirection_RemainsRidgeToEaveAndIndependentFromTextRotation()
    {
        var face0 = TimberSlopeArrowCalculator.Calculate(
            0d, -4000d, 0d, 0d, 0d, -2000d, isReversed: true);
        var face1 = TimberSlopeArrowCalculator.Calculate(
            0d, 4000d, 0d, 0d, 0d, 2000d, isReversed: true);

        Assert.True(face0.TipY < face0.TailY);
        Assert.True(face1.TipY > face1.TailY);
        Assert.Equal(TextRotation(90d), TextRotation(-90d), 12);
        Assert.Contains("IsSlopeDirectionReversed = true", RafterReplacement);
        Assert.Contains("RoofGeneratedRafterSetService.Materialize(", RafterWorkflow);
        Assert.DoesNotContain("RoofGeneratedTimber", ArrowRenderer);
    }

    [Fact]
    public void SlopeValue_IsUnchangedByTextOrientation()
    {
        const double slopeDegrees = 46d;
        var first = TimberSlopeAngleFormatter.Format(
            slopeDegrees,
            CultureInfo.InvariantCulture);
        _ = TextRotation(90d);
        var second = TimberSlopeAngleFormatter.Format(
            slopeDegrees,
            CultureInfo.InvariantCulture);

        Assert.Equal("46°", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ProductionSlopeText_ReusesDimensionTextOrientationRule()
    {
        Assert.Contains(
            "TimberStandaloneNativeLeaderOrientationRules\n" +
            "                .ResolveTextPresentationRadians(placement!.RotationRadians)",
            Normalize(SlopeTextService));
        Assert.Contains("postGeometry?.RotationRadians ??", SlopeTextService);
        Assert.DoesNotContain("RoofGeneratedTimber", SlopeTextService);
        Assert.DoesNotContain("RafterRoofFace", SlopeTextService);
    }

    [Fact]
    public void FixDoesNotChangeSchemasOrAddReactors()
    {
        Assert.Equal(7, TimberElementDataSchema.CurrentVersion);
        Assert.Equal(3, RoofDefinitionDataSchema.CurrentVersion);
        Assert.Equal(1, RoofDisplayDataSchema.CurrentVersion);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
        Assert.DoesNotContain("ObjectModified", SlopeTextService);
        Assert.DoesNotContain("CommandEnded", SlopeTextService);
    }

    private static double TextRotation(double sourceDegrees) =>
        TimberStandaloneNativeLeaderOrientationRules
            .ResolveTextPresentationRadians(sourceDegrees * Math.PI / 180d);

    private static double ToDegrees(double radians) => radians * 180d / Math.PI;

    private static string Normalize(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal);
}
