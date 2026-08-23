using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source-contract + resource coverage for the unique Ribbon icon cleanup:
/// AK_CUSTOM and AK_RENUMBER now have their own dedicated IconKeys (and PNG
/// assets) instead of sharing the assign / recalc icons. Proves the duplicate
/// mappings are resolved, the approved assets exist at 16/32, the assign/recalc
/// assets remain, and Ribbon + Classic Toolbar share the same descriptors.
/// Labels, commands, and localization are untouched.
/// </summary>
public sealed class CommandUiIconIdentitySourceContractTests
{
    private static readonly string Catalog = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Localization", "CommandUiCatalog.cs");

    [Fact]
    public void CustomUsesItsOwnDedicatedIconKey()
    {
        Assert.Equal("custom", CommandUiCatalog.Custom.IconKey);
    }

    [Fact]
    public void AssignKeepsAssignIconKey()
    {
        Assert.Equal("assign", CommandUiCatalog.Assign.IconKey);
    }

    [Fact]
    public void CustomAndAssignNoLongerShareIconKey()
    {
        Assert.NotEqual(CommandUiCatalog.Custom.IconKey, CommandUiCatalog.Assign.IconKey);
    }

    [Fact]
    public void RenumberUsesItsOwnDedicatedIconKey()
    {
        Assert.Equal("renumber", CommandUiCatalog.Renumber.IconKey);
    }

    [Fact]
    public void RecalcKeepsRecalcIconKey()
    {
        Assert.Equal("recalc", CommandUiCatalog.Recalc.IconKey);
    }

    [Fact]
    public void RenumberAndRecalcNoLongerShareIconKey()
    {
        Assert.NotEqual(CommandUiCatalog.Renumber.IconKey, CommandUiCatalog.Recalc.IconKey);
    }

    [Fact]
    public void ApprovedCustomPngAssetsExistAtBothSizes()
    {
        AssertPngDimensions(IconPath("custom", 16), 16, 16);
        AssertPngDimensions(IconPath("custom", 32), 32, 32);
    }

    [Fact]
    public void ApprovedRenumberPngAssetsExistAtBothSizes()
    {
        AssertPngDimensions(IconPath("renumber", 16), 16, 16);
        AssertPngDimensions(IconPath("renumber", 32), 32, 32);
    }

    [Fact]
    public void ExistingAssignAndRecalcAssetsRemainPresent()
    {
        AssertPngDimensions(IconPath("assign", 16), 16, 16);
        AssertPngDimensions(IconPath("assign", 32), 32, 32);
        AssertPngDimensions(IconPath("recalc", 16), 16, 16);
        AssertPngDimensions(IconPath("recalc", 32), 32, 32);
    }

    [Fact]
    public void CatalogResolvesBothUniqueIconKeys()
    {
        Assert.Contains("\"custom\"", Catalog);
        Assert.Contains("\"renumber\"", Catalog);
    }

    [Fact]
    public void RibbonAndClassicToolbarShareTheSameDescriptors()
    {
        // Both surfaces consume the same CommandUiDescriptor instances, so the
        // unique icon keys apply to Ribbon and Classic Toolbar alike.
        Assert.Contains(CommandUiCatalog.Custom, CommandUiCatalog.RibbonCommands);
        Assert.Contains(CommandUiCatalog.Renumber, CommandUiCatalog.RibbonCommands);
        Assert.Contains(CommandUiCatalog.Custom, CommandUiCatalog.ClassicToolbarCommands);
        Assert.Contains(CommandUiCatalog.Renumber, CommandUiCatalog.ClassicToolbarCommands);
    }

    [Fact]
    public void NoCommandCountOrLabelChange()
    {
        // Commands and labels remain unchanged — only IconKey identity differs.
        Assert.Equal(AcKrovyCommandNames.Custom, CommandUiCatalog.Custom.CommandName);
        Assert.Equal(AcKrovyCommandNames.Renumber, CommandUiCatalog.Renumber.CommandName);
        Assert.Equal("CommandUi_Custom_Label", CommandUiCatalog.Custom.LabelResourceKey);
        Assert.Equal("CommandUi_Renumber_Label", CommandUiCatalog.Renumber.LabelResourceKey);
    }

    private static string IconPath(string key, int size)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException();
        }

        return Path.Combine(
            directory.FullName,
            "src", "AcKrovy.AutoCAD", "Resources", "Icons",
            $"ak_{key}_{size}.png");
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
}
