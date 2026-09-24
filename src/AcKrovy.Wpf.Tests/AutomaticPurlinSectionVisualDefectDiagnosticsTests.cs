using System.Globalization;
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
/// READ-ONLY diagnostics for Defect A (eave cut) and Defect B (WallPlateBottom).
/// Not a production fix. Does not change expected golden values of other suites.
/// </summary>
public sealed class AutomaticPurlinSectionVisualDefectDiagnosticsTests
{
    private readonly ITestOutputHelper _output;

    public AutomaticPurlinSectionVisualDefectDiagnosticsTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void DefectA_OriginalSvgEaveCut_Versus_BuildRaftersPerpCut()
    {
        // Original SVG left path (absolute):
        // M0.1479,3521.0295
        // → (5656.2574, 370.9147) lower ridge
        // → (5323.4162, 185.5420) upper ridge
        // → (0.1479, 3150.2844) upper eave
        // → (0.1479, 3521.0295) lower eave  << VERTICAL plumb eave face (ΔX=0)
        var svgUpperEave = (0.1479d, 3150.2844d);
        var svgLowerEave = (0.1479d, 3521.0295d);
        var svgEaveDx = svgLowerEave.Item1 - svgUpperEave.Item1;
        var svgEaveDz = svgLowerEave.Item2 - svgUpperEave.Item2;
        _output.WriteLine(
            $"SVG left eave face: upper={svgUpperEave} lower={svgLowerEave} " +
            $"dX={svgEaveDx:F4} dZ={svgEaveDz:F4} (plumb if dX≈0)");

        var presentation = CreatePresentation(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            referenceLocalZMm: 1000d,
            wallBottomRelativeMm: -1000d,
            int1RelativeMm: -500d,
            int2RelativeMm: 500d);
        var left = Assert.Single(
            presentation.Rafters,
            r => r.Side == AutomaticPurlinSectionSide.Left);
        Assert.True(left.Corners.Count >= 4);
        // BuildRafters: [0] upper eave-ish, [1] upper ridge, [2] lower ridge, [3] lower eave-ish
        var u0 = left.Corners[0];
        var u1 = left.Corners[1];
        var l1 = left.Corners[2];
        var l0 = left.Corners[3];
        // Eave termination = the edge connecting the two lower-X upper/lower corners.
        var eaveUpper = u0.XMm <= u1.XMm ? u0 : u1;
        var eaveLower = l0.XMm <= l1.XMm ? l0 : l1;
        // Prefer matching by min X of upper edge start
        eaveUpper = left.Corners[0].XMm <= left.Corners[1].XMm ? left.Corners[0] : left.Corners[1];
        eaveLower = left.Corners[3].XMm <= left.Corners[2].XMm ? left.Corners[3] : left.Corners[2];
        var brDx = eaveLower.XMm - eaveUpper.XMm;
        var brDz = eaveLower.ZMm - eaveUpper.ZMm;
        var perpLen = Math.Sqrt(brDx * brDx + brDz * brDz);
        _output.WriteLine(
            $"BuildRafters left eave face: upper=({eaveUpper.XMm:F3},{eaveUpper.ZMm:F3}) " +
            $"lower=({eaveLower.XMm:F3},{eaveLower.ZMm:F3}) dX={brDx:F3} dZ={brDz:F3} " +
            $"len={perpLen:F3} (perp to axis if ≈125)");
        Assert.Equal(125d, perpLen, 3);

        // Slope edges (seating): upper [0]→[1], lower [3]→[2] — must stay authoritative.
        var midX = (left.Corners[0].XMm + left.Corners[1].XMm) / 2d;
        Assert.True(
            AutomaticPurlinSectionSvgTemplate.TryResolveBuildRaftersLowerUpperLocalZMm(
                presentation.Rafters,
                AutomaticPurlinSectionSide.Left,
                midX,
                out var lo,
                out var hi));
        _output.WriteLine($"mid seating span={hi - lo:F3} (must remain 176.777 @45°)");
        Assert.Equal(125d / Math.Cos(Math.PI / 4d), hi - lo, 3);

        // Proposed presentation-only plumb eave: keep upper/lower slope ends at eave X of
        // upper tip, drop vertical to lower Z at same X (or raise lower X to upper eave X).
        var proposedLowerEave = new AutomaticPurlinSectionPointMm(eaveUpper.XMm, eaveLower.ZMm);
        // But lower Z at upper's X along the lower slope edge:
        AutomaticPurlinSectionSvgTemplate.TryResolveBuildRaftersLowerUpperLocalZMm(
            presentation.Rafters,
            AutomaticPurlinSectionSide.Left,
            eaveUpper.XMm,
            out var lowerAtUpperX,
            out _);
        var proposedPlumbLower = new AutomaticPurlinSectionPointMm(eaveUpper.XMm, lowerAtUpperX);
        _output.WriteLine(
            $"Proposed plumb lower eave: ({proposedPlumbLower.XMm:F3},{proposedPlumbLower.ZMm:F3}) " +
            $"vs current lower eave ({eaveLower.XMm:F3},{eaveLower.ZMm:F3})");
        _output.WriteLine(
            $"Plumb face dX=0, dZ={proposedPlumbLower.ZMm - eaveUpper.ZMm:F3}; " +
            $"slope lower edge at eave X unchanged → seating contact unchanged.");
    }

    [Fact]
    public void DefectB_EquivalentPhysicalLayouts_ThreeDatums_RenderedCoords()
    {
        // Anchor physical layout under ExplicitLocalPlane LocalZ=1000, WP Bottom=-1000,
        // Int1=-500, Int2=+500, seating 0%. Then rebuild WallPlateBottom / SourceEavePlane
        // so Core physical Bottom/Center/Top match the Explicit layout.
        const double rafterH = 125d;
        const double rafterW = 100d;
        var solved = Solve(45d);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            0d);

        var explicitDatum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            1000d);
        var explicitLayout = CreateBottomEdgeLayout(-1000d, -500d, 500d, seating);
        var explicitPlan = CreatePlan(solved, explicitLayout, explicitDatum, rafterH);
        var explicitPres = CreatePresentation(solved, explicitPlan, explicitLayout, explicitDatum, rafterH, rafterW);

        var explicitPhys = CapturePhysical(explicitPlan);
        _output.WriteLine("=== ExplicitLocalPlane physical (authority) ===");
        DumpPhysical(explicitPhys);

        // WallPlateBottom: ReferenceLocalZ must equal WP BottomLocalZ; relatives are offsets
        // from that bottom. For identical physical bottoms:
        //   relative = physicalBottom - wpPhysicalBottom
        var wpBottomPhys = explicitPhys
            .First(p => p.Role == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .BottomLocalZMm;
        var wpDatum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            0d,
            wpBottomPhys);
        var wpLayout = CreateBottomEdgeLayout(
            relativeMm: 0d, // WP bottom = reference
            int1RelativeMm: explicitPhys.First(p => p.Role == RoofAutomaticPurlinGeneratorRole.Intermediate && p.BottomLocalZMm < 1000).BottomLocalZMm - wpBottomPhys,
            int2RelativeMm: explicitPhys.Where(p => p.Role == RoofAutomaticPurlinGeneratorRole.Intermediate).Max(p => p.BottomLocalZMm) - wpBottomPhys,
            seating);
        // Fix int relatives more carefully from unique bottoms
        var ints = explicitPhys
            .Where(p => p.Role == RoofAutomaticPurlinGeneratorRole.Intermediate)
            .OrderBy(p => p.BottomLocalZMm)
            .ToList();
        wpLayout = CreateBottomEdgeLayout(
            0d,
            ints[0].BottomLocalZMm - wpBottomPhys,
            ints[1].BottomLocalZMm - wpBottomPhys,
            seating);
        var wpPlan = CreatePlan(solved, wpLayout, wpDatum, rafterH);
        var wpPres = CreatePresentation(solved, wpPlan, wpLayout, wpDatum, rafterH, rafterW);
        var wpPhys = CapturePhysical(wpPlan);

        // SourceEavePlane: ReferenceLocalZ=0; relative = physicalBottom - 0
        var seDatum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.SourceEavePlane,
            0d,
            0d);
        var seLayout = CreateBottomEdgeLayout(
            wpBottomPhys - 0d,
            ints[0].BottomLocalZMm,
            ints[1].BottomLocalZMm,
            seating);
        var sePlan = CreatePlan(solved, seLayout, seDatum, rafterH);
        var sePres = CreatePresentation(solved, sePlan, seLayout, seDatum, rafterH, rafterW);
        var sePhys = CapturePhysical(sePlan);

        _output.WriteLine("=== Physical equivalence check (BottomLocalZ) ===");
        ComparePhysical(explicitPhys, wpPhys, "Explicit vs WallPlateBottom");
        ComparePhysical(explicitPhys, sePhys, "Explicit vs SourceEave");

        _output.WriteLine("=== Schematic member CenterZ / Top / guide ===");
        DumpSchematic("Explicit", explicitPres);
        DumpSchematic("WallPlateBottom", wpPres);
        DumpSchematic("SourceEave", sePres);

        _output.WriteLine("=== Rendered member rects + rafter polygons (UnitToView) ===");
        DumpRendered("Explicit", explicitPres);
        DumpRendered("WallPlateBottom", wpPres);
        DumpRendered("SourceEave", sePres);

        // First mismatch: schematic CenterZ delta between Explicit and WallPlateBottom
        // for the same physical BottomLocalZ.
        foreach (var role in new[]
                 {
                     RoofAutomaticPurlinGeneratorRole.WallPlate,
                     RoofAutomaticPurlinGeneratorRole.Intermediate,
                     RoofAutomaticPurlinGeneratorRole.Ridge,
                 })
        {
            var a = explicitPres.Members.Where(m => m.Role == role).OrderBy(m => m.CenterXMm).ToList();
            var b = wpPres.Members.Where(m => m.Role == role).OrderBy(m => m.CenterXMm).ToList();
            if (a.Count == 0 || b.Count == 0)
            {
                continue;
            }

            for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
            {
                var dCenter = a[i].CenterZMm - b[i].CenterZMm;
                var dTop = a[i].MemberTopZMm - b[i].MemberTopZMm;
                _output.WriteLine(
                    $"MISMATCH? {role}[{i}] Explicit.CenterZ={a[i].CenterZMm:F3} " +
                    $"WPBottom.CenterZ={b[i].CenterZMm:F3} ΔCenter={dCenter:F3} ΔTop={dTop:F3}");
            }
        }

        // HOST-like: switch datum while keeping PlacementValueMm text unchanged.
        var vm = new AutomaticPurlinDialogViewModel(
            solved.Geometry,
            solved.Provenance,
            explicitLayout,
            true,
            explicitDatum,
            true,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            TimberElementDefaults.For(TimberElementType.Rafter),
            CultureInfo.GetCultureInfo("en"),
            AutomaticPurlinDialogMode.ProductionEdit,
            existingAutomaticPurlinCount: 0);
        vm.WallPlateEnabled = true;
        Assert.True(vm.TryGetPreviewPlan(out var planBefore));
        var beforeWp = planBefore!.Items.First(i =>
            i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
        _output.WriteLine(
            $"HOST-like BEFORE Explicit: WP BotLocal={beforeWp.ElevationProfile!.BottomLocalZMm:F3} " +
            $"BotRel={beforeWp.ElevationProfile.BottomRelativeElevationMm:F3} " +
            $"PlaceText={vm.WallPlateRow.PlacementValueText}");

        vm.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        var draftOk = vm.TryCreateDraft(out var draftErr, out var afterDatum);
        var previewOk = vm.TryGetPreviewPlan(out var planAfter);
        _output.WriteLine(
            $"HOST-like AFTER switch: draftOk={draftOk} draftErr={draftErr} " +
            $"previewOk={previewOk} PlaceText={vm.WallPlateRow.PlacementValueText} " +
            $"Int0Place={vm.Rows.FirstOrDefault(r => r.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate)?.PlacementValueText}");
        if (afterDatum is not null)
        {
            _output.WriteLine($"  datum.RefLocalZ={afterDatum.ReferenceLocalZMm:F3}");
        }

        if (previewOk && planAfter is not null)
        {
            var afterWp = planAfter.Items.First(i =>
                i.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate);
            _output.WriteLine(
                $"  WP BotLocal={afterWp.ElevationProfile!.BottomLocalZMm:F3} " +
                $"BotRel={afterWp.ElevationProfile.BottomRelativeElevationMm:F3} " +
                $"ΔBotLocal={afterWp.ElevationProfile.BottomLocalZMm - beforeWp.ElevationProfile!.BottomLocalZMm:F3}");
            foreach (var role in new[]
                     {
                         RoofAutomaticPurlinGeneratorRole.Intermediate,
                         RoofAutomaticPurlinGeneratorRole.Ridge,
                     })
            {
                var bItems = planBefore!.Items.Where(i => i.GeneratorRole == role)
                    .OrderBy(i => i.ElevationProfile!.BottomLocalZMm).ToList();
                var aItems = planAfter.Items.Where(i => i.GeneratorRole == role)
                    .OrderBy(i => i.ElevationProfile!.BottomLocalZMm).ToList();
                for (var i = 0; i < Math.Min(bItems.Count, aItems.Count); i++)
                {
                    _output.WriteLine(
                        $"  {role}[{i}] beforeBot={bItems[i].ElevationProfile!.BottomLocalZMm:F3} " +
                        $"afterBot={aItems[i].ElevationProfile!.BottomLocalZMm:F3} " +
                        $"Δ={aItems[i].ElevationProfile!.BottomLocalZMm - bItems[i].ElevationProfile!.BottomLocalZMm:F3}");
                }
            }
        }

        // Retained schematic after invalid draft (presentation retain path).
        var retained = vm.SectionPresentation;
        _output.WriteLine(
            $"  Retained schematic guide={retained.ReferencePlane?.LocalZMm:F3} " +
            $"kind={retained.ReferencePlane?.Kind} memberCount={retained.Members.Count}");
    }

    private void DumpSchematic(string label, AutomaticPurlinSectionPresentation p)
    {
        _output.WriteLine($"-- {label} schematic --");
        _output.WriteLine(
            $"  ReferencePlane LocalZ={p.ReferencePlane?.LocalZMm:F3} " +
            $"VisualZ={p.ReferencePlane?.VisualLocalZMm:F3} Kind={p.ReferencePlane?.Kind}");
        foreach (var m in p.Members.OrderBy(m => m.Role).ThenBy(m => m.CenterXMm))
        {
            _output.WriteLine(
                $"  {m.Role}/{m.Side}: CX={m.CenterXMm:F3} CZ={m.CenterZMm:F3} " +
                $"Top={m.MemberTopZMm:F3} Bot={m.CenterZMm - m.HeightMm / 2d:F3}");
        }
    }

    private void DumpRendered(string label, AutomaticPurlinSectionPresentation p)
    {
        static System.Windows.Point ToView(double x, double z) => new(x, -z);
        var scene = AutomaticPurlinSectionSvgTemplate.CreateMasterScene(p, ToView);
        _output.WriteLine($"-- {label} rendered (Y=-Z) --");
        foreach (var side in new[] { AutomaticPurlinSectionSide.Left, AutomaticPurlinSectionSide.Right })
        {
            if (!AutomaticPurlinSectionSvgTemplate.TryGetRenderedRafterViewPolygon(scene, side, out var poly))
            {
                continue;
            }

            _output.WriteLine(
                $"  rafter {side}: " +
                string.Join(" ", poly.Select(pt => $"({pt.X:F1},{pt.Y:F1})")));
        }

        foreach (var m in p.Members.Where(m => m.Role == RoofAutomaticPurlinGeneratorRole.WallPlate)
                     .OrderBy(m => m.CenterXMm))
        {
            var topLeft = ToView(m.CenterXMm - m.WidthMm / 2d, m.MemberTopZMm);
            var bot = ToView(m.CenterXMm, m.CenterZMm - m.HeightMm / 2d);
            _output.WriteLine(
                $"  WP {m.Side} rect topY={topLeft.Y:F3} botY={bot.Y:F3} " +
                $"(model Top={m.MemberTopZMm:F3} Bot={m.CenterZMm - m.HeightMm / 2d:F3})");
        }
    }

    private void DumpPhysical(List<Phys> rows)
    {
        foreach (var r in rows.OrderBy(r => r.Role).ThenBy(r => r.BottomLocalZMm))
        {
            _output.WriteLine(
                $"  {r.Role}: Bot={r.BottomLocalZMm:F3} Cen={r.CenterLocalZMm:F3} Top={r.TopLocalZMm:F3}");
        }
    }

    private void ComparePhysical(List<Phys> a, List<Phys> b, string label)
    {
        _output.WriteLine($"-- {label} --");
        foreach (var role in a.Select(x => x.Role).Distinct())
        {
            var aa = a.Where(x => x.Role == role).OrderBy(x => x.BottomLocalZMm).ToList();
            var bb = b.Where(x => x.Role == role).OrderBy(x => x.BottomLocalZMm).ToList();
            for (var i = 0; i < Math.Min(aa.Count, bb.Count); i++)
            {
                _output.WriteLine(
                    $"  {role}[{i}] ΔBot={aa[i].BottomLocalZMm - bb[i].BottomLocalZMm:F3} " +
                    $"ΔCen={aa[i].CenterLocalZMm - bb[i].CenterLocalZMm:F3} " +
                    $"ΔTop={aa[i].TopLocalZMm - bb[i].TopLocalZMm:F3}");
            }
        }
    }

    private static List<Phys> CapturePhysical(RoofAutomaticPurlinPlan plan) =>
        plan.Items
            .Where(i => i.ElevationProfile is not null)
            .GroupBy(i => (i.GeneratorRole, Math.Round(i.ElevationProfile!.BottomLocalZMm, 3)))
            .Select(g => g.First())
            .Select(i => new Phys(
                i.GeneratorRole,
                i.ElevationProfile!.BottomLocalZMm,
                i.ElevationProfile.CenterLocalZMm,
                i.ElevationProfile.TopLocalZMm))
            .ToList();

    private AutomaticPurlinSectionPresentation CreatePresentation(
        RoofRelativeElevationReferenceKind kind,
        double referenceLocalZMm,
        double wallBottomRelativeMm,
        double int1RelativeMm,
        double int2RelativeMm)
    {
        var solved = Solve(45d);
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            0d);
        var datum = new RoofRelativeElevationDatum(kind, 0d, referenceLocalZMm);
        var layout = CreateBottomEdgeLayout(wallBottomRelativeMm, int1RelativeMm, int2RelativeMm, seating);
        var plan = CreatePlan(solved, layout, datum, 125d);
        return CreatePresentation(solved, plan, layout, datum, 125d, 100d);
    }

    private static AutomaticPurlinSectionPresentation CreatePresentation(
        SolvedFixture solved,
        RoofAutomaticPurlinPlan plan,
        RoofAutomaticPurlinLayout layout,
        RoofRelativeElevationDatum datum,
        double rafterH,
        double rafterW)
    {
        var culture = CultureInfo.GetCultureInfo("en");
        return AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            rafterH,
            rafterW,
            datum,
            layout);
    }

    private static RoofAutomaticPurlinLayout CreateBottomEdgeLayout(
        double relativeMm,
        double int1RelativeMm,
        double int2RelativeMm,
        RoofAutomaticPurlinSeatingDepth seating) =>
        new(true,
        [
            new(
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                int1RelativeMm,
                SeatingDepth: seating,
                WidthMm: 160d,
                HeightMm: 220d),
            new(
                "cccccccccccccccccccccccccccccccc",
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                int2RelativeMm,
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
                relativeMm,
                SeatingDepth: seating,
                WidthMm: 140d,
                HeightMm: 140d),
            RidgeWidthMm = 160d,
            RidgeHeightMm = 220d,
            RidgeSeatingDepth = seating,
        };

    private static RoofAutomaticPurlinPlan CreatePlan(
        SolvedFixture solved,
        RoofAutomaticPurlinLayout layout,
        RoofRelativeElevationDatum datum,
        double rafterH)
    {
        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(datum, 220d, rafterH)
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

    private sealed record Phys(
        RoofAutomaticPurlinGeneratorRole Role,
        double BottomLocalZMm,
        double CenterLocalZMm,
        double TopLocalZMm);

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
