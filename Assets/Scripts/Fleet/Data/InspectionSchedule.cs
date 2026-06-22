using System;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// Harmonogram przegladow pojazdu. Kazdy poziom ma wlasne kryteria:
    /// P1 — nie rzadziej niz 72h (czas gry)
    /// P2 — nie rzadziej niz 28 dni (czas gry)
    /// P3 — nie rzadziej niz 250 000 km
    /// P4 — nie rzadziej niz 500 000 km LUB 5 lat (co wystapi szybciej)
    /// P5 — nie rzadziej niz 3 000 000 km LUB 30 lat (co wystapi szybciej)
    /// Wykonanie wyzszego poziomu resetuje rowniez wszystkie nizsze.
    /// </summary>
    [Serializable]
    public class InspectionSchedule
    {
        // ── Ostatnio wykonane (absolutne momenty/przebiegi) ──
        public long  lastP1GameTime;    // sekundy gry
        public long  lastP2GameTime;    // sekundy gry
        public float lastP3Mileage;     // km (stan licznika przy wykonaniu)
        public long  lastP4GameTime;    // sekundy gry
        public float lastP4Mileage;     // km
        public long  lastP5GameTime;    // sekundy gry
        public float lastP5Mileage;     // km

        // D4: usunięte dead-code const'y P*_LIMIT_* (były duplikatem
        // FleetBalanceConstants.DefaultInspection*, nigdzie nie używane poza
        // wewnętrznymi referencjami). Single source of truth: FleetBalanceConstants
        // (defaulty) + InspectionCatalog (per-seria override).

        // ── Sekundy-na-jednostke (czas gry) ──
        public const long SEC_HOUR = 3600L;
        public const long SEC_DAY  = 86_400L;
        public const long SEC_YEAR = 31_557_600L;   // 365.25 dni

        /// <summary>Status jednego poziomu przegladu.</summary>
        public struct LevelStatus
        {
            public InspectionLevel level;
            public float progress;        // 0..1 (>=1 = przekroczony)
            public float remainingKm;     // float.NaN jezeli brak limitu km
            public long  remainingSec;    // long.MinValue jezeli brak limitu czasowego
            public bool  hasKmLimit;
            public bool  hasTimeLimit;
            public bool IsOverdue => progress >= 1f;
        }

        /// <summary>
        /// M7-1: Status przegladu dla konkretnego poziomu, używając per-seria intervals.
        /// Preferowany overload — sięga do <see cref="InspectionCatalog"/> via seriesId.
        /// </summary>
        public LevelStatus GetStatus(InspectionLevel level, InspectionIntervals intervals, long nowGameTime, float currentMileage)
        {
            if (intervals == null) intervals = InspectionIntervals.CreateDefault();
            var s = new LevelStatus
            {
                level = level,
                remainingKm = float.NaN,
                remainingSec = long.MinValue
            };
            switch (level)
            {
                case InspectionLevel.P1:
                {
                    s.hasTimeLimit = true;
                    long elapsed = nowGameTime - lastP1GameTime;
                    float hours = elapsed / (float)SEC_HOUR;
                    s.progress = Math.Max(0f, hours / intervals.p1LimitHours);
                    s.remainingSec = (long)intervals.p1LimitHours * SEC_HOUR - elapsed;
                    break;
                }
                case InspectionLevel.P2:
                {
                    s.hasTimeLimit = true;
                    long elapsed = nowGameTime - lastP2GameTime;
                    float days = elapsed / (float)SEC_DAY;
                    s.progress = Math.Max(0f, days / intervals.p2LimitDays);
                    s.remainingSec = (long)intervals.p2LimitDays * SEC_DAY - elapsed;
                    break;
                }
                case InspectionLevel.P3:
                {
                    s.hasKmLimit = true;
                    float km = currentMileage - lastP3Mileage;
                    s.progress = Math.Max(0f, km / intervals.p3LimitKm);
                    s.remainingKm = intervals.p3LimitKm - km;
                    break;
                }
                case InspectionLevel.P4:
                {
                    s.hasKmLimit = true;
                    s.hasTimeLimit = true;
                    float km = Math.Max(0f, currentMileage - lastP4Mileage);
                    long elapsed = nowGameTime - lastP4GameTime;
                    float years = elapsed / (float)SEC_YEAR;
                    float pKm = km / intervals.p4LimitKm;
                    float pT  = Math.Max(0f, years / intervals.p4LimitYears);
                    s.progress = Math.Max(pKm, pT);
                    s.remainingKm = intervals.p4LimitKm - km;
                    s.remainingSec = (long)intervals.p4LimitYears * SEC_YEAR - elapsed;
                    break;
                }
                case InspectionLevel.P5:
                {
                    s.hasKmLimit = true;
                    s.hasTimeLimit = true;
                    float km = Math.Max(0f, currentMileage - lastP5Mileage);
                    long elapsed = nowGameTime - lastP5GameTime;
                    float years = elapsed / (float)SEC_YEAR;
                    float pKm = km / intervals.p5LimitKm;
                    float pT  = Math.Max(0f, years / intervals.p5LimitYears);
                    s.progress = Math.Max(pKm, pT);
                    s.remainingKm = intervals.p5LimitKm - km;
                    s.remainingSec = (long)intervals.p5LimitYears * SEC_YEAR - elapsed;
                    break;
                }
            }
            return s;
        }

        /// <summary>
        /// LEGACY overload — używa <see cref="InspectionIntervals.CreateDefault"/> (czyli
        /// <see cref="FleetBalanceConstants"/>.Default*). Dla kompat — preferuj overload
        /// z explicit intervals.
        /// </summary>
        public LevelStatus GetStatus(InspectionLevel level, long nowGameTime, float currentMileage)
            => GetStatus(level, InspectionIntervals.CreateDefault(), nowGameTime, currentMileage);

        /// <summary>Zwraca poziom o najwyzszym progressie (najbardziej pilny).</summary>
        public LevelStatus GetMostUrgent(InspectionIntervals intervals, long nowGameTime, float currentMileage)
        {
            var best = GetStatus(InspectionLevel.P1, intervals, nowGameTime, currentMileage);
            for (int i = 1; i <= 4; i++)
            {
                var s = GetStatus((InspectionLevel)i, intervals, nowGameTime, currentMileage);
                if (s.progress > best.progress) best = s;
            }
            return best;
        }

        /// <summary>LEGACY overload — domyślne intervals.</summary>
        public LevelStatus GetMostUrgent(long nowGameTime, float currentMileage)
            => GetMostUrgent(InspectionIntervals.CreateDefault(), nowGameTime, currentMileage);

        /// <summary>Wykonanie przegladu — resetuje ten i wszystkie nizsze poziomy.</summary>
        public void Perform(InspectionLevel level, long nowGameTime, float currentMileage)
        {
            // lower level always reset
            lastP1GameTime = nowGameTime;
            if (level >= InspectionLevel.P2) lastP2GameTime = nowGameTime;
            if (level >= InspectionLevel.P3) lastP3Mileage  = currentMileage;
            if (level >= InspectionLevel.P4)
            {
                lastP4GameTime = nowGameTime;
                lastP4Mileage  = currentMileage;
            }
            if (level >= InspectionLevel.P5)
            {
                lastP5GameTime = nowGameTime;
                lastP5Mileage  = currentMileage;
            }
        }

        /// <summary>Swiezy harmonogram — wszystkie liczniki wyzerowane od teraz / bieżącego przebiegu.</summary>
        public static InspectionSchedule CreateFresh(long nowGameTime, float currentMileage)
        {
            var s = new InspectionSchedule();
            s.lastP1GameTime = nowGameTime;
            s.lastP2GameTime = nowGameTime;
            s.lastP3Mileage  = currentMileage;
            s.lastP4GameTime = nowGameTime;
            s.lastP4Mileage  = currentMileage;
            s.lastP5GameTime = nowGameTime;
            s.lastP5Mileage  = currentMileage;
            return s;
        }

        /// <summary>
        /// Rekonstruuje harmonogram dla pojazdu uzywanego na podstawie "zuzycia" per poziom.
        /// Argumenty to jak duzo km / ile czasu uplynelo od ostatniego przegladu danego poziomu.
        /// </summary>
        public static InspectionSchedule Reconstruct(
            long nowGameTime, float currentMileage,
            float hoursSinceP1, float daysSinceP2,
            float kmSinceP3,
            float kmSinceP4, float yearsSinceP4,
            float kmSinceP5, float yearsSinceP5)
        {
            return new InspectionSchedule
            {
                lastP1GameTime = nowGameTime - (long)(hoursSinceP1 * SEC_HOUR),
                lastP2GameTime = nowGameTime - (long)(daysSinceP2  * SEC_DAY),
                lastP3Mileage  = currentMileage - kmSinceP3,
                lastP4GameTime = nowGameTime - (long)(yearsSinceP4 * SEC_YEAR),
                lastP4Mileage  = currentMileage - kmSinceP4,
                lastP5GameTime = nowGameTime - (long)(yearsSinceP5 * SEC_YEAR),
                lastP5Mileage  = currentMileage - kmSinceP5
            };
        }

        // ── Helpery formatujace dla UI ──

        public static string FormatRemainingTime(long seconds)
        {
            if (seconds == long.MinValue) return "—";
            bool neg = seconds < 0;
            long s = Math.Abs(seconds);
            string prefix = neg ? "-" : "";
            if (s >= SEC_YEAR)  return $"{prefix}{s / (float)SEC_YEAR:F1} lat";
            if (s >= SEC_DAY)   return $"{prefix}{s / SEC_DAY} dni";
            if (s >= SEC_HOUR)  return $"{prefix}{s / SEC_HOUR} h";
            return $"{prefix}{s / 60} min";
        }

        public static string FormatRemainingKm(float km)
        {
            if (float.IsNaN(km)) return "—";
            return $"{km:N0} km";
        }

        public static string LevelLimitText(InspectionLevel level, InspectionIntervals intervals)
        {
            if (intervals == null) intervals = InspectionIntervals.CreateDefault();
            switch (level)
            {
                case InspectionLevel.P1: return $"co {intervals.p1LimitHours} h";
                case InspectionLevel.P2: return $"co {intervals.p2LimitDays} dni";
                case InspectionLevel.P3: return $"co {intervals.p3LimitKm / 1000f:F0}k km";
                case InspectionLevel.P4: return $"co {intervals.p4LimitKm / 1000f:F0}k km / {intervals.p4LimitYears} lat";
                case InspectionLevel.P5: return $"co {intervals.p5LimitKm / 1_000_000f:F1}M km / {intervals.p5LimitYears} lat";
            }
            return "";
        }

        /// <summary>LEGACY overload — domyślne intervals.</summary>
        public static string LevelLimitText(InspectionLevel level)
            => LevelLimitText(level, InspectionIntervals.CreateDefault());
    }
}
