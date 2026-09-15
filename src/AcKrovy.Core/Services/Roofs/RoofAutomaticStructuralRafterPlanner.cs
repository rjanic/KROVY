using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Filters resolved roof topology to the deterministic desired automatic Hip/Valley
/// rafter set. Ridge identities remain available to future subsystems but are not timber.
/// </summary>
public static class RoofAutomaticStructuralRafterPlanner
{
    public static RoofAutomaticStructuralRafterPlanResult Create(
        RoofStructuralEdgeResolutionResult? resolution,
        TimberElementDefaultProfile? defaultProfile = null)
    {
        if (resolution is null || !resolution.IsValid)
        {
            return Invalid(RoofAutomaticStructuralRafterPlanError.InvalidStructuralResolution);
        }

        var items = new List<RoofAutomaticStructuralRafterPlanItem>(resolution.Edges.Count);
        foreach (var edge in resolution.Edges)
        {
            if (edge.StructuralRole == RoofStructuralRole.Ridge)
            {
                continue;
            }

            // Timber follows physical fold semantics: exposed convex Hip folds and
            // concave Valley folds. Horizontal Ridge is excluded upstream as Ridge
            // role. Path anchors remain optional provenance, not eligibility.
            if (!edge.IsAutomaticStructuralTimberEligible)
            {
                continue;
            }

            if (!TryMapType(edge.StructuralRole, out var elementType))
            {
                return Invalid(RoofAutomaticStructuralRafterPlanError.UnsupportedStructuralRole);
            }

            if (double.IsNaN(edge.Length3dMm) ||
                double.IsInfinity(edge.Length3dMm) ||
                edge.Length3dMm <= 0d)
            {
                return Invalid(RoofAutomaticStructuralRafterPlanError.InvalidStructuralLength);
            }

            var data = TimberElementDefaults.For(elementType, defaultProfile) with
            {
                AnnotationMode = TimberAnnotationMode.NoAnnotations,
                LengthCalculationMode = LengthCalculationMode.PlanLength,
                ManualLengthMm = null,
            };
            items.Add(new RoofAutomaticStructuralRafterPlanItem(
                edge.StructuralIdentity,
                elementType,
                edge.Segment3D,
                data));
        }

        if (items.Select(item => item.LogicalKey).Distinct().Count() != items.Count)
        {
            return Invalid(RoofAutomaticStructuralRafterPlanError.DuplicateLogicalKey);
        }

        var ordered = items
            .OrderBy(item => item.LogicalKey.Role)
            .ThenBy(item => item.LogicalKey.BoundaryEdgeIdA)
            .ThenBy(item => item.LogicalKey.BoundaryEdgeIdB)
            .ToArray();
        return new RoofAutomaticStructuralRafterPlanResult(
            true,
            Array.AsReadOnly(ordered),
            RoofAutomaticStructuralRafterPlanError.None);
    }

    public static bool TryMapType(
        RoofStructuralRole role,
        out TimberElementType elementType)
    {
        elementType = role switch
        {
            RoofStructuralRole.Hip => TimberElementType.HipRafter,
            RoofStructuralRole.Valley => TimberElementType.ValleyRafter,
            _ => default,
        };
        return role is RoofStructuralRole.Hip or
            RoofStructuralRole.Valley;
    }

    private static RoofAutomaticStructuralRafterPlanResult Invalid(
        RoofAutomaticStructuralRafterPlanError error) => new(
            false,
            Array.Empty<RoofAutomaticStructuralRafterPlanItem>(),
            error);
}
