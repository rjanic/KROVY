namespace AcKrovy.Core.Models;

public static class TimberElementDataSchema
{
    public const int CurrentVersion = 7;
    public const int LegacyImplicitVersion = 1;

    /// <summary>
    /// Last version whose annotation typography used one shared text-style name
    /// and the legacy height keys. Still readable, never rewritten on read.
    /// </summary>
    public const int SharedAnnotationTextStyleVersion = 6;
}
