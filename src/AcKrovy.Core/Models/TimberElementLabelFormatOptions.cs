namespace AcKrovy.Core.Models;

public sealed record TimberElementLabelFormatOptions
{
    public static TimberElementLabelFormatOptions Default { get; } = new();

    public string DimensionSeparator { get; init; } = "x";
}
