using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// CAD-neutral behavior of the AK_ROOF_EDIT rebase path:
/// RoofDefinitionPersistence.UpdateGeometry must apply only the intentionally
/// edited physical fields, always write schema 5 and preserve edit state and
/// manual overrides verbatim.
/// </summary>
public sealed class RoofDefinitionUpdateGeometryTests
{
    [Fact]
    public void SimpleToAsymmetric_RebasesKindSlopesAndDeltaHeight()
    {
        var source = RectangleInput();
        var footprint = Validate(source);
        var existing = V5Data(RoofKind.SimpleGable, 30d, 30d, 0d, RoofRidgeEdgeFamily.SourceEdge01);
        var asymmetric = Solve(footprint, 20d, 35d, 1d, 0d, 450d);

        var updated = RoofDefinitionPersistence.UpdateGeometry(existing, source, asymmetric);

        Assert.Equal(RoofDefinitionDataSchema.CurrentVersion, updated.SchemaVersion);
        Assert.Equal(RoofKind.AsymmetricGable, updated.Kind);
        Assert.Equal(20d, updated.Face0SlopeDegrees);
        Assert.Equal(35d, updated.EffectiveFace1SlopeDegrees);
        Assert.Equal(450d, updated.EaveHeightDifferenceMm);
        Assert.Equal(RoofRidgeEdgeFamily.SourceEdge01, updated.RidgeEdgeFamily);
        Assert.NotNull(updated.RigidFootprint);
        Assert.Equal(RoofEditState.Locked, updated.EditState);
        Assert.Empty(updated.Overrides);
        AssertRoundTrips(updated);
    }

    [Fact]
    public void AsymmetricToSimple_ForcesEqualSlopesAndZeroDeltaHeight()
    {
        var source = RectangleInput();
        var footprint = Validate(source);
        var existing = V5Data(RoofKind.AsymmetricGable, 20d, 35d, 450d, RoofRidgeEdgeFamily.SourceEdge01);
        var symmetric = Solve(footprint, 30d, 30d, 1d, 0d, 0d);

        var updated = RoofDefinitionPersistence.UpdateGeometry(existing, source, symmetric);

        Assert.Equal(RoofKind.SimpleGable, updated.Kind);
        Assert.Equal(30d, updated.Face0SlopeDegrees);
        Assert.Equal(updated.Face0SlopeDegrees, updated.EffectiveFace1SlopeDegrees);
        Assert.Equal(0d, updated.EaveHeightDifferenceMm);
        AssertRoundTrips(updated);
    }

    [Fact]
    public void EditStateAndManualOverrides_SurviveTheRebase()
    {
        var source = RectangleInput();
        var footprint = Validate(source);
        var key = new RoofGeneratedMemberKey(
            RoofGeneratedTimberKind.Rafter,
            RafterRoofFace.Face0,
            StationIndex: 2);
        var overrideData = RoofGeneratedMemberOverride.Suppress(key, reservedElementId: "K7");
        var existing = V5Data(RoofKind.AsymmetricGable, 20d, 35d, 450d, RoofRidgeEdgeFamily.SourceEdge01) with
        {
            EditState = RoofEditState.Unlocked,
            ManualOverrides = [overrideData],
        };
        var edited = Solve(footprint, 25d, 40d, 1d, 0d, 300d);

        var updated = RoofDefinitionPersistence.UpdateGeometry(existing, source, edited);

        Assert.Equal(RoofEditState.Unlocked, updated.EditState);
        var restoredOverride = Assert.Single(updated.Overrides);
        Assert.Equal(key, restoredOverride.Key);
        Assert.True(restoredOverride.Suppressed);
        Assert.Equal("K7", restoredOverride.ReservedElementId);
        AssertRoundTrips(updated);
    }

    [Fact]
    public void RidgeDirectionChange_ReResolvesTheEdgeFamily()
    {
        var source = RectangleInput();
        var footprint = Validate(source);
        var existing = V5Data(RoofKind.SimpleGable, 30d, 30d, 0d, RoofRidgeEdgeFamily.SourceEdge01);
        var rotated = Solve(footprint, 30d, 30d, 0d, 1d);

        var updated = RoofDefinitionPersistence.UpdateGeometry(existing, source, rotated);

        Assert.Equal(RoofRidgeEdgeFamily.SourceEdge12, updated.RidgeEdgeFamily);
        AssertRoundTrips(updated);
    }

    [Fact]
    public void SameRidgeDirection_KeepsTheEdgeFamily()
    {
        var source = RectangleInput();
        var footprint = Validate(source);
        var existing = V5Data(RoofKind.SimpleGable, 30d, 30d, 0d, RoofRidgeEdgeFamily.SourceEdge01);
        var same = Solve(footprint, 35d, 35d, 1d, 0d);

        var updated = RoofDefinitionPersistence.UpdateGeometry(existing, source, same);

        Assert.Equal(RoofRidgeEdgeFamily.SourceEdge01, updated.RidgeEdgeFamily);
    }

    [Fact]
    public void LegacySchemaOne_RebasesToSchemaFiveWithRigidFootprint()
    {
        var source = RectangleInput();
        var footprint = Validate(source);
        var legacy = new RoofDefinitionData(
            RoofDefinitionDataSchema.LegacyAbsoluteVersion,
            RoofKind.SimpleGable,
            30d,
            RidgeDirectionX: 1d,
            RidgeDirectionY: 0d,
            FootprintSignature: footprint.Signature);
        var edited = Solve(footprint, 25d, 25d, 1d, 0d);

        var updated = RoofDefinitionPersistence.UpdateGeometry(legacy, source, edited);

        Assert.Equal(RoofDefinitionDataSchema.CurrentVersion, updated.SchemaVersion);
        Assert.NotNull(updated.RigidFootprint);
        Assert.Equal(4, updated.RigidFootprint!.VertexCount);
        Assert.NotNull(updated.RidgeEdgeFamily);
        AssertRoundTrips(updated);
    }

    [Fact]
    public void InvalidExistingDefinition_IsRejected()
    {
        var source = RectangleInput();
        var geometry = Solve(Validate(source), 30d, 30d, 1d, 0d);
        var invalid = V5Data(RoofKind.SimpleGable, 20d, 35d, 0d, RoofRidgeEdgeFamily.SourceEdge01);

        Assert.Throws<ArgumentException>(() =>
            RoofDefinitionPersistence.UpdateGeometry(invalid, source, geometry));
    }

    [Fact]
    public void SourceTopologyCannotRepresentRidgeAxis_IsRejected()
    {
        var degenerateSource = new RoofFootprintInput(
        [
            new RoofPoint2D(0d, 0d),
            new RoofPoint2D(10000d, 0d),
            new RoofPoint2D(10000d, 6000d),
        ], true, false, true);
        var existing = V5Data(RoofKind.SimpleGable, 30d, 30d, 0d, RoofRidgeEdgeFamily.SourceEdge01);
        var geometry = Solve(Validate(RectangleInput()), 30d, 30d, 1d, 0d);

        Assert.Throws<ArgumentException>(() =>
            RoofDefinitionPersistence.UpdateGeometry(existing, degenerateSource, geometry));
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        var source = RectangleInput();
        var geometry = Solve(Validate(source), 30d, 30d, 1d, 0d);
        var data = V5Data(RoofKind.SimpleGable, 30d, 30d, 0d, RoofRidgeEdgeFamily.SourceEdge01);

        Assert.Throws<ArgumentNullException>(() =>
            RoofDefinitionPersistence.UpdateGeometry(null!, source, geometry));
        Assert.Throws<ArgumentNullException>(() =>
            RoofDefinitionPersistence.UpdateGeometry(data, null!, geometry));
        Assert.Throws<ArgumentNullException>(() =>
            RoofDefinitionPersistence.UpdateGeometry(data, source, null!));
    }

    private static void AssertRoundTrips(RoofDefinitionData data)
    {
        var payload = RoofDefinitionDataCodec.Encode(data);
        Assert.True(RoofDefinitionDataCodec.TryDecode(payload, out var decoded, out var error));
        Assert.Equal(RoofDefinitionDataDecodeError.None, error);
        Assert.Equal(RoofDefinitionDataSchema.CurrentVersion, decoded!.SchemaVersion);
    }

    private static RoofDefinitionData V5Data(
        RoofKind kind,
        double slope0,
        double slope1,
        double eaveHeightDifferenceMm,
        RoofRidgeEdgeFamily family) =>
        new(
            RoofDefinitionDataSchema.CurrentVersion,
            kind,
            slope0,
            RidgeEdgeFamily: family,
            RigidFootprint: new RoofRigidFootprintDescriptor(
                4,
                RoofPolygonOrientation.CounterClockwise,
                10000d,
                6000d),
            Face1SlopeDegrees: slope1,
            EaveHeightDifferenceMm: eaveHeightDifferenceMm);

    private static SimpleGableRoofGeometry Solve(
        RoofFootprint footprint,
        double slope0,
        double slope1,
        double directionX,
        double directionY,
        double eaveHeightDifferenceMm = 0d)
    {
        Assert.True(RoofDirection2D.TryCreate(directionX, directionY, out var direction));
        var kind = Math.Abs(slope0 - slope1) <= SimpleGableRoofGeometryTolerance.AngularTolerance &&
                   Math.Abs(eaveHeightDifferenceMm) <= SimpleGableRoofGeometryTolerance.CoordinateToleranceMm
            ? RoofKind.SimpleGable
            : RoofKind.AsymmetricGable;
        var result = RoofGeometrySolver.Solve(new RoofDefinition(
            footprint,
            new RoofParameters(
                slope0,
                direction,
                Face1SlopeDegrees: slope1,
                EaveHeightDifferenceMm: eaveHeightDifferenceMm),
            kind));
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<SimpleGableRoofGeometry>(result.Geometry);
    }

    private static RoofFootprintInput RectangleInput() => new(
    [
        new RoofPoint2D(0d, 0d),
        new RoofPoint2D(10000d, 0d),
        new RoofPoint2D(10000d, 6000d),
        new RoofPoint2D(0d, 6000d),
    ], true, false, true);

    private static RoofFootprint Validate(RoofFootprintInput input)
    {
        var result = RoofFootprintValidator.Validate(input);
        Assert.True(result.IsValid, result.Error.ToString());
        return Assert.IsType<RoofFootprint>(result.Footprint);
    }
}
