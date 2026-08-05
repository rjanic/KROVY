namespace AcKrovy.Core.Models;

/// <summary>
/// CAD-neutral planar point for Core layout math.
/// Prefer this over host geometry types.
/// Existing domain points (<see cref="TimberRectangularFootprintPoint"/>,
/// <see cref="TimberSlopeAnnotationPoint"/>) remain for their specific models.
/// </summary>
public readonly record struct TimberPlanarPoint(double X, double Y)
{
    public TimberPlanarPoint Offset(TimberPlanarVector vector) =>
        new(X + vector.X, Y + vector.Y);

    public TimberPlanarPoint Offset(double deltaX, double deltaY) =>
        new(X + deltaX, Y + deltaY);

    public double DistanceTo(TimberPlanarPoint other)
    {
        var dx = other.X - X;
        var dy = other.Y - Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>CAD-neutral planar vector for Core layout math.</summary>
public readonly record struct TimberPlanarVector(double X, double Y)
{
    public TimberPlanarVector Scale(double scalar) =>
        new(X * scalar, Y * scalar);

    /// <summary>Unit vector for angle measured from +X toward +Y.</summary>
    public static TimberPlanarVector FromAngleRadians(double radians) =>
        new(Math.Cos(radians), Math.Sin(radians));

    /// <summary>Left-handed perpendicular (+90°): T=(tx,ty) → N=(-ty, tx).</summary>
    public TimberPlanarVector PerpendicularLeft => new(-Y, X);

    public double Length => Math.Sqrt(X * X + Y * Y);
}
