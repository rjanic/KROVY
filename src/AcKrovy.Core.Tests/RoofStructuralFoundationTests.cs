using System.Text.Json;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofStructuralFoundationTests
{
    [Theory]
    [InlineData(RoofStructuralRole.Hip)]
    [InlineData(RoofStructuralRole.Valley)]
    public void AcceptedMove_ReplaysSameKeyTwiceAndSuppressionSurvivesReferenceRemoval(RoofStructuralRole role)
    {
        var key = new RoofStructuralLogicalKey(role, 1, 4);
        var other = new RoofStructuralLogicalKey(role, 2, 5);
        RoofAutomaticStructuralRafterPlanItem[] canonical =
        [new(key, role == RoofStructuralRole.Hip ? TimberElementType.HipRafter : TimberElementType.ValleyRafter,
            new(new(100, 200, 0), new(900, 1000, 600)), new TimberElementData()),
         new(other, TimberElementType.HipRafter, new(new(1200, 200, 0), new(2000, 1000, 600)), new TimberElementData())];
        var state = RoofStructuralEditState.Empty;
        var before = new RoofSegment3D(new(100, 200, 0), new(900, 1000, 0));
        for (var move = 0; move < 2; move++)
        {
            var after = new RoofSegment3D(new(before.Start.X + 80, before.Start.Y - 30, 0), new(before.End.X + 80, before.End.Y - 30, 0));
            Assert.True(RoofStructuralEditRules.TryAcceptMove(state, key, before, after, out state));
            // Roundtrip is the same serializer used by owner persistence (including keys).
            state = JsonSerializer.Deserialize<RoofStructuralEditState>(JsonSerializer.Serialize(state))!;
            Assert.True(RoofStructuralEditRules.IsValid(state));
            for (var reconcile = 0; reconcile < 2; reconcile++)
            {
                var live = RoofStructuralEditRules.ApplyPlan(canonical, state);
                var member = Assert.Single(live, item => item.LogicalKey == key);
                Assert.Equal(after.Start.X, member.Segment3D.Start.X);
                Assert.Equal(after.End.Y, member.Segment3D.End.Y);
                Assert.Equal(canonical[0].Segment3D.End.Z, member.Segment3D.End.Z);
                Assert.Equal(canonical[0].True3DLengthMm, member.True3DLengthMm, 8);
                Assert.Equal(canonical[1], Assert.Single(live, item => item.LogicalKey == other));
                Assert.Equal(live.Count, live.Select(item => item.LogicalKey).Distinct().Count());
            }
            before = after;
        }
        var movedStateJson = JsonSerializer.Serialize(state);
        for (var erase = 0; erase < 2; erase++)
        {
            state = RoofStructuralEditRules.Upsert(state, RoofStructuralEditRules.Get(state, key) with { Suppressed = true });
            Assert.Single(state.Members);
            var live = RoofStructuralEditRules.ApplyPlan(canonical, state);
            Assert.Single(live);
            Assert.DoesNotContain(live, item => item.LogicalKey == key);
            // A temporarily absent topology key does not delete its suppression/placement.
            Assert.Single(RoofStructuralEditRules.ApplyPlan(new[] { canonical[1] }, state));
        }
        var erasedStateJson = JsonSerializer.Serialize(state);
        var undoState = JsonSerializer.Deserialize<RoofStructuralEditState>(movedStateJson)!;
        var redoState = JsonSerializer.Deserialize<RoofStructuralEditState>(erasedStateJson)!;
        Assert.Equal(2, RoofStructuralEditRules.ApplyPlan(canonical, undoState).Count);
        Assert.Single(RoofStructuralEditRules.ApplyPlan(canonical, redoState));
        // Native DWG undo is a HOST test; this proves semantic snapshots are self-contained.
    }

    [Theory]
    [InlineData(RoofStructuralRole.Hip, RoofEditState.Locked)]
    [InlineData(RoofStructuralRole.Hip, RoofEditState.Unlocked)]
    [InlineData(RoofStructuralRole.Valley, RoofEditState.Locked)]
    [InlineData(RoofStructuralRole.Valley, RoofEditState.Unlocked)]
    public void PlanAndPhysicalAuthority_AreIndependentOfRoofLock(RoofStructuralRole role, RoofEditState state)
    {
        Assert.True(RoofStructuralIdentityRules.TryCreate(role, 1, 4, out _, out _));
        var planAction = state == RoofEditState.Unlocked ? RoofStructuralNativeAction.AcceptPlan : RoofStructuralNativeAction.RestorePlan;
        foreach (var command in new[] { "MOVE", "ERASE" })
            Assert.Equal(planAction, RoofStructuralEditRules.Classify(command, false, state));
        foreach (var command in new[] { "MOVE", "ERASE", "STRETCH", "GRIP_STRETCH" })
            Assert.Equal(RoofStructuralNativeAction.RebuildPhysical, RoofStructuralEditRules.Classify(command, true, state));
    }

    [Theory]
    [InlineData("TRIM")]
    [InlineData("EXTEND")]
    [InlineData("STRETCH")]
    [InlineData("GRIP_STRETCH")]
    [InlineData("COPY")]
    [InlineData("MIRROR")]
    [InlineData("BREAK")]
    [InlineData("BREAKATPOINT")]
    public void FurtherFamilies_ReceiveFirstOpportunityWithoutInventingPlanSemantics(string command)
    {
        Assert.True(RoofStructuralEditRules.HasFirstClaimOpportunity(command));
        Assert.Equal(RoofStructuralNativeAction.Unclaimed, RoofStructuralEditRules.Classify(command, false, RoofEditState.Unlocked));
    }

    [Theory]
    [InlineData("U")]
    [InlineData("UNDO")]
    [InlineData("REDO")]
    [InlineData("MREDO")]
    [InlineData("SCALE")]
    public void UnsupportedAndUndoCommands_DoNotEnterFirstClaim(string command) =>
        Assert.False(RoofStructuralEditRules.HasFirstClaimOpportunity(command));

    [Theory]
    [InlineData(0, 1, 0)] // endpoint length change
    [InlineData(1, 1, 3)] // per-member Z
    [InlineData(double.NaN, 1, 0)]
    [InlineData(double.PositiveInfinity, 1, 0)]
    public void InvalidMoves_DoNotChangePersistentState(double startDx, double endDx, double z)
    {
        var before = new RoofSegment3D(new(0, 0, 0), new(100, 100, 0));
        var after = new RoofSegment3D(new(startDx, 0, z), new(100 + endDx, 100, z));
        var state = RoofStructuralEditState.Empty;
        Assert.False(RoofStructuralEditRules.TryAcceptMove(state, new(RoofStructuralRole.Hip, 1, 4), before, after, out var accepted));
        Assert.Same(state, accepted);
    }

    [Fact]
    public void CorruptState_FailsClosedWithoutResettingAcceptedEdits()
    {
        var edit = new RoofStructuralMemberEdit(new(RoofStructuralRole.Hip, 1, 4), 70, -30, false);
        Assert.False(RoofStructuralEditRules.IsValid(new(2, new[] { edit })));
        Assert.False(RoofStructuralEditRules.IsValid(new(1, new[] { edit, edit })));
        Assert.False(RoofStructuralEditRules.IsValid(new(1, new[] { edit with { OffsetXmm = double.NaN } })));
        Assert.False(RoofStructuralEditRules.IsValid(new(1, new[] { edit with { LogicalKey = new(RoofStructuralRole.Hip, 4, 1) } })));
        Assert.False(RoofStructuralEditRules.IsValid(JsonSerializer.Deserialize<RoofStructuralEditState>("{\"SchemaVersion\":1,\"Members\":[{\"LogicalKey\":null}]}")));
        Assert.Throws<ArgumentException>(() => RoofStructuralEditRules.ApplyPlan(Array.Empty<RoofAutomaticStructuralRafterPlanItem>(), new(2, new[] { edit })).First());
    }

    [Theory]
    [InlineData("Hip|1|4")]
    [InlineData("Valley|2|5")]
    public void Group_RepeatedNativeReattachmentRemovesExactSurplusSlots(string key)
    {
        var expected = new HashSet<string> { "owner", "display", key, "solid:" + key, "annotation" };
        var actual = expected.ToList();
        for (var operation = 0; operation < 2; operation++)
        {
            actual.Add(key); // native unerase/restore reattachment
            actual.Add("solid:" + key);
            actual.Add("erased-old-solid");
            actual.Remove("annotation");
            foreach (var index in RoofAssemblyGroupMembershipRules.SurplusOrForeignMemberIndices(actual, expected).Reverse()) actual.RemoveAt(index);
            var present = actual.ToHashSet();
            foreach (var member in expected) if (present.Add(member)) actual.Add(member);
            Assert.True(RoofAssemblyGroupMembershipRules.IsCanonicalMembership(actual, expected));
            Assert.Equal(0, RoofAssemblyGroupMembershipRules.CountDuplicates(actual));
            Assert.Equal(0, RoofAssemblyGroupMembershipRules.CountMissing(actual, expected));
            Assert.Equal(0, RoofAssemblyGroupMembershipRules.CountForeign(actual, expected));
            Assert.Empty(RoofAssemblyGroupMembershipRules.SurplusOrForeignMemberIndices(actual, expected));
        }
    }
}
