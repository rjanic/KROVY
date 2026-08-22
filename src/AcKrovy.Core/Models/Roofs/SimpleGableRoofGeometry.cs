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
        double slopeDegrees,
        RoofKind kind = RoofKind.SimpleGable,
        double? face1RunMm = null,
        double? face1SlopeDegrees = null,
        double eaveHeightDifferenceMm = 0d)
    {
        Ridge = ridge;
        Faces = faces.ToArray();
        RidgeDirection = ridgeDirection;
        RunMm = runMm;
        RiseMm = riseMm;
        SlopeDegrees = slopeDegrees;
        Kind = kind;
        Face0RunMm = runMm;
        Face1RunMm = face1RunMm ?? runMm;
        Face0SlopeDegrees = slopeDegrees;
        Face1SlopeDegrees = face1SlopeDegrees ?? slopeDegrees;
        EaveHeightDifferenceMm = eaveHeightDifferenceMm;
        Signature = BuildSignature();
    }

    public RoofSegment3D Ridge { get; }

    public IReadOnlyList<SimpleGableRoofFace> Faces { get; }

    public RoofDirection2D RidgeDirection { get; }

    public double RidgeLengthMm => Ridge.LengthMm;

    public double RunMm { get; }

    public double RiseMm { get; }

    public double SlopeDegrees { get; }

    public RoofKind Kind { get; }

    public double Face0RunMm { get; }

    public double Face1RunMm { get; }

    public double Face0SlopeDegrees { get; }

    public double Face1SlopeDegrees { get; }

    /// <summary>Signed eave elevation difference zB - zA in millimetres.</summary>
    public double EaveHeightDifferenceMm { get; }

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
        if (Kind == RoofKind.AsymmetricGable)
        {
            values.AddRange([
                (double)Kind,
                Face0RunMm,
                Face1RunMm,
                Face0SlopeDegrees,
                Face1SlopeDegrees,
            ]);
            if (EaveHeightDifferenceMm != 0d)
            {
                values.Add(EaveHeightDifferenceMm);
            }
        }
        values.AddRange(Faces.SelectMany(face =>
            face.BoundaryPoints.SelectMany(point => new[] { point.X, point.Y, point.Z })));
        return string.Join(
            ";",
            values.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
    }
}
