using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOrdinaryAuthorityTransitionTests
{
    [Theory]
    [InlineData("MOVE")]
    [InlineData("GRIP_STRETCH")]
    public void FirstGeometricEdit_RequiresDetachDecision(string command) =>
        Assert.Equal(RoofOrdinaryEditDecision.PromptDetach,
            RoofOrdinaryAuthorityTransitionRules.Decide(
                RoofOrdinaryGeometryAuthority.RoofOwned, command,
                geometryChanged: true, sourceChanged: false, roofLocked: false));

    [Theory]
    [InlineData("MOVE")]
    [InlineData("GRIP_STRETCH")]
    public void AlreadyIndependent_DoesNotPromptAgain(string command) =>
        Assert.Equal(RoofOrdinaryEditDecision.KeepIndependent,
            RoofOrdinaryAuthorityTransitionRules.Decide(
                RoofOrdinaryGeometryAuthority.Independent, command,
                geometryChanged: true, sourceChanged: false, roofLocked: false));

    [Theory]
    [InlineData("MOVE", false, false)]
    [InlineData("GRIP_STRETCH", false, false)]
    [InlineData("PROPERTY", true, false)]
    [InlineData("MOVE", true, true)]
    public void MetadataOrWholeRoofEdit_DoesNotDetach(string command,
        bool geometryChanged, bool sourceChanged) =>
        Assert.Equal(RoofOrdinaryEditDecision.NoTransition,
            RoofOrdinaryAuthorityTransitionRules.Decide(
                RoofOrdinaryGeometryAuthority.RoofOwned, command,
                geometryChanged, sourceChanged, roofLocked: false));

    [Fact]
    public void LockedRoof_EditCannotDetach() =>
        Assert.Equal(RoofOrdinaryEditDecision.LockedRoof,
            RoofOrdinaryAuthorityTransitionRules.Decide(
                RoofOrdinaryGeometryAuthority.RoofOwned, "MOVE",
                geometryChanged: true, sourceChanged: false, roofLocked: true));

    [Theory]
    [InlineData(RoofOrdinaryGeometryAuthority.RoofOwned, false)]
    [InlineData(RoofOrdinaryGeometryAuthority.RoofOwned, true)]
    [InlineData(RoofOrdinaryGeometryAuthority.Independent, false)]
    public void DirectPhysicalEdit_NeverBecomesUserGeometryAuthority(
        RoofOrdinaryGeometryAuthority authority, bool locked)
    {
        Assert.Equal(RoofOrdinaryEditRepresentation.Plan2D,
            RoofOrdinaryAuthorityTransitionRules.UserGeometryEditAuthority);
        Assert.Equal(RoofOrdinaryEditDecision.RestoreDerivedPhysical,
            RoofOrdinaryAuthorityTransitionRules.Decide(authority, "MOVE",
                geometryChanged: true, sourceChanged: false, roofLocked: locked,
                editRepresentation: RoofOrdinaryEditRepresentation.Physical3D));
    }
}
