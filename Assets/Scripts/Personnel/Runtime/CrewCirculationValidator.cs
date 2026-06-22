using System;
using System.Collections.Generic;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-8 / §7.3: Walidator turnusow pracowniczych — 7-warstwowy, analog
    /// <c>CirculationValidator</c> z M5 ale dla ludzi.
    ///
    /// Warstwy:
    /// 1. <b>Spojnosc stacji</b>: duty[N].endStation == duty[N+1].startStation
    ///    (wyjatek: Handover moze w tej samej stacji, Break/Overnight nie wymagaja stacji)
    /// 2. <b>Spojnosc czasowa</b>: duty[N].endTime &lt; duty[N+1].startTime (+ handover buffer min 5min)
    /// 3. <b>Max work hours</b>: sum Service+Deadhead duration &lt;= 12h / doba per dayOffset
    /// 4. <b>Break after 4h</b>: po 4h ciaglej pracy Service+Deadhead wymagana Break &gt;= 30 min
    /// 5. <b>Skill check</b>: Service kategorii EI/EC wymaga driver skill &gt;= 3★
    ///    (M8-11 gdy kategoria IRJ dostepna z TrainRun, w M8-8 tylko placeholder)
    /// 6. <b>Availability</b>: assignedEmployee nie ma Vacation/SickLeave/Training
    ///    w <see cref="CrewCirculation.specificDates"/> lub <see cref="CrewCirculation.calendarDays"/>
    /// 7. <b>Uniqueness</b>: jeden pracownik = jeden aktywny turnus per dzien
    ///
    /// Multi-day (D20): duties z roznymi dayOffset liczone osobno per dzien.
    /// Overnight duty musi byc miedzy ostatnia sluzba dnia N a pierwsza dnia N+1.
    /// </summary>
    public static class CrewCirculationValidator
    {
        public static CrewValidationResult Validate(CrewCirculation c)
        {
            var result = new CrewValidationResult();
            if (c == null) { result.Errors.Add("Circulation null"); return result; }

            if (c.duties == null || c.duties.Count == 0)
            {
                result.Warnings.Add("Brak sluzb — turnus pusty");
                return result;
            }

            // Grupuj duties per dayOffset
            var dutiesByDay = new Dictionary<int, List<CrewDuty>>();
            for (int i = 0; i < c.duties.Count; i++)
            {
                var d = c.duties[i];
                if (d.dayOffset >= c.durationDays)
                {
                    result.Errors.Add($"Sluzba #{i} ma dayOffset {d.dayOffset} przekraczajacy durationDays {c.durationDays}");
                    continue;
                }
                if (!dutiesByDay.ContainsKey(d.dayOffset))
                    dutiesByDay[d.dayOffset] = new List<CrewDuty>();
                dutiesByDay[d.dayOffset].Add(d);
            }

            // Warstwa 1+2: Spojnosc miedzy duties (per day, sorted by startTime)
            foreach (var kv in dutiesByDay)
            {
                var dayDuties = kv.Value;
                dayDuties.Sort((a, b) => string.Compare(a.startTimeIso, b.startTimeIso, StringComparison.Ordinal));

                for (int i = 0; i < dayDuties.Count - 1; i++)
                {
                    var curr = dayDuties[i];
                    var next = dayDuties[i + 1];
                    ValidateSequence(curr, next, result, kv.Key);
                }
            }

            // Warstwa 3: Max work hours per dzien
            foreach (var kv in dutiesByDay)
            {
                int workMinutes = 0;
                foreach (var d in kv.Value)
                {
                    if (d.kind == CrewDutyKind.Service || d.kind == CrewDutyKind.Deadhead)
                        workMinutes += d.GetDurationMinutes();
                }
                int maxMinutes = PersonnelBalanceConstants.CrewMaxWorkHoursPerDay * 60;
                if (workMinutes > maxMinutes)
                {
                    result.Errors.Add($"Dzien {kv.Key + 1}: {workMinutes} min pracy przekracza limit {maxMinutes} min (12h)");
                }
            }

            // Warstwa 4: Break po 4h ciaglej pracy
            foreach (var kv in dutiesByDay)
            {
                int continuousMinutes = 0;
                foreach (var d in kv.Value)
                {
                    if (d.kind == CrewDutyKind.Service || d.kind == CrewDutyKind.Deadhead)
                    {
                        continuousMinutes += d.GetDurationMinutes();
                        if (continuousMinutes > PersonnelBalanceConstants.CrewMinBreakAfterHours * 60)
                        {
                            result.Warnings.Add($"Dzien {kv.Key + 1}: ciagla praca &gt; {PersonnelBalanceConstants.CrewMinBreakAfterHours}h bez przerwy (sluzba #{d.dutyIndex})");
                            continuousMinutes = 0; // reset po warning
                        }
                    }
                    else if (d.kind == CrewDutyKind.Break && d.GetDurationMinutes() >= PersonnelBalanceConstants.CrewMinBreakMinutes)
                    {
                        continuousMinutes = 0; // reset przez przerwe
                    }
                }
            }

            // Warstwa 5: Skill check (placeholder — post-EA z TrainRun kategoria)
            // BUG-010 cz.2 (post-EA): user'owa decyzja 2026-05-07 — w EA pracownicy
            // NIE potrzebują kwalifikacji kategorii (każdy może obsłużyć IC/RO/EI/lokalne).
            // Placeholder data + UI okno: <see cref="EmployeeQualifications"/>.categoryPermits.
            // Future: TimetableService.GetTrainRun(referencedTrainRunId).category lookup.

            // Warstwa 6: Availability pracownika (tylko gdy assigned)
            if (c.assignedEmployeeId > 0)
            {
                var emp = PersonnelService.GetById(c.assignedEmployeeId);
                if (emp == null)
                {
                    result.Errors.Add($"Przypisany pracownik #{c.assignedEmployeeId} nie istnieje");
                }
                else
                {
                    if (emp.role != c.role)
                        result.Errors.Add($"Pracownik jest {emp.role}, turnus wymaga {c.role}");

                    if (!emp.IsActive)
                        result.Errors.Add($"Pracownik {emp.DisplayFullName} jest {emp.status} (nieaktywny)");

                    // BUG-010 cz.3 + BUG-090: Schedule overrides (urlop/L4/training)
                    // blokują przypisanie tylko wtedy, gdy ich zakres realnie nachodzi
                    // na daty działania turnusu. Wcześniej dowolny przyszły urlop
                    // blokował aktywację każdego turnusu tego pracownika.
                    var sched = PersonnelService.GetSchedule(emp.employeeId);
                    if (sched != null)
                    {
                        foreach (var o in sched.overrides)
                        {
                            if (o.type == ScheduleOverrideType.Vacation ||
                                o.type == ScheduleOverrideType.SickLeave ||
                                o.type == ScheduleOverrideType.Training)
                            {
                                if (OverrideOverlapsCirculation(o, c))
                                {
                                    result.Errors.Add($"Pracownik ma {o.type} {o.dateIsoStart}..{o.dateIsoEnd} — nie może być na służbie w datach turnusu");
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            // Warstwa 7: Uniqueness (pracownik w 1 aktywnym turnusie per dzien)
            if (c.assignedEmployeeId > 0)
            {
                foreach (var other in CrewCirculationService.All)
                {
                    if (other.crewCirculationId == c.crewCirculationId) continue;
                    if (other.assignedEmployeeId != c.assignedEmployeeId) continue;
                    if (other.status != RailwayManager.Timetable.CirculationStatus.Active) continue;

                    // Sprawdz conflict calendar
                    if (HasCalendarConflict(c, other))
                    {
                        result.Errors.Add($"Pracownik juz ma aktywny turnus #{other.crewCirculationId} '{other.name}' w tych samych dniach");
                    }
                }
            }

            // Multi-day dodatkowe: overnight duties
            if (c.durationDays > 1)
            {
                ValidateOvernights(c, result);
            }

            return result;
        }

        static void ValidateSequence(CrewDuty curr, CrewDuty next, CrewValidationResult result, int dayOffset)
        {
            // Czasowa — curr.endTime &lt; next.startTime
            if (!string.IsNullOrEmpty(curr.endTimeIso) && !string.IsNullOrEmpty(next.startTimeIso))
            {
                if (string.Compare(curr.endTimeIso, next.startTimeIso, StringComparison.Ordinal) > 0)
                {
                    result.Errors.Add($"Dzien {dayOffset + 1}: sluzby #{curr.dutyIndex} i #{next.dutyIndex} " +
                                      $"nakladaja sie czasowo ({curr.endTimeIso} vs {next.startTimeIso})");
                }
            }

            // Handover buffer: min 5 min miedzy
            if (curr.kind == CrewDutyKind.Handover || next.kind == CrewDutyKind.Handover)
            {
                // BUG-031: TryParse zamiast try/catch — błędne ISO time strings
                // dorzucają Warning zamiast cichego skip.
                if (IsoTime.TryParseTime(curr.endTimeIso, out var end) &&
                    IsoTime.TryParseTime(next.startTimeIso, out var start))
                {
                    var gap = (start - end).TotalMinutes;
                    if (gap > 0 && gap < PersonnelBalanceConstants.CrewHandoverMinMinutes)
                        result.Warnings.Add($"Dzien {dayOffset + 1}: handover miedzy #{curr.dutyIndex}-#{next.dutyIndex} " +
                                            $"ma tylko {gap:F0} min buforu (min {PersonnelBalanceConstants.CrewHandoverMinMinutes})");
                }
                else
                {
                    result.Warnings.Add($"Dzien {dayOffset + 1}: handover miedzy #{curr.dutyIndex}-#{next.dutyIndex} " +
                                        $"ma niepoprawny format czasu ('{curr.endTimeIso}' / '{next.startTimeIso}') — pomijam handover check");
                }
            }

            // Spojnosc stacji (tylko dla Service/Deadhead — Break/Handover/Overnight bez wymogu)
            bool currHasStation = (curr.kind == CrewDutyKind.Service || curr.kind == CrewDutyKind.Deadhead);
            bool nextHasStation = (next.kind == CrewDutyKind.Service || next.kind == CrewDutyKind.Deadhead);
            if (currHasStation && nextHasStation)
            {
                if (!string.IsNullOrEmpty(curr.endStationName) && !string.IsNullOrEmpty(next.startStationName)
                    && curr.endStationName != next.startStationName)
                {
                    result.Errors.Add($"Dzien {dayOffset + 1}: sluzba #{curr.dutyIndex} konczy w '{curr.endStationName}', " +
                                      $"kolejna #{next.dutyIndex} startuje w '{next.startStationName}' " +
                                      $"— brak spojnosci (dodaj Deadhead lub Break)");
                }
            }
        }

        static void ValidateOvernights(CrewCirculation c, CrewValidationResult result)
        {
            // Multi-day musi konczyc w home depot (GameState.HomeDepotStationId hook — M8-11)
            // W M8-8: weryfikujemy tylko ze ostatnie duties nie pozostawia pracownika daleko

            for (int day = 0; day < c.durationDays - 1; day++)
            {
                // Po kazdym dniu (oprocz ostatniego) musi byc Overnight
                bool hasOvernight = false;
                foreach (var d in c.duties)
                {
                    if (d.dayOffset == day && d.kind == CrewDutyKind.Overnight)
                    {
                        hasOvernight = true;
                        if (d.overnightHotel == null)
                            result.Warnings.Add($"Overnight dnia {day + 1} bez przypisanego hotelu — uzyj fallback 'delegacja prywatna'");
                        break;
                    }
                }
                if (!hasOvernight)
                    result.Warnings.Add($"Brak Overnight po dniu {day + 1} — multi-day turnus bez noclegu");
            }
        }

        static bool HasCalendarConflict(CrewCirculation a, CrewCirculation b)
        {
            bool aSpecific = a.specificDates != null && a.specificDates.Count > 0;
            bool bSpecific = b.specificDates != null && b.specificDates.Count > 0;

            if (aSpecific && bSpecific)
            {
                foreach (var date in EnumerateSpecificCoveredDates(a))
                    if (SpecificCoversDate(b, date)) return true;
                return false;
            }

            if (aSpecific)
            {
                foreach (var date in EnumerateSpecificCoveredDates(a))
                    if (RecurringCoversDate(b, date)) return true;
                return false;
            }

            if (bSpecific)
            {
                foreach (var date in EnumerateSpecificCoveredDates(b))
                    if (RecurringCoversDate(a, date)) return true;
                return false;
            }

            // Oba cykliczne: konflikt gdy zakresy dni pokrywanych przez multi-day turnusy
            // maja wspolny dzien tygodnia.
            int aCovered = BuildCoveredDayMaskBits(a.calendarDays.bits, a.durationDays);
            int bCovered = BuildCoveredDayMaskBits(b.calendarDays.bits, b.durationDays);
            return (aCovered & bCovered) != 0;
        }

        static bool OverrideOverlapsCirculation(ScheduleOverride o, CrewCirculation c)
        {
            if (!TryParseDate(o.dateIsoStart, out var start)) return false;
            if (!TryParseDate(o.dateIsoEnd, out var end)) return false;
            if (end < start) (start, end) = (end, start);

            bool hasSpecificDates = c.specificDates != null && c.specificDates.Count > 0;
            if (hasSpecificDates)
            {
                foreach (var date in EnumerateSpecificCoveredDates(c))
                    if (date >= start && date <= end) return true;
                return false;
            }

            // Recurring turnus: sprawdzamy tylko okres override'a. Limit defensywny,
            // zeby uszkodzony save z wieloletnim override'em nie robil dlugiej petli.
            int maxDays = Math.Min(732, Math.Max(0, (int)(end - start).TotalDays) + 1);
            for (int i = 0; i < maxDays; i++)
            {
                if (RecurringCoversDate(c, start.AddDays(i))) return true;
            }
            return false;
        }

        static IEnumerable<DateTime> EnumerateSpecificCoveredDates(CrewCirculation c)
        {
            if (c?.specificDates == null) yield break;

            int days = Math.Max(1, c.durationDays);
            foreach (var iso in c.specificDates)
            {
                if (!TryParseDate(iso, out var start)) continue;
                for (int offset = 0; offset < days; offset++)
                    yield return start.AddDays(offset);
            }
        }

        static bool SpecificCoversDate(CrewCirculation c, DateTime date)
        {
            foreach (var covered in EnumerateSpecificCoveredDates(c))
                if (covered.Date == date.Date) return true;
            return false;
        }

        static bool RecurringCoversDate(CrewCirculation c, DateTime date)
        {
            if (c == null) return false;
            int days = Math.Max(1, c.durationDays);
            for (int offset = 0; offset < days; offset++)
            {
                var startDate = date.AddDays(-offset);
                int dayOfWeekMonday0 = ((int)startDate.DayOfWeek + 6) % 7;
                if (c.calendarDays.Runs(dayOfWeekMonday0)) return true;
            }
            return false;
        }

        static int BuildCoveredDayMaskBits(int startBits, int durationDays)
        {
            int result = 0;
            int days = Math.Max(1, durationDays);
            for (int offset = 0; offset < days; offset++)
                result |= ShiftDayMaskBits(startBits, offset);
            return result;
        }

        static int ShiftDayMaskBits(int bits, int offset)
        {
            int result = 0;
            for (int day = 0; day < 7; day++)
            {
                if ((bits & (1 << day)) == 0) continue;
                int shifted = (day + offset) % 7;
                result |= 1 << shifted;
            }
            return result;
        }

        static bool TryParseDate(string iso, out DateTime date)
        {
            date = default;
            if (string.IsNullOrEmpty(iso)) return false;
            try
            {
                date = IsoTime.ParseDate(iso.Length >= 10 ? iso.Substring(0, 10) : iso);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>M8-8: Wynik walidacji turnusu pracowniczego.</summary>
    public class CrewValidationResult
    {
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        public bool IsValid => Errors.Count == 0;

        public string GetSummary()
        {
            if (IsValid && Warnings.Count == 0) return "✓ OK";
            var sb = new System.Text.StringBuilder();
            if (Errors.Count > 0) sb.Append($"✗ {Errors.Count} blad{(Errors.Count == 1 ? "" : "ow")}");
            if (Warnings.Count > 0)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append($"⚠ {Warnings.Count} ostrzezen");
            }
            return sb.ToString();
        }
    }
}
