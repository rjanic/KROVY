using AcKrovy.Infrastructure.Diagnostics;
using AcKrovy.Infrastructure.Settings;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RecoverableSettingsStoreTests
{
    [Fact]
    public void ValidFile_LoadsWithoutBackup()
    {
        var fileSystem = new FakeFileSystem("{\"value\":\"ok\"}");
        var store = Store(fileSystem);

        var result = store.Load(
            fileSystem.Path,
            "test",
            json => json.Contains("ok", StringComparison.Ordinal) ? new Setting("ok") : null,
            () => new Setting("default"));

        Assert.Equal("ok", result.Value.Value);
        Assert.Equal(SettingsFileState.Loaded, result.Status.State);
        Assert.Equal(0, fileSystem.MoveCalls);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("")]
    public void InvalidOrEmptyFile_IsMovedToTimestampedCorruptBackup(string content)
    {
        var fileSystem = new FakeFileSystem(content);
        var store = Store(fileSystem);

        var result = store.Load(
            fileSystem.Path,
            "test",
            _ => throw new FormatException(),
            () => new Setting("default"));

        Assert.Equal("default", result.Value.Value);
        Assert.Equal(SettingsFileState.CorruptBackupCreated, result.Status.State);
        Assert.Equal("settings.corrupt.20260726-221500.json", result.Status.BackupFileName);
        Assert.False(fileSystem.FileExists(fileSystem.Path));
        Assert.True(fileSystem.FileExists(
            @"C:\settings\settings.corrupt.20260726-221500.json"));
    }

    [Fact]
    public void UnreadableFile_WithBackupFailure_UsesMemoryDefaultsAndBlocksSave()
    {
        var fileSystem = new FakeFileSystem("{bad}")
        {
            ReadFailure = new UnauthorizedAccessException(),
            MoveFailure = new IOException("locked"),
        };
        var store = Store(fileSystem);

        var result = store.Load(
            fileSystem.Path,
            "test",
            _ => new Setting("never"),
            () => new Setting("default"));

        Assert.Equal(SettingsFileState.CorruptBackupFailed, result.Status.State);
        Assert.True(fileSystem.FileExists(fileSystem.Path));
        Assert.Throws<SettingsWriteBlockedException>(() =>
            store.Save(fileSystem.Path, "test", "{\"value\":\"new\"}"));
        Assert.Equal("{bad}", fileSystem.Content(fileSystem.Path));
    }

    [Fact]
    public void UnreadableFile_WithSuccessfulBackup_UsesMemoryDefaultsAndPreservesBackup()
    {
        var fileSystem = new FakeFileSystem("{unreadable}")
        {
            ReadFailure = new UnauthorizedAccessException(),
        };
        var store = Store(fileSystem);

        var result = store.Load(
            fileSystem.Path,
            "test",
            _ => new Setting("never"),
            () => new Setting("default"));

        Assert.Equal("default", result.Value.Value);
        Assert.Equal(SettingsFileState.CorruptBackupCreated, result.Status.State);
        Assert.False(fileSystem.FileExists(fileSystem.Path));
        Assert.Equal(
            "{unreadable}",
            fileSystem.Content(@"C:\settings\settings.corrupt.20260726-221500.json"));
    }

    [Fact]
    public void RepeatedLoadOfSameBlockedCorruptFile_DoesNotCreateBackupStorm()
    {
        var fileSystem = new FakeFileSystem("{bad}")
        {
            MoveFailure = new IOException("locked"),
        };
        var store = Store(fileSystem);

        _ = store.Load(fileSystem.Path, "test", _ => throw new FormatException(), () => new Setting("default"));
        _ = store.Load(fileSystem.Path, "test", _ => throw new FormatException(), () => new Setting("default"));

        Assert.Equal(1, fileSystem.MoveCalls);
    }

    [Fact]
    public void SuccessfulBackup_AllowsLaterValidSave()
    {
        var fileSystem = new FakeFileSystem("{bad}");
        var store = Store(fileSystem);

        _ = store.Load(fileSystem.Path, "test", _ => throw new FormatException(), () => new Setting("default"));
        store.Save(fileSystem.Path, "test", "{\"value\":\"new\"}");

        Assert.Equal("{\"value\":\"new\"}", fileSystem.Content(fileSystem.Path));
        Assert.Equal(SettingsFileState.Loaded, Assert.Single(store.GetStatuses()).State);
    }

    [Fact]
    public void Diagnostics_DistinguishLoadFromSave()
    {
        var fileSystem = new FakeFileSystem("{\"value\":\"ok\"}");
        var diagnostics = new CapturingDiagnosticSink();
        var store = new RecoverableSettingsStore(
            fileSystem,
            diagnostics,
            () => new DateTimeOffset(
                2026,
                7,
                27,
                8,
                0,
                0,
                TimeSpan.FromHours(2)));

        _ = store.Load(
            fileSystem.Path,
            SettingsConfigurationSubject.ApplicationLanguage,
            json => json.Contains("ok", StringComparison.Ordinal)
                ? new Setting("ok")
                : null,
            () => new Setting("default"));

        var loaded = Assert.Single(diagnostics.Events);
        Assert.Contains("Settings file loaded.", loaded.Message);
        Assert.DoesNotContain("Settings file saved.", loaded.Message);
        Assert.Equal(
            new SettingsConfigurationDetail(
                SettingsConfigurationSubject.ApplicationLanguage,
                SettingsConfigurationAction.Loaded),
            loaded.SettingsConfiguration);

        store.Save(
            fileSystem.Path,
            SettingsConfigurationSubject.ApplicationLanguage,
            "{\"value\":\"new\"}");

        var saved = Assert.Single(diagnostics.Events.Skip(1));
        Assert.Contains("Settings file saved.", saved.Message);
        Assert.DoesNotContain("Settings file loaded.", saved.Message);
        Assert.Equal(
            new SettingsConfigurationDetail(
                SettingsConfigurationSubject.ApplicationLanguage,
                SettingsConfigurationAction.Saved),
            saved.SettingsConfiguration);
    }

    private static RecoverableSettingsStore Store(FakeFileSystem fileSystem) =>
        new(
            fileSystem,
            NullDiagnosticSink.Instance,
            () => new DateTimeOffset(2026, 7, 26, 22, 15, 0, TimeSpan.FromHours(2)));

    private sealed record Setting(string Value);

    private sealed class CapturingDiagnosticSink : IDiagnosticSink
    {
        public List<DiagnosticEvent> Events { get; } = [];

        public void Write(DiagnosticEvent diagnosticEvent) =>
            Events.Add(diagnosticEvent);

        public IReadOnlyList<DiagnosticEvent> GetRecentEvents(int maximumCount) =>
            Events.TakeLast(maximumCount).ToArray();
    }

    private sealed class FakeFileSystem : ISettingsFileSystem
    {
        private readonly Dictionary<string, string> _files =
            new(StringComparer.OrdinalIgnoreCase);

        public FakeFileSystem(string content)
        {
            _files[Path] = content;
        }

        public string Path => @"C:\settings\settings.json";
        public Exception? ReadFailure { get; init; }
        public Exception? MoveFailure { get; init; }
        public int MoveCalls { get; private set; }

        public bool FileExists(string path) => _files.ContainsKey(path);

        public string ReadAllText(string path)
        {
            if (ReadFailure is not null)
            {
                throw ReadFailure;
            }

            return _files[path];
        }

        public void WriteAllText(string path, string content) => _files[path] = content;

        public void CreateDirectory(string path)
        {
        }

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            MoveCalls++;
            if (MoveFailure is not null)
            {
                throw MoveFailure;
            }

            if (!overwrite && _files.ContainsKey(destinationPath))
            {
                throw new IOException();
            }

            _files[destinationPath] = _files[sourcePath];
            _files.Remove(sourcePath);
        }

        public long GetFileLength(string path) => _files[path].Length;

        public DateTime GetLastWriteTimeUtc(string path) => new(2026, 7, 26, 20, 0, 0, DateTimeKind.Utc);

        public string Content(string path) => _files[path];
    }
}
