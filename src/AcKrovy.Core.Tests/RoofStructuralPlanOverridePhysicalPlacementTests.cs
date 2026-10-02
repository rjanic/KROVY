using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Absolute Plan overrides must lift Plan XY onto the structural roof axis.
/// Ridge-side HOST NRE: CreateSolid prior sequence Append(UpperNodeMiterPlane!)
/// after Extract cleared miter but left envelope planes — covered here with mitered fixtures.
/// </summary>
public sealed class RoofStructuralPlanOverridePhysicalPlacementTests
{
    [Theory]
    [InlineData(30d, false)]
    [InlineData(45d, false)]
    [InlineData(30d, true)]
    public void AutomaticHip_PhysicalStaysElevatedWithSensibleVolume(double pitch, bool mirrorX)
    {
        var fixture = CreateFixture(pitch, valley: false, mirrorX: mirrorX, withMiter: true);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var canonical, out var reason), reason);
        var edit = RoofStructuralEditRules.Get(RoofStructuralEditState.Empty, canonical!.StructuralKey);
        var placed = RoofStructuralEditRules.Place(canonical, fixture.PlanSegment, edit);
        AssertPlacementHealthy(canonical, placed, expectedLengthRatio: 1d);
        Assert.False(edit.HasAbsolutePlan);
        AssertCreateSolidPriorSequenceSafe(placed);
    }

    [Theory]
    [InlineData(30d, false, 0.25, 1.0)] // eave-side
    [InlineData(30d, false, 0.0, 0.75)] // ridge-side
    [InlineData(30d, false, 0.20, 0.80)] // both ends
    [InlineData(30d, false, 0.0, 0.50)] // 50% ridge trim
    [InlineData(30d, false, 0.0, 0.90)] // near-ridge but not at ridge
    [InlineData(45d, false, 0.25, 1.0)] // eave 45
    [InlineData(45d, false, 0.0, 0.70)] // ridge 45
    [InlineData(30d, true, 0.25, 1.0)] // mirrored eave
    [InlineData(30d, true, 0.0, 0.70)] // mirrored ridge
    public void TrimmedHip_PlanOverride_KeepsElevatedPhysicalOnFold(
        double pitch, bool mirrorX, double tStart, double tEnd)
    {
        var fixture = CreateFixture(pitch, valley: false, mirrorX: mirrorX, withMiter: true);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var canonical, out var reason), reason);
        Assert.NotNull(canonical!.UpperNodeMiterPlane);
        Assert.NotEmpty(canonical.RoofEnvelopeClipPlanes);
        var plan = PlanXy(canonical.Geometry.UpperAxis);
        var trimmedPlan = new RoofSegment3D(Lerp(plan, tStart), Lerp(plan, tEnd));
        Assert.Equal(0d, trimmedPlan.Start.Z);
        Assert.Equal(0d, trimmedPlan.End.Z);
        Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(
            RoofStructuralEditState.Empty, canonical.StructuralKey, trimmedPlan, out var state));
        var edit = RoofStructuralEditRules.Get(state, canonical.StructuralKey);
        Assert.True(edit.HasAbsolutePlan);
        Assert.Equal(0d, edit.OffsetXmm);

        var placed = RoofStructuralEditRules.Place(canonical, fixture.PlanSegment, edit);
        Assert.Equal(canonical.StructuralKey, placed.StructuralKey);
        AssertPlacementHealthy(canonical, placed, expectedLengthRatio: Math.Abs(tEnd - tStart));
        AssertCreateSolidPriorSequenceSafe(placed);

        var expectedStart = Lerp(canonical.Geometry.UpperAxis, tStart);
        var expectedEnd = Lerp(canonical.Geometry.UpperAxis, tEnd);
        Assert.Equal(expectedStart.Z, placed.Geometry.UpperAxis.Start.Z, 5);
        Assert.Equal(expectedEnd.Z, placed.Geometry.UpperAxis.End.Z, 5);
        Assert.True(placed.Geometry.UpperAxis.Start.Z > 500d);
        Assert.True(placed.Geometry.UpperAxis.End.Z > 500d);

        var ridgeEdited = Math.Max(tStart, tEnd) < 1d - 1e-6;
        var eaveEdited = Math.Min(tStart, tEnd) > 1e-6;
        if (ridgeEdited)
        {
            Assert.Null(placed.UpperNodeMiterPlane);
            Assert.Empty(placed.RoofEnvelopeClipPlanes);
            // Longitudinal end cut at the edited ridge station — not the obsolete ridge join.
            Assert.Equal(Lerp(canonical.Geometry.UpperAxis, Math.Max(tStart, tEnd)).X,
                placed.RidgeClipPlane.Point.X, 4);
        }
        else
        {
            Assert.NotNull(placed.UpperNodeMiterPlane);
            Assert.Equal(canonical.UpperNodeMiterPlane, placed.UpperNodeMiterPlane);
        }

        if (!eaveEdited)
            Assert.Equal(canonical.LowerEndClipPlanes.Count, placed.LowerEndClipPlanes.Count);

        Assert.Equal(canonical.Geometry.WidthMm, placed.Geometry.WidthMm, 5);
        Assert.Equal(canonical.Geometry.PhysicalVerticalHeightMm,
            placed.Geometry.PhysicalVerticalHeightMm, 5);
    }

    [Fact]
    public void RidgeSideTrim_WithMiter_DoesNotLeaveNullMiterInCreateSolidPriorPath()
    {
        // HOST reproduction: ridge endpoint moved inward, eave unchanged, mitered Hip.
        var fixture = CreateFixture(30d, valley: false, withMiter: true);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var canonical, out var reason), reason);
        Assert.NotNull(canonical!.UpperNodeMiterPlane);
        Assert.True(canonical.RoofEnvelopeClipPlanes.Count > 0);

        var plan = PlanXy(canonical.Geometry.UpperAxis);
        // A unchanged (eave), B → B' inward on fold (ridge-side TRIM).
        var trimmed = new RoofSegment3D(plan.Start, Lerp(plan, 0.85));
        Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(
            RoofStructuralEditState.Empty, canonical.StructuralKey, trimmed, out var state));
        var placed = RoofStructuralEditRules.Place(canonical, fixture.PlanSegment,
            RoofStructuralEditRules.Get(state, canonical.StructuralKey));

        Assert.Null(placed.UpperNodeMiterPlane);
        Assert.Empty(placed.RoofEnvelopeClipPlanes);
        Assert.Equal(canonical.Geometry.UpperAxis.Start, placed.Geometry.UpperAxis.Start);
        Assert.Equal(Lerp(canonical.Geometry.UpperAxis, 0.85).Z, placed.Geometry.UpperAxis.End.Z, 5);

        // Legacy CreateSolid bug: Append(null!) then touch .Point → NRE. Prove the
        // buggy sequence would still be unsafe if envelope were retained with null miter.
        Assert.ThrowsAny<Exception>(() =>
        {
            var legacyPrior = canonical.EaveClipPlanes
                .Concat(canonical.LowerEndClipPlanes)
                .Append(placed.UpperNodeMiterPlane!)
                .Concat(canonical.RoofEnvelopeClipPlanes);
            foreach (var plane in legacyPrior)
                _ = plane.Point;
        });

        AssertCreateSolidPriorSequenceSafe(placed);
        AssertPlacementHealthy(canonical, placed, expectedLengthRatio: 0.85);
    }

    [Theory]
    [InlineData(0.25, 1.0)] // eave trim → extend back
    [InlineData(0.0, 0.70)] // ridge trim → extend back
    public void TrimThenExtendBackToCanonical_RestoresAutomaticEndTreatment(
        double tStart, double tEnd)
    {
        var fixture = CreateFixture(30d, valley: false, withMiter: true);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var canonical, out var reason), reason);
        var plan = PlanXy(canonical!.Geometry.UpperAxis);
        var trimmed = new RoofSegment3D(Lerp(plan, tStart), Lerp(plan, tEnd));
        Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(
            RoofStructuralEditState.Empty, canonical.StructuralKey, trimmed, plan, out var state));
        var shortened = RoofStructuralEditRules.Place(canonical, fixture.PlanSegment,
            RoofStructuralEditRules.Get(state, canonical.StructuralKey));
        Assert.NotEqual(canonical.Geometry.UpperAxis.LengthMm, shortened.Geometry.UpperAxis.LengthMm);

        Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(
            state, canonical.StructuralKey, plan, plan, out state));
        var restoredEdit = RoofStructuralEditRules.Get(state, canonical.StructuralKey);
        Assert.False(restoredEdit.HasAbsolutePlan);
        var restored = RoofStructuralEditRules.Place(canonical, fixture.PlanSegment, restoredEdit);
        Assert.Equal(canonical.StructuralKey, restored.StructuralKey);
        Assert.Equal(canonical.Geometry.UpperAxis, restored.Geometry.UpperAxis);
        Assert.Equal(canonical.UpperNodeMiterPlane, restored.UpperNodeMiterPlane);
        Assert.Equal(canonical.RidgeClipPlane, restored.RidgeClipPlane);
        Assert.Equal(canonical.RoofEnvelopeClipPlanes.Count, restored.RoofEnvelopeClipPlanes.Count);
        Assert.Equal(canonical.LowerEndClipPlanes.Count, restored.LowerEndClipPlanes.Count);
        AssertPlacementHealthy(canonical, restored, expectedLengthRatio: 1d);
        AssertCreateSolidPriorSequenceSafe(restored);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReversedPlanEndpointOrder_StillClassifiesRidgeSemantically(bool mirrorX)
    {
        var fixture = CreateFixture(30d, valley: false, mirrorX: mirrorX, withMiter: true);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var canonical, out var reason), reason);
        var plan = PlanXy(canonical!.Geometry.UpperAxis);
        // Store Plan with ridge→eave order; shorten the ridge semantic end (plan.End here
        // after reverse is eave — move plan.Start which is ridge in reversed storage).
        var reversedShort = new RoofSegment3D(Lerp(plan, 0.70), plan.Start);
        Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(
            RoofStructuralEditState.Empty, canonical.StructuralKey, reversedShort, out var state));
        var placed = RoofStructuralEditRules.Place(canonical, fixture.PlanSegment,
            RoofStructuralEditRules.Get(state, canonical.StructuralKey));
        Assert.Null(placed.UpperNodeMiterPlane);
        Assert.Empty(placed.RoofEnvelopeClipPlanes);
        AssertPlacementHealthy(canonical, placed, expectedLengthRatio: 0.70);
        AssertCreateSolidPriorSequenceSafe(placed);
    }

    [Theory]
    [InlineData(0.25, 1.0)]
    [InlineData(0.0, 0.75)]
    public void Valley_SemanticEndTrims_UseSameMapper(double tStart, double tEnd)
    {
        var fixture = CreateFixture(35d, valley: true, withMiter: false);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var canonical, out var reason), reason);
        var plan = PlanXy(canonical!.Geometry.UpperAxis);
        var trimmed = new RoofSegment3D(Lerp(plan, tStart), Lerp(plan, tEnd));
        Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(
            RoofStructuralEditState.Empty, canonical.StructuralKey, trimmed, out var state));
        var edit = RoofStructuralEditRules.Get(state, canonical.StructuralKey);
        var placed = RoofStructuralEditRules.Place(canonical, fixture.PlanSegment, edit);
        Assert.Equal(RoofStructuralRole.Valley, placed.Geometry.Role);
        Assert.Equal(canonical.StructuralKey, placed.StructuralKey);
        AssertPlacementHealthy(canonical, placed, expectedLengthRatio: Math.Abs(tEnd - tStart));
        AssertCreateSolidPriorSequenceSafe(placed);
        if (Math.Max(tStart, tEnd) < 1d - 1e-6)
            Assert.Null(placed.UpperNodeMiterPlane);
    }

    [Fact]
    public void RidgeSideStretchInward_UsesSameAbsolutePlanPathAsTrim()
    {
        // STRETCH/GRIP persist the same absolute Plan endpoints — one Place path.
        var fixture = CreateFixture(30d, valley: false, withMiter: true);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var canonical, out var reason), reason);
        var plan = PlanXy(canonical!.Geometry.UpperAxis);
        var gripped = new RoofSegment3D(plan.Start, Lerp(plan, 0.80));
        Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(
            RoofStructuralEditState.Empty, canonical.StructuralKey, gripped, out var state));
        Assert.Equal(RoofStructuralNativeAction.AcceptPlan,
            RoofStructuralEditRules.Classify("STRETCH", false, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.AcceptPlan,
            RoofStructuralEditRules.Classify("GRIP_STRETCH", false, RoofEditState.Unlocked));
        var placed = RoofStructuralEditRules.Place(canonical, fixture.PlanSegment,
            RoofStructuralEditRules.Get(state, canonical.StructuralKey));
        Assert.Null(placed.UpperNodeMiterPlane);
        Assert.Empty(placed.RoofEnvelopeClipPlanes);
        AssertPlacementHealthy(canonical, placed, expectedLengthRatio: 0.80);
        AssertCreateSolidPriorSequenceSafe(placed);
    }

    [Fact]
    public void AbsolutePlanEqualToAutomatic_NormalizesToAutomaticAndKeepsCanonicalClips()
    {
        var fixture = CreateFixture(35d, valley: false, withMiter: true);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var canonical, out var reason), reason);
        var plan = PlanXy(canonical!.Geometry.UpperAxis);
        Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(
            RoofStructuralEditState.Empty, canonical.StructuralKey, plan, plan, out var state));
        var edit = RoofStructuralEditRules.Get(state, canonical.StructuralKey);
        Assert.False(edit.HasAbsolutePlan);
        Assert.Equal(RoofStructuralPlanEditClass.Automatic,
            RoofStructuralEditRules.ClassifyMemberEdit(plan, edit));
        var placed = RoofStructuralEditRules.Place(canonical, fixture.PlanSegment, edit);
        Assert.Equal(canonical.LowerEndClipPlanes.Count, placed.LowerEndClipPlanes.Count);
        Assert.Equal(canonical.RidgeClipPlane, placed.RidgeClipPlane);
        Assert.Equal(canonical.UpperNodeMiterPlane, placed.UpperNodeMiterPlane);
        Assert.Equal(canonical.Geometry.UpperAxis, placed.Geometry.UpperAxis);
        AssertCreateSolidPriorSequenceSafe(placed);
    }

    [Theory]
    [InlineData(15d)]
    [InlineData(20d)]
    [InlineData(25d)]
    [InlineData(30d)]
    [InlineData(35d)]
    [InlineData(40d)]
    [InlineData(45d)]
    [InlineData(50d)]
    [InlineData(55d)]
    [InlineData(60d)]
    public void PitchMatrix_HipOnFoldEaveAndRidgeTrim_StayElevated(double pitch)
    {
        var fixture = CreateFixture(pitch, valley: false, withMiter: true);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var canonical, out var reason), reason);
        var plan = PlanXy(canonical!.Geometry.UpperAxis);
        foreach (var (t0, t1) in new[] { (0.10, 1.0), (0.25, 1.0), (0.50, 1.0), (0.0, 0.90), (0.0, 0.75), (0.0, 0.50), (0.20, 0.80) })
        {
            var trimmed = new RoofSegment3D(Lerp(plan, t0), Lerp(plan, t1));
            Assert.Equal(RoofStructuralPlanEditClass.OnFoldSubsegment,
                RoofStructuralEditRules.ClassifyPlanGeometry(plan, trimmed));
            Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(
                RoofStructuralEditState.Empty, canonical.StructuralKey, trimmed, plan, out var state));
            var placed = RoofStructuralEditRules.Place(canonical, fixture.PlanSegment,
                RoofStructuralEditRules.Get(state, canonical.StructuralKey));
            AssertPlacementHealthy(canonical, placed, Math.Abs(t1 - t0));
            AssertCreateSolidPriorSequenceSafe(placed);
        }
    }

    [Fact]
    public void MoveThenTrimAbsolutePlan_PlacesOffsetShortenedTimber()
    {
        var fixture = CreateFixture(30d, valley: false, withMiter: true);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var canonical, out var reason), reason);
        var plan = PlanXy(canonical!.Geometry.UpperAxis);
        const double dx = 250d;
        const double dy = -180d;
        var movedTrimmed = new RoofSegment3D(
            new(plan.Start.X + dx, plan.Start.Y + dy, 0),
            new(Lerp(plan, 0.70).X + dx, Lerp(plan, 0.70).Y + dy, 0));
        Assert.Equal(RoofStructuralPlanEditClass.OffsetRigid,
            RoofStructuralEditRules.ClassifyPlanGeometry(plan, movedTrimmed));
        Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(
            RoofStructuralEditState.Empty, canonical.StructuralKey, movedTrimmed, plan, out var state));
        var placed = RoofStructuralEditRules.Place(canonical, fixture.PlanSegment,
            RoofStructuralEditRules.Get(state, canonical.StructuralKey));
        Assert.Equal(canonical.StructuralKey, placed.StructuralKey);
        Assert.InRange(placed.Geometry.UpperAxis.Start.X,
            Lerp(canonical.Geometry.UpperAxis, 0).X + dx - 1d,
            Lerp(canonical.Geometry.UpperAxis, 0).X + dx + 1d);
        Assert.InRange(placed.Geometry.UpperAxis.LengthMm,
            canonical.Geometry.UpperAxis.LengthMm * 0.70 - 2d,
            canonical.Geometry.UpperAxis.LengthMm * 0.70 + 2d);
        Assert.True(placed.Geometry.UpperAxis.Start.Z > 500d);
        Assert.Null(placed.UpperNodeMiterPlane);
        AssertCreateSolidPriorSequenceSafe(placed);
    }

    [Theory]
    [InlineData(30d, 0.0, 0.75)]
    [InlineData(45d, 0.25, 1.0)]
    [InlineData(30d, 0.20, 0.85)]
    public void ValleyPitchParity_OnFoldTrims(double pitch, double tStart, double tEnd)
    {
        var fixture = CreateFixture(pitch, valley: true, withMiter: false);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var canonical, out var reason), reason);
        var plan = PlanXy(canonical!.Geometry.UpperAxis);
        var trimmed = new RoofSegment3D(Lerp(plan, tStart), Lerp(plan, tEnd));
        Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(
            RoofStructuralEditState.Empty, canonical.StructuralKey, trimmed, plan, out var state));
        var placed = RoofStructuralEditRules.Place(canonical, fixture.PlanSegment,
            RoofStructuralEditRules.Get(state, canonical.StructuralKey));
        Assert.Equal(RoofStructuralRole.Valley, placed.Geometry.Role);
        AssertPlacementHealthy(canonical, placed, Math.Abs(tEnd - tStart));
        AssertCreateSolidPriorSequenceSafe(placed);
    }

    [Fact]
    public void OffFoldStretch_IsRejectedWhenCanonicalFoldProvided()
    {
        var fixture = CreateFixture(30d, valley: false, withMiter: true);
        Assert.True(RoofStructuralRafterPolyhedronService.TryBuild(
            fixture.Request, out var canonical, out var reason), reason);
        var plan = PlanXy(canonical!.Geometry.UpperAxis);
        var sideways = new RoofSegment3D(plan.Start,
            new(plan.End.X + 500, plan.End.Y - 800, 0));
        Assert.Equal(RoofStructuralPlanEditClass.ArbitraryPlanLine,
            RoofStructuralEditRules.ClassifyPlanGeometry(plan, sideways));
        Assert.False(RoofStructuralEditRules.TryAcceptPlanGeometry(
            RoofStructuralEditState.Empty, canonical.StructuralKey, sideways, plan, out _));
    }

    private static void AssertPlacementHealthy(
        RoofStructuralRafterPolyhedron canonical,
        RoofStructuralRafterPolyhedron placed,
        double expectedLengthRatio)
    {
        Assert.Equal(canonical.StructuralKey, placed.StructuralKey);
        Assert.True(placed.Geometry.UpperAxis.LengthMm > 100d);
        Assert.InRange(placed.Geometry.UpperAxis.LengthMm,
            canonical.Geometry.UpperAxis.LengthMm * expectedLengthRatio - 2d,
            canonical.Geometry.UpperAxis.LengthMm * expectedLengthRatio + 2d);
        var clipped = ClipAuthoritative(placed);
        Assert.True(clipped.Count >= 6);
        var minZ = clipped.Min(point => point.Z);
        var maxZ = clipped.Max(point => point.Z);
        Assert.True(maxZ - minZ > 40d, $"degenerate height span {maxZ - minZ}");
        Assert.True(clipped.Average(point => point.Z) > 500d,
            $"Physical collapsed near Z=0 (avg={clipped.Average(point => point.Z)})");
        var proxy = placed.Geometry.UpperAxis.LengthMm *
                    placed.Geometry.WidthMm *
                    placed.Geometry.PhysicalVerticalHeightMm;
        var canonicalProxy = canonical.Geometry.UpperAxis.LengthMm *
                             canonical.Geometry.WidthMm *
                             canonical.Geometry.PhysicalVerticalHeightMm;
        Assert.InRange(proxy,
            canonicalProxy * expectedLengthRatio * 0.45,
            canonicalProxy * expectedLengthRatio * 1.35);
        Assert.True(proxy > canonicalProxy * 0.05,
            $"tiny remnant proxy volume {proxy} vs canonical {canonicalProxy}");
    }

    /// <summary>
    /// Mirrors CreateSolid envelope prior construction — must never dereference a null miter.
    /// </summary>
    private static void AssertCreateSolidPriorSequenceSafe(RoofStructuralRafterPolyhedron model)
    {
        for (var index = 0; index < model.RoofEnvelopeClipPlanes.Count; index++)
        {
            IEnumerable<RoofStructuralRafterClipPlane> prior =
                model.EaveClipPlanes.Concat(model.LowerEndClipPlanes);
            if (model.UpperNodeMiterPlane is { } miter)
                prior = prior.Append(miter);
            prior = prior.Concat(model.RoofEnvelopeClipPlanes.Take(index));
            foreach (var plane in prior)
            {
                Assert.True(double.IsFinite(plane.Point.X));
                Assert.True(double.IsFinite(plane.RetainedNormal.X));
            }
        }

        if (model.UpperNodeMiterPlane is null)
            Assert.True(double.IsFinite(model.RidgeClipPlane.Point.X));
    }

    private static IReadOnlyList<RoofPoint3D> ClipAuthoritative(
        RoofStructuralRafterPolyhedron model)
    {
        IEnumerable<RoofStructuralRafterClipPlane> planes =
            model.EaveClipPlanes.Concat(model.LowerEndClipPlanes);
        if (model.UpperNodeMiterPlane is { } miter)
            planes = planes.Append(miter);
        planes = planes.Concat(model.RoofEnvelopeClipPlanes).Append(model.RidgeClipPlane);
        var clipPlanes = planes
            .Select(plane => new RoofConvexPrismPlaneClipper.Plane(
                plane.Point, plane.RetainedNormal))
            .ToArray();
        var points = new List<RoofPoint3D>();
        foreach (var half in model.ConvexHalves)
        {
            Assert.True(RoofConvexPrismPlaneClipper.TryClip(
                half.SourcePrismVertices, clipPlanes, out var clipped, allowNoOpPlanes: true),
                "authoritative structural clip failed");
            points.AddRange(clipped!.Body);
        }

        return points;
    }

    private static (RoofTopology Topology, int EdgeIndex,
        RoofStructuralRafterPolyhedronRequest Request, RoofSegment3D PlanSegment)
        CreateFixture(double pitch, bool valley, bool mirrorX = false, bool withMiter = false)
    {
        RoofPoint2D[] points = valley
            ? [new(0, 0), new(8000, 0), new(8000, 3000),
                new(3000, 3000), new(3000, 8000), new(0, 8000)]
            : [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)];
        if (mirrorX)
            points = points.Select(point => new RoofPoint2D(-point.X, point.Y)).ToArray();
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(points, true));
        Assert.True(footprint.IsValid);
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!, new RoofParameters(pitch), RoofKind.Hip));
        Assert.True(solved.IsValid);
        var geometry = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var topology = geometry.Topology;
        var layoutResult = RoofFaceRafterLayoutService.Create(topology, 500d);
        Assert.True(layoutResult.IsValid);
        Assert.True(RoofFaceRafterMaterializationAdapter.TryCreateMaterializationLayout(
            geometry, layoutResult.Layout!, 80d, out var generated));
        var sources = topology.Edges.Select((edge, index) => (edge, index))
            .Where(item => item.edge.Kind is RoofTopologyEdgeKind.Hip or RoofTopologyEdgeKind.Valley)
            .Select(item => new RoofStructuralRafterTrimSource(item.index,
                item.edge.Kind == RoofTopologyEdgeKind.Hip
                    ? RoofRafterBoundaryRole.Hip : RoofRafterBoundaryRole.Valley,
                topology.Segment(item.edge), 120d)).ToArray();
        Assert.True(RoofAutomaticRafterPhysicalBuilder.TryBuild(
            "roof", topology, layoutResult.Layout!, generated, 3000d, 80d, 160d,
            new RoofAutomaticRafterPhysicalSettings(LowerEndCutMode.Vertical), null, sources,
            out var ordinary, out var ordinaryReason), ordinaryReason);
        var wanted = valley ? RoofTopologyEdgeKind.Valley : RoofTopologyEdgeKind.Hip;
        var hips = topology.Edges.Select((edge, index) => (edge, index))
            .Where(item => item.edge.Kind == RoofTopologyEdgeKind.Hip)
            .Select(item =>
            {
                var ids = item.edge.FaceIndices.Select(faceIndex =>
                    topology.Faces[faceIndex].SourceEdgeIndex + 1)
                    .OrderBy(id => id).ToArray();
                return new ResolvedRoofStructuralEdge(
                    new RoofStructuralLogicalKey(RoofStructuralRole.Hip, ids[0], ids[1]),
                    item.index, topology.Segment(item.edge),
                    IsPhysicalFoldTimberEligible: true);
            }).ToArray();
        RoofStructuralRafterClipPlane? miter = null;
        IReadOnlyDictionary<int, RoofStructuralRafterClipPlane> miters =
            new Dictionary<int, RoofStructuralRafterClipPlane>();
        if (withMiter && !valley)
        {
            Assert.True(RoofStructuralUpperNodeMiterResolver.TryResolve(
                topology, hips, out var resolvedMiters, out var miterReason), miterReason);
            miters = resolvedMiters;
        }

        var selected = topology.Edges.Select((edge, index) => (edge, index))
            .First(item => item.edge.Kind == wanted &&
                ordinary!.Members.Any(member =>
                    member.StructuralCut?.TopologyEdgeIndex == item.index) &&
                (!withMiter || valley || miters.ContainsKey(item.index)));
        if (withMiter && !valley)
            miter = miters[selected.index];
        var role = valley ? RoofStructuralRole.Valley : RoofStructuralRole.Hip;
        var faceIds = selected.edge.FaceIndices
            .Select(index => topology.Faces[index].SourceEdgeIndex + 1)
            .OrderBy(index => index).ToArray();
        var key = new RoofStructuralLogicalKey(role, faceIds[0], faceIds[1]);
        var resolved = new ResolvedRoofStructuralEdge(
            key, selected.index, topology.Segment(selected.edge),
            IsPhysicalFoldTimberEligible: true);
        var request = new RoofStructuralRafterPolyhedronRequest(topology, resolved,
            3000d, 120d, RoofStructuralHeightMode.Automatic, null, ordinary!.Members,
            UpperNodeMiterPlane: miter,
            LowerEndCutMode: LowerEndCutMode.Vertical);
        var planSegment = PlanXy(topology.Segment(selected.edge));
        return (topology, selected.index, request, planSegment);
    }

    private static RoofSegment3D PlanXy(RoofSegment3D segment) =>
        new(new(segment.Start.X, segment.Start.Y, 0), new(segment.End.X, segment.End.Y, 0));

    private static RoofPoint3D Lerp(RoofSegment3D segment, double t) =>
        new(
            segment.Start.X + (segment.End.X - segment.Start.X) * t,
            segment.Start.Y + (segment.End.Y - segment.Start.Y) * t,
            segment.Start.Z + (segment.End.Z - segment.Start.Z) * t);
}
