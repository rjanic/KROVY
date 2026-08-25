using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Intentional definition changes for a monopitch roof.</summary>
public static class MonopitchRoofDefinitionRules
{
    public static RoofDefinition Mirror(RoofDefinition definition)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }
        if (definition.Kind != RoofKind.Monopitch ||
            definition.Parameters.SlopeDirection is not { } direction ||
            !RoofDirection2D.TryCreate(-direction.X, -direction.Y, out var reversed))
        {
            throw new ArgumentException("A valid monopitch definition is required.", nameof(definition));
        }

        return new RoofDefinition(
            definition.Footprint,
            definition.Parameters with { SlopeDirection = reversed },
            RoofKind.Monopitch);
    }

    /// <summary>
    /// Rebases plane-local generated-member overrides when the semantic Monopitch
    /// HIGH/LOW operation reverses the canonical LOW-to-HIGH member direction.
    /// The physical unordered XY member geometry stays unchanged; this is not an
    /// XY reflection of the footprint or of the generated members.
    /// </summary>
    public static RoofDefinitionData PreserveGeneratedMemberOverridesAcrossSemanticMirror(
        RoofDefinitionData updated,
        IRoofGeometry before,
        IRoofGeometry after)
    {
        if (updated is null)
        {
            throw new ArgumentNullException(nameof(updated));
        }
        if (before is null)
        {
            throw new ArgumentNullException(nameof(before));
        }
        if (after is null)
        {
            throw new ArgumentNullException(nameof(after));
        }

        if (updated.Kind != RoofKind.Monopitch ||
            before is not MonopitchRoofGeometry beforeMonopitch ||
            after is not MonopitchRoofGeometry afterMonopitch ||
            updated.Overrides.Count == 0 ||
            !IsOppositeDirection(
                beforeMonopitch.LowToHighDirection,
                afterMonopitch.LowToHighDirection) ||
            Math.Abs(beforeMonopitch.SpanMm - afterMonopitch.SpanMm) >
                SimpleGableRoofGeometryTolerance.CoordinateToleranceMm)
        {
            return updated;
        }

        var spanMm = beforeMonopitch.SpanMm;
        var rebased = updated.Overrides
            .Select(item => RebaseForReversedCanonicalDirection(item, spanMm))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
        return updated with { ManualOverrides = rebased };
    }

    private static RoofGeneratedMemberOverride? RebaseForReversedCanonicalDirection(
        RoofGeneratedMemberOverride item,
        double canonicalLengthMm)
    {
        if (item.Suppressed || !item.HasGeometryOverride)
        {
            return item;
        }

        // U'=-U and V'=-V. Rotation stays measured about the same +Z normal.
        // Reversing the already-rotated body adds the pivot compensation below;
        // physical Start/End roles exchange without changing their offset signs.
        var rotation = item.RotationRadians;
        var rebased = item with
        {
            AlongMm = canonicalLengthMm * (1d - Math.Cos(rotation)) - item.AlongMm,
            LateralMm = -item.LateralMm - canonicalLengthMm * Math.Sin(rotation),
            StartOffsetMm = item.EndOffsetMm,
            EndOffsetMm = item.StartOffsetMm,
        };
        return RoofGeneratedMemberOverrideMath.Normalize(rebased);
    }

    private static bool IsOppositeDirection(RoofDirection2D before, RoofDirection2D after)
    {
        var dot = before.X * after.X + before.Y * after.Y;
        var cross = before.X * after.Y - before.Y * after.X;
        return Math.Abs(cross) <= SimpleGableRoofGeometryTolerance.AngularTolerance &&
               dot <= -1d + SimpleGableRoofGeometryTolerance.AngularTolerance;
    }
}
