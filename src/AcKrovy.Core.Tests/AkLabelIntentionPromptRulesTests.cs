using System.Xml.Linq;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class AkLabelIntentionPromptRulesTests
{
    private const string SkMissingLocal = "Chýbajúce";
    private const string SkSelectLocal = "Vybrať";
    private const string SkAllLocal = "ObnovitVsetky";
    private const string SkMissingDisplay = "Chýbajúce";
    private const string SkSelectDisplay = "Vybrať";
    private const string SkAllDisplay = "Obnoviť všetky";

    [Theory]
    [InlineData(null, true, AkLabelIntention.MissingOnly)]
    [InlineData("", true, AkLabelIntention.MissingOnly)]
    [InlineData("Missing", false, AkLabelIntention.MissingOnly)]
    [InlineData("C", false, AkLabelIntention.MissingOnly)]
    [InlineData("c", false, AkLabelIntention.MissingOnly)]
    [InlineData("Chýbajúce", false, AkLabelIntention.MissingOnly)]
    [InlineData("Select", false, AkLabelIntention.ResetSelected)]
    [InlineData("V", false, AkLabelIntention.ResetSelected)]
    [InlineData("v", false, AkLabelIntention.ResetSelected)]
    [InlineData("Vybrať", false, AkLabelIntention.ResetSelected)]
    [InlineData("All", false, AkLabelIntention.ResetAll)]
    [InlineData("O", false, AkLabelIntention.ResetAll)]
    [InlineData("o", false, AkLabelIntention.ResetAll)]
    [InlineData("ObnovitVsetky", false, AkLabelIntention.ResetAll)]
    [InlineData("Obnoviť všetky", false, AkLabelIntention.ResetAll)]
    public void Slovak_MapsEnterGlobalsLocalsDisplaysAndInitials(
        string? raw,
        bool isNone,
        AkLabelIntention expected)
    {
        Assert.Equal(expected, ParseSk(raw, isNone));
    }

    [Fact]
    public void Slovak_AllDisplay_IsNeverMappedToSelect()
    {
        var intention = ParseSk("Obnoviť všetky", isNone: false);
        Assert.Equal(AkLabelIntention.ResetAll, intention);
        Assert.NotEqual(AkLabelIntention.ResetSelected, intention);
    }

    [Fact]
    public void Slovak_LocalInitials_AreUnique_CVO()
    {
        Assert.True(AkLabelIntentionPromptRules.HaveUniqueLocalInitials(
            SkMissingLocal,
            SkSelectLocal,
            SkAllLocal));
        Assert.Equal('C', AkLabelIntentionPromptRules.GetLocalInitial(SkMissingLocal));
        Assert.Equal('V', AkLabelIntentionPromptRules.GetLocalInitial(SkSelectLocal));
        Assert.Equal('O', AkLabelIntentionPromptRules.GetLocalInitial(SkAllLocal));
    }

    [Theory]
    [InlineData("Chybějící", "Vybrat", "ObnovitVse", 'C', 'V', 'O')]
    [InlineData("Missing", "Select", "All", 'M', 'S', 'A')]
    [InlineData("Fehlende", "Auswählen", "Zuruecksetzen", 'F', 'A', 'Z')]
    [InlineData("Brakujące", "Wybierz", "PrzywrocWszystkie", 'B', 'W', 'P')]
    [InlineData("Manquantes", "Sélectionner", "RestaurerToutes", 'M', 'S', 'R')]
    public void AllLanguages_LocalInitials_AreUnique(
        string missingLocal,
        string selectLocal,
        string allLocal,
        char missingInitial,
        char selectInitial,
        char allInitial)
    {
        Assert.True(AkLabelIntentionPromptRules.HaveUniqueLocalInitials(
            missingLocal,
            selectLocal,
            allLocal));
        Assert.Equal(missingInitial, AkLabelIntentionPromptRules.GetLocalInitial(missingLocal));
        Assert.Equal(selectInitial, AkLabelIntentionPromptRules.GetLocalInitial(selectLocal));
        Assert.Equal(allInitial, AkLabelIntentionPromptRules.GetLocalInitial(allLocal));
    }

    [Theory]
    [InlineData("Chybějící", "Vybrat", "ObnovitVse", "Chybějící", "Vybrat", "Obnovit vše")]
    [InlineData("Missing", "Select", "All", "Missing", "Select", "All")]
    [InlineData("Fehlende", "Auswählen", "Zuruecksetzen", "Fehlende", "Auswählen", "Zurücksetzen: alle")]
    [InlineData("Brakujące", "Wybierz", "PrzywrocWszystkie", "Brakujące", "Wybierz", "Przywróć wszystkie")]
    [InlineData("Manquantes", "Sélectionner", "RestaurerToutes", "Manquantes", "Sélectionner", "Restaurer toutes")]
    public void Localized_GlobalsLocalsDisplays_MapCorrectly(
        string missingLocal,
        string selectLocal,
        string allLocal,
        string missingDisplay,
        string selectDisplay,
        string allDisplay)
    {
        Assert.Equal(
            AkLabelIntention.MissingOnly,
            AkLabelIntentionPromptRules.Parse(
                "Missing", false, missingLocal, selectLocal, allLocal,
                missingDisplay, selectDisplay, allDisplay));
        Assert.Equal(
            AkLabelIntention.ResetSelected,
            AkLabelIntentionPromptRules.Parse(
                "Select", false, missingLocal, selectLocal, allLocal,
                missingDisplay, selectDisplay, allDisplay));
        Assert.Equal(
            AkLabelIntention.ResetAll,
            AkLabelIntentionPromptRules.Parse(
                "All", false, missingLocal, selectLocal, allLocal,
                missingDisplay, selectDisplay, allDisplay));

        Assert.Equal(
            AkLabelIntention.MissingOnly,
            AkLabelIntentionPromptRules.Parse(
                missingLocal, false, missingLocal, selectLocal, allLocal,
                missingDisplay, selectDisplay, allDisplay));
        Assert.Equal(
            AkLabelIntention.ResetSelected,
            AkLabelIntentionPromptRules.Parse(
                selectLocal, false, missingLocal, selectLocal, allLocal,
                missingDisplay, selectDisplay, allDisplay));
        Assert.Equal(
            AkLabelIntention.ResetAll,
            AkLabelIntentionPromptRules.Parse(
                allLocal, false, missingLocal, selectLocal, allLocal,
                missingDisplay, selectDisplay, allDisplay));

        Assert.Equal(
            AkLabelIntention.MissingOnly,
            AkLabelIntentionPromptRules.Parse(
                missingDisplay, false, missingLocal, selectLocal, allLocal,
                missingDisplay, selectDisplay, allDisplay));
        Assert.Equal(
            AkLabelIntention.ResetSelected,
            AkLabelIntentionPromptRules.Parse(
                selectDisplay, false, missingLocal, selectLocal, allLocal,
                missingDisplay, selectDisplay, allDisplay));
        Assert.Equal(
            AkLabelIntention.ResetAll,
            AkLabelIntentionPromptRules.Parse(
                allDisplay, false, missingLocal, selectLocal, allLocal,
                missingDisplay, selectDisplay, allDisplay));

        Assert.Equal(
            AkLabelIntention.MissingOnly,
            AkLabelIntentionPromptRules.Parse(
                AkLabelIntentionPromptRules.GetLocalInitial(missingLocal).ToString(),
                false, missingLocal, selectLocal, allLocal,
                missingDisplay, selectDisplay, allDisplay));
        Assert.Equal(
            AkLabelIntention.ResetSelected,
            AkLabelIntentionPromptRules.Parse(
                AkLabelIntentionPromptRules.GetLocalInitial(selectLocal).ToString(),
                false, missingLocal, selectLocal, allLocal,
                missingDisplay, selectDisplay, allDisplay));
        Assert.Equal(
            AkLabelIntention.ResetAll,
            AkLabelIntentionPromptRules.Parse(
                AkLabelIntentionPromptRules.GetLocalInitial(allLocal).ToString(),
                false, missingLocal, selectLocal, allLocal,
                missingDisplay, selectDisplay, allDisplay));
    }

    [Fact]
    public void UnknownToken_DefaultsToMissingOnly_NotSelect()
    {
        Assert.Equal(AkLabelIntention.MissingOnly, ParseSk("SomethingElse", isNone: false));
    }

    [Theory]
    [InlineData(null, true, AkLabelIntention.MissingOnly)]
    [InlineData("", true, AkLabelIntention.MissingOnly)]
    [InlineData("Missing", false, AkLabelIntention.MissingOnly)]
    [InlineData("F", false, AkLabelIntention.MissingOnly)]
    [InlineData("f", false, AkLabelIntention.MissingOnly)]
    [InlineData("Fehlende", false, AkLabelIntention.MissingOnly)]
    [InlineData("Select", false, AkLabelIntention.ResetSelected)]
    [InlineData("A", false, AkLabelIntention.ResetSelected)]
    [InlineData("a", false, AkLabelIntention.ResetSelected)]
    [InlineData("Auswählen", false, AkLabelIntention.ResetSelected)]
    [InlineData("All", false, AkLabelIntention.ResetAll)]
    [InlineData("ResetAll", false, AkLabelIntention.ResetAll)]
    [InlineData("Z", false, AkLabelIntention.ResetAll)]
    [InlineData("z", false, AkLabelIntention.ResetAll)]
    [InlineData("Zuruecksetzen", false, AkLabelIntention.ResetAll)]
    [InlineData("Zurücksetzen: alle", false, AkLabelIntention.ResetAll)]
    public void German_MapsEnterGlobalsLocalsDisplaysAndInitials(
        string? raw,
        bool isNone,
        AkLabelIntention expected)
    {
        Assert.Equal(expected, ParseDe(raw, isNone));
    }

    [Fact]
    public void German_DisplayInitials_AreUnique_F_A_Z()
    {
        const string missingDisplay = "Fehlende";
        const string selectDisplay = "Auswählen";
        const string allDisplay = "Zurücksetzen: alle";

        Assert.Equal('F', AkLabelIntentionPromptRules.GetLocalInitial(missingDisplay));
        Assert.Equal('A', AkLabelIntentionPromptRules.GetLocalInitial(selectDisplay));
        Assert.Equal('Z', AkLabelIntentionPromptRules.GetLocalInitial(allDisplay));
        Assert.True(AkLabelIntentionPromptRules.HaveUniqueLocalInitials(
            missingDisplay,
            selectDisplay,
            allDisplay));

        // Old colliding display must not remain the registered ResetAll display.
        Assert.Equal(
            'A',
            AkLabelIntentionPromptRules.GetLocalInitial("Alle zurücksetzen"));
        Assert.False(AkLabelIntentionPromptRules.HaveUniqueLocalInitials(
            missingDisplay,
            selectDisplay,
            "Alle zurücksetzen"));

        Assert.Equal(
            AkLabelIntention.ResetAll,
            ParseDe("Zurücksetzen: alle", isNone: false));
        Assert.Equal(
            AkLabelIntention.ResetSelected,
            ParseDe("Auswählen", isNone: false));
        Assert.Equal(
            AkLabelIntention.ResetAll,
            ParseDe("Z", isNone: false));
        Assert.Equal(
            AkLabelIntention.ResetAll,
            ParseDe("ResetAll", isNone: false));
    }

    [Fact]
    public void German_SelectLocal_CollidesWithAllGlobal_RequiresDisambiguatedRegistration()
    {
        Assert.True(AkLabelIntentionPromptRules.HasAllGlobalSelectLocalCollision("Auswählen"));
        Assert.Equal(
            AkLabelIntentionPromptRules.GlobalAllDisambiguated,
            AkLabelIntentionPromptRules.ResolveRegisteredAllGlobal("Auswählen"));
        Assert.True(AkLabelIntentionPromptRules.HaveUniqueRegisteredAllInitial("Auswählen"));
        Assert.Equal('F', AkLabelIntentionPromptRules.GetLocalInitial("Fehlende"));
        Assert.Equal('A', AkLabelIntentionPromptRules.GetLocalInitial("Auswählen"));
        Assert.Equal('Z', AkLabelIntentionPromptRules.GetLocalInitial("Zuruecksetzen"));
    }

    [Theory]
    [InlineData("Vybrať", "All")]
    [InlineData("Vybrat", "All")]
    [InlineData("Select", "All")]
    [InlineData("Wybierz", "All")]
    [InlineData("Sélectionner", "All")]
    public void NonGermanSelectLocals_KeepRegisteredAllGlobal(
        string selectLocal,
        string expectedRegisteredAllGlobal)
    {
        Assert.False(AkLabelIntentionPromptRules.HasAllGlobalSelectLocalCollision(selectLocal));
        Assert.Equal(
            expectedRegisteredAllGlobal,
            AkLabelIntentionPromptRules.ResolveRegisteredAllGlobal(selectLocal));
    }

    [Fact]
    public void GermanResx_KeepsApprovedDisplayAndLocalTokens()
    {
        var path = Path.Combine(
            FindRepoRoot(),
            "src",
            "AcKrovy.Localization",
            "Resources",
            "UiStrings.de.resx");
        var document = XDocument.Load(path);
        string Value(string name) =>
            document.Root!
                .Elements("data")
                .First(e => (string?)e.Attribute("name") == name)
                .Element("value")!
                .Value;

        Assert.Equal("Fehlende", Value("Command_Labels_KeywordMissingLocal"));
        Assert.Equal("Auswählen", Value("Command_Labels_KeywordSelectLocal"));
        Assert.Equal("Zuruecksetzen", Value("Command_Labels_KeywordAllLocal"));
        Assert.Equal("Fehlende", Value("Command_Labels_KeywordMissing"));
        Assert.Equal("Auswählen", Value("Command_Labels_KeywordSelect"));
        Assert.Equal("Zurücksetzen: alle", Value("Command_Labels_KeywordAll"));
        Assert.Contains(
            "[Fehlende/Auswählen/Zurücksetzen: alle] <Fehlende>:",
            Value("Command_Labels_IntentionPrompt")
            .Replace("&#xA;", "\n", StringComparison.Ordinal)
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal));

        // Confirm-button wording may still say "Alle zurücksetzen"; DynInput display must not.
        Assert.Equal("Alle zurücksetzen", Value("AkLabelResetAllConfirm_Confirm"));
        Assert.NotEqual(
            Value("AkLabelResetAllConfirm_Confirm"),
            Value("Command_Labels_KeywordAll"));
    }

    [Fact]
    public void ResxLocals_HaveUniqueInitials_InEveryLanguagePack()
    {
        foreach (var relative in new[]
                 {
                     "src/AcKrovy.Localization/Resources/UiStrings.resx",
                     "src/AcKrovy.Localization/Resources/UiStrings.cs.resx",
                     "src/AcKrovy.Localization/Resources/UiStrings.en.resx",
                     "src/AcKrovy.Localization/Resources/UiStrings.de.resx",
                     "src/AcKrovy.Localization/Resources/UiStrings.pl.resx",
                     "src/AcKrovy.Localization/Resources/UiStrings.fr.resx",
                 })
        {
            var values = ReadKeywordLocals(relative);
            Assert.True(
                AkLabelIntentionPromptRules.HaveUniqueLocalInitials(
                    values.MissingLocal,
                    values.SelectLocal,
                    values.AllLocal),
                $"Local keyword initials collide in {relative}: " +
                $"{values.MissingLocal}/{values.SelectLocal}/{values.AllLocal}");
            Assert.True(
                AkLabelIntentionPromptRules.HaveUniqueRegisteredAllInitial(values.SelectLocal),
                $"Registered All global still collides with Select local in {relative}");

            var displays = ReadKeywordDisplays(relative);
            Assert.True(
                AkLabelIntentionPromptRules.HaveUniqueLocalInitials(
                    displays.MissingDisplay,
                    displays.SelectDisplay,
                    displays.AllDisplay),
                $"Display keyword initials collide in {relative}: " +
                $"{displays.MissingDisplay}/{displays.SelectDisplay}/{displays.AllDisplay}");
        }
    }

    private static AkLabelIntention ParseSk(string? raw, bool isNone) =>
        AkLabelIntentionPromptRules.Parse(
            raw,
            isNone,
            SkMissingLocal,
            SkSelectLocal,
            SkAllLocal,
            SkMissingDisplay,
            SkSelectDisplay,
            SkAllDisplay);

    private static AkLabelIntention ParseDe(string? raw, bool isNone) =>
        AkLabelIntentionPromptRules.Parse(
            raw,
            isNone,
            "Fehlende",
            "Auswählen",
            "Zuruecksetzen",
            "Fehlende",
            "Auswählen",
            "Zurücksetzen: alle");

    private static (string MissingLocal, string SelectLocal, string AllLocal) ReadKeywordLocals(
        string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        var document = XDocument.Load(path);
        string Value(string name) =>
            document.Root!
                .Elements("data")
                .First(e => (string?)e.Attribute("name") == name)
                .Element("value")!
                .Value;

        return (
            Value("Command_Labels_KeywordMissingLocal"),
            Value("Command_Labels_KeywordSelectLocal"),
            Value("Command_Labels_KeywordAllLocal"));
    }

    private static (string MissingDisplay, string SelectDisplay, string AllDisplay) ReadKeywordDisplays(
        string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        var document = XDocument.Load(path);
        string Value(string name) =>
            document.Root!
                .Elements("data")
                .First(e => (string?)e.Attribute("name") == name)
                .Element("value")!
                .Value;

        return (
            Value("Command_Labels_KeywordMissing"),
            Value("Command_Labels_KeywordSelect"),
            Value("Command_Labels_KeywordAll"));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AcKrovy.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
