using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using Xunit;

namespace AcKrovy.Core.Tests;

/// <summary>
/// CAD-neutral reflection parity for Complex X Hip whole-roof MIRROR.
/// Stale cloned BoundaryIdentity must fail ValidateCurrentSource after winding
/// reverse; re-homing CreateSequential from the mirrored source must restore a
/// valid identity without weakening CurrentRawWindingMismatch.
/// </summary>
public sealed class RoofHipWholeRoofMirrorBoundaryIdentityReflectionTests
{
    [Fact]
    public void ComplexX_MirrorAcrossX_ReversesRawWinding()
    {
        var source = Normalize(ComplexX());
        var mirrored = Normalize(MirrorAcrossX(ComplexX()));

        Assert.NotEqual(RoofPolygonOrientation.Undefined, source.Validation.SourceOrientation);
        Assert.NotEqual(RoofPolygonOrientation.Undefined, mirrored.Validation.SourceOrientation);
        Assert.NotEqual(source.Validation.SourceOrientation, mirrored.Validation.SourceOrientation);
        Assert.Equal(source.EdgeProvenance.Count, mirrored.EdgeProvenance.Count);
    }

    [Fact]
    public void ComplexX_MirrorAcrossX_SemanticTopologyAndStructuralDesiredSetsAreReflectionInvariant()
    {
        var source = CreateDesiredSet(ComplexX());
        var mirrored = CreateDesiredSet(MirrorAcrossX(ComplexX()));

        Assert.Equal(
            CountTopology(source.Geometry, RoofTopologyEdgeKind.Ridge),
            CountTopology(mirrored.Geometry, RoofTopologyEdgeKind.Ridge));
        Assert.Equal(
            CountTopology(source.Geometry, RoofTopologyEdgeKind.Hip),
            CountTopology(mirrored.Geometry, RoofTopologyEdgeKind.Hip));
        Assert.Equal(
            CountTopology(source.Geometry, RoofTopologyEdgeKind.Valley),
            CountTopology(mirrored.Geometry, RoofTopologyEdgeKind.Valley));

        Assert.Equal(
            CountStructural(source.Structural, TimberElementType.HipRafter),
            CountStructural(mirrored.Structural, TimberElementType.HipRafter));
        Assert.Equal(
            CountStructural(source.Structural, TimberElementType.ValleyRafter),
            CountStructural(mirrored.Structural, TimberElementType.ValleyRafter));
        Assert.Equal(source.Ordinary.Rafters.Count, mirrored.Ordinary.Rafters.Count);

        // Fold roles remain Hip/Valley timber-eligible with unique logical keys.
        Assert.Equal(
            mirrored.Structural.Items.Count,
            mirrored.Structural.Items.Select(item => item.LogicalKey).Distinct().Count());
        Assert.All(mirrored.Structural.Items, item =>
        {
            Assert.True(
                item.ElementType is TimberElementType.HipRafter or TimberElementType.ValleyRafter);
            Assert.True(item.LogicalKey.BoundaryEdgeIdA > 0);
            Assert.True(item.LogicalKey.BoundaryEdgeIdB > 0);
        });
    }

    [Fact]
    public void ComplexX_StaleClonedBoundaryIdentity_FailsValidateCurrentSource_AfterWindingFlip()
    {
        var source = Normalize(ComplexX());
        var mirrored = Normalize(MirrorAcrossX(ComplexX()));
        var stale = RoofBoundaryIdentityRules.CreateSequential(
            source.EdgeProvenance.Count,
            source.Validation.SourceOrientation).Identity!;

        Assert.Equal(
            RoofBoundaryIdentityError.None,
            RoofBoundaryIdentityRules.ValidateCurrentSource(
                stale,
                source.EdgeProvenance.Count,
                source.Validation.SourceOrientation));
        Assert.Equal(
            RoofBoundaryIdentityError.CurrentRawWindingMismatch,
            RoofBoundaryIdentityRules.ValidateCurrentSource(
                stale,
                mirrored.EdgeProvenance.Count,
                mirrored.Validation.SourceOrientation));
    }

    [Fact]
    public void ComplexX_RehomedBoundaryIdentity_MatchesMirroredSource_AndPassesValidateCurrentSource()
    {
        var source = Normalize(ComplexX());
        var mirrored = Normalize(MirrorAcrossX(ComplexX()));
        var stale = RoofBoundaryIdentityRules.CreateSequential(
            source.EdgeProvenance.Count,
            source.Validation.SourceOrientation).Identity!;
        var rehomed = RoofBoundaryIdentityRules.CreateSequential(
            mirrored.EdgeProvenance.Count,
            mirrored.Validation.SourceOrientation).Identity!;

        Assert.NotEqual(stale.RawWinding, rehomed.RawWinding);
        Assert.Equal(mirrored.Validation.SourceOrientation, rehomed.RawWinding);
        Assert.Equal(mirrored.EdgeProvenance.Count, rehomed.PhysicalSegmentCount);
        Assert.Equal(
            RoofBoundaryIdentityError.None,
            RoofBoundaryIdentityRules.ValidateCurrentSource(
                rehomed,
                mirrored.EdgeProvenance.Count,
                mirrored.Validation.SourceOrientation));

        // Original identity remains valid for the original winding — re-home is
        // new-owner-only and must not imply mutating the source identity.
        Assert.Equal(
            RoofBoundaryIdentityError.None,
            RoofBoundaryIdentityRules.ValidateCurrentSource(
                stale,
                source.EdgeProvenance.Count,
                source.Validation.SourceOrientation));
    }

    [Fact]
    public void ComplexX_RehomedIdentity_ResolvesStructuralDesiredSetMatchingSourceCounts()
    {
        var source = CreateDesiredSet(ComplexX());
        var mirroredFootprint = MirrorAcrossX(ComplexX());
        var mirroredNormalized = Normalize(mirroredFootprint);
        var rehomed = RoofBoundaryIdentityRules.CreateSequential(
            mirroredNormalized.EdgeProvenance.Count,
            mirroredNormalized.Validation.SourceOrientation).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(
            new RoofFootprintInput(mirroredFootprint, IsClosed: true),
            rehomed);
        Assert.True(provenance.IsValid, provenance.IdentityError.ToString());

        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            mirroredNormalized.Validation.Footprint!,
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(solved.IsValid);
        var geometry = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var resolution = RoofStructuralEdgeIdentityResolver.Resolve(geometry, provenance);
        Assert.True(resolution.IsValid, resolution.Error.ToString());
        var structural = RoofAutomaticStructuralRafterPlanner.Create(resolution);
        Assert.True(structural.IsValid, structural.Error.ToString());

        Assert.Equal(
            CountStructural(source.Structural, TimberElementType.HipRafter),
            CountStructural(structural, TimberElementType.HipRafter));
        Assert.Equal(
            CountStructural(source.Structural, TimberElementType.ValleyRafter),
            CountStructural(structural, TimberElementType.ValleyRafter));
    }

    [Fact]
    public void StaleClonedIdentity_DoesNotBecomeValidByWeakeningWindingRule()
    {
        // Guard: CurrentRawWindingMismatch remains a hard failure — re-home must
        // create a new identity, never accept opposite winding on the stale payload.
        var identity = RoofBoundaryIdentityRules.CreateSequential(
            4,
            RoofPolygonOrientation.Clockwise).Identity!;
        Assert.Equal(
            RoofBoundaryIdentityError.CurrentRawWindingMismatch,
            RoofBoundaryIdentityRules.ValidateCurrentSource(
                identity,
                4,
                RoofPolygonOrientation.CounterClockwise));
    }

    private static DesiredSet CreateDesiredSet(RoofPoint2D[] footprint)
    {
        var input = new RoofFootprintInput(footprint, IsClosed: true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid, normalized.Validation.Error.ToString());
        var solved = RoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(solved.IsValid, solved.Error.ToString());
        var geometry = Assert.IsType<HipRoofGeometry>(solved.Geometry);
        var ordinaryResult = RoofRafterLayoutSolver.Solve(
            geometry,
            new RafterLayoutParameters(900d, 80d, 500d));
        Assert.True(ordinaryResult.IsValid, ordinaryResult.Error.ToString());

        var identity = RoofBoundaryIdentityRules.CreateSequential(
            normalized.EdgeProvenance.Count,
            normalized.Validation.SourceOrientation).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        Assert.True(provenance.IsValid, provenance.IdentityError.ToString());
        var resolution = RoofStructuralEdgeIdentityResolver.Resolve(geometry, provenance);
        Assert.True(resolution.IsValid, resolution.Error.ToString());
        var structural = RoofAutomaticStructuralRafterPlanner.Create(resolution);
        Assert.True(structural.IsValid, structural.Error.ToString());
        return new DesiredSet(geometry, ordinaryResult.Layout!, structural);
    }

    private static RoofFootprintNormalizationResult Normalize(RoofPoint2D[] footprint)
    {
        var result = RoofFootprintValidator.ValidateWithProvenance(
            new RoofFootprintInput(footprint, IsClosed: true));
        Assert.True(result.Validation.IsValid, result.Validation.Error.ToString());
        return result;
    }

    private static int CountTopology(HipRoofGeometry geometry, RoofTopologyEdgeKind kind) =>
        geometry.Topology.Edges.Count(edge => edge.Kind == kind);

    private static int CountStructural(
        RoofAutomaticStructuralRafterPlanResult plan,
        TimberElementType type) =>
        plan.Items.Count(item => item.ElementType == type);

    private static RoofPoint2D[] MirrorAcrossX(IEnumerable<RoofPoint2D> points) =>
        points.Select(point => new RoofPoint2D(point.X, -point.Y)).ToArray();

    // Four reflex corners — same Complex X fixture as whole-roof Hip HOST parity.
    private static RoofPoint2D[] ComplexX() =>
    [
        new(0, 2000), new(2000, 2000), new(2000, 0), new(6000, 0),
        new(6000, 2000), new(8000, 2000), new(8000, 5500), new(6000, 5500),
        new(6000, 8000), new(2000, 8000), new(2000, 5500), new(0, 5500),
    ];

    private sealed record DesiredSet(
        HipRoofGeometry Geometry,
        RoofRafterLayout Ordinary,
        RoofAutomaticStructuralRafterPlanResult Structural);
}
