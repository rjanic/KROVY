namespace AcKrovy.Core.Models;

public sealed record TimberFramedLeaderManualOffset(
    double AlongAxisMm,
    double NormalAxisMm)
{
    public static TimberFramedLeaderManualOffset Zero { get; } = new(0d, 0d);
}
