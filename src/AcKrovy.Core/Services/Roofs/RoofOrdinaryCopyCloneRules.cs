using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Exact clone provenance and new member identity; placement is never ownership.</summary>
public static class RoofOrdinaryCopyCloneRules
{
    public static bool TryPreparePhysicalCopy(RoofOrdinaryPhysicalBuildState state, RoofSegment3D plan,
        out RoofOrdinaryPhysicalBuildState? copy)
    {
        copy = null;
        if (!RoofOrdinaryPhysicalBuildStateRules.TryRebase(state, plan, out var translated) ||
            !RoofOrdinaryPhysicalFrameRules.TryCreate(translated!, plan, state.Anchor.SourceFaceIndex, out var frame) ||
            !RoofOrdinaryHorizontalSectionFrameRules.TryCreate(frame!.LongitudinalAxis, out var section)) return false;
        // Rebase the member's full context, including cuts. The live roof never
        // projects a copied Independent back to its old position/elevation.
        copy = translated! with { SectionFrame = section };
        return true;
    }

    public static IReadOnlyDictionary<T, T> GetMappedPackage<T>(IReadOnlyCollection<T> sourcePackage,
        IReadOnlyDictionary<T, T> mapping, IReadOnlyCollection<T> preExisting, bool ownerCopied) where T : notnull
    {
        if (ownerCopied) return new Dictionary<T, T>();
        var pairs = mapping.Where(pair => sourcePackage.Contains(pair.Key) &&
            preExisting.Contains(pair.Key) && !preExisting.Contains(pair.Value)).ToArray();
        if (pairs.Select(pair => pair.Value).Distinct().Count() != pairs.Length)
            throw new ArgumentException("Ambiguous native Ordinary COPY map.", nameof(mapping));
        return pairs.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public static RoofIndependentOrdinaryTimberData CreateIdentity(
        RoofIndependentOrdinaryTimberData? source, RoofGeneratedTimberData? generated,
        string newMemberId)
    {
        if (source is not null && generated is not null ||
            source is null && generated is not { MemberKind: RoofGeneratedTimberKind.Rafter } ||
            source is not null && (!RoofIndependentOrdinaryTimberDataCodec.IsValid(source) ||
                source.EntityRole != RoofIndependentOrdinaryEntityRole.PlanLine ||
                source.IndependentMemberId == newMemberId))
            throw new ArgumentException("Invalid Ordinary COPY source.");
        var identity = new RoofIndependentOrdinaryTimberData(
            RoofIndependentOrdinaryTimberDataSchema.CurrentVersion, newMemberId,
            source is null ? RoofIndependentOrdinaryOriginKind.CopiedFromAuto :
                RoofIndependentOrdinaryOriginKind.CopiedFromIndependent,
            RoofIndependentOrdinaryEntityRole.PlanLine,
            source?.SourceRoofReference ?? generated?.RoofOwnerReference,
            source?.SourceGeneratedMemberKey ?? (generated is null ? null : RoofGeneratedMemberKey.From(generated)));
        if (!RoofIndependentOrdinaryTimberDataCodec.IsValid(identity))
            throw new ArgumentException("Invalid new Ordinary COPY identity.", nameof(newMemberId));
        return identity;
    }
}
