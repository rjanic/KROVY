using System.Runtime.InteropServices;
using System.IO;
using AcKrovy.Cad.Abstractions.Layers;
using AcKrovy.Core.Models;
using AcKrovy.Infrastructure.Diagnostics;
using AcKrovy.Infrastructure.Settings;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.Diagnostics;

internal static class AcKrovyDiagnostics
{
    private static readonly Lazy<FileDiagnosticLogger> LoggerInstance = new(() =>
        new FileDiagnosticLogger(LogDirectory));

    private static readonly Lazy<RecoverableSettingsStore> SettingsInstance = new(() =>
        new RecoverableSettingsStore(diagnostics: Logger));

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ACAD_KROVY",
        "Logs");

    public static IDiagnosticSink Logger => LoggerInstance.Value;

    public static RecoverableSettingsStore Settings => SettingsInstance.Value;

    public static void InitializeSession(string hostName, string hostVersion)
    {
        Info(
            "PluginSession",
            $"ProductVersion={ApplicationVersionProvider.DisplayVersion}; " +
            $"MetadataSchema={TimberElementDataSchema.CurrentVersion}; " +
            $"LayerProfileSchema={ElementLayerProfile.CurrentVersion}; " +
            $"Host={hostName}; HostVersion={hostVersion}; " +
            $"Runtime={RuntimeInformation.FrameworkDescription}; " +
            $"Culture={AppLanguageService.CurrentUiCulture.Name}; " +
            $"Language={AppLanguageService.CurrentLanguageCode}");
    }

    public static void Info(string eventName, string message, string? commandName = null) =>
        Write(DiagnosticLevel.Information, eventName, message, commandName);

    public static void Warning(
        string eventName,
        string message,
        string? commandName = null,
        Exception? exception = null) =>
        Write(DiagnosticLevel.Warning, eventName, message, commandName, exception);

    public static void Error(
        string eventName,
        string message,
        string? commandName = null,
        Exception? exception = null) =>
        Write(DiagnosticLevel.Error, eventName, message, commandName, exception);

    private static void Write(
        DiagnosticLevel level,
        string eventName,
        string message,
        string? commandName = null,
        Exception? exception = null)
    {
        try
        {
            Logger.Write(new DiagnosticEvent(
                DateTimeOffset.Now,
                level,
                eventName,
                message,
                commandName,
                exception?.GetType().FullName,
                exception?.StackTrace));
        }
        catch
        {
            // Diagnostics must remain strictly best-effort.
        }
    }
}
