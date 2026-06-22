using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>M7-6: Dane katalogowe jednego typu części.</summary>
    public readonly struct PartInfo
    {
        public readonly ComponentType type;
        public readonly string displayName;
        public readonly int priceGroszy;   // cena jednostkowa [gr] (1 szt)
        public readonly int deliveryDays;  // dni dostawy (czas gry)

        public PartInfo(ComponentType type, string displayName, int priceGroszy, int deliveryDays)
        {
            this.type = type;
            this.displayName = displayName;
            this.priceGroszy = priceGroszy;
            this.deliveryDays = deliveryDays;
        }

        public int PriceZl => priceGroszy / 100;
    }

    /// <summary>
    /// M7-6: Katalog 12 typów części (1:1 z ComponentType).
    ///
    /// Ceny i czasy dostawy zgodnie z docs/design/m7-maintenance.md sekcja "Parts inventory".
    /// Hardcoded w M7 (post-EA: export do JSON gdy potrzeba balancingu).
    /// </summary>
    public static class PartCatalog
    {
        static readonly Dictionary<ComponentType, PartInfo> _byType = new()
        {
            [ComponentType.Engine]     = new(ComponentType.Engine,     "Silnik trakcyjny",       15_000_000, 5),
            [ComponentType.Brake]      = new(ComponentType.Brake,      "Hamulec (zestaw)",        2_500_000, 3),
            [ComponentType.Doors]      = new(ComponentType.Doors,      "Drzwi kompletne",         1_500_000, 3),
            [ComponentType.AC]         = new(ComponentType.AC,         "Klimatyzacja",            3_500_000, 4),
            [ComponentType.Body]       = new(ComponentType.Body,       "Pudło (naprawa)",         5_000_000, 7),
            [ComponentType.Wheels]     = new(ComponentType.Wheels,     "Zestaw kołowy",           1_800_000, 3),
            [ComponentType.Electrical] = new(ComponentType.Electrical, "Instalacja elektryczna",  4_000_000, 4),
            [ComponentType.Interior]   = new(ComponentType.Interior,   "Tapicerka / wnętrze",     2_000_000, 3),
            [ComponentType.Lights]     = new(ComponentType.Lights,     "Reflektory + oświetlenie",  800_000, 2),
            [ComponentType.Toilets]    = new(ComponentType.Toilets,    "Toalety (pakiet)",        1_200_000, 3),
            [ComponentType.Pantograph] = new(ComponentType.Pantograph, "Pantograf",               4_500_000, 4),
            [ComponentType.Coupling]   = new(ComponentType.Coupling,   "Sprzęg UIC",              2_200_000, 3),
        };

        public static IEnumerable<PartInfo> All => _byType.Values;

        public static PartInfo Get(ComponentType type)
            => _byType.TryGetValue(type, out var info) ? info : default;

        /// <summary>Lista wszystkich typów (do iteracji w UI, uporządkowane wg enum index).</summary>
        public static IEnumerable<ComponentType> AllTypes
        {
            get
            {
                for (int i = 0; i <= 11; i++) yield return (ComponentType)i;
            }
        }
    }
}
