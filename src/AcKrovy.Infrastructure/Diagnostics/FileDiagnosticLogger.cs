using System.Globalization;
using System.Text;

namespace AcKrovy.Infrastructure.Diagnostics;

public sealed class FileDiagnosticLogger : IDiagnosticSink
{
    public const long DefaultMaximumFileBytes = 5L * 1024L * 1024L;
    public const int DefaultRetentionDays = 14;

    private readonly object _sync = new();
    private readonly string _logDirectory;
    private readonly long _maximumFileBytes;
    private readonly int _retentionDays;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Queue<DiagnosticEvent> _recentEvents = new();
    private readonly int _recentEventCapacity;

    public FileDiagnosticLogger(
        string logDirectory,
        long maximumFileBytes = DefaultMaximumFileBytes,
        int retentionDays = DefaultRetentionDays,
        Func<DateTimeOffset>? clock = null,
        int recentEventCapacity = 100)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            throw new ArgumentException("A log directory is required.", nameof(logDirectory));
        }

        if (maximumFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        }

        if (retentionDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays));
        }

        if (recentEventCapacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(recentEventCapacity));
        }

        _logDirectory = logDirectory;
        _maximumFileBytes = maximumFileBytes;
        _retentionDays = retentionDays;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _recentEventCapacity = recentEventCapacity;
    }

    public string LogDirectory => _logDirectory;

    public void Write(DiagnosticEvent diagnosticEvent)
    {
        if (diagnosticEvent is null)
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                var safeEvent = Sanitize(diagnosticEvent);
                AddRecentEvent(safeEvent);
                Directory.CreateDirectory(_logDirectory);
                CleanupExpiredFiles();

                var payload = Encoding.UTF8.GetBytes(FormatLine(safeEvent));
                while (payload.Length > _maximumFileBytes &&
                       (!string.IsNullOrEmpty(safeEvent.Message) ||
                        !string.IsNullOrEmpty(safeEvent.StackTrace)))
                {
                    safeEvent = safeEvent with
                    {
                        Message = TruncateToHalf(safeEvent.Message) ?? string.Empty,
                        StackTrace = TruncateToHalf(safeEvent.StackTrace),
                    };
                    payload = Encoding.UTF8.GetBytes(FormatLine(safeEvent));
                }

                if (payload.Length > _maximumFileBytes)
                {
                    return;
                }

                var targetPath = ResolveWritablePath(safeEvent.Timestamp.LocalDateTime.Date, payload.Length);
                using var stream = new FileStream(
                    targetPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite);
                stream.Write(payload, 0, payload.Length);
            }
        }
        catch
        {
            // Diagnostics must never break plug-in initialization or a command.
        }
    }

    public IReadOnlyList<DiagnosticEvent> GetRecentEvents(int maximumCount)
    {
        if (maximumCount <= 0)
        {
            return Array.Empty<DiagnosticEvent>();
        }

        try
        {
            lock (_sync)
            {
                return _recentEvents
                    .Reverse()
                    .Take(maximumCount)
                    .Reverse()
                    .ToArray();
            }
        }
        catch
        {
            return Array.Empty<DiagnosticEvent>();
        }
    }

    private static DiagnosticEvent Sanitize(DiagnosticEvent diagnosticEvent) =>
        diagnosticEvent with
        {
            EventName = DiagnosticSanitizer.Sanitize(diagnosticEvent.EventName),
            Message = Truncate(DiagnosticSanitizer.Sanitize(diagnosticEvent.Message), 4096) ?? string.Empty,
            CommandName = DiagnosticSanitizer.Sanitize(diagnosticEvent.CommandName),
            ExceptionType = DiagnosticSanitizer.Sanitize(diagnosticEvent.ExceptionType),
            StackTrace = Truncate(DiagnosticSanitizer.Sanitize(diagnosticEvent.StackTrace), 65_536),
            SettingsConfiguration = diagnosticEvent.SettingsConfiguration is { } settings
                ? settings with
                {
                    Subject = DiagnosticSanitizer.Sanitize(settings.Subject),
                }
                : null,
        };

    private void AddRecentEvent(DiagnosticEvent diagnosticEvent)
    {
        _recentEvents.Enqueue(diagnosticEvent);
        while (_recentEvents.Count > _recentEventCapacity)
        {
            _recentEvents.Dequeue();
        }
    }

    private string ResolveWritablePath(DateTime date, int payloadLength)
    {
        var dateToken = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        for (var index = 0; index < 10_000; index++)
        {
            var suffix = index == 0 ? string.Empty : $".{index}";
            var path = Path.Combine(_logDirectory, $"ACAD_KROVY-{dateToken}{suffix}.log");
            if (!File.Exists(path) || new FileInfo(path).Length + payloadLength <= _maximumFileBytes)
            {
                return path;
            }
        }

        throw new IOException("No diagnostic log rotation slot is available.");
    }

    private void CleanupExpiredFiles()
    {
        var cutoffUtc = _clock().UtcDateTime.AddDays(-_retentionDays);
        foreach (var path in Directory.EnumerateFiles(_logDirectory, "ACAD_KROVY-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoffUtc)
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // One inaccessible log must not stop cleanup or logging.
            }
        }
    }

    private static string FormatLine(DiagnosticEvent diagnosticEvent)
    {
        var fields = new[]
        {
            diagnosticEvent.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            diagnosticEvent.Level.ToString(),
            diagnosticEvent.EventName,
            $"command={diagnosticEvent.CommandName}",
            $"message={diagnosticEvent.Message}",
            $"exception={diagnosticEvent.ExceptionType}",
            $"stack={diagnosticEvent.StackTrace}",
        };

        return string.Join("|", fields.Select(Escape)) + "\r\n";
    }

    private static string Escape(string? value) =>
        (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("|", "\\|")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");

    private static string? Truncate(string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength
            ? value
            : value.Substring(0, maximumLength);

    private static string? TruncateToHalf(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value!.Substring(0, value.Length / 2);
    }
}
