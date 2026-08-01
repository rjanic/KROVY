using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementDefaultProfileTests
{
    [Fact]
    public void CreateDefault_UsesFactoryCuttingAllowanceByType()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();

        Assert.Equal(100, profile.GetCuttingLengthRoundingStepMm());
        foreach (TimberElementType type in Enum.GetValues(typeof(TimberElementType)))
        {
            Assert.Equal(TimberElementDefaultProfile.GetFactoryCuttingAllowanceMm(type), profile.GetCuttingAllowanceMm(type));
        }
    }

    [Fact]
    public void Normalize_OldProfileWithoutRoundingStepUsesFactoryFallback()
    {
        var profile = new TimberElementDefaultProfile
        {
            CuttingLengthRoundingStepMm = 0,
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 150),
            },
        }.Normalize();

        Assert.Equal(100, profile.GetCuttingLengthRoundingStepMm());
        Assert.Equal(150, profile.GetCuttingAllowanceMm(TimberElementType.Rafter));
    }

    [Fact]
    public void Normalize_StoresPositiveIntegerRoundingStep()
    {
        var profile = new TimberElementDefaultProfile
        {
            CuttingLengthRoundingStepMm = 50,
        }.Normalize();

        Assert.Equal(50, profile.GetCuttingLengthRoundingStepMm());
    }

    [Fact]
    public void CreateDefault_UsesConservativePurlinAllowance()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();

        Assert.Equal(200, profile.GetCuttingAllowanceMm(TimberElementType.Purlin));
    }

    [Fact]
    public void CreateDefault_HasDefaultAnnotationScaleDenominator()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();

        Assert.Equal(
            TimberAnnotationScaleRules.DefaultDenominator,
            profile.AnnotationScaleDenominator);
    }

    [Fact]
    public void CreateDefault_UsesVersionTwoAndFactoryAnnotationTextSettings()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();

        Assert.Equal(TimberElementDefaultProfile.CurrentVersion, profile.Version);
        Assert.Equal(2, profile.Version);
        Assert.Equal(
            TimberAnnotationTextSettingsRules.Default,
            profile.DefaultAnnotationTextSettings);
    }

    [Fact]
    public void JsonDeserialize_LegacyVersionOneWithoutTextSettingsKeepsNull()
    {
        const string json = """
            {
              "version": 1,
              "annotationScaleDenominator": 50,
              "styles": []
            }
            """;

        var profile = Assert.IsType<TimberElementDefaultProfile>(
            JsonSerializer.Deserialize<TimberElementDefaultProfile>(
                json,
                JsonOptions)).Normalize();

        Assert.Equal(1, profile.Version);
        Assert.Null(profile.DefaultAnnotationTextSettings);
    }

    [Fact]
    public void Normalize_AnnotationTextSettingsFallsBackPerInvalidFieldWithoutClamping()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();
        profile.DefaultAnnotationTextSettings = new TimberAnnotationTextSettings(
            " ISOCP ",
            11d,
            3.2d,
            0.5d);

        var normalized = Assert.IsType<TimberAnnotationTextSettings>(
            profile.Normalize().DefaultAnnotationTextSettings);

        Assert.Equal("ISOCP", normalized.TextStyleName);
        Assert.Equal(2.5d, normalized.LabelAndDimensionPaperHeightMm);
        Assert.Equal(3.2d, normalized.ItemNumberPaperHeightMm);
        Assert.Equal(1.6d, normalized.SlopeAnglePaperHeightMm);
    }

    [Fact]
    public void JsonRoundtrip_VersionTwoPreservesAnnotationTextSettings()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();
        profile.DefaultAnnotationTextSettings = new TimberAnnotationTextSettings(
            "ISOCP",
            3d,
            3.1d,
            2d);

        var json = JsonSerializer.Serialize(profile.Normalize(), JsonOptions);
        var persisted = Assert.IsType<TimberElementDefaultProfile>(
            JsonSerializer.Deserialize<TimberElementDefaultProfile>(
                json,
                JsonOptions)).Normalize();

        Assert.Equal(2, persisted.Version);
        Assert.Equal(
            profile.DefaultAnnotationTextSettings,
            persisted.DefaultAnnotationTextSettings);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(250)]
    public void Normalize_KeepsValidAnnotationScaleDenominator(int denominator)
    {
        var profile = new TimberElementDefaultProfile
        {
            AnnotationScaleDenominator = denominator,
            Styles = TimberElementDefaultProfile.CreateDefault().Styles,
        };

        Assert.Equal(denominator, profile.Normalize().AnnotationScaleDenominator);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(251)]
    [InlineData(-5)]
    public void Normalize_InvalidAnnotationScaleDenominatorFallsBackToDefault(int denominator)
    {
        var profile = new TimberElementDefaultProfile
        {
            AnnotationScaleDenominator = denominator,
            Styles = TimberElementDefaultProfile.CreateDefault().Styles,
        };

        Assert.Equal(
            TimberAnnotationScaleRules.DefaultDenominator,
            profile.Normalize().AnnotationScaleDenominator);
    }

    [Fact]
    public void JsonDeserialize_OldProfileWithoutAnnotationScaleDenominatorDefaultsToDefault()
    {
        var profile = DeserializeProfileWithoutScaleDenominator();

        Assert.Equal(
            TimberAnnotationScaleRules.DefaultDenominator,
            profile.AnnotationScaleDenominator);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(250)]
    public void JsonDeserialize_ValidAnnotationScaleDenominatorIsPreserved(int denominator)
    {
        var profile = DeserializeProfileWithScaleDenominator(denominator);

        Assert.Equal(denominator, profile.AnnotationScaleDenominator);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(251)]
    [InlineData(-5)]
    public void JsonDeserialize_InvalidAnnotationScaleDenominatorNormalizesToDefault(int denominator)
    {
        var profile = DeserializeProfileWithScaleDenominator(denominator);

        Assert.Equal(
            TimberAnnotationScaleRules.DefaultDenominator,
            profile.AnnotationScaleDenominator);
    }

    [Fact]
    public void JsonRoundtrip_PreservesExistingFieldsAndAnnotationScaleDenominator()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();
        profile.AnnotationScaleDenominator = 75;

        var json = JsonSerializer.Serialize(profile.Normalize(), JsonOptions);
        var persisted = JsonSerializer.Deserialize<TimberElementDefaultProfile>(json, JsonOptions)!
            .Normalize();

        Assert.Equal(75, persisted.AnnotationScaleDenominator);
        Assert.Equal(profile.DefaultAnnotationMode, persisted.DefaultAnnotationMode);
        Assert.Equal(profile.DefaultItemNumberLeaderStyle, persisted.DefaultItemNumberLeaderStyle);
        Assert.Equal(profile.Styles.Count, persisted.Styles.Count);
    }

    [Fact]
    public void JsonSerialize_NormalizesInvalidAnnotationScaleDenominatorToDefault()
    {
        var profile = new TimberElementDefaultProfile
        {
            AnnotationScaleDenominator = 0,
            Styles = TimberElementDefaultProfile.CreateDefault().Styles,
        };

        var json = JsonSerializer.Serialize(profile.Normalize(), JsonOptions);

        Assert.Contains(
            $"\"annotationScaleDenominator\":{TimberAnnotationScaleRules.DefaultDenominator}",
            json,
            StringComparison.Ordinal);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static TimberElementDefaultProfile DeserializeProfileWithScaleDenominator(int denominator)
    {
        var profile = TimberElementDefaultProfile.CreateDefault();
        profile.AnnotationScaleDenominator = denominator;
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        return JsonSerializer.Deserialize<TimberElementDefaultProfile>(json, JsonOptions)!.Normalize();
    }

    private static TimberElementDefaultProfile DeserializeProfileWithoutScaleDenominator()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();
        var json = JsonSerializer.Serialize(profile, JsonOptions)
            .Replace(
                ",\"annotationScaleDenominator\":50",
                string.Empty,
                StringComparison.Ordinal);

        return JsonSerializer.Deserialize<TimberElementDefaultProfile>(json, JsonOptions)!.Normalize();
    }

    [Fact]
    public void CreateDefault_HasValidNonNegativeAllowanceForEveryType()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();

        foreach (TimberElementType type in Enum.GetValues(typeof(TimberElementType)))
        {
            var allowance = profile.GetCuttingAllowanceMm(type);
            Assert.InRange(allowance, 0, TimberElementDefaultProfile.MaxCuttingAllowanceMm);
        }
    }

    [Fact]
    public void GetCuttingAllowanceMm_AllowsDifferentValuesPerType()
    {
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 150),
                new(TimberElementType.Purlin, 200),
                new(TimberElementType.Post, 50),
            },
        };

        Assert.Equal(150, profile.GetCuttingAllowanceMm(TimberElementType.Rafter));
        Assert.Equal(200, profile.GetCuttingAllowanceMm(TimberElementType.Purlin));
        Assert.Equal(50, profile.GetCuttingAllowanceMm(TimberElementType.Post));
        Assert.Equal(TimberElementDefaultProfile.FactoryCuttingAllowanceMm, profile.GetCuttingAllowanceMm(TimberElementType.Brace));
    }

    [Fact]
    public void Normalize_ClampsNegativeCuttingAllowanceToZero()
    {
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, -25),
            },
        }.Normalize();

        Assert.Equal(0, profile.GetCuttingAllowanceMm(TimberElementType.Rafter));
    }

    [Fact]
    public void Normalize_ClampsExcessiveCuttingAllowanceToMaximum()
    {
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, TimberElementDefaultProfile.MaxCuttingAllowanceMm + 1),
            },
        }.Normalize();

        Assert.Equal(TimberElementDefaultProfile.MaxCuttingAllowanceMm, profile.GetCuttingAllowanceMm(TimberElementType.Rafter));
    }

    [Fact]
    public void TimberElementDefaults_UsesProfileCuttingAllowanceForNewData()
    {
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 150),
                new(TimberElementType.Brace, 75),
            },
        };

        var rafter = TimberElementDefaults.For(TimberElementType.Rafter, profile);
        var brace = TimberElementDefaults.For(TimberElementType.Brace, profile);

        Assert.Equal(150, rafter.CuttingAllowanceMm);
        Assert.Equal(75, brace.CuttingAllowanceMm);
    }

    [Fact]
    public void TimberElementDefaults_UsesSavedAnnotationModeStyleAndScale()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();
        profile.DefaultAnnotationMode = TimberAnnotationMode.DimensionsWithItemNumber;
        profile.DefaultItemNumberLeaderStyle = ItemNumberLeaderStyle.Rectangle;
        profile.AnnotationScaleDenominator = 25;
        profile.DefaultAnnotationTextSettings = new TimberAnnotationTextSettings(
            "ISOCP",
            3d,
            3.1d,
            2d);

        var element = TimberElementDefaults.For(TimberElementType.Rafter, profile);

        Assert.Equal(profile.DefaultAnnotationMode, element.AnnotationMode);
        Assert.Equal(profile.DefaultItemNumberLeaderStyle, element.ItemNumberLeaderStyle);
        Assert.Equal(25, element.AnnotationScaleDenominatorOverride);
        Assert.Equal(
            profile.DefaultAnnotationTextSettings,
            element.AnnotationTextSettings);
    }

    [Fact]
    public void TimberElementDefaults_LegacyNullTextSettingsRemainNull()
    {
        var legacyProfile = new TimberElementDefaultProfile
        {
            Version = 1,
            DefaultAnnotationTextSettings = null,
        };

        var element = TimberElementDefaults.For(
            TimberElementType.Rafter,
            legacyProfile);

        Assert.Null(element.AnnotationTextSettings);
    }

    [Fact]
    public void ChangedGlobalDefault_DoesNotMutateExistingElementData()
    {
        var existing = TimberElementDefaults.For(TimberElementType.Rafter, TimberElementDefaultProfile.CreateDefault());
        var changedProfile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 250),
            },
        };

        var newElement = TimberElementDefaults.For(TimberElementType.Rafter, changedProfile);

        Assert.Equal(100, existing.CuttingAllowanceMm);
        Assert.Equal(250, newElement.CuttingAllowanceMm);
    }

    [Fact]
    public void ApplyCuttingAllowance_UpdatesExistingElementFromCurrentTypeDefault()
    {
        var existing = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            ElementId = "K1",
            CuttingAllowanceMm = 80,
        };
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 180),
            },
        };

        var updated = TimberElementDefaultApplicator.ApplyCuttingAllowance(existing, profile);

        Assert.Equal(180, updated.CuttingAllowanceMm);
        Assert.Equal(existing.ElementId, updated.ElementId);
        Assert.Equal(existing.ElementType, updated.ElementType);
    }

    [Fact]
    public void ApplyCuttingAllowance_UsesDifferentDefaultsForDifferentTypes()
    {
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 150),
                new(TimberElementType.Brace, 50),
            },
        };

        var rafter = TimberElementDefaultApplicator.ApplyCuttingAllowance(
            TimberElementDefaults.For(TimberElementType.Rafter),
            profile);
        var brace = TimberElementDefaultApplicator.ApplyCuttingAllowance(
            TimberElementDefaults.For(TimberElementType.Brace),
            profile);

        Assert.Equal(150, rafter.CuttingAllowanceMm);
        Assert.Equal(50, brace.CuttingAllowanceMm);
    }

    [Fact]
    public void ApplyCuttingAllowance_AppliesPerElementTypeForMixedSelection()
    {
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 300),
                new(TimberElementType.WallPlate, 100),
                new(TimberElementType.Post, 200),
            },
        };
        var elements = new[]
        {
            TimberElementDefaults.For(TimberElementType.Rafter) with { CuttingAllowanceMm = 500 },
            TimberElementDefaults.For(TimberElementType.WallPlate) with { CuttingAllowanceMm = 500 },
            TimberElementDefaults.For(TimberElementType.Post) with { CuttingAllowanceMm = 500 },
        };

        var result = elements
            .Select(element => TimberElementDefaultApplicator.ApplyCuttingAllowance(element, profile))
            .ToList();

        Assert.Equal(300, result[0].CuttingAllowanceMm);
        Assert.Equal(100, result[1].CuttingAllowanceMm);
        Assert.Equal(200, result[2].CuttingAllowanceMm);
    }

    [Fact]
    public void ApplyCuttingAllowance_ChangedAllowanceChangesCuttingLength()
    {
        var existing = TimberElementDefaults.For(TimberElementType.Purlin) with
        {
            LengthCalculationMode = LengthCalculationMode.PlanLength,
            CuttingAllowanceMm = 0,
        };
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Purlin, 120),
            },
        };

        var before = TimberCalculator.Measure(existing, planLengthMm: 5000);
        var after = TimberCalculator.Measure(
            TimberElementDefaultApplicator.ApplyCuttingAllowance(existing, profile),
            planLengthMm: 5000);

        Assert.Equal(5000, before.CuttingLengthMm);
        Assert.Equal(5200, after.CuttingLengthMm);
    }

    [Fact]
    public void ChangedIndividualAllowance_DoesNotMutateGlobalDefaultProfile()
    {
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 300),
            },
        };
        var element = TimberElementDefaults.For(TimberElementType.Rafter, profile);

        var overridden = TimberElementPatcher.Apply(element, new TimberElementPatch(
            ElementType: null,
            WidthMm: null,
            HeightMm: null,
            SlopeDegrees: null,
            RoofPlaneId: null,
            CuttingAllowanceMm: 500,
            LengthCalculationMode: null,
            ManualLengthMm: null,
            Material: null,
            Note: null));

        Assert.Equal(500, overridden.CuttingAllowanceMm);
        Assert.Equal(300, profile.GetCuttingAllowanceMm(TimberElementType.Rafter));
    }

    [Fact]
    public void ChangedAllowanceWithManualLengthModePreservesManualLengthModeAndManualLength()
    {
        var element = TimberElementDefaults.For(TimberElementType.Post) with
        {
            LengthCalculationMode = LengthCalculationMode.ManualLength,
            ManualLengthMm = 2500,
            CuttingAllowanceMm = 100,
        };

        var result = TimberElementPatcher.Apply(element, new TimberElementPatch(
            ElementType: null,
            WidthMm: null,
            HeightMm: null,
            SlopeDegrees: null,
            RoofPlaneId: null,
            CuttingAllowanceMm: 300,
            LengthCalculationMode: null,
            ManualLengthMm: null,
            Material: null,
            Note: null));
        var measurement = TimberCalculator.Measure(result, planLengthMm: 1000);

        Assert.Equal(LengthCalculationMode.ManualLength, result.LengthCalculationMode);
        Assert.Equal(2500, result.ManualLengthMm);
        Assert.Equal(2500, measurement.ActualLengthMm);
        Assert.Equal(2800, measurement.CuttingLengthMm);
    }

    [Fact]
    public void ManualLengthMode_UsesManualLengthAndConfiguredRoundingStepForCuttingLength()
    {
        var element = TimberElementDefaults.For(TimberElementType.Post) with
        {
            LengthCalculationMode = LengthCalculationMode.ManualLength,
            ManualLengthMm = 2500,
            CuttingAllowanceMm = 75,
        };

        var measurement = TimberCalculator.Measure(element, planLengthMm: 1000, roundingIncrementMm: 50);

        Assert.Equal(LengthCalculationMode.ManualLength, element.LengthCalculationMode);
        Assert.Equal(2500, measurement.ActualLengthMm);
        Assert.Equal(2600, measurement.CuttingLengthMm);
    }

    [Fact]
    public void ApplyCuttingAllowance_ChangedCuttingLengthChangesManufacturingSignature()
    {
        var existing = TimberElementDefaults.For(TimberElementType.Purlin) with
        {
            LengthCalculationMode = LengthCalculationMode.PlanLength,
            CuttingAllowanceMm = 0,
        };
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Purlin, 120),
            },
        };

        var before = TimberElementSignature.FromMeasurement(TimberCalculator.Measure(existing, 5000));
        var after = TimberElementSignature.FromMeasurement(TimberCalculator.Measure(
            TimberElementDefaultApplicator.ApplyCuttingAllowance(existing, profile),
            5000));

        Assert.NotEqual(before, after);
        Assert.Equal(5200, after.CuttingLengthMm);
    }

    [Fact]
    public void ApplyCuttingAllowance_SameResultingSignaturesShareItemIdentity()
    {
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 80),
            },
        };
        var first = TimberElementDefaultApplicator.ApplyCuttingAllowance(
            TimberElementDefaults.For(TimberElementType.Rafter) with
            {
                ElementId = "K1",
                LengthCalculationMode = LengthCalculationMode.PlanLength,
            },
            profile);
        var second = TimberElementDefaultApplicator.ApplyCuttingAllowance(
            TimberElementDefaults.For(TimberElementType.Rafter) with
            {
                ElementId = "K2",
                LengthCalculationMode = LengthCalculationMode.PlanLength,
            },
            profile);

        var assignments = TimberElementItemNumbering.AssignElementIds(new[]
        {
            TimberCalculator.Measure(first, 5010),
            TimberCalculator.Measure(second, 5020),
        });

        Assert.Equal(5100, assignments[0].Signature.CuttingLengthMm);
        Assert.Equal(5100, assignments[1].Signature.CuttingLengthMm);
        Assert.Equal(assignments[0].ElementId, assignments[1].ElementId);
    }

    [Fact]
    public void NewElementsOnlyFlow_DoesNotMutateExistingElementData()
    {
        var existing = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            CuttingAllowanceMm = 75,
        };
        var profile = new TimberElementDefaultProfile
        {
            Styles = new List<TimberElementDefaultStyle>
            {
                new(TimberElementType.Rafter, 250),
            },
        };

        var newElement = TimberElementDefaults.For(TimberElementType.Rafter, profile);

        Assert.Equal(75, existing.CuttingAllowanceMm);
        Assert.Equal(250, newElement.CuttingAllowanceMm);
    }
}
