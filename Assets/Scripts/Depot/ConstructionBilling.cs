using RailwayManager.Economy;

namespace DepotSystem
{
    /// <summary>
    /// M-Economy Faza 5: rozliczanie budowy infrastruktury w zajezdni.
    ///
    /// <see cref="TryCharge"/> = polityka „nie stać → nie buduj" (decyzja user'a): sprawdza
    /// <see cref="MoneyLedger.CanAfford"/>, pobiera kasę TYLKO gdy stać, zwraca czy się udało
    /// (false → caller anuluje budowę). <see cref="Refund"/> = pełny zwrot przy undo/usunięciu
    /// (recompute kosztu przez <see cref="ConstructionCosts"/> → ta sama kwota).
    ///
    /// Cienka warstwa nad MoneyLedger (asmdef Economy) — koszt trafia do bilansu dziennego.
    /// </summary>
    public static class ConstructionBilling
    {
        /// <summary>Gdy true — <see cref="TryCharge"/>/<see cref="Refund"/> są no-opem (zwraca true bez
        /// pobrania). Ustawiane na czas generowania DOMYŚLNEGO layoutu (init zajezdni) i WCZYTYWANIA
        /// save'a — wtedy infrastruktura powstaje „za darmo" (była już opłacona / to setup, nie budowa
        /// gracza). Bez tego: external tracks na starcie + re-detekcja pokoi przy load = błędne obciążenia.
        /// Zawsze ustawiać w try/finally.</summary>
        public static bool SuppressCharging;

        public static bool CanAfford(long groszy) => MoneyLedger.CanAfford(groszy);

        /// <summary>Pobiera koszt jeśli stać → true. Brak środków → false (BEZ pobrania), caller anuluje.
        /// Koszt ≤ 0 (np. RoomType.None) lub <see cref="SuppressCharging"/> → true (no-op, nie blokuje).</summary>
        public static bool TryCharge(long groszy, string category, string source)
        {
            if (groszy <= 0 || SuppressCharging) return true;
            if (!MoneyLedger.CanAfford(groszy)) return false;
            MoneyLedger.Spend(groszy, category, source);
            return true;
        }

        /// <summary>BEZWARUNKOWE pobranie (suppress-aware, BEZ sprawdzania stać) — gdy affordability
        /// była sprawdzona wcześniej na wejściu gracza, a faktyczne pobranie dzieje się głębiej w builderze
        /// (np. tory: blokada w state machine, charge w PlaceTrackVisuals). Koszt ≤ 0 lub suppress → no-op.</summary>
        public static void Charge(long groszy, string category, string source)
        {
            if (groszy <= 0 || SuppressCharging) return;
            MoneyLedger.Spend(groszy, category, source);
        }

        /// <summary>Pełny zwrot (undo/usunięcie). Koszt ≤ 0 lub <see cref="SuppressCharging"/> → no-op.</summary>
        public static void Refund(long groszy, string category, string source)
        {
            if (groszy <= 0 || SuppressCharging) return;
            MoneyLedger.Earn(groszy, category, source);
        }
    }
}
