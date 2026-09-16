using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// CAD-neutral ownership classification for the DEBUG roof ownership invariant.
/// GROUP membership is not ownership proof; each generated class keeps its own store.
/// </summary>
public static class RoofGeneratedOwnershipInvariantRules
{
    public enum CandidateClass
    {
        IgnoreNonTimber = 0,
        AttachedManual = 1,
        OrdinaryGenerated = 2,
        StructuralGenerated = 3,
        AutomaticPurlin = 4,
        InvalidOrdinaryOwnership = 5,
        InvalidStructuralOwnership = 6,
        OrphanPhysicalTimber = 7,
    }

    public sealed record Snapshot(
        int PhysicalTimberCandidates,
        int OrdinaryGenerated,
        int StructuralGenerated,
        int AutomaticPurlin,
        int InvalidOrdinaryOwnership,
        int InvalidStructuralOwnership,
        int OrphanPhysicalTimber)
    {
        public bool IsOk =>
            InvalidOrdinaryOwnership == 0 &&
            InvalidStructuralOwnership == 0 &&
            OrphanPhysicalTimber == 0;
    }

    public static CandidateClass Classify(
        bool hasGenericTimberMetadata,
        bool hasAttachedManualOwnership,
        bool hasOrdinaryGeneratedOwnership,
        string? ordinaryOwnerReference,
        bool hasStructuralGeneratedOwnership,
        RoofStructuralRole structuralRole,
        string? structuralOwnerReference,
        bool hasAutomaticPurlinOwnership,
        string? automaticPurlinOwnerReference,
        string expectedOwnerReference)
    {
        if (!hasGenericTimberMetadata)
        {
            return CandidateClass.IgnoreNonTimber;
        }

        if (hasAttachedManualOwnership)
        {
            return CandidateClass.AttachedManual;
        }

        if (hasOrdinaryGeneratedOwnership)
        {
            return OwnerMatches(ordinaryOwnerReference, expectedOwnerReference)
                ? CandidateClass.OrdinaryGenerated
                : CandidateClass.InvalidOrdinaryOwnership;
        }

        if (hasStructuralGeneratedOwnership)
        {
            if (!RoofStructuralGeneratedLockRules.IsLockProtectedRole(structuralRole))
            {
                return CandidateClass.InvalidStructuralOwnership;
            }

            return OwnerMatches(structuralOwnerReference, expectedOwnerReference)
                ? CandidateClass.StructuralGenerated
                : CandidateClass.InvalidStructuralOwnership;
        }

        if (hasAutomaticPurlinOwnership)
        {
            return OwnerMatches(automaticPurlinOwnerReference, expectedOwnerReference)
                ? CandidateClass.AutomaticPurlin
                : CandidateClass.OrphanPhysicalTimber;
        }

        // Generic timber in the roof assembly without any generated ownership class.
        return CandidateClass.OrphanPhysicalTimber;
    }

    public static Snapshot Aggregate(IEnumerable<CandidateClass> classes)
    {
        var physical = 0;
        var ordinary = 0;
        var structural = 0;
        var purlin = 0;
        var invalidOrdinary = 0;
        var invalidStructural = 0;
        var orphan = 0;
        foreach (var classification in classes)
        {
            switch (classification)
            {
                case CandidateClass.IgnoreNonTimber:
                case CandidateClass.AttachedManual:
                    break;
                case CandidateClass.OrdinaryGenerated:
                    physical++;
                    ordinary++;
                    break;
                case CandidateClass.StructuralGenerated:
                    physical++;
                    structural++;
                    break;
                case CandidateClass.AutomaticPurlin:
                    physical++;
                    purlin++;
                    break;
                case CandidateClass.InvalidOrdinaryOwnership:
                    physical++;
                    invalidOrdinary++;
                    break;
                case CandidateClass.InvalidStructuralOwnership:
                    physical++;
                    invalidStructural++;
                    break;
                case CandidateClass.OrphanPhysicalTimber:
                    physical++;
                    orphan++;
                    break;
            }
        }

        return new Snapshot(
            physical,
            ordinary,
            structural,
            purlin,
            invalidOrdinary,
            invalidStructural,
            orphan);
    }

    private static bool OwnerMatches(string? actual, string expected) =>
        !string.IsNullOrWhiteSpace(actual) &&
        !string.IsNullOrWhiteSpace(expected) &&
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}
