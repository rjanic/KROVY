namespace AcKrovy.Cad.Abstractions.Layers;

public sealed class CadLayerApplyResult
{
    private CadLayerApplyResult(
        string requestedLinetypeName,
        string appliedLinetypeName,
        bool usedFallback,
        string? preservedConflictingLayerName = null,
        bool drawingChanged = false)
    {
        RequestedLinetypeName = requestedLinetypeName;
        AppliedLinetypeName = appliedLinetypeName;
        UsedFallback = usedFallback;
        PreservedConflictingLayerName = preservedConflictingLayerName;
        DrawingChanged = drawingChanged;
    }

    public string RequestedLinetypeName { get; }
    public string AppliedLinetypeName { get; }
    public bool UsedFallback { get; }
    public string? PreservedConflictingLayerName { get; }
    public bool PreservedConflictingLayer => !string.IsNullOrWhiteSpace(PreservedConflictingLayerName);
    public bool DrawingChanged { get; }

    public static CadLayerApplyResult Applied(string name) => new(name, name, false);

    public static CadLayerApplyResult Fallback(string requestedName) =>
        new(requestedName, CadLinetypeNames.Continuous, true);

    public CadLayerApplyResult WithApplication(
        string appliedLinetypeName,
        string? preservedConflictingLayerName,
        bool drawingChanged) =>
        new(
            RequestedLinetypeName,
            appliedLinetypeName,
            UsedFallback,
            preservedConflictingLayerName,
            drawingChanged);
}
