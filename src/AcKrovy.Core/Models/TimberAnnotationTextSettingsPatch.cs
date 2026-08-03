using AcKrovy.Core.Services;

namespace AcKrovy.Core.Models;

public enum TimberAnnotationTextSettingsChange
{
    Unchanged = 0,
    Set = 1,
}

/// <summary>
/// Role-scoped change for one annotation text role. A role is either left
/// untouched or set to an explicit style name plus paper height, so applying a
/// patch never disturbs the other two roles.
/// </summary>
public sealed record TimberAnnotationTextRolePatch
{
    public static TimberAnnotationTextRolePatch Unchanged { get; } =
        new(TimberAnnotationTextSettingsChange.Unchanged, null, 0d);

    public TimberAnnotationTextSettingsChange Change { get; }
    public string? TextStyleName { get; }
    public double PaperHeightMm { get; }

    private TimberAnnotationTextRolePatch(
        TimberAnnotationTextSettingsChange change,
        string? textStyleName,
        double paperHeightMm)
    {
        Change = change;
        TextStyleName = textStyleName;
        PaperHeightMm = paperHeightMm;
    }

    public static TimberAnnotationTextRolePatch Set(
        TimberAnnotationTextRole role,
        string textStyleName,
        double paperHeightMm) =>
        new(
            TimberAnnotationTextSettingsChange.Set,
            TimberAnnotationTextSettingsRules.ValidateAndNormalizeTextStyleName(
                textStyleName,
                nameof(textStyleName)),
            TimberAnnotationTextSettingsRules.ValidatePaperHeightMm(
                role,
                paperHeightMm,
                nameof(paperHeightMm)));
}

/// <summary>
/// Independent patches for the item-code, dimension and slope text roles.
/// An all-unchanged patch is a no-op and keeps legacy null settings null.
/// </summary>
public sealed record TimberAnnotationTextSettingsPatch
{
    public static TimberAnnotationTextSettingsPatch Unchanged { get; } = new(
        TimberAnnotationTextRolePatch.Unchanged,
        TimberAnnotationTextRolePatch.Unchanged,
        TimberAnnotationTextRolePatch.Unchanged);

    public TimberAnnotationTextRolePatch ItemCode { get; }
    public TimberAnnotationTextRolePatch Dimension { get; }
    public TimberAnnotationTextRolePatch Slope { get; }

    public TimberAnnotationTextSettingsChange Change =>
        ItemCode.Change == TimberAnnotationTextSettingsChange.Unchanged &&
        Dimension.Change == TimberAnnotationTextSettingsChange.Unchanged &&
        Slope.Change == TimberAnnotationTextSettingsChange.Unchanged
            ? TimberAnnotationTextSettingsChange.Unchanged
            : TimberAnnotationTextSettingsChange.Set;

    private TimberAnnotationTextSettingsPatch(
        TimberAnnotationTextRolePatch itemCode,
        TimberAnnotationTextRolePatch dimension,
        TimberAnnotationTextRolePatch slope)
    {
        ItemCode = itemCode ?? throw new ArgumentNullException(nameof(itemCode));
        Dimension = dimension ?? throw new ArgumentNullException(nameof(dimension));
        Slope = slope ?? throw new ArgumentNullException(nameof(slope));
    }

    public static TimberAnnotationTextSettingsPatch ForRoles(
        TimberAnnotationTextRolePatch itemCode,
        TimberAnnotationTextRolePatch dimension,
        TimberAnnotationTextRolePatch slope) =>
        new(itemCode, dimension, slope);

    public static TimberAnnotationTextSettingsPatch ForRole(
        TimberAnnotationTextRole role,
        string textStyleName,
        double paperHeightMm)
    {
        var rolePatch = TimberAnnotationTextRolePatch.Set(
            role,
            textStyleName,
            paperHeightMm);
        return role switch
        {
            TimberAnnotationTextRole.ItemCode => new(
                rolePatch,
                TimberAnnotationTextRolePatch.Unchanged,
                TimberAnnotationTextRolePatch.Unchanged),
            TimberAnnotationTextRole.Dimension => new(
                TimberAnnotationTextRolePatch.Unchanged,
                rolePatch,
                TimberAnnotationTextRolePatch.Unchanged),
            TimberAnnotationTextRole.Slope => new(
                TimberAnnotationTextRolePatch.Unchanged,
                TimberAnnotationTextRolePatch.Unchanged,
                rolePatch),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    /// <summary>Sets all three roles from one validated settings value.</summary>
    public static TimberAnnotationTextSettingsPatch Set(
        TimberAnnotationTextSettings settings)
    {
        var normalized =
            TimberAnnotationTextSettingsRules.ValidateAndNormalize(settings);
        return new(
            TimberAnnotationTextRolePatch.Set(
                TimberAnnotationTextRole.ItemCode,
                normalized.ItemCodeTextStyleName,
                normalized.ItemCodePaperHeightMm),
            TimberAnnotationTextRolePatch.Set(
                TimberAnnotationTextRole.Dimension,
                normalized.DimensionTextStyleName,
                normalized.DimensionPaperHeightMm),
            TimberAnnotationTextRolePatch.Set(
                TimberAnnotationTextRole.Slope,
                normalized.SlopeTextStyleName,
                normalized.SlopePaperHeightMm));
    }

    public TimberAnnotationTextRolePatch ForRole(TimberAnnotationTextRole role) =>
        role switch
        {
            TimberAnnotationTextRole.ItemCode => ItemCode,
            TimberAnnotationTextRole.Dimension => Dimension,
            TimberAnnotationTextRole.Slope => Slope,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

    /// <summary>
    /// Applies the role patches to stored settings. An all-unchanged patch
    /// returns the current value untouched, including a legacy null. A partial
    /// patch materializes the untouched roles from the current value, or from
    /// the factory defaults when the element still has no explicit settings.
    /// </summary>
    public TimberAnnotationTextSettings? Apply(
        TimberAnnotationTextSettings? current)
    {
        if (Change == TimberAnnotationTextSettingsChange.Unchanged)
        {
            return current;
        }

        var baseline =
            TimberAnnotationTextSettingsRules.NormalizeStored(current) ??
            TimberAnnotationTextSettingsRules.Default;

        foreach (var role in Roles)
        {
            var rolePatch = ForRole(role);
            if (rolePatch.Change != TimberAnnotationTextSettingsChange.Set)
            {
                continue;
            }

            baseline = baseline.WithRole(
                role,
                rolePatch.TextStyleName!,
                rolePatch.PaperHeightMm);
        }

        return baseline;
    }

    private static readonly TimberAnnotationTextRole[] Roles =
    {
        TimberAnnotationTextRole.ItemCode,
        TimberAnnotationTextRole.Dimension,
        TimberAnnotationTextRole.Slope,
    };
}
