using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class TimetableCreatorUI
    {
        /// <summary>
        /// Wywołuje TrainNumberValidator.GenerateAutoNumber, wpisuje wynik w pole UI.
        /// Wymaga wybranej trasy (potrzebne województwo startu do obszaru konstrukcyjnego)
        /// i wyklasyfikowanej kategorii IRJ — tymczasowo klasyfikujemy "na żywo" z aktualnych
        /// pól żeby gracz mógł kliknąć Auto przed Confirm.
        /// </summary>
        private void AutoGenTrainNumber()
        {
            if (_startStation == null || _endStation == null)
            {
                if (_trainNumberStatus != null)
                    _trainNumberStatus.text = LocalizationService.Get("timetable.creator.train_number_status.select_route_first");
                return;
            }

            var (cat, voivodeship) = ClassifyForUiPreview();
            var existing = TimetableService.GetActiveTrainNumbers();
            string num = TrainNumberValidator.GenerateAutoNumber(cat, voivodeship, existing);
            if (num == null)
            {
                if (_trainNumberStatus != null)
                {
                    _trainNumberStatus.text = LocalizationService.Get("timetable.creator.train_number_status.no_free_for_category");
                    _trainNumberStatus.color = UITheme.Danger;
                }
                return;
            }

            if (_trainNumberInput != null) _trainNumberInput.text = num;
        }

        /// <summary>
        /// Sprawdza wpisany numer wobec aktualnego stanu i pokazuje feedback w label.
        /// Nie blokuje confirm — to robi ResolveAndAssignTrainNumber.
        /// </summary>
        private void ValidateTrainNumberUI()
        {
            if (_trainNumberStatus == null) return;
            string num = _trainNumberInput?.text;
            if (string.IsNullOrEmpty(num))
            {
                _trainNumberStatus.text = LocalizationService.Get("timetable.creator.train_number_status.empty_will_autogen");
                _trainNumberStatus.color = UITheme.SecondaryText;
                return;
            }
            if (_startStation == null)
            {
                _trainNumberStatus.text = LocalizationService.Get("timetable.creator.train_number_status.select_route_to_validate");
                _trainNumberStatus.color = UITheme.SecondaryText;
                return;
            }

            var (cat, voivodeship) = ClassifyForUiPreview();
            var existing = TimetableService.GetActiveTrainNumbers();
            var result = TrainNumberValidator.Validate(num, cat, voivodeship, existing);
            if (result.IsValid)
            {
                _trainNumberStatus.text = string.Format(
                    LocalizationService.Get("timetable.creator.train_number_status.ok_format"),
                    IrjCategoryCatalog.GetCode(cat));
                _trainNumberStatus.color = UITheme.Success;
            }
            else
            {
                _trainNumberStatus.text = result.message;
                _trainNumberStatus.color = result.result == TrainNumberValidator.Result.NumberInUse
                    ? UITheme.Danger
                    : UITheme.Warning;
            }
        }

        /// <summary>
        /// Tymczasowa klasyfikacja IRJ + odczyt województwa startu — używane przez
        /// AutoGen i UI walidator (zanim gracz kliknie Confirm).
        /// </summary>
        private (IrjCategory cat, string startVoi) ClassifyForUiPreview()
        {
            int vmax = 120;
            if (_vmaxInput != null) int.TryParse(_vmaxInput.text, out vmax);
            int startMin = ParseTime(_startTimeInput?.text ?? "06:00");

            var fallback = new IrjCategory(IrjGroup.RegionalFast, TractionLetter.ElectricUnit);
            var init = TimetableInitializer.Instance;
            if (init == null || _startStation == null)
                return (fallback, _startStation?.voivodeship);

            var routeStations = new List<RailwayStation> { _startStation };
            foreach (var wp in _waypoints)
                if (wp != null) routeStations.Add(wp);
            if (_endStation != null) routeStations.Add(_endStation);

            var polyline = new List<Vector2>();
            var cityNames = new List<string>();
            foreach (var rs in routeStations)
            {
                polyline.Add(rs.position);
                cityNames.Add(rs.cityName);
            }

            var input = new CategoryClassifier.ClassificationInput
            {
                routePolyline = polyline,
                stopsOnRoute = routeStations.Count,
                totalStationsOnRoute = routeStations.Count,
                startMinutesFromMidnight = startMin,
                maxSpeedKmh = vmax,
                compositionMode = _compositionMode,
                isElectric = true,
                voivodeshipResolver = init.Resolver,
                agglomerations = init.Agglomerations,
                stationCityNames = cityNames
            };
            var cat = CategoryClassifier.Classify(input);
            return (cat, _startStation.voivodeship);
        }

        /// <summary>
        /// Wykonuje końcową logikę numeru pociągu w Confirm.
        /// </summary>
        private bool ResolveAndAssignTrainNumber(Timetable tt, Route route)
        {
            string startVoi = _startStation?.voivodeship;
            var existing = TimetableService.GetActiveTrainNumbers();
            string typed = _trainNumberInput?.text;

            if (string.IsNullOrWhiteSpace(typed))
            {
                string auto = TrainNumberValidator.GenerateAutoNumber(tt.irjCategory, startVoi, existing);
                if (auto == null)
                {
                    Log.Warn("[TimetableCreator] Auto-gen numeru zwrócił null — nie ma wolnych slotów dla tej kategorii.");
                    if (_trainNumberStatus != null)
                    {
                    _trainNumberStatus.text = LocalizationService.Get("timetable.creator.train_number_status.no_free");
                    _trainNumberStatus.color = UITheme.Danger;
                }
                return false;
            }
                tt.trainNumber = auto;
                Log.Info($"[TimetableCreator] Auto-gen numeru: {auto} dla {IrjCategoryCatalog.GetCode(tt.irjCategory)}");
                return true;
            }

            var result = TrainNumberValidator.Validate(typed, tt.irjCategory, startVoi, existing);
            if (result.IsValid)
            {
                tt.trainNumber = typed;
                return true;
            }

            if (result.result == TrainNumberValidator.Result.NumberInUse
                || result.result == TrainNumberValidator.Result.InvalidFormat)
            {
                Log.Warn($"[TimetableCreator] Numer odrzucony (hard): {result.message}");
                if (_trainNumberStatus != null)
                {
                    _trainNumberStatus.text = result.message;
                    _trainNumberStatus.color = UITheme.Danger;
                }
                return false;
            }

            if (!_trainNumberOverrideAccepted)
            {
                _trainNumberOverrideAccepted = true;
                if (_trainNumberStatus != null)
                {
                    _trainNumberStatus.text = string.Format(
                        LocalizationService.Get("timetable.creator.train_number_status.soft_warn_format"),
                        result.message);
                    _trainNumberStatus.color = UITheme.Warning;
                }
                Log.Info($"[TimetableCreator] Soft warn dla numeru {typed}: {result.message}. "
                         + "Wymagany drugi confirm żeby zatwierdzić.");
                return false;
            }

            tt.trainNumber = typed;
            Log.Info($"[TimetableCreator] Numer {typed} użyty mimo ostrzeżenia: {result.message}");
            return true;
        }
    }
}
