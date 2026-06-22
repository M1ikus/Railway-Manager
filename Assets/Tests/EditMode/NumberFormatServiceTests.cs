using NUnit.Framework;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M13-3: testy NumberFormatService — formatowanie waluty/liczb/procentów per locale.
    /// localeOverride czyni je deterministycznymi (bez polegania na current locale). EditMode.
    ///
    /// Kluczowy kontrakt (bug-fix 2026-05-15): waluta ZAWSZE "zł" suffix niezależnie od locale —
    /// gracz EN/DE/JP NIE widzi $/€/¥. Asercje odporne (Contains) — nie zamrażają dokładnych
    /// separatorów (zależą od danych kultury Unity Mono). Główne locale: PL/EN/DE.
    /// </summary>
    public class NumberFormatServiceTests
    {
        [Test]
        public void Currency_AlwaysHasZlSuffix_RegardlessOfLocale()
        {
            Assert.That(NumberFormatService.FormatCurrency(1234.56m, LocaleCode.PL), Does.Contain("zł"));
            Assert.That(NumberFormatService.FormatCurrency(1234.56m, LocaleCode.EN), Does.Contain("zł"));
            Assert.That(NumberFormatService.FormatCurrency(1234.56m, LocaleCode.DE), Does.Contain("zł"));
        }

        [Test]
        public void Currency_NoForeignSymbol_EvenInEnglishGerman()
        {
            // Bug-fix: EN nie może pokazać "$", DE nie "€" (waluta gry to zawsze PLN).
            string en = NumberFormatService.FormatCurrency(1234.56m, LocaleCode.EN);
            string de = NumberFormatService.FormatCurrency(1234.56m, LocaleCode.DE);
            Assert.That(en, Does.Not.Contain("$"), "EN locale NIE pokazuje dolara.");
            Assert.That(de, Does.Not.Contain("€"), "DE locale NIE pokazuje euro.");
        }

        [Test]
        public void Currency_LongOverload_NoDecimals()
        {
            // long overload → 0 miejsc po przecinku (typowe money UI tycoona).
            string s = NumberFormatService.FormatCurrency(5000L, LocaleCode.PL);
            Assert.That(s, Does.Contain("5"));
            Assert.That(s, Does.Contain("zł"));
            // 0 decimals → brak ",00" / ".00" części dziesiętnej
            Assert.That(s, Does.Not.Contain(",00").And.Not.Contain(".00"),
                "long overload bez miejsc dziesiętnych.");
        }

        [Test]
        public void Currency_ContainsDigits()
        {
            // 1234.56 → string zawiera cyfry kwoty (separatory zależne od locale).
            string pl = NumberFormatService.FormatCurrency(1234.56m, LocaleCode.PL);
            Assert.That(pl, Does.Contain("1").And.Contain("234"), "Cyfry kwoty obecne.");
            Assert.That(pl, Does.Contain("56"), "Część dziesiętna obecna (default 2 miejsca).");
        }

        [Test]
        public void Currency_NegativeAmount_Formatted()
        {
            // Ujemna kwota (np. strata) → ma znak minus + "zł".
            string s = NumberFormatService.FormatCurrency(-500.00m, LocaleCode.PL);
            Assert.That(s, Does.Contain("zł"));
            Assert.That(s, Does.Contain("500"));
            Assert.That(s, Does.Contain("-").Or.Contain("("), "Ujemna kwota oznaczona minusem/nawiasem.");
        }

        [Test]
        public void Percent_FormatsFractionAsPercent()
        {
            // 0.85 → "85%" (P0). Asercja odporna: zawiera "85" i "%".
            string s = NumberFormatService.FormatPercent(0.85, 0, LocaleCode.PL);
            Assert.That(s, Does.Contain("85"));
            Assert.That(s, Does.Contain("%"));
        }

        [Test]
        public void Percent_WithDecimals()
        {
            // 0.8567 z 1 miejscem → "85,7%" / "85.7%" (zależnie od locale separatora).
            string s = NumberFormatService.FormatPercent(0.8567, 1, LocaleCode.PL);
            Assert.That(s, Does.Contain("85"));
            Assert.That(s, Does.Contain("%"));
        }

        [Test]
        public void Number_HasThousandsSeparator()
        {
            // 1234567 → separatory tysięcy (PL spacja, EN przecinek) — sprawdzamy że NIE jest goły ciąg.
            string s = NumberFormatService.FormatNumber(1234567L, LocaleCode.PL);
            Assert.That(s, Does.Contain("1").And.Contain("234").And.Contain("567"),
                "Wszystkie grupy cyfr obecne.");
            Assert.That(s, Is.Not.EqualTo("1234567"), "Separatory tysięcy zastosowane (nie goły ciąg).");
        }

        [Test]
        public void Number_IntOverload_NoDecimals()
        {
            string s = NumberFormatService.FormatNumber(42, LocaleCode.PL);
            Assert.That(s, Is.EqualTo("42"), "Mała liczba int bez separatorów i bez miejsc dziesiętnych.");
        }

        [Test]
        public void LocaleSeparators_DifferBetweenPlAndEn()
        {
            // PL i EN używają różnych separatorów → sformatowana ta sama liczba daje różne stringi.
            // (Nie zamrażamy KTÓRYCH separatorów — tylko że się różnią, co jest sednem lokalizacji.)
            string pl = NumberFormatService.FormatNumber(1234567.89m, 2, LocaleCode.PL);
            string en = NumberFormatService.FormatNumber(1234567.89m, 2, LocaleCode.EN);
            Assert.That(pl, Is.Not.EqualTo(en), "PL i EN formatują liczby różnie (lokalizacja separatorów).");
        }
    }
}
