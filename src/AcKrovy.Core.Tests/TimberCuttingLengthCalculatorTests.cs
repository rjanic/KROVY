using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberCuttingLengthCalculatorTests
{
    [Theory]
    [InlineData(4800, 0, 4800)]
    [InlineData(4801, 0, 4900)]
    [InlineData(4823, 100, 5000)]
    [InlineData(4900, 100, 5000)]
    [InlineData(4901, 100, 5100)]
    public void Calculate_AddsAllowanceAndRoundsUpToHundred(
        double measuredLengthMm,
        double allowanceMm,
        double expectedCuttingLengthMm)
    {
        var result = TimberCuttingLengthCalculator.Calculate(measuredLengthMm, allowanceMm);

        Assert.Equal(expectedCuttingLengthMm, result);
    }

    [Fact]
    public void Calculate_AllowsZeroAllowance()
    {
        var result = TimberCuttingLengthCalculator.Calculate(5100, 0);

        Assert.Equal(5100, result);
    }

    [Fact]
    public void Calculate_UsesCustomRoundingStepForFutureConfiguration()
    {
        var result = TimberCuttingLengthCalculator.Calculate(4823, 100, roundingStepMm: 50);

        Assert.Equal(4950, result);
    }

    [Fact]
    public void ResolveAllowance_NullOverrideUsesTypeDefault()
    {
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Purlin, 250),
            },
        };

        var result = TimberCuttingAllowanceResolver.Resolve(TimberElementType.Purlin, null, profile);

        Assert.Equal(250, result);
    }

    [Fact]
    public void ResolveAllowance_ZeroOverrideUsesZero()
    {
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Purlin, 250),
            },
        };

        var result = TimberCuttingAllowanceResolver.Resolve(TimberElementType.Purlin, 0, profile);

        Assert.Equal(0, result);
    }

    [Fact]
    public void ResolveAllowance_ExplicitOverrideWinsOverDefault()
    {
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Purlin, 250),
            },
        };

        var result = TimberCuttingAllowanceResolver.Resolve(TimberElementType.Purlin, 75, profile);

        Assert.Equal(75, result);
    }
}
