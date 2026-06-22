using System;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-1 / D20, D29: Rezerwacja hotelu dla multi-day turnusu.
    /// Koszt + wplyw na morale/fatigue/error chance zalezy od <see cref="tier"/>:
    ///
    /// Basic (80 zl): morale -1/noc, fatigue regen ×0.8, error chance ×1.1
    /// Standard (150 zl): baseline (neutralne)
    /// Premium (250 zl): morale +2/noc, fatigue regen ×1.2, error chance ×0.9
    ///
    /// Gdy brak dostepnego hotelu w danym miescie: fallback "delegacja prywatna"
    /// = 2× koszt Standard + extra morale penalty -3 (gracz zostawia pracownika bez konkretnego pokoju).
    ///
    /// Ceny regionalne (Warszawa +50%, male miasta -30%) → POST-EA (D36).
    /// </summary>
    [Serializable]
    public class HotelBooking
    {
        public HotelTier tier = HotelTier.Standard;

        /// <summary>Nazwa miasta (stacji) gdzie nocleg.</summary>
        public string cityStationName;

        /// <summary>Pracownik korzystajacy z noclegu.</summary>
        public int employeeId;

        /// <summary>Liczba nocy (1-2 w EA, multi-day max 3 dni = 2 noce).</summary>
        public int nights = 1;

        /// <summary>Obliczony koszt w groszach (cache przy booking).</summary>
        public int costGroszy;

        /// <summary>Data zameldowania (ISO).</summary>
        public string checkInDateIso;

        /// <summary>True gdy fallback "delegacja prywatna" (brak realnego hotelu).</summary>
        public bool isPrivateFallback;
    }
}
