using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberFramedLeaderManualOffsetCalculator
{
    public static TimberFramedLeaderManualOffset Capture(
        TimberFramedLeaderManualOffset persistedOffset,
        double lastAppliedX,
        double lastAppliedY,
        double actualX,
        double actualY,
        double oldRotationRadians)
    {
        if (persistedOffset is null)
        {
            throw new ArgumentNullException(nameof(persistedOffset));
        }

        var deltaX = actualX - lastAppliedX;
        var deltaY = actualY - lastAppliedY;
        var cosine = Math.Cos(oldRotationRadians);
        var sine = Math.Sin(oldRotationRadians);
        return new TimberFramedLeaderManualOffset(
            persistedOffset.AlongAxisMm + deltaX * cosine + deltaY * sine,
            persistedOffset.NormalAxisMm - deltaX * sine + deltaY * cosine);
    }

    public static (double X, double Y) Apply(
        TimberFramedLeaderManualOffset offset,
        double automaticX,
        double automaticY,
        double newRotationRadians)
    {
        if (offset is null)
        {
            throw new ArgumentNullException(nameof(offset));
        }

        var cosine = Math.Cos(newRotationRadians);
        var sine = Math.Sin(newRotationRadians);
        return (
            automaticX + offset.AlongAxisMm * cosine - offset.NormalAxisMm * sine,
            automaticY + offset.AlongAxisMm * sine + offset.NormalAxisMm * cosine);
    }
}
