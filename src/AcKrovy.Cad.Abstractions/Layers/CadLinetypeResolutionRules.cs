namespace AcKrovy.Cad.Abstractions.Layers;

public static class CadLinetypeResolutionRules
{
    public static CadLayerApplyResult Resolve(
        string? requestedName,
        Func<string, bool> exists,
        Func<string, bool> tryLoadStandard)
    {
        if (exists is null)
        {
            throw new ArgumentNullException(nameof(exists));
        }

        if (tryLoadStandard is null)
        {
            throw new ArgumentNullException(nameof(tryLoadStandard));
        }

        var normalizedName = CadLinetypeNames.Normalize(requestedName);
        if (exists(normalizedName))
        {
            return CadLayerApplyResult.Applied(normalizedName);
        }

        if (CadLinetypeNames.IsSupportedStandard(normalizedName) &&
            !string.Equals(
                normalizedName,
                CadLinetypeNames.Continuous,
                StringComparison.OrdinalIgnoreCase) &&
            tryLoadStandard(normalizedName) &&
            exists(normalizedName))
        {
            return CadLayerApplyResult.Applied(normalizedName);
        }

        return CadLayerApplyResult.Fallback(normalizedName);
    }
}
