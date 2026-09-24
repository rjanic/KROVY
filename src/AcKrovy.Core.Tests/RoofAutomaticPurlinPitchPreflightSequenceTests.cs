using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// CAD-neutral regression for HOST-proven pitch edit sequence:
/// persisted Intermediate BottomEdgeHeightAboveReference must reject roofs that
/// become too low, without clamping or relocating the configured member.
/// AutoCAD preflight/group atomicity is covered by live-regen source contracts.
/// </summary>
public sealed class RoofAutomaticPurlinPitchPreflightSequenceTests
{
    private const string LayoutId = "cccccccccccccccccccccccccccccccc";
    private const double ConfiguredBottomEdgeMm = 1500d;

    [Fact]
    public void AbsoluteBottomEdge_RejectsLowPitch_AcceptsValidPitch_WithoutLayoutMutation()
    {
        var footprint = Points((0, 0), (10000, 0), (10000, 6000), (0, 6000));
        var layout = AbsoluteBottomEdgeLayout(ConfiguredBottomEdgeMm);
        var layoutBefore = Describe(layout);

        var at45 = PlanAt(footprint, 45d, layout);
        Assert.True(at45.IsValid, at45.Error.ToString());
        Assert.True(at45.Geometry.RiseMm > ConfiguredBottomEdgeMm);

        var to12 = PlanAt(footprint, 12d, layout);
        Assert.False(to12.IsValid);
        Assert.Equal(RoofAutomaticPurlinPlanError.ElevationOutsideRoof, to12.Error);
        Assert.Equal(layoutBefore, Describe(layout));

        var to55 = PlanAt(footprint, 55d, layout);
        Assert.True(to55.IsValid, to55.Error.ToString());
        Assert.True(to55.Geometry.RiseMm > ConfiguredBottomEdgeMm);
        Assert.Equal(layoutBefore, Describe(layout));

        var to20 = PlanAt(footprint, 20d, layout);
        Assert.False(to20.IsValid);
        Assert.Equal(RoofAutomaticPurlinPlanError.ElevationOutsideRoof, to20.Error);
        Assert.Equal(layoutBefore, Describe(layout));

        var backTo45 = PlanAt(footprint, 45d, layout);
        Assert.True(backTo45.IsValid, backTo45.Error.ToString());
        Assert.Equal(
            Keys(Assert.IsType<RoofAutomaticPurlinPlan>(at45.Plan)),
            Keys(Assert.IsType<RoofAutomaticPurlinPlan>(backTo45.Plan)));
        Assert.Equal(layoutBefore, Describe(layout));
    }

    [Fact]
    public void ValidPitchChange_StillProducesDeterministicDesiredSet()
    {
        var footprint = Points((0, 0), (10000, 0), (10000, 6000), (0, 6000));
        var layout = AbsoluteBottomEdgeLayout(ConfiguredBottomEdgeMm);

        var first = PlanAt(footprint, 45d, layout);
        var second = PlanAt(footprint, 55d, layout);
        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.NotEqual(first.Geometry.RiseMm, second.Geometry.RiseMm);
        Assert.Equal(
            Keys(Assert.IsType<RoofAutomaticPurlinPlan>(first.Plan)).Count,
            Keys(Assert.IsType<RoofAutomaticPurlinPlan>(second.Plan)).Count);
    }

    [Fact]
    public void LiveService_MapsElevationOutsideFamilyToPurlinSpecificKey()
    {
        var live = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "AcKrovy.AutoCAD",
            "Infrastructure",
            "RoofAutomaticPurlinLiveRegenerationService.cs"));
        Assert.Contains("LocalizationKeyElevationOutside =", live);
        Assert.Contains("Command_RoofEdit_PurlinElevationOutsideRoof", live);
        Assert.Contains("Command_RoofEdit_PurlinLayoutIncompatible", live);
        Assert.Contains("RoofAutomaticPurlinPlanError.ElevationOutsideRoof", live);
        Assert.Contains("RoofAutomaticPurlinPlanError.ImpossiblePhysicalPlacement", live);
        Assert.Contains("RoofAutomaticPurlinPlanError.CriticalEventElevation", live);
        Assert.Contains("ResolveLocalizationKey(", live);
    }

    [Fact]
    public void RoofEditPurlinLocalizationKeys_ExistInAllLanguagePacks()
    {
        const string elevationKey = "Command_RoofEdit_PurlinElevationOutsideRoof";
        const string layoutKey = "Command_RoofEdit_PurlinLayoutIncompatible";
        var resourceDir = Path.Combine(RepositoryRoot(), "src", "AcKrovy.Localization", "Resources");
        var packs = new[]
        {
            "UiStrings.resx",
            "UiStrings.cs.resx",
            "UiStrings.en.resx",
            "UiStrings.de.resx",
            "UiStrings.pl.resx",
            "UiStrings.fr.resx",
        };

        foreach (var pack in packs)
        {
            var text = File.ReadAllText(Path.Combine(resourceDir, pack));
            Assert.Contains($"name=\"{elevationKey}\"", text, StringComparison.Ordinal);
            Assert.Contains($"name=\"{layoutKey}\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain(
                $"name=\"{elevationKey}\" xml:space=\"preserve\"><value></value>",
                text,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AbsoluteBottomEdge_IndependentRiseCheck_RejectsWhenRiseBelowConfiguredHeight()
    {
        // Independent geometry: half-span 3000 mm → Rise = 3000·tan(pitch).
        // BottomEdgeHeightAboveReference places the roof-plane slice at
        // bottom + purlinHeight/2 (center). With PurlinHeightMm=220 that is 1610 mm,
        // so Rise must exceed 1610 (α > arctan(1610/3000) ≈ 28.23°).
        var footprint = Points((0, 0), (10000, 0), (10000, 6000), (0, 6000));
        var layout = AbsoluteBottomEdgeLayout(ConfiguredBottomEdgeMm);
        const double halfSpanMm = 3000d;
        const double purlinHeightMm = 220d;
        var requiredSliceMm = ConfiguredBottomEdgeMm + purlinHeightMm / 2d;

        var shallow = PlanAt(footprint, 20d, layout);
        Assert.False(shallow.IsValid);
        Assert.Equal(RoofAutomaticPurlinPlanError.ElevationOutsideRoof, shallow.Error);
        Assert.Equal(halfSpanMm * Math.Tan(20d * Math.PI / 180d), shallow.Geometry.RiseMm, 6);
        Assert.True(shallow.Geometry.RiseMm < requiredSliceMm);

        var steepEnough = PlanAt(footprint, 35d, layout);
        Assert.True(steepEnough.IsValid, steepEnough.Error.ToString());
        Assert.Equal(halfSpanMm * Math.Tan(35d * Math.PI / 180d), steepEnough.Geometry.RiseMm, 6);
        Assert.True(steepEnough.Geometry.RiseMm > requiredSliceMm);
    }

    private static Planned PlanAt(
        RoofPoint2D[] points,
        double pitchDegrees,
        RoofAutomaticPurlinLayout layout)
    {
        var input = new RoofFootprintInput(points, IsClosed: true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid);
        var identity = RoofBoundaryIdentityRules.Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            normalized.EdgeProvenance.Count,
            RoofBoundaryIdentityRules.FormatWinding(normalized.Validation.SourceOrientation),
            Enumerable.Range(1, normalized.EdgeProvenance.Count).ToArray()).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        Assert.True(provenance.IsValid);
        var geometryResult = HipRoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(pitchDegrees),
            RoofKind.Hip));
        Assert.True(geometryResult.IsValid, geometryResult.Error.ToString());
        var geometry = Assert.IsType<HipRoofGeometry>(geometryResult.Geometry);
        var planned = RoofAutomaticPurlinPlanner.Create(
            geometry,
            provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(
                new RoofRelativeElevationDatum(
                    RoofRelativeElevationReferenceKind.SourceEavePlane,
                    0d,
                    0d),
                PurlinHeightMm: 220d,
                RafterHeightMm: 160d)
            {
                PurlinWidthMm = 160d,
            });
        return new Planned(geometry, planned.IsValid, planned.Plan, planned.Error);
    }

    private static RoofAutomaticPurlinLayout AbsoluteBottomEdgeLayout(double bottomEdgeMm) =>
        new(false,
        [
            new RoofAutomaticPurlinLayoutItem(
                LayoutId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                bottomEdgeMm),
        ]);

    private static string Describe(RoofAutomaticPurlinLayout layout) =>
        string.Join("|", layout.IntermediateItems.Select(item =>
            string.Join(
                ":",
                item.LayoutItemId,
                item.Enabled,
                item.PlacementMode,
                item.PlacementValueMm.ToString("R"))));

    private static IReadOnlyList<string> Keys(RoofAutomaticPurlinPlan plan) =>
        plan.Items.Select(item => item.GeneratedKey.ToString()).OrderBy(v => v).ToArray();

    private static RoofPoint2D[] Points(params (double X, double Y)[] points) =>
        points.Select(point => new RoofPoint2D(point.X, point.Y)).ToArray();

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AcKrovy.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed record Planned(
        HipRoofGeometry Geometry,
        bool IsValid,
        RoofAutomaticPurlinPlan? Plan,
        RoofAutomaticPurlinPlanError Error);
}
