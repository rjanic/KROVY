namespace AcKrovy.Core.Models;

/// <summary>
/// Portable G4 composite entity candidate used for SourceHandle-first matching.
/// </summary>
public sealed record TimberFramedG4CompositeCandidate
{
    public string EntityKey { get; init; } = string.Empty;
    public string ElementId { get; init; } = string.Empty;
    public string SourceHandle { get; init; } = string.Empty;
    public TimberMainAnnotationComponentRole ComponentRole { get; init; } =
        TimberMainAnnotationComponentRole.CircleLeaderLine;
    public string? AnnotationGroupId { get; init; }
    public bool IsLegacyBlockLeader { get; init; }
}
