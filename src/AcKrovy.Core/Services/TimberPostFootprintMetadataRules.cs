using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberPostFootprintMetadataRules
{
    public static bool IsValidNewFootprintPost(TimberElementData data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return data.ElementType == TimberElementType.Post &&
            data.FootprintWidthEdgeIndex is { } edgeIndex &&
            TimberRectangularFootprintEdgeRules.IsValidEdgeIndex(edgeIndex);
    }

    public static bool HasPreferredFootprintMetadataShape(TimberElementData data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return data.ElementType == TimberElementType.Post
            ? data.FootprintWidthEdgeIndex is null || IsValidNewFootprintPost(data)
            : data.FootprintWidthEdgeIndex is null;
    }
}
