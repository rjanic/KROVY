using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Decides which accepted generated members need the AK_RECALC per-element
/// pipeline after a command-end geometry write, and how signature groups
/// participate in targeted numbering.
/// </summary>
public static class RoofGeneratedMemberRecalcScopeRules
{
    public static bool RequiresRecalculation(
        RoofGeneratedMemberGeometry before,
        RoofGeneratedMemberGeometry after) =>
        !RoofGeneratedMemberOverrideMath.GeometryEquals(before, after);

    public static bool RequiresNumberingSynchronization(
        TimberElementSignature before,
        TimberElementSignature after)
    {
        if (before is null)
        {
            throw new ArgumentNullException(nameof(before));
        }

        if (after is null)
        {
            throw new ArgumentNullException(nameof(after));
        }

        return before != after;
    }

    public static TimberElementSignature SignatureFrom(
        TimberElementData data,
        double planLengthMm,
        double roundingStepMm = TimberCalculator.CuttingLengthRoundingIncrementMm) =>
        TimberElementSignature.FromMeasurement(
            TimberCalculator.Measure(data, planLengthMm, roundingStepMm));

    public static int CountAffectedSignatureGroups(
        IEnumerable<RoofGeneratedMemberSignatureTransition> transitions)
    {
        if (transitions is null)
        {
            throw new ArgumentNullException(nameof(transitions));
        }

        var affected = new HashSet<TimberElementSignature>();
        foreach (var transition in transitions)
        {
            if (transition is null ||
                !RequiresNumberingSynchronization(transition.OldSignature, transition.NewSignature))
            {
                continue;
            }

            affected.Add(transition.OldSignature);
            affected.Add(transition.NewSignature);
        }

        return affected.Count;
    }

    public static string FormatSignature(TimberElementSignature signature)
    {
        if (signature is null)
        {
            throw new ArgumentNullException(nameof(signature));
        }

        var custom = string.IsNullOrWhiteSpace(signature.CustomElementTypeId)
            ? string.Empty
            : ":" + signature.CustomElementTypeId.Trim();
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}{1}|{2}|{3:0}x{4:0}|{5:0}",
            signature.ElementType,
            custom,
            FormatMaterial(signature.Material),
            signature.WidthMm,
            signature.HeightMm,
            signature.CuttingLengthMm);
    }

    private static string FormatMaterial(string material)
    {
        if (string.IsNullOrWhiteSpace(material))
        {
            return "-";
        }

        return material.Trim()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(' ', '_');
    }
}
