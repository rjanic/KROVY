namespace AcKrovy.Core.Models;

/// <summary>
/// G5 one-MLeader content geometry kind.
/// Separate from <see cref="ItemNumberLeaderStyle"/> because that enum's
/// <see cref="ItemNumberLeaderStyle.Plain"/> means a native MText-content
/// leader, while G5 Plain means BlockContent without frame geometry.
/// </summary>
public enum TimberFramedBlockContentKind
{
    /// <summary>BlockContent with ITEM_NO/WIDTH/HEIGHT AttrDefs and no frame entity.</summary>
    Plain = 0,

    Circle = 1,
    Rectangle = 2,
    Slot = 3,
}

/// <summary>
/// Distinguishes shared BTR families: item-only (ITEM_NO) vs combined
/// (ITEM_NO + WIDTH + HEIGHT).
/// </summary>
public enum TimberFramedBlockContentPresentation
{
    ItemOnly = 0,
    Combined = 1,
}

/// <summary>
/// Combined WIDTH/HEIGHT AttrDef column side in block-local X.
/// Not screen Left/Right and not leader knee Side — only which side of the
/// frame the dimension column occupies so it stays toward the knee.
/// </summary>
public enum TimberFramedBlockContentDimensionColumnSide
{
    /// <summary>
    /// WIDTH/HEIGHT at negative local X (content right of knee / +local dogleg).
    /// </summary>
    NegativeLocalX = 0,

    /// <summary>
    /// WIDTH/HEIGHT at positive local X (content left of knee / −local dogleg).
    /// </summary>
    PositiveLocalX = 1,
}
