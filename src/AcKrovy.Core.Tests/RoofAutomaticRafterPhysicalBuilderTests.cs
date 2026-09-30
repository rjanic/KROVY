using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticRafterPhysicalBuilderTests
{
    [Fact]
    public void BottomContactEdge_IsGeometricAndIndependentOfFaceVertexOrder()
    {
        var bottom = new[]
        {
            new RoofPoint3D(0d, 0d, 0d), new RoofPoint3D(4d, 0d, 0d),
            new RoofPoint3D(4d, 2d, 0d), new RoofPoint3D(0d, 2d, 0d),
        };
        var cut = new[]
        {
            new RoofPoint3D(4d, 2d, 3d), bottom[1],
            new RoofPoint3D(4d, 0d, 3d), bottom[2],
        };
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryFindBottomContactEdge(
            bottom, cut, out var edge));
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryFindBottomContactEdge(
            bottom.Reverse().ToArray(), cut.Reverse().ToArray(), out var reordered));
        Assert.Contains(edge.Start, new[] { bottom[1], bottom[2] });
        Assert.Contains(edge.End, new[] { bottom[1], bottom[2] });
        Assert.Contains(reordered.Start, new[] { edge.Start, edge.End });
        Assert.Contains(reordered.End, new[] { edge.Start, edge.End });
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical)]
    [InlineData(LowerEndCutMode.Perpendicular)]
    [InlineData(LowerEndCutMode.Horizontal)]
    public void HipSideTrim_PreservesPlanIdentity_AndCutsAtActualHalfWidth(
        LowerEndCutMode eaveMode)
    {
        var (geometry, layout, generated) = Solve();
        const double structuralWidth = 120d;
        var sources = StructuralSources(geometry.Topology, structuralWidth);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, layout, generated, 3000d, 80d, 125d,
            new RoofAutomaticRafterPhysicalSettings(eaveMode), null, sources,
            out var model, out var reason), reason);
        Assert.Equal(generated.Rafters.Count, model!.Members.Count);
        Assert.Equal(model.Members.Count,
            model.Members.Select(member => member.MemberKey).Distinct().Count());
        var trimmed = model.Members.Where(member => member.StructuralCut is not null).ToArray();
        Assert.NotEmpty(trimmed);
        foreach (var member in trimmed)
        {
            var segment = layout.Segments[member.MemberKey.StationIndex];
            Assert.Equal(new RoofPoint3D(segment.PlanStart.X, segment.PlanStart.Y, 0d),
                member.PlanAxis.Start);
            Assert.Equal(new RoofPoint3D(segment.PlanEnd.X, segment.PlanEnd.Y, 0d),
                member.PlanAxis.End);
            Assert.Equal(generated.Rafters[member.MemberKey.StationIndex].LogicalKey,
                member.MemberKey);
            var cut = member.StructuralCut!;
            if (eaveMode != LowerEndCutMode.Horizontal)
            {
                var lowerEdge = Assert.IsType<RoofSegment3D>(cut.LowerContactEdge);
                Assert.NotNull(cut.BottomFaceVertices);
                Assert.True(lowerEdge.Start.DistanceTo(lowerEdge.End) > 1e-5);
                foreach (var endpoint in new[] { lowerEdge.Start, lowerEdge.End })
                {
                    Assert.Contains(cut.BottomFaceVertices!, point =>
                        point.DistanceTo(endpoint) <= 1e-5);
                    Assert.Contains(cut.CutFaceVertices, point =>
                        point.DistanceTo(endpoint) <= 1e-5);
                }
            }
            Assert.Equal(structuralWidth, cut.StructuralWidthMm);
            Assert.Equal(0d, cut.PlaneNormal.Z);
            var axis = sources.Single(source =>
                source.TopologyEdgeIndex == cut.TopologyEdgeIndex).Axis;
            var offset = Dot(Direction(axis.Start, cut.PlanePoint), cut.PlaneNormal);
            Assert.Equal(structuralWidth / 2d, offset, 6);
            Assert.All(cut.CutFaceVertices, vertex => Assert.Equal(0d,
                Dot(Direction(cut.PlanePoint, vertex), cut.PlaneNormal), 5));
            Assert.All(member.SolidVertices, vertex => Assert.True(
                Dot(Direction(cut.PlanePoint, vertex), cut.PlaneNormal) >= -1e-5));
            var face = geometry.Topology.Faces.Single(item =>
                item.SourceEdgeIndex == member.SourceFaceIndex);
            Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                geometry.Topology, face, out var roofNormal));
            var roofOrigin = geometry.Topology.Nodes[face.BoundaryNodeIndices[0]];
            Assert.All(cut.TopFaceVertices, vertex => Assert.Equal(0d,
                roofNormal.X * (vertex.X - roofOrigin.X) +
                roofNormal.Y * (vertex.Y - roofOrigin.Y) +
                roofNormal.Z * (vertex.Z - roofOrigin.Z - 3000d), 5));
            var canonical = segment.EndBoundaryRole is RoofRafterBoundaryRole.Hip
                or RoofRafterBoundaryRole.Valley ? segment.PlanEnd : segment.PlanStart;
            var axisDx = axis.End.X - axis.Start.X;
            var axisDy = axis.End.Y - axis.Start.Y;
            Assert.Equal(0d,
                (canonical.X - axis.Start.X) * axisDy -
                (canonical.Y - axis.Start.Y) * axisDx, 4);
        }
    }

    private static RoofStructuralRafterTrimSource[] StructuralSources(
        RoofTopology topology, double width) => topology.Edges
        .Select((edge, index) => (edge, index))
        .Where(item => item.edge.Kind is RoofTopologyEdgeKind.Hip or
            RoofTopologyEdgeKind.Valley)
        .Select(item => new RoofStructuralRafterTrimSource(
            item.index,
            item.edge.Kind == RoofTopologyEdgeKind.Hip
                ? RoofRafterBoundaryRole.Hip : RoofRafterBoundaryRole.Valley,
            topology.Segment(item.edge), width))
        .ToArray();

    [Fact]
    public void ValleySideTrim_OnSupportedConcaveRoof_UsesFacingHalfWidth()
    {
        var (geometry, layout, generated) = SolveL();
        Assert.Contains(layout.Segments, segment =>
            segment.StartBoundaryRole == RoofRafterBoundaryRole.Valley ||
            segment.EndBoundaryRole == RoofRafterBoundaryRole.Valley);
        var sources = StructuralSources(geometry.Topology, 140d);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "L", geometry.Topology, layout, generated, 3000d, 80d, 125d,
            new RoofAutomaticRafterPhysicalSettings(), null, sources,
            out var model, out var reason), reason);
        var valley = model!.Members.Where(member =>
            member.StructuralCut?.Role == RoofRafterBoundaryRole.Valley).ToArray();
        Assert.NotEmpty(valley);
        foreach (var member in valley)
        {
            var cut = member.StructuralCut!;
            var axis = sources.Single(source =>
                source.TopologyEdgeIndex == cut.TopologyEdgeIndex).Axis;
            Assert.Equal(70d,
                Dot(Direction(axis.Start, cut.PlanePoint), cut.PlaneNormal), 6);
            Assert.Equal(0d, member.PlanAxis.Start.Z);
            Assert.Equal(0d, member.PlanAxis.End.Z);
            Assert.All(cut.CutFaceVertices, vertex => Assert.Equal(0d,
                Dot(Direction(cut.PlanePoint, vertex), cut.PlaneNormal), 5));
        }
    }

    [Fact]
    public void StructuralWidthChange_MovesOnlyPhysicalSideCut_NotLogicalAxis()
    {
        var (geometry, layout, generated) = Solve();
        RoofAutomaticRafterPhysicalModel BuildFor(double structuralWidth)
        {
            Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
                "AB", geometry.Topology, layout, generated, 3000d, 80d, 125d,
                new RoofAutomaticRafterPhysicalSettings(), null,
                StructuralSources(geometry.Topology, structuralWidth),
                out var model, out var reason), reason);
            return model!;
        }
        var narrow = BuildFor(80d);
        var wide = BuildFor(160d);
        foreach (var (a, b) in narrow.Members.Zip(wide.Members))
        {
            Assert.Equal(a.MemberKey, b.MemberKey);
            Assert.Equal(a.PlanAxis, b.PlanAxis);
            if (a.StructuralCut is null)
                continue;
            Assert.Equal(160d, b.StructuralCut!.StructuralWidthMm);
            var moved = Direction(a.StructuralCut.PlanePoint,
                b.StructuralCut.PlanePoint);
            Assert.Equal(40d, Dot(moved, a.StructuralCut.PlaneNormal), 6);
        }
    }

    [Fact]
    public void AsymmetricHip_Non45StructuralEnd_UsesPlaneNotLengthSubtraction()
    {
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [
                new RoofPoint2D(0, 0), new RoofPoint2D(10000, 0),
                new RoofPoint2D(9500, 6000), new RoofPoint2D(1200, 6000),
            ], true));
        Assert.True(footprint.IsValid);
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!, new RoofParameters(35d), RoofKind.Hip));
        Assert.True(solved.IsValid);
        var geometry = (HipRoofGeometry)solved.Geometry!;
        var layoutResult = RoofFaceRafterLayoutService.Create(geometry.Topology, 500d);
        Assert.True(layoutResult.IsValid);
        var layout = layoutResult.Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry, layout, 80d, out var generated));
        var sources = StructuralSources(geometry.Topology, 100d);
        Assert.False(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "TRAP", geometry.Topology, layout, generated, 3000d, 80d, 125d,
            new RoofAutomaticRafterPhysicalSettings(), null, sources,
            out var unresolved, out var fullReason));
        Assert.Null(unresolved);
        Assert.Contains("StructuralTargetAmbiguous", fullReason);
        var non45 = new List<RoofAutomaticRafterPhysicalMember>();
        foreach (var rafter in generated.Rafters)
        {
            var filtered = generated with { Rafters = [rafter] };
            if (!RoofAutomaticRafterPhysicalBuilder.TryBuild(
                    "TRAP", geometry.Topology, layout, filtered, 3000d, 80d, 125d,
                    new RoofAutomaticRafterPhysicalSettings(), null, sources,
                    out var model, out var reason))
            {
                Assert.Contains("StructuralTargetAmbiguous", reason);
                continue;
            }
            var member = Assert.Single(model!.Members);
            if (member.StructuralCut is not { } cut)
                continue;
            var axis = sources.Single(item =>
                item.TopologyEdgeIndex == cut.TopologyEdgeIndex).Axis;
            var ordinary = Direction(member.PlanAxis.Start, member.PlanAxis.End);
            var structural = Direction(axis.Start, axis.End);
            var cosine = Math.Abs((ordinary.X * structural.X +
                ordinary.Y * structural.Y) /
                (Math.Sqrt(ordinary.X * ordinary.X + ordinary.Y * ordinary.Y) *
                 Math.Sqrt(structural.X * structural.X + structural.Y * structural.Y)));
            if (Math.Abs(cosine - Math.Sqrt(0.5d)) > 0.01d && cosine < 0.99d)
                non45.Add(member);
        }
        Assert.NotEmpty(non45);
        Assert.All(non45, member =>
        {
            var cut = member.StructuralCut!;
            Assert.All(cut.CutFaceVertices, point => Assert.Equal(0d,
                Dot(Direction(cut.PlanePoint, point), cut.PlaneNormal), 5));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralTrim_ReplayedAxisAndSuppression_KeepExactMemberSet(
        bool valley)
    {
        var (geometry, layout, generated) = valley ? SolveL() : Solve();
        var role = valley ? RoofRafterBoundaryRole.Valley : RoofRafterBoundaryRole.Hip;
        var replay = RoofGeneratedMemberReplayPlanner.Create(
            generated, 0d, new RoofPoint3D(0, 0, 1), null);
        var items = replay.Items.ToArray();
        var index = Array.FindIndex(items, item =>
            layout.Segments[item.Rafter.StationIndex].StartBoundaryRole == role ||
            layout.Segments[item.Rafter.StationIndex].EndBoundaryRole == role);
        Assert.True(index >= 0);
        var original = items[index].Geometry!.Value;
        var structuralAtEnd = layout.Segments[items[index].Rafter.StationIndex]
            .EndBoundaryRole == role;
        var dx = original.End.X - original.Start.X;
        var dy = original.End.Y - original.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var edited = structuralAtEnd
            ? original with
            {
                Start = new RoofPoint3D(original.Start.X - 20d * dy / length,
                    original.Start.Y + 20d * dx / length, 0d),
            }
            : original with
            {
                End = new RoofPoint3D(original.End.X - 20d * dy / length,
                    original.End.Y + 20d * dx / length, 0d),
            };
        items[index] = items[index] with
        {
            Geometry = edited,
            Disposition = RoofGeneratedMemberReplayDisposition.GeometryReplayed,
        };
        var sources = StructuralSources(geometry.Topology, 100d);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, layout, generated, 3000d, 80d, 125d,
            new RoofAutomaticRafterPhysicalSettings(),
            replay with { Items = items }, sources,
            out var model, out var reason), reason);
        var member = Assert.Single(model!.Members,
            item => item.MemberKey == items[index].Rafter.LogicalKey);
        Assert.NotNull(member.StructuralCut);
        Assert.Equal(edited.Start, member.PlanAxis.Start);
        Assert.Equal(edited.End, member.PlanAxis.End);
        Assert.Equal(model.Members.Count,
            model.Members.Select(item => item.MemberKey).Distinct().Count());

        items[index] = items[index] with
        {
            Geometry = null,
            Disposition = RoofGeneratedMemberReplayDisposition.Suppressed,
        };
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, layout, generated, 3000d, 80d, 125d,
            new RoofAutomaticRafterPhysicalSettings(),
            replay with { Items = items }, sources,
            out var suppressed, out reason), reason);
        Assert.Equal(model.Members.Count - 1, suppressed!.Members.Count);
        Assert.DoesNotContain(suppressed.Members,
            item => item.MemberKey == items[index].Rafter.LogicalKey);
    }

    private static (HipRoofGeometry Geometry, RoofFaceRafterLayout Layout,
        RoofRafterLayout Generated) SolveL()
    {
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [
                new RoofPoint2D(0, 0), new RoofPoint2D(8000, 0),
                new RoofPoint2D(8000, 3000), new RoofPoint2D(3000, 3000),
                new RoofPoint2D(3000, 8000), new RoofPoint2D(0, 8000),
            ], true));
        Assert.True(footprint.IsValid);
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!, new RoofParameters(35d), RoofKind.Hip));
        Assert.True(solved.IsValid);
        var geometry = (HipRoofGeometry)solved.Geometry!;
        var layout = RoofFaceRafterLayoutService.Create(geometry.Topology, 500d);
        Assert.True(layout.IsValid);
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry, layout.Layout!, 80d, out var generated));
        return (geometry, layout.Layout!, generated);
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical)]
    [InlineData(LowerEndCutMode.Perpendicular)]
    [InlineData(LowerEndCutMode.Horizontal)]
    public void EaveCuts_KeepPlanAxisAndUpperEdgeOnRoofBoundary(
        LowerEndCutMode cutMode)
    {
        var (geometry, layout, generated) = Solve();
        var settings = new RoofAutomaticRafterPhysicalSettings(cutMode);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, layout, generated, 3000d, 80d, 125d,
            settings, out var model));

        Assert.Equal(layout.Segments.Count, model!.Members.Count);
        for (var index = 0; index < model.Members.Count; index++)
        {
            var member = model.Members[index];
            var segment = layout.Segments[index];
            Assert.Equal(0d, member.PlanAxis.Start.Z);
            Assert.Equal(0d, member.PlanAxis.End.Z);
            Assert.Equal(segment.PlanStart.X, member.PlanAxis.Start.X, 7);
            Assert.Equal(segment.PlanStart.Y, member.PlanAxis.Start.Y, 7);
            Assert.Equal(segment.PlanEnd.X, member.PlanAxis.End.X, 7);
            Assert.Equal(segment.PlanEnd.Y, member.PlanAxis.End.Y, 7);
            Assert.Equal(index, member.MemberKey.StationIndex);
            Assert.True(member.SolidVertices.Count >= 8);
            Assert.Equal(generated.Rafters[index].TrueLengthMm,
                member.PhysicalLengthMm, 6);
        }

        var eaveMember = model.Members.First(member =>
            member.StartBoundaryRole == RoofRafterBoundaryRole.Eave);
        var vertices = eaveMember.SolidVertices;
        var upper = cutMode == LowerEndCutMode.Horizontal
            ? Midpoint(eaveMember.HorizontalCut!.TopFaceVertices
                .Where(point => Math.Abs(point.Z - 3000d) < 1e-6).First(),
                eaveMember.HorizontalCut.TopFaceVertices
                .Where(point => Math.Abs(point.Z - 3000d) < 1e-6).Last())
            : Midpoint(vertices[0], vertices[1]);
        var lower = cutMode == LowerEndCutMode.Horizontal
            ? default
            : Midpoint(vertices[4], vertices[5]);
        Assert.Equal(3000d, upper.Z, 7);
        Assert.Equal(eaveMember.PlanAxis.Start.X, upper.X, 7);
        Assert.Equal(eaveMember.PlanAxis.Start.Y, upper.Y, 7);
        switch (cutMode)
        {
            case LowerEndCutMode.Vertical:
                Assert.Equal(upper.X, lower.X, 7);
                Assert.Equal(upper.Y, lower.Y, 7);
                break;
            case LowerEndCutMode.Horizontal:
                Assert.All(eaveMember.HorizontalCut!.CutFaceVertices,
                    point => Assert.Equal(3000d, point.Z, 7));
                break;
            case LowerEndCutMode.Perpendicular:
                var run = Direction(
                    Midpoint(vertices[0], vertices[1]),
                    Midpoint(vertices[2], vertices[3]));
                var cutVector = Direction(upper, lower);
                Assert.Equal(0d, Dot(run, cutVector), 7);
                break;
        }
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(3000d)]
    public void HorizontalCut_UsesResolvedEaveWcsPlane_WithoutMovingUpperRoofFace(
        double eaveElevationMm)
    {
        var (geometry, layout, generated) = Solve();
        var model = BuildAtElevation(geometry, layout, generated,
            new RoofAutomaticRafterPhysicalSettings(LowerEndCutMode.Horizontal),
            eaveElevationMm);
        Assert.Equal(generated.Rafters.Count, model.Members.Count);

        foreach (var member in model.Members)
        {
            var segment = layout.Segments[member.MemberKey.StationIndex];
            var vertices = member.SolidVertices;
            var face = geometry.Topology.Faces.Single(item =>
                item.SourceEdgeIndex == member.SourceFaceIndex);
            Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                geometry.Topology, face, out var normal));
            var origin = geometry.Topology.Nodes[face.BoundaryNodeIndices[0]];
            var cut = member.HorizontalCut!;
            foreach (var point in cut.TopFaceVertices)
            {
                Assert.Equal(0d,
                    normal.X * (point.X - origin.X) +
                    normal.Y * (point.Y - origin.Y) +
                    normal.Z * (point.Z - origin.Z - eaveElevationMm), 6);
            }

            if (member.StartBoundaryRole != RoofRafterBoundaryRole.Eave &&
                member.EndBoundaryRole != RoofRafterBoundaryRole.Eave)
            {
                continue;
            }

            var eaveEdge = geometry.Topology.Segment(
                geometry.Topology.Edges[segment.SourceEaveEdgeIndex]);
            var topEave = cut.TopFaceVertices.Where(point =>
                Math.Abs(point.Z - eaveElevationMm) < 1e-6).ToArray();
            Assert.True(topEave.Length >= 2);
            foreach (var point in topEave)
            {
                Assert.Equal(eaveElevationMm, point.Z, 6);
                var cross = (point.X - eaveEdge.Start.X) *
                    (eaveEdge.End.Y - eaveEdge.Start.Y) -
                    (point.Y - eaveEdge.Start.Y) *
                    (eaveEdge.End.X - eaveEdge.Start.X);
                Assert.Equal(0d, cross, 5);
            }
            foreach (var point in cut.CutFaceVertices)
            {
                Assert.Equal(eaveElevationMm, point.Z, 6);
            }
            Assert.All(vertices, point =>
                Assert.True(point.Z >= eaveElevationMm - 1e-6));
        }
    }

    [Fact]
    public void SwitchingCutModes_PreservesLogicalMemberAndPlanAxis_AndRepeatedBuildIsUnique()
    {
        var (geometry, layout, generated) = Solve();
        var models = new[]
        {
            LowerEndCutMode.Vertical,
            LowerEndCutMode.Horizontal,
            LowerEndCutMode.Perpendicular,
            LowerEndCutMode.Horizontal,
        }.Select(mode => BuildAtElevation(geometry, layout, generated,
            new RoofAutomaticRafterPhysicalSettings(mode), 3000d)).ToArray();

        var baseline = models[0];
        Assert.Equal(baseline.Members.Count,
            baseline.Members.Select(member => member.MemberKey).Distinct().Count());
        foreach (var model in models.Skip(1))
        {
            Assert.Equal(baseline.Members.Count, model.Members.Count);
            foreach (var (original, current) in baseline.Members.Zip(model.Members))
            {
                Assert.Equal(original.MemberKey, current.MemberKey);
                Assert.Equal(original.PlanAxis, current.PlanAxis);
                if (current.HorizontalCut is null)
                {
                    Assert.Equal(original.SolidVertices.Take(4), current.SolidVertices.Take(4));
                }
                else
                {
                    Assert.Equal(original.SolidVertices[2],
                        current.HorizontalCut.SourcePrismVertices[2]);
                    Assert.Equal(original.SolidVertices[3],
                        current.HorizontalCut.SourcePrismVertices[3]);
                }
                Assert.Equal(0d, current.PlanAxis.Start.Z);
                Assert.Equal(0d, current.PlanAxis.End.Z);
            }
        }
        foreach (var (first, repeated) in models[1].Members.Zip(models[3].Members))
        {
            Assert.Equal(first.MemberKey, repeated.MemberKey);
            Assert.Equal(first.SolidVertices, repeated.SolidVertices);
        }
    }

    [Theory]
    [InlineData(25d, 25d)]
    [InlineData(-45d, 60d)]
    public void HorizontalCut_ReplayedAxis_KeepsEntireLowerEaveEdgeOnWcsEavePlane(
        double endOffsetX, double endOffsetY)
    {
        var (geometry, layout, generated) = Solve();
        var replay = RoofGeneratedMemberReplayPlanner.Create(
            generated, 0d, new RoofPoint3D(0d, 0d, 1d), null);
        var items = replay.Items.ToArray();
        var index = Array.FindIndex(items, item =>
            layout.Segments[item.Rafter.StationIndex].StartBoundaryRole ==
            RoofRafterBoundaryRole.Eave);
        Assert.True(index >= 0);
        var original = items[index].Geometry!.Value;
        items[index] = items[index] with
        {
            Geometry = original with
            {
                End = new RoofPoint3D(
                    original.End.X + endOffsetX,
                    original.End.Y + endOffsetY, 0d),
            },
            Disposition = RoofGeneratedMemberReplayDisposition.GeometryReplayed,
        };

        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, layout, generated, 3000d, 80d, 125d,
            new RoofAutomaticRafterPhysicalSettings(LowerEndCutMode.Horizontal),
            replay with { Items = items }, out var model));
        var member = Assert.Single(model!.Members,
            item => item.MemberKey == items[index].Rafter.LogicalKey);
        Assert.Equal(model.Members.Count,
            model.Members.Select(item => item.MemberKey).Distinct().Count());
        Assert.NotNull(member.HorizontalCut);
        Assert.All(member.HorizontalCut.CutFaceVertices,
            point => Assert.Equal(3000d, point.Z, 6));
        Assert.All(member.SolidVertices,
            point => Assert.True(point.Z >= 3000d - 1e-6));
        Assert.Equal(original.Start, member.PlanAxis.Start);
        Assert.Equal(items[index].Geometry!.Value.End, member.PlanAxis.End);
        Assert.Equal(items[index].Rafter.LogicalKey, member.MemberKey);
        var face = geometry.Topology.Faces.Single(item =>
            item.SourceEdgeIndex == member.SourceFaceIndex);
        Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
            geometry.Topology, face, out var normal));
        var origin = geometry.Topology.Nodes[face.BoundaryNodeIndices[0]];
        Assert.All(member.HorizontalCut.TopFaceVertices, point =>
            Assert.Equal(0d,
                normal.X * (point.X - origin.X) +
                normal.Y * (point.Y - origin.Y) +
                normal.Z * (point.Z - origin.Z - 3000d), 6));
        var eaveEdge = geometry.Topology.Segment(
            geometry.Topology.Edges[
                layout.Segments[member.MemberKey.StationIndex].SourceEaveEdgeIndex]);
        var topEave = member.HorizontalCut.TopFaceVertices.Where(point =>
            Math.Abs(point.Z - 3000d) < 1e-6).ToArray();
        Assert.True(topEave.Length >= 2);
        Assert.All(topEave, point => Assert.Equal(0d,
            (point.X - eaveEdge.Start.X) * (eaveEdge.End.Y - eaveEdge.Start.Y) -
            (point.Y - eaveEdge.Start.Y) * (eaveEdge.End.X - eaveEdge.Start.X), 5));
        var source = member.HorizontalCut.SourcePrismVertices;
        var width = Direction(source[2], source[3]);
        Assert.Equal(80d, Math.Sqrt(Dot(width, width)), 6);
        var height = Direction(source[6], source[2]);
        Assert.Equal(125d,
            normal.X * height.X + normal.Y * height.Y + normal.Z * height.Z, 6);
    }

    [Fact]
    public void HorizontalCut_ParallelToEaveAxis_FailsWithReasonAndNoPartialModel()
    {
        var (geometry, layout, generated) = Solve();
        var replay = RoofGeneratedMemberReplayPlanner.Create(
            generated, 0d, new RoofPoint3D(0d, 0d, 1d), null);
        var items = replay.Items.ToArray();
        var index = Array.FindIndex(items, item =>
            layout.Segments[item.Rafter.StationIndex].StartBoundaryRole ==
            RoofRafterBoundaryRole.Eave);
        Assert.True(index >= 0);
        var original = items[index].Geometry!.Value;
        items[index] = items[index] with
        {
            Geometry = original with
            {
                End = new RoofPoint3D(original.Start.X + 500d,
                    original.Start.Y, 0d),
            },
            Disposition = RoofGeneratedMemberReplayDisposition.GeometryReplayed,
        };
        Assert.False(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, layout, generated, 3000d, 80d, 125d,
            new RoofAutomaticRafterPhysicalSettings(LowerEndCutMode.Horizontal),
            replay with { Items = items }, out var model, out var failureReason));
        Assert.Null(model);
        Assert.Contains("HorizontalCut:InvalidUpslopeDirection", failureReason);
    }

    [Fact]
    public void FilteredGeneratedSet_DoesNotCreateOrphanPhysicalMembers()
    {
        var (geometry, layout, generated) = Solve();
        var retained = generated.Rafters.Where((_, index) => index % 2 == 0).ToArray();
        var filtered = generated with { Rafters = retained };

        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, layout, filtered, 3000d, 80d, 125d,
            new RoofAutomaticRafterPhysicalSettings(), out var model));
        Assert.Equal(retained.Length, model!.Members.Count);
        Assert.Equal(retained.Select(item => item.LogicalKey),
            model.Members.Select(item => item.MemberKey));
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical)]
    [InlineData(LowerEndCutMode.Horizontal)]
    public void SuppressionReplay_OmitsOnlyItsSolid_WithoutChangingOtherPlanAxes(
        LowerEndCutMode cutMode)
    {
        var (geometry, layout, generated) = Solve();
        var replay = RoofGeneratedMemberReplayPlanner.Create(
            generated, 0d, new RoofPoint3D(0d, 0d, 1d), null);
        var items = replay.Items.ToArray();
        items[0] = items[0] with
        {
            Geometry = null,
            Disposition = RoofGeneratedMemberReplayDisposition.Suppressed,
        };

        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, layout, generated, 3000d, 80d, 125d,
            new RoofAutomaticRafterPhysicalSettings(cutMode), replay with { Items = items },
            out var model));
        Assert.Equal(generated.Rafters.Count - 1, model!.Members.Count);
        Assert.DoesNotContain(model.Members,
            member => member.MemberKey == generated.Rafters[0].LogicalKey);
        Assert.All(model.Members, member =>
        {
            Assert.Equal(0d, member.PlanAxis.Start.Z);
            Assert.Equal(0d, member.PlanAxis.End.Z);
        });
    }

    [Fact]
    public void GeometryReplay_UsesLogicalAxis_AndKeepsUpperCornersOnRoofPlane()
    {
        var (geometry, layout, generated) = Solve();
        var replay = RoofGeneratedMemberReplayPlanner.Create(
            generated, 0d, new RoofPoint3D(0d, 0d, 1d), null);
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, layout, generated, 3000d, 80d, 125d,
            new RoofAutomaticRafterPhysicalSettings(), replay, out var baseline));
        var baselineMember = Assert.Single(baseline!.Members,
            item => item.MemberKey == generated.Rafters[0].LogicalKey);
        var items = replay.Items.ToArray();
        var original = items[0].Geometry!.Value;
        var end = new RoofPoint3D(
            original.End.X - (original.End.X - original.Start.X) * 0.1d,
            original.End.Y - (original.End.Y - original.Start.Y) * 0.1d + 25d,
            0d);
        items[0] = items[0] with
        {
            Geometry = original with { End = end },
            Disposition = RoofGeneratedMemberReplayDisposition.GeometryReplayed,
        };

        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, layout, generated, 3000d, 80d, 125d,
            new RoofAutomaticRafterPhysicalSettings(), replay with { Items = items },
            out var model));
        var member = Assert.Single(model!.Members,
            item => item.MemberKey == generated.Rafters[0].LogicalKey);
        Assert.Equal(end, member.PlanAxis.End);
        Assert.Equal(0d, member.PlanAxis.End.Z);
        Assert.NotEqual(generated.Rafters[0].TrueLengthMm, member.PhysicalLengthMm);
        Assert.NotEqual(baselineMember.PhysicalLengthMm, member.PhysicalLengthMm);
        var transverse = Direction(member.SolidVertices[0], member.SolidVertices[1]);
        Assert.Equal(member.WidthMm, Math.Sqrt(Dot(transverse, transverse)), 6);
        var face = geometry.Topology.Faces.Single(face =>
            face.SourceEdgeIndex == member.SourceFaceIndex);
        Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
            geometry.Topology, face, out var normal));
        var origin = geometry.Topology.Nodes[face.BoundaryNodeIndices[0]];
        foreach (var point in member.SolidVertices.Take(4))
        {
            Assert.Equal(0d,
                normal.X * (point.X - origin.X) +
                normal.Y * (point.Y - origin.Y) +
                normal.Z * (point.Z - origin.Z - 3000d), 6);
        }
    }

    [Fact]
    public void MovedPlanAxis_RebuildsPhysicalMemberWithSameLogicalKey()
    {
        var (geometry, layout, generated) = Solve();
        var replay = RoofGeneratedMemberReplayPlanner.Create(
            generated, 0d, new RoofPoint3D(0d, 0d, 1d), null);
        var items = replay.Items.ToArray();
        var original = items[0].Geometry!.Value;
        var dx = original.End.X - original.Start.X;
        var dy = original.End.Y - original.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var offsetX = -20d * dy / length;
        var offsetY = 20d * dx / length;
        var moved = original with
        {
            Start = new RoofPoint3D(original.Start.X + offsetX,
                original.Start.Y + offsetY, 0d),
            End = new RoofPoint3D(original.End.X + offsetX,
                original.End.Y + offsetY, 0d),
        };
        items[0] = items[0] with
        {
            Geometry = moved,
            Disposition = RoofGeneratedMemberReplayDisposition.GeometryReplayed,
        };

        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, layout, generated, 3000d, 80d, 125d,
            new RoofAutomaticRafterPhysicalSettings(), replay with { Items = items },
            out var model));
        var member = Assert.Single(model!.Members,
            item => item.MemberKey == generated.Rafters[0].LogicalKey);
        Assert.Equal(moved.Start, member.PlanAxis.Start);
        Assert.Equal(moved.End, member.PlanAxis.End);
        Assert.Equal(0d, member.PlanAxis.Start.Z);
        Assert.Equal(0d, member.PlanAxis.End.Z);
        Assert.Equal(model.Members.Count,
            model.Members.Select(item => item.MemberKey).Distinct().Count());
    }

    [Theory]
    [InlineData(RidgeJoinMode.Meet)]
    [InlineData(RidgeJoinMode.Overlap)]
    public void RidgeModes_LeavePlanAndPhysicalUpperFaceUnchanged(RidgeJoinMode ridgeMode)
    {
        var (geometry, layout, generated) = Solve();
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, layout, generated, 3000d, 80d, 125d,
            new RoofAutomaticRafterPhysicalSettings(
                LowerEndCutMode.Vertical, ridgeMode), out var model));
        var baseline = Build(geometry, layout, generated,
            new RoofAutomaticRafterPhysicalSettings());

        foreach (var (member, original) in model!.Members.Zip(baseline.Members))
        {
            Assert.Equal(original.PlanAxis, member.PlanAxis);
            Assert.Equal(original.SolidVertices.Take(4), member.SolidVertices.Take(4));
            var face = geometry.Topology.Faces.Single(face =>
                face.SourceEdgeIndex == member.SourceFaceIndex);
            Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                geometry.Topology, face, out var normal));
            var origin = geometry.Topology.Nodes[face.BoundaryNodeIndices[0]];
            foreach (var vertex in member.SolidVertices.Take(4))
            {
                var planeOffset = normal.X * (vertex.X - origin.X) +
                    normal.Y * (vertex.Y - origin.Y) +
                    normal.Z * (vertex.Z - origin.Z - 3000d);
                Assert.Equal(0d, planeOffset, 6);
            }
        }

        var ridge = model.Members.First(member =>
            member.EndBoundaryRole == RoofRafterBoundaryRole.Ridge);
        var endTop = Midpoint(ridge.SolidVertices[2], ridge.SolidVertices[3]);
        var endBottom = Midpoint(ridge.SolidVertices[6], ridge.SolidVertices[7]);
        if (ridgeMode == RidgeJoinMode.Meet)
        {
            Assert.Equal(endTop.X, endBottom.X, 7);
            Assert.Equal(endTop.Y, endBottom.Y, 7);
        }
        else
        {
            Assert.True(Math.Sqrt(
                Math.Pow(endTop.X - endBottom.X, 2) +
                Math.Pow(endTop.Y - endBottom.Y, 2)) > 1d);
        }
    }

    [Theory]
    [InlineData(RidgeJoinMode.Meet)]
    [InlineData(RidgeJoinMode.Overlap)]
    public void OpposingRidgePair_HasCommonMeetPlaneOrSymmetricOverlap(
        RidgeJoinMode mode)
    {
        var (geometry, layout, generated) = Solve();
        var model = Build(geometry, layout, generated,
            new RoofAutomaticRafterPhysicalSettings(
                LowerEndCutMode.Vertical, mode));
        var ridgeMembers = model.Members.Where(member =>
            member.EndBoundaryRole == RoofRafterBoundaryRole.Ridge).ToArray();
        var pair = (from first in ridgeMembers
                    from second in ridgeMembers
                    where first.MemberKey.StationIndex < second.MemberKey.StationIndex &&
                          first.PlanAxis.End.DistanceTo(second.PlanAxis.End) < 1e-6 &&
                          Dot(Direction(first.PlanAxis.Start, first.PlanAxis.End),
                              Direction(second.PlanAxis.Start, second.PlanAxis.End)) < 0d
                    select (first, second)).First();

        foreach (var member in new[] { pair.first, pair.second })
        {
            var upper = Midpoint(member.SolidVertices[2], member.SolidVertices[3]);
            var lower = Midpoint(member.SolidVertices[6], member.SolidVertices[7]);
            var run = Direction(member.PlanAxis.Start, member.PlanAxis.End);
            var penetration = Dot(Direction(upper, lower), run) /
                Math.Sqrt(Dot(run, run));
            if (mode == RidgeJoinMode.Meet)
            {
                Assert.Equal(0d, penetration, 6);
            }
            else
            {
                Assert.True(penetration > 1d);
            }
        }
    }

    [Theory]
    [InlineData(30d)]
    [InlineData(35d)]
    [InlineData(45d)]
    [InlineData(60d)]
    public void OverlapRidgePair_StaysBelowBothActualRoofPlanes(double pitch)
    {
        var (geometry, layout, generated) = Solve(pitch);
        var overlap = Build(geometry, layout, generated,
            new RoofAutomaticRafterPhysicalSettings(
                LowerEndCutMode.Vertical, RidgeJoinMode.Overlap));
        var meet = Build(geometry, layout, generated,
            new RoofAutomaticRafterPhysicalSettings(
                LowerEndCutMode.Vertical, RidgeJoinMode.Meet));
        Assert.Equal(meet.Members.Select(member => member.MemberKey),
            overlap.Members.Select(member => member.MemberKey));
        Assert.All(overlap.Members.Zip(meet.Members), pair =>
            Assert.Equal(pair.Second.PlanAxis, pair.First.PlanAxis));
        var ridgeMembers = overlap.Members.Where(member =>
            member.EndBoundaryRole == RoofRafterBoundaryRole.Ridge).ToArray();
        var pair = (from first in ridgeMembers
                    from second in ridgeMembers
                    where first.MemberKey.StationIndex < second.MemberKey.StationIndex &&
                          first.PlanAxis.End.DistanceTo(second.PlanAxis.End) < 1e-6 &&
                          Dot(Direction(first.PlanAxis.Start, first.PlanAxis.End),
                              Direction(second.PlanAxis.Start, second.PlanAxis.End)) < 0d
                    select (first, second)).First();
        foreach (var (member, opposite) in new[]
                 { (pair.first, pair.second), (pair.second, pair.first) })
        {
            Assert.Equal(0d, member.PlanAxis.Start.Z);
            Assert.Equal(0d, member.PlanAxis.End.Z);
            Assert.NotEqual(member.MemberKey, opposite.MemberKey);
            var otherFace = geometry.Topology.Faces.Single(face =>
                face.SourceEdgeIndex == opposite.SourceFaceIndex);
            Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                geometry.Topology, otherFace, out var normal));
            var origin = geometry.Topology.Nodes[otherFace.BoundaryNodeIndices[0]];
            Assert.All(member.SolidVertices, point =>
                Assert.True(normal.X * (point.X - origin.X) +
                    normal.Y * (point.Y - origin.Y) +
                    normal.Z * (point.Z - origin.Z - 3000d) <= 1e-5));
            var ownFace = geometry.Topology.Faces.Single(face =>
                face.SourceEdgeIndex == member.SourceFaceIndex);
            Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                geometry.Topology, ownFace, out var ownNormal));
            var ownOrigin = geometry.Topology.Nodes[ownFace.BoundaryNodeIndices[0]];
            var upper = member.RidgeOverlapCut?.TopFaceVertices ??
                member.SolidVertices.Take(4).ToArray();
            Assert.All(upper, point =>
                Assert.InRange(Math.Abs(ownNormal.X * (point.X - ownOrigin.X) +
                    ownNormal.Y * (point.Y - ownOrigin.Y) +
                    ownNormal.Z * (point.Z - ownOrigin.Z - 3000d)), 0d, 1e-5));
            var direction = Direction(member.PlanAxis.Start, member.PlanAxis.End);
            var length = Math.Sqrt(Dot(direction, direction));
            Assert.True(member.SolidVertices.Max(point =>
                Dot(Direction(member.PlanAxis.End, point), direction) / length) > 1d);
            if (pitch == 45d)
                Assert.Null(member.RidgeOverlapCut);
            if (pitch == 60d)
            {
                var cut = Assert.IsType<RoofRidgeOverlapCut>(member.RidgeOverlapCut);
                Assert.True(cut.CutFaceVertices.Count >= 3);
                Assert.True(PolygonArea(cut.CutFaceVertices) >
                    member.WidthMm * member.HeightMm * 0.01d);
                for (var i = 0; i < cut.CutFaceVertices.Count; i++)
                    Assert.True(cut.CutFaceVertices[i].DistanceTo(
                        cut.CutFaceVertices[(i + 1) % cut.CutFaceVertices.Count]) > 1e-4);
            }
        }
    }

    [Theory]
    [InlineData(LowerEndCutMode.Vertical)]
    [InlineData(LowerEndCutMode.Perpendicular)]
    [InlineData(LowerEndCutMode.Horizontal)]
    public void Overlap_ComposesWithEachEaveCutMode(
        LowerEndCutMode lowerMode)
        => AssertOverlapComposesWithEaveCutMode(60d, lowerMode);

    [Theory]
    [InlineData(LowerEndCutMode.Vertical)]
    [InlineData(LowerEndCutMode.Perpendicular)]
    [InlineData(LowerEndCutMode.Horizontal)]
    public void ShallowOverlap_ComposesWithEachEaveCutMode(
        LowerEndCutMode lowerMode)
        => AssertOverlapComposesWithEaveCutMode(30d, lowerMode);

    private static void AssertOverlapComposesWithEaveCutMode(
        double pitch, LowerEndCutMode lowerMode)
    {
        var (geometry, layout, generated) = Solve(pitch);
        var model = Build(geometry, layout, generated,
            new RoofAutomaticRafterPhysicalSettings(
                lowerMode, RidgeJoinMode.Overlap));
        var clipped = model.Members.Where(member =>
            member.RidgeOverlapCut is not null).ToArray();
        Assert.NotEmpty(clipped);
        Assert.All(clipped, member =>
        {
            var plane = member.RidgeOverlapCut!;
            Assert.All(member.SolidVertices, point =>
                Assert.True(Dot(Direction(plane.PlanePoint, point),
                    plane.RetainedNormal) >= -1e-5));
            Assert.Equal(member.SolidVertices, plane.BodyVertices);
            if (pitch < 45d)
                Assert.True(plane.RidgeExtensionMm > 0d);
        });
    }

    [Theory]
    [InlineData(20d)]
    [InlineData(30d)]
    [InlineData(35d)]
    [InlineData(40d)]
    [InlineData(44d)]
    [InlineData(45d)]
    [InlineData(50d)]
    [InlineData(60d)]
    public void OverlapLowerRidgeEdges_ReachOppositeUpperPlane(double pitch) =>
        AssertOverlapLowerEdgeIntersections(pitch);

    [Theory]
    [InlineData(44.9d)]
    [InlineData(45.0d)]
    [InlineData(45.1d)]
    public void OverlapLowerRidgeEdges_AreContinuousAcrossFortyFiveDegrees(
        double pitch) => AssertOverlapLowerEdgeIntersections(pitch);

    private static void AssertOverlapLowerEdgeIntersections(double pitch)
    {
        var (geometry, layout, generated) = Solve(pitch);
        var model = Build(geometry, layout, generated,
            new RoofAutomaticRafterPhysicalSettings(
                LowerEndCutMode.Vertical, RidgeJoinMode.Overlap));
        var ridgeMembers = model.Members.Where(member =>
            member.EndBoundaryRole == RoofRafterBoundaryRole.Ridge).ToArray();
        var pair = (from first in ridgeMembers
                    from second in ridgeMembers
                    where first.MemberKey.StationIndex < second.MemberKey.StationIndex &&
                          first.PlanAxis.End.DistanceTo(second.PlanAxis.End) < 1e-6 &&
                          Dot(Direction(first.PlanAxis.Start, first.PlanAxis.End),
                              Direction(second.PlanAxis.Start, second.PlanAxis.End)) < 0d
                    select (first, second)).First();
        foreach (var (member, opposite) in new[]
                 { (pair.first, pair.second), (pair.second, pair.first) })
        {
            var otherFace = geometry.Topology.Faces.Single(face =>
                face.SourceEdgeIndex == opposite.SourceFaceIndex);
            Assert.True(RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(
                geometry.Topology, otherFace, out var normal));
            var origin = geometry.Topology.Nodes[otherFace.BoundaryNodeIndices[0]];
            var planePoint = new RoofPoint3D(origin.X, origin.Y, origin.Z + 3000d);
            var retained = new RoofPoint3D(-normal.X, -normal.Y, -normal.Z);
            var source = member.RidgeOverlapCut?.SourcePrismVertices ??
                member.SolidVertices;
            var finalBottom = member.RidgeOverlapCut?.BottomFaceVertices ??
                member.SolidVertices.Skip(4).ToArray();
            var direction = Direction(source[0], source[2]);
            var length = Math.Sqrt(Dot(direction, direction));
            direction = new RoofPoint3D(direction.X / length,
                direction.Y / length, direction.Z / length);
            var rate = Dot(direction, retained);
            Assert.True(rate < -1e-6);
            foreach (var lowerCorner in new[] { source[6], source[7] })
            {
                var signed = Dot(Direction(planePoint, lowerCorner), retained);
                // The source must reach or pass its actual ray/plane crossing;
                // otherwise a finite source end face survives under the ridge.
                Assert.True(signed <= 1e-5);
                var t = -signed / rate;
                var intersection = new RoofPoint3D(
                    lowerCorner.X + direction.X * t,
                    lowerCorner.Y + direction.Y * t,
                    lowerCorner.Z + direction.Z * t);
                Assert.Contains(finalBottom, point =>
                    point.DistanceTo(intersection) <= 1e-4);
            }
            Assert.All(member.SolidVertices, point =>
                Assert.True(Dot(Direction(planePoint, point), retained) >= -1e-5));
            if (pitch < 45d)
            {
                var cut = Assert.IsType<RoofRidgeOverlapCut>(member.RidgeOverlapCut);
                Assert.True(cut.RidgeExtensionMm > 0d);
                Assert.True(cut.CutFaceVertices.Count >= 3);
                Assert.True(PolygonArea(cut.CutFaceVertices) >
                    member.WidthMm * member.HeightMm * 0.01d);
                for (var i = 0; i < cut.CutFaceVertices.Count; i++)
                    Assert.True(cut.CutFaceVertices[i].DistanceTo(
                        cut.CutFaceVertices[(i + 1) % cut.CutFaceVertices.Count]) > 1e-4);
            }
        }
    }

    private static RoofAutomaticRafterPhysicalModel Build(
        HipRoofGeometry geometry,
        RoofFaceRafterLayout layout,
        RoofRafterLayout generated,
        RoofAutomaticRafterPhysicalSettings settings)
        => BuildAtElevation(geometry, layout, generated, settings, 3000d);

    private static RoofAutomaticRafterPhysicalModel BuildAtElevation(
        HipRoofGeometry geometry,
        RoofFaceRafterLayout layout,
        RoofRafterLayout generated,
        RoofAutomaticRafterPhysicalSettings settings,
        double eaveElevationMm)
    {
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "AB", geometry.Topology, layout, generated, eaveElevationMm, 80d, 125d,
            settings, out var model));
        return model!;
    }

    private static (HipRoofGeometry Geometry, RoofFaceRafterLayout Layout,
        RoofRafterLayout Generated) Solve(double pitch = 45d)
    {
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [
                new RoofPoint2D(0, 0),
                new RoofPoint2D(10000, 0),
                new RoofPoint2D(10000, 6000),
                new RoofPoint2D(0, 6000),
            ], true));
        Assert.True(footprint.IsValid);
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!, new RoofParameters(pitch), RoofKind.Hip));
        Assert.True(solved.IsValid);
        var geometry = (HipRoofGeometry)solved.Geometry!;
        var layout = RoofFaceRafterLayoutService.Create(geometry.Topology, 600d);
        Assert.True(layout.IsValid);
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry, layout.Layout!, 80d, out var generated));
        return (geometry, layout.Layout!, generated);
    }

    private static RoofPoint3D Midpoint(RoofPoint3D a, RoofPoint3D b) =>
        new((a.X + b.X) / 2d, (a.Y + b.Y) / 2d, (a.Z + b.Z) / 2d);

    private static RoofPoint3D Direction(RoofPoint3D a, RoofPoint3D b) =>
        new(b.X - a.X, b.Y - a.Y, b.Z - a.Z);

    private static double Dot(RoofPoint3D a, RoofPoint3D b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static double PolygonArea(IReadOnlyList<RoofPoint3D> points)
    {
        var total = new RoofPoint3D(0d, 0d, 0d);
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            total = new RoofPoint3D(
                total.X + a.Y * b.Z - a.Z * b.Y,
                total.Y + a.Z * b.X - a.X * b.Z,
                total.Z + a.X * b.Y - a.Y * b.X);
        }
        return Math.Sqrt(Dot(total, total)) / 2d;
    }
}
