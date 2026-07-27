using AcKrovy.AutoCAD.ClassicToolbar;
using AcKrovy.AutoCAD.Ribbon;
using AcKrovy.Localization;

namespace AcKrovy.AutoCAD.Settings;

internal interface IApplicationLanguageWorkflow
{
    bool TryApplyUserSelection(string languageCode);
}

internal sealed class ApplicationLanguageWorkflow : IApplicationLanguageWorkflow
{
    private readonly Func<string> _currentLanguageCode;
    private readonly Action<string> _applyLanguage;
    private readonly Action<string> _saveLanguage;
    private readonly Action _refreshLocalizedUi;
    private bool _isApplying;

    internal ApplicationLanguageWorkflow(
        Func<string> currentLanguageCode,
        Action<string> applyLanguage,
        Action<string> saveLanguage,
        Action refreshLocalizedUi)
    {
        _currentLanguageCode = currentLanguageCode ??
            throw new ArgumentNullException(nameof(currentLanguageCode));
        _applyLanguage = applyLanguage ??
            throw new ArgumentNullException(nameof(applyLanguage));
        _saveLanguage = saveLanguage ??
            throw new ArgumentNullException(nameof(saveLanguage));
        _refreshLocalizedUi = refreshLocalizedUi ??
            throw new ArgumentNullException(nameof(refreshLocalizedUi));
    }

    public bool TryApplyUserSelection(string languageCode)
    {
        var normalized = AppLanguageService.NormalizeLanguageCode(languageCode);
        if (_isApplying ||
            string.Equals(
                AppLanguageService.NormalizeLanguageCode(_currentLanguageCode()),
                normalized,
                StringComparison.Ordinal))
        {
            return false;
        }

        _isApplying = true;
        try
        {
            _applyLanguage(normalized);
            try
            {
                _saveLanguage(normalized);
            }
            catch
            {
                // Persistence is UI-only and must not undo an active language
                // change or block the current drawing.
            }

            _refreshLocalizedUi();
            return true;
        }
        finally
        {
            _isApplying = false;
        }
    }
}

internal static class AutoCadApplicationLanguageWorkflow
{
    public static IApplicationLanguageWorkflow Shared { get; } =
        new ApplicationLanguageWorkflow(
            () => AppLanguageService.CurrentLanguageCode,
            languageCode => _ = AppLanguageService.Apply(languageCode),
            languageCode => AppLanguageSettingsStore.Save(
                new AppLanguageSettings
                {
                    LanguageCode = languageCode,
                }),
            RefreshLocalizedUi);

    private static void RefreshLocalizedUi()
    {
        if (!AcKrovyRibbon.RebuildLocalizedUi(activateTab: false))
        {
            AcKrovyRibbon.ScheduleCreation();
        }

        ClassicToolbarManager.RefreshLocalizedContent();
    }
}
