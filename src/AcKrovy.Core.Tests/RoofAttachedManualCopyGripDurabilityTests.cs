using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAttachedManualCopyGripDurabilityTests
{
    private sealed record Fixture(HipRoofGeometry Roof, RoofFaceRafterLayout Faces,
        RoofAutomaticRafterPhysicalModel Generated, RoofAutomaticRafterPhysicalMember Source);

    [Theory]
    [InlineData(1, -162.1840056343167)]
    [InlineData(2, -162.1840056343167)]
    [InlineData(2, 0)]
    public void SequentialCopyCommands_NormalizeNativeZ_KeepEveryCloneAndOneBody(int count, double nativeZ)
    {
        var f = Solve();
        var children = new List<RoofAttachedManualPhysicalInput>();
        var bodies = f.Generated.Members.Select((member, index) =>
            new RoofOrdinaryPhysicalBinding("source" + index, member.PhysicalIdentity)).ToList();
        for (var command = 0; command < count; command++)
        {
            var source = Geometry(f.Source.PlanAxis);
            var native = Translate(source, 5991 + command * 500, -149, nativeZ);
            if (Math.Abs(nativeZ) > 1)
            {
                // Reproduce the first divergent HOST state: raw native Plan Z
                // cannot be reconciled with the source-frame physical reference.
                var raw = Child(f, native);
                Assert.False(RoofAttachedManualPhysicalBuilder.TryAppend(f.Roof.Topology, f.Faces, f.Generated,
                    [new(raw, Segment(native), 80, 125)], 3000, new(), null, out _, out _));
            }
            Assert.True(RoofOrdinaryCopyPlanRules.TryAccept(source, native, out var accepted));
            Assert.Equal(native.Start.X, accepted.Start.X, 6);
            Assert.Equal(native.Start.Y, accepted.Start.Y, 6);
            Assert.Equal(0, accepted.Start.Z);
            Assert.Equal(0, accepted.End.Z);
            var data = Child(f, accepted);
            Assert.DoesNotContain(children, child => child.Metadata.SemanticIdentity == data.SemanticIdentity);
            children.Add(new(data, Segment(accepted), 80, 125));
            var model = Append(f, children.ToArray());
            Assert.Equal(f.Generated.Members.Count + command + 1, model.Members.Count);
            Assert.Same(f.Source, model.Members.Single(member => member.PhysicalIdentity == f.Source.PhysicalIdentity));
            // Native solid duplicate is collateral of THIS COPY; accepted prior
            // AttachedManual bodies must survive the next command's reconciliation.
            bodies.Add(new("native" + command, f.Source.PhysicalIdentity, true));
            Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(
                model.Members.Select(member => member.PhysicalIdentity).ToArray(), [], bodies, true, out var plan));
            Assert.Equal(["native" + command], plan!.RemoveBodyIdentities);
            Assert.Equal([RoofAttachedManualIdentityRules.PhysicalKey(data)], plan.RebuildKeys);
            bodies.RemoveAll(body => plan.RemoveBodyIdentities.Contains(body.BodyIdentity));
            bodies.AddRange(plan.RebuildKeys.Select(key => new RoofOrdinaryPhysicalBinding("rebuilt:" + key, key)));
            Assert.True(RoofOrdinaryPhysicalReconciliationRules.IsCanonical(
                model.Members.Select(member => member.PhysicalIdentity).ToArray(), bodies.Select(body => body.SemanticKey).ToArray()));
            Assert.All(children, child => Assert.Single(bodies, body =>
                body.SemanticKey == RoofAttachedManualIdentityRules.PhysicalKey(child.Metadata)));
            var expectedGroup = new[] { "roof", "sourcePlan" }.Concat(bodies.Select(body => body.BodyIdentity))
                .Concat(children.SelectMany(child => new[] { child.Metadata.ChildIdentity,
                    "label:" + child.Metadata.SemanticIdentity, "slope:" + child.Metadata.SemanticIdentity })).ToArray();
            Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(expectedGroup, expectedGroup));
        }
    }

    [Theory]
    [InlineData(false, 200, 0)]
    [InlineData(false, 200, 700)]
    [InlineData(true, -200, 697.10926668308)]
    [InlineData(true, 66.921405571, 697.10926668308)]
    [InlineData(false, 0, 800)]
    [InlineData(true, 0, 1000)]
    [InlineData(true, 700, 1200)]
    [InlineData(false, 700, 1200)]
    public void EndpointGrip_AcceptsNativeXY_PreservesIdentityAndRoofPlaneFrame(bool start, double along, double lateral)
    {
        var f = Solve();
        var before = Translate(Geometry(f.Source.PlanAxis), 5991, -149);
        var data = Child(f, before);
        var bodyBefore = Append(f, [new(data, Segment(before), 80, 125)]).Members.Single(member => member.AttachedManualIdentity is not null);
        var accepted = Accept(f, data, before, Drag(before, start, along, lateral));
        Assert.Equal(start ? "Start" : "End", accepted.Endpoint);
        Assert.False(accepted.Grip.Clamped);
        Assert.Equal(Drag(before, start, along, lateral), accepted.Geometry);
        AssertPoint(start ? before.End : before.Start, start ? accepted.Geometry.End : accepted.Geometry.Start);
        if (lateral != 0) Assert.NotEqual(accepted.Grip.AxisBefore, accepted.Grip.AxisAfter);
        Assert.Equal(data.SemanticIdentity, accepted.Metadata.SemanticIdentity);
        Assert.Equal(data.ChildIdentity, accepted.Metadata.ChildIdentity);
        Assert.Equal(data.RoofOwnerReference, accepted.Metadata.RoofOwnerReference);
        Assert.Equal(data.AnchorGeneratedMemberKey, accepted.Metadata.AnchorGeneratedMemberKey);
        Assert.Equal(data.Origin, accepted.Metadata.Origin);
        Assert.NotNull(accepted.Metadata.PhysicalReferenceSegment);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(f.Source.PlanAxis.Start, f.Source.PlanAxis.End,
            accepted.Metadata.RelativeSegment!, out var replayStart, out var replayEnd));
        AssertPoint(accepted.Geometry.Start, replayStart);
        AssertPoint(accepted.Geometry.End, replayEnd);
        var after = Append(f, [new(RoundTrip(accepted.Metadata), Segment(accepted.Geometry), 80, 125)])
            .Members.Single(member => member.AttachedManualIdentity is not null);
        Assert.Equal(Segment(accepted.Geometry), after.PlanAxis);
        var frameBefore = Frame(bodyBefore);
        var frameAfter = Frame(after);
        Assert.True(frameAfter.IsFaithful);
        AssertRoofFrame(f, after);
        Assert.Equal(bodyBefore.WidthMm, after.WidthMm);
        Assert.Equal(bodyBefore.HeightMm, after.HeightMm);
        if (Math.Abs(along) > 1e-5) Assert.NotEqual(bodyBefore.PhysicalLengthMm, after.PhysicalLengthMm);
    }

    [Fact]
    public void CopyMoveThenRepeatedFreeformGrips_PreservePlanPlacement_NoAccumulatedTwist()
    {
        var f = Solve();
        var live = Translate(Geometry(f.Source.PlanAxis), 5991, -149);
        var data = Child(f, live);
        // Existing MOVE persists its Plan pose and retains the pre-copy physical reference.
        live = Translate(live, -500, 375);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(f.Source.PlanAxis.Start, f.Source.PlanAxis.End,
            live.Start, live.End, out var movedRelative));
        data = data with { RelativeSegment = movedRelative };
        var originalIdentity = data.SemanticIdentity;
        var fixedStart = live.Start;
        var beforeFrame = Frame(Append(f, [new(data, Segment(live), 80, 125)])
            .Members.Single(member => member.AttachedManualIdentity is not null));
        for (var index = 0; index < 6; index++)
        {
            var grip = Accept(f, data, live, Drag(live, false, -80, 500 + index * 120));
            Assert.Equal(Drag(live, false, -80, 500 + index * 120), grip.Geometry);
            AssertPoint(fixedStart, grip.Geometry.Start);
            data = RoundTrip(grip.Metadata);
            live = grip.Geometry;
            Assert.Equal(originalIdentity, data.SemanticIdentity);
            var body = Append(f, [new(data, Segment(live), 80, 125)])
                .Members.Single(member => member.AttachedManualIdentity is not null);
            var frame = Frame(body);
            Assert.True(frame.IsFaithful);
            AssertRoofFrame(f, body);
        }
    }

    [Fact]
    public void CopyOfCopy_GetFreshUuid_AndRetainEditedReference()
    {
        var f = Solve();
        var original = Translate(Geometry(f.Source.PlanAxis), 500, 100);
        var grip = Accept(f, Child(f, original), original, Drag(original, false, -300, 700));
        var copy = Translate(grip.Geometry, 250, 40, -162);
        Assert.True(RoofOrdinaryCopyPlanRules.TryAccept(grip.Geometry, copy, out var accepted));
        var second = Child(f, accepted) with { PhysicalReferenceSegment = grip.Metadata.PhysicalReferenceSegment };
        Assert.NotEqual(grip.Metadata.SemanticIdentity, second.SemanticIdentity);
        var model = Append(f, [new(grip.Metadata, Segment(grip.Geometry), 80, 125), new(second, Segment(accepted), 80, 125)]);
        Assert.Equal(f.Generated.Members.Count + 2, model.Members.Count);
        Assert.Equal(model.Members.Count, model.Members.Select(member => member.PhysicalIdentity).Distinct().Count());
    }

    [Fact]
    public void RotatedCurrentAxis_AcceptsNativeXY_AndCapturesEditedPhysicalReference()
    {
        var before = new RoofGeneratedMemberGeometry(new(40000, 12000, 0), new(41200, 13600, 0));
        var key = new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 8);
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(before.Start, before.End,
            before.Start, before.End, out var relative));
        var data = new RoofAttachedManualTimberData(5, "AB", "SOURCE", RoofTimberChildRole.AttachedManual,
            key, relative, RoofAttachedManualOrigin.Copy, RoofAttachedManualIdentityRules.Create());
        Assert.True(RoofAttachedManualGripRules.TryAccept(data, before.Start, before.End,
            before, Drag(before, false, 300, 1000), out var accepted));
        Assert.Equal(Drag(before, false, 300, 1000), accepted!.Geometry);
        Assert.Equal(Math.Sqrt(2300 * 2300 + 1000 * 1000), accepted.Geometry.LengthMm, 6);
        Assert.Equal(data.SemanticIdentity, accepted.Metadata.SemanticIdentity);
        Assert.Equal(accepted.Metadata.RelativeSegment, accepted.Metadata.PhysicalReferenceSegment);
    }

    [Fact]
    public void HostRouting_CopyCannotEnterManualEditRecovery_AndNormalizationPrecedesPromotion()
    {
        var resize = Read("RoofLiveResizeService.cs");
        var routing = RoofUxSourceContractText.Member(resize, "var acceptedOrdinaryStretchOwnerIds", "if (plan.DerivedPhysicalMoveMembers.Count");
        Assert.Contains("!LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(globalCommandName)", routing);
        var manual = Read("RoofGeneratedMemberManualEditService.cs");
        Assert.True(manual.IndexOf("if (LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(globalCommandName))", StringComparison.Ordinal) <
            manual.IndexOf("outcome = ProcessOwner", StringComparison.Ordinal));
        var live = Read("LiveGeometrySynchronizationService.cs");
        Assert.True(live.IndexOf("RoofOrdinaryCopyPlanRules.TryAccept", StringComparison.Ordinal) <
            live.IndexOf("Process(_document, command, copyCandidates, propagateFailure: true)", StringComparison.Ordinal));
        Assert.Contains("ROOF_COPY_FINAL", live);
        Assert.Contains("physicalCount=", live);
        Assert.Contains("recoveryClaimed=", live);
        var attached = Read("RoofAttachedManualLifecycleService.cs");
        Assert.Contains("RoofAttachedManualGripRules.TryAccept", attached);
        Assert.Contains("gripSnapshot.Assembly.TimberLines", attached);
        Assert.Contains("childLine.StartPoint = ToAcad(grip.Geometry.Start)", attached);
        Assert.Contains("kind: \"AttachedManual\"", attached);
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand("COPY"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("COPY"));
        Assert.True(RoofUnsupportedStretchRecoveryRules.IsRecoveryCommand("STRETCH"));
        Assert.True(RoofUnsupportedStretchRecoveryRules.IsRecoveryCommand("GRIP_STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("TRIM"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("EXTEND"));
    }

    [Fact]
    public void InvalidGripAndInvalidCopy_CannotBecomeAcceptedGeometry()
    {
        var f = Solve();
        var before = Geometry(f.Source.PlanAxis);
        var data = Child(f, before);
        Assert.False(RoofOrdinaryCopyPlanRules.TryAccept(before, before with { End = Shift(before.End, 70, 0) }, out _));
        Assert.False(RoofOrdinaryCopyPlanRules.TryAccept(before, before with { Start = new(double.NaN, 0, 0) }, out _));
        Assert.False(RoofAttachedManualGripRules.TryAccept(data, before.Start, before.End, before,
            Drag(Drag(before, true, 50, 100), false, 100, 200), out _));
        Assert.False(RoofAttachedManualGripRules.TryAccept(data with { Origin = RoofAttachedManualOrigin.Split },
            before.Start, before.End, before, Drag(before, false, 100, 700), out _));
    }

    [Theory]
    [InlineData(true, 445, 1306)]
    [InlineData(false, 445, 1306)]
    [InlineData(true, 100000000, 100000000)]
    [InlineData(false, 100000000, -100000000)]
    public void CrossingGrips_KeepLateralAndPositiveGap_IdentityAnchorFrameAndCanonicalBody(
        bool start, double crossing, double lateral)
    {
        var f = Solve();
        var original = Translate(Geometry(f.Source.PlanAxis), 5991, -149);
        var live = original;
        var data = Child(f, live);
        var originalData = data;
        var childKey = RoofAttachedManualIdentityRules.PhysicalKey(data);
        var bodies = f.Generated.Members.Select(member => new RoofOrdinaryPhysicalBinding(
            "source:" + member.PhysicalIdentity, member.PhysicalIdentity)).Append(new("child:0", childKey)).ToList();
        var frameBefore = Frame(Append(f, [new(data, Segment(live), 80, 125)])
            .Members.Single(member => member.AttachedManualIdentity is not null));
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var along = (start ? 1 : -1) * (live.LengthMm + crossing);
            var grip = Accept(f, data, live, Drag(live, start, along, lateral));
            Assert.Equal(start ? "Start" : "End", grip.Endpoint);
            Assert.True(grip.Grip.Clamped);
            Assert.Equal(500, grip.Grip.MinimumLengthMm);
            Assert.True(grip.Geometry.LengthMm >= 500 - 1e-5);
            var u = grip.Grip.AxisBefore;
            var priorPoint = start ? live.Start : live.End;
            var acceptedPoint = start ? grip.Geometry.Start : grip.Geometry.End;
            Assert.Equal(lateral, -(acceptedPoint.X - priorPoint.X) * u.Y + (acceptedPoint.Y - priorPoint.Y) * u.X, 4);
            Assert.True(grip.Grip.AxisAfter.X * u.X + grip.Grip.AxisAfter.Y * u.Y > 0);
            AssertPoint(start ? original.End : original.Start, start ? grip.Geometry.End : grip.Geometry.Start);
            data = RoundTrip(grip.Metadata);
            live = grip.Geometry;
            Assert.Equal(originalData.SemanticIdentity, data.SemanticIdentity);
            Assert.Equal(originalData.ChildIdentity, data.ChildIdentity);
            Assert.Equal(originalData.RoofOwnerReference, data.RoofOwnerReference);
            Assert.Equal(originalData.AnchorGeneratedMemberKey, data.AnchorGeneratedMemberKey);
            Assert.Equal(originalData.Origin, data.Origin);
            Assert.NotNull(data.PhysicalReferenceSegment);
            Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(f.Source.PlanAxis.Start, f.Source.PlanAxis.End,
                data.RelativeSegment!, out var replayStart, out var replayEnd));
            AssertPoint(live.Start, replayStart);
            AssertPoint(live.End, replayEnd);
            var model = Append(f, [new(data, Segment(live), 80, 125)]);
            var body = Assert.Single(model.Members, member => member.AttachedManualIdentity is not null);
            var frameAfter = Frame(body);
            Assert.True(frameAfter.IsFaithful);
            AssertRoofFrame(f, body);
            var keys = model.Members.Select(member => member.PhysicalIdentity).ToArray();
            Assert.True(RoofOrdinaryPhysicalReconciliationRules.TryPlan(keys, [childKey], bodies, false, out var plan));
            Assert.Equal([childKey], plan!.RebuildKeys);
            Assert.Equal(["child:" + attempt], plan.RemoveBodyIdentities);
            bodies.RemoveAll(body => plan.RemoveBodyIdentities.Contains(body.BodyIdentity));
            bodies.Add(new("child:" + (attempt + 1), childKey));
            var finalKeys = bodies.Select(body => body.SemanticKey).ToArray();
            Assert.True(RoofOrdinaryPhysicalReconciliationRules.IsCanonical(keys, finalKeys));
            Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(finalKeys, keys));
        }
    }

    [Fact]
    public void FreeformDiagnostics_ReachBothHostAcceptancePaths()
    {
        var diag = Read("RoofGeneratedMemberManualEditDiag.cs");
        foreach (var field in new[] { "planYawDeltaDegrees=", "topFacePlaneErrorMm=", "minimumLength=", "clamped=" })
            Assert.Contains(field, diag);
        Assert.Contains("RoofOrdinaryFreeformGripRules.TryAccept", Read("RoofGeneratedMemberManualEditService.cs"));
        Assert.Contains("grip.Grip, \"ok\"", Read("RoofAttachedManualLifecycleService.cs"));
    }

    private static void AssertRoofFrame(Fixture f, RoofAutomaticRafterPhysicalMember body)
    {
        Assert.True(RoofOrdinaryRoofPlaneFrameRules.TryDescribe(f.Roof.Topology, body, 3000, out var frame));
        Assert.InRange(frame!.TopFacePlaneErrorMm, 0, 1e-4);
        Assert.InRange(frame.SectionOrthogonalityError, 0, 1e-6);
        Assert.InRange(frame.HeightNormalErrorMm, 0, 1e-4);
        var physicalPlanLength = Math.Sqrt(frame.PhysicalAxis.X * frame.PhysicalAxis.X + frame.PhysicalAxis.Y * frame.PhysicalAxis.Y);
        Assert.Equal((body.PlanAxis.End.X - body.PlanAxis.Start.X) / body.PlanAxis.Start.DistanceTo(body.PlanAxis.End),
            frame.PhysicalAxis.X / physicalPlanLength, 5);
        Assert.Equal((body.PlanAxis.End.Y - body.PlanAxis.Start.Y) / body.PlanAxis.Start.DistanceTo(body.PlanAxis.End),
            frame.PhysicalAxis.Y / physicalPlanLength, 5);
    }

    private static RoofAttachedManualGripAcceptance Accept(Fixture f, RoofAttachedManualTimberData data,
        RoofGeneratedMemberGeometry before, RoofGeneratedMemberGeometry native)
    {
        Assert.True(RoofAttachedManualGripRules.TryAccept(data, f.Source.PlanAxis.Start, f.Source.PlanAxis.End,
            before, native, out var accepted));
        return accepted!;
    }
    private static RoofAttachedManualTimberData Child(Fixture f, RoofGeneratedMemberGeometry axis)
    {
        var anchor = f.Source.PlanAxis;
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(anchor.Start, anchor.End, axis.Start, axis.End, out var relative));
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(anchor.Start, anchor.End, anchor.Start, anchor.End, out var reference));
        return new(5, "AB", Guid.NewGuid().ToString("N"), RoofTimberChildRole.AttachedManual, f.Source.MemberKey,
            relative, RoofAttachedManualOrigin.Copy, RoofAttachedManualIdentityRules.Create(), reference);
    }
    private static Fixture Solve()
    {
        var input = new RoofFootprintInput([new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)], true);
        var footprint = RoofFootprintValidator.Validate(input);
        var roof = Assert.IsType<HipRoofGeometry>(RoofGeometrySolver.Solve(new(footprint.Footprint!, new(30), RoofKind.Hip)).Geometry);
        var faces = RoofFaceRafterLayoutService.Create(roof.Topology, 600).Layout!;
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(roof, faces, 80, out var layout));
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild("AB", roof.Topology, faces, layout, 3000, 80, 125, new(), out var generated));
        var source = generated!.Members.First(member => member.StartBoundaryRole == RoofRafterBoundaryRole.Eave && member.EndBoundaryRole == RoofRafterBoundaryRole.Ridge);
        return new(roof, faces, generated, source);
    }
    private static RoofAutomaticRafterPhysicalModel Append(Fixture f, RoofAttachedManualPhysicalInput[] children)
    {
        Assert.True(RoofAttachedManualPhysicalBuilder.TryAppend(f.Roof.Topology, f.Faces, f.Generated, children,
            3000, new(), null, out var model, out var reason), reason);
        return model!;
    }
    private static RoofOrdinaryPhysicalPlanFrameRules.FrameCorrespondence Frame(RoofAutomaticRafterPhysicalMember member)
    {
        var prism = member.HorizontalCut?.SourcePrismVertices ?? member.StructuralCut?.SourcePrismVertices ??
            member.RidgeOverlapCut?.SourcePrismVertices ?? member.RidgeMeetCut?.SourcePrismVertices ??
            member.SolidVertices;
        Assert.True(RoofOrdinaryPhysicalPlanFrameRules.TryDescribe(member.PlanAxis, prism, member.WidthMm, out var frame, out var reason), reason);
        return frame!;
    }
    private static RoofAttachedManualTimberData RoundTrip(RoofAttachedManualTimberData data)
    {
        Assert.True(RoofAttachedManualTimberDataCodec.TryDecode(RoofAttachedManualTimberDataCodec.Encode(data), out var result));
        return result!;
    }
    private static RoofGeneratedMemberGeometry Drag(RoofGeneratedMemberGeometry before, bool start, double along, double lateral)
    {
        var dx = (before.End.X - before.Start.X) / before.LengthMm;
        var dy = (before.End.Y - before.Start.Y) / before.LengthMm;
        var point = start ? before.Start : before.End;
        var moved = Shift(point, dx * along - dy * lateral, dy * along + dx * lateral);
        return start ? before with { Start = moved } : before with { End = moved };
    }
    private static RoofPoint3D Shift(RoofPoint3D p, double x, double y, double z = 0) => new(p.X + x, p.Y + y, p.Z + z);
    private static RoofGeneratedMemberGeometry Translate(RoofGeneratedMemberGeometry g, double x, double y, double z = 0) => new(Shift(g.Start, x, y, z), Shift(g.End, x, y, z));
    private static RoofGeneratedMemberGeometry Geometry(RoofSegment3D axis) => new(axis.Start, axis.End);
    private static RoofSegment3D Segment(RoofGeneratedMemberGeometry axis) => new(axis.Start, axis.End);
    private static void AssertPoint(RoofPoint3D expected, RoofPoint3D actual) => Assert.True(expected.DistanceTo(actual) < 1e-5);
    private static void AssertAxis(RoofGeneratedMemberGeometry expected, RoofGeneratedMemberGeometry actual)
    {
        Assert.Equal((expected.End.X - expected.Start.X) / expected.LengthMm, (actual.End.X - actual.Start.X) / actual.LengthMm, 6);
        Assert.Equal((expected.End.Y - expected.Start.Y) / expected.LengthMm, (actual.End.Y - actual.Start.Y) / actual.LengthMm, 6);
    }
    private static string Read(string file) => RoofUxSourceContractText.Read("src", "AcKrovy.AutoCAD", "Infrastructure", file);
}
