namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Explicit roof timber child role. Generated members carry a recipe key;
/// AttachedManual members belong to a roof without a generated station key.
/// </summary>
public enum RoofTimberChildRole
{
    Generated = 0,
    AttachedManual = 1,
    Standalone = 2,
}
