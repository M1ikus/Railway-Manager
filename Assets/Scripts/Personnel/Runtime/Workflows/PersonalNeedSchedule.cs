using UnityEngine;

namespace RailwayManager.Personnel.Workflows
{
    /// <summary>
    /// TD-034 C: czysty, deterministyczny harmonogram czynności osobistych w oknie zmiany.
    /// Bez stanu Unity / bez Random / bez Time.time → wynik zależy tylko od (shiftStart, shiftEnd, seed)
    /// → MP/EditMode-friendly (dyscyplina z TD-031/033). Używany przez <see cref="ScheduledNeedProvider"/>.
    ///
    /// <para>Czasy zwracane jako absolutny game-time (s) — caller przekazuje okno zmiany w tej samej skali.</para>
    /// </summary>
    public static class PersonalNeedSchedule
    {
        const float JitterFrac = 0.16f; // ±8% długości zmiany rozrzutu wokół planowanego punktu

        /// <summary>Liczba wizyt w łazience w tej zmianie (1 lub 2), deterministycznie z seed.</summary>
        public static int BathroomCount(int seed) => Frac01(seed, 11) < 0.5f ? 1 : 2;

        /// <summary>
        /// Czas i-tej (0-based) wizyty w łazience. Równy podział zmiany na (count+1) segmentów,
        /// wizyta na granicy segmentu + jitter z seed. Zwraca w granicach [shiftStart, shiftEnd].
        /// </summary>
        public static long PlannedBathroomTime(long shiftStart, long shiftEnd, int seed, int index, int count)
        {
            long span = shiftEnd - shiftStart;
            if (span <= 0) return shiftStart;
            float baseFrac = (index + 1) / (float)(count + 1);
            float jitter = (Frac01(seed, 100 + index) - 0.5f) * JitterFrac;
            float t = Mathf.Clamp01(baseFrac + jitter);
            return shiftStart + (long)(span * t);
        }

        /// <summary>Czas przerwy/posiłku — ~środek zmiany + jitter z seed. W granicach [shiftStart, shiftEnd].</summary>
        public static long PlannedBreakTime(long shiftStart, long shiftEnd, int seed)
        {
            long span = shiftEnd - shiftStart;
            if (span <= 0) return shiftStart;
            float jitter = (Frac01(seed, 200) - 0.5f) * JitterFrac;
            float t = Mathf.Clamp01(0.5f + jitter);
            return shiftStart + (long)(span * t);
        }

        /// <summary>Deterministyczny pseudo-los [0,1) z (seed, salt) — hash, bez Random.</summary>
        static float Frac01(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)(seed * 73856093) ^ (uint)(salt * 19349663);
                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / (float)0x1000000;
            }
        }
    }
}
