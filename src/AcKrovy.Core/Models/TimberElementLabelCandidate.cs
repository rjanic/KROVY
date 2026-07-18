namespace AcKrovy.Core.Models;

public sealed record TimberElementLabelCandidate
{
    public string LabelKey { get; init; } = string.Empty;
    public string ElementId { get; init; } = string.Empty;
    public string SourceHandle { get; init; } = string.Empty;
}
