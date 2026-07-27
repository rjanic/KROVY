using System.Globalization;
using AcKrovy.Infrastructure.Diagnostics;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.Diagnostics;

internal static class DiagnosticsRecentEventFormatter
{
    private const int MaximumDetailLength = 160;

    public static string Format(
        DiagnosticEvent diagnosticEvent,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        ArgumentNullException.ThrowIfNull(culture);

        var levelKey = diagnosticEvent.Level switch
        {
            DiagnosticLevel.Information => "DiagnosticsLevel_Information",
            DiagnosticLevel.Warning => "DiagnosticsLevel_Warning",
            DiagnosticLevel.Error => "DiagnosticsLevel_Error",
            _ => "Common_Unknown",
        };
        var detail = diagnosticEvent.EventName switch
        {
            "CommandStarted" or "CommandCompleted"
                when !string.IsNullOrWhiteSpace(diagnosticEvent.CommandName) =>
                diagnosticEvent.CommandName,
            "SettingsConfiguration" =>
                FormatSettingsConfiguration(diagnosticEvent.SettingsConfiguration, culture),
            _ => null,
        };
        var safeDetail = FormatDetail(detail);
        return $"{diagnosticEvent.Timestamp:HH:mm:ss} " +
            $"[{UiStrings.GetString(levelKey, culture)}] {diagnosticEvent.EventName}" +
            (safeDetail.Length == 0 ? string.Empty : $" — {safeDetail}");
    }

    private static string? FormatSettingsConfiguration(
        SettingsConfigurationDetail? detail,
        CultureInfo culture)
    {
        if (detail is null)
        {
            return null;
        }

        var subjectKey = detail.Subject switch
        {
            SettingsConfigurationSubject.ApplicationLanguage =>
                "DiagnosticsEvent_SubjectApplicationLanguage",
            SettingsConfigurationSubject.SettingsUiPreferences =>
                "DiagnosticsEvent_SubjectSettingsUiPreferences",
            SettingsConfigurationSubject.LayerProfile =>
                "DiagnosticsEvent_SubjectLayerProfile",
            SettingsConfigurationSubject.TimberDefaults =>
                "DiagnosticsEvent_SubjectTimberDefaults",
            SettingsConfigurationSubject.CustomElementDefinitions =>
                "DiagnosticsEvent_SubjectCustomElementDefinitions",
            _ => null,
        };
        var subject = subjectKey is null
            ? FormatDetail(detail.Subject)
            : UiStrings.GetString(subjectKey, culture);
        var actionKey = detail.Action switch
        {
            SettingsConfigurationAction.Missing => "DiagnosticsWindow_StateMissing",
            SettingsConfigurationAction.Loaded => "DiagnosticsWindow_StateLoaded",
            SettingsConfigurationAction.Saved => "DiagnosticsEvent_ActionSaved",
            SettingsConfigurationAction.CorruptBackupCreated => "DiagnosticsWindow_StateRecovered",
            SettingsConfigurationAction.CorruptBackupFailed => "DiagnosticsWindow_StateMemoryOnly",
            SettingsConfigurationAction.SaveFailed => "DiagnosticsWindow_StateSaveFailed",
            _ => "Common_Unknown",
        };

        return $"{subject}: {UiStrings.GetString(actionKey, culture)}";
    }

    private static string FormatDetail(string? detail)
    {
        var sanitized = DiagnosticSanitizer
            .Sanitize(detail)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return sanitized.Length <= MaximumDetailLength
            ? sanitized
            : sanitized.Substring(0, MaximumDetailLength - 3) + "...";
    }
}
