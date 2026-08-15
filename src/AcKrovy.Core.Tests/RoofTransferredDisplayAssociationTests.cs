using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofTransferredDisplayAssociationTests
{
    private const string SelectedOwner = "4B2";
    private const string StaleOwner = "1A3";

    [Fact]
    public void ValidSevenLinesWithStaleOwnerAndNoGroup_AreAssociatedNotMissing()
    {
        var fixture = CreateFixture();
        var observations = Observe(StaleOwner, fixture.Edges, fixture.Signature);

        Assert.True(RoofTransferredDisplayAssociation.TryMatch(
            SelectedOwner,
            fixture.Edges,
            fixture.Signature,
            observations,
            Array.Empty<string>(),
            out var match));
        Assert.Equal(StaleOwner, match.StoredOwnerReference);
        Assert.True(match.Validation.IsCurrent);
        Assert.Equal(
            RoofDisplayLifecycleKind.GroupMissingRehydratable,
            RoofDisplayLifecycleClassifier.Classify(match.Validation, groupIsCurrent: false));
        Assert.NotEqual(RoofDisplayLifecycleKind.MissingDisplay, RoofDisplayLifecycleClassifier.Classify(
            match.Validation,
            groupIsCurrent: false));
    }

    [Fact]
    public void AssociatedSet_KeepsAllSevenRolesExactlyOnce()
    {
        var fixture = CreateFixture();
        var observations = Observe(StaleOwner, fixture.Edges, fixture.Signature);

        Assert.True(RoofTransferredDisplayAssociation.TryMatch(
            SelectedOwner,
            fixture.Edges,
            fixture.Signature,
            observations,
            Array.Empty<string>(),
            out _));
        Assert.Equal(7, observations.Length);
        Assert.Equal(7, observations.Select(observation => observation.Data!.Role).Distinct().Count());
        Assert.All(Enum.GetValues<RoofDisplayEdgeRole>(), role =>
            Assert.Contains(observations, observation => observation.Data!.Role == role));
    }

    [Fact]
    public void LiveForeignOwner_IsNotStolen()
    {
        var fixture = CreateFixture();
        var observations = Observe(StaleOwner, fixture.Edges, fixture.Signature);

        Assert.False(RoofTransferredDisplayAssociation.TryMatch(
            SelectedOwner,
            fixture.Edges,
            fixture.Signature,
            observations,
            [StaleOwner],
            out _));
    }

    [Fact]
    public void StaleGeometry_IsNotGrouped()
    {
        var fixture = CreateFixture();
        var observations = Observe(StaleOwner, fixture.Edges, fixture.Signature);
        var moved = observations[0].Segment;
        observations[0] = observations[0] with
        {
            Segment = new RoofSegment3D(
                moved.Start with { X = moved.Start.X + 1d },
                moved.End with { X = moved.End.X + 1d }),
        };

        Assert.False(RoofTransferredDisplayAssociation.TryMatch(
            SelectedOwner,
            fixture.Edges,
            fixture.Signature,
            observations,
            Array.Empty<string>(),
            out _));
        Assert.Equal(
            RoofDisplayLifecycleKind.StaleDisplay,
            RoofDisplayLifecycleClassifier.Classify(
                RoofDisplayValidator.Validate(
                    SelectedOwner,
                    fixture.Edges,
                    fixture.Signature,
                    Remap(observations, SelectedOwner)),
                groupIsCurrent: false));
    }

    [Fact]
    public void DuplicateOrExtraChild_IsNotGrouped()
    {
        var fixture = CreateFixture();
        var observations = Observe(StaleOwner, fixture.Edges, fixture.Signature).ToList();
        observations.Add(observations[0]);

        Assert.False(RoofTransferredDisplayAssociation.TryMatch(
            SelectedOwner,
            fixture.Edges,
            fixture.Signature,
            observations,
            Array.Empty<string>(),
            out _));
    }

    [Fact]
    public void FutureDisplaySchema_IsNotAdoptedAsCurrent()
    {
        var fixture = CreateFixture();
        var observations = Observe(StaleOwner, fixture.Edges, fixture.Signature);
        observations[0] = observations[0] with
        {
            Data = null,
            MetadataError = RoofDisplayDataDecodeError.UnsupportedFutureSchema,
        };

        Assert.True(RoofTransferredDisplayAssociation.TryMatch(
            SelectedOwner,
            fixture.Edges,
            fixture.Signature,
            observations,
            Array.Empty<string>(),
            out var match));
        Assert.False(match.Validation.IsCurrent);
        Assert.True(match.Validation.Issues.HasFlag(RoofDisplayValidationIssue.UnsupportedFutureSchema));
        Assert.Equal(
            RoofDisplayLifecycleKind.UnsupportedFutureSchema,
            RoofDisplayLifecycleClassifier.Classify(match.Validation, groupIsCurrent: false));
    }

    [Fact]
    public void EmptyCandidates_RemainTrueMissingDisplay()
    {
        var fixture = CreateFixture();

        Assert.False(RoofTransferredDisplayAssociation.TryMatch(
            SelectedOwner,
            fixture.Edges,
            fixture.Signature,
            [],
            Array.Empty<string>(),
            out _));
        Assert.Equal(
            RoofDisplayLifecycleKind.MissingDisplay,
            RoofDisplayLifecycleClassifier.Classify(
                RoofDisplayValidator.Validate(SelectedOwner, fixture.Edges, fixture.Signature, []),
                groupIsCurrent: false));
    }

    [Fact]
    public void GeneratedRaftersAndAnnotations_AreNotDisplayCandidates()
    {
        var fixture = CreateFixture();
        var observations = Observe(StaleOwner, fixture.Edges, fixture.Signature);

        Assert.True(RoofTransferredDisplayAssociation.TryMatch(
            SelectedOwner,
            fixture.Edges,
            fixture.Signature,
            observations,
            Array.Empty<string>(),
            out var match));
        Assert.Equal(7, Observe(StaleOwner, fixture.Edges, fixture.Signature).Length);
        Assert.DoesNotContain("Rafter", match.StoredOwnerReference, StringComparison.OrdinalIgnoreCase);
        Assert.All(observations, observation => Assert.True(observation.IsNativeLine));
    }

    private static IReadOnlyList<RoofDisplayObservation> Remap(
        IReadOnlyList<RoofDisplayObservation> observations,
        string owner) =>
        observations.Select(observation => observation with
        {
            OwnerReference = owner,
            Data = observation.Data is null ? null : observation.Data with { OwnerReference = owner },
        }).ToArray();

    private static RoofDisplayObservation[] Observe(
        string owner,
        IReadOnlyList<RoofDisplayEdge> edges,
        string signature) =>
        edges.Select(edge => new RoofDisplayObservation(
            owner,
            new RoofDisplayData(1, owner, edge.Role, signature),
            RoofDisplayDataDecodeError.None,
            edge.Segment)).ToArray();

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
