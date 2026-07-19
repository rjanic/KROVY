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
    public void CurrentVersion_IsOne()
    {
        Assert.Equal(1, TimberElementDataSchema.CurrentVersion);
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
        Assert.Equal(data.SlopeDegrees, normalized.SlopeDegrees);
        Assert.Equal(data.IsSlopeDirectionReversed, normalized.IsSlopeDirectionReversed);
        Assert.Equal(data.RoofPlaneId, normalized.RoofPlaneId);
        Assert.Equal(data.CuttingAllowanceMm, normalized.CuttingAllowanceMm);
        Assert.Equal(data.LengthCalculationMode, normalized.LengthCalculationMode);
        Assert.Equal(data.ManualLengthMm, normalized.ManualLengthMm);
        Assert.Equal(data.Material, normalized.Material);
        Assert.Equal(data.Note, normalized.Note);
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
        Assert.Equal(2, exception.SchemaVersion);
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
    public void Serialize_NewJson_IncludesVersionOne()
    {
        var data = Sample();

        var json = JsonSerializer.Serialize(data, JsonOptions);

        Assert.Contains("\"SchemaVersion\":1", json);
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
