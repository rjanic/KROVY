namespace AcKrovy.Core.Models;

public sealed record TimberElementSimilarityCriteria
{
    public bool MatchElementType { get; init; } = true;
    public bool MatchCrossSection { get; init; } = true;
    public bool MatchMaterial { get; init; } = true;
    public bool MatchElementId { get; init; }
    public bool MatchCuttingLength { get; init; }
    public bool MatchCustomElementTypeId { get; init; }
    public double CuttingLengthToleranceMm { get; init; } = 1d;

    public static TimberElementSimilarityCriteria CreateDefault(TimberElementSnapshot seed)
    {
        if (seed?.Data is null)
        {
            throw new ArgumentNullException(nameof(seed));
        }

        return new TimberElementSimilarityCriteria
        {
            MatchCustomElementTypeId = seed.Data.ElementType == TimberElementType.Custom,
        };
    }
}
