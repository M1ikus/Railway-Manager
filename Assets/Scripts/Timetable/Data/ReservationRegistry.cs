using System;
using System.Collections.Generic;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Pojedyncza rezerwacja zasobu (peronu, segmentu toru, itp.) w zadanym oknie czasowym.
    /// Czasy w sekundach od gry (absolutne), nie od startu rozkładu.
    /// </summary>
    [Serializable]
    public struct Reservation
    {
        public long startGameTimeSec;
        public long endGameTimeSec;
        public int trainRunId;
        public int timetableId;

        public bool Overlaps(long otherStart, long otherEnd)
            => otherStart < endGameTimeSec && otherEnd > startGameTimeSec;
    }

    /// <summary>
    /// Generyczny rejestr rezerwacji — wspólna logika dla peronów stacyjnych, segmentów toru
    /// i dowolnych zasobów wymagających ekskluzywnego dostępu w oknie czasowym.
    /// TKey identyfikuje zasób (platformId, segmentId, nodeId, itp.).
    /// </summary>
    public class ReservationRegistry<TKey>
    {
        private readonly Dictionary<TKey, List<Reservation>> _reservations = new();

        /// <summary>Czy zasób jest wolny w całym oknie [startSec, endSec).</summary>
        public bool IsFree(TKey key, long startSec, long endSec)
        {
            if (!_reservations.TryGetValue(key, out var list)) return true;
            foreach (var r in list)
                if (r.Overlaps(startSec, endSec)) return false;
            return true;
        }

        /// <summary>Dodaje rezerwację. Zakłada że IsFree zostało wcześniej sprawdzone.</summary>
        public void Add(TKey key, Reservation reservation)
        {
            if (!_reservations.TryGetValue(key, out var list))
            {
                list = new List<Reservation>();
                _reservations[key] = list;
            }
            list.Add(reservation);
        }

        /// <summary>Usuwa wszystkie rezerwacje dla podanego trainRunId (np. przy anulowaniu kursu).</summary>
        public int RemoveByTrainRun(int trainRunId)
        {
            int removed = 0;
            foreach (var list in _reservations.Values)
                removed += list.RemoveAll(r => r.trainRunId == trainRunId);
            return removed;
        }

        /// <summary>Usuwa wszystkie rezerwacje dla podanego timetableId (np. przy usunięciu rozkładu).</summary>
        public int RemoveByTimetable(int timetableId)
        {
            int removed = 0;
            foreach (var list in _reservations.Values)
                removed += list.RemoveAll(r => r.timetableId == timetableId);
            return removed;
        }

        /// <summary>
        /// Szuka najbliższego wolnego okna [startSec, startSec+durationSec) w promieniu maxOffsetSec
        /// od wnioskowanego startu. Zwraca proponowany startSec albo long.MinValue jeśli nie znalazł.
        /// Używane przy kolizji: "najbliższy wolny slot".
        /// </summary>
        public long FindNearestFreeSlot(TKey key, long desiredStartSec, int durationSec, int maxOffsetSec)
        {
            if (IsFree(key, desiredStartSec, desiredStartSec + durationSec))
                return desiredStartSec;

            // Szukaj w przód i w tył na przemian, co 60 sekund
            const int step = 60;
            for (int offset = step; offset <= maxOffsetSec; offset += step)
            {
                long forward = desiredStartSec + offset;
                if (IsFree(key, forward, forward + durationSec)) return forward;

                long backward = desiredStartSec - offset;
                if (backward >= 0 && IsFree(key, backward, backward + durationSec)) return backward;
            }
            return long.MinValue;
        }

        /// <summary>Zwraca listę wszystkich rezerwacji danego zasobu (do UI debugowego).</summary>
        public IReadOnlyList<Reservation> GetReservations(TKey key)
        {
            if (_reservations.TryGetValue(key, out var list)) return list;
            return Array.Empty<Reservation>();
        }

        public void Clear() => _reservations.Clear();
    }
}
