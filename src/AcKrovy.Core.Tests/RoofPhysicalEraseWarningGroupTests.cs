using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofPhysicalEraseWarningGroupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarningPreferenceCannotChangeFinalMembership_EvenWithLateNativeReattachment(bool warningOn)
    {
        const string solid = "2B44";
        var expected = Enumerable.Range(0, 160).Select(i => "member-" + i).Append(solid).ToHashSet();
        var group = expected.ToList(); // native slot has reattached when restore transaction commits
        var appendCount = 0;
        var dialogs = 0;
        var order = new List<string>();
        var completion = new RoofPhysicalEraseCompletion();
        void Sync(string phase)
        {
            order.Add(phase);
            if (RoofAssemblyGroupMembershipRules.IsCanonicalMembership(group, expected)) return;
            foreach (var index in RoofAssemblyGroupMembershipRules.SurplusOrForeignMemberIndices(group, expected).Reverse())
                group.RemoveAt(index);
            var present = group.ToHashSet();
            foreach (var id in expected)
                if (present.Add(id)) { group.Add(id); appendCount++; }
            Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(group, expected));
        }
        Assert.True(completion.TryComplete(true, () => Sync("post-commit"), () =>
        {
            order.Add("warning");
            var rejected = WarningPreferenceRules.RejectDerived3DEdit(new WarningPreferences { WarnDerived3DEdit = warningOn }, () =>
            {
                dialogs++;
                group.Add(solid); // reproduce a delayed native slot during the modal message pump
                return false;
            });
            Assert.False(rejected.Accepted);
        }, () => Sync("post-warning")));
        Assert.Equal(new[] { "post-commit", "warning", "post-warning" }, order);
        Assert.Equal(warningOn ? 1 : 0, dialogs);
        Assert.Equal(0, appendCount); // never race native unerase with an unnecessary plugin Append
        Assert.Equal(161, group.Count);
        Assert.Equal(161, group.Distinct().Count());
        Assert.Equal(0, RoofAssemblyGroupMembershipRules.CountDuplicates(group));
        Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(group, expected));
        Assert.False(completion.TryComplete(true, () => throw new Exception("duplicate repair"),
            () => throw new Exception("duplicate warning"), () => throw new Exception("duplicate final check")));
    }

    [Fact]
    public void CompletionWaitsForRestoreCommit_AndFinalCheckRunsEvenIfWarningThrows()
    {
        var completion = new RoofPhysicalEraseCompletion();
        var calls = new List<string>();
        Assert.False(completion.TryComplete(false, () => calls.Add("sync"), () => calls.Add("warning"), () => calls.Add("verify")));
        Assert.Empty(calls);
        Assert.Throws<InvalidOperationException>(() => completion.TryComplete(true,
            () => calls.Add("sync"), () => throw new InvalidOperationException("UX failure"), () => calls.Add("verify")));
        Assert.Equal(new[] { "sync", "verify" }, calls);
    }

    [Fact]
    public void AdapterKeepsPurePhysicalCompletionInsideEraseScope_AndUsesExistingCanonicalizerOnly()
    {
        var erase = Read("RoofOrdinaryEraseLifecycleService.cs");
        Assert.Contains("physicalOnlyCommand = affected.All(m => !m.LineId.IsErased)", erase);
        Assert.Contains("affected.Where(_ => !physicalOnlyCommand)", erase);
        Assert.Contains("deferGroupVerification: physicalOnlyCommand", erase);
        Assert.True(erase.IndexOf("transaction.Commit()", StringComparison.Ordinal) < erase.IndexOf("context.PendingCompletion =", StringComparison.Ordinal));
        var terminal = Read("RoofOrdinaryPhysicalEraseCompletionService.cs");
        Assert.Contains("TopTransaction is null", terminal);
        Assert.Contains("RoofAssemblyGroupSyncService.TrySyncForOwner", terminal);
        Assert.Contains("restoredSolids.Contains(ids.Key)", terminal);
        Assert.Contains("verifyFinalGroup: () => Synchronize(true)", terminal);
        foreach (var forbidden in new[] { "AcApp.Idle", "group.Append(", "group.Remove", "Erase(false)", "RoofDefinitionStore.Write", "IndependentMemberId", "WarnDerived3DEdit" })
            Assert.DoesNotContain(forbidden, terminal);
        var live = Read("LiveGeometrySynchronizationService.cs");
        var invoke = live.IndexOf("physicalEraseCompletion?.Invoke()", StringComparison.Ordinal);
        Assert.True(invoke > 0 && invoke < live.IndexOf("EndStretchUndoMark()", invoke, StringComparison.Ordinal));
        foreach (var invariant in new[] { "suppressionWritten=False", "independentCreated=False", "attachedManualWritten=False", "legacyGeometryOverrideWritten=False", "annotationsRemoved=" })
            Assert.Contains(invariant, erase);
    }

    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcKrovy.sln"))) root = root.Parent;
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "AcKrovy.AutoCAD", "Infrastructure", file));
    }
}
