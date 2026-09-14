using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofBoundaryIdentityPreviewResolverTests
{
    [Fact]
    public void MissingIdentity_CreatesSequentialEphemeralRawProvenance()
    {
        var input = ClockwiseRectangle();

        var result = RoofBoundaryIdentityPreviewResolver.Resolve(
            input,
            persistedIdentity: null,
            RoofBoundaryIdentityError.Missing);

        Assert.True(result.IsValid);
        Assert.Equal(RoofBoundaryIdentityPreviewSource.Ephemeral, result.Source);
        Assert.Equal(Enumerable.Range(1, 4), result.Identity!.BoundaryEdgeIds);
        Assert.Equal(RoofPolygonOrientation.Clockwise, result.Identity.RawWinding);
        Assert.All(result.Provenance!.EdgeProvenance, edge =>
            Assert.Equal(edge.RawPhysicalSegmentIndex + 1, edge.BoundaryEdgeId));
    }

    [Fact]
    public void ValidPersistedIdentity_UsesPersistedIdentityUnchanged()
    {
        var input = ClockwiseRectangle();
        var identity = RoofBoundaryIdentityRules.Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            4,
            RoofBoundaryIdentityRules.ClockwiseToken,
            [41, 42, 43, 44]).Identity!;

        var result = RoofBoundaryIdentityPreviewResolver.Resolve(
            input,
            identity,
            RoofBoundaryIdentityError.None);

        Assert.True(result.IsValid);
        Assert.Equal(RoofBoundaryIdentityPreviewSource.Persisted, result.Source);
        Assert.Same(identity, result.Identity);
        Assert.Equal([41, 42, 43, 44], result.Identity!.BoundaryEdgeIds);
    }

    [Fact]
    public void MalformedPersistedIdentity_FailsClosedWithoutEphemeralReplacement()
    {
        var result = RoofBoundaryIdentityPreviewResolver.Resolve(
            ClockwiseRectangle(),
            persistedIdentity: null,
            RoofBoundaryIdentityError.DuplicateBoundaryEdgeId);

        Assert.False(result.IsValid);
        Assert.Equal(RoofBoundaryIdentityPreviewSource.Blocked, result.Source);
        Assert.Equal(RoofBoundaryIdentityError.DuplicateBoundaryEdgeId, result.Error);
        Assert.Null(result.Identity);
        Assert.Null(result.Provenance);
    }

    [Fact]
    public void IncompatiblePersistedIdentity_FailsClosed()
    {
        var incompatible = RoofBoundaryIdentityRules.Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            3,
            RoofBoundaryIdentityRules.ClockwiseToken,
            [1, 2, 3]).Identity!;

        var result = RoofBoundaryIdentityPreviewResolver.Resolve(
            ClockwiseRectangle(),
            incompatible,
            RoofBoundaryIdentityError.None);

        Assert.False(result.IsValid);
        Assert.Equal(RoofBoundaryIdentityPreviewSource.Blocked, result.Source);
        Assert.Equal(
            RoofBoundaryIdentityError.CurrentPhysicalSegmentCountMismatch,
            result.Error);
    }

    private static RoofFootprintInput ClockwiseRectangle() => new(
        [new(0, 0), new(0, 6000), new(10000, 6000), new(10000, 0)],
        IsClosed: true);
}
