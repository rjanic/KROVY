using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class AutomaticPurlinDialogViewModelTests
{
    private const string LayoutIdA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string LayoutIdB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void ExistingLayoutAndDatum_LoadExactlyWithoutChangingStableRows()
    {
        var solved = Solve(RectanglePoints());
        var ridge = Assert.Single(Ridges(solved));
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm,
            35d);
        var layout = new RoofAutomaticPurlinLayout(true,
        [
            new(LayoutIdA, false,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference, 500d),
            new(LayoutIdB, true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge, 1200d,
                ridge.StructuralIdentity, seating),
        ]);
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.WallPlateBottom,
            3580d,
            125d);

        var viewModel = CreateViewModel(solved, layout, true, datum, true);

        Assert.True(viewModel.LayoutExists);
        Assert.True(viewModel.DatumExists);
        Assert.True(viewModel.RidgeEnabled);
        Assert.Equal(new[] { LayoutIdA, LayoutIdB },
            viewModel.Rows.Select(row => row.LayoutItemId));
        Assert.Equal("+3.580", viewModel.RelativeReferenceText);
        Assert.Equal(RoofRelativeElevationReferenceKind.WallPlateBottom, viewModel.ReferenceKind);
        Assert.True(viewModel.TryCreateDraft(out var draftLayout, out var draftDatum));
        Assert.True(draftLayout!.RidgeEnabled);
        Assert.Equal(
            layout.IntermediateItems.Select(item => item.LayoutItemId),
            draftLayout.IntermediateItems.Select(item => item.LayoutItemId));
        Assert.Equal(
            layout.IntermediateItems.Select(item => item.PlacementMode),
            draftLayout.IntermediateItems.Select(item => item.PlacementMode));
        Assert.Equal(datum, draftDatum);
        Assert.NotNull(draftLayout.RidgeSeatingDepth);
    }

    [Fact]
    public void MissingLayoutAndDatum_StartAsExplicitUnpersistedNewDraftDefaults()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);

        Assert.False(viewModel.LayoutExists);
        Assert.False(viewModel.DatumExists);
        Assert.True(viewModel.WallPlateEnabled);
        Assert.True(viewModel.RidgeEnabled);
        Assert.Single(viewModel.Rows);
        Assert.True(viewModel.Rows[0].Enabled);
        Assert.Equal(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            viewModel.WallPlateRow.PlacementMode);
        Assert.Equal("500", viewModel.WallPlateRow.PlacementValueText);
        Assert.Equal("140", viewModel.WallPlateRow.WidthText);
        Assert.Equal("140", viewModel.WallPlateRow.HeightText);
        Assert.True(viewModel.WallPlateRow.IsSeatingApplicable);
        Assert.Equal(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            viewModel.WallPlateRow.SelectedSeatingMode);
        Assert.Equal("25", viewModel.WallPlateRow.SeatingDepthValueText);
        Assert.Equal("160", viewModel.RidgeWidthText);
        Assert.Equal("220", viewModel.RidgeHeightText);
        Assert.Equal(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            viewModel.SelectedRidgeSeatingMode);
        Assert.Equal("25", viewModel.RidgeSeatingDepthValueText);
        Assert.Equal(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            viewModel.Rows[0].PlacementMode);
        Assert.Equal("1800", viewModel.Rows[0].PlacementValueText);
        Assert.Equal("160", viewModel.Rows[0].WidthText);
        Assert.Equal("220", viewModel.Rows[0].HeightText);
        Assert.Equal(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            viewModel.Rows[0].SelectedSeatingMode);
        Assert.Equal("25", viewModel.Rows[0].SeatingDepthValueText);
        Assert.Contains("not set", viewModel.DatumPersistenceStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("±0.000", viewModel.RelativeReferenceText);
        Assert.Equal(RoofRelativeElevationReferenceKind.SourceEavePlane, viewModel.ReferenceKind);
        Assert.True(viewModel.TryCreateDraft(out var layout, out var datum));
        Assert.True(layout!.RidgeEnabled);
        Assert.True(layout.WallPlateEnabled);
        Assert.Equal(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            layout.WallPlatePlacement!.PlacementMode);
        Assert.Equal(500d, layout.WallPlatePlacement.PlacementValueMm);
        Assert.Equal(140d, layout.WallPlatePlacement.WidthMm);
        Assert.Equal(140d, layout.WallPlatePlacement.HeightMm);
        Assert.Equal(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            layout.WallPlatePlacement.SeatingDepth!.Mode);
        Assert.Equal(25d, layout.WallPlatePlacement.SeatingDepth.Value);
        Assert.Equal(160d, layout.RidgeWidthMm);
        Assert.Equal(220d, layout.RidgeHeightMm);
        Assert.Equal(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            layout.RidgeSeatingDepth!.Mode);
        Assert.Equal(25d, layout.RidgeSeatingDepth.Value);
        var intermediate = Assert.Single(layout.IntermediateItems);
        Assert.True(intermediate.Enabled);
        Assert.Equal(RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave, intermediate.PlacementMode);
        Assert.Equal(1800d, intermediate.PlacementValueMm);
        Assert.Equal(160d, intermediate.WidthMm);
        Assert.Equal(220d, intermediate.HeightMm);
        Assert.Equal(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            intermediate.SeatingDepth!.Mode);
        Assert.Equal(25d, intermediate.SeatingDepth.Value);
        Assert.Equal(RoofRelativeElevationReferenceKind.SourceEavePlane, datum!.ReferenceKind);
        Assert.Equal(0d, datum.ReferenceRelativeElevationMm);
    }

    [Fact]
    public void HostMissingLayout_EmptyPlaceholderStillAppliesNewDraftDefaults()
    {
        // Production/debug snapshots pass Empty + LayoutExists=false when no owner
        // section exists. That path must not keep Empty's all-off state.
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(
            solved,
            RoofAutomaticPurlinLayout.Empty,
            layoutExists: false,
            null,
            false,
            mode: AutomaticPurlinDialogMode.ProductionEdit);

        Assert.False(viewModel.LayoutExists);
        Assert.True(viewModel.WallPlateEnabled);
        Assert.True(viewModel.RidgeEnabled);
        Assert.Single(viewModel.Rows);
        Assert.True(viewModel.Rows[0].Enabled);
        Assert.Equal("500", viewModel.WallPlateRow.PlacementValueText);
        Assert.Equal("140", viewModel.WallPlateRow.WidthText);
        Assert.Equal("140", viewModel.WallPlateRow.HeightText);
        Assert.Equal("25", viewModel.WallPlateRow.SeatingDepthValueText);
        Assert.Equal("160", viewModel.RidgeWidthText);
        Assert.Equal("220", viewModel.RidgeHeightText);
        Assert.Equal("25", viewModel.RidgeSeatingDepthValueText);
        Assert.Equal("1800", viewModel.Rows[0].PlacementValueText);
        Assert.Equal("160", viewModel.Rows[0].WidthText);
        Assert.Equal("220", viewModel.Rows[0].HeightText);
        Assert.Equal("25", viewModel.Rows[0].SeatingDepthValueText);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan));
        Assert.True(plan!.Items.Count(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate) > 0);
        Assert.Contains(plan.Items, item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);
        Assert.True(plan.Items.Count(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate) > 0);
    }

    [Fact]
    public void ZeroPercentSeating_KeepsPreviewMembersAndDepthZero()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        Assert.True(viewModel.TryGetPreviewPlan(out var before));
        var beforeKeys = before!.Items.Select(item => item.GeneratedKey).ToArray();
        var beforeCount = before.Items.Count;

        viewModel.WallPlateRow.SeatingDepthValueText = "0";
        viewModel.RidgeSeatingDepthValueText = "0";
        viewModel.Rows[0].SeatingDepthValueText = "0";

        Assert.True(viewModel.TryGetPreviewPlan(out var after));
        Assert.Equal(beforeCount, after!.Items.Count);
        Assert.Equal(beforeKeys, after.Items.Select(item => item.GeneratedKey));
        Assert.All(after.Items, item =>
        {
            Assert.Equal(0d, item.ElevationProfile!.SeatingDepthMm);
            Assert.True(item.LengthMm > 0d);
            if (item.PhysicalPlacement is not null)
            {
                Assert.Equal(0d, item.PhysicalPlacement.SeatingDepthMm, 9);
            }
        });
        Assert.Equal(
            before.Items.Count(item => item.GeneratorRole != RoofAutomaticPurlinGeneratorRole.Ridge) / 2
                + before.Items.Count(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge),
            viewModel.SectionPresentation.Members.Count);
        Assert.All(
            viewModel.SectionPresentation.Members,
            member => Assert.Equal(0d, member.SeatingDepthMm));
        Assert.True(viewModel.TryCreateDraft(out var draft, out _));
        Assert.Equal(0d, draft!.WallPlatePlacement!.SeatingDepth!.Value);
        Assert.Equal(0d, draft.RidgeSeatingDepth!.Value);
        Assert.Equal(0d, Assert.Single(draft.IntermediateItems).SeatingDepth!.Value);
    }

    [Fact]
    public void PersistedLayout_IsNotReplacedByNewDraftDefaults()
    {
        var solved = Solve(RectanglePoints());
        var stored = new RoofAutomaticPurlinLayout(
            false,
            [
                new(
                    LayoutIdA,
                    false,
                    RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge,
                    900d,
                    WidthMm: 100d,
                    HeightMm: 120d),
            ])
        {
            WallPlateEnabled = false,
            RidgeWidthMm = 90d,
            RidgeHeightMm = 110d,
        };
        var viewModel = CreateViewModel(solved, stored, true, null, false);

        Assert.True(viewModel.LayoutExists);
        Assert.False(viewModel.WallPlateEnabled);
        Assert.False(viewModel.RidgeEnabled);
        Assert.Single(viewModel.Rows);
        Assert.False(viewModel.Rows[0].Enabled);
        Assert.Equal("900", viewModel.Rows[0].PlacementValueText);
        Assert.Equal("100", viewModel.Rows[0].WidthText);
        Assert.Equal("120", viewModel.Rows[0].HeightText);
        Assert.Equal("90", viewModel.RidgeWidthText);
        Assert.Equal("110", viewModel.RidgeHeightText);
        Assert.True(viewModel.TryCreateDraft(out var draft, out _));
        Assert.False(draft!.WallPlateEnabled);
        Assert.False(draft.RidgeEnabled);
        Assert.Equal(90d, draft.RidgeWidthMm);
        Assert.Equal(110d, draft.RidgeHeightMm);
        Assert.Equal(LayoutIdA, Assert.Single(draft.IntermediateItems).LayoutItemId);
    }

    [Fact]
    public void PersistedWallPlateSetting_LoadsAndRoundtripsThroughDraft()
    {
        var solved = Solve(RectanglePoints());
        var stored = RoofAutomaticPurlinLayout.Empty with
        {
            WallPlateEnabled = true,
            WallPlateLowerEdgeHeightMm = 1000d,
        };
        var viewModel = CreateViewModel(solved, stored, true, null, false);

        Assert.True(viewModel.WallPlateEnabled);
        Assert.Equal(
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            viewModel.WallPlateRow.PlacementMode);
        Assert.Equal("1000", viewModel.WallPlateRow.PlacementValueText);
        Assert.True(viewModel.TryCreateDraft(out var draft, out _));
        Assert.True(draft!.WallPlateEnabled);
        Assert.Equal(1000d, draft.WallPlateLowerEdgeHeightMm);
        Assert.Equal(
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            draft.WallPlatePlacement!.PlacementMode);
        Assert.Equal(1000d, draft.WallPlatePlacement.PlacementValueMm);
    }

    [Fact]
    public void WallPlatePlacement_ExposesSameModesAsIntermediateAndDrivesPreview()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        ClearIntermediateRows(viewModel);
        viewModel.RidgeEnabled = false;
        viewModel.WallPlateEnabled = true;

        Assert.Equal(
            viewModel.PlacementModes.Select(option => option.Mode),
            viewModel.WallPlateRow.PlacementModes.Select(option => option.Mode));
        Assert.Equal(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            viewModel.WallPlateRow.PlacementMode);
        Assert.Equal("500", viewModel.WallPlateRow.PlacementValueText);

        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
        viewModel.WallPlateRow.PlacementValueText = "0";
        Assert.True(viewModel.WallPlateRow.IsSeatingApplicable);
        Assert.True(viewModel.TryGetPreviewPlan(out var atZero));
        Assert.All(atZero!.Items, item =>
        {
            Assert.Equal(RoofAutomaticPurlinGeneratorRole.WallPlate, item.GeneratorRole);
            Assert.Equal(40d, item.ElevationProfile!.SeatingDepthMm);
            Assert.NotNull(item.PhysicalPlacement);
            Assert.Equal(40d, item.PhysicalPlacement!.SeatingDepthMm, 9);
        });

        viewModel.WallPlateRow.PlacementValueText = "900";
        Assert.True(viewModel.TryCreateDraft(out var draft, out _));
        Assert.Equal(900d, draft!.WallPlateLowerEdgeHeightMm);
        Assert.True(viewModel.TryGetPreviewPlan(out var atNineHundred));
        Assert.Equal(
            atZero.Items.Select(item => item.GeneratedKey),
            atNineHundred!.Items.Select(item => item.GeneratedKey));
        Assert.All(atNineHundred.Items, item =>
        {
            Assert.Equal(40d, item.ElevationProfile!.SeatingDepthMm);
            Assert.True(item.ElevationProfile.BottomRelativeElevationMm >
                atZero.Items.First().ElevationProfile!.BottomRelativeElevationMm);
        });
        Assert.All(
            atZero.Items.Zip(atNineHundred.Items),
            pair =>
            {
                Assert.NotEqual(pair.First.Segment3D.Start.X, pair.Second.Segment3D.Start.X);
                Assert.NotEqual(pair.First.Segment3D.Start.Y, pair.Second.Segment3D.Start.Y);
            });

        viewModel.WallPlateRow.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        viewModel.WallPlateRow.PlacementValueText = "1500";
        Assert.True(viewModel.TryCreateDraft(out var distanceDraft, out _));
        Assert.Equal(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            distanceDraft!.WallPlatePlacement!.PlacementMode);
        Assert.Equal(1500d, distanceDraft.WallPlatePlacement.PlacementValueMm);
        Assert.Equal(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            distanceDraft.WallPlatePlacement.SeatingDepth!.Mode);
        Assert.Equal(25d, distanceDraft.WallPlatePlacement.SeatingDepth.Value);
        Assert.True(viewModel.TryGetPreviewPlan(out var distancePreview));
        Assert.All(distancePreview!.Items, item =>
        {
            Assert.Equal(RoofAutomaticPurlinGeneratorRole.WallPlate, item.GeneratorRole);
            Assert.NotNull(item.ElevationProfile!.SeatingDepthMm);
            Assert.Equal(viewModel.RafterHeightMm * 0.25d, item.ElevationProfile.SeatingDepthMm!.Value, 9);
        });
    }

    [Fact]
    public void BottomEdge_SignedOffset_BelowElevatedExplicitLocalPlane_IsAccepted()
    {
        var solved = Solve(RectanglePoints());
        var datum = new RoofRelativeElevationDatum(
            RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
            0d,
            1000d);
        var viewModel = CreateViewModel(
            solved,
            null,
            false,
            datum,
            true,
            CultureInfo.GetCultureInfo("sk"),
            AutomaticPurlinDialogMode.ProductionEdit);
        ClearIntermediateRows(viewModel);
        viewModel.RidgeEnabled = false;
        viewModel.WallPlateEnabled = true;
        viewModel.ReferenceKind = RoofRelativeElevationReferenceKind.ExplicitLocalPlane;
        viewModel.ReferenceLocalZText = "1000";
        viewModel.RelativeReferenceText = "0";
        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
        viewModel.WallPlateRow.WidthText = "140";
        viewModel.WallPlateRow.HeightText = "140";
        viewModel.WallPlateRow.SelectedSeatingMode =
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight;
        viewModel.WallPlateRow.SeatingDepthValueText = "25";

        Assert.Contains(
            "Výškový rozdiel SH voči referencii",
            viewModel.WallPlateRow.ValueLabel,
            StringComparison.Ordinal);

        foreach (var (text, offsetMm) in new[]
                 {
                     ("-200", -200d),
                     ("0", 0d),
                     ("+20", 20d),
                     ("20", 20d),
                 })
        {
            viewModel.WallPlateRow.PlacementValueText = text;
            viewModel.WallPlateRow.RefreshFieldValidation();
            Assert.False(
                viewModel.WallPlateRow.PlacementValueHasError,
                $"offset text '{text}' should parse");
            Assert.True(viewModel.TryGetPreviewPlan(out var plan), text);
            Assert.All(
                plan!.Items.Where(item =>
                    item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate),
                item =>
                {
                    Assert.Equal(offsetMm, item.ElevationProfile!.BottomRelativeElevationMm, 3);
                    Assert.Equal(
                        1000d + offsetMm,
                        item.ElevationProfile.BottomLocalZMm,
                        3);
                });
            Assert.True(viewModel.CanApply || viewModel.TryCreateDraft(out _, out _), text);
        }

        // Far below physical roof: field accepts the signed number; Apply is blocked.
        viewModel.WallPlateRow.PlacementValueText = "-5000";
        viewModel.WallPlateRow.RefreshFieldValidation();
        Assert.False(viewModel.WallPlateRow.PlacementValueHasError);
        Assert.False(viewModel.CanApply);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ValidationMessage));
    }

    [Fact]
    public void WallPlateBottomReference_ResolvesActualLowerEdgeDatumForPreviewAndDraft()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        viewModel.WallPlateEnabled = true;
        viewModel.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
        viewModel.RelativeReferenceText = "±0.000";
        viewModel.WallPlateRow.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        viewModel.WallPlateRow.PlacementValueText = "500";

        Assert.True(viewModel.TryCreateDraft(out _, out var datum));
        Assert.Equal(RoofRelativeElevationReferenceKind.WallPlateBottom, datum!.ReferenceKind);
        Assert.Equal(0d, datum.ReferenceRelativeElevationMm, 9);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan));
        var walls = plan!.Items
            .Where(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate)
            .ToArray();
        Assert.NotEmpty(walls);
        Assert.All(
            walls,
            item =>
            {
                Assert.Equal(0d, item.ElevationProfile!.BottomRelativeElevationMm, 9);
                Assert.Equal(item.HeightMm, item.ElevationProfile.TopRelativeElevationMm -
                    item.ElevationProfile.BottomRelativeElevationMm, 9);
                Assert.Equal(40d, item.ElevationProfile.SeatingDepthMm);
                Assert.Equal(datum.ReferenceLocalZMm, item.ElevationProfile.BottomLocalZMm, 9);
            });
        Assert.True(double.IsFinite(datum.ReferenceLocalZMm));
        Assert.Contains(
            datum.ReferenceLocalZMm.ToString("0.###", CultureInfo.InvariantCulture),
            viewModel.ReferenceLocalZText.Replace(',', '.'),
            StringComparison.Ordinal);

        viewModel.AddRow();
        viewModel.Rows[0].PlacementMode =
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
        viewModel.Rows[0].PlacementValueText = "800";
        Assert.True(viewModel.TryGetPreviewPlan(out var withIntermediate));
        Assert.All(
            withIntermediate!.Items.Where(item =>
                item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate),
            item =>
            {
                Assert.Equal(40d, item.ElevationProfile!.SeatingDepthMm);
                Assert.Equal(
                    datum.ReferenceLocalZMm + item.ElevationProfile.BottomRelativeElevationMm,
                    item.ElevationProfile.BottomLocalZMm,
                    9);
            });
    }

    [Fact]
    public void WallPlateLowerEdgeHeight_ZeroVersusThousand_MakesDraftLayoutUnequal()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        viewModel.WallPlateEnabled = true;
        viewModel.WallPlateRow.PlacementMode =
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
        viewModel.WallPlateRow.PlacementValueText = "0";
        Assert.True(viewModel.TryCreateDraft(out var atZero, out _));

        viewModel.WallPlateRow.PlacementValueText = "1000";
        Assert.True(viewModel.TryCreateDraft(out var atThousand, out _));

        Assert.Equal(0d, atZero!.WallPlateLowerEdgeHeightMm);
        Assert.Equal(1000d, atThousand!.WallPlateLowerEdgeHeightMm);
        Assert.NotEqual(atZero, atThousand);
    }

    [Fact]
    public void WallPlateToggle_UsesCorePlannerForExpectedRectangleCombinations()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        viewModel.RidgeEnabled = false;
        ClearIntermediateRows(viewModel);
        viewModel.WallPlateEnabled = false;

        Assert.True(viewModel.TryGetPreviewPlan(out var empty));
        Assert.Empty(empty!.Items);

        viewModel.WallPlateEnabled = true;
        Assert.True(viewModel.TryGetPreviewPlan(out var wallPlatesOnly));
        Assert.Equal(4, wallPlatesOnly!.Items.Count);
        Assert.All(wallPlatesOnly.Items, item =>
            Assert.Equal(RoofAutomaticPurlinGeneratorRole.WallPlate, item.GeneratorRole));

        viewModel.RidgeEnabled = true;
        Assert.True(viewModel.TryGetPreviewPlan(out var wallPlatesAndRidge));
        Assert.Equal(4, wallPlatesAndRidge!.Items.Count(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate));
        Assert.Single(
            wallPlatesAndRidge.Items,
            item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);

        _ = viewModel.AddRow();
        Assert.True(viewModel.TryGetPreviewPlan(out var mixed));
        Assert.Equal(4, mixed!.Items.Count(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate));
        Assert.Single(
            mixed.Items,
            item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);
        Assert.Equal(4, mixed.Items.Count(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate));
    }

    [Fact]
    public void MissingDatum_AllowsInMemoryReferenceToEnableFiveItemPreview()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        ClearIntermediateRows(viewModel);

        Assert.False(viewModel.DatumExists);
        viewModel.WallPlateEnabled = false;
        viewModel.RelativeReferenceText = "+3.580";
        viewModel.RidgeEnabled = true;
        var row = viewModel.AddRow();
        row.PlacementMode = RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
        row.PlacementValueText = "500";

        Assert.True(viewModel.CanPreview);
        Assert.Equal("40 mm", row.SeatingDepthDerived);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan));
        Assert.Equal(5, plan!.Items.Count);
        Assert.All(
            plan.Items.Where(item =>
                item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate),
            item =>
            {
                Assert.Equal(40d, item.ElevationProfile!.SeatingDepthMm);
                Assert.NotNull(item.PhysicalPlacement);
                Assert.Equal(40d, item.PhysicalPlacement!.SeatingDepthMm);
                Assert.True(
                    item.PhysicalPlacement.PurlinTopLocalZMm >
                    item.PhysicalPlacement.PurlinBottomLocalZMm);
            });
    }

    [Fact]
    public void BottomEdgeDraft_ExposesCoreDerivedElevationsAndFivePreviewSegments()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(
            solved,
            RoofAutomaticPurlinLayout.Empty,
            layoutExists: true,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                3580d,
                0d),
            true);

        viewModel.RidgeEnabled = true;
        var row = viewModel.AddRow();
        row.PlacementMode = RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
        row.PlacementValueText = "500";

        Assert.Equal("500", row.PlacementValueText);
        Assert.Equal("160 × 220 mm", viewModel.PurlinSectionText);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan));
        Assert.Equal(5, plan!.Items.Count);
        Assert.Equal(4, plan.Items.Count(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate));
        Assert.All(plan.Items.Where(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate), item =>
        {
            Assert.Equal(40d, item.ElevationProfile!.SeatingDepthMm);
            Assert.NotNull(item.PhysicalPlacement);
            Assert.Equal(40d, item.PhysicalPlacement!.SeatingDepthMm);
            Assert.True(
                item.PhysicalPlacement.PurlinTopLocalZMm >
                item.PhysicalPlacement.PurlinBottomLocalZMm);
        });
        var intermediateProfile = plan.Items
            .First(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate)
            .ElevationProfile!;
        Assert.Equal(
            row.BottomRelative,
            RoofRelativeElevationDatumRules.FormatMetres(
                intermediateProfile.BottomRelativeElevationMm));
    }

    [Fact]
    public void ModeSwitch_UpdatesDynamicLabelAndAutoSelectsSingleHorizontalRidge()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        ClearIntermediateRows(viewModel);
        var row = viewModel.AddRow();

        row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        Assert.Equal(
            UiStrings.GetString("AutomaticPurlin_ValueEaveDistance", CultureInfo.GetCultureInfo("en")),
            row.ValueLabel);
        Assert.Null(row.SelectedRidgeReference);
        Assert.True(row.IsSeatingApplicable);
        Assert.Equal(RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            row.SelectedSeatingMode);
        Assert.Equal("25", row.SeatingDepthValueText);
        Assert.Equal("%", row.SeatingUnit);
        Assert.Contains("25", row.SeatingPolicyText, StringComparison.Ordinal);

        row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;
        Assert.Single(row.RidgeReferences);
        Assert.NotNull(row.SelectedRidgeReference);
        Assert.False(row.IsRidgeReferenceVisible);
        Assert.True(viewModel.TryCreateDraft(out var layout, out _));
        Assert.Equal(
            row.SelectedRidgeReference!.Key,
            Assert.Single(layout!.IntermediateItems).ReferenceRidgeKey);
    }

    [Fact]
    public void DistanceMode_DefaultSeatingProducesPhysicalDerivedValuesAndPreservesIdentity()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        ClearIntermediateRows(viewModel);
        var row = viewModel.AddRow();
        var layoutItemId = row.LayoutItemId;

        row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        row.PlacementValueText = "500";

        Assert.True(viewModel.CanPreview);
        Assert.Equal(layoutItemId, row.LayoutItemId);
        Assert.Equal("500 mm", row.PlanPosition);
        Assert.NotEqual("—", row.RoofPlaneRelative);
        Assert.Equal("40 mm", row.SeatingDepthDerived);
        Assert.NotEqual("—", row.BottomRelative);
        Assert.NotEqual("—", row.CenterRelative);
        Assert.NotEqual("—", row.TopRelative);
        Assert.True(viewModel.TryCreateDraft(out var layout, out _));
        var draftItem = Assert.Single(layout!.IntermediateItems);
        Assert.Equal(layoutItemId, draftItem.LayoutItemId);
        Assert.Equal(
            new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                25d),
            draftItem.SeatingDepth);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan));
        var intermediate = plan!.Items.Where(item =>
            item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate).ToArray();
        Assert.Equal(4, intermediate.Length);
        Assert.All(intermediate, item => Assert.NotNull(item.PhysicalPlacement));
        Assert.All(intermediate, item => Assert.Equal(
            intermediate[0].Segment3D.Start.Z,
            item.Segment3D.Start.Z,
            9));
    }

    [Fact]
    public void AbsoluteSeating_IsStrictlyValidatedAgainstRafterHeight()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        ClearIntermediateRows(viewModel);
        var row = viewModel.AddRow();
        row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        row.SelectedSeatingMode = RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm;
        row.SeatingDepthValueText = "40";

        Assert.True(viewModel.TryCreateDraft(out var layout, out _));
        Assert.Equal(
            new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm,
                40d),
            Assert.Single(layout!.IntermediateItems).SeatingDepth);
        Assert.Equal("mm", row.SeatingUnit);
        Assert.True(viewModel.CanPreview);

        row.SeatingDepthValueText = "161";

        Assert.True(viewModel.CanPreview); // last valid preview retained
        Assert.False(viewModel.TryCreateDraft(out _, out _));
        Assert.NotEmpty(viewModel.ValidationMessage);
        Assert.Equal("Out of range", viewModel.ValidationMessage);

        row.SeatingDepthValueText = "160";
        Assert.True(
            viewModel.TryGetPreviewPlan(out _),
            viewModel.ValidationMessage + " / " + viewModel.PreviewDiagnosticReason);
        Assert.True(viewModel.TryCreateDraft(out var full, out _));
        Assert.Equal(
            new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm,
                160d),
            Assert.Single(full!.IntermediateItems).SeatingDepth);
    }

    [Fact]
    public void PercentSeating_InclusiveZeroAndOneHundredRemainValid_OutsideShowsOutOfRange()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        Assert.True(viewModel.TryGetPreviewPlan(out var before));
        var beforeCount = before!.Items.Count;
        var beforeKeys = before.Items.Select(item => item.GeneratedKey).ToArray();

        viewModel.WallPlateRow.SeatingDepthValueText = "0";
        viewModel.RidgeSeatingDepthValueText = "0";
        viewModel.Rows[0].SeatingDepthValueText = "0";
        Assert.True(viewModel.CanPreview);
        Assert.True(viewModel.TryGetPreviewPlan(out var at0));
        Assert.Equal(beforeCount, at0!.Items.Count);
        Assert.Equal(beforeKeys, at0.Items.Select(item => item.GeneratedKey));

        viewModel.WallPlateRow.SeatingDepthValueText = "100";
        viewModel.RidgeSeatingDepthValueText = "100";
        viewModel.Rows[0].SeatingDepthValueText = "100";
        Assert.True(viewModel.CanPreview);
        Assert.True(viewModel.TryGetPreviewPlan(out var at100));
        Assert.Equal(beforeCount, at100!.Items.Count);
        Assert.Equal(beforeKeys, at100.Items.Select(item => item.GeneratedKey));
        Assert.All(at100.Items, item =>
        {
            Assert.Equal(viewModel.RafterHeightMm, item.ElevationProfile!.SeatingDepthMm);
            Assert.NotNull(item.PhysicalPlacement);
            var tanPitch = Math.Tan(solved.Geometry.PrimarySlopeDegrees * Math.PI / 180d);
            Assert.Equal(
                item.PhysicalPlacement!.RafterUpperSurfaceLocalZMm -
                (item.WidthMm / 2d) * tanPitch,
                item.PhysicalPlacement.PurlinTopLocalZMm,
                8);
        });

        viewModel.Rows[0].SeatingDepthValueText = "101";
        Assert.True(viewModel.CanPreview); // last valid preview retained
        Assert.False(viewModel.TryCreateDraft(out _, out _));
        Assert.Equal("Out of range", viewModel.ValidationMessage);
        Assert.Equal("101", viewModel.Rows[0].SeatingDepthValueText);

        viewModel.Rows[0].SeatingDepthValueText = "-1";
        Assert.True(viewModel.CanPreview);
        Assert.False(viewModel.TryCreateDraft(out _, out _));
        Assert.Equal("Out of range", viewModel.ValidationMessage);
        Assert.Equal("-1", viewModel.Rows[0].SeatingDepthValueText);

        viewModel.Rows[0].SeatingDepthValueText = "50";
        Assert.True(viewModel.CanPreview);
        Assert.True(string.IsNullOrEmpty(viewModel.ValidationMessage));
    }

    [Fact]
    public void BottomEdgeMode_KeepsBottomEdgePlacementAndAllowsSeating()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        ClearIntermediateRows(viewModel);
        var row = viewModel.AddRow();
        row.PlacementMode = RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
        row.PlacementValueText = "500";

        Assert.True(row.IsSeatingApplicable);
        Assert.True(viewModel.TryCreateDraft(out var layout, out _));
        Assert.Equal(
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            Assert.Single(layout!.IntermediateItems).PlacementMode);
        Assert.NotNull(Assert.Single(layout.IntermediateItems).SeatingDepth);
        Assert.Equal(25d, Assert.Single(layout.IntermediateItems).SeatingDepth!.Value);
    }

    [Fact]
    public void MultipleHorizontalRidgesRequireFriendlyExplicitSelection()
    {
        var solved = Solve(
        [
            new(0, 0), new(8000, 0), new(8000, 3000),
            new(3000, 3000), new(3000, 8000), new(0, 8000),
        ]);
        Assert.True(Ridges(solved).Count > 1);
        var viewModel = CreateViewModel(solved, null, false, null, false);
        ClearIntermediateRows(viewModel);
        viewModel.RidgeEnabled = false;
        viewModel.WallPlateEnabled = false;
        var row = viewModel.AddRow();

        row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;

        Assert.True(row.IsRidgeReferenceVisible);
        Assert.Null(row.SelectedRidgeReference);
        Assert.All(row.RidgeReferences, option =>
        {
            Assert.StartsWith("Ridge ", option.Label, StringComparison.Ordinal);
            Assert.DoesNotContain("|", option.Label, StringComparison.Ordinal);
        });
        Assert.False(viewModel.TryCreateDraft(out _, out _));
        Assert.NotEmpty(viewModel.ValidationMessage);
        Assert.True(row.RidgeReferenceHasError);

        row.SelectedRidgeReference = row.RidgeReferences[0];

        Assert.True(viewModel.CanPreview, viewModel.ValidationMessage + " / " + viewModel.PreviewDiagnosticReason);
        Assert.True(viewModel.TryCreateDraft(out var layout, out _));
        Assert.Equal(
            row.RidgeReferences[0].Key,
            Assert.Single(layout!.IntermediateItems).ReferenceRidgeKey);
    }

    [Fact]
    public void AddRemovePreservesCanonicalDraftIdentityAndInvalidValueClearsPreview()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(solved, null, false, null, false);
        ClearIntermediateRows(viewModel);
        var first = viewModel.AddRow();
        var second = viewModel.AddRow();

        Assert.Equal("Intermediate purlin 1", first.DisplayName);
        Assert.Equal("Intermediate purlin 2", second.DisplayName);
        Assert.Matches("^[0-9a-f]{32}$", first.LayoutItemId);
        Assert.Matches("^[0-9a-f]{32}$", second.LayoutItemId);
        Assert.NotEqual(first.LayoutItemId, second.LayoutItemId);
        Assert.True(viewModel.TryGetPreviewPlan(out _));

        first.PlacementValueText = "invalid";
        Assert.True(viewModel.TryGetPreviewPlan(out _)); // last valid preview retained
        Assert.True(viewModel.CanPreview);
        Assert.False(viewModel.TryCreateDraft(out _, out _));
        Assert.NotEmpty(viewModel.ValidationMessage);

        viewModel.RemoveRow(first);
        Assert.Single(viewModel.Rows);
        Assert.Same(second, viewModel.Rows[0]);
        Assert.Equal("Intermediate purlin 1", second.DisplayName);
        Assert.True(viewModel.TryGetPreviewPlan(out _));
        Assert.True(string.IsNullOrEmpty(viewModel.ValidationMessage));
    }

    [Fact]
    public void AdvancedDatum_LocalZAppearsOnlyForExplicitLocalPlane()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var viewModel = CreateViewModel(
                Solve(RectanglePoints()),
                null,
                false,
                null,
                false,
                AppLanguageService.CurrentUiCulture);
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();

            Assert.Equal(
                UiStrings.GetString("AutomaticPurlin_ReferenceLocalZHelp"),
                window.LocalReferencePanel.ToolTip);

            // Default new-draft datum is SourceEavePlane — Lokálna poloha stays hidden.
            Assert.False(viewModel.IsLocalReferencePositionVisible);
            Assert.False(window.LocalReferencePanel.IsVisible);

            viewModel.ReferenceKind = RoofRelativeElevationReferenceKind.ExplicitLocalPlane;
            window.UpdateLayout();
            Assert.True(viewModel.IsLocalReferencePositionVisible);
            Assert.True(window.LocalReferencePanel.IsVisible);

            viewModel.ReferenceKind = RoofRelativeElevationReferenceKind.WallPlateBottom;
            window.UpdateLayout();
            Assert.False(viewModel.IsLocalReferencePositionVisible);
            Assert.False(window.LocalReferencePanel.IsVisible);

            window.Close();
        });
    }

    [Fact]
    public void RowPlacementMode_SwitchesContextualSeatingAndExpectedDerivedValues()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var viewModel = CreateViewModel(
                Solve(RectanglePoints()),
                null,
                false,
                null,
                false,
                AppLanguageService.CurrentUiCulture);
            var row = Assert.Single(viewModel.Rows);
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();
            SelectIntermediateEditorTab(window, viewModel);
            window.UpdateLayout();

            var seatingControls = Descendants<FrameworkElement>(window)
                .First(element =>
                    (element.Name == "SeatingControlsPanel" ||
                     Equals(element.Tag, "SeatingControlsPanel")) &&
                    element.IsVisible);
            var heightStatus = Descendants<FrameworkElement>(window)
                .First(element =>
                    element.Name == "HeightModeSeatingStatus" ||
                    Equals(element.Tag, "HeightModeSeatingStatus"));
            Assert.True(seatingControls.IsVisible);
            Assert.False(heightStatus.IsVisible);
            Assert.Equal(RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                row.SelectedSeatingMode);
            Assert.Equal("25", row.SeatingDepthValueText);

            row.PlacementMode = RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;
            row.PlacementValueText = "800";
            window.UpdateLayout();

            Assert.True(seatingControls.IsVisible);
            Assert.False(heightStatus.IsVisible);
            Assert.True(row.IsSeatingApplicable);

            row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
            row.PlacementValueText = "1000";
            window.UpdateLayout();

            Assert.True(seatingControls.IsVisible);
            Assert.False(heightStatus.IsVisible);
            Assert.Equal(RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                row.SelectedSeatingMode);
            Assert.Equal("25", row.SeatingDepthValueText);
            Assert.Equal("40 mm", row.SeatingDepthDerived);
            Assert.True(viewModel.TryGetPreviewPlan(out var draftPlan) && draftPlan is not null);
            var intermediate = draftPlan!.Items.First(item =>
                item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate &&
                string.Equals(item.LayoutItemId, row.LayoutItemId, StringComparison.Ordinal));
            Assert.True(
                RoofAutomaticPurlinRoofPlaneRules.TryResolveFromPlanItem(
                    intermediate,
                    30d,
                    viewModel.RafterHeightMm,
                    out var roofPlaneRelativeMm));
            Assert.Equal(
                RoofRelativeElevationDatumRules.FormatMetres(
                    roofPlaneRelativeMm,
                    AppLanguageService.CurrentUiCulture),
                row.RoofPlaneRelative);
            Assert.Equal(
                RoofRelativeElevationDatumRules.FormatMetres(
                    intermediate.ElevationProfile!.BottomRelativeElevationMm,
                    AppLanguageService.CurrentUiCulture),
                row.BottomRelative);
            Assert.Equal(
                RoofRelativeElevationDatumRules.FormatMetres(
                    intermediate.ElevationProfile.CenterRelativeElevationMm,
                    AppLanguageService.CurrentUiCulture),
                row.CenterRelative);
            Assert.Equal(
                RoofRelativeElevationDatumRules.FormatMetres(
                    intermediate.ElevationProfile.TopRelativeElevationMm,
                    AppLanguageService.CurrentUiCulture),
                row.TopRelative);
            Assert.Equal(220d,
                intermediate.ElevationProfile.TopRelativeElevationMm -
                intermediate.ElevationProfile.BottomRelativeElevationMm,
                6);

            row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;
            window.UpdateLayout();

            Assert.True(seatingControls.IsVisible);
            Assert.False(heightStatus.IsVisible);
            window.Close();
        });
    }

    [Fact]
    public void FiveRows_ScrollInsideBoundedBodyWhileFooterStaysVisible()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var viewModel = CreateViewModel(Solve(RectanglePoints()), null, false, null, false);
            for (var index = 0; index < 5; index++)
            {
                viewModel.AddRow();
            }

            var window = CreateOffscreenWindow(viewModel);
            window.Height = window.MinHeight;
            window.Show();
            window.UpdateLayout();
            SelectIntermediateEditorTab(window, viewModel, intermediateIndex: viewModel.Rows.Count - 1);
            window.UpdateLayout();

            Assert.Equal(2 + viewModel.Rows.Count, viewModel.EditorTabs.Count);
            Assert.NotNull(window.DialogScrollViewer);
            Assert.Equal(0d, window.DialogScrollViewer!.ScrollableWidth);
            Assert.Equal(ScrollBarVisibility.Disabled,
                window.DialogScrollViewer.HorizontalScrollBarVisibility);
            Assert.True(window.FooterPanel.IsVisible);
            Assert.False(IsDescendantOf(window.FooterPanel, window.DialogScrollViewer));
            Assert.True(IsDescendantOf(window.IntermediateRowsControl!, window.DialogScrollViewer));
            Assert.Contains(window.CancelButton, Descendants<Button>(window.FooterPanel));
            Assert.Contains(window.PreviewButton, Descendants<Button>(window.FooterPanel));
            window.Close();
        });
    }

    [Fact]
    public void MinimumWindowWidthKeepsConfigurationControlsAndSectionUsableWithoutHorizontalScroll()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var viewModel = CreateViewModel(Solve(RectanglePoints()), null, false, null, false);
            viewModel.WallPlateEnabled = true;
            viewModel.RidgeEnabled = true;
            viewModel.AddRow();
            viewModel.Rows[0].PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
            viewModel.Rows[0].PlacementValueText = "1000";
            var window = CreateOffscreenWindow(viewModel);
            window.Width = 1400d;
            window.Height = 680d;
            window.Show();
            window.UpdateLayout();
            SelectIntermediateEditorTab(window, viewModel);
            window.UpdateLayout();

            Assert.True(window.ActualWidth >= 1399d);
            Assert.NotNull(window.DialogScrollViewer);
            Assert.True(window.DialogScrollViewer!.ActualWidth >= 600d);
            Assert.Equal(0d, window.DialogScrollViewer.ScrollableWidth);
            Assert.Equal(
                ScrollBarVisibility.Disabled,
                window.DialogScrollViewer.HorizontalScrollBarVisibility);
            Assert.All(
                Descendants<ComboBox>(window.DialogScrollViewer).Where(control => control.IsVisible),
                control => Assert.True(control.ActualWidth >= 55d));
            Assert.All(
                Descendants<TextBox>(window.DialogScrollViewer).Where(control => control.IsVisible),
                control => Assert.True(control.ActualWidth >= 55d));
            Assert.True(window.RoofSectionView.ActualWidth >= 599d);
            Assert.Equal(2, viewModel.SectionPresentation.Rafters.Count);
            Assert.Equal(7, viewModel.SectionPresentation.Members.Count);
            window.Close();
        });
    }

    [Fact]
    public void AddAndCompactRemoveButtons_PreserveRowBindings()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var viewModel = CreateViewModel(Solve(RectanglePoints()), null, false, null, false);
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            SelectIntermediateEditorTab(window, viewModel);
            window.UpdateLayout();
            Assert.Single(viewModel.Rows);
            window.AddRowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();

            Assert.Equal(2, viewModel.Rows.Count);
            Assert.Equal(
                AutomaticPurlinEditorTabKind.Intermediate,
                viewModel.SelectedEditorTab?.Kind);
            Assert.Same(viewModel.Rows[1], viewModel.SelectedEditorTab?.IntermediateRow);
            var remove = Descendants<Button>(window.IntermediateRowsControl!)
                .Single(button => ReferenceEquals(button.CommandParameter, viewModel.Rows[1]));
            Assert.Equal(0d, remove.MinWidth);

            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Single(viewModel.Rows);
            window.Close();
        });
    }

    [Fact]
    public void MissingBoundaryIdentityFailsPreviewWithoutCreatingFallbackIdentity()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = new AutomaticPurlinDialogViewModel(
            solved.Geometry,
            null,
            RoofAutomaticPurlinLayout.Empty,
            false,
            null,
            false,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            TimberElementDefaults.For(TimberElementType.Rafter),
            CultureInfo.GetCultureInfo("en"));

        viewModel.RidgeEnabled = true;

        Assert.False(viewModel.CanPreview);
        Assert.False(viewModel.TryGetPreviewPlan(out _));
        Assert.Contains("boundary identity", viewModel.ValidationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowLoadsInEveryLanguageAndShowsApplyOnlyInProductionMode()
    {
        RunSta(() =>
        {
            var solved = Solve(RectanglePoints());
            foreach (var language in new[] { "sk", "cs", "en", "de", "pl", "fr" })
            {
                AppLanguageService.Apply(language);
                var viewModel = CreateViewModel(
                    solved,
                    null,
                    false,
                    null,
                    false,
                    AppLanguageService.CurrentUiCulture);
                var window = new AutomaticPurlinDialogWindow(viewModel, SettingsTheme.Light)
                {
                    Left = -30000,
                    Top = -30000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };
                window.Show();
                window.UpdateLayout();
                SelectIntermediateEditorTab(window, viewModel);
                window.UpdateLayout();

                Assert.NotEqual("AutomaticPurlin_Title", window.Title);
                Assert.Same(window.FindResource("SettingsPrimaryButtonStyle"), window.PreviewButton.Style);
                Assert.Same(window.FindResource("SettingsSecondaryButtonStyle"), window.CancelButton.Style);
                Assert.Contains(window.PreviewButton, Descendants<Button>(window));
                Assert.Contains(window.CancelButton, Descendants<Button>(window));
                Assert.NotNull(window.ApplyButton);
                Assert.False(window.ApplyButton.IsVisible);
                Assert.Equal(ScrollBarVisibility.Auto,
                    window.DialogScrollViewer!.VerticalScrollBarVisibility);
                window.Close();

                var productionViewModel = CreateViewModel(
                    solved,
                    null,
                    false,
                    null,
                    false,
                    AppLanguageService.CurrentUiCulture,
                    AutomaticPurlinDialogMode.ProductionEdit);
                productionViewModel.RidgeEnabled = true;
                var productionWindow = CreateOffscreenWindow(productionViewModel);
                productionWindow.Show();
                productionWindow.UpdateLayout();

                Assert.True(productionWindow.ApplyButton.IsVisible);
                Assert.True(productionWindow.ApplyButton.IsEnabled);
                Assert.Same(
                    productionWindow.FindResource("SettingsPrimaryButtonStyle"),
                    productionWindow.ApplyButton.Style.BasedOn);
                productionWindow.Close();
            }
        });
    }

    [Fact]
    public void ProductionApplyRequiresValidDesiredOrExistingMembersAndPreservesDraftOnFailure()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(
            solved,
            null,
            false,
            null,
            false,
            mode: AutomaticPurlinDialogMode.ProductionEdit);
        ClearIntermediateRows(viewModel);
        viewModel.RidgeEnabled = false;
        viewModel.WallPlateEnabled = false;

        Assert.True(viewModel.IsProductionEdit);
        Assert.True(viewModel.CanPreview);
        Assert.False(viewModel.CanApply);

        viewModel.RidgeEnabled = true;

        Assert.True(viewModel.CanApply);
        Assert.True(viewModel.TryBeginApply(out var layout, out var datum, out var preview));
        Assert.NotNull(layout);
        Assert.NotNull(datum);
        Assert.NotEmpty(preview!.Items);
        Assert.False(viewModel.CanApply);

        viewModel.CompleteApplyFailure();

        Assert.True(viewModel.RidgeEnabled);
        Assert.True(viewModel.CanApply);
        Assert.Contains("could not", viewModel.ValidationMessage, StringComparison.OrdinalIgnoreCase);

        viewModel.RelativeReferenceText = "invalid";

        Assert.False(viewModel.CanApply);
        Assert.False(viewModel.TryBeginApply(out _, out _, out _));
    }

    [Fact]
    public void ExistingGeneratedMembersAllowExplicitEmptyApplyForRemoval()
    {
        var viewModel = CreateViewModel(
            Solve(RectanglePoints()),
            RoofAutomaticPurlinLayout.Empty,
            true,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                0d,
                0d),
            true,
            mode: AutomaticPurlinDialogMode.ProductionEdit,
            existingAutomaticPurlinCount: 4);

        Assert.True(viewModel.CanApply);
        Assert.True(viewModel.TryBeginApply(out var layout, out _, out var preview));
        Assert.False(layout!.RidgeEnabled);
        Assert.Empty(layout.IntermediateItems);
        Assert.Empty(preview!.Items);
    }

    [Fact]
    public void CompactSchematicTracksSelectedRowAndPlacementCues()
    {
        var viewModel = CreateViewModel(
            Solve(RectanglePoints()),
            null,
            false,
            null,
            false);
        ClearIntermediateRows(viewModel);
        viewModel.RidgeEnabled = false;
        var first = viewModel.AddRow();
        var second = viewModel.AddRow();

        Assert.Same(second, viewModel.SelectedRow);
        Assert.False(first.IsSchematicSelected);
        Assert.True(second.IsSchematicSelected);
        Assert.True(viewModel.ShowEaveDistanceCue);
        Assert.True(viewModel.ShowSeatingCue);
        Assert.False(viewModel.ShowVerticalHeightCue);
        Assert.True(viewModel.SchematicAnyIntermediateEnabled);
        Assert.True(viewModel.SchematicIntermediateEmphasized);
        Assert.False(viewModel.SchematicRidgeActive);

        viewModel.SelectedRow = first;
        first.PlacementMode = RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference;

        Assert.True(first.IsSchematicSelected);
        Assert.False(second.IsSchematicSelected);
        Assert.True(viewModel.ShowVerticalHeightCue);
        Assert.True(viewModel.ShowSeatingCue);

        first.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;

        Assert.True(viewModel.ShowEaveDistanceCue);
        Assert.True(viewModel.ShowSeatingCue);

        first.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge;

        Assert.True(viewModel.ShowRidgeDistanceCue);
        Assert.False(viewModel.ShowEaveDistanceCue);
    }

    [Fact]
    public void SchematicHighlightsTrackRidgeFocusAndEnabledIntermediateRows()
    {
        var viewModel = CreateViewModel(
            Solve(RectanglePoints()),
            null,
            false,
            null,
            false);

        Assert.True(viewModel.SchematicRidgeActive);
        Assert.False(viewModel.SchematicRidgeEmphasized);
        Assert.True(viewModel.SchematicAnyIntermediateEnabled);
        Assert.True(viewModel.SchematicIntermediateEmphasized);
        Assert.Contains("automatic-purlin-roof-section.png", viewModel.SchematicImagePackUri);

        viewModel.RidgeEnabled = false;
        Assert.False(viewModel.SchematicRidgeActive);
        Assert.False(viewModel.SchematicRidgeEmphasized);

        viewModel.RidgeEnabled = true;
        Assert.True(viewModel.SchematicRidgeActive);
        Assert.False(viewModel.SchematicRidgeEmphasized);

        viewModel.SetRidgeInteractionActive(true);
        Assert.True(viewModel.SchematicRidgeEmphasized);

        var row = viewModel.Rows[0];
        row.Enabled = true;
        Assert.True(viewModel.SchematicAnyIntermediateEnabled);
        Assert.True(viewModel.SchematicIntermediateEmphasized);

        viewModel.SetIntermediateInteractionActive(true);
        Assert.False(viewModel.SchematicRidgeEmphasized);
        Assert.True(viewModel.SchematicIntermediateEmphasized);

        row.Enabled = false;
        Assert.False(viewModel.SchematicAnyIntermediateEnabled);
        Assert.False(viewModel.SchematicIntermediateEmphasized);
        Assert.True(viewModel.SchematicRidgeActive);
    }

    [Fact]
    public void ProductionApplyUsesTheExactCurrentPreviewPlan()
    {
        var solved = Solve(RectanglePoints());
        var viewModel = CreateViewModel(
            solved,
            null,
            false,
            new RoofRelativeElevationDatum(
                RoofRelativeElevationReferenceKind.ExplicitLocalPlane,
                3580d,
                0d),
            true,
            mode: AutomaticPurlinDialogMode.ProductionEdit);
        ClearIntermediateRows(viewModel);
        viewModel.RidgeEnabled = true;
        var row = viewModel.AddRow();
        row.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        row.PlacementValueText = "1000";

        Assert.True(viewModel.TryBeginApply(out var layout, out var datum, out var preview));
        var wallPlate = layout!.WallPlatePlacement;
        var direct = RoofAutomaticPurlinPlanner.Create(
            solved.Geometry,
            solved.Provenance,
            layout,
            new RoofAutomaticPurlinPlanningInput(
                datum!,
                TimberElementDefaults.For(TimberElementType.Purlin).HeightMm,
                TimberElementDefaults.For(TimberElementType.Rafter).HeightMm)
            {
                PurlinWidthMm = TimberElementDefaults.For(TimberElementType.Purlin).WidthMm,
                WallPlatesEnabled = layout.WallPlateEnabled,
                WallPlateWidthMm = wallPlate?.WidthMm ??
                    TimberElementDefaults.For(TimberElementType.WallPlate).WidthMm,
                WallPlateHeightMm = wallPlate?.HeightMm ??
                    TimberElementDefaults.For(TimberElementType.WallPlate).HeightMm,
            });

        Assert.True(direct.IsValid);
        Assert.Equal(direct.Plan!.Items, preview!.Items);
    }

    [Fact]
    public void WallPlateWidthHeightEdit_FlowsIntoLayoutAndPlanner()
    {
        var viewModel = CreateViewModel(Solve(RectanglePoints()), null, false, null, false);
        viewModel.WallPlateEnabled = true;
        viewModel.WallPlateRow.WidthText = "150";
        viewModel.WallPlateRow.HeightText = "180";

        Assert.True(viewModel.TryCreateDraft(out var layout, out _));
        Assert.Equal(150d, layout!.WallPlatePlacement!.WidthMm);
        Assert.Equal(180d, layout.WallPlatePlacement.HeightMm);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan));
        Assert.All(
            plan!.Items.Where(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.WallPlate),
            item =>
            {
                Assert.Equal(150d, item.WidthMm, 9);
                Assert.Equal(180d, item.HeightMm, 9);
                Assert.Equal(40d, item.ElevationProfile!.SeatingDepthMm);
                Assert.Equal(
                    item.ElevationProfile.TopRelativeElevationMm -
                    item.ElevationProfile.BottomRelativeElevationMm,
                    item.HeightMm,
                    9);
            });
    }

    [Fact]
    public void RidgeWidthHeightEdit_FlowsIntoLayoutAndPlanner()
    {
        var viewModel = CreateViewModel(Solve(RectanglePoints()), null, false, null, false);
        viewModel.RidgeEnabled = true;
        viewModel.RidgeWidthText = "175";
        viewModel.RidgeHeightText = "250";

        Assert.True(viewModel.TryCreateDraft(out var layout, out _));
        Assert.Equal(175d, layout!.RidgeWidthMm);
        Assert.Equal(250d, layout.RidgeHeightMm);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan));
        var ridge = Assert.Single(
            plan!.Items,
            item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Ridge);
        Assert.Equal(175d, ridge.WidthMm, 9);
        Assert.Equal(250d, ridge.HeightMm, 9);
    }

    [Fact]
    public void IntermediateWidthHeightEdit_FlowsIntoLayoutPlannerAndKeepsGeneratedKey()
    {
        var viewModel = CreateViewModel(Solve(RectanglePoints()), null, false, null, false);
        var row = Assert.Single(viewModel.Rows);
        Assert.True(viewModel.TryGetPreviewPlan(out var before));
        var beforeKeys = before!.Items
            .Where(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate)
            .Select(item => item.GeneratedKey)
            .ToArray();
        Assert.NotEmpty(beforeKeys);

        row.WidthText = "140";
        row.HeightText = "200";
        Assert.True(viewModel.TryCreateDraft(out var layout, out _));
        Assert.Equal(140d, layout!.IntermediateItems[0].WidthMm);
        Assert.Equal(200d, layout.IntermediateItems[0].HeightMm);
        Assert.True(viewModel.TryGetPreviewPlan(out var after));
        var afterItems = after!.Items
            .Where(item => item.GeneratorRole == RoofAutomaticPurlinGeneratorRole.Intermediate)
            .ToArray();
        Assert.Equal(beforeKeys, afterItems.Select(item => item.GeneratedKey));
        Assert.All(afterItems, item =>
        {
            Assert.Equal(140d, item.WidthMm, 9);
            Assert.Equal(200d, item.HeightMm, 9);
            Assert.Equal(40d, item.ElevationProfile!.SeatingDepthMm);
            Assert.Equal(
                item.ElevationProfile.TopRelativeElevationMm -
                item.ElevationProfile.BottomRelativeElevationMm,
                item.HeightMm,
                9);
        });
    }

    [Fact]
    public void PercentSeating_UsesAuthoritativeRafterHeightForDerivedDepth()
    {
        var solved = Solve(RectanglePoints());
        var tallRafter = TimberElementDefaults.For(TimberElementType.Rafter) with
        {
            WidthMm = 100d,
            HeightMm = 200d,
        };
        var viewModel = new AutomaticPurlinDialogViewModel(
            solved.Geometry,
            solved.Provenance,
            null,
            false,
            null,
            false,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            tallRafter,
            CultureInfo.GetCultureInfo("en"));

        Assert.Equal(100d, viewModel.RafterWidthMm);
        Assert.Equal(200d, viewModel.RafterHeightMm);
        Assert.Equal("50 mm", viewModel.WallPlateRow.SeatingDepthDerived);
        Assert.Equal("50 mm", viewModel.RidgeSeatingDepthDerived);
        Assert.Equal("50 mm", viewModel.Rows[0].SeatingDepthDerived);
        Assert.Equal("Rafter 100 × 200", viewModel.SectionPresentation.RafterDimensionText);
        Assert.Equal("200 mm", viewModel.SectionPresentation.RafterHeightDimensionText);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan));
        Assert.All(
            plan!.Items.Where(item => item.ElevationProfile?.SeatingDepthMm is not null),
            item => Assert.Equal(50d, item.ElevationProfile!.SeatingDepthMm!.Value, 9));
    }

    [Fact]
    public void LegacyLayout_UsesDefaultDimensionsUntilEdited()
    {
        var solved = Solve(RectanglePoints());
        var layout = new RoofAutomaticPurlinLayout(
            true,
            [
                new(
                    LayoutIdA,
                    true,
                    RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                    500d),
            ]);
        var viewModel = CreateViewModel(solved, layout, true, null, false);

        Assert.Equal("160", viewModel.Rows[0].WidthText);
        Assert.Equal("220", viewModel.Rows[0].HeightText);
        Assert.Equal("160", viewModel.RidgeWidthText);
        Assert.Equal("220", viewModel.RidgeHeightText);
        Assert.True(viewModel.TryCreateDraft(out var draft, out _));
        Assert.Null(draft!.IntermediateItems[0].WidthMm);
        Assert.Null(draft.IntermediateItems[0].HeightMm);
        Assert.Null(draft.RidgeWidthMm);
        Assert.Null(draft.RidgeHeightMm);
        Assert.True(viewModel.TryGetPreviewPlan(out var plan));
        Assert.All(
            plan!.Items.Where(item => item.GeneratorRole != RoofAutomaticPurlinGeneratorRole.WallPlate),
            item =>
            {
                Assert.Equal(160d, item.WidthMm, 9);
                Assert.Equal(220d, item.HeightMm, 9);
            });
    }

    [Fact]
    public void SectionPresentation_TracksActiveMembersFromPreviewPlan()
    {
        var viewModel = CreateViewModel(Solve(RectanglePoints()), null, false, null, false);
        ClearIntermediateRows(viewModel);
        viewModel.WallPlateEnabled = true;
        viewModel.RidgeEnabled = true;
        var enabled = viewModel.AddRow();
        enabled.PlacementValueText = "700";
        var disabled = viewModel.AddRow();
        disabled.Enabled = false;

        var presentation = viewModel.SectionPresentation;
        Assert.Contains(
            presentation.Members,
            member => member.Role == RoofAutomaticPurlinGeneratorRole.WallPlate);
        Assert.Contains(
            presentation.Members,
            member => member.Role == RoofAutomaticPurlinGeneratorRole.Ridge);
        Assert.Contains(
            presentation.Members,
            member => member.Role == RoofAutomaticPurlinGeneratorRole.Intermediate);
        Assert.DoesNotContain(
            presentation.Members,
            member => member.StableKey.Contains(disabled.LayoutItemId, StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewButton_SuspendsDialogWithoutApply()
    {
        RunSta(() =>
        {
            var viewModel = CreateViewModel(
                Solve(RectanglePoints()),
                null,
                false,
                null,
                false,
                mode: AutomaticPurlinDialogMode.ProductionEdit);
            viewModel.RidgeEnabled = true;
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();
            Assert.True(viewModel.CanPreview);
            NamedDescendant<System.Windows.Controls.Button>(window, "PreviewButton").RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.True(window.IsSuspendedForCadPreview);
            Assert.NotNull(window.SavedRestoreBounds);
            Assert.True(window.IsClosed);
            Assert.True(viewModel.RidgeEnabled);
            Assert.True(viewModel.TryCreateDraft(out _, out _));
        });
    }

    private static AutomaticPurlinDialogViewModel CreateViewModel(
        SolvedFixture solved,
        RoofAutomaticPurlinLayout? layout,
        bool layoutExists,
        RoofRelativeElevationDatum? datum,
        bool datumExists,
        CultureInfo? culture = null,
        AutomaticPurlinDialogMode mode = AutomaticPurlinDialogMode.ReadOnlyPreview,
        int existingAutomaticPurlinCount = 0,
        AutomaticPurlinRafterDimensionSource rafterDimensionSource =
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe) => new(
            solved.Geometry,
            solved.Provenance,
            layout,
            layoutExists,
            datum,
            datumExists,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            TimberElementDefaults.For(TimberElementType.Rafter),
        culture ?? CultureInfo.GetCultureInfo("en"),
        mode,
        existingAutomaticPurlinCount,
        storedDatumLoadError: null,
        rafterDimensionSource);

    private static void ClearIntermediateRows(AutomaticPurlinDialogViewModel viewModel)
    {
        while (viewModel.Rows.Count > 0)
        {
            viewModel.RemoveRow(viewModel.Rows[0]);
        }
    }

    private static void SelectIntermediateEditorTab(
        AutomaticPurlinDialogWindow window,
        AutomaticPurlinDialogViewModel viewModel,
        int intermediateIndex = 0)
    {
        var tab = viewModel.EditorTabs
            .Where(item => item.Kind == AutomaticPurlinEditorTabKind.Intermediate)
            .Skip(intermediateIndex)
            .First();
        viewModel.SelectedEditorTab = tab;
        window.ElementEditorsTabControl.SelectedItem = tab;
        window.UpdateLayout();
    }

    private static AutomaticPurlinDialogWindow CreateOffscreenWindow(
        AutomaticPurlinDialogViewModel viewModel) =>
        new(viewModel, SettingsTheme.Light)
        {
            Left = -30000,
            Top = -30000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };

    private static IReadOnlyList<ResolvedRoofStructuralEdge> Ridges(SolvedFixture solved) =>
        RoofStructuralEdgeIdentityResolver.Resolve(solved.Geometry, solved.Provenance).Edges
            .Where(edge => edge.StructuralRole == RoofStructuralRole.Ridge &&
                Math.Abs(edge.Segment3D.Start.Z - edge.Segment3D.End.Z) <=
                RoofAutomaticPurlinPlanner.CoordinateToleranceMm)
            .ToArray();

    private static SolvedFixture Solve(IReadOnlyList<RoofPoint2D> points)
    {
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
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(geometry.IsValid, geometry.Error.ToString());
        return new SolvedFixture(Assert.IsType<HipRoofGeometry>(geometry.Geometry), provenance);
    }

    private static RoofPoint2D[] RectanglePoints() =>
        [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)];

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static T NamedDescendant<T>(DependencyObject root, string name)
        where T : FrameworkElement => Descendants<T>(root).Single(element => element.Name == name);

    private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        for (var current = child; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Automatic Purlin dialog test timed out.");
        Assert.Null(failure);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
