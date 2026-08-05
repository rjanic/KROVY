using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationReadabilityRulesTests
{
    [Theory]
    [InlineData(0d, 0d, false)]
    [InlineData(35d * Math.PI / 180d, 35d * Math.PI / 180d, false)]
    [InlineData(90d * Math.PI / 180d, 90d * Math.PI / 180d, false)]
    [InlineData(135d * Math.PI / 180d, 135d * Math.PI / 180d - Math.PI, true)]
    [InlineData(Math.PI, 0d, true)]
    [InlineData(215d * Math.PI / 180d, 215d * Math.PI / 180d - Math.PI, true)]
    [InlineData(270d * Math.PI / 180d, 270d * Math.PI / 180d - Math.PI, true)]
    public void NormalizeReadableRotation_MatchesFullLabelContract(
        double raw,
        double expectedReadable,
        bool expectedFlip)
    {
        var readable =
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(raw);
        Assert.Equal(expectedReadable, readable, 12);
        Assert.Equal(
            expectedFlip,
            TimberAnnotationReadabilityRules.IsReadabilityFlipped(raw));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NormalizeReadableRotation_RejectsNonFinite(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberAnnotationReadabilityRules.NormalizeReadableRotationRadians(value));
}
