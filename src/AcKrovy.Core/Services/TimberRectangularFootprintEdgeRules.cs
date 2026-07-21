using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberRectangularFootprintEdgeRules
{
    public const int MinimumEdgeIndex = 0;
    public const int MaximumEdgeIndex = TimberRectangularFootprintGeometry.RequiredVertexCount - 1;

    public static bool IsValidEdgeIndex(int edgeIndex) =>
        edgeIndex >= MinimumEdgeIndex && edgeIndex <= MaximumEdgeIndex;

    public static bool AreAdjacentEdges(int firstEdgeIndex, int secondEdgeIndex)
    {
        if (!IsValidEdgeIndex(firstEdgeIndex) || !IsValidEdgeIndex(secondEdgeIndex))
        {
            return false;
        }

        var edgeCount = TimberRectangularFootprintGeometry.RequiredVertexCount;
        return (firstEdgeIndex + 1) % edgeCount == secondEdgeIndex ||
            (firstEdgeIndex + edgeCount - 1) % edgeCount == secondEdgeIndex;
    }

    public static bool AreOppositeEdges(int firstEdgeIndex, int secondEdgeIndex) =>
        IsValidEdgeIndex(firstEdgeIndex) &&
        IsValidEdgeIndex(secondEdgeIndex) &&
        (firstEdgeIndex + 2) % TimberRectangularFootprintGeometry.RequiredVertexCount == secondEdgeIndex;

    public static bool TryResolveDimensions(
        TimberRectangularFootprintGeometry geometry,
        int widthEdgeIndex,
        int heightEdgeIndex,
        out TimberRectangularFootprintDimensions? dimensions)
    {
        dimensions = null;
        if (geometry is null ||
            !AreAdjacentEdges(widthEdgeIndex, heightEdgeIndex) ||
            !TimberRectangularFootprintValidator.Validate(geometry.Vertices).IsValid)
        {
            return false;
        }

        dimensions = new TimberRectangularFootprintDimensions(
            widthEdgeIndex,
            heightEdgeIndex,
            geometry.Segments[widthEdgeIndex].LengthMm,
            geometry.Segments[heightEdgeIndex].LengthMm);
        return true;
    }

    public static TimberRectangularFootprintDimensions ResolveDimensions(
        TimberRectangularFootprintGeometry geometry,
        int widthEdgeIndex)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (!IsValidEdgeIndex(widthEdgeIndex))
        {
            throw new ArgumentOutOfRangeException(nameof(widthEdgeIndex));
        }

        var heightEdgeIndex = (widthEdgeIndex + 1) % TimberRectangularFootprintGeometry.RequiredVertexCount;
        if (!TryResolveDimensions(geometry, widthEdgeIndex, heightEdgeIndex, out var dimensions))
        {
            throw new ArgumentException("The supplied geometry is not a valid rectangle.", nameof(geometry));
        }

        return dimensions!;
    }

    /// <summary>
    /// Returns every viable width orientation. A future CAD adapter can compare
    /// these candidates with previous WidthMm/HeightMm values after edge reindexing.
    /// </summary>
    public static IReadOnlyList<TimberRectangularFootprintDimensions> ResolveDimensionCandidates(
        TimberRectangularFootprintGeometry geometry)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (!TimberRectangularFootprintValidator.Validate(geometry.Vertices).IsValid)
        {
            return Array.Empty<TimberRectangularFootprintDimensions>();
        }

        return Enumerable.Range(MinimumEdgeIndex, TimberRectangularFootprintGeometry.RequiredVertexCount)
            .Select(index => ResolveDimensions(geometry, index))
            .ToArray();
    }
}
