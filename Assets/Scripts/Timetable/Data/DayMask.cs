using System;

namespace RailwayManager.Timetable
{
    /// <summary>Maska dni tygodnia — który z pn/wt/śr/cz/pt/sb/nd kursuje rozkład.</summary>
    [Serializable]
    public struct DayMask
    {
        /// <summary>Flagi dni. Bit 0 = poniedziałek, bit 6 = niedziela.</summary>
        public byte bits;

        public const byte Monday    = 1 << 0;
        public const byte Tuesday   = 1 << 1;
        public const byte Wednesday = 1 << 2;
        public const byte Thursday  = 1 << 3;
        public const byte Friday    = 1 << 4;
        public const byte Saturday  = 1 << 5;
        public const byte Sunday    = 1 << 6;

        public const byte Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday;
        public const byte Weekend  = Saturday | Sunday;
        public const byte EveryDay = Weekdays | Weekend;

        public static DayMask Daily() => new() { bits = EveryDay };
        public static DayMask OnlyWeekdays() => new() { bits = Weekdays };
        public static DayMask OnlyWeekend() => new() { bits = Weekend };

        /// <summary>Czy rozkład kursuje danego dnia tygodnia (0 = poniedziałek, 6 = niedziela).</summary>
        public bool Runs(int dayOfWeek0Mon) => (bits & (1 << dayOfWeek0Mon)) != 0;

        public void Set(int dayOfWeek0Mon, bool value)
        {
            if (value) bits |= (byte)(1 << dayOfWeek0Mon);
            else       bits &= (byte)~(1 << dayOfWeek0Mon);
        }

        public int Count()
        {
            int c = 0;
            for (int i = 0; i < 7; i++) if (Runs(i)) c++;
            return c;
        }
    }
}
