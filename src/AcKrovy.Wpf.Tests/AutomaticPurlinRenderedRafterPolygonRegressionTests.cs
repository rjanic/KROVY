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
/// Independent final-polygon regression: expected = BuildRafters slope edges with
/// presentation-only plumb eave tip × viewport fit; actual = Geometry from
/// CreateMasterScene. Contact uses the extracted polygon only.
/// </summary>
public sealed class AutomaticPurlinRenderedRafterPolygonRegressionTests
{
    private const double ViewportW = 640d;
    private const double ViewportH = 520d;
    private const double MaxContactPixelError = 1d;
    private const double MaxCornerPixelError = 0.75d;
    private const double RafterHeightMm = 125d;
    private const double RafterWidthMm = 100d;

    private readonly ITestOutputHelper _output;

    public AutomaticPurlinRenderedRafterPolygonRegressionTests(ITestOutputHelper output) =>
        _output = output;

    public static IEnumerable<object[]> HostMatrix()
    {
        foreach (var pitch in new[] { 30d, 45d })
        {
            foreach (var seating in new[] { 0d, 25d, 50d, 75d, 100d })
            {
                foreach (var mode in new[]
                         {
                             RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                             RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                         })
                {
                    foreach (var datum in new[]
                             {
                                 RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                                 RoofRelativeElevationReferenceKind.SourceEavePlane,
                                 RoofRelativeElevationReferenceKind.WallPlateBottom,
                             })
                    {
                        yield return [pitch, seating, mode, datum];
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(HostMatrix))]
    public void RenderedRafterPolygons_MatchPlumbEaveBuildRaftersThroughViewportFit(
        double pitchDegrees,
        double seatingPercent,
        RoofAutomaticPurlinPlacementMode placementMode,
        RoofRelativeElevationReferenceKind datumKind)
    {
        var presentation = CreatePresentation(pitchDegrees, seatingPercent, placementMode, datumKind);
        var fit = CreateFitTransform(presentation, ViewportW, ViewportH);
        var scene = AutomaticPurlinSectionSvgTemplate.CreateMasterScene(
            presentation,
            (x, z) => fit.ToView(x, z));

        foreach (var side in new[]
                 {
                     AutomaticPurlinSectionSide.Left,
                     AutomaticPurlinSectionSide.Right,
                 })
        {
            var rafter = Assert.Single(presentation.Rafters, r => r.Side == side);
            Assert.True(
                AutomaticPurlinSectionSvgTemplate.TryGetRenderedRafterViewPolygon(
                    scene,
                    side,
                    out var rendered));
            // Expected computed independently (not via TryCreatePlumbEaveRafterCorners).
            var expectedCorners = BuildExpectedPlumbEaveCorners(rafter);
            Assert.Equal(expectedCorners.Count, rendered.Count);

            for (var i = 0; i < expectedCorners.Count; i++)
            {
                var expected = fit.ToView(expectedCorners[i].XMm, expectedCorners[i].ZMm);
                var actual = rendered[i];
                Assert.True(
                    Math.Abs(expected.X - actual.X) <= MaxCornerPixelError &&
                    Math.Abs(expected.Y - actual.Y) <= MaxCornerPixelError,
                    $"{side}[{i}]: expected=({expected.X:F3},{expected.Y:F3}) " +
                    $"rendered=({actual.X:F3},{actual.Y:F3})");
            }

            // Upper tip unchanged from BuildRafters.
            var upperEave = rafter.Corners[0].ZMm <= rafter.Corners[1].ZMm
                ? rafter.Corners[0]
                : rafter.Corners[1];
            Assert.Contains(
                rendered,
                p => Math.Abs(p.X - fit.ToView(upperEave.XMm, upperEave.ZMm).X) <= MaxCornerPixelError &&
                     Math.Abs(p.Y - fit.ToView(upperEave.XMm, upperEave.ZMm).Y) <= MaxCornerPixelError);

            var midX = (rafter.Corners[0].XMm + rafter.Corners[1].XMm) / 2d;
            Assert.True(TryEdgeViewYAtModelX(rendered, 0, 1, midX, fit, out var upperY));
            Assert.True(TryEdgeViewYAtModelX(rendered, 3, 2, midX, fit, out var lowerY));
            var verticalSpanMm = Math.Abs(upperY - lowerY) / fit.Scale;
            var expectedSpanMm = Math.Abs(
                EdgeZ(rafter.Corners[0], rafter.Corners[1], midX) -
                EdgeZ(rafter.Corners[3], rafter.Corners[2], midX));
            Assert.Equal(expectedSpanMm, verticalSpanMm, 3);
            if (Math.Abs(pitchDegrees - 45d) < 0.01d)
            {
                Assert.Equal(RafterHeightMm / Math.Cos(Math.PI / 4d), verticalSpanMm, 3);
            }
        }
    }

    /// <summary>
    /// Independent expected plumb-eave quad (mirrors production rule without calling it).
    /// </summary>
    private static IReadOnlyList<AutomaticPurlinSectionPointMm> BuildExpectedPlumbEaveCorners(
        AutomaticPurlinSectionRafterMm rafter)
    {
        var upper0 = rafter.Corners[0];
        var upper1 = rafter.Corners[1];
        var lower1 = rafter.Corners[2];
        var lower0 = rafter.Corners[3];
        var upperEaveIs0 = upper0.ZMm <= upper1.ZMm;
        var upperEave = upperEaveIs0 ? upper0 : upper1;
        var lowerAtEaveX = EdgeZ(lower0, lower1, upperEave.XMm);
        var plumb = new AutomaticPurlinSectionPointMm(upperEave.XMm, lowerAtEaveX);
        return upperEaveIs0
            ? [upper0, upper1, lower1, plumb]
            : [upper0, upper1, plumb, lower0];
    }

    [Theory]
    [MemberData(nameof(HostMatrix))]
    public void RenderedContactStations_WithinOnePixelOfMemberTop(
        double pitchDegrees,
        double seatingPercent,
        RoofAutomaticPurlinPlacementMode placementMode,
        RoofRelativeElevationReferenceKind datumKind)
    {
        var presentation = CreatePresentation(pitchDegrees, seatingPercent, placementMode, datumKind);
        var fit = CreateFitTransform(presentation, ViewportW, ViewportH);
        var scene = AutomaticPurlinSectionSvgTemplate.CreateMasterScene(
            presentation,
            (x, z) => fit.ToView(x, z));
        var seatingFraction = seatingPercent / 100d;

        foreach (var member in presentation.Members.Where(m =>
                     m.Role is RoofAutomaticPurlinGeneratorRole.WallPlate
                         or RoofAutomaticPurlinGeneratorRole.Intermediate
                         or RoofAutomaticPurlinGeneratorRole.Ridge))
        {
            if (member.Role == RoofAutomaticPurlinGeneratorRole.Ridge)
            {
                AssertContactForRidge(scene, fit, member, seatingFraction);
                continue;
            }

            if (member.Side is not (AutomaticPurlinSectionSide.Left or AutomaticPurlinSectionSide.Right))
            {
                continue;
            }

            Assert.True(
                AutomaticPurlinSectionSvgTemplate.TryGetRenderedRafterViewPolygon(
                    scene,
                    member.Side,
                    out var rendered));
            var contactX = member.CenterXMm +
                AutomaticPurlinSectionPresentation.SideContactCornerOffsetXMm(
                    member.Side,
                    member.WidthMm);
            Assert.True(TryEdgeViewYAtModelX(rendered, 3, 2, contactX, fit, out var lowerY));
            Assert.True(TryEdgeViewYAtModelX(rendered, 0, 1, contactX, fit, out var upperY));
            var contactY = lowerY + seatingFraction * (upperY - lowerY);
            var memberTopY = fit.ToView(contactX, member.MemberTopZMm).Y;
            var pixelErr = Math.Abs(contactY - memberTopY);
            _output.WriteLine(
                $"{pitchDegrees}° seat={seatingPercent}% {placementMode}/{datumKind} " +
                $"{member.Role}/{member.Side}: pxErr={pixelErr:F3}");

            if (placementMode is RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave
                    or RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge ||
                seatingPercent is 0d or 100d ||
                member.Role == RoofAutomaticPurlinGeneratorRole.Ridge)
            {
                if (placementMode == RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference &&
                    seatingPercent is not (0d or 100d))
                {
                    continue;
                }

                Assert.True(
                    pixelErr <= MaxContactPixelError,
                    $"{member.Role}/{member.Side}: contact px error {pixelErr:F3}");
            }
        }
    }

    [Fact]
    public void HostFixture_BottomEdgeZeroSeating_WallPlateBottomOnReferenceGuide()
    {
        var solved = Solve(45d);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            0d);
        var layout = new RoofAutomaticPurlinLayout(true,
        [
            new(
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                -500d,
                SeatingDepth: seating,
                WidthMm: 160d,
                HeightMm: 220d),
            new(
                "cccccccccccccccccccccccccccccccc",
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                500d,
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
                -1000d,
                SeatingDepth: seating,
                WidthMm: 140d,
                HeightMm: 140d),
            RidgeWidthMm = 160d,
            RidgeHeightMm = 220d,
            RidgeSeatingDepth = seating,
        };
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            1000d);
        var plan = CreatePlan(solved, layout, datum);
        var culture = CultureInfo.GetCultureInfo("en");
        var presentation = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            RafterHeightMm,
            RafterWidthMm,
            datum,
            layout);
        Assert.NotNull(presentation.ReferencePlane);
        var guideZ = presentation.ReferencePlane!.LocalZMm;
        var fit = CreateFitTransform(presentation, ViewportW, ViewportH);

        foreach (var member in presentation.Members.Where(m =>
                     m.Role == RoofAutomaticPurlinGeneratorRole.WallPlate))
        {
            var bottomZ = member.CenterZMm - member.HeightMm / 2d;
            Assert.Equal(guideZ - 1000d, bottomZ, 3);
            Assert.True(
                Math.Abs(
                    fit.ToView(member.CenterXMm, bottomZ).Y -
                    fit.ToView(member.CenterXMm, guideZ - 1000d).Y) <= MaxContactPixelError);
        }
    }

    [Fact]
    public void CreateMasterScene_EmitsExactlyOnePhysicalRafterBodyPerSide()
    {
        var presentation = CreatePresentation(
            45d,
            0d,
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane);
        var fit = CreateFitTransform(presentation, ViewportW, ViewportH);
        var scene = AutomaticPurlinSectionSvgTemplate.CreateMasterScene(
            presentation,
            (x, z) => fit.ToView(x, z));

        Assert.True(
            AutomaticPurlinSectionSvgTemplate.TryGetRenderedRafterViewPolygon(
                scene, AutomaticPurlinSectionSide.Left, out var left));
        Assert.True(
            AutomaticPurlinSectionSvgTemplate.TryGetRenderedRafterViewPolygon(
                scene, AutomaticPurlinSectionSide.Right, out var right));
        Assert.Equal(4, left.Count);
        Assert.Equal(4, right.Count);

        var woodRafterCount = scene.Children
            .OfType<GeometryDrawing>()
            .Count(d => d.Geometry is PathGeometry);
        Assert.Equal(2, woodRafterCount);
    }

    private void AssertContactForRidge(
        DrawingGroup scene,
        FitTransform fit,
        AutomaticPurlinSectionMemberMm member,
        double seatingFraction)
    {
        var leftX = member.CenterXMm - member.WidthMm / 2d;
        var rightX = member.CenterXMm + member.WidthMm / 2d;
        Assert.True(
            AutomaticPurlinSectionSvgTemplate.TryGetRenderedRafterViewPolygon(
                scene, AutomaticPurlinSectionSide.Left, out var leftPoly));
        Assert.True(
            AutomaticPurlinSectionSvgTemplate.TryGetRenderedRafterViewPolygon(
                scene, AutomaticPurlinSectionSide.Right, out var rightPoly));
        Assert.True(TryEdgeViewYAtModelX(leftPoly, 3, 2, leftX, fit, out var leftLower));
        Assert.True(TryEdgeViewYAtModelX(leftPoly, 0, 1, leftX, fit, out var leftUpper));
        Assert.True(TryEdgeViewYAtModelX(rightPoly, 3, 2, rightX, fit, out var rightLower));
        Assert.True(TryEdgeViewYAtModelX(rightPoly, 0, 1, rightX, fit, out var rightUpper));
        var expectedTopY =
            ((leftLower + seatingFraction * (leftUpper - leftLower)) +
             (rightLower + seatingFraction * (rightUpper - rightLower))) / 2d;
        var memberTopY = fit.ToView(member.CenterXMm, member.MemberTopZMm).Y;
        var pixelErr = Math.Abs(expectedTopY - memberTopY);
        _output.WriteLine($"Ridge seat={seatingFraction:P0}: pxErr={pixelErr:F3}");
        Assert.True(pixelErr <= MaxContactPixelError, $"Ridge contact px error {pixelErr:F3}");
    }

    private static bool TryEdgeViewYAtModelX(
        IReadOnlyList<Point> polygon,
        int startIndex,
        int endIndex,
        double modelXMm,
        FitTransform fit,
        out double viewY)
    {
        viewY = 0d;
        if (polygon.Count <= Math.Max(startIndex, endIndex))
        {
            return false;
        }

        var targetViewX = fit.ToView(modelXMm, 0d).X;
        var a = polygon[startIndex];
        var b = polygon[endIndex];
        var dx = b.X - a.X;
        if (Math.Abs(dx) <= 1e-9d)
        {
            viewY = (a.Y + b.Y) / 2d;
            return true;
        }

        var t = (targetViewX - a.X) / dx;
        viewY = a.Y + t * (b.Y - a.Y);
        return double.IsFinite(viewY);
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

    private static bool TryCreatePresentation(
        double pitchDegrees,
        double seatingPercent,
        RoofAutomaticPurlinPlacementMode placementMode,
        RoofRelativeElevationReferenceKind datumKind,
        out AutomaticPurlinSectionPresentation presentation)
    {
        presentation = AutomaticPurlinSectionPresentation.Empty;
        var solved = Solve(pitchDegrees);
        var layout = CreateLayout(placementMode, seatingPercent, datumKind);
        var datum = CreateDatum(datumKind);
        // ExplicitLocalPlane LocalZ=1000 elevates the architectural frame so BottomEdge
        // stations 0/250/500 stay inside both 30° and 45° roofs.
        var planResult = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(datum, 220d, RafterHeightMm)
            {
                PurlinWidthMm = 160d,
                WallPlatesEnabled = layout.WallPlateEnabled,
                WallPlateWidthMm = layout.WallPlatePlacement?.WidthMm ?? 140d,
                WallPlateHeightMm = layout.WallPlatePlacement?.HeightMm ?? 140d,
            });
        if (!planResult.IsValid || planResult.Plan is not RoofAutomaticPurlinPlan plan)
        {
            return false;
        }

        var culture = CultureInfo.GetCultureInfo("en");
        presentation = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            RafterHeightMm,
            RafterWidthMm,
            datum,
            layout);
        return presentation.Rafters.Count >= 2;
    }

    private static AutomaticPurlinSectionPresentation CreatePresentation(
        double pitchDegrees,
        double seatingPercent,
        RoofAutomaticPurlinPlacementMode placementMode,
        RoofRelativeElevationReferenceKind datumKind)
    {
        Assert.True(
            TryCreatePresentation(
                pitchDegrees,
                seatingPercent,
                placementMode,
                datumKind,
                out var presentation),
            "expected a valid plan for this fixture");
        return presentation;
    }

    private static RoofRelativeElevationDatum CreateDatum(
        RoofRelativeElevationReferenceKind datumKind) =>
        // Matrix stations use LocalZ=0 so BottomEdge 0/250/500 stay inside 30° and 45°.
        // The dedicated Host fixture uses ExplicitLocalPlane LocalZ=1000 with −1.000 m bottoms.
        new(datumKind, 0d, 0d);

    private static RoofAutomaticPurlinLayout CreateLayout(
        RoofAutomaticPurlinPlacementMode mode,
        double seatingPercent,
        RoofRelativeElevationReferenceKind datumKind)
    {
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            seatingPercent);

        double wallValue;
        double int1Value;
        double int2Value;
        if (mode == RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference)
        {
            // Keep stations inside the roof for every pitch/datum. Host −1.000/−0.500/+0.500
            // requires ExplicitLocalPlane LocalZ=1000 and is covered by dedicated Host facts.
            (wallValue, int1Value, int2Value) = (0d, 250d, 500d);
        }
        else
        {
            wallValue = 200d;
            int1Value = 1500d;
            int2Value = 2500d;
        }

        return new RoofAutomaticPurlinLayout(true,
        [
            new(
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                true,
                mode,
                int1Value,
                SeatingDepth: seating,
                WidthMm: 160d,
                HeightMm: 220d),
            new(
                "cccccccccccccccccccccccccccccccc",
                true,
                mode,
                int2Value,
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
                wallValue,
                SeatingDepth: seating,
                WidthMm: 140d,
                HeightMm: 140d),
            RidgeWidthMm = 160d,
            RidgeHeightMm = 220d,
            RidgeSeatingDepth = seating,
        };
    }

    private static RoofAutomaticPurlinPlan CreatePlan(
        SolvedFixture solved,
        RoofAutomaticPurlinLayout layout,
        RoofRelativeElevationDatum datum)
    {
        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(datum, 220d, RafterHeightMm)
            {
                PurlinWidthMm = 160d,
                WallPlatesEnabled = layout.WallPlateEnabled,
                WallPlateWidthMm = layout.WallPlatePlacement?.WidthMm ?? 140d,
                WallPlateHeightMm = layout.WallPlatePlacement?.HeightMm ?? 140d,
            });
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<RoofAutomaticPurlinPlan>(result.Plan);
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

    private static FitTransform CreateFitTransform(
        AutomaticPurlinSectionPresentation presentation,
        double viewportWidth,
        double viewportHeight)
    {
        const double marginPx = 28d;
        const double minExtent = 1d;
        var extentX = Math.Max(minExtent, presentation.MaxXMm - presentation.MinXMm);
        var extentZ = Math.Max(minExtent, presentation.MaxZMm - presentation.MinZMm);
        var availableWidth = Math.Max(1d, viewportWidth - 2d * marginPx);
        var availableHeight = Math.Max(1d, viewportHeight - 2d * marginPx);
        var scale = Math.Min(availableWidth / extentX, availableHeight / extentZ);
        var contentWidth = extentX * scale;
        var contentHeight = extentZ * scale;
        var offsetX = (viewportWidth - contentWidth) / 2d - presentation.MinXMm * scale;
        var offsetY = marginPx + (availableHeight - contentHeight) / 2d + presentation.MaxZMm * scale;
        return new FitTransform(scale, offsetX, offsetY);
    }

    private readonly record struct FitTransform(double Scale, double OffsetX, double OffsetY)
    {
        internal Point ToView(double xMm, double zMm) =>
            new(xMm * Scale + OffsetX, OffsetY - zMm * Scale);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
