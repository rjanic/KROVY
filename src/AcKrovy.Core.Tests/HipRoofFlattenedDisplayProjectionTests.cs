using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Physical3DEnabled roofs must present a true flattened XY plan for owned 2D display
/// while physical 3D elevations remain ResolvedEave + topologyLocalZ.
/// </summary>
public sealed class HipRoofFlattenedDisplayProjectionTests
{
    [Theory]
    [InlineData(0d)]
    [InlineData(500d)]
    public void FlattenedOwnedDisplay_AllEndpointsShareSourceElevation(double sourceElevation)
    {
        var (footprint, geometry) = Solve(10000, 6000, 45);
        var edges = RoofWireframe.CreateOwnedHipOrLegacy(
            geometry,
            sourceElevation,
            physical3DEnabled: true);

        Assert.Equal(5, edges.Count);
        Assert.All(edges, edge =>
        {
            Assert.Equal(sourceElevation, edge.Segment.Start.Z, 12);
            Assert.Equal(sourceElevation, edge.Segment.End.Z, 12);
        });

        // Topology solver stays spatial — flatten is presentation only.
        Assert.Contains(geometry.Topology.Nodes, node => Math.Abs(node.Z - 3000d) < 1e-6);
        Assert.Contains(geometry.Topology.Nodes, node => Math.Abs(node.Z) < 1e-9);
    }

    [Fact]
    public void LegacySpatialOwnedDisplay_StillAddsLocalZToSourceElevation()
    {
        var (_, geometry) = Solve(10000, 6000, 45);
        var edges = RoofWireframe.CreateOwnedHipOrLegacy(
            geometry,
            sourceElevation: 500d,
            physical3DEnabled: false);

        Assert.Contains(edges, edge =>
            HipRoofWireframe.IsRidgeRole(edge.Role) &&
            Math.Abs(edge.Segment.Start.Z - 3500d) < 1e-6 &&
            Math.Abs(edge.Segment.End.Z - 3500d) < 1e-6);
        Assert.Contains(edges, edge =>
            edge.Role is >= RoofDisplayEdgeRole.Hip00 and <= RoofDisplayEdgeRole.Hip47 &&
            Math.Abs(edge.Segment.Start.Z - 500d) < 1e-6);
    }

    [Fact]
    public void Physical3D_EavePlus3000_IgnoresSourceElevation_While2DUsesIt()
    {
        var (footprint, geometry) = Solve(10000, 6000, 45);
        const double sourceElevation = 500d;
        var elevation = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Eave,
            3000d,
            halfRoofWidthMm: 3000d,
            pitchDegrees: 45d,
            physical3DEnabled: true,
            RoofPhysicalDisplayVisibility.Both);

        var display2D = RoofWireframe.CreateOwnedHipOrLegacy(
            geometry,
            sourceElevation,
            physical3DEnabled: true);
        Assert.All(display2D, edge =>
        {
            Assert.Equal(sourceElevation, edge.Segment.Start.Z, 12);
            Assert.Equal(sourceElevation, edge.Segment.End.Z, 12);
        });

        var model = RectangularHipRoofPhysical3DBuilder.TryBuild(
            "OWNER",
            footprint,
            geometry,
            elevation).Model!;
        Assert.All(model.Eaves, eave =>
        {
            Assert.Equal(3000d, eave.Segment.Start.Z, 9);
            Assert.Equal(3000d, eave.Segment.End.Z, 9);
        });
        Assert.All(model.Ridges, ridge =>
        {
            Assert.Equal(6000d, ridge.Segment.Start.Z, 9);
            Assert.Equal(6000d, ridge.Segment.End.Z, 9);
            Assert.Equal(4000d, ridge.Segment.LengthMm, 8);
        });
    }

    [Fact]
    public void DisplayVisibility_DoesNotAlterPhysicalElevationsOrFlattenPlane()
    {
        var (footprint, geometry) = Solve(10000, 6000, 45);
        var both = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Eave,
            3000d,
            3000d,
            45d,
            true,
            RoofPhysicalDisplayVisibility.Both);
        var planOnly = RoofAbsoluteElevationRules.WithDisplayVisibility(
            both,
            RoofPhysicalDisplayVisibility.Plan2D);
        var modelOnly = RoofAbsoluteElevationRules.WithDisplayVisibility(
            both,
            RoofPhysicalDisplayVisibility.Model3D);

        Assert.Equal(3000d, planOnly.ResolvedEaveRelativeElevationMm);
        Assert.Equal(6000d, planOnly.ResolvedRidgeRelativeElevationMm, 9);
        Assert.Equal(3000d, modelOnly.ResolvedEaveRelativeElevationMm);
        Assert.Equal(6000d, modelOnly.ResolvedRidgeRelativeElevationMm, 9);

        var modelBoth = RectangularHipRoofPhysical3DBuilder.TryBuild("A", footprint, geometry, both).Model!;
        var modelPlan = RectangularHipRoofPhysical3DBuilder.TryBuild("B", footprint, geometry, planOnly).Model!;
        var model3D = RectangularHipRoofPhysical3DBuilder.TryBuild("C", footprint, geometry, modelOnly).Model!;

        Assert.Equal(
            modelBoth.Ridges.Single().Segment.Start.Z,
            modelPlan.Ridges.Single().Segment.Start.Z,
            12);
        Assert.Equal(
            modelBoth.Ridges.Single().Segment.Start.Z,
            model3D.Ridges.Single().Segment.Start.Z,
            12);

        var edgesPlan = RoofWireframe.CreateOwnedHipOrLegacy(geometry, 0d, true);
        var edgesModel = RoofWireframe.CreateOwnedHipOrLegacy(geometry, 0d, true);
        Assert.Equal(
            RoofWireframe.BuildGenerationSignature(edgesPlan),
            RoofWireframe.BuildGenerationSignature(edgesModel));
    }

    [Fact]
    public void FlattenedProjection_PreservesPlanXyOfHipAndRidge()
    {
        var (_, geometry) = Solve(10000, 6000, 45);
        var spatial = HipRoofWireframe.Create(
            geometry,
            0d,
            RoofDisplayProjectionKind.SpatialLocalZ);
        var flat = HipRoofWireframe.Create(
            geometry,
            0d,
            RoofDisplayProjectionKind.FlattenedDrawingPlane);

        Assert.Equal(spatial.Count, flat.Count);
        foreach (var spatialEdge in spatial)
        {
            var flatEdge = Assert.Single(flat, edge => edge.Role == spatialEdge.Role);
            Assert.Equal(spatialEdge.Segment.Start.X, flatEdge.Segment.Start.X, 12);
            Assert.Equal(spatialEdge.Segment.Start.Y, flatEdge.Segment.Start.Y, 12);
            Assert.Equal(spatialEdge.Segment.End.X, flatEdge.Segment.End.X, 12);
            Assert.Equal(spatialEdge.Segment.End.Y, flatEdge.Segment.End.Y, 12);
            Assert.Equal(0d, flatEdge.Segment.Start.Z, 12);
            Assert.Equal(0d, flatEdge.Segment.End.Z, 12);
        }
    }

    private static (RoofFootprint Footprint, HipRoofGeometry Geometry) Solve(
        double length,
        double width,
        double pitch)
    {
        var footprint = Validate([
            new RoofPoint2D(0, 0),
            new RoofPoint2D(length, 0),
            new RoofPoint2D(length, width),
            new RoofPoint2D(0, width),
        ]);
        var geometry = Assert.IsType<HipRoofGeometry>(
            RoofGeometrySolver.Solve(
                new RoofDefinition(footprint, new RoofParameters(pitch), RoofKind.Hip)).Geometry);
        return (footprint, geometry);
    }

    private static RoofFootprint Validate(IReadOnlyList<RoofPoint2D> points)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(points, true));
        Assert.True(validation.IsValid, validation.Error.ToString());
        return validation.Footprint!;
    }
}
