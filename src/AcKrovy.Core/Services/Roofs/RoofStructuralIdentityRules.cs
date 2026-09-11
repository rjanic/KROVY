using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

public static class RoofStructuralIdentityRules
{
    public static bool TryCreate(
        RoofStructuralRole role,
        int firstBoundaryEdgeId,
        int secondBoundaryEdgeId,
        out RoofStructuralLogicalKey? identity,
        out RoofStructuralIdentityError error)
    {
        identity = null;
        if (!IsSupportedRole(role))
        {
            error = RoofStructuralIdentityError.UnsupportedRole;
            return false;
        }

        if (firstBoundaryEdgeId <= 0 || secondBoundaryEdgeId <= 0)
        {
            error = RoofStructuralIdentityError.NonPositiveBoundaryEdgeId;
            return false;
        }

        if (firstBoundaryEdgeId == secondBoundaryEdgeId)
        {
            error = RoofStructuralIdentityError.SameBoundaryEdgeId;
            return false;
        }

        identity = new RoofStructuralLogicalKey(
            role,
            Math.Min(firstBoundaryEdgeId, secondBoundaryEdgeId),
            Math.Max(firstBoundaryEdgeId, secondBoundaryEdgeId));
        error = RoofStructuralIdentityError.None;
        return true;
    }

    public static bool TryValidateCanonical(
        RoofStructuralLogicalKey? identity,
        out RoofStructuralIdentityError error)
    {
        if (identity is null || !IsSupportedRole(identity.Role))
        {
            error = RoofStructuralIdentityError.UnsupportedRole;
            return false;
        }

        if (identity.BoundaryEdgeIdA <= 0 || identity.BoundaryEdgeIdB <= 0)
        {
            error = RoofStructuralIdentityError.NonPositiveBoundaryEdgeId;
            return false;
        }

        if (identity.BoundaryEdgeIdA == identity.BoundaryEdgeIdB)
        {
            error = RoofStructuralIdentityError.SameBoundaryEdgeId;
            return false;
        }

        if (identity.BoundaryEdgeIdA > identity.BoundaryEdgeIdB)
        {
            error = RoofStructuralIdentityError.NonCanonicalBoundaryPair;
            return false;
        }

        error = RoofStructuralIdentityError.None;
        return true;
    }

    public static bool TryFindDuplicate(
        IEnumerable<RoofStructuralLogicalKey> identities,
        out RoofStructuralLogicalKey? duplicate)
    {
        if (identities is null)
        {
            throw new ArgumentNullException(nameof(identities));
        }

        var seen = new HashSet<RoofStructuralLogicalKey>();
        foreach (var identity in identities)
        {
            if (!seen.Add(identity))
            {
                duplicate = identity;
                return true;
            }
        }

        duplicate = null;
        return false;
    }

    public static bool IsSupportedRole(RoofStructuralRole role) => role is
        RoofStructuralRole.Hip or RoofStructuralRole.Valley or RoofStructuralRole.Ridge;
}
