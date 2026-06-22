using System;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Timetable.Meetings;
using RailwayManager.SharedUI.Suggestions;

namespace RailwayManager.Timetable.Suggestions
{
    /// <summary>
    /// Sugestia synchronizacji mijanki — zsynchronizować PT stop timing dwóch timetables
    /// żeby oba przybyły simultaneously na stacji mijankowej.
    /// </summary>
    [Serializable]
    public struct MijankaSuggestion
    {
        public int timetableIdA;
        public int timetableIdB;
        public int stationNodeId;
        public string stationName;
        /// <summary>Original meeting window start (sekund od midnight) z MeetingEvent.</summary>
        public int originalWindowStartSec;
        /// <summary>Sugerowany dwell adjustment (sekundy) — tt_A czeka N s na tt_B.</summary>
        public int suggestedDwellAdjustmentSec;
        public string description;
        /// <summary>Stable contextKey dla SuggestionMemoryService (dismiss tracking).</summary>
        public string contextKey;
    }

    /// <summary>
    /// M-TimetableUX F1.13b: Proactive mijanka synchronization suggestion.
    ///
    /// **Trigger:** wywołanie GenerateSuggestions() po OnTimetableSaved (F1.12 hook) lub
    /// po OnPathComputed (single-track segment detection — TBD F1.13b polish).
    ///
    /// **Algorithm:**
    /// 1. Query <see cref="MeetingEventsService"/>.GetMeetingsByType(MijankaOpportunity)
    /// 2. Per meeting, compute optymalne sync time (oba arrive simultaneously)
    /// 3. Suggest dwell adjustment dla TR_A żeby spotkała się z TR_B w środku ich windows
    /// 4. Filter przez SuggestionMemoryService (skip dismissed)
    ///
    /// **Implementation status:** service stand-alone, suggestion generation done.
    /// UI modal "Synchronizować?" + Accept side effects (adjust dwell, update reservations,
    /// mark meeting as synchronized) — deferred do F1.16 progressive disclosure (UI integration).
    ///
    /// **Direction-based mijanka detection** (proper single-track topology check) —
    /// TBD post-F1.13b (wymaga BlockSection + Edge metadata analysis żeby wykryć czy
    /// rzeczywiście single-track segment z opposite directions). Pre-F1.13b: heurystyka
    /// MeetingEventsService classification (oba PT → MijankaOpportunity).
    /// </summary>
    public static class MijankaSuggestionService
    {
        public static event Action<MijankaSuggestion> OnSuggestionAvailable;

        /// <summary>
        /// Generuje sugestie mijanki z aktualnego stanu MeetingEventsService.
        /// Filtruje sugestie już dismissed via SuggestionMemoryService.
        /// </summary>
        public static List<MijankaSuggestion> GenerateSuggestions(int? sourceTimetableId = null)
        {
            var result = new List<MijankaSuggestion>();
            var mijankaEvents = MeetingEventsService.GetMeetingsByType(MeetingType.MijankaOpportunity);

            foreach (var ev in mijankaEvents)
            {
                // Filter by source timetable jeśli podane
                if (sourceTimetableId.HasValue
                    && ev.timetableIdA != sourceTimetableId.Value
                    && ev.timetableIdB != sourceTimetableId.Value)
                    continue;

                // Compute suggested adjustment — środek window'a jako rendez-vous time
                int midpoint = (ev.windowStartSec + ev.windowEndSec) / 2;
                int adjustmentSec = midpoint - ev.windowStartSec; // tt_A waits adjustmentSec for tt_B

                var contextKey = MakeContextKey(ev.timetableIdA, ev.timetableIdB, ev.stationNodeId, ev.windowStartSec);
                if (!SuggestionMemoryService.ShouldShow(SuggestionType.Mijanka, contextKey))
                    continue;

                var suggestion = new MijankaSuggestion
                {
                    timetableIdA = ev.timetableIdA,
                    timetableIdB = ev.timetableIdB,
                    stationNodeId = ev.stationNodeId,
                    stationName = ev.stationName,
                    originalWindowStartSec = ev.windowStartSec,
                    suggestedDwellAdjustmentSec = adjustmentSec,
                    description = $"Mijanka możliwa w {ev.stationName} {FormatTime(midpoint)} — synchronizować PT stops " +
                                  $"(TR#{ev.timetableIdA} ↔ TR#{ev.timetableIdB}, dwell adjust ±{adjustmentSec}s)",
                    contextKey = contextKey
                };
                result.Add(suggestion);
                OnSuggestionAvailable?.Invoke(suggestion);
            }

            if (result.Count > 0)
                Log.Info($"[F1.13b] Generated {result.Count} mijanka suggestion(s)");
            return result;
        }

        /// <summary>
        /// Akceptacja sugestii przez gracza. Side effects (dwell adjust, reservation update,
        /// meeting marker "synchronized") deferred do F1.16 — pre-F1.16 tylko mark accepted
        /// w SuggestionMemoryService.
        /// </summary>
        public static void Accept(MijankaSuggestion suggestion)
        {
            SuggestionMemoryService.RecordChoice(SuggestionType.Mijanka, suggestion.contextKey, SuggestionChoice.Accept);

            // F1.13b polish: side effects on Accept
            // 1. Adjust dwell w pierwszym TT (timetableIdA) żeby arrival matched midpoint window
            int adjustedStops = AdjustDwellForMijanka(suggestion.timetableIdA, suggestion.stationNodeId, suggestion.suggestedDwellAdjustmentSec);

            // 2. Emit notification "Mijanka zsynchronizowana"
            string timeStr = $"{(suggestion.originalWindowStartSec + suggestion.suggestedDwellAdjustmentSec) / 3600:D2}:" +
                             $"{((suggestion.originalWindowStartSec + suggestion.suggestedDwellAdjustmentSec) % 3600) / 60:D2}";
            RailwayManager.Timetable.Notifications.TimetableNotificationService.Add(
                RailwayManager.Timetable.Notifications.NotificationSeverity.Info,
                RailwayManager.Timetable.Notifications.NotificationType.MijankaDetected,
                $"Mijanka zsynchronizowana: TR#{suggestion.timetableIdA}↔TR#{suggestion.timetableIdB} w {suggestion.stationName} ~{timeStr} " +
                $"(dwell +{suggestion.suggestedDwellAdjustmentSec}s w TR#{suggestion.timetableIdA})",
                stopIndex: -1,
                timeOfDaySec: suggestion.originalWindowStartSec + suggestion.suggestedDwellAdjustmentSec,
                sourceTimetableId: suggestion.timetableIdA);

            // 3. Invalidate MeetingEventsService cache — refresh meeting events z nowym timing
            MeetingEventsService.Invalidate();

            Log.Info($"[F1.13b accept] Mijanka synced: TR#{suggestion.timetableIdA}↔TR#{suggestion.timetableIdB} w {suggestion.stationName}; " +
                     $"adjusted {adjustedStops} stops; dwell +{suggestion.suggestedDwellAdjustmentSec}s");

            // 4. Platform reservation update — wymaga ReservationManager re-reserve flow.
            // Deferred: caller (gdy Active state timetable) musi wywołać AutoAssignPlatforms +
            // ReserveForTimetable. Pre-Active state: reservations naturalnie computed na Activate.
        }

        /// <summary>
        /// Adjust dwell stop'u o stationNodeId w danym TT — extends plannedDepartureSec + cascade
        /// downstream stops o adjustmentSec.
        /// </summary>
        private static int AdjustDwellForMijanka(int timetableId, int stationNodeId, int adjustmentSec)
        {
            var tt = TimetableService.GetTimetable(timetableId);
            if (tt?.stops == null) return 0;

            int adjusted = 0;
            bool foundTarget = false;
            for (int i = 0; i < tt.stops.Count; i++)
            {
                var stop = tt.stops[i];
                if (!foundTarget)
                {
                    if (stop.stationNodeId != stationNodeId) continue;
                    // Target stop — extend dwell (push departure)
                    stop.plannedDepartureSec += adjustmentSec;
                    foundTarget = true;
                    adjusted++;
                    continue;
                }
                // Cascade downstream — shift arrival + departure
                stop.plannedArrivalSec += adjustmentSec;
                stop.plannedDepartureSec += adjustmentSec;
                adjusted++;
            }
            return adjusted;
        }

        /// <summary>Player dismiss — no re-prompt dla tego context'u.</summary>
        public static void Dismiss(MijankaSuggestion suggestion)
        {
            SuggestionMemoryService.RecordChoice(SuggestionType.Mijanka, suggestion.contextKey, SuggestionChoice.Dismiss);
        }

        /// <summary>Player snooze — re-prompt po N sec game time.</summary>
        public static void Snooze(MijankaSuggestion suggestion, long snoozeDurationSec = 3600)
        {
            SuggestionMemoryService.RecordChoice(SuggestionType.Mijanka, suggestion.contextKey,
                SuggestionChoice.Snooze, snoozeDurationSec);
        }

        /// <summary>Stable key dla SuggestionMemoryService — dismiss tracking.</summary>
        private static string MakeContextKey(int idA, int idB, int stationNodeId, int windowStartSec)
        {
            // Sort IDs dla symetrii (mijanka A↔B == B↔A)
            int low = Math.Min(idA, idB);
            int high = Math.Max(idA, idB);
            return $"mijanka:{low}+{high}@{stationNodeId}@{windowStartSec}";
        }

        private static string FormatTime(int totalSec)
        {
            int h = totalSec / 3600;
            int m = (totalSec % 3600) / 60;
            return $"{h:D2}:{m:D2}";
        }
    }
}
