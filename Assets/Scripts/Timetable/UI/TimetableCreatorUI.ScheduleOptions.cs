using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class TimetableCreatorUI
    {
        private void OnTaktToggleChanged(bool isOn)
        {
            if (_taktRow != null) _taktRow.SetActive(isOn);
            UpdateTaktSummary();
        }

        /// <summary>
        /// Pokazuje podgląd ile kursów wygeneruje takt (np. "23 kursy 06:00..22:00 co 60 min")
        /// i ostrzega przy nieprawidłowych parametrach.
        /// </summary>
        private void UpdateTaktSummary()
        {
            if (_taktSummary == null || _taktToggle == null || !_taktToggle.isOn) return;

            var spec = ReadTaktSpec();
            if (spec.intervalMinutes <= 0)
            {
                _taktSummary.text = LocalizationService.Get("timetable.creator.takt_summary.interval_must_be_positive");
                _taktSummary.color = UITheme.Danger;
                return;
            }
            int runs = spec.RunsPerDay();
            if (runs <= 0)
            {
                _taktSummary.text = LocalizationService.Get("timetable.creator.takt_summary.last_before_first");
                _taktSummary.color = UITheme.Danger;
                return;
            }
            string firstHHMM = $"{spec.firstRunMinutesFromMidnight / 60:D2}:{spec.firstRunMinutesFromMidnight % 60:D2}";
            string lastHHMM = $"{spec.lastRunMinutesFromMidnight / 60:D2}:{spec.lastRunMinutesFromMidnight % 60:D2}";
            _taktSummary.text = string.Format(
                LocalizationService.Get("timetable.creator.takt_summary.summary_format"),
                runs,
                firstHHMM,
                lastHHMM,
                spec.intervalMinutes);
            _taktSummary.color = UITheme.Success;
        }

        /// <summary>
        /// Buduje FrequencySpec z aktualnych pól UI. Pierwszy kurs = pole startTime,
        /// interwał + ostatni kurs z pól taktu. Single jeśli toggle wyłączony.
        /// </summary>
        private FrequencySpec ReadFrequencySpec()
        {
            int startMin = ParseTime(_startTimeInput?.text ?? "06:00");
            if (_taktToggle == null || !_taktToggle.isOn)
                return FrequencySpec.SingleRun(startMin);
            return ReadTaktSpec();
        }

        private FrequencySpec ReadTaktSpec()
        {
            int startMin = ParseTime(_startTimeInput?.text ?? "06:00");
            int interval = 60;
            if (_taktIntervalInput != null) int.TryParse(_taktIntervalInput.text, out interval);
            int lastMin = ParseTime(_taktLastInput?.text ?? "22:00");
            if (lastMin < startMin) lastMin += 24 * 60;
            return FrequencySpec.Takt(interval, startMin, lastMin);
        }

        /// <summary>
        /// Sprawdza kolizje dla każdego wystąpienia taktu (każdy start-of-run).
        /// Zwraca listę ostrzeżeń per godzina kursu — albo pustą jeśli wszystko OK.
        /// </summary>
        private List<string> CheckTaktCollisions(FrequencySpec spec, Route route, TimetableInitializer init)
        {
            var warnings = new List<string>();
            if (spec.type != FrequencyType.Takt || _stops == null) return warnings;
            int interval = spec.intervalMinutes;
            if (interval <= 0) return warnings;

            for (int t = spec.firstRunMinutesFromMidnight;
                 t <= spec.lastRunMinutesFromMidnight;
                 t += interval)
            {
                int normalized = t % (24 * 60);
                var col = ReservationManager.CheckCollisions(_stops, route, init.Graph, normalized, init);
                if (col != null && col.Count > 0)
                    warnings.Add($"{normalized / 60:D2}:{normalized % 60:D2} ({col.Count} kolizji)");
            }
            return warnings;
        }

        /// <summary>Czyta DayMask z 7 toggle'ów kalendarza.</summary>
        private DayMask ReadDayMaskFromToggles()
        {
            var dm = new DayMask();
            for (int i = 0; i < 7; i++)
                if (_dayToggles[i] != null && _dayToggles[i].isOn)
                    dm.Set(i, true);
            if (dm.Count() == 0) dm = DayMask.Daily();
            return dm;
        }

        /// <summary>Ustawia 7 toggle'ów na podany bitmask preset.</summary>
        private void SetDayMask(byte bits)
        {
            for (int i = 0; i < 7; i++)
                if (_dayToggles[i] != null)
                    _dayToggles[i].isOn = (bits & (1 << i)) != 0;
        }

        /// <summary>
        /// Re-populuje opcje dropdown'a kategorii handlowych na podstawie aktualnego
        /// stanu TimetableService.CommercialCategories. Wywoływane przy każdym Open()
        /// żeby nowe kategorie z CategoryEditor były widoczne.
        /// </summary>
        private void RefreshCategoryDropdown()
        {
            if (_categoryDropdown == null) return;
            var opts = new List<TMP_Dropdown.OptionData>();
            foreach (var c in TimetableService.CommercialCategories)
                opts.Add(new TMP_Dropdown.OptionData($"{c.shortCode} — {c.displayName}"));
            _categoryDropdown.options = opts;
            _categoryDropdown.RefreshShownValue();
        }

        /// <summary>Default = dziś (czas gry). Format YYYY-MM-DD.</summary>
        private static string DefaultStartDate()
        {
            return System.DateTime.Today.ToString("yyyy-MM-dd");
        }
    }
}
