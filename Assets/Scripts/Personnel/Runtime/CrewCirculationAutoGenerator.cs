using System;
using System.Collections.Generic;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Timetable;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-9 / §7.4: Auto-generator turnusow pracowniczych — chain-building analog
    /// <c>CirculationAutoGenerator</c> z M5 ale dla ludzi.
    ///
    /// Algorytm:
    /// 1. Sortuj TrainRun'y wg startTimeIso ascending
    /// 2. Filter: dla DriverOnly/ConductorOnly tylko kursy wymagajace tej roli (D16 per composition)
    /// 3. Chain building — per turnus:
    ///    a. Pierwszy nieprzypisany TR = pierwsza sluzba (Service)
    ///    b. Loop: znajdz kolejny TR gdzie startStation == previous.endStation && gap &gt;= minGap &amp;&amp; total work &lt;= maxHours
    ///    c. Jesli found: add Break + Service
    ///    d. Jesli not: stop, move to next turnus
    /// 4. Jesli koniec turnusu daleko od home &amp;&amp; autoReturnDeadhead: add Deadhead placeholder
    ///
    /// Multi-day (D20): jesli <see cref="CrewAutoGenSettings.allowMultiDay"/>, chain przekraczajace
    /// 1-day limit dostaje Overnight + dayOffset++. M8-9: flag jest, ale pelna implementacja M8-11.
    ///
    /// Generator nie dotyka CrewCirculationService dopoki <see cref="Commit"/> — gracz widzi preview i decyduje.
    /// </summary>
    public static class CrewCirculationAutoGenerator
    {
        /// <summary>Glowna funkcja — zwraca preview nie-committed turnusow.</summary>
        public static CrewAutoGenPreview Generate(
            List<CrewAutoGenTrainRunInput> inputs,
            CrewAutoGenSettings settings)
        {
            var preview = new CrewAutoGenPreview { SettingsUsed = settings?.Clone() ?? new CrewAutoGenSettings() };
            if (inputs == null || inputs.Count == 0)
            {
                preview.Warnings.Add("Brak TrainRun'ów na wejściu — pusta pula.");
                return preview;
            }
            settings ??= new CrewAutoGenSettings();

            // Group by date
            var byDate = new Dictionary<string, List<CrewAutoGenTrainRunInput>>();
            foreach (var tr in inputs)
            {
                string date = string.IsNullOrEmpty(tr.dateIso) ? "" : tr.dateIso;
                if (!byDate.ContainsKey(date)) byDate[date] = new List<CrewAutoGenTrainRunInput>();
                byDate[date].Add(tr);
            }

            int circCounter = 1;
            foreach (var kv in byDate)
            {
                var dateRuns = kv.Value;
                // Sort by startTime
                dateRuns.Sort((a, b) => string.Compare(a.startTimeIso ?? "", b.startTimeIso ?? "", StringComparison.Ordinal));

                // Generuj dla wybranych rol
                if (settings.roleMode == AutoGenRoleMode.DriverOnly || settings.roleMode == AutoGenRoleMode.Both)
                {
                    GenerateForRole(dateRuns, EmployeeRole.Driver, kv.Key, settings, preview, ref circCounter);
                }

                if (settings.roleMode == AutoGenRoleMode.ConductorOnly || settings.roleMode == AutoGenRoleMode.Both)
                {
                    // Filter: tylko kursy wymagajace konduktora (D16/D31)
                    var conductorNeeded = FilterConductorRequired(dateRuns);
                    GenerateForRole(conductorNeeded, EmployeeRole.Conductor, kv.Key, settings, preview, ref circCounter);
                }
            }

            // Policz unused
            int totalRuns = inputs.Count;
            int usedRuns = 0;
            foreach (var c in preview.GeneratedCirculations)
                foreach (var d in c.duties)
                    if (d.kind == CrewDutyKind.Service && d.referencedTrainRunId > 0) usedRuns++;
            preview.UnusedTrainRunIds = Math.Max(0, totalRuns - usedRuns);

            if (preview.UnusedTrainRunIds > 0)
                preview.Warnings.Add($"{preview.UnusedTrainRunIds} TrainRun'ów nie zmieściło się w turnusy (limit 12h lub brak chainu).");

            Log.Info($"[CrewCirculationAutoGenerator] Generated {preview.GeneratedCirculations.Count} turnuses " +
                     $"({preview.TotalDuties} duties, {preview.DeadheadsGenerated} deadheads, {preview.UnusedTrainRunIds} unused)");
            return preview;
        }

        static void GenerateForRole(
            List<CrewAutoGenTrainRunInput> runs,
            EmployeeRole role,
            string dateIso,
            CrewAutoGenSettings settings,
            CrewAutoGenPreview preview,
            ref int circCounter)
        {
            if (runs.Count == 0) return;

            var remaining = new List<CrewAutoGenTrainRunInput>(runs);
            while (remaining.Count > 0)
            {
                var first = remaining[0];
                remaining.RemoveAt(0);

                var c = new CrewCirculation
                {
                    crewCirculationId = -circCounter, // negatywne id = preview (real id przyznaje Commit)
                    name = $"{settings.namePrefix}-{role.ToString()[0]}-{circCounter:D2}",
                    role = role,
                    assignedEmployeeId = -1,
                    calendarDays = DayMask.Daily(),
                    specificDates = new List<string> { dateIso },
                    duties = new List<CrewDuty>(),
                    status = CirculationStatus.Draft,
                    durationDays = 1,
                    notes = $"Auto-generated {DateTime.Now:HH:mm}"
                };
                circCounter++;

                // First duty = Service
                var firstDuty = MakeServiceDuty(first, 0, c.duties.Count);
                c.duties.Add(firstDuty);
                preview.TotalDuties++;

                int totalMinutes = firstDuty.GetDurationMinutes();
                string lastEndStation = first.endStation;
                string lastEndTime = first.endTimeIso;

                // Chain building
                while (true)
                {
                    var next = FindNextChainable(remaining, lastEndStation, lastEndTime, settings.minGapMinutes);
                    if (next == null) break;

                    int nextDuration = ComputeDurationMinutes(next.startTimeIso, next.endTimeIso);
                    int maxMinutes = settings.maxWorkHoursPerDay * 60;
                    if (totalMinutes + nextDuration > maxMinutes)
                    {
                        // Exceeded limit — stop chain
                        break;
                    }

                    // Add Break if gap > 0
                    int gapMin = ComputeGapMinutes(lastEndTime, next.startTimeIso);
                    if (gapMin > 0)
                    {
                        c.duties.Add(new CrewDuty
                        {
                            kind = CrewDutyKind.Break,
                            dayOffset = 0,
                            startTimeIso = lastEndTime,
                            endTimeIso = next.startTimeIso,
                            startStationName = lastEndStation,
                            endStationName = lastEndStation,
                            referencedTrainRunId = -1,
                            dutyIndex = c.duties.Count
                        });
                        preview.TotalDuties++;
                    }

                    // Add Service
                    var nextDuty = MakeServiceDuty(next, 0, c.duties.Count);
                    c.duties.Add(nextDuty);
                    preview.TotalDuties++;
                    totalMinutes += nextDuration;
                    lastEndStation = next.endStation;
                    lastEndTime = next.endTimeIso;

                    remaining.Remove(next);
                }

                // Deadhead return jesli nie w home depot
                // M8-11: porownanie z GameState.HomeDepotStationId (wymaga mapowania stationName→id).
                // W M8-9: placeholder — zawsze generuj jesli autoReturnDeadhead i lastEndStation != first.startStation
                if (settings.autoReturnDeadhead && !string.IsNullOrEmpty(lastEndStation)
                    && lastEndStation != first.startStation)
                {
                    try
                    {
                        var retStart = IsoTime.ParseTime(lastEndTime).Add(TimeSpan.FromMinutes(settings.minGapMinutes));
                        var retEnd = retStart.Add(TimeSpan.FromHours(2)); // 2h placeholder

                        c.duties.Add(new CrewDuty
                        {
                            kind = CrewDutyKind.Deadhead,
                            dayOffset = 0,
                            startTimeIso = retStart.ToString(@"hh\:mm\:ss"),
                            endTimeIso = retEnd.ToString(@"hh\:mm\:ss"),
                            startStationName = lastEndStation,
                            endStationName = first.startStation, // home
                            referencedTrainRunId = -1,
                            dutyIndex = c.duties.Count,
                            referencedCirculationId = -1
                        });
                        preview.TotalDuties++;
                        preview.DeadheadsGenerated++;
                    }
                    catch
                    {
                        preview.Warnings.Add($"Turnus {c.name}: nie udalo sie wygenerowac deadhead powrotnego (parse error)");
                    }
                }

                preview.GeneratedCirculations.Add(c);
            }
        }

        /// <summary>Kursy wymagajace konduktora wg D16/D31 (wagon/EMU/DMU osobno).</summary>
        static List<CrewAutoGenTrainRunInput> FilterConductorRequired(List<CrewAutoGenTrainRunInput> runs)
        {
            var result = new List<CrewAutoGenTrainRunInput>();
            foreach (var r in runs)
            {
                bool needed = r.passengerCarsCount > PersonnelBalanceConstants.ConductorRequiredFromWagonCount
                           || r.emuCount > PersonnelBalanceConstants.ConductorRequiredFromEmuCount
                           || r.dmuCount > PersonnelBalanceConstants.ConductorRequiredFromDmuCount;
                if (needed) result.Add(r);
            }
            return result;
        }

        static CrewAutoGenTrainRunInput FindNextChainable(
            List<CrewAutoGenTrainRunInput> remaining,
            string startStation,
            string afterTime,
            int minGapMinutes)
        {
            foreach (var candidate in remaining)
            {
                if (candidate.startStation != startStation) continue;
                int gap = ComputeGapMinutes(afterTime, candidate.startTimeIso);
                if (gap < minGapMinutes) continue;
                return candidate;
            }
            return null;
        }

        static int ComputeDurationMinutes(string startIso, string endIso)
        {
            try
            {
                var s = IsoTime.ParseTime(startIso);
                var e = IsoTime.ParseTime(endIso);
                var span = e - s;
                if (span.TotalMinutes < 0) span = span.Add(TimeSpan.FromHours(24));
                return (int)span.TotalMinutes;
            }
            catch { return 0; }
        }

        static int ComputeGapMinutes(string afterIso, string beforeIso)
        {
            try
            {
                var a = IsoTime.ParseTime(afterIso);
                var b = IsoTime.ParseTime(beforeIso);
                var gap = b - a;
                if (gap.TotalMinutes < 0) gap = gap.Add(TimeSpan.FromHours(24));
                return (int)gap.TotalMinutes;
            }
            catch { return int.MaxValue; }
        }

        static CrewDuty MakeServiceDuty(CrewAutoGenTrainRunInput tr, int dayOffset, int dutyIndex)
        {
            return new CrewDuty
            {
                kind = CrewDutyKind.Service,
                dayOffset = dayOffset,
                startTimeIso = tr.startTimeIso,
                endTimeIso = tr.endTimeIso,
                startStationName = tr.startStation,
                endStationName = tr.endStation,
                referencedTrainRunId = tr.trainRunId,
                referencedCirculationId = tr.circulationId,
                dutyIndex = dutyIndex
            };
        }

        // ═══ Commit ═══

        /// <summary>
        /// Applikuje preview jako realny stan — tworzy <see cref="CrewCirculation"/> przez Service.
        /// Duplikaty (wg nazwy) pominiete — gracz moze commit wielokrotnie.
        /// </summary>
        public static int Commit(CrewAutoGenPreview preview)
        {
            if (preview == null || preview.GeneratedCirculations.Count == 0) return 0;

            int committed = 0;
            foreach (var src in preview.GeneratedCirculations)
            {
                // Check duplicate by name
                bool dup = false;
                foreach (var existing in CrewCirculationService.All)
                {
                    if (existing.name == src.name) { dup = true; break; }
                }
                if (dup) continue;

                var newCirc = CrewCirculationService.Create(src.name, src.role);
                if (newCirc == null) continue;

                CrewCirculationService.SetDurationDays(newCirc.crewCirculationId, src.durationDays);
                foreach (var d in src.duties)
                {
                    // Reset dutyIndex (Add auto-assigns)
                    d.dutyIndex = -1;
                    CrewCirculationService.AddDuty(newCirc.crewCirculationId, d);
                }

                committed++;
            }

            Log.Info($"[CrewCirculationAutoGenerator] Committed {committed}/{preview.GeneratedCirculations.Count} circulations");
            return committed;
        }

        // ═══ Debug: sample fake timetable ═══

        /// <summary>
        /// Debug helper — generuje 5 przykladowych TrainRun'ow dla szybkiego testowania.
        /// W produkcji: pobiera z TimetableService (M8-11 adapter).
        /// </summary>
        public static List<CrewAutoGenTrainRunInput> DebugCreateSampleTimetable(string dateIso)
        {
            return new List<CrewAutoGenTrainRunInput>
            {
                new() { trainRunId = 1001, startStation = "Krakow Glowny", endStation = "Warszawa Zachodnia",
                        startTimeIso = "06:00:00", endTimeIso = "09:30:00", dateIso = dateIso,
                        irjCategory = "EI", passengerCarsCount = 6 },
                new() { trainRunId = 1002, startStation = "Warszawa Zachodnia", endStation = "Olsztyn Glowny",
                        startTimeIso = "11:00:00", endTimeIso = "14:00:00", dateIso = dateIso,
                        irjCategory = "RO", emuCount = 2 },
                new() { trainRunId = 1003, startStation = "Olsztyn Glowny", endStation = "Warszawa Zachodnia",
                        startTimeIso = "16:00:00", endTimeIso = "19:00:00", dateIso = dateIso,
                        irjCategory = "RO", emuCount = 2 },
                new() { trainRunId = 1004, startStation = "Warszawa Zachodnia", endStation = "Krakow Glowny",
                        startTimeIso = "20:00:00", endTimeIso = "23:30:00", dateIso = dateIso,
                        irjCategory = "EI", passengerCarsCount = 6 },
                new() { trainRunId = 1005, startStation = "Krakow Glowny", endStation = "Rzeszow Glowny",
                        startTimeIso = "14:00:00", endTimeIso = "17:00:00", dateIso = dateIso,
                        irjCategory = "RE", passengerCarsCount = 4 }
            };
        }
    }
}
