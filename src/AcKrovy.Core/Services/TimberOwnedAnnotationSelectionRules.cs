using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

/// <summary>
/// Read-only classification and logical grouping for owned KROVY annotations.
/// Manual Edit kóty transforms will consume these groups; this layer never
/// mutates geometry or metadata.
/// </summary>
public static class TimberOwnedAnnotationSelectionRules
{
    public const int ProductionRendererGeneration =
        TimberMainAnnotationOwnershipRules.G5RendererGeneration;

    /// <summary>
    /// Evaluate a user selection of owned annotation component probes.
    /// <paramref name="componentsBySourceHandle"/> must include every owned
    /// main-annotation component for each SourceHandle that appears in
    /// <paramref name="selectedComponents"/> so Combined Plain can expand.
    /// </summary>
    public static TimberOwnedAnnotationSelectionEvaluation Evaluate(
        IReadOnlyList<TimberOwnedAnnotationComponentProbe> selectedComponents,
        IReadOnlyDictionary<string, IReadOnlyList<TimberOwnedAnnotationComponentProbe>>
            componentsBySourceHandle)
    {
        if (selectedComponents is null)
        {
            throw new ArgumentNullException(nameof(selectedComponents));
        }

        if (componentsBySourceHandle is null)
        {
            throw new ArgumentNullException(nameof(componentsBySourceHandle));
        }

        var accepted = new List<TimberOwnedAnnotationAcceptedGroup>();
        var skipped = new List<TimberOwnedAnnotationSkippedItem>();
        var rejected = new List<TimberOwnedAnnotationRejectedGroup>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var consumedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var consumedGroupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var selected in selectedComponents)
        {
            if (selected is null ||
                string.IsNullOrWhiteSpace(selected.ComponentKey))
            {
                skipped.Add(new TimberOwnedAnnotationSkippedItem(
                    null,
                    TimberOwnedAnnotationSkipReason.UnrelatedEntity));
                continue;
            }

            var key = selected.ComponentKey.Trim();
            if (!seenKeys.Add(key))
            {
                skipped.Add(new TimberOwnedAnnotationSkippedItem(
                    key,
                    TimberOwnedAnnotationSkipReason.DuplicateSelection));
                continue;
            }

            if (consumedKeys.Contains(key))
            {
                skipped.Add(new TimberOwnedAnnotationSkippedItem(
                    key,
                    TimberOwnedAnnotationSkipReason.AlreadyConsumedByGroup));
                continue;
            }

            if (string.IsNullOrWhiteSpace(selected.SourceHandle))
            {
                rejected.Add(Reject(
                    null,
                    null,
                    TimberOwnedAnnotationRejectReason.UnresolvedSource,
                    key));
                continue;
            }

            var sourceHandle = selected.SourceHandle.Trim();
            if (!componentsBySourceHandle.TryGetValue(sourceHandle, out var siblings) ||
                siblings is null ||
                siblings.Count == 0)
            {
                siblings = new[] { selected };
            }

            var outcome = TryBuildGroup(selected, siblings);
            if (outcome.Disposition == TimberOwnedAnnotationSelectionDisposition.Rejected)
            {
                rejected.Add(outcome.Rejected!);
                MarkConsumed(consumedKeys, outcome.Rejected!.ComponentKeys);
                continue;
            }

            var groupKey = outcome.Accepted!.Snapshot.LogicalGroupKey;
            if (!consumedGroupKeys.Add(groupKey))
            {
                skipped.Add(new TimberOwnedAnnotationSkippedItem(
                    key,
                    TimberOwnedAnnotationSkipReason.AlreadyConsumedByGroup));
                continue;
            }

            accepted.Add(outcome.Accepted);
            MarkConsumed(
                consumedKeys,
                outcome.Accepted.Snapshot.Components.Select(c => c.ComponentKey));
        }

        return new TimberOwnedAnnotationSelectionEvaluation(
            accepted,
            skipped,
            rejected);
    }

    public static bool IsLegacyG4Role(TimberMainAnnotationComponentRole role) =>
        TimberMainAnnotationOwnershipRules.IsG4CompositeRole(role);

    public static bool IsFramedStyle(ItemNumberLeaderStyle style) =>
        ItemNumberLeaderStyleRules.Normalize(style) is
            ItemNumberLeaderStyle.Circle or
            ItemNumberLeaderStyle.Slot or
            ItemNumberLeaderStyle.Rectangle;

    private static GroupBuildOutcome TryBuildGroup(
        TimberOwnedAnnotationComponentProbe seed,
        IReadOnlyList<TimberOwnedAnnotationComponentProbe> siblings)
    {
        var mode = TimberAnnotationModeRules.Normalize(seed.AnnotationMode);
        var style = ItemNumberLeaderStyleRules.Normalize(seed.ItemNumberLeaderStyle);

        if (IsLegacyG4Role(seed.ComponentRole))
        {
            return GroupBuildOutcome.Reject(Reject(
                BuildGroupKey(seed.SourceHandle, "LegacyG4"),
                seed.SourceHandle,
                TimberOwnedAnnotationRejectReason.LegacyG4Composite,
                CollectKeys(siblings.Where(s =>
                    string.Equals(
                        s.SourceHandle.Trim(),
                        seed.SourceHandle.Trim(),
                        StringComparison.OrdinalIgnoreCase) &&
                    IsLegacyG4Role(s.ComponentRole)))));
        }

        if (mode is TimberAnnotationMode.FullLabel or TimberAnnotationMode.NoAnnotations)
        {
            return GroupBuildOutcome.Reject(Reject(
                null,
                seed.SourceHandle,
                TimberOwnedAnnotationRejectReason.UnsupportedRepresentation,
                seed.ComponentKey));
        }

        if (mode == TimberAnnotationMode.ItemNumberLeader &&
            style == ItemNumberLeaderStyle.Plain)
        {
            return BuildSingleLeaderGroup(
                seed,
                TimberOwnedAnnotationRepresentationKind.PlainItemOnly,
                frameStyle: null,
                requireBlockContent: false,
                requireMTextContent: true,
                expectedRole: TimberMainAnnotationComponentRole.Primary,
                requireRendererGeneration: false);
        }

        if (mode == TimberAnnotationMode.DimensionsLeader)
        {
            return BuildSingleLeaderGroup(
                seed,
                TimberOwnedAnnotationRepresentationKind.DimensionsOnly,
                frameStyle: null,
                requireBlockContent: false,
                requireMTextContent: true,
                expectedRole: TimberMainAnnotationComponentRole.Primary,
                requireRendererGeneration: false);
        }

        if (mode == TimberAnnotationMode.ItemNumberLeader && IsFramedStyle(style))
        {
            return BuildSingleLeaderGroup(
                seed,
                TimberOwnedAnnotationRepresentationKind.FramedItemOnly,
                frameStyle: style,
                requireBlockContent: true,
                requireMTextContent: false,
                expectedRole: TimberMainAnnotationComponentRole.Primary,
                requireRendererGeneration: true);
        }

        if (mode == TimberAnnotationMode.DimensionsWithItemNumber &&
            style == ItemNumberLeaderStyle.Plain)
        {
            return BuildCombinedPlainGroup(seed, siblings);
        }

        if (mode == TimberAnnotationMode.DimensionsWithItemNumber && IsFramedStyle(style))
        {
            return BuildSingleLeaderGroup(
                seed,
                TimberOwnedAnnotationRepresentationKind.R3Combined,
                frameStyle: style,
                requireBlockContent: true,
                requireMTextContent: false,
                expectedRole: TimberMainAnnotationComponentRole.FramedItem,
                requireRendererGeneration: true);
        }

        return GroupBuildOutcome.Reject(Reject(
            null,
            seed.SourceHandle,
            TimberOwnedAnnotationRejectReason.UnsupportedRepresentation,
            seed.ComponentKey));
    }

    private static GroupBuildOutcome BuildSingleLeaderGroup(
        TimberOwnedAnnotationComponentProbe seed,
        TimberOwnedAnnotationRepresentationKind kind,
        ItemNumberLeaderStyle? frameStyle,
        bool requireBlockContent,
        bool requireMTextContent,
        TimberMainAnnotationComponentRole expectedRole,
        bool requireRendererGeneration)
    {
        if (seed.ComponentRole != expectedRole)
        {
            return GroupBuildOutcome.Reject(Reject(
                BuildGroupKey(seed.SourceHandle, kind),
                seed.SourceHandle,
                TimberOwnedAnnotationRejectReason.RoleMismatch,
                seed.ComponentKey));
        }

        if (seed.EntityKind != TimberOwnedAnnotationEntityKind.MLeader)
        {
            return GroupBuildOutcome.Reject(Reject(
                BuildGroupKey(seed.SourceHandle, kind),
                seed.SourceHandle,
                TimberOwnedAnnotationRejectReason.EntityKindMismatch,
                seed.ComponentKey));
        }

        if (requireBlockContent && !seed.IsBlockContentMLeader)
        {
            return GroupBuildOutcome.Reject(Reject(
                BuildGroupKey(seed.SourceHandle, kind),
                seed.SourceHandle,
                TimberOwnedAnnotationRejectReason.ContentTypeMismatch,
                seed.ComponentKey));
        }

        if (requireMTextContent && !seed.IsMTextContentMLeader)
        {
            return GroupBuildOutcome.Reject(Reject(
                BuildGroupKey(seed.SourceHandle, kind),
                seed.SourceHandle,
                TimberOwnedAnnotationRejectReason.ContentTypeMismatch,
                seed.ComponentKey));
        }

        if (requireRendererGeneration &&
            seed.RendererGeneration is not null &&
            seed.RendererGeneration != ProductionRendererGeneration)
        {
            return GroupBuildOutcome.Reject(Reject(
                BuildGroupKey(seed.SourceHandle, kind),
                seed.SourceHandle,
                TimberOwnedAnnotationRejectReason.RendererGenerationMismatch,
                seed.ComponentKey));
        }

        if (requireRendererGeneration &&
            kind == TimberOwnedAnnotationRepresentationKind.R3Combined &&
            seed.RendererGeneration != ProductionRendererGeneration)
        {
            return GroupBuildOutcome.Reject(Reject(
                BuildGroupKey(seed.SourceHandle, kind),
                seed.SourceHandle,
                TimberOwnedAnnotationRejectReason.RendererGenerationMismatch,
                seed.ComponentKey));
        }

        var style = ItemNumberLeaderStyleRules.Normalize(seed.ItemNumberLeaderStyle);
        var snapshot = new TimberOwnedAnnotationGroupSnapshot
        {
            LogicalGroupKey = BuildGroupKey(seed.SourceHandle, kind),
            RepresentationKind = kind,
            FrameStyle = frameStyle,
            SourceHandle = seed.SourceHandle.Trim(),
            ElementId = seed.ElementId?.Trim() ?? string.Empty,
            AnnotationMode = TimberAnnotationModeRules.Normalize(seed.AnnotationMode),
            ItemNumberLeaderStyle = style,
            RendererGeneration = seed.RendererGeneration,
            ComponentCount = 1,
            MainLeaderComponentKey = seed.ComponentKey.Trim(),
            Components =
            [
                new TimberOwnedAnnotationComponentSnapshot
                {
                    ComponentKey = seed.ComponentKey.Trim(),
                    ComponentRole = seed.ComponentRole,
                    EntityKind = seed.EntityKind,
                    IsMainLeader = true,
                },
            ],
            ContentOrientationIsAbsoluteWorld =
                kind is TimberOwnedAnnotationRepresentationKind.PlainItemOnly or
                    TimberOwnedAnnotationRepresentationKind.DimensionsOnly or
                    TimberOwnedAnnotationRepresentationKind.FramedItemOnly,
            SourceResolved = false,
        };

        return GroupBuildOutcome.Accept(
            new TimberOwnedAnnotationAcceptedGroup(snapshot));
    }

    private static GroupBuildOutcome BuildCombinedPlainGroup(
        TimberOwnedAnnotationComponentProbe seed,
        IReadOnlyList<TimberOwnedAnnotationComponentProbe> siblings)
    {
        var sourceHandle = seed.SourceHandle.Trim();
        var candidates = siblings
            .Where(sibling =>
                !string.IsNullOrWhiteSpace(sibling.ComponentKey) &&
                !string.IsNullOrWhiteSpace(sibling.SourceHandle) &&
                string.Equals(
                    sibling.SourceHandle.Trim(),
                    sourceHandle,
                    StringComparison.OrdinalIgnoreCase) &&
                TimberAnnotationModeRules.Normalize(sibling.AnnotationMode) ==
                    TimberAnnotationMode.DimensionsWithItemNumber &&
                ItemNumberLeaderStyleRules.Normalize(sibling.ItemNumberLeaderStyle) ==
                    ItemNumberLeaderStyle.Plain)
            .ToList();

        var leaders = candidates
            .Where(c =>
                c.ComponentRole == TimberMainAnnotationComponentRole.FramedItem &&
                c.EntityKind == TimberOwnedAnnotationEntityKind.MLeader)
            .ToList();
        var dimensions = candidates
            .Where(c =>
                c.ComponentRole == TimberMainAnnotationComponentRole.Primary &&
                c.EntityKind == TimberOwnedAnnotationEntityKind.MText)
            .ToList();

        if (leaders.Count == 0 || dimensions.Count == 0)
        {
            return GroupBuildOutcome.Reject(Reject(
                BuildGroupKey(sourceHandle, TimberOwnedAnnotationRepresentationKind.CombinedPlain),
                sourceHandle,
                TimberOwnedAnnotationRejectReason.IncompleteCombinedPlain,
                CollectKeys(candidates.Count > 0 ? candidates : new[] { seed })));
        }

        if (leaders.Count != 1 || dimensions.Count != 1)
        {
            return GroupBuildOutcome.Reject(Reject(
                BuildGroupKey(sourceHandle, TimberOwnedAnnotationRepresentationKind.CombinedPlain),
                sourceHandle,
                TimberOwnedAnnotationRejectReason.AmbiguousOwnership,
                CollectKeys(leaders.Concat(dimensions))));
        }

        var leader = leaders[0];
        var dimension = dimensions[0];
        // Combined Plain item is a native MTextContent MLeader.
        if (!leader.IsMTextContentMLeader)
        {
            return GroupBuildOutcome.Reject(Reject(
                BuildGroupKey(sourceHandle, TimberOwnedAnnotationRepresentationKind.CombinedPlain),
                sourceHandle,
                TimberOwnedAnnotationRejectReason.ContentTypeMismatch,
                CollectKeys(new[] { leader, dimension })));
        }

        if (seed.ComponentRole is not (
                TimberMainAnnotationComponentRole.FramedItem or
                TimberMainAnnotationComponentRole.Primary))
        {
            return GroupBuildOutcome.Reject(Reject(
                BuildGroupKey(sourceHandle, TimberOwnedAnnotationRepresentationKind.CombinedPlain),
                sourceHandle,
                TimberOwnedAnnotationRejectReason.RoleMismatch,
                seed.ComponentKey));
        }

        var snapshot = new TimberOwnedAnnotationGroupSnapshot
        {
            LogicalGroupKey = BuildGroupKey(
                sourceHandle,
                TimberOwnedAnnotationRepresentationKind.CombinedPlain),
            RepresentationKind = TimberOwnedAnnotationRepresentationKind.CombinedPlain,
            FrameStyle = null,
            SourceHandle = sourceHandle,
            ElementId = FirstNonEmpty(leader.ElementId, dimension.ElementId, seed.ElementId),
            AnnotationMode = TimberAnnotationMode.DimensionsWithItemNumber,
            ItemNumberLeaderStyle = ItemNumberLeaderStyle.Plain,
            RendererGeneration = leader.RendererGeneration ?? dimension.RendererGeneration,
            ComponentCount = 2,
            MainLeaderComponentKey = leader.ComponentKey.Trim(),
            Components =
            [
                new TimberOwnedAnnotationComponentSnapshot
                {
                    ComponentKey = leader.ComponentKey.Trim(),
                    ComponentRole = TimberMainAnnotationComponentRole.FramedItem,
                    EntityKind = TimberOwnedAnnotationEntityKind.MLeader,
                    IsMainLeader = true,
                },
                new TimberOwnedAnnotationComponentSnapshot
                {
                    ComponentKey = dimension.ComponentKey.Trim(),
                    ComponentRole = TimberMainAnnotationComponentRole.Primary,
                    EntityKind = TimberOwnedAnnotationEntityKind.MText,
                    IsMainLeader = false,
                },
            ],
            ContentOrientationIsAbsoluteWorld = true,
            SourceResolved = false,
        };

        return GroupBuildOutcome.Accept(
            new TimberOwnedAnnotationAcceptedGroup(snapshot));
    }

    public static string BuildGroupKey(
        string sourceHandle,
        TimberOwnedAnnotationRepresentationKind kind) =>
        BuildGroupKey(sourceHandle, kind.ToString());

    public static string BuildGroupKey(string sourceHandle, string kindToken) =>
        $"{sourceHandle.Trim()}|{kindToken}";

    private static TimberOwnedAnnotationRejectedGroup Reject(
        string? logicalGroupKey,
        string? sourceHandle,
        TimberOwnedAnnotationRejectReason reason,
        params string[] componentKeys) =>
        Reject(
            logicalGroupKey,
            sourceHandle,
            reason,
            (IEnumerable<string>)componentKeys);

    private static TimberOwnedAnnotationRejectedGroup Reject(
        string? logicalGroupKey,
        string? sourceHandle,
        TimberOwnedAnnotationRejectReason reason,
        IEnumerable<string> componentKeys) =>
        new(
            logicalGroupKey,
            string.IsNullOrWhiteSpace(sourceHandle) ? null : sourceHandle!.Trim(),
            reason,
            CollectKeys(componentKeys));

    private static IReadOnlyList<string> CollectKeys(
        IEnumerable<TimberOwnedAnnotationComponentProbe> probes) =>
        CollectKeys(probes
            .Where(probe => !string.IsNullOrWhiteSpace(probe.ComponentKey))
            .Select(probe => probe.ComponentKey));

    private static IReadOnlyList<string> CollectKeys(IEnumerable<string> keys) =>
        keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static void MarkConsumed(
        HashSet<string> consumed,
        IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                consumed.Add(key.Trim());
            }
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!.Trim();
            }
        }

        return string.Empty;
    }

    private sealed record GroupBuildOutcome(
        TimberOwnedAnnotationSelectionDisposition Disposition,
        TimberOwnedAnnotationAcceptedGroup? Accepted,
        TimberOwnedAnnotationRejectedGroup? Rejected)
    {
        public static GroupBuildOutcome Accept(
            TimberOwnedAnnotationAcceptedGroup accepted) =>
            new(TimberOwnedAnnotationSelectionDisposition.Accepted, accepted, null);

        public static GroupBuildOutcome Reject(
            TimberOwnedAnnotationRejectedGroup rejected) =>
            new(TimberOwnedAnnotationSelectionDisposition.Rejected, null, rejected);
    }
}
