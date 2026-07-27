using AcKrovy.Infrastructure.Diagnostics;

namespace AcKrovy.Infrastructure.Settings;

public enum SettingsFileState
{
    Missing,
    Loaded,
    CorruptBackupCreated,
    CorruptBackupFailed,
    SaveFailed,
}

public sealed record SettingsFileStatus(
    string LogicalName,
    string FileName,
    SettingsFileState State,
    DateTimeOffset Timestamp,
    string? BackupFileName = null);

public sealed record RecoverableSettingsLoadResult<T>(
    T Value,
    SettingsFileStatus Status);

public sealed class SettingsWriteBlockedException : IOException
{
    public SettingsWriteBlockedException(string fileName)
        : base($"The corrupt settings file '{fileName}' could not be backed up and must not be overwritten.")
    {
    }
}

public sealed class RecoverableSettingsStore
{
    private readonly object _sync = new();
    private readonly ISettingsFileSystem _fileSystem;
    private readonly IDiagnosticSink _diagnostics;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, TrackedState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    public RecoverableSettingsStore(
        ISettingsFileSystem? fileSystem = null,
        IDiagnosticSink? diagnostics = null,
        Func<DateTimeOffset>? clock = null)
    {
        _fileSystem = fileSystem ?? new PhysicalSettingsFileSystem();
        _diagnostics = diagnostics ?? NullDiagnosticSink.Instance;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public RecoverableSettingsLoadResult<T> Load<T>(
        string path,
        string logicalName,
        Func<string, T?> deserialize,
        Func<T> createDefault)
        where T : class
    {
        if (deserialize is null)
        {
            throw new ArgumentNullException(nameof(deserialize));
        }

        if (createDefault is null)
        {
            throw new ArgumentNullException(nameof(createDefault));
        }
        ValidatePathAndName(path, logicalName);

        lock (_sync)
        {
            if (!_fileSystem.FileExists(path))
            {
                var missing = Register(path, logicalName, SettingsFileState.Missing);
                LogStatus(
                    missing,
                    DiagnosticLevel.Information,
                    SettingsConfigurationAction.Missing,
                    "Settings file is missing; defaults are active.");
                return new RecoverableSettingsLoadResult<T>(createDefault(), missing);
            }

            var fingerprint = TryGetFingerprint(path);
            if (_states.TryGetValue(path, out var tracked) &&
                tracked.Status.State == SettingsFileState.CorruptBackupFailed &&
                tracked.Fingerprint == fingerprint)
            {
                return new RecoverableSettingsLoadResult<T>(createDefault(), tracked.Status);
            }

            try
            {
                var value = deserialize(_fileSystem.ReadAllText(path));
                if (value is null)
                {
                    throw new InvalidDataException("Settings deserializer returned no value.");
                }

                var loaded = Register(path, logicalName, SettingsFileState.Loaded, fingerprint: fingerprint);
                LogStatus(
                    loaded,
                    DiagnosticLevel.Information,
                    SettingsConfigurationAction.Loaded,
                    "Settings file loaded.");
                return new RecoverableSettingsLoadResult<T>(value, loaded);
            }
            catch (Exception exception)
            {
                return Recover(path, logicalName, fingerprint, createDefault, exception);
            }
        }
    }

    public void Save(
        string path,
        string logicalName,
        string content)
    {
        ValidatePathAndName(path, logicalName);
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        lock (_sync)
        {
            if (_states.TryGetValue(path, out var tracked) &&
                tracked.Status.State == SettingsFileState.CorruptBackupFailed)
            {
                throw new SettingsWriteBlockedException(Path.GetFileName(path));
            }

            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("Settings path must have a directory.");
            }

            var temporaryPath = path + ".tmp";
            try
            {
                _fileSystem.CreateDirectory(directory);
                _fileSystem.WriteAllText(temporaryPath, content);
                _fileSystem.MoveFile(temporaryPath, path, overwrite: true);
                var status = Register(
                    path,
                    logicalName,
                    SettingsFileState.Loaded,
                    fingerprint: TryGetFingerprint(path));
                LogStatus(
                    status,
                    DiagnosticLevel.Information,
                    SettingsConfigurationAction.Saved,
                    "Settings file saved.");
            }
            catch (Exception exception)
            {
                var failed = Register(path, logicalName, SettingsFileState.SaveFailed);
                LogStatus(
                    failed,
                    DiagnosticLevel.Error,
                    SettingsConfigurationAction.SaveFailed,
                    "Settings file save failed.",
                    exception);
                throw;
            }
        }
    }

    public IReadOnlyList<SettingsFileStatus> GetStatuses()
    {
        lock (_sync)
        {
            return _states.Values
                .Select(value => value.Status)
                .OrderBy(status => status.LogicalName, StringComparer.Ordinal)
                .ToArray();
        }
    }

    private RecoverableSettingsLoadResult<T> Recover<T>(
        string path,
        string logicalName,
        FileFingerprint? fingerprint,
        Func<T> createDefault,
        Exception loadException)
        where T : class
    {
        var backupPath = CreateBackupPath(path);
        try
        {
            _fileSystem.MoveFile(path, backupPath, overwrite: false);
            var recovered = Register(
                path,
                logicalName,
                SettingsFileState.CorruptBackupCreated,
                Path.GetFileName(backupPath));
            LogStatus(
                recovered,
                DiagnosticLevel.Warning,
                SettingsConfigurationAction.CorruptBackupCreated,
                "Corrupt settings were preserved and defaults are active.",
                loadException);
            return new RecoverableSettingsLoadResult<T>(createDefault(), recovered);
        }
        catch (Exception backupException)
        {
            var failed = Register(
                path,
                logicalName,
                SettingsFileState.CorruptBackupFailed,
                fingerprint: fingerprint);
            LogStatus(
                failed,
                DiagnosticLevel.Error,
                SettingsConfigurationAction.CorruptBackupFailed,
                $"Corrupt settings backup failed after {loadException.GetType().Name}. Defaults are memory-only.",
                backupException);
            return new RecoverableSettingsLoadResult<T>(createDefault(), failed);
        }
    }

    private string CreateBackupPath(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var timestamp = _clock().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var candidate = Path.Combine(directory, $"{baseName}.corrupt.{timestamp}{extension}");
        for (var index = 1; _fileSystem.FileExists(candidate); index++)
        {
            candidate = Path.Combine(directory, $"{baseName}.corrupt.{timestamp}-{index:00}{extension}");
        }

        return candidate;
    }

    private SettingsFileStatus Register(
        string path,
        string logicalName,
        SettingsFileState state,
        string? backupFileName = null,
        FileFingerprint? fingerprint = null)
    {
        var status = new SettingsFileStatus(
            logicalName,
            Path.GetFileName(path),
            state,
            _clock(),
            backupFileName);
        _states[path] = new TrackedState(status, fingerprint);
        return status;
    }

    private FileFingerprint? TryGetFingerprint(string path)
    {
        try
        {
            return new FileFingerprint(
                _fileSystem.GetFileLength(path),
                _fileSystem.GetLastWriteTimeUtc(path));
        }
        catch
        {
            return null;
        }
    }

    private void LogStatus(
        SettingsFileStatus status,
        DiagnosticLevel level,
        SettingsConfigurationAction action,
        string message,
        Exception? exception = null)
    {
        _diagnostics.Write(new DiagnosticEvent(
            status.Timestamp,
            level,
            "SettingsConfiguration",
            $"{status.LogicalName}: {status.State}. {message}",
            ExceptionType: exception?.GetType().FullName,
            StackTrace: exception?.StackTrace)
        {
            SettingsConfiguration = new SettingsConfigurationDetail(
                status.LogicalName,
                action),
        });
    }

    private static void ValidatePathAndName(string path, string logicalName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A settings path is required.", nameof(path));
        }

        if (string.IsNullOrWhiteSpace(logicalName))
        {
            throw new ArgumentException("A logical settings name is required.", nameof(logicalName));
        }
    }

    private sealed record TrackedState(
        SettingsFileStatus Status,
        FileFingerprint? Fingerprint);

    private sealed record FileFingerprint(long Length, DateTime LastWriteTimeUtc);
}
