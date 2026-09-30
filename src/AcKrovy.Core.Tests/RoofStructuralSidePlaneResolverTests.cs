using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofStructuralSidePlaneResolverTests
{
    [Theory]
    [InlineData(RoofRafterBoundaryRole.Hip, 0d, 1000d, 80d)]
    [InlineData(RoofRafterBoundaryRole.Hip, 1000d, 1000d, 120d)]
    [InlineData(RoofRafterBoundaryRole.Hip, 1732.050807568877d, 1000d, 140d)]
    [InlineData(RoofRafterBoundaryRole.Valley, 1000d, 1000d, 100d)]
    [InlineData(RoofRafterBoundaryRole.Valley, 1732.050807568877d, 1000d, 180d)]
    [InlineData(RoofRafterBoundaryRole.Valley, -1000d, -1000d, 160d)]
    public void PlumbSidePlane_HasExactPlanHalfWidth_ForGeneralApproachAngle(
        RoofRafterBoundaryRole role, double approachX, double approachY,
        double structuralWidth)
    {
        var axis = new RoofSegment3D(
            new RoofPoint3D(0d, 0d, 3000d),
            new RoofPoint3D(2000d, 0d, 4000d));
        var target = new RoofStructuralRafterTrimSource(7, role, axis,
            structuralWidth);
        var centerlineEndpoint = new RoofPoint2D(500d, 0d);
        var interior = new RoofPoint2D(
            centerlineEndpoint.X + approachX,
            centerlineEndpoint.Y + approachY);

        Assert.True(RoofStructuralSidePlaneResolver.TryCreatePlane(
            target, interior, out var plane, out var reason), reason);
        Assert.Equal(0d, plane!.PlaneNormal.Z);
        Assert.Equal(1d, Math.Sqrt(
            plane.PlaneNormal.X * plane.PlaneNormal.X +
            plane.PlaneNormal.Y * plane.PlaneNormal.Y), 8);
        var measuredOffset =
            (plane.PlanePoint.X - axis.Start.X) * plane.PlaneNormal.X +
            (plane.PlanePoint.Y - axis.Start.Y) * plane.PlaneNormal.Y;
        Assert.Equal(structuralWidth / 2d, measuredOffset, 8);
        Assert.Equal(0d, centerlineEndpoint.Y);

        // The line/plane intersection uses the full approach angle, never a
        // fixed subtraction of W/2 along the ordinary axis.
        var signedAtInterior =
            (interior.X - plane.PlanePoint.X) * plane.PlaneNormal.X +
            (interior.Y - plane.PlanePoint.Y) * plane.PlaneNormal.Y;
        var signedAtCenter =
            (centerlineEndpoint.X - plane.PlanePoint.X) * plane.PlaneNormal.X +
            (centerlineEndpoint.Y - plane.PlanePoint.Y) * plane.PlaneNormal.Y;
        var fraction = -signedAtCenter / (signedAtInterior - signedAtCenter);
        var cut = new RoofPoint2D(
            centerlineEndpoint.X + fraction * approachX,
            centerlineEndpoint.Y + fraction * approachY);
        Assert.Equal(0d,
            (cut.X - plane.PlanePoint.X) * plane.PlaneNormal.X +
            (cut.Y - plane.PlanePoint.Y) * plane.PlaneNormal.Y, 8);
        if (Math.Abs(approachX) > 1d)
        {
            var distanceAlongOrdinary = fraction *
                Math.Sqrt(approachX * approachX + approachY * approachY);
            Assert.True(distanceAlongOrdinary > structuralWidth / 2d);
        }
    }

    [Theory]
    [InlineData(0d, "StructuralPlanDirectionDegenerate")]
    [InlineData(-1d, "StructuralWidthInvalid")]
    public void InvalidStructuralInput_FailsWithReason(
        double width, string expectedReason)
    {
        var axis = width == 0d
            ? new RoofSegment3D(new RoofPoint3D(0, 0, 0),
                new RoofPoint3D(0, 0, 100))
            : new RoofSegment3D(new RoofPoint3D(0, 0, 0),
                new RoofPoint3D(1000, 0, 100));
        var target = new RoofStructuralRafterTrimSource(
            0, RoofRafterBoundaryRole.Hip, axis,
            width == 0d ? 80d : width);
        Assert.False(RoofStructuralSidePlaneResolver.TryCreatePlane(
            target, new RoofPoint2D(0, 100), out var cut, out var reason));
        Assert.Null(cut);
        Assert.Equal(expectedReason, reason);
    }
}
