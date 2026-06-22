using System.Collections.Generic;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Walidator numeru pociągu wpisanego przez gracza.
    ///
    /// Sprawdza:
    /// 1. Format: sam ciąg cyfr, bez spacji/myślników
    /// 2. Długość: zgodna z tabelą IRJ dla danej kategorii (4, 5, lub 6 cyfr)
    /// 3. Obszar konstrukcyjny: pierwsza cyfra odpowiada województwu start trasy
    /// 4. Unikalność: numer nie jest już używany przez inny aktywny rozkład
    ///
    /// Zwraca wynik z enum + opisem błędu (do wyświetlenia gracza).
    /// Jeśli gracz nie wpisał numeru (null/empty) — NumberMissing, auto-generate powinien
    /// być wywołany z GenerateAutoNumber() osobno.
    /// </summary>
    public static class TrainNumberValidator
    {
        public enum Result
        {
            Valid,
            NumberMissing,
            InvalidFormat,
            WrongDigitCount,
            WrongConstructionArea,
            NumberInUse
        }

        public readonly struct ValidationResult
        {
            public readonly Result result;
            public readonly string message;
            public ValidationResult(Result r, string m) { result = r; message = m; }
            public bool IsValid => result == Result.Valid;
        }

        /// <summary>
        /// Pełna walidacja numeru pociągu. `existingNumbers` to zbiór numerów już zajętych
        /// przez inne aktywne rozkłady (do sprawdzenia unikalności).
        /// </summary>
        public static ValidationResult Validate(
            string number,
            IrjCategory category,
            string startVoivodeship,
            HashSet<string> existingNumbers = null)
        {
            if (string.IsNullOrWhiteSpace(number))
                return new ValidationResult(Result.NumberMissing,
                    "Numer pociągu nie został wpisany.");

            // 1. Format: tylko cyfry
            foreach (char c in number)
                if (c < '0' || c > '9')
                    return new ValidationResult(Result.InvalidFormat,
                        $"Numer '{number}' zawiera niepoprawne znaki — dozwolone tylko cyfry.");

            // 2. Długość
            var entry = IrjCategoryCatalog.Get(category);
            if (!entry.isAllowed)
                return new ValidationResult(Result.InvalidFormat,
                    $"Kombinacja kategorii {category} nie jest dozwolona w tabeli IRJ.");

            if (number.Length != entry.numberDigits)
                return new ValidationResult(Result.WrongDigitCount,
                    $"Numer dla kategorii {entry.code} musi mieć {entry.numberDigits} cyfr "
                    + $"(wpisano {number.Length}).");

            // 3. Obszar konstrukcyjny (tylko jeśli znamy województwo startu)
            if (!string.IsNullOrEmpty(startVoivodeship))
            {
                int expectedDigit = ConstructionAreaCatalog.GetDigitForVoivodeship(startVoivodeship);
                int firstDigit = number[0] - '0';
                if (firstDigit != expectedDigit)
                {
                    var area = ConstructionAreaCatalog.GetByDigit(expectedDigit);
                    string areaName = area?.shortName ?? $"Obszar {expectedDigit}";
                    return new ValidationResult(Result.WrongConstructionArea,
                        $"Numer dla trasy startującej w woj. {startVoivodeship} "
                        + $"powinien zaczynać się cyfrą {expectedDigit} ({areaName}).");
                }
            }

            // 4. Unikalność
            if (existingNumbers != null && existingNumbers.Contains(number))
                return new ValidationResult(Result.NumberInUse,
                    $"Numer {number} jest już używany przez inny aktywny rozkład.");

            return new ValidationResult(Result.Valid, "OK");
        }

        /// <summary>
        /// Generuje wolny numer pociągu dla danej kategorii i obszaru konstrukcyjnego.
        /// Algorytm: pierwsza cyfra z obszaru, reszta iterowana od 0 aż znajdzie wolny.
        /// W realu PKP PLK przydziela numery z konkretnych zakresów per kategoria; tutaj
        /// uproszczenie — sekwencyjne szukanie w obszarze.
        /// </summary>
        public static string GenerateAutoNumber(
            IrjCategory category,
            string startVoivodeship,
            HashSet<string> existingNumbers)
        {
            var entry = IrjCategoryCatalog.Get(category);
            if (!entry.isAllowed) return null;

            int digits = entry.numberDigits;
            int firstDigit = string.IsNullOrEmpty(startVoivodeship)
                ? 9
                : ConstructionAreaCatalog.GetDigitForVoivodeship(startVoivodeship);

            // Maksymalna liczba = 10^(digits-1), np. dla 5 cyfr: 10000..99999
            int remainingDigitsMax = 1;
            for (int i = 0; i < digits - 1; i++) remainingDigitsMax *= 10;

            for (int i = 0; i < remainingDigitsMax; i++)
            {
                string candidate = firstDigit.ToString() + i.ToString().PadLeft(digits - 1, '0');
                if (existingNumbers == null || !existingNumbers.Contains(candidate))
                    return candidate;
            }

            return null; // wszystkie zajęte (nie powinno się zdarzyć w normalnej grze)
        }
    }
}
