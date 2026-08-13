using System.Globalization;

namespace AcKrovy.Core.Models.Roofs;

/// <summary>Deterministic neutral geometry for one centered rectangular gable roof.</summary>
public sealed class SimpleGableRoofGeometry
{
    internal SimpleGableRoofGeometry(
        RoofSegment3D ridge,
        IReadOnlyList<SimpleGableRoofFace> faces,
        RoofDirection2D ridgeDirection,
        double runMm,
        double riseMm,
        double slopeDegrees)
    {
        Ridge = ridge;
        Faces = faces.ToArray();
        RidgeDirection = ridgeDirection;
        RunMm = runMm;
        RiseMm = riseMm;
        SlopeDegrees = slopeDegrees;
        Signature = BuildSignature();
    }

    public RoofSegment3D Ridge { get; }

    public IReadOnlyList<SimpleGableRoofFace> Faces { get; }

    public RoofDirection2D RidgeDirection { get; }

    public double RidgeLengthMm => Ridge.LengthMm;

    public double RunMm { get; }

    public double RiseMm { get; }

    public double SlopeDegrees { get; }

    public string Signature { get; }

    private string BuildSignature()
    {
        var values = new List<double>
        {
            Ridge.Start.X,
            Ridge.Start.Y,
            Ridge.Start.Z,
            Ridge.End.X,
            Ridge.End.Y,
            Ridge.End.Z,
            RidgeDirection.X,
            RidgeDirection.Y,
            RunMm,
            RiseMm,
            SlopeDegrees,
        };
        values.AddRange(Faces.SelectMany(face =>
            face.BoundaryPoints.SelectMany(point => new[] { point.X, point.Y, point.Z })));
        return string.Join(
            ";",
            values.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
    }
}
