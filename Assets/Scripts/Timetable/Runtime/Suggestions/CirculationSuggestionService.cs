using System;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.SharedUI.Suggestions;

namespace RailwayManager.Timetable.Suggestions
{
    /// <summary>
    /// Sugestia połączenia dwóch rozkładów w obieg (M5) — earlier kończy w X, later zaczyna w X
    /// w czasie 5-30 min po. Compatible consist (oba MultipleUnit lub oba LocoWithCars).
    /// </summary>
    [Serializable]
    public struct CirculationSuggestion
    {
        public int timetableIdEarlier;
        public int timetableIdLater;
        public int connectionStationNodeId;
        public string connectionStationName;
        /// <summary>Time gap (sekundy) między arrival earlier a departure later.</summary>
        public int gapSec;
        public string description;
        public string contextKey;
    }

    /// <summary>
    /// M-TimetableUX F1.12: Proactive circulation suggestion (at-save trigger).
    ///
    /// **Trigger:** caller wywołuje <see cref="GenerateSuggestions(int)"/> po <see cref="TimetableService.OnTimetablesChanged"/>
    /// (lub explicit po save w UI workflow). Pre-F1.12: trigger manualny przez UI modal lub
    /// ContextMenu test.
    ///
    /// **Algorithm:**
    /// 1. Iterate <see cref="TimetableService.Timetables"/>
    /// 2. Per pair (TR_new, TR_existing): check czy endStation TR_a == startStation TR_b w time gap 5-30 min
    /// 3. Filter consist compatibility (composition.mode match)
    /// 4. Filter via <see cref="SuggestionMemoryService"/> (skip dismissed)
    /// 5. Generate <see cref="CirculationSuggestion"/> z stable contextKey
    ///
    /// **Side effects on Accept** deferred do F1.16 — pre-F1.16 tylko mark accepted w
    /// SuggestionMemoryService. Real Accept = call <c>CirculationService.AddCirculation</c> z
    /// 2-step circulation (TR_earlier → TR_later) z home base = connection station.
    ///
    /// **Consist compatibility:** pre-F1.12 heurystyka — composition.mode match (MultipleUnit vs
    /// LocoWithCars). Real check (electrification, max length, assigned vehicle types) deferred
    /// do F1.16.
    /// </summary>
    public static class CirculationSuggestionService
    {
        /// <summary>Min gap między arrival earlier a departure later — 5 min (turnaround minimum).</summary>
        public const int MinGapSec = 5 * 60;
        /// <summary>Max gap — 30 min (longer = idle time waste, suggest osobny obieg).</summary>
        public const int MaxGapSec = 30 * 60;

        public static event Action<CirculationSuggestion> OnSuggestionAvailable;

        /// <summary>
        /// Generuje sugestie połączenia dla danego "nowego" timetable z existing collection.
        /// Caller przekazuje newTimetableId (np. tt który właśnie został saved).
        /// </summary>
        public static List<CirculationSuggestion> GenerateSuggestions(int newTimetableId)
        {
            var result = new List<CirculationSuggestion>();
            var newTt = TimetableService.GetTimetable(newTimetableId);
            if (newTt == null || newTt.stops == null || newTt.stops.Count < 2) return result;

            int newStartSec = newTt.frequency.firstRunMinutesFromMidnight * 60;
            int newEndSec = newStartSec + newTt.stops[newTt.stops.Count - 1].plannedArrivalSec;
            int newStartStationId = newTt.stops[0].stationNodeId;
            int newEndStationId = newTt.stops[newTt.stops.Count - 1].stationNodeId;
            string newStartName = newTt.stops[0].stationName;
            string newEndName = newTt.stops[newTt.stops.Count - 1].stationName;

            foreach (var existing in TimetableService.Timetables)
            {
                if (existing == null) continue;
                if (existing.id == newTimetableId) continue;
                if (existing.stops == null || existing.stops.Count < 2) continue;

                // Consist compatibility check (heurystyka pre-F1.16)
                if (!ConsistCompatible(newTt, existing)) continue;

                int existingStartSec = existing.frequency.firstRunMinutesFromMidnight * 60;
                int existingEndSec = existingStartSec + existing.stops[existing.stops.Count - 1].plannedArrivalSec;
                int existingStartStationId = existing.stops[0].stationNodeId;
                int existingEndStationId = existing.stops[existing.stops.Count - 1].stationNodeId;
                string existingStartName = existing.stops[0].stationName;
                string existingEndName = existing.stops[existing.stops.Count - 1].stationName;

                // Case A: new ends w stacji gdzie existing zaczyna (new → existing)
                if (newEndStationId == existingStartStationId)
                {
                    int gap = existingStartSec - newEndSec;
                    if (gap >= MinGapSec && gap <= MaxGapSec)
                        TryAdd(result, newTimetableId, existing.id, newEndStationId, newEndName, gap);
                }

                // Case B: existing ends w stacji gdzie new zaczyna (existing → new)
                if (existingEndStationId == newStartStationId)
                {
                    int gap = newStartSec - existingEndSec;
                    if (gap >= MinGapSec && gap <= MaxGapSec)
                        TryAdd(result, existing.id, newTimetableId, existingEndStationId, existingEndName, gap);
                }
            }

            if (result.Count > 0)
                Log.Info($"[F1.12] Generated {result.Count} circulation suggestion(s) dla TR#{newTimetableId}");
            return result;
        }

        /// <summary>
        /// M11 AS-6: rozdzielenie propose/commit — buduje 2-stepowy obieg z sugestii BEZ
        /// dodawania do CirculationService (kontrakt jak Plan() asystenta: zero side-efektów).
        /// Null gdy rozkłady sugestii już nie istnieją (stale).
        /// </summary>
        public static Circulation BuildProposal(CirculationSuggestion suggestion)
        {
            var ttEarlier = TimetableService.GetTimetable(suggestion.timetableIdEarlier);
            var ttLater = TimetableService.GetTimetable(suggestion.timetableIdLater);
            if (ttEarlier == null || ttLater == null)
            {
                Log.Warn($"[F1.12] Cannot resolve TT — earlier=#{suggestion.timetableIdEarlier}, later=#{suggestion.timetableIdLater}");
                return null;
            }

            // DayMask jest struct (nie nullable) — kopiuj bezpośrednio z ttEarlier
            var circulation = new Circulation
            {
                name = $"Obieg auto: TR{suggestion.timetableIdEarlier}→TR{suggestion.timetableIdLater}",
                calendar = ttEarlier.calendar,
                weeksValid = ttEarlier.weeksValid > 0 ? ttEarlier.weeksValid : 4,
                status = CirculationStatus.Draft, // Draft initially — gracz przypisze vehicle/crew
                notes = $"Auto-generated z F1.12 suggestion (gap {suggestion.gapSec / 60} min w {suggestion.connectionStationName})"
            };
            circulation.steps.Add(new CirculationStep(suggestion.timetableIdEarlier, StepKind.Commercial));
            circulation.steps.Add(new CirculationStep(suggestion.timetableIdLater, StepKind.Commercial));
            return circulation;
        }

        public static void Accept(CirculationSuggestion suggestion)
        {
            SuggestionMemoryService.RecordChoice(SuggestionType.Circulation, suggestion.contextKey, SuggestionChoice.Accept);

            // F1.12 polish (post-F1.16) + M11 AS-6 split: commit = BuildProposal + Add.
            var circulation = BuildProposal(suggestion);
            if (circulation == null) return;

            var added = CirculationService.AddCirculation(circulation);
            Log.Info($"[F1.12 accept] Created circulation #{added.id} ('{added.name}') z 2 steps: TR{suggestion.timetableIdEarlier}→TR{suggestion.timetableIdLater}");
        }

        public static void Dismiss(CirculationSuggestion suggestion) =>
            SuggestionMemoryService.RecordChoice(SuggestionType.Circulation, suggestion.contextKey, SuggestionChoice.Dismiss);

        public static void Snooze(CirculationSuggestion suggestion, long snoozeDurationSec = 3600) =>
            SuggestionMemoryService.RecordChoice(SuggestionType.Circulation, suggestion.contextKey,
                SuggestionChoice.Snooze, snoozeDurationSec);

        // ─── Helpers ───

        private static void TryAdd(List<CirculationSuggestion> result, int idEarlier, int idLater,
            int stationNodeId, string stationName, int gapSec)
        {
            string contextKey = $"circulation:{idEarlier}->{idLater}@{stationNodeId}";
            if (!SuggestionMemoryService.ShouldShow(SuggestionType.Circulation, contextKey)) return;

            var s = new CirculationSuggestion
            {
                timetableIdEarlier = idEarlier,
                timetableIdLater = idLater,
                connectionStationNodeId = stationNodeId,
                connectionStationName = stationName,
                gapSec = gapSec,
                description = $"Wykryto połączenie obiegu: TR#{idEarlier} ⟶ TR#{idLater} w {stationName} " +
                              $"(gap {gapSec / 60} min). Połączyć w obieg?",
                contextKey = contextKey
            };
            result.Add(s);
            OnSuggestionAvailable?.Invoke(s);
        }

        /// <summary>
        /// Heurystyka consist compatibility: oba composition.mode match.
        /// Pre-F1.16 — brak full check (electrification, max length, vehicle types compatibility).
        /// </summary>
        private static bool ConsistCompatible(Timetable a, Timetable b)
        {
            if (a.composition == null || b.composition == null) return true; // brak danych = optymistycznie OK
            return a.composition.mode == b.composition.mode;
        }
    }
}
