namespace AcKrovy.Core.Models;

public enum TimberItemLeaderBlockSize
{
    Small,
    Medium,
    Large,
}

public sealed record TimberItemLeaderBlockDefinition(
    ItemNumberLeaderStyle Style,
    TimberItemLeaderBlockSize Size,
    string BlockName,
    double WidthMm,
    double HeightMm,
    double TextHeightMm);
