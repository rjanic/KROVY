using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofStructuralIdentityTests
{
    [Theory]
    [InlineData(RoofStructuralRole.Hip, 7, 2, "Hip|2|7")]
    [InlineData(RoofStructuralRole.Valley, 4, 3, "Valley|3|4")]
    [InlineData(RoofStructuralRole.Ridge, 1, 6, "Ridge|1|6")]
    public void LogicalKey_CanonicalizesBoundaryPair(
        RoofStructuralRole role,
        int first,
        int second,
        string expected)
    {
        Assert.True(RoofStructuralIdentityRules.TryCreate(
            role,
            first,
            second,
            out var identity,
            out var error));

        Assert.Equal(RoofStructuralIdentityError.None, error);
        Assert.Equal(expected, identity!.ToString());
        Assert.True(identity.BoundaryEdgeIdA < identity.BoundaryEdgeIdB);
    }

    [Fact]
    public void DuplicateLogicalKey_IsDetectedWithoutSuffixOrMerge()
    {
        var first = Key(RoofStructuralRole.Hip, 2, 7);
        var other = Key(RoofStructuralRole.Ridge, 1, 6);

        Assert.True(RoofStructuralIdentityRules.TryFindDuplicate(
            [first, other, first],
            out var duplicate));
        Assert.Equal(first, duplicate);
    }

    [Theory]
    [InlineData(RoofStructuralRole.Undefined, 1, 2, RoofStructuralIdentityError.UnsupportedRole)]
    [InlineData((RoofStructuralRole)99, 1, 2, RoofStructuralIdentityError.UnsupportedRole)]
    [InlineData(RoofStructuralRole.Hip, 0, 2, RoofStructuralIdentityError.NonPositiveBoundaryEdgeId)]
    [InlineData(RoofStructuralRole.Hip, -1, 2, RoofStructuralIdentityError.NonPositiveBoundaryEdgeId)]
    [InlineData(RoofStructuralRole.Hip, 2, 2, RoofStructuralIdentityError.SameBoundaryEdgeId)]
    public void InvalidLogicalKeyInput_FailsClosed(
        RoofStructuralRole role,
        int first,
        int second,
        RoofStructuralIdentityError expected)
    {
        Assert.False(RoofStructuralIdentityRules.TryCreate(
            role,
            first,
            second,
            out var identity,
            out var error));
        Assert.Null(identity);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void StoredStructuralMetadata_UsesOwnerPlusCanonicalLocalKey()
    {
        var created = RoofStructuralGeneratedDataRules.Create(
            "2af",
            RoofStructuralRole.Hip,
            7,
            2);

        Assert.True(created.IsValid);
        Assert.Equal(RoofStructuralGeneratedDataError.None, created.Error);
        Assert.Equal("2AF", created.Data!.RoofOwnerReference);
        Assert.Equal("Hip|2|7", created.Data.LogicalKey.ToString());
        Assert.Equal(RoofStructuralGeneratedDataSchema.CurrentVersion, created.Data.SchemaVersion);
    }

    [Fact]
    public void ValidStructuralMetadata_RoundTripsThroughStoredFields()
    {
        var created = RoofStructuralGeneratedDataRules.Create(
            "2af",
            RoofStructuralRole.Valley,
            8,
            3);
        Assert.True(created.IsValid);

        var decoded = RoofStructuralGeneratedDataRules.ValidateStored(
            created.Data!.SchemaVersion,
            created.Data.RoofOwnerReference,
            RoofStructuralGeneratedDataRules.FormatRole(created.Data.StructuralRole),
            created.Data.BoundaryEdgeIdA,
            created.Data.BoundaryEdgeIdB);

        Assert.True(decoded.IsValid);
        Assert.Equal(created.Data, decoded.Data);
    }

    [Theory]
    [InlineData("Hip", RoofStructuralRole.Hip)]
    [InlineData("Valley", RoofStructuralRole.Valley)]
    [InlineData("Ridge", RoofStructuralRole.Ridge)]
    public void StructuralRoleTokens_RoundTripExactly(
        string token,
        RoofStructuralRole expected)
    {
        Assert.True(RoofStructuralGeneratedDataRules.TryParseRole(token, out var role));
        Assert.Equal(expected, role);
        Assert.Equal(token, RoofStructuralGeneratedDataRules.FormatRole(role));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Eave")]
    [InlineData("CoplanarSeam")]
    [InlineData("hip")]
    [InlineData("Rafter")]
    public void UnsupportedStructuralRoleToken_IsRejected(string? token)
    {
        Assert.False(RoofStructuralGeneratedDataRules.TryParseRole(token, out var role));
        Assert.Equal(RoofStructuralRole.Undefined, role);
    }

    [Theory]
    [InlineData(0, "2AF", "Hip", 2, 7, RoofStructuralGeneratedDataError.UnsupportedSchemaVersion)]
    [InlineData(2, "2AF", "Hip", 2, 7, RoofStructuralGeneratedDataError.UnsupportedSchemaVersion)]
    [InlineData(1, null, "Hip", 2, 7, RoofStructuralGeneratedDataError.MissingOwnerReference)]
    [InlineData(1, "", "Hip", 2, 7, RoofStructuralGeneratedDataError.MissingOwnerReference)]
    [InlineData(1, "0", "Hip", 2, 7, RoofStructuralGeneratedDataError.MalformedOwnerReference)]
    [InlineData(1, "XYZ", "Hip", 2, 7, RoofStructuralGeneratedDataError.MalformedOwnerReference)]
    [InlineData(1, "2AF", "Eave", 2, 7, RoofStructuralGeneratedDataError.UnsupportedRole)]
    [InlineData(1, "2AF", "CoplanarSeam", 2, 7, RoofStructuralGeneratedDataError.UnsupportedRole)]
    [InlineData(1, "2AF", "Hip", 0, 7, RoofStructuralGeneratedDataError.NonPositiveBoundaryEdgeId)]
    [InlineData(1, "2AF", "Hip", 2, 2, RoofStructuralGeneratedDataError.SameBoundaryEdgeId)]
    [InlineData(1, "2AF", "Hip", 7, 2, RoofStructuralGeneratedDataError.NonCanonicalBoundaryPair)]
    public void InvalidStoredStructuralMetadata_FailsClosed(
        int schema,
        string? owner,
        string? role,
        int boundaryA,
        int boundaryB,
        RoofStructuralGeneratedDataError expected)
    {
        var result = RoofStructuralGeneratedDataRules.ValidateStored(
            schema,
            owner,
            role,
            boundaryA,
            boundaryB);

        Assert.False(result.IsValid);
        Assert.Null(result.Data);
        Assert.Equal(expected, result.Error);
    }

    [Fact]
    public void StructuralSchemas_AreIndependentFromOrdinaryRaftersAndTimber()
    {
        Assert.Equal(1, RoofStructuralGeneratedDataSchema.CurrentVersion);
        Assert.Equal(1, RoofGeneratedTimberDataSchema.CurrentVersion);
        Assert.Equal(7, AcKrovy.Core.Models.TimberElementDataSchema.CurrentVersion);
        Assert.Equal(1, (int)RoofStructuralRole.Hip);
        Assert.Equal(2, (int)RoofStructuralRole.Valley);
        Assert.Equal(3, (int)RoofStructuralRole.Ridge);
    }

    private static RoofStructuralLogicalKey Key(
        RoofStructuralRole role,
        int first,
        int second)
    {
        Assert.True(RoofStructuralIdentityRules.TryCreate(
            role,
            first,
            second,
            out var key,
            out _));
        return key!;
    }
}
