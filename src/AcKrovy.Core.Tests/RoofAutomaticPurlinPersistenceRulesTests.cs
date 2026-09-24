using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticPurlinPersistenceRulesTests
{
    [Fact]
    public void GeneratedWallPlateRoundtrip_PreservesOwnerAndBoundaryIdentity()
    {
        var original = new RoofAutomaticPurlinWallPlateKey(27);
        var created = RoofAutomaticPurlinGeneratedDataRules.Create("00af", original);
        var read = RoofAutomaticPurlinGeneratedDataRules.ValidateWallPlateStored(
            RoofAutomaticPurlinGeneratedDataSchema.CurrentVersion,
            created.Data!.RoofOwnerReference,
            RoofAutomaticPurlinGeneratedDataRules.WallPlateToken,
            original.BoundaryEdgeId);

        Assert.True(read.IsValid, read.Error.ToString());
        Assert.Equal("AF", read.Data!.RoofOwnerReference);
        Assert.Equal(RoofAutomaticPurlinGeneratorRole.WallPlate, read.Data.GeneratorRole);
        Assert.Equal(original, read.Data.GeneratedKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GeneratedWallPlate_RejectsNonPositiveBoundaryIdentity(int boundaryEdgeId)
    {
        var result = RoofAutomaticPurlinGeneratedDataRules.ValidateWallPlateStored(
            RoofAutomaticPurlinGeneratedDataSchema.CurrentVersion,
            "AF",
            RoofAutomaticPurlinGeneratedDataRules.WallPlateToken,
            boundaryEdgeId);

        Assert.False(result.IsValid);
        Assert.Equal(
            RoofAutomaticPurlinGeneratedDataError.InvalidWallPlateBoundaryEdgeId,
            result.Error);
    }

    private const string LayoutIdA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string LayoutIdB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void EmptyLayout_IsTheMissingSectionInMemoryDefault()
    {
        Assert.False(RoofAutomaticPurlinLayout.Empty.RidgeEnabled);
        Assert.False(RoofAutomaticPurlinLayout.Empty.WallPlateEnabled);
        Assert.Equal(0d, RoofAutomaticPurlinLayout.Empty.WallPlateLowerEdgeHeightMm);
        Assert.Empty(RoofAutomaticPurlinLayout.Empty.IntermediateItems);
    }

    [Fact]
    public void LegacySchemaOneLayoutWithoutWallPlateField_DefaultsToFalse()
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.Version1,
            1,
            Array.Empty<RoofPurlinLayoutStoredItem>());

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.False(result.Layout!.WallPlateEnabled);
        Assert.Equal(0d, result.Layout.WallPlateLowerEdgeHeightMm);
    }

    [Fact]
    public void LegacySchemaOneLayoutWithWallPlateFlagOnly_DefaultsLowerEdgeToZero()
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.Version1,
            0,
            Array.Empty<RoofPurlinLayoutStoredItem>(),
            wallPlateEnabledValue: 1);

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.True(result.Layout!.WallPlateEnabled);
        Assert.Equal(0d, result.Layout.WallPlateLowerEdgeHeightMm);
    }

    [Fact]
    public void WallPlateEnabled_RoundtripPreservesTrueWithoutSchemaChange()
    {
        var layout = RoofAutomaticPurlinLayout.Empty with { WallPlateEnabled = true };

        var result = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.True(result.Layout!.WallPlateEnabled);
        Assert.Equal(0d, result.Layout.WallPlateLowerEdgeHeightMm);
        Assert.Equal(3, RoofPurlinLayoutSchema.CurrentVersion);
    }

    [Fact]
    public void WallPlateLowerEdgeHeight_RoundtripPreservesValueWithoutSchemaBump()
    {
        var layout = RoofAutomaticPurlinLayout.Empty with
        {
            WallPlateEnabled = true,
            WallPlateLowerEdgeHeightMm = 1000d,
        };

        var result = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.True(result.Layout!.WallPlateEnabled);
        Assert.Equal(1000d, result.Layout.WallPlateLowerEdgeHeightMm);
        Assert.Equal(
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            result.Layout.WallPlatePlacement!.PlacementMode);
        Assert.Equal(1000d, result.Layout.WallPlatePlacement.PlacementValueMm);
        Assert.Equal(3, RoofPurlinLayoutSchema.CurrentVersion);
    }

    [Theory]
    [InlineData(357.417d)]
    [InlineData(596.285d)]
    public void WallPlateBottom_RelativeZeroPlace_ExplicitLowerEdge_RoundTripsExact(
        double lowerEdgeMm)
    {
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var layout = new RoofAutomaticPurlinLayout(
            false,
            [
                new(
                    LayoutIdB,
                    true,
                    RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                    lowerEdgeMm >= 500d ? 1476.681d : 1010d,
                    SeatingDepth: seating,
                    WidthMm: 160d,
                    HeightMm: 220d),
            ])
        {
            WallPlateEnabled = true,
            WallPlateLowerEdgeHeightMm = lowerEdgeMm,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                0d,
                SeatingDepth: seating,
                WidthMm: 140d,
                HeightMm: 140d),
        };

        var written = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);
        Assert.True(written.IsValid, written.Error.ToString());
        Assert.Equal(0d, written.Layout!.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(lowerEdgeMm, written.Layout.WallPlateLowerEdgeHeightMm, 3);

        var storedWallPlate = RoofPurlinLayoutPersistenceRules.ToStoredWallPlatePlacement(
            written.Layout.WallPlatePlacement);
        var reread = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            ridgeEnabledValue: 0,
            items:
            [
                new(
                    LayoutIdB,
                    1,
                    RoofPurlinLayoutPersistenceRules.BottomEdgeHeightAboveReferenceToken,
                    written.Layout.IntermediateItems[0].PlacementValueMm,
                    RoofPurlinLayoutPersistenceRules.NoReferenceRidgeToken,
                    0,
                    0,
                    RoofPurlinLayoutPersistenceRules.PercentOfRafterHeightToken,
                    25d,
                    160d,
                    220d),
            ],
            wallPlateEnabledValue: 1,
            wallPlateLowerEdgeHeightMm: written.Layout.WallPlateLowerEdgeHeightMm,
            wallPlatePlacementItem: storedWallPlate,
            sectionDimensions: new RoofPurlinLayoutStoredSectionDimensions(140d, 140d, 0d, 0d),
            wallPlateLowerEdgeHeightExplicit: true);
        Assert.True(reread.IsValid, reread.Error.ToString());
        Assert.Equal(0d, reread.Layout!.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(lowerEdgeMm, reread.Layout.WallPlateLowerEdgeHeightMm, 3);
        Assert.True(
            RoofPurlinLayoutPersistenceRules.AreEquivalentForOwnerWrite(
                written.Layout,
                reread.Layout));
    }

    [Fact]
    public void LegacyFullPlacementWithoutExplicitLowerEdge_FallsBackToPlace()
    {
        var storedWallPlate = new RoofPurlinLayoutStoredItem(
            RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
            1,
            RoofPurlinLayoutPersistenceRules.BottomEdgeHeightAboveReferenceToken,
            900d,
            RoofPurlinLayoutPersistenceRules.NoReferenceRidgeToken,
            0,
            0,
            RoofPurlinLayoutPersistenceRules.PercentOfRafterHeightToken,
            25d);
        var reread = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            ridgeEnabledValue: 0,
            items: Array.Empty<RoofPurlinLayoutStoredItem>(),
            wallPlateEnabledValue: 1,
            wallPlateLowerEdgeHeightMm: 0d,
            wallPlatePlacementItem: storedWallPlate,
            wallPlateLowerEdgeHeightExplicit: false);
        Assert.True(reread.IsValid, reread.Error.ToString());
        Assert.Equal(900d, reread.Layout!.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(900d, reread.Layout.WallPlateLowerEdgeHeightMm, 9);
    }

    [Theory]
    [InlineData(-250d)]
    [InlineData(0d)]
    [InlineData(1000d)]
    public void ExplicitLowerEdge_PreservesSignedAndZeroBottomEdge(double placeAndLowerEdgeMm)
    {
        var layout = new RoofAutomaticPurlinLayout(false, Array.Empty<RoofAutomaticPurlinLayoutItem>())
        {
            WallPlateEnabled = true,
            WallPlateLowerEdgeHeightMm = placeAndLowerEdgeMm,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                placeAndLowerEdgeMm),
        };

        var written = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);
        Assert.True(written.IsValid, written.Error.ToString());
        Assert.Equal(placeAndLowerEdgeMm, written.Layout!.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(placeAndLowerEdgeMm, written.Layout.WallPlateLowerEdgeHeightMm, 9);

        var reread = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            0,
            Array.Empty<RoofPurlinLayoutStoredItem>(),
            1,
            written.Layout.WallPlateLowerEdgeHeightMm,
            RoofPurlinLayoutPersistenceRules.ToStoredWallPlatePlacement(
                written.Layout.WallPlatePlacement),
            wallPlateLowerEdgeHeightExplicit: true);
        Assert.True(reread.IsValid, reread.Error.ToString());
        Assert.True(
            RoofPurlinLayoutPersistenceRules.AreEquivalentForOwnerWrite(
                written.Layout,
                reread.Layout!));
    }

    [Fact]
    public void WallPlateBottom_RelativeZeroPlace_PreservesLowerEdgeBootstrapStash()
    {
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var layout = new RoofAutomaticPurlinLayout(false, Array.Empty<RoofAutomaticPurlinLayoutItem>())
        {
            WallPlateEnabled = true,
            WallPlateLowerEdgeHeightMm = 596.285d,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                0d,
                SeatingDepth: seating,
                WidthMm: 140d,
                HeightMm: 140d),
        };

        var written = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);
        Assert.True(written.IsValid, written.Error.ToString());
        Assert.Equal(0d, written.Layout!.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(596.285d, written.Layout.WallPlateLowerEdgeHeightMm, 3);

        var storedWallPlate = RoofPurlinLayoutPersistenceRules.ToStoredWallPlatePlacement(
            written.Layout.WallPlatePlacement);
        var reread = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            ridgeEnabledValue: 0,
            items: Array.Empty<RoofPurlinLayoutStoredItem>(),
            wallPlateEnabledValue: 1,
            wallPlateLowerEdgeHeightMm: written.Layout.WallPlateLowerEdgeHeightMm,
            wallPlatePlacementItem: storedWallPlate,
            sectionDimensions: new RoofPurlinLayoutStoredSectionDimensions(140d, 140d, 0d, 0d),
            wallPlateLowerEdgeHeightExplicit: true);
        Assert.True(reread.IsValid, reread.Error.ToString());
        Assert.Equal(0d, reread.Layout!.WallPlatePlacement!.PlacementValueMm, 9);
        Assert.Equal(596.285d, reread.Layout.WallPlateLowerEdgeHeightMm, 3);
        Assert.True(
            RoofPurlinLayoutPersistenceRules.AreEquivalentForOwnerWrite(
                written.Layout,
                reread.Layout));
    }

    [Fact]
    public void LegacyLayoutWithoutSectionDimensions_StillValidatesWithoutSchemaBump()
    {
        var items = new[]
        {
            Stored(LayoutIdA, elevation: 1200d),
        };
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            ridgeEnabledValue: 1,
            items,
            wallPlateEnabledValue: 1,
            wallPlateLowerEdgeHeightMm: 900d);

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.True(result.Layout!.RidgeEnabled);
        Assert.True(result.Layout.WallPlateEnabled);
        Assert.Equal(900d, result.Layout.WallPlateLowerEdgeHeightMm);
        Assert.Null(result.Layout.RidgeWidthMm);
        Assert.Null(result.Layout.RidgeHeightMm);
        Assert.Null(result.Layout.WallPlatePlacement!.WidthMm);
        Assert.Null(result.Layout.WallPlatePlacement.HeightMm);
        Assert.Null(result.Layout.IntermediateItems[0].WidthMm);
        Assert.Null(result.Layout.IntermediateItems[0].HeightMm);
        Assert.Equal(3, RoofPurlinLayoutSchema.CurrentVersion);
        Assert.False(RoofPurlinLayoutPersistenceRules.HasPersistedSectionDimensions(result.Layout));
    }

    [Fact]
    public void SectionDimensions_RoundtripPreservesOverridesWithoutSchemaBump()
    {
        var layout = new RoofAutomaticPurlinLayout(
            true,
            [
                new RoofAutomaticPurlinLayoutItem(
                    LayoutIdA,
                    true,
                    RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                    800d,
                    WidthMm: 120d,
                    HeightMm: 180d),
                new RoofAutomaticPurlinLayoutItem(
                    LayoutIdB,
                    true,
                    RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                    1100d),
            ])
        {
            WallPlateEnabled = true,
            WallPlateLowerEdgeHeightMm = 500d,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
                500d,
                WidthMm: 140d,
                HeightMm: 160d),
            RidgeWidthMm = 100d,
            RidgeHeightMm = 200d,
        };

        var result = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.Equal(3, RoofPurlinLayoutSchema.CurrentVersion);
        Assert.True(RoofPurlinLayoutPersistenceRules.HasPersistedSectionDimensions(result.Layout!));
        Assert.Equal(140d, result.Layout!.WallPlatePlacement!.WidthMm);
        Assert.Equal(160d, result.Layout.WallPlatePlacement.HeightMm);
        Assert.Equal(100d, result.Layout.RidgeWidthMm);
        Assert.Equal(200d, result.Layout.RidgeHeightMm);
        Assert.Equal(120d, result.Layout.IntermediateItems[0].WidthMm);
        Assert.Equal(180d, result.Layout.IntermediateItems[0].HeightMm);
        Assert.Null(result.Layout.IntermediateItems[1].WidthMm);
        Assert.Null(result.Layout.IntermediateItems[1].HeightMm);

        var storedItems = new[]
        {
            new RoofPurlinLayoutStoredItem(
                LayoutIdA,
                1,
                RoofPurlinLayoutPersistenceRules.BottomEdgeHeightAboveReferenceToken,
                800d,
                RoofPurlinLayoutPersistenceRules.NoReferenceRidgeToken,
                0,
                0,
                RoofPurlinLayoutPersistenceRules.NoSeatingDepthToken,
                0d,
                120d,
                180d),
            new RoofPurlinLayoutStoredItem(
                LayoutIdB,
                1,
                RoofPurlinLayoutPersistenceRules.BottomEdgeHeightAboveReferenceToken,
                1100d,
                RoofPurlinLayoutPersistenceRules.NoReferenceRidgeToken,
                0,
                0,
                RoofPurlinLayoutPersistenceRules.NoSeatingDepthToken,
                0d),
        };
        var sectionDimensions = new RoofPurlinLayoutStoredSectionDimensions(140d, 160d, 100d, 200d);
        var reread = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            1,
            storedItems,
            wallPlateEnabledValue: 1,
            wallPlateLowerEdgeHeightMm: 500d,
            sectionDimensions: sectionDimensions);

        Assert.True(reread.IsValid, reread.Error.ToString());
        Assert.Equal(result.Layout!.RidgeEnabled, reread.Layout!.RidgeEnabled);
        Assert.Equal(result.Layout.WallPlateEnabled, reread.Layout.WallPlateEnabled);
        Assert.Equal(result.Layout.WallPlateLowerEdgeHeightMm, reread.Layout.WallPlateLowerEdgeHeightMm);
        Assert.Equal(result.Layout.WallPlatePlacement, reread.Layout.WallPlatePlacement);
        Assert.Equal(result.Layout.RidgeWidthMm, reread.Layout.RidgeWidthMm);
        Assert.Equal(result.Layout.RidgeHeightMm, reread.Layout.RidgeHeightMm);
        Assert.Equal(result.Layout.IntermediateItems, reread.Layout.IntermediateItems);
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SectionDimensions_RejectInvalidStoredValues(double invalid)
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            0,
            Array.Empty<RoofPurlinLayoutStoredItem>(),
            sectionDimensions: new RoofPurlinLayoutStoredSectionDimensions(invalid, 140d, 0d, 0d));

        Assert.False(result.IsValid);
        Assert.Equal(RoofPurlinLayoutPersistenceError.InvalidSectionDimension, result.Error);
    }

    [Fact]
    public void WallPlatePlanDistancePlacement_RoundtripPreservesSharedModeWithoutSchemaBump()
    {
        var seating = new RoofAutomaticPurlinSeatingDepth(
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
            25d);
        var layout = RoofAutomaticPurlinLayout.Empty with
        {
            WallPlateEnabled = true,
            WallPlatePlacement = new RoofAutomaticPurlinLayoutItem(
                RoofAutomaticPurlinLayoutItemIdentity.WallPlatePlacementId,
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                1500d,
                SeatingDepth: seating),
        };

        var result = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.Equal(0d, result.Layout!.WallPlateLowerEdgeHeightMm);
        Assert.Equal(
            RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
            result.Layout.WallPlatePlacement!.PlacementMode);
        Assert.Equal(1500d, result.Layout.WallPlatePlacement.PlacementValueMm);
        Assert.Equal(seating, result.Layout.WallPlatePlacement.SeatingDepth);
        Assert.Equal(3, RoofPurlinLayoutSchema.CurrentVersion);
    }

    [Fact]
    public void LegacyLowerEdgeOnly_MapsToBottomEdgeSharedPlacement()
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            0,
            Array.Empty<RoofPurlinLayoutStoredItem>(),
            wallPlateEnabledValue: 1,
            wallPlateLowerEdgeHeightMm: 900d);

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.Equal(900d, result.Layout!.WallPlateLowerEdgeHeightMm);
        Assert.Equal(
            RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
            result.Layout.WallPlatePlacement!.PlacementMode);
        Assert.Equal(900d, result.Layout.WallPlatePlacement.PlacementValueMm);
        Assert.Null(result.Layout.WallPlatePlacement.SeatingDepth);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    public void Layout_InvalidWallPlateLowerEdgeHeightFails(double value)
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            0,
            Array.Empty<RoofPurlinLayoutStoredItem>(),
            wallPlateEnabledValue: 1,
            wallPlateLowerEdgeHeightMm: value);

        Assert.False(result.IsValid);
        Assert.Equal(RoofPurlinLayoutPersistenceError.InvalidWallPlateLowerEdgeHeight, result.Error);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Layout_InvalidWallPlateBooleanFails(int value)
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            0,
            Array.Empty<RoofPurlinLayoutStoredItem>(),
            value);

        Assert.False(result.IsValid);
        Assert.Equal(RoofPurlinLayoutPersistenceError.InvalidWallPlateEnabled, result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LayoutRoundtrip_PreservesRidgeAndZeroRows(bool ridgeEnabled)
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            ridgeEnabled ? 1 : 0,
            Array.Empty<RoofPurlinLayoutStoredItem>());

        Assert.True(result.IsValid);
        Assert.Equal(ridgeEnabled, result.Layout!.RidgeEnabled);
        Assert.Empty(result.Layout.IntermediateItems);
    }

    [Fact]
    public void LayoutRoundtrip_PreservesMultipleRowsOrderIdentityAndDisabledState()
    {
        var stored = new[]
        {
            Stored(LayoutIdB, enabled: false, elevation: 2400d),
            Stored(LayoutIdA, enabled: true, elevation: 1200d),
        };

        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(1, 1, stored);

        Assert.True(result.IsValid);
        Assert.True(result.Layout!.RidgeEnabled);
        Assert.Equal(new[] { LayoutIdB, LayoutIdA },
            result.Layout.IntermediateItems.Select(item => item.LayoutItemId));
        Assert.False(result.Layout.IntermediateItems[0].Enabled);
        Assert.True(result.Layout.IntermediateItems[1].Enabled);
        Assert.Equal(2400d, result.Layout.IntermediateItems[0].PlacementValueMm);
        Assert.Equal(1200d, result.Layout.IntermediateItems[1].PlacementValueMm);
    }

    [Fact]
    public void ReorderingLayoutRows_PreservesEachRowsIdentity()
    {
        var first = RoofPurlinLayoutPersistenceRules.ValidateForWrite(new(false,
        [
            HeightItem(LayoutIdA, 1000d),
            HeightItem(LayoutIdB, 2000d),
        ])).Layout!;
        var reordered = RoofPurlinLayoutPersistenceRules.ValidateForWrite(new(false,
        [
            first.IntermediateItems[1],
            first.IntermediateItems[0],
        ])).Layout!;

        Assert.Equal(new[] { LayoutIdB, LayoutIdA },
            reordered.IntermediateItems.Select(item => item.LayoutItemId));
        Assert.Equal(
            first.IntermediateItems.Select(item => item.LayoutItemId).Order(),
            reordered.IntermediateItems.Select(item => item.LayoutItemId).Order());
    }

    [Theory]
    [InlineData(-1, RoofPurlinLayoutPersistenceError.InvalidRidgeEnabled)]
    [InlineData(2, RoofPurlinLayoutPersistenceError.InvalidRidgeEnabled)]
    public void Layout_InvalidRidgeBooleanFails(int value, RoofPurlinLayoutPersistenceError error) =>
        AssertLayoutError(error, value, Array.Empty<RoofPurlinLayoutStoredItem>());

    [Theory]
    [InlineData("not-a-guid", RoofPurlinLayoutPersistenceError.MalformedLayoutItemId)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", RoofPurlinLayoutPersistenceError.NonCanonicalLayoutItemId)]
    public void Layout_MalformedOrNoncanonicalIdFails(
        string id,
        RoofPurlinLayoutPersistenceError error) =>
        AssertLayoutError(error, 0, [Stored(id)]);

    [Fact]
    public void Layout_DuplicateIdFails()
    {
        AssertLayoutError(
            RoofPurlinLayoutPersistenceError.DuplicateLayoutItemId,
            0,
            [Stored(LayoutIdA), Stored(LayoutIdA)]);
    }

    [Fact]
    public void Layout_InvalidItemBooleanFails()
    {
        AssertLayoutError(
            RoofPurlinLayoutPersistenceError.InvalidEnabled,
            0,
            [Stored(LayoutIdA) with { EnabledValue = 2 }]);
    }

    [Fact]
    public void Layout_WrongPlacementTokenFails()
    {
        AssertLayoutError(
            RoofPurlinLayoutPersistenceError.InvalidPlacementToken,
            0,
            [Stored(LayoutIdA) with { PlacementToken = "elevationAboveEave" }]);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Layout_NonfinitePlacementFails(double elevation)
    {
        AssertLayoutError(
            RoofPurlinLayoutPersistenceError.InvalidPlacementValue,
            0,
            [Stored(LayoutIdA, elevation: elevation)]);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-200d)]
    public void Layout_BottomEdge_AllowsZeroAndNegativeSignedOffset(double elevation)
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(
            1,
            0,
            [Stored(LayoutIdA, elevation: elevation)]);
        Assert.True(result.IsValid, result.Error.ToString());
        Assert.Equal(elevation, result.Layout!.IntermediateItems[0].PlacementValueMm);
    }

    [Fact]
    public void Layout_PlanDistance_StillRejectsNonPositive()
    {
        AssertLayoutError(
            RoofPurlinLayoutPersistenceError.InvalidPlacementValue,
            0,
            [Stored(LayoutIdA, elevation: -1d) with
            {
                PlacementToken = RoofPurlinLayoutPersistenceRules.PlanDistanceFromEaveToken,
                SeatingDepthToken = RoofPurlinLayoutPersistenceRules.PercentOfRafterHeightToken,
                SeatingDepthValue = 25d,
            }]);
    }

    [Fact]
    public void Layout_UnsupportedSchemaFails()
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(4, 0, []);
        Assert.Equal(RoofPurlinLayoutPersistenceError.UnsupportedSchemaVersion, result.Error);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void LayoutPersistence_DoesNotApplyRoofRiseValidation()
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(
            1,
            0,
            [Stored(LayoutIdA, elevation: double.MaxValue)]);
        Assert.True(result.IsValid);
        Assert.Equal(double.MaxValue, result.Layout!.IntermediateItems[0].PlacementValueMm);
    }

    [Fact]
    public void LayoutRoundtrip_PreservesAllPlacementModesRidgeKeyAndSeating()
    {
        var layout = new RoofAutomaticPurlinLayout(true,
        [
            HeightItem(LayoutIdA, 500d),
            new RoofAutomaticPurlinLayoutItem(
                LayoutIdB,
                true,
                RoofAutomaticPurlinPlacementMode.PlanDistanceFromRidge,
                900d,
                new RoofStructuralLogicalKey(RoofStructuralRole.Ridge, 2, 7),
                new RoofAutomaticPurlinSeatingDepth(
                    RoofAutomaticPurlinSeatingDepthMode.AbsoluteMm,
                    35d)),
        ]);

        var result = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);

        Assert.True(result.IsValid, result.Error.ToString());
        Assert.True(result.Layout!.RidgeEnabled);
        Assert.Equal(layout.IntermediateItems, result.Layout.IntermediateItems);
    }

    [Fact]
    public void EaveDistance_PercentSeatingRoundtripIsStrict()
    {
        var stored = Stored(LayoutIdA) with
        {
            PlacementToken = RoofPurlinLayoutPersistenceRules.PlanDistanceFromEaveToken,
            SeatingDepthToken = RoofPurlinLayoutPersistenceRules.PercentOfRafterHeightToken,
            SeatingDepthValue = 25d,
        };

        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(1, 0, [stored]);

        Assert.True(result.IsValid);
        var item = Assert.Single(result.Layout!.IntermediateItems);
        Assert.Equal(RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave, item.PlacementMode);
        Assert.Equal(
            new RoofAutomaticPurlinSeatingDepth(
                RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                25d),
            item.SeatingDepth);
    }

    [Theory]
    [InlineData(101d)]
    [InlineData(-1d)]
    public void PercentSeating_OutsideAllowedRangeFailsClosed(double value)
    {
        AssertLayoutError(
            RoofPurlinLayoutPersistenceError.InvalidSeatingDepth,
            0,
            [Stored(LayoutIdA) with
            {
                PlacementToken = RoofPurlinLayoutPersistenceRules.PlanDistanceFromEaveToken,
                SeatingDepthToken =
                    RoofPurlinLayoutPersistenceRules.PercentOfRafterHeightToken,
                SeatingDepthValue = value,
            }]);
    }

    [Fact]
    public void PercentSeating_OneHundredIsAccepted()
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateForWrite(
            new RoofAutomaticPurlinLayout(
                false,
                [
                    new(
                        LayoutIdA,
                        true,
                        RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                        1800d,
                        SeatingDepth: new RoofAutomaticPurlinSeatingDepth(
                            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                            100d)),
                ]));

        Assert.True(result.IsValid, result.Error.ToString());
    }

    [Fact]
    public void PercentSeating_ZeroIsAccepted()
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateForWrite(
            new RoofAutomaticPurlinLayout(
                false,
                [
                    new(
                        LayoutIdA,
                        true,
                        RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave,
                        1800d,
                        SeatingDepth: new RoofAutomaticPurlinSeatingDepth(
                            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight,
                            0d)),
                ]));
        Assert.True(result.IsValid, result.Error.ToString());
    }

    [Fact]
    public void DistanceModeWithoutSeatingAndForeignRidgeReferenceFailClosed()
    {
        AssertLayoutError(
            RoofPurlinLayoutPersistenceError.InvalidSeatingDepthToken,
            0,
            [Stored(LayoutIdA) with
            {
                PlacementToken = RoofPurlinLayoutPersistenceRules.PlanDistanceFromEaveToken,
            }]);
        AssertLayoutError(
            RoofPurlinLayoutPersistenceError.InvalidReferenceRidge,
            0,
            [Stored(LayoutIdA) with
            {
                ReferenceRidgeRoleToken = RoofPurlinLayoutPersistenceRules.RidgeReferenceToken,
                ReferenceRidgeBoundaryEdgeIdA = 2,
                ReferenceRidgeBoundaryEdgeIdB = 7,
            }]);
    }

    [Fact]
    public void GeneratedRidgeRoundtrip_PreservesOwnerRoleAndLogicalKey()
    {
        var original = new RoofAutomaticPurlinRidgeKey(
            new(RoofStructuralRole.Ridge, 2, 7));
        var created = RoofAutomaticPurlinGeneratedDataRules.Create("00af", original);
        var stored = Assert.IsType<RoofAutomaticPurlinRidgeKey>(created.Data!.GeneratedKey);
        var read = RoofAutomaticPurlinGeneratedDataRules.ValidateRidgeStored(
            1,
            created.Data.RoofOwnerReference,
            "Ridge",
            stored.StructuralKey.BoundaryEdgeIdA,
            stored.StructuralKey.BoundaryEdgeIdB);

        Assert.True(read.IsValid);
        Assert.Equal("AF", read.Data!.RoofOwnerReference);
        Assert.Equal(RoofAutomaticPurlinGeneratorRole.Ridge, read.Data.GeneratorRole);
        Assert.Equal(original, read.Data.GeneratedKey);
    }

    [Theory]
    [InlineData(0, 2, RoofAutomaticPurlinGeneratedDataError.NonPositiveBoundaryEdgeId)]
    [InlineData(-1, 2, RoofAutomaticPurlinGeneratedDataError.NonPositiveBoundaryEdgeId)]
    [InlineData(2, 2, RoofAutomaticPurlinGeneratedDataError.SameBoundaryEdgeId)]
    [InlineData(7, 2, RoofAutomaticPurlinGeneratedDataError.NonCanonicalBoundaryPair)]
    public void GeneratedRidge_InvalidPairFails(
        int a,
        int b,
        RoofAutomaticPurlinGeneratedDataError error)
    {
        var result = RoofAutomaticPurlinGeneratedDataRules.ValidateRidgeStored(
            1, "AF", "Ridge", a, b);
        Assert.Equal(error, result.Error);
        Assert.Null(result.Data);
    }

    [Theory]
    [InlineData("Eave|5", RoofTopologyEdgeKind.Eave, 5, 0)]
    [InlineData("Hip|2|7", RoofTopologyEdgeKind.Hip, 2, 7)]
    [InlineData("Valley|3|4", RoofTopologyEdgeKind.Valley, 3, 4)]
    [InlineData("Ridge|1|6", RoofTopologyEdgeKind.Ridge, 1, 6)]
    [InlineData("CoplanarSeam|2|8", RoofTopologyEdgeKind.CoplanarSeam, 2, 8)]
    public void EndpointParser_AcceptsEveryExactCanonicalKind(
        string token,
        RoofTopologyEdgeKind kind,
        int a,
        int b)
    {
        Assert.True(RoofAutomaticPurlinBoundaryKeyRules.TryParse(
            token,
            out var key,
            out var error));
        Assert.Equal(RoofAutomaticPurlinBoundaryKeyError.None, error);
        Assert.Equal(new RoofAutomaticPurlinBoundaryKey(kind, a, b), key);
        Assert.True(RoofAutomaticPurlinBoundaryKeyRules.TryFormat(key, out var formatted, out _));
        Assert.Equal(token, formatted);
    }

    [Theory]
    [InlineData("eave|5", RoofAutomaticPurlinBoundaryKeyError.UnsupportedKind)]
    [InlineData("Unknown|1|2", RoofAutomaticPurlinBoundaryKeyError.UnsupportedKind)]
    [InlineData(" Eave|5", RoofAutomaticPurlinBoundaryKeyError.UnsupportedKind)]
    [InlineData("Eave|5 ", RoofAutomaticPurlinBoundaryKeyError.MalformedToken)]
    [InlineData("Eave|0", RoofAutomaticPurlinBoundaryKeyError.NonPositiveBoundaryEdgeId)]
    [InlineData("Hip|2", RoofAutomaticPurlinBoundaryKeyError.MalformedToken)]
    [InlineData("Hip|2|2", RoofAutomaticPurlinBoundaryKeyError.SameBoundaryEdgeId)]
    [InlineData("Hip|7|2", RoofAutomaticPurlinBoundaryKeyError.NonCanonicalBoundaryPair)]
    [InlineData("Hip|2|7|9", RoofAutomaticPurlinBoundaryKeyError.MalformedToken)]
    public void EndpointParser_RejectsMalformedOrNoncanonicalTokens(
        string token,
        RoofAutomaticPurlinBoundaryKeyError expected)
    {
        Assert.False(RoofAutomaticPurlinBoundaryKeyRules.TryParse(
            token,
            out var key,
            out var error));
        Assert.Null(key);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void GeneratedIntermediateCreate_CanonicalizesEndpointOrderBeforeWrite()
    {
        var eave = new RoofAutomaticPurlinBoundaryKey(RoofTopologyEdgeKind.Eave, 5, 0);
        var hip = new RoofAutomaticPurlinBoundaryKey(RoofTopologyEdgeKind.Hip, 2, 7);
        var original = new RoofAutomaticPurlinIntermediateKey(LayoutIdA, 3, hip, eave);

        var created = RoofAutomaticPurlinGeneratedDataRules.Create("af", original);
        var canonical = Assert.IsType<RoofAutomaticPurlinIntermediateKey>(
            created.Data!.GeneratedKey);

        Assert.Equal(eave, canonical.EndpointBoundaryKeyA);
        Assert.Equal(hip, canonical.EndpointBoundaryKeyB);
        Assert.Equal(LayoutIdA, canonical.LayoutItemId);
        Assert.Equal(3, canonical.SourceFaceBoundaryEdgeId);
    }

    [Fact]
    public void GeneratedIntermediateRoundtrip_ReconstructsExactCanonicalLogicalKey()
    {
        var original = new RoofAutomaticPurlinIntermediateKey(
            LayoutIdA,
            4,
            new(RoofTopologyEdgeKind.Eave, 5, 0),
            new(RoofTopologyEdgeKind.Valley, 3, 8));
        var created = RoofAutomaticPurlinGeneratedDataRules.Create("AF", original);
        var read = RoofAutomaticPurlinGeneratedDataRules.ValidateIntermediateStored(
            1,
            created.Data!.RoofOwnerReference,
            "Intermediate",
            LayoutIdA,
            4,
            "Eave|5",
            "Valley|3|8");

        Assert.True(read.IsValid);
        Assert.Equal("AF", read.Data!.RoofOwnerReference);
        Assert.Equal(RoofAutomaticPurlinGeneratorRole.Intermediate, read.Data.GeneratorRole);
        Assert.Equal(original, read.Data.GeneratedKey);
    }

    [Theory]
    [InlineData(2, "AF", "Intermediate", LayoutIdA, 1, "Eave|1", "Hip|2|3", RoofAutomaticPurlinGeneratedDataError.UnsupportedSchemaVersion)]
    [InlineData(1, "0", "Intermediate", LayoutIdA, 1, "Eave|1", "Hip|2|3", RoofAutomaticPurlinGeneratedDataError.MalformedOwnerReference)]
    [InlineData(1, "AF", "intermediate", LayoutIdA, 1, "Eave|1", "Hip|2|3", RoofAutomaticPurlinGeneratedDataError.UnsupportedRole)]
    [InlineData(1, "AF", "Intermediate", "not-a-guid", 1, "Eave|1", "Hip|2|3", RoofAutomaticPurlinGeneratedDataError.MalformedLayoutItemId)]
    [InlineData(1, "AF", "Intermediate", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", 1, "Eave|1", "Hip|2|3", RoofAutomaticPurlinGeneratedDataError.NonCanonicalLayoutItemId)]
    [InlineData(1, "AF", "Intermediate", LayoutIdA, 0, "Eave|1", "Hip|2|3", RoofAutomaticPurlinGeneratedDataError.NonPositiveSourceFaceBoundaryEdgeId)]
    [InlineData(1, "AF", "Intermediate", LayoutIdA, 1, "Eave|1", "Unknown|2|3", RoofAutomaticPurlinGeneratedDataError.UnsupportedEndpointKind)]
    [InlineData(1, "AF", "Intermediate", LayoutIdA, 1, "Eave|1", "Hip|3|2", RoofAutomaticPurlinGeneratedDataError.NonCanonicalBoundaryPair)]
    [InlineData(1, "AF", "Intermediate", LayoutIdA, 1, "Eave|1", "Eave|1", RoofAutomaticPurlinGeneratedDataError.IdenticalEndpointBoundaryKeys)]
    [InlineData(1, "AF", "Intermediate", LayoutIdA, 1, "Hip|2|3", "Eave|1", RoofAutomaticPurlinGeneratedDataError.NonCanonicalEndpointBoundaryOrder)]
    public void GeneratedIntermediate_InvalidStoredDataFailsAtomically(
        int schema,
        string owner,
        string role,
        string layoutId,
        int sourceFace,
        string endpointA,
        string endpointB,
        RoofAutomaticPurlinGeneratedDataError expected)
    {
        var result = RoofAutomaticPurlinGeneratedDataRules.ValidateIntermediateStored(
            schema, owner, role, layoutId, sourceFace, endpointA, endpointB);
        Assert.False(result.IsValid);
        Assert.Null(result.Data);
        Assert.Equal(expected, result.Error);
    }

    private static RoofPurlinLayoutStoredItem Stored(
        string id,
        bool enabled = true,
        double elevation = 1000d) => new(
            id,
            enabled ? 1 : 0,
            RoofPurlinLayoutPersistenceRules.BottomEdgeHeightAboveReferenceToken,
            elevation,
            RoofPurlinLayoutPersistenceRules.NoReferenceRidgeToken,
            0,
            0,
            RoofPurlinLayoutPersistenceRules.NoSeatingDepthToken,
            0d);

    private static RoofAutomaticPurlinLayoutItem HeightItem(string id, double value) => new(
        id,
        true,
        RoofAutomaticPurlinPlacementMode.BottomEdgeHeightAboveReference,
        value);

    private static void AssertLayoutError(
        RoofPurlinLayoutPersistenceError expected,
        int ridgeEnabled,
        IReadOnlyList<RoofPurlinLayoutStoredItem> items)
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(
            RoofPurlinLayoutSchema.CurrentVersion,
            ridgeEnabled,
            items);
        Assert.False(result.IsValid);
        Assert.Null(result.Layout);
        Assert.Equal(expected, result.Error);
    }
}
