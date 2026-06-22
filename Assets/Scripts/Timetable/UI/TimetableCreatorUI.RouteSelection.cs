using System.Collections.Generic;
using TMPro;
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
        /// Konfiguracja preset'u dla otwarcia kreatora z innego systemu (np. Fleet
        /// "wyślij na przegląd" → wstaw stację start = obecne home depot, koniec =
        /// najbliższy warsztat). Wszystkie pola opcjonalne — null lub pusty oznacza
        /// "nie ustawiaj". Po preset'ie gracz może edytować dane normalnie.
        /// </summary>
        public class CreatorPreset
        {
            public string startStationName;
            public string endStationName;
            public List<string> waypointStationNames;
            public string commercialCategoryId;     // np. "service" dla przeglądu
            public CompositionMode? compositionMode;
            public int? maxSpeedKmh;
            public int? startMinutesFromMidnight;
            public string startDateIso;
            public int? weeksValid;                 // null → użyj defaultu (4)
            public string trainNumber;              // jeśli null → auto-gen przy Confirm
            public string presetReason;             // do logu, np. "Send to inspection: EU07-001"

            /// <summary>
            /// Callback wywołany po zatwierdzeniu rozkładu (Confirm). Otrzymuje
            /// świeżo utworzony Timetable. Używany przez M5 Obiegi żeby auto-dodać
            /// wygenerowany rozkład służbowy do sekwencji kreatora obiegu.
            /// Null = brak callback'u (zwykły flow Confirm → ReturnToList).
            /// </summary>
            public System.Action<Timetable> onConfirmed;
        }

        private int _pickingWaypointIndex = -1;
        private readonly List<Transform> _waypointSuggestions = new();

        /// <summary>
        /// Otwiera kreator i wypełnia pola wartościami z preset'u.
        /// Używać z zewnątrz (FleetPanel, MaintenanceUI, itp.) zamiast Open().
        /// Bezpieczne wobec braku nazw stacji — pomija pola których nie da się rozwiązać.
        /// </summary>
        public void OpenWithPreset(CreatorPreset preset)
        {
            Open(); // czysty stan
            _activePreset = preset;
            if (preset == null) return;

            var init = TimetableInitializer.Instance;
            if (init == null || init.Stations == null)
            {
                Log.Warn("[TimetableCreator] OpenWithPreset: TimetableInitializer not ready");
                return;
            }

            if (!string.IsNullOrEmpty(preset.presetReason))
                Log.Info($"[TimetableCreator] OpenWithPreset: {preset.presetReason}");

            var start = ResolveStation(init, preset.startStationName);
            if (start != null)
            {
                _startStation = start;
                if (_startInput != null) _startInput.text = start.name;
            }

            var end = ResolveStation(init, preset.endStationName);
            if (end != null)
            {
                _endStation = end;
                if (_endInput != null) _endInput.text = end.name;
            }

            if (preset.waypointStationNames != null)
            {
                foreach (var wpName in preset.waypointStationNames)
                {
                    var wp = ResolveStation(init, wpName);
                    if (wp != null) _waypoints.Add(wp);
                }
                RefreshWaypointsUI();
            }

            if (preset.compositionMode.HasValue)
            {
                _compositionMode = preset.compositionMode.Value;
                if (_emuToggle != null)
                    _emuToggle.isOn = _compositionMode == CompositionMode.MultipleUnit;
            }

            if (preset.maxSpeedKmh.HasValue && _vmaxInput != null)
                _vmaxInput.text = preset.maxSpeedKmh.Value.ToString();

            if (preset.startMinutesFromMidnight.HasValue && _startTimeInput != null)
            {
                int mins = preset.startMinutesFromMidnight.Value;
                _startTimeInput.text = $"{(mins / 60) % 24:D2}:{mins % 60:D2}";
            }

            if (!string.IsNullOrEmpty(preset.startDateIso) && _startDateInput != null)
                _startDateInput.text = preset.startDateIso;

            if (preset.weeksValid.HasValue && _weeksValidInput != null)
                _weeksValidInput.text = preset.weeksValid.Value.ToString();

            if (!string.IsNullOrEmpty(preset.trainNumber) && _trainNumberInput != null)
                _trainNumberInput.text = preset.trainNumber;

            if (!string.IsNullOrEmpty(preset.commercialCategoryId) && _categoryDropdown != null)
            {
                var cats = TimetableService.CommercialCategories;
                for (int i = 0; i < cats.Count; i++)
                {
                    if (cats[i].id == preset.commercialCategoryId)
                    {
                        _categoryDropdown.value = i;
                        break;
                    }
                }
            }

            Refresh();
        }

        /// <summary>
        /// M-TimetableUX F1.19: Reverse direction shortcut. Pre-fills creator z reversed
        /// data ze źródłowego timetable: A↔B swapped, waypoints reversed, start time =
        /// source arrival + turnaround.
        ///
        /// Po zatwierdzeniu kreatora (Confirm) auto-trigger F1.12 circulation suggestion —
        /// jeśli source + reverse mogą tworzyć obieg (5-30 min gap), gracz dostanie modal
        /// "Połączyć w obieg?".
        /// </summary>
        /// <param name="sourceTimetableId">ID rozkładu źródłowego (TR1234 → TR1234_reverse)</param>
        /// <param name="turnaroundMinutes">Default 30 min — pociąg potrzebuje czasu na
        /// reverse (zmiana kabiny EZT) + technical inspection. M-Balance tunes per CompositionMode.</param>
        public void OpenReverseTimetable(int sourceTimetableId, int turnaroundMinutes = 30)
        {
            var src = TimetableService.GetTimetable(sourceTimetableId);
            if (src == null || src.stops == null || src.stops.Count < 2)
            {
                Log.Warn($"[F1.19] OpenReverseTimetable: invalid source TR#{sourceTimetableId}");
                return;
            }

            var srcStartStop = src.stops[0];
            var srcEndStop = src.stops[src.stops.Count - 1];
            int srcEndTimeAbsMin = src.frequency.firstRunMinutesFromMidnight + (srcEndStop.plannedArrivalSec / 60);
            int reverseStartMin = (srcEndTimeAbsMin + turnaroundMinutes) % (24 * 60);

            // Build reversed waypoints — pomijamy first/last (są jako start/end), reversed kolejność.
            var reversedWaypoints = new List<string>();
            for (int i = src.stops.Count - 2; i >= 1; i--)
            {
                if (src.stops[i].stopType == StopType.PH)
                    reversedWaypoints.Add(src.stops[i].stationName);
            }

            var preset = new CreatorPreset
            {
                startStationName = srcEndStop.stationName,
                endStationName = srcStartStop.stationName,
                waypointStationNames = reversedWaypoints,
                commercialCategoryId = src.commercialCategoryId,
                compositionMode = src.composition?.mode,
                maxSpeedKmh = src.composition?.maxSpeedKmh,
                startMinutesFromMidnight = reverseStartMin,
                weeksValid = src.weeksValid > 0 ? src.weeksValid : (int?)null,
                presetReason = $"F1.19 reverse direction: TR#{sourceTimetableId} ({srcStartStop.stationName} → {srcEndStop.stationName})",
                onConfirmed = (newTt) =>
                {
                    // M-TimetableUX F1.19 auto-trigger F1.12: nowy reverse + source = potencjalny obieg
                    Log.Info($"[F1.19] Reverse timetable created: TR#{newTt.id}. Triggering F1.12 circulation suggestion...");
                    var suggestions = RailwayManager.Timetable.Suggestions.CirculationSuggestionService.GenerateSuggestions(newTt.id);
                    if (suggestions.Count > 0)
                        Log.Info($"[F1.19] {suggestions.Count} circulation suggestion(s) generated — UI modal w F1.16 polish.");
                }
            };

            OpenWithPreset(preset);
        }

        private static RailwayStation ResolveStation(TimetableInitializer init, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || init?.Stations == null) return null;
            foreach (var s in init.Stations)
                if (s != null && string.Equals(s.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return s;
            Log.Warn($"[TimetableCreator] OpenWithPreset: nie znaleziono stacji '{name}'");
            return null;
        }

        private void OnStartInputChanged(string text)
        {
            ShowSuggestions(text, _startSuggestions, true);
        }

        private void OnEndInputChanged(string text)
        {
            ShowSuggestions(text, _endSuggestions, false);
        }

        private void ShowSuggestions(string query, Transform container, bool isStart)
        {
            if (container == null)
            {
                Log.Warn("[TimetableCreator] Suggestions container is null!");
                return;
            }
            foreach (Transform ch in container) Destroy(ch.gameObject);

            if (string.IsNullOrEmpty(query) || query.Length < 2)
            {
                container.gameObject.SetActive(false);
                return;
            }

            var init = TimetableInitializer.Instance;
            if (init == null || init.Stations == null)
            {
                Log.Warn($"[TimetableCreator] Init={init != null}, Stations={init?.Stations?.Count ?? -1}");
                return;
            }

            var q = query.ToLowerInvariant();
            int count = 0;
            foreach (var s in init.Stations)
            {
                if (s.name == null || !s.name.ToLowerInvariant().Contains(q)) continue;
                // M-TimetableUX 2026-05-11: filter halty bez track_ref (mieszają w stops list).
                if (IsHaltWithoutTrackRef(s, init)) continue;
                if (count >= 6) break;

                var captured = s;
                var isStartCapt = isStart;
                var row = new GameObject(s.name);
                row.transform.SetParent(container, false);
                row.AddComponent<LayoutElement>().preferredHeight = 22;
                var bg = row.AddComponent<Image>();
                UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Inset);
                var btn = row.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.colors = UITheme.CreateColorBlock(
                    UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f),
                    UITheme.WithAlpha(UITheme.RaisedSurface, 0.92f),
                    UITheme.WithAlpha(UITheme.Border, 0.92f),
                    UITheme.WithAlpha(UITheme.RaisedSurface, 0.92f),
                    UITheme.WithAlpha(UITheme.Border, 0.45f));
                btn.onClick.AddListener(() => SelectSuggestion(captured, isStartCapt));

                var txt = new GameObject("T");
                txt.transform.SetParent(row.transform, false);
                var trt = txt.AddComponent<RectTransform>();
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(6, 0);
                trt.offsetMax = Vector2.zero;
                var tx = txt.AddComponent<TextMeshProUGUI>();
                tx.fontSize = 11;
                tx.alignment = TextAlignmentOptions.MidlineLeft;
                string typeLabel = LocalizationService.Get(s.isMajorStation
                    ? "timetable.creator.route.suggestion.station"
                    : "timetable.creator.route.suggestion.halt");
                tx.text = $"{s.name} ({typeLabel})";
                UITheme.ApplyTmpText(tx, UIThemeTextRole.Primary);

                count++;
            }

            container.gameObject.SetActive(count > 0);
        }

        private void SelectSuggestion(RailwayStation station, bool isStart)
        {
            if (isStart)
            {
                _startStation = station;
                _startTrack = ""; // reset track override przy zmianie stacji
                if (_startInput != null) _startInput.text = station.name;
                HideSuggestions(_startSuggestions);
                RefreshEndpointTrackDropdown(true);
            }
            else
            {
                _endStation = station;
                _endTrack = "";
                if (_endInput != null) _endInput.text = station.name;
                HideSuggestions(_endSuggestions);
                RefreshEndpointTrackDropdown(false);
            }
            Refresh();
            ScheduleBackgroundAlternativesGeneration();
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11: debounced background trigger dla K-shortest paths.
        /// Wywoływany po każdej zmianie A/B/Via/track. Cancel previous + start new coroutine.
        /// 500ms debounce — user może wpisywać dalsze rzeczy bez wywoływania kilku pathfinderów.
        /// </summary>
        private void ScheduleBackgroundAlternativesGeneration()
        {
            if (_backgroundGenerationCoroutine != null)
            {
                StopCoroutine(_backgroundGenerationCoroutine);
                _backgroundGenerationCoroutine = null;
            }
            if (_startStation == null || _endStation == null) return;
            _backgroundGenerationCoroutine = StartCoroutine(BackgroundGenerateAlternativesCoroutine());
        }

        private System.Collections.IEnumerator BackgroundGenerateAlternativesCoroutine()
        {
            // Debounce 500ms
            yield return new WaitForSeconds(0.5f);

            var init = TimetableInitializer.Instance;
            if (init == null || !init.IsReady || _startStation == null || _endStation == null)
            {
                _alternativeRoutes.Clear();
                RefreshAlternativesUI();
                _backgroundGenerationCoroutine = null;
                yield break;
            }

            // Run synchronously dla teraz (pathfinder jest fast po init-state v3, ~50-200ms typowo).
            // Future: Task.Run dla rzeczywistego threading jeśli okaże się slow.
            int startNode = (_startStation.pathNodeId >= 0 && !string.IsNullOrEmpty(_startTrack))
                ? init.Graph.FindNodeOnTrack(_startStation.pathNodeId, _startTrack)
                : _startStation.pathNodeId;
            if (startNode < 0) startNode = _startStation.pathNodeId;
            int endNode = (_endStation.pathNodeId >= 0 && !string.IsNullOrEmpty(_endTrack))
                ? init.Graph.FindNodeOnTrack(_endStation.pathNodeId, _endTrack)
                : _endStation.pathNodeId;
            if (endNode < 0) endNode = _endStation.pathNodeId;

            float t0 = Time.realtimeSinceStartup;
            _alternativeRoutes = RailwayPathfinder.FindKShortestPaths(
                init.Graph, startNode, endNode, _alternativesK, _alternativesMaxRatio);
            float dt = Time.realtimeSinceStartup - t0;
            Log.Info($"[Alternatives] FindKShortestPaths: {_alternativeRoutes.Count} paths in {dt * 1000f:F0}ms "
                   + $"(K={_alternativesK}, maxRatio={_alternativesMaxRatio:F2})");

            _selectedAlternativeIdx = 0;
            RefreshAlternativesUI();
            _backgroundGenerationCoroutine = null;
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11: rebuild UI panel alternatyw — buttony "lk9+lk204 (153km)".
        /// Klik na button ustawia _selectedAlternativeIdx + (TODO) BuildRoute z tą trasą.
        /// </summary>
        private void RefreshAlternativesUI()
        {
            if (_routeAlternativesContainer == null) return;
            foreach (Transform ch in _routeAlternativesContainer) Destroy(ch.gameObject);

            if (_alternativeRoutes == null || _alternativeRoutes.Count == 0) return;

            for (int i = 0; i < _alternativeRoutes.Count; i++)
            {
                int idx = i;
                var path = _alternativeRoutes[i];
                string lines = ExtractLinesShortFormat(path);
                float km = path.totalLengthM / 1000f;
                bool selected = (idx == _selectedAlternativeIdx);
                string prefix = selected ? "✓ " : "   ";
                string label = $"{prefix}{lines}  ({km:F1} km)";

                var row = Row(_routeAlternativesContainer);
                StyleRow(row);
                var btn = row.GetComponent<Button>();
                if (btn == null) btn = row.AddComponent<Button>();
                btn.onClick.AddListener(() => SelectAlternative(idx));

                Lbl(row.transform, label, 11, selected ? new Color(0.6f, 1f, 0.6f) : new Color(0.7f, 0.85f, 1f));
            }
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11 v2: ekstraktuj line refs w **kolejności jazdy** (chronologicznie).
        /// Per linia: total distance + % udziału. Format:
        /// "lk204 (35km/22%) → lk9 (118km/78%)"
        /// </summary>
        private string ExtractLinesShortFormat(RailwayPathfinder.PathResult path)
        {
            var init = TimetableInitializer.Instance;
            if (init?.Graph == null || path.edgeIds == null) return "(brak danych)";
            var graph = init.Graph;

            // dist[ref] = sumaryczna długość; firstAppearance[ref] = pierwszy edgeIdx gdzie ref pojawił się
            var dist = new Dictionary<string, float>();
            var firstAppearance = new Dictionary<string, int>();
            float total = 0f;

            for (int i = 0; i < path.edgeIds.Count; i++)
            {
                int eid = path.edgeIds[i];
                if (eid < 0 || eid >= graph.EdgeCount) continue;
                var edge = graph.GetEdge(eid);
                if (edge.metadata == null) continue;
                string refTag = null;
                if (edge.metadata.TryGetValue("railway:line_ref", out var lr) && !string.IsNullOrEmpty(lr))
                    refTag = lr;
                else if (edge.metadata.TryGetValue("ref", out var sr) && !string.IsNullOrEmpty(sr))
                    refTag = sr;
                if (refTag == null) continue;
                total += edge.lengthM;
                foreach (var r in refTag.Split(';'))
                {
                    var trimmed = r.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    if (!IsValidLineRefStatic(trimmed)) continue;
                    if (!dist.TryGetValue(trimmed, out var d)) d = 0f;
                    dist[trimmed] = d + edge.lengthM;
                    if (!firstAppearance.ContainsKey(trimmed))
                        firstAppearance[trimmed] = i;
                }
            }
            if (dist.Count == 0 || total < 0.1f) return "(brak ref)";

            // Filter ≥5% udział (filtruje krótkie fragmenty na rozjazdach)
            var significant = new List<string>();
            foreach (var kv in dist)
                if (kv.Value / total >= 0.05f) significant.Add(kv.Key);

            if (significant.Count == 0)
            {
                // Fallback: top 3 by distance
                var tmp = new List<KeyValuePair<string, float>>(dist);
                tmp.Sort((a, b) => b.Value.CompareTo(a.Value));
                for (int i = 0; i < System.Math.Min(3, tmp.Count); i++) significant.Add(tmp[i].Key);
            }

            // Sortuj by first appearance (chronologicznie wzdłuż trasy)
            significant.Sort((a, b) => firstAppearance[a].CompareTo(firstAppearance[b]));

            var parts = new List<string>();
            foreach (var r in significant)
            {
                float d = dist[r];
                int km = Mathf.RoundToInt(d / 1000f);
                int pct = Mathf.RoundToInt(d / total * 100f);
                parts.Add($"lk{r} ({km}km/{pct}%)");
            }
            return string.Join(" → ", parts);
        }

        /// <summary>
        /// Akceptujemy numeric refs (max 4 znaki — polskie linie 1-9999). Filter dłuższych
        /// ('151111') i pure-letter ('R'). Numeric + literowy suffix też OK ('9a', '204X').
        /// </summary>
        private static bool IsValidLineRefStatic(string r)
        {
            if (string.IsNullOrEmpty(r) || r.Length > 4) return false;
            bool hasDigit = false;
            foreach (char c in r)
            {
                if (char.IsDigit(c)) hasDigit = true;
                else if (!char.IsLetter(c)) return false;
            }
            return hasDigit;
        }

        private void SelectAlternative(int idx)
        {
            if (idx < 0 || idx >= _alternativeRoutes.Count) return;
            _selectedAlternativeIdx = idx;
            RefreshAlternativesUI();
            Log.Info($"[Alternatives] Selected idx={idx} length={_alternativeRoutes[idx].totalLengthM / 1000f:F1}km");
            // TODO: aktualizować _currentRoute z wybraną alternatywą + BuildStops
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11: refresh dropdown toru dla A (start=true) lub C (start=false).
        /// Opcje: "(auto)" + lista trackRef z init.TrackData. Reuse logic z BuildWaypointTrackDropdown.
        /// </summary>
        private void RefreshEndpointTrackDropdown(bool isStart)
        {
            var dd = isStart ? _startTrackDropdown : _endTrackDropdown;
            if (dd == null) return;
            var station = isStart ? _startStation : _endStation;
            string currentTrack = isStart ? _startTrack : _endTrack;

            var options = new List<TMP_Dropdown.OptionData>();
            options.Add(new TMP_Dropdown.OptionData("(auto)"));
            var trackRefByIdx = new List<string> { "" };
            int selectedIdx = 0;

            if (station != null)
            {
                var init = TimetableInitializer.Instance;
                if (init?.TrackData != null && init.TrackData.IsLoaded)
                {
                    var tracks = init.TrackData.GetTracks(station.name);
                    if (tracks != null)
                    {
                        tracks.Sort((a, b) =>
                        {
                            bool aNum = int.TryParse(a.trackRef, out int ai);
                            bool bNum = int.TryParse(b.trackRef, out int bi);
                            if (aNum && bNum) return ai.CompareTo(bi);
                            if (aNum) return -1;
                            if (bNum) return 1;
                            return string.Compare(a.trackRef, b.trackRef, System.StringComparison.OrdinalIgnoreCase);
                        });
                        for (int i = 0; i < tracks.Count; i++)
                        {
                            var t = tracks[i];
                            string label = t.hasPlatform ? $"Tor {t.trackRef}" : $"Tor {t.trackRef} (bez peronu)";
                            options.Add(new TMP_Dropdown.OptionData(label));
                            trackRefByIdx.Add(t.trackRef);
                            if (t.trackRef == currentTrack) selectedIdx = i + 1;
                        }
                    }
                }
            }

            dd.ClearOptions();
            dd.AddOptions(options);
            dd.value = selectedIdx;
            dd.RefreshShownValue();

            // Re-bind onValueChanged żeby capture aktualny trackRefByIdx (handler closure).
            dd.onValueChanged.RemoveAllListeners();
            if (isStart)
                dd.onValueChanged.AddListener(val => OnStartTrackChangedFromList(val, trackRefByIdx));
            else
                dd.onValueChanged.AddListener(val => OnEndTrackChangedFromList(val, trackRefByIdx));
        }

        // Stub handlers — actual logic w FromList variants (po refresh z proper trackRefByIdx mapping).
        private void OnStartTrackChanged(int idx) { /* re-bound via RefreshEndpointTrackDropdown */ }
        private void OnEndTrackChanged(int idx) { /* re-bound via RefreshEndpointTrackDropdown */ }

        private void OnStartTrackChangedFromList(int idx, List<string> trackRefByIdx)
        {
            if (idx < 0 || idx >= trackRefByIdx.Count) return;
            _startTrack = trackRefByIdx[idx];
            Log.Info($"[Track A] Set to '{_startTrack}'");
        }
        private void OnEndTrackChangedFromList(int idx, List<string> trackRefByIdx)
        {
            if (idx < 0 || idx >= trackRefByIdx.Count) return;
            _endTrack = trackRefByIdx[idx];
            Log.Info($"[Track C] Set to '{_endTrack}'");
        }

        private void HideSuggestions(Transform container)
        {
            if (container != null) container.gameObject.SetActive(false);
        }

        private void PickOnMap(bool isStart)
        {
            // RMP-H: wybór na MINI-MAPIE w kreatorze zamiast przełączania na pełną MapScene.
            _pickingStart = isStart;
            _pickingWaypointIndex = -1;
            ArmMiniPick();
        }

        private void AddWaypoint()
        {
            _waypoints.Add(null);
            _waypointTracks.Add(""); // (auto)
            RefreshWaypointsUI();
        }

        private void RemoveWaypoint(int index)
        {
            if (index >= 0 && index < _waypoints.Count)
            {
                _waypoints.RemoveAt(index);
                if (index < _waypointTracks.Count) _waypointTracks.RemoveAt(index);
                RefreshWaypointsUI();
                Refresh();
            }
        }

        /// <summary>
        /// M-TimetableUX F1.3: Reorder waypoint w sekwencji ↑/↓.
        /// Direction: -1 = up (earlier), +1 = down (later). No-op gdy edge case.
        /// </summary>
        private void MoveWaypoint(int index, int direction)
        {
            int newIdx = index + direction;
            if (index < 0 || index >= _waypoints.Count) return;
            if (newIdx < 0 || newIdx >= _waypoints.Count) return;

            var temp = _waypoints[index];
            _waypoints[index] = _waypoints[newIdx];
            _waypoints[newIdx] = temp;
            // Sync track list — swap parallel too żeby tor był sticky do waypointa.
            if (index < _waypointTracks.Count && newIdx < _waypointTracks.Count)
            {
                var tt = _waypointTracks[index];
                _waypointTracks[index] = _waypointTracks[newIdx];
                _waypointTracks[newIdx] = tt;
            }
            RefreshWaypointsUI();
            Refresh();
        }

        private void SetWaypoint(int index, RailwayStation station)
        {
            if (index >= 0 && index < _waypoints.Count)
            {
                _waypoints[index] = station;
                // M-TimetableUX 2026-05-11: RefreshWaypointsUI żeby InputField pokazał pełną
                // nazwę po autocomplete pick (był bug: user wpisał "Gdy", klika "Gdynia Główna",
                // text pozostawał "Gdy").
                RefreshWaypointsUI();
                Refresh();
            }
        }

        private void PickWaypointOnMap(int index)
        {
            _pickingWaypointIndex = index;
            _pickingStart = false;
            ArmMiniPick();
        }

        /// <summary>
        /// M-TimetableUX F1.3 polish: append new waypoint przez click na stację na mapie.
        /// Sentinel _pickingWaypointIndex = -2 oznacza "append new" w ShowAfterPicking flow.
        /// </summary>
        private void PickNewWaypointOnMap()
        {
            _pickingWaypointIndex = -2; // sentinel: append new waypoint
            _pickingStart = false;
            ArmMiniPick();
        }

        private void RefreshWaypointsUI()
        {
            if (_waypointsContainer == null) return;
            foreach (Transform ch in _waypointsContainer) Destroy(ch.gameObject);
            _waypointSuggestions.Clear();

            for (int i = 0; i < _waypoints.Count; i++)
            {
                int idx = i;
                var wp = _waypoints[i];

                var row = Row(_waypointsContainer);
                StyleRow(row);
                Lbl(
                    row.transform,
                    string.Format(LocalizationService.Get("timetable.creator.route.label_via_format"), i + 1),
                    12,
                    new Color(0.7f, 0.7f, 0.7f));

                var wpInput = Inp(
                    row.transform,
                    wp?.name ?? "",
                    140,
                    LocalizationService.Get("timetable.creator.route.waypoint_placeholder"));

                // M-TimetableUX 2026-05-11: track dropdown per waypoint.
                // "(auto)" + tory stacji z init.TrackData. Wybór tor → force-route przez ten tor
                // (BuildRoute użyje FindNodeOnTrack zamiast ResolveTrunkNode).
                BuildWaypointTrackDropdown(row.transform, idx, wp);

                // M-TimetableUX F1.3: reorder ↑/↓ buttons. Disabled state via gray color
                // gdy at edge (first → no ↑, last → no ↓). Krótki label żeby zmieścić w row.
                bool canUp = idx > 0;
                bool canDown = idx < _waypoints.Count - 1;
                Btn(
                    row.transform,
                    "↑",
                    () => { if (canUp) MoveWaypoint(idx, -1); },
                    canUp ? new Color(0.4f, 0.5f, 0.6f) : new Color(0.3f, 0.3f, 0.3f),
                    24);
                Btn(
                    row.transform,
                    "↓",
                    () => { if (canDown) MoveWaypoint(idx, +1); },
                    canDown ? new Color(0.4f, 0.5f, 0.6f) : new Color(0.3f, 0.3f, 0.3f),
                    24);

                Btn(
                    row.transform,
                    LocalizationService.Get("timetable.creator.button.pick_on_map"),
                    () => PickWaypointOnMap(idx),
                    new Color(0.25f, 0.5f, 0.8f),
                    70);
                Btn(
                    row.transform,
                    LocalizationService.Get("timetable.creator.button.remove_waypoint"),
                    () => RemoveWaypoint(idx),
                    new Color(0.6f, 0.2f, 0.2f),
                    28);

                var sugContainer = MakeSuggestionsContainer(_waypointsContainer);
                _waypointSuggestions.Add(sugContainer);

                int sugIdx = _waypointSuggestions.Count - 1;
                wpInput.onValueChanged.AddListener(text =>
                    ShowWaypointSuggestions(text, sugIdx, idx));
            }

            // M-TimetableUX F1.3 polish: dwa buttony append flow.
            // "+ Wpisz nazwę" → AddWaypoint() (pusty row z InputField + autocomplete dropdown).
            // "+ Wybierz na mapie" → PickNewWaypointOnMap (sentinel -2 → state machine click flow).
            var appendRow = Row(_waypointsContainer);
            StyleRow(appendRow);
            Btn(
                appendRow.transform,
                "+ Wpisz nazwę",
                AddWaypoint,
                new Color(0.3f, 0.5f, 0.6f),
                140);
            Btn(
                appendRow.transform,
                "+ Wybierz na mapie",
                PickNewWaypointOnMap,
                new Color(0.3f, 0.6f, 0.4f),
                160);
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11: track dropdown w wierszu waypointa.
        /// Opcje: "(auto)" + lista trackRef z `init.TrackData.GetTracks(stationName)`.
        /// Selected trackRef zapisany w `_waypointTracks[idx]`. Pusty string = "(auto)" =
        /// pathfinder używa ResolveTrunkNode. Specyficzny tor = force pathfinder przez
        /// FindNodeOnTrack(station, trackRef).
        ///
        /// Gdy `wp == null` (pusty waypoint świeżo dodany), dropdown ma tylko "(auto)".
        /// Po wpisaniu nazwy + autocomplete pick, RefreshWaypointsUI re-creates row z pełną listą.
        /// </summary>
        private void BuildWaypointTrackDropdown(Transform rowParent, int waypointIdx, RailwayStation wp)
        {
            var dd = MakeDropdown(rowParent);
            dd.GetComponent<LayoutElement>().preferredWidth = 90;
            dd.GetComponent<LayoutElement>().flexibleWidth = 0;

            var options = new List<TMP_Dropdown.OptionData>();
            options.Add(new TMP_Dropdown.OptionData("(auto)"));

            int selectedIdx = 0;
            string selectedTrackRef = (waypointIdx < _waypointTracks.Count)
                ? _waypointTracks[waypointIdx]
                : "";

            if (wp != null)
            {
                var init = TimetableInitializer.Instance;
                if (init?.TrackData != null && init.TrackData.IsLoaded)
                {
                    var tracks = init.TrackData.GetTracks(wp.name);
                    if (tracks != null)
                    {
                        // Sortuj numerycznie jeśli możliwe (1, 2, 3, ..., 1a, 2a) — najpierw int parse, potem string.
                        tracks.Sort((a, b) =>
                        {
                            bool aNum = int.TryParse(a.trackRef, out int ai);
                            bool bNum = int.TryParse(b.trackRef, out int bi);
                            if (aNum && bNum) return ai.CompareTo(bi);
                            if (aNum) return -1;
                            if (bNum) return 1;
                            return string.Compare(a.trackRef, b.trackRef, System.StringComparison.OrdinalIgnoreCase);
                        });
                        for (int i = 0; i < tracks.Count; i++)
                        {
                            var t = tracks[i];
                            string label = t.hasPlatform ? $"Tor {t.trackRef}" : $"Tor {t.trackRef} (bez peronu)";
                            options.Add(new TMP_Dropdown.OptionData(label));
                            if (t.trackRef == selectedTrackRef) selectedIdx = i + 1;
                        }
                    }
                }
            }

            dd.ClearOptions();
            dd.AddOptions(options);
            dd.value = selectedIdx;
            dd.RefreshShownValue();

            int captured = waypointIdx;
            // Rebuild trackRef lookup table dla onValueChanged closure (options 0 = auto, 1+ = trackRef).
            var trackRefByIdx = new List<string> { "" };
            if (wp != null)
            {
                var init = TimetableInitializer.Instance;
                if (init?.TrackData != null && init.TrackData.IsLoaded)
                {
                    var tracks = init.TrackData.GetTracks(wp.name);
                    if (tracks != null)
                    {
                        tracks.Sort((a, b) =>
                        {
                            bool aNum = int.TryParse(a.trackRef, out int ai);
                            bool bNum = int.TryParse(b.trackRef, out int bi);
                            if (aNum && bNum) return ai.CompareTo(bi);
                            if (aNum) return -1;
                            if (bNum) return 1;
                            return string.Compare(a.trackRef, b.trackRef, System.StringComparison.OrdinalIgnoreCase);
                        });
                        foreach (var t in tracks) trackRefByIdx.Add(t.trackRef);
                    }
                }
            }

            dd.onValueChanged.AddListener(val =>
            {
                if (captured < 0 || captured >= _waypointTracks.Count) return;
                if (val < 0 || val >= trackRefByIdx.Count) return;
                _waypointTracks[captured] = trackRefByIdx[val];
                Log.Info($"[Waypoint] Track set idx={captured} → '{trackRefByIdx[val]}' (was '{selectedTrackRef}')");
            });
        }

        private void ShowWaypointSuggestions(string query, int sugContainerIdx, int waypointIdx)
        {
            if (sugContainerIdx < 0 || sugContainerIdx >= _waypointSuggestions.Count) return;
            var container = _waypointSuggestions[sugContainerIdx];
            if (container == null) return;

            foreach (Transform ch in container) Destroy(ch.gameObject);

            if (string.IsNullOrEmpty(query) || query.Length < 2)
            {
                container.gameObject.SetActive(false);
                return;
            }

            var init = TimetableInitializer.Instance;
            if (init == null || init.Stations == null) return;

            var q = query.ToLowerInvariant();
            int count = 0;
            foreach (var s in init.Stations)
            {
                if (s.name == null || !s.name.ToLowerInvariant().Contains(q)) continue;
                // M-TimetableUX 2026-05-11: filter halty bez track_ref (mieszają w stops list).
                if (IsHaltWithoutTrackRef(s, init)) continue;
                if (count >= 6) break;

                var captured = s;
                int wpIdx = waypointIdx;
                var row = new GameObject(s.name);
                row.transform.SetParent(container, false);
                row.AddComponent<LayoutElement>().preferredHeight = 22;
                var bg = row.AddComponent<Image>();
                UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Inset);
                var btn = row.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.colors = UITheme.CreateColorBlock(
                    UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f),
                    UITheme.WithAlpha(UITheme.RaisedSurface, 0.92f),
                    UITheme.WithAlpha(UITheme.Border, 0.92f),
                    UITheme.WithAlpha(UITheme.RaisedSurface, 0.92f),
                    UITheme.WithAlpha(UITheme.Border, 0.45f));
                btn.onClick.AddListener(() =>
                {
                    SetWaypoint(wpIdx, captured);
                    container.gameObject.SetActive(false);
                });

                var txt = new GameObject("T");
                txt.transform.SetParent(row.transform, false);
                var trt = txt.AddComponent<RectTransform>();
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(6, 0);
                trt.offsetMax = Vector2.zero;
                var tx = txt.AddComponent<TextMeshProUGUI>();
                tx.fontSize = 11;
                tx.alignment = TextAlignmentOptions.MidlineLeft;
                string typeLabel = LocalizationService.Get(s.isMajorStation
                    ? "timetable.creator.route.suggestion.station"
                    : "timetable.creator.route.suggestion.halt");
                tx.text = $"{s.name} ({typeLabel})";
                UITheme.ApplyTmpText(tx, UIThemeTextRole.Primary);
                count++;
            }

            container.gameObject.SetActive(count > 0);
        }
    }
}
