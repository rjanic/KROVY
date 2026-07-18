namespace AcKrovy.Core.Models;

public sealed class TimberElementDefaultProfile
{
    public const double FactoryCuttingAllowanceMm = 100d;

    public int Version { get; set; } = 1;
    public List<TimberElementDefaultStyle> Styles { get; set; } = new();

    public double GetCuttingAllowanceMm(TimberElementType type)
    {
        var stored = Styles.FirstOrDefault(style => style.ElementType == type);
        return stored is null
            ? FactoryCuttingAllowanceMm
            : Math.Max(0, stored.CuttingAllowanceMm);
    }

    public TimberElementDefaultProfile Normalize()
    {
        return new TimberElementDefaultProfile
        {
            Version = Version <= 0 ? 1 : Version,
            Styles = Enum
                .GetValues(typeof(TimberElementType))
                .Cast<TimberElementType>()
                .Select(type => new TimberElementDefaultStyle(type, GetCuttingAllowanceMm(type)))
                .ToList(),
        };
    }

    public static TimberElementDefaultProfile CreateDefault() => new()
    {
        Styles = Enum
            .GetValues(typeof(TimberElementType))
            .Cast<TimberElementType>()
            .Select(type => new TimberElementDefaultStyle(type, FactoryCuttingAllowanceMm))
            .ToList(),
    };
}

public sealed class TimberElementDefaultStyle
{
    public TimberElementDefaultStyle()
    {
    }

    public TimberElementDefaultStyle(TimberElementType elementType, double cuttingAllowanceMm)
    {
        ElementType = elementType;
        CuttingAllowanceMm = cuttingAllowanceMm;
    }

    public TimberElementType ElementType { get; set; }
    public double CuttingAllowanceMm { get; set; }
}
