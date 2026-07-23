namespace AcKrovy.Core.Models;

/// <summary>
/// Reusable definition of a user-defined linear timber element. The identifier
/// is the stable technical identity; name and prefix remain user-owned data.
/// </summary>
public sealed record CustomElementDefinition(
    string Id,
    string Name,
    string Prefix);
