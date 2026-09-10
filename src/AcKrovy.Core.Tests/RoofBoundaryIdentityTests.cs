using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofBoundaryIdentityTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(11)]
    public void SequentialIdentity_UsesOwnerScopedPositiveIds(int count)
    {
        var result = RoofBoundaryIdentityRules.CreateSequential(
            count,
            RoofPolygonOrientation.CounterClockwise);

        Assert.True(result.IsValid);
        Assert.Equal(RoofBoundaryIdentitySchema.CurrentVersion, result.Identity!.SchemaVersion);
        Assert.Equal(count, result.Identity.PhysicalSegmentCount);
        Assert.Equal(Enumerable.Range(1, count), result.Identity.BoundaryEdgeIds);
    }

    [Fact]
    public void DuplicateBoundaryId_IsRejected()
    {
        var result = Validate(4, [1, 2, 2, 4]);

        Assert.False(result.IsValid);
        Assert.Equal(RoofBoundaryIdentityError.DuplicateBoundaryEdgeId, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveBoundaryId_IsRejected(int invalidId)
    {
        var result = Validate(4, [1, 2, 3, invalidId]);

        Assert.False(result.IsValid);
        Assert.Equal(RoofBoundaryIdentityError.NonPositiveBoundaryEdgeId, result.Error);
    }

    [Fact]
    public void StoredCountDifferentFromIdCount_IsRejected()
    {
        var result = Validate(6, [1, 2, 3, 4]);

        Assert.False(result.IsValid);
        Assert.Equal(RoofBoundaryIdentityError.BoundaryEdgeIdCountMismatch, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnsupportedSchema_IsRejected(int schema)
    {
        var result = RoofBoundaryIdentityRules.Validate(
            schema,
            4,
            RoofBoundaryIdentityRules.ClockwiseToken,
            [1, 2, 3, 4]);

        Assert.False(result.IsValid);
        Assert.Equal(RoofBoundaryIdentityError.UnsupportedSchemaVersion, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Clockwise")]
    [InlineData("cw")]
    public void MalformedWindingToken_IsRejected(string? token)
    {
        var result = RoofBoundaryIdentityRules.Validate(1, 4, token, [1, 2, 3, 4]);

        Assert.False(result.IsValid);
        Assert.Equal(RoofBoundaryIdentityError.MalformedWindingToken, result.Error);
    }

    [Fact]
    public void MissingIdSequence_IsRejectedAsIncomplete()
    {
        var result = RoofBoundaryIdentityRules.Validate(1, 4, "CW", null);

        Assert.False(result.IsValid);
        Assert.Equal(RoofBoundaryIdentityError.IncompletePayload, result.Error);
    }

    [Fact]
    public void CurrentPhysicalCountMismatch_FailsClosed()
    {
        var identity = Validate(4, [1, 2, 3, 4]).Identity!;

        var error = RoofBoundaryIdentityRules.ValidateCurrentSource(
            identity,
            5,
            RoofPolygonOrientation.CounterClockwise);

        Assert.Equal(RoofBoundaryIdentityError.CurrentPhysicalSegmentCountMismatch, error);
    }

    [Fact]
    public void CurrentRawWindingMismatch_FailsClosed()
    {
        var identity = Validate(4, [1, 2, 3, 4]).Identity!;

        var error = RoofBoundaryIdentityRules.ValidateCurrentSource(
            identity,
            4,
            RoofPolygonOrientation.Clockwise);

        Assert.Equal(RoofBoundaryIdentityError.CurrentRawWindingMismatch, error);
    }

    [Fact]
    public void CounterClockwiseRectangle_MapsCanonicalEdgesToRawSegments()
    {
        var input = Closed(P(0, 0), P(10, 0), P(10, 5), P(0, 5));
        var result = Resolve(input, [11, 12, 13, 14]);

        Assert.Equal([0, 1, 2, 3], RawIndices(result));
        Assert.Equal([11, 12, 13, 14], BoundaryIds(result));
        AssertComplete(input, result);
    }

    [Fact]
    public void ClockwiseRectangle_ReversesEdgeProvenanceCorrectly()
    {
        var input = Closed(P(0, 0), P(0, 5), P(10, 5), P(10, 0));
        var identity = RoofBoundaryIdentityRules.Validate(1, 4, "CW", [21, 22, 23, 24]).Identity!;
        var result = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);

        Assert.True(result.IsValid);
        Assert.Equal([3, 2, 1, 0], RawIndices(result));
        Assert.Equal([24, 23, 22, 21], BoundaryIds(result));
        AssertComplete(input, result);
    }

    [Fact]
    public void ExplicitClosingDuplicate_IsNotAnExtraPhysicalSegment()
    {
        var input = new RoofFootprintInput(
            [P(0, 0), P(10, 0), P(10, 5), P(0, 5), P(0, 0)],
            IsClosed: false);
        var result = Resolve(input, [1, 2, 3, 4]);

        Assert.True(result.IsValid);
        Assert.Equal(4, result.EdgeProvenance.Count);
        Assert.Equal([0, 1, 2, 3], RawIndices(result));
        AssertComplete(input, result);
    }

    [Fact]
    public void CyclicRawStart_ChangesInputIndexesButNotCanonicalGeometry()
    {
        var first = Closed(P(0, 0), P(10, 0), P(10, 5), P(0, 5));
        var shifted = Closed(P(10, 5), P(0, 5), P(0, 0), P(10, 0));
        var firstResult = Resolve(first, [101, 102, 103, 104]);
        var shiftedResult = Resolve(shifted, [201, 202, 203, 204]);

        Assert.Equal(firstResult.Footprint!.Signature, shiftedResult.Footprint!.Signature);
        Assert.Equal([0, 1, 2, 3], RawIndices(firstResult));
        Assert.Equal([2, 3, 0, 1], RawIndices(shiftedResult));
        Assert.Equal([203, 204, 201, 202], BoundaryIds(shiftedResult));
        AssertComplete(first, firstResult);
        AssertComplete(shifted, shiftedResult);
    }

    [Fact]
    public void ReversedRawRepresentation_HasSameGeometryButInputSpecificProvenance()
    {
        var ccw = Closed(P(0, 0), P(9, 0), P(11, 4), P(7, 9), P(0, 6));
        var cw = Closed(P(0, 0), P(0, 6), P(7, 9), P(11, 4), P(9, 0));
        var ccwResult = Resolve(ccw, [1, 2, 3, 4, 5]);
        var cwIdentity = RoofBoundaryIdentityRules.Validate(1, 5, "CW", [6, 7, 8, 9, 10]).Identity!;
        var cwResult = RoofBoundaryIdentityProvenanceResolver.Resolve(cw, cwIdentity);

        Assert.Equal(ccwResult.Footprint!.Signature, cwResult.Footprint!.Signature);
        Assert.Equal([0, 1, 2, 3, 4], RawIndices(ccwResult));
        Assert.Equal([4, 3, 2, 1, 0], RawIndices(cwResult));
        AssertComplete(ccw, ccwResult);
        AssertComplete(cw, cwResult);
    }

    public static IEnumerable<object[]> FootprintFixtures()
    {
        yield return ["rectangle", new[] { P(0, 0), P(10, 0), P(10, 6), P(0, 6) }];
        yield return ["convex-irregular", new[] { P(1, 1), P(8, 0), P(12, 4), P(8, 9), P(0, 6) }];
        yield return ["L", new[] { P(0, 0), P(10, 0), P(10, 4), P(4, 4), P(4, 10), P(0, 10) }];
        yield return ["U", new[] { P(0, 0), P(12, 0), P(12, 10), P(8, 10), P(8, 4), P(4, 4), P(4, 10), P(0, 10) }];
        yield return ["T", new[] { P(0, 0), P(12, 0), P(12, 4), P(8, 4), P(8, 12), P(4, 12), P(4, 4), P(0, 4) }];
        yield return ["stepped-concave", new[] { P(0, 0), P(14, 0), P(14, 4), P(10, 4), P(10, 8), P(6, 8), P(6, 12), P(0, 12) }];
    }

    [Theory]
    [MemberData(nameof(FootprintFixtures))]
    public void SupportedFootprints_HaveCompleteBijectiveProvenance(
        string name,
        RoofPoint2D[] vertices)
    {
        var input = new RoofFootprintInput(vertices, IsClosed: true);
        var ids = Enumerable.Range(100, vertices.Length).ToArray();
        var result = Resolve(input, ids);

        Assert.True(result.IsValid, name);
        AssertComplete(input, result);
    }

    [Fact]
    public void FutureTopologyConsumer_CanResolveStableBoundaryPair()
    {
        var result = Resolve(
            Closed(P(0, 0), P(10, 0), P(10, 5), P(0, 5)),
            [40, 10, 30, 20]);

        Assert.True(RoofBoundaryIdentityProvenanceResolver.TryResolveBoundaryPair(
            result,
            firstNormalizedBoundaryEdgeIndex: 0,
            secondNormalizedBoundaryEdgeIndex: 3,
            out var pair));
        Assert.Equal(new RoofBoundaryEdgeIdPair(20, 40), pair);
    }

    private static RoofBoundaryIdentityValidationResult Validate(
        int count,
        IReadOnlyList<int> ids) => RoofBoundaryIdentityRules.Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            count,
            RoofBoundaryIdentityRules.CounterClockwiseToken,
            ids);

    private static RoofBoundaryIdentityProvenanceResult Resolve(
        RoofFootprintInput input,
        IReadOnlyList<int> ids)
    {
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid);
        var identity = RoofBoundaryIdentityRules.Validate(
            1,
            normalized.EdgeProvenance.Count,
            RoofBoundaryIdentityRules.FormatWinding(normalized.Validation.SourceOrientation),
            ids).Identity!;
        return RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
    }

    private static void AssertComplete(
        RoofFootprintInput input,
        RoofBoundaryIdentityProvenanceResult result)
    {
        Assert.True(result.IsValid);
        var raw = input.Vertices!.ToList();
        if (RoofFootprintValidator.HasRepeatedClosingVertex(raw))
        {
            raw.RemoveAt(raw.Count - 1);
        }

        Assert.Equal(raw.Count, result.EdgeProvenance.Count);
        Assert.Equal(Enumerable.Range(0, raw.Count), RawIndices(result).OrderBy(index => index));
        Assert.Equal(raw.Count, BoundaryIds(result).Distinct().Count());
        foreach (var edge in result.EdgeProvenance)
        {
            var normalized = result.Footprint!.Edges[edge.NormalizedBoundaryEdgeIndex];
            var rawStart = raw[edge.RawPhysicalSegmentIndex];
            var rawEnd = raw[(edge.RawPhysicalSegmentIndex + 1) % raw.Count];
            var sameDirection = Close(normalized.Start, rawStart) && Close(normalized.End, rawEnd);
            var reverseDirection = Close(normalized.Start, rawEnd) && Close(normalized.End, rawStart);
            Assert.True(sameDirection || reverseDirection);
        }
    }

    private static int[] RawIndices(RoofBoundaryIdentityProvenanceResult result) =>
        result.EdgeProvenance.Select(edge => edge.RawPhysicalSegmentIndex).ToArray();

    private static int[] BoundaryIds(RoofBoundaryIdentityProvenanceResult result) =>
        result.EdgeProvenance.Select(edge => edge.BoundaryEdgeId).ToArray();

    private static bool Close(RoofPoint2D first, RoofPoint2D second) =>
        first.DistanceTo(second) <= RoofFootprintValidator.DuplicateVertexToleranceMm;

    private static RoofFootprintInput Closed(params RoofPoint2D[] vertices) =>
        new(vertices, IsClosed: true);

    private static RoofPoint2D P(double x, double y) => new(x, y);
}
