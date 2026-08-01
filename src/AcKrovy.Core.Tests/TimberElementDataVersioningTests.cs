using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberElementDataVersioningTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void CurrentVersion_IsSix()
    {
        Assert.Equal(6, TimberElementDataSchema.CurrentVersion);
    }

    [Fact]
    public void Normalize_KeepsVersionOne()
    {
        var data = Sample() with { SchemaVersion = 1 };

        var normalized = TimberElementDataVersioning.Normalize(data);

        Assert.Equal(1, normalized.SchemaVersion);
    }

    [Fact]
    public void Normalize_InterpretsDefaultVersionAsVersionOne()
    {
        var data = Sample() with { SchemaVersion = 0 };

        var normalized = TimberElementDataVersioning.Normalize(data);

        Assert.Equal(1, normalized.SchemaVersion);
    }

    [Fact]
    public void Normalize_DoesNotChangeOtherValues()
    {
        var data = Sample() with { SchemaVersion = 0 };

        var normalized = TimberElementDataVersioning.Normalize(data);

        Assert.Equal(data.ElementId, normalized.ElementId);
        Assert.Equal(data.ElementType, normalized.ElementType);
        Assert.Equal(data.WidthMm, normalized.WidthMm);
        Assert.Equal(data.HeightMm, normalized.HeightMm);
        Assert.Equal(data.FootprintWidthEdgeIndex, normalized.FootprintWidthEdgeIndex);
        Assert.Equal(data.SlopeDegrees, normalized.SlopeDegrees);
        Assert.Equal(data.IsSlopeDirectionReversed, normalized.IsSlopeDirectionReversed);
        Assert.Equal(data.RoofPlaneId, normalized.RoofPlaneId);
        Assert.Equal(data.CuttingAllowanceMm, normalized.CuttingAllowanceMm);
        Assert.Equal(data.LengthCalculationMode, normalized.LengthCalculationMode);
        Assert.Equal(data.ManualLengthMm, normalized.ManualLengthMm);
        Assert.Equal(data.Material, normalized.Material);
        Assert.Equal(data.Note, normalized.Note);
        Assert.Equal(data.AnnotationMode, normalized.AnnotationMode);
        Assert.Equal(data.ItemNumberLeaderStyle, normalized.ItemNumberLeaderStyle);
        Assert.Equal(
            data.AnnotationScaleDenominatorOverride,
            normalized.AnnotationScaleDenominatorOverride);
        Assert.Equal(data.AnnotationTextSettings, normalized.AnnotationTextSettings);
    }

    [Fact]
    public void IsSupported_RecognizesFutureUnsupportedVersion()
    {
        var data = Sample() with { SchemaVersion = TimberElementDataSchema.CurrentVersion + 1 };

        Assert.False(TimberElementDataVersioning.IsSupported(data));
    }

    [Fact]
    public void Normalize_RejectsFutureUnsupportedVersion()
    {
        var data = Sample() with { SchemaVersion = TimberElementDataSchema.CurrentVersion + 1 };

        var exception = Assert.Throws<UnsupportedTimberElementDataSchemaException>(() =>
            TimberElementDataVersioning.Normalize(data));
        Assert.Equal(
            TimberElementDataSchema.CurrentVersion + 1,
            exception.SchemaVersion);
        Assert.Equal(TimberElementDataSchema.CurrentVersion, exception.CurrentVersion);
    }

    [Fact]
    public void Normalize_PreservesCoreTimberValuesForLegacyData()
    {
        var data = Sample() with
        {
            SchemaVersion = 0,
            ElementType = TimberElementType.Post,
            WidthMm = 140,
            HeightMm = 180,
            SlopeDegrees = 0,
            CuttingAllowanceMm = 75,
            ManualLengthMm = 2600,
        };

        var normalized = TimberElementDataVersioning.Normalize(data);

        Assert.Equal(TimberElementType.Post, normalized.ElementType);
        Assert.Equal(140, normalized.WidthMm);
        Assert.Equal(180, normalized.HeightMm);
        Assert.Equal(0, normalized.SlopeDegrees);
        Assert.Equal(75, normalized.CuttingAllowanceMm);
        Assert.Equal(2600, normalized.ManualLengthMm);
        Assert.Null(normalized.FootprintWidthEdgeIndex);
    }

    [Fact]
    public void Deserialize_OldJsonWithoutVersion_InterpretsAsVersionOne()
    {
        const string json = """
            {
              "ElementId": "K9",
              "ElementType": "Rafter",
              "WidthMm": 90,
              "HeightMm": 170,
              "SlopeDegrees": 37,
              "RoofPlaneId": "R3",
              "CuttingAllowanceMm": 120,
              "LengthCalculationMode": "SlopeCorrected",
              "ManualLengthMm": null,
              "Material": "Smrek C24",
              "Note": "bez verzie"
            }
            """;

        var data = JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions);

        Assert.NotNull(data);
        var normalized = TimberElementDataVersioning.Normalize(data!);
        Assert.Equal(1, normalized.SchemaVersion);
        Assert.Equal("K9", normalized.ElementId);
        Assert.Equal(90, normalized.WidthMm);
        Assert.Equal(170, normalized.HeightMm);
        Assert.Equal(37, normalized.SlopeDegrees);
        Assert.False(normalized.IsSlopeDirectionReversed);
        Assert.Null(normalized.FootprintWidthEdgeIndex);
    }

    [Fact]
    public void Deserialize_OldJsonWithoutCuttingAllowance_UsesFactoryFallback()
    {
        const string json = """
            {
              "SchemaVersion": 1,
              "ElementId": "K9",
              "ElementType": "Rafter",
              "WidthMm": 90,
              "HeightMm": 170,
              "SlopeDegrees": 37,
              "RoofPlaneId": "R3",
              "LengthCalculationMode": "SlopeCorrected",
              "ManualLengthMm": null,
              "Material": "Smrek C24",
              "Note": "bez prídavku"
            }
            """;

        var data = JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions);

        Assert.NotNull(data);
        var normalized = TimberElementDataVersioning.Normalize(data!);
        Assert.Equal(TimberElementDefaultProfile.FactoryCuttingAllowanceMm, normalized.CuttingAllowanceMm);
    }

    [Fact]
    public void Serialize_NewJson_IncludesVersionSix()
    {
        var data = Sample();

        var json = JsonSerializer.Serialize(data, JsonOptions);

        Assert.Contains("\"SchemaVersion\":6", json);
    }

    [Fact]
    public void PrepareForWrite_UpgradesVersionOneWithoutChangingValues()
    {
        var legacy = Sample() with
        {
            SchemaVersion = 1,
            ElementType = TimberElementType.Post,
            FootprintWidthEdgeIndex = null,
        };

        var prepared = TimberElementDataVersioning.PrepareForWrite(legacy);

        Assert.Equal(6, prepared.SchemaVersion);
        Assert.Null(prepared.FootprintWidthEdgeIndex);
        Assert.Equal(legacy.ElementId, prepared.ElementId);
        Assert.Equal(legacy.WidthMm, prepared.WidthMm);
        Assert.Equal(legacy.HeightMm, prepared.HeightMm);
    }

    [Fact]
    public void Deserialize_SchemaFourWithoutOverride_KeepsNullAndDoesNotUpgrade()
    {
        const string json = """
            {
              "SchemaVersion": 4,
              "ElementId": "K4",
              "ElementType": "Rafter"
            }
            """;

        var deserialized = Assert.IsType<TimberElementData>(
            JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions));
        var normalized = TimberElementDataVersioning.Normalize(deserialized);

        Assert.Equal(4, normalized.SchemaVersion);
        Assert.Null(normalized.AnnotationScaleDenominatorOverride);
        Assert.Null(normalized.AnnotationTextSettings);
    }

    [Fact]
    public void Deserialize_SchemaFiveWithoutTextSettingsKeepsNullAndDoesNotUpgrade()
    {
        const string json = """
            {
              "SchemaVersion": 5,
              "ElementId": "K5",
              "ElementType": "Rafter",
              "AnnotationScaleDenominatorOverride": 75
            }
            """;

        var deserialized = Assert.IsType<TimberElementData>(
            JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions));
        var normalized = TimberElementDataVersioning.Normalize(deserialized);

        Assert.Equal(5, normalized.SchemaVersion);
        Assert.Equal(75, normalized.AnnotationScaleDenominatorOverride);
        Assert.Null(normalized.AnnotationTextSettings);
    }

    [Fact]
    public void PrepareForWrite_SchemaFiveUpgradesToSixAndKeepsLegacyTextNull()
    {
        var source = Sample() with
        {
            SchemaVersion = 5,
            AnnotationTextSettings = null,
        };

        var prepared = TimberElementDataVersioning.PrepareForWrite(source);

        Assert.Equal(6, prepared.SchemaVersion);
        Assert.Null(prepared.AnnotationTextSettings);
    }

    [Fact]
    public void PrepareForWrite_SchemaFourUpgradesToSix()
    {
        var source = Sample() with
        {
            SchemaVersion = 4,
            AnnotationScaleDenominatorOverride = null,
        };

        var result = TimberElementDataVersioning.PrepareForWrite(source);

        Assert.Equal(6, result.SchemaVersion);
        Assert.Null(result.AnnotationScaleDenominatorOverride);
        Assert.Null(result.AnnotationTextSettings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(5)]
    [InlineData(250)]
    public void Serialize_SchemaFive_RoundTripsOverrideAndKeepsLegacyTextNull(
        int? denominator)
    {
        var source = Sample() with
        {
            SchemaVersion = 5,
            AnnotationScaleDenominatorOverride = denominator,
            AnnotationTextSettings = null,
        };

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = Assert.IsType<TimberElementData>(
            JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions));

        Assert.Equal(5, result.SchemaVersion);
        Assert.Equal(denominator, result.AnnotationScaleDenominatorOverride);
        Assert.Null(result.AnnotationTextSettings);
    }

    [Fact]
    public void Serialize_SchemaSix_RoundTripsAnnotationTextSettings()
    {
        var settings = new TimberAnnotationTextSettings(
            "ISOCP",
            3.2d,
            3.1d,
            2d);
        var source = Sample() with { AnnotationTextSettings = settings };

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = Assert.IsType<TimberElementData>(
            JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions));

        Assert.Equal(6, result.SchemaVersion);
        Assert.Equal(settings, result.AnnotationTextSettings);
    }

    [Fact]
    public void Normalize_InvalidStoredTextFieldsFallsBackWithoutChangingSchema()
    {
        var source = Sample() with
        {
            SchemaVersion = 6,
            AnnotationTextSettings = new TimberAnnotationTextSettings(
                " ",
                20d,
                8d,
                double.PositiveInfinity),
        };

        var normalized = TimberElementDataVersioning.Normalize(source);

        Assert.Equal(6, normalized.SchemaVersion);
        Assert.Equal(
            TimberAnnotationTextSettingsRules.Default,
            normalized.AnnotationTextSettings);
    }

    [Fact]
    public void Normalize_InvalidStoredOverride_DoesNotRepairValue()
    {
        var source = Sample() with { AnnotationScaleDenominatorOverride = 4 };

        var normalized = TimberElementDataVersioning.Normalize(source);

        Assert.Equal(4, normalized.AnnotationScaleDenominatorOverride);
    }

    private static TimberElementData Sample() => new()
    {
        SchemaVersion = TimberElementDataSchema.CurrentVersion,
        ElementId = "K1",
        ElementType = TimberElementType.Rafter,
        WidthMm = 80,
        HeightMm = 160,
        SlopeDegrees = 35,
        RoofPlaneId = "R1",
        CuttingAllowanceMm = 100,
        LengthCalculationMode = LengthCalculationMode.AutoByElementType,
        ManualLengthMm = 2500,
        Material = "Smrek C24",
        Note = "poznamka",
    };
}
