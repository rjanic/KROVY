using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// EditState-aware source-change policy. A LOCKED roof must never accept a
/// shape-changing source resize as a supported resize — only a rigid-equivalent
/// (whole-roof translation) source change is allowed while Locked. Therefore any
/// geometrically-valid-but-shape-changing resize (SupportedResize) on a Locked roof
/// is reclassified as Unsupported so the existing unsupported STRETCH recovery path
/// restores the source to its pre-command state and keeps generated timber /
/// annotations consistent. RigidEquivalent, Unsupported and None are unchanged.
/// Unlocked roofs are unaffected (SupportedResize stays a supported resize).
/// </summary>
public static class RoofSourceChangeEditStatePolicy
{
    /// <summary>
    /// Applies the edit-state policy for a specific roof kind. Hip currently has no
    /// generated-timber edit lifecycle, so its persisted Locked state does not block a
    /// valid source-footprint refresh. Other roof kinds retain the established lock rule.
    /// </summary>
    public static RoofSourceChangeKind EffectiveKind(
        RoofKind roofKind,
        RoofEditState editState,
        RoofSourceChangeKind geometricKind) =>
        roofKind == RoofKind.Hip
            ? geometricKind
            : EffectiveKind(editState, geometricKind);

    /// <summary>
    /// Returns the effective source-change kind for the given persisted EditState,
    /// applying the Locked-roof shape-change rejection policy on top of the pure
    /// geometric classification.
    /// </summary>
    public static RoofSourceChangeKind EffectiveKind(
        RoofEditState editState,
        RoofSourceChangeKind geometricKind)
    {
        if (editState == RoofEditState.Locked &&
            geometricKind == RoofSourceChangeKind.SupportedResize)
        {
            return RoofSourceChangeKind.Unsupported;
        }

        return geometricKind;
    }
}
