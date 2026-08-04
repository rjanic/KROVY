#if DEBUG
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Host-facing STEP diagnostics for G4 Case F proof. Enabled only while a
/// proof CREATE/VERIFY writer is attached; production calls are no-ops.
/// </summary>
internal static class AutoCadFramedG4HostDiagnostics
{
    private static Action<string>? _writer;

    public static bool IsEnabled => _writer is not null;

    public static IDisposable Attach(Action<string> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        return new DetachScope();
    }

    public static void Step(string step, string detail) =>
        Write($"STEP {step}: {detail}");

    public static void Outcome(string outcome, string detail) =>
        Write($"OUTCOME {outcome}: {detail}");

    public static void Fail(
        string step,
        string condition,
        Exception? exception = null,
        ObjectId? sourceId = null,
        ObjectId? leaderId = null,
        ObjectId? frameId = null,
        ObjectId? itemTextId = null,
        string? frameBlockName = null,
        ObjectId? frameBlockId = null,
        ObjectId? textStyleId = null,
        double? expectedHeight = null,
        double? actualHeight = null,
        string? annotationGroupId = null,
        string? sourceHandle = null)
    {
        Write($"STEP {step}: FAILED");
        Write($"  condition={condition}");
        if (exception is not null)
        {
            Write($"  exception={exception.GetType().FullName}");
            if (exception is Autodesk.AutoCAD.Runtime.Exception acadException)
            {
                Write($"  ErrorStatus={acadException.ErrorStatus}");
            }

            Write($"  message={exception.Message}");
            Write($"  stack={exception.StackTrace}");
        }

        Write($"  sourceObjectId={FormatId(sourceId)} handle={sourceHandle ?? "<n/a>"}");
        Write($"  leaderObjectId={FormatId(leaderId)}");
        Write($"  frameObjectId={FormatId(frameId)}");
        Write($"  itemTextObjectId={FormatId(itemTextId)}");
        Write(
            $"  frameBlock={frameBlockName ?? "<n/a>"} " +
            $"frameBlockId={FormatId(frameBlockId)}");
        Write($"  TextStyleId={FormatId(textStyleId)}");
        Write(
            $"  height expected={FormatDouble(expectedHeight)} " +
            $"actual={FormatDouble(actualHeight)}");
        Write($"  AnnotationGroupId={annotationGroupId ?? "<n/a>"}");
    }

    private static void Write(string message) => _writer?.Invoke(message);

    private static string FormatId(ObjectId? id) =>
        id is null || id.Value.IsNull ? "<null>" : id.Value.ToString();

    private static string FormatDouble(double? value) =>
        value?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ??
        "<n/a>";

    private sealed class DetachScope : IDisposable
    {
        public void Dispose() => _writer = null;
    }
}
#endif
