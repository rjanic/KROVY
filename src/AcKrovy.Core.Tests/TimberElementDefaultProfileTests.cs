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

        foreach (TimberElementType type in Enum.GetValues(typeof(TimberElementType)))
        {
            Assert.Equal(TimberElementDefaultProfile.GetFactoryCuttingAllowanceMm(type), profile.GetCuttingAllowanceMm(type));
        }
    }

    [Fact]
    public void CreateDefault_UsesConservativePurlinAllowance()
    {
        var profile = TimberElementDefaultProfile.CreateDefault();

        Assert.Equal(200, profile.GetCuttingAllowanceMm(TimberElementType.Purlin));
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
