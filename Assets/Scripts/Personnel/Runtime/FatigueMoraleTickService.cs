using System;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-5: Daily tick aktualizujacy fatigue i morale pracownikow.
    ///
    /// <b>Fatigue</b> (0-100, §5.2):
    /// - OnShift: +rate × 8h (rate per rola w <see cref="RoleDefinitions.GetFatigueRatePerHour"/>)
    /// - Resting: -<see cref="PersonnelBalanceConstants.FatigueRegenDayOff"/> (default 80, pelny reset)
    /// - Sick/LongSick: -10 (chorujacy odpoczywa ale nie pelny regen)
    /// - Available: bez zmian
    ///
    /// <b>Morale</b> (0-100, §5.1):
    /// - Pensja &gt;10% powyzej rynkowej → +1/dzien
    /// - Pensja &gt;10% ponizej rynkowej → -2/dzien
    /// - Fatigue &gt; 80 przez 2+ dni z rzedu → -3/dzien (tracker w <see cref="ShiftManager"/>)
    /// - Nocna zmiana 3+ razy w ostatnich 7 dniach → -1/dzien (overtime penalty — uproszczone)
    /// - (Missed payment: natychmiastowe -20 obslugiwane przez PayrollService)
    ///
    /// Po zmianach: <see cref="PersonnelService.NotifyEmployeeDataChanged"/> dla UI refresh.
    /// </summary>
    public static class FatigueMoraleTickService
    {
        /// <summary>Wywolane z <see cref="PersonnelDailyScheduler"/> po ShiftManager.</summary>
        public static void ApplyDailyTick(string dateIso)
        {
            // MM-7: cache room morale bonus raz na tick (state pokoi nie zmienia się
            // w trakcie loop'a, no point recompute per pracownik).
            int roomMoraleBonus = ComputeRoomMoraleBonus();

            int updated = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (!e.IsActive) continue;
                if (e.status == EmployeeStatus.Retired || e.status == EmployeeStatus.Fired) continue;

                UpdateFatigue(e);
                UpdateMorale(e, roomMoraleBonus);
                ClampAll(e);
                updated++;
            }
            if (updated > 0)
            {
                PersonnelService.NotifyEmployeeDataChanged();
                Log.Debug($"[FatigueMoraleTickService] Tick {dateIso}: {updated} employees updated " +
                          $"(room morale bonus +{roomMoraleBonus})");
            }
        }

        /// <summary>MM-7: cache morale bonus z Supervisor/Social/Bathroom raz na tick.</summary>
        static int ComputeRoomMoraleBonus()
        {
            int supervisorBonus = MoraleBonusService.SupervisorBonusForLvl(MoraleBonusService.GetSupervisorLvl());

            int socialBonus = MoraleBonusService.AnyRoomReachable(DepotSystem.RoomType.Social)
                ? MoraleBonusService.SocialBonusForLvl(MoraleBonusService.GetSocialLvl())
                : 0;

            int bathroomBonus = MoraleBonusService.AnyRoomReachable(DepotSystem.RoomType.Bathroom)
                ? UnityEngine.Mathf.RoundToInt(MoraleBonusService.BathroomBonusForLvl(MoraleBonusService.GetBathroomLvl()))
                : 0;

            return supervisorBonus + socialBonus + bathroomBonus;
        }

        static void UpdateFatigue(Employee e)
        {
            switch (e.status)
            {
                case EmployeeStatus.OnShift:
                    {
                        float rate = RoleDefinitions.GetFatigueRatePerHour(e.role);
                        int shiftHours = PersonnelBalanceConstants.ShiftDurationSec / 3600; // 8
                        e.currentFatigue += (int)(rate * shiftHours);
                        break;
                    }
                case EmployeeStatus.Resting:
                    e.currentFatigue -= (int)PersonnelBalanceConstants.FatigueRegenDayOff;
                    break;
                case EmployeeStatus.Sick:
                case EmployeeStatus.LongSick:
                    e.currentFatigue -= 10;
                    break;
                case EmployeeStatus.Training:
                    e.currentFatigue -= 5;
                    break;
                default:
                    break; // Available: neutral
            }

            // Fatigue tracker dla morale penalty
            if (e.currentFatigue > 80)
                ShiftManager.IncrementFatigueOverDays(e.employeeId);
            else
                ShiftManager.ResetFatigueTracker(e.employeeId);
        }

        static void UpdateMorale(Employee e, int roomMoraleBonus)
        {
            // BUG-060 v2: per-source bucket recompute (cap'd osobno → bonus z roomu nigdy nie
            // znika "cicho" przez global clamp). External events (bonus pay, missed payment,
            // hotel reject, fired colleague) modyfikują odpowiedni bucket przez
            // moraleBreakdown.ApplyDeltaToSalary/Room — patrz HotelBookingService, PayrollService.

            // Lazy migration: legacy save bez breakdown → init z `currentMorale`
            if (e.moraleBreakdown == null)
                e.moraleBreakdown = MoraleBreakdown.FromLegacyMorale(e.currentMorale);

            // ── 1) Salary bucket: smooth ramp ku target (cap 35) ──
            // Target zależny od salary vs rynek: 35 max gdy >+10%, 0 gdy <-10%, 17 (≈50%) neutral.
            int marketSalary = RoleDefinitions.GetExpectedSalaryGroszy(e.role, e.skill);
            int salaryTarget;
            if (marketSalary <= 0)
            {
                salaryTarget = PersonnelBalanceConstants.MoraleSalaryCapMax / 2; // unknown → neutral
            }
            else
            {
                float salaryDiff = (e.currentSalaryGroszy - marketSalary) / (float)marketSalary;
                if (salaryDiff > 0.10f) salaryTarget = PersonnelBalanceConstants.MoraleSalaryCapMax;       // 35
                else if (salaryDiff < -0.10f) salaryTarget = 0;                                            // 0
                else salaryTarget = PersonnelBalanceConstants.MoraleSalaryCapMax / 2;                      // ~17 neutral
            }
            // Smooth ramp ±MoraleDailySalaryAbove (1 pkt/dzień ku target — gradual change)
            int salaryDelta = Math.Sign(salaryTarget - e.moraleBreakdown.salaryContrib);
            if (salaryDelta != 0)
                e.moraleBreakdown.ApplyDeltaToSalary(salaryDelta * PersonnelBalanceConstants.MoraleDailySalaryAbove);

            // ── 2) Fatigue bucket: target = (100 - fatigue) / 4 → 25 gdy 0 fatigue, 0 gdy 100 ──
            int fatigueTarget = (100 - Math.Clamp(e.currentFatigue, 0, 100)) * PersonnelBalanceConstants.MoraleFatigueCapMax / 100;
            // Penalty extra gdy fatigue > 80 przez 2+ dni (zamiast monolithic morale -3)
            if (ShiftManager.GetFatigueOverDays(e.employeeId) >= 2)
                fatigueTarget = Math.Max(0, fatigueTarget + PersonnelBalanceConstants.MoraleFatigueOverPenalty);
            e.moraleBreakdown.SetFatigue(fatigueTarget);

            // ── 3) Overtime bucket: 25 normalna zmiana, 0 gdy Night + fatigue > 50 ──
            int overtimeTarget = PersonnelBalanceConstants.MoraleOvertimeCapMax;
            if (e.currentShift == ShiftType.Night && e.currentFatigue > 50)
                overtimeTarget = Math.Max(0, overtimeTarget + PersonnelBalanceConstants.MoraleOvertimePenalty * 5); // -5 (5× -1 penalty)
            e.moraleBreakdown.SetOvertime(overtimeTarget);

            // ── 4) Room bucket: cache'd bonus z Supervisor + Social + Bathroom upgrades (cap 15) ──
            // MM-7: bonus per-employee niezróżnicowany (wszyscy active = ten sam roomMoraleBonus).
            // Post-EA: per-employee accessSide pathfinding dla bardziej precyzyjnego rozkładu.
            e.moraleBreakdown.SetRoom(roomMoraleBonus);

            // ── Sync legacy currentMorale field z breakdown total (Read API stays consistent) ──
            e.currentMorale = e.moraleBreakdown.Total;
        }

        static void ClampAll(Employee e)
        {
            // BUG-060 v2: morale jest computed z breakdown.Total (już per-bucket clamped).
            // Tutaj zachowujemy fallback clamp na wypadek direct legacy mutations
            // (PersonnelService.GiveBonus, PayrollService.OnMissed itp. — patrz audit).
            e.currentMorale = Math.Clamp(e.currentMorale, 0, 100);
            e.currentFatigue = Math.Clamp(e.currentFatigue, 0, 100);
        }
    }
}
