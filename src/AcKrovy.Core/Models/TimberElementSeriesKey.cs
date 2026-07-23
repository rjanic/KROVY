namespace AcKrovy.Core.Models;

/// <summary>
/// Stable numbering series identity. Built-in types use an empty custom id;
/// each custom definition owns an independent series.
/// </summary>
public readonly record struct TimberElementSeriesKey(
    TimberElementType ElementType,
    string CustomElementTypeId);
