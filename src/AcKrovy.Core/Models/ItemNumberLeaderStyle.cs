namespace AcKrovy.Core.Models;

public enum ItemNumberLeaderStyle
{
    Plain = 0,
    Circle = 1,
    Slot = 2,
    // Backward-compatible alias for development DWGs written before the
    // framed style was correctly named Slot.
    Ellipse = Slot,
    Rectangle = 3,
}
