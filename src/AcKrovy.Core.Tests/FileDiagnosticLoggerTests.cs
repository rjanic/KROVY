using AcKrovy.Infrastructure.Diagnostics;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class FileDiagnosticLoggerTests
{
    [Fact]
    public void ParallelWrites_AreCompleteAndThreadSafe()
    {
        using var directory = new TemporaryDirectory();
        var now = new DateTimeOffset(2026, 7, 26, 22, 0, 0, TimeSpan.FromHours(2));
        var logger = new FileDiagnosticLogger(directory.Path, clock: () => now);

        Parallel.For(0, 100, index => logger.Write(Event(now, $"event-{index}")));

        var content = string.Concat(Directory.GetFiles(directory.Path).Select(File.ReadAllText));
        Assert.Equal(100, content.Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(100, logger.GetRecentEvents(100).Count);
    }

    [Fact]
    public void SizeLimit_RotatesWithoutOversizedAppend()
    {
        using var directory = new TemporaryDirectory();
        var now = new DateTimeOffset(2026, 7, 26, 22, 0, 0, TimeSpan.Zero);
        var logger = new FileDiagnosticLogger(
            directory.Path,
            maximumFileBytes: 240,
            clock: () => now);

        for (var index = 0; index < 10; index++)
        {
            logger.Write(Event(now, new string('x', 80)));
        }

        Assert.True(Directory.GetFiles(directory.Path, "*.log").Length > 1);
        Assert.All(
            Directory.GetFiles(directory.Path, "*.log"),
            path => Assert.True(new FileInfo(path).Length <= 240));
    }

    [Fact]
    public void OversizedPayload_IsTruncatedToConfiguredFileLimit()
    {
        using var directory = new TemporaryDirectory();
        var now = new DateTimeOffset(2026, 7, 26, 22, 0, 0, TimeSpan.Zero);
        var logger = new FileDiagnosticLogger(
            directory.Path,
            maximumFileBytes: 300,
            clock: () => now);

        logger.Write(Event(now, new string('x', 10_000)));

        var path = Assert.Single(Directory.GetFiles(directory.Path, "*.log"));
        Assert.True(new FileInfo(path).Length <= 300);
    }

    [Fact]
    public void Retention_RemovesExpiredLogsOnly()
    {
        using var directory = new TemporaryDirectory();
        var oldPath = System.IO.Path.Combine(directory.Path, "ACAD_KROVY-20260701.log");
        var recentPath = System.IO.Path.Combine(directory.Path, "ACAD_KROVY-20260720.log");
        File.WriteAllText(oldPath, "old");
        File.WriteAllText(recentPath, "recent");
        File.SetLastWriteTimeUtc(oldPath, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(recentPath, new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc));
        var now = new DateTimeOffset(2026, 7, 26, 22, 0, 0, TimeSpan.Zero);
        var logger = new FileDiagnosticLogger(directory.Path, retentionDays: 14, clock: () => now);

        logger.Write(Event(now, "current"));

        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(recentPath));
    }

    [Fact]
    public void FileSystemFailure_NeverEscapes()
    {
        using var directory = new TemporaryDirectory();
        var fileInsteadOfDirectory = System.IO.Path.Combine(directory.Path, "blocked");
        File.WriteAllText(fileInsteadOfDirectory, "not a directory");
        var logger = new FileDiagnosticLogger(fileInsteadOfDirectory);

        var exception = Record.Exception(() => logger.Write(Event(DateTimeOffset.Now, "failure")));

        Assert.Null(exception);
    }

    [Fact]
    public void SensitivePathsAndUserName_AreSanitized()
    {
        using var directory = new TemporaryDirectory();
        var now = DateTimeOffset.Now;
        var logger = new FileDiagnosticLogger(directory.Path, clock: () => now);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var message = $"{profile}\\Documents\\secret.dwg by {Environment.UserName}";

        logger.Write(Event(now, message));

        var content = Assert.Single(Directory.GetFiles(directory.Path)).Pipe(File.ReadAllText);
        Assert.DoesNotContain(profile, content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret.dwg", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<drawing>", content);
    }

    [Fact]
    public void ForwardSlashDrawingPath_IsSanitized()
    {
        using var directory = new TemporaryDirectory();
        var now = DateTimeOffset.Now;
        var logger = new FileDiagnosticLogger(directory.Path, clock: () => now);

        logger.Write(Event(now, "C:/Projects/Client/secret.dwg"));

        var content = Assert.Single(Directory.GetFiles(directory.Path)).Pipe(File.ReadAllText);
        Assert.DoesNotContain("secret.dwg", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<drawing>", content);
    }

    [Fact]
    public void LocalApplicationDataPath_UsesStableAnonymousToken()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var path = System.IO.Path.Combine(
            localApplicationData,
            "ACAD_KROVY",
            "Logs");

        var sanitized = DiagnosticSanitizer.Sanitize(path);

        Assert.StartsWith(
            "%LOCALAPPDATA%",
            sanitized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Environment.UserName,
            sanitized,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            System.IO.Path.Combine("ACAD_KROVY", "Logs"),
            sanitized,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StructuredSettingsDetail_DoesNotChangeRawLogContract()
    {
        using var directory = new TemporaryDirectory();
        var now = new DateTimeOffset(2026, 7, 27, 9, 30, 0, TimeSpan.FromHours(2));
        var logger = new FileDiagnosticLogger(directory.Path, clock: () => now);
        var diagnosticEvent = new DiagnosticEvent(
            now,
            DiagnosticLevel.Information,
            "SettingsConfiguration",
            "Application language: Loaded. Settings file loaded.")
        {
            SettingsConfiguration = new SettingsConfigurationDetail(
                SettingsConfigurationSubject.ApplicationLanguage,
                SettingsConfigurationAction.Loaded),
        };

        logger.Write(diagnosticEvent);

        var content = Assert.Single(Directory.GetFiles(directory.Path)).Pipe(File.ReadAllText);
        Assert.Contains(
            "message=Application language: Loaded. Settings file loaded.",
            content,
            StringComparison.Ordinal);
        Assert.Equal(7, content.TrimEnd().Split('|').Length);
        Assert.DoesNotContain(nameof(SettingsConfigurationDetail), content, StringComparison.Ordinal);
        var recent = Assert.Single(logger.GetRecentEvents(1));
        Assert.Equal(diagnosticEvent.SettingsConfiguration, recent.SettingsConfiguration);
    }

    [Fact]
    public void StructuredSettingsSubject_IsSanitizedInRecentEvents()
    {
        using var directory = new TemporaryDirectory();
        var now = DateTimeOffset.Now;
        var logger = new FileDiagnosticLogger(directory.Path, clock: () => now);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var diagnosticEvent = new DiagnosticEvent(
            now,
            DiagnosticLevel.Information,
            "SettingsConfiguration",
            "Unknown settings event.")
        {
            SettingsConfiguration = new SettingsConfigurationDetail(
                System.IO.Path.Combine(profile, "private"),
                SettingsConfigurationAction.Loaded),
        };

        logger.Write(diagnosticEvent);

        var recent = Assert.Single(logger.GetRecentEvents(1));
        Assert.DoesNotContain(
            profile,
            recent.SettingsConfiguration!.Subject,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Environment.UserName,
            recent.SettingsConfiguration.Subject,
            StringComparison.OrdinalIgnoreCase);
    }

    private static DiagnosticEvent Event(DateTimeOffset timestamp, string message) =>
        new(timestamp, DiagnosticLevel.Information, "Test", message);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "AcKrovyTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

internal static class TestPipeExtensions
{
    public static TResult Pipe<T, TResult>(this T value, Func<T, TResult> pipe) => pipe(value);
}
