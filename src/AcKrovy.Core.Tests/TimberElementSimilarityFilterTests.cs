using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementSimilarityFilterTests
{
    [Fact]
    public void DefaultCriteria_MatchTypeCrossSectionMaterialAndCustomDefinitionOnlyForCustom()
    {
        var standard = Snapshot();
        var custom = Snapshot(TimberElementType.Custom) with
        {
            Data = Snapshot(TimberElementType.Custom).Data with
            {
                CustomElementTypeId = "custom-a",
                CustomElementTypeName = "Dormer beam",
                CustomElementTypePrefix = "DB",
            },
        };

        var standardCriteria = TimberElementSimilarityCriteria.CreateDefault(standard);
        var customCriteria = TimberElementSimilarityCriteria.CreateDefault(custom);

        Assert.True(standardCriteria.MatchElementType);
        Assert.True(standardCriteria.MatchCrossSection);
        Assert.True(standardCriteria.MatchMaterial);
        Assert.False(standardCriteria.MatchElementId);
        Assert.False(standardCriteria.MatchCuttingLength);
        Assert.False(standardCriteria.MatchCustomElementTypeId);
        Assert.True(customCriteria.MatchCustomElementTypeId);
        Assert.Equal(1d, customCriteria.CuttingLengthToleranceMm);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("material")]
    [InlineData("item")]
    [InlineData("custom")]
    public void EnabledCriterion_RejectsDifferentCandidate(string difference)
    {
        var seed = Snapshot(TimberElementType.Custom);
        var data = seed.Data with
        {
            ElementType = difference == "type" ? TimberElementType.Rafter : seed.Data.ElementType,
            WidthMm = difference == "width" ? 160 : seed.Data.WidthMm,
            HeightMm = difference == "height" ? 80 : seed.Data.HeightMm,
            Material = difference == "material" ? "BSH GL24h" : seed.Data.Material,
            ElementId = difference == "item" ? "X2" : seed.Data.ElementId,
            CustomElementTypeId = difference == "custom" ? "custom-b" : seed.Data.CustomElementTypeId,
        };
        var criteria = new TimberElementSimilarityCriteria
        {
            MatchElementType = difference == "type",
            MatchCrossSection = difference is "width" or "height",
            MatchMaterial = difference == "material",
            MatchElementId = difference == "item",
            MatchCustomElementTypeId = difference == "custom",
        };

        Assert.False(TimberElementSimilarityFilter.Matches(
            seed,
            new TimberElementSnapshot(data, seed.PlanLengthMm),
            criteria));
    }

    [Fact]
    public void CrossSection_DoesNotRotateWidthAndHeight()
    {
        var seed = Snapshot() with { Data = Snapshot().Data with { WidthMm = 80, HeightMm = 160 } };
        var rotated = seed with { Data = seed.Data with { WidthMm = 160, HeightMm = 80 } };

        Assert.False(TimberElementSimilarityFilter.Matches(
            seed,
            rotated,
            new TimberElementSimilarityCriteria
            {
                MatchElementType = false,
                MatchCrossSection = true,
                MatchMaterial = false,
            }));
    }

    [Fact]
    public void CanonicalMaterial_UsesExactInternalValue()
    {
        var seed = Snapshot();
        var differentCase = seed with
        {
            Data = seed.Data with { Material = seed.Data.Material.ToUpperInvariant() },
        };

        Assert.False(TimberElementSimilarityFilter.Matches(
            seed,
            differentCase,
            new TimberElementSimilarityCriteria
            {
                MatchElementType = false,
                MatchCrossSection = false,
                MatchMaterial = true,
            }));
    }

    [Theory]
    [InlineData(0, 5000, true)]
    [InlineData(0, 5001, false)]
    [InlineData(1, 5001, true)]
    [InlineData(1, 5002, false)]
    [InlineData(10, 5010, true)]
    public void CuttingLengthTolerance_IsInclusive(
        double tolerance,
        double candidateLength,
        bool expected)
    {
        var seed = Snapshot(planLengthMm: 5000);
        var candidate = Snapshot(planLengthMm: candidateLength);
        var criteria = new TimberElementSimilarityCriteria
        {
            MatchElementType = false,
            MatchCrossSection = false,
            MatchMaterial = false,
            MatchCuttingLength = true,
            CuttingLengthToleranceMm = tolerance,
        };

        Assert.Equal(expected, TimberElementSimilarityFilter.Matches(
            seed,
            candidate,
            criteria,
            roundingIncrementMm: 1));
    }

    [Fact]
    public void NegativeTolerance_IsRejected()
    {
        var criteria = new TimberElementSimilarityCriteria
        {
            CuttingLengthToleranceMm = -1,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberElementSimilarityFilter.Matches(Snapshot(), Snapshot(), criteria));
    }

    [Fact]
    public void AllCriteriaEnabled_AcceptIdenticalSnapshot()
    {
        var seed = Snapshot(TimberElementType.Custom);
        var criteria = new TimberElementSimilarityCriteria
        {
            MatchElementType = true,
            MatchCrossSection = true,
            MatchMaterial = true,
            MatchElementId = true,
            MatchCuttingLength = true,
            MatchCustomElementTypeId = true,
            CuttingLengthToleranceMm = 1,
        };

        Assert.True(TimberElementSimilarityFilter.Matches(
            seed,
            seed,
            criteria,
            roundingIncrementMm: 1));
    }

    [Fact]
    public void MissingOrInvalidSnapshot_DoesNotThrowAndDoesNotMatch()
    {
        var invalid = Snapshot() with
        {
            Data = Snapshot().Data with { WidthMm = double.NaN },
        };
        var missingMaterial = Snapshot() with
        {
            Data = Snapshot().Data with { Material = null! },
        };
        var invalidPlanLength = Snapshot(planLengthMm: double.PositiveInfinity);

        Assert.False(TimberElementSimilarityFilter.Matches(
            null,
            Snapshot(),
            new TimberElementSimilarityCriteria()));
        Assert.False(TimberElementSimilarityFilter.Matches(
            Snapshot(),
            invalid,
            new TimberElementSimilarityCriteria()));
        Assert.False(TimberElementSimilarityFilter.Matches(
            Snapshot(),
            missingMaterial,
            new TimberElementSimilarityCriteria()));
        Assert.False(TimberElementSimilarityFilter.Matches(
            Snapshot(),
            invalidPlanLength,
            new TimberElementSimilarityCriteria()));
    }

    private static TimberElementSnapshot Snapshot(
        TimberElementType type = TimberElementType.Rafter,
        double planLengthMm = 5000) =>
        new(
            new TimberElementData
            {
                SchemaVersion = TimberElementDataSchema.CurrentVersion,
                ElementId = type == TimberElementType.Custom ? "X1" : "K1",
                ElementType = type,
                CustomElementTypeId = type == TimberElementType.Custom ? "custom-a" : null,
                CustomElementTypeName = type == TimberElementType.Custom ? "Custom A" : null,
                CustomElementTypePrefix = type == TimberElementType.Custom ? "X" : null,
                WidthMm = 80,
                HeightMm = 160,
                Material = "Smrek C24",
                LengthCalculationMode = LengthCalculationMode.PlanLength,
                CuttingAllowanceMm = 0,
                SlopeDegrees = 0,
            },
            planLengthMm);
}
