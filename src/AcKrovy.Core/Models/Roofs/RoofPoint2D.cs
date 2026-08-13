namespace AcKrovy.Core.Models.Roofs;

/// <summary>A CAD-neutral point in the drawing XY plane, expressed in millimetres.</summary>
public readonly record struct RoofPoint2D(double X, double Y)
{
    public double DistanceTo(RoofPoint2D other)
    {
        var deltaX = other.X - X;
        var deltaY = other.Y - Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }
}
