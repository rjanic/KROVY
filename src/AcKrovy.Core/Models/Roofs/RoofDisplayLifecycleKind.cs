namespace AcKrovy.Core.Models.Roofs;

/// <summary>Host-neutral lifecycle state for one selected roof's display and UX group.</summary>
public enum RoofDisplayLifecycleKind
{
    Current = 0,
    GroupMissingRehydratable = 1,
    MissingDisplay = 2,
    StaleDisplay = 3,
    UnsupportedFutureSchema = 4,
}
