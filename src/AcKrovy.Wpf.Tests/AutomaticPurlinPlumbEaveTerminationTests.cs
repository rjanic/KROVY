using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;
using Xunit.Abstractions;

namespace AcKrovy.Wpf.Tests;

/// <summary>
/// Defect A: plumb eave termination with preserved BuildRafters slope seating edges.
/// </summary>
public sealed class AutomaticPurlinPlumbEaveTerminationTests
{
    private const double MaxMm = 0.05d;
    private readonly ITestOutputHelper _output;

    public AutomaticPurlinPlumbEaveTerminationTests(ITestOutputHelper output) =>
        _output = output;

    public static IEnumerable<object[]> PitchAndHeight()
    {
        foreach (var pitch in new[] { 30d, 45d })
        {
            foreach (var height in new[] { 100d, 125d, 160d })
            {
                yield return [pitch, height];
            }
        }
    }

    [Theory]
    [MemberData(nameof(PitchAndHeight))]
    public void RenderedEaveTermination_IsPlumb_AndLowerLiesOnBuildRaftersSlope(
        double pitchDegrees,
        double rafterHeightMm)
    {
        var presentation = CreatePresentation(pitchDegrees, rafterHeightMm, seatingPercent: 0d);
        var scene = AutomaticPurlinSectionSvgTemplate.CreateMasterScene(
            presentation,
            (x, z) => new Point(x, -z));

        AutomaticPurlinSectionPointMm? leftUpperEave = null;
        AutomaticPurlinSectionPointMm? rightUpperEave = null;

        foreach (var side in new[]
                 {
                     AutomaticPurlinSectionSide.Left,
                     AutomaticPurlinSectionSide.Right,
                 })
        {
            var rafter = Assert.Single(presentation.Rafters, r => r.Side == side);
            Assert.True(
                AutomaticPurlinSectionSvgTemplate.TryGetRenderedRafterViewPolygon(
                    scene, side, out var rendered));
            Assert.Equal(4, rendered.Count);

            var buildUpperEave = rafter.Corners[0].ZMm <= rafter.Corners[1].ZMm
                ? rafter.Corners[0]
                : rafter.Corners[1];
            // Rendered points are in view space Y=-Z; invert to model for assertions.
            var modelCorners = rendered
                .Select(p => new AutomaticPurlinSectionPointMm(p.X, -p.Y))
                .ToList();

            var renderedUpperEave = modelCorners
                .Where(c => Math.Abs(c.XMm - buildUpperEave.XMm) < 1d)
                .OrderBy(c => Math.Abs(c.ZMm - buildUpperEave.ZMm))
                .First();
            Assert.Equal(buildUpperEave.XMm, renderedUpperEave.XMm, 3);
            Assert.Equal(buildUpperEave.ZMm, renderedUpperEave.ZMm, 3);

            var renderedLowerEave = modelCorners
                .Where(c => Math.Abs(c.XMm - renderedUpperEave.XMm) <= MaxMm)
                .OrderBy(c => c.ZMm)
                .First();
            Assert.True(renderedLowerEave.ZMm < renderedUpperEave.ZMm);
            Assert.Equal(renderedUpperEave.XMm, renderedLowerEave.XMm, 3);

            var lowerOnSlope = EdgeZ(rafter.Corners[3], rafter.Corners[2], renderedUpperEave.XMm);
            Assert.Equal(lowerOnSlope, renderedLowerEave.ZMm, 3);

            var verticalSpan = Math.Abs(renderedUpperEave.ZMm - renderedLowerEave.ZMm);
            var expectedSpan = Math.Abs(
                EdgeZ(rafter.Corners[0], rafter.Corners[1], renderedUpperEave.XMm) -
                EdgeZ(rafter.Corners[3], rafter.Corners[2], renderedUpperEave.XMm));
            Assert.Equal(expectedSpan, verticalSpan, 3);
            if (Math.Abs(pitchDegrees - 45d) < 0.01d)
            {
                Assert.Equal(rafterHeightMm / Math.Cos(Math.PI / 4d), verticalSpan, 3);
            }

            // Perpendicular thickness along the BuildRafters offset (unchanged physical).
            var ox = rafter.Corners[3].XMm - rafter.Corners[0].XMm;
            var oz = rafter.Corners[3].ZMm - rafter.Corners[0].ZMm;
            Assert.Equal(rafterHeightMm, Math.Sqrt(ox * ox + oz * oz), 3);

            _output.WriteLine(
                $"{pitchDegrees}° H={rafterHeightMm} {side}: " +
                $"upperEave=({renderedUpperEave.XMm:F3},{renderedUpperEave.ZMm:F3}) " +
                $"plumbLower=({renderedLowerEave.XMm:F3},{renderedLowerEave.ZMm:F3}) " +
                $"span={verticalSpan:F3}");

            if (side == AutomaticPurlinSectionSide.Left)
            {
                leftUpperEave = renderedUpperEave;
            }
            else
            {
                rightUpperEave = renderedUpperEave;
            }
        }

        Assert.NotNull(leftUpperEave);
        Assert.NotNull(rightUpperEave);
        // Mirror: same |X| eave tips, same Z.
        Assert.Equal(Math.Abs(leftUpperEave!.Value.XMm), Math.Abs(rightUpperEave!.Value.XMm), 3);
        Assert.Equal(leftUpperEave.Value.ZMm, rightUpperEave.Value.ZMm, 3);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(25d)]
    [InlineData(50d)]
    [InlineData(75d)]
    [InlineData(100d)]
    public void SeatingContact_Unchanged_ForWallPlateIntermediateAndRidge(double seatingPercent)
    {
        const double pitch = 45d;
        const double rafterH = 125d;
        var presentation = CreatePresentation(pitch, rafterH, seatingPercent);
        var fraction = seatingPercent / 100d;

        foreach (var member in presentation.Members.Where(m =>
                     m.Role is RoofAutomaticPurlinGeneratorRole.WallPlate
                         or RoofAutomaticPurlinGeneratorRole.Intermediate
                         or RoofAutomaticPurlinGeneratorRole.Ridge))
        {
            if (member.Role == RoofAutomaticPurlinGeneratorRole.Ridge)
            {
                var leftX = member.CenterXMm - member.WidthMm / 2d;
                var rightX = member.CenterXMm + member.WidthMm / 2d;
                Assert.True(
                    AutomaticPurlinSectionSvgTemplate.TryInterpolateVisualRafterSeatingLocalZMm(
                        presentation.Rafters,
                        AutomaticPurlinSectionSide.Left,
                        leftX,
                        fraction,
                        out var leftTop,
                        out _));
                Assert.True(
                    AutomaticPurlinSectionSvgTemplate.TryInterpolateVisualRafterSeatingLocalZMm(
                        presentation.Rafters,
                        AutomaticPurlinSectionSide.Right,
                        rightX,
                        fraction,
                        out var rightTop,
                        out _));
                Assert.Equal((leftTop + rightTop) / 2d, member.MemberTopZMm, 3);
                continue;
            }

            if (member.Side is not (AutomaticPurlinSectionSide.Left or AutomaticPurlinSectionSide.Right))
            {
                continue;
            }

            // BottomEdge keeps physical Top; PlanDistance/Ridge snap. This fixture is BottomEdge.
            // Physical Top must still equal BuildRafters seating edge (authority unchanged by plumb tip).
            var contactX = member.CenterXMm +
                AutomaticPurlinSectionPresentation.SideContactCornerOffsetXMm(
                    member.Side, member.WidthMm);
            Assert.True(
                AutomaticPurlinSectionSvgTemplate.TryInterpolateVisualRafterSeatingLocalZMm(
                    presentation.Rafters,
                    member.Side,
                    contactX,
                    fraction,
                    out var edgeZ,
                    out _));
            // At 0%/100% BottomEdge Top tracks seating edge; intermediate seating % also tracks
            // Core physical seating which uses the same BuildRafters frame.
            Assert.Equal(edgeZ, member.MemberTopZMm, 3);
        }

        var ridge = Assert.Single(
            presentation.Members,
            m => m.Role == RoofAutomaticPurlinGeneratorRole.Ridge);
        Assert.True(ridge.CenterXMm == 0d || Math.Abs(ridge.CenterXMm) < 1d);
    }

    [Fact]
    public void HostFixture_OldVsNewEaveCoordinates_Documented()
    {
        var presentation = CreatePresentation(45d, 125d, 0d);
        var left = Assert.Single(
            presentation.Rafters,
            r => r.Side == AutomaticPurlinSectionSide.Left);
        var oldLowerEave = left.Corners[0].ZMm <= left.Corners[1].ZMm
            ? left.Corners[3]
            : left.Corners[2];
        Assert.True(
            AutomaticPurlinSectionSvgTemplate.TryCreatePlumbEaveRafterCorners(left, out var plumb));
        var upperEave = left.Corners[0].ZMm <= left.Corners[1].ZMm
            ? plumb[0]
            : plumb[1];
        var newLowerEave = plumb.First(c =>
            Math.Abs(c.XMm - upperEave.XMm) <= MaxMm &&
            Math.Abs(c.ZMm - upperEave.ZMm) > 1d);
        _output.WriteLine(
            $"OLD perp lower eave=({oldLowerEave.XMm:F3},{oldLowerEave.ZMm:F3})");
        _output.WriteLine(
            $"NEW plumb lower eave=({newLowerEave.XMm:F3},{newLowerEave.ZMm:F3})");
        _output.WriteLine(
            $"upper eave=({upperEave.XMm:F3},{upperEave.ZMm:F3})");
        Assert.Equal(upperEave.XMm, newLowerEave.XMm, 3);
        Assert.NotEqual(oldLowerEave.XMm, newLowerEave.XMm, 1);
        Assert.Equal(125d / Math.Cos(Math.PI / 4d), Math.Abs(upperEave.ZMm - newLowerEave.ZMm), 3);
    }

    [Fact]
    public void TechnicalElevations_Unchanged_ByPlumbTip()
    {
        var presentation = CreatePresentation(45d, 125d, 0d);
        var wp = Assert.Single(
            presentation.Members,
            m => m.Role == RoofAutomaticPurlinGeneratorRole.WallPlate &&
                 m.Side == AutomaticPurlinSectionSide.Left);
        // Host-like Bottom −1.000 m under ExplicitLocalPlane LocalZ=1000 → bottom ≈ 0.
        Assert.Equal(0d, wp.CenterZMm - wp.HeightMm / 2d, 3);
        Assert.Equal(140d, wp.MemberTopZMm, 3);
        var ridge = Assert.Single(
            presentation.Members,
            m => m.Role == RoofAutomaticPurlinGeneratorRole.Ridge);
        Assert.Equal(2743.223d, ridge.MemberTopZMm, 1);
    }

    private static AutomaticPurlinSectionPresentation CreatePresentation(
        double pitchDegrees,
        double rafterHeightMm,
        double seatingPercent)
    {
        var solved = Solve(pitchDegrees);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            seatingPercent);
        // Mild BottomEdge stations so 30°/taller rafters stay inside the roof.
        // Host −1000/−500/+500 is covered by HostFixture_* and seating theories at 45°/125.
        var wallRel = pitchDegrees >= 40d && Math.Abs(rafterHeightMm - 125d) < 0.01d
            ? -1000d
            : 0d;
        var int1Rel = wallRel == -1000d ? -500d : 250d;
        var int2Rel = wallRel == -1000d ? 500d : 500d;
        var layout = new RoofAutomaticPurlinLayout(true,
        [
            new(
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                int1Rel,
                SeatingDepth: seating,
                WidthMm: 160d,
                HeightMm: 220d),
            new(
                "cccccccccccccccccccccccccccccccc",
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                int2Rel,
                SeatingDepth: seating,
                WidthMm: 160d,
                HeightMm: 220d),
        ])
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                wallRel,
                SeatingDepth: seating,
                WidthMm: 140d,
                HeightMm: 140d),
            RidgeWidthMm = 160d,
            RidgeHeightMm = 220d,
            RidgeSeatingDepth = seating,
        };
        var datumLocalZ = wallRel == -1000d ? 1000d : 0d;
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            datumLocalZ);
        var planResult = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(datum, 220d, rafterHeightMm)
            {
                PurlinWidthMm = 160d,
                WallPlatesEnabled = true,
                WallPlateWidthMm = 140d,
                WallPlateHeightMm = 140d,
            });
        Assert.True(planResult.IsValid, planResult.Error.ToString());
        var plan = Assert.IsType<RoofAutomaticPurlinPlan>(planResult.Plan);
        var culture = CultureInfo.GetCultureInfo("en");
        return AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            rafterHeightMm,
            100d,
            datum,
            layout);
    }

    private static double EdgeZ(
        AutomaticPurlinSectionPointMm a,
        AutomaticPurlinSectionPointMm b,
        double xMm)
    {
        var dx = b.XMm - a.XMm;
        if (Math.Abs(dx) <= 0.01d)
        {
            return (a.ZMm + b.ZMm) / 2d;
        }

        return a.ZMm + (xMm - a.XMm) / dx * (b.ZMm - a.ZMm);
    }

    private static SolvedFixture Solve(double pitchDegrees)
    {
        var points = new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000) };
        var input = new RoofFootprintInput(points, true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid);
        var identity = RoofBoundaryIdentityRules.Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            normalized.EdgeProvenance.Count,
            RoofBoundaryIdentityRules.FormatWinding(normalized.Validation.SourceOrientation),
            Enumerable.Range(1, normalized.EdgeProvenance.Count).ToArray()).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        var geometry = HipRoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(pitchDegrees),
            RoofKind.Hip));
        Assert.True(geometry.IsValid);
        return new SolvedFixture(Assert.IsType<HipRoofGeometry>(geometry.Geometry), provenance);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
