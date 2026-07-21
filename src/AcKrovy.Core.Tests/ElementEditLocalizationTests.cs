using System.Globalization;
using AcKrovy.Core.Models;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Core.Tests;

[Collection(LocalizationCultureCollection.CollectionName)]
public sealed class ElementEditLocalizationTests
{
    [Theory]
    [InlineData("sk", "en-US", "Krokva", "Ručne zadaná dĺžka", "Normálny (začiatok → koniec)")]
    [InlineData("cs", "en-US", "Krokev", "Ručně zadaná délka", "Normální (začátek → konec)")]
    [InlineData("en", "sk-SK", "Rafter", "Manually entered length", "Normal (start → end)")]
    [InlineData("de", "en-US", "Sparren", "Manuell eingegebene Länge", "Normal (Anfang → Ende)")]
    [InlineData("pl", "en-US", "Krokiew", "Długość wprowadzona ręcznie", "Normalny (początek → koniec)")]
    [InlineData("fr", "en-US", "Chevron", "Longueur saisie manuellement", "Normal (début → fin)")]
    public void DynamicEditDisplayTextsUseActiveAppLanguageInsteadOfCommandThreadCulture(
        string languageCode,
        string commandThreadCulture,
        string expectedElementType,
        string expectedLengthMode,
        string expectedSlopeDirection)
    {
        var previousLanguage = AppLanguageService.CurrentLanguageCode;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var previousBindingCulture = UiStringBindingSource.Shared.Culture;
        try
        {
            AppLanguageService.Apply(languageCode);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(commandThreadCulture);
            var activeCulture = AppLanguageService.CurrentUiCulture;

            Assert.Equal(languageCode, activeCulture.Name);
            Assert.Equal(
                expectedElementType,
                TimberElementTypeDisplayNameProvider.GetDisplayName(
                    TimberElementType.Rafter,
                    activeCulture));
            Assert.Equal(
                expectedLengthMode,
                LengthCalculationModeDisplayNameProvider.GetDisplayName(
                    LengthCalculationMode.ManualLength,
                    activeCulture));
            Assert.Equal(
                expectedSlopeDirection,
                SlopeDirectionDisplayNameProvider.GetDisplayName(
                    isReversed: false,
                    culture: activeCulture));
        }
        finally
        {
            AppLanguageService.Apply(previousLanguage);
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUiCulture;
            UiStringBindingSource.Shared.Culture = previousBindingCulture;
        }
    }
}
