using System.Text.Json;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// Elevation → ordinary lifecycle interoperability (CAD-neutral).
/// HOST proved Elevation Persist(patchedState) left GRIP unable to resolve height
/// face pairs even though TryBuild materialized orientation.NewFrame. Persist must
/// use the post-rebuild accepted frame (TryCapture / updated), same as Detach/GRIP.
/// </summary>
public sealed class StructuralMemberElevationGripInteroperabilityTests
{
    private const double Tol = 1e-6;

    [Theory]
    [InlineData(50d)]
    [InlineData(35d)]
    [InlineData(40d)]
    [InlineData(60d)]
    [InlineData(-30d)]
    public void ElevationRebuild_BeforeAnyGrip_MeasuredBodyPitchMatchesTarget(double targetSlopeDegrees)
    {
        var seed = Seed();
        var plan = seed.AcceptedPlanAxis;
        var planLen = plan.Start.DistanceTo(plan.End);
        var elevation = ElevationForSlope(planLen, targetSlopeDegrees)
            with { CalculationMode = StructuralMemberElevationCalculationMode.LowerSlope };
        var targetPitch = StructuralMemberElevationRules.AbsolutePitchDegrees(targetSlopeDegrees);
        Assert.True(StructuralMemberElevationRules.TryPatchBuildStateForElevation(
            seed, plan, elevation, out var patched) && patched is not null);
        Assert.Equal(targetPitch, patched!.PitchDegrees, 0.05);

        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            patched, plan, out var body, out var updated, out var reason,
            useIndependentHorizontalFrame: true), reason);
        Assert.NotNull(updated?.SectionFrame);
        var measuredPitch = StructuralMemberElevationRules.PitchDegreesFromLongitudinalAxis(
            updated!.SectionFrame!.LongitudinalAxis);
        Assert.Equal(targetPitch, measuredPitch, 0.05);
        Assert.Equal(targetPitch, updated.PitchDegrees, 0.05);
        Assert.True(StructuralMemberElevationRules.ElevationPhysicalPitchesAgree(
            elevation, plan, updated));
        Assert.Equal(80d, body!.WidthMm, Tol);
        Assert.Equal(160d, body.HeightMm, Tol);
        Assert.Equal(0d, plan.Start.Z);
        Assert.Equal(0d, plan.End.Z);

        // True length ≈ sqrt(plan² + ΔZ²)
        var deltaZ = elevation.AxisEndElevationMm - elevation.AxisStartElevationMm;
        var expectedLength = Math.Sqrt(planLen * planLen + deltaZ * deltaZ);
        Assert.Equal(expectedLength, body.PhysicalLengthMm, 0.05);
    }

    [Theory]
    [InlineData(StructuralMemberElevationCalculationMode.LowerSlope, 50d)]
    [InlineData(StructuralMemberElevationCalculationMode.UpperSlope, 50d)]
    [InlineData(StructuralMemberElevationCalculationMode.LowerUpper, 45d)]
    public void AfterElevation_GripPlanChange_PreservesSlopeRegardlessOfCalculationMode(
        StructuralMemberElevationCalculationMode mode,
        double initialSlopeDegrees)
    {
        var seed = Seed();
        var plan = seed.AcceptedPlanAxis;
        var planLen = plan.Start.DistanceTo(plan.End);
        var elevation = ElevationForSlope(planLen, initialSlopeDegrees) with { CalculationMode = mode };
        Assert.True(StructuralMemberElevationRules.TryPatchBuildStateForElevation(
            seed, plan, elevation, out var patched) && patched is not null);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            patched, plan, out _, out var accepted, out var reason,
            useIndependentHorizontalFrame: true), reason);

        // Start endpoint moved (LENGTHEN/GRIP) — End Z is the anchor.
        var stretched = plan with { Start = new(plan.Start.X, plan.Start.Y - 250d, 0d) };
        var newLen = stretched.Start.DistanceTo(stretched.End);
        Assert.True(StructuralMemberElevationRules.TryAdaptForClassicPlanEdit(
            elevation, plan, stretched, out var adapted) && adapted is not null);

        var absBefore = Math.Abs(initialSlopeDegrees);
        var slopeAfter = StructuralMemberElevationRules.AbsolutePitchDegrees(
            StructuralMemberElevationRules.DeriveSlopeDegrees(
                adapted!.AxisStartElevationMm, adapted.AxisEndElevationMm, newLen));
        Assert.Equal(absBefore, slopeAfter, 0.05);
        // Anchor (End) Z unchanged; moved Start Z recalculated.
        Assert.Equal(elevation.AxisEndElevationMm, adapted.AxisEndElevationMm, Tol);
        Assert.NotEqual(elevation.AxisStartElevationMm, adapted.AxisStartElevationMm);

        Assert.True(StructuralMemberElevationRules.TryPatchBuildStateForElevation(
            accepted!, stretched, adapted, out var gripPatched) && gripPatched is not null);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            gripPatched, stretched, out var gripBody, out var gripAccepted, out reason,
            useIndependentHorizontalFrame: true), reason);
        Assert.True(StructuralMemberElevationRules.ElevationPhysicalPitchesAgree(
            adapted, stretched, gripAccepted!));
        Assert.Equal(80d, gripBody!.WidthMm, Tol);
        Assert.Equal(160d, gripBody.HeightMm, Tol);
        Assert.Equal(0d, stretched.Start.Z);
        Assert.Equal(0d, stretched.End.Z);
    }

    [Theory]
    [InlineData(50d)]
    [InlineData(35d)]
    [InlineData(40d)]
    [InlineData(60d)]
    [InlineData(-30d)]
    public void ElevationRebuild_AcceptedFrame_ResolvesAgainstCurrentBody(double targetSlopeDegrees)
    {
        var seed = Seed();
        var plan = seed.AcceptedPlanAxis;
        Assert.Equal(0d, plan.Start.Z);
        Assert.Equal(0d, plan.End.Z);
        var planLen = plan.Start.DistanceTo(plan.End);
        var elevation = ElevationForSlope(planLen, targetSlopeDegrees);
        Assert.True(StructuralMemberElevationRules.TryPatchBuildStateForElevation(
            seed, plan, elevation, out var patched) && patched is not null);

        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            patched, plan, out var body, out var updated, out var reason,
            useIndependentHorizontalFrame: true), reason);
        Assert.NotNull(body);
        Assert.NotNull(updated);
        Assert.NotNull(updated!.SectionFrame);

        // Authoritative post-rebuild state (Core equivalent of TryCapture result).
        var accepted = updated;
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.MatchesCurrentBody(
            accepted, plan, body!.SolidVertices));
        Assert.True(RoofOrdinarySectionTransportRules.IsValid(accepted.SectionFrame));
        Assert.Equal(80d, body.WidthMm, Tol);
        Assert.Equal(160d, body.HeightMm, Tol);
        Assert.Equal(0d, plan.Start.Z);
        Assert.Equal(0d, plan.End.Z);

        // SectionFrameReader-style longitudinal-side contract using accepted L.
        Assert.True(TryResolveWidthAndHeightPairs(
            body.SolidVertices, accepted.SectionFrame!, 80d, 160d, out var sides),
            "accepted frame must resolve both width and height face pairs");
        Assert.True(sides >= 4);

        // Persisting the pre-build patch frame is unsafe when TryBuild transports it.
        if (patched!.SectionFrame is { } patchFrame &&
            accepted.SectionFrame is { } acceptedFrame &&
            patchFrame.LongitudinalAxis.DistanceTo(acceptedFrame.LongitudinalAxis) > 1e-7)
        {
            Assert.False(TryResolveWidthAndHeightPairs(
                body.SolidVertices, patchFrame, 80d, 160d, out _),
                "pre-build elevation patch frame must not be used as GRIP hint when it diverges");
        }
    }

    [Theory]
    [InlineData(50d)]
    [InlineData(35d)]
    [InlineData(-25d)]
    public void AfterElevation_GripStretchLengthenRotateAndSecondElevationSucceed(double targetSlopeDegrees)
    {
        var seed = Seed();
        var plan = seed.AcceptedPlanAxis;
        var planLen = plan.Start.DistanceTo(plan.End);
        var elevation = ElevationForSlope(planLen, targetSlopeDegrees);
        Assert.True(StructuralMemberElevationRules.TryPatchBuildStateForElevation(
            seed, plan, elevation, out var patched) && patched is not null);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            patched, plan, out var body, out var accepted, out var reason,
            useIndependentHorizontalFrame: true), reason);
        Assert.NotNull(accepted?.SectionFrame);

        // GRIP / STRETCH: plan endpoint move with independent horizontal frame.
        var stretched = plan with
        {
            Start = new(plan.Start.X, plan.Start.Y - 200d, 0d),
        };
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            accepted!, stretched, out var gripBody, out var gripAccepted, out reason,
            useIndependentHorizontalFrame: true), reason);
        Assert.Equal(80d, gripBody!.WidthMm, Tol);
        Assert.Equal(160d, gripBody.HeightMm, Tol);
        Assert.Equal(0d, stretched.Start.Z);
        Assert.Equal(0d, stretched.End.Z);
        Assert.True(TryResolveWidthAndHeightPairs(
            gripBody.SolidVertices, gripAccepted!.SectionFrame!, 80d, 160d, out _));

        // LENGTHEN
        var lengthened = stretched with
        {
            End = new(stretched.End.X, stretched.End.Y + 150d, 0d),
        };
        Assert.True(RoofOrdinaryLengthenRules.IsEndpointLengthEdit(stretched, lengthened, out reason), reason);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            gripAccepted, lengthened, out var lengthenBody, out var lengthenAccepted, out reason,
            useIndependentHorizontalFrame: true), reason);
        Assert.Equal(80d, lengthenBody!.WidthMm, Tol);
        Assert.Equal(160d, lengthenBody.HeightMm, Tol);

        // ROTATE
        var dx = lengthened.End.X - lengthened.Start.X;
        var dy = lengthened.End.Y - lengthened.Start.Y;
        var angle = 20d * Math.PI / 180d;
        var rotated = new RoofSegment3D(
            lengthened.Start,
            new(
                lengthened.Start.X + dx * Math.Cos(angle) - dy * Math.Sin(angle),
                lengthened.Start.Y + dx * Math.Sin(angle) + dy * Math.Cos(angle),
                0d));
        Assert.True(RoofOrdinaryRotateRules.IsRigidPlanRotation(lengthened, rotated));
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            lengthenAccepted!, rotated, out var rotateBody, out var rotateAccepted, out reason,
            useIndependentHorizontalFrame: true), reason);
        Assert.Equal(80d, rotateBody!.WidthMm, Tol);
        Assert.Equal(160d, rotateBody.HeightMm, Tol);
        Assert.Equal(0d, rotated.Start.Z);
        Assert.Equal(0d, rotated.End.Z);

        // Second Elevation edit from the post-rotate accepted state.
        var secondPlanLen = rotated.Start.DistanceTo(rotated.End);
        var secondElevation = ElevationForSlope(secondPlanLen, targetSlopeDegrees + 5d);
        Assert.True(StructuralMemberElevationRules.TryPatchBuildStateForElevation(
            rotateAccepted!, rotated, secondElevation, out var secondPatched) && secondPatched is not null);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            secondPatched, rotated, out var secondBody, out var secondAccepted, out reason,
            useIndependentHorizontalFrame: true), reason);
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.MatchesCurrentBody(
            secondAccepted!, rotated, secondBody!.SolidVertices));
        Assert.True(TryResolveWidthAndHeightPairs(
            secondBody.SolidVertices, secondAccepted!.SectionFrame!, 80d, 160d, out _));

        // IndependentMemberId stability is ownership-layer; Core codec round-trips a fixed id.
        var identity = new RoofIndependentOrdinaryTimberData(
            1, "369c1cdb71ad488f870b272942bf670f",
            RoofIndependentOrdinaryOriginKind.DetachedFromAuto,
            RoofIndependentOrdinaryEntityRole.PlanLine, "2912", seed.MemberKey);
        var encoded = RoofIndependentOrdinaryTimberDataCodec.Encode(identity);
        Assert.Equal(encoded, RoofIndependentOrdinaryTimberDataCodec.Encode(
            identity with { IndependentMemberId = identity.IndependentMemberId }));

        // Persist round-trip of accepted v2 remains valid for GRIP.
        var reopened = JsonSerializer.Deserialize<RoofOrdinaryPhysicalBuildState>(
            JsonSerializer.Serialize(secondAccepted))!;
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.IsValid(reopened));
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            reopened, rotated, out _, out _, out reason, useIndependentHorizontalFrame: true), reason);
        Assert.Equal(body!.WidthMm, secondBody.WidthMm, Tol);
        Assert.Equal(body.HeightMm, secondBody.HeightMm, Tol);
    }

    [Fact]
    public void Elevation45To50_PersistedAcceptedFrame_NotPreBuildPatch_IsRequiredForGrip()
    {
        var seed = Seed();
        var plan = seed.AcceptedPlanAxis;
        var planLen = plan.Start.DistanceTo(plan.End);
        var elevation = ElevationForSlope(planLen, 50d);
        Assert.True(StructuralMemberElevationRules.TryPatchBuildStateForElevation(
            seed, plan, elevation, out var patched) && patched is not null);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(
            patched, plan, out var body, out var updated, out var reason,
            useIndependentHorizontalFrame: true), reason);

        // GRIP Accept path Persist(updated) — updated is always body-compatible.
        Assert.True(RoofOrdinaryPhysicalBuildStateMigrationRules.MatchesCurrentBody(
            updated!, plan, body!.SolidVertices));

        // Source contract for AutoCAD Elevation: Persist acceptedState after TryCapture,
        // never the bare pre-build patchedState alone.
        var elevationService = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofOrdinaryElevationLifecycleService.cs"));
        Assert.Contains("RoofOrdinaryPhysicalSectionFrameReader.TryCapture(solid, updated, out var captured)",
            elevationService);
        Assert.Contains("acceptedState = captured", elevationService);
        Assert.Contains("Persist(line, transaction, acceptedState)", elevationService);
        Assert.DoesNotContain("Persist(line, transaction, patchedState)", elevationService);
    }

    [Fact]
    public void FailedGripRemainsAtomic_SourceContract()
    {
        var grip = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofOrdinaryGripLifecycleService.cs"));
        Assert.Contains("Ordinary GRIP full physical build state unavailable", grip);
        Assert.Contains("rollback=", grip);
        Assert.Contains("physicalRebuild=", grip);
        Assert.Contains("catch (Exception rollbackError)", grip);
    }

    [Fact]
    public void SuccessfulElevationIncrementsModifiedCount_FailedDoesNot()
    {
        var edit = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "AcKrovy.AutoCAD", "Commands", "AcKrovyCommands.cs"));
        var start = edit.IndexOf("public void Edit()", StringComparison.Ordinal);
        var end = edit.IndexOf("public void FlipSlopeDirection()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var block = edit[start..end];
        Assert.Contains("elevationLifecycleSucceeded", block);
        Assert.Contains("if (elevationLifecycleSucceeded && changed == 0)", block);
        Assert.Contains("changed = 1;", block);
        Assert.Contains("Command_Edit_ResultFormat", block);
        // Failed Elevation aborts before timber patch accounting.
        var fail = block.IndexOf("RoofOrdinaryElevation_Failed", StringComparison.Ordinal);
        var patchLoop = block.IndexOf("foreach (var id in ids)", StringComparison.Ordinal);
        Assert.True(fail >= 0 && patchLoop > fail);
        Assert.Contains("return;", block[fail..patchLoop]);
        Assert.DoesNotContain("changed = 1;", block[fail..patchLoop]);
    }

    [Fact]
    public void ElevationRebuildPhysical_CapturesSectionFrameBeforePersist_SourceContract()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "AcKrovy.AutoCAD", "Infrastructure", "RoofOrdinaryElevationLifecycleService.cs"));
        var rebuild = service[
            service.IndexOf("private static bool RebuildPhysical(", StringComparison.Ordinal)..
            service.IndexOf("private static bool ShowDetachDialog(", StringComparison.Ordinal)];
        Assert.Contains("useIndependentHorizontalFrame: true", rebuild);
        Assert.Contains("MaterializeOrdinaryMember(physical)", rebuild);
        Assert.Contains("TryCapture(solid, updated, out var captured)", rebuild);
        Assert.Contains("acceptedState = captured", rebuild);
        Assert.DoesNotContain("line.StartPoint", rebuild);
        Assert.DoesNotContain("line.EndPoint", rebuild);
    }

    /// <summary>
    /// CAD-neutral analogue of SectionFrameReader longitudinal side + width/height Pair:
    /// stored L/W/H must be orthonormal and the body extents along W/H must match
    /// the persisted section dimensions (same contract GRIP uses after Elevation).
    /// </summary>
    private static bool TryResolveWidthAndHeightPairs(
        IReadOnlyList<RoofPoint3D> vertices,
        RoofOrdinarySectionFrame hint,
        double widthMm,
        double heightMm,
        out int longitudinalSideFaces)
    {
        longitudinalSideFaces = 0;
        if (!RoofOrdinarySectionTransportRules.IsValid(hint) || vertices.Count < 8)
            return false;

        var l = hint.LongitudinalAxis;
        var w = hint.WidthAxis;
        var h = hint.HeightAxis;
        if (Math.Abs(Dot(w, h)) > 1e-7 || Math.Abs(Dot(w, l)) > 1e-7 || Math.Abs(Dot(h, l)) > 1e-7)
            return false;

        var plusW = Extreme(vertices, w);
        var minusW = Extreme(vertices, Scale(w, -1));
        var plusH = Extreme(vertices, h);
        var minusH = Extreme(vertices, Scale(h, -1));
        var widthDist = Math.Abs(Dot(Sub(plusW, minusW), w));
        var heightDist = Math.Abs(Dot(Sub(plusH, minusH), h));
        if (Math.Abs(widthDist - widthMm) > 0.05 || Math.Abs(heightDist - heightMm) > 0.05)
            return false;

        // Longitudinal side faces = ±W and ±H when all are ⊥ stored L.
        foreach (var n in new[] { w, Scale(w, -1), h, Scale(h, -1) })
        {
            if (Math.Abs(Dot(n, l)) <= 1e-5)
                longitudinalSideFaces++;
        }

        return longitudinalSideFaces >= 4;
    }

    private static RoofPoint3D Extreme(IReadOnlyList<RoofPoint3D> vertices, RoofPoint3D axis) =>
        vertices.OrderByDescending(p => Dot(p, axis)).First();

    private static StructuralMemberElevationState ElevationForSlope(double planLengthMm, double slopeDegrees)
    {
        var rise = planLengthMm * Math.Tan(slopeDegrees * Math.PI / 180d);
        // Start at eave (lower) when slope positive; reverse for negative to keep signed slope.
        return slopeDegrees >= 0
            ? StructuralMemberElevationRules.CreateSloped(0d, rise)
            : StructuralMemberElevationRules.CreateSloped(Math.Abs(rise), 0d);
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

    private static RoofPoint3D Sub(RoofPoint3D a, RoofPoint3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static RoofPoint3D Scale(RoofPoint3D p, double s) => new(p.X * s, p.Y * s, p.Z * s);
    private static double Dot(RoofPoint3D a, RoofPoint3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static string RepoRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        return root!.FullName;
    }
}
