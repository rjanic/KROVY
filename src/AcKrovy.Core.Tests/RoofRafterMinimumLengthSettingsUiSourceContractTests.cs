using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRafterMinimumLengthSettingsUiSourceContractTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void SettingsPageSplitsAutomaticRaftersRoundingAndAllowances()
    {
        var xaml = Read("src", "AcKrovy.AutoCAD", "UI", "LayerSettingsWindow.xaml");
        var window = Read("src", "AcKrovy.AutoCAD", "UI", "LayerSettingsWindow.xaml.cs");

        Assert.Contains("SettingsWindow_Manufacturing_AutomaticRaftersSection", xaml);
        Assert.Contains("SettingsWindow_Manufacturing_LengthCalculationSection", xaml);
        Assert.Contains("SettingsWindow_Manufacturing_AllowancesSection", xaml);
        Assert.Contains("MinimumAutomaticRafterLengthMmText", xaml);
        Assert.Contains("SettingsWindow_Manufacturing_MinimumAutomaticRafterLengthHelp", xaml);
        Assert.Contains("MinimumAutomaticRafterLengthMmText", window);
        Assert.Contains("RoofRafterLengthRules.DefaultMinimumAutomaticLengthMm", window);
        Assert.Contains("MinimumAutomaticLengthMm", window);
    }

    [Fact]
    public void StoreKeepsSchemaOneAndAcceptsLegacyThreeRealPayload()
    {
        var store = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "AutoCadRoofRafterSpacingStore.cs");

        Assert.Contains("RoofRafterSettingsPayload.SchemaVersion", store);
        Assert.Contains("values.Count is not (3 or 4)", store);
        Assert.Contains("settings.MinimumAutomaticLengthMm", store);
        Assert.Contains("CreateLayoutParameters", store);
        Assert.Contains("RoofRafterSettingsPayload", store);
    }

    [Fact]
    public void LiveResizeAndGenerationHonorPersistedMinimumLength()
    {
        var replace = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofGeneratedRafterSetService.cs");
        var live = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofLiveResizeService.cs");
        var workflow = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofRafterCommandWorkflow.cs");
        var solver = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofRafterLayoutSolver.cs");

        Assert.Contains("CreateLayoutParameters", replace);
        Assert.Contains("CreateLayoutParameters", live);
        Assert.Contains("forceRegenerateOnSourceResize: true", live);
        Assert.Contains("\"STRETCH\"", Read(
            "src", "AcKrovy.Core", "Services", "LiveGeometryCommandRules.cs"));
        Assert.Contains("\"GRIP_STRETCH\"", Read(
            "src", "AcKrovy.Core", "Services", "LiveGeometryCommandRules.cs"));
        Assert.Contains("MinimumAutomaticLengthMm", workflow);
        Assert.Contains("FilterByMinimumTrueLength", solver);
        Assert.Contains("Finalize(", solver);
    }

    [Fact]
    public void AllSixLanguagePacksContainMinimumLengthKeys()
    {
        foreach (var name in new[]
        {
            "UiStrings.resx", "UiStrings.cs.resx", "UiStrings.en.resx",
            "UiStrings.de.resx", "UiStrings.pl.resx", "UiStrings.fr.resx",
        })
        {
            var resource = Read("src", "AcKrovy.Localization", "Resources", name);
            Assert.Contains("SettingsWindow_Manufacturing_AutomaticRaftersSection", resource);
            Assert.Contains("SettingsWindow_Manufacturing_MinimumAutomaticRafterLength", resource);
            Assert.Contains("SettingsWindow_Manufacturing_MinimumAutomaticRafterLengthHelp", resource);
            Assert.Contains("SettingsWindow_Manufacturing_LengthCalculationSection", resource);
            Assert.Contains("SettingsWindow_Manufacturing_AllowancesSection", resource);
        }
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
