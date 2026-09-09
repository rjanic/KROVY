using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// HOST H7c: pre-command snapshot capture must share effective-closed normalization
/// with topology/validator. Prior tests always used IsClosed=true and never exercised
/// the AutoCAD Closed=false + duplicated terminal point path that produced
/// sourceVertexCount=7 normalizedVertexCount=0 reason=invalid-source-polyline.
/// </summary>
public sealed class RoofAssemblySnapshotHostPolylineNormalizationTests
{
    public static IEnumerable<object[]> HostClosedShapes()
    {
        yield return ["Rectangle", Rectangle(), 4];
        yield return ["L", LShape(), 6];
        yield return ["U", UShape(), 8];
        yield return ["T", TShape(), 8];
        yield return ["Offset", OffsetParallel(), 8];
    }

    [Theory]
    [MemberData(nameof(HostClosedShapes))]
    public void ExplicitClosingVertex_OpenFlag_CapturesWithNormalizedCount(
        string name,
        RoofPoint2D[] unique,
        int expectedNormalized)
    {
        var withoutCloseClosedFlag = new RoofFootprintInput(unique, IsClosed: true);
        var withCloseOpenFlag = new RoofFootprintInput(
            unique.Concat([unique[0]]).ToArray(),
            IsClosed: false);
        var withCloseClosedFlag = new RoofFootprintInput(
            unique.Concat([unique[0]]).ToArray(),
            IsClosed: true);

        Assert.Equal(unique.Length + 1, withCloseOpenFlag.Vertices!.Count);
        Assert.False(withCloseOpenFlag.IsClosed);
        Assert.True(RoofFootprintValidator.IsEffectivelyClosed(withCloseOpenFlag), name);
        Assert.True(RoofFootprintValidator.HasRepeatedClosingVertex(withCloseOpenFlag.Vertices), name);

        var stored = CreateHip(withoutCloseClosedFlag);

        Assert.True(
            RoofAssemblySnapshotCaptureRules.TryEvaluateSourceForCapture(
                withCloseOpenFlag,
                stored,
                out var sourceCount,
                out var normalizedCount,
                out var classifier,
                out var reason),
            $"{name}: {reason}");
        Assert.Equal(unique.Length + 1, sourceCount);
        Assert.Equal(expectedNormalized, normalizedCount);
        Assert.Equal(string.Empty, reason);
        Assert.True(
            classifier is RoofSourceChangeKind.RigidEquivalent or RoofSourceChangeKind.SupportedResize,
            $"{name}: {classifier}");

        Assert.True(
            RoofAssemblySnapshotCaptureRules.TryEvaluateSourceForCapture(
                withoutCloseClosedFlag,
                stored,
                out var sourceClosed,
                out var normalizedClosed,
                out _,
                out _),
            name);
        Assert.Equal(unique.Length, sourceClosed);
        Assert.Equal(expectedNormalized, normalizedClosed);

        Assert.True(
            RoofAssemblySnapshotCaptureRules.TryEvaluateSourceForCapture(
                withCloseClosedFlag,
                stored,
                out var sourceBoth,
                out var normalizedBoth,
                out _,
                out _),
            name);
        Assert.Equal(unique.Length + 1, sourceBoth);
        Assert.Equal(expectedNormalized, normalizedBoth);

        Assert.True(
            RoofAssemblySnapshotCaptureRules.ClosingVertexDuplicationIsSemanticallyEquivalent(
                withCloseOpenFlag,
                withoutCloseClosedFlag,
                stored),
            name);
    }

    [Fact]
    public void HostL_SevenVerticesOpenFlag_WouldHaveFailedOldClosedOnlyGate()
    {
        // Exact HOST L shape: sourceVertexCount=7, Closed=false.
        var unique = LShape();
        var hostLike = new RoofFootprintInput(
            unique.Concat([unique[0]]).ToArray(),
            IsClosed: false);
        Assert.Equal(7, hostLike.Vertices!.Count);
        Assert.False(hostLike.IsClosed);

        // Old gate: Vertices.Count >= 3 && !IsClosed → invalid-source-polyline
        var oldGateRejected = hostLike.Vertices is null ||
            hostLike.Vertices.Count < 3 ||
            !hostLike.IsClosed;
        Assert.True(oldGateRejected);

        var stored = CreateHip(new RoofFootprintInput(unique, true));
        Assert.True(RoofAssemblySnapshotCaptureRules.TryEvaluateSourceForCapture(
            hostLike,
            stored,
            out var sourceCount,
            out var normalizedCount,
            out _,
            out var reason));
        Assert.Equal(7, sourceCount);
        Assert.Equal(6, normalizedCount);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void HostSteppedNineVerticesOpenFlag_NormalizesToEight()
    {
        var unique = TShape();
        var hostLike = new RoofFootprintInput(
            unique.Concat([unique[0]]).ToArray(),
            IsClosed: false);
        Assert.Equal(9, hostLike.Vertices!.Count);

        var stored = CreateHip(new RoofFootprintInput(unique, true));
        Assert.True(RoofAssemblySnapshotCaptureRules.TryEvaluateSourceForCapture(
            hostLike,
            stored,
            out var sourceCount,
            out var normalizedCount,
            out _,
            out _));
        Assert.Equal(9, sourceCount);
        Assert.Equal(8, normalizedCount);
    }

    [Fact]
    public void TrulyOpenPolyline_StillRejectedAsInvalidSource()
    {
        var open = new RoofFootprintInput(LShape(), IsClosed: false);
        Assert.False(RoofFootprintValidator.IsEffectivelyClosed(open));
        Assert.False(RoofAssemblySnapshotCaptureRules.TryEvaluateSourceForCapture(
            open,
            CreateHip(new RoofFootprintInput(LShape(), true)),
            out _,
            out var normalized,
            out var classifier,
            out var reason));
        Assert.Equal(0, normalized);
        Assert.Equal(RoofSourceChangeKind.None, classifier);
        Assert.Equal("invalid-source-polyline", reason);
    }

    [Fact]
    public void SnapshotEligibility_AcceptsOpenFlagWithRepeatedClose()
    {
        var unique = LShape();
        var snap = new RoofUnsupportedStretchSourceSnapshotData(
            "291A",
            unique.Concat([unique[0]]).ToArray(),
            IsClosed: false,
            ElevationMm: 0d,
            NormalX: 0d,
            NormalY: 0d,
            NormalZ: 1d);
        Assert.True(RoofUnsupportedStretchRecoveryRules.IsEligibleSnapshot(snap));
        Assert.True(RoofUnsupportedStretchRecoveryRules.RestoredMatchesSnapshot(
            snap.Vertices,
            liveClosed: false,
            snap));
    }

    [Fact]
    public void SourceModifiedPriority_Unchanged()
    {
        Assert.Equal(
            RoofGeneratedMemberLockedTamperRules.Classification.SupportedSourceResize,
            RoofGeneratedMemberLockedTamperRules.Classify(
                RoofEditState.Locked,
                "STRETCH",
                sourceModified: true,
                generatedMemberModified: true,
                ownedAnnotationModified: true,
                RoofSourceChangeKind.SupportedResize));
        Assert.Equal(
            RoofGeneratedMemberLockedTamperRules.Classification.SupportedSourceResize,
            RoofGeneratedMemberLockedTamperRules.Classify(
                RoofEditState.Locked,
                "GRIP_STRETCH",
                sourceModified: true,
                generatedMemberModified: true,
                ownedAnnotationModified: false,
                RoofSourceChangeKind.SupportedResize));
    }

    private static RoofDefinitionData CreateHip(RoofFootprintInput input)
    {
        var validation = RoofFootprintValidator.Validate(input);
        Assert.True(validation.IsValid, validation.Error.ToString());
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(solved.IsValid, solved.Error.ToString());
        return RoofDefinitionPersistence.Create(
            input,
            validation.Footprint!,
            Assert.IsType<HipRoofGeometry>(solved.Geometry));
    }

    private static RoofPoint2D[] Rectangle() =>
        [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)];

    private static RoofPoint2D[] LShape() =>
    [
        new(0, 0), new(8000, 0), new(8000, 3000),
        new(3000, 3000), new(3000, 8000), new(0, 8000),
    ];

    private static RoofPoint2D[] UShape() =>
    [
        new(0, 0), new(10000, 0), new(10000, 9000), new(7000, 9000),
        new(7000, 3000), new(3000, 3000), new(3000, 9000), new(0, 9000),
    ];

    private static RoofPoint2D[] TShape() =>
    [
        new(0, 0), new(10000, 0), new(10000, 3000), new(6500, 3000),
        new(6500, 9000), new(3500, 9000), new(3500, 3000), new(0, 3000),
    ];

    private static RoofPoint2D[] OffsetParallel() =>
    [
        new(0, 0), new(16000, 0), new(16000, 5000), new(10000, 5000),
        new(10000, 12000), new(5000, 12000), new(5000, 3000), new(0, 3000),
    ];
}
