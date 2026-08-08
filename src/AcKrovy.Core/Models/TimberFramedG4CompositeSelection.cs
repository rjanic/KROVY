namespace AcKrovy.Core.Models;

/// <summary>
/// Result of G4 composite matching. Entity keys to delete are same-owner
/// duplicates; they must never include entities owned by another SourceHandle.
/// </summary>
public sealed record TimberFramedG4CompositeSelection
{
    public string? LeaderKey { get; init; }
    public string? FrameKey { get; init; }
    public string? ItemCodeKey { get; init; }
    public string? LegacyBlockLeaderKey { get; init; }
    public string? AnnotationGroupId { get; init; }
    public IReadOnlyList<string> EntityKeysToDelete { get; init; } =
        Array.Empty<string>();

    public static TimberFramedG4CompositeSelection Empty { get; } = new();

    public bool HasAnySelectedEntity =>
        !string.IsNullOrWhiteSpace(LeaderKey) ||
        !string.IsNullOrWhiteSpace(FrameKey) ||
        !string.IsNullOrWhiteSpace(ItemCodeKey) ||
        !string.IsNullOrWhiteSpace(LegacyBlockLeaderKey);
}
