namespace AcKrovy.Core.Models.Roofs;

/// <summary>Stable semantic role of one line in a roof-kind-specific display topology.</summary>
public enum RoofDisplayEdgeRole
{
    Ridge = 0,
    Eave0 = 1,
    Eave1 = 2,
    GableSlope00 = 3,
    GableSlope01 = 4,
    GableSlope10 = 5,
    GableSlope11 = 6,
    MonopitchLowEave = 7,
    MonopitchHighEave = 8,
    MonopitchSlopeSide0 = 9,
    MonopitchSlopeSide1 = 10,
    MonopitchDirection = 11,
    MonopitchDirectionWing0 = 12,
    MonopitchDirectionWing1 = 13,
}
