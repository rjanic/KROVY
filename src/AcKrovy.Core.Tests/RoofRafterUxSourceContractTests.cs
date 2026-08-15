using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofRafterUxSourceContractTests
{
    private static readonly string Workflow = Read("Infrastructure", "RoofRafterCommandWorkflow.cs");
    private static readonly string Ribbon = Read("Ribbon", "AcKrovyRibbon.cs");
    private static readonly string Catalog = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Localization", "CommandUiCatalog.cs");
    private static readonly string WindowXaml = Read("UI", "RoofRafterWindow.xaml");
    private static readonly string WindowCode = Read("UI", "RoofRafterWindow.xaml.cs");
    private static readonly string Preferences = Read("Settings", "SettingsUiPreferencesStore.cs");

    [Fact]
    public void RoofRibbonUsesOneNativeDropdownAndSeparateRafterCommandButton()
    {
        Assert.Contains("private static RibbonSplitButton RoofTypeDropDown()", Ribbon);
        Assert.Contains("Button(CommandUiCatalog.RoofRafters)", Ribbon);
        Assert.Contains("AcKrovyCommandNames.RoofRafters", Catalog);
        Assert.Equal(AcKrovyCommandNames.Roof, CommandUiCatalog.Roof.CommandName);
        Assert.Equal(AcKrovyCommandNames.RoofRafters, CommandUiCatalog.RoofRafters.CommandName);
    }

    [Fact]
    public void OnlyGableItemIsEnabledAndFutureTypesHaveNoProductionCommand()
    {
        Assert.Contains("split.Items.Add(Button(CommandUiCatalog.Roof", Ribbon);
        Assert.Contains("DisabledButton(CommandUiCatalog.RoofHip)", Ribbon);
        Assert.Contains("DisabledButton(CommandUiCatalog.RoofHalfHip)", Ribbon);
        Assert.Contains("DisabledButton(CommandUiCatalog.RoofMonoPitch)", Ribbon);
        Assert.Contains("button.IsEnabled = false", Ribbon);
        Assert.Equal(string.Empty, CommandUiCatalog.RoofHip.CommandName);
        Assert.Equal(string.Empty, CommandUiCatalog.RoofHalfHip.CommandName);
        Assert.Equal(string.Empty, CommandUiCatalog.RoofMonoPitch.CommandName);
        Assert.DoesNotContain("AK_ROOF_HIP", Catalog);
        Assert.DoesNotContain("AK_ROOF_HALFHIP", Catalog);
        Assert.DoesNotContain("AK_ROOF_MONOPITCH", Catalog);
    }

    [Fact]
    public void RibbonAndDialogUseLocalizedResourceKeysOnly()
    {
        Assert.Contains("CommandUi_RoofMenu_Label", Catalog);
        Assert.Contains("CommandUi_RoofGable_Label", Catalog);
        Assert.Contains("CommandUi_RoofRafters_Label", Catalog);
        Assert.Contains("{localization:Loc RoofRafterWindow_", WindowXaml);
        Assert.DoesNotContain("Šírka", WindowXaml);
        Assert.DoesNotContain("Krokvy", WindowXaml);
    }

    [Fact]
    public void DialogIsDrawingNeutralAndWpfCreateIsTheOnlyConfirmation()
    {
        var dialogPrefix = Workflow[..Workflow.IndexOf("TryCreateRafters(", StringComparison.Ordinal)];
        Assert.Contains("new RoofRafterWindow(", dialogPrefix);
        Assert.Contains("SettingsWindowOwner.TryAssign", dialogPrefix);
        Assert.Contains("AcApp.ShowModalWindow(dialog)", dialogPrefix);
        Assert.DoesNotContain("OpenMode.ForWrite", dialogPrefix);
        Assert.DoesNotContain("AppendEntity", dialogPrefix);
        Assert.DoesNotContain("ConfirmYesNo", Workflow);
        Assert.DoesNotContain("ShowRafters", Workflow);
        Assert.DoesNotContain("Autodesk.", WindowCode);
    }

    [Fact]
    public void PreferencesUseExistingUiStoreAndSaveOnlyAfterSuccessfulCreation()
    {
        Assert.Contains("AutomaticRafterPreferences", Preferences);
        Assert.Contains("LocalSettingsPaths.UiPreferences", Preferences);
        Assert.Contains("if (result.IsSuccess)", Workflow);
        var success = Workflow.IndexOf("if (result.IsSuccess)", StringComparison.Ordinal);
        Assert.True(Workflow.IndexOf("SettingsUiPreferencesStore.Save", success, StringComparison.Ordinal) > success);
        Assert.DoesNotContain("RoofDefinitionStore.Write", Workflow);
        Assert.DoesNotContain("RoofGeneratedTimberStore.Write", WindowCode + Preferences);
    }

    [Fact]
    public void IconsUseExistingPersistentPngPipelineAtBothRibbonSizes()
    {
        var keys = new[] { "roof", "roof_gable", "roof_hip", "roof_halfhip", "roof_monopitch", "roof_rafters" };
        foreach (var key in keys)
        {
            AssertPngDimensions(IconPath(key, 16), 16, 16);
            AssertPngDimensions(IconPath(key, 32), 32, 32);
        }
        Assert.Contains("RibbonIconProvider.Get(parent.IconKey, 32)", Ribbon);
        Assert.Contains("RibbonIconProvider.Get(parent.IconKey, 16)", Ribbon);
    }

    private static string Read(params string[] path) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", path[0], path[1]);

    private static string IconPath(string key, int size)
    {
        var root = RepositoryRoot();
        return Path.Combine(root, "src", "AcKrovy.AutoCAD", "Resources", "Icons", $"ak_{key}_{size}.png");
    }

    private static void AssertPngDimensions(string path, int expectedWidth, int expectedHeight)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 24);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        Assert.Equal(expectedWidth, ReadBigEndian(bytes, 16));
        Assert.Equal(expectedHeight, ReadBigEndian(bytes, 20));
    }

    private static int ReadBigEndian(byte[] bytes, int offset) =>
        (bytes[offset] << 24) |
        (bytes[offset + 1] << 16) |
        (bytes[offset + 2] << 8) |
        bytes[offset + 3];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
