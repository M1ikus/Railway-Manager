using NUnit.Framework;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M13-3: testy LocalizationService — kontrakt lookup + fallback chain (current → EN → "[key]").
    /// EditMode. Testuje DETERMINISTYCZNE ścieżki (null/empty/nieznany klucz/format) odporne na
    /// zawartość strings.json — NIE polega na konkretnych istniejących kluczach (to byłoby kruche).
    /// </summary>
    public class LocalizationServiceTests
    {
        // Klucz gwarantowanie nieistniejący w żadnym locale.
        const string MissingKey = "__test_missing_key_xyzzy_998877__";

        [Test]
        public void Get_NullOrEmptyKey_ReturnsEmpty()
        {
            Assert.That(LocalizationService.Get(null), Is.EqualTo(""));
            Assert.That(LocalizationService.Get(""), Is.EqualTo(""));
        }

        [Test]
        public void Get_UnknownKey_ReturnsBracketedPlaceholder()
        {
            // Fallback końcowy: nieznany klucz → "[key]" (debug placeholder, widoczny w UI = brakujące tłumaczenie).
            Assert.That(LocalizationService.Get(MissingKey), Is.EqualTo($"[{MissingKey}]"));
        }

        [Test]
        public void HasKey_UnknownKey_False()
        {
            Assert.That(LocalizationService.HasKey(MissingKey), Is.False);
        }

        [Test]
        public void Get_WithArgs_UnknownKey_DoesNotThrow()
        {
            // Get(key, args) na nieznanym kluczu: template = "[key]" (brak {0}), string.Format bezpieczny.
            string result = LocalizationService.Get(MissingKey, 42, "x");
            Assert.That(result, Is.EqualTo($"[{MissingKey}]"), "Args nie psują placeholdera (brak {0} w template).");
        }

        [Test]
        public void Get_WithNullArgs_ReturnsTemplate()
        {
            Assert.That(LocalizationService.Get(MissingKey, null), Is.EqualTo($"[{MissingKey}]"));
        }

        [Test]
        public void Get_WithEmptyArgs_ReturnsTemplate()
        {
            Assert.That(LocalizationService.Get(MissingKey, new object[0]), Is.EqualTo($"[{MissingKey}]"));
        }

        [Test]
        public void CurrentLocale_IsValidEnumValue()
        {
            // CurrentLocale zawsze jest poprawną wartością enum (default EN przed SetLocale).
            Assert.That(System.Enum.IsDefined(typeof(LocaleCode), LocalizationService.CurrentLocale), Is.True);
        }
    }
}
