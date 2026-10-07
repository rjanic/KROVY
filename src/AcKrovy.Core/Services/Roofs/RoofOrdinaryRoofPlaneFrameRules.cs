using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Measures a rebuilt prism against its authoritative current roof face.</summary>
public static class RoofOrdinaryRoofPlaneFrameRules
{
    public sealed record Frame(RoofPoint3D RoofNormal, RoofPoint3D PhysicalAxis,
        RoofPoint3D WidthAxis, double TopFacePlaneErrorMm, double SectionOrthogonalityError,
        double HeightNormalErrorMm);

    public static bool TryDescribe(RoofTopology topology, RoofAutomaticRafterPhysicalMember member,
        double eaveElevationMm, out Frame? frame)
    {
        frame = null;
        var face = topology.Faces.SingleOrDefault(item => item.SourceEdgeIndex == member.SourceFaceIndex);
        if (face is null || face.BoundaryNodeIndices.Count < 3 ||
            !RoofRafterPhysicalGeometry.TryCreateUpwardUnitNormal(topology, face, out var faceNormal)) return false;
        var prism = member.HorizontalCut?.SourcePrismVertices ?? member.StructuralCut?.SourcePrismVertices ??
            member.RidgeOverlapCut?.SourcePrismVertices ??
            member.RidgeMeetCut?.SourcePrismVertices ?? member.SolidVertices;
        if (prism.Count < 8) return false;
        var normal = new RoofPoint3D(faceNormal.X, faceNormal.Y, faceNormal.Z);
        var node = topology.Nodes[face.BoundaryNodeIndices[0]];
        var origin = new RoofPoint3D(node.X, node.Y, node.Z + eaveElevationMm);
        var run = Subtract(prism[2], prism[0]);
        var width = Subtract(prism[1], prism[0]);
        var runLength = Length(run);
        var widthLength = Length(width);
        if (runLength <= 1e-9 || widthLength <= 1e-9) return false;
        run = Scale(run, 1 / runLength);
        width = Scale(width, 1 / widthLength);
        var top = prism.Take(4).Concat(member.HorizontalCut?.TopFaceVertices ?? Array.Empty<RoofPoint3D>())
            .Concat(member.StructuralCut?.TopFaceVertices ?? Array.Empty<RoofPoint3D>())
            .Concat(member.RidgeOverlapCut?.TopFaceVertices ?? Array.Empty<RoofPoint3D>())
            .Concat(member.RidgeMeetCut?.TopFaceVertices ?? Array.Empty<RoofPoint3D>());
        var error = top.Max(point => Math.Abs(Dot(Subtract(point, origin), normal)));
        var heightError = Enumerable.Range(0, 4).Max(index =>
            Math.Abs(Dot(Subtract(prism[index], prism[index + 4]), normal) - member.HeightMm));
        frame = new(normal, run, width, error,
            Math.Max(Math.Abs(Dot(run, width)), Math.Max(Math.Abs(Dot(run, normal)), Math.Abs(Dot(width, normal)))), heightError);
        return !double.IsNaN(error) && !double.IsInfinity(error);
    }

    private static RoofPoint3D Subtract(RoofPoint3D a, RoofPoint3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static RoofPoint3D Scale(RoofPoint3D p, double s) => new(p.X * s, p.Y * s, p.Z * s);
    private static double Dot(RoofPoint3D a, RoofPoint3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static double Length(RoofPoint3D p) => Math.Sqrt(Dot(p, p));
}
