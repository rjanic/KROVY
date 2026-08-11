namespace AcKrovy.Core.Models;

/// <summary>
/// Parsed production R3 BlockContent variant identity.
/// Combined always carries a content-side token (RIGHT/LEFT).
/// Content kind is CIR / REC / SLT / PLAIN when the BTR name still carries it.
/// </summary>
public readonly struct TimberFramedBlockContentR3VariantParse
{
    public TimberFramedBlockContentR3VariantParse(
        bool isCombined,
        bool isItemOnly,
        TimberFramedBlockContentDimensionColumnSide? contentVariantSide,
        TimberFramedBlockContentKind? contentKind = null)
    {
        IsCombined = isCombined;
        IsItemOnly = isItemOnly;
        ContentVariantSide = contentVariantSide;
        ContentKind = contentKind;
    }

    public bool IsCombined { get; }

    public bool IsItemOnly { get; }

    /// <summary>
    /// Combined RIGHT/LEFT column layout. Null for ItemOnly or legacy
    /// side-agnostic R3 Combined names that lack a content-variant token.
    /// </summary>
    public TimberFramedBlockContentDimensionColumnSide? ContentVariantSide { get; }

    /// <summary>
    /// Frame content kind encoded in the BTR name. Null when the kind token is
    /// missing or ambiguous (fail closed for identity matching).
    /// </summary>
    public TimberFramedBlockContentKind? ContentKind { get; }

    public bool IsProductionCombinedTarget =>
        IsCombined && ContentVariantSide is not null;

    /// <summary>
    /// Production Combined identity with both kind and RIGHT/LEFT side.
    /// </summary>
    public bool IsProductionCombinedContentIdentity =>
        IsProductionCombinedTarget && ContentKind is not null;
}
