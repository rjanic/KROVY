using AcKrovy.Core.Models;

namespace AcKrovy.Core.Services;

public static class TimberElementSeriesRules
{
    public static TimberElementSeriesKey GetKey(TimberElementData data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        return data.ElementType == TimberElementType.Custom
            ? new TimberElementSeriesKey(
                TimberElementType.Custom,
                data.CustomElementTypeId?.Trim() ?? string.Empty)
            : new TimberElementSeriesKey(data.ElementType, string.Empty);
    }

    public static TimberElementSeriesKey GetKey(TimberElementSignature signature)
    {
        if (signature is null)
        {
            throw new ArgumentNullException(nameof(signature));
        }
        return signature.ElementType == TimberElementType.Custom
            ? new TimberElementSeriesKey(
                TimberElementType.Custom,
                signature.CustomElementTypeId.Trim())
            : new TimberElementSeriesKey(signature.ElementType, string.Empty);
    }

    public static string GetPrefix(TimberElementData data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }
        if (data.ElementType != TimberElementType.Custom)
        {
            return TimberElementIdentityPrefixes.GetPrefix(data.ElementType);
        }

        if (!CustomElementDefinitionRules.TryFromElementData(data, out var definition) ||
            definition is null)
        {
            throw new ArgumentException("Custom timber element definition is incomplete.", nameof(data));
        }

        return definition.Prefix;
    }
}
