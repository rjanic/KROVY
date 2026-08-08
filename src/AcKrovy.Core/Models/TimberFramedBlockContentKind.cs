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
/// Enum names are literal bake signs: NegativeLocalX → −offset,
/// PositiveLocalX → +offset. Production R3 tokens R3_RIGHT / R3_LEFT are
/// compatibility labels for knee-side layouts (see
/// <see cref="Services.TimberFramedCombinedG5ContentVariantRules"/>); they do
/// not mean world/source Left/Right. Text is never mirrored.
/// </summary>
public enum TimberFramedBlockContentDimensionColumnSide
{
    /// <summary>
    /// AttrDef column X = −offset. R3_RIGHT PASS token (knee on −local X of frame).
    /// </summary>
    NegativeLocalX = 0,

    /// <summary>
    /// AttrDef column X = +offset. R3_LEFT token (knee on +local X of frame).
    /// </summary>
    PositiveLocalX = 1,
}
