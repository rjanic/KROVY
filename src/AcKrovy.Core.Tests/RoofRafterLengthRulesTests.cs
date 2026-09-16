using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRafterLengthRulesTests
{
    [Fact]
    public void MissingSettingsResolveDefaultMinimumLengthTo500()
    {
        var settings = RoofRafterSpacingRules.Resolve(false, null);

        Assert.Equal(
            RoofRafterLengthRules.DefaultMinimumAutomaticLengthMm,
            settings.MinimumAutomaticLengthMm);
        Assert.Equal(500d, settings.MinimumAutomaticLengthMm);
    }

    [Fact]
    public void ValidCustomMinimumLengthIsPreservedWithoutClamping()
    {
        var stored = new RoofRafterSettings(900d, 500d, 800d);

        Assert.Same(stored, RoofRafterSpacingRules.Resolve(true, stored));
        Assert.Equal(800d, stored.MinimumAutomaticLengthMm);
    }

    [Fact]
    public void CreateDefaultRestoresMinimumLengthTo500()
    {
        Assert.Equal(
            500d,
            RoofRafterSettings.CreateDefault().MinimumAutomaticLengthMm);
    }

    [Theory]
    [InlineData(900d, 500d, 500d, true)]
    [InlineData(900d, 500d, 0d, false)]
    [InlineData(900d, 500d, -1d, false)]
    [InlineData(900d, 500d, double.NaN, false)]
    [InlineData(400d, 500d, 500d, false)]
    public void SettingsValidationIncludesMinimumLength(
        double defaultSpacing,
        double minimumSpacing,
        double minimumLength,
        bool expected)
    {
        Assert.Equal(
            expected,
            RoofRafterSpacingRules.IsValidSettings(
                defaultSpacing,
                minimumSpacing,
                minimumLength));
    }

    [Theory]
    [InlineData(499d, 500d, false)]
    [InlineData(500d, 500d, true)]
    [InlineData(500.0000000001d, 500d, true)]
    [InlineData(700d, 500d, true)]
    [InlineData(420d, 800d, false)]
    [InlineData(800d, 800d, true)]
    public void TrueLengthThresholdIsInclusiveAtBoundary(
        double trueLengthMm,
        double minimumLengthMm,
        bool expected)
    {
        Assert.Equal(
            expected,
            RoofRafterLengthRules.MeetsMinimumTrueLength(trueLengthMm, minimumLengthMm));
    }

    [Fact]
    public void OrdinaryLayoutSuppressesCandidatesBelowConfiguredTrueLength()
    {
        var geometry = SolveGable(10000d, 8000d, 30d);
        var baseline = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(900d, 80d, 1d)).Layout!;
        Assert.NotEmpty(baseline.Rafters);
        var threshold = baseline.Rafters.Min(rafter => rafter.TrueLengthMm);

        var below = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(900d, 80d, threshold + 1d)).Layout!;
        var atBoundary = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(900d, 80d, threshold)).Layout!;
        var above = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(900d, 80d, Math.Max(1d, threshold - 1d))).Layout!;

        Assert.True(below.Rafters.Count < baseline.Rafters.Count);
        Assert.Equal(baseline.Rafters.Count, atBoundary.Rafters.Count);
        Assert.Equal(baseline.Rafters.Count, above.Rafters.Count);
        Assert.All(
            atBoundary.Rafters,
            rafter => Assert.True(
                RoofRafterLengthRules.MeetsMinimumTrueLength(rafter.TrueLengthMm, threshold)));
    }

    [Fact]
    public void ResizeStyleResolvesCanSuppressThenRecreateOrdinaryRafters()
    {
        var shortGeometry = SolveGable(10000d, 2000d, 30d);
        var longGeometry = SolveGable(10000d, 8000d, 30d);
        const double threshold = 3500d;

        var afterShrink = RoofRafterLayoutSolver.Solve(
            shortGeometry,
            new RafterLayoutParameters(900d, 80d, threshold)).Layout!;
        var afterGrow = RoofRafterLayoutSolver.Solve(
            longGeometry,
            new RafterLayoutParameters(900d, 80d, threshold)).Layout!;

        Assert.Empty(afterShrink.Rafters);
        Assert.NotEmpty(afterGrow.Rafters);
        Assert.Equal(
            afterGrow.Rafters.Count,
            afterGrow.Rafters.Select(rafter => rafter.LogicalKey).Distinct().Count());
    }

    [Fact]
    public void MinimumLengthFilterDoesNotChangeStationPlanningMetrics()
    {
        var geometry = SolveGable(10000d, 8000d, 30d);
        var unfiltered = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(900d, 80d, 1d)).Layout!;
        var filtered = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(900d, 80d, 20000d)).Layout!;

        Assert.Equal(unfiltered.StationCount, filtered.StationCount);
        Assert.Equal(unfiltered.IntervalCount, filtered.IntervalCount);
        Assert.Equal(unfiltered.ActualSpacingMm, filtered.ActualSpacingMm);
        Assert.Equal(unfiltered.Signature, filtered.Signature);
        Assert.Empty(filtered.Rafters);
        Assert.NotEmpty(unfiltered.Rafters);
    }

    [Fact]
    public void HipAndValleyStructuralPlanningRemainOutsideOrdinaryLengthFilter()
    {
        var structural = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofAutomaticStructuralRafterPlanner.cs");
        var lengthRules = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofRafterLengthRules.cs");

        Assert.Contains("TrueLengthMm", lengthRules);
        Assert.DoesNotContain("HipRafter", lengthRules);
        Assert.DoesNotContain("ValleyRafter", lengthRules);
        Assert.DoesNotContain("MinimumAutomaticLength", structural);
        Assert.DoesNotContain("RoofRafterLengthRules", structural);
    }

    private static SimpleGableRoofGeometry SolveGable(
        double width,
        double depth,
        double slope)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [
                new(0d, 0d),
                new(width, 0d),
                new(width, depth),
                new(0d, depth),
            ],
            true));
        Assert.True(validation.IsValid, validation.Error.ToString());
        Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var direction));
        var result = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(slope, direction)));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Geometry!;
    }

    private static string Read(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new DirectoryNotFoundException();
        return File.ReadAllText(Path.Combine([root, .. path]));
    }
}
