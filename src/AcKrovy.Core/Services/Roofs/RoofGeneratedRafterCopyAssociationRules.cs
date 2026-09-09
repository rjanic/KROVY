using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Deterministic same-DWG COPY ownership association for generated rafters.
/// Uses expected shared roof-layout geometry — never nearest-neighbor or handle order.
/// </summary>
public static class RoofGeneratedRafterCopyAssociationRules
{
    public static RoofGeneratedRafterCopyAssociationPlan BuildPlan(
        IReadOnlyList<RoofGeneratedRafterCopyOwnerTarget> owners,
        IReadOnlyList<RoofGeneratedRafterGeometryObservation> observations)
    {
        if (owners is null || owners.Count == 0 || observations is null || observations.Count == 0)
        {
            return new RoofGeneratedRafterCopyAssociationPlan([]);
        }

        var validObservations = observations
            .Where(IsValidObservation)
            .ToArray();
        if (validObservations.Length == 0)
        {
            return new RoofGeneratedRafterCopyAssociationPlan([]);
        }

        var recipes = validObservations
            .Select(item => item.Recipe)
            .GroupBy(RecipeKey)
            .Select(group => group.First())
            .Where(RoofRafterGenerationRecipeRules.IsValid)
            .ToArray();

        var provisional = new Dictionary<string, RoofGeneratedRafterCopyAssociation>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var owner in owners)
        {
            if (owner is null ||
                string.IsNullOrWhiteSpace(owner.OwnerReference) ||
                owner.Geometry is null)
            {
                continue;
            }

            var matches = new List<RoofGeneratedRafterCopyAssociation>();
            foreach (var recipe in recipes)
            {
                var layoutResult = RoofRafterLayoutSolver.Solve(
                    owner.Geometry,
                    new RafterLayoutParameters(recipe.MaximumSpacingMm, recipe.WidthMm));
                if (!layoutResult.IsValid || layoutResult.Layout is null)
                {
                    continue;
                }

                var recipeCandidates = validObservations
                    .Where(item => RecipeKey(item.Recipe) == RecipeKey(recipe))
                    .ToArray();
                if (!TryMatchCompleteSet(
                        layoutResult.Layout,
                        recipeCandidates,
                        out var matched))
                {
                    continue;
                }

                matches.Add(new RoofGeneratedRafterCopyAssociation(
                    owner.OwnerReference,
                    recipe,
                    layoutResult.Layout,
                    matched,
                    RequiresRewrite(owner.OwnerReference, layoutResult.Layout, matched)));
            }

            if (matches.Count == 1)
            {
                provisional[owner.OwnerReference] = matches[0];
            }
        }

        var claimedBy = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguousOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in provisional)
        {
            foreach (var member in pair.Value.Members)
            {
                if (claimedBy.TryGetValue(member.MemberKey, out var otherOwner) &&
                    !string.Equals(otherOwner, pair.Key, StringComparison.OrdinalIgnoreCase))
                {
                    ambiguousOwners.Add(pair.Key);
                    ambiguousOwners.Add(otherOwner);
                    continue;
                }

                claimedBy[member.MemberKey] = pair.Key;
            }
        }

        var associations = provisional
            .Where(pair => !ambiguousOwners.Contains(pair.Key))
            .Select(pair => pair.Value)
            .ToArray();
        return new RoofGeneratedRafterCopyAssociationPlan(associations);
    }

    public static bool TryMatchCompleteSet(
        RoofRafterLayout expected,
        IReadOnlyList<RoofGeneratedRafterGeometryObservation> candidates,
        out IReadOnlyList<RoofGeneratedRafterGeometryObservation> matched)
        => TryMatchCompleteSetCore(
            expected?.Rafters,
            expected?.StationCount ?? 0,
            candidates,
            GeometryMatches,
            out matched);

    public static bool TryMatchCompleteSet(
        SimpleGableRafterLayout expected,
        IReadOnlyList<RoofGeneratedRafterGeometryObservation> candidates,
        out IReadOnlyList<RoofGeneratedRafterGeometryObservation> matched)
        => TryMatchCompleteSetCore(
            expected?.Rafters,
            expected?.StationCount ?? 0,
            candidates,
            GeometryMatches,
            out matched);

    private static bool TryMatchCompleteSetCore<TExpected>(
        IReadOnlyList<TExpected>? expectedRafters,
        int stationCount,
        IReadOnlyList<RoofGeneratedRafterGeometryObservation> candidates,
        Func<TExpected, RoofGeneratedRafterGeometryObservation, bool> geometryMatches,
        out IReadOnlyList<RoofGeneratedRafterGeometryObservation> matched)
        where TExpected : class
    {
        matched = Array.Empty<RoofGeneratedRafterGeometryObservation>();
        if (expectedRafters is null ||
            expectedRafters.Count == 0 ||
            candidates is null ||
            candidates.Count < expectedRafters.Count)
        {
            return false;
        }

        var remaining = candidates.ToList();
        var selected = new List<RoofGeneratedRafterGeometryObservation>(expectedRafters.Count);
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var expectedRafter in expectedRafters)
        {
            var identity = ExpectedIdentity(expectedRafter);
            var hits = remaining
                .Where(candidate =>
                    !usedKeys.Contains(candidate.MemberKey) &&
                    candidate.Face == identity.Face &&
                    candidate.StationIndex == identity.StationIndex &&
                    candidate.StationCount == stationCount &&
                    geometryMatches(expectedRafter, candidate))
                .ToArray();
            if (hits.Length != 1)
            {
                return false;
            }

            selected.Add(hits[0]);
            usedKeys.Add(hits[0].MemberKey);
            remaining.Remove(hits[0]);
        }

        if (selected.Count != expectedRafters.Count ||
            !RoofGeneratedTimberOwnershipRules.HasUniqueMemberStations(
                selected.Select(ToGeneratedData).ToArray()))
        {
            return false;
        }

        matched = selected;
        return true;
    }

    public static bool GeometryMatches(
        RoofRafterGeometry expected,
        RoofGeneratedRafterGeometryObservation actual) =>
        SegmentsEqual(expected.PlanStart, expected.PlanEnd, actual.PlanStart, actual.PlanEnd) ||
        SegmentsEqual(expected.PlanStart, expected.PlanEnd, actual.PlanEnd, actual.PlanStart);

    public static bool GeometryMatches(
        SimpleGableRafter expected,
        RoofGeneratedRafterGeometryObservation actual) =>
        SegmentsEqual(expected.PlanStart, expected.PlanEnd, actual.PlanStart, actual.PlanEnd) ||
        SegmentsEqual(expected.PlanStart, expected.PlanEnd, actual.PlanEnd, actual.PlanStart);

    private static (RafterRoofFace Face, int StationIndex) ExpectedIdentity<TExpected>(
        TExpected expected) =>
        expected switch
        {
            RoofRafterGeometry shared => (shared.Face, shared.StationIndex),
            SimpleGableRafter gable => (gable.Face, gable.StationIndex),
            _ => throw new ArgumentException("Unsupported expected rafter geometry.", nameof(expected)),
        };

    private static bool SegmentsEqual(
        RoofPoint2D expectedStart,
        RoofPoint2D expectedEnd,
        RoofPoint2D actualStart,
        RoofPoint2D actualEnd) =>
        PointsEqual(expectedStart, actualStart) && PointsEqual(expectedEnd, actualEnd);

    private static bool PointsEqual(RoofPoint2D left, RoofPoint2D right)
    {
        var tolerance = SimpleGableRoofGeometryTolerance.CoordinateToleranceMm;
        return Math.Abs(left.X - right.X) <= tolerance &&
               Math.Abs(left.Y - right.Y) <= tolerance;
    }

    private static bool RequiresRewrite(
        string ownerReference,
        RoofRafterLayout expectedLayout,
        IReadOnlyList<RoofGeneratedRafterGeometryObservation> members) =>
        members.Any(member =>
            !string.Equals(
                member.EffectiveOwnerReference,
                ownerReference,
                StringComparison.OrdinalIgnoreCase) ||
            !RoofGeneratedTimberFreshness.MatchesPersistedLayoutIdentity(
                member.LayoutSignature,
                expectedLayout.Signature));

    private static bool IsValidObservation(RoofGeneratedRafterGeometryObservation observation) =>
        observation is not null &&
        !string.IsNullOrWhiteSpace(observation.MemberKey) &&
        RoofRafterGenerationRecipeRules.IsValid(observation.Recipe) &&
        observation.StationCount >= 2 &&
        observation.StationIndex >= 0 &&
        observation.StationIndex < observation.StationCount;

    private static string RecipeKey(RoofRafterGenerationRecipe recipe) =>
        string.Join(
            "|",
            recipe.WidthMm.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            recipe.HeightMm.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            recipe.MaximumSpacingMm.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            recipe.Material);

    private static RoofGeneratedTimberData ToGeneratedData(
        RoofGeneratedRafterGeometryObservation observation) =>
        new(
            RoofGeneratedTimberDataSchema.CurrentVersion,
            observation.EffectiveOwnerReference,
            RoofGeneratedTimberKind.Rafter,
            observation.Face,
            observation.StationIndex,
            observation.StationCount,
            observation.Recipe.MaximumSpacingMm,
            string.IsNullOrWhiteSpace(observation.LayoutSignature)
                ? "pending"
                : observation.LayoutSignature);
}
