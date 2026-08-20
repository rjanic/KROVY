namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Roof-level permission for controlled manual edits of generated timber.
/// Does not disable source-roof validation in either state.
/// </summary>
public enum RoofEditState
{
    Locked = 0,
    Unlocked = 1,
}
