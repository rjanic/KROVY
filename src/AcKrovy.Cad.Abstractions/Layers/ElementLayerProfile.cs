using AcKrovy.Core.Models;

namespace AcKrovy.Cad.Abstractions.Layers;

/// <summary>
/// Používateľské pravidlá, ktoré určujú, na akú hladinu, farbu a typ čiary sa zaradí
/// inteligentný prvok ACAD KROVY.
/// </summary>
public sealed class ElementLayerProfile
{
    public const int CurrentVersion = 3;
    public const double MinLinetypeScale = 0.01;
    public const double MaxLinetypeScale = 1000.0;
    public const double DefaultRafterLinetypeScale = 0.5;
    public const double DefaultOtherLinetypeScale = 1.0;

    public int Version { get; set; } = CurrentVersion;
    public List<ElementLayerStyle> Styles { get; set; } = new();

    public ElementLayerStyle GetStyle(TimberElementType type)
    {
        var stored = Styles.FirstOrDefault(style => style.ElementType == type);
        if (stored is not null)
        {
            return stored;
        }

        return CreateDefault().Styles.First(style => style.ElementType == type);
    }

    public ElementLayerProfile Normalize()
    {
        var defaults = CreateDefault();
        return new ElementLayerProfile
        {
            Version = CurrentVersion,
            Styles = Enum
                .GetValues(typeof(TimberElementType))
                .Cast<TimberElementType>()
                .Select(type =>
                {
                    var fallback = defaults.GetStyle(type);
                    var stored = Styles.FirstOrDefault(style => style.ElementType == type);
                    var layerName = stored?.LayerName;

                    return new ElementLayerStyle
                    {
                        ElementType = type,
                        LayerName = string.IsNullOrWhiteSpace(layerName)
                            ? fallback.LayerName
                            : layerName!.Trim(),
                        ColorIndex = stored is { ColorIndex: >= 1 and <= 255 }
                            ? stored.ColorIndex
                            : fallback.ColorIndex,
                        LinetypeName = string.IsNullOrWhiteSpace(stored?.LinetypeName)
                            ? fallback.LinetypeName
                            : stored!.LinetypeName.Trim(),
                        LinetypeScale = IsValidLinetypeScale(stored?.LinetypeScale)
                            ? stored!.LinetypeScale
                            : fallback.LinetypeScale,
                    };
                })
                .ToList(),
        };
    }

    public static ElementLayerProfile CreateDefault() => new()
    {
        Styles = new List<ElementLayerStyle>
        {
            new(
                TimberElementType.Rafter,
                "KROKVA",
                2,
                CadLinetypeNames.DashDot,
                DefaultRafterLinetypeScale),
            new(TimberElementType.WallPlate, "POMURNICA", 30),
            new(TimberElementType.Purlin, "VAZNICA", 4),
            new(TimberElementType.Post, "STLPIK", 3),
            new(TimberElementType.CollarTie, "KLIESTINA", 5),
            new(TimberElementType.Brace, "VZPERA", 1),
            new(TimberElementType.TieBeam, "VAZNY_TRAM", 6),
            new(TimberElementType.Custom, "KROV_CUSTOM", 7),
        },
    };

    public static double GetDefaultLinetypeScale(TimberElementType type) =>
        type == TimberElementType.Rafter
            ? DefaultRafterLinetypeScale
            : DefaultOtherLinetypeScale;

    public static bool IsValidLinetypeScale(double? value) =>
        value is { } scale &&
        !double.IsNaN(scale) &&
        !double.IsInfinity(scale) &&
        scale >= MinLinetypeScale &&
        scale <= MaxLinetypeScale;
}

public sealed class ElementLayerStyle
{
    public ElementLayerStyle()
    {
    }

    public ElementLayerStyle(TimberElementType elementType, string layerName, int colorIndex)
        : this(elementType, layerName, colorIndex, CadLinetypeNames.Continuous)
    {
    }

    public ElementLayerStyle(
        TimberElementType elementType,
        string layerName,
        int colorIndex,
        string linetypeName)
        : this(
            elementType,
            layerName,
            colorIndex,
            linetypeName,
            ElementLayerProfile.GetDefaultLinetypeScale(elementType))
    {
    }

    public ElementLayerStyle(
        TimberElementType elementType,
        string layerName,
        int colorIndex,
        string linetypeName,
        double linetypeScale)
    {
        ElementType = elementType;
        LayerName = layerName;
        ColorIndex = colorIndex;
        LinetypeName = linetypeName;
        LinetypeScale = linetypeScale;
    }

    public TimberElementType ElementType { get; set; }
    public string LayerName { get; set; } = string.Empty;
    public int ColorIndex { get; set; }
    public string LinetypeName { get; set; } = string.Empty;
    public double LinetypeScale { get; set; }
}
