using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    public partial class TimetableCreatorUI
    {
        private void GenerateRoute()
        {
            if (_startStation == null || _endStation == null) return;
            var init = TimetableInitializer.Instance;
            if (init == null || !init.IsReady) return;

            RailwayPathfinder.ResetCounters();
            var swAll = System.Diagnostics.Stopwatch.StartNew();

            var swBuildRoute = System.Diagnostics.Stopwatch.StartNew();
            var route = BuildRoute();
            swBuildRoute.Stop();
            if (route == null) return;
            int afterBuildRouteCalls = RailwayPathfinder.CallCount;
            double afterBuildRouteAStar = RailwayPathfinder.AStarTotalMs;

            int catIdx = _categoryDropdown != null ? _categoryDropdown.value : 0;
            var cats = TimetableService.CommercialCategories;
            var cat = catIdx < cats.Count ? cats[catIdx] : null;
            if (cat == null) { Log.Warn("[TimetableCreator] Brak kategorii"); return; }

            int vmax = 120;
            if (_vmaxInput != null) int.TryParse(_vmaxInput.text, out vmax);
            int startMin = ParseTime(_startTimeInput?.text ?? "06:00");

            var swBuildStops = System.Diagnostics.Stopwatch.StartNew();
            _stops = TimetableBuilder.BuildStops(route, init.Graph, cat, _compositionMode, vmax, startMin);
            ApplyStopMemory(route, cat);
            swBuildStops.Stop();

            _showTimes = false;

            var swRefresh = System.Diagnostics.Stopwatch.StartNew();
            RefreshStopsList(startMin);
            swRefresh.Stop();
            int refreshCalls = RailwayPathfinder.CallCount - afterBuildRouteCalls;
            double refreshAStar = RailwayPathfinder.AStarTotalMs - afterBuildRouteAStar;

            int beforeRebuildCalls = RailwayPathfinder.CallCount;
            double beforeRebuildAStar = RailwayPathfinder.AStarTotalMs;
            var swRebuild = System.Diagnostics.Stopwatch.StartNew();
            RebuildRouteViaTrackWaypoints(route, _stops);
            swRebuild.Stop();
            int rebuildCalls = RailwayPathfinder.CallCount - beforeRebuildCalls;
            double rebuildAStar = RailwayPathfinder.AStarTotalMs - beforeRebuildAStar;

            _currentRoute = route;

            Log.Info($"[TimetableCreator] Route generated: {route.totalLengthM / 1000f:F1} km, "
                     + $"{_stops.Count} stops");

            RefreshSummary(cat);
            swAll.Stop();

            double total = swAll.Elapsed.TotalMilliseconds;
            Log.Info($"[TimetableCreator] === GENERATEROUTE PERF === TOTAL={total:F0}ms ({total / 1000.0:F1}s) | "
                     + $"BuildRoute={swBuildRoute.Elapsed.TotalMilliseconds:F0}ms (A*={afterBuildRouteCalls}c) | "
                     + $"BuildStops={swBuildStops.Elapsed.TotalMilliseconds:F0}ms | "
                     + $"RefreshStopsList={swRefresh.Elapsed.TotalMilliseconds:F0}ms (A*={refreshCalls}c, "
                     + $"sumAStar={refreshAStar:F0}ms, avg={(refreshCalls > 0 ? refreshAStar / refreshCalls : 0):F1}ms/call) | "
                     + $"RebuildViaTracks={swRebuild.Elapsed.TotalMilliseconds:F0}ms (A*={rebuildCalls}c, "
                     + $"sumAStar={rebuildAStar:F0}ms, avg={(rebuildCalls > 0 ? rebuildAStar / rebuildCalls : 0):F1}ms/call) | "
                     + $"GLOBAL A*: calls={RailwayPathfinder.CallCount} (failed={RailwayPathfinder.FailedCalls}), "
                     + $"explored={RailwayPathfinder.ExploredTotal}, "
                     + $"sumAStar={RailwayPathfinder.AStarTotalMs:F0}ms, "
                     + $"sumTotal={RailwayPathfinder.TotalTimeMs:F0}ms");

            if (_computeBtn != null) _computeBtn.interactable = true;
            if (_confirmBtn != null) _confirmBtn.interactable = false;

            RefreshRoutePreview();
        }

        private void ComputeStops()
        {
            if (_stops == null || _stops.Count < 2) return;
            var init = TimetableInitializer.Instance;
            if (init == null || !init.IsReady) return;

            if (_currentRoute == null)
            {
                GenerateRoute();
                if (_currentRoute == null) return;
            }

            RebuildRouteViaTrackWaypoints(_currentRoute, _stops);
            _showTimes = true;

            int catIdx = _categoryDropdown != null ? _categoryDropdown.value : 0;
            var cats = TimetableService.CommercialCategories;
            var cat = catIdx < cats.Count ? cats[catIdx] : null;
            if (cat == null) return;

            int vmax = 120;
            if (_vmaxInput != null) int.TryParse(_vmaxInput.text, out vmax);
            int startMin = ParseTime(_startTimeInput?.text ?? "06:00");

            _stops = TimetableBuilder.BuildStops(_currentRoute, init.Graph, cat, _compositionMode, vmax, startMin);
            ApplyStopMemory(_currentRoute, cat);
            RecalculateStopTimes();

            var freq = ReadFrequencySpec();
            _collisions = ReservationManager.CheckCollisionsForFrequency(
                _stops, _currentRoute, init.Graph, freq, init);
            UpdateTaktSummary();

            RefreshStopsList(startMin);
            RefreshSummary(cat);

            bool hasStops = _stops != null && _stops.Count >= 2;
            bool hasCollisions = _collisions.Count > 0;
            if (_confirmBtn != null) _confirmBtn.interactable = hasStops && !hasCollisions;
            if (_forceConfirmBtn != null) _forceConfirmBtn.gameObject.SetActive(hasCollisions);

            RefreshRoutePreview();
        }

        private void Confirm()
        {
            if (_stops == null || _stops.Count < 2) return;

            var route = _currentRoute;
            if (route == null)
            {
                route = BuildRoute();
                if (route == null) return;
                RebuildRouteViaTrackWaypoints(route, _stops);
            }
            else
            {
                RebuildRouteViaTrackWaypoints(route, _stops);
            }

            TimetableService.AddRoute(route);

            int catIdx = _categoryDropdown != null ? _categoryDropdown.value : 0;
            var cats = TimetableService.CommercialCategories;
            var cat = catIdx < cats.Count ? cats[catIdx] : cats[0];
            int vmax = 120;
            if (_vmaxInput != null) int.TryParse(_vmaxInput.text, out vmax);
            int startMin = ParseTime(_startTimeInput?.text ?? "06:00");

            var tt = TimetableBuilder.CreateTimetable(
                route, cat.id, _stops, _compositionMode, vmax, startMin);

            if (tt.composition != null)
                tt.composition.assignment = _compositionAssignment;

            tt.frequency = ReadFrequencySpec();
            tt.calendar = ReadDayMaskFromToggles();

            int weeks = 4;
            if (_weeksValidInput != null && int.TryParse(_weeksValidInput.text, out int parsedWeeks))
                weeks = Mathf.Max(0, parsedWeeks);
            tt.weeksValid = weeks;

            ClassifyIrjForTimetable(tt, route, vmax, startMin);

            if (!ResolveAndAssignTrainNumber(tt, route))
                return;

            string dateRaw = _startDateInput?.text;
            if (!string.IsNullOrWhiteSpace(dateRaw)
                && IsoTime.TryParseDate(dateRaw, out var parsedDate))
            {
                tt.startDateIso = parsedDate.ToString("yyyy-MM-dd");
            }
            else
            {
                tt.startDateIso = DefaultStartDate();
                if (!string.IsNullOrWhiteSpace(dateRaw))
                    Log.Warn($"[TimetableCreator] Nieparsowalna data '{dateRaw}', ustawiono domyslna {tt.startDateIso}");
            }

            TimetableService.AddTimetable(tt);

            foreach (var wp in _waypoints)
                if (wp != null) tt.viaStationNames.Add(wp.name);

            var init2 = TimetableInitializer.Instance;
            ReservationManager.AutoAssignPlatforms(tt, init2);
            ReservationManager.ReserveForTimetable(tt, route, init2?.Graph);

            SaveStopMemoryForCurrentSelection(route, cat);

            Log.Info($"[TimetableCreator] Saved: {tt.name}");

            var presetCallback = _activePreset?.onConfirmed;
            _stops = null;
            _collisions.Clear();
            Hide();

            if (presetCallback != null)
            {
                Log.Info($"[TimetableCreator] Preset callback invoked for '{tt.name}'");
                try { presetCallback.Invoke(tt); }
                catch (System.Exception ex) { Log.Error($"[TimetableCreator] Preset callback error: {ex}"); }
                _activePreset = null;
            }
            else
            {
                ReturnToList();
            }
        }

        private static void ClassifyIrjForTimetable(Timetable tt, Route route, int vmaxKmh, int startMin)
        {
            if (tt == null || route == null) return;
            if (tt.irjCategoryManualOverride) return;

            var init = TimetableInitializer.Instance;
            if (init == null) return;

            List<Vector2> polyline;
            if (route.nodeIds != null && init.Graph != null)
                polyline = init.Graph.BuildRoutePolyline(route.nodeIds, out _);
            else
                polyline = new List<Vector2>();

            int passengerStops = 0;
            if (tt.stops != null)
                foreach (var s in tt.stops)
                    if (s.stopType == StopType.PH) passengerStops++;

            int totalStations = route.stations?.Count ?? tt.stops?.Count ?? 0;

            var stationCityNames = new List<string>();
            if (route.stations != null)
                foreach (var rs in route.stations)
                    stationCityNames.Add(rs.cityName);

            bool isElectric = true;

            var input = new CategoryClassifier.ClassificationInput
            {
                routePolyline = polyline,
                stopsOnRoute = passengerStops,
                totalStationsOnRoute = totalStations,
                startMinutesFromMidnight = startMin,
                maxSpeedKmh = vmaxKmh,
                compositionMode = tt.composition?.mode ?? CompositionMode.MultipleUnit,
                isElectric = isElectric,
                voivodeshipResolver = init.Resolver,
                agglomerations = init.Agglomerations,
                stationCityNames = stationCityNames
            };

            tt.irjCategory = CategoryClassifier.Classify(input);
            string code = IrjCategoryCatalog.GetCode(tt.irjCategory);
            Log.Info($"[TimetableCreator] Auto IRJ: {code} "
                     + $"(stops {passengerStops}/{totalStations}, vmax {vmaxKmh}, start {startMin / 60:D2}:{startMin % 60:D2})");
        }

        private void CancelAll()
        {
            _stops = null;
            _activePreset = null;
            RouteBuildStateMachine.Instance?.Cancel();
            Hide();
            ReturnToList();
        }

        private void ReturnToList()
        {
            if (TimetableListUI.Instance != null)
            {
                TimetableListUI.Instance.RefreshTable();
                TimetableListUI.Instance.Show();
            }
        }
    }
}
