using System;
using System.Collections.Generic;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-5 / D10: Miesieczna wyplata pensji — pierwszy dzien kazdego miesiaca gry.
    ///
    /// Flow per pracownik (aktywny):
    /// 1. Oblicz pensje (currentSalaryGroszy)
    /// 2. Sprawdz czy staac firme (GameState.Money &gt;= salary/100)
    /// 3a. Tak: <see cref="EconomyManager.AddCost"/> category="Personnel" + update lastPaidDateIso
    /// 3b. Nie: missedPaymentsCount++, morale -20 (D10)
    /// 4. Po 3 missed z rzedu → auto-fire (bez severance) — pracownik odszedl dobrowolnie
    ///
    /// Idempotent: jeden raz per miesiac (tracker <see cref="_lastPaidYearMonth"/>).
    /// </summary>
    public static class PayrollService
    {
        /// <summary>Format "yyyy-MM" — ostatni miesiac dla ktorego wyplaty wykonano. Empty = nigdy.</summary>
        static string _lastPaidYearMonth = "";

        public static string LastPaidYearMonth => _lastPaidYearMonth;

        /// <summary>Reset dla nowej gry / save load.</summary>
        public static void Reset() => _lastPaidYearMonth = "";

        /// <summary>BUG-087: restore miesiaca ostatniej wyplaty po load.</summary>
        public static void RestoreLastPaidYearMonth(string yearMonth)
        {
            _lastPaidYearMonth = string.IsNullOrEmpty(yearMonth) ? "" : yearMonth;
        }

        /// <summary>
        /// Wywolane z <see cref="PersonnelDailyScheduler"/> kazdego dnia — sprawdza day=1 i
        /// czy ten miesiac jeszcze nie byl wyplacony.
        /// </summary>
        public static void ApplyDailyTick(string dateIso)
        {
            DateTime date;
            try { date = IsoTime.ParseDate(dateIso); } catch { return; }

            // Tylko pierwszy dzien miesiaca
            if (date.Day != 1) return;

            string ym = date.ToString("yyyy-MM");
            if (_lastPaidYearMonth == ym) return;

            PayAll(date);
            _lastPaidYearMonth = ym;
        }

        /// <summary>Wymusza wyplate teraz (debug / manual trigger).</summary>
        public static PayrollReport PayAll(DateTime date)
        {
            var report = new PayrollReport
            {
                yearMonth = date.ToString("yyyy-MM"),
                paidCount = 0,
                missedCount = 0,
                autoFiredCount = 0,
                totalPaidGroszy = 0,
                totalMissedGroszy = 0
            };

            var econ = EconomyManager.Instance;
            var toFire = new List<int>();

            // MB-1 Phase B: difficulty multiplier (0.5 - 2.0)
            float salaryMult = DifficultyService.Modifiers.SalaryMultiplier;

            // Fire ustawia status=Fired bez Remove z Employees → bezpieczna iteracja bez snapshot.
            // toFire kolekcjonuje IDs i wykonuje Fire DOPIERO po foreach (linie poniżej).
            foreach (var e in PersonnelService.Employees)
            {
                if (!e.IsActive) continue;

                // M6.5-3: brutto pracownika != koszt pracodawcy.
                // Pracownik dostaje brutto, firma odpisuje brutto + ZUS pracodawcy (~21% PL).
                int bruttoSalary = (int)(e.currentSalaryGroszy * salaryMult);
                if (bruttoSalary <= 0) continue;

                float employerMult = PayrollConstants.GetEmployerCostMultiplier(e.countryCode);
                int employerTotalCost = (int)(bruttoSalary * employerMult);

                // "Money * 100" ze zl do groszy. Approximate bo economy nie obsluguje groszy dokladnie
                // (patrz EconomyManager.AddCost: dzieli /100).
                bool canPay = GameState.Money * 100L >= employerTotalCost;

                if (canPay)
                {
                    if (econ != null)
                    {
                        econ.AddCost(-1, employerTotalCost, "Personnel",
                            $"Salary #{e.employeeId} {e.DisplayFullName} (brutto {bruttoSalary/100} zl + ZUS pracodawcy)");
                    }
                    else
                    {
                        GameState.Money -= employerTotalCost / 100;
                        Log.Warn("[PayrollService] EconomyManager null — deducted from GameState.Money directly");
                    }
                    e.lastPaidDateIso = date.ToString("yyyy-MM-dd");
                    e.missedPaymentsCount = 0;
                    report.paidCount++;
                    report.totalPaidGroszy += employerTotalCost;
                }
                else
                {
                    e.missedPaymentsCount++;
                    // BUG-060 v2: missed payment → salary bucket penalty
                    if (e.moraleBreakdown == null) e.moraleBreakdown = MoraleBreakdown.FromLegacyMorale(e.currentMorale);
                    e.moraleBreakdown.ApplyDeltaToSalary(PersonnelBalanceConstants.MoraleMissedPayment);
                    e.currentMorale = e.moraleBreakdown.Total;
                    report.missedCount++;
                    report.totalMissedGroszy += employerTotalCost;

                    Log.Warn($"[PayrollService] Missed salary for #{e.employeeId} {e.DisplayFullName} " +
                             $"(count={e.missedPaymentsCount}/3, morale now {e.currentMorale})");

                    // After 3 missed → voluntary leave (auto-fire, no severance)
                    if (e.missedPaymentsCount >= 3)
                    {
                        toFire.Add(e.employeeId);
                    }
                }
            }

            // Fire zbiorczo po petli (aby nie zepsuc iteracji)
            foreach (var id in toFire)
            {
                var emp = PersonnelService.GetById(id);
                if (emp == null) continue;
                Log.Warn($"[PayrollService] Auto-fire #{id} {emp.DisplayFullName} after 3 missed payments (voluntary leave)");
                PersonnelService.Fire(id, paySeverance: false);
                report.autoFiredCount++;
            }

            PersonnelService.NotifyEmployeeDataChanged();

            Log.Info($"[PayrollService] Payroll {report.yearMonth}: " +
                     $"paid={report.paidCount} ({report.totalPaidGroszy / 100}zł), " +
                     $"missed={report.missedCount} ({report.totalMissedGroszy / 100}zł), " +
                     $"auto-fired={report.autoFiredCount}");

            return report;
        }

        /// <summary>Prognoza miesiecznej sumy KOSZTU FIRMY (brutto + ZUS pracodawcy per kraj).
        /// M6.5-3: zwraca total cost firmy, nie sume brutto pracownikow.
        /// BUG-084: long accumulator + clamp dla endgame fanatyk (1500+ employees × wysokie salary
        /// → overflow w int). Wzorzec z BUG-073.</summary>
        public static int EstimateMonthlyTotalGroszy()
        {
            long total = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (!e.IsActive) continue;
                float mult = PayrollConstants.GetEmployerCostMultiplier(e.countryCode);
                total += (long)(e.currentSalaryGroszy * mult);
            }
            if (total > int.MaxValue)
            {
                Log.Warn($"[PayrollService] EstimateMonthlyTotal overflow: {total}gr > int.MaxValue, clamped");
                return int.MaxValue;
            }
            return total < 0 ? 0 : (int)total;
        }

        /// <summary>Suma brutto pracownikow (bez ZUS pracodawcy). Dla UI rozdzielenia "co dostaja pracownicy" vs "co kosztuje firme".
        /// BUG-084: long accumulator (analogicznie jak EstimateMonthlyTotalGroszy).</summary>
        public static int EstimateMonthlyBruttoTotalGroszy()
        {
            long total = 0;
            foreach (var e in PersonnelService.Employees)
                if (e.IsActive) total += e.currentSalaryGroszy;
            if (total > int.MaxValue)
            {
                Log.Warn($"[PayrollService] EstimateMonthlyBruttoTotal overflow: {total}gr > int.MaxValue, clamped");
                return int.MaxValue;
            }
            return total < 0 ? 0 : (int)total;
        }

        /// <summary>Per-role podzial KOSZTU FIRMY (brutto + ZUS pracodawcy per kraj).
        /// BUG-084: long accumulator per-role + clamp (analogicznie jak EstimateMonthlyTotalGroszy).</summary>
        public static Dictionary<EmployeeRole, int> EstimateMonthlyByRoleGroszy()
        {
            var accumLong = new Dictionary<EmployeeRole, long>();
            foreach (EmployeeRole role in Enum.GetValues(typeof(EmployeeRole)))
                accumLong[role] = 0L;

            foreach (var e in PersonnelService.Employees)
            {
                if (!e.IsActive) continue;
                float mult = PayrollConstants.GetEmployerCostMultiplier(e.countryCode);
                accumLong[e.role] += (long)(e.currentSalaryGroszy * mult);
            }

            var result = new Dictionary<EmployeeRole, int>();
            foreach (var kv in accumLong)
            {
                if (kv.Value > int.MaxValue)
                {
                    Log.Warn($"[PayrollService] EstimateMonthlyByRole overflow dla {kv.Key}: {kv.Value}gr > int.MaxValue, clamped");
                    result[kv.Key] = int.MaxValue;
                }
                else
                {
                    result[kv.Key] = kv.Value < 0 ? 0 : (int)kv.Value;
                }
            }
            return result;
        }
    }

    /// <summary>Wynik wyplaty jednego miesiaca — dla UI i log'ow.</summary>
    public class PayrollReport
    {
        public string yearMonth;
        public int paidCount;
        public int missedCount;
        public int autoFiredCount;
        public int totalPaidGroszy;
        public int totalMissedGroszy;
    }
}
