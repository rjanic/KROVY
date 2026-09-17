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
    /// A same-DWG MIRROR may persist a pure winding reversal on
    /// <see cref="RoofRigidFootprintDescriptor.SourceOrientation"/> (edge lengths
    /// unchanged). Live-resize rewrite of the mirrored owner also produces that
    /// flip; treat it as equivalent for whole-roof pairing only.
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
            !RigidFootprintsEquivalent(left.RigidFootprint, right.RigidFootprint))
        {
            return false;
        }

        return left.Overrides.SequenceEqual(right.Overrides);
    }

    /// <summary>
    /// Exact descriptor equality, or a pure orientation/winding flip with equal
    /// Edge01/Edge12 length families (MIRROR / mirrored live-resize rewrite).
    /// </summary>
    public static bool RigidFootprintsEquivalent(
        RoofRigidFootprintDescriptor? left,
        RoofRigidFootprintDescriptor? right)
    {
        if (Equals(left, right))
        {
            return true;
        }

        return IsOrientationFlippedRigid(left, right);
    }

    private static bool IsOrientationFlippedRigid(
        RoofRigidFootprintDescriptor? left,
        RoofRigidFootprintDescriptor? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        if (left.VertexCount != right.VertexCount ||
            left.SourceOrientation == right.SourceOrientation ||
            left.SourceOrientation == RoofPolygonOrientation.Undefined ||
            right.SourceOrientation == RoofPolygonOrientation.Undefined)
        {
            return false;
        }

        return Math.Abs(left.Edge01LengthMm - right.Edge01LengthMm) <=
                   SimpleGableRoofGeometryTolerance.LengthTolerance(
                       left.Edge01LengthMm,
                       right.Edge01LengthMm) &&
               Math.Abs(left.Edge12LengthMm - right.Edge12LengthMm) <=
                   SimpleGableRoofGeometryTolerance.LengthTolerance(
                       left.Edge12LengthMm,
                       right.Edge12LengthMm);
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
        int appendedAttachedManualCount) =>
        IsCompleteAssemblyClone(
            preCommandGeneratedCount,
            0,
            preCommandAttachedManualCount,
            appendedGeneratedCount,
            0,
            appendedAttachedManualCount);

    /// <summary>
    /// Completeness including independently persisted structural Hip/Valley members.
    /// Every physical child category must match exactly; otherwise the selection is a
    /// partial assembly and must retain the existing per-member COPY semantics.
    /// </summary>
    public static bool IsCompleteAssemblyClone(
        int preCommandGeneratedCount,
        int preCommandStructuralGeneratedCount,
        int preCommandAttachedManualCount,
        int appendedGeneratedCount,
        int appendedStructuralGeneratedCount,
        int appendedAttachedManualCount)
    {
        if (preCommandGeneratedCount < 0 ||
            preCommandStructuralGeneratedCount < 0 ||
            preCommandAttachedManualCount < 0 ||
            appendedGeneratedCount < 0 ||
            appendedStructuralGeneratedCount < 0 ||
            appendedAttachedManualCount < 0)
        {
            return false;
        }

        if (preCommandGeneratedCount +
            preCommandStructuralGeneratedCount +
            preCommandAttachedManualCount == 0)
        {
            return false;
        }

        return preCommandGeneratedCount == appendedGeneratedCount &&
               preCommandStructuralGeneratedCount == appendedStructuralGeneratedCount &&
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
