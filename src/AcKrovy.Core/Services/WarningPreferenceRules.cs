using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public sealed record WarningDialogResponse(bool Accepted, bool DoNotShowAgain);

public sealed record MemberWarningDecision(bool Accepted, bool ConfirmationShown,
    bool AutomaticConfirm, bool WarningPreferenceChanged, WarningPreferences Preferences);

public static class WarningPreferenceRules
{
    public static MemberWarningDecision ConfirmDetach(WarningPreferences preferences,
        bool hasAutomaticMember, Func<WarningDialogResponse> show)
    {
        if (!hasAutomaticMember) return new(true, false, false, false, preferences);
        if (!preferences.ConfirmAutomaticMemberDetach) return new(true, false, true, false, preferences);
        var response = show();
        var next = response.Accepted && response.DoNotShowAgain
            ? preferences with { ConfirmAutomaticMemberDetach = false } : preferences;
        return new(response.Accepted, true, false, next != preferences, next);
    }

    public static MemberWarningDecision RejectDerived3DEdit(WarningPreferences preferences,
        Func<bool> show)
    {
        var next = preferences.WarnDerived3DEdit && show()
            ? preferences with { WarnDerived3DEdit = false } : preferences;
        // Suppressing the warning NEVER accepts a derived-geometry edit.
        return new(false, preferences.WarnDerived3DEdit, false, next != preferences, next);
    }

    public static WarningPreferences RestoreWarnings() => new();
}
