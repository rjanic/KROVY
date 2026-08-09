using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class TimberOwnedAnnotationSelectionRulesTests
{
    [Fact]
    public void PlainItemOnly_IsAccepted()
    {
        var probe = Leader(
            "L1",
            "H1",
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain,
            TimberMainAnnotationComponentRole.Primary,
            mTextContent: true);

        var result = Evaluate(probe);

        var accepted = Assert.Single(result.Accepted);
        Assert.Equal(
            TimberOwnedAnnotationRepresentationKind.PlainItemOnly,
            accepted.Snapshot.RepresentationKind);
        Assert.Equal("L1", accepted.Snapshot.MainLeaderComponentKey);
        Assert.Equal(1, accepted.Snapshot.ComponentCount);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void DimensionsOnly_IsAccepted()
    {
        var probe = Leader(
            "D1",
            "H1",
            TimberAnnotationMode.DimensionsLeader,
            ItemNumberLeaderStyle.Plain,
            TimberMainAnnotationComponentRole.Primary,
            mTextContent: true);

        var accepted = Assert.Single(Evaluate(probe).Accepted);
        Assert.Equal(
            TimberOwnedAnnotationRepresentationKind.DimensionsOnly,
            accepted.Snapshot.RepresentationKind);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    public void FramedItemOnly_IsAccepted(ItemNumberLeaderStyle style)
    {
        var probe = Leader(
            "F1",
            "H1",
            TimberAnnotationMode.ItemNumberLeader,
            style,
            TimberMainAnnotationComponentRole.Primary,
            blockContent: true,
            rendererGeneration: 5);

        var accepted = Assert.Single(Evaluate(probe).Accepted);
        Assert.Equal(
            TimberOwnedAnnotationRepresentationKind.FramedItemOnly,
            accepted.Snapshot.RepresentationKind);
        Assert.Equal(style, accepted.Snapshot.FrameStyle);
    }

    [Theory]
    [InlineData(ItemNumberLeaderStyle.Circle)]
    [InlineData(ItemNumberLeaderStyle.Rectangle)]
    [InlineData(ItemNumberLeaderStyle.Slot)]
    public void R3Combined_IsAccepted(ItemNumberLeaderStyle style)
    {
        var probe = Leader(
            "R1",
            "H1",
            TimberAnnotationMode.DimensionsWithItemNumber,
            style,
            TimberMainAnnotationComponentRole.FramedItem,
            blockContent: true,
            rendererGeneration: 5);

        var accepted = Assert.Single(Evaluate(probe).Accepted);
        Assert.Equal(
            TimberOwnedAnnotationRepresentationKind.R3Combined,
            accepted.Snapshot.RepresentationKind);
        Assert.Equal(style, accepted.Snapshot.FrameStyle);
    }

    [Fact]
    public void CombinedPlain_SelectingLeader_ExpandsToBothComponents()
    {
        var leader = CombinedLeader("ML1", "H1");
        var dimensions = CombinedDimensions("MT1", "H1");

        var result = Evaluate(
            new[] { leader },
            BySource(leader, dimensions));

        var accepted = Assert.Single(result.Accepted);
        Assert.Equal(
            TimberOwnedAnnotationRepresentationKind.CombinedPlain,
            accepted.Snapshot.RepresentationKind);
        Assert.Equal(2, accepted.Snapshot.ComponentCount);
        Assert.Equal("ML1", accepted.Snapshot.MainLeaderComponentKey);
        Assert.Contains(
            accepted.Snapshot.Components,
            component => component.ComponentKey == "MT1");
    }

    [Fact]
    public void CombinedPlain_SelectingDimensions_ExpandsToBothComponents()
    {
        var leader = CombinedLeader("ML1", "H1");
        var dimensions = CombinedDimensions("MT1", "H1");

        var result = Evaluate(
            new[] { dimensions },
            BySource(leader, dimensions));

        var accepted = Assert.Single(result.Accepted);
        Assert.Equal(2, accepted.Snapshot.ComponentCount);
        Assert.Equal("ML1", accepted.Snapshot.MainLeaderComponentKey);
    }

    [Fact]
    public void CombinedPlain_SelectingBoth_DeduplicatesToOneGroup()
    {
        var leader = CombinedLeader("ML1", "H1");
        var dimensions = CombinedDimensions("MT1", "H1");

        var result = Evaluate(
            new[] { leader, dimensions },
            BySource(leader, dimensions));

        Assert.Single(result.Accepted);
        Assert.Contains(
            result.Skipped,
            item => item.Reason ==
                TimberOwnedAnnotationSkipReason.AlreadyConsumedByGroup);
    }

    [Fact]
    public void CombinedPlain_Incomplete_IsRejected()
    {
        var leader = CombinedLeader("ML1", "H1");

        var result = Evaluate(
            new[] { leader },
            BySource(leader));

        var rejected = Assert.Single(result.Rejected);
        Assert.Equal(
            TimberOwnedAnnotationRejectReason.IncompleteCombinedPlain,
            rejected.Reason);
        Assert.Empty(result.Accepted);
    }

    [Fact]
    public void CombinedPlain_AmbiguousDuplicateLeaders_IsRejected()
    {
        var leaderA = CombinedLeader("ML1", "H1");
        var leaderB = CombinedLeader("ML2", "H1");
        var dimensions = CombinedDimensions("MT1", "H1");

        var result = Evaluate(
            new[] { leaderA },
            BySource(leaderA, leaderB, dimensions));

        var rejected = Assert.Single(result.Rejected);
        Assert.Equal(
            TimberOwnedAnnotationRejectReason.AmbiguousOwnership,
            rejected.Reason);
    }

    [Fact]
    public void WrongRole_IsRejected()
    {
        var probe = Leader(
            "L1",
            "H1",
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain,
            TimberMainAnnotationComponentRole.FramedItem,
            mTextContent: true);

        var rejected = Assert.Single(Evaluate(probe).Rejected);
        Assert.Equal(TimberOwnedAnnotationRejectReason.RoleMismatch, rejected.Reason);
    }

    [Fact]
    public void EmptySourceHandle_IsRejectedAsUnresolvedSource()
    {
        var probe = Leader(
            "L1",
            " ",
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain,
            TimberMainAnnotationComponentRole.Primary,
            mTextContent: true);

        var rejected = Assert.Single(Evaluate(probe).Rejected);
        Assert.Equal(
            TimberOwnedAnnotationRejectReason.UnresolvedSource,
            rejected.Reason);
    }

    [Fact]
    public void DuplicateSelectedIds_AreSkipped()
    {
        var probe = Leader(
            "L1",
            "H1",
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain,
            TimberMainAnnotationComponentRole.Primary,
            mTextContent: true);

        var result = Evaluate(
            new[] { probe, probe },
            BySource(probe));

        Assert.Single(result.Accepted);
        Assert.Contains(
            result.Skipped,
            item => item.Reason == TimberOwnedAnnotationSkipReason.DuplicateSelection);
    }

    [Fact]
    public void MixedValidAndInvalidSelection_KeepsValidGroups()
    {
        var plain = Leader(
            "P1",
            "H1",
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain,
            TimberMainAnnotationComponentRole.Primary,
            mTextContent: true);
        var fullLabel = new TimberOwnedAnnotationComponentProbe
        {
            ComponentKey = "FL1",
            SourceHandle = "H2",
            ElementId = "K2",
            AnnotationMode = TimberAnnotationMode.FullLabel,
            ItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain,
            ComponentRole = TimberMainAnnotationComponentRole.Primary,
            EntityKind = TimberOwnedAnnotationEntityKind.MText,
        };

        var result = Evaluate(
            new[] { plain, fullLabel },
            BySource(plain, fullLabel));

        Assert.Single(result.Accepted);
        Assert.Single(result.Rejected);
        Assert.Equal(
            TimberOwnedAnnotationRejectReason.UnsupportedRepresentation,
            result.Rejected[0].Reason);
    }

    [Fact]
    public void LegacyG4_IsRejected()
    {
        var probe = new TimberOwnedAnnotationComponentProbe
        {
            ComponentKey = "G4",
            SourceHandle = "H1",
            ElementId = "K1",
            AnnotationMode = TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle = ItemNumberLeaderStyle.Circle,
            ComponentRole = TimberMainAnnotationComponentRole.CircleText,
            RendererGeneration = 4,
            EntityKind = TimberOwnedAnnotationEntityKind.MText,
        };

        var rejected = Assert.Single(Evaluate(probe).Rejected);
        Assert.Equal(
            TimberOwnedAnnotationRejectReason.LegacyG4Composite,
            rejected.Reason);
    }

    [Fact]
    public void FullLabel_IsRejected()
    {
        var probe = new TimberOwnedAnnotationComponentProbe
        {
            ComponentKey = "FL1",
            SourceHandle = "H1",
            ElementId = "K1",
            AnnotationMode = TimberAnnotationMode.FullLabel,
            ItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain,
            ComponentRole = TimberMainAnnotationComponentRole.Primary,
            EntityKind = TimberOwnedAnnotationEntityKind.MText,
        };

        var rejected = Assert.Single(Evaluate(probe).Rejected);
        Assert.Equal(
            TimberOwnedAnnotationRejectReason.UnsupportedRepresentation,
            rejected.Reason);
    }

    [Fact]
    public void R3Combined_WrongRole_IsRejected()
    {
        var probe = Leader(
            "R1",
            "H1",
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Circle,
            TimberMainAnnotationComponentRole.Primary,
            blockContent: true,
            rendererGeneration: 5);

        var rejected = Assert.Single(Evaluate(probe).Rejected);
        Assert.Equal(TimberOwnedAnnotationRejectReason.RoleMismatch, rejected.Reason);
    }

    [Fact]
    public void R3Combined_MissingRendererGeneration_IsRejected()
    {
        var probe = Leader(
            "R1",
            "H1",
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Circle,
            TimberMainAnnotationComponentRole.FramedItem,
            blockContent: true,
            rendererGeneration: null);

        var rejected = Assert.Single(Evaluate(probe).Rejected);
        Assert.Equal(
            TimberOwnedAnnotationRejectReason.RendererGenerationMismatch,
            rejected.Reason);
    }

    [Fact]
    public void PlainItemOnly_WrongContentType_IsRejected()
    {
        var probe = Leader(
            "L1",
            "H1",
            TimberAnnotationMode.ItemNumberLeader,
            ItemNumberLeaderStyle.Plain,
            TimberMainAnnotationComponentRole.Primary,
            blockContent: true);

        var rejected = Assert.Single(Evaluate(probe).Rejected);
        Assert.Equal(
            TimberOwnedAnnotationRejectReason.ContentTypeMismatch,
            rejected.Reason);
    }

    [Fact]
    public void CoreSelectionModel_HasNoAutodeskAssemblyDependency()
    {
        var references = typeof(TimberOwnedAnnotationSelectionRules)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(references, name =>
            name.StartsWith("Autodesk.AutoCAD", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("AcMgd", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("AcDbMgd", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("AcCoreMgd", StringComparison.OrdinalIgnoreCase));
    }

    private static TimberOwnedAnnotationSelectionEvaluation Evaluate(
        TimberOwnedAnnotationComponentProbe probe) =>
        Evaluate(
            new[] { probe },
            BySource(probe));

    private static TimberOwnedAnnotationSelectionEvaluation Evaluate(
        IReadOnlyList<TimberOwnedAnnotationComponentProbe> selected,
        IReadOnlyDictionary<string, IReadOnlyList<TimberOwnedAnnotationComponentProbe>>
            bySource) =>
        TimberOwnedAnnotationSelectionRules.Evaluate(selected, bySource);

    private static IReadOnlyDictionary<string, IReadOnlyList<TimberOwnedAnnotationComponentProbe>>
        BySource(params TimberOwnedAnnotationComponentProbe[] probes) =>
        probes
            .GroupBy(
                probe => probe.SourceHandle.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<TimberOwnedAnnotationComponentProbe>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

    private static TimberOwnedAnnotationComponentProbe CombinedLeader(
        string key,
        string sourceHandle) =>
        Leader(
            key,
            sourceHandle,
            TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle.Plain,
            TimberMainAnnotationComponentRole.FramedItem,
            mTextContent: true);

    private static TimberOwnedAnnotationComponentProbe CombinedDimensions(
        string key,
        string sourceHandle) =>
        new()
        {
            ComponentKey = key,
            SourceHandle = sourceHandle,
            ElementId = "K1",
            AnnotationMode = TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain,
            ComponentRole = TimberMainAnnotationComponentRole.Primary,
            EntityKind = TimberOwnedAnnotationEntityKind.MText,
        };

    private static TimberOwnedAnnotationComponentProbe Leader(
        string key,
        string sourceHandle,
        TimberAnnotationMode mode,
        ItemNumberLeaderStyle style,
        TimberMainAnnotationComponentRole role,
        bool mTextContent = false,
        bool blockContent = false,
        int? rendererGeneration = null) =>
        new()
        {
            ComponentKey = key,
            SourceHandle = sourceHandle,
            ElementId = "K1",
            AnnotationMode = mode,
            ItemNumberLeaderStyle = style,
            ComponentRole = role,
            RendererGeneration = rendererGeneration,
            EntityKind = TimberOwnedAnnotationEntityKind.MLeader,
            IsMTextContentMLeader = mTextContent,
            IsBlockContentMLeader = blockContent,
        };
}
