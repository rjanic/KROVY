using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofLogicalGeneratedAnchorContextTests
{
    [Fact]
    public void RoofKindNeutralProviderInput_UsesSharedSuppressionSemantics()
    {
        var key = new RoofGeneratedMemberKey(
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            7);
        var geometry = new RoofGeneratedMemberGeometry(
            new RoofPoint3D(10d, 20d, 30d),
            new RoofPoint3D(40d, 50d, 60d));
        var context = RoofLogicalGeneratedAnchorContext.Create(
            [new RoofLogicalGeneratedAnchor(key, geometry)],
            [RoofGeneratedMemberOverride.Suppress(key)]);

        var resolved = context.Resolve(key);

        Assert.Equal(1, context.LogicalMemberCount);
        Assert.Equal(RoofLogicalGeneratedAnchorResolutionKind.VirtualSuppressed, resolved.Kind);
        Assert.Equal(geometry, resolved.Geometry);
    }

    [Fact]
    public void ExactLogicalSuppressedKey_ResolvesRawCanonicalSegment()
    {
        var layout = Layout(Solve(RoofKind.SimpleGable, 30d, 30d));
        var rafter = layout.Rafters.Single(item =>
            item.Face == RafterRoofFace.Face0 && item.StationIndex == 3);
        var key = RoofGeneratedMemberKey.From(rafter);
        var expected = RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 125d);
        var context = RoofLogicalGeneratedAnchorContext.FromSimpleGableLayout(
            layout,
            125d,
            [RoofGeneratedMemberOverride.Suppress(key, "R-17")]);

        var resolved = context.Resolve(key);

        Assert.Equal(RoofLogicalGeneratedAnchorResolutionKind.VirtualSuppressed, resolved.Kind);
        Assert.Equal(expected, resolved.Geometry);
    }

    [Fact]
    public void KeyAbsentFromCurrentLayout_ReturnsLogicalKeyAbsent()
    {
        var layout = Layout(Solve(RoofKind.SimpleGable, 30d, 30d));
        var absent = new RoofGeneratedMemberKey(
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            layout.StationCount);
        var context = RoofLogicalGeneratedAnchorContext.FromSimpleGableLayout(
            layout,
            0d,
            [RoofGeneratedMemberOverride.Suppress(absent)]);

        var resolved = context.Resolve(absent);

        Assert.Equal(RoofLogicalGeneratedAnchorResolutionKind.LogicalKeyAbsent, resolved.Kind);
        Assert.Null(resolved.Geometry);
    }

    [Fact]
    public void LogicalKeyWithoutSuppression_DoesNotProvideVirtualFallback()
    {
        var layout = Layout(Solve(RoofKind.SimpleGable, 30d, 30d));
        var key = RoofGeneratedMemberKey.From(layout.Rafters[0]);
        var context = RoofLogicalGeneratedAnchorContext.FromSimpleGableLayout(
            layout,
            0d,
            null);

        var resolved = context.Resolve(key);

        Assert.Equal(RoofLogicalGeneratedAnchorResolutionKind.NotSuppressed, resolved.Kind);
        Assert.Null(resolved.Geometry);
    }

    [Fact]
    public void SuppressionMustMatchExactFaceAndStationKey()
    {
        var layout = Layout(Solve(RoofKind.SimpleGable, 30d, 30d));
        var requested = new RoofGeneratedMemberKey(
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            2);
        var otherFace = requested with { RoofFace = RafterRoofFace.Face1 };
        var context = RoofLogicalGeneratedAnchorContext.FromSimpleGableLayout(
            layout,
            0d,
            [RoofGeneratedMemberOverride.Suppress(otherFace)]);

        var resolved = context.Resolve(requested);

        Assert.Equal(RoofLogicalGeneratedAnchorResolutionKind.NotSuppressed, resolved.Kind);
    }

    [Fact]
    public void SuppressionIdentityAndGeometryFields_DoNotAlterCanonicalSegment()
    {
        var layout = Layout(Solve(RoofKind.SimpleGable, 30d, 30d));
        var rafter = layout.Rafters.Single(item =>
            item.Face == RafterRoofFace.Face1 && item.StationIndex == 4);
        var key = RoofGeneratedMemberKey.From(rafter);
        var expected = RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 80d);
        var malformedGeometryOnSuppress = new RoofGeneratedMemberOverride(
            key,
            Suppressed: true,
            AlongMm: 900d,
            LateralMm: -250d,
            RotationRadians: 0.5d,
            StartOffsetMm: 100d,
            EndOffsetMm: -50d,
            ReservedElementId: "IGNORED-ID");
        var context = RoofLogicalGeneratedAnchorContext.FromSimpleGableLayout(
            layout,
            80d,
            [malformedGeometryOnSuppress]);

        var resolved = context.Resolve(key);

        Assert.Equal(RoofLogicalGeneratedAnchorResolutionKind.VirtualSuppressed, resolved.Kind);
        Assert.Equal(expected, resolved.Geometry);
    }

    [Theory]
    [InlineData(RoofKind.SimpleGable, 30d, 30d)]
    [InlineData(RoofKind.AsymmetricGable, 20d, 35d)]
    public void CurrentGableKinds_ResolveSuppressedLogicalAnchor(
        RoofKind kind,
        double face0Slope,
        double face1Slope)
    {
        var layout = Layout(Solve(kind, face0Slope, face1Slope));
        var rafter = layout.Rafters.Single(item =>
            item.Face == RafterRoofFace.Face0 && item.StationIndex == 5);
        var key = RoofGeneratedMemberKey.From(rafter);
        var context = RoofLogicalGeneratedAnchorContext.FromSimpleGableLayout(
            layout,
            0d,
            [RoofGeneratedMemberOverride.Suppress(key)]);

        var resolved = context.Resolve(key);

        Assert.Equal(RoofLogicalGeneratedAnchorResolutionKind.VirtualSuppressed, resolved.Kind);
        Assert.Equal(
            RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0d),
            resolved.Geometry);
    }

    private static SimpleGableRafterLayout Layout(SimpleGableRoofGeometry geometry)
    {
        var result = SimpleGableRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(800d, 100d));
        Assert.True(result.IsValid, result.Error.ToString());
        return result.Layout!;
    }

    private static SimpleGableRoofGeometry Solve(
        RoofKind kind,
        double face0Slope,
        double face1Slope)
    {
        var input = new RoofFootprintInput(
            [new(0d, 0d), new(10000d, 0d), new(10000d, 6000d), new(0d, 6000d)],
            IsClosed: true,
            HasCurvedSegments: false,
            IsPlanar: true);
        var validation = RoofFootprintValidator.Validate(input);
        Assert.True(validation.IsValid, validation.Error.ToString());
        Assert.True(RoofDirection2D.TryCreate(1d, 0d, out var ridgeDirection));
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(
                face0Slope,
                ridgeDirection,
                Face1SlopeDegrees: face1Slope),
            kind));
        Assert.True(solved.IsValid, solved.Error.ToString());
        return solved.Geometry!;
    }
}
