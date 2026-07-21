using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberRectangularFootprintFoundationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void AxisAlignedRectangle_IsValidAndExposesDerivedGeometry()
    {
        var result = Validate(Rectangle(140d, 200d));

        Assert.True(result.IsValid);
        var geometry = Assert.IsType<TimberRectangularFootprintGeometry>(result.Geometry);
        Assert.Equal(4, geometry.Vertices.Count);
        Assert.Equal(4, geometry.Segments.Count);
        Assert.Equal(new[] { 140d, 200d, 140d, 200d }, geometry.Segments.Select(x => x.LengthMm));
        Assert.Equal(new TimberRectangularFootprintPoint(70d, 100d), geometry.Center);
        Assert.Equal(new TimberRectangularFootprintBounds(0d, 0d, 140d, 200d), geometry.Bounds);
        Assert.Equal(28000d, geometry.AreaMm2);
    }

    [Fact]
    public void RotatedRectangle_IsValid()
    {
        var result = Validate(Rotate(Rectangle(140d, 200d), 31d));

        Assert.True(result.IsValid);
        Assert.Equal(28000d, result.Geometry!.AreaMm2, precision: 6);
    }

    [Fact]
    public void Square_IsValid()
    {
        var result = Validate(Rectangle(140d, 140d));

        Assert.True(result.IsValid);
        Assert.All(result.Geometry!.Segments, segment => Assert.Equal(140d, segment.LengthMm));
    }

    [Fact]
    public void CounterClockwiseVertices_AreValid()
    {
        var result = Validate(Rectangle(140d, 200d));

        Assert.True(result.IsValid);
        Assert.True(result.Geometry!.SignedAreaMm2 > 0d);
    }

    [Fact]
    public void ClockwiseVertices_AreValid()
    {
        var result = Validate(Rectangle(140d, 200d).Reverse().ToArray());

        Assert.True(result.IsValid);
        Assert.True(result.Geometry!.SignedAreaMm2 < 0d);
    }

    [Fact]
    public void Trapezoid_IsRejected()
    {
        var result = Validate(
            P(0d, 0d), P(140d, 0d), P(120d, 100d), P(20d, 100d));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void NonRectangularParallelogram_IsRejected()
    {
        var result = Validate(
            P(0d, 0d), P(140d, 0d), P(180d, 100d), P(40d, 100d));

        Assert.False(result.IsValid);
        Assert.Equal(
            TimberRectangularFootprintValidationError.AdjacentEdgesNotPerpendicular,
            result.Error);
    }

    [Fact]
    public void ZeroLengthEdge_IsRejected()
    {
        var result = Validate(
            P(0d, 0d), P(0d, 0d), P(140d, 100d), P(0d, 100d));

        Assert.False(result.IsValid);
        Assert.Equal(TimberRectangularFootprintValidationError.ZeroLengthEdge, result.Error);
    }

    [Fact]
    public void DegenerateArea_IsRejected()
    {
        var result = Validate(
            P(0d, 0d), P(100d, 0d), P(200d, 0d), P(300d, 0d));

        Assert.False(result.IsValid);
        Assert.Equal(TimberRectangularFootprintValidationError.DegenerateArea, result.Error);
    }

    [Fact]
    public void InvalidVertexCount_IsRejectedWithoutConstructingGeometry()
    {
        var result = TimberRectangularFootprintValidator.Validate(
            new[] { P(0d, 0d), P(100d, 0d), P(100d, 100d) });

        Assert.False(result.IsValid);
        Assert.Null(result.Geometry);
        Assert.Equal(TimberRectangularFootprintValidationError.InvalidVertexCount, result.Error);
    }

    [Theory]
    [InlineData(0, 140d, 200d)]
    [InlineData(1, 200d, 140d)]
    [InlineData(2, 140d, 200d)]
    [InlineData(3, 200d, 140d)]
    public void StoredWidthEdgeIndex_MapsWidthAndHeight(
        int widthEdgeIndex,
        double expectedWidthMm,
        double expectedHeightMm)
    {
        var geometry = ValidGeometry(Rectangle(140d, 200d));

        var dimensions = TimberRectangularFootprintEdgeRules.ResolveDimensions(
            geometry,
            widthEdgeIndex);

        Assert.Equal(widthEdgeIndex, dimensions.WidthEdgeIndex);
        Assert.Equal(expectedWidthMm, dimensions.WidthMm);
        Assert.Equal(expectedHeightMm, dimensions.HeightMm);
        Assert.True(TimberRectangularFootprintEdgeRules.AreAdjacentEdges(
            dimensions.WidthEdgeIndex,
            dimensions.HeightEdgeIndex));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 3)]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 0)]
    [InlineData(3, 2)]
    public void AdjacentEdgeValidation_Passes(int firstEdgeIndex, int secondEdgeIndex)
    {
        Assert.True(TimberRectangularFootprintEdgeRules.AreAdjacentEdges(
            firstEdgeIndex,
            secondEdgeIndex));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 3)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    public void OppositeEdgeValidation_FailsForHeightSelection(
        int widthEdgeIndex,
        int heightEdgeIndex)
    {
        var geometry = ValidGeometry(Rectangle(140d, 200d));

        var resolved = TimberRectangularFootprintEdgeRules.TryResolveDimensions(
            geometry,
            widthEdgeIndex,
            heightEdgeIndex,
            out var dimensions);

        Assert.True(TimberRectangularFootprintEdgeRules.AreOppositeEdges(
            widthEdgeIndex,
            heightEdgeIndex));
        Assert.False(resolved);
        Assert.Null(dimensions);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(100)]
    public void InvalidEdgeIndex_IsRejected(int edgeIndex)
    {
        Assert.False(TimberRectangularFootprintEdgeRules.IsValidEdgeIndex(edgeIndex));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TimberRectangularFootprintEdgeRules.ResolveDimensions(
                ValidGeometry(Rectangle(140d, 200d)),
                edgeIndex));
    }

    [Fact]
    public void DimensionCandidates_AllowFutureEdgeReindexRecovery()
    {
        var candidates = TimberRectangularFootprintEdgeRules.ResolveDimensionCandidates(
            ValidGeometry(Rectangle(140d, 200d)));

        Assert.Equal(4, candidates.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, candidates.Select(candidate => candidate.WidthEdgeIndex));
        Assert.Equal(2, candidates.Count(candidate => candidate.WidthMm == 140d && candidate.HeightMm == 200d));
        Assert.Equal(2, candidates.Count(candidate => candidate.WidthMm == 200d && candidate.HeightMm == 140d));
    }

    [Fact]
    public void SchemaVersionOnePayload_LoadsAsLegacyWithoutFootprintIndex()
    {
        const string json = """
            {
              "SchemaVersion": 1,
              "ElementId": "S1",
              "ElementType": "Post",
              "WidthMm": 140,
              "HeightMm": 140,
              "SlopeDegrees": 0,
              "LengthCalculationMode": "ManualLength",
              "ManualLengthMm": 2500
            }
            """;

        var data = TimberElementDataVersioning.Normalize(
            Assert.IsType<TimberElementData>(JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions)));

        Assert.Equal(1, data.SchemaVersion);
        Assert.Null(data.FootprintWidthEdgeIndex);
        Assert.False(TimberPostFootprintMetadataRules.IsValidNewFootprintPost(data));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SchemaVersionTwo_RoundTripsFootprintIndex(int edgeIndex)
    {
        var source = FootprintPost(edgeIndex);

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var loaded = Assert.IsType<TimberElementData>(
            JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions));

        Assert.Equal(2, loaded.SchemaVersion);
        Assert.Equal(edgeIndex, loaded.FootprintWidthEdgeIndex);
        Assert.True(TimberPostFootprintMetadataRules.IsValidNewFootprintPost(loaded));
    }

    [Fact]
    public void SchemaVersionTwo_NullFootprintIndexRoundTrips()
    {
        var source = TimberElementDefaults.For(TimberElementType.Post) with
        {
            FootprintWidthEdgeIndex = null,
        };

        var loaded = Assert.IsType<TimberElementData>(JsonSerializer.Deserialize<TimberElementData>(
            JsonSerializer.Serialize(source, JsonOptions),
            JsonOptions));

        Assert.Equal(2, loaded.SchemaVersion);
        Assert.Null(loaded.FootprintWidthEdgeIndex);
        Assert.True(TimberPostFootprintMetadataRules.HasPreferredFootprintMetadataShape(loaded));
    }

    [Fact]
    public void LegacyLineBasedPost_RemainsLegacyWhenPreparedForWrite()
    {
        var legacy = TimberElementDefaults.For(TimberElementType.Post) with
        {
            SchemaVersion = 1,
            FootprintWidthEdgeIndex = null,
        };

        var prepared = TimberElementDataVersioning.PrepareForWrite(legacy);

        Assert.Equal(2, prepared.SchemaVersion);
        Assert.Null(prepared.FootprintWidthEdgeIndex);
        Assert.Equal(legacy.WidthMm, prepared.WidthMm);
        Assert.Equal(legacy.HeightMm, prepared.HeightMm);
        Assert.Equal(legacy.ManualLengthMm, prepared.ManualLengthMm);
    }

    [Fact]
    public void NonPostWithoutFootprintIndex_RemainsUnchanged()
    {
        var rafter = TimberElementDefaults.For(TimberElementType.Rafter);

        var normalized = TimberElementDataVersioning.Normalize(rafter);

        Assert.Equal(rafter, normalized);
        Assert.Null(normalized.FootprintWidthEdgeIndex);
        Assert.True(TimberPostFootprintMetadataRules.HasPreferredFootprintMetadataShape(normalized));
    }

    [Fact]
    public void StrictNewFootprintRules_RejectInvalidOrNonPostMetadata()
    {
        Assert.False(TimberPostFootprintMetadataRules.IsValidNewFootprintPost(
            FootprintPost(4)));
        Assert.False(TimberPostFootprintMetadataRules.IsValidNewFootprintPost(
            TimberElementDefaults.For(TimberElementType.Rafter) with
            {
                FootprintWidthEdgeIndex = 0,
            }));
    }

    [Fact]
    public void ReadNormalization_RemainsTolerantOfUnknownFootprintIndex()
    {
        var data = FootprintPost(99);

        var normalized = TimberElementDataVersioning.Normalize(data);

        Assert.Equal(99, normalized.FootprintWidthEdgeIndex);
        Assert.False(TimberPostFootprintMetadataRules.IsValidNewFootprintPost(normalized));
    }

    [Fact]
    public void CoreRectangleFoundation_HasNoAutodeskAssemblyDependency()
    {
        var references = typeof(TimberRectangularFootprintGeometry)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(references, name =>
            name.StartsWith("Autodesk.AutoCAD", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("AcMgd", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("AcDbMgd", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("AcCoreMgd", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExistingMetadataPayloadWithoutVersion_RemainsReadableAsVersionOne()
    {
        const string json = """
            {
              "ElementId": "K9",
              "ElementType": "Rafter",
              "WidthMm": 80,
              "HeightMm": 160,
              "SlopeDegrees": 35,
              "LengthCalculationMode": "SlopeCorrected"
            }
            """;

        var loaded = TimberElementDataVersioning.Normalize(
            Assert.IsType<TimberElementData>(JsonSerializer.Deserialize<TimberElementData>(json, JsonOptions)));

        Assert.Equal(TimberElementDataSchema.LegacyImplicitVersion, loaded.SchemaVersion);
        Assert.Equal("K9", loaded.ElementId);
        Assert.Null(loaded.FootprintWidthEdgeIndex);
    }

    [Fact]
    public void FootprintIndex_DoesNotChangeExistingItemSignature()
    {
        var first = FootprintPost(0);
        var second = first with { FootprintWidthEdgeIndex = 2 };
        var firstMeasurement = TimberCalculator.Measure(first, 1000d);
        var secondMeasurement = TimberCalculator.Measure(second, 1000d);

        Assert.Equal(
            TimberElementSignature.FromMeasurement(firstMeasurement),
            TimberElementSignature.FromMeasurement(secondMeasurement));
    }

    [Fact]
    public void ValidatorTolerances_AreCentralizedAndNonZero()
    {
        Assert.True(TimberRectangularFootprintValidator.MinimumEdgeLengthMm > 0d);
        Assert.True(TimberRectangularFootprintValidator.MinimumAreaMm2 > 0d);
        Assert.True(TimberRectangularFootprintValidator.AngularToleranceDegrees > 0d);
        Assert.True(TimberRectangularFootprintValidator.OppositeLengthAbsoluteToleranceMm > 0d);
        Assert.True(TimberRectangularFootprintValidator.OppositeLengthRelativeTolerance > 0d);
    }

    private static TimberElementData FootprintPost(int edgeIndex) =>
        TimberElementDefaults.For(TimberElementType.Post) with
        {
            SchemaVersion = 2,
            ElementId = "S1",
            WidthMm = edgeIndex % 2 == 0 ? 140d : 200d,
            HeightMm = edgeIndex % 2 == 0 ? 200d : 140d,
            LengthCalculationMode = LengthCalculationMode.ManualLength,
            ManualLengthMm = 2500d,
            FootprintWidthEdgeIndex = edgeIndex,
        };

    private static TimberRectangularFootprintValidationResult Validate(
        params TimberRectangularFootprintPoint[] vertices) =>
        TimberRectangularFootprintValidator.Validate(vertices);

    private static TimberRectangularFootprintGeometry ValidGeometry(
        IReadOnlyList<TimberRectangularFootprintPoint> vertices) =>
        Assert.IsType<TimberRectangularFootprintGeometry>(Validate(vertices.ToArray()).Geometry);

    private static TimberRectangularFootprintPoint[] Rectangle(double widthMm, double heightMm) =>
    [
        P(0d, 0d),
        P(widthMm, 0d),
        P(widthMm, heightMm),
        P(0d, heightMm),
    ];

    private static TimberRectangularFootprintPoint[] Rotate(
        IReadOnlyList<TimberRectangularFootprintPoint> points,
        double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return points
            .Select(point => P(
                point.X * cosine - point.Y * sine,
                point.X * sine + point.Y * cosine))
            .ToArray();
    }

    private static TimberRectangularFootprintPoint P(double x, double y) => new(x, y);
}
