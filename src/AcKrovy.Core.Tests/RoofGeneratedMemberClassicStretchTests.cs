using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedMemberClassicStretchTests
{
    private static readonly RoofPoint3D ZUp = new(0d, 0d, 1d);
    private static readonly RoofGeneratedMemberKey Face0Station0 =
        new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 0);

    [Fact]
    public void UnlockedClassicStretch_IsEligibleForExistingClassifier()
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsClassicStretch("STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand("STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand("STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsTargetedRecalcCommand("STRETCH"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsGeneratedTimberEditCommand("STRETCH"));
    }

    [Fact]
    public void RepresentableStretch_Translation_IsAccepted()
    {
        var canonical = Horizontal(5000d);
        var observed = Translate(canonical, 0d, 180d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyPureTranslation(
            canonical, observed, ZUp, out _, out _, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, reason);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, observed, ZUp, Face0Station0, "K4", out var overrideData));
        Assert.Equal(180d, overrideData!.LateralMm, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(observed, replayed));
    }

    [Fact]
    public void RepresentableStretch_CollinearEndpoint_IsAccepted()
    {
        var canonical = Horizontal(5000d);
        var observed = new RoofGeneratedMemberGeometry(
            canonical.Start,
            new RoofPoint3D(5600d, 0d, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical, observed, ZUp, out _, out var endDelta, out _, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, reason);
        Assert.Equal(600d, endDelta, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, observed, ZUp, Face0Station0, "K4", out var overrideData));
        Assert.Equal(600d, overrideData!.EndOffsetMm, 6);
    }

    [Fact]
    public void RepresentableStretch_EqualLengthRotateTranslate_IsAccepted()
    {
        var canonical = Horizontal(5000d);
        var observed = Translate(RotateAround(canonical, canonical.Start, 0.2d), 80d, 40d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, observed, ZUp, Face0Station0, "K4", out var overrideData));
        Assert.NotNull(overrideData);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(observed, replayed));
    }

    [Fact]
    public void RepresentableStretch_AngledEndpoint_IsAccepted()
    {
        var canonical = Horizontal(5000d);
        var observed = new RoofGeneratedMemberGeometry(
            canonical.Start,
            new RoofPoint3D(4800d, 350d, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, observed, ZUp, Face0Station0, "K4", out var overrideData));
        Assert.NotNull(overrideData);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, overrideData, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(observed, replayed));
    }

    [Fact]
    public void ExistingTrimThenStretch_Composes()
    {
        var canonical = Horizontal(5000d);
        var trim = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, Face0Station0, "K4", 0d, -300d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, trim, out var baseline));
        var stretched = Translate(baseline, 0d, 120d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, stretched, ZUp, Face0Station0, "K4", out var composed));
        Assert.Equal(-300d, composed!.EndOffsetMm, 6);
        Assert.Equal(120d, composed.LateralMm, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, composed, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(stretched, replayed));
    }

    [Fact]
    public void OsNapZ_IsProjectedBeforeClassify()
    {
        var canonical = Horizontal(5000d);
        var raw = new RoofGeneratedMemberGeometry(
            new RoofPoint3D(0d, 0d, 1335.853d),
            new RoofPoint3D(5300d, 0d, 1335.853d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(canonical, ZUp, out var basis));
        var observed = RoofGeneratedMemberOverrideMath.NormalizeToBasis(raw, basis, out var maxZ);
        Assert.Equal(1335.853d, maxZ, 6);
        Assert.Equal(0d, observed.Start.Z, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, observed, ZUp, Face0Station0, "K4", out var overrideData));
        Assert.Equal(300d, overrideData!.EndOffsetMm, 6);
    }

    [Fact]
    public void UnrepresentableAndOffPlane_AreRejected()
    {
        var canonical = Horizontal(4000d);
        var lifted = new RoofGeneratedMemberGeometry(
            new RoofPoint3D(0d, 0d, 40d),
            new RoofPoint3D(4000d, 0d, 80d));
        Assert.False(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, lifted, ZUp, Face0Station0, "K4", out _));
        Assert.Equal(
            "unrepresentable-stretch",
            RoofGeneratedMemberOverrideMath.ToReasonToken(
                RoofGeneratedMemberManualEditReason.UnrepresentableStretch));
        Assert.Equal(
            "unsupported-grip",
            RoofGeneratedMemberOverrideMath.ToReasonToken(
                RoofGeneratedMemberManualEditReason.UnsupportedGrip));
    }

    [Fact]
    public void SchemaStaysAtThree()
    {
        Assert.Equal(3, RoofDefinitionDataSchema.CurrentVersion);
    }

    private static RoofGeneratedMemberGeometry Horizontal(double length) =>
        new(new RoofPoint3D(0d, 0d, 0d), new RoofPoint3D(length, 0d, 0d));

    private static RoofGeneratedMemberGeometry Translate(
        RoofGeneratedMemberGeometry geometry,
        double along,
        double lateral) =>
        new(
            new RoofPoint3D(geometry.Start.X + along, geometry.Start.Y + lateral, geometry.Start.Z),
            new RoofPoint3D(geometry.End.X + along, geometry.End.Y + lateral, geometry.End.Z));

    private static RoofGeneratedMemberGeometry RotateAround(
        RoofGeneratedMemberGeometry geometry,
        RoofPoint3D origin,
        double radians)
    {
        return new RoofGeneratedMemberGeometry(
            RotatePoint(geometry.Start, origin, radians),
            RotatePoint(geometry.End, origin, radians));
    }

    private static RoofPoint3D RotatePoint(RoofPoint3D point, RoofPoint3D origin, double radians)
    {
        var dx = point.X - origin.X;
        var dy = point.Y - origin.Y;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new RoofPoint3D(
            origin.X + (dx * cos) - (dy * sin),
            origin.Y + (dx * sin) + (dy * cos),
            point.Z);
    }
}
