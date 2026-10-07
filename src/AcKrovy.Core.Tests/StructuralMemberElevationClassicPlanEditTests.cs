using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Classic plan edits (LENGTHEN / GRIP / STRETCH) must preserve Independent slope
/// magnitude like AUTO, and lower/upper are physical Z roles (not immutable Start/End).
/// </summary>
public sealed class StructuralMemberElevationClassicPlanEditTests
{
    private const double Tol = 1e-6;

    [Theory]
    [InlineData(500d)]
    [InlineData(-200d)]
    public void Independent_Lengthen_Preserves50DegreeSlope_AndAnchorZ(double deltaAlongMm)
    {
        const double slope = 50d;
        var plan = Plan(4000d);
        var planLen = plan.Start.DistanceTo(plan.End);
        var elevation = ElevationForSlope(planLen, slope);
        var endMoved = LengthenEnd(plan, deltaAlongMm);
        var newLen = endMoved.Start.DistanceTo(endMoved.End);

        Assert.True(StructuralMemberElevationRules.TryAdaptForClassicPlanEdit(
            elevation, plan, endMoved, out var adapted) && adapted is not null);

        Assert.Equal(elevation.AxisStartElevationMm, adapted!.AxisStartElevationMm, Tol);
        var slopeAfter = StructuralMemberElevationRules.AbsolutePitchDegrees(
            StructuralMemberElevationRules.DeriveSlopeDegrees(
                adapted.AxisStartElevationMm, adapted.AxisEndElevationMm, newLen));
        Assert.Equal(slope, slopeAfter, 0.05);
        var expectedEndZ = StructuralMemberElevationRules.DeriveEndFromStartAndSlope(
            elevation.AxisStartElevationMm, newLen, slope);
        Assert.Equal(expectedEndZ, adapted.AxisEndElevationMm, 0.05);
        Assert.Equal(0d, endMoved.Start.Z);
        Assert.Equal(0d, endMoved.End.Z);
    }

    [Fact]
    public void AutoAndIndependent_Lengthen_ShareSlopePreservationSemantics()
    {
        // AUTO: no Elevation XRecord → PitchDegrees from build state is preserved by TryBuild.
        // Independent with Elevation: classic adapt preserves the same absolute pitch.
        const double slope = 50d;
        var seed = Seed();
        var plan = seed.AcceptedPlanAxis;
        var planLen = plan.Start.DistanceTo(plan.End);
        var elevation = ElevationForSlope(planLen, slope);
        Assert.True(StructuralMemberElevationRules.TryPatchBuildStateForElevation(
            seed, plan, elevation, out var patched) && patched is not null);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            patched, plan, out _, out var accepted, out var reason,
            useIndependentHorizontalFrame: true), reason);

        var lengthened = LengthenEnd(plan, 500d);
        Assert.True(RoofOrdinaryLengthenRules.IsEndpointLengthEdit(plan, lengthened, out reason), reason);

        // Independent elevation path
        Assert.True(StructuralMemberElevationRules.TryAdaptForClassicPlanEdit(
            elevation, plan, lengthened, out var adapted) && adapted is not null);
        Assert.True(StructuralMemberElevationRules.TryPatchBuildStateForElevation(
            accepted!, lengthened, adapted!, out var indepPatched) && indepPatched is not null);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            indepPatched, lengthened, out _, out var indepAccepted, out reason,
            useIndependentHorizontalFrame: true), reason);

        // AUTO-like path (PitchDegrees preserved, no Elevation adapt)
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            accepted!, lengthened, out _, out var autoAccepted, out reason,
            useIndependentHorizontalFrame: true), reason);

        Assert.Equal(slope, indepAccepted!.PitchDegrees, 0.05);
        Assert.Equal(slope, autoAccepted!.PitchDegrees, 0.05);
        Assert.Equal(
            StructuralMemberElevationRules.PitchDegreesFromLongitudinalAxis(
                indepAccepted.SectionFrame!.LongitudinalAxis),
            StructuralMemberElevationRules.PitchDegreesFromLongitudinalAxis(
                autoAccepted.SectionFrame!.LongitudinalAxis),
            0.05);
        Assert.True(StructuralMemberElevationRules.ElevationPhysicalPitchesAgree(
            adapted, lengthened, indepAccepted));
    }

    [Fact]
    public void LowerUpperCrossing_AcceptsAndNormalizesRoles()
    {
        // A=3000, B=6000 → edit A=7000 → lower=B, upper=A, fall reverses, slope positive.
        const double planLen = 3000d;
        var before = StructuralMemberElevationRules.CreateSloped(3000d, 6000d);
        Assert.True(StructuralMemberElevationRules.IsStartTheEaveEnd(
            before.AxisStartElevationMm, before.AxisEndElevationMm));

        var after = StructuralMemberElevationRules.CreateSloped(7000d, 6000d);
        Assert.False(StructuralMemberElevationRules.IsStartTheEaveEnd(
            after.AxisStartElevationMm, after.AxisEndElevationMm));
        Assert.Equal(6000d, Math.Min(after.AxisStartElevationMm, after.AxisEndElevationMm), Tol);
        Assert.Equal(7000d, Math.Max(after.AxisStartElevationMm, after.AxisEndElevationMm), Tol);

        var abs = StructuralMemberElevationRules.AbsolutePitchDegrees(
            StructuralMemberElevationRules.DeriveSlopeDegrees(
                after.AxisStartElevationMm, after.AxisEndElevationMm, planLen));
        Assert.True(abs > 0d);
        Assert.Equal(
            StructuralMemberElevationRules.FormatSlopeDegrees(abs),
            StructuralMemberElevationRules.FormatSlopeDegrees(
                StructuralMemberElevationRules.DeriveSlopeDegrees(
                    after.AxisStartElevationMm, after.AxisEndElevationMm, planLen)));

        Assert.True(StructuralMemberElevationRules.ResolveIsSlopeDirectionReversedForDownhill(
            before.AxisStartElevationMm, before.AxisEndElevationMm));
        Assert.False(StructuralMemberElevationRules.ResolveIsSlopeDirectionReversedForDownhill(
            after.AxisStartElevationMm, after.AxisEndElevationMm));
    }

    [Fact]
    public void LowerUpperCrossing_ReverseBack_NormalizesAgain()
    {
        var crossed = StructuralMemberElevationRules.CreateSloped(7000d, 6000d);
        var back = StructuralMemberElevationRules.CreateSloped(3000d, 6000d);
        Assert.True(StructuralMemberElevationRules.IsStartTheEaveEnd(
            back.AxisStartElevationMm, back.AxisEndElevationMm));
        Assert.True(StructuralMemberElevationRules.ResolveIsSlopeDirectionReversedForDownhill(
            back.AxisStartElevationMm, back.AxisEndElevationMm));
        Assert.False(StructuralMemberElevationRules.ResolveIsSlopeDirectionReversedForDownhill(
            crossed.AxisStartElevationMm, crossed.AxisEndElevationMm));
    }

    [Fact]
    public void StartEndLineReversed_LowerUpperStillByPhysicalZ()
    {
        // Line Start high, End low — UI lower/upper still by Min/Max Z.
        var state = StructuralMemberElevationRules.CreateSloped(6000d, 3000d);
        Assert.False(StructuralMemberElevationRules.IsStartTheEaveEnd(
            state.AxisStartElevationMm, state.AxisEndElevationMm));
        Assert.Equal(3000d, Math.Min(state.AxisStartElevationMm, state.AxisEndElevationMm), Tol);
        Assert.Equal(6000d, Math.Max(state.AxisStartElevationMm, state.AxisEndElevationMm), Tol);
        Assert.False(StructuralMemberElevationRules.ResolveIsSlopeDirectionReversedForDownhill(
            state.AxisStartElevationMm, state.AxisEndElevationMm));
    }

    [Fact]
    public void MirroredIndependent_SameClassicLengthenSemantics()
    {
        // Mirrored fall: Start higher than End (negative Start→End slope).
        const double slope = 50d;
        var plan = Plan(4000d);
        var planLen = plan.Start.DistanceTo(plan.End);
        var elevation = ElevationForSlope(planLen, -slope);
        Assert.False(StructuralMemberElevationRules.IsStartTheEaveEnd(
            elevation.AxisStartElevationMm, elevation.AxisEndElevationMm));

        var lengthened = LengthenEnd(plan, 500d);
        var newLen = lengthened.Start.DistanceTo(lengthened.End);
        Assert.True(StructuralMemberElevationRules.TryAdaptForClassicPlanEdit(
            elevation, plan, lengthened, out var adapted) && adapted is not null);

        Assert.Equal(elevation.AxisStartElevationMm, adapted!.AxisStartElevationMm, Tol);
        var slopeAfter = StructuralMemberElevationRules.AbsolutePitchDegrees(
            StructuralMemberElevationRules.DeriveSlopeDegrees(
                adapted.AxisStartElevationMm, adapted.AxisEndElevationMm, newLen));
        Assert.Equal(slope, slopeAfter, 0.05);
        Assert.False(StructuralMemberElevationRules.IsStartTheEaveEnd(
            adapted.AxisStartElevationMm, adapted.AxisEndElevationMm));
    }

    [Fact]
    public void Horizontal_ZEqual_SlopeZero_NoStaleFall()
    {
        var plan = Plan(4000d);
        var elevation = StructuralMemberElevationRules.CreateUniform(2500d);
        var lengthened = LengthenEnd(plan, 500d);
        Assert.True(StructuralMemberElevationRules.TryAdaptForClassicPlanEdit(
            elevation, plan, lengthened, out var adapted) && adapted is not null);

        Assert.Equal(2500d, adapted!.AxisStartElevationMm, Tol);
        Assert.Equal(2500d, adapted.AxisEndElevationMm, Tol);
        Assert.Equal(StructuralMemberElevationBehavior.UniformElevation, adapted.Behavior);
        Assert.True(StructuralMemberElevationRules.IsHorizontal(
            adapted.AxisStartElevationMm, adapted.AxisEndElevationMm));
        Assert.False(StructuralMemberElevationRules.ResolveIsSlopeDirectionReversedForDownhill(
            adapted.AxisStartElevationMm, adapted.AxisEndElevationMm));
        Assert.Equal(0d, StructuralMemberElevationRules.DeriveSlopeDegrees(
            adapted.AxisStartElevationMm, adapted.AxisEndElevationMm,
            lengthened.Start.DistanceTo(lengthened.End)), Tol);
    }

    [Fact]
    public void AfterCrossing_ReopenSemantics_LowerUpperAndFall()
    {
        // Simulates AK_EDIT reopen from persisted A/B after crossing.
        var persisted = StructuralMemberElevationRules.CreateSloped(7000d, 6000d);
        var lower = Math.Min(persisted.AxisStartElevationMm, persisted.AxisEndElevationMm);
        var upper = Math.Max(persisted.AxisStartElevationMm, persisted.AxisEndElevationMm);
        Assert.Equal(6000d, lower, Tol);
        Assert.Equal(7000d, upper, Tol);
        Assert.False(StructuralMemberElevationRules.IsStartTheEaveEnd(
            persisted.AxisStartElevationMm, persisted.AxisEndElevationMm));
        Assert.False(StructuralMemberElevationRules.ResolveIsSlopeDirectionReversedForDownhill(
            persisted.AxisStartElevationMm, persisted.AxisEndElevationMm));
        var slopeText = StructuralMemberElevationRules.FormatSlopeDegrees(
            StructuralMemberElevationRules.DeriveSlopeDegrees(
                persisted.AxisStartElevationMm, persisted.AxisEndElevationMm, 3000d));
        Assert.False(slopeText.StartsWith('-'));
        Assert.EndsWith("°", slopeText);
    }

    [Fact]
    public void ClassicLengthen_PhysicalPitchMatchesPersistedSlope()
    {
        const double slope = 50d;
        var seed = Seed();
        var plan = seed.AcceptedPlanAxis;
        var planLen = plan.Start.DistanceTo(plan.End);
        var elevation = ElevationForSlope(planLen, slope);
        Assert.True(StructuralMemberElevationRules.TryPatchBuildStateForElevation(
            seed, plan, elevation, out var patched) && patched is not null);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            patched, plan, out _, out var accepted, out var reason,
            useIndependentHorizontalFrame: true), reason);

        var lengthened = LengthenEnd(plan, 500d);
        Assert.True(StructuralMemberElevationRules.TryAdaptForClassicPlanEdit(
            elevation, plan, lengthened, out var adapted) && adapted is not null);
        Assert.True(StructuralMemberElevationRules.TryPatchBuildStateForElevation(
            accepted!, lengthened, adapted!, out var gripPatched) && gripPatched is not null);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            gripPatched, lengthened, out var body, out var rebuilt, out reason,
            useIndependentHorizontalFrame: true), reason);

        Assert.True(StructuralMemberElevationRules.ElevationPhysicalPitchesAgree(
            adapted, lengthened, rebuilt!));
        Assert.Equal(slope, rebuilt!.PitchDegrees, 0.05);
        Assert.Equal(slope, StructuralMemberElevationRules.PitchDegreesFromLongitudinalAxis(
            rebuilt.SectionFrame!.LongitudinalAxis), 0.05);
        Assert.Equal(0d, lengthened.Start.Z);
        Assert.Equal(0d, lengthened.End.Z);
        Assert.Equal(80d, body!.WidthMm, Tol);
        Assert.Equal(160d, body.HeightMm, Tol);
    }

    [Fact]
    public void Move_PreservesBothEndpointElevations()
    {
        var plan = Plan(4000d);
        var elevation = ElevationForSlope(4000d, 40d);
        var moved = new RoofSegment3D(
            new(plan.Start.X + 100d, plan.Start.Y + 50d, 0d),
            new(plan.End.X + 100d, plan.End.Y + 50d, 0d));
        Assert.Equal(RoofOrdinaryGripChange.Middle,
            RoofOrdinaryGripLifecycleRules.Classify(plan, moved));
        Assert.True(StructuralMemberElevationRules.TryAdaptForClassicPlanEdit(
            elevation, plan, moved, out var adapted) && adapted is not null);
        Assert.Equal(elevation.AxisStartElevationMm, adapted!.AxisStartElevationMm, Tol);
        Assert.Equal(elevation.AxisEndElevationMm, adapted.AxisEndElevationMm, Tol);
    }

    [Fact]
    public void GripSourceContract_UsesClassicPlanEditNotCalculationMode()
    {
        var grip = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofOrdinaryGripLifecycleService.cs"));
        Assert.Contains("TryAdaptForClassicPlanEdit", grip);
        Assert.DoesNotContain("TryAdaptForPlanChange(", grip);
    }

    private static RoofSegment3D Plan(double lengthMm) =>
        new(new(0d, 0d, 0d), new(0d, lengthMm, 0d));

    private static RoofSegment3D LengthenEnd(RoofSegment3D plan, double deltaAlongMm)
    {
        var dx = plan.End.X - plan.Start.X;
        var dy = plan.End.Y - plan.Start.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        var ux = dx / len;
        var uy = dy / len;
        return plan with
        {
            End = new(plan.End.X + ux * deltaAlongMm, plan.End.Y + uy * deltaAlongMm, 0d),
        };
    }

    private static StructuralMemberElevationState ElevationForSlope(double planLengthMm, double slopeDegrees)
    {
        var rise = planLengthMm * Math.Tan(Math.Abs(slopeDegrees) * Math.PI / 180d);
        return slopeDegrees >= 0
            ? StructuralMemberElevationRules.CreateSloped(0d, rise)
            : StructuralMemberElevationRules.CreateSloped(rise, 0d);
    }

    private static RoofOrdinaryPhysicalBuildState Seed()
    {
        const double x = 36141.02099635819, y = 11478.696868240506;
        var footprint = RoofFootprintValidator.Validate(new([
            new(x, y), new(x + 10000, y), new(x + 10000, y + 6000), new(x, y + 6000)
        ], true)).Footprint!;
        var roof = Assert.IsType<HipRoofGeometry>(
            RoofGeometrySolver.Solve(new(footprint, new(45), RoofKind.Hip)).Geometry);
        var anchor = RoofFaceRafterLayoutService.Create(roof.Topology, 900).Layout!.Segments.First(s =>
            s.SourceFaceIndex == 0 && s.EndBoundaryRole == RoofRafterBoundaryRole.Ridge);
        return RoofOrdinaryPhysicalBuildStateRules.Capture(
            roof.Topology, anchor,
            new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 5),
            3000, 80, 160, new(),
            Array.Empty<RoofStructuralRafterTrimSource>(),
            new(new(41591.02099635819, y, 0), new(41591.02099635819, y + 3000, 0)));
    }

    private static string RepoRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        return root!.FullName;
    }
}
