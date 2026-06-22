using System.Collections.Generic;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Statyczny katalog kategorii IRJ PKP — tabela z rozporządzenia w/s oznaczeń pociągów.
    /// Lookup po (IrjGroup, TractionLetter) → 3-literowy kod + liczba cyfr numeru pociągu.
    /// Wpisy z gwiazdką w tabeli źródłowej (*) są traktowane jako pełnoprawne, oznaczone isRare = true.
    /// 'X' w tabeli oryginalnej = kombinacja niedozwolona (np. ENJ — ekspres nocny jako EMU).
    /// </summary>
    public static class IrjCategoryCatalog
    {
        /// <summary>Wpis w katalogu: kod + liczba cyfr + czy kombinacja jest w ogóle dozwolona.</summary>
        public readonly struct Entry
        {
            public readonly string code;         // np. "EIE"
            public readonly int numberDigits;    // 4/5/6
            public readonly bool isAllowed;      // false dla 'X' w tabeli
            public readonly bool isRare;         // true dla * — zarezerwowane / rzadko stosowane
            public readonly string polishName;   // nazwa w tabeli

            public Entry(string code, int digits, bool allowed, bool rare, string name)
            {
                this.code = code;
                this.numberDigits = digits;
                this.isAllowed = allowed;
                this.isRare = rare;
                this.polishName = name;
            }

            public static Entry Forbidden(string name) => new(null, 0, false, false, name);
        }

        private static readonly Dictionary<IrjCategory, Entry> _lookup = BuildLookup();

        public static Entry Get(IrjCategory cat)
            => _lookup.TryGetValue(cat, out var e) ? e : Entry.Forbidden("(unknown)");

        public static string GetCode(IrjCategory cat) => Get(cat).code ?? "???";

        public static int GetDigits(IrjCategory cat) => Get(cat).numberDigits;

        public static bool IsAllowed(IrjCategory cat) => Get(cat).isAllowed;

        /// <summary>Wszystkie dozwolone kombinacje (do UI select, walidacji override).</summary>
        public static IEnumerable<KeyValuePair<IrjCategory, Entry>> AllAllowed()
        {
            foreach (var kv in _lookup)
                if (kv.Value.isAllowed) yield return kv;
        }

        // ─────────────────────────────────────────────────────────────
        // Tabela źródłowa z rozporządzenia — grupy A/B/C/D
        // ─────────────────────────────────────────────────────────────
        private static Dictionary<IrjCategory, Entry> BuildLookup()
        {
            var d = new Dictionary<IrjCategory, Entry>(128);

            // Helper do dodawania wiersza tabeli jednocześnie dla 4 wariantów trakcyjnych
            void Row(IrjGroup g, string name, int digits,
                     string electricLoco, string electricUnit, string dieselLoco, string dieselUnit,
                     bool rareEloco = false, bool rareEunit = false, bool rareDloco = false, bool rareDunit = false)
            {
                Add(g, TractionLetter.ElectricLoco, electricLoco, digits, name, rareEloco);
                Add(g, TractionLetter.ElectricUnit, electricUnit, digits, name, rareEunit);
                Add(g, TractionLetter.DieselLoco,   dieselLoco,   digits, name, rareDloco);
                Add(g, TractionLetter.DieselUnit,   dieselUnit,   digits, name, rareDunit);
            }

            void Add(IrjGroup g, TractionLetter t, string code, int digits, string name, bool rare)
            {
                var key = new IrjCategory(g, t);
                if (string.IsNullOrEmpty(code) || code == "X")
                    d[key] = Entry.Forbidden(name);
                else
                    d[key] = new Entry(code, digits, true, rare, name);
            }

            // ── A. Pociągi pasażerskie ──
            Row(IrjGroup.ExpressDomestic,           "Ekspresowy krajowy",            4, "EIE", "EIJ", "EIS", "EIM", rareEunit: true, rareDunit: true);
            Row(IrjGroup.ExpressInternational,      "Ekspresowy międzynarodowy",     5, "ECE", "ECJ", "ECS", "ECM", rareEunit: true, rareDunit: true);
            Row(IrjGroup.ExpressInternationalNight, "Ekspresowy międzynar. nocny",   5, "ENE",  "X" , "ENS",  "X" );
            Row(IrjGroup.InterregionalFast,         "Międzywojewódzki pospieszny",   5, "MPE", "MPJ", "MPS", "MPM");
            Row(IrjGroup.InterregionalFastNight,    "Międzywoj. pospieszny nocny",   5, "MHE", "MHJ", "MHS",  "X" );
            Row(IrjGroup.InternationalFast,         "Międzynarodowy pospieszny",     5, "MME", "MMJ", "MMS", "MMM");
            Row(IrjGroup.InterregionalLocal,        "Międzywojewódzki osobowy",      5, "MOE", "MOJ", "MOS", "MOM");
            Row(IrjGroup.RegionalFast,              "Wojewódzki pospieszny",         5, "RPE", "RPJ", "RPS", "RPM");
            Row(IrjGroup.RegionalAgglomeration,     "Wojewódzki osob. aglomeracyjny",5, "RAE", "RAJ", "RAS", "RAM");
            Row(IrjGroup.RegionalInternational,     "Wojewódzki osob. międzynar.",   5, "RME", "RMJ", "RMS", "RMM");
            Row(IrjGroup.RegionalLocal,             "Wojewódzki osobowy",            5, "ROE", "ROJ", "ROS", "ROM");
            Row(IrjGroup.EmptyPassenger,            "Próżny pasażerski",             6, "PWE", "PWJ", "PWS", "PWM");
            Row(IrjGroup.EmptyPassengerTest,        "Próżny pasażerski próbny",      6, "PXE", "PXJ", "PXS", "PXM");

            // ── B1. Towarowe międzynarodowe (tylko lokomotywowe) ──
            Row(IrjGroup.FreightIntlIntermodal, "Towarowy międzynar. intermodalny", 6, "TCE", "X", "TCS", "X");
            Row(IrjGroup.FreightIntlMass,       "Towarowy międzynar. masowy",       6, "TGE", "X", "TGS", "X");
            Row(IrjGroup.FreightIntlNonMass,    "Towarowy międzynar. niemasowy",    6, "TRE", "X", "TRS", "X");

            // ── B2. Towarowe krajowe ──
            Row(IrjGroup.FreightDomesticIntermodal, "Towarowy krajowy intermodalny", 6, "TDE", "X", "TDS", "X");
            Row(IrjGroup.FreightDomesticMass,       "Towarowy krajowy masowy",       6, "TME", "X", "TMS", "X");
            Row(IrjGroup.FreightDomesticNonMass,    "Towarowy krajowy niemasowy",    6, "TNE", "X", "TNS", "X");
            Row(IrjGroup.FreightStationService,     "Towarowy do obsługi stacji",    6, "TKE", "X", "TKS", "X");
            Row(IrjGroup.FreightEmptyTest,          "Próżny skład towarowy/próbny",  6, "TSE", "TSJ", "TSS", "TSM",
                rareEunit: true, rareDunit: true);

            // ── C. Pojazdy luzem ──
            Row(IrjGroup.LoneLocoPassenger, "Pasażerska lokomotywa luzem",  6, "LPE", "X", "LPS", "X", rareDloco: true);
            Row(IrjGroup.LoneLocoFreight,   "Towarowa lokomotywa luzem",    6, "LTE", "X", "LTS", "X", rareDloco: true);
            Row(IrjGroup.LoneLocoShunt,     "Lokomotywa manewrowa luzem",   6, "LSE", "X", "LSS", "X", rareEloco: true);

            // ── D. Utrzymaniowo-naprawcze ──
            Row(IrjGroup.MaintenanceInspection, "Inspekcyjny, diagnostyczny", 6, "ZNE", "ZNJ", "ZNS", "ZNM",
                rareEunit: true, rareDunit: true);

            return d;
        }
    }
}
