using System;
using System.Collections.Generic;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Meetings
{
    /// <summary>
    /// Klasyfikacja meeting eventu między dwoma rozkładami na tej samej stacji.
    /// </summary>
    public enum MeetingType
    {
        /// <summary>Oba pociągi PH w tej samej window — pasażerowie mogą się przesiąść.</summary>
        PassengerMeeting,
        /// <summary>Opposite-direction trains, single-track segment context — mijanka opportunity (F1.13b).</summary>
        MijankaOpportunity,
        /// <summary>PH/ZD overlap z compatible direction — crew swap candidate (F1.13a).</summary>
        CrewSwapEligible
    }

    /// <summary>
    /// Pojedynczy meeting event między dwoma rozkładami na konkretnej stacji w konkretnym oknie czasowym.
    /// </summary>
    [Serializable]
    public struct MeetingEvent
    {
        public int timetableIdA;
        public int timetableIdB;
        public int stationNodeId;
        public string stationName;
        /// <summary>Start meeting window (sekund od midnight) — max(arrivalA, arrivalB).</summary>
        public int windowStartSec;
        /// <summary>End meeting window — min(departureA, departureB).</summary>
        public int windowEndSec;
        public MeetingType type;
        /// <summary>Skrócony human-readable description dla notifications/UI.</summary>
        public string description;
    }

    /// <summary>
    /// M-TimetableUX F1.14: Spatial-temporal index meeting events między rozkładami.
    ///
    /// Wykrywa overlapy (T_a, T_b) at station S: przyjazd/odjazd windows się nakładają →
    /// generuje MeetingEvent z classification (passenger / mijanka / crew swap).
    ///
    /// **Lazy rebuild:** subscribuje <see cref="TimetableService.OnTimetablesChanged"/> →
    /// invalidate cache. Pierwszy query po invalidate triggeruje rebuild. Caller nie musi
    /// manage'ować lifecycle.
    ///
    /// **Performance:** per game day ~100 trains × 30 stops = 3000 events, O(M²) per station
    /// gdzie M&lt;20 (typowy max trains per station per day) → cheap, target &lt;100ms full rebuild.
    ///
    /// **Foundation dla:**
    /// - F1.12 Circulation suggestion (passenger meeting → "Połączyć z istniejącym obiegiem?")
    /// - F1.13a Crew swap suggestion (CrewSwapEligible → "Drużyna może wsiąść do TR_other")
    /// - F1.13b Mijanka suggestion (MijankaOpportunity → "Synchronize PT mijanki?")
    /// - F1.14 UI Map overlay + Gantt timeline (deferred do F1.16+)
    /// </summary>
    public static class MeetingEventsService
    {
        // Storage: stationNodeId → List<MeetingEvent>
        private static readonly Dictionary<int, List<MeetingEvent>> _byStation = new();
        private static readonly List<MeetingEvent> _allEvents = new();
        private static bool _dirty = true;
        private static bool _subscribed = false;

        public static int EventCount => _allEvents.Count;
        public static bool IsDirty => _dirty;

        /// <summary>
        /// Subscribe na TimetableService.OnTimetablesChanged dla automatic invalidation.
        /// Idempotent — multiple calls safe. Wywołać raz w bootstrap (np. TimetableInitializer Awake).
        /// </summary>
        public static void Bootstrap()
        {
            if (_subscribed) return;
            TimetableService.OnTimetablesChanged += Invalidate;
            _subscribed = true;
            _dirty = true;
            Log.Info("[MeetingEvents] Bootstrapped — subscribed na TimetableService.OnTimetablesChanged");
        }

        /// <summary>Mark cache jako dirty — next query rebuilduje lazy.</summary>
        public static void Invalidate() => _dirty = true;

        /// <summary>
        /// Force immediate rebuild. Normalnie nie potrzebne (lazy w queries),
        /// ale przydatne dla diagnostic/profiling.
        /// </summary>
        public static void Rebuild()
        {
            _byStation.Clear();
            _allEvents.Clear();

            var ttList = TimetableService.Timetables;
            if (ttList == null || ttList.Count < 2)
            {
                _dirty = false;
                return;
            }

            // Per pair of timetables, znajdź overlapy at common stations.
            // O(N² × stops) — dla N=100, M=30 to 100×100×30 = 300k checks → cheap.
            for (int i = 0; i < ttList.Count; i++)
            {
                var tA = ttList[i];
                if (tA?.stops == null) continue;
                int baseSecA = tA.frequency.firstRunMinutesFromMidnight * 60;

                for (int j = i + 1; j < ttList.Count; j++)
                {
                    var tB = ttList[j];
                    if (tB?.stops == null) continue;
                    int baseSecB = tB.frequency.firstRunMinutesFromMidnight * 60;

                    // Find common stations
                    foreach (var sA in tA.stops)
                    {
                        if (sA.stopType == StopType.Transit) continue;
                        foreach (var sB in tB.stops)
                        {
                            if (sB.stopType == StopType.Transit) continue;
                            if (sA.stationNodeId != sB.stationNodeId) continue;

                            // Both stops at same station — check time window overlap
                            int absArrA = baseSecA + sA.plannedArrivalSec;
                            int absDepA = baseSecA + sA.plannedDepartureSec;
                            int absArrB = baseSecB + sB.plannedArrivalSec;
                            int absDepB = baseSecB + sB.plannedDepartureSec;

                            int overlapStart = Math.Max(absArrA, absArrB);
                            int overlapEnd = Math.Min(absDepA, absDepB);
                            if (overlapEnd <= overlapStart) continue; // no overlap

                            var meetingType = ClassifyMeeting(sA.stopType, sB.stopType);
                            var ev = new MeetingEvent
                            {
                                timetableIdA = tA.id,
                                timetableIdB = tB.id,
                                stationNodeId = sA.stationNodeId,
                                stationName = sA.stationName,
                                windowStartSec = overlapStart,
                                windowEndSec = overlapEnd,
                                type = meetingType,
                                description = FormatDescription(tA.id, tB.id, sA.stationName, overlapStart, meetingType)
                            };

                            _allEvents.Add(ev);
                            if (!_byStation.TryGetValue(sA.stationNodeId, out var list))
                            {
                                list = new List<MeetingEvent>();
                                _byStation[sA.stationNodeId] = list;
                            }
                            list.Add(ev);
                        }
                    }
                }
            }

            _dirty = false;
            Log.Info($"[MeetingEvents] Rebuilt: {_allEvents.Count} events across {_byStation.Count} stations");
        }

        /// <summary>
        /// Wszystkie meeting events na danej stacji w time window.
        /// Lazy rebuild jeśli dirty.
        /// </summary>
        public static List<MeetingEvent> GetMeetingsAtStation(int stationNodeId, int timeWindowStartSec, int timeWindowEndSec)
        {
            if (_dirty) Rebuild();
            var result = new List<MeetingEvent>();
            if (!_byStation.TryGetValue(stationNodeId, out var list)) return result;
            foreach (var ev in list)
            {
                // Overlap z requested window
                if (ev.windowEndSec <= timeWindowStartSec) continue;
                if (ev.windowStartSec >= timeWindowEndSec) continue;
                result.Add(ev);
            }
            return result;
        }

        /// <summary>
        /// Wszystkie meeting events involving given timetable (np. dla F1.12 circulation suggestion).
        /// </summary>
        public static List<MeetingEvent> GetMeetingsForTimetable(int timetableId)
        {
            if (_dirty) Rebuild();
            var result = new List<MeetingEvent>();
            foreach (var ev in _allEvents)
                if (ev.timetableIdA == timetableId || ev.timetableIdB == timetableId)
                    result.Add(ev);
            return result;
        }

        /// <summary>
        /// Filtr per type — np. wszystkie mijanki dla F1.13b suggestion service.
        /// </summary>
        public static List<MeetingEvent> GetMeetingsByType(MeetingType type)
        {
            if (_dirty) Rebuild();
            var result = new List<MeetingEvent>();
            foreach (var ev in _allEvents)
                if (ev.type == type)
                    result.Add(ev);
            return result;
        }

        public static IReadOnlyList<MeetingEvent> AllEvents
        {
            get
            {
                if (_dirty) Rebuild();
                return _allEvents;
            }
        }

        // ── Classification ──

        /// <summary>
        /// Classification heurystyka per stop types.
        /// Direction-based mijanka detection wymaga route topology lookup — odłożone do F1.13b
        /// (sprawdzić czy oba pociągi są na single-track segment z opposite directions).
        /// Pre-F1.13b: PassengerMeeting jeśli oba PH; CrewSwapEligible jeśli ≥1 ZD;
        /// MijankaOpportunity gdy oba PT (heuristic — może być mijanka).
        /// </summary>
        private static MeetingType ClassifyMeeting(StopType a, StopType b)
        {
            if (a == StopType.ZD || b == StopType.ZD)
                return MeetingType.CrewSwapEligible;
            if (a == StopType.PT && b == StopType.PT)
                return MeetingType.MijankaOpportunity;
            // Both PH (lub mix PH+PT) → passenger meeting
            return MeetingType.PassengerMeeting;
        }

        private static string FormatDescription(int idA, int idB, string stationName, int windowStartSec, MeetingType type)
        {
            int hours = windowStartSec / 3600;
            int minutes = (windowStartSec % 3600) / 60;
            string timeStr = $"{hours:D2}:{minutes:D2}";
            string typeStr = type switch
            {
                MeetingType.PassengerMeeting => "Spotkanie pasażerskie",
                MeetingType.MijankaOpportunity => "Mijanka",
                MeetingType.CrewSwapEligible => "Możliwa zmiana drużyny",
                _ => "Spotkanie"
            };
            return $"{typeStr}: TR#{idA} ↔ TR#{idB} w {stationName} {timeStr}";
        }
    }
}
