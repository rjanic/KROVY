namespace AcKrovy.Core.Models.Roofs;

/// <summary>A deterministic CAD-neutral segment in the local roof datum.</summary>
public sealed record RoofSegment3D(RoofPoint3D Start, RoofPoint3D End)
{
    public double LengthMm => Start.DistanceTo(End);

    /// <summary>
    /// Physical member inclination above horizontal from the authoritative 3D
    /// segment. Magnitude is endpoint-reversal invariant (uses |ΔZ| and plan run).
    /// </summary>
    public double InclinationDegreesAboveHorizontal
    {
        get
        {
            var deltaX = End.X - Start.X;
            var deltaY = End.Y - Start.Y;
            var deltaZ = End.Z - Start.Z;
            var horizontal = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            return Math.Atan2(Math.Abs(deltaZ), horizontal) * (180d / Math.PI);
        }
    }
}
