using System;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Specyfikacja częstotliwości rozkładu: pojedynczy kurs lub takt (co N minut).
    /// Generator tworzy listę TrainRun z tego + DayMask + validFrom/validTo.
    /// </summary>
    [Serializable]
    public struct FrequencySpec
    {
        public FrequencyType type;

        /// <summary>Interwał taktu w minutach (tylko dla type == Takt).</summary>
        public int intervalMinutes;

        /// <summary>Pierwsza godzina startu w minutach od północy (0..1439). Dla Single = godzina kursu.</summary>
        public int firstRunMinutesFromMidnight;

        /// <summary>Ostatnia godzina startu w minutach od północy (tylko Takt).</summary>
        public int lastRunMinutesFromMidnight;

        public static FrequencySpec SingleRun(int minutesFromMidnight) => new()
        {
            type = FrequencyType.Single,
            firstRunMinutesFromMidnight = minutesFromMidnight
        };

        public static FrequencySpec Takt(int intervalMin, int firstMin, int lastMin) => new()
        {
            type = FrequencyType.Takt,
            intervalMinutes = intervalMin,
            firstRunMinutesFromMidnight = firstMin,
            lastRunMinutesFromMidnight = lastMin
        };

        /// <summary>Ile kursów wygeneruje ten spec dla jednego dnia.</summary>
        public int RunsPerDay()
        {
            if (type == FrequencyType.Single) return 1;
            if (intervalMinutes <= 0) return 0;
            int span = lastRunMinutesFromMidnight - firstRunMinutesFromMidnight;
            if (span < 0) return 0;
            return span / intervalMinutes + 1;
        }
    }
}
