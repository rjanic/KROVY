using System.Globalization;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutomaticPurlinSectionPresentationTests
{
    [Fact]
    public void Create_IncludesActiveRolesAndSkipsDisabledRows()
    {
        var solved = Solve();
        var culture = CultureInfo.GetCultureInfo("en");
        var layout = new RoofAutomaticPurlinLayout(
            true,
            [
                new(
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    true,
                    RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                    800d,
                    WidthMm: 160d,
                    HeightMm: 220d),
                new(
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    false,
                    RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                    1200d,
                    WidthMm: 160d,
                    HeightMm: 220d),
            ])
        {
            WallPlateEnabled = true,
            WallPlateLowerEdgeHeightMm = 0d,
            RidgeWidthMm = 180d,
            RidgeHeightMm = 240d,
        };

        var plan = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(
                new RoofRelativeElevationDatum(
                    RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                    0d,
                    0d),
                TimberElementDefaults.For(TimberElementType.Purlin).HeightMm,
                TimberElementDefaults.For(TimberElementType.Rafter).HeightMm)
            {
                PurlinWidthMm = TimberElementDefaults.For(TimberElementType.Purlin).WidthMm,
                WallPlatesEnabled = true,
                WallPlateWidthMm = TimberElementDefaults.For(TimberElementType.WallPlate).WidthMm,
                WallPlateHeightMm = TimberElementDefaults.For(TimberElementType.WallPlate).HeightMm,
            });
        Assert.True(plan.IsValid, plan.Error.ToString());
        Assert.Equal(4, plan.Plan!.Items.Count(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate));
        Assert.Equal(4, plan.Plan.Items.Count(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate));
        Assert.Single(plan.Plan.Items, item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);

        var presentation = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan.Plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture);

        Assert.Equal(2, presentation.SlopeLines.Count);
        Assert.Equal(2, presentation.Walls.Count);
        Assert.Equal(2, presentation.Members.Count(member => member.Role == RoofAutomaticPurlinGeneratorRole.WallPlate));
        Assert.Single(presentation.Members, member => member.Role == RoofAutomaticPurlinGeneratorRole.Ridge);
        Assert.Equal(2, presentation.Members.Count(member => member.Role == RoofAutomaticPurlinGeneratorRole.Intermediate));
        Assert.Equal(
            [AutomaticPurlinSectionSide.Left, AutomaticPurlinSectionSide.Right],
            presentation.Rafters.Select(rafter => rafter.Side));
        Assert.All(presentation.Rafters, rafter =>
        {
            Assert.Equal(4, rafter.Corners.Count);
            var first = rafter.Corners[0];
            var fourth = rafter.Corners[3];
            var depth = Math.Sqrt(
                Math.Pow(first.XMm - fourth.XMm, 2d) +
                Math.Pow(first.ZMm - fourth.ZMm, 2d));
            Assert.Equal(presentation.RafterHeightMm, depth, 8);
        });
        Assert.DoesNotContain(
            presentation.Members,
            member => member.StableKey.Contains("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", StringComparison.Ordinal));
        Assert.Contains(
            presentation.Members,
            member => member.DimensionText.Contains("180", StringComparison.Ordinal) &&
                      member.Role == RoofAutomaticPurlinGeneratorRole.Ridge);
        Assert.All(presentation.Members, member =>
        {
            Assert.False(string.IsNullOrWhiteSpace(member.BottomText));
            Assert.False(string.IsNullOrWhiteSpace(member.TopText));
            Assert.Equal(string.Empty, member.AxisText);
            Assert.StartsWith("BE ", member.BottomText, StringComparison.Ordinal);
            Assert.StartsWith("TE ", member.TopText, StringComparison.Ordinal);
            Assert.DoesNotContain("OS", member.BottomText, StringComparison.Ordinal);
            Assert.DoesNotContain("OS", member.TopText, StringComparison.Ordinal);
            Assert.DoesNotContain("OS", member.AxisText, StringComparison.Ordinal);
        });
        Assert.Equal(160d, presentation.RafterHeightMm, 9);
        Assert.Equal(80d, presentation.RafterWidthMm, 9);
        Assert.Equal("Rafter 80 × 160", presentation.RafterDimensionText);
        Assert.Equal("80 × 160", presentation.RafterSectionDimensionText);
        Assert.Equal("160 mm", presentation.RafterHeightDimensionText);
        Assert.Equal("Rafter", presentation.RafterCaptionText);
    }

    [Fact]
    public void Create_WallPlateOnlyMapsFourGeneratedFacesToTwoSectionMembers()
    {
        var solved = Solve();
        var plan = CreatePlan(solved, new RoofAutomaticPurlinLayout(false, [])
        {
            WallPlateEnabled = true,
        });
        Assert.Equal(4, plan.Items.Count(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate));

        var presentation = CreatePresentation(solved, plan);

        var wallPlates = presentation.Members
            .Where(member => member.Role == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .ToArray();
        Assert.Equal(2, wallPlates.Length);
        Assert.Equal(
            [AutomaticPurlinSectionSide.Left, AutomaticPurlinSectionSide.Right],
            wallPlates.Select(member => member.Side).Order());
        Assert.All(presentation.Walls, wall =>
        {
            var plate = Assert.Single(wallPlates, member => member.Side == wall.Side);
            Assert.Equal(plate.CenterXMm, wall.CenterXMm, 8);
            Assert.Equal(plate.CenterZMm - plate.HeightMm / 2d, wall.TopZMm, 8);
        });
    }

    [Fact]
    public void Create_OneIntermediateRowMapsFourGeneratedFacesToTwoSectionMembers()
    {
        var solved = Solve();
        var plan = CreatePlan(solved, new RoofAutomaticPurlinLayout(false,
        [
            new(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                800d),
        ]));
        Assert.Equal(4, plan.Items.Count(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate));

        var presentation = CreatePresentation(solved, plan);

        var intermediate = presentation.Members
            .Where(member => member.Role == RoofAutomaticPurlinGeneratorRole.Intermediate)
            .ToArray();
        Assert.Equal(2, intermediate.Length);
        Assert.Equal(
            [AutomaticPurlinSectionSide.Left, AutomaticPurlinSectionSide.Right],
            intermediate.Select(member => member.Side).Order());
        Assert.Equal(2, intermediate.Select(member => member.StableKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Create_RidgeEnabledMapsToOneCenteredSectionMember()
    {
        var solved = Solve();
        var plan = CreatePlan(solved, new RoofAutomaticPurlinLayout(true, []));

        var ridge = Assert.Single(
            CreatePresentation(solved, plan).Members,
            member => member.Role == RoofAutomaticPurlinGeneratorRole.Ridge);
        Assert.Equal(AutomaticPurlinSectionSide.Center, ridge.Side);
    }

    [Fact]
    public void Create_SeatingDepthChangesVisibleMemberRafterOverlap()
    {
        var solved = Solve();
        AutomaticPurlinSectionMemberMm MemberAt(double seatingPercent)
        {
            var layout = new RoofAutomaticPurlinLayout(false,
            [
                new(
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    true,
                    RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                    1000d,
                    SeatingDepth: new(
                        RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                        seatingPercent)),
            ]);
            var plan = CreatePlan(solved, layout);
            return Assert.Single(
                CreatePresentation(solved, plan, layout: layout).Members,
                member => member.Role == RoofAutomaticPurlinGeneratorRole.Intermediate &&
                          member.Side == AutomaticPurlinSectionSide.Left);
        }

        var tenPercent = MemberAt(10d);
        var twentyFivePercent = MemberAt(25d);

        Assert.Equal(16d, tenPercent.SeatingDepthMm);
        Assert.Equal(40d, twentyFivePercent.SeatingDepthMm);
        Assert.True(tenPercent.VisibleRafterOverlapZMm > 0d);
        Assert.True(twentyFivePercent.VisibleRafterOverlapZMm > tenPercent.VisibleRafterOverlapZMm!.Value);
        // Deeper seating raises the visual contact (and thus the schematic top).
        Assert.True(twentyFivePercent.MemberTopZMm > tenPercent.MemberTopZMm);
        Assert.Equal(
            tenPercent.VisibleRafterOverlapZMm!.Value * 2.5d,
            twentyFivePercent.VisibleRafterOverlapZMm!.Value,
            6);
    }

    [Fact]
    public void Create_RafterHeight160_SeatingDepth40_AppliesToWallPlateRidgeAndIntermediate()
    {
        var solved = Solve();
        var rafterHeightMm = 160d;
        var seatingDepthMm = 40d;
        var layout = new RoofAutomaticPurlinLayout(true,
        [
            new(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                1000d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm,
                    seatingDepthMm)),
        ])
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                500d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm,
                    seatingDepthMm)),
        };
        var plan = CreatePlan(solved, layout);

        var presentation = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, CultureInfo.GetCultureInfo("en")),
            CultureInfo.GetCultureInfo("en"),
            rafterHeightMm,
            layout: layout);

        Assert.Equal(rafterHeightMm, presentation.RafterHeightMm, 9);
        var seated = presentation.Members
            .Where(member =>
                member.Role is RoofAutomaticPurlinGeneratorRole.WallPlate
                    or RoofAutomaticPurlinGeneratorRole.Intermediate
                    or RoofAutomaticPurlinGeneratorRole.Ridge)
            .ToArray();
        Assert.Equal(5, seated.Length);
        Assert.Contains(seated, member => member.Role == RoofAutomaticPurlinGeneratorRole.Ridge);
        Assert.All(seated, member =>
        {
            Assert.Equal(seatingDepthMm, member.SeatingDepthMm);
            Assert.NotNull(member.RafterLowerSurfaceZMm);
            Assert.True(member.VisibleRafterOverlapZMm > 0d);
            Assert.True(member.VisibleRafterOverlapZMm < rafterHeightMm);
            Assert.Equal(seatingDepthMm, member.VisibleRafterOverlapZMm);
        });
    }

    [Fact]
    public void Create_ZeroSeating_AnnotatesDepthAndAlignsPlanDistanceTopsToVisualEdge()
    {
        var solved = Solve();
        var layout = new RoofAutomaticPurlinLayout(true,
        [
            new(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                1800d,
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
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                500d,
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
        var plan = CreatePlan(solved, layout);

        var presentation = CreatePresentation(solved, plan, ExplicitZeroDatum(), layout);
        Assert.True(presentation.Members.Count >= 5);
        Assert.All(presentation.Members, member => Assert.Equal(0d, member.SeatingDepthMm));
        AssertPlanDistanceVisualTops(presentation, seatingFraction: 0d);
        AssertMemberHeightsMatchPlan(plan, presentation);
    }

    [Fact]
    public void Create_FullPercentSeating_AnnotatesDepthAndAlignsPlanDistanceTopsToVisualEdge()
    {
        var solved = Solve();
        var rafterHeight = TimberElementDefaults.For(TimberElementType.Rafter).HeightMm;
        var layout = new RoofAutomaticPurlinLayout(true,
        [
            new(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                1800d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    100d),
                WidthMm: 160d,
                HeightMm: 220d),
        ])
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                500d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    100d),
                WidthMm: 140d,
                HeightMm: 140d),
            RidgeWidthMm = 160d,
            RidgeHeightMm = 220d,
            RidgeSeatingDepth = new(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                100d),
        };
        var plan = CreatePlan(solved, layout);

        var presentation = CreatePresentation(solved, plan, ExplicitZeroDatum(), layout);
        Assert.True(presentation.Members.Count >= 5);
        Assert.All(presentation.Members, member => Assert.Equal(rafterHeight, member.SeatingDepthMm));
        AssertPlanDistanceVisualTops(presentation, seatingFraction: 1d);
        AssertMemberHeightsMatchPlan(plan, presentation);
    }

    [Fact]
    public void Create_SideMembers_UseOuterTopCornerOffsetForContactX()
    {
        var solved = Solve();
        var plan = CreatePlan(solved, new RoofAutomaticPurlinLayout(false,
        [
            new(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                1800d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    0d),
                WidthMm: 200d,
                HeightMm: 200d),
        ]));
        var presentation = CreatePresentation(solved, plan);
        var right = Assert.Single(
            presentation.Members,
            member => member.Role == RoofAutomaticPurlinGeneratorRole.Intermediate &&
                      member.Side == AutomaticPurlinSectionSide.Right);
        var contactX = right.CenterXMm +
            AutomaticPurlinSectionPresentation.SideContactCornerOffsetXMm(
                right.Side,
                right.WidthMm);
        Assert.True(contactX > right.CenterXMm);
        Assert.Equal(right.WidthMm / 2d, contactX - right.CenterXMm, 9);
    }

    [Theory]
    [InlineData(30d, 0d)]
    [InlineData(30d, 200d)]
    [InlineData(45d, 0d)]
    [InlineData(45d, 200d)]
    public void Create_BottomEdgeWallPlate_BottomCoincidesWithReferenceAcrossDatums(
        double pitchDegrees,
        double bottomMm)
    {
        foreach (var kind in new[]
                 {
                     RoofRelativeElevationReferenceKind.SourceEavePlane,
                     RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                     RoofRelativeElevationReferenceKind.WallPlateBottom,
                 })
        {
            AssertWallPlateBottomCoincidesWithReference(
                pitchDegrees,
                bottomMm,
                wallPlateHeightMm: 140d,
                rafterHeightMm: 125d,
                kind);
        }
    }

    [Theory]
    [InlineData(30d, 120d)]
    [InlineData(45d, 140d)]
    [InlineData(45d, 180d)]
    public void Create_BottomEdgeWallPlate_RespectsTimberHeightAtSchematicScale(
        double pitchDegrees,
        double wallPlateHeightMm)
    {
        AssertWallPlateBottomCoincidesWithReference(
            pitchDegrees,
            bottomMm: 0d,
            wallPlateHeightMm,
            rafterHeightMm: 125d,
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane);
    }

    [Theory]
    [InlineData(30d, 0d)]
    [InlineData(30d, 100d)]
    [InlineData(45d, 0d)]
    [InlineData(45d, 100d)]
    public void Create_PhysicalElevations_RemainAuthoritativeInLabelsAcrossPitchAndSeating(
        double pitchDegrees,
        double seatingPercent)
    {
        var solved = Solve(pitchDegrees);
        var layout = new RoofAutomaticPurlinLayout(true,
        [
            new(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                1800d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    seatingPercent),
                WidthMm: 160d,
                HeightMm: 220d),
        ])
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                500d,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    seatingPercent),
                WidthMm: 140d,
                HeightMm: 140d),
            RidgeWidthMm = 160d,
            RidgeHeightMm = 220d,
            RidgeSeatingDepth = new(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                seatingPercent),
        };
        var plan = CreatePlan(solved, layout);

        var presentation = CreatePresentation(solved, plan, ExplicitZeroDatum(), layout);
        AssertMemberHeightsMatchPlan(plan, presentation);
        AssertPlanDistanceVisualTops(presentation, seatingPercent / 100d);
        Assert.Contains(
            presentation.Members,
            member => member.Role == RoofAutomaticPurlinGeneratorRole.Intermediate);
        Assert.Contains(
            presentation.Members,
            member => member.Role == RoofAutomaticPurlinGeneratorRole.Ridge);
    }

    [Theory]
    [InlineData(30d)]
    [InlineData(45d)]
    public void Create_SeatingPercents_AnnotateDepthAndAlignVisualTops(double pitchDegrees)
    {
        var solved = Solve(pitchDegrees);
        foreach (var percent in new[] { 25d, 50d, 75d })
        {
            var layout = new RoofAutomaticPurlinLayout(false,
            [
                new(
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    true,
                    RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                    1800d,
                    SeatingDepth: new(
                        RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                        percent),
                    WidthMm: 160d,
                    HeightMm: 220d),
            ]);
            var plan = CreatePlan(solved, layout);
            var presentation = CreatePresentation(solved, plan, ExplicitZeroDatum(), layout);
            var expectedDepth =
                TimberElementDefaults.For(TimberElementType.Rafter).HeightMm * percent / 100d;
            Assert.All(
                presentation.Members.Where(member =>
                    member.Role is RoofAutomaticPurlinGeneratorRole.Intermediate),
                member =>
                {
                    Assert.Equal(expectedDepth, member.SeatingDepthMm);
                    Assert.Equal(expectedDepth, member.VisibleRafterOverlapZMm);
                });
            AssertPlanDistanceVisualTops(presentation, percent / 100d);
            AssertMemberHeightsMatchPlan(plan, presentation);
        }
    }

    private static void AssertWallPlateBottomCoincidesWithReference(
        double pitchDegrees,
        double bottomMm,
        double wallPlateHeightMm,
        double rafterHeightMm,
        RoofRelativeElevationReferenceKind kind)
    {
        var solved = Solve(pitchDegrees);
        var datum = kind switch
        {
            RoofRelativeElevationReferenceKind.SourceEavePlane =>
                new RoofRelativeElevationDatum(kind, 0d, 0d),
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane =>
                new RoofRelativeElevationDatum(kind, 0d, 0d),
            RoofRelativeElevationReferenceKind.WallPlateBottom =>
                new RoofRelativeElevationDatum(kind, 0d, 0d),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var layout = new RoofAutomaticPurlinLayout(false, [])
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                bottomMm,
                SeatingDepth: new(
                    RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                    25d),
                WidthMm: wallPlateHeightMm,
                HeightMm: wallPlateHeightMm),
        };
        var planResult = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(
                datum,
                TimberElementDefaults.For(TimberElementType.Purlin).HeightMm,
                rafterHeightMm)
            {
                PurlinWidthMm = TimberElementDefaults.For(TimberElementType.Purlin).WidthMm,
                WallPlatesEnabled = true,
                WallPlateWidthMm = wallPlateHeightMm,
                WallPlateHeightMm = wallPlateHeightMm,
            });
        Assert.True(planResult.IsValid, planResult.Error.ToString());
        var plan = Assert.IsType<RoofAutomaticPurlinPlan>(planResult.Plan);

        var culture = CultureInfo.GetCultureInfo("en");
        var presentation = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            rafterHeightMm,
            80d,
            datum,
            layout);

        Assert.NotNull(presentation.ReferencePlane);
        var wallPlates = presentation.Members
            .Where(member => member.Role == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .ToArray();
        Assert.Equal(2, wallPlates.Length);
        // SourceEave / Explicit: guide is architectural 0 mapped to the eave tip; bottom sits
        // bottomMm above that guide. WallPlateBottom: guide follows the drawn bottoms.
        var expectedBottomOffsetMm =
            kind == RoofRelativeElevationReferenceKind.WallPlateBottom ? 0d : bottomMm;
        Assert.All(wallPlates, plate =>
        {
            var bottomZ = plate.CenterZMm - plate.HeightMm / 2d;
            var topZ = plate.CenterZMm + plate.HeightMm / 2d;
            Assert.Equal(
                presentation.ReferencePlane!.LocalZMm + expectedBottomOffsetMm,
                bottomZ,
                6);
            Assert.Equal(wallPlateHeightMm, topZ - bottomZ, 6);
            Assert.Equal(wallPlateHeightMm, plate.HeightMm, 6);
            Assert.Contains("BE ", plate.BottomText, StringComparison.Ordinal);
            Assert.Contains("TE ", plate.TopText, StringComparison.Ordinal);
        });
        AssertMembersMatchPlanElevations(plan, presentation);
    }

    private static void AssertPlanDistanceVisualTops(
        AutomaticPurlinSectionPresentation presentation,
        double seatingFraction)
    {
        foreach (var member in presentation.Members.Where(member =>
                     member.Role is RoofAutomaticPurlinGeneratorRole.WallPlate
                         or RoofAutomaticPurlinGeneratorRole.Intermediate
                         or RoofAutomaticPurlinGeneratorRole.Ridge))
        {
            if (member.Side == AutomaticPurlinSectionSide.Center ||
                member.Role == RoofAutomaticPurlinGeneratorRole.Ridge)
            {
                var leftX = member.CenterXMm - member.WidthMm / 2d;
                var rightX = member.CenterXMm + member.WidthMm / 2d;
                Assert.True(
                    AutomaticPurlinSectionSvgTemplate.TryInterpolateVisualRafterSeatingLocalZMm(
                        presentation.Rafters,
                        AutomaticPurlinSectionSide.Left,
                        leftX,
                        seatingFraction,
                        out var leftContact,
                        out _));
                Assert.True(
                    AutomaticPurlinSectionSvgTemplate.TryInterpolateVisualRafterSeatingLocalZMm(
                        presentation.Rafters,
                        AutomaticPurlinSectionSide.Right,
                        rightX,
                        seatingFraction,
                        out var rightContact,
                        out _));
                Assert.Equal((leftContact + rightContact) / 2d, member.MemberTopZMm, 3);
                continue;
            }

            var contactX = member.CenterXMm +
                AutomaticPurlinSectionPresentation.SideContactCornerOffsetXMm(
                    member.Side,
                    member.WidthMm);
            Assert.True(
                AutomaticPurlinSectionSvgTemplate.TryInterpolateVisualRafterSeatingLocalZMm(
                    presentation.Rafters,
                    member.Side,
                    contactX,
                    seatingFraction,
                    out var visualContactZ,
                    out _));
            Assert.Equal(visualContactZ, member.MemberTopZMm, 3);
        }
    }

    private static void AssertMemberHeightsMatchPlan(
        RoofAutomaticPurlinPlan plan,
        AutomaticPurlinSectionPresentation presentation)
    {
        foreach (var member in presentation.Members)
        {
            var profile = plan.Items
                .Where(item =>
                    item.GeneratorRole == member.Role &&
                    item.ElevationProfile is not null)
                .Select(item => item.ElevationProfile!)
                .OrderBy(candidate =>
                    Math.Abs((candidate.TopLocalZMm - candidate.BottomLocalZMm) - member.HeightMm))
                .FirstOrDefault();
            Assert.NotNull(profile);
            Assert.Equal(profile!.TopLocalZMm - profile.BottomLocalZMm, member.HeightMm, 3);
            Assert.Contains("BE ", member.BottomText, StringComparison.Ordinal);
            Assert.Contains("TE ", member.TopText, StringComparison.Ordinal);
        }
    }

    private static void AssertMembersMatchPlanElevations(
        RoofAutomaticPurlinPlan plan,
        AutomaticPurlinSectionPresentation presentation)
    {
        Assert.True(
            AutomaticPurlinSectionSvgTemplate.TryResolveVisualSourceEaveTopLocalZMm(
                presentation,
                out var originZ));
        foreach (var member in presentation.Members)
        {
            var profile = plan.Items
                .Where(item =>
                    item.GeneratorRole == member.Role &&
                    item.ElevationProfile is not null &&
                    Math.Abs(item.ElevationProfile.CenterLocalZMm + originZ - member.CenterZMm) < 1d)
                .Select(item => item.ElevationProfile!)
                .OrderBy(profile => Math.Abs(profile.CenterLocalZMm + originZ - member.CenterZMm))
                .FirstOrDefault();
            Assert.NotNull(profile);
            Assert.Equal(originZ + profile!.CenterLocalZMm, member.CenterZMm, 6);
            Assert.Equal(
                profile.TopLocalZMm - profile.BottomLocalZMm,
                member.HeightMm,
                3);
            Assert.Equal(
                originZ + profile.BottomLocalZMm,
                member.CenterZMm - member.HeightMm / 2d,
                6);
            Assert.Equal(
                originZ + profile.TopLocalZMm,
                member.CenterZMm + member.HeightMm / 2d,
                6);
        }
    }

    private static RoofRelativeElevationDatum ExplicitZeroDatum() =>
        new(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            0d);

    [Fact]
    public void Create_RafterDimensionUsesAuthoritativeWidthAndHeight()
    {
        var solved = Solve();
        var plan = CreatePlan(solved, new RoofAutomaticPurlinLayout(false, [])
        {
            WallPlateEnabled = true,
        });
        var culture = CultureInfo.GetCultureInfo("en");
        var presentation = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            rafterHeightMm: 200d,
            rafterWidthMm: 100d);

        Assert.Equal(200d, presentation.RafterHeightMm, 9);
        Assert.Equal(100d, presentation.RafterWidthMm, 9);
        Assert.Equal("200 mm", presentation.RafterHeightDimensionText);
        Assert.Equal("Rafter 100 × 200", presentation.RafterDimensionText);
        Assert.All(presentation.Rafters, rafter =>
        {
            var first = rafter.Corners[0];
            var fourth = rafter.Corners[3];
            var depth = Math.Sqrt(
                Math.Pow(first.XMm - fourth.XMm, 2d) +
                Math.Pow(first.ZMm - fourth.ZMm, 2d));
            Assert.Equal(200d, depth, 8);
        });
    }

    [Fact]
    public void Create_SectionLabelsUseLocalizedEdgeAbbrevsWithoutOs()
    {
        var solved = Solve();
        var plan = CreatePlan(solved, new RoofAutomaticPurlinLayout(true,
        [
            new(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                900d,
                HeightMm: 140d),
        ])
        {
            WallPlateEnabled = true,
        });

        Assert.All(
            new[]
            {
                ("sk-SK", "SH", "HH"),
                ("cs-CZ", "SH", "HH"),
                ("en", "BE", "TE"),
                ("de-DE", "UK", "OK"),
                ("pl-PL", "DK", "GK"),
                ("fr-FR", "BI", "BS"),
            },
            pair =>
            {
                var culture = CultureInfo.GetCultureInfo(pair.Item1);
                var presentation = AutomaticPurlinSectionPresentation.Create(
                    solved.Geometry,
                    plan,
                    item => item.GeneratorRole.ToString(),
                    key => UiStrings.GetString(key, culture),
                    culture,
                    160d);
                Assert.All(presentation.Members, member =>
                {
                    Assert.StartsWith(pair.Item2 + " ", member.BottomText, StringComparison.Ordinal);
                    Assert.StartsWith(pair.Item3 + " ", member.TopText, StringComparison.Ordinal);
                    Assert.Equal(string.Empty, member.AxisText);
                    Assert.DoesNotContain("OS", member.BottomText, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("OS", member.TopText, StringComparison.OrdinalIgnoreCase);
                });
            });
    }


    [Fact]
    public void FilteredSectionMembersKeepLabelLanesCollisionFree()
    {
        var solved = Solve();
        var plan = CreatePlan(solved, new RoofAutomaticPurlinLayout(true,
        [
            new("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true, RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference, 700d),
            new("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", true, RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference, 1200d),
        ])
        {
            WallPlateEnabled = true,
        });
        var presentation = CreatePresentation(solved, plan);
        var requests = presentation.Members.Select(member =>
            new AutomaticPurlinSectionLabelLanes.LabelRequest(
                member.StableKey,
                member.CenterXMm / 10d + 400d,
                20d,
                148d,
                64d)).ToArray();

        var placements = AutomaticPurlinSectionLabelLanes.Assign(
            requests,
            laneStep: 70d,
            padding: 4d,
            lanesGrowNegativeY: false);

        Assert.DoesNotContain(
            placements.SelectMany((left, index) => placements.Skip(index + 1).Select(right => (left, right))),
            pair => pair.left.Bounds.Intersects(pair.right.Bounds, padding: 4d));
    }

    [Fact]
    public void Create_UsesPlanElevationsNotIndependentPlacementSolver()
    {
        var solved = Solve();
        var culture = CultureInfo.GetCultureInfo("en");
        var layout = new RoofAutomaticPurlinLayout(
            false,
            [
                new(
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    true,
                    RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                    900d,
                    HeightMm: 180d),
            ]);
        var plan = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(
                new RoofRelativeElevationDatum(
                    RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                    0d,
                    0d),
                TimberElementDefaults.For(TimberElementType.Purlin).HeightMm,
                TimberElementDefaults.For(TimberElementType.Rafter).HeightMm)
            {
                PurlinWidthMm = TimberElementDefaults.For(TimberElementType.Purlin).WidthMm,
            });
        Assert.True(plan.IsValid, plan.Error.ToString());
        var expected = plan.Plan!.Items[0].ElevationProfile!;

        var presentation = AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan.Plan,
            _ => "row",
            key => UiStrings.GetString(key, culture),
            culture);
        var expectedBottom = RoofRelativeElevationDatumRules.FormatMetres(
            expected.BottomRelativeElevationMm,
            culture);
        var expectedTop = RoofRelativeElevationDatumRules.FormatMetres(
            expected.TopRelativeElevationMm,
            culture);
        Assert.All(presentation.Members, member =>
        {
            Assert.Equal(180d, member.HeightMm, 9);
            Assert.Equal(40d, member.SeatingDepthMm);
            Assert.Equal(40d, member.VisibleRafterOverlapZMm);
            Assert.Equal($"BE {expectedBottom}", member.BottomText);
            Assert.Equal($"TE {expectedTop}", member.TopText);
            Assert.Equal(string.Empty, member.AxisText);
        });
        AssertMembersMatchPlanElevations(plan.Plan, presentation);
    }

    private static SolvedFixture Solve(double pitchDegrees = 30d)
    {
        var points = new RoofPoint2D[] { new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000) };
        var input = new RoofFootprintInput(points, true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid, normalized.Validation.Error.ToString());
        var identity = RoofBoundaryIdentityRules.Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            normalized.EdgeProvenance.Count,
            RoofBoundaryIdentityRules.FormatWinding(normalized.Validation.SourceOrientation),
            Enumerable.Range(1, normalized.EdgeProvenance.Count).ToArray()).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        Assert.True(provenance.IsValid);
        var geometry = HipRoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(pitchDegrees),
            RoofKind.Hip));
        Assert.True(geometry.IsValid, geometry.Error.ToString());
        return new SolvedFixture(Assert.IsType<HipRoofGeometry>(geometry.Geometry), provenance);
    }

    private static RoofAutomaticPurlinPlan CreatePlan(
        SolvedFixture solved,
        RoofAutomaticPurlinLayout layout)
    {
        var result = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(
                new RoofRelativeElevationDatum(
                    RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                    0d,
                    0d),
                TimberElementDefaults.For(TimberElementType.Purlin).HeightMm,
                TimberElementDefaults.For(TimberElementType.Rafter).HeightMm)
            {
                PurlinWidthMm = TimberElementDefaults.For(TimberElementType.Purlin).WidthMm,
                WallPlatesEnabled = layout.WallPlateEnabled,
                WallPlateWidthMm = TimberElementDefaults.For(TimberElementType.WallPlate).WidthMm,
                WallPlateHeightMm = TimberElementDefaults.For(TimberElementType.WallPlate).HeightMm,
            });
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<RoofAutomaticPurlinPlan>(result.Plan);
    }

    private static AutomaticPurlinSectionPresentation CreatePresentation(
        SolvedFixture solved,
        RoofAutomaticPurlinPlan plan,
        RoofRelativeElevationDatum? referenceDatum = null,
        RoofAutomaticPurlinLayout? layout = null)
    {
        var culture = CultureInfo.GetCultureInfo("en");
        return AutomaticPurlinSectionPresentation.Create(
            solved.Geometry,
            plan,
            item => item.GeneratorRole.ToString(),
            key => UiStrings.GetString(key, culture),
            culture,
            TimberElementDefaults.For(TimberElementType.Rafter).HeightMm,
            TimberElementDefaults.For(TimberElementType.Rafter).WidthMm,
            referenceDatum,
            layout);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
