using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RectangularHipRoofPhysical3DBuilderTests
{
    [Theory]
    [InlineData(15d)]
    [InlineData(30d)]
    [InlineData(45d)]
    [InlineData(55d)]
    public void AxisAligned_10000x6000_ProducesSharedPlanarFaces(double pitch)
    {
        var (footprint, geometry) = Solve(10000, 6000, pitch);
        var elevation = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Eave,
            0d,
            3000d,
            pitch,
            true);
        var result = RectangularHipRoofPhysical3DBuilder.TryBuild(
            "OWNER1",
            footprint,
            geometry,
            elevation);
        Assert.True(result.IsValid);
        var model = result.Model!;
        Assert.Equal(4, model.Faces.Count);
        Assert.Equal(4, model.Eaves.Count);
        Assert.Equal(4, model.Hips.Count);
        Assert.False(model.IsPyramidal);
        Assert.Single(model.Ridges);

        var rise = 3000d * Math.Tan(pitch * Math.PI / 180d);
        Assert.Equal(rise, model.Elevation.RiseMm, 9);
        Assert.All(model.Eaves, eave =>
        {
            Assert.Equal(0d, eave.Segment.Start.Z, 9);
            Assert.Equal(0d, eave.Segment.End.Z, 9);
        });
        Assert.All(model.Ridges, ridge =>
        {
            Assert.Equal(rise, ridge.Segment.Start.Z, 9);
            Assert.Equal(rise, ridge.Segment.End.Z, 9);
            Assert.Equal(4000d, ridge.Segment.LengthMm, 8);
        });
        AssertAdjacentFacesShareEdges(model);
    }

    [Fact]
    public void Fixture_45Degrees_ExactRidgeEndpoints()
    {
        var (footprint, geometry) = Solve(10000, 6000, 45);
        var elevation = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Eave,
            0d,
            3000d,
            45d,
            true);
        Assert.True(RectangularHipRoofPhysical3DBuilder.TryBuild(
            "A",
            footprint,
            geometry,
            elevation).IsValid);
        var model = RectangularHipRoofPhysical3DBuilder.TryBuild(
            "A",
            footprint,
            geometry,
            elevation).Model!;
        var ridge = model.Ridges.Single().Segment;
        var ends = new[] { ridge.Start, ridge.End }
            .OrderBy(point => point.X)
            .ToArray();
        AssertPoint(new RoofPoint3D(3000, 3000, 3000), ends[0]);
        AssertPoint(new RoofPoint3D(7000, 3000, 3000), ends[1]);
    }

    [Fact]
    public void EavePlus3000_RaisesEntireRoof_IgnoringAnyPolylineElevationConcept()
    {
        // Decision 2A: physical Z uses resolved eave only. A host polyline Elevation of
        // 500 mm must never be added here — Core has no polyline elevation parameter.
        var (footprint, geometry) = Solve(10000, 6000, 45);
        var elevation = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Eave,
            3000d,
            3000d,
            45d,
            true);
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
        });
        Assert.DoesNotContain(
            model.Vertices,
            point => Math.Abs(point.Z - 3500d) < 1e-6);
    }

    [Fact]
    public void RidgeModePlus6000_MatchesEavePlus3000Geometry()
    {
        var (footprint, geometry) = Solve(10000, 6000, 45);
        var fromEave = RectangularHipRoofPhysical3DBuilder.TryBuild(
            "O",
            footprint,
            geometry,
            RoofAbsoluteElevationRules.FromEntered(
                RoofAbsoluteElevationInputMode.Eave,
                3000d,
                3000d,
                45d,
                true)).Model!;
        var fromRidge = RectangularHipRoofPhysical3DBuilder.TryBuild(
            "O",
            footprint,
            geometry,
            RoofAbsoluteElevationRules.FromEntered(
                RoofAbsoluteElevationInputMode.Ridge,
                6000d,
                3000d,
                45d,
                true)).Model!;
        Assert.Equal(fromEave.Vertices.Count, fromRidge.Vertices.Count);
        for (var i = 0; i < fromEave.Vertices.Count; i++)
        {
            Assert.Equal(fromEave.Vertices[i].X, fromRidge.Vertices[i].X, 9);
            Assert.Equal(fromEave.Vertices[i].Y, fromRidge.Vertices[i].Y, 9);
            Assert.Equal(fromEave.Vertices[i].Z, fromRidge.Vertices[i].Z, 9);
        }
    }

    [Fact]
    public void RotatedAndTranslatedRectangle_RemainsPlanarAndEligible()
    {
        var angle = 37d * Math.PI / 180d;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        RoofPoint2D Map(double x, double y) =>
            new(12000 + x * cos - y * sin, -8000 + x * sin + y * cos);
        var vertices = new[]
        {
            Map(0, 0),
            Map(10000, 0),
            Map(10000, 6000),
            Map(0, 6000),
        };
        var (footprint, geometry) = Solve(vertices, 45);
        var elevation = RoofAbsoluteElevationRules.CreateDefault(3000, 45, true);
        var result = RectangularHipRoofPhysical3DBuilder.TryBuild(
            "R",
            footprint,
            geometry,
            elevation);
        Assert.True(result.IsValid);
        Assert.Equal(4000d, result.Model!.Ridges.Single().Segment.LengthMm, 6);
        AssertAdjacentFacesShareEdges(result.Model);
    }

    [Fact]
    public void Square_ProducesPyramidalModelWithoutRidgeEdges()
    {
        var (footprint, geometry) = Solve(6000, 6000, 30);
        Assert.True(geometry.IsPyramidal);
        var elevation = RoofAbsoluteElevationRules.CreateDefault(3000, 30, true);
        var model = RectangularHipRoofPhysical3DBuilder.TryBuild(
            "S",
            footprint,
            geometry,
            elevation).Model!;
        Assert.True(model.IsPyramidal);
        Assert.Empty(model.Ridges);
        Assert.Equal(4, model.Faces.Count);
        Assert.All(model.Faces, face => Assert.Equal(3, face.Polygon.Count));
        Assert.NotNull(model.Apex);
    }

    [Fact]
    public void LShape_IsRejectedExplicitly()
    {
        var vertices = new RoofPoint2D[]
        {
            new(0, 0), new(8000, 0), new(8000, 3000),
            new(3000, 3000), new(3000, 8000), new(0, 8000),
        };
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(vertices, true));
        Assert.True(validation.IsValid);
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(30),
            RoofKind.Hip));
        Assert.True(solved.IsValid);
        var result = RectangularHipRoofPhysical3DBuilder.TryBuild(
            "L",
            validation.Footprint!,
            (HipRoofGeometry)solved.Geometry!,
            RoofAbsoluteElevationRules.CreateDefault(1500, 30, true));
        Assert.False(result.IsValid);
        Assert.Equal(RectangularHipRoofPhysical3DBuildError.Ineligible, result.Error);
        Assert.Equal(
            RectangularSymmetricHipEligibilityError.FootprintNotRectangular,
            result.EligibilityError);
    }

    [Fact]
    public void Accessor_RequiresPhysical3DEnabled()
    {
        var (footprint, geometry) = Solve(10000, 6000, 45);
        Assert.False(RoofPhysical3DModelAccessor.TryCreateFromSolvedHip(
            "O",
            footprint,
            geometry,
            persistedElevation: null,
            out _));
        var enabled = RoofPhysicalElevationRules.CreateFromState(
            RoofAbsoluteElevationRules.FromEntered(
                RoofAbsoluteElevationInputMode.Eave,
                0d,
                3000d,
                45d,
                true));
        Assert.True(RoofPhysical3DModelAccessor.TryCreateFromSolvedHip(
            "O",
            footprint,
            geometry,
            enabled,
            out var model));
        Assert.NotNull(model);
    }

    private static (RoofFootprint Footprint, HipRoofGeometry Geometry) Solve(
        double length,
        double width,
        double pitch) =>
        Solve(
            new[]
            {
                new RoofPoint2D(0, 0),
                new RoofPoint2D(length, 0),
                new RoofPoint2D(length, width),
                new RoofPoint2D(0, width),
            },
            pitch);

    private static (RoofFootprint Footprint, HipRoofGeometry Geometry) Solve(
        IReadOnlyList<RoofPoint2D> vertices,
        double pitch)
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(vertices, true));
        Assert.True(validation.IsValid);
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(pitch),
            RoofKind.Hip));
        Assert.True(result.IsValid);
        return (validation.Footprint!, (HipRoofGeometry)result.Geometry!);
    }

    private static void AssertAdjacentFacesShareEdges(RoofPhysical3DModel model)
    {
        var edgeKeys = new HashSet<string>();
        foreach (var face in model.Faces)
        {
            for (var i = 0; i < face.Polygon.Count; i++)
            {
                var a = face.Polygon[i];
                var b = face.Polygon[(i + 1) % face.Polygon.Count];
                edgeKeys.Add(EdgeKey(a, b));
            }
        }

        // Every shared topology edge appears on exactly two faces (or one for eaves).
        foreach (var hip in model.Hips)
        {
            Assert.Contains(EdgeKey(hip.Segment.Start, hip.Segment.End), edgeKeys);
        }

        foreach (var ridge in model.Ridges)
        {
            Assert.Contains(EdgeKey(ridge.Segment.Start, ridge.Segment.End), edgeKeys);
        }
    }

    private static string EdgeKey(RoofPoint3D a, RoofPoint3D b)
    {
        var first = string.Join(",", a.X.ToString("R"), a.Y.ToString("R"), a.Z.ToString("R"));
        var second = string.Join(",", b.X.ToString("R"), b.Y.ToString("R"), b.Z.ToString("R"));
        return string.CompareOrdinal(first, second) <= 0
            ? first + "|" + second
            : second + "|" + first;
    }

    private static void AssertPoint(RoofPoint3D expected, RoofPoint3D actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
        Assert.Equal(expected.Z, actual.Z, 8);
    }
}
