using System;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Meetings;
using RailwayManager.SharedUI.Suggestions;

namespace RailwayManager.Personnel.Suggestions
{
    /// <summary>
    /// Sugestia zmiany drużyny — drużyna TR_new wsiada do TR_other w stacji X żeby
    /// wrócić w kierunku home depot bez hotelu.
    /// </summary>
    [Serializable]
    public struct CrewSwapSuggestion
    {
        public int timetableIdNew;
        public int timetableIdOther;
        public int stationNodeId;
        public string stationName;
        /// <summary>Time of swap (sekund od midnight) — TR_other departure - 3 min buffer.</summary>
        public int swapTimeOfDaySec;
        /// <summary>Estimated hotel cost savings (zł) — rough heuristic pre-F1.16.</summary>
        public int estimatedSavingsZl;
        public string description;
        public string contextKey;
    }

    /// <summary>
    /// M-TimetableUX F1.13a: Proactive crew swap suggestion (at-stop-add / at-save trigger).
    ///
    /// **Trigger:** caller (np. UI po save) wywołuje GenerateSuggestions(newTimetableId).
    /// Pre-F1.16: explicit call. Post-F1.16: subscribe na <see cref="TimetableService.OnTimetablesChanged"/>.
    ///
    /// **Algorithm:**
    /// 1. Query <see cref="MeetingEventsService"/>.GetMeetingsForTimetable(newTtId)
    /// 2. Filter <see cref="MeetingType.CrewSwapEligible"/> (≥1 ZD stop w meeting)
    /// 3. License compat: <see cref="EmployeeQualifications.HasTractionPermit"/> (EA-mode permissive)
    /// 4. Direction toward home depot: CheckDirectionTowardHomeDepot (euclidean z PathfindingGraph)
    /// 5. Hotel savings: real lookup z <see cref="PersonnelBalanceConstants.HotelCostStandardPerNight"/>
    ///    — gap >= OvernightThresholdHours (8h) avoids full hotel night (150 zł), shorter gap scaled
    /// 6. Filter via <see cref="SuggestionMemoryService"/> (skip dismissed)
    ///
    /// **Side effects on Accept** deferred do F1.16:
    /// - Mark both timetables' meeting stop jako ZD (jeśli był PH, upgrade)
    /// - Create CrewSwapEvent w <c>CrewCirculationService</c> linking dwóch turnusów
    /// - Update <c>CrewCirculation.assignedEmployeeId</c>
    /// - Player notification "Crew swap created"
    ///
    /// Pre-F1.16 Accept: tylko mark accepted w SuggestionMemoryService (record dismiss tracking).
    /// </summary>
    public static class CrewSwapSuggestionService
    {
        /// <summary>Min savings żeby suggestion miała sens (zł). Poniżej → skip suggestion.</summary>
        public const int MinSavingsThresholdZl = 50;

        /// <summary>Gap window threshold powyżej którego zakładamy że crew swap eliminuje overnight hotel (godzin).</summary>
        public const int OvernightThresholdHours = 8;

        private static bool _subscribed = false;

        /// <summary>
        /// M-TimetableUX F1.15 cross-asmdef bridge — subscribe na
        /// <see cref="RailwayManager.Timetable.Workflows.TimetableWorkflowOrchestrator.OnNewTimetableSuggestionsRequested"/>.
        /// Idempotent. Wywołać w Personnel bootstrap.
        /// </summary>
        public static void Bootstrap()
        {
            if (_subscribed) return;
            RailwayManager.Timetable.Workflows.TimetableWorkflowOrchestrator.OnNewTimetableSuggestionsRequested += GenerateSuggestionsForOrchestrator;
            _subscribed = true;
            Log.Info("[F1.15] CrewSwapSuggestionService bootstrapped — subscribed na TimetableWorkflowOrchestrator");
        }

        private static void GenerateSuggestionsForOrchestrator(int newTimetableId)
        {
            var suggestions = GenerateSuggestions(newTimetableId);
            // Suggestions auto-emitted via OnSuggestionAvailable event. Logging w GenerateSuggestions już zrobiony.
        }


        public static event Action<CrewSwapSuggestion> OnSuggestionAvailable;

        /// <summary>
        /// Generuje crew swap suggestions dla danego "nowego" timetable.
        /// Iteruje meeting events typu CrewSwapEligible involving newTimetableId.
        /// </summary>
        public static List<CrewSwapSuggestion> GenerateSuggestions(int newTimetableId)
            => GenerateSuggestions(newTimetableId, skipMemoryFilter: false);

        /// <summary>
        /// M11 AS-6: <paramref name="skipMemoryFilter"/> = true pomija filtr
        /// SuggestionMemoryService (czysty Propose niezależny od historii dismiss/snooze —
        /// dla konsumenta, który filtruje sam, np. panel asystenta).
        /// </summary>
        public static List<CrewSwapSuggestion> GenerateSuggestions(int newTimetableId, bool skipMemoryFilter)
        {
            var result = new List<CrewSwapSuggestion>();
            var meetings = MeetingEventsService.GetMeetingsForTimetable(newTimetableId);

            foreach (var ev in meetings)
            {
                if (ev.type != MeetingType.CrewSwapEligible) continue;

                int otherTtId = ev.timetableIdA == newTimetableId ? ev.timetableIdB : ev.timetableIdA;

                // Pre-F1.16 heurystyka: license compat + direction always true
                if (!CheckLicenseCompatibility(newTimetableId, otherTtId)) continue;
                if (!CheckDirectionTowardHomeDepot(newTimetableId, otherTtId, ev.stationNodeId)) continue;

                // F1.13a polish: real hotel cost savings z PersonnelBalanceConstants.
                // - Gap window >= OvernightThresholdHours (8h): zakładamy że crew swap eliminuje
                //   pełen overnight hotel night → savings = HotelCostStandardPerNight (150 zł)
                // - Gap window < 8h: smaller savings (waiting time mostly day-time, no hotel)
                //   → savings = HotelCostStandardPerNight scaled by (gap_hours / 8)
                int gapHours = Math.Max(1, (ev.windowEndSec - ev.windowStartSec) / 3600);
                int hotelStandardZl = PersonnelBalanceConstants.HotelCostStandardPerNight / 100; // grosze→zł
                int estimatedSavings = gapHours >= OvernightThresholdHours
                    ? hotelStandardZl
                    : (hotelStandardZl * gapHours) / OvernightThresholdHours;
                if (estimatedSavings < MinSavingsThresholdZl) continue;

                int swapTime = ev.windowStartSec + 60; // arrival + 1 min buffer (pre-F1.16 simplified)
                string contextKey = MakeContextKey(newTimetableId, otherTtId, ev.stationNodeId, ev.windowStartSec);
                if (!skipMemoryFilter
                    && !SuggestionMemoryService.ShouldShow(SuggestionType.CrewSwap, contextKey)) continue;

                var suggestion = new CrewSwapSuggestion
                {
                    timetableIdNew = newTimetableId,
                    timetableIdOther = otherTtId,
                    stationNodeId = ev.stationNodeId,
                    stationName = ev.stationName,
                    swapTimeOfDaySec = swapTime,
                    estimatedSavingsZl = estimatedSavings,
                    description = $"Drużyna z TR#{newTimetableId} może wsiąść do TR#{otherTtId} w {ev.stationName} " +
                                  $"{FormatTime(swapTime)} — oszczędność ~{estimatedSavings} zł hotelu. Utworzyć zmianę?",
                    contextKey = contextKey
                };
                result.Add(suggestion);
                OnSuggestionAvailable?.Invoke(suggestion);
            }

            if (result.Count > 0)
                Log.Info($"[F1.13a] Generated {result.Count} crew swap suggestion(s) dla TR#{newTimetableId}");
            return result;
        }

        public static void Accept(CrewSwapSuggestion suggestion)
        {
            SuggestionMemoryService.RecordChoice(SuggestionType.CrewSwap, suggestion.contextKey, SuggestionChoice.Accept);

            // F1.13a polish: side effects on Accept
            // 1. Upgrade meeting stop StopType do ZD w obu TTs (mark crew swap point physically)
            int upgradedNew = UpgradeMeetingStopToZD(suggestion.timetableIdNew, suggestion.stationNodeId);
            int upgradedOther = UpgradeMeetingStopToZD(suggestion.timetableIdOther, suggestion.stationNodeId);

            // 2. Emit notification dla player'a
            string timeStr = $"{suggestion.swapTimeOfDaySec / 3600:D2}:{(suggestion.swapTimeOfDaySec % 3600) / 60:D2}";
            RailwayManager.Timetable.Notifications.TimetableNotificationService.Add(
                RailwayManager.Timetable.Notifications.NotificationSeverity.Info,
                RailwayManager.Timetable.Notifications.NotificationType.SuggestionAvailable,
                $"Crew swap utworzony: TR#{suggestion.timetableIdNew}↔TR#{suggestion.timetableIdOther} w {suggestion.stationName} {timeStr}. " +
                $"Oszczędność: ~{suggestion.estimatedSavingsZl} zł hotelu.",
                stopIndex: -1,
                timeOfDaySec: suggestion.swapTimeOfDaySec,
                sourceTimetableId: suggestion.timetableIdNew);

            Log.Info($"[F1.13a accept] Crew swap accepted: TR#{suggestion.timetableIdNew}↔TR#{suggestion.timetableIdOther} w {suggestion.stationName}; " +
                     $"upgraded {upgradedNew + upgradedOther} stops do ZD; savings ~{suggestion.estimatedSavingsZl} zł");

            // 3. CrewCirculation.assignedEmployeeId update — wymaga dedicated CrewSwapEvent data type
            // (post-EA). Pre-EA: assignment driver swap manual via CrewCirculationEditorUI.
            // M11 AS-6 (2026-06-12): deferral PODTRZYMANY świadomie — runtime wymiana maszynistów
            // mid-run potrzebuje modelu CrewSwapEvent (kto przejmuje którą służbę od którego
            // przystanku); półimplementacja bez tego modelu = zepsute grafiki. Decyzja spójna
            // z oryginalnym spec'em M-TimetableUX (post-EA). Asystent forwarduje sugestię
            // (bridge AS-6), akcept = istniejący flow (ZD stop + notyfikacja).
        }

        /// <summary>
        /// Upgrade stop o danym stationNodeId do StopType.ZD (jeśli był PH/Transit) — physical
        /// mark crew swap point w timetable stops.
        /// </summary>
        private static int UpgradeMeetingStopToZD(int timetableId, int stationNodeId)
        {
            var tt = TimetableService.GetTimetable(timetableId);
            if (tt?.stops == null) return 0;
            int upgraded = 0;
            foreach (var stop in tt.stops)
            {
                if (stop.stationNodeId != stationNodeId) continue;
                if (stop.stopType == StopType.ZD) continue; // already ZD, skip
                // Upgrade: PH/PT/Transit → ZD (crew swap requires stop)
                stop.stopType = StopType.ZD;
                upgraded++;
            }
            return upgraded;
        }

        public static void Dismiss(CrewSwapSuggestion suggestion) =>
            SuggestionMemoryService.RecordChoice(SuggestionType.CrewSwap, suggestion.contextKey, SuggestionChoice.Dismiss);

        public static void Snooze(CrewSwapSuggestion suggestion, long snoozeDurationSec = 3600) =>
            SuggestionMemoryService.RecordChoice(SuggestionType.CrewSwap, suggestion.contextKey,
                SuggestionChoice.Snooze, snoozeDurationSec);

        // ─── Heurystyki pre-F1.16 ───

        /// <summary>
        /// Stub: license compatibility check. Pre-F1.16 zawsze true.
        /// F1.16: lookup `EmployeeQualifications` dla driver assigned to TR_new vs trakcja TR_other.
        /// </summary>
        private static bool CheckLicenseCompatibility(int ttIdA, int ttIdB) => true;

        /// <summary>
        /// F1.13a polish: real direction toward home depot check.
        /// Lookup driver assigned to TR_new (via CrewCirculationService.FindByTrainRun lub
        /// CrewAssignmentService.GetDriverForTrainRun). Jeśli driver.homeStationNodeId set,
        /// porównuje euclidean distance: czy TR_other.endStation jest bliżej home niż TR_new.endStation.
        ///
        /// Returns true gdy:
        /// - Brak driver assigned do TR_new (legacy permissive)
        /// - driver.homeStationNodeId == -1 (no home assigned, legacy permissive)
        /// - TR_other.endStation jest bliżej (lub równo blisko) home niż TR_new.endStation
        /// </summary>
        private static bool CheckDirectionTowardHomeDepot(int ttNewId, int ttOtherId, int meetingStationNodeId)
        {
            var graph = TimetableInitializer.Instance?.Graph;
            if (graph == null) return true; // graph not loaded, permissive

            // Find driver assigned to TR_new dla today's runDate.
            // Note: TR_new może nie być yet w runtime list (newly saved). Try CrewCirculationService lookup.
            var ttNew = TimetableService.GetTimetable(ttNewId);
            var ttOther = TimetableService.GetTimetable(ttOtherId);
            if (ttNew?.stops == null || ttOther?.stops == null) return true;

            // Find driver via CrewCirculation referencedTrainRun (M8-8 D5)
            Employee driver = FindDriverByTimetable(ttNewId);
            if (driver == null || driver.homeStationNodeId < 0) return true; // legacy permissive

            // Compute euclidean distances z PathfindingGraph
            if (driver.homeStationNodeId >= graph.NodeCount) return true;
            var homePos = graph.GetNode(driver.homeStationNodeId).position;

            var ttNewEndStop = ttNew.stops[ttNew.stops.Count - 1];
            var ttOtherEndStop = ttOther.stops[ttOther.stops.Count - 1];
            if (ttNewEndStop.stationNodeId < 0 || ttOtherEndStop.stationNodeId < 0) return true;
            if (ttNewEndStop.stationNodeId >= graph.NodeCount || ttOtherEndStop.stationNodeId >= graph.NodeCount) return true;

            var ttNewEndPos = graph.GetNode(ttNewEndStop.stationNodeId).position;
            var ttOtherEndPos = graph.GetNode(ttOtherEndStop.stationNodeId).position;

            float distNew = UnityEngine.Vector2.Distance(ttNewEndPos, homePos);
            float distOther = UnityEngine.Vector2.Distance(ttOtherEndPos, homePos);

            // Direction is "toward home" jeśli TR_other end bliżej home (lub equal w tolerance 1km)
            return distOther <= distNew + 1000f;
        }

        /// <summary>
        /// Helper dla F1.13a — find first driver assigned to TR via crew circulations.
        /// Brute force scan All circulations (pre-EA scale OK; post-EA index lookup).
        /// </summary>
        private static Employee FindDriverByTimetable(int timetableId)
        {
            foreach (var circ in CrewCirculationService.All)
            {
                if (circ == null || circ.role != EmployeeRole.Driver) continue;
                if (circ.assignedEmployeeId <= 0) continue;
                // Check czy circulation duties reference any TR z this timetable.
                // Bridge: TrainRunGenerator generates TrainRuns per timetable, CrewCirculationService.duties
                // referencjuje referencedTrainRunId — sprawdzić czy któraś referuje timetable
                bool matches = false;
                if (circ.duties != null)
                {
                    foreach (var duty in circ.duties)
                    {
                        if (duty.referencedTrainRunId <= 0) continue;
                        TrainRun tr = null;
                        foreach (var t in TimetableService.TrainRuns)
                            if (t.id == duty.referencedTrainRunId) { tr = t; break; }
                        if (tr != null && tr.timetableId == timetableId) { matches = true; break; }
                    }
                }
                if (!matches) continue;
                return PersonnelService.GetById(circ.assignedEmployeeId);
            }
            return null;
        }

        // ─── Helpers ───

        private static string MakeContextKey(int idNew, int idOther, int stationNodeId, int windowStartSec)
        {
            int low = Math.Min(idNew, idOther);
            int high = Math.Max(idNew, idOther);
            return $"crewswap:{low}+{high}@{stationNodeId}@{windowStartSec}";
        }

        private static string FormatTime(int totalSec)
        {
            int h = totalSec / 3600;
            int m = (totalSec % 3600) / 60;
            return $"{h:D2}:{m:D2}";
        }
    }
}
