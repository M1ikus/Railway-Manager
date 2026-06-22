using System;
using UnityEngine;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Pojedynczy postój rozkładu — przyjazd i odjazd w sekundach liczonych od startu rozkładu.
    /// Osobno plan i faktyczny stan runtime (pod M9: automatyczne prowadzenie ruchu).
    /// </summary>
    [Serializable]
    public class TimetableStop
    {
        public int stationNodeId;              // węzeł z RailwayGraph
        public string stationName;             // cache do UI

        /// <summary>Planowany czas przyjazdu w sekundach od startu rozkładu. 0 dla stacji pierwszej.</summary>
        public int plannedArrivalSec;

        /// <summary>Planowany czas odjazdu w sekundach od startu rozkładu. Równy arrival dla stacji ostatniej.</summary>
        public int plannedDepartureSec;

        /// <summary>Na którym peronie. -1 = nie przypisane (do auto-przypisania przy rezerwacji).</summary>
        public int platformId = -1;

        /// <summary>Numer toru stacyjnego z OSM (railway:track_ref), np. "1", "3".
        /// Null/empty = brak przypisania — A* wybierze najkrótszy tor.</summary>
        public string trackRef;

        /// <summary>
        /// Typ postoju wg PKP/Polregio (M-TimetableUX F1.1). Walidacja per location × hasPlatform:
        /// <see cref="StopTypeValidator.IsAllowed"/>. Default <see cref="StopType.PH"/> (postój handlowy).
        /// </summary>
        public StopType stopType = StopType.PH;

        /// <summary>
        /// M-TimetableUX F1.7: czas postoju w sekundach. Multiple of 6 (PKP convention),
        /// range [6, 1800] (6s minimum, 30 min max dla ZD long crew swap).
        /// Property setter waliduje granularność. Default 60s.
        /// </summary>
        [SerializeField] int _dwellSeconds = 60;
        public int dwellSeconds
        {
            get => _dwellSeconds;
            set
            {
                int rounded = (value / 6) * 6;
                _dwellSeconds = Mathf.Clamp(rounded, 6, 1800);
            }
        }

        /// <summary>Odległość tej stacji od początku trasy w metrach (do kalkulacji fizyki).</summary>
        public float distanceFromStartM;

        /// <summary>Czy to stacja zmiany kierunku — wymaga dłuższego minimalnego postoju.</summary>
        public bool isReversePoint;

        // ── Runtime (M9+) — faktyczne czasy ──
        /// <summary>Rzeczywisty czas przyjazdu w sekundach od startu (może różnić się od plan).</summary>
        public int actualArrivalSec;
        /// <summary>Rzeczywisty czas odjazdu w sekundach od startu.</summary>
        public int actualDepartureSec;
    }
}
