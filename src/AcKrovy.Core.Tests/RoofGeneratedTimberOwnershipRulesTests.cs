using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedTimberOwnershipRulesTests
{
    [Fact]
    public void UniqueStations_AreReplaceable()
    {
        var members = new[]
        {
            Member("A", RafterRoofFace.Face0, 0),
            Member("A", RafterRoofFace.Face0, 1),
            Member("A", RafterRoofFace.Face1, 0),
            Member("A", RafterRoofFace.Face1, 1),
        };

        Assert.True(RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(members));
    }

    [Fact]
    public void DuplicateStations_MeanCollapsedCopyOwnership()
    {
        var members = new[]
        {
            Member("A", RafterRoofFace.Face0, 0),
            Member("A", RafterRoofFace.Face0, 1),
            Member("A", RafterRoofFace.Face0, 0), // copied set still claiming owner A
            Member("A", RafterRoofFace.Face0, 1),
        };

        Assert.False(RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(members));
    }

    [Fact]
    public void EmptyOrNullMembers_AreNotReplaceable()
    {
        Assert.False(RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations([]));
        Assert.False(RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(null!));
    }

    private static RoofGeneratedTimberData Member(
        string owner,
        RafterRoofFace face,
        int station) =>
        new(
            RoofGeneratedTimberDataSchema.CurrentVersion,
            owner,
            RoofGeneratedTimberKind.Rafter,
            face,
            station,
            2,
            900d,
            "sig");
}
