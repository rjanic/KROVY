using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofIndependentOrdinaryTimberDataCodecTests
{
    [Theory]
    [InlineData(RoofIndependentOrdinaryEntityRole.PlanLine)]
    [InlineData(RoofIndependentOrdinaryEntityRole.PhysicalSolid)]
    [InlineData(RoofIndependentOrdinaryEntityRole.Annotation)]
    public void DetachedIdentity_RoundTripsWithProvenanceOnly(RoofIndependentOrdinaryEntityRole role)
    {
        var original = new RoofIndependentOrdinaryTimberData(1,
            "edc10e5b07f04e44accd4a842ed2b570",
            RoofIndependentOrdinaryOriginKind.DetachedFromAuto,
            role, "AB12", new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 4));

        Assert.True(RoofIndependentOrdinaryTimberDataCodec.TryDecode(
            RoofIndependentOrdinaryTimberDataCodec.Encode(original), out var decoded));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void PlanAndPhysicalShareMemberIdentity_ButDifferentEntityRoles()
    {
        var plan = new RoofIndependentOrdinaryTimberData(1,
            "edc10e5b07f04e44accd4a842ed2b570",
            RoofIndependentOrdinaryOriginKind.DetachedFromAuto,
            RoofIndependentOrdinaryEntityRole.PlanLine, "AB12", null);
        var physical = plan with { EntityRole = RoofIndependentOrdinaryEntityRole.PhysicalSolid };
        Assert.Equal(plan.IndependentMemberId, physical.IndependentMemberId);
        Assert.NotEqual(plan.EntityRole, physical.EntityRole);
        Assert.Equal(plan, Decode(RoofIndependentOrdinaryTimberDataCodec.Encode(plan)));
        Assert.Equal(physical, Decode(RoofIndependentOrdinaryTimberDataCodec.Encode(physical)));
    }

    [Theory]
    [InlineData("2|edc10e5b07f04e44accd4a842ed2b570|DetachedFromAuto|PlanLine|AB12|Rafter|Face0|4")]
    [InlineData("1|bad-guid|DetachedFromAuto|PlanLine|AB12|Rafter|Face0|4")]
    [InlineData("1|edc10e5b07f04e44accd4a842ed2b570|DetachedFromAuto|PlanLine|AB12|Rafter|-|4")]
    public void InvalidOrFuturePayload_IsNotAdopted(string payload) =>
        Assert.False(RoofIndependentOrdinaryTimberDataCodec.TryDecode(payload, out _));

    private static RoofIndependentOrdinaryTimberData? Decode(string payload)
    {
        Assert.True(RoofIndependentOrdinaryTimberDataCodec.TryDecode(payload, out var value));
        return value;
    }
}
