using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>The single roof-type dispatch boundary for canonical roof geometry.</summary>
public static class RoofGeometrySolver
{
    public static SimpleGableRoofGeometryResult Solve(RoofDefinition definition)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }
        return definition.Kind switch
        {
            RoofKind.SimpleGable or RoofKind.AsymmetricGable =>
                SimpleGableRoofGeometrySolver.Solve(definition),
            _ => new SimpleGableRoofGeometryResult(
                false,
                null,
                SimpleGableRoofGeometryError.InvalidRoofKind),
        };
    }
}
