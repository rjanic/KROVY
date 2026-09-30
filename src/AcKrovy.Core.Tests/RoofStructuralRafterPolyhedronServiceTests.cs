using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofStructuralRafterPolyhedronServiceTests
{
    [Theory]
    [InlineData(30d)]
    [InlineData(45d)]
    [InlineData(60d)]
    public void HipTopProfile_HasOnePlanarFace_NotTwoRoofBevels(double pitch)
    {
        var fixture = CreateFixture(pitch, false);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var result, out var reason), reason);
        Assert.Single(result!.Geometry.Faces,
            face => face.Kind == RoofStructuralPhysicalFaceKind.Top);
        Assert.DoesNotContain(result.Geometry.Faces, face => face.Kind is
            RoofStructuralPhysicalFaceKind.TopLeft or
            RoofStructuralPhysicalFaceKind.TopRight);
        var body = result.Geometry;
        var left = Assert.IsType<RoofSegment3D>(body.UpperLeftEdge);
        var right = Assert.IsType<RoofSegment3D>(body.UpperRightEdge);
        var center = Assert.IsType<RoofSegment3D>(body.PhysicalTopAxis);
        Assert.Equal(body.UpperAxis.Start.X, center.Start.X, 6);
        Assert.Equal(body.UpperAxis.Start.Y, center.Start.Y, 6);
        Assert.True(center.Start.Z < body.UpperAxis.Start.Z);
        Assert.True(center.End.Z < body.UpperAxis.End.Z);
        Assert.Equal((left.Start.Z + right.Start.Z) / 2d, center.Start.Z, 6);
        Assert.Equal((left.End.Z + right.End.Z) / 2d, center.End.Z, 6);
        Assert.Equal(120d, left.Start.DistanceTo(right.Start), 5);
        Assert.Equal(120d, left.End.DistanceTo(right.End), 5);
        var top = Assert.Single(body.Faces,
            face => face.Kind == RoofStructuralPhysicalFaceKind.Top);
        var longitudinal = new RoofPoint3D(left.End.X - left.Start.X,
            left.End.Y - left.Start.Y, left.End.Z - left.Start.Z);
        var transverse = new RoofPoint3D(right.Start.X - left.Start.X,
            right.Start.Y - left.Start.Y, right.Start.Z - left.Start.Z);
        var planeNormal = new RoofPoint3D(
            longitudinal.Y * transverse.Z - longitudinal.Z * transverse.Y,
            longitudinal.Z * transverse.X - longitudinal.X * transverse.Z,
            longitudinal.X * transverse.Y - longitudinal.Y * transverse.X);
        var normalLength = Math.Sqrt(planeNormal.X * planeNormal.X +
            planeNormal.Y * planeNormal.Y + planeNormal.Z * planeNormal.Z);
        Assert.True(normalLength > 1d);
        Assert.All(top.Vertices, point => Assert.InRange(Math.Abs(
            (point.X - left.Start.X) * planeNormal.X +
            (point.Y - left.Start.Y) * planeNormal.Y +
            (point.Z - left.Start.Z) * planeNormal.Z) / normalLength, 0d, 1e-5));
        var adjacent = fixture.Topology.Edges[fixture.EdgeIndex].FaceIndices
            .Select(index => fixture.Topology.Faces[index]).ToArray();
        foreach (var (upperEdge, side) in new[] { (left, 1), (right, -1) })
        {
            var roofFace = Assert.Single(adjacent, face => Math.Sign(
                SignedOffset(body.UpperAxis, new RoofPoint3D(
                    face.BoundaryNodeIndices.Average(index => fixture.Topology.Nodes[index].X),
                    face.BoundaryNodeIndices.Average(index => fixture.Topology.Nodes[index].Y),
                    0d))) == side);
            Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                fixture.Topology, roofFace, out var normal));
            var origin = fixture.Topology.Nodes[roofFace.BoundaryNodeIndices[0]];
            foreach (var point in new[] { upperEdge.Start, upperEdge.End })
                Assert.InRange(Math.Abs(normal.X * (point.X - origin.X) +
                    normal.Y * (point.Y - origin.Y) +
                    normal.Z * (point.Z - origin.Z - 3000d)), 0d, 1e-5);
        }
        foreach (var roofFace in adjacent)
        {
            Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                fixture.Topology, roofFace, out var normal));
            var origin = fixture.Topology.Nodes[roofFace.BoundaryNodeIndices[0]];
            Assert.All(body.BodyVertices, point => Assert.True(
                normal.X * (point.X - origin.X) +
                normal.Y * (point.Y - origin.Y) +
                normal.Z * (point.Z - origin.Z - 3000d) <= 1e-5));
        }
        var source = Assert.Single(result.ConvexHalves).SourcePrismVertices;
        Assert.Equal(body.WidthMm, source[0].DistanceTo(source[1]), 5);
        Assert.Equal(body.PhysicalVerticalHeightMm,
            source[0].Z - source[4].Z, 5);
        Assert.Equal(body.PhysicalVerticalHeightMm,
            source[1].Z - source[5].Z, 5);
        Assert.Equal(source[0].Z, source[1].Z, 5);
    }

    [Theory]
    [InlineData(30d)]
    [InlineData(45d)]
    [InlineData(60d)]
    public void HipAutomaticHeight_CoversEntireOrdinaryCutFaceOnEachSide(
        double pitch)
    {
        var fixture = CreateFixture(pitch, false);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var result, out var reason), reason);
        var body = result!.Geometry;
        var contacts = fixture.Request.OrdinaryMembers.Where(member =>
            member.StructuralCut?.TopologyEdgeIndex == fixture.EdgeIndex).ToArray();
        Assert.NotEmpty(contacts);
        var requiredDepth = 0d;
        foreach (var member in contacts)
        {
            var face = Assert.Single(fixture.Topology.Faces, item =>
                item.SourceEdgeIndex == member.SourceFaceIndex);
            Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                fixture.Topology, face, out var normal));
            var origin = fixture.Topology.Nodes[face.BoundaryNodeIndices[0]];
            Assert.Equal(120d, member.StructuralCut!.StructuralWidthMm, 5);
            Assert.Equal(60d, Math.Abs(SignedOffset(body.UpperAxis,
                member.StructuralCut.PlanePoint)), 5);
            foreach (var point in member.StructuralCut.CutFaceVertices)
            {
                var topZ = origin.Z + 3000d -
                    (normal.X * (point.X - origin.X) +
                     normal.Y * (point.Y - origin.Y)) / normal.Z;
                var depth = topZ - point.Z;
                Assert.InRange(depth, -1e-5, body.PhysicalVerticalHeightMm + 1e-5);
                requiredDepth = Math.Max(requiredDepth, depth);
            }
        }
        Assert.Equal(requiredDepth, body.RequiredAutomaticHeightMm, 5);
        Assert.Equal(requiredDepth, body.PhysicalVerticalHeightMm, 5);
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical, false)]
    [InlineData(LowerEndCutMode.Perpendicular, false)]
    [InlineData(LowerEndCutMode.Horizontal, false)]
    [InlineData(LowerEndCutMode.Vertical, true)]
    [InlineData(LowerEndCutMode.Perpendicular, true)]
    [InlineData(LowerEndCutMode.Horizontal, true)]
    public void PhysicalLowerEnd_InheritsOrdinaryMode_AndKeepsRoofAndNode(
        LowerEndCutMode mode, bool valley)
    {
        var fixture = CreateFixture(35d, valley, lowerEndCutMode: mode);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var result, out var reason), reason);
        var body = result!.Geometry;
        Assert.Equal(mode, result.LowerEndCutMode);
        if (valley)
            Assert.Contains(body.BodyVertices,
                point => point.DistanceTo(body.UpperAxis.End) <= 1e-5);
        else
        {
            Assert.NotNull(body.PhysicalTopAxis);
            Assert.True(body.PhysicalTopAxis!.End.Z < body.UpperAxis.End.Z);
            Assert.DoesNotContain(body.BodyVertices,
                point => point.DistanceTo(body.UpperAxis.End) <= 1e-5);
        }
        Assert.All(result.ConvexHalves, half =>
        {
            if (valley)
                Assert.Contains(half.ClippedBodyVertices,
                    point => point.DistanceTo(body.UpperAxis.End) <= 1e-5);
            var face = Assert.Single(fixture.Topology.Faces,
                item => item.SourceEdgeIndex == half.AdjacentSourceFaceIndex);
            Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                fixture.Topology, face, out var normal));
            var origin = fixture.Topology.Nodes[face.BoundaryNodeIndices[0]];
            Assert.All(half.ClippedBodyVertices, point =>
                Assert.True(normal.X * (point.X - origin.X) +
                    normal.Y * (point.Y - origin.Y) +
                    normal.Z * (point.Z - origin.Z - 3000d) <= 1e-5));
        });
        Assert.All(body.BodyVertices, point =>
        {
            Assert.All(result.EaveClipPlanes, plane =>
                Assert.True(Dot(point, plane) >= -1e-5));
            Assert.All(result.LowerEndClipPlanes, plane =>
                Assert.True(Dot(point, plane) >= -1e-5));
        });
        var lowerPlane = result.LowerEndClipPlanes[0];
        if (mode == LowerEndCutMode.Horizontal)
        {
            Assert.Single(result.LowerEndClipPlanes);
            var expectedCutZ = valley ? 3000d : body.PhysicalTopAxis!.Start.Z;
            Assert.Equal(expectedCutZ, lowerPlane.Point.Z, 5);
            Assert.Equal(new RoofPoint3D(0d, 0d, 1d), lowerPlane.RetainedNormal);
            Assert.All(body.BodyVertices, point =>
                Assert.True(point.Z >= expectedCutZ - 1e-5));
        }
        else
        {
            Assert.Equal(2, result.LowerEndClipPlanes.Count);
            if (mode == LowerEndCutMode.Vertical)
                Assert.Equal(0d, lowerPlane.RetainedNormal.Z, 5);
            else
                Assert.True(lowerPlane.RetainedNormal.Z > 0d);
            var lowerEdges = fixture.Request.OrdinaryMembers
                .Where(member => member.StructuralCut?.TopologyEdgeIndex == fixture.EdgeIndex)
                .Select(member => Assert.IsType<RoofSegment3D>(
                    member.StructuralCut!.LowerContactEdge)).ToArray();
            Assert.All(lowerEdges.SelectMany(edge =>
                new[] { edge.Start, edge.End }), point =>
                Assert.True(Dot(point, lowerPlane) >= -1e-5));
            // At the canonical eave corner the two boundary planes can
            // reduce the end face to an edge/point. A finite end cap would
            // necessarily remove the corner from the plan projection.
            Assert.All(body.Faces.Where(face =>
                    face.Kind == RoofStructuralPhysicalFaceKind.LowerEndProfile),
                face => Assert.All(face.Vertices, point =>
                    Assert.InRange(Math.Abs(Dot(point, lowerPlane)), 0d, 1e-5)));
            var bottomPlane = result.LowerEndClipPlanes[1];
            Assert.Equal(new RoofPoint3D(0d, 0d, 1d),
                bottomPlane.RetainedNormal);
            Assert.Equal(EaveNearestOrdinary(fixture.Request,
                fixture.EdgeIndex, body.UpperAxis)
                .SelectMany(member => member.SolidVertices).Min(point => point.Z),
                bottomPlane.Point.Z, 5);
            Assert.Equal(bottomPlane.Point.Z,
                body.BodyVertices.Min(point => point.Z), 5);
            Assert.All(body.BodyVertices,
                point => Assert.True(Dot(point, lowerPlane) >= -1e-5));
        }
        Assert.All(fixture.Request.OrdinaryMembers, member =>
        {
            Assert.Equal(0d, member.PlanAxis.Start.Z);
            Assert.Equal(0d, member.PlanAxis.End.Z);
        });
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical)]
    [InlineData(LowerEndCutMode.Perpendicular)]
    public void LowerEnd_UsesRealBottomContactOnBothSides_IndependentOfVertexOrder(
        LowerEndCutMode mode)
    {
        var fixture = CreateFixture(35d, false, lowerEndCutMode: mode);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var original, out var reason), reason);
        var contacts = fixture.Request.OrdinaryMembers.Where(member =>
            member.StructuralCut?.TopologyEdgeIndex == fixture.EdgeIndex).ToArray();
        Assert.True(contacts.Length >= 2);
        var axis = original!.Geometry.UpperAxis;
        Assert.Equal(2, contacts.Select(member =>
            Math.Sign(SignedOffset(axis, member.StructuralCut!.PlanePoint)))
            .Distinct().Count());
        var direction = new RoofPoint3D(axis.End.X - axis.Start.X,
            axis.End.Y - axis.Start.Y, 0d);
        var selected = contacts.GroupBy(member =>
            Math.Sign(SignedOffset(axis, member.StructuralCut!.PlanePoint)))
            .Select(group => group.OrderBy(member =>
            {
                var edge = member.StructuralCut!.LowerContactEdge!;
                var midpoint = new RoofPoint3D(
                    (edge.Start.X + edge.End.X) / 2d,
                    (edge.Start.Y + edge.End.Y) / 2d, 0d);
                return (midpoint.X - axis.Start.X) * direction.X +
                    (midpoint.Y - axis.Start.Y) * direction.Y;
            }).First()).ToArray();
        var endpoints = selected.SelectMany(member =>
        {
            var edge = member.StructuralCut!.LowerContactEdge!;
            return new[] { edge.Start, edge.End };
        }).ToArray();
        var plane = original.LowerEndClipPlanes[0];
        Assert.All(endpoints, point => Assert.True(Dot(point, plane) >= -1e-5));
        Assert.Equal(selected.SelectMany(member => member.SolidVertices)
                .Min(point => point.Z),
            original.LowerEndClipPlanes[1].Point.Z, 5);

        var shuffled = fixture.Request.OrdinaryMembers.Select(member =>
            member.StructuralCut is { } cut
                ? member with { StructuralCut = cut with
                {
                    CutFaceVertices = cut.CutFaceVertices.Reverse().ToArray(),
                    BottomFaceVertices = cut.BottomFaceVertices?.Reverse().ToArray(),
                } }
                : member).Reverse().ToArray();
        var reorderedRequest = fixture.Request with { OrdinaryMembers = shuffled };
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            reorderedRequest, out var reordered, out reason), reason);
        var other = reordered!.LowerEndClipPlanes[0];
        Assert.Equal(0d, Dot(other.Point, plane), 5);
        Assert.Equal(plane.RetainedNormal.X, other.RetainedNormal.X, 6);
        Assert.Equal(plane.RetainedNormal.Y, other.RetainedNormal.Y, 6);
        Assert.Equal(plane.RetainedNormal.Z, other.RetainedNormal.Z, 6);
        Assert.Equal(original.LowerEndClipPlanes[1].Point.Z,
            reordered.LowerEndClipPlanes[1].Point.Z, 6);
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical)]
    [InlineData(LowerEndCutMode.Perpendicular)]
    public void HipTwoPlaneCut_RemovesVisibleWasteBelowOrdinaryBottom(
        LowerEndCutMode mode)
    {
        var fixture = CreateFixture(35d, false, lowerEndCutMode: mode);
        var request = fixture.Request with
        {
            HeightMode = RoofStructuralHeightMode.Explicit,
            ExplicitVerticalHeightMm = 420d,
        };
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            request, out var model, out var reason), reason);
        var result = model!;
        Assert.Equal(2, result.LowerEndClipPlanes.Count);
        var endFace = result.LowerEndClipPlanes[0];
        var bottomTrim = result.LowerEndClipPlanes[1];
        var axis = result.Geometry.UpperAxis;
        var dx = axis.End.X - axis.Start.X;
        var dy = axis.End.Y - axis.Start.Y;
        var dz = axis.End.Z - axis.Start.Z;
        var axisLength = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        var normalLength = mode == LowerEndCutMode.Vertical
            ? Math.Sqrt(dx * dx + dy * dy) : axisLength;
        Assert.Equal(dx / normalLength,
            endFace.RetainedNormal.X, 6);
        Assert.Equal(dy / normalLength,
            endFace.RetainedNormal.Y, 6);
        Assert.Equal(mode == LowerEndCutMode.Vertical ? 0d : dz / axisLength,
            endFace.RetainedNormal.Z, 6);
        Assert.Equal(new RoofPoint3D(0d, 0d, 1d),
            bottomTrim.RetainedNormal);
        var relevant = EaveNearestOrdinary(request, fixture.EdgeIndex, axis);
        var ordinaryLowestZ = relevant.SelectMany(member => member.SolidVertices)
            .Min(point => point.Z);
        var contactLowestZ = relevant.SelectMany(member =>
        {
            var edge = member.StructuralCut!.LowerContactEdge!;
            return new[] { edge.Start.Z, edge.End.Z };
        }).Min();
        Assert.True(ordinaryLowestZ < contactLowestZ - 1d);
        Assert.Equal(ordinaryLowestZ, bottomTrim.Point.Z, 5);
        Assert.Equal(ordinaryLowestZ,
            result.Geometry.BodyVertices.Min(point => point.Z), 5);
        Assert.Contains(result.Geometry.Faces, face =>
            face.Kind == RoofStructuralPhysicalFaceKind.BottomTrim);
        Assert.All(result.ConvexHalves.SelectMany(half => half.ClippedBodyVertices),
            point => Assert.True(point.Z >= ordinaryLowestZ - 1e-5));

        // Reconstruct the same source halves without the second plane. The
        // excess below the red WCS plane must be real, not just a parameter.
        var withoutBottomTrim = result.EaveClipPlanes.Append(endFace)
            .Select(plane => new RoofConvexPrismPlaneClipper.Plane(
                plane.Point, plane.RetainedNormal)).ToArray();
        var untrimmedFloors = new List<double>();
        foreach (var half in result.ConvexHalves)
        {
            Assert.True(RoofConvexPrismPlaneClipper.TryClip(
                half.SourcePrismVertices, withoutBottomTrim,
                out var untrimmed, allowNoOpPlanes: true));
            untrimmedFloors.Add(untrimmed!.Body.Min(point => point.Z));
        }
        Assert.True(untrimmedFloors.Min() < ordinaryLowestZ - 1d);
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical)]
    [InlineData(LowerEndCutMode.Perpendicular)]
    public void HipBottomTrim_DoesNotShortenEavePlanProjection(
        LowerEndCutMode mode)
    {
        var fixture = CreateFixture(35d, false, lowerEndCutMode: mode);
        var request = fixture.Request with
        {
            HeightMode = RoofStructuralHeightMode.Explicit,
            ExplicitVerticalHeightMm = 420d,
        };
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            request, out var result, out var reason), reason);
        var axis = result!.Geometry.UpperAxis;
        var alongX = axis.End.X - axis.Start.X;
        var alongY = axis.End.Y - axis.Start.Y;
        Assert.All(result.ConvexHalves, half =>
        {
            var sourceStart = half.SourcePrismVertices[0];
            Assert.True((sourceStart.X - axis.Start.X) * alongX +
                (sourceStart.Y - axis.Start.Y) * alongY < -1d);
            Assert.Contains(half.ClippedBodyVertices, point =>
                Math.Abs(point.X - axis.Start.X) <= 1e-5 &&
                Math.Abs(point.Y - axis.Start.Y) <= 1e-5);
            var beforePlanes = result.EaveClipPlanes
                .Append(result.LowerEndClipPlanes[0])
                .Append(result.RidgeClipPlane)
                .Select(plane => new RoofConvexPrismPlaneClipper.Plane(
                    plane.Point, plane.RetainedNormal)).ToArray();
            Assert.True(RoofConvexPrismPlaneClipper.TryClip(
                half.SourcePrismVertices, beforePlanes, out var before,
                allowNoOpPlanes: true));
            AssertSamePlanProjection(before!.Body, half.ClippedBodyVertices);
        });
        var bottom = result.LowerEndClipPlanes[1];
        Assert.Equal(new RoofPoint3D(0d, 0d, 1d), bottom.RetainedNormal);
        Assert.Equal(bottom.Point.Z,
            result.Geometry.BodyVertices.Min(point => point.Z), 5);
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical)]
    [InlineData(LowerEndCutMode.Perpendicular)]
    public void HipChangingOrdinaryLowestZ_DoesNotMoveCanonicalEaveEnd(
        LowerEndCutMode mode)
    {
        var shallow = CreateFixture(35d, false, lowerEndCutMode: mode,
            ordinaryHeightMm: 160d);
        var deep = CreateFixture(35d, false, lowerEndCutMode: mode,
            ordinaryHeightMm: 180d);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            shallow.Request, out var first, out var reason), reason);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            deep.Request, out var second, out reason), reason);
        Assert.NotEqual(first!.LowerEndClipPlanes[1].Point.Z,
            second!.LowerEndClipPlanes[1].Point.Z);
        Assert.Equal(first.Geometry.UpperAxis.Start,
            second.Geometry.UpperAxis.Start);
        AssertSamePlanProjection(first.Geometry.BodyVertices,
            second.Geometry.BodyVertices);
    }

    [Theory]
    [InlineData(30d)]
    [InlineData(35d)]
    [InlineData(45d)]
    [InlineData(60d)]
    public void HipHorizontal_CutsAtPhysicalTopCenter_WithoutShorteningEavePlan(
        double pitch)
    {
        var fixture = CreateFixture(pitch, false,
            lowerEndCutMode: LowerEndCutMode.Horizontal);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var result, out var reason), reason);
        Assert.Single(result!.LowerEndClipPlanes);
        var axis = result.Geometry.UpperAxis;
        var physicalTop = Assert.IsType<RoofSegment3D>(
            result.Geometry.PhysicalTopAxis);
        var cut = result.LowerEndClipPlanes[0];
        Assert.Equal(physicalTop.Start.Z, cut.Point.Z, 5);
        Assert.True(cut.Point.Z < axis.Start.Z - 1d);
        Assert.Equal(new RoofPoint3D(0d, 0d, 1d), cut.RetainedNormal);
        var half = Assert.Single(result.ConvexHalves);
        var source = half.SourcePrismVertices;
        Assert.Equal(60d, SignedOffset(axis, source[0]), 5);
        Assert.Equal(-60d, SignedOffset(axis, source[1]), 5);
        Assert.Contains(result.Geometry.BodyVertices, point =>
            Math.Abs(point.X - axis.Start.X) <= 1e-5 &&
            Math.Abs(point.Y - axis.Start.Y) <= 1e-5 &&
            Math.Abs(point.Z - cut.Point.Z) <= 1e-5);
        var beforePlanes = result.EaveClipPlanes
            .Append(result.RidgeClipPlane)
            .Select(plane => new RoofConvexPrismPlaneClipper.Plane(
                plane.Point, plane.RetainedNormal)).ToArray();
        Assert.True(RoofConvexPrismPlaneClipper.TryClip(source, beforePlanes,
            out var before, allowNoOpPlanes: true));
        AssertSamePlanProjection(before!.Body, half.ClippedBodyVertices);
    }

    [Theory]
    [InlineData(30d, 184.75208614068d)]
    [InlineData(45d, 226.27416997970d)]
    public void PerpendicularVerticalStructuralCut_UsesActualFaceSpan(
        double pitch, double expectedSpan)
    {
        const double ordinaryHeight = 160d;
        const double length = 1000d;
        var radians = pitch * Math.PI / 180d;
        var rise = length * Math.Tan(radians);
        var bottomX = ordinaryHeight * Math.Sin(radians);
        var bottomZ = -ordinaryHeight * Math.Cos(radians);
        var source = new[]
        {
            new RoofPoint3D(0d, -40d, 0d),
            new RoofPoint3D(0d, 40d, 0d),
            new RoofPoint3D(length, -40d, rise),
            new RoofPoint3D(length, 40d, rise),
            new RoofPoint3D(bottomX, -40d, bottomZ),
            new RoofPoint3D(bottomX, 40d, bottomZ),
            new RoofPoint3D(length + bottomX, -40d, rise + bottomZ),
            new RoofPoint3D(length + bottomX, 40d, rise + bottomZ),
        };
        Assert.True(RoofConvexPrismPlaneClipper.TryClip(source,
            [new RoofConvexPrismPlaneClipper.Plane(
                new RoofPoint3D(500d, 0d, 0d), new RoofPoint3D(1d, 0d, 0d))],
            out var clipped));
        var face = Assert.Single(clipped!.CutFaces);
        var actualSpan = face.Max(point => point.Z) - face.Min(point => point.Z);
        Assert.Equal(expectedSpan, actualSpan, 6);
    }

    [Theory]
    [InlineData(30d)]
    [InlineData(45d)]
    public void Hip_UsesActualOrdinaryCutFaces_AndProducesOneTopFace(double pitch)
    {
        var fixture = CreateFixture(pitch, false);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var result, out var reason), reason);
        Assert.NotNull(result);
        var body = result!.Geometry;
        Assert.Equal(RoofStructuralRole.Hip, body.Role);
        Assert.Equal(3000d, body.UpperAxis.Start.Z, 5);
        Assert.Equal(120d, body.WidthMm);
        Assert.Equal(2, result.EaveBoundaryEdgeIndices.Count);
        Assert.Equal(body.RequiredAutomaticHeightMm,
            body.PhysicalVerticalHeightMm, 5);
        Assert.Single(body.Faces,
            face => face.Kind == RoofStructuralPhysicalFaceKind.Top);
        Assert.Contains(body.Faces, face => face.Kind == RoofStructuralPhysicalFaceKind.SideLeft);
        Assert.Contains(body.Faces, face => face.Kind == RoofStructuralPhysicalFaceKind.SideRight);
        Assert.All(body.BodyVertices, point => Assert.True(
            point.Z <= body.UpperAxis.End.Z + 1e-5));
        Assert.True(result.MaximumOrdinaryCutVerticalSpanMm > 0d);
        var actualCuts = fixture.Request.OrdinaryMembers.Where(member =>
            member.StructuralCut?.TopologyEdgeIndex == fixture.EdgeIndex)
            .Select(member => member.StructuralCut!.CutFaceVertices).ToArray();
        Assert.Equal(actualCuts.Max(cut =>
            cut.Max(point => point.Z) - cut.Min(point => point.Z)),
            result.MaximumOrdinaryCutVerticalSpanMm, 5);
        // A diagonal Hip side adds the top-edge rise across the ordinary width.
        var expectedObliqueSpan = 160d / Math.Cos(pitch * Math.PI / 180d) +
            80d * Math.Tan(pitch * Math.PI / 180d);
        Assert.InRange(result.MaximumOrdinaryCutVerticalSpanMm,
            expectedObliqueSpan - 1e-3, expectedObliqueSpan + 1e-3);
        Assert.Single(result.ConvexHalves);
        Assert.Equal(0, result.ConvexHalves[0].SideSign);
        foreach (var half in result.ConvexHalves)
        {
            var v = half.SourcePrismVertices;
            Assert.Equal(8, v.Count);
            Assert.Equal(v[2].X - v[0].X, v[3].X - v[1].X, 6);
            Assert.Equal(v[2].Y - v[0].Y, v[3].Y - v[1].Y, 6);
            Assert.Equal(v[2].Z - v[0].Z, v[3].Z - v[1].Z, 6);
            Assert.Equal(v[2].Z - v[0].Z, v[6].Z - v[4].Z, 6);
            Assert.Equal(60d, Math.Abs(SignedOffset(body.UpperAxis, v[0])), 5);
            Assert.Equal(60d, Math.Abs(SignedOffset(body.UpperAxis, v[1])), 5);
            Assert.True(v[2].DistanceTo(body.UpperAxis.End) > 0d);
        }
        Assert.Equal(2, result.EaveClipPlanes.Count);
        Assert.Equal(0d, result.RidgeClipPlane.RetainedNormal.Z);
        Assert.All(result.EaveClipPlanes,
            plane => Assert.Equal(0d, plane.RetainedNormal.Z));
        Assert.All(body.BodyVertices, point =>
        {
            foreach (var plane in result.EaveClipPlanes.Append(result.RidgeClipPlane))
                Assert.True(Dot(point, plane) >= -1e-5);
        });
        var edge = fixture.Topology.Edges[fixture.EdgeIndex];
        var anchor = edge.StartNodeIndex < fixture.Topology.BoundaryVertexCount
            ? edge.StartNodeIndex : edge.EndNodeIndex;
        Assert.Equal(fixture.Topology.Nodes[anchor].X, body.UpperAxis.Start.X, 6);
        Assert.Equal(fixture.Topology.Nodes[anchor].Y, body.UpperAxis.Start.Y, 6);
    }

    [Fact]
    public void ExplicitHeight_PreservesOverride_AndRecommendsFromCurrentCuts()
    {
        var fixture = CreateFixture(30d, false);
        var request = fixture.Request with
        {
            HeightMode = RoofStructuralHeightMode.Explicit,
            ExplicitVerticalHeightMm = 360d,
        };
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            request, out var result, out var reason), reason);
        Assert.Equal(360d, result!.Geometry.PhysicalVerticalHeightMm);
        Assert.True(result.Geometry.RequiredAutomaticHeightMm < 360d);
        Assert.True(result.Geometry.UsesExplicitHeight);
        var automatic = request with
        {
            HeightMode = RoofStructuralHeightMode.Automatic,
            ExplicitVerticalHeightMm = null,
        };
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            automatic, out var recalculated, out reason), reason);
        Assert.Equal(recalculated!.Geometry.RequiredAutomaticHeightMm,
            recalculated.Geometry.PhysicalVerticalHeightMm);
        Assert.Equal(result.StructuralKey, recalculated.StructuralKey);

        var shortHeight = recalculated.Geometry.RequiredAutomaticHeightMm - 1d;
        var shortOverride = request with { ExplicitVerticalHeightMm = shortHeight };
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            shortOverride, out var shortResult, out reason), reason);
        Assert.Equal(shortHeight, shortResult!.Geometry.PhysicalVerticalHeightMm);
        Assert.True(shortResult.Geometry.RequiredAutomaticHeightMm > shortHeight);
        Assert.Equal("ExplicitHeightBelowOrdinaryCutRequirement",
            shortResult.Geometry.Warning);
    }

    [Fact]
    public void MultipleOrdinaryCuts_ChooseMaximum_AndWidthMatchesTheirSidePlanes()
    {
        var fixture = CreateFixture(45d, false);
        var contacts = fixture.Request.OrdinaryMembers.Where(member =>
            member.StructuralCut?.TopologyEdgeIndex == fixture.EdgeIndex).ToArray();
        Assert.True(contacts.Length >= 2);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var result, out var reason), reason);
        Assert.Equal(contacts.Max(member =>
            member.StructuralCut!.CutFaceVertices.Max(point => point.Z) -
            member.StructuralCut.CutFaceVertices.Min(point => point.Z)),
            result!.MaximumOrdinaryCutVerticalSpanMm, 4);
        foreach (var member in contacts)
        {
            var side = member.StructuralCut!;
            Assert.Equal(120d, side.StructuralWidthMm);
            var signed = SignedOffset(result.Geometry.UpperAxis,
                side.PlanePoint);
            Assert.Equal(60d, Math.Abs(signed), 5);
        }
    }

    [Fact]
    public void Valley_UsesSameBuilder_OnlyForRealTopologyEdge()
    {
        var fixture = CreateFixture(35d, true);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var result, out var reason), reason);
        Assert.Equal(RoofStructuralRole.Valley, result!.Geometry.Role);
        Assert.Equal(RoofStructuralHeightMode.Automatic, result.HeightMode);
        Assert.Equal(2, result.EaveBoundaryEdgeIndices.Count);
        Assert.True(result.Geometry.BodyVertices.Count >= 6);
        Assert.Equal(2, result.ConvexHalves.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rectangle_AllFourPhysicalHipEdges_BuildClipReadyConvexHalves(
        bool mirrorX)
    {
        var fixture = CreateFixture(45d, false, mirrorX: mirrorX);
        var hips = fixture.Topology.Edges.Select((edge, index) => (edge, index))
            .Where(item => item.edge.Kind == RoofTopologyEdgeKind.Hip).ToArray();
        Assert.Equal(4, hips.Length);
        foreach (var (edge, index) in hips)
        {
            var ids = edge.FaceIndices.Select(faceIndex =>
                fixture.Topology.Faces[faceIndex].SourceEdgeIndex + 1)
                .OrderBy(id => id).ToArray();
            var resolved = new ResolvedRoofStructuralEdge(
                new RoofStructuralLogicalKey(RoofStructuralRole.Hip,
                    ids[0], ids[1]), index, fixture.Topology.Segment(edge),
                IsPhysicalFoldTimberEligible: true);
            var request = fixture.Request with { StructuralEdge = resolved };
            Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
                request, out var model, out var reason),
                $"edge {index}: {reason}");
            Assert.Single(model!.ConvexHalves);
            Assert.All(model.ConvexHalves, half =>
            {
                Assert.Equal(8, half.SourcePrismVertices.Count);
                Assert.True(half.ClippedBodyVertices.Count >= 6);
                Assert.True(half.ClippedTopFaceVertices.Count >= 3);
            });
            Assert.Equal(2, model.EaveClipPlanes.Count);
        }
    }

    [Theory]
    [InlineData(false, LowerEndCutMode.Vertical)]
    [InlineData(true, LowerEndCutMode.Vertical)]
    [InlineData(false, LowerEndCutMode.Perpendicular)]
    [InlineData(true, LowerEndCutMode.Perpendicular)]
    [InlineData(false, LowerEndCutMode.Horizontal)]
    [InlineData(true, LowerEndCutMode.Horizontal)]
    public void SharedUpperNode_UsesOneComplementaryVerticalMiter_ForBothHips(
        bool mirrorX, LowerEndCutMode mode)
    {
        var fixture = CreateFixture(30d, false, mirrorX: mirrorX,
            lowerEndCutMode: mode);
        var hips = fixture.Topology.Edges.Select((edge, index) => (edge, index))
            .Where(item => item.edge.Kind == RoofTopologyEdgeKind.Hip)
            .Select(item =>
            {
                var ids = item.edge.FaceIndices.Select(faceIndex =>
                    fixture.Topology.Faces[faceIndex].SourceEdgeIndex + 1)
                    .OrderBy(id => id).ToArray();
                return new ResolvedRoofStructuralEdge(
                    new RoofStructuralLogicalKey(RoofStructuralRole.Hip,
                        ids[0], ids[1]), item.index,
                    fixture.Topology.Segment(item.edge),
                    IsPhysicalFoldTimberEligible: true);
            }).ToArray();
        Assert.True(RoofStructuralUpperNodeMiterResolver.TryResolve(
            fixture.Topology, hips, out var planes, out var reason), reason);
        Assert.Equal(4, planes.Count);
        var seamFaces = new Dictionary<int, IReadOnlyList<RoofPoint3D>>();
        foreach (var edge in hips)
        {
            var plane = planes[edge.TopologyEdgeIndex];
            Assert.Equal(0d, plane.RetainedNormal.Z);
            var request = fixture.Request with
            {
                StructuralEdge = edge,
                UpperNodeMiterPlane = plane,
            };
            Assert.True(RoofStructuralRafterPhysicalBuilder.TryBuild(
                fixture.Topology, edge.TopologyEdgeIndex,
                request.PhysicalEaveElevationMm, request.StructuralWidthMm,
                null, request.OrdinaryMembers, mode, plane,
                out var directBody, out var directReason), directReason);
            Assert.NotNull(directBody);
            Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
                request, out var model, out reason),
                $"{reason}; faces={directBody!.Faces.Count}; body={directBody.BodyVertices.Count}");
            Assert.NotNull(model!.UpperNodeMiterPlane);
            Assert.All(model.Geometry.BodyVertices,
                point => Assert.True(Dot(point, plane) >= -1e-5));
            Assert.Contains(model.Geometry.BodyVertices,
                point => Math.Abs(Dot(point, plane)) <= 1e-5);
            var seam = Assert.Single(model.Geometry.Faces,
                face => face.Kind == RoofStructuralPhysicalFaceKind.UpperNodeMiter)
                .Vertices;
            Assert.InRange(seam.Min(point => SignedOffset(model.Geometry.UpperAxis, point)),
                -model.Geometry.WidthMm / 2d - 1e-4,
                -model.Geometry.WidthMm / 2d + 1e-4);
            Assert.InRange(seam.Max(point => SignedOffset(model.Geometry.UpperAxis, point)),
                model.Geometry.WidthMm / 2d - 1e-4,
                model.Geometry.WidthMm / 2d + 1e-4);
            var topologyEdge = fixture.Topology.Edges[edge.TopologyEdgeIndex];
            var upperNode = topologyEdge.StartNodeIndex < fixture.Topology.BoundaryVertexCount
                ? topologyEdge.EndNodeIndex : topologyEdge.StartNodeIndex;
            var incidentFaces = fixture.Topology.Faces.Where(face =>
                face.BoundaryNodeIndices.Contains(upperNode)).ToArray();
            Assert.True(incidentFaces.Length >= 3);
            foreach (var roofFace in incidentFaces)
            {
                Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                    fixture.Topology, roofFace, out var roofNormal));
                var roofOrigin = fixture.Topology.Nodes[roofFace.BoundaryNodeIndices[0]];
                Assert.All(model.ConvexHalves.SelectMany(half => half.ClippedBodyVertices), point =>
                    Assert.True(roofNormal.X * (point.X - roofOrigin.X) +
                        roofNormal.Y * (point.Y - roofOrigin.Y) +
                        roofNormal.Z * (point.Z - roofOrigin.Z - 3000d) <= 1e-5,
                        $"above incident roof plane at {point}"));
            }
            seamFaces.Add(edge.TopologyEdgeIndex, seam);
        }
        foreach (var pair in planes.Values.GroupBy(plane =>
                     (Math.Round(plane.Point.X, 5), Math.Round(plane.Point.Y, 5))))
        {
            var two = pair.ToArray();
            Assert.Equal(2, two.Length);
            Assert.Equal(two[0].RetainedNormal.X, -two[1].RetainedNormal.X, 8);
            Assert.Equal(two[0].RetainedNormal.Y, -two[1].RetainedNormal.Y, 8);
        }
        foreach (var pair in planes.GroupBy(item =>
                     (Math.Round(item.Value.Point.X, 5),
                         Math.Round(item.Value.Point.Y, 5))))
        {
            var two = pair.ToArray();
            Assert.Equal(2, two.Length);
            var first = seamFaces[two[0].Key]
                .Select(point => (Math.Round(point.X, 5),
                    Math.Round(point.Y, 5), Math.Round(point.Z, 5))).Order().ToArray();
            var second = seamFaces[two[1].Key]
                .Select(point => (Math.Round(point.X, 5),
                    Math.Round(point.Y, 5), Math.Round(point.Z, 5))).Order().ToArray();
            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void NonRightAngleUpperNode_UsesAxisBisector_NotFixedFortyFiveDegrees()
    {
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(0, 0), new(10000, 0), new(9000, 6000), new(0, 6000)], true));
        Assert.True(footprint.IsValid);
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!, new RoofParameters(30d), RoofKind.Hip));
        Assert.True(solved.IsValid);
        var topology = Assert.IsType<HipRoofGeometry>(solved.Geometry).Topology;
        var hips = topology.Edges.Select((edge, index) => (edge, index))
            .Where(item => item.edge.Kind == RoofTopologyEdgeKind.Hip)
            .Select(item => new ResolvedRoofStructuralEdge(
                new RoofStructuralLogicalKey(RoofStructuralRole.Hip,
                    item.index + 1, item.index + 2), item.index,
                topology.Segment(item.edge), IsPhysicalFoldTimberEligible: true))
            .ToArray();
        Assert.True(RoofStructuralUpperNodeMiterResolver.TryResolve(
            topology, hips, out var planes, out var reason), reason);
        Assert.NotEmpty(planes);
        Assert.Contains(planes.Values, plane =>
            Math.Abs(Math.Abs(plane.RetainedNormal.X) -
                Math.Abs(plane.RetainedNormal.Y)) > 0.01);
        var nonRightPair = planes.GroupBy(item =>
            (Math.Round(item.Value.Point.X, 5), Math.Round(item.Value.Point.Y, 5)))
            .Where(group => group.Any(item =>
                Math.Abs(Math.Abs(item.Value.RetainedNormal.X) -
                    Math.Abs(item.Value.RetainedNormal.Y)) > 0.01)).First();
        var pair = nonRightPair.ToArray();
        Assert.Equal(2, pair.Length);
        Assert.Equal(pair[0].Value.RetainedNormal.X,
            -pair[1].Value.RetainedNormal.X, 8);
        Assert.Equal(pair[0].Value.RetainedNormal.Y,
            -pair[1].Value.RetainedNormal.Y, 8);
        var node = pair[0].Value.Point;
        const double width = 120d;
        var seamReach = new double[2];
        for (var index = 0; index < 2; index++)
        {
            var source = topology.Edges[pair[index].Key];
            var eaveIndex = source.StartNodeIndex < topology.BoundaryVertexCount
                ? source.StartNodeIndex : source.EndNodeIndex;
            var eave = topology.Nodes[eaveIndex];
            var dx = eave.X - node.X;
            var dy = eave.Y - node.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            var ax = dx / length;
            var ay = dy / length;
            var bisectorX = -pair[index].Value.RetainedNormal.Y;
            var bisectorY = pair[index].Value.RetainedNormal.X;
            var transverse = Math.Abs(ax * bisectorY - ay * bisectorX);
            Assert.True(transverse > 1e-5);
            seamReach[index] = width / (2d * transverse);
            Assert.True(dx * pair[index].Value.RetainedNormal.X +
                dy * pair[index].Value.RetainedNormal.Y > 0d);
            var upperIndex = source.StartNodeIndex == eaveIndex
                ? source.EndNodeIndex : source.StartNodeIndex;
            Assert.Equal(node.X, topology.Nodes[upperIndex].X, 6);
            Assert.Equal(node.Y, topology.Nodes[upperIndex].Y, 6);
        }
        // Equal seam reach on the common bisector: the two equal-width strips
        // have neither plan overlap nor a plan gap after opposite halfspace cuts.
        Assert.Equal(seamReach[0], seamReach[1], 5);
    }

    [Fact]
    public void NonRightAngleHipPair_ClipsFullPhysicalSeam_ToRoofEnvelope()
    {
        var fixture = CreateFixture(30d, false, footprintPoints:
        [new(0, 0), new(10000, 0), new(9000, 6000), new(0, 6000)],
            spacingMm: 900d);
        var hips = fixture.Topology.Edges.Select((edge, index) => (edge, index))
            .Where(item => item.edge.Kind == RoofTopologyEdgeKind.Hip)
            .Select(item =>
            {
                var ids = item.edge.FaceIndices.Select(faceIndex =>
                    fixture.Topology.Faces[faceIndex].SourceEdgeIndex + 1)
                    .OrderBy(id => id).ToArray();
                return new ResolvedRoofStructuralEdge(
                    new RoofStructuralLogicalKey(RoofStructuralRole.Hip,
                        ids[0], ids[1]), item.index,
                    fixture.Topology.Segment(item.edge),
                    IsPhysicalFoldTimberEligible: true);
            }).ToArray();
        Assert.True(RoofStructuralUpperNodeMiterResolver.TryResolve(
            fixture.Topology, hips, out var miters, out var reason), reason);
        Assert.NotEmpty(miters);
        foreach (var item in hips.Where(item => miters.ContainsKey(item.TopologyEdgeIndex)))
        {
            var request = fixture.Request with
            {
                StructuralEdge = item,
                UpperNodeMiterPlane = miters[item.TopologyEdgeIndex],
            };
            Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
                request, out var body, out reason), reason);
            var seam = Assert.Single(body!.Geometry.Faces,
                face => face.Kind == RoofStructuralPhysicalFaceKind.UpperNodeMiter);
            Assert.InRange(seam.Vertices.Min(point => SignedOffset(body.Geometry.UpperAxis, point)),
                -60.0001d, -59.9999d);
            Assert.InRange(seam.Vertices.Max(point => SignedOffset(body.Geometry.UpperAxis, point)),
                59.9999d, 60.0001d);
            Assert.True(body.Geometry.PhysicalTopAxis!.End.Z <
                body.Geometry.UpperAxis.End.Z);
            Assert.All(body.ConvexHalves, half =>
                Assert.All(half.ClippedBodyVertices, point =>
                {
                    foreach (var plane in body.RoofEnvelopeClipPlanes)
                        Assert.True(Dot(point, plane) >= -1e-5);
                }));
        }
    }

    [Fact]
    public void MissingContacts_AndWidthMismatch_FailClosed()
    {
        var fixture = CreateFixture(45d, false);
        Assert.False(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request with { OrdinaryMembers = Array.Empty<RoofAutomaticRafterPhysicalMember>() },
            out var missing, out var reason));
        Assert.Null(missing);
        Assert.Equal("AutomaticHeightNeedsOrdinaryCuts", reason);
        Assert.False(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request with { StructuralWidthMm = 160d },
            out var mismatched, out reason));
        Assert.Null(mismatched);
        Assert.Equal("OrdinaryStructuralCutMismatch", reason);
    }

    [Fact]
    public void WiderStructuralSetting_RebuildsMatchingPhysicalSideFaces()
    {
        var fixture = CreateFixture(45d, false, 160d);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var model, out var reason), reason);
        Assert.Equal(160d, model!.Geometry.WidthMm);
        Assert.All(model.ConvexHalves, half => Assert.Equal(80d,
            Math.Abs(SignedOffset(model.Geometry.UpperAxis,
                half.SourcePrismVertices[1])), 5));
    }

    [Fact]
    public void InclinedTopologyRidge_MappedToHip_IsCheckedAsFoldBeforeUnsupportedAnchor()
    {
        var points = new[]
        {
            new RoofPoint2D(47947.81366099819, 12295.18433052305),
            new RoofPoint2D(47947.81366099819, 21478.922129281324),
            new RoofPoint2D(57988.8584085473, 21478.922129281324),
            new RoofPoint2D(57988.8584085473, 15403.104446947087),
            new RoofPoint2D(52849.74092174883, 15403.104446947087),
            new RoofPoint2D(52849.740921748824, 12295.18433052305),
        };
        var footprint = RoofFootprintValidator.Validate(
            new RoofFootprintInput(points, true));
        Assert.True(footprint.IsValid);
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!, new RoofParameters(30d), RoofKind.Hip));
        Assert.True(solved.IsValid);
        var topology = Assert.IsType<HipRoofGeometry>(solved.Geometry).Topology;
        var selected = Assert.Single(topology.Edges.Select((edge, index) =>
            (edge, index)), item => item.edge.Kind == RoofTopologyEdgeKind.Ridge &&
            !RoofPhysicalStructuralFold.IsHorizontalRidge(topology, item.edge));
        Assert.True(RoofPhysicalStructuralFold.IsTimberEligibleFold(topology,
            selected.edge, RoofStructuralRole.Hip));
        var resolved = new ResolvedRoofStructuralEdge(
            new RoofStructuralLogicalKey(RoofStructuralRole.Hip, 1, 4),
            selected.index, topology.Segment(selected.edge),
            IsPhysicalFoldTimberEligible: true);
        var request = new RoofStructuralRafterPolyhedronRequest(topology,
            resolved, 3000d, 120d, RoofStructuralHeightMode.Explicit,
            240d, Array.Empty<RoofAutomaticRafterPhysicalMember>());
        Assert.False(RoofStructuralRafterPolyhedronService.TryBuild(
            request, out var model, out var reason));
        Assert.Null(model);
        Assert.Equal("StructuralEaveAnchorUnresolved", reason);
    }

    private static (RoofTopology Topology, int EdgeIndex,
        RoofStructuralRafterPolyhedronRequest Request) CreateFixture(
        double pitch, bool valley, double structuralWidth = 120d,
        bool mirrorX = false,
        LowerEndCutMode lowerEndCutMode = LowerEndCutMode.Vertical,
        RoofPoint2D[]? footprintPoints = null,
        double spacingMm = 500d,
        double ordinaryHeightMm = 160d)
    {
        RoofPoint2D[] points = footprintPoints ?? (valley
            ? [new(0, 0), new(8000, 0), new(8000, 3000),
                new(3000, 3000), new(3000, 8000), new(0, 8000)]
            : [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)]);
        if (mirrorX)
            points = points.Select(point => new RoofPoint2D(-point.X, point.Y)).ToArray();
        var footprint = RoofFootprintValidator.Validate(
            new RoofFootprintInput(points, true));
        Assert.True(footprint.IsValid);
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!, new RoofParameters(pitch), RoofKind.Hip));
        Assert.True(solved.IsValid);
        var geometry = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var topology = geometry.Topology;
        var layoutResult = RoofFaceRafterLayoutService.Create(topology, spacingMm);
        Assert.True(layoutResult.IsValid);
        var layout = layoutResult.Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry, layout, 80d, out var generated));
        var sources = topology.Edges.Select((edge, index) => (edge, index))
            .Where(item => item.edge.Kind is RoofTopologyEdgeKind.Hip or
                RoofTopologyEdgeKind.Valley)
            .Select(item => new RoofStructuralRafterTrimSource(item.index,
                item.edge.Kind == RoofTopologyEdgeKind.Hip
                    ? RoofRafterBoundaryRole.Hip : RoofRafterBoundaryRole.Valley,
                topology.Segment(item.edge), structuralWidth)).ToArray();
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "roof", topology, layout, generated, 3000d, 80d, ordinaryHeightMm,
            new RoofAutomaticRafterPhysicalSettings(lowerEndCutMode), null, sources,
            out var ordinary, out var ordinaryReason), ordinaryReason);
        var wanted = valley ? RoofTopologyEdgeKind.Valley : RoofTopologyEdgeKind.Hip;
        var selected = topology.Edges.Select((edge, index) => (edge, index))
            .First(item => item.edge.Kind == wanted &&
                ordinary!.Members.Any(member =>
                    member.StructuralCut?.TopologyEdgeIndex == item.index));
        var role = valley ? RoofStructuralRole.Valley : RoofStructuralRole.Hip;
        var faceIds = selected.edge.FaceIndices
            .Select(index => topology.Faces[index].SourceEdgeIndex + 1)
            .OrderBy(index => index).ToArray();
        var resolved = new ResolvedRoofStructuralEdge(
            new RoofStructuralLogicalKey(role, faceIds[0], faceIds[1]),
            selected.index, topology.Segment(selected.edge),
            IsPhysicalFoldTimberEligible: true);
        return (topology, selected.index,
            new RoofStructuralRafterPolyhedronRequest(topology, resolved,
                3000d, structuralWidth, RoofStructuralHeightMode.Automatic,
                null, ordinary!.Members,
                LowerEndCutMode: lowerEndCutMode));
    }

    private static RoofAutomaticRafterPhysicalMember[] EaveNearestOrdinary(
        RoofStructuralRafterPolyhedronRequest request, int edgeIndex,
        RoofSegment3D axis)
    {
        var dx = axis.End.X - axis.Start.X;
        var dy = axis.End.Y - axis.Start.Y;
        return request.OrdinaryMembers
            .Where(member => member.StructuralCut?.TopologyEdgeIndex == edgeIndex)
            .GroupBy(member => Math.Sign(SignedOffset(axis,
                member.StructuralCut!.PlanePoint)))
            .Select(group => group.OrderBy(member =>
            {
                var edge = member.StructuralCut!.LowerContactEdge!;
                var midX = (edge.Start.X + edge.End.X) / 2d - axis.Start.X;
                var midY = (edge.Start.Y + edge.End.Y) / 2d - axis.Start.Y;
                return midX * dx + midY * dy;
            }).First()).ToArray();
    }

    private static double SignedOffset(RoofSegment3D axis, RoofPoint3D point)
    {
        var dx = axis.End.X - axis.Start.X;
        var dy = axis.End.Y - axis.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        return ((point.X - axis.Start.X) * -dy +
            (point.Y - axis.Start.Y) * dx) / length;
    }

    private static double Dot(RoofPoint3D point,
        RoofStructuralRafterClipPlane plane) =>
        (point.X - plane.Point.X) * plane.RetainedNormal.X +
        (point.Y - plane.Point.Y) * plane.RetainedNormal.Y +
        (point.Z - plane.Point.Z) * plane.RetainedNormal.Z;

    private static void AssertSamePlanProjection(
        IReadOnlyList<RoofPoint3D> before, IReadOnlyList<RoofPoint3D> after)
    {
        var points = before.Concat(after).ToArray();
        var directions = new List<(double X, double Y)> { (1d, 0d), (0d, 1d) };
        for (var i = 0; i < points.Length; i++)
        for (var j = i + 1; j < points.Length; j++)
        {
            var nx = points[i].Y - points[j].Y;
            var ny = points[j].X - points[i].X;
            var length = Math.Sqrt(nx * nx + ny * ny);
            if (length > 1e-6) directions.Add((nx / length, ny / length));
        }
        foreach (var (x, y) in directions)
        {
            var beforeValues = before.Select(point => point.X * x + point.Y * y);
            var afterValues = after.Select(point => point.X * x + point.Y * y);
            Assert.InRange(Math.Abs(beforeValues.Max() - afterValues.Max()), 0d, 1e-4);
            Assert.InRange(Math.Abs(beforeValues.Min() - afterValues.Min()), 0d, 1e-4);
        }
    }

}
