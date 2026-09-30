using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofStructuralPhysicalSettingsTests
{
    [Fact]
    public void OwnerSchemaAndHeightModeValues_AreStable()
    {
        Assert.Equal(3, RoofPhysicalElevationSchema.Version3);
        Assert.Equal(4, RoofPhysicalElevationSchema.CurrentVersion);
        Assert.Equal(0, (int)RoofStructuralHeightMode.Automatic);
        Assert.Equal(1, (int)RoofStructuralHeightMode.Explicit);
        Assert.Equal(120d, RoofStructuralPhysicalSettings.DefaultWidthMm);
    }

    [Theory]
    [InlineData(RoofPhysicalElevationSchema.Version1)]
    [InlineData(RoofPhysicalElevationSchema.Version2)]
    [InlineData(RoofPhysicalElevationSchema.Version3)]
    public void LegacyOwnerSchemas_ReceiveStructuralDefaults(int version)
    {
        var result = RoofPhysicalElevationRules.Validate(
            version, RoofAbsoluteElevationInputMode.Eave, 1000d, 1000d, true);

        Assert.True(result.IsValid);
        Assert.Equal(120d, result.Data!.StructuralWidthMm);
        Assert.Equal(RoofStructuralHeightMode.Automatic, result.Data.StructuralHeightMode);
        Assert.Equal(0d, result.Data.StructuralExplicitHeightMm);

        var state = RoofPhysicalElevationRules.ToState(result.Data, 500d);
        Assert.Equal(result.Data.StructuralWidthMm, state.StructuralWidthMm);
        Assert.Equal(result.Data.StructuralHeightMode, state.StructuralHeightMode);
        Assert.Equal(result.Data.StructuralExplicitHeightMm, state.StructuralExplicitHeightMm);
    }

    [Fact]
    public void MissingOwnerStore_UsesStructuralDefaults()
    {
        var state = RoofPhysicalElevationRules.MissingStoreDefault(500d);
        Assert.Equal(120d, state.StructuralWidthMm);
        Assert.Equal(RoofStructuralHeightMode.Automatic, state.StructuralHeightMode);
        Assert.Equal(0d, state.StructuralExplicitHeightMm);
    }

    [Fact]
    public void ExplicitStructuralSettings_SurviveStateAndElevationChanges()
    {
        var state = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Eave, 1000d, 3000d, 30d, true) with
        {
            StructuralWidthMm = 160d,
            StructuralHeightMode = RoofStructuralHeightMode.Explicit,
            StructuralExplicitHeightMm = 240d,
        };

        state = RoofAbsoluteElevationRules.SwitchMode(
            state, RoofAbsoluteElevationInputMode.Ridge);
        state = RoofAbsoluteElevationRules.RecalculateForGeometry(state, 3500d, 35d);
        state = RoofAbsoluteElevationRules.WithPhysical3DEnabled(state, false);
        var data = RoofPhysicalElevationRules.CreateFromState(state);
        var restored = RoofPhysicalElevationRules.ToState(data, state.RiseMm);

        Assert.Equal(RoofPhysicalElevationSchema.CurrentVersion, data.SchemaVersion);
        Assert.Equal(160d, data.StructuralWidthMm);
        Assert.Equal(RoofStructuralHeightMode.Explicit, data.StructuralHeightMode);
        Assert.Equal(240d, data.StructuralExplicitHeightMm);
        Assert.Equal(data.StructuralWidthMm, restored.StructuralWidthMm);
        Assert.Equal(data.StructuralHeightMode, restored.StructuralHeightMode);
        Assert.Equal(data.StructuralExplicitHeightMm, restored.StructuralExplicitHeightMm);
    }

    [Fact]
    public void AutomaticMode_PreservesPositiveExplicitHeightForLaterSwitch()
    {
        var result = Validate(
            width: 120d, mode: RoofStructuralHeightMode.Automatic, height: 240d);

        Assert.True(result.IsValid);
        Assert.Equal(240d, result.Data!.StructuralExplicitHeightMm);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidWidth_IsRejected(double width)
    {
        Assert.Equal(RoofPhysicalElevationError.InvalidStructuralWidth,
            Validate(width, RoofStructuralHeightMode.Automatic, 0d).Error);
    }

    [Theory]
    [InlineData(RoofStructuralHeightMode.Automatic, -1d)]
    [InlineData(RoofStructuralHeightMode.Automatic, double.NaN)]
    [InlineData(RoofStructuralHeightMode.Explicit, 0d)]
    [InlineData(RoofStructuralHeightMode.Explicit, double.PositiveInfinity)]
    public void InvalidHeight_IsRejected(RoofStructuralHeightMode mode, double height)
    {
        Assert.Equal(RoofPhysicalElevationError.InvalidStructuralExplicitHeight,
            Validate(120d, mode, height).Error);
    }

    [Fact]
    public void UnknownHeightMode_IsRejected()
    {
        Assert.Equal(RoofPhysicalElevationError.UnsupportedStructuralHeightMode,
            Validate(120d, (RoofStructuralHeightMode)99, 200d).Error);
    }

    private static RoofPhysicalElevationValidationResult Validate(
        double width, RoofStructuralHeightMode mode, double height) =>
        RoofPhysicalElevationRules.Validate(
            RoofPhysicalElevationSchema.CurrentVersion,
            RoofAbsoluteElevationInputMode.Eave,
            1000d,
            1000d,
            true,
            structuralWidthMm: width,
            structuralHeightMode: mode,
            structuralExplicitHeightMm: height);
}
