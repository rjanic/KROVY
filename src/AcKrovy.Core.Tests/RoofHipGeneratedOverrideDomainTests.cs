using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// HOST H7e: Hip manual overrides that leave the footprint must dormantInvalidDomain
/// even when the legacy station×run UV slab would still intersect (L/T notch).
/// </summary>
public sealed class RoofHipGeneratedOverrideDomainTests
{
    private const double SpacingMm = 500d;
    private const double WidthMm = 80d;
    private static readonly RoofPoint3D ZUp = RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal;

    public static IEnumerable<object[]> OutsideHipShapes()
    {
        yield return ["Rectangle", Rectangle()];
        yield return ["L", LShape()];
        yield return ["U", UShape()];
        yield return ["T", TShape()];
        yield return ["Concave", Concave()];
    }

    [Theory]
    [MemberData(nameof(OutsideHipShapes))]
    public void OutsideFootprintOverride_DormantInvalidDomain(string name, RoofPoint2D[] polygon)
    {
        var layout = Layout(polygon);
        var target = PickOrdinaryRafter(layout);
        var outside = TranslateFarOutside(layout, target);
        var stored = CaptureOverride(layout, target, outside);

        var plan = Plan(layout, [stored]);
        var item = plan.Items.Single(candidate => candidate.Rafter.LogicalKey.Equals(target.LogicalKey));

        Assert.True(plan.IsValid, name);
        Assert.Equal(1, plan.StoredOverrideCount);
        Assert.Equal(1, plan.ResolvedOverrideCount);
        Assert.Equal(0, plan.GeometryReplayCount);
        Assert.Equal(1, plan.DormantInvalidDomainCount);
        Assert.Equal(RoofGeneratedMemberReplayDisposition.DormantInvalidDomain, item.Disposition);
        Assert.Equal(Canonical(target), item.Geometry);
        Assert.False(
            RoofGeneratedMemberDomainRules.OverlapsBoundedPlane(layout, target, outside),
            name);
    }

    [Theory]
    [InlineData("L")]
    [InlineData("T")]
    public void ValidSmallLateralInsideFace_StillReplays(string shape)
    {
        var polygon = shape == "L" ? LShape() : TShape();
        var layout = Layout(polygon);
        var target = PickOrdinaryRafter(layout);
        var stored = new RoofGeneratedMemberOverride(
            target.LogicalKey,
            false,
            AlongMm: 0d,
            LateralMm: 40d,
            RotationRadians: 0d,
            StartOffsetMm: 0d,
            EndOffsetMm: 0d);

        var plan = Plan(layout, [stored]);
        Assert.Equal(1, plan.GeometryReplayCount);
        Assert.Equal(0, plan.DormantInvalidDomainCount);
        Assert.Equal(
            RoofGeneratedMemberReplayDisposition.GeometryReplayed,
            plan.Items.Single(item => item.Rafter.LogicalKey.Equals(target.LogicalKey)).Disposition);
    }

    [Fact]
    public void L_ControlPair_InsideReplays_OutsideDormant()
    {
        var layout = Layout(LShape());
        var target = PickOrdinaryRafter(layout);
        var inside = new RoofGeneratedMemberOverride(
            target.LogicalKey,
            false,
            0d,
            40d,
            0d,
            0d,
            0d);
        var outside = CaptureOverride(layout, target, TranslateFarOutside(layout, target));

        Assert.Equal(1, Plan(layout, [inside]).GeometryReplayCount);
        Assert.Equal(1, Plan(layout, [outside]).DormantInvalidDomainCount);
    }

    [Fact]
    public void LegacyUvSlabWouldAcceptLNotch_ButFootprintRejects()
    {
        // Proves the H7e root cause: notch XY is outside the L footprint but can still
        // land inside the Hip adapter's fake station×run UV rectangle.
        var layout = Layout(LShape());
        Assert.True(layout.DomainPolygon.Count >= 6);
        var target = PickOrdinaryRafter(layout);
        var notch = new RoofGeneratedMemberGeometry(
            new RoofPoint3D(4500d, 4500d, 0d),
            new RoofPoint3D(4500d, 5500d, 0d));

        Assert.False(RoofFootprintContainmentRules.SegmentOverlapsPolygon(
            new RoofPoint2D(4500d, 4500d),
            new RoofPoint2D(4500d, 5500d),
            layout.DomainPolygon));
        Assert.False(RoofGeneratedMemberDomainRules.OverlapsBoundedPlane(layout, target, notch));
    }

    [Fact]
    public void LogicalKeyMissing_RemainsSeparateFromInvalidDomain()
    {
        var layout = Layout(LShape());
        var missing = new RoofGeneratedMemberOverride(
            new RoofGeneratedMemberKey(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 9999),
            false,
            0d,
            100d,
            0d,
            0d,
            0d);
        var plan = Plan(layout, [missing]);
        Assert.Equal(0, plan.ResolvedOverrideCount);
        Assert.Equal(1, plan.DormantMissingKeyCount);
        Assert.Equal(0, plan.DormantInvalidDomainCount);
    }

    private static RoofGeneratedMemberReplayPlan Plan(
        RoofRafterLayout layout,
        IReadOnlyList<RoofGeneratedMemberOverride> overrides) =>
        RoofGeneratedMemberReplayPlanner.Create(layout, 0d, ZUp, overrides);

    private static RoofGeneratedMemberGeometry Canonical(RoofRafterGeometry rafter) =>
        RoofGeneratedMemberOverrideRules.CanonicalGeometry(rafter, 0d);

    private static RoofGeneratedMemberOverride CaptureOverride(
        RoofRafterLayout layout,
        RoofRafterGeometry target,
        RoofGeneratedMemberGeometry observed)
    {
        _ = layout;
        var canonical = Canonical(target);
        Assert.True(RoofGeneratedMemberOverrideMath.TryComposeRigidKeepingEndpointOffsets(
            canonical,
            observed,
            ZUp,
            target.LogicalKey,
            existing: null,
            reservedElementId: null,
            out var captured,
            out var failure),
            failure?.Reason ?? "compose-failed");
        Assert.NotNull(captured);
        return captured!;
    }

    private static RoofGeneratedMemberGeometry TranslateFarOutside(
        RoofRafterLayout layout,
        RoofRafterGeometry target)
    {
        var maxX = layout.DomainPolygon.Max(point => point.X);
        var maxY = layout.DomainPolygon.Max(point => point.Y);
        var shiftX = maxX + 5000d;
        var shiftY = maxY + 5000d;
        var canonical = Canonical(target);
        return new RoofGeneratedMemberGeometry(
            new RoofPoint3D(canonical.Start.X + shiftX, canonical.Start.Y + shiftY, canonical.Start.Z),
            new RoofPoint3D(canonical.End.X + shiftX, canonical.End.Y + shiftY, canonical.End.Z));
    }

    private static RoofRafterGeometry PickOrdinaryRafter(RoofRafterLayout layout)
    {
        // Prefer a mid-length ordinary centerline so small lateral stays on-face.
        return layout.Rafters
            .OrderByDescending(item => item.PlanLengthMm)
            .Skip(Math.Max(0, layout.Rafters.Count / 4))
            .First();
    }

    private static RoofRafterLayout Layout(RoofPoint2D[] polygon)
    {
        var geometry = SolveHip(polygon, 30d);
        var result = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(SpacingMm, WidthMm));
        Assert.True(result.IsValid, result.Error.ToString());
        Assert.True(result.Layout!.DomainPolygon.Count >= 3);
        return result.Layout;
    }

    private static HipRoofGeometry SolveHip(IReadOnlyList<RoofPoint2D> polygon, double slope)
    {
        var footprint = RoofFootprintValidator.Validate(new RoofFootprintInput(polygon, true));
        Assert.True(footprint.IsValid, footprint.Error.ToString());
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint.Footprint!,
            new RoofParameters(slope),
            RoofKind.Hip));
        Assert.True(solved.IsValid, solved.Error.ToString());
        return Assert.IsType<HipRoofGeometry>(solved.Geometry);
    }

    private static RoofPoint2D[] Rectangle() =>
        [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)];

    private static RoofPoint2D[] LShape() =>
    [
        new(0, 0), new(8000, 0), new(8000, 3000),
        new(3000, 3000), new(3000, 8000), new(0, 8000),
    ];

    private static RoofPoint2D[] UShape() =>
    [
        new(0, 0), new(10000, 0), new(10000, 9000), new(7000, 9000),
        new(7000, 3000), new(3000, 3000), new(3000, 9000), new(0, 9000),
    ];

    private static RoofPoint2D[] TShape() =>
    [
        new(0, 0), new(10000, 0), new(10000, 3000), new(6500, 3000),
        new(6500, 9000), new(3500, 9000), new(3500, 3000), new(0, 3000),
    ];

    private static RoofPoint2D[] Concave() =>
    [
        new(0, 0), new(12000, 0), new(12000, 4000), new(8000, 4000),
        new(8000, 8000), new(4000, 8000), new(4000, 4000), new(0, 4000),
    ];
}
