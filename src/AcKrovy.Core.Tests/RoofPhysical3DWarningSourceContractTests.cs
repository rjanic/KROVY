using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofPhysical3DWarningSourceContractTests
{
    [Fact]
    public void PhysicalWarningIsShownOnceAfterWholeCommandAggregation()
    {
        var service = ReadAutoCad("RoofOrdinaryLogicalMoveService.cs");
        Assert.Contains("if (physicalRejected)", service);
        Assert.Contains("RoofPhysical3DWarningService.Show()", service);
        Assert.Contains("several ObjectModified events", service);
        Assert.Contains("physicalWarningCount=", service);
        Assert.Contains("planChanged=", service);
        Assert.Contains("solidChanged=", service);
    }

    [Fact]
    public void WarningAndDetachDialogsReuseWoodDialogShell()
    {
        var warning = ReadAutoCad("UI/RoofPhysical3DWarningWindow.cs");
        var detach = ReadAutoCad("UI/RoofIndependentOrdinaryDetachWindow.cs");
        Assert.Contains(": WoodDialogWindow", warning);
        Assert.Contains(": WoodDialogWindow", detach);
        Assert.Contains("ApplyWoodButtonStyle", warning);
        Assert.Contains("ApplyWoodButtonStyle", detach);
        Assert.Contains("SettingsPanelBackgroundBrush", warning + detach);
        Assert.Contains("WoodOakDarkBrush", warning + detach);
        Assert.DoesNotContain("MessageBox", warning + detach);
        var warningService = ReadAutoCad("UI/RoofPhysical3DWarningService.cs");
        var editService = ReadAutoCad("RoofGeneratedMemberManualEditService.cs");
        Assert.Contains("MemberWarningPreferenceService.WarnDerived3DEdit", warningService);
        Assert.Contains("MemberWarningPreferenceService.ConfirmAutomaticDetach", editService);
        Assert.Contains("SettingsWindowOwner.TryAssign", ReadAutoCad("UI/MemberWarningPreferenceService.cs"));
    }

    private static string ReadAutoCad(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            directory = directory.Parent;
        var path = relativePath.StartsWith("UI/", StringComparison.Ordinal)
            ? Path.Combine(directory!.FullName, "src", "AcKrovy.AutoCAD", relativePath)
            : Path.Combine(directory!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", relativePath);
        return File.ReadAllText(path);
    }
}
