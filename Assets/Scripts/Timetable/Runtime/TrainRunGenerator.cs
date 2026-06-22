using System.Collections.Generic;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Generator konkretnych <see cref="TrainRun"/>ów z <see cref="Circulation"/>.
    /// Wywoływany gdy obieg przechodzi Draft → Active (M5 Etap 10). M9 Ruch pociągów
    /// używa tych instancji do spawnowania fizycznych pojazdów na mapie.
    ///
    /// Dla każdej aktywnej daty obiegu (z GetActiveDates) × każdego kroku w sekwencji
    /// generowany jest jeden TrainRun. Identyfikacja przez (circulationId, stepIndex,
    /// runDateIso) jest unikalna.
    /// </summary>
    public static class TrainRunGenerator
    {
        /// <summary>
        /// Generuje TrainRun'y dla całego aktywnego okna obiegu (GetActiveDates).
        /// Najpierw czyści istniejące runs z tego obiegu (Clear), potem tworzy nowe.
        /// </summary>
        public static int GenerateForCirculation(Circulation circulation)
        {
            if (circulation == null) return 0;

            // Wyczyść poprzednie runs z tego obiegu (na wypadek edycji)
            int cleared = ClearForCirculation(circulation.id);

            int generated = 0;
            var dates = circulation.GetActiveDates();
            foreach (var date in dates)
            {
                string dateIso = date.ToString("yyyy-MM-dd");
                // BUG-065 fix: explicit Utc DateTimeKind eliminuje desync między machinami w
                // różnych timezone'ach. Bez tego DateTimeOffset(date) interpretuje jako local
                // → laptop UTC+2 vs desktop UTC+0 dawałyby inny `runDateGameTime` dla tego
                // samego `runDateIso`. M10 MP host vs klient cross-timezone = desync.
                long dayEpochSec = new System.DateTimeOffset(
                    System.DateTime.SpecifyKind(date, System.DateTimeKind.Utc),
                    System.TimeSpan.Zero).ToUnixTimeSeconds();

                for (int stepIdx = 0; stepIdx < circulation.steps.Count; stepIdx++)
                {
                    var step = circulation.steps[stepIdx];
                    var tt = TimetableService.GetTimetable(step.timetableId);
                    if (tt == null)
                    {
                        Log.Warn($"[TrainRunGenerator] Brak Timetable #{step.timetableId} dla obiegu #{circulation.id}");
                        continue;
                    }

                    var run = new TrainRun
                    {
                        id = TimetableService.AllocateTrainRunId(),
                        timetableId = tt.id,
                        circulationId = circulation.id,
                        circulationStepIndex = stepIdx,
                        runDateIso = dateIso,
                        runDateGameTime = dayEpochSec,
                        startMinutesFromMidnight = tt.StartMinutes,
                        trainNumberSnapshot = tt.trainNumber
                    };
                    TimetableService.TrainRuns.Add(run);
                    generated++;
                }
            }

            Log.Info($"[TrainRunGenerator] Obieg #{circulation.id} '{circulation.name}': "
                     + $"wyczyszczono {cleared}, wygenerowano {generated} TrainRun'ów "
                     + $"({dates.Count} dni × {circulation.steps.Count} kroków)");
            return generated;
        }

        /// <summary>
        /// TD-037 (rolling window): DOGENEROWUJE TrainRun'y dla dat z bieżącego okna
        /// (<see cref="Circulation.GetActiveDates"/> liczy „od dziś"), których jeszcze NIE MA —
        /// klucz unikalności (circulationId, stepIndex, runDateIso). BEZ Clear — istniejące runy
        /// (i ich ID, do których linkuje CrewDuty.referencedTrainRunId) zostają nietknięte.
        ///
        /// Naprawia latentny bug: generacja była one-shot przy aktywacji (okno default 4 tyg) —
        /// po upłynięciu okna świat umierał. Wołane przy granicy dnia (TrainRunWindowTopUp)
        /// + po restore z save (save starszy niż okno → świat odżywa od dziś).
        /// </summary>
        public static int TopUpForCirculation(Circulation circulation)
        {
            if (circulation == null || circulation.status != CirculationStatus.Active) return 0;

            // Istniejące pary (stepIdx, dateIso) tego obiegu
            var existing = new HashSet<(int step, string date)>();
            foreach (var r in TimetableService.TrainRuns)
                if (r.circulationId == circulation.id)
                    existing.Add((r.circulationStepIndex, r.runDateIso));

            int generated = 0;
            var dates = circulation.GetActiveDates();
            foreach (var date in dates)
            {
                string dateIso = date.ToString("yyyy-MM-dd");
                long dayEpochSec = new System.DateTimeOffset(
                    System.DateTime.SpecifyKind(date, System.DateTimeKind.Utc),
                    System.TimeSpan.Zero).ToUnixTimeSeconds();

                for (int stepIdx = 0; stepIdx < circulation.steps.Count; stepIdx++)
                {
                    if (existing.Contains((stepIdx, dateIso))) continue;

                    var step = circulation.steps[stepIdx];
                    var tt = TimetableService.GetTimetable(step.timetableId);
                    if (tt == null) continue;

                    TimetableService.TrainRuns.Add(new TrainRun
                    {
                        id = TimetableService.AllocateTrainRunId(),
                        timetableId = tt.id,
                        circulationId = circulation.id,
                        circulationStepIndex = stepIdx,
                        runDateIso = dateIso,
                        runDateGameTime = dayEpochSec,
                        startMinutesFromMidnight = tt.StartMinutes,
                        trainNumberSnapshot = tt.trainNumber
                    });
                    generated++;
                }
            }

            if (generated > 0)
                Log.Info($"[TrainRunGenerator] Top-up obiegu #{circulation.id} '{circulation.name}': "
                         + $"+{generated} TrainRun'ów (okno {dates.Count} dni)");
            return generated;
        }

        /// <summary>Usuwa wszystkie TrainRun'y należące do danego obiegu.</summary>
        public static int ClearForCirculation(int circulationId)
        {
            int removed = 0;
            for (int i = TimetableService.TrainRuns.Count - 1; i >= 0; i--)
            {
                if (TimetableService.TrainRuns[i].circulationId == circulationId)
                {
                    TimetableService.TrainRuns.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>Zwraca wszystkie TrainRun'y dla danego obiegu (do query/debug).</summary>
        public static List<TrainRun> GetForCirculation(int circulationId)
        {
            var result = new List<TrainRun>();
            foreach (var r in TimetableService.TrainRuns)
                if (r.circulationId == circulationId)
                    result.Add(r);
            return result;
        }
    }
}
