using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>The shared roof-kind dispatch boundary for preview and permanent display edges.</summary>
public static class RoofWireframe
{
    private static readonly RoofWireframeRoleTopology GableTopology = new(
        [
            RoofDisplayEdgeRole.Ridge,
            RoofDisplayEdgeRole.Eave0,
            RoofDisplayEdgeRole.Eave1,
            RoofDisplayEdgeRole.GableSlope00,
            RoofDisplayEdgeRole.GableSlope01,
            RoofDisplayEdgeRole.GableSlope10,
            RoofDisplayEdgeRole.GableSlope11,
        ],
        RoofDisplayEdgeRole.Eave0,
        RoofDisplayEdgeRole.Eave1);

    private static readonly RoofWireframeRoleTopology MonopitchTopology = new(
        [
            RoofDisplayEdgeRole.MonopitchLowEave,
            RoofDisplayEdgeRole.MonopitchHighEave,
            RoofDisplayEdgeRole.MonopitchSlopeSide0,
            RoofDisplayEdgeRole.MonopitchSlopeSide1,
            RoofDisplayEdgeRole.MonopitchDirection,
            RoofDisplayEdgeRole.MonopitchDirectionWing0,
            RoofDisplayEdgeRole.MonopitchDirectionWing1,
        ],
        RoofDisplayEdgeRole.MonopitchLowEave,
        RoofDisplayEdgeRole.MonopitchHighEave);

    public static IReadOnlyList<RoofDisplayEdge> Create(
        IRoofGeometry geometry,
        double sourceElevation) =>
        Create(geometry, sourceElevation, RoofDisplayProjectionKind.SpatialLocalZ);

    public static IReadOnlyList<RoofDisplayEdge> Create(
        IRoofGeometry geometry,
        double sourceElevation,
        RoofDisplayProjectionKind projection) => geometry switch
    {
        SimpleGableRoofGeometry gable =>
            SimpleGableRoofWireframe.Create(gable, sourceElevation),
        MonopitchRoofGeometry monopitch =>
            MonopitchRoofWireframe.Create(monopitch, sourceElevation),
        HipRoofGeometry hip =>
            HipRoofWireframe.Create(hip, sourceElevation, projection),
        _ => throw new ArgumentException("Unsupported roof geometry.", nameof(geometry)),
    };

    /// <summary>
    /// Owned Hip display edges: flattened drawing-plane projection when physical 3D is
    /// enabled; otherwise legacy spatial local-Z wireframe.
    /// </summary>
    public static IReadOnlyList<RoofDisplayEdge> CreateOwnedHipOrLegacy(
        IRoofGeometry geometry,
        double sourceElevation,
        bool physical3DEnabled)
    {
        var projection = geometry is HipRoofGeometry && physical3DEnabled
            ? RoofDisplayProjectionKind.FlattenedDrawingPlane
            : RoofDisplayProjectionKind.SpatialLocalZ;
        var edges = Create(geometry, sourceElevation, projection);
        // Physical3D plan: source polyline is the sole outer eave/perimeter.
        return physical3DEnabled && geometry is HipRoofGeometry
            ? RoofPhysical3DPlanDisplayRules.FilterOwnedPhysical3DPlanEdges(edges)
            : edges;
    }

    public static bool TryGetTopology(RoofKind kind, out RoofWireframeRoleTopology topology)
    {
        topology = kind switch
        {
            RoofKind.SimpleGable or RoofKind.AsymmetricGable => GableTopology,
            RoofKind.Monopitch => MonopitchTopology,
            _ => null!,
        };
        return topology is not null;
    }

    public static bool IsCompleteRoleSet(IEnumerable<RoofDisplayEdgeRole>? roles)
    {
        if (roles is null)
        {
            return false;
        }

        var set = new HashSet<RoofDisplayEdgeRole>(roles);
        if (set.Count == GableTopology.Roles.Count &&
            (set.SetEquals(GableTopology.Roles) || set.SetEquals(MonopitchTopology.Roles)))
        {
            return true;
        }

        return set.Count > 0 && set.All(HipRoofWireframe.IsHipTopologyRole);
    }

    public static string BuildGenerationSignature(IReadOnlyList<RoofDisplayEdge> edges)
    {
        if (edges is null)
        {
            throw new ArgumentNullException(nameof(edges));
        }
        if (edges.Count == 0 || edges.Select(edge => edge.Role).Distinct().Count() != edges.Count ||
            edges.Any(edge => !Enum.IsDefined(typeof(RoofDisplayEdgeRole), edge.Role)) ||
            edges.Any(edge => !IsFinite(edge.Segment.Start) || !IsFinite(edge.Segment.End)))
        {
            throw new ArgumentException("A roof wireframe must contain unique finite roles.", nameof(edges));
        }

        return string.Join(";", edges
            .OrderBy(edge => edge.Role)
            .SelectMany(edge => new[]
            {
                ((int)edge.Role).ToString(CultureInfo.InvariantCulture),
                Format(edge.Segment.Start.X), Format(edge.Segment.Start.Y), Format(edge.Segment.Start.Z),
                Format(edge.Segment.End.X), Format(edge.Segment.End.Y), Format(edge.Segment.End.Z),
            }));
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static bool IsFinite(RoofPoint3D point) =>
        IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);
    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}

public sealed record RoofWireframeRoleTopology(
    IReadOnlyList<RoofDisplayEdgeRole> Roles,
    RoofDisplayEdgeRole FirstOppositeEaveRole,
    RoofDisplayEdgeRole SecondOppositeEaveRole);
