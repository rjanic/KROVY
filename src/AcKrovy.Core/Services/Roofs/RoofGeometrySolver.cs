using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>The single roof-type dispatch boundary for canonical roof geometry.</summary>
public static class RoofGeometrySolver
{
    public static RoofGeometryResult Solve(RoofDefinition definition)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }
        if (definition.Kind == RoofKind.Monopitch)
        {
            return MonopitchRoofGeometrySolver.Solve(definition);
        }
        if (definition.Kind == RoofKind.Hip)
        {
            return HipRoofGeometrySolver.Solve(definition);
        }

        var gable = definition.Kind switch
        {
            RoofKind.SimpleGable or RoofKind.AsymmetricGable =>
                SimpleGableRoofGeometrySolver.Solve(definition),
            _ => new SimpleGableRoofGeometryResult(
                false,
                null,
                SimpleGableRoofGeometryError.InvalidRoofKind),
        };
        return new RoofGeometryResult(gable.IsValid, gable.Geometry, gable.Error);
    }
}
