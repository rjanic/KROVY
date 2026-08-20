namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// How an AttachedManual roof timber child was created. COPY clones follow their
/// generated anchor during source SupportedResize (like an edited Generated member);
/// split/BREAK fragments keep the legacy keep-in-place resize rule.
/// </summary>
public enum RoofAttachedManualOrigin
{
    Split = 0,
    Copy = 1,
}
