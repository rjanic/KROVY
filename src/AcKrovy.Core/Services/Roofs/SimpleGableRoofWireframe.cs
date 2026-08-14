using System.Globalization;
using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Enumerates the one ridge, two eaves and four gable-end slopes directly from
/// solver output. It does not solve or infer roof geometry.
/// </summary>
public static class SimpleGableRoofWireframe
{
    public const int EdgeCount = 7;

    public static IReadOnlyList<RoofDisplayEdge> Create(
        SimpleGableRoofGeometry geometry,
        double sourceElevation)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }
        if (!IsFinite(sourceElevation))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceElevation));
        }
        if (geometry.Faces.Count != 2)
        {
            throw new ArgumentException("Simple-gable geometry must contain exactly two faces.", nameof(geometry));
        }

        var faces = geometry.Faces.OrderBy(face => face.Index).ToArray();
        if (faces[0].Index != 0 || faces[1].Index != 1)
        {
            throw new ArgumentException("Simple-gable faces must have canonical indexes 0 and 1.", nameof(geometry));
        }

        RoofPoint3D Wcs(RoofPoint3D point)
        {
            var z = sourceElevation + point.Z;
            if (!IsFinite(point.X) || !IsFinite(point.Y) || !IsFinite(point.Z) || !IsFinite(z))
            {
                throw new ArgumentException("Simple-gable wireframe coordinates must be finite.", nameof(geometry));
            }
            return new RoofPoint3D(point.X, point.Y, z);
        }
        RoofSegment3D Segment(RoofPoint3D start, RoofPoint3D end) =>
            new(Wcs(start), Wcs(end));

        return new[]
        {
            new RoofDisplayEdge(RoofDisplayEdgeRole.Ridge, Segment(geometry.Ridge.Start, geometry.Ridge.End)),
            new RoofDisplayEdge(RoofDisplayEdgeRole.Eave0, Segment(faces[0].Eave.Start, faces[0].Eave.End)),
            new RoofDisplayEdge(RoofDisplayEdgeRole.Eave1, Segment(faces[1].Eave.Start, faces[1].Eave.End)),
            new RoofDisplayEdge(RoofDisplayEdgeRole.GableSlope00, Segment(geometry.Ridge.Start, faces[0].Eave.Start)),
            new RoofDisplayEdge(RoofDisplayEdgeRole.GableSlope01, Segment(geometry.Ridge.End, faces[0].Eave.End)),
            new RoofDisplayEdge(RoofDisplayEdgeRole.GableSlope10, Segment(geometry.Ridge.Start, faces[1].Eave.Start)),
            new RoofDisplayEdge(RoofDisplayEdgeRole.GableSlope11, Segment(geometry.Ridge.End, faces[1].Eave.End)),
        };
    }

    public static string BuildGenerationSignature(IReadOnlyList<RoofDisplayEdge> edges)
    {
        if (edges is null)
        {
            throw new ArgumentNullException(nameof(edges));
        }
        if (edges.Count != EdgeCount)
        {
            throw new ArgumentException("A simple-gable wireframe must contain exactly seven edges.", nameof(edges));
        }
        if (edges.Select(edge => edge.Role).Distinct().Count() != EdgeCount ||
            edges.Any(edge => !Enum.IsDefined(typeof(RoofDisplayEdgeRole), edge.Role)) ||
            edges.Any(edge => !IsFinite(edge.Segment.Start.X) ||
                              !IsFinite(edge.Segment.Start.Y) ||
                              !IsFinite(edge.Segment.Start.Z) ||
                              !IsFinite(edge.Segment.End.X) ||
                              !IsFinite(edge.Segment.End.Y) ||
                              !IsFinite(edge.Segment.End.Z)))
        {
            throw new ArgumentException("A simple-gable wireframe must contain seven unique finite roles.", nameof(edges));
        }

        return string.Join(";", edges
            .OrderBy(edge => edge.Role)
            .SelectMany(edge => new[]
            {
                ((int)edge.Role).ToString(CultureInfo.InvariantCulture),
                Format(edge.Segment.Start.X),
                Format(edge.Segment.Start.Y),
                Format(edge.Segment.Start.Z),
                Format(edge.Segment.End.X),
                Format(edge.Segment.End.Y),
                Format(edge.Segment.End.Z),
            }));
    }

    private static string Format(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
