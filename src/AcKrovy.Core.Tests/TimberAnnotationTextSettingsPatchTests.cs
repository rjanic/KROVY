using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberAnnotationTextSettingsPatchTests
{
    [Fact]
    public void Unchanged_IsNoOpOverExplicitSettingsAndOverLegacyNull()
    {
        var stored = Settings();

        Assert.Equal(
            TimberAnnotationTextSettingsChange.Unchanged,
            TimberAnnotationTextSettingsPatch.Unchanged.Change);
        Assert.Same(
            stored,
            TimberAnnotationTextSettingsPatch.Unchanged.Apply(stored));
        Assert.Null(TimberAnnotationTextSettingsPatch.Unchanged.Apply(null));
    }

    [Theory]
    [InlineData(TimberAnnotationTextRole.ItemCode)]
    [InlineData(TimberAnnotationTextRole.Dimension)]
    [InlineData(TimberAnnotationTextRole.Slope)]
    public void ForRole_ChangesOnlyThatRole(TimberAnnotationTextRole role)
    {
        var stored = Settings();
        var patch = TimberAnnotationTextSettingsPatch.ForRole(role, "ARIAL", 2d);

        var applied = Assert.IsType<TimberAnnotationTextSettings>(
            patch.Apply(stored));

        Assert.Equal(TimberAnnotationTextSettingsChange.Set, patch.Change);
        Assert.Equal("ARIAL", applied.GetTextStyleName(role));
        Assert.Equal(2d, applied.GetPaperHeightMm(role));
        foreach (var other in AllRoles)
        {
            if (other == role)
            {
                continue;
            }

            Assert.Equal(
                TimberAnnotationTextSettingsChange.Unchanged,
                patch.ForRole(other).Change);
            Assert.Equal(
                stored.GetTextStyleName(other),
                applied.GetTextStyleName(other));
            Assert.Equal(
                stored.GetPaperHeightMm(other),
                applied.GetPaperHeightMm(other));
        }
    }

    [Fact]
    public void ForRoles_RepresentsMixedSelectionOfSetAndUnchangedRoles()
    {
        var stored = Settings();
        var patch = TimberAnnotationTextSettingsPatch.ForRoles(
            TimberAnnotationTextRolePatch.Set(
                TimberAnnotationTextRole.ItemCode,
                "ARIAL",
                3d),
            TimberAnnotationTextRolePatch.Unchanged,
            TimberAnnotationTextRolePatch.Set(
                TimberAnnotationTextRole.Slope,
                "ROMANS",
                2d));

        var applied = Assert.IsType<TimberAnnotationTextSettings>(
            patch.Apply(stored));

        Assert.Equal("ARIAL", applied.ItemCodeTextStyleName);
        Assert.Equal(3d, applied.ItemCodePaperHeightMm);
        Assert.Equal(stored.DimensionTextStyleName, applied.DimensionTextStyleName);
        Assert.Equal(stored.DimensionPaperHeightMm, applied.DimensionPaperHeightMm);
        Assert.Equal("ROMANS", applied.SlopeTextStyleName);
        Assert.Equal(2d, applied.SlopePaperHeightMm);
        Assert.False(applied.HasSharedTextStyleName);
    }

    [Fact]
    public void ForRole_OverLegacyNullMaterializesUntouchedRolesFromFactoryDefaults()
    {
        var applied = Assert.IsType<TimberAnnotationTextSettings>(
            TimberAnnotationTextSettingsPatch.ForRole(
                TimberAnnotationTextRole.Slope,
                "ROMANS",
                2d)
                .Apply(null));

        Assert.Equal("ROMANS", applied.SlopeTextStyleName);
        Assert.Equal(2d, applied.SlopePaperHeightMm);
        Assert.Equal(
            TimberAnnotationTextSettingsRules.DefaultTextStyleName,
            applied.ItemCodeTextStyleName);
        Assert.Equal(
            TimberAnnotationTextSettingsRules.DefaultItemCodePaperHeightMm,
            applied.ItemCodePaperHeightMm);
        Assert.Equal(
            TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
            applied.DimensionPaperHeightMm);
    }

    [Fact]
    public void Set_AppliesEveryRoleAndTrimsStyleNames()
    {
        var patch = TimberAnnotationTextSettingsPatch.Set(
            new TimberAnnotationTextSettings(
                "  ARIAL  ",
                "  ISOCP  ",
                "  ROMANS  ",
                3d,
                3.1d,
                2d));

        var applied = Assert.IsType<TimberAnnotationTextSettings>(
            patch.Apply(Settings()));

        Assert.Equal(
            new TimberAnnotationTextSettings(
                "ARIAL",
                "ISOCP",
                "ROMANS",
                3d,
                3.1d,
                2d),
            applied);
    }

    [Fact]
    public void Apply_RepairsInvalidStoredRolesBeforeSettingThePatchedRole()
    {
        var stored = Settings() with { DimensionPaperHeightMm = 42d };

        var applied = Assert.IsType<TimberAnnotationTextSettings>(
            TimberAnnotationTextSettingsPatch.ForRole(
                TimberAnnotationTextRole.ItemCode,
                "ARIAL",
                3d)
                .Apply(stored));

        Assert.Equal("ARIAL", applied.ItemCodeTextStyleName);
        Assert.Equal(
            TimberAnnotationTextSettingsRules.DefaultDimensionPaperHeightMm,
            applied.DimensionPaperHeightMm);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Style\nName")]
    public void RolePatch_RejectsInvalidStyleName(string textStyleName) =>
        Assert.Throws<ArgumentException>(() =>
            TimberAnnotationTextRolePatch.Set(
                TimberAnnotationTextRole.ItemCode,
                textStyleName,
                2.7d));

    [Theory]
    [InlineData(TimberAnnotationTextRole.ItemCode, 3.501d)]
    [InlineData(TimberAnnotationTextRole.Dimension, 10.001d)]
    [InlineData(TimberAnnotationTextRole.Slope, 5.001d)]
    [InlineData(TimberAnnotationTextRole.ItemCode, double.NaN)]
    [InlineData(TimberAnnotationTextRole.Dimension, double.PositiveInfinity)]
    [InlineData(TimberAnnotationTextRole.Slope, double.NegativeInfinity)]
    public void RolePatch_RejectsOutOfRangeHeightPerRoleWithoutClamping(
        TimberAnnotationTextRole role,
        double paperHeightMm) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberAnnotationTextRolePatch.Set(role, "ISOCP", paperHeightMm));

    [Fact]
    public void RolePatch_AcceptsHeightAllowedByItsOwnRoleRangeOnly()
    {
        Assert.Equal(
            5d,
            TimberAnnotationTextRolePatch.Set(
                TimberAnnotationTextRole.Slope,
                "ISOCP",
                5d).PaperHeightMm);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberAnnotationTextRolePatch.Set(
                TimberAnnotationTextRole.ItemCode,
                "ISOCP",
                5d));
    }

    private static readonly TimberAnnotationTextRole[] AllRoles =
    {
        TimberAnnotationTextRole.ItemCode,
        TimberAnnotationTextRole.Dimension,
        TimberAnnotationTextRole.Slope,
    };

    private static TimberAnnotationTextSettings Settings() =>
        new("ISOCP", "ROMANS", "ARIAL", 2.7d, 2.5d, 1.6d);
}
