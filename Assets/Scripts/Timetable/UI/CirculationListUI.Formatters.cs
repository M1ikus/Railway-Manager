using System.Collections.Generic;
using System.Text;
using RailwayManager.Fleet;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class CirculationListUI
    {
        private static string FormatTrack(TimetableStop stop)
        {
            if (stop == null) return "";
            if (stop.platformId < 0) return "";
            return string.Format(
                LocalizationService.Get("timetable.circulations.step_bar.track_format"),
                stop.platformId);
        }

        private static string FormatRouteShort(Circulation c)
        {
            if (c.steps == null || c.steps.Count == 0)
                return LocalizationService.Get("timetable.circulations.format.route_empty");

            var first = TimetableService.GetTimetable(c.steps[0].timetableId);
            var last = TimetableService.GetTimetable(c.steps[c.steps.Count - 1].timetableId);
            string unknown = LocalizationService.Get("timetable.circulations.format.unknown");
            string s = first?.FirstStop?.stationName ?? unknown;
            string e = last?.LastStop?.stationName ?? unknown;
            string fmtKey = c.steps.Count == 1
                ? "timetable.circulations.format.route_single_format"
                : "timetable.circulations.format.route_multi_format";
            return string.Format(LocalizationService.Get(fmtKey), s, e);
        }

        private static string FormatVehicleShort(List<int> ids)
        {
            if (ids == null || ids.Count == 0)
                return LocalizationService.Get("timetable.circulations.format.vehicle_add");

            if (ids.Count == 1)
            {
                foreach (var v in FleetService.OwnedVehicles)
                    if (v != null && v.id == ids[0])
                        return string.Format(
                            LocalizationService.Get("timetable.circulations.format.vehicle_named_format"),
                            v.series, v.number);

                return string.Format(
                    LocalizationService.Get("timetable.circulations.format.vehicle_unknown_format"),
                    ids[0]);
            }

            return string.Format(
                LocalizationService.Get("timetable.circulations.format.vehicles_count_format"),
                ids.Count);
        }

        private static string FormatDayMask(DayMask mask)
        {
            if (mask.bits == DayMask.EveryDay)
                return LocalizationService.Get("timetable.circulations.format.day_mask_everyday");
            if (mask.bits == DayMask.Weekdays)
                return LocalizationService.Get("timetable.circulations.format.day_mask_weekdays");
            if (mask.bits == DayMask.Weekend)
                return LocalizationService.Get("timetable.circulations.format.day_mask_weekend");

            char[] days = { 'p', 'w', 'ś', 'c', 'p', 's', 'n' };
            var sb = new StringBuilder();
            for (int i = 0; i < 7; i++)
                sb.Append(mask.Runs(i) ? days[i] : '-');
            return sb.ToString();
        }

        private static string TrimStr(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max - 1) + "…";
        }

        private static string FmtHHMM(int minutes)
        {
            int h = (minutes / 60) % 24;
            int m = minutes % 60;
            return $"{h:D2}:{m:D2}";
        }
    }
}
