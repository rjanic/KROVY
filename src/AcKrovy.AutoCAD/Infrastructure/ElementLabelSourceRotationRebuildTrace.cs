#if DEBUG
using AcKrovy.Core.Services;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static partial class ElementLabelService
{
    private static readonly object SourceRotationRebuildTraceGate = new();
    private static readonly Dictionary<string, G5SourceRotationRebuildTrace>
        SourceRotationRebuildTraces = new(StringComparer.OrdinalIgnoreCase);

    internal static bool TryGetSourceRotationRebuildTrace(
        string sourceHandle,
        out G5SourceRotationRebuildTrace trace)
    {
        lock (SourceRotationRebuildTraceGate)
        {
            return SourceRotationRebuildTraces.TryGetValue(sourceHandle, out trace!);
        }
    }

    private static void RecordSourceRotationRebuildPending(
        string sourceHandle,
        TimberFramedCombinedG5SourceRotationRebuildDecision decision,
        string oldAnnotationHandle)
    {
        lock (SourceRotationRebuildTraceGate)
        {
            SourceRotationRebuildTraces[sourceHandle] =
                new G5SourceRotationRebuildTrace(
                    decision.SourceAxisBeforeRadians,
                    decision.SourceAxisAfterRadians,
                    decision.SourceAxisDeltaRadians,
                    decision.SourceRotationDetected,
                    decision.AnnotationRebuildRequired,
                    AnnotationRebuilt: false,
                    oldAnnotationHandle,
                    NewAnnotationHandle: null,
                    decision.RebuildReason);
        }
    }

    private static void CompleteSourceRotationRebuildTrace(
        string sourceHandle,
        string newAnnotationHandle)
    {
        lock (SourceRotationRebuildTraceGate)
        {
            if (!SourceRotationRebuildTraces.TryGetValue(sourceHandle, out var pending) ||
                !pending.AnnotationRebuildRequired)
            {
                return;
            }

            SourceRotationRebuildTraces[sourceHandle] = pending with
            {
                AnnotationRebuilt = true,
                NewAnnotationHandle = newAnnotationHandle,
            };
        }
    }

    private static void RecordSourceRotationNoRebuildTrace(
        string sourceHandle,
        TimberFramedCombinedG5SourceRotationRebuildDecision decision,
        string annotationHandle)
    {
        lock (SourceRotationRebuildTraceGate)
        {
            SourceRotationRebuildTraces[sourceHandle] =
                new G5SourceRotationRebuildTrace(
                    decision.SourceAxisBeforeRadians,
                    decision.SourceAxisAfterRadians,
                    decision.SourceAxisDeltaRadians,
                    decision.SourceRotationDetected,
                    decision.AnnotationRebuildRequired,
                    AnnotationRebuilt: false,
                    annotationHandle,
                    annotationHandle,
                    decision.RebuildReason);
        }
    }
}

internal sealed record G5SourceRotationRebuildTrace(
    double SourceAxisBeforeRadians,
    double SourceAxisAfterRadians,
    double SourceAxisDeltaRadians,
    bool SourceRotationDetected,
    bool AnnotationRebuildRequired,
    bool AnnotationRebuilt,
    string OldAnnotationHandle,
    string? NewAnnotationHandle,
    string RebuildReason);
#endif
