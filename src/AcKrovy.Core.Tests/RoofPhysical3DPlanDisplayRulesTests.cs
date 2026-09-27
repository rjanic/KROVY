using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Physical3D plan UX: source polyline is the sole outer eave; generated Face eave
/// edges stay invisible; owned 2D display never emits perimeter roles.
/// </summary>
public sealed class RoofPhysical3DPlanDisplayRulesTests
{
    [Fact]
    public void Physical3DFlattenedDisplay_ContainsNoPerimeterEaveRoles()
    {
        var (footprint, geometry) = Solve(10000, 6000, 30);
        var edges = RoofWireframe.CreateOwnedHipOrLegacy(geometry, 0d, physical3DEnabled: true);

        Assert.Equal(5, edges.Count);
        Assert.DoesNotContain(edges, edge =>
            RoofPhysical3DPlanDisplayRules.IsSourcePerimeterDisplayRole(edge.Role));
        Assert.Equal(4, edges.Count(edge =>
            edge.Role is >= RoofDisplayEdgeRole.Hip00 and <= RoofDisplayEdgeRole.Hip47));
        Assert.Equal(1, edges.Count(edge => HipRoofWireframe.IsRidgeRole(edge.Role)));
        Assert.All(edges, edge =>
        {
            Assert.Equal(0d, edge.Segment.Start.Z, 12);
            Assert.Equal(0d, edge.Segment.End.Z, 12);
        });

        // No generated plan segment may share XY with a topology eave.
        var topologyEaves = geometry.Topology.Edges
            .Where(edge => edge.Kind == RoofTopologyEdgeKind.Eave)
            .Select(edge => new RoofSegment3D(
                geometry.Topology.Nodes[edge.StartNodeIndex],
                geometry.Topology.Nodes[edge.EndNodeIndex]))
            .ToArray();
        Assert.All(edges, edge =>
            Assert.False(
                RoofPhysical3DPlanDisplayRules.IsCoincidentWithFootprintPerimeterXy(
                    edge.Segment,
                    topologyEaves)));
        Assert.True(RectangularRoofFootprintRules.IsRectangular(footprint));
    }

    [Fact]
    public void FaceEdgeVisibility_HidesOnlyEavePerimeterEdges()
    {
        var (footprint, geometry) = Solve(10000, 6000, 30);
        var elevation = RoofAbsoluteElevationRules.FromEntered(
            RoofAbsoluteElevationInputMode.Eave,
            3000d,
            3000d,
            30d,
            true);
        var model = RectangularHipRoofPhysical3DBuilder.TryBuild(
            "OWNER",
            footprint,
            geometry,
            elevation).Model!;

        Assert.Equal(4, model.Faces.Count);
        Assert.Equal(4, model.Eaves.Count);
        Assert.Equal(4, model.Hips.Count);
        Assert.Single(model.Ridges);

        foreach (var face in model.Faces)
        {
            var visibility = RoofPhysical3DPlanDisplayRules.FaceEdgeVisibility(
                face.Polygon,
                face.EaveSegment);
            Assert.Equal(4, visibility.Length);
            Assert.Contains(false, visibility); // at least the eave edge is hidden
            Assert.Contains(true, visibility); // hip/ridge Face edges remain available
            Assert.Equal(3000d, face.EaveSegment.Start.Z, 6);
            Assert.Equal(3000d, face.EaveSegment.End.Z, 6);
        }

        // Physical elevations unchanged by the presentation rule.
        Assert.All(model.Eaves, eave =>
        {
            Assert.Equal(3000d, eave.Segment.Start.Z, 9);
            Assert.Equal(3000d, eave.Segment.End.Z, 9);
        });
        Assert.All(model.Ridges, ridge =>
        {
            Assert.True(ridge.Segment.Start.Z > 3000d);
            Assert.True(ridge.Segment.End.Z > 3000d);
        });
    }

    [Fact]
    public void FilterOwnedPhysical3DPlanEdges_StripsLegacyPerimeterRoles()
    {
        var mixed = new RoofDisplayEdge[]
        {
            new(RoofDisplayEdgeRole.Eave0, Seg(0, 0, 10000, 0)),
            new(RoofDisplayEdgeRole.Hip00, Seg(0, 0, 3000, 3000)),
            new(RoofDisplayEdgeRole.HipRidge00, Seg(3000, 3000, 7000, 3000)),
            new(RoofDisplayEdgeRole.Eave1, Seg(0, 6000, 10000, 6000)),
        };

        var filtered = RoofPhysical3DPlanDisplayRules.FilterOwnedPhysical3DPlanEdges(mixed);
        Assert.Equal(2, filtered.Count);
        Assert.DoesNotContain(filtered, edge =>
            RoofPhysical3DPlanDisplayRules.IsSourcePerimeterDisplayRole(edge.Role));
    }

    [Fact]
    public void SuspendedIrregularThenRestoredRectangle_StillOmitsPerimeterDisplay()
    {
        var original = Validate(Input(Rectangle(10000, 6000)));
        var irregular = Validate(Input([
            new RoofPoint2D(0, 0),
            new RoofPoint2D(10000, 0),
            new RoofPoint2D(10000, 6000),
            new RoofPoint2D(1500, 4500),
        ]));
        var stored = RoofDefinitionPersistence.Create(
            Input(Rectangle(10000, 6000)),
            original,
            Solve(original, 30));
        var irregularClass = RoofDefinitionPersistence.Classify(
            Input([
                new RoofPoint2D(0, 0),
                new RoofPoint2D(10000, 0),
                new RoofPoint2D(10000, 6000),
                new RoofPoint2D(1500, 4500),
            ]),
            irregular,
            stored);
        var irregularGeom = Assert.IsType<HipRoofGeometry>(irregularClass.Geometry);
        Assert.False(
            RectangularSymmetricHipEligibility.Evaluate(irregular, irregularGeom).IsEligible);

        var suspendedDisplay = RoofWireframe.CreateOwnedHipOrLegacy(
            irregularGeom,
            0d,
            physical3DEnabled: true);
        Assert.DoesNotContain(suspendedDisplay, edge =>
            RoofPhysical3DPlanDisplayRules.IsSourcePerimeterDisplayRole(edge.Role));

        var restoredClass = RoofDefinitionPersistence.Classify(
            Input(Rectangle(10000, 6000)),
            original,
            RoofDefinitionPersistence.UpdateGeometry(
                stored,
                Input([
                    new RoofPoint2D(0, 0),
                    new RoofPoint2D(10000, 0),
                    new RoofPoint2D(10000, 6000),
                    new RoofPoint2D(1500, 4500),
                ]),
                irregularGeom));
        // After UpdateGeometry descriptor changes; restore classification from original store:
        var back = RoofDefinitionPersistence.Classify(
            Input(Rectangle(10000, 6000)),
            original,
            stored);
        var backGeom = Assert.IsType<HipRoofGeometry>(back.Geometry);
        Assert.True(RectangularSymmetricHipEligibility.Evaluate(original, backGeom).IsEligible);
        var recovered = RoofWireframe.CreateOwnedHipOrLegacy(backGeom, 0d, true);
        Assert.DoesNotContain(recovered, edge =>
            RoofPhysical3DPlanDisplayRules.IsSourcePerimeterDisplayRole(edge.Role));
        Assert.Equal(5, recovered.Count);
    }

    private static RoofSegment3D Seg(double x0, double y0, double x1, double y1) =>
        new(new RoofPoint3D(x0, y0, 0), new RoofPoint3D(x1, y1, 0));

    private static RoofPoint2D[] Rectangle(double width, double height) =>
        [new(0, 0), new(width, 0), new(width, height), new(0, height)];

    private static RoofFootprintInput Input(IReadOnlyList<RoofPoint2D> points) =>
        new(points, true, false, true);

    private static RoofFootprint Validate(RoofFootprintInput input)
    {
        var validation = RoofFootprintValidator.Validate(input);
        Assert.True(validation.IsValid, validation.Error.ToString());
        return validation.Footprint!;
    }

    private static (RoofFootprint Footprint, HipRoofGeometry Geometry) Solve(
        double length,
        double width,
        double pitch)
    {
        var footprint = Validate(Input(Rectangle(length, width)));
        return (footprint, Solve(footprint, pitch));
    }

    private static HipRoofGeometry Solve(RoofFootprint footprint, double slope)
    {
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(slope),
            RoofKind.Hip));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<HipRoofGeometry>(result.Geometry);
    }
}
