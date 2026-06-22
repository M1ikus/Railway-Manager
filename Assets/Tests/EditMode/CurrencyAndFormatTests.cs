using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-Economy Faza 2: CurrencyService (konwersja PLN→waluta wyświetlania + symbol) + integracja
    /// z NumberFormatService.FormatCurrency. Baza zawsze PLN; default PLN = backward-compat "zł".
    /// </summary>
    public class CurrencyAndFormatTests
    {
        Currency _backup;

        [SetUp] public void SetUp() { _backup = CurrencyService.DisplayCurrency; }
        [TearDown] public void TearDown() { CurrencyService.DisplayCurrency = _backup; }

        [Test]
        public void ConvertFromPln_PerCurrency()
        {
            Assert.That(CurrencyService.ConvertFromPln(1000m, Currency.PLN), Is.EqualTo(1000m), "PLN→PLN identity.");
            Assert.That(CurrencyService.ConvertFromPln(430m, Currency.EUR), Is.EqualTo(100m), "430 zł / 4.30 = 100 €.");
            Assert.That(CurrencyService.ConvertFromPln(400m, Currency.USD), Is.EqualTo(100m), "400 zł / 4.00 = 100 $.");
            Assert.That(CurrencyService.ConvertFromPln(17m,  Currency.CZK), Is.EqualTo(100m), "17 zł / 0.17 = 100 Kč.");
        }

        [Test]
        public void Symbol_And_Prefix_PerCurrency()
        {
            Assert.That(CurrencyService.Symbol(Currency.PLN), Is.EqualTo("zł"));
            Assert.That(CurrencyService.Symbol(Currency.EUR), Is.EqualTo("€"));
            Assert.That(CurrencyService.Symbol(Currency.USD), Is.EqualTo("$"));
            Assert.That(CurrencyService.Symbol(Currency.CZK), Is.EqualTo("Kč"));
            Assert.That(CurrencyService.IsPrefixSymbol(Currency.USD), Is.True,  "USD prefix ($100).");
            Assert.That(CurrencyService.IsPrefixSymbol(Currency.PLN), Is.False, "zł suffix.");
        }

        [Test]
        public void FormatCurrency_DefaultPln_BackwardCompatZl()
        {
            CurrencyService.DisplayCurrency = Currency.PLN;
            string s = NumberFormatService.FormatCurrency(1234L, LocaleCode.PL);
            Assert.That(s, Does.Contain("zł"), "Default PLN — suffix zł jak przed Fazą 2.");
            Assert.That(s, Does.Contain("1"), "Kwota obecna.");
        }

        [Test]
        public void FormatCurrency_Eur_ConvertsAndSymbol()
        {
            CurrencyService.DisplayCurrency = Currency.EUR;
            string s = NumberFormatService.FormatCurrency(430L, LocaleCode.PL);
            Assert.That(s, Does.Contain("€"), "Symbol euro.");
            Assert.That(s, Does.Contain("100"), "430 zł → 100 € (kurs 4.30).");
        }

        [Test]
        public void FormatCurrency_Usd_PrefixSymbol()
        {
            CurrencyService.DisplayCurrency = Currency.USD;
            string s = NumberFormatService.FormatCurrency(400L, LocaleCode.PL);
            Assert.That(s, Does.Contain("$"), "Symbol dolara.");
            Assert.That(s, Does.Contain("100"), "400 zł → 100 $ (kurs 4.00).");
        }
    }
}
