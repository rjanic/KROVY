using AcKrovy.Core.Models;

namespace AcKrovy.Cad.Abstractions.Layers;

/// <summary>
/// Používateľské pravidlá, ktoré určujú, na akú hladinu a farbu sa zaradí
/// inteligentný prvok ACAD KROVY.
/// </summary>
public sealed class ElementLayerProfile
{
    public int Version { get; set; } = 1;
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
            Version = Version <= 0 ? 1 : Version,
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
                    };
                })
                .ToList(),
        };
    }

    public static ElementLayerProfile CreateDefault() => new()
    {
        Styles = new List<ElementLayerStyle>
        {
            new(TimberElementType.Rafter, "KROKVA", 2),
            new(TimberElementType.WallPlate, "POMURNICA", 30),
            new(TimberElementType.Purlin, "VAZNICA", 4),
            new(TimberElementType.Post, "STLPIK", 3),
            new(TimberElementType.CollarTie, "KLIESTINA", 5),
            new(TimberElementType.Brace, "VZPERA", 1),
            new(TimberElementType.TieBeam, "VAZNY_TRAM", 6),
        },
    };
}

public sealed class ElementLayerStyle
{
    public ElementLayerStyle()
    {
    }

    public ElementLayerStyle(TimberElementType elementType, string layerName, int colorIndex)
    {
        ElementType = elementType;
        LayerName = layerName;
        ColorIndex = colorIndex;
    }

    public TimberElementType ElementType { get; set; }
    public string LayerName { get; set; } = string.Empty;
    public int ColorIndex { get; set; }
}
