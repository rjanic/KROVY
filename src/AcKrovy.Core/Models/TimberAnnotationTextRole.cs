namespace AcKrovy.Core.Models;

/// <summary>
/// The three independent annotation text roles. Values are persisted-stable.
/// </summary>
public enum TimberAnnotationTextRole
{
    /// <summary>Item code / main element label, for example K1, P8, S1, W3.</summary>
    ItemCode = 0,

    /// <summary>Dimensions text, for example 80/160, 150/150, 80x160.</summary>
    Dimension = 1,

    /// <summary>Numeric slope-angle text, for example 35°.</summary>
    Slope = 2,
}
