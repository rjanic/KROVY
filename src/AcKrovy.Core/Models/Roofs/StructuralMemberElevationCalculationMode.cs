namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// Controls which two of the three elevation fields (lower Z, upper Z, slope) are
/// independently editable. The third is always derived.
///
/// LowerUpper: Z spodný + Z vrchný are editable; slope is calculated.
/// LowerSlope:  Z spodný + Sklon are editable; Z vrchný is calculated.
/// UpperSlope:  Z vrchný + Sklon are editable; Z spodný is calculated.
///
/// Switching calculation mode without changing the physical geometry values
/// must NOT detach an AUTO member and must NOT trigger any confirmation.
/// </summary>
public enum StructuralMemberElevationCalculationMode
{
    /// <summary>Z spodný + Z vrchný are editable; slope is derived.</summary>
    LowerUpper = 1,

    /// <summary>Z spodný + Sklon are editable; Z vrchný is derived.</summary>
    LowerSlope = 2,

    /// <summary>Z vrchný + Sklon are editable; Z spodný is derived.</summary>
    UpperSlope = 3,
}
