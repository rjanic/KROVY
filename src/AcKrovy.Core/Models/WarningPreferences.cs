namespace AcKrovy.Core.Models;

/// <summary>Application-wide UX preferences; never grant geometry authority.</summary>
public sealed record WarningPreferences
{
    public bool ConfirmAutomaticMemberDetach { get; init; } = true;
    public bool WarnDerived3DEdit { get; init; } = true;
}
