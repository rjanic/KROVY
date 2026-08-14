using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofDisplayValidatorTests
{
    private const string Owner = "2AF";

    [Fact]
    public void NoChildren_IsMissing()
    {
        var fixture = CreateFixture();

        var result = RoofDisplayValidator.Validate(Owner, fixture.Edges, fixture.Signature, []);

        Assert.Equal(RoofDisplayState.Missing, result.State);
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.MissingChild));
    }

    [Fact]
    public void ExactSevenOwnedLines_AreCurrent()
    {
        var fixture = CreateFixture();

        var result = RoofDisplayValidator.Validate(
            Owner,
            fixture.Edges,
            fixture.Signature,
            fixture.Edges.Select(edge => Observe(edge, fixture.Signature)).ToArray());

        Assert.True(result.IsCurrent);
        Assert.Equal(RoofDisplayValidationIssue.None, result.Issues);
    }

    [Fact]
    public void ReversedNativeLineEndpoints_RemainCurrent()
    {
        var fixture = CreateFixture();
        var observations = fixture.Edges.Select(edge => Observe(edge, fixture.Signature)).ToArray();
        var first = observations[0];
        observations[0] = first with
        {
            Segment = new RoofSegment3D(first.Segment.End, first.Segment.Start),
        };

        Assert.True(RoofDisplayValidator.Validate(
            Owner, fixture.Edges, fixture.Signature, observations).IsCurrent);
    }

    [Fact]
    public void DeletedMovedAndDuplicateChildren_AreStale()
    {
        var fixture = CreateFixture();
        var observations = fixture.Edges.Select(edge => Observe(edge, fixture.Signature)).ToList();
        observations.RemoveAt(6);
        observations[1] = observations[1] with
        {
            Segment = new RoofSegment3D(
                observations[1].Segment.Start,
                observations[1].Segment.End with { X = observations[1].Segment.End.X + 10d }),
        };
        observations.Add(observations[0] with { Segment = observations[0].Segment });

        var result = RoofDisplayValidator.Validate(Owner, fixture.Edges, fixture.Signature, observations);

        Assert.Equal(RoofDisplayState.Stale, result.State);
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.DuplicateRole));
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.MissingRole));
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.GeometryMismatch));
    }

    [Fact]
    public void WrongOwnerMalformedMetadataAndNonLine_AreDetected()
    {
        var fixture = CreateFixture();
        var observations = fixture.Edges.Select(edge => Observe(edge, fixture.Signature)).ToArray();
        observations[0] = observations[0] with { OwnerReference = "OTHER" };
        observations[1] = observations[1] with
        {
            Data = null,
            MetadataError = RoofDisplayDataDecodeError.MalformedPayload,
        };
        observations[2] = observations[2] with { IsNativeLine = false };

        var result = RoofDisplayValidator.Validate(Owner, fixture.Edges, fixture.Signature, observations);

        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.WrongOwner));
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.MalformedMetadata));
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.UnsupportedEntityType));
    }

    [Fact]
    public void OldGenerationSignature_WithExactCurrentGeometry_RemainsCurrentAfterRigidGroupMove()
    {
        var fixture = CreateFixture();
        var observations = fixture.Edges.Select(edge => Observe(edge, "old-generation")).ToArray();

        var result = RoofDisplayValidator.Validate(Owner, fixture.Edges, fixture.Signature, observations);

        Assert.True(result.IsCurrent);
        Assert.Equal(RoofDisplayValidationIssue.None, result.Issues);
    }

    [Fact]
    public void ManualChildMoveBeyondDisplayTolerance_IsStale()
    {
        var fixture = CreateFixture();
        var observations = fixture.Edges.Select(edge => Observe(edge, fixture.Signature)).ToArray();
        var moved = observations[0].Segment;
        observations[0] = observations[0] with
        {
            Segment = new RoofSegment3D(
                moved.Start with { X = moved.Start.X + 1d },
                moved.End with { X = moved.End.X + 1d }),
        };

        var result = RoofDisplayValidator.Validate(Owner, fixture.Edges, fixture.Signature, observations);

        Assert.Equal(RoofDisplayState.Stale, result.State);
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.GeometryMismatch));
    }

    [Fact]
    public void ManualChildEndpointStretchBeyondDisplayTolerance_IsStale()
    {
        var fixture = CreateFixture();
        var observations = fixture.Edges.Select(edge => Observe(edge, fixture.Signature)).ToArray();
        var stretched = observations[3].Segment;
        observations[3] = observations[3] with
        {
            Segment = new RoofSegment3D(
                stretched.Start,
                stretched.End with { Y = stretched.End.Y + 1d }),
        };

        var result = RoofDisplayValidator.Validate(Owner, fixture.Edges, fixture.Signature, observations);

        Assert.Equal(RoofDisplayState.Stale, result.State);
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.GeometryMismatch));
    }

    [Fact]
    public void WrongEdgeRole_IsStale()
    {
        var fixture = CreateFixture();
        var observations = fixture.Edges.Select(edge => Observe(edge, fixture.Signature)).ToArray();
        observations[0] = observations[0] with
        {
            Data = observations[0].Data! with { Role = RoofDisplayEdgeRole.Eave0 },
        };

        var result = RoofDisplayValidator.Validate(Owner, fixture.Edges, fixture.Signature, observations);

        Assert.Equal(RoofDisplayState.Stale, result.State);
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.DuplicateRole));
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.MissingRole));
    }

    [Fact]
    public void MissingEdge_IsStale()
    {
        var fixture = CreateFixture();
        var observations = fixture.Edges
            .Take(fixture.Edges.Count - 1)
            .Select(edge => Observe(edge, fixture.Signature))
            .ToArray();

        var result = RoofDisplayValidator.Validate(Owner, fixture.Edges, fixture.Signature, observations);

        Assert.Equal(RoofDisplayState.Stale, result.State);
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.MissingChild));
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.MissingRole));
    }

    [Fact]
    public void DuplicateEdgeRole_IsStale()
    {
        var fixture = CreateFixture();
        var observations = fixture.Edges.Select(edge => Observe(edge, fixture.Signature)).ToList();
        observations.Add(observations[0]);

        var result = RoofDisplayValidator.Validate(Owner, fixture.Edges, fixture.Signature, observations);

        Assert.Equal(RoofDisplayState.Stale, result.State);
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.ExtraChild));
        Assert.True(result.Issues.HasFlag(RoofDisplayValidationIssue.DuplicateRole));
    }

    private static RoofDisplayObservation Observe(RoofDisplayEdge edge, string signature) =>
        new(
            Owner,
            new RoofDisplayData(1, Owner, edge.Role, signature),
            RoofDisplayDataDecodeError.None,
            edge.Segment);

    private static (IReadOnlyList<RoofDisplayEdge> Edges, string Signature) CreateFixture()
    {
        var validation = RoofFootprintValidator.Validate(new RoofFootprintInput(
            [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)],
            IsClosed: true));
        Assert.True(RoofDirection2D.TryCreate(1, 0, out var direction));
        var solved = SimpleGableRoofGeometrySolver.Solve(new RoofDefinition(
            validation.Footprint!,
            new RoofParameters(30, direction)));
        var edges = SimpleGableRoofWireframe.Create(solved.Geometry!, 0d);
        return (edges, SimpleGableRoofWireframe.BuildGenerationSignature(edges));
    }
}
