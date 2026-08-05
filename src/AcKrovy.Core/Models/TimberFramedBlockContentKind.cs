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
