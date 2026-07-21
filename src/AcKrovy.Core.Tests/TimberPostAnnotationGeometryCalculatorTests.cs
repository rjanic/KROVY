using System.Text.Json;
using System.Text.Json.Serialization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberPostAnnotationGeometryCalculatorTests
{
    [Fact]
    public void PostUsesPerpendicularMarkerAndNinetyDegreeDisplayText()
    {
        Assert.Equal(
            TimberSlopeGlyphKind.PostPerpendicularMarker,
            TimberSlopeAnnotationRules.ResolveGlyphKind(TimberElementType.Post, 0d));
        Assert.Equal(
            90d,
            TimberSlopeAnnotationRules.ResolveDisplayAngleDegrees(TimberElementType.Post, 0d));
        Assert.True(TimberSlopeAnnotationRules.ShouldDisplayAngleText(TimberElementType.Post, 0d));
    }

    [Theory]
    [InlineData(TimberElementType.Rafter, 35d, TimberSlopeGlyphKind.DirectionalArrow)]
    [InlineData(TimberElementType.WallPlate, 0d, TimberSlopeGlyphKind.HorizontalMarker)]
    [InlineData(TimberElementType.Purlin, 0d, TimberSlopeGlyphKind.HorizontalMarker)]
    [InlineData(TimberElementType.CollarTie, 0d, TimberSlopeGlyphKind.HorizontalMarker)]
    [InlineData(TimberElementType.Brace, 35d, TimberSlopeGlyphKind.DirectionalArrow)]
    [InlineData(TimberElementType.TieBeam, 0d, TimberSlopeGlyphKind.HorizontalMarker)]
    public void NonPostTypesKeepExistingSlopeGlyphRules(
        TimberElementType elementType,
        double slopeDegrees,
        TimberSlopeGlyphKind expected)
    {
        Assert.Equal(expected, TimberSlopeAnnotationRules.ResolveGlyphKind(elementType, slopeDegrees));
        Assert.Equal(
            slopeDegrees,
            TimberSlopeAnnotationRules.ResolveDisplayAngleDegrees(elementType, slopeDegrees));
    }

    [Fact]
    public void HorizontalPostBuildsSmallInvertedTAndTextFromOneAnchor()
    {
        var geometry = TimberPostAnnotationGeometryCalculator.Calculate(
            0d, 0d, 1000d, 0d, 400d, 200d);

        Assert.Equal(new TimberSlopeAnnotationPoint(400d, 230d), geometry.Anchor);
        Assert.Equal(new TimberSlopeAnnotationPoint(340d, 230d), geometry.CapStart);
        Assert.Equal(new TimberSlopeAnnotationPoint(460d, 230d), geometry.CapEnd);
        Assert.Equal(new TimberSlopeAnnotationPoint(400d, 330d), geometry.StemEnd);
        Assert.Equal(new TimberSlopeAnnotationPoint(560d, 280d), geometry.TextPosition);
        Assert.Equal(0d, geometry.RotationRadians);
        Assert.Equal(
            TimberPostAnnotationGeometryCalculator.CapHalfLengthMm * 2d,
            Distance(geometry.CapStart, geometry.CapEnd));
        Assert.Equal(
            TimberPostAnnotationGeometryCalculator.StemLengthMm,
            Distance(geometry.Anchor, geometry.StemEnd));
    }

    [Fact]
    public void BlockDefinitionGeometryIsStrictlyLocalAroundZeroOrigin()
    {
        var local = TimberPostAnnotationGeometryCalculator.CreateLocal();

        Assert.Equal(new TimberSlopeAnnotationPoint(0d, 0d), local.Anchor);
        Assert.Equal(new TimberSlopeAnnotationPoint(-60d, 0d), local.CapStart);
        Assert.Equal(new TimberSlopeAnnotationPoint(60d, 0d), local.CapEnd);
        Assert.Equal(new TimberSlopeAnnotationPoint(0d, 100d), local.StemEnd);
        Assert.Equal(new TimberSlopeAnnotationPoint(160d, 50d), local.TextPosition);
        Assert.Equal(0d, local.RotationRadians);
        Assert.True(local.TextPosition.X > local.CapEnd.X);
    }

    [Fact]
    public void LocalToWorldTransformAppliesAnchorTranslationExactlyOnce()
    {
        var world = TimberPostAnnotationGeometryCalculator.TransformLocalToWorld(
            TimberPostAnnotationGeometryCalculator.CreateLocal(),
            anchorX: 10000d,
            anchorY: 20000d,
            rotationRadians: 0d);

        Assert.Equal(new TimberSlopeAnnotationPoint(10000d, 20000d), world.Anchor);
        Assert.Equal(new TimberSlopeAnnotationPoint(9940d, 20000d), world.CapStart);
        Assert.Equal(new TimberSlopeAnnotationPoint(10060d, 20000d), world.CapEnd);
        Assert.Equal(new TimberSlopeAnnotationPoint(10000d, 20100d), world.StemEnd);
        Assert.Equal(new TimberSlopeAnnotationPoint(10160d, 20050d), world.TextPosition);
    }

    [Fact]
    public void BlockReferenceTransformMatchesDirectWorldCalculation()
    {
        const double anchorX = 4200d;
        const double anchorY = 1700d;
        var direct = TimberPostAnnotationGeometryCalculator.Calculate(
            0d, 0d, 1000d, 1000d, anchorX, anchorY);
        var blockTransform = TimberPostAnnotationGeometryCalculator.TransformLocalToWorld(
            TimberPostAnnotationGeometryCalculator.CreateLocal(),
            direct.Anchor.X,
            direct.Anchor.Y,
            direct.RotationRadians);

        Assert.Equal(direct, blockTransform);
        Assert.NotEqual(anchorX, direct.Anchor.X);
        Assert.NotEqual(anchorY, direct.Anchor.Y);
        Assert.Equal(
            TimberPostAnnotationGeometryCalculator.AnnotationNormalOffsetMm,
            Distance(new TimberSlopeAnnotationPoint(anchorX, anchorY), direct.Anchor),
            precision: 6);
    }

    [Theory]
    [InlineData(0d, 0d, 1000d, 0d)]
    [InlineData(1000d, 0d, 0d, 0d)]
    [InlineData(0d, 0d, 0d, 1000d)]
    [InlineData(0d, 0d, 1000d, 1000d)]
    public void DifferentOrientationsKeepStablePerpendicularGeometry(
        double startX,
        double startY,
        double endX,
        double endY)
    {
        var first = TimberPostAnnotationGeometryCalculator.Calculate(
            startX, startY, endX, endY, 300d, 250d);
        var second = TimberPostAnnotationGeometryCalculator.Calculate(
            startX, startY, endX, endY, 300d, 250d);
        var capX = first.CapEnd.X - first.CapStart.X;
        var capY = first.CapEnd.Y - first.CapStart.Y;
        var stemX = first.StemEnd.X - first.Anchor.X;
        var stemY = first.StemEnd.Y - first.Anchor.Y;
        var sourceAnchor = new TimberSlopeAnnotationPoint(300d, 250d);
        var offsetX = first.Anchor.X - sourceAnchor.X;
        var offsetY = first.Anchor.Y - sourceAnchor.Y;

        Assert.Equal(first, second);
        Assert.Equal(
            TimberPostAnnotationGeometryCalculator.AnnotationNormalOffsetMm,
            Distance(sourceAnchor, first.Anchor),
            precision: 6);
        Assert.True(stemX * offsetX + stemY * offsetY > 0d);
        Assert.Equal(0d, capX * stemX + capY * stemY, precision: 6);
        Assert.Equal(first.Anchor.X, (first.CapStart.X + first.CapEnd.X) / 2d, precision: 6);
        Assert.Equal(first.Anchor.Y, (first.CapStart.Y + first.CapEnd.Y) / 2d, precision: 6);
        Assert.Equal(
            TimberPostAnnotationGeometryCalculator.CapHalfLengthMm * 2d,
            Distance(first.CapStart, first.CapEnd),
            precision: 6);
        Assert.Equal(
            TimberPostAnnotationGeometryCalculator.StemLengthMm,
            Distance(first.Anchor, first.StemEnd),
            precision: 6);
    }

    [Fact]
    public void PostCollisionPlacementUsesRealCombinedSymbolAndTextExtent()
    {
        const double elementLengthMm = 3000d;
        var placement = TimberSlopeAnnotationPlacementCalculator.Calculate(
            elementLengthMm,
            new TimberSlopeAnnotationLongitudinalInterval(1100d, 1900d),
            TimberPostAnnotationGeometryCalculator.CollisionHalfExtentMm);

        Assert.Equal(730d, placement.AnchorDistanceMm);
        Assert.NotEqual(elementLengthMm / 2d, placement.AnchorDistanceMm);
        Assert.False(placement.UsesPreferredPosition);
    }

    [Fact]
    public void FlipSlopeCannotChangePostVisualMeaning()
    {
        Assert.False(TimberSlopeAnnotationRules.CanFlipDirection(TimberElementType.Post, 0d));
        Assert.False(TimberSlopeAnnotationRules.CanFlipDirection(TimberElementType.Post, 35d));
        Assert.True(TimberSlopeAnnotationRules.CanFlipDirection(TimberElementType.Rafter, 35d));
    }

    [Fact]
    public void VisualDecisionDoesNotChangePostTechnicalMetadata()
    {
        var data = TimberElementDefaults.For(TimberElementType.Post) with
        {
            ElementId = "S1",
            SlopeDegrees = 0d,
            IsSlopeDirectionReversed = true,
            CuttingAllowanceMm = 200d,
        };
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
        };
        var before = JsonSerializer.Serialize(data, options);

        _ = TimberSlopeAnnotationRules.ResolveGlyphKind(data.ElementType, data.SlopeDegrees);
        _ = TimberSlopeAnnotationRules.ResolveDisplayAngleDegrees(data.ElementType, data.SlopeDegrees);
        var after = JsonSerializer.Serialize(data, options);

        Assert.Equal(before, after);
        Assert.Contains("\"ElementType\":\"Post\"", after, StringComparison.Ordinal);
        Assert.Contains("\"SlopeDegrees\":0", after, StringComparison.Ordinal);
        Assert.Contains("\"IsSlopeDirectionReversed\":true", after, StringComparison.Ordinal);
    }

    [Fact]
    public void GlyphKindValuesRemainExplicitAndStable()
    {
        Assert.Equal(0, (int)TimberSlopeGlyphKind.None);
        Assert.Equal(1, (int)TimberSlopeGlyphKind.HorizontalMarker);
        Assert.Equal(2, (int)TimberSlopeGlyphKind.DirectionalArrow);
        Assert.Equal(3, (int)TimberSlopeGlyphKind.PostPerpendicularMarker);
    }

    private static double Distance(TimberSlopeAnnotationPoint first, TimberSlopeAnnotationPoint second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
