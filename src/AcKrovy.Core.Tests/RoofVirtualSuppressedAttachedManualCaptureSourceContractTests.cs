using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofVirtualSuppressedAttachedManualCaptureSourceContractTests
{
    private static readonly string Manual = ReadAutoCad("RoofGeneratedMemberManualEditService.cs");
    private static readonly string Context = ReadAutoCad("RoofGeneratedAnchorResolutionContext.cs");
    private static readonly string Diag = ReadAutoCad("RoofGeneratedMemberManualEditDiag.cs");
    private static readonly string CommandRules = RoofUxSourceContractText.Read(
        "src", "AcKrovy.Core", "Services", "Roofs", "RoofGeneratedMemberEditCommandRules.cs");

    [Fact]
    public void SplitCapture_BuildsOneOwnerScopedContextFromAlreadyRestoredGeometry()
    {
        var factory = Member(
            Manual,
            "private static RoofGeneratedAnchorResolutionContext? CreateSplitAnchorResolutionContext",
            "private static bool TryClassifyAcceptedMemberEdit");

        Assert.Contains("RoofGeneratedRafterSetService.TryRecoverRecipe", factory);
        Assert.Contains("SimpleGableRafterLayoutSolver.Solve", factory);
        Assert.Contains("RoofGeneratedAnchorResolutionContext.TryCreate", factory);
        Assert.Contains("physicalGeneratedIds", factory);
        Assert.Equal(1, Count(factory, "SimpleGableRafterLayoutSolver.Solve("));

        var accept = Member(Manual, "private static bool TryAcceptUnlockedEdits", factory.Split('\n')[0].Trim());
        Assert.Equal(1, Count(accept, "CreateSplitAnchorResolutionContext("));
        Assert.Contains("restored.Geometry", accept);
    }

    [Fact]
    public void AttachedManualRole_IsClassifiedBeforeAnchorResolutionOrAnyWrite()
    {
        var capture = CaptureMethod();
        var role = capture.IndexOf("RoofAttachedManualTimberStore.Read(anchorLine).Data is { } attachedSource", StringComparison.Ordinal);
        var resolve = capture.IndexOf("anchorResolutionContext?.Resolve(attachedAnchorKey)", StringComparison.Ordinal);
        var clear = capture.IndexOf("RoofGeneratedTimberStore.TryClear(extra", StringComparison.Ordinal);

        Assert.True(role >= 0 && resolve > role && clear > resolve);
        Assert.Contains("sourceRole = attachedSource.Origin", capture);
        Assert.DoesNotContain("TryFindGeneratedAnchorLine", capture);
    }

    [Fact]
    public void PhysicalFirstAndVirtualSuppressedGeometryFeedTheSameCapturePath()
    {
        var resolve = Member(Context, "public RoofGeneratedAnchorResolution Resolve", "private static Point3d ToAcad");
        Assert.True(
            resolve.IndexOf("_physicalByKey.TryGetValue", StringComparison.Ordinal) <
            resolve.IndexOf("_logical.Resolve(key)", StringComparison.Ordinal));
        Assert.Contains("RoofGeneratedAnchorResolutionKind.VirtualSuppressed", resolve);

        var capture = CaptureMethod();
        Assert.Contains("anchorStart = anchorResolution.Start", capture);
        Assert.Contains("anchorEnd = anchorResolution.End", capture);
        Assert.Contains("CreateAnchoredData(", capture);
    }

    [Theory]
    [InlineData(0d, 0d, 1000d, 0d, 100d, 0d, 400d, 0d)]
    [InlineData(100d, 200d, 700d, 800d, 220d, 320d, 480d, 580d)]
    public void CanonicalVirtualSegment_IsSufficientForIndependentRelativeCapture(
        double ax0,
        double ay0,
        double ax1,
        double ay1,
        double cx0,
        double cy0,
        double cx1,
        double cy1)
    {
        var anchorStart = new RoofPoint3D(ax0, ay0, 0d);
        var anchorEnd = new RoofPoint3D(ax1, ay1, 0d);
        var childStart = new RoofPoint3D(cx0, cy0, 0d);
        var childEnd = new RoofPoint3D(cx1, cy1, 0d);

        Assert.True(RoofAttachedManualRelativeGeometryRules.TryCapture(
            anchorStart, anchorEnd, childStart, childEnd, out var relative));
        Assert.True(RoofAttachedManualRelativeGeometryRules.TryReplay(
            anchorStart, anchorEnd, relative, out var replayStart, out var replayEnd));
        Assert.Equal(childStart.X, replayStart.X, 6);
        Assert.Equal(childStart.Y, replayStart.Y, 6);
        Assert.Equal(childEnd.X, replayEnd.X, 6);
        Assert.Equal(childEnd.Y, replayEnd.Y, 6);
    }

    [Fact]
    public void LogicalAbsentInconsistentAndUnavailable_FailBeforeDestructiveFallback()
    {
        var capture = CaptureMethod();
        var failure = Segment(capture, "if (!anchorResolution.IsResolved)", "resolvedAnchorKey = attachedAnchorKey;");

        Assert.Contains("anchorResolution.DiagnosticToken", capture);
        Assert.Contains("return false;", failure);
        Assert.Contains("attached-anchor-", failure);
        Assert.DoesNotContain("TryClear", failure);
        Assert.DoesNotContain("WriteAnchored", failure);
        Assert.DoesNotContain("new RoofAttachedManualTimberData", failure);
        Assert.Contains("LogicalAbsent", Context);
        Assert.Contains("Inconsistent", Context);
        Assert.Contains("Unavailable", Context);
    }

    [Fact]
    public void CopyBreak_PreservesCopyOriginal_AndCreatesSplitSiblingWithSameKey()
    {
        var capture = CaptureMethod();
        Assert.Contains("sourceRole == \"AttachedManualCopy\"", capture);
        Assert.Contains("generatedHandle,", capture);
        Assert.Contains("attachedManualHandle,", capture);
        Assert.Contains("RoofAttachedManualOrigin.Copy);", capture);
        Assert.Contains("RoofAttachedManualOrigin.Split);", capture);
        Assert.Contains("resolvedAnchorKey = attachedAnchorKey", capture);
    }

    [Fact]
    public void SplitSource_RepeatedBreakAndTrimRemainSplitWithIndependentIdentity()
    {
        var capture = CaptureMethod();
        Assert.Contains(": \"AttachedManual\"", capture);
        Assert.Contains("attachedManualHandle", capture);
        Assert.Contains("RoofAttachedManualOrigin.Split);", capture);
        Assert.Contains("extra.StartPoint", capture);
        Assert.Contains("extra.EndPoint", capture);
        Assert.Contains("IsSplitCommand(globalCommandName)", Manual);
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSplitCommand("BREAK"));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsSplitCommand("TRIM"));
    }

    [Fact]
    public void OneSidedAndSplitTrim_UseSharedCaptureWithoutNearestHealing()
    {
        var promotion = Member(Manual, "private static bool TryPromoteSplitFragments", "private static bool TryAttachManualSplitFragment");
        Assert.Contains("foreach (var id in modifiedIds)", promotion);
        Assert.Contains("TryAttachManualSplitFragment(", promotion);
        Assert.DoesNotContain("SelectNearestAnchor", promotion + CaptureMethod());
        Assert.DoesNotContain("SelectNearestMirrorAnchor", promotion + CaptureMethod());
        Assert.Contains("IsTrimCommand(globalCommandName) || IsBreakCommand(globalCommandName)", CommandRules);
    }

    [Fact]
    public void ValidAttachedManualNeverEmitsGeneratedSplitForSuppressedPhysicalMiss()
    {
        var capture = CaptureMethod();
        Assert.Contains("WriteSplitResult(", capture);
        Assert.Contains("sourceRole", capture);
        Assert.Contains("resolution", capture);
        Assert.Contains("ROOF_ATTACHED_MANUAL_SPLIT", Diag);
        Assert.Contains("resolution={Token(resolution)}", Diag);
        Assert.DoesNotContain("TryFindGeneratedAnchorLine", capture);
    }

    [Fact]
    public void LegacySchemaOneFallbackRemainsButIsAfterAnchoredFailureReturn()
    {
        var capture = CaptureMethod();
        var failureReturn = capture.IndexOf("return false;", capture.IndexOf("if (!anchorResolution.IsResolved)", StringComparison.Ordinal), StringComparison.Ordinal);
        var legacy = capture.IndexOf("new RoofAttachedManualTimberData(\n                1,", StringComparison.Ordinal);
        Assert.True(failureReturn >= 0 && legacy > failureReturn);
    }

    [Fact]
    public void CaptureContextExistsOnlyInsideGenuineSplitManualEditTransaction()
    {
        var factory = Member(
            Manual,
            "private static RoofGeneratedAnchorResolutionContext? CreateSplitAnchorResolutionContext",
            "private static bool TryClassifyAcceptedMemberEdit");
        Assert.Contains("IsSplitCommand(globalCommandName)", factory);
        Assert.DoesNotContain("Timer", factory + CaptureMethod());
        Assert.DoesNotContain("SendStringToExecute", factory + CaptureMethod());
        Assert.DoesNotContain("Idle", factory + CaptureMethod());
    }

    private static string CaptureMethod() => Member(
        Manual,
        "private static bool TryAttachManualSplitFragment",
        "private static bool TryOpenSnapshotLine");

    private static int Count(string source, string token) =>
        source.Split(token, StringSplitOptions.None).Length - 1;

    private static string ReadAutoCad(string fileName) => RoofUxSourceContractText.Read(
        "src", "AcKrovy.AutoCAD", "Infrastructure", fileName);

    private static string Member(string source, string start, string end) =>
        RoofUxSourceContractText.Member(source, start, end);

    private static string Segment(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start token '{start}' not found.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End token '{end}' not found after '{start}'.");
        return source.Substring(startIndex, endIndex - startIndex);
    }
}
