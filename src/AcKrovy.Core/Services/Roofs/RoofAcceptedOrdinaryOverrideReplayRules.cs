using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Accepted Unlocked ordinary edits remain authoritative for their exact
/// resolved key. Footprint membership is geometric information, not revocation of
/// an accepted edit. Unresolved keys and suppression retain the base planner policy.</summary>
public static class RoofAcceptedOrdinaryOverrideReplayRules
{
    public static RoofGeneratedMemberReplayPlan Apply(
        RoofGeneratedMemberReplayPlan replay,
        RoofEditState editState,
        IReadOnlyDictionary<RoofGeneratedMemberKey, RoofGeneratedMemberGeometry>? livePlans = null)
    {
        if (!replay.IsValid || editState != RoofEditState.Unlocked) return replay;
        var restored = 0;
        var mismatch = false;
        var items = replay.Items.Select(item =>
        {
            if (item.Disposition == RoofGeneratedMemberReplayDisposition.GeometryReplayed)
                return item with { CarriesAcceptedTranslation = true };
            if (item.Disposition != RoofGeneratedMemberReplayDisposition.DormantInvalidDomain ||
                item.Override is not { Suppressed: false } edit ||
                !RoofGeneratedMemberOverrideMath.TryApply(
                    RoofGeneratedMemberOverrideRules.CanonicalGeometry(item.Rafter, 0d),
                    RoofGeneratedMemberOverrideRules.SourceWorkingPlaneNormal, edit, out var applied))
                return item;
            // Existing-member reconcile must agree with the accepted live Plan.
            // Resize has no live replacement yet and replays against the new layout.
            if (livePlans is not null &&
                (!livePlans.TryGetValue(item.Rafter.LogicalKey, out var live) ||
                 !(live.Start.DistanceTo(applied.Start) <= RoofGeneratedMemberOverrideMath.LengthToleranceMm) ||
                 !(live.End.DistanceTo(applied.End) <= RoofGeneratedMemberOverrideMath.LengthToleranceMm)))
            {
                mismatch = true;
                return item;
            }
            restored++;
            return item with { Geometry = applied, Disposition = RoofGeneratedMemberReplayDisposition.GeometryReplayed,
                CarriesAcceptedTranslation = true };
        }).ToArray();
        if (mismatch)
            return replay with { IsValid = false, FailureReason = "accepted-override-live-plan-mismatch" };
        return replay with
        {
            Items = items,
            GeometryReplayCount = replay.GeometryReplayCount + restored,
            DormantInvalidDomainCount = replay.DormantInvalidDomainCount - restored,
        };
    }
}
