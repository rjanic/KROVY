using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofAutomaticPurlinPersistenceRulesTests
{
    private const string LayoutIdA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string LayoutIdB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void EmptyLayout_IsTheMissingSectionInMemoryDefault()
    {
        Assert.False(RoofAutomaticPurlinLayout.Empty.RidgeEnabled);
        Assert.Empty(RoofAutomaticPurlinLayout.Empty.IntermediateItems);
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
    [InlineData(0d)]
    [InlineData(-1d)]
    public void Layout_NonfiniteOrNonpositivePlacementFails(double elevation)
    {
        AssertLayoutError(
            RoofPurlinLayoutPersistenceError.InvalidPlacementValue,
            0,
            [Stored(LayoutIdA, elevation: elevation)]);
    }

    [Fact]
    public void Layout_UnsupportedSchemaFails()
    {
        var result = RoofPurlinLayoutPersistenceRules.ValidateStored(2, 0, []);
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
    [InlineData(0d)]
    [InlineData(100d)]
    [InlineData(101d)]
    public void PercentSeating_OutsideOpenIntervalFailsClosed(double value)
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
