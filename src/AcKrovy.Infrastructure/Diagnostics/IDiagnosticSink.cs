namespace AcKrovy.Infrastructure.Diagnostics;

public interface IDiagnosticSink
{
    void Write(DiagnosticEvent diagnosticEvent);

    IReadOnlyList<DiagnosticEvent> GetRecentEvents(int maximumCount);
}

public sealed class NullDiagnosticSink : IDiagnosticSink
{
    public static NullDiagnosticSink Instance { get; } = new();

    private NullDiagnosticSink()
    {
    }

    public void Write(DiagnosticEvent diagnosticEvent)
    {
    }

    public IReadOnlyList<DiagnosticEvent> GetRecentEvents(int maximumCount) =>
        Array.Empty<DiagnosticEvent>();
}
