using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Source contracts: AK_ASSIGN/AK_EDIT cannot mutate ordinary generated-rafter
/// recipe fields; geometry override lifecycle remains on ManualEditService.
/// </summary>
public sealed class RoofGeneratedOrdinaryRafterAssignProtectionSourceContractTests
{
    private static readonly string Commands = Read(
        "Commands", "AcKrovyCommands.cs");
    private static readonly string Guard = Read(
        "Infrastructure", "RoofGeneratedOrdinaryRafterMetadataGuard.cs");
    private static readonly string ManualEdit = Read(
        "Infrastructure", "RoofGeneratedMemberManualEditService.cs");

    [Fact]
    public void AssignAndEdit_BlockRecipeDefiningWritesOnOrdinaryGeneratedRafters()
    {
        Assert.Contains("RoofGeneratedOrdinaryRafterMetadataGuard", Commands);
        Assert.Contains("IsOrdinaryGeneratedRafter(entity)", Commands);
        Assert.Contains("MutatesRecipeDefiningFields(", Commands);
        Assert.Contains("BlocksRecipeDefiningWrite(", Commands);
        Assert.Contains("Command_GeneratedRafter_UseRoofRafters", Guard + Commands);
        Assert.Contains("RoofGeneratedTimberKind.Rafter", Guard);
        Assert.DoesNotContain("RoofGeneratedTimberStore.Write", Commands);
    }

    [Fact]
    public void Assign_StillWritesNormalTimberWhenNotOrdinaryGeneratedRafter()
    {
        var assign = Segment(
            Commands,
            "[CommandMethod(AcKrovyCommandNames.Assign",
            "[CommandMethod(AcKrovyCommandNames.Renumber");
        Assert.Contains("metadataStore.Write(entity, merged);", assign);
        Assert.Contains("IsOrdinaryGeneratedRafter(entity)", assign);
        Assert.Contains("skipped++", assign);
    }

    [Fact]
    public void GeometryOverrideLifecycle_RemainsOnManualEditService()
    {
        Assert.Contains("RoofGeneratedMemberManualEditService", ManualEdit);
        Assert.DoesNotContain(
            "RoofGeneratedOrdinaryRafterMetadataGuard",
            ManualEdit);
        Assert.DoesNotContain(
            "Command_GeneratedRafter_UseRoofRafters",
            ManualEdit);
    }

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, start);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, end);
        return source[startIndex..endIndex];
    }

    private static string Read(string folder, string fileName) =>
        RoofUxSourceContractText.Read("src", "AcKrovy.AutoCAD", folder, fileName);
}
