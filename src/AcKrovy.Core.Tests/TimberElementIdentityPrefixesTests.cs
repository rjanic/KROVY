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
    public void GetPrefix_ReturnsStableTechnicalPrefix(TimberElementType type, string expectedPrefix)
    {
        Assert.Equal(expectedPrefix, TimberElementIdentityPrefixes.GetPrefix(type));
    }
}
