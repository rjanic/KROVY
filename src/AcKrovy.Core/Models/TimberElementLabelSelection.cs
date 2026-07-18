namespace AcKrovy.Core.Models;

public sealed record TimberElementLabelSelection
{
    public string? LabelKeyToUpdate { get; init; }
    public IReadOnlyList<string> LabelKeysToDelete { get; init; } = Array.Empty<string>();
}
