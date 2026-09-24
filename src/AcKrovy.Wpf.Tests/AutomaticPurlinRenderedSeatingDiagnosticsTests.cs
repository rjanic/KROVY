using System.Globalization;
using System.Windows;
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
/// Post-fix seating diagnostics: rendered BuildRafters polygons must match
/// helper edges and MemberTop at 0% seating (HOST fixture).
/// </summary>
public sealed class AutomaticPurlinRenderedSeatingDiagnosticsTests
{
    private readonly ITestOutputHelper _output;

    public AutomaticPurlinRenderedSeatingDiagnosticsTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void HostFixture_ZeroSeating_RenderedPolygonMatchesBuildRaftersAndMemberTop()
    {
        var presentation = CreateHostPresentation();
        var gaps = new List<(string Role, string Side, double HelperGap, double RenderedGap)>();

        foreach (var member in presentation.Members.Where(m =>
                     m.Role is RoofAutomaticPurlinGeneratorRole.WallPlate
                         or RoofAutomaticPurlinGeneratorRole.Intermediate
                         or RoofAutomaticPurlinGeneratorRole.Ridge))
        {
            if (member.Side is not (AutomaticPurlinSectionSide.Left or AutomaticPurlinSectionSide.Right) &&
                member.Role != RoofAutomaticPurlinGeneratorRole.Ridge)
            {
                continue;
            }

            var contactX = member.Role == RoofAutomaticPurlinGeneratorRole.Ridge
                ? member.CenterXMm
                : member.CenterXMm +
                  AutomaticPurlinSectionPresentation.SideContactCornerOffsetXMm(
                      member.Side,
                      member.WidthMm);
            var side = member.Side == AutomaticPurlinSectionSide.Center
                ? AutomaticPurlinSectionSide.Left
                : member.Side;

            Assert.True(
                AutomaticPurlinSectionSvgTemplate.TryResolveBuildRaftersLowerUpperLocalZMm(
                    presentation.Rafters, side, contactX, out var helperLower, out _));
            Assert.True(
                AutomaticPurlinSectionSvgTemplate.TryResolveSvgMasterRafterLowerUpperLocalZMm(
                    presentation.Rafters, side, contactX, out var renderedLower, out _));

            var helperGap = Math.Abs(helperLower - renderedLower);
            // Ridge Top uses outer-corner average; WP/Int Top == lower at 0%.
            var contactTop = member.Role == RoofAutomaticPurlinGeneratorRole.Ridge
                ? helperLower
                : member.MemberTopZMm;
            var renderedGap = member.Role == RoofAutomaticPurlinGeneratorRole.Ridge
                ? helperGap
                : Math.Abs(member.MemberTopZMm - renderedLower);
            gaps.Add((member.Role.ToString(), member.Side.ToString(), helperGap, renderedGap));

            _output.WriteLine(
                $"{member.Role}/{member.Side}: Top={member.MemberTopZMm:F3} " +
                $"helperLower={helperLower:F3} renderedLower={renderedLower:F3} " +
                $"helperGap={helperGap:F3} renderedGap={renderedGap:F3}");
            _ = contactTop;
        }

        Assert.NotEmpty(gaps);
        Assert.All(gaps, g =>
        {
            Assert.True(g.HelperGap < 1d, $"{g.Role}/{g.Side} helper≠rendered {g.HelperGap}");
            if (g.Role is "WallPlate" or "Intermediate")
            {
                Assert.True(g.RenderedGap < 1d, $"{g.Role}/{g.Side} Top≠rendered {g.RenderedGap}");
            }
        });
    }

    [Fact]
    public void DrawnThickness_MatchesPhysicalSpan_NotLegacySvgDoubleThickness()
    {
        var presentation = CreateHostPresentation();
        var left = Assert.Single(
            presentation.Rafters,
            r => r.Side == AutomaticPurlinSectionSide.Left);
        var x = (left.CenterLine.X1Mm + left.CenterLine.X2Mm) / 2d;
        Assert.True(
            AutomaticPurlinSectionSvgTemplate.TryResolveBuildRaftersLowerUpperLocalZMm(
                presentation.Rafters, AutomaticPurlinSectionSide.Left, x,
                out var helperLower, out var helperUpper));
        Assert.True(
            AutomaticPurlinSectionSvgTemplate.TryResolveSvgMasterRafterLowerUpperLocalZMm(
                presentation.Rafters, AutomaticPurlinSectionSide.Left, x,
                out var renderedLower, out var renderedUpper));

        var helperSpan = helperUpper - helperLower;
        var renderedSpan = renderedUpper - renderedLower;
        _output.WriteLine($"midX={x:F3} helperSpan={helperSpan:F3} renderedSpan={renderedSpan:F3}");
        Assert.Equal(helperSpan, renderedSpan, 3);
        Assert.Equal(125d / Math.Cos(Math.PI / 4d), renderedSpan, 3);
        Assert.True(renderedSpan < 200d, "must not regress to legacy ~358 mm SVG span");
    }

    [Fact]
    public void HostFixture_ZeroSeating_PixelContactErrorAgainstRenderedLowerEdge()
    {
        const double viewportW = 640d;
        const double viewportH = 520d;
        var presentation = CreateHostPresentation();
        var fit = CreateFitTransform(presentation, viewportW, viewportH);
        var scene = AutomaticPurlinSectionSvgTemplate.CreateMasterScene(
            presentation,
            (x, z) => fit.ToView(x, z));

        foreach (var member in presentation.Members.Where(m =>
                     m.Role is RoofAutomaticPurlinGeneratorRole.WallPlate
                         or RoofAutomaticPurlinGeneratorRole.Intermediate))
        {
            if (member.Side is not (AutomaticPurlinSectionSide.Left or AutomaticPurlinSectionSide.Right))
            {
                continue;
            }

            Assert.True(
                AutomaticPurlinSectionSvgTemplate.TryGetRenderedRafterViewPolygon(
                    scene, member.Side, out var polygon));
            var contactX = member.CenterXMm +
                AutomaticPurlinSectionPresentation.SideContactCornerOffsetXMm(
                    member.Side, member.WidthMm);
            var targetViewX = fit.ToView(contactX, 0d).X;
            // Lower edge = corners[3]→[2] in rendered order.
            var a = polygon[3];
            var b = polygon[2];
            var t = Math.Abs(b.X - a.X) <= 1e-9d ? 0.5d : (targetViewX - a.X) / (b.X - a.X);
            var lowerY = a.Y + t * (b.Y - a.Y);
            var contactY = fit.ToView(contactX, member.MemberTopZMm).Y;
            var pixelErr = Math.Abs(contactY - lowerY);
            _output.WriteLine($"{member.Role}/{member.Side}: pxErr={pixelErr:F3}");
            Assert.True(pixelErr <= 1d, $"{member.Role}/{member.Side} pxErr={pixelErr:F3}");
        }
    }

    private static AutomaticPurlinSectionPresentation CreateHostPresentation()
    {
        var solved = Solve(45d);
        var layout = CreateHostLayout();
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            1000d);
        var plan = CreatePlan(solved, layout, 125d, datum);
        return CreatePresentation(solved, plan, layout, 125d, 100d, datum);
    }

    private static RoofAutomaticPurlinLayout CreateHostLayout() =>
        new(true,
        [
            new(
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                -500d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    0d),
                WidthMm: 160d,
                HeightMm: 220d),
            new(
                "cccccccccccccccccccccccccccccccc",
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                500d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    0d),
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
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    0d),
                WidthMm: 140d,
                HeightMm: 140d),
            RidgeWidthMm = 160d,
            RidgeHeightMm = 220d,
            RidgeSeatingDepth = new(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                0d),
        };

    private static RoofAutomaticPurlinPlan CreatePlan(
        SolvedFixture solved,
        RoofAutomaticPurlinLayout layout,
        double rafterHeightMm,
        RoofRelativeElevationDatum datum)
    {
        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(datum, 220d, rafterHeightMm)
            {
                PurlinWidthMm = 160d,
                WallPlatesEnabled = layout.WallPlateEnabled,
                WallPlateWidthMm = layout.WallPlatePlacement?.WidthMm ?? 140d,
                WallPlateHeightMm = layout.WallPlatePlacement?.HeightMm ?? 140d,
            });
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<RoofAutomaticPurlinPlan>(result.Plan);
    }

    private static AutomaticPurlinSectionPresentation CreatePresentation(
        SolvedFixture solved,
        RoofAutomaticPurlinPlan plan,
        RoofAutomaticPurlinLayout layout,
        double rafterHeightMm,
        double rafterWidthMm,
        RoofRelativeElevationDatum datum)
    {
        var culture = CultureInfo.GetCultureInfo("en");
        return AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            rafterHeightMm,
            rafterWidthMm,
            datum,
            layout);
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
