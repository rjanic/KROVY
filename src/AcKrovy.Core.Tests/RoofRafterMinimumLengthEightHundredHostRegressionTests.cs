using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Exact HOST-reported configuration: changing only MinimumAutomaticLengthMm must
/// never mutate spacing fields, and 900/500/800 must validate + materialize.
/// </summary>
public sealed class RoofRafterMinimumLengthEightHundredHostRegressionTests
{
    private static readonly string Root = FindRoot();

    [Theory]
    [InlineData(500d)]
    [InlineData(300d)]
    [InlineData(800d)]
    [InlineData(1200d)]
    public void NineHundredFiveHundredWithLength_RoundtripsWithoutMutatingSpacing(
        double minimumLengthMm)
    {
        var settings = new RoofRafterSettings(900d, 500d, minimumLengthMm);
        var encoded = RoofRafterSettingsPayload.EncodeReals(settings);

        Assert.Equal(900d, encoded[0]);
        Assert.Equal(500d, encoded[1]);
        Assert.Equal(minimumLengthMm, encoded[2]);

        Assert.True(RoofRafterSettingsPayload.TryDecode(
            RoofRafterSettingsPayload.SchemaVersion,
            encoded,
            out var restored));
        Assert.Equal(900d, restored!.DefaultAutomaticSpacingMm);
        Assert.Equal(500d, restored.MinimumAutomaticSpacingMm);
        Assert.Equal(minimumLengthMm, restored.MinimumAutomaticLengthMm);
        Assert.True(RoofRafterSpacingRules.IsValidSettings(
            restored.DefaultAutomaticSpacingMm,
            restored.MinimumAutomaticSpacingMm,
            restored.MinimumAutomaticLengthMm));
        Assert.True(RoofRafterSpacingRules.IsValidAutomaticWorkingSpacing(
            restored.DefaultAutomaticSpacingMm,
            restored.MinimumAutomaticSpacingMm));
    }

    [Fact]
    public void NineHundredFiveHundredEightHundred_CreateLayoutParametersKeepSpacingIntact()
    {
        var settings = new RoofRafterSettings(900d, 500d, 800d);
        var parameters = new RafterLayoutParameters(
            MaximumSpacingMm: settings.DefaultAutomaticSpacingMm,
            RafterPlanWidthMm: 80d,
            MinimumAutomaticLengthMm: settings.MinimumAutomaticLengthMm);

        Assert.Equal(900d, parameters.MaximumSpacingMm);
        Assert.Equal(80d, parameters.RafterPlanWidthMm);
        Assert.Equal(800d, parameters.MinimumAutomaticLengthMm);
    }

    [Theory]
    [InlineData(500d)]
    [InlineData(300d)]
    [InlineData(800d)]
    [InlineData(1200d)]
    public void NineHundredFiveHundredWithLength_ValidateAndIsConsistentPass(
        double minimumLengthMm)
    {
        var geometry = SolveGable(10000d, 8000d, 30d);
        var validation = RoofRafterRequestValidator.Validate(
            geometry,
            widthMm: 80d,
            heightMm: 160d,
            maximumSpacingMm: 900d,
            minimumAutomaticSpacingMm: 500d,
            material: "Smrek C24",
            minimumAutomaticLengthMm: minimumLengthMm);

        Assert.True(validation.IsValid, validation.Error.ToString());
        Assert.NotNull(validation.Layout);
        Assert.Equal(900d, validation.Request!.MaximumSpacingMm);
        Assert.True(RoofRafterMaterializationRules.IsConsistent(geometry, validation.Layout!));
    }

    [Fact]
    public void EightHundredFiltersShorterThanFiveHundred_WithoutSpacingMutation()
    {
        var geometry = SolveHipL(30d);
        var at500 = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(900d, 80d, 500d)).Layout!;
        var at800 = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(900d, 80d, 800d)).Layout!;

        Assert.Equal(900d, at500.RequestedMaximumSpacingMm);
        Assert.Equal(900d, at800.RequestedMaximumSpacingMm);
        Assert.True(at800.Rafters.Count <= at500.Rafters.Count);
        Assert.True(RoofRafterMaterializationRules.IsConsistent(geometry, at500));
        Assert.True(RoofRafterMaterializationRules.IsConsistent(geometry, at800));
        Assert.All(at800.Rafters, rafter =>
            Assert.True(RoofRafterLengthRules.MeetsMinimumTrueLength(rafter.TrueLengthMm, 800d)));
    }

    [Fact]
    public void CreateWorkflowSourceContract_PassesMinimumLengthIntoValidate()
    {
        var workflow = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofRafterCommandWorkflow.cs");
        var tryCreate = Segment(
            workflow,
            "private static RoofRafterCreationResult TryCreateRafters(",
            "private sealed record SelectedRoof(");

        Assert.Contains("rafterSettings.MinimumAutomaticLengthMm", workflow);
        Assert.Contains("double minimumAutomaticLengthMm", tryCreate);
        Assert.Contains("minimumAutomaticLengthMm)", tryCreate);
        Assert.Contains("ValidateHip(", tryCreate);
        Assert.Contains("minimumAutomaticLengthMm", tryCreate);
        Assert.Contains(
            "RoofRafterMaterializationRules.IsConsistent",
            tryCreate);
        Assert.True(
            tryCreate.LastIndexOf("Command_RoofRafters_GenerationFailed", StringComparison.Ordinal) >
            tryCreate.IndexOf("RoofRafterMaterializationRules.IsConsistent", StringComparison.Ordinal));
    }

    private static string Segment(string source, string start, string end)
    {
        var first = source.IndexOf(start, StringComparison.Ordinal);
        var last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(first >= 0, "Missing start: " + start);
        Assert.True(last > first, "Missing end: " + end);
        return source[first..last];
    }

    private static HipRoofGeometry SolveHipL(double slopeDegrees)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [
                new(0d, 0d),
                new(12000d, 0d),
                new(12000d, 6000d),
                new(6000d, 6000d),
                new(6000d, 12000d),
                new(0d, 12000d),
            ],
            true));
        Assert.True(validation.IsValid, validation.Error.ToString());
        var result = HipRoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(slopeDegrees),
            RoofKind.Hip));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<HipRoofGeometry>(result.Geometry);
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

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([Root, .. path]));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
