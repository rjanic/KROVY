namespace AcKrovy.Infrastructure.Diagnostics;

public enum DiagnosticLevel
{
    Information,
    Warning,
    Error,
}

public enum SettingsConfigurationAction
{
    Missing,
    Loaded,
    Saved,
    CorruptBackupCreated,
    CorruptBackupFailed,
    SaveFailed,
}

public static class SettingsConfigurationSubject
{
    public const string ApplicationLanguage = "Application language";
    public const string SettingsUiPreferences = "Settings UI preferences";
    public const string LayerProfile = "Layer profile";
    public const string TimberDefaults = "Timber defaults";
    public const string CustomElementDefinitions = "Custom element definitions";
}

public sealed record SettingsConfigurationDetail(
    string Subject,
    SettingsConfigurationAction Action);

public sealed record DiagnosticEvent(
    DateTimeOffset Timestamp,
    DiagnosticLevel Level,
    string EventName,
    string Message,
    string? CommandName = null,
    string? ExceptionType = null,
    string? StackTrace = null)
{
    public SettingsConfigurationDetail? SettingsConfiguration { get; init; }
}
