using System;
using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M7-5: Dane zewnętrznego zakładu naprawczego (ZNTK/NEWAG/PESA…).
    ///
    /// Ładowane z <c>Assets/StreamingAssets/Fleet/external_workshops.json</c>.
    /// Użytkowane jako alternatywa dla własnego warsztatu — fixed fee (wszystko
    /// wliczone: parts+pensje+zysk ZNTK), ale wymagana dostawa + powrót.
    /// </summary>
    [Serializable]
    public class ExternalWorkshop
    {
        public string id;
        public string name;

        /// <summary>Fragment nazwy stacji — lookup via TimetableInitializer.FindStation().</summary>
        public string locationStationName;

        /// <summary>
        /// Lista poziomów przeglądu — "P1","P2","P3","P4","P5" (JSON-friendly,
        /// parsed via <see cref="ExternalWorkshopExt"/>).
        /// </summary>
        public List<string> canPerform = new();

        /// <summary>
        /// Specjalizacje — string equivalents of <see cref="FleetVehicleType"/>:
        /// "ElectricLocomotive","DieselLocomotive","EMU","DMU","PassengerCar".
        /// </summary>
        public List<string> specializations = new();

        /// <summary>Mnożnik ceny (1.0 = baseline, 1.15 = 15% drożej).</summary>
        public float priceMultiplier = 1.0f;

        /// <summary>Mnożnik czasu trwania przeglądu (1.0 = jak self-build, 1.1 = 10% wolniej).</summary>
        public float durationMultiplier = 1.1f;

        /// <summary>Czy zakład oferuje modernizacje pojazdu (post-1.0 feature).</summary>
        public bool modernizationAvailable;

        // ── M-FC-9: Paint job services ──
        /// <summary>Cena malowania pojazdu w tym ZNTK (PLN). 0 = brak usługi.</summary>
        public long paintCostPln;

        /// <summary>Czas malowania (dni gry).</summary>
        public int paintTimeDays;
    }

    /// <summary>Helper extensions do ExternalWorkshop.</summary>
    public static class ExternalWorkshopExt
    {
        public static bool CanPerformLevel(this ExternalWorkshop w, InspectionLevel level)
        {
            if (w?.canPerform == null) return false;
            string code = $"P{(int)level + 1}"; // P1..P5 (enum 0..4)
            return w.canPerform.Contains(code);
        }

        public static bool CanServeType(this ExternalWorkshop w, FleetVehicleType type)
        {
            if (w?.specializations == null || w.specializations.Count == 0) return true;
            string code = type.ToString();
            return w.specializations.Contains(code);
        }
    }
}
