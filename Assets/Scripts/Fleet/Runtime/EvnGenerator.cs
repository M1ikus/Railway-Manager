using System.Collections.Generic;
using System.Text;
using RailwayManager.Core;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// Generator 12-cyfrowych numerów EVN (European Vehicle Number).
    ///
    /// Format: XX XX XXXX XXX X
    /// - XX (poz. 1-2): kompatybilność międzynarodowa / kategoria
    /// - XX (poz. 3-4): kod kraju właściciela (51 = Polska)
    /// - XXXX (poz. 5-8): parametry techniczno-eksploatacyjne
    /// - XXX (poz. 9-11): numer indywidualny w serii
    /// - X (poz. 12): cyfra kontrolna (algorytm Luhna)
    /// </summary>
    public static class EvnGenerator
    {
        private const string PolandCountryCode = "51";

        // B4: rejestr wszystkich wygenerowanych/przywróconych EVN — gwarantuje uniqueness
        // przy flotach >1000 pojazdów per typ (serial w EVN ma tylko 3 cyfry, czyli pula
        // 1000 unikalnych per kombinację compat+tech). Save/load wywołuje RegisterExisting
        // dla każdego załadowanego pojazdu po restore.
        private static readonly HashSet<string> _generated = new();

        /// <summary>Reset rejestru EVN przy „Nowa gra". Wywoływane przez FleetService.ResetForNewGame.</summary>
        public static void ResetRegistry() => _generated.Clear();

        /// <summary>Save/load: rejestruje EVN przywrócony z save, żeby kolejne Generate nie kolidowało.</summary>
        public static void RegisterExisting(string evn)
        {
            if (!string.IsNullOrEmpty(evn)) _generated.Add(evn);
        }

        /// <summary>Diagnostyka: liczba zarejestrowanych EVN (testy + smoke).</summary>
        public static int RegisteredCount => _generated.Count;

        /// <summary>
        /// Generuje pełny EVN dla danego typu pojazdu i numeru seryjnego.
        /// Przy kolizji z poprzednio wygenerowanym EVN wykonuje sweep serial 0..999.
        /// Gdy pula wyczerpana → Log.Error + zwraca ostatni próbowany (gra kontynuuje).
        /// </summary>
        public static string Generate(FleetVehicleType type, int serialNumber)
        {
            string compat = GetCompatibilityCode(type);
            string country = PolandCountryCode;
            string tech = GetTechParamsCode(type);
            int seed = serialNumber % 1000;
            string lastTry = null;

            // Sweep całej puli 0..999 startując od `seed`.
            for (int offset = 0; offset < 1000; offset++)
            {
                int s = (seed + offset) % 1000;
                string serial = s.ToString("D3");
                string first11 = compat + country + tech + serial;
                int checkDigit = CalculateLuhnCheckDigit(first11);
                string candidate = $"{compat} {country} {tech} {serial} {checkDigit}";
                lastTry = candidate;
                if (_generated.Add(candidate)) return candidate;
            }

            Log.Error($"[EvnGenerator] Pula EVN wyczerpana dla typu {type} compat={compat} tech={tech} " +
                      $"(>1000 pojazdów). Zwracam duplikat: {lastTry}. Wymaga rozszerzenia tech-params per seria.");
            return lastTry;
        }

        /// <summary>Parsuje sformatowany EVN (z spacjami) na ciąg 12 cyfr bez separatorów.</summary>
        public static string StripFormatting(string evn)
        {
            if (string.IsNullOrEmpty(evn)) return "";
            var sb = new StringBuilder(12);
            foreach (char c in evn)
                if (c >= '0' && c <= '9') sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Waliduje EVN — sprawdza format (12 cyfr) i cyfrę kontrolną Luhna.</summary>
        public static bool IsValid(string evn)
        {
            string digits = StripFormatting(evn);
            if (digits.Length != 12) return false;
            int check = CalculateLuhnCheckDigit(digits.Substring(0, 11));
            return check == (digits[11] - '0');
        }

        // ── Kody wewnętrzne ──

        /// <summary>Pierwsze 2 cyfry — kategoria/kompatybilność pojazdu.</summary>
        private static string GetCompatibilityCode(FleetVehicleType type)
        {
            return type switch
            {
                FleetVehicleType.ElectricLocomotive => "91",  // lok. elektryczna
                FleetVehicleType.DieselLocomotive   => "92",  // lok. spalinowa
                FleetVehicleType.EMU                => "94",  // EZT
                FleetVehicleType.DMU                => "95",  // SZT
                FleetVehicleType.PassengerCar       => "50",  // wagon osobowy
                _                                   => "50"
            };
        }

        /// <summary>Środkowe 4 cyfry — parametry techniczno-eksploatacyjne (upraszczone).</summary>
        private static string GetTechParamsCode(FleetVehicleType type)
        {
            return type switch
            {
                FleetVehicleType.ElectricLocomotive => "0010",
                FleetVehicleType.DieselLocomotive   => "0020",
                FleetVehicleType.EMU                => "0300",
                FleetVehicleType.DMU                => "0400",
                FleetVehicleType.PassengerCar       => "5000",
                _                                   => "0000"
            };
        }

        /// <summary>
        /// Algorytm Luhna — oblicza cyfrę kontrolną dla ciągu 11 cyfr.
        /// Podwajamy co drugą cyfrę od prawej; jeśli wynik >9, odejmujemy 9. Suma + check ≡ 0 (mod 10).
        /// </summary>
        private static int CalculateLuhnCheckDigit(string first11)
        {
            int sum = 0;
            // W EVN numerujemy pozycje 1-11; podwajamy pozycje parzyste (2,4,6,8,10) licząc od lewej,
            // co odpowiada pozycjom nieparzystym licząc od prawej (1,3,5,7,9) — dokładnie jak Luhn dla 11 cyfr.
            for (int i = 0; i < 11; i++)
            {
                int digit = first11[i] - '0';
                // Pozycja od prawej (0-indexed): 10 - i
                int posFromRight = 10 - i;
                if (posFromRight % 2 == 0)
                {
                    digit *= 2;
                    if (digit > 9) digit -= 9;
                }
                sum += digit;
            }
            return (10 - (sum % 10)) % 10;
        }
    }
}
