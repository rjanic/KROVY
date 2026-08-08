namespace AcKrovy.Core.Models;

/// <summary>
/// Parsed production R3 BlockContent variant identity.
/// Combined always carries a content-side token (RIGHT/LEFT).
/// </summary>
public readonly struct TimberFramedBlockContentR3VariantParse
{
    public TimberFramedBlockContentR3VariantParse(
        bool isCombined,
        bool isItemOnly,
        TimberFramedBlockContentDimensionColumnSide? contentVariantSide)
    {
        IsCombined = isCombined;
        IsItemOnly = isItemOnly;
        ContentVariantSide = contentVariantSide;
    }

    public bool IsCombined { get; }

    public bool IsItemOnly { get; }

    /// <summary>
    /// Combined RIGHT/LEFT column layout. Null for ItemOnly or legacy
    /// side-agnostic R3 Combined names that lack a content-variant token.
    /// </summary>
    public TimberFramedBlockContentDimensionColumnSide? ContentVariantSide { get; }

    public bool IsProductionCombinedTarget =>
        IsCombined && ContentVariantSide is not null;
}
