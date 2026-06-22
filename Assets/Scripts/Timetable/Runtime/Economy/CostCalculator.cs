using RailwayManager.Fleet;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M6-4: Kalkulator kosztów operacyjnych.
    /// - Per km: paliwo/energia (zależy od trakcji) + track access fee (PLK-like)
    /// - Per postój: platform fee (wyższy na dużych stacjach, niski na przystankach)
    /// - Per dzień: overhead (placeholder do M8 gdy będą realne pensje)
    ///
    /// Wartości placeholder do M6.5 Rebalance (post-M13).
    /// </summary>
    public static class CostCalculator
    {
        // ── Per km ──────────────────────────────────────────────────

        /// <summary>Koszt paliwa/energii [gr] per km dla pojazdu trakcyjnego.</summary>
        const int ElectricCostPerKmGroszy = 250;   // 2.50 zł/km
        const int DieselCostPerKmGroszy = 400;     // 4.00 zł/km
        const int PassiveVehicleCostPerKmGroszy = 50; // 0.50 zł/km — wagony (zużycie kół, konserwacja)

        /// <summary>Opłata zarządcy infrastruktury (PKP PLK-like) [gr] per km — niezależnie od trakcji.
        /// M6.5-4: bumpnięte 150 → 631 (real PLK 2024-25 średnia pasażerski). Patrz <see cref="EconomyConstants.TuiPasazerskiSredniaGroszy"/>.</summary>
        const int TrackAccessFeePerKmGroszy = EconomyConstants.TuiPasazerskiSredniaGroszy; // 6.31 zł/pociągokm

        /// <summary>
        /// Koszt operacyjny dla pojedynczego pojazdu [gr] per km.
        ///
        /// M-Fleet-4: preferowane wartość z <see cref="FleetVehicleData.operationalCostPerKmGroszy"/>
        /// (pochodzi z fleet_catalog_ea.json lub konfiguratora). Gdy = 0 (stary pojazd bez nowego
        /// spec'u) → fallback na heurystykę wg typu.
        ///
        /// Track access fee NIE dodawany gdy pojazd ma własny koszt z catalog (spec zakłada że
        /// fleet catalog zawiera już wszystko wliczone). Dodawany tylko w fallback heurystyce.
        /// </summary>
        public static int GetVehicleOperationalCostPerKm(FleetVehicleData v)
        {
            if (v == null) return 0;

            // M-Fleet-4: preferuj wartość z catalog
            if (v.operationalCostPerKmGroszy > 0)
                return v.operationalCostPerKmGroszy;

            // Fallback: heurystyka wg typu (stare dane bez spec'u)
            switch (v.type)
            {
                case FleetVehicleType.ElectricLocomotive:
                case FleetVehicleType.EMU:
                    return ElectricCostPerKmGroszy + TrackAccessFeePerKmGroszy;
                case FleetVehicleType.DieselLocomotive:
                case FleetVehicleType.DMU:
                    return DieselCostPerKmGroszy + TrackAccessFeePerKmGroszy;
                case FleetVehicleType.PassengerCar:
                    return PassiveVehicleCostPerKmGroszy;
                default:
                    return PassiveVehicleCostPerKmGroszy;
            }
        }

        /// <summary>Sumaryczny koszt operacyjny składu [gr] per km.</summary>
        public static int GetConsistOperationalCostPerKm(System.Collections.Generic.List<int> vehicleIds)
        {
            if (vehicleIds == null || vehicleIds.Count == 0) return 0;
            int total = 0;
            foreach (var v in FleetService.OwnedVehicles)
            {
                if (vehicleIds.Contains(v.id))
                    total += GetVehicleOperationalCostPerKm(v);
            }
            return total;
        }

        // ── Per postój ──────────────────────────────────────────────

        /// <summary>
        /// Opłata za zatrzymanie się na danej stacji [gr] (cennik OIU PKP PLK — stałe
        /// <see cref="EconomyConstants"/> Premium 130 / Kat I 50 / Kat II 20 / Kat III 5 / halt 1 zł).
        ///
        /// TD-036a: kategoria wg <c>StationImportance</c> (progi w EconomyConstants — kalibracja
        /// M-Balance). <paramref name="importance"/> &lt; 0 (OD matrix niezbudowana / brak danych)
        /// → fallback wg <c>isMajorStation</c>: Kategoria III / halt.
        /// </summary>
        public static int GetPlatformFeeGroszy(RailwayStation station, float importance = -1f)
        {
            if (station == null) return 0;

            if (importance < 0f)
                return station.isMajorStation
                    ? EconomyConstants.PlatformFeeKategoriaIIIGroszy
                    : EconomyConstants.PlatformFeeHaltGroszy;

            if (importance >= EconomyConstants.PlatformFeeImportancePremium)      return EconomyConstants.PlatformFeePremiumGroszy;
            if (importance >= EconomyConstants.PlatformFeeImportanceKategoriaI)   return EconomyConstants.PlatformFeeKategoriaIGroszy;
            if (importance >= EconomyConstants.PlatformFeeImportanceKategoriaII)  return EconomyConstants.PlatformFeeKategoriaIIGroszy;
            if (importance >= EconomyConstants.PlatformFeeImportanceKategoriaIII) return EconomyConstants.PlatformFeeKategoriaIIIGroszy;
            return EconomyConstants.PlatformFeeHaltGroszy;
        }

        // ── Per dzień ───────────────────────────────────────────────

        /// <summary>
        /// Daily overhead [gr] — fixed costs niezwiązane z pracownikami: utility bills
        /// (prąd/woda/grzanie depotu), wynajem terenu, ubezpieczenia, leasing IT/admin.
        ///
        /// BUG-062 (clarification): NIE zastępowane przez PayrollService — pensje
        /// pracowników to OSOBNA kategoria (`Personnel` w EconomyManager.AddCost).
        /// Wcześniejszy komentarz "M8 zastąpione realnymi pensjami" był niepoprawny.
        /// Overhead działa równolegle z payroll i jest skalowany przez OperationalCostMultiplier
        /// (whitelisted w EconomyManager.OperationalCategories).
        /// </summary>
        public const int DailyOverheadGroszy = 500_000; // 5000 zł
    }
}
