using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Timetable;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M4: testy TrainNumberValidator — walidacja + auto-generacja numerów pociągów IRJ.
    /// Czysta logika (IrjCategoryCatalog + ConstructionAreaCatalog mają dane wbudowane). EditMode.
    ///
    /// Reguły: format (tylko cyfry), długość per kategoria, obszar konstrukcyjny (1. cyfra =
    /// województwo startu: mazowieckie→1, dolnośląskie→6, nieznane→9), unikalność.
    /// </summary>
    public class TrainNumberValidatorTests
    {
        // Kategoria referencyjna — RegionalLocal+ElectricUnit (RO, dozwolona, ma stały digit count).
        static readonly IrjCategory Cat = new IrjCategory(IrjGroup.RegionalLocal, TractionLetter.ElectricUnit);
        static int Digits => IrjCategoryCatalog.GetDigits(Cat);

        /// <summary>Numer o poprawnej długości zaczynający się od cyfry obszaru (mazowieckie=1).</summary>
        static string ValidNumberForMazowieckie()
        {
            int d = Digits;
            // "1" + zera dopełniające do długości
            return "1" + new string('0', d - 1);
        }

        [Test]
        public void Validate_MissingNumber_ReturnsNumberMissing()
        {
            var r = TrainNumberValidator.Validate(null, Cat, "mazowieckie");
            Assert.That(r.result, Is.EqualTo(TrainNumberValidator.Result.NumberMissing));
            var r2 = TrainNumberValidator.Validate("   ", Cat, "mazowieckie");
            Assert.That(r2.result, Is.EqualTo(TrainNumberValidator.Result.NumberMissing));
        }

        [Test]
        public void Validate_NonDigitChars_ReturnsInvalidFormat()
        {
            var r = TrainNumberValidator.Validate("12A4", Cat, "mazowieckie");
            Assert.That(r.result, Is.EqualTo(TrainNumberValidator.Result.InvalidFormat));
            var rDash = TrainNumberValidator.Validate("12-34", Cat, "mazowieckie");
            Assert.That(rDash.result, Is.EqualTo(TrainNumberValidator.Result.InvalidFormat));
        }

        [Test]
        public void Validate_WrongDigitCount_ReturnsWrongDigitCount()
        {
            // O jedną cyfrę za krótki numer.
            string tooShort = "1" + new string('0', Digits - 2);
            var r = TrainNumberValidator.Validate(tooShort, Cat, "mazowieckie");
            Assert.That(r.result, Is.EqualTo(TrainNumberValidator.Result.WrongDigitCount));
        }

        [Test]
        public void Validate_WrongConstructionArea_ReturnsWrongArea()
        {
            // Numer zaczyna się od 1 (mazowieckie), ale trasa startuje w dolnośląskim (→6).
            string num = "1" + new string('0', Digits - 1);
            var r = TrainNumberValidator.Validate(num, Cat, "dolnośląskie");
            Assert.That(r.result, Is.EqualTo(TrainNumberValidator.Result.WrongConstructionArea),
                "1. cyfra musi pasować do obszaru startu (dolnośląskie→6, nie 1).");
        }

        [Test]
        public void Validate_CorrectAreaDigit_Valid()
        {
            // dolnośląskie → obszar 6; numer zaczyna się 6.
            string num = "6" + new string('0', Digits - 1);
            var r = TrainNumberValidator.Validate(num, Cat, "dolnośląskie");
            Assert.That(r.IsValid, Is.True, r.message);
        }

        [Test]
        public void Validate_MazowieckieStartsWith1_Valid()
        {
            var r = TrainNumberValidator.Validate(ValidNumberForMazowieckie(), Cat, "mazowieckie");
            Assert.That(r.IsValid, Is.True, r.message);
        }

        [Test]
        public void Validate_NumberInUse_ReturnsInUse()
        {
            string num = ValidNumberForMazowieckie();
            var existing = new HashSet<string> { num };
            var r = TrainNumberValidator.Validate(num, Cat, "mazowieckie", existing);
            Assert.That(r.result, Is.EqualTo(TrainNumberValidator.Result.NumberInUse));
        }

        [Test]
        public void Validate_NoVoivodeship_SkipsAreaCheck()
        {
            // Brak województwa → pomija sprawdzenie obszaru (dowolna 1. cyfra OK formatowo).
            string num = "7" + new string('0', Digits - 1);
            var r = TrainNumberValidator.Validate(num, Cat, null);
            Assert.That(r.IsValid, Is.True, "Bez znanego województwa obszar nie jest sprawdzany.");
        }

        // ── GenerateAutoNumber ───────────────────────────────────────

        [Test]
        public void GenerateAutoNumber_StartsWithAreaDigit_CorrectLength()
        {
            string num = TrainNumberValidator.GenerateAutoNumber(Cat, "mazowieckie", new HashSet<string>());
            Assert.That(num, Is.Not.Null);
            Assert.That(num.Length, Is.EqualTo(Digits), "Wygenerowany numer ma poprawną długość.");
            Assert.That(num[0], Is.EqualTo('1'), "Zaczyna się cyfrą obszaru (mazowieckie→1).");
        }

        [Test]
        public void GenerateAutoNumber_SkipsExisting()
        {
            // Pierwsze kandydaty zajęte → generator zwraca następny wolny.
            int d = Digits;
            var existing = new HashSet<string>
            {
                "1" + new string('0', d - 1),                       // 10...0
                "1" + new string('0', d - 2) + "1"                  // 10...1
            };
            string num = TrainNumberValidator.GenerateAutoNumber(Cat, "mazowieckie", existing);
            Assert.That(num, Is.Not.Null);
            Assert.That(existing.Contains(num), Is.False, "Wygenerowany numer nie koliduje z zajętymi.");
            Assert.That(num[0], Is.EqualTo('1'));
        }

        [Test]
        public void GenerateAutoNumber_ResultPassesValidation()
        {
            // Round-trip: wygenerowany numer musi przejść własną walidację.
            var existing = new HashSet<string>();
            string num = TrainNumberValidator.GenerateAutoNumber(Cat, "pomorskie", existing);
            var r = TrainNumberValidator.Validate(num, Cat, "pomorskie", existing);
            Assert.That(r.IsValid, Is.True, $"Auto-numer '{num}' powinien przejść walidację: {r.message}");
        }
    }
}
