using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcKrovy.AutoCAD.Infrastructure;

internal static class PostFootprintRuntimeGeometryResolver
{
    public static bool TryResolve(
        Entity entity,
        TimberElementData data,
        out TimberRectangularFootprintGeometry? geometry,
        out TimberRectangularFootprintDimensions? dimensions)
    {
        geometry = null;
        dimensions = null;

        if (!TimberPostFootprintMetadataRules.IsValidNewFootprintPost(data) ||
            entity is not Polyline polyline ||
            !PostFootprintGeometryExtractor.TryExtract(polyline, out geometry, out _) ||
            geometry is null)
        {
            return false;
        }

        dimensions = TimberRectangularFootprintEdgeRules.ResolveDimensions(
            geometry,
            data.FootprintWidthEdgeIndex!.Value);
        return true;
    }
}
