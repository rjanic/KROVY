using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Neutral semantic regressions plus routing guards; not native HOST event simulations.</summary>
public sealed class RoofOrdinaryCopyLifecycleTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PlanAndMixedCopies_HaveOneNewIdentityAndPreserveSource(bool independent, bool mixed)
    {
        var source = independent ? Identity() : null;
        var generated = independent ? null : Generated();
        var map = new Dictionary<int, int> { [1] = 101 };
        if (mixed) map.Add(2, 102);
        var claimed = RoofOrdinaryCopyCloneRules.GetMappedPackage(new[] { 1, 2, 3 }, map, new[] { 1, 2, 3, 4 }, false);
        Assert.Equal(mixed ? 2 : 1, claimed.Count);
        Assert.Equal(101, claimed[1]);
        var copy = RoofOrdinaryCopyCloneRules.CreateIdentity(source, generated, Guid.NewGuid().ToString("N"));
        Assert.NotEqual(source?.IndependentMemberId, copy.IndependentMemberId);
        Assert.Equal(independent ? RoofIndependentOrdinaryOriginKind.CopiedFromIndependent :
            RoofIndependentOrdinaryOriginKind.CopiedFromAuto, copy.OriginKind);
        Assert.Equal(source?.SourceRoofReference ?? generated!.RoofOwnerReference, copy.SourceRoofReference);
        Assert.Equal(RoofIndependentOrdinaryEntityRole.PlanLine, copy.EntityRole);
        Assert.True(RoofIndependentOrdinaryTimberDataCodec.TryDecode(RoofIndependentOrdinaryTimberDataCodec.Encode(copy), out var decoded));
        Assert.Equal(copy, decoded);
        Assert.Equal(independent ? Identity() : null, source);
        Assert.Equal(independent ? null : Generated(), generated);
    }

    [Fact]
    public void MultipleMembersAndDestinations_UseExactMapsAndUniqueIds()
    {
        var identities = new List<string>();
        var lines = new HashSet<int>();
        foreach (var destination in new[] { 100, 200, 300 })
        foreach (var source in new[] { 1, 4 })
        {
            var map = new Dictionary<int, int> { [1] = destination + 1, [2] = destination + 2,
                [4] = destination + 4, [5] = destination + 5 };
            var package = RoofOrdinaryCopyCloneRules.GetMappedPackage(new[] { source, source + 1 }, map,
                new[] { 1, 2, 4, 5 }, false);
            lines.Add(package[source]);
            identities.Add(RoofOrdinaryCopyCloneRules.CreateIdentity(Identity(), null, Guid.NewGuid().ToString("N")).IndependentMemberId);
        }
        Assert.Equal(6, lines.Count);
        Assert.Equal(6, identities.Distinct().Count());
    }

    [Fact]
    public void PhysicalOnlyAndAnnotations_AreMappedDerivedEntities_NotLogicalPlans()
    {
        var mapped = RoofOrdinaryCopyCloneRules.GetMappedPackage(new[] { 1, 2, 3 },
            new Dictionary<int, int> { [2] = 102, [3] = 103 }, new[] { 1, 2, 3 }, false);
        Assert.False(mapped.ContainsKey(1));
        Assert.Equal(new[] { 102, 103 }, mapped.Values);
    }

    [Fact]
    public void WholeRoofAndOwnerCopy_StayOutsideOrdinaryCopy()
    {
        var mapped = RoofOrdinaryCopyCloneRules.GetMappedPackage(new[] { 1, 2, 3 },
            new Dictionary<int, int> { [1] = 101, [2] = 102, [3] = 103, [4] = 104 }, new[] { 1, 2, 3, 4 }, true);
        Assert.Empty(mapped);
        var live = Read("LiveGeometrySynchronizationService.cs");
        Assert.True(live.IndexOf("RoofWholeRoofCopyRebindService.Process", StringComparison.Ordinal) <
            live.IndexOf("var ordinaryCopyClaimed", StringComparison.Ordinal));
        Assert.Contains("map.ContainsKey(member.OwnerId)", Read("RoofOrdinaryCopyLifecycleService.cs"));
    }

    [Fact]
    public void PreExistingDestinationsAndForeignSources_AreNeverClaimed()
    {
        var map = new Dictionary<int, int> { [1] = 2, [2] = 102, [9] = 109 };
        var mapped = RoofOrdinaryCopyCloneRules.GetMappedPackage(new[] { 1, 2, 9 }, map, new[] { 1, 2 }, false);
        Assert.Equal(102, Assert.Single(mapped).Value);
        Assert.Equal(3, map.Count);
        Assert.Throws<ArgumentException>(() => RoofOrdinaryCopyCloneRules.GetMappedPackage(new[] { 1, 2 },
            new Dictionary<int, int> { [1] = 100, [2] = 100 }, new[] { 1, 2 }, false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(700)]
    [InlineData(-350)]
    public void CopyKeepsNativeXY_NormalizesZAndRetainsManufacturingSignature(double dz)
    {
        var source = new RoofGeneratedMemberGeometry(new(1000, 2000, 0), new(2200, 4200, 0));
        var native = new RoofGeneratedMemberGeometry(new(6700, 1350, dz), new(7900, 3550, dz));
        Assert.True(RoofOrdinaryCopyPlanRules.TryAccept(source, native, out var accepted));
        Assert.Equal(new RoofGeneratedMemberGeometry(new(6700, 1350, 0), new(7900, 3550, 0)), accepted);
        var data = new TimberElementData { ElementId = "K4", ElementType = TimberElementType.Rafter,
            WidthMm = 80, HeightMm = 160, SlopeDegrees = 35, CuttingAllowanceMm = 100, Material = "Smrek C24" };
        var a = TimberCalculator.Measure(data, source.Start.DistanceTo(source.End), 50);
        var b = TimberCalculator.Measure(data, accepted.Start.DistanceTo(accepted.End), 50);
        Assert.Equal(TimberElementSignature.FromMeasurement(a), TimberElementSignature.FromMeasurement(b));
        var result = TimberElementItemNumbering.AssignElementIdsAfterGeometryEdit(new[] {
            new TimberElementItemNumberingCandidate(a, false), new TimberElementItemNumberingCandidate(b, false) });
        Assert.All(result, item => Assert.Equal("K4", item.ElementId));
        Assert.Equal(0, source.Start.Z);
    }

    [Fact]
    public void InvalidIdentityAndNonRigidClone_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => RoofOrdinaryCopyCloneRules.CreateIdentity(Identity(), null, Identity().IndependentMemberId));
        Assert.Throws<ArgumentException>(() => RoofOrdinaryCopyCloneRules.CreateIdentity(null, null, Guid.NewGuid().ToString("N")));
        Assert.Throws<ArgumentException>(() => RoofOrdinaryCopyCloneRules.CreateIdentity(null, Generated(), "bad-id"));
        Assert.False(RoofIndependentOrdinaryTimberDataCodec.IsValid(Identity() with { OriginKind = (RoofIndependentOrdinaryOriginKind)99 }));
        Assert.False(RoofOrdinaryCopyPlanRules.TryAccept(new(new(0,0,0), new(0,3000,0)),
            new(new(1000,0,0), new(1000,3100,0)), out _));
    }

    [Fact]
    public void NativeCloneClaim_PrecedesLegacyRecoveryAndGroupReconciliation()
    {
        var live = Read("LiveGeometrySynchronizationService.cs");
        var start = live.IndexOf("var ordinaryCopyClaimed", StringComparison.Ordinal);
        var legacy = live.IndexOf("ProcessNativeMemberClones(globalCommandName", start, StringComparison.Ordinal);
        var recovery = live.IndexOf("RoofLiveResizeService.Process", legacy, StringComparison.Ordinal);
        Assert.True(start > 0 && legacy > start && recovery > legacy);
        Assert.Contains("handledNativeMemberIds.UnionWith(ordinaryCopyClaimed)", live);
        foreach (var array in new[] { "appendedTimberIds", "appendedPasteEntityIds", "appendedAnnotationIds", "appendedLabelIds" })
            Assert.Contains($"{array} = {array}.Where(id => !ordinaryCopyClaimed.Contains(id))", live);
        var service = Read("RoofOrdinaryCopyLifecycleService.cs");
        var claim = service.IndexOf("TransferFromRoof(line", StringComparison.Ordinal);
        var erase = service.IndexOf("entity.Erase()", claim, StringComparison.Ordinal);
        var accept = service.IndexOf("RoofOrdinaryGripLifecycleService.Accept", erase, StringComparison.Ordinal);
        var group = service.IndexOf("TrySyncForOwner", accept, StringComparison.Ordinal);
        Assert.True(claim >= 0 && erase > claim && accept > erase && group > accept);
        Assert.Contains("RoofOrdinaryGripLifecycleService.RecalculateDesignations", service);
        var confirm = service.IndexOf("MemberWarningPreferenceService.ConfirmAutomaticDetach", StringComparison.Ordinal);
        Assert.True(confirm > 0);
        var mirrorGate = service.LastIndexOf("if (context.Mirror)", confirm, StringComparison.Ordinal);
        Assert.True(mirrorGate >= 0);
        Assert.DoesNotContain("}", service[mirrorGate..confirm]);
        Assert.DoesNotContain("RoofAttachedManualCopyCloneReinitializeService", service);
        Assert.DoesNotContain("RoofGeneratedRafterCopyOwnershipRehydrationService", service);
        Assert.Contains("RoofPhysical3DWarningService.Show()", service);
        Assert.Contains("VerifyPackages(document, transaction, copied)", service);
    }

    [Fact]
    public void CommandContext_IsFreshAndClearedOnEndCancelFailAndRepeatedCommands()
    {
        var live = Read("LiveGeometrySynchronizationService.cs");
        Assert.Contains("new RoofOrdinaryCopyLifecycleService.Context(_document)", live);
        Assert.Contains("_ordinaryCopyContext?.Observe(_nativeRoofClones)", live);
        Assert.Contains("_ordinaryCopyContext?.Trace(\"end\")", live);
        Assert.Contains("CompleteOrdinaryCopyOnAbort(\"cancel\")", live);
        Assert.Contains("CompleteOrdinaryCopyOnAbort(\"fail\")", live);
        var service = Read("RoofOrdinaryCopyLifecycleService.cs");
        foreach (var collection in new[] { "Sources", "LineClones", "SolidClones", "Claimed", "Processed", "Rejected" })
            Assert.True(service.IndexOf(collection + ".Clear()", StringComparison.Ordinal) < service.IndexOf("Trace(\"disposed\")", StringComparison.Ordinal));
        Assert.Contains("context.ProcessingStarted = true", service);
        Assert.Contains("native.ConsumeMemberClones(nativeClones)", service);
    }

    private static RoofGeneratedTimberData Generated() => new(1, "AB12", RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 4, 10, 900, "source");
    private static RoofIndependentOrdinaryTimberData Identity() => new(1, "edc10e5b07f04e44accd4a842ed2b570",
        RoofIndependentOrdinaryOriginKind.DetachedFromAuto, RoofIndependentOrdinaryEntityRole.PlanLine, "AB12", null);
    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }
}
