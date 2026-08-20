namespace AcKrovy.Core.Models.Roofs;

/// <summary>
/// CAD-neutral reason for generated-member manual-edit classification.
/// Host diagnostics map these to stable tokens; host-only guards stay in the adapter.
/// </summary>
public enum RoofGeneratedMemberManualEditReason
{
    None = 0,
    Accepted = 1,
    NeitherEndpointChanged = 2,
    OffPlane = 3,
    InvalidLength = 4,
    NonCollinear = 5,
    BothEndpointsChanged = 6,
    BasisFailed = 7,
    ReplayFailed = 8,
    CompositionFailed = 9,
    LengthChanged = 10,
    DirectionChanged = 11,
    NotPureTranslation = 12,
    UnsupportedGrip = 13,
    UnrepresentableStretch = 14,
}
