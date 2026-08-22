using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofGeneratedMemberTrimOverrideTests
{
    private static readonly RoofPoint3D ZUp = new(0d, 0d, 1d);
    private static readonly RoofGeneratedMemberKey Face0Station0 =
        new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 0);

    [Fact]
    public void UnlockedPureEndTrim_IsAccepted()
    {
        var canonical = Horizontal(5200d);
        var observed = new RoofGeneratedMemberGeometry(canonical.Start, new(4900d, 0d, 0d));
        AssertEndpoint(canonical, observed, start: 0d, end: -300d);
    }

    [Fact]
    public void LogicalStartEndpointTrim_IsAccepted()
    {
        var canonical = Horizontal(5200d);
        var observed = new RoofGeneratedMemberGeometry(new(300d, 0d, 0d), canonical.End);
        AssertEndpoint(canonical, observed, start: -300d, end: 0d);
    }

    [Fact]
    public void LogicalEndEndpointTrim_IsAccepted()
    {
        var canonical = Horizontal(5200d);
        var observed = new RoofGeneratedMemberGeometry(canonical.Start, new(4700d, 0d, 0d));
        AssertEndpoint(canonical, observed, start: 0d, end: -500d);
    }

    [Fact]
    public void ReversedLineDirection_MapsToLogicalEndOffset()
    {
        var canonical = Horizontal(5200d);
        var reversedTrim = new RoofGeneratedMemberGeometry(
            new(4900d, 0d, 1800d),
            new(0d, 0d, 0d));
        Assert.False(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, reversedTrim, ZUp, Face0Station0, "K4", out _));
        AssertEndpoint(canonical, reversedTrim, start: 0d, end: -300d);
    }

    [Fact]
    public void IncrementalSecondTrim_ComposesWithFirst()
    {
        var canonical = Horizontal(5200d);
        var firstObserved = new RoofGeneratedMemberGeometry(canonical.Start, new(5000d, 0d, 0d));
        AssertEndpoint(canonical, firstObserved, start: 0d, end: -200d);
        var first = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null, Face0Station0, "K4", 0d, -200d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, first, out var baseline));
        var secondObserved = new RoofGeneratedMemberGeometry(baseline.Start, new(4700d, 0d, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            baseline, secondObserved, ZUp, out var startDelta, out var endDelta, out _, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, reason);
        Assert.Equal(0d, startDelta, 6);
        Assert.Equal(-300d, endDelta, 6);
        var composed = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            first, Face0Station0, "K4", startDelta, endDelta);
        Assert.Equal(-500d, composed!.EndOffsetMm, 6);
        Assert.Equal(0d, composed.StartOffsetMm, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, composed, out var replayed));
        Assert.Equal(4700d, replayed.LengthMm, 6);
    }

    [Fact]
    public void Trim_PreservesExistingMoveOverride()
    {
        var canonical = Horizontal(5200d);
        var move = new RoofGeneratedMemberOverride(Face0Station0, false, 150d, 40d, 0d, 0d, 0d, "K4");
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, move, out var baseline));
        var trimmed = new RoofGeneratedMemberGeometry(
            baseline.Start,
            new(baseline.End.X - 250d, baseline.End.Y, baseline.End.Z));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            baseline, trimmed, ZUp, out var startDelta, out var endDelta, out _, out _));
        var composed = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            move, Face0Station0, "K4", startDelta, endDelta);
        Assert.Equal(150d, composed!.AlongMm, 6);
        Assert.Equal(40d, composed.LateralMm, 6);
        Assert.Equal(0d, composed.RotationRadians, 8);
        Assert.Equal(-250d, composed.EndOffsetMm, 6);
        Assert.Equal(0d, composed.StartOffsetMm, 6);
    }

    [Fact]
    public void Trim_PreservesExistingRotateOverride()
    {
        var canonical = Horizontal(5000d);
        var rotate = new RoofGeneratedMemberOverride(Face0Station0, false, 0d, 0d, 0.3d, 0d, 0d, "K4");
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, rotate, out var baseline));
        Assert.True(RoofGeneratedMemberOverrideMath.TryCreateBasis(baseline, ZUp, out var basis));
        var trimmed = new RoofGeneratedMemberGeometry(
            baseline.Start,
            Add(baseline.End, Scale(basis.AxisU, -200d)));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            baseline, trimmed, ZUp, out var startDelta, out var endDelta, out _, out _));
        var composed = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            rotate, Face0Station0, "K4", startDelta, endDelta);
        Assert.Equal(0.3d, composed!.RotationRadians, 8);
        Assert.Equal(0d, composed.AlongMm, 6);
        Assert.Equal(-200d, composed.EndOffsetMm, 6);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, composed, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(trimmed, replayed));
    }

    [Fact]
    public void Trim_PreservesOppositeEndpointOverride()
    {
        var existing = new RoofGeneratedMemberOverride(Face0Station0, false, 0d, 0d, 0d, -300d, 0d, "K4");
        var canonical = Horizontal(5200d);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, existing, out var baseline));
        var trimmed = new RoofGeneratedMemberGeometry(baseline.Start, new(baseline.End.X - 200d, 0d, 0d));
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            baseline, trimmed, ZUp, out var startDelta, out var endDelta, out _, out _));
        var composed = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            existing, Face0Station0, "K4", startDelta, endDelta);
        Assert.Equal(-300d, composed!.StartOffsetMm, 6);
        Assert.Equal(-200d, composed.EndOffsetMm, 6);
        Assert.Equal(0d, composed.AlongMm, 6);
    }

    [Fact]
    public void InvalidZeroLength_IsRejected()
    {
        var canonical = Horizontal(4000d);
        var collapsed = new RoofGeneratedMemberGeometry(new(1000d, 0d, 0d), new(1000d, 0d, 0d));
        Assert.False(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical, collapsed, ZUp, out _, out _, out _, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.InvalidLength, reason);
    }

    [Fact]
    public void NonCollinearEndpointChange_IsRejected()
    {
        var canonical = Horizontal(4000d);
        var skewed = new RoofGeneratedMemberGeometry(canonical.Start, new(3700d, 80d, 0d));
        Assert.False(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical, skewed, ZUp, out _, out _, out _, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.NonCollinear, reason);
    }

    [Fact]
    public void OffPlaneMove_IsStillRejectedByFullClassify()
    {
        var canonical = Horizontal(4000d);
        var lifted = new RoofGeneratedMemberGeometry(
            new RoofPoint3D(0d, 0d, 50d),
            new RoofPoint3D(4000d, 0d, 50d));
        Assert.False(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, lifted, ZUp, Face0Station0, null, out _));
        var offAxisAndLifted = new RoofGeneratedMemberGeometry(canonical.Start, new(3700d, 80d, 1800d));
        Assert.False(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical, offAxisAndLifted, ZUp, out _, out _, out _, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.NonCollinear, reason);
    }

    [Fact]
    public void ThreeDimensionalCuttingEdgeTrim_ProjectsOntoWorkingPlane()
    {
        var canonical = Horizontal(5200d);
        var apparent = new RoofGeneratedMemberGeometry(
            canonical.Start,
            new(4900d, 0d, 1800d));
        Assert.False(RoofGeneratedMemberOverrideMath.TryClassify(
            canonical, apparent, ZUp, Face0Station0, "K4", out _));
        AssertEndpoint(canonical, apparent, start: 0d, end: -300d);
    }

    [Fact]
    public void NearCollinearNumericNoise_IsAcceptedAsTrim()
    {
        var canonical = Horizontal(5200d);
        var noisy = new RoofGeneratedMemberGeometry(canonical.Start, new(4900d, 0.005d, 0d));
        AssertEndpoint(canonical, noisy, start: 0d, end: -300d);
    }

    [Fact]
    public void BothEndpointsChanged_IsRejected()
    {
        var canonical = Horizontal(5200d);
        var both = new RoofGeneratedMemberGeometry(new(200d, 0d, 0d), new(4900d, 0d, 0d));
        Assert.False(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical, both, ZUp, out _, out _, out _, out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.BothEndpointsChanged, reason);
    }

    [Fact]
    public void Schema3_RoundTripsEndpointOffsets()
    {
        var original = new RoofDefinitionData(
            RoofDefinitionDataSchema.HybridLifecycleVersion,
            RoofKind.SimpleGable,
            35d,
            RidgeEdgeFamily: RoofRidgeEdgeFamily.SourceEdge01,
            RigidFootprint: new RoofRigidFootprintDescriptor(
                4,
                RoofPolygonOrientation.CounterClockwise,
                10000d,
                6000d),
            EditState: RoofEditState.Unlocked,
            ManualOverrides:
            [
                new RoofGeneratedMemberOverride(Face0Station0, false, 150d, 0d, 0d, -200d, -500d, "K12"),
            ]);
        var payload = RoofDefinitionDataCodec.Encode(original);
        Assert.StartsWith("3|", payload, StringComparison.Ordinal);
        Assert.True(RoofDefinitionDataCodec.TryDecode(payload, out var decoded, out _));
        Assert.Equal(RoofEditState.Unlocked, decoded!.EditState);
        Assert.Equal(-200d, decoded.Overrides[0].StartOffsetMm);
        Assert.Equal(-500d, decoded.Overrides[0].EndOffsetMm);
        Assert.Equal(150d, decoded.Overrides[0].AlongMm);
        Assert.Equal("K12", decoded.Overrides[0].ReservedElementId);
        Assert.Equal(payload, RoofDefinitionDataCodec.Encode(decoded));
    }

    [Fact]
    public void ReasonTokens_MatchHostDiagnostics()
    {
        Assert.Equal(
            "off-plane-result",
            RoofGeneratedMemberOverrideMath.ToReasonToken(RoofGeneratedMemberManualEditReason.OffPlane));
        Assert.Equal(
            "non-collinear-result",
            RoofGeneratedMemberOverrideMath.ToReasonToken(RoofGeneratedMemberManualEditReason.NonCollinear));
        Assert.Equal(
            "both-endpoints-changed",
            RoofGeneratedMemberOverrideMath.ToReasonToken(RoofGeneratedMemberManualEditReason.BothEndpointsChanged));
        Assert.Equal(
            "invalid-zero-length",
            RoofGeneratedMemberOverrideMath.ToReasonToken(RoofGeneratedMemberManualEditReason.InvalidLength));
    }

    [Fact]
    public void TrimCommandRule_IsEndpointOnly()
    {
        Assert.True(RoofGeneratedMemberEditCommandRules.IsEndpointTrimOrExtendCommand("TRIM"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsEndpointTrimOrExtendCommand("_EXTEND"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsEndpointTrimOrExtendCommand("MOVE"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsTrimCommand("TRIM"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsTrimCommand("_TRIM"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsTrimCommand("EXTEND"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsTrimCommand("MOVE"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsExtendCommand("EXTEND"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsExtendCommand("_EXTEND"));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsExtendCommand("TRIM"));
    }

    [Fact]
    public void ShortenedPlanLength_UpdatesSlopeAwareMeasurement()
    {
        var data = new AcKrovy.Core.Models.TimberElementData
        {
            SchemaVersion = AcKrovy.Core.Models.TimberElementDataSchema.CurrentVersion,
            ElementId = "K4",
            ElementType = AcKrovy.Core.Models.TimberElementType.Rafter,
            WidthMm = 80,
            HeightMm = 160,
            SlopeDegrees = 35d,
        };
        var full = AcKrovy.Core.Services.TimberCalculator.Measure(data, 5200d);
        var trimmed = AcKrovy.Core.Services.TimberCalculator.Measure(data, 4900d);
        Assert.Equal("K4", trimmed.Data.ElementId);
        Assert.True(trimmed.ActualLengthMm < full.ActualLengthMm);
        Assert.True(trimmed.CuttingLengthMm < full.CuttingLengthMm);
        Assert.True(trimmed.VolumeM3 < full.VolumeM3);
        Assert.Equal(4900d, trimmed.PlanLengthMm);
    }

    private static void AssertEndpoint(
        RoofGeneratedMemberGeometry canonical,
        RoofGeneratedMemberGeometry observed,
        double start,
        double end)
    {
        Assert.True(RoofGeneratedMemberOverrideMath.TryClassifyCollinearEndpointEdit(
            canonical,
            observed,
            ZUp,
            out var startDelta,
            out var endDelta,
            out var accepted,
            out var reason));
        Assert.Equal(RoofGeneratedMemberManualEditReason.Accepted, reason);
        Assert.Equal(start, startDelta, 6);
        Assert.Equal(end, endDelta, 6);
        var composed = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            null,
            Face0Station0,
            "K4",
            startDelta,
            endDelta);
        Assert.NotNull(composed);
        Assert.True(RoofGeneratedMemberOverrideMath.TryApply(canonical, ZUp, composed, out var replayed));
        Assert.True(RoofGeneratedMemberOverrideMath.GeometryEquals(accepted, replayed));
        Assert.True(accepted.Start.Z == 0d && accepted.End.Z == 0d);
    }

    private static RoofGeneratedMemberGeometry Horizontal(double length) =>
        new(new RoofPoint3D(0d, 0d, 0d), new RoofPoint3D(length, 0d, 0d));

    private static RoofPoint3D Add(RoofPoint3D left, RoofPoint3D right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static RoofPoint3D Scale(RoofPoint3D vector, double scalar) =>
        new(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);
}
