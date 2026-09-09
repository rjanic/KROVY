namespace AcKrovy.Core.Services.Roofs;

/// <summary>
/// Pre-command classification of an entity that may be erased during a native ERASE.
/// Resolved from live metadata before the command mutates the database.
/// </summary>
public enum RoofEraseMappedKind
{
    Unknown = 0,
    Source = 1,
    Display = 2,
    GeneratedTimber = 3,
    GeneratedAnnotation = 4
}
