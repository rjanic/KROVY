using AcKrovy.Core.Models.Roofs;

namespace AcKrovy.Core.Services.Roofs;

/// <summary>Intentional definition changes for a monopitch roof.</summary>
public static class MonopitchRoofDefinitionRules
{
    public static RoofDefinition Mirror(RoofDefinition definition)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }
        if (definition.Kind != RoofKind.Monopitch ||
            definition.Parameters.SlopeDirection is not { } direction ||
            !RoofDirection2D.TryCreate(-direction.X, -direction.Y, out var reversed))
        {
            throw new ArgumentException("A valid monopitch definition is required.", nameof(definition));
        }

        return new RoofDefinition(
            definition.Footprint,
            definition.Parameters with { SlopeDirection = reversed },
            RoofKind.Monopitch);
    }
}
