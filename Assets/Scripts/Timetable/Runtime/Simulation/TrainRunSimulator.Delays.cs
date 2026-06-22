using System;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;

namespace RailwayManager.Timetable.Simulation
{
    public partial class TrainRunSimulator
    {
        // ── Delay reputation events + cascade do nastepnego kursu ───

        /// <summary>
        /// M6-5: Po despawnie kursu, oceń opóźnienie i aplikuj reputation event.
        /// Zbiera stacje z trasy (startowa, końcowa, pośrednie) + województwa.
        /// </summary>
        void ApplyDelayReputationEvent(SimulatedTrain st)
        {
            var rep = RailwayManager.Timetable.Economy.ReputationManager.Instance;
            if (rep == null) return;

            int delaySec = st.trainRun.currentDelaySec;
            RailwayManager.Timetable.Economy.ReputationEventType? evt = null;

            // TD-023 fix 2026-05-15: align z subsidy ±5min on-time (consistent gameplay feedback).
            // Stare progi 1/5/15min powodowały "cichy spadek reputacji przy 75% punctuality"
            // gdy subsidy nadal pokazywała on-time. Teraz: ≤5min = brak event (= on-time w obu
            // systemach, source of truth: EconomyManager.PunctualityThresholdSec=300s).
            // Real-world grounding: PKP IC / Polregio statystyki używają ~5min tolerance.
            if (delaySec >= 30 * 60) evt = RailwayManager.Timetable.Economy.ReputationEventType.DelayMajor;
            else if (delaySec >= 15 * 60) evt = RailwayManager.Timetable.Economy.ReputationEventType.DelayMedium;
            else if (delaySec >= 5 * 60) evt = RailwayManager.Timetable.Economy.ReputationEventType.DelayMinor;

            if (!evt.HasValue) return;

            // Zbierz stacje z trasy — używamy effective timetable stops
            var stationIds = new System.Collections.Generic.List<int>();
            var tt = TimetableService.GetTimetable(st.trainRun.timetableId);
            if (tt != null)
            {
                // Konwersja stationNodeId → stationId via RailwayStation
                var init = TimetableInitializer.Instance;
                if (init?.Stations != null)
                {
                    foreach (var stop in tt.stops)
                    {
                        foreach (var rs in init.Stations)
                        {
                            if (rs.pathNodeId == stop.stationNodeId)
                            {
                                stationIds.Add(rs.stationId);
                                break;
                            }
                        }
                    }
                }
            }

            // Województwa — na razie empty (route crossing wymagałby Voivodeship lookup per-stationId)
            rep.ApplyEvent(evt.Value, stationIds, null,
                $"run#{st.trainRun.id} '{st.trainRun.trainNumberSnapshot}' delay={delaySec}s");
        }

        /// <summary>
        /// Kaskada opóźnień: jeśli pociąg kończy kurs z opóźnieniem, następny kurs
        /// tego pojazdu w obiegu (Circulation) startuje z tym samym opóźnieniem
        /// (pomniejszonym o ewentualny bufor między kursami).
        /// </summary>
        void PropagateDelayToNextRun(SimulatedTrain st)
        {
            var tr = st.trainRun;
            if (tr.currentDelaySec <= 0) return;
            if (tr.circulationId < 0) return; // standalone, nie w obiegu

            // Znajdź następny krok w obiegu
            var circulation = CirculationService.GetCirculation(tr.circulationId);
            if (circulation == null) return;

            int nextStepIdx = tr.circulationStepIndex + 1;
            if (nextStepIdx >= circulation.steps.Count) return; // ostatni krok w obiegu

            // Znajdź TrainRun następnego kroku na ten sam dzień
            var nextStep = circulation.steps[nextStepIdx];
            TrainRun nextRun = null;
            foreach (var candidate in TimetableService.TrainRuns)
            {
                if (candidate.circulationId == tr.circulationId &&
                    candidate.circulationStepIndex == nextStepIdx &&
                    candidate.runDateIso == tr.runDateIso &&
                    !candidate.isCompleted && !candidate.isCancelled)
                {
                    nextRun = candidate;
                    break;
                }
            }

            if (nextRun == null) return;

            // Bufor: czas między planowanym końcem tego kursu a planowanym startem następnego
            var lastStop = st.timetable.stops[st.timetable.stops.Count - 1];
            float thisEndSec = st.departureTimeOfDaySec + lastStop.plannedArrivalSec;
            float nextStartSec = nextRun.startMinutesFromMidnight * 60f;
            float bufferSec = nextStartSec - thisEndSec;

            // Propagowane opóźnienie = obecne opóźnienie - bufor (bufor absorbuje część)
            int rawPropagated = tr.currentDelaySec - (int)Mathf.Max(bufferSec, 0f);
            if (rawPropagated <= 0) return; // bufor wystarczył

            // MB-1 Phase B: difficulty multiplier (0.0 = brak kaskady, 2.5 = realny efekt domina)
            float diffMult = DifficultyService.Modifiers.DelayPropagationMultiplier;
            int propagatedDelay = (int)(rawPropagated * diffMult);
            if (propagatedDelay <= 0) return; // mnożnik 0.0 → brak kaskady

            // BUG-051 fix: Math.Max zamiast nadpisywania. NextRun mogło już mieć własne
            // opóźnienie (np. blok zajęty + cascade z innego pociągu) — propagatedDelay
            // nie powinno tego skasować. Bierzemy max (większe opóźnienie wygrywa).
            int beforeDelay = nextRun.currentDelaySec;
            nextRun.currentDelaySec = Math.Max(beforeDelay, propagatedDelay);
            Log.Info($"[TrainRunSimulator] Delay cascade: '{tr.trainNumberSnapshot}' → " +
                     $"'{nextRun.trainNumberSnapshot}' on {tr.runDateIso}: " +
                     $"{tr.currentDelaySec}s delay, {bufferSec:F0}s buffer, " +
                     $"propagated {propagatedDelay}s (×{diffMult:F2}), " +
                     $"nextRun delay: {beforeDelay}s → {nextRun.currentDelaySec}s");
        }
    }
}
