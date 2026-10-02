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
    [InlineData("ARRAY")]
    [InlineData("ARRAYRECT")]
    [InlineData("ARRAYPOLAR")]
    [InlineData("ARRAYPATH")]
    public void IndividualCloneCommands_AreExplicitlyRejectedForBothRepresentations(string command)
    {
        Assert.True(RoofStructuralEditRules.HasFirstClaimOpportunity(command));
        Assert.True(RoofStructuralEditRules.IsCloneRejectCommand(command));
        Assert.Equal(RoofStructuralCommandDisposition.ExplicitlyRejected, RoofStructuralEditRules.GetDisposition(command));
        Assert.Equal(RoofStructuralNativeAction.RejectClone, RoofStructuralEditRules.Classify(command, false, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.RejectClone, RoofStructuralEditRules.Classify(command, true, RoofEditState.Locked));
    }

    [Fact]
    public void UnlockedCopy_AcceptsManualStructuralClone()
    {
        Assert.True(RoofStructuralEditRules.IsManualCloneAcceptCommand("COPY"));
        Assert.False(RoofStructuralEditRules.IsCloneRejectCommand("COPY"));
        Assert.False(RoofStructuralEditRules.IsPlanRestoreCommand("COPY"));
        Assert.Equal(RoofStructuralAttachedManualCreationKind.Copy,
            RoofStructuralEditRules.CreationKindForCommand("COPY"));
        Assert.Equal(RoofStructuralCommandDisposition.Supported,
            RoofStructuralEditRules.GetDisposition("COPY"));
        Assert.Equal(RoofStructuralNativeAction.AcceptManualClone,
            RoofStructuralEditRules.Classify("COPY", false, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.RejectClone,
            RoofStructuralEditRules.Classify("COPY", false, RoofEditState.Locked));
        Assert.Equal(RoofStructuralNativeAction.AcceptManualClone,
            RoofStructuralEditRules.Classify("COPY", true, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.AcceptManualClone,
            RoofStructuralEditRules.Classify("MIRROR", false, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.RestorePlan,
            RoofStructuralEditRules.Classify("OFFSET", false, RoofEditState.Unlocked));
    }

    [Theory]
    [InlineData("ROTATE")]
    [InlineData("SCALE")]
    [InlineData("BREAK")]
    [InlineData("BREAKATPOINT")]
    [InlineData("FILLET")]
    [InlineData("CHAMFER")]
    [InlineData("JOIN")]
    [InlineData("OFFSET")]
    [InlineData("EXPLODE")]
    public void UnsupportedGeometryCommands_RestorePlanAndRebuildPhysical(string command)
    {
        Assert.True(RoofStructuralEditRules.HasFirstClaimOpportunity(command));
        Assert.Equal(RoofStructuralCommandDisposition.ExplicitlyRejected, RoofStructuralEditRules.GetDisposition(command));
        Assert.Equal(RoofStructuralNativeAction.RestorePlan, RoofStructuralEditRules.Classify(command, false, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.RebuildPhysical, RoofStructuralEditRules.Classify(command, true, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.RebuildPhysical, RoofStructuralEditRules.Classify(command, true, RoofEditState.Locked));
    }

    [Theory]
    [InlineData("STRETCH")]
    [InlineData("GRIP_STRETCH")]
    [InlineData("TRIM")]
    [InlineData("EXTEND")]
    public void PriorityPackA_UnlockedPlanGeometry_IsAcceptPlan(string command)
    {
        Assert.True(RoofStructuralEditRules.IsPlanGeometryAcceptCommand(command));
        Assert.Equal(RoofStructuralCommandDisposition.Supported, RoofStructuralEditRules.GetDisposition(command));
        Assert.Equal(RoofStructuralNativeAction.AcceptPlan, RoofStructuralEditRules.Classify(command, false, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.Unclaimed, RoofStructuralEditRules.Classify(command, false, RoofEditState.Locked));
        Assert.Equal(RoofStructuralNativeAction.RebuildPhysical, RoofStructuralEditRules.Classify(command, true, RoofEditState.Unlocked));
    }

    [Fact]
    public void Rotate_IsRestorePlan_ArbitraryRotationIsNotValidHipValley()
    {
        Assert.False(RoofStructuralEditRules.IsPlanGeometryAcceptCommand("ROTATE"));
        Assert.True(RoofStructuralEditRules.IsPlanRestoreCommand("ROTATE"));
        Assert.Equal(RoofStructuralCommandDisposition.ExplicitlyRejected,
            RoofStructuralEditRules.GetDisposition("ROTATE"));
        Assert.Equal(RoofStructuralNativeAction.RestorePlan,
            RoofStructuralEditRules.Classify("ROTATE", false, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.Unclaimed,
            RoofStructuralEditRules.Classify("ROTATE", false, RoofEditState.Locked));
    }

    [Theory]
    [InlineData(RoofStructuralRole.Hip, "STRETCH")]
    [InlineData(RoofStructuralRole.Valley, "STRETCH")]
    [InlineData(RoofStructuralRole.Hip, "GRIP_STRETCH")]
    [InlineData(RoofStructuralRole.Valley, "TRIM")]
    [InlineData(RoofStructuralRole.Hip, "EXTEND")]
    [InlineData(RoofStructuralRole.Valley, "ROTATE")]
    [InlineData(RoofStructuralRole.Hip, "BREAK")]
    [InlineData(RoofStructuralRole.Hip, "SCALE")]
    public void LockedPlan2DGeometryReject_IsUnclaimedSoLockedGuardOwnsRestore(
        RoofStructuralRole role, string command)
    {
        _ = role;
        Assert.Equal(RoofStructuralNativeAction.Unclaimed,
            RoofStructuralEditRules.Classify(command, false, RoofEditState.Locked));
        // Locked MOVE/ERASE remain structural RestorePlan (foundation), not this deferral.
        Assert.Equal(RoofStructuralNativeAction.RestorePlan,
            RoofStructuralEditRules.Classify("MOVE", false, RoofEditState.Locked));
        Assert.Equal(RoofStructuralNativeAction.RestorePlan,
            RoofStructuralEditRules.Classify("ERASE", false, RoofEditState.Locked));
    }

    [Theory]
    [InlineData(RoofStructuralRole.Hip)]
    [InlineData(RoofStructuralRole.Valley)]
    public void AcceptedPlanGeometry_PersistsAbsoluteEndpointsAndClearsOffset(RoofStructuralRole role)
    {
        var key = new RoofStructuralLogicalKey(role, 1, 4);
        var canonicalSeg = new RoofSegment3D(new(100, 200, 50), new(900, 1000, 600));
        RoofAutomaticStructuralRafterPlanItem[] canonical =
        [new(key, role == RoofStructuralRole.Hip ? TimberElementType.HipRafter : TimberElementType.ValleyRafter,
            canonicalSeg, new TimberElementData())];
        var state = RoofStructuralEditState.Empty;
        Assert.True(RoofStructuralEditRules.TryAcceptMove(state, key,
            new(new(100, 200, 0), new(900, 1000, 0)),
            new(new(150, 220, 0), new(950, 1020, 0)), out state));
        Assert.Equal(50, RoofStructuralEditRules.Get(state, key).OffsetXmm);
        // On-fold shorten of the *automatic* fold (not the offset line): clears Offset,
        // stores absolute Plan on the fold.
        var planXy = new RoofSegment3D(new(100, 200, 0), new(900, 1000, 0));
        var stretched = new RoofSegment3D(
            new(100 + 0.25 * 800, 200 + 0.25 * 800, 0),
            new(900, 1000, 0));
        Assert.Equal(RoofStructuralPlanEditClass.OnFoldSubsegment,
            RoofStructuralEditRules.ClassifyPlanGeometry(planXy, stretched));
        Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(state, key, stretched, canonicalSeg, out state));
        var edit = RoofStructuralEditRules.Get(state, key);
        Assert.True(edit.HasAbsolutePlan);
        Assert.Equal(0, edit.OffsetXmm);
        Assert.Equal(0, edit.OffsetYmm);
        Assert.Equal(stretched.Start.X, edit.PlanStartXmm);
        Assert.Equal(stretched.End.X, edit.PlanEndXmm);
        state = JsonSerializer.Deserialize<RoofStructuralEditState>(JsonSerializer.Serialize(state))!;
        var live = Assert.Single(RoofStructuralEditRules.ApplyPlan(canonical, state));
        Assert.Equal(stretched.Start.X, live.Segment3D.Start.X);
        Assert.Equal(stretched.End.X, live.Segment3D.End.X);
        Assert.Equal(50, live.Segment3D.Start.Z); // Plan override keeps canonical Z for 3D plan item
        Assert.StartsWith("Plan|", RoofStructuralEditRules.PhysicalSignatureToken(edit), StringComparison.Ordinal);
        Assert.False(RoofStructuralEditRules.TryAcceptPlanGeometry(state, key,
            new(new(0, 0, 0), new(0, 0, 0)), canonicalSeg, out var rejected));
        Assert.Same(state, rejected);
        Assert.False(RoofStructuralEditRules.TryAcceptPlanGeometry(state, key,
            new(new(1, 2, 5), new(3, 4, 5)), canonicalSeg, out rejected));
        Assert.Same(state, rejected);
        // Off-fold / rotated Plan is rejected when canonical is supplied.
        Assert.False(RoofStructuralEditRules.TryAcceptPlanGeometry(state, key,
            new(new(100, 200, 0), new(100, 1000, 0)), canonicalSeg, out rejected));
        Assert.Same(state, rejected);
    }

    [Theory]
    [InlineData(RoofStructuralRole.Hip)]
    [InlineData(RoofStructuralRole.Valley)]
    public void CanonicalPlanRestore_NormalizesToAutomatic(RoofStructuralRole role)
    {
        var key = new RoofStructuralLogicalKey(role, 1, 4);
        var canonicalSeg = new RoofSegment3D(new(100, 200, 0), new(900, 1000, 0));
        var state = RoofStructuralEditState.Empty;
        var trimmed = new RoofSegment3D(new(100, 200, 0), new(500, 600, 0));
        Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(state, key, trimmed, canonicalSeg, out state));
        Assert.True(RoofStructuralEditRules.Get(state, key).HasAbsolutePlan);
        Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(state, key, canonicalSeg, canonicalSeg, out state));
        Assert.False(RoofStructuralEditRules.Get(state, key).HasAbsolutePlan);
        Assert.Equal(RoofStructuralPlanEditClass.Automatic,
            RoofStructuralEditRules.ClassifyMemberEdit(canonicalSeg, RoofStructuralEditRules.Get(state, key)));
    }

    [Theory]
    [InlineData(RoofStructuralRole.Hip)]
    [InlineData(RoofStructuralRole.Valley)]
    public void MoveThenOnFoldTrim_ClearsOffsetAndStoresAbsolutePlan(RoofStructuralRole role)
    {
        var key = new RoofStructuralLogicalKey(role, 1, 4);
        var canonicalSeg = new RoofSegment3D(new(0, 0, 0), new(1000, 0, 0));
        var state = RoofStructuralEditState.Empty;
        Assert.True(RoofStructuralEditRules.TryAcceptMove(state, key,
            canonicalSeg, new(new(100, 50, 0), new(1100, 50, 0)), out state));
        Assert.Equal(100, RoofStructuralEditRules.Get(state, key).OffsetXmm);
        Assert.Equal(50, RoofStructuralEditRules.Get(state, key).OffsetYmm);
        // Trim the moved line (parallel offset of a subsegment).
        var trimmedMoved = new RoofSegment3D(new(100, 50, 0), new(700, 50, 0));
        Assert.Equal(RoofStructuralPlanEditClass.OffsetRigid,
            RoofStructuralEditRules.ClassifyPlanGeometry(canonicalSeg, trimmedMoved));
        Assert.True(RoofStructuralEditRules.TryAcceptPlanGeometry(state, key, trimmedMoved, canonicalSeg, out state));
        var edit = RoofStructuralEditRules.Get(state, key);
        Assert.True(edit.HasAbsolutePlan);
        Assert.Equal(0, edit.OffsetXmm);
        Assert.Equal(0, edit.OffsetYmm);
        Assert.Equal(100, edit.PlanStartXmm);
        Assert.Equal(700, edit.PlanEndXmm);
    }

    [Theory]
    [InlineData(RoofStructuralRole.Hip)]
    [InlineData(RoofStructuralRole.Valley)]
    public void UnlockedStretch_IsPersistentAcceptNotRestore(RoofStructuralRole role)
    {
        _ = role;
        Assert.Equal(RoofStructuralNativeAction.AcceptPlan,
            RoofStructuralEditRules.Classify("STRETCH", false, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.Unclaimed,
            RoofStructuralEditRules.Classify("STRETCH", false, RoofEditState.Locked));
        Assert.Equal(RoofStructuralNativeAction.AcceptPlan,
            RoofStructuralEditRules.Classify("MOVE", false, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.AcceptPlan,
            RoofStructuralEditRules.Classify("ERASE", false, RoofEditState.Unlocked));
        Assert.Equal(RoofStructuralNativeAction.RebuildPhysical,
            RoofStructuralEditRules.Classify("STRETCH", true, RoofEditState.Unlocked));
    }

    [Theory]
    [InlineData("U")]
    [InlineData("UNDO")]
    [InlineData("REDO")]
    [InlineData("MREDO")]
    public void UndoFamily_IsSupportedWithoutFirstClaimWrites(string command)
    {
        Assert.False(RoofStructuralEditRules.HasFirstClaimOpportunity(command));
        Assert.Equal(RoofStructuralCommandDisposition.Supported, RoofStructuralEditRules.GetDisposition(command));
    }

    [Theory]
    [InlineData("SAVE")]
    [InlineData("QSAVE")]
    [InlineData("CLOSE")]
    [InlineData("OPEN")]
    public void PersistenceCommands_AreSupportedByOwnerSemanticState(string command) =>
        Assert.Equal(RoofStructuralCommandDisposition.Supported, RoofStructuralEditRules.GetDisposition(command));

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
    [InlineData(RoofStructuralRole.Hip)]
    [InlineData(RoofStructuralRole.Valley)]
    public void OrdinaryRafterRoles_AreNeverStructuralHipValleyClaims(RoofStructuralRole role)
    {
        Assert.True(RoofStructuralEditRules.IsStructuralHipValleyRole(role));
        Assert.False(RoofStructuralEditRules.IsStructuralHipValleyRole(RoofStructuralRole.Ridge));
        Assert.False(RoofStructuralEditRules.IsStructuralHipValleyRole(null));
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

    [Theory]
    [InlineData(RoofStructuralRole.Hip)]
    [InlineData(RoofStructuralRole.Valley)]
    public void PersistenceRoundtrip_RetainsSuppressionAndPlacementAcrossReopen(RoofStructuralRole role)
    {
        var key = new RoofStructuralLogicalKey(role, 1, 4);
        var state = RoofStructuralEditRules.Upsert(RoofStructuralEditState.Empty,
            new RoofStructuralMemberEdit(key, 125.5, -40.25, true));
        var reopened = JsonSerializer.Deserialize<RoofStructuralEditState>(JsonSerializer.Serialize(state))!;
        Assert.True(RoofStructuralEditRules.IsValid(reopened));
        var edit = RoofStructuralEditRules.Get(reopened, key);
        Assert.True(edit.Suppressed);
        Assert.Equal(125.5, edit.OffsetXmm);
        Assert.Equal(-40.25, edit.OffsetYmm);
        Assert.Empty(RoofStructuralEditRules.ApplyPlan(
        [
            new(key, TimberElementType.HipRafter, new(new(0, 0, 0), new(100, 100, 50)), new TimberElementData())
        ], reopened));
    }
}
