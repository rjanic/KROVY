using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class ElementLayerProfileTests
{
    [Theory]
    [InlineData(TimberElementType.Rafter, "KROKVA", 2)]
    [InlineData(TimberElementType.WallPlate, "POMURNICA", 30)]
    [InlineData(TimberElementType.Purlin, "VAZNICA", 4)]
    [InlineData(TimberElementType.Post, "STLPIK", 3)]
    [InlineData(TimberElementType.CollarTie, "KLIESTINA", 5)]
    [InlineData(TimberElementType.Brace, "VZPERA", 1)]
    [InlineData(TimberElementType.TieBeam, "VAZNY_TRAM", 6)]
    public void CreateDefault_KeepsCurrentLayerNamesAndColors(
        TimberElementType type,
        string expectedLayerName,
        int expectedColorIndex)
    {
        var style = ElementLayerProfile.CreateDefault().GetStyle(type);

        Assert.Equal(expectedLayerName, style.LayerName);
        Assert.Equal(expectedColorIndex, style.ColorIndex);
    }

    [Fact]
    public void Normalize_TrimsStoredLayerName()
    {
        var profile = new ElementLayerProfile
        {
            Styles = new List<ElementLayerStyle>
            {
                new(TimberElementType.Rafter, "  KROV_KROKVA  ", 2),
            },
        };

        var normalized = profile.Normalize();

        Assert.Equal("KROV_KROKVA", normalized.GetStyle(TimberElementType.Rafter).LayerName);
    }

    [Fact]
    public void Normalize_UsesFallbackForMissingLayerName()
    {
        var profile = new ElementLayerProfile
        {
            Styles = new List<ElementLayerStyle>
            {
                new(TimberElementType.Rafter, "", 2),
            },
        };

        var normalized = profile.Normalize();

        Assert.Equal("KROKVA", normalized.GetStyle(TimberElementType.Rafter).LayerName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void Normalize_UsesFallbackForInvalidColorIndex(int invalidColorIndex)
    {
        var profile = new ElementLayerProfile
        {
            Styles = new List<ElementLayerStyle>
            {
                new(TimberElementType.Rafter, "KROV_KROKVA", invalidColorIndex),
            },
        };

        var normalized = profile.Normalize();

        Assert.Equal(2, normalized.GetStyle(TimberElementType.Rafter).ColorIndex);
    }
}
