using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementIdentityPrefixesTests
{
    [Theory]
    [InlineData(TimberElementType.Rafter, "K")]
    [InlineData(TimberElementType.WallPlate, "P")]
    [InlineData(TimberElementType.Purlin, "V")]
    [InlineData(TimberElementType.Post, "S")]
    [InlineData(TimberElementType.CollarTie, "KL")]
    [InlineData(TimberElementType.Brace, "W")]
    [InlineData(TimberElementType.TieBeam, "VT")]
    [InlineData(TimberElementType.HipRafter, "NK")]
    [InlineData(TimberElementType.ValleyRafter, "UK")]
    public void GetPrefix_ReturnsStableTechnicalPrefix(TimberElementType type, string expectedPrefix)
    {
        Assert.Equal(expectedPrefix, TimberElementIdentityPrefixes.GetPrefix(type));
    }

    [Fact]
    public void BuiltInPrefixes_AreGloballyUniqueAndReserveStructuralPrefixes()
    {
        var prefixes = Enum.GetValues<TimberElementType>()
            .Where(type => type != TimberElementType.Custom)
            .Select(TimberElementIdentityPrefixes.GetPrefix)
            .ToArray();

        Assert.Equal(prefixes.Length, prefixes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(prefixes, prefix =>
            string.Equals(prefix, "HP", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("NK1", TimberElementIdentityRules.CreateElementId(TimberElementType.HipRafter, 1));
        Assert.Equal("UK1", TimberElementIdentityRules.CreateElementId(TimberElementType.ValleyRafter, 1));
        Assert.Throws<ArgumentException>(() => CustomElementDefinitionRules.Normalize(
            new CustomElementDefinition("a", "A", "nk")));
        Assert.Throws<ArgumentException>(() => CustomElementDefinitionRules.Normalize(
            new CustomElementDefinition("b", "B", "Uk")));
    }
}
