using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Pitch adaptation: preserve plan stations; recompute BottomEdge for the new pitch.
/// HOST reference: 6000×10000 hip, rafter 100×125, WP 140×140 @700 mm, Int 160×220 @1800 mm.
/// </summary>
public sealed class RoofAutomaticPurlinPitchAdaptationRulesTests
{
    private const double StationToleranceMm = 1e-6d;
    private const double BottomToleranceMm = 1e-3d;

    [Theory]
    [InlineData(55d, 700d, 140d, 140d, 25d, 596.285)]
    [InlineData(45d, 700d, 140d, 140d, 25d, 357.417)]
    [InlineData(55d, 1800d, 160d, 220d, 25d, 2072.966)]
    [InlineData(45d, 1800d, 160d, 220d, 25d, 1367.417)]
    public void ClosedFormBottom_MatchesHostReference(
        double pitchDegrees,
        double planDistanceMm,
        double widthMm,
        double heightMm,
        double seatingPercent,
        double expectedBottomMm)
    {
        Assert.True(
            RoofAutomaticPurlinPitchAdaptationRules.TryComputeBottomLocalZMm(
                planDistanceMm,
                pitchDegrees,
                widthMm,
                heightMm,
                rafterHeightMm: 125d,
                new RoofAutomaticPurlinSeatingDepth(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    seatingPercent),
                out var bottom,
                out _));
        Assert.Equal(expectedBottomMm, bottom, 3);
    }

    [Fact]
    public void HostCase_55_To_45_To_55_BottomEdge_PreservesExactStations_NoDrift()
    {
        var at55 = Solve(55d);
        var at45 = Solve(45d);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            0d);

        // Seed as PlanDistance so stations are exact, then convert displayed BottomEdge
        // via adaptation after planning once at 55°.
        var distanceLayout = CreateLayout(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            wallValueMm: 700d,
            intValueMm: 1800d,
            seating);
        var input = CreateInput(datum, rafterHeightMm: 125d);
        var plan55 = RoofAutomaticPurlinPlanner.Create(
            at55.Geometry,
            at55.Provenance,
            distanceLayout,
            input);
        Assert.True(plan55.IsValid, plan55.Error.ToString());

        var bottomEdgeAt55 = ToBottomEdgeLayout(plan55.Plan!, datum, distanceLayout);
        Assert.Equal(
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            bottomEdgeAt55.WallPlatePlacement!.PlacementMode);
        Assert.Equal(596.285, bottomEdgeAt55.WallPlatePlacement.PlacementValueMm, 3);
        Assert.Equal(2072.966, bottomEdgeAt55.IntermediateItems[0].PlacementValueMm, 3);

        var to45 = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            at55.Geometry,
            at45.Geometry,
            at55.Provenance,
            bottomEdgeAt55,
            input);
        Assert.True(to45.IsValid, to45.Error.ToString());
        Assert.True(to45.LayoutChanged);
        Assert.Equal(
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            to45.AdaptedLayout!.WallPlatePlacement!.PlacementMode);
        Assert.Equal(357.417, to45.AdaptedLayout.WallPlatePlacement.PlacementValueMm, 3);
        Assert.Equal(1367.417, to45.AdaptedLayout.IntermediateItems[0].PlacementValueMm, 3);

        AssertStations(at45, to45.AdaptedLayout!, input, 700d, 1800d);

        var back55 = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            at45.Geometry,
            at55.Geometry,
            at55.Provenance,
            to45.AdaptedLayout,
            input);
        Assert.True(back55.IsValid, back55.Error.ToString());
        Assert.Equal(596.285, back55.AdaptedLayout!.WallPlatePlacement!.PlacementValueMm, 3);
        Assert.Equal(2072.966, back55.AdaptedLayout.IntermediateItems[0].PlacementValueMm, 3);
        AssertStations(at55, back55.AdaptedLayout, input, 700d, 1800d);

        // Second round-trip: no accumulated drift.
        var again45 = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            at55.Geometry,
            at45.Geometry,
            at55.Provenance,
            back55.AdaptedLayout,
            input);
        Assert.True(again45.IsValid);
        Assert.Equal(
            to45.AdaptedLayout.WallPlatePlacement.PlacementValueMm,
            again45.AdaptedLayout!.WallPlatePlacement!.PlacementValueMm,
            9);
        Assert.Equal(
            to45.AdaptedLayout.IntermediateItems[0].PlacementValueMm,
            again45.AdaptedLayout.IntermediateItems[0].PlacementValueMm,
            9);
        AssertStations(at45, again45.AdaptedLayout, input, 700d, 1800d);
    }

    [Fact]
    public void WallPlateBottom_BottomEdge_55_To_45_To_55_PersistsRelativeZero_NoDrift()
    {
        var at55 = Solve(55d);
        var at45 = Solve(45d);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            0d,
            0d);
        var distanceLayout = CreateLayout(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            wallValueMm: 700d,
            intValueMm: 1800d,
            seating);
        var input = CreateInput(datum, rafterHeightMm: 125d);
        var plan55 = RoofAutomaticPurlinPlanner.Create(
            at55.Geometry,
            at55.Provenance,
            distanceLayout,
            input);
        Assert.True(plan55.IsValid, plan55.Error.ToString());

        var bottomEdgeAt55 = ToWallPlateBottomRelativeBottomEdgeLayout(
            plan55.Plan!,
            distanceLayout);
        Assert.Equal(0d, bottomEdgeAt55.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(596.285, bottomEdgeAt55.WallPlateLowerEdgeHeightMm, 3);
        Assert.Equal(1476.681, bottomEdgeAt55.IntermediateItems[0].PlacementValueMm, 3);
        AssertStations(at55, bottomEdgeAt55, input, 700d, 1800d);

        var to45 = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            at55.Geometry,
            at45.Geometry,
            at55.Provenance,
            bottomEdgeAt55,
            input);
        Assert.True(to45.IsValid, to45.Error.ToString());
        Assert.True(to45.LayoutChanged);
        Assert.Equal(0d, to45.AdaptedLayout!.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(357.417, to45.AdaptedLayout.WallPlateLowerEdgeHeightMm, 3);
        Assert.Equal(1010d, to45.AdaptedLayout.IntermediateItems[0].PlacementValueMm, 3);
        AssertStations(at45, to45.AdaptedLayout, input, 700d, 1800d);

        var back55 = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            at45.Geometry,
            at55.Geometry,
            at55.Provenance,
            to45.AdaptedLayout,
            input);
        Assert.True(back55.IsValid, back55.Error.ToString());
        Assert.Equal(0d, back55.AdaptedLayout!.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(596.285, back55.AdaptedLayout.WallPlateLowerEdgeHeightMm, 3);
        Assert.Equal(1476.681, back55.AdaptedLayout.IntermediateItems[0].PlacementValueMm, 3);
        AssertStations(at55, back55.AdaptedLayout, input, 700d, 1800d);

        var again45 = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            at55.Geometry,
            at45.Geometry,
            at55.Provenance,
            back55.AdaptedLayout,
            input);
        Assert.True(again45.IsValid);
        Assert.Equal(0d, again45.AdaptedLayout!.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(
            to45.AdaptedLayout.WallPlateLowerEdgeHeightMm,
            again45.AdaptedLayout.WallPlateLowerEdgeHeightMm,
            9);
        Assert.Equal(
            to45.AdaptedLayout.IntermediateItems[0].PlacementValueMm,
            again45.AdaptedLayout.IntermediateItems[0].PlacementValueMm,
            9);
        AssertStations(at45, again45.AdaptedLayout, input, 700d, 1800d);
    }

    [Fact]
    public void WallPlateBottom_PlanDistance_Unchanged_AcrossPitch()
    {
        var at55 = Solve(55d);
        var at45 = Solve(45d);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var layout = CreateLayout(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            700d,
            1800d,
            seating);
        var input = CreateInput(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.WallPlateBottom,
                0d,
                0d),
            125d);

        var adapted = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            at55.Geometry,
            at45.Geometry,
            at55.Provenance,
            layout,
            input);
        Assert.True(adapted.IsValid, adapted.Error.ToString());
        Assert.False(adapted.LayoutChanged);
        Assert.Equal(700d, adapted.AdaptedLayout!.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(1800d, adapted.AdaptedLayout.IntermediateItems[0].PlacementValueMm, 9);
        AssertStations(at45, adapted.AdaptedLayout, input, 700d, 1800d);
    }

    [Fact]
    public void PrepareWallPlateBottomEdgeZero_ExpandsStashedAbsoluteForPlanning()
    {
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var product = CreateLayout(
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            wallValueMm: 0d,
            intValueMm: 1476.681d,
            seating) with
        {
            WallPlateLowerEdgeHeightMm = 596.285d,
        };

        var planning = RoofAutomaticPurlinPitchAdaptationRules
            .PrepareWallPlateBottomEdgeZeroForBootstrapPlanning(product);
        Assert.Equal(0d, product.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(596.285d, planning.WallPlatePlacement!.PlacementValueMm, 3);
        Assert.Equal(596.285d, planning.WallPlateLowerEdgeHeightMm, 3);
        Assert.Equal(1476.681d, planning.IntermediateItems[0].PlacementValueMm, 3);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(1000d)]
    public void ExplicitLocalZ_BottomEdge_PreservesStation_AcrossPitch(double localZMm)
    {
        var at55 = Solve(55d);
        var at45 = Solve(45d);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            localZMm);
        var distanceLayout = CreateLayout(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            700d,
            1800d,
            seating);
        var input = CreateInput(datum, 125d);
        var plan55 = RoofAutomaticPurlinPlanner.Create(
            at55.Geometry,
            at55.Provenance,
            distanceLayout,
            input);
        Assert.True(plan55.IsValid);
        var bottomEdge = ToBottomEdgeLayout(plan55.Plan!, datum, distanceLayout);

        var adapted = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            at55.Geometry,
            at45.Geometry,
            at55.Provenance,
            bottomEdge,
            input);
        Assert.True(adapted.IsValid, adapted.Error.ToString());
        Assert.Equal(357.417 - localZMm, adapted.AdaptedLayout!.WallPlatePlacement!.PlacementValueMm, 3);
        Assert.Equal(1367.417 - localZMm, adapted.AdaptedLayout.IntermediateItems[0].PlacementValueMm, 3);
        AssertStations(at45, adapted.AdaptedLayout, input, 700d, 1800d);
    }

    [Fact]
    public void SourceEavePlane_BottomEdge_UsesAbsoluteOffsets_AcrossPitch()
    {
        var at55 = Solve(55d);
        var at45 = Solve(45d);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.SourceEavePlane,
            0d,
            0d);
        var distanceLayout = CreateLayout(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            700d,
            1800d,
            seating);
        var input = CreateInput(datum, 125d);
        var plan55 = RoofAutomaticPurlinPlanner.Create(
            at55.Geometry,
            at55.Provenance,
            distanceLayout,
            input);
        Assert.True(plan55.IsValid);
        var bottomEdge = ToBottomEdgeLayout(plan55.Plan!, datum, distanceLayout);
        Assert.Equal(596.285, bottomEdge.WallPlatePlacement!.PlacementValueMm, 3);

        var adapted = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            at55.Geometry,
            at45.Geometry,
            at55.Provenance,
            bottomEdge,
            input);
        Assert.True(adapted.IsValid, adapted.Error.ToString());
        Assert.Equal(357.417, adapted.AdaptedLayout!.WallPlatePlacement!.PlacementValueMm, 3);
        Assert.Equal(1367.417, adapted.AdaptedLayout.IntermediateItems[0].PlacementValueMm, 3);
        AssertStations(at45, adapted.AdaptedLayout, input, 700d, 1800d);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(25d)]
    [InlineData(100d)]
    public void PlanDistanceMode_Unchanged_AcrossPitch(double seatingPercent)
    {
        var at55 = Solve(55d);
        var at45 = Solve(45d);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            seatingPercent);
        var layout = CreateLayout(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            700d,
            1800d,
            seating);
        var input = CreateInput(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                0d,
                0d),
            125d);

        var adapted = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            at55.Geometry,
            at45.Geometry,
            at55.Provenance,
            layout,
            input);
        Assert.True(adapted.IsValid, adapted.Error.ToString());
        Assert.False(adapted.LayoutChanged);
        Assert.Equal(700d, adapted.AdaptedLayout!.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(1800d, adapted.AdaptedLayout.IntermediateItems[0].PlacementValueMm, 9);
        AssertStations(at45, adapted.AdaptedLayout, input, 700d, 1800d);
    }

    [Fact]
    public void ExplicitLocalZ_1000_RecomputesOffset_PreservesStation()
    {
        var at55 = Solve(55d);
        var at45 = Solve(45d);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            1000d);
        var distanceLayout = CreateLayout(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            700d,
            1800d,
            seating);
        var input = CreateInput(datum, 125d);
        var plan55 = RoofAutomaticPurlinPlanner.Create(
            at55.Geometry,
            at55.Provenance,
            distanceLayout,
            input);
        Assert.True(plan55.IsValid);
        var bottomEdge = ToBottomEdgeLayout(plan55.Plan!, datum, distanceLayout);

        var adapted = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            at55.Geometry,
            at45.Geometry,
            at55.Provenance,
            bottomEdge,
            input);
        Assert.True(adapted.IsValid, adapted.Error.ToString());
        Assert.Equal(357.417 - 1000d, adapted.AdaptedLayout!.WallPlatePlacement!.PlacementValueMm, 3);
        Assert.Equal(1367.417 - 1000d, adapted.AdaptedLayout.IntermediateItems[0].PlacementValueMm, 3);
        AssertStations(at45, adapted.AdaptedLayout, input, 700d, 1800d);
    }

    [Fact]
    public void NegativeBottomEdgeOffset_SurvivesPitchChange()
    {
        var at55 = Solve(55d);
        var at45 = Solve(45d);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            1000d);
        // Place WP below the elevated datum but still on a valid station (700 mm).
        var distanceLayout = CreateLayout(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            700d,
            1800d,
            seating);
        var input = CreateInput(datum, 125d);
        var plan55 = RoofAutomaticPurlinPlanner.Create(
            at55.Geometry,
            at55.Provenance,
            distanceLayout,
            input);
        Assert.True(plan55.IsValid);
        var bottomEdge = ToBottomEdgeLayout(plan55.Plan!, datum, distanceLayout);
        Assert.True(bottomEdge.WallPlatePlacement!.PlacementValueMm < 0d);

        var adapted = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            at55.Geometry,
            at45.Geometry,
            at55.Provenance,
            bottomEdge,
            input);
        Assert.True(adapted.IsValid, adapted.Error.ToString());
        Assert.True(adapted.AdaptedLayout!.WallPlatePlacement!.PlacementValueMm < 0d);
        AssertStations(at45, adapted.AdaptedLayout, input, 700d, 1800d);
    }

    [Fact]
    public void SamePitch_IsNoOp()
    {
        var solved = Solve(45d);
        var layout = CreateLayout(
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            357.417,
            1367.417,
            new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                25d));
        var input = CreateInput(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                0d,
                0d),
            125d);
        var result = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            solved.Geometry,
            solved.Geometry,
            solved.Provenance,
            layout,
            input);
        Assert.True(result.IsValid);
        Assert.False(result.LayoutChanged);
        Assert.Same(layout, result.AdaptedLayout);
    }

    [Fact]
    public void StationOutsideNewRoof_FailsWithoutClamping()
    {
        var at55 = Solve(55d, shortSpanMm: 6000d);
        var shallowShort = Solve(10d, shortSpanMm: 1500d);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var distanceLayout = CreateLayout(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            700d,
            1800d,
            seating);
        var input = CreateInput(
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                0d,
                0d),
            125d);
        var plan55 = RoofAutomaticPurlinPlanner.Create(
            at55.Geometry,
            at55.Provenance,
            distanceLayout,
            input);
        Assert.True(plan55.IsValid);
        var bottomEdge = ToBottomEdgeLayout(plan55.Plan!, input.RelativeElevationDatum, distanceLayout);

        // 1800 mm station exceeds shallowShort maxExclusive (~750 mm).
        var fail = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            at55.Geometry,
            shallowShort.Geometry,
            at55.Provenance,
            bottomEdge,
            input);
        Assert.False(fail.IsValid);
        Assert.Equal(RoofAutomaticPurlinPlanError.ElevationOutsideRoof, fail.Error);
        Assert.Null(fail.AdaptedLayout);
    }

    [Fact]
    public void MultipleIntermediates_BothSlopes_PreserveStations()
    {
        var at55 = Solve(55d);
        var at45 = Solve(45d);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var layout = new RoofAutomaticPurlinLayout(
            false,
            [
                new(
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    true,
                    RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                    1200d,
                    SeatingDepth: seating,
                    WidthMm: 160d,
                    HeightMm: 220d),
                new(
                    "cccccccccccccccccccccccccccccccc",
                    true,
                    RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                    2400d,
                    SeatingDepth: seating,
                    WidthMm: 160d,
                    HeightMm: 220d),
            ])
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                700d,
                SeatingDepth: seating,
                WidthMm: 140d,
                HeightMm: 140d),
        };
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.SourceEavePlane,
            0d,
            0d);
        var input = CreateInput(datum, 125d);
        var plan55 = RoofAutomaticPurlinPlanner.Create(
            at55.Geometry,
            at55.Provenance,
            layout,
            input);
        Assert.True(plan55.IsValid);
        var bottomEdge = ToBottomEdgeLayout(plan55.Plan!, datum, layout);

        var adapted = RoofAutomaticPurlinPitchAdaptationRules.TryAdaptLayoutPreservingPlanStations(
            at55.Geometry,
            at45.Geometry,
            at55.Provenance,
            bottomEdge,
            input);
        Assert.True(adapted.IsValid, adapted.Error.ToString());
        AssertStations(at45, adapted.AdaptedLayout!, input, 700d, 1200d, 2400d);

        var leftRight = RoofAutomaticPurlinPlanner.Create(
            at45.Geometry,
            at45.Provenance,
            adapted.AdaptedLayout!,
            input);
        Assert.True(leftRight.IsValid);
        Assert.True(
            leftRight.Plan!.Items.Count(i =>
                i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate) >= 2);
    }

    private static void AssertStations(
        SolvedFixture solved,
        RoofAutomaticPurlinLayout layout,
        RoofAutomaticPurlinPlanningInput input,
        params double[] expectedStationsMm)
    {
        var plan = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            input);
        Assert.True(plan.IsValid, plan.Error.ToString());
        var pitch = solved.Geometry.Topology.PitchDegrees;
        var actual = new List<double>();
        foreach (var group in plan.Plan!.Items
                     .Where(i => i.GeneratorRole is
                         RoofAutomaticPurlinGeneratorRole.WallPlate or
                         RoofAutomaticPurlinGeneratorRole.Intermediate)
                     .GroupBy(i =>
                         i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate
                             ? RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId
                             : i.LayoutItemId)
                     .OrderBy(g =>
                         g.Key == RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId
                             ? string.Empty
                             : g.Key))
        {
            Assert.True(
                RoofAutomaticPurlinPitchAdaptationRules.TryResolvePlanDistanceFromEaveMm(
                    group.First().PhysicalPlacement!.RafterUpperSurfaceLocalZMm,
                    pitch,
                    out var station));
            actual.Add(station);
            Assert.All(
                group,
                item =>
                {
                    Assert.True(
                        RoofAutomaticPurlinPitchAdaptationRules.TryResolvePlanDistanceFromEaveMm(
                            item.PhysicalPlacement!.RafterUpperSurfaceLocalZMm,
                            pitch,
                            out var faceStation));
                    Assert.Equal(station, faceStation, 6);
                });
        }

        Assert.Equal(expectedStationsMm.Length, actual.Count);
        for (var i = 0; i < expectedStationsMm.Length; i++)
        {
            Assert.Equal(expectedStationsMm[i], actual[i], 6);
        }
    }

    private static RoofAutomaticPurlinLayout ToWallPlateBottomRelativeBottomEdgeLayout(
        RoofAutomaticPurlinPlan plan,
        RoofAutomaticPurlinLayout sourceLayout)
    {
        var wp = plan.Items.First(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate &&
            item.ElevationProfile is not null);
        var wpBottom = wp.ElevationProfile!.BottomLocalZMm;
        var intermediates = sourceLayout.IntermediateItems
            .Select(item =>
            {
                var planned = plan.Items.First(p =>
                    p.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate &&
                    string.Equals(p.LayoutItemId, item.LayoutItemId, StringComparison.Ordinal) &&
                    p.ElevationProfile is not null);
                return item with
                {
                    PlacementMode =
                        RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                    PlacementValueMm =
                        planned.ElevationProfile!.BottomLocalZMm - wpBottom,
                };
            })
            .ToArray();
        var wpPlacement = RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(sourceLayout) with
        {
            PlacementMode = RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            PlacementValueMm = 0d,
        };
        return sourceLayout with
        {
            IntermediateItems = intermediates,
            WallPlatePlacement = wpPlacement,
            WallPlateLowerEdgeHeightMm = wpBottom,
        };
    }

    private static RoofAutomaticPurlinLayout ToBottomEdgeLayout(
        RoofAutomaticPurlinPlan plan,
        RoofRelativeElevationDatum datum,
        RoofAutomaticPurlinLayout sourceLayout)
    {
        var wp = plan.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        var intermediates = sourceLayout.IntermediateItems
            .Select(item =>
            {
                var planned = plan.Items.First(i =>
                    i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate &&
                    string.Equals(i.LayoutItemId, item.LayoutItemId, StringComparison.Ordinal));
                return item with
                {
                    PlacementMode =
                        RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                    PlacementValueMm =
                        planned.ElevationProfile!.BottomLocalZMm - datum.ReferenceLocalZMm,
                };
            })
            .ToArray();
        var wpPlacement = RoofPurlinLayoutPersistenceRules.ResolveWallPlatePlacement(sourceLayout) with
        {
            PlacementMode = RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            PlacementValueMm =
                wp.ElevationProfile!.BottomLocalZMm - datum.ReferenceLocalZMm,
        };
        return sourceLayout with
        {
            IntermediateItems = intermediates,
            WallPlatePlacement = wpPlacement,
            WallPlateLowerEdgeHeightMm = wpPlacement.PlacementValueMm,
        };
    }

    private static RoofAutomaticPurlinLayout CreateLayout(
        RoofAutomaticPurlinPlacementMode mode,
        double wallValueMm,
        double intValueMm,
        RoofAutomaticPurlinSeatingDepth seating) =>
        new(false,
        [
            new(
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                true,
                mode,
                intValueMm,
                SeatingDepth: seating,
                WidthMm: 160d,
                HeightMm: 220d),
        ])
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                mode,
                wallValueMm,
                SeatingDepth: seating,
                WidthMm: 140d,
                HeightMm: 140d),
        };

    private static RoofAutomaticPurlinPlanningInput CreateInput(
        RoofRelativeElevationDatum datum,
        double rafterHeightMm) =>
        new(datum, 220d, rafterHeightMm)
        {
            PurlinWidthMm = 160d,
            WallPlatesEnabled = true,
            WallPlateWidthMm = 140d,
            WallPlateHeightMm = 140d,
        };

    private static SolvedFixture Solve(double pitchDegrees, double shortSpanMm = 6000d)
    {
        var longSpanMm = 10000d;
        var points = new RoofPoint2D[]
        {
            new(0, 0), new(longSpanMm, 0), new(longSpanMm, shortSpanMm), new(0, shortSpanMm),
        };
        var input = new RoofFootprintInput(points, true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid);
        var identity = RoofBoundaryIdentityRules.Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            normalized.EdgeProvenance.Count,
            RoofBoundaryIdentityRules.FormatWinding(normalized.Validation.SourceOrientation),
            Enumerable.Range(1, normalized.EdgeProvenance.Count).ToArray()).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        var geometry = Assert.IsType<HipRoofGeometry>(
            HipRoofGeometrySolver.Solve(new RoofDefinition(
                normalized.Validation.Footprint!,
                new RoofParameters(pitchDegrees),
                RoofKind.Hip)).Geometry);
        return new SolvedFixture(geometry, provenance);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
