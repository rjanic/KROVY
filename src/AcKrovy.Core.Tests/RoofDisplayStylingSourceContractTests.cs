using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDisplayStylingSourceContractTests
{
    private static readonly string Service = RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofDisplayService.cs");

    [Fact]
    public void SixNonRidgeEdgesUseRedGeneralRoofLayer()
    {
        Assert.Contains("LayerName = \"KROV_STRECHA\"", Service);
        Assert.Contains("LayerColorIndex = 1", Service);
        Assert.Contains("isRidge ? RidgeLayerName : LayerName", Service);
    }

    [Fact]
    public void RidgeUsesDeterministicRedRidgeLayer()
    {
        Assert.Contains("RidgeLayerName = \"KROV_STRECHA_HREBEN\"", Service);
        Assert.Contains("RidgeLayerColorIndex = 1", Service);
        Assert.Contains("role == RoofDisplayEdgeRole.Ridge", Service);
        Assert.Contains("isRidge ? RidgeLayerColorIndex : LayerColorIndex", Service);
    }

    [Fact]
    public void EveryDisplayLineRemainsByLayer()
    {
        Assert.Contains("ApplyToAnnotationEntity", Service);
        Assert.Contains("line.LinetypeId = database.ByLayerLinetype", Service);
        Assert.Contains("line.LineWeight = LineWeight.ByLayer", Service);
        Assert.DoesNotContain("line.ColorIndex =", Service);
        Assert.DoesNotContain("ColorMethod.ByAci", Service);
    }

    [Fact]
    public void InternalLayerNamesAreNotLocalized()
    {
        Assert.DoesNotContain("UiStrings", Service);
        Assert.DoesNotContain("AppLanguageService", Service);
    }
}
