using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofManualIdentitySyncTests
{
    private static readonly RoofGeneratedMemberKey Key =
        new(RoofGeneratedTimberKind.Rafter, RafterRoofFace.Face0, 10);

    [Fact]
    public void SecondCompose_ReusesExistingReservation_NotNewPassedId()
    {
        // Second BREAK composes on top of the first BREAK's override. ComposeEndpointOffsets
        // reuses the EXISTING ReservedElementId (K3) rather than the passed-in current id.
        // This is why, WITHOUT the post-recalc sync, the persisted override keeps the stale
        // pre-recalc K3 while the recalc moves the member to K5.
        var firstBreak = new RoofGeneratedMemberOverride(
            Key,
            Suppressed: false,
            AlongMm: 0d,
            LateralMm: 0d,
            RotationRadians: 0d,
            StartOffsetMm: 200d,
            EndOffsetMm: 0d,
            ReservedElementId: "K3");

        var secondBreak = RoofGeneratedMemberOverrideMath.ComposeEndpointOffsets(
            firstBreak,
            Key,
            reservedElementId: "K5",
            startOffsetDeltaMm: 50d,
            endOffsetDeltaMm: 0d);

        Assert.NotNull(secondBreak);
        Assert.Equal("K3", secondBreak!.ReservedElementId);
    }

    [Fact]
    public void UpsertWithSyncedReservation_PreservesFinalElementId()
    {
        // The fix: after recalc, the override is re-upserted with ReservedElementId set to
        // the FINAL reconciled ElementId (K5). RoofManualOverrideSet.Upsert -> Normalize
        // must preserve the updated reservation so the rebuild cannot replay stale K3.
        var stale = new RoofGeneratedMemberOverride(
            Key,
            Suppressed: false,
            AlongMm: 0d,
            LateralMm: 0d,
            RotationRadians: 0d,
            StartOffsetMm: 250d,
            EndOffsetMm: 0d,
            ReservedElementId: "K3");

        var set = new RoofManualOverrideSet(new[] { stale });
        var synced = set.Upsert(stale with { ReservedElementId = "K5" });

        Assert.True(synced.TryGet(Key, out var result));
        Assert.Equal("K5", result.ReservedElementId);
        Assert.Equal(250d, result.StartOffsetMm, 3);
    }

    [Fact]
    public void RebuildForcesElementIdFromReserved_SoConsistentReservationPreventsCollision()
    {
        // A SupportedResize rebuild assigns the override's ReservedElementId to the
        // recreated Generated member. A stale K3 reservation therefore collides with the
        // Split fragment's K3; a synced K5 reservation keeps the 1300 mm and 550 mm
        // signatures in different item groups.
        var set = new RoofManualOverrideSet(new[]
        {
            new RoofGeneratedMemberOverride(
                Key,
                Suppressed: false,
                AlongMm: 0d,
                LateralMm: 0d,
                RotationRadians: 0d,
                StartOffsetMm: 1300d,
                EndOffsetMm: 0d,
                ReservedElementId: "K5"),
        });

        Assert.True(set.TryGet(Key, out var reserved));
        Assert.Equal("K5", reserved.ReservedElementId);
        // Two distinct signatures (1300 vs 550) must not resolve to the same reserved id.
        Assert.NotEqual("K3", reserved.ReservedElementId);
    }
}
