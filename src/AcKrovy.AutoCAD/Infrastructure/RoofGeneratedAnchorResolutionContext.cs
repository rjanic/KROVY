using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AcKrovy.AutoCAD.Infrastructure;

internal enum RoofGeneratedAnchorResolutionKind
{
    Physical = 0,
    VirtualSuppressed = 1,
    LogicalAbsent = 2,
    Inconsistent = 3,
    Unavailable = 4,
}

internal sealed record RoofGeneratedAnchorResolution(
    RoofGeneratedAnchorResolutionKind Kind,
    Point3d Start,
    Point3d End,
    string AnchorHandle)
{
    public bool IsResolved =>
        Kind is RoofGeneratedAnchorResolutionKind.Physical or
            RoofGeneratedAnchorResolutionKind.VirtualSuppressed;

    public string DiagnosticToken => Kind switch
    {
        RoofGeneratedAnchorResolutionKind.Physical => "physical",
        RoofGeneratedAnchorResolutionKind.VirtualSuppressed => "virtual-suppressed",
        RoofGeneratedAnchorResolutionKind.LogicalAbsent => "logical-absent",
        RoofGeneratedAnchorResolutionKind.Inconsistent => "inconsistent",
        _ => "unavailable",
    };
}

/// <summary>
/// One transaction-local, owner-scoped anchor index. Physical materialized geometry is
/// authoritative; the CAD-neutral logical context is consulted only when the exact
/// physical key is absent.
/// </summary>
internal sealed class RoofGeneratedAnchorResolutionContext
{
    private readonly IReadOnlyDictionary<RoofGeneratedMemberKey, PhysicalAnchor> _physicalByKey;
    private readonly RoofLogicalGeneratedAnchorContext _logical;

    private RoofGeneratedAnchorResolutionContext(
        IReadOnlyDictionary<RoofGeneratedMemberKey, PhysicalAnchor> physicalByKey,
        RoofLogicalGeneratedAnchorContext logical)
    {
        _physicalByKey = physicalByKey;
        _logical = logical;
    }

    public static bool TryCreate(
        Database database,
        Transaction transaction,
        IReadOnlyCollection<ObjectId> physicalGeneratedIds,
        SimpleGableRafterLayout layout,
        double sourceElevationMm,
        IEnumerable<RoofGeneratedMemberOverride>? overrides,
        out RoofGeneratedAnchorResolutionContext? context)
    {
        context = null;
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(physicalGeneratedIds);
        ArgumentNullException.ThrowIfNull(layout);

        var physicalByKey = new Dictionary<RoofGeneratedMemberKey, PhysicalAnchor>();
        foreach (var id in physicalGeneratedIds)
        {
            if (!AutoCadObjectIdAccess.TryGetObject<Line>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var line,
                    database) ||
                line is null ||
                line.IsErased)
            {
                return false;
            }

            var generated = RoofGeneratedTimberStore.Read(line).Data;
            if (generated is null)
            {
                return false;
            }

            var key = RoofGeneratedMemberKey.From(generated);
            if (!physicalByKey.TryAdd(
                    key,
                    new PhysicalAnchor(line.StartPoint, line.EndPoint, line.Handle.ToString())))
            {
                return false;
            }
        }

        try
        {
            context = new RoofGeneratedAnchorResolutionContext(
                physicalByKey,
                RoofLogicalGeneratedAnchorContext.FromSimpleGableLayout(
                    layout,
                    sourceElevationMm,
                    overrides));
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    public RoofGeneratedAnchorResolution Resolve(RoofGeneratedMemberKey key)
    {
        // Absolute invariant: an exact live physical member wins, including its applied
        // MOVE/ROTATE/TRIM geometry override.
        if (_physicalByKey.TryGetValue(key, out var physical))
        {
            return new RoofGeneratedAnchorResolution(
                RoofGeneratedAnchorResolutionKind.Physical,
                physical.Start,
                physical.End,
                physical.Handle);
        }

        var logical = _logical.Resolve(key);
        if (logical.Kind == RoofLogicalGeneratedAnchorResolutionKind.VirtualSuppressed &&
            logical.Geometry is not null)
        {
            var geometry = logical.Geometry.Value;
            return new RoofGeneratedAnchorResolution(
                RoofGeneratedAnchorResolutionKind.VirtualSuppressed,
                ToAcad(geometry.Start),
                ToAcad(geometry.End),
                "-");
        }

        return new RoofGeneratedAnchorResolution(
            logical.Kind == RoofLogicalGeneratedAnchorResolutionKind.LogicalKeyAbsent
                ? RoofGeneratedAnchorResolutionKind.LogicalAbsent
                : RoofGeneratedAnchorResolutionKind.Inconsistent,
            Point3d.Origin,
            Point3d.Origin,
            "-");
    }

    private static Point3d ToAcad(RoofPoint3D point) => new(point.X, point.Y, point.Z);

    private sealed record PhysicalAnchor(Point3d Start, Point3d End, string Handle);
}
