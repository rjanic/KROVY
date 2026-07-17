using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementLabelsTests
{
    [Theory]
    [InlineData(TimberElementType.Rafter, "K")]
    [InlineData(TimberElementType.WallPlate, "P")]
    [InlineData(TimberElementType.Purlin, "V")]
    [InlineData(TimberElementType.Post, "S")]
    [InlineData(TimberElementType.CollarTie, "KL")]
    [InlineData(TimberElementType.Brace, "W")]
    [InlineData(TimberElementType.TieBeam, "VT")]
    public void Prefix_ReturnsCurrentPrefix(TimberElementType type, string expectedPrefix)
    {
        Assert.Equal(expectedPrefix, TimberElementLabels.Prefix(type));
    }

    [Theory]
    [InlineData(TimberElementType.Rafter, "Krokva")]
    [InlineData(TimberElementType.WallPlate, "Pomurnica")]
    [InlineData(TimberElementType.Purlin, "Vaznica")]
    [InlineData(TimberElementType.Post, "Stlpik")]
    [InlineData(TimberElementType.CollarTie, "Kliestina / hambalok")]
    [InlineData(TimberElementType.Brace, "Vzpera")]
    [InlineData(TimberElementType.TieBeam, "Vazny tram")]
    public void ToSlovak_ReturnsNonEmptyLabel(TimberElementType type, string expectedWithoutDiacritics)
    {
        var label = TimberElementLabels.ToSlovak(type);

        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.Equal(expectedWithoutDiacritics, RemoveDiacritics(label));
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) !=
                System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(chars).Normalize(System.Text.NormalizationForm.FormC);
    }
}
