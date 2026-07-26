using System.Text.RegularExpressions;
using AcKrovy.Core.Models;

namespace AcKrovy.Cad.Abstractions.Layers;

public sealed class CadLayerNameCandidate
{
    public CadLayerNameCandidate(
        string name,
        bool isErased = false,
        bool isXrefDependent = false)
    {
        Name = name;
        IsErased = isErased;
        IsXrefDependent = isXrefDependent;
    }

    public string Name { get; }
    public bool IsErased { get; }
    public bool IsXrefDependent { get; }
}

public sealed class CadLayerPreset
{
    public CadLayerPreset(
        string name,
        int aciColorIndex,
        string linetypeName,
        double? uniformEntityLinetypeScale = null,
        bool hasMixedEntityLinetypeScales = false)
    {
        Name = name;
        AciColorIndex = aciColorIndex;
        LinetypeName = CadLinetypeNames.Normalize(linetypeName);
        UniformEntityLinetypeScale = uniformEntityLinetypeScale;
        HasMixedEntityLinetypeScales = hasMixedEntityLinetypeScales;
    }

    public string Name { get; }
    public int AciColorIndex { get; }
    public string LinetypeName { get; }
    public double? UniformEntityLinetypeScale { get; }
    public bool HasMixedEntityLinetypeScales { get; }
}

public sealed class CadLayerOverrideIntent
{
    public CadLayerOverrideIntent(
        TimberElementType elementType,
        string? selectedExistingLayerName,
        bool hasPropertyOverrides)
    {
        ElementType = elementType;
        SelectedExistingLayerName = selectedExistingLayerName;
        HasPropertyOverrides = hasPropertyOverrides;
    }

    public TimberElementType ElementType { get; }
    public string? SelectedExistingLayerName { get; }
    public bool HasPropertyOverrides { get; }
}

public sealed class CadLayerScaleHydrationResult
{
    public CadLayerScaleHydrationResult(
        double value,
        bool loadedFromEntities,
        bool hasMixedValues)
    {
        Value = value;
        LoadedFromEntities = loadedFromEntities;
        HasMixedValues = hasMixedValues;
    }

    public double Value { get; }
    public bool LoadedFromEntities { get; }
    public bool HasMixedValues { get; }
}

public static class CadLayerScaleHydrationRules
{
    public const double ComparisonTolerance = 0.0000001d;

    public static CadLayerScaleHydrationResult Resolve(
        double currentProfileValue,
        IEnumerable<double> entityValues)
    {
        if (entityValues is null)
        {
            throw new ArgumentNullException(nameof(entityValues));
        }

        var values = entityValues
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .ToArray();
        if (values.Length == 0)
        {
            return new CadLayerScaleHydrationResult(
                currentProfileValue,
                loadedFromEntities: false,
                hasMixedValues: false);
        }

        var first = values[0];
        var mixed = values.Any(value =>
            Math.Abs(value - first) > ComparisonTolerance);
        return mixed
            ? new CadLayerScaleHydrationResult(
                currentProfileValue,
                loadedFromEntities: false,
                hasMixedValues: true)
            : new CadLayerScaleHydrationResult(
                first,
                loadedFromEntities: true,
                hasMixedValues: false);
    }
}

public static class CadLayerOverrideRules
{
    public static bool RequiresSuffix(
        string layerName,
        bool physicalLayerMatchesRequestedAppearance,
        CadLayerOverrideIntent? intent)
    {
        var loadedFromSelectedExistingLayer =
            intent is not null &&
            string.Equals(
                intent.SelectedExistingLayerName,
                layerName,
                StringComparison.OrdinalIgnoreCase);
        return loadedFromSelectedExistingLayer
            ? intent!.HasPropertyOverrides
            : !physicalLayerMatchesRequestedAppearance;
    }
}

public static class CadLayerNameRules
{
    private static readonly Regex GeneratedSuffixRegex = new(
        @"^(?<base>.+)_(?<suffix>\d{2,})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<string> SelectUsableLocalNames(
        IEnumerable<CadLayerNameCandidate> candidates)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "0",
            "Defpoints",
        };
        foreach (var candidate in candidates)
        {
            if (!candidate.IsErased &&
                !candidate.IsXrefDependent &&
                !string.IsNullOrWhiteSpace(candidate.Name))
            {
                names.Add(candidate.Name.Trim());
            }
        }

        return names
            .OrderBy(LayerSortGroup)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string NextConflictFreeName(
        string requestedName,
        IEnumerable<string> occupiedNames)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            throw new ArgumentException("A base layer name is required.", nameof(requestedName));
        }

        var occupied = new HashSet<string>(
            occupiedNames ?? throw new ArgumentNullException(nameof(occupiedNames)),
            StringComparer.OrdinalIgnoreCase);
        var baseName = GetCanonicalBaseName(requestedName);

        for (var suffix = 1; ; suffix++)
        {
            var suffixText = $"_{suffix:D2}";
            var maximumBaseLength = 255 - suffixText.Length;
            var safeBase = baseName.Length <= maximumBaseLength
                ? baseName
                : baseName.Substring(0, maximumBaseLength);
            var candidate = safeBase + suffixText;
            if (!occupied.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    public static string GetCanonicalBaseName(string layerName)
    {
        if (string.IsNullOrWhiteSpace(layerName))
        {
            throw new ArgumentException("A layer name is required.", nameof(layerName));
        }

        var normalized = layerName.Trim();
        while (GeneratedSuffixRegex.Match(normalized) is { Success: true } match)
        {
            normalized = match.Groups["base"].Value;
        }

        return normalized;
    }

    public static bool IsCanonicalOrGeneratedVariant(
        string candidateName,
        string canonicalBaseName)
    {
        if (string.IsNullOrWhiteSpace(candidateName) ||
            string.IsNullOrWhiteSpace(canonicalBaseName))
        {
            return false;
        }

        return string.Equals(
            GetCanonicalBaseName(candidateName),
            GetCanonicalBaseName(canonicalBaseName),
            StringComparison.OrdinalIgnoreCase);
    }

    private static int LayerSortGroup(string name) =>
        string.Equals(name, "0", StringComparison.OrdinalIgnoreCase) ? 0 :
        string.Equals(name, "Defpoints", StringComparison.OrdinalIgnoreCase) ? 1 :
        2;
}
