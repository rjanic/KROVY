using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Newtonsoft.Json;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofOrdinaryMirrorLifecycleTests
{
    [Theory]
    [InlineData(false, false, false)] [InlineData(false, true, false)]
    [InlineData(true, false, false)] [InlineData(true, true, false)]
    [InlineData(false, false, true)] [InlineData(true, true, true)]
    public void PlanAndMixedMirrors_CreateOneNewIdentity_SourceIsImmutable(bool independent, bool mixed, bool erase)
    {
        var source = independent ? Identity() : null;
        var generated = independent ? null : Generated();
        var before = JsonConvert.SerializeObject(source ?? (object?)generated);
        var map = new Dictionary<int, int> { [1] = 101 };
        if (mixed) map.Add(2, 102);
        var package = RoofOrdinaryCopyCloneRules.GetMappedPackage(new[] { 1, 2, 3 }, map, new[] { 1, 2, 3 }, false);
        Assert.Equal(mixed ? 2 : 1, package.Count);
        var clone = RoofOrdinaryMirrorRules.CreateIdentity(source, generated, Guid.NewGuid().ToString("N"));
        Assert.NotEqual(source?.IndependentMemberId, clone.IndependentMemberId);
        Assert.Equal(independent ? RoofIndependentOrdinaryOriginKind.MirroredFromIndependent :
            RoofIndependentOrdinaryOriginKind.MirroredFromAuto, clone.OriginKind);
        Assert.Equal(RoofIndependentOrdinaryEntityRole.PlanLine, clone.EntityRole);
        Assert.True(RoofIndependentOrdinaryTimberDataCodec.TryDecode(RoofIndependentOrdinaryTimberDataCodec.Encode(clone), out var decoded));
        Assert.Equal(clone, decoded);
        Assert.Equal(before, JsonConvert.SerializeObject(source ?? (object?)generated));
        Assert.Equal(erase, RoofOrdinaryMirrorRules.ReplacesSource(false, erase));
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(30, false)] [InlineData(30, true)]
    [InlineData(90, false)] [InlineData(90, true)]
    [InlineData(-20, false)] [InlineData(-20, true)]
    public void SlopedYawedMirror_PreservesNativeAxisAndCuts_HorizontalWidth_UpwardRightHandedFrame(double angle, bool reverse)
    {
        var state = RoofIndependentOrdinaryHorizontalFrameTests.Fixture(35, 37);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(state, state.AcceptedPlanAxis, out _, out var independent,
            out var reason, useIndependentHorizontalFrame: true), reason);
        state = independent!;
        var before = JsonConvert.SerializeObject(state);
        var r = Reflection(angle);
        var reflected = new RoofSegment3D(r.Point(state.AcceptedPlanAxis.Start), r.Point(state.AcceptedPlanAxis.End));
        if (reverse) reflected = new(reflected.End, reflected.Start);
        var native = new RoofSegment3D(reflected.Start with { Z = 700 }, reflected.End with { Z = 700 });
        Assert.True(RoofOrdinaryMirrorRules.TryResolve(state.AcceptedPlanAxis, native, out var resolved));
        Assert.Equal(reverse, resolved!.EndpointsReversed);
        Assert.True(RoofOrdinaryMirrorRules.TryPreparePhysicalMirror(state, native, out var copy));
        Assert.Equal(reflected, copy!.AcceptedPlanAxis);
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(copy, reflected, out var member, out var final,
            out reason, useIndependentHorizontalFrame: true), reason);
        Assert.Equal(reflected, member!.PlanAxis);
        Assert.Equal(80, member.WidthMm); Assert.Equal(160, member.HeightMm);
        var frame = member.SectionOrientation!.NewFrame;
        Assert.InRange(Math.Abs(frame.WidthAxis.Z), 0, 1e-9);
        Assert.True(frame.HeightAxis.Z > 0);
        Assert.InRange(Dot(Cross(frame.LongitudinalAxis, frame.WidthAxis), frame.HeightAxis), 1 - 1e-9, 1 + 1e-9);
        Assert.Equal(frame, final!.SectionFrame);
        Assert.Equal(0, member.PlanAxis.Start.Z); Assert.Equal(0, member.PlanAxis.End.Z);
        for (var i = 0; i < state.Nodes.Length; i++)
            Assert.InRange(r.Point(state.Nodes[i]).DistanceTo(copy.Nodes[i]), 0, 1e-7);
        var prism = member.HorizontalCut?.SourcePrismVertices ?? member.StructuralCut?.SourcePrismVertices ??
            member.RidgeOverlapCut?.SourcePrismVertices ?? member.RidgeMeetCut?.SourcePrismVertices ?? member.SolidVertices;
        Assert.InRange(prism[0].DistanceTo(prism[1]), 80 - 1e-7, 80 + 1e-7);
        for (var i = 0; i < 4; i++)
            Assert.InRange(Dot(Sub(prism[i], prism[i + 4]), frame.HeightAxis), 160 - 1e-7, 160 + 1e-7);
        if (member.RidgeMeetCut is { } cut)
            Assert.All(cut.CutFaceVertices, p => Assert.InRange(Math.Abs(Dot(Sub(p, cut.PlanePoint), cut.RetainedNormal)), 0, 1e-7));
        Assert.True(RoofOrdinaryPhysicalBuildStateRules.TryBuild(state, state.AcceptedPlanAxis,
            out var original, out _, out reason, useIndependentHorizontalFrame: true), reason);
        var expectedVertices = original!.SolidVertices.Select(r.Point).ToArray();
        Assert.Equal(expectedVertices.Length, member.SolidVertices.Count);
        Assert.All(expectedVertices, p => Assert.Contains(member.SolidVertices, q => p.DistanceTo(q) < 1e-4));
        Assert.Equal(reverse ? state.Anchor.EndBoundaryRole : state.Anchor.StartBoundaryRole,
            final.Anchor.StartBoundaryRole);
        Assert.Equal(reverse ? state.Anchor.StartBoundaryRole : state.Anchor.EndBoundaryRole,
            final.Anchor.EndBoundaryRole);
        Assert.Equal(before, JsonConvert.SerializeObject(state));
    }

    [Fact]
    public void MirrorDoesNotGuessAcrossAmbiguousCenteredAxis_OrAcceptChangedLength()
    {
        var source = new RoofSegment3D(new(-1000, 0, 0), new(1000, 0, 0));
        Assert.False(RoofOrdinaryMirrorRules.TryResolve(source, new(new(0, -1000, 0), new(0, 1000, 0)), out _));
        Assert.False(RoofOrdinaryMirrorRules.TryResolve(source, new(new(5000, 0, 0), new(7200, 0, 0)), out _));
        Assert.False(RoofOrdinaryMirrorRules.TryResolve(source, new(new(double.NaN, 0, 0), new(0, 1000, 0)), out _));
        Assert.Throws<ArgumentException>(() => RoofOrdinaryMirrorRules.CreateIdentity(Identity(), null, Identity().IndependentMemberId));
    }

    [Fact]
    public void PhysicalOnly_MultiMember_WholeOwnerMappingsUseSharedExactCloneScope()
    {
        var onlySolid = RoofOrdinaryCopyCloneRules.GetMappedPackage(new[] { 1, 2 }, new Dictionary<int, int> { [2] = 102 }, new[] { 1, 2 }, false);
        Assert.False(onlySolid.ContainsKey(1));
        var map = new Dictionary<int, int> { [1] = 101, [2] = 102, [3] = 103, [4] = 104 };
        Assert.Empty(RoofOrdinaryCopyCloneRules.GetMappedPackage(new[] { 1, 2 }, map, new[] { 1, 2, 3, 4 }, true));
        var ids = new HashSet<string>();
        foreach (var source in new[] { 1, 3 })
        {
            Assert.Equal(2, RoofOrdinaryCopyCloneRules.GetMappedPackage(new[] { source, source + 1 }, map, new[] { 1, 2, 3, 4 }, false).Count);
            ids.Add(RoofOrdinaryMirrorRules.CreateIdentity(null, Generated(), Guid.NewGuid().ToString("N")).IndependentMemberId);
        }
        Assert.Equal(2, ids.Count);
        Assert.True(RoofOrdinaryMirrorRules.ReplacesSource(true, false));
    }

    [Fact]
    public void AdapterClaimsBeforeLegacyMirrorAndErase_UsesOnePackageEngine_SharedDetachPreference()
    {
        var live = Read("LiveGeometrySynchronizationService.cs");
        var claim = live.IndexOf("var ordinaryMirrorClaimed", StringComparison.Ordinal);
        Assert.True(live.IndexOf("RoofWholeRoofCopyRebindService.Process", StringComparison.Ordinal) < claim);
        Assert.True(live.IndexOf("ProcessNativeMemberClones(globalCommandName", claim, StringComparison.Ordinal) > claim);
        Assert.True(live.IndexOf("RoofLiveResizeService.Process", claim, StringComparison.Ordinal) > claim);
        Assert.Contains("mirrorModifiedTimberIds = mirrorModifiedTimberIds?.Where(id => !ordinaryMirrorClaimed.Contains(id))", live);
        Assert.True(live.IndexOf("erasedSourceHandles = erasedSourceHandles.Where(handle => !ordinaryMirrorHandles.Contains(handle))",
            claim, StringComparison.Ordinal) < live.IndexOf("ProcessNativeMemberClones(globalCommandName", claim, StringComparison.Ordinal));
        var engine = Read("RoofOrdinaryCopyLifecycleService.cs");
        Assert.Contains("RoofOrdinaryMirrorRules.TryPreparePhysicalMirror", engine);
        Assert.Contains("RoofOrdinaryGripLifecycleService.Accept", engine);
        Assert.Contains("RoofOrdinaryGripLifecycleService.RecalculateDesignations", engine);
        Assert.Contains("VerifyReplacements(transaction, replaced, candidates.Values)", engine);
        Assert.DoesNotContain("TryWriteSuppressOverride", engine);
        Assert.Contains("suppressionWritten=False", engine);
        Assert.Contains("source.Entities.Where(e => !retainedLines.Contains(e.Id))", engine);
        Assert.Contains("sources.Except(replaced)", engine);
        Assert.Contains("RoofPhysical3DWarningService.Show()", engine);
        Assert.Contains("MemberWarningPreferenceService.ConfirmAutomaticDetach", engine);
        Assert.DoesNotContain("TryDetachAndPromote", engine);
        Assert.Contains("VerifyPackages(document, transaction, copied)", engine);
    }

    [Fact]
    public void FreshCommandContext_EndCancelFailDispose_HasNoStaleMirrorState()
    {
        var live = Read("LiveGeometrySynchronizationService.cs");
        Assert.Contains("new RoofOrdinaryCopyLifecycleService.Context(_document, mirror: true)", live);
        Assert.Contains("_ordinaryMirrorContext?.Observe(_nativeRoofClones)", live);
        Assert.Contains("_ordinaryMirrorContext?.Trace(\"end\")", live);
        Assert.Contains("CancelOrdinaryMirror(\"cancel\")", live); Assert.Contains("CancelOrdinaryMirror(\"fail\")", live);
        Assert.Contains("_ordinaryMirrorContext = null", live);
        var engine = Read("RoofOrdinaryCopyLifecycleService.cs");
        Assert.Contains("internal static void CancelMirror", engine);
        Assert.Contains("EraseSource = null", engine);
        foreach (var name in new[] { "Sources", "LineClones", "SolidClones", "Claimed", "Processed", "Rejected" })
            Assert.True(engine.IndexOf(name + ".Clear()", StringComparison.Ordinal) < engine.IndexOf("Trace(\"disposed\")", StringComparison.Ordinal));
    }

    private static RoofOrdinaryMirrorRules.Reflection Reflection(double angle)
    {
        var theta = 2 * angle * Math.PI / 180;
        var axis = new RoofPoint3D(65000, 44000, 0);
        return new(Math.Cos(theta), Math.Sin(theta), -Math.Cos(theta), axis, axis, false);
    }
    private static RoofGeneratedTimberData Generated() => new(1, "AB12", RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 4, 10, 900, "source");
    private static RoofIndependentOrdinaryTimberData Identity() => new(1, "edc10e5b07f04e44accd4a842ed2b570",
        RoofIndependentOrdinaryOriginKind.DetachedFromAuto, RoofIndependentOrdinaryEntityRole.PlanLine, "AB12", null);
    private static RoofPoint3D Sub(RoofPoint3D a, RoofPoint3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static RoofPoint3D Cross(RoofPoint3D a, RoofPoint3D b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static double Dot(RoofPoint3D a, RoofPoint3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }
}
