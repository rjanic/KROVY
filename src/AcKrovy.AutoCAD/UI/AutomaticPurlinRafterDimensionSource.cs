namespace AcKrovy.AutoCAD.UI;

/// <summary>
/// Where the dialog's current rafter width/height came from.
/// Profile seed is never treated as a measured physical rafter.
/// </summary>
internal enum AutomaticPurlinRafterDimensionSource
{
    /// <summary>No ordinary rafters on the roof; values are profile defaults only.</summary>
    UnresolvedProfileSeed = 0,

    /// <summary>Recovered from generated ordinary rafters owned by the roof.</summary>
    RecoveredRoofRecipe = 1,

    /// <summary>User picked a CAD rafter entity as dimension source.</summary>
    SelectedRafter = 2,

    /// <summary>User explicitly entered width/height in the manual dialog.</summary>
    ExplicitManual = 3,
}
