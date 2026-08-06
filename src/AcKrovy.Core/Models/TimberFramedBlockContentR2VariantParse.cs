namespace AcKrovy.Core.Models;

/// <summary>
/// CAD-neutral parse of a P3 R2 BlockContent variant key / safe block name.
/// </summary>
public readonly struct TimberFramedBlockContentR2VariantParse
{
    public TimberFramedBlockContentR2VariantParse(
        bool isCombined,
        bool isItemOnly,
        TimberFramedBlockContentDimensionColumnSide? dimensionColumnSide)
    {
        IsCombined = isCombined;
        IsItemOnly = isItemOnly;
        DimensionColumnSide = dimensionColumnSide;
    }

    public bool IsCombined { get; }

    public bool IsItemOnly { get; }

    public TimberFramedBlockContentDimensionColumnSide? DimensionColumnSide { get; }

    public bool IsP3R2CombinedTarget =>
        IsCombined &&
        !IsItemOnly &&
        DimensionColumnSide is not null;
}
