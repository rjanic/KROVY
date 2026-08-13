namespace AcKrovy.Core.Models.Roofs;

/// <summary>A CAD-neutral point in the local roof datum, expressed in millimetres.</summary>
public readonly record struct RoofPoint3D(double X, double Y, double Z)
{
    public double DistanceTo(RoofPoint3D other)
    {
        var deltaX = other.X - X;
        var deltaY = other.Y - Y;
        var deltaZ = other.Z - Z;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ);
    }
}
