using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class TimetableCreatorUI
    {
        /// <summary>
        /// Stosuje pamięć postojów z poprzednich sesji kreatora dla tej samej (trasa×kategoria).
        /// Pamięć przechowuje listę stationNodeId które miały postój pasażerski. Reszta
        /// stacji (oprócz pierwszej i ostatniej) zostaje PassThrough.
        /// </summary>
        private void ApplyStopMemory(Route route, CommercialCategory cat)
        {
            if (_stops == null || route == null || cat == null) return;
            var memorized = TimetableService.LoadStopMemory(route.RouteHash, cat.id);
            if (memorized == null || memorized.Count == 0) return;

            var memSet = new HashSet<int>(memorized);
            int restored = 0;
            for (int i = 0; i < _stops.Count; i++)
            {
                bool isFirstOrLast = (i == 0 || i == _stops.Count - 1);
                if (isFirstOrLast) continue;

                bool wasPassenger = memSet.Contains(_stops[i].stationNodeId);
                var newType = wasPassenger ? StopType.PH : StopType.Transit;
                if (_stops[i].stopType != newType)
                {
                    _stops[i].stopType = newType;
                    restored++;
                }
            }
            if (restored > 0)
                Log.Info($"[TimetableCreator] Pamięć postojów: przywrócono {restored} wyborów "
                         + $"({memSet.Count} pasażerskich w pamięci dla {cat.shortCode})");
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11: Remove stop + add to blacklist + rebuild route.
        /// User klikuje "✕" przy stopie → stationNodeId trafia do blacklist (FindStationsPerSegment
        /// pomija go przy regen), stops list jest rebuilt z aktualnej trasy bez tego punktu.
        /// Blacklist reset gdy nowy A→B (Reset() w TimetableCreatorUI.cs).
        /// </summary>
        private void RemoveStopAndBlacklist(int stopIdx, int stationNodeId)
        {
            if (_stops == null || stopIdx < 0 || stopIdx >= _stops.Count) return;
            // Skip first/last (start/end stops nie są usuwalne)
            if (stopIdx == 0 || stopIdx == _stops.Count - 1) return;

            _blacklistedStationNodeIds.Add(stationNodeId);
            Log.Info($"[TimetableCreator] Stop usunięty: {_stops[stopIdx].stationName} " +
                     $"(stationNodeId={stationNodeId}); blacklist size={_blacklistedStationNodeIds.Count}");

            // Pełen rebuild route bez tego stopu — GenerateRoute() flow:
            // BuildRoute (F1.6 FindStationsPerSegment pomija blacklist) → BuildStops → RefreshStopsList →
            // RebuildRouteViaTrackWaypoints. Pathfinder znajdzie alternatywę między prev a next stop.
            GenerateRoute();
        }

        /// <summary>Zapisuje aktualne wybory pasażerskich postojów do pamięci.</summary>
        private void SaveStopMemoryForCurrentSelection(Route route, CommercialCategory cat)
        {
            if (_stops == null || route == null || cat == null) return;
            var passengerStops = new List<int>();
            foreach (var s in _stops)
                if (s.stopType == StopType.PH)
                    passengerStops.Add(s.stationNodeId);
            TimetableService.SaveStopMemory(route.RouteHash, cat.id, passengerStops);
        }

        /// <summary>
        /// Wywoływane po zmianie toru w dropdown. Przebudowuje trasę i sprawdza
        /// czy zmiana nie wydłuża segmentu >2x (ostrzeżenie o nieodpowiednim torze).
        /// </summary>
        void OnTrackChanged(int stopIdx, string oldTrackRef, string newTrackRef)
        {
            if (_currentRoute == null || oldTrackRef == newTrackRef) return;

            _manualTrackOverrides[_stops[stopIdx].stationName] = newTrackRef;
            var graph = TimetableInitializer.Instance?.Graph;
            if (graph == null) return;

            var stop = _stops[stopIdx];
            TimetableStop prev = stopIdx > 0 ? _stops[stopIdx - 1] : null;
            TimetableStop next = stopIdx < _stops.Count - 1 ? _stops[stopIdx + 1] : null;

            float oldLen = MeasureSegmentViaTrack(graph, stop, oldTrackRef, prev, next);
            float newLen = MeasureSegmentViaTrack(graph, stop, newTrackRef, prev, next);

            if (oldLen > 0 && newLen > oldLen * 2f)
            {
                Log.Warn($"[TimetableCreator] Tor {newTrackRef} na {stop.stationName}: " +
                         $"segment {newLen / 1000f:F1}km vs {oldLen / 1000f:F1}km (>{newLen / oldLen:F1}x) " +
                         $"— tor moze byc nieodpowiedni do kierunku jazdy");

                if (_summaryText != null)
                {
                    string warn = string.Format(
                        LocalizationService.Get("timetable.creator.stops.track_warning_format"),
                        newTrackRef,
                        stop.stationName,
                        (newLen / oldLen).ToString("F1"));
                    _summaryText.text = warn;
                    _summaryText.color = UITheme.Warning;
                }
            }

            RebuildRouteViaTrackWaypoints(_currentRoute, _stops);
            RefreshSummary(GetSelectedCategory());
        }

        /// <summary>Mierzy długość segmentu prev→track→next przez podany tor.</summary>
        static float MeasureSegmentViaTrack(PathfindingGraph graph, TimetableStop stop,
                                             string trackRef, TimetableStop prev, TimetableStop next)
        {
            if (string.IsNullOrEmpty(trackRef)) return 0f;
            int trackNode = graph.FindNodeOnTrack(stop.stationNodeId, trackRef);
            if (trackNode < 0) return 0f;

            float len = 0f;
            if (prev != null && prev.stationNodeId >= 0)
            {
                var r = RailwayPathfinder.FindPath(graph, prev.stationNodeId, trackNode, 50_000);
                len += r.success ? r.totalLengthM : 0f;
            }
            if (next != null && next.stationNodeId >= 0)
            {
                var r = RailwayPathfinder.FindPath(graph, trackNode, next.stationNodeId, 50_000);
                len += r.success ? r.totalLengthM : 0f;
            }
            return len;
        }

        /// <summary>
        /// Przebudowuje route.nodeIds przez waypoints torowe z przypisanych trackRef w stopach.
        /// Logika identyczna jak SimulatedTrain.BuildEffectiveRoute — ale wykonana JUŻ w kreatorze
        /// żeby zapisana trasa (i overlay) odzwierciedlała prawidłowe tory stacyjne.
        /// Modyfikuje route in-place (nodeIds + totalLengthM).
        /// </summary>
        static void RebuildRouteViaTrackWaypoints(Route route, List<TimetableStop> stops)
        {
            var graph = TimetableInitializer.Instance?.Graph;
            if (graph == null || route?.nodeIds == null || route.nodeIds.Count < 2 || stops == null)
                return;

            // M-TimetableUX 2026-05-11 bug fix (Gdynia zawijas):
            // Per stop z trackRef, używamy ENTRY + EXIT nodes na konkretnym torze (zamiast pojedynczego).
            // Pair wymusza linear traversal w direction jazdy — pathfinder wchodzi entry side,
            // wychodzi exit side, eliminuje pętle przez inne tory.

            var waypoints = new List<int>();
            int trackWaypoints = 0;

            // First stop: tylko exit node (path zaczyna się tu, prev = start sam)
            int startNode = route.nodeIds[0];
            if (stops.Count > 0 && !string.IsNullOrEmpty(stops[0].trackRef))
            {
                Vector2 nextPos = stops.Count > 1
                    ? graph.GetNode(stops[1].stationNodeId).position
                    : graph.GetNode(route.nodeIds[route.nodeIds.Count - 1]).position;
                var (entry, exit) = graph.FindEntryExitNodesOnTrack(
                    stops[0].stationNodeId, stops[0].trackRef,
                    graph.GetNode(startNode).position, nextPos);
                if (exit >= 0) { startNode = exit; trackWaypoints++; }
            }
            waypoints.Add(startNode);

            for (int i = 1; i < stops.Count - 1; i++)
            {
                var stop = stops[i];
                if (string.IsNullOrEmpty(stop.trackRef)) continue;

                Vector2 prevPos = graph.GetNode(stops[i - 1].stationNodeId).position;
                Vector2 nextPos = graph.GetNode(stops[i + 1].stationNodeId).position;
                var (entry, exit) = graph.FindEntryExitNodesOnTrack(
                    stop.stationNodeId, stop.trackRef, prevPos, nextPos);
                if (entry < 0) continue;

                // Add entry waypoint
                if (entry != waypoints[waypoints.Count - 1])
                {
                    waypoints.Add(entry);
                    trackWaypoints++;
                }
                // Add exit waypoint (jeśli różny od entry — krótka stacja może mieć single node)
                if (exit >= 0 && exit != entry && exit != waypoints[waypoints.Count - 1])
                {
                    waypoints.Add(exit);
                }
            }

            // Last stop: tylko entry node (path tu się kończy, next = end sam)
            int endNode = route.nodeIds[route.nodeIds.Count - 1];
            if (stops.Count > 1 && !string.IsNullOrEmpty(stops[stops.Count - 1].trackRef))
            {
                Vector2 prevPos = stops.Count > 2
                    ? graph.GetNode(stops[stops.Count - 2].stationNodeId).position
                    : graph.GetNode(route.nodeIds[0]).position;
                var (entry, exit) = graph.FindEntryExitNodesOnTrack(
                    stops[stops.Count - 1].stationNodeId,
                    stops[stops.Count - 1].trackRef,
                    prevPos, graph.GetNode(endNode).position);
                if (entry >= 0) { endNode = entry; trackWaypoints++; }
            }
            if (endNode != waypoints[waypoints.Count - 1])
                waypoints.Add(endNode);

            if (trackWaypoints == 0) return;

            for (int i = waypoints.Count - 1; i > 0; i--)
                if (waypoints[i] == waypoints[i - 1])
                    waypoints.RemoveAt(i);

            if (waypoints.Count < 2) return;

            var result = RailwayPathfinder.FindPathViaWaypoints(graph, waypoints);
            if (result.success)
            {
                RailwayManager.Core.Log.Info(
                    $"[TimetableCreator] Route rebuilt via {trackWaypoints} track waypoints " +
                    $"({waypoints.Count} total waypoint nodes z entry/exit pairs): "
                    + $"{result.nodeIds.Count} nodes, {result.totalLengthM:F0}m "
                    + $"(was {route.nodeIds.Count} nodes, {route.totalLengthM:F0}m)");
                route.nodeIds = result.nodeIds;
                route.totalLengthM = result.totalLengthM;
            }
            else
            {
                RailwayManager.Core.Log.Warn("[TimetableCreator] Track waypoint re-routing failed — keeping original route");
            }
        }

        private void RefreshStopsList(int startMin)
        {
            if (_stopsContent == null || _stops == null) return;

            _ftCallCount = 0;
            _ftJsonHits = 0;
            _ftOsmFallbacks = 0;
            _ftJsonMs = 0;
            _ftOsmMs = 0;
            var swDestroy = System.Diagnostics.Stopwatch.StartNew();
            foreach (Transform ch in _stopsContent) Destroy(ch.gameObject);
            swDestroy.Stop();

            int startSec = startMin * 60;
            int dropdownsCreated = 0;
            double dtRow = 0, dtFindTracks = 0, dtFilter = 0, dtRank = 0, dtDropdown = 0, dtLabels = 0;
            var swRefreshTotal = System.Diagnostics.Stopwatch.StartNew();

            float[] stopKm = null;
            if (_currentRoute != null && _currentRoute.stations != null)
            {
                stopKm = new float[_stops.Count];
                for (int si = 0; si < _stops.Count && si < _currentRoute.stations.Count; si++)
                    stopKm[si] = _currentRoute.stations[si].distanceFromStartM / 1000f;
            }

            var swStep = new System.Diagnostics.Stopwatch();
            for (int i = 0; i < _stops.Count; i++)
            {
                var stop = _stops[i];
                bool isFirstOrLast = (i == 0 || i == _stops.Count - 1);
                int idx = i;

                swStep.Restart();
                var row = Row(_stopsContent);
                StyleRow(row);
                swStep.Stop();
                dtRow += swStep.Elapsed.TotalMilliseconds;

                if (!isFirstOrLast && _showTimes)
                {
                    // M-TimetableUX F1.4: dropdown StopType (PH/PT/ZD/Transit) zamiast 2-state toggle.
                    // F1.16 spec: "defaults pre-filled ale edytowalne" we wszystkich modes.
                    // (poprzedni F1.16 polish gating overreach — revertowane 2026-05-11 per user bug report)
                    var allowedTypes = GetStopAllowedTypes(stop);
                    if (allowedTypes.Count > 1)
                    {
                        // Validate current type still allowed (may switched after platform data change)
                        if (!allowedTypes.Contains(stop.stopType))
                            stop.stopType = allowedTypes[0];

                        var typeDd = MakeStopTypeDropdown(row.transform, allowedTypes, stop.stopType);
                        var captAllowed = allowedTypes;
                        int captIdx = idx;
                        typeDd.onValueChanged.AddListener(val =>
                        {
                            if (val < 0 || val >= captAllowed.Count) return;
                            _stops[captIdx].stopType = captAllowed[val];
                            RecalculateStopTimes();
                            int sm = ParseTime(_startTimeInput?.text ?? "06:00");
                            RefreshStopsList(sm);
                            RefreshSummary(GetSelectedCategory());
                        });
                    }
                    else
                    {
                        // Single-allowed → fixed label (np. halt bez peronu = Transit only)
                        var lbl = Lbl(row.transform, StopTypeValidator.ShortCode(stop.stopType), 10, new Color(0.7f, 0.7f, 0.7f));
                        lbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 60;
                    }
                }
                else if (_showTimes)
                {
                    var spacer = new GameObject("Sp");
                    spacer.transform.SetParent(row.transform, false);
                    spacer.AddComponent<LayoutElement>().preferredWidth = 60;
                }

                ReservationManager.CollisionInfo? collision = null;
                if (_showTimes && _collisions != null)
                    foreach (var c in _collisions)
                        if (c.stopIndex == i) { collision = (ReservationManager.CollisionInfo?)c; break; }

                // M-TimetableUX F1.4: track dropdown dla wszystkich typów postoju (PH/PT/ZD) — platform reservation działa na wszystkich.
                // Transit pomija track dropdown (przelot bez stopu).
                bool isPax = stop.stopType == StopType.PH;
                bool hasPlatformOps = stop.stopType != StopType.Transit;
                bool hasCollision = collision.HasValue;
                // F1.16 deeper gating: w Basic mode hide collision red highlight (player nie zna concept'u
                // collision details; F1.18 notification "Pociąg czeka N min" wystarcza). Advanced+ pokazuje
                // visual red highlight + collision info dla diagnostic.
                bool showCollisionDetails = hasCollision &&
                    RailwayManager.SharedUI.PlayerProgressService.GetEffectiveMode() != RailwayManager.SharedUI.UIMode.Basic;

                Color nameColor = showCollisionDetails ? new Color(1f, 0.3f, 0.3f)
                    : (isPax ? Color.white : new Color(0.5f, 0.5f, 0.5f));

                if (showCollisionDetails)
                {
                    var rowBg = row.GetComponent<Image>();
                    if (rowBg == null) rowBg = row.AddComponent<Image>();
                    UITheme.ApplySurface(rowBg, UITheme.WithAlpha(UITheme.Danger, 0.24f), UIShapePreset.Inset);
                }

                swStep.Restart();
                var nameLabel = Lbl(row.transform, stop.stationName, 11, nameColor);
                nameLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

                // M-TimetableUX 2026-05-11: per-stop "✕" remove button — pomijalne dla
                // start/end stops. Klik dodaje stationNodeId do blacklist (no re-add przy regen)
                // i rebuilduje trasę bez tego punktu.
                if (!isFirstOrLast)
                {
                    int captStopIdx = i;
                    int captStationNodeId = stop.stationNodeId;
                    Btn(row.transform, "✕", () => RemoveStopAndBlacklist(captStopIdx, captStationNodeId),
                        new Color(0.55f, 0.2f, 0.2f), 22);
                }
                swStep.Stop();
                dtLabels += swStep.Elapsed.TotalMilliseconds;

                if (hasPlatformOps)
                {
                    swStep.Restart();
                    var allTracks = FindTracksForStop(stop);
                    var tracks = allTracks.FindAll(t => t.hasPlatform);
                    if (tracks.Count == 0) tracks = allTracks;
                    swStep.Stop();
                    dtFindTracks += swStep.Elapsed.TotalMilliseconds;

                    TimetableStop prevStop = idx > 0 ? _stops[idx - 1] : null;
                    TimetableStop nextStop = idx < _stops.Count - 1 ? _stops[idx + 1] : null;

                    swStep.Restart();
                    tracks = FilterTracksByReachability(tracks, stop, prevStop, nextStop);
                    swStep.Stop();
                    dtFilter += swStep.Elapsed.TotalMilliseconds;

                    swStep.Restart();
                    RankTracksByProximity(tracks, stop, prevStop, nextStop);
                    swStep.Stop();
                    dtRank += swStep.Elapsed.TotalMilliseconds;

                    if (tracks.Count > 0)
                    {
                        int defaultIdx = 0;
                        if (_manualTrackOverrides.TryGetValue(stop.stationName, out var manualRef))
                        {
                            for (int ti = 0; ti < tracks.Count; ti++)
                                if (tracks[ti].trackRef == manualRef) { defaultIdx = ti; break; }
                        }

                        // F1.16 spec: dropdown zawsze edytowalny (defaults pre-filled).
                        // Track dropdown widoczny we wszystkich modes.
                        swStep.Restart();
                        var trackDd = MakeTrackDropdown(row.transform, tracks, 90);
                        trackDd.value = defaultIdx;
                        int captIdx = idx;
                        var captTracks = tracks;
                        trackDd.onValueChanged.AddListener(val =>
                        {
                            if (val >= captTracks.Count) return;
                            string oldTrack = _stops[captIdx].trackRef;
                            _stops[captIdx].platformId = val;
                            _stops[captIdx].trackRef = captTracks[val].trackRef;
                            OnTrackChanged(captIdx, oldTrack, captTracks[val].trackRef);
                        });
                        _stops[captIdx].trackRef = tracks[defaultIdx].trackRef;
                        swStep.Stop();
                        dtDropdown += swStep.Elapsed.TotalMilliseconds;
                        dropdownsCreated++;
                    }
                }

                if (stopKm != null && i < stopKm.Length)
                {
                    swStep.Restart();
                    string kmText = string.Format(
                        LocalizationService.Get("timetable.creator.stops.km_format"),
                        stopKm[i].ToString("F1"));
                    Lbl(row.transform, kmText, 10, new Color(0.6f, 0.7f, 0.8f));
                    swStep.Stop();
                    dtLabels += swStep.Elapsed.TotalMilliseconds;
                }

                if (_showTimes)
                {
                    string arrival = FmtTime(startSec + stop.plannedArrivalSec);
                    string departure = FmtTime(startSec + stop.plannedDepartureSec);
                    Color timeColor = hasCollision ? new Color(1f, 0.4f, 0.4f)
                        : (isPax ? new Color(0.8f, 0.9f, 0.8f) : new Color(0.4f, 0.4f, 0.4f));

                    Lbl(row.transform, arrival, 11, timeColor);
                    Lbl(row.transform, "\u2192", 11, new Color(0.4f, 0.4f, 0.4f));
                    Lbl(row.transform, departure, 11, timeColor);

                    if (isPax)
                    {
                        int stopDur = stop.plannedDepartureSec - stop.plannedArrivalSec;
                        var durInput = Inp(row.transform, $"{stopDur}", 35);
                        int captIdx2 = idx;
                        durInput.onEndEdit.AddListener(text =>
                        {
                            if (_stops == null || captIdx2 >= _stops.Count) return;
                            if (int.TryParse(text, out int newDur) && newDur >= 0)
                            {
                                _stops[captIdx2].plannedDepartureSec = _stops[captIdx2].plannedArrivalSec + newDur;
                                RefreshSummary(GetSelectedCategory());
                            }
                        });
                        Lbl(
                            row.transform,
                            LocalizationService.Get("timetable.creator.stops.duration_unit_s"),
                            10,
                            new Color(0.5f, 0.5f, 0.5f));
                    }
                }

                if (showCollisionDetails)
                    Lbl(row.transform, collision.Value.description, 9, new Color(1f, 0.3f, 0.3f));
            }

            if (_collisions != null)
                foreach (var c in _collisions)
                    if (c.stopIndex < 0)
                    {
                        var segRow = Row(_stopsContent);
                        StyleRow(segRow);
                        UITheme.ApplySurface(
                            segRow.GetComponent<Image>(),
                            UITheme.WithAlpha(UITheme.Danger, 0.24f),
                            UIShapePreset.Inset);
                        Lbl(
                            segRow.transform,
                            string.Format(LocalizationService.Get("timetable.creator.stops.segment_collision_format"), c.description),
                            10,
                            new Color(1f, 0.4f, 0.2f));
                    }

            swRefreshTotal.Stop();
            double totalRefresh = swRefreshTotal.Elapsed.TotalMilliseconds;
            double measured = dtRow + dtFindTracks + dtFilter + dtRank + dtDropdown + dtLabels;
            double other = totalRefresh - measured;
            Log.Info($"[RefreshStopsList] PERF stops={_stops.Count} dropdowns={dropdownsCreated} | "
                     + $"Destroy={swDestroy.Elapsed.TotalMilliseconds:F0}ms | "
                     + $"Row={dtRow:F0}ms FindTracks={dtFindTracks:F0}ms "
                     + $"Filter={dtFilter:F0}ms Rank={dtRank:F0}ms "
                     + $"Dropdown={dtDropdown:F0}ms Labels={dtLabels:F0}ms | "
                     + $"OTHER={other:F0}ms (other = totalRefresh - measured) | "
                     + $"avgPerStop={(totalRefresh / _stops.Count):F1}ms");
            Log.Info($"[FindTracksForStop] calls={_ftCallCount} | "
                     + $"JSON_hits={_ftJsonHits} ({_ftJsonMs:F0}ms total, "
                     + $"avg={(_ftJsonHits > 0 ? _ftJsonMs / _ftCallCount : 0):F2}ms/call) | "
                     + $"OSM_fallbacks={_ftOsmFallbacks} ({_ftOsmMs:F0}ms total, "
                     + $"avg={(_ftOsmFallbacks > 0 ? _ftOsmMs / _ftOsmFallbacks : 0):F1}ms/call)");
        }

        /// <summary>Przelicza czasy odjazdu po zmianie checkboxów postojów.</summary>
        private void RecalculateStopTimes()
        {
            if (_stops == null || _stops.Count == 0) return;
            var cat = GetSelectedCategory();
            int minStop = cat?.minStopSeconds ?? TimetableTuningConstants.DefaultMinStopSeconds;

            for (int i = 0; i < _stops.Count; i++)
            {
                bool isFirstOrLast = (i == 0 || i == _stops.Count - 1);
                int stopDur = (!isFirstOrLast && _stops[i].stopType == StopType.PH) ? minStop : 0;
                _stops[i].plannedDepartureSec = _stops[i].plannedArrivalSec + stopDur;
            }
        }

        private CommercialCategory GetSelectedCategory()
        {
            int catIdx = _categoryDropdown != null ? _categoryDropdown.value : 0;
            var cats = TimetableService.CommercialCategories;
            return catIdx < cats.Count ? cats[catIdx] : null;
        }

        private void RefreshSummary(CommercialCategory cat)
        {
            if (_summaryText == null || _stops == null || _stops.Count == 0) return;
            int totalSec = _stops[_stops.Count - 1].plannedDepartureSec;
            int pax = 0;
            foreach (var s in _stops)
                if (s.stopType == StopType.PH) pax++;
            _summaryText.text = string.Format(
                LocalizationService.Get("timetable.creator.stops.summary_format"),
                totalSec / 60,
                pax,
                _stops.Count,
                cat?.shortCode ?? "?");
            _summaryText.color = UITheme.Success;
        }

        /// <summary>
        /// M-TimetableUX F1.4: zwraca dopuszczalne <see cref="StopType"/> per stop wg
        /// <see cref="StopTypeValidator"/>. Lookup isMajorStation z init.Stations + hasPlatform
        /// z FindTracksForStop. Używane w UI dropdown filter.
        /// </summary>
        private List<StopType> GetStopAllowedTypes(TimetableStop stop)
        {
            // Lookup isMajorStation
            bool isMajor = false;
            var init = TimetableInitializer.Instance;
            if (init?.Stations != null)
            {
                foreach (var st in init.Stations)
                {
                    if (st.name == stop.stationName) { isMajor = st.isMajorStation; break; }
                }
            }

            // Check hasPlatform
            bool hasPlatform = false;
            var tracks = FindTracksForStop(stop);
            foreach (var t in tracks)
                if (t.hasPlatform) { hasPlatform = true; break; }

            var allowed = StopTypeValidator.GetAllowedTypes(isMajor, hasPlatform);
            var result = new List<StopType>(allowed.Count);
            for (int i = 0; i < allowed.Count; i++)
                result.Add(allowed[i]);
            return result;
        }
    }
}
