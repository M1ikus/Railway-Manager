using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-8 / D20 + D29: Serwis rezerwacji hoteli dla multi-day turnusow.
    ///
    /// Tier-y (D29):
    /// - Basic (80 zl/noc): morale -1/noc, fatigue regen ×0.8, error chance ×1.1
    /// - Standard (150 zl): baseline — neutralne
    /// - Premium (250 zl): morale +2/noc, fatigue regen ×1.2, error chance ×0.9
    ///
    /// Global default w settings firmy (<see cref="CompanyDefaultHotelTier"/>, D36 → settings UI).
    /// Override per <see cref="CrewDuty.overnightHotel"/>.
    ///
    /// Fallback "delegacja prywatna" (D20): gdy brak dostepnego hotelu w miescie →
    /// 2× cost Standard + <see cref="PersonnelBalanceConstants.HotelPrivateFallbackMoralePenalty"/>.
    ///
    /// Koszty wyplacane w <see cref="ApplyBookingCost"/> — wywolywane przez M8-11 runtime
    /// gdy Overnight duty wchodzi w zycie. W M8-8 tylko api tworzenia rezerwacji.
    /// </summary>
    public static class HotelBookingService
    {
        /// <summary>Globalny default tier dla nowych bookings (settings firmy). D36: post-EA regional.</summary>
        public static HotelTier CompanyDefaultHotelTier { get; set; } = HotelTier.Standard;

        public static List<HotelBooking> AllBookings { get; } = new();

        public static event Action<HotelBooking> OnBookingCreated;
        public static event Action<HotelBooking> OnBookingCancelled;

        public static void ResetAll()
        {
            AllBookings.Clear();
        }

        /// <summary>
        /// Koszt rezerwacji (grosze). D29 ceny + D36 flaga regional multiplier (post-EA = 1.0).
        /// </summary>
        public static int ComputeCost(HotelTier tier, int nights)
        {
            int perNight = tier switch
            {
                HotelTier.Basic => PersonnelBalanceConstants.HotelCostBasicPerNight,
                HotelTier.Standard => PersonnelBalanceConstants.HotelCostStandardPerNight,
                HotelTier.Premium => PersonnelBalanceConstants.HotelCostPremiumPerNight,
                _ => PersonnelBalanceConstants.HotelCostStandardPerNight
            };
            // D36: regional multiplier post-EA (placeholder 1.0)
            // MB-1 Phase B: difficulty multiplier HotelCost (0.5 - 2.0)
            float diffMult = DifficultyService.Modifiers.HotelCostMultiplier;
            return (int)(perNight * Math.Max(1, nights) * diffMult);
        }

        /// <summary>
        /// Tworzy rezerwacje (nie wyplaca jeszcze — koszt przy <see cref="ApplyBookingCost"/>).
        /// </summary>
        public static HotelBooking CreateBooking(int employeeId, string cityStationName, HotelTier tier, int nights, string checkInDateIso)
        {
            if (nights <= 0) return null;

            var booking = new HotelBooking
            {
                tier = tier,
                cityStationName = cityStationName ?? "",
                employeeId = employeeId,
                nights = nights,
                costGroszy = ComputeCost(tier, nights),
                checkInDateIso = checkInDateIso,
                isPrivateFallback = false
            };
            AllBookings.Add(booking);
            OnBookingCreated?.Invoke(booking);
            Log.Info($"[HotelBookingService] Booking: emp #{employeeId} in {cityStationName} " +
                     $"tier={tier}, {nights}x night = {booking.costGroszy / 100}zl ({checkInDateIso})");
            return booking;
        }

        /// <summary>
        /// Fallback gdy brak dostepnego hotelu w miescie (D20).
        ///
        /// BUG-057 (clarification): koszt = base × HotelPrivateFallbackMult × difficulty.
        /// W worst case (Realistic preset 1.5×) kary się **multiplikatywnie kumulują** →
        /// 2.0 × 1.5 = 3.0× standard cost. To **design intent** (gracz świadomie wybrał
        /// Realistic = wszystkie koszty droższe, fallback to dodatkowa kara). Cap na max
        /// 4.0× dla obrony przed extreme custom difficulty configs (np. HotelCost 3.0).
        /// </summary>
        public static HotelBooking CreatePrivateFallback(int employeeId, string cityStationName, int nights, string checkInDateIso)
        {
            if (nights <= 0) return null;

            // MB-1 Phase B: difficulty multiplier też dla fallback (gracz wybiera trudność dla wszystkich kosztów)
            float diffMult = DifficultyService.Modifiers.HotelCostMultiplier;
            int baseCost = PersonnelBalanceConstants.HotelCostStandardPerNight * nights;
            // BUG-057: cumulative cap 4.0× — bez tego custom Realistic + 3.0 hotel multiplier
            // → 6× cost (nie sensible). 4× max zachowuje "drogi fallback" feeling bez extremów.
            float cumulativeMult = Mathf.Min(4.0f,
                PersonnelBalanceConstants.HotelPrivateFallbackMult * diffMult);
            int finalCost = (int)(baseCost * cumulativeMult);

            var booking = new HotelBooking
            {
                tier = HotelTier.Standard, // fallback zakladamy Standard tier
                cityStationName = cityStationName ?? "",
                employeeId = employeeId,
                nights = nights,
                costGroszy = finalCost,
                checkInDateIso = checkInDateIso,
                isPrivateFallback = true
            };
            AllBookings.Add(booking);
            OnBookingCreated?.Invoke(booking);
            Log.Info($"[HotelBookingService] Private fallback: emp #{employeeId} in {cityStationName} " +
                     $"{nights}x = {finalCost / 100}zl (2× Standard)");
            return booking;
        }

        public static bool CancelBooking(HotelBooking booking)
        {
            if (booking == null) return false;
            if (!AllBookings.Remove(booking)) return false;
            OnBookingCancelled?.Invoke(booking);
            return true;
        }

        /// <summary>
        /// Wyplaca koszt bookingu + aplikuje morale/fatigue modifier (D29) na pracownika.
        /// Wywolywane przez M8-11 runtime gdy Overnight duty wchodzi w zycie.
        /// </summary>
        public static void ApplyBookingCost(HotelBooking booking)
        {
            if (booking == null) return;

            var econ = EconomyManager.Instance;
            if (econ != null)
                econ.AddCost(-1, booking.costGroszy, "Personnel",
                    $"Hotel {booking.tier} × {booking.nights} for emp #{booking.employeeId} in {booking.cityStationName}");
            else
                GameState.Money -= booking.costGroszy / 100;

            var emp = PersonnelService.GetById(booking.employeeId);
            if (emp != null)
            {
                int moraleDelta = booking.tier switch
                {
                    HotelTier.Basic => PersonnelBalanceConstants.HotelMoraleBasicPenalty,
                    HotelTier.Standard => PersonnelBalanceConstants.HotelMoraleStandardBonus,
                    HotelTier.Premium => PersonnelBalanceConstants.HotelMoralePremiumBonus,
                    _ => 0
                };
                if (booking.isPrivateFallback)
                    moraleDelta += PersonnelBalanceConstants.HotelPrivateFallbackMoralePenalty;

                moraleDelta *= booking.nights;
                // BUG-060 v2: hotel quality affects "rooms" bucket (pracownik zły bo dostał gorszy nocleg)
                if (emp.moraleBreakdown == null) emp.moraleBreakdown = MoraleBreakdown.FromLegacyMorale(emp.currentMorale);
                emp.moraleBreakdown.ApplyDeltaToRoom(moraleDelta);
                emp.currentMorale = emp.moraleBreakdown.Total;

                // Fatigue regen multiplier — aplikowane przy FatigueMoraleTickService w trakcie pobytu
                // W M8-8: uproszczony one-shot fatigue reduction wg tier
                float regenMult = booking.tier switch
                {
                    HotelTier.Basic => PersonnelBalanceConstants.HotelBasicFatigueRegenMult,
                    HotelTier.Premium => PersonnelBalanceConstants.HotelPremiumFatigueRegenMult,
                    _ => PersonnelBalanceConstants.HotelStandardFatigueRegenMult
                };
                int fatigueRegen = (int)(PersonnelBalanceConstants.FatigueRegenDayOff * regenMult * booking.nights);
                emp.currentFatigue = Math.Max(0, emp.currentFatigue - fatigueRegen);

                PersonnelService.NotifyStatusChanged(emp);
            }
        }
    }
}
