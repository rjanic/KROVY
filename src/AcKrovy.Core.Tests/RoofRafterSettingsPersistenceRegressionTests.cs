using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRafterSettingsPersistenceRegressionTests
{
    [Fact]
    public void DefaultNineHundredFiveHundredFiveHundred_RoundtripsAndStaysConsistent()
    {
        var settings = new RoofRafterSettings(900d, 500d, 500d);
        Assert.True(RoofRafterSettingsPayload.TryDecode(
            RoofRafterSettingsPayload.SchemaVersion,
            RoofRafterSettingsPayload.EncodeReals(settings),
            out var restored));
        Assert.Equal(settings, restored);

        var geometry = SolveGable(10000d, 8000d, 30d);
        var layout = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(
                MaximumSpacingMm: restored!.DefaultAutomaticSpacingMm,
                RafterPlanWidthMm: 80d,
                MinimumAutomaticLengthMm: restored.MinimumAutomaticLengthMm)).Layout!;

        Assert.True(RoofRafterMaterializationRules.IsConsistent(geometry, layout));
    }

    [Fact]
    public void CustomMinimumLengthEightHundred_DoesNotMutateSpacingFields()
    {
        var encoded = RoofRafterSettingsPayload.EncodeReals(
            new RoofRafterSettings(900d, 500d, 800d));

        Assert.Equal(900d, encoded[0]);
        Assert.Equal(500d, encoded[1]);
        Assert.Equal(800d, encoded[2]);

        Assert.True(RoofRafterSettingsPayload.TryDecode(
            RoofRafterSettingsPayload.SchemaVersion,
            encoded,
            out var restored));
        Assert.Equal(900d, restored!.DefaultAutomaticSpacingMm);
        Assert.Equal(500d, restored.MinimumAutomaticSpacingMm);
        Assert.Equal(800d, restored.MinimumAutomaticLengthMm);

        var parameters = new RafterLayoutParameters(
            MaximumSpacingMm: restored.DefaultAutomaticSpacingMm,
            RafterPlanWidthMm: 80d,
            MinimumAutomaticLengthMm: restored.MinimumAutomaticLengthMm);
        Assert.Equal(900d, parameters.MaximumSpacingMm);
        Assert.Equal(80d, parameters.RafterPlanWidthMm);
        Assert.Equal(800d, parameters.MinimumAutomaticLengthMm);
    }

    [Fact]
    public void LegacyTwoRealPayload_DefaultsOnlyMinimumLengthTo500()
    {
        Assert.True(RoofRafterSettingsPayload.TryDecode(
            RoofRafterSettingsPayload.SchemaVersion,
            [775d, 450d],
            out var restored));

        Assert.Equal(775d, restored!.DefaultAutomaticSpacingMm);
        Assert.Equal(450d, restored.MinimumAutomaticSpacingMm);
        Assert.Equal(500d, restored.MinimumAutomaticLengthMm);
    }

    [Fact]
    public void FourRealPayload_PreservesAllFieldsExactly()
    {
        var source = new RoofRafterSettings(880d, 420d, 650d);
        Assert.True(RoofRafterSettingsPayload.TryDecode(
            RoofRafterSettingsPayload.SchemaVersion,
            RoofRafterSettingsPayload.EncodeReals(source),
            out var restored));
        Assert.Equal(source, restored);
    }

    [Fact]
    public void FilteredHipLayout_PassesIsConsistentForCreatePath()
    {
        var geometry = SolveHipL(30d);
        var layout = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(
                MaximumSpacingMm: 500d,
                RafterPlanWidthMm: 80d,
                MinimumAutomaticLengthMm: 500d)).Layout!;
        var unfiltered = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(
                MaximumSpacingMm: 500d,
                RafterPlanWidthMm: 80d,
                MinimumAutomaticLengthMm: 1d)).Layout!;

        Assert.True(layout.Rafters.Count < unfiltered.Rafters.Count);
        Assert.True(RoofRafterMaterializationRules.IsConsistent(geometry, layout));
        Assert.True(RoofRafterMaterializationRules.IsConsistent(geometry, unfiltered));
    }

    [Fact]
    public void SettingsApplySourceContract_MapsEachUiFieldToCorrectProperty()
    {
        var window = Read(
            "src", "AcKrovy.AutoCAD", "UI", "LayerSettingsWindow.xaml.cs");
        var xaml = Read(
            "src", "AcKrovy.AutoCAD", "UI", "LayerSettingsWindow.xaml");
        var store = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadRoofRafterSpacingStore.cs");
        var applyCtor = Segment(
            window,
            "new RoofRafterSettings(",
            "rafterSpacingChanged");

        Assert.Contains("DefaultAutomaticRafterSpacingMmText", xaml);
        Assert.Contains("MinimumAutomaticRafterSpacingMmText", xaml);
        Assert.Contains("MinimumAutomaticRafterLengthMmText", xaml);
        Assert.Contains("DefaultAutomaticSpacingMm: defaultAutomaticSpacingMm", applyCtor);
        Assert.Contains("MinimumAutomaticSpacingMm: minimumAutomaticSpacingMm", applyCtor);
        Assert.Contains("MinimumAutomaticLengthMm: minimumAutomaticLengthMm", applyCtor);
        Assert.True(
            applyCtor.IndexOf("DefaultAutomaticSpacingMm", StringComparison.Ordinal) <
            applyCtor.IndexOf("MinimumAutomaticSpacingMm", StringComparison.Ordinal));
        Assert.True(
            applyCtor.IndexOf("MinimumAutomaticSpacingMm", StringComparison.Ordinal) <
            applyCtor.IndexOf("MinimumAutomaticLengthMm", StringComparison.Ordinal));
        Assert.Contains("RoofRafterSettingsPayload.EncodeReals", store);
        Assert.Contains("RoofRafterSettingsPayload.TryDecode", store);
        Assert.Contains(
            "MinimumAutomaticLengthMm: settings.MinimumAutomaticLengthMm",
            store);
        Assert.Contains("MaximumSpacingMm: maximumSpacingMm", store);
    }

    private static string Segment(string source, string start, string end)
    {
        var first = source.IndexOf(start, StringComparison.Ordinal);
        var last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(first >= 0, "Missing start: " + start);
        Assert.True(last > first, "Missing end: " + end);
        return source.Substring(first, last - first);
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
