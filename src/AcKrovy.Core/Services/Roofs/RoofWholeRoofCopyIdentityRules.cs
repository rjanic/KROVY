using System;
using System.Collections.Generic;
using System.Linq;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Deterministic identity/completeness rules for whole-roof same-DWG COPY detection.
/// Payload/event-based and translation-independent — never spatial guessing.
/// A copied source Polyline carries a verbatim logical clone of the source
/// RoofDefinition (kind, slopes, ΔH, ridge direction, edit state, suppression and
/// per-member overrides), so decoded payload equality identifies the old owner
/// deterministically even for roofs with suppressed/edited members.
/// </summary>
public static class RoofWholeRoofCopyIdentityRules
{
    public enum RoofWholeRoofCopyPairing
    {
        None = 0,
        Unique = 1,
        Ambiguous = 2,
    }

    /// <summary>
    /// Decoded logical payload equality between two roof definitions.
    /// A same-DWG COPY clones the source XData verbatim, so every persisted field
    /// (including suppression/overrides and edit state) must be equal. Fields that
    /// legitimately differ for a copied Polyline (its CAD handle, absolute WCS
    /// footprint vertices) are not part of the decoded payload, so the comparison is
    /// translation-independent by construction.
    /// </summary>
    public static bool DefinitionsEquivalent(
        RoofDefinitionData? left,
        RoofDefinitionData? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null ||
            right is null ||
            left.SchemaVersion != right.SchemaVersion ||
            left.Kind != right.Kind ||
            left.SlopeDegrees != right.SlopeDegrees ||
            left.Face1SlopeDegrees != right.Face1SlopeDegrees ||
            left.EaveHeightDifferenceMm != right.EaveHeightDifferenceMm ||
            left.RidgeDirectionX != right.RidgeDirectionX ||
            left.RidgeDirectionY != right.RidgeDirectionY ||
            left.FootprintSignature != right.FootprintSignature ||
            left.RidgeEdgeFamily != right.RidgeEdgeFamily ||
            left.EditState != right.EditState ||
            !Equals(left.RigidFootprint, right.RigidFootprint))
        {
            return false;
        }

        return left.Overrides.SequenceEqual(right.Overrides);
    }

    /// <summary>
    /// Completeness = the full CURRENT physical owned assembly selected for COPY
    /// (suppressed members are physically absent in both the pre-command snapshot and
    /// the appended set), NOT the pristine canonical solver layout. The source roof
    /// must own timber; copying the source Polyline alone is not an assembly copy.
    /// </summary>
    public static bool IsCompleteAssemblyClone(
        int preCommandGeneratedCount,
        int preCommandAttachedManualCount,
        int appendedGeneratedCount,
        int appendedAttachedManualCount)
    {
        if (preCommandGeneratedCount < 0 ||
            preCommandAttachedManualCount < 0 ||
            appendedGeneratedCount < 0 ||
            appendedAttachedManualCount < 0)
        {
            return false;
        }

        if (preCommandGeneratedCount + preCommandAttachedManualCount == 0)
        {
            return false;
        }

        return preCommandGeneratedCount == appendedGeneratedCount &&
               preCommandAttachedManualCount == appendedAttachedManualCount;
    }

    public static RoofWholeRoofCopyPairing ClassifyPairing(int matchingOldOwnerCount) =>
        matchingOldOwnerCount switch
        {
            1 => RoofWholeRoofCopyPairing.Unique,
            > 1 => RoofWholeRoofCopyPairing.Ambiguous,
            _ => RoofWholeRoofCopyPairing.None,
        };
}
