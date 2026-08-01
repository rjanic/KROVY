using AcKrovy.Core.Services;

namespace AcKrovy.Core.Models;

public enum TimberAnnotationTextSettingsChange
{
    Unchanged = 0,
    Set = 1,
}

public sealed record TimberAnnotationTextSettingsPatch
{
    public static TimberAnnotationTextSettingsPatch Unchanged { get; } =
        new(TimberAnnotationTextSettingsChange.Unchanged, null);

    public TimberAnnotationTextSettingsChange Change { get; }
    public TimberAnnotationTextSettings? Settings { get; }

    private TimberAnnotationTextSettingsPatch(
        TimberAnnotationTextSettingsChange change,
        TimberAnnotationTextSettings? settings)
    {
        Change = change;
        Settings = settings;
    }

    public static TimberAnnotationTextSettingsPatch Set(
        TimberAnnotationTextSettings settings) =>
        new(
            TimberAnnotationTextSettingsChange.Set,
            TimberAnnotationTextSettingsRules.ValidateAndNormalize(settings));
}
