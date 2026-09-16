using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRafterSettingsUiSourceContractTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void SettingsUsesManufacturingSectionAndWritesOnlyAfterApply()
    {
        var xaml = Read("src", "AcKrovy.AutoCAD", "UI", "LayerSettingsWindow.xaml");
        var window = Read("src", "AcKrovy.AutoCAD", "UI", "LayerSettingsWindow.xaml.cs");
        var commands = Read("src", "AcKrovy.AutoCAD", "Commands", "AcKrovyCommands.cs");

        Assert.Contains("SettingsWindow_Manufacturing_DefaultAutomaticRafterSpacing", xaml);
        Assert.Contains("SettingsWindow_Manufacturing_MinimumAutomaticRafterSpacing", xaml);
        Assert.Contains("SettingsWindow_Manufacturing_MinimumAutomaticRafterLength", xaml);
        Assert.Contains("DefaultAutomaticRafterSpacingMmText", xaml);
        Assert.Contains("MinimumAutomaticRafterSpacingMmText", xaml);
        Assert.Contains("MinimumAutomaticRafterLengthMmText", xaml);
        Assert.Contains("TryReadPositiveFiniteNumber", window);
        Assert.Contains("RoofRafterSpacingRules.IsValidSettings", window);
        Assert.Contains("RafterSpacingChanged", window);
        Assert.Contains("if (request.RafterSpacingChanged)", commands);
        Assert.Contains("AutoCadRoofRafterSpacingStore", commands);
        var open = Segment(commands, "public void OpenSettings()", "private static IntPtr");
        Assert.Contains("ReadRafterSpacingSettingsState", open);
        Assert.DoesNotContain(".Write(", open);
    }

    [Fact]
    public void AllSixLanguagePacksContainRafterSpacingKeys()
    {
        foreach (var name in new[]
        {
            "UiStrings.resx", "UiStrings.cs.resx", "UiStrings.en.resx",
            "UiStrings.de.resx", "UiStrings.pl.resx", "UiStrings.fr.resx",
        })
        {
            var resource = Read("src", "AcKrovy.Localization", "Resources", name);
            Assert.Contains("SettingsWindow_Manufacturing_DefaultAutomaticRafterSpacing", resource);
            Assert.Contains("SettingsWindow_Manufacturing_MinimumAutomaticRafterSpacing", resource);
            Assert.Contains("SettingsWindow_Manufacturing_MinimumAutomaticRafterLength", resource);
            Assert.Contains("RoofRafterWindow_InvalidAutomaticSpacingFormat", resource);
            Assert.Contains("Dialog_SettingsRafterSpacing", resource);
        }
    }

    [Fact]
    public void RoofRaftersLoadsDrawingDefaultAndValidatesWorkingSpacingAgainstMinimum()
    {
        var workflow = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofRafterCommandWorkflow.cs");
        var window = Read(
            "src", "AcKrovy.AutoCAD", "UI",
            "RoofRafterWindow.xaml.cs");
        var validator = Read(
            "src", "AcKrovy.Core", "Services", "Roofs",
            "RoofRafterRequestValidator.cs");
        var create = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofCommandWorkflow.cs");
        var edit = Read(
            "src", "AcKrovy.AutoCAD", "Infrastructure",
            "RoofEditCommandWorkflow.cs");

        Assert.Contains("AutoCadRoofRafterSpacingStore.ReadEffective", workflow);
        Assert.Contains(
            "MaximumSpacingMm = rafterSettings.DefaultAutomaticSpacingMm",
            workflow);
        Assert.Contains("rafterSettings.MinimumAutomaticSpacingMm", workflow);
        Assert.Contains("IsValidAutomaticWorkingSpacing", validator);
        Assert.Contains("RoofFaceRafterLayoutService.Create(hip.Topology, spacing)", window);
        Assert.DoesNotContain("AutoCadRoofRafterSpacingStore", create);
        Assert.DoesNotContain("AutoCadRoofRafterSpacingStore", edit);
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([Root, .. path]));

    private static string Segment(string source, string start, string end)
    {
        var first = source.IndexOf(start, StringComparison.Ordinal);
        var last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(first >= 0, "Missing start: " + start);
        Assert.True(last > first, "Missing end: " + end);
        return source.Substring(first, last - first);
    }

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
