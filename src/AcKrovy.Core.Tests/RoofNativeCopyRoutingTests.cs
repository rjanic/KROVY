using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>Adapter routing guards, not a simulation of native AutoCAD callbacks.</summary>
public sealed class RoofNativeCopyRoutingTests
{
    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }

    [Theory]
    [InlineData("COPY")]
    [InlineData("_.copy")]
    public void CopyUsesOwnershipRoute_NotDirectGeometryOverride(string command)
    {
        Assert.True(LiveGeometryCommandRules.IsSameDwgCopyOwnershipCommand(command));
        Assert.True(RoofGeneratedMemberEditCommandRules.IsAssemblySnapshotCommand(command));
        Assert.False(RoofGeneratedMemberEditCommandRules.IsSupportedUnlockedGeneratedTimberCommand(command));
        var source = Read("LiveGeometrySynchronizationService.cs");
        var whole = source.IndexOf("RoofWholeRoofCopyRebindService.Process", StringComparison.Ordinal);
        var copy = source.IndexOf("var handledNativeMemberIds = new HashSet<ObjectId>()", StringComparison.Ordinal);
        var semantic = source.IndexOf("ProcessNativeMemberClones(globalCommandName", copy, StringComparison.Ordinal);
        var recovery = source.IndexOf("RoofLiveResizeService.Process", semantic, StringComparison.Ordinal);
        Assert.True(whole >= 0 && copy > whole && semantic > copy && recovery > semantic);
        var routing = source[copy..recovery];
        Assert.Contains("copy: nativeCopy, handledNativeMemberIds", routing);
        Assert.Contains("ids = ids.Where(id => !handledNativeMemberIds.Contains(id))", routing);
        Assert.Contains("appendedTimberIds = appendedTimberIds.Where(id => !handledNativeMemberIds.Contains(id))", routing);
        Assert.DoesNotContain("ProcessNativeMemberClones(globalCommandName", source[recovery..]);
    }

    [Fact]
    public void RepeatedEventsAndMultipleMappings_AreConsumedByClone_NotSource()
    {
        var source = Read("LiveGeometrySynchronizationService.cs");
        Assert.Contains(".Distinct().ToArray()", source);
        Assert.Contains("!snapshot.IsMemberCloneConsumed(id)", source);
        Assert.Contains("snapshot.ConsumeMemberClones(ownedNativeAdded)", source);
        Assert.Contains("var copyCandidates = ownedNativeAdded.ToArray()", source);
        var snapshot = Read("RoofNativeCloneSnapshot.cs");
        Assert.Contains(".GroupBy(pair => pair.Value)", snapshot);
        Assert.Contains("group.Select(pair => pair.Key).Distinct().Count() == 1", snapshot);
        Assert.Contains("_consumedMemberClones.UnionWith(ids)", snapshot);
        Assert.Contains("_consumedMemberClones.Clear()", snapshot);
    }

    [Fact]
    public void CloneTransaction_RestoresStructuralBeforeNumbering_ThenPromotesBuildsAndVerifies()
    {
        var source = Read("LiveGeometrySynchronizationService.cs");
        var start = source.IndexOf("private void ProcessNativeMemberClones", StringComparison.Ordinal);
        var method = source[start..source.IndexOf("private bool TryOpenNativeMemberOwner", start, StringComparison.Ordinal)];
        var restore = method.IndexOf("TryRestoreStructuralHipValleyMembersOnly", StringComparison.Ordinal);
        var attached = method.IndexOf("RoofAttachedManualCopyCloneReinitializeService.Process", StringComparison.Ordinal);
        var generated = method.IndexOf("RoofGeneratedRafterCopyOwnershipRehydrationService.Process", StringComparison.Ordinal);
        var physical = method.IndexOf("TryReconcileSemanticMembersInTransaction", StringComparison.Ordinal);
        var group = method.IndexOf("RoofAssemblyGroupSyncService.TrySyncForOwner", StringComparison.Ordinal);
        var verify = method.IndexOf("TryVerifyStretchPhysicalState", StringComparison.Ordinal);
        var commit = method.IndexOf("transaction.Commit()", StringComparison.Ordinal);
        Assert.True(restore >= 0 && attached > restore && generated > attached && physical > generated &&
            group > physical && verify > group && commit > verify);
        Assert.Contains("Native member structural snapshot restoration failed", method);
        Assert.Contains("unchangedCloneSources.Add(pair.Value)", method);
        Assert.Contains("RoofGeneratedMemberOverrideMath.GeometryEquals(source.Binding.Geometry", method);
        Assert.Contains(".Where(id => !snapshot.IsPreExisting(id)).ToArray()", method);
        Assert.Contains("DetachMembersBeforeErase", method);
        Assert.Contains("RecoverNativeMemberFailure", method);
    }

    [Fact]
    public void AutomaticTrace_IsCompact_ExplicitAuditRetainsInventory()
    {
        var source = Read("RoofPhysical3DHostDiagnostics.cs");
        Assert.DoesNotContain("ObserveFullAudit", source);
        Assert.DoesNotContain("Audit(Document, \"Command", source);
        Assert.DoesNotContain("Audit(document, \"Command", source);
        Assert.Contains("NATIVE_EVENT_SUMMARY", source);
        Assert.Contains("GROUP_SUMMARY", source);
        Assert.Contains("uniqueKeys=", source);
        Assert.Contains("if (deep) Write(document, $\"GROUP_MEMBERS", source);
        Assert.Contains("if (deep) Write(document, $\"PHYSICAL_INVENTORY", source);
        Assert.Contains("OwnerCounts(document, transaction, owner, phase, deep: true)", source);
        Assert.Contains("public static void Audit(Document document", source);
        Assert.Contains("PLAN_MEMBER", source);
        Assert.Contains("DUPLICATE_PHYSICAL_KEY", source);
        Assert.Contains("if (!mapped && before == after) continue", source);
    }
}
