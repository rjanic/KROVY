using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofStructuralCutAndResizeFailureRecoveryTests
{
    [Fact]
    public void TestA_OutsideFootprintTranslatedCut_IsNonContact_AndResizePolyhedronSucceeds()
    {
        var fixture = CreateHipFixture();
        var contact = fixture.Request.OrdinaryMembers.First(member =>
            member.StructuralCut?.TopologyEdgeIndex == fixture.EdgeIndex);
        var moved = TranslateMember(contact, 5000, -2700);
        Assert.Equal(
            RoofStructuralOrdinaryContactRules.Classification.NonContact,
            RoofStructuralOrdinaryContactRules.Classify(
                moved,
                fixture.Topology.Segment(fixture.Topology.Edges[fixture.EdgeIndex]),
                fixture.EdgeIndex,
                RoofRafterBoundaryRole.Hip,
                fixture.Request.StructuralWidthMm,
                out _));

        var members = fixture.Request.OrdinaryMembers
            .Select(member => member.MemberKey == contact.MemberKey ? moved : member)
            .ToArray();
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request with { OrdinaryMembers = members },
            out var body,
            out var reason), reason);
        Assert.NotNull(body);
        Assert.NotEqual("OrdinaryCutNotOnStructuralSide", reason);
    }

    [Fact]
    public void TestB_OnSideOverriddenOrdinary_RemainsContact()
    {
        var fixture = CreateHipFixture();
        var contact = fixture.Request.OrdinaryMembers.First(member =>
            member.StructuralCut?.TopologyEdgeIndex == fixture.EdgeIndex &&
            member.StructuralCut.CutFaceVertices.Count >= 3);
        Assert.Equal(
            RoofStructuralOrdinaryContactRules.Classification.Contact,
            RoofStructuralOrdinaryContactRules.Classify(
                contact,
                fixture.Topology.Segment(fixture.Topology.Edges[fixture.EdgeIndex]),
                fixture.EdgeIndex,
                RoofRafterBoundaryRole.Hip,
                fixture.Request.StructuralWidthMm,
                out var detail));
        Assert.Equal("contact", detail);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var body, out var reason), reason);
        Assert.NotNull(body);
    }

    [Fact]
    public void TestC_CanonicalIntersectingOrdinary_Unchanged()
    {
        var fixture = CreateHipFixture();
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var body, out var reason), reason);
        Assert.NotNull(body);
        Assert.True(body!.Geometry.PhysicalVerticalHeightMm > 0d);
        Assert.Contains(fixture.Request.OrdinaryMembers, member =>
            member.StructuralCut?.TopologyEdgeIndex == fixture.EdgeIndex &&
            RoofStructuralOrdinaryContactRules.Classify(
                member,
                fixture.Topology.Segment(fixture.Topology.Edges[fixture.EdgeIndex]),
                fixture.EdgeIndex,
                RoofRafterBoundaryRole.Hip,
                fixture.Request.StructuralWidthMm,
                out _) == RoofStructuralOrdinaryContactRules.Classification.Contact);
    }

    [Fact]
    public void TestD_ForcedStructuralFailure_RestoresFullPreCommandAggregateAndRuntime()
    {
        var session = NewSession();
        session.InjectedFailureAtStructuralPhysical =
            RoofSupportedResizeFailureRecoveryRules.OrdinaryCutNotOnStructuralSideFailure;
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.SourceDetected,
            Mutated(session.PreCommand)));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.OrdinaryRebuilt));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.OverrideReplayed));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.GroupTouched));
        Assert.False(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.StructuralPhysical));
        Assert.Equal(RoofSupportedResizeFailureRecoveryRules.Phase.Failed, session.CurrentPhase);
        Assert.Equal(
            RoofSupportedResizeFailureRecoveryRules.OrdinaryCutNotOnStructuralSideFailure,
            session.FailureReason);
        Assert.False(session.Runtime.IsFullyClean);
        Assert.True(session.TryRecoverFromHardFailure());
        Assert.Equal(RoofSupportedResizeFailureRecoveryRules.Phase.Recovered, session.CurrentPhase);
        Assert.True(session.MatchesPreCommand());
        Assert.True(session.Runtime.IsFullyClean);
        Assert.True(session.Runtime.NextCommandReady);
        Assert.False(session.Runtime.PendingResize);
        Assert.False(session.Runtime.HasStaleSnapshot);
        Assert.False(session.Runtime.SuppressionActive);
        Assert.False(session.Runtime.ReentrancyActive);
    }

    [Fact]
    public void TestE_F_G_ImmediatelyAfterRollback_OrdinaryAndSourceEditsWork()
    {
        var session = FailAndRecover();
        Assert.True(session.TryExecuteAfterRecovery(
            RoofSupportedResizeFailureRecoveryRules.LifecycleOperation.OrdinaryMove));
        Assert.Equal(1, session.SuccessfulOrdinaryMoveCount);
        Assert.True(session.Runtime.IsFullyClean);

        Assert.True(session.TryExecuteAfterRecovery(
            RoofSupportedResizeFailureRecoveryRules.LifecycleOperation.OrdinaryGripStretch));
        Assert.Equal(1, session.SuccessfulOrdinaryGripStretchCount);
        Assert.True(session.Runtime.IsFullyClean);

        Assert.True(session.TryExecuteAfterRecovery(
            RoofSupportedResizeFailureRecoveryRules.LifecycleOperation.SourceGripStretch));
        Assert.Equal(1, session.SuccessfulSourceResizeCount);
        Assert.Equal(RoofSupportedResizeFailureRecoveryRules.Phase.Commit, session.CurrentPhase);
        Assert.True(session.Runtime.IsFullyClean);
    }

    [Fact]
    public void TestH_RepeatedFailureSuccess_DoesNotDegradeLifecycle()
    {
        var session = NewSession();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            session.InjectedFailureAtStructuralPhysical = null;
            Assert.True(session.TryExecuteAfterRecovery(
                RoofSupportedResizeFailureRecoveryRules.LifecycleOperation.SourceGripStretch));
            Assert.Equal(cycle + 1, session.SuccessfulSourceResizeCount);
            Assert.True(session.Runtime.IsFullyClean);

            session.InjectedFailureAtStructuralPhysical =
                RoofSupportedResizeFailureRecoveryRules.OrdinaryCutNotOnStructuralSideFailure;
            Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.SourceDetected,
                Mutated(session.PreCommand)));
            Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.OrdinaryRebuilt));
            Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.OverrideReplayed));
            Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.GroupTouched));
            Assert.False(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.StructuralPhysical));
            Assert.True(session.TryRecoverFromHardFailure());
            Assert.True(session.TryExecuteAfterRecovery(
                RoofSupportedResizeFailureRecoveryRules.LifecycleOperation.OrdinaryMove));
            Assert.True(session.TryExecuteAfterRecovery(
                RoofSupportedResizeFailureRecoveryRules.LifecycleOperation.OrdinaryGripStretch));
        }

        Assert.Equal(2, session.ForcedFailureCount);
        Assert.Equal(2, session.SuccessfulSourceResizeCount);
        Assert.Equal(2, session.SuccessfulOrdinaryMoveCount);
        Assert.Equal(2, session.SuccessfulOrdinaryGripStretchCount);
        Assert.True(session.Runtime.IsFullyClean);
        Assert.True(session.MatchesPreCommand());
    }

    [Fact]
    public void CorruptOnEdgeButOffSideCut_StillHardFails()
    {
        var fixture = CreateHipFixture();
        var contact = fixture.Request.OrdinaryMembers.First(member =>
            member.StructuralCut?.TopologyEdgeIndex == fixture.EdgeIndex);
        var cut = contact.StructuralCut!;
        var corrupt = contact with
        {
            StructuralCut = cut with
            {
                PlanePoint = new RoofPoint3D(
                    cut.PlanePoint.X + cut.PlaneNormal.X * 500d,
                    cut.PlanePoint.Y + cut.PlaneNormal.Y * 500d,
                    cut.PlanePoint.Z),
                CutFaceVertices = cut.CutFaceVertices
                    .Select(point => new RoofPoint3D(
                        point.X + cut.PlaneNormal.X * 500d,
                        point.Y + cut.PlaneNormal.Y * 500d,
                        point.Z))
                    .ToArray(),
            },
        };
        Assert.True(RoofStructuralOrdinaryContactRules.PlanTouchesStructuralEdge(
            corrupt.PlanAxis,
            fixture.Topology.Segment(fixture.Topology.Edges[fixture.EdgeIndex])));
        Assert.Equal(
            RoofStructuralOrdinaryContactRules.Classification.Corrupt,
            RoofStructuralOrdinaryContactRules.Classify(
                corrupt,
                fixture.Topology.Segment(fixture.Topology.Edges[fixture.EdgeIndex]),
                fixture.EdgeIndex,
                RoofRafterBoundaryRole.Hip,
                fixture.Request.StructuralWidthMm,
                out _));
        var members = fixture.Request.OrdinaryMembers
            .Select(member => member.MemberKey == contact.MemberKey ? corrupt : member)
            .ToArray();
        Assert.False(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request with { OrdinaryMembers = members },
            out _,
            out var reason));
        Assert.Equal("OrdinaryCutNotOnStructuralSide", reason);
    }

    private static RoofSupportedResizeFailureRecoveryRules.Session FailAndRecover()
    {
        var session = NewSession();
        session.InjectedFailureAtStructuralPhysical =
            RoofSupportedResizeFailureRecoveryRules.OrdinaryCutNotOnStructuralSideFailure;
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.SourceDetected,
            Mutated(session.PreCommand)));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.OrdinaryRebuilt));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.OverrideReplayed));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.GroupTouched));
        Assert.False(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.StructuralPhysical));
        Assert.True(session.TryRecoverFromHardFailure());
        return session;
    }

    private static RoofSupportedResizeFailureRecoveryRules.Session NewSession() =>
        new(new RoofSupportedResizeFailureRecoveryRules.AggregateSnapshot(
            "source-v1",
            "plan-v1",
            "ordinary-phys-v1",
            "structural-phys-v1",
            "overrides-v1",
            "annotations-v1",
            GroupCanonical: true));

    [Fact]
    public void RecoveryVerdict_FailsWhenGroupCanonicalFalse()
    {
        Assert.False(RoofSupportedResizeFailureRecoveryRules.IsRecoveryVerdictOk(
            dbRollback: true,
            runtimeReset: true,
            pendingResize: false,
            suppressionDepth: 0,
            hasActiveOwner: false,
            groupCanonical: false,
            sourceGeometryRestored: true,
            roofDefinitionRestored: true,
            physicalInventoryRestored: true,
            annotationsRestored: true,
            nextCommandReady: true));
        Assert.True(RoofSupportedResizeFailureRecoveryRules.IsRecoveryVerdictOk(
            dbRollback: true,
            runtimeReset: true,
            pendingResize: false,
            suppressionDepth: 0,
            hasActiveOwner: false,
            groupCanonical: true,
            sourceGeometryRestored: true,
            roofDefinitionRestored: true,
            physicalInventoryRestored: true,
            annotationsRestored: true,
            nextCommandReady: true));
    }

    [Fact]
    public void PartialRestore_WithWrongRigidFootprintOrGroupCount_IsNotExact()
    {
        var session = NewSession();
        session.InjectedFailureAtStructuralPhysical =
            RoofSupportedResizeFailureRecoveryRules.OrdinaryCutNotOnStructuralSideFailure;
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.SourceDetected,
            Mutated(session.PreCommand)));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.OrdinaryRebuilt));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.OverrideReplayed));
        Assert.True(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.GroupTouched));
        Assert.False(session.TryAdvance(RoofSupportedResizeFailureRecoveryRules.Phase.StructuralPhysical));
        Assert.False(session.TryRecoverFromHardFailure(forceGroupCanonicalFalse: true));
        Assert.False(session.CompareToPreCommand().IsExactRestore);
        Assert.Equal(7138.038790322185d, session.Live.RigidFootprintEdge12Mm);
        Assert.Equal(182, session.Live.GroupMemberCount);
    }

    [Fact]
    public void MoveThenLateralGrip_Physical3DFollowsRotatedPlanWithoutExtraYaw()
    {
        var (roof, faces, layout) = SolveContinuity();
        var rafter = layout.Rafters.First(item => item.StationIndex == 2);
        var canonical = RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0);
        var moved = new RoofGeneratedMemberGeometry(
            Shift(canonical.Start, 5000, -2700), Shift(canonical.End, 5000, -2700));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, moved, RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
            rafter.LogicalKey, "K6", out var moveOverride));
        var diagonal = moved with
        {
            End = new RoofPoint3D(moved.End.X - 541.1, moved.End.Y + 2898.0, 0d),
        };
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, diagonal, RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
            rafter.LogicalKey, "K6", out var rotatedOverride));
        Assert.True(Math.Abs(rotatedOverride!.RotationRadians) > 1e-3);
        var replay = RoofAcceptedOrdinaryOverrideReplayRules.Apply(
            RoofGeneratedMemberReplayPlanner.CreateForExistingPhysicalMembers(
                layout, new[] { rotatedOverride },
                new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry>
                {
                    [rafter.LogicalKey] = diagonal,
                }),
            RoofEditState.Unlocked,
            new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry>
            {
                [rafter.LogicalKey] = diagonal,
            });
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "2912", roof.Topology, faces, layout, 0, 80, 125,
            new RoofAutomaticRafterPhysicalSettings(), replay,
            StructuralSources(roof), out var model, out var failure), failure);
        var member = Assert.Single(model!.Members, item => item.MemberKey == rafter.LogicalKey);
        Assert.True(RoofOrdinaryPhysicalPlanFrameRules.TryDescribe(
            member.PlanAxis, member.SolidVertices, member.WidthMm,
            out var frame, out var reason), reason);
        Assert.True(frame!.AxesAligned, "plan/physical XY axes diverge");
        Assert.True(frame.SectionOrthogonalToPlan, "section not orthogonal to longitudinal");
        Assert.False(frame.ExtraYawRelativeToPlan, "Physical3D introduced yaw beyond Plan");
        Assert.True(frame.IsFaithful);
        Assert.Equal(diagonal.Start.X, member.PlanAxis.Start.X, 5);
        Assert.Equal(diagonal.End.Y, member.PlanAxis.End.Y, 5);
        // Legitimate Plan rotation is expected for lateral endpoint grip; Physical3D must follow it.
        Assert.True(Math.Abs(rotatedOverride.RotationRadians) > 1e-3);
        _ = moveOverride;
    }

    [Fact]
    public void MoveThenLongitudinalGrip_DoesNotIntroduceUnexpectedRotation()
    {
        var (roof, faces, layout) = SolveContinuity();
        var rafter = layout.Rafters.Single(item => item.StationIndex == 2);
        var canonical = RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0);
        var moved = new RoofGeneratedMemberGeometry(
            Shift(canonical.Start, 5000, -2700), Shift(canonical.End, 5000, -2700));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, moved, RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
            rafter.LogicalKey, "K2", out var moveOverride));
        var extended = moved with { End = Shift(moved.End, 0, 600) };
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            moved, extended, RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal,
            out var startDelta, out var endDelta, out _, out _));
        var cumulative = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            moveOverride!, rafter.LogicalKey, "K2", startDelta, endDelta)!;
        Assert.InRange(Math.Abs(cumulative.RotationRadians), 0d, 1e-9);
        var replay = RoofAcceptedOrdinaryOverrideReplayRules.Apply(
            RoofGeneratedMemberReplayPlanner.CreateForExistingPhysicalMembers(
                layout, new[] { cumulative },
                new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry>
                {
                    [rafter.LogicalKey] = extended,
                }),
            RoofEditState.Unlocked,
            new Dictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry>
            {
                [rafter.LogicalKey] = extended,
            });
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "2912", roof.Topology, faces, layout, 0, 80, 125,
            new RoofAutomaticRafterPhysicalSettings(), replay,
            StructuralSources(roof), out var model, out var failure), failure);
        var member = Assert.Single(model!.Members, item => item.MemberKey == rafter.LogicalKey);
        Assert.True(RoofOrdinaryPhysicalPlanFrameRules.TryDescribe(
            member.PlanAxis, member.SolidVertices, member.WidthMm,
            out var frame, out var reason), reason);
        Assert.True(frame!.IsFaithful);
        Assert.InRange(Math.Abs(cumulative.RotationRadians), 0d, 1e-9);
    }

    private static RoofSupportedResizeFailureRecoveryRules.AggregateSnapshot Mutated(
        RoofSupportedResizeFailureRecoveryRules.AggregateSnapshot pre) =>
        pre with
        {
            SourceGeometryToken = pre.SourceGeometryToken + "-stretched",
            GeneratedPlanToken = pre.GeneratedPlanToken + "-rebuilt",
            GroupCanonical = false,
            RigidFootprintEdge12Mm = 7138.038790322185d,
            GroupMemberCount = 182,
        };

    private static (HipRoofGeometry, RoofFaceRafterLayout, RoofRafterLayout) SolveContinuity()
    {
        const double x = 33991.69391084029, y = 10951.930768028607;
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(x, y), new(x + 10000, y), new(x + 10000, y + 6000), new(x, y + 6000)], true));
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!, new RoofParameters(30), RoofKind.Hip));
        Assert.True(solved.IsValid);
        var roof = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var faces = RoofFaceRafterLayoutService.Create(roof.Topology, 600).Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            roof, faces, 80, out var layout));
        return (roof, faces, layout);
    }

    private static IReadOnlyList<RoofStructuralRafterTrimSource> StructuralSources(HipRoofGeometry roof) =>
        roof.Topology.Edges.Select((edge, index) => (edge, index))
            .Where(item => item.edge.Kind is RoofTopologyEdgeKind.Hip or RoofTopologyEdgeKind.Valley)
            .Select(item => new RoofStructuralRafterTrimSource(
                item.index,
                item.edge.Kind == RoofTopologyEdgeKind.Hip
                    ? RoofRafterBoundaryRole.Hip : RoofRafterBoundaryRole.Valley,
                roof.Topology.Segment(item.edge), 120))
            .ToArray();

    private static RoofPoint3D Shift(RoofPoint3D p, double dx, double dy) =>
        new(p.X + dx, p.Y + dy, p.Z);

    private static RoofAutomaticRafterPhysicalMember TranslateMember(
        RoofAutomaticRafterPhysicalMember member, double dx, double dy)
    {
        RoofPoint3D Shift(RoofPoint3D point) => new(point.X + dx, point.Y + dy, point.Z);
        IReadOnlyList<RoofPoint3D> ShiftAll(IReadOnlyList<RoofPoint3D> points) =>
            points.Select(Shift).ToArray();
        return member with
        {
            PlanAxis = new RoofSegment3D(Shift(member.PlanAxis.Start), Shift(member.PlanAxis.End)),
            SolidVertices = ShiftAll(member.SolidVertices),
            StructuralCut = member.StructuralCut is { } cut
                ? cut with
                {
                    PlanePoint = Shift(cut.PlanePoint),
                    SourcePrismVertices = ShiftAll(cut.SourcePrismVertices),
                    CutFaceVertices = ShiftAll(cut.CutFaceVertices),
                    TopFaceVertices = ShiftAll(cut.TopFaceVertices),
                    BottomFaceVertices = cut.BottomFaceVertices is { } bottom ? ShiftAll(bottom) : null,
                    LowerContactEdge = cut.LowerContactEdge is { } edge
                        ? new RoofSegment3D(Shift(edge.Start), Shift(edge.End)) : null,
                }
                : null,
        };
    }

    private static (
        RoofTopology Topology,
        int EdgeIndex,
        RoofStructuralRafterPolyhedronRequest Request) CreateHipFixture()
    {
        var points = new[]
        {
            new RoofPoint2D(0, 0), new RoofPoint2D(10000, 0),
            new RoofPoint2D(10000, 6000), new RoofPoint2D(0, 6000),
        };
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(points, true));
        Assert.True(footprint.IsValid);
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!, new RoofParameters(45d), RoofKind.Hip));
        Assert.True(solved.IsValid);
        var geometry = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var topology = geometry.Topology;
        var layoutResult = RoofFaceRafterLayoutService.Create(topology, 500d);
        Assert.True(layoutResult.IsValid);
        var layout = layoutResult.Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry, layout, 80d, out var generated));
        const double structuralWidth = 120d;
        var sources = topology.Edges.Select((edge, index) => (edge, index))
            .Where(item => item.edge.Kind is RoofTopologyEdgeKind.Hip or RoofTopologyEdgeKind.Valley)
            .Select(item => new RoofStructuralRafterTrimSource(
                item.index,
                item.edge.Kind == RoofTopologyEdgeKind.Hip
                    ? RoofRafterBoundaryRole.Hip : RoofRafterBoundaryRole.Valley,
                topology.Segment(item.edge),
                structuralWidth))
            .ToArray();
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "roof", topology, layout, generated, 3000d, 80d, 160d,
            new RoofAutomaticRafterPhysicalSettings(LowerEndCutMode.Vertical), null, sources,
            out var ordinary, out var ordinaryReason), ordinaryReason);
        var selected = topology.Edges.Select((edge, index) => (edge, index))
            .First(item => item.edge.Kind == RoofTopologyEdgeKind.Hip &&
                ordinary!.Members.Any(member =>
                    member.StructuralCut?.TopologyEdgeIndex == item.index));
        var faceIds = selected.edge.FaceIndices
            .Select(index => topology.Faces[index].SourceEdgeIndex + 1)
            .OrderBy(index => index).ToArray();
        var resolved = new ResolvedRoofStructuralEdge(
            new RoofStructuralLogicalKey(RoofStructuralRole.Hip, faceIds[0], faceIds[1]),
            selected.index, topology.Segment(selected.edge),
            IsPhysicalFoldTimberEligible: true);
        return (topology, selected.index,
            new RoofStructuralRafterPolyhedronRequest(topology, resolved,
                3000d, structuralWidth, RoofStructuralHeightMode.Automatic,
                null, ordinary!.Members));
    }
}
