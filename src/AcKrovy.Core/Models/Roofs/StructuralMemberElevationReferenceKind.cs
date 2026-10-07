namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// User-visible elevation reference datum selector.
/// Switching the datum converts the displayed values for the same physical geometry —
/// it does NOT move the physical member. Only a geometry-affecting edit (lower Z, upper Z,
/// or slope change) moves the physical member.
/// SH = Spodná hrana (lower face).
/// OS = Os (neutral axis / centerline).
/// VH = Vrchná hrana (upper face).
/// </summary>
public enum StructuralMemberElevationReferenceKind
{
    /// <summary>Spodná hrana — elevation of the lower face.</summary>
    SH = 1,

    /// <summary>Os — elevation of the neutral axis (center of section).</summary>
    OS = 2,

    /// <summary>Vrchná hrana — elevation of the upper face.</summary>
    VH = 3,
}
