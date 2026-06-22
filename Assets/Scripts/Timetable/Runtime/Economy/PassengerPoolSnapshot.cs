using System;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// TD-037: kolumnowy snapshot CAŁEJ puli pasażerów (decyzja user 2026-06-10 — pełna serializacja,
    /// de facto M13-8 Phase 2 dla pasażerów). Tablice per pole zamiast listy obiektów — kompaktowy
    /// JSON i szybki parse przy skali 50k agentów (bundle i tak gzip). Enumy jako int.
    /// Wszystkie tablice mają tę samą długość (Count agentów).
    /// </summary>
    [Serializable]
    public class PassengerPoolSnapshot
    {
        public int nextAgentId = 1;
        public float spawnAccumulator;

        public int[] agentId;
        public int[] originStationId;
        public int[] destinationStationId;
        public int[] preference;          // (int)PassengerPreference
        public int[] walletGroszy;
        public int[] state;               // (int)PassengerState
        public int[] currentStationId;
        public int[] currentTrainRunId;
        public float[] spawnTimeSec;
        public float[] abandonTimeSec;
        public int[] paidTotalGroszy;
        public int[] transferCount;
        public int[] purpose;             // (int)TripPurpose
        public int[] desiredClass;        // (int)SeatZoneType
        public int[] currentLegIndex;

        public int Count => agentId?.Length ?? 0;

        /// <summary>Spójność kolumn (wszystkie tablice tej samej długości co agentId).</summary>
        public bool IsConsistent()
        {
            int n = Count;
            return Len(originStationId) == n && Len(destinationStationId) == n && Len(preference) == n
                && Len(walletGroszy) == n && Len(state) == n && Len(currentStationId) == n
                && Len(currentTrainRunId) == n && Len(spawnTimeSec) == n && Len(abandonTimeSec) == n
                && Len(paidTotalGroszy) == n && Len(transferCount) == n && Len(purpose) == n
                && Len(desiredClass) == n && Len(currentLegIndex) == n;

            static int Len(Array a) => a?.Length ?? 0;
        }
    }
}
