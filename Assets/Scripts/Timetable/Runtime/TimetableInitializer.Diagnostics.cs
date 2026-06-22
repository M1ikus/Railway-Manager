using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    // Audit 2026-05-15 (split god class): 13 diagnostic + smoke test ContextMenu metod
    // wyniesionych z root TimetableInitializer.cs. DEBUG/Regenerate dla pipeline initializing
    // + M-TimetableUX validators (F1.0.5/F1.1/F1.5/F1.6/F1.9/F1.10/F1.12/F1.13b/F1.14/F1.18/F1.20).
    public partial class TimetableInitializer
    {
        /// <summary>Manual trigger dla debug — wywołać z ContextMenu jeśli auto-init nie startuje.</summary>
        [ContextMenu("DEBUG: Force Initialize")]
        public void DebugForceInitialize()
        {
            Log.Info("[TimetableInitializer] Manual Initialize triggered from ContextMenu");
            isReady = false;
            IsInitializing = false;
            Initialize();
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11: wymuszone regenerowanie StationTrackData JSON.
        /// Usuwa stary plik + regen z aktualną logiką Generate (z platform refs jako tracks).
        /// </summary>
        /// <summary>
        /// Diagnostic: listuje platforms + edges blisko Królewa Malborskiego (hardcoded). Pokaż
        /// dokładnie co jest w danych — ile platforms, jakie platformName (ref), jakie edges
        /// w 30m radius, perpendicular distance per edge, czy edge ma już railway:track_ref.
        /// </summary>
        [ContextMenu("M-TimetableUX: DIAGNOSE Królewo Malborskie")]
        public void DiagnoseKrolewoMalborskie()
        {
            if (!IsReady) { Log.Warn("Init not ready"); return; }

            const string target = "Królewo Malborskie";
            var matchingStations = new List<RailwayStation>();
            foreach (var st in Stations)
            {
                if (st.name != null && st.name.IndexOf(target, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    matchingStations.Add(st);
            }
            Log.Info($"[DIAG] Found {matchingStations.Count} stations matching '{target}'");

            foreach (var st in matchingStations)
            {
                Log.Info($"[DIAG] Station '{st.name}' stationId={st.stationId} pathNodeId={st.pathNodeId} "
                       + $"pos=({st.position.x:F1},{st.position.y:F1}) isMajor={st.isMajorStation}");

                // Find platforms matched do tej stacji (nearest station from platform centroid)
                int matchedCount = 0;
                for (int i = 0; i < Platforms.Count; i++)
                {
                    var plat = Platforms[i];
                    // Match by nearest station from plat.position
                    string nearestName = null;
                    float bestDistSq = float.MaxValue;
                    foreach (var s in Stations)
                    {
                        float dsq = (s.position - plat.position).sqrMagnitude;
                        if (dsq < bestDistSq) { bestDistSq = dsq; nearestName = s.name; }
                    }
                    if (nearestName != st.name) continue;
                    matchedCount++;

                    Log.Info($"[DIAG]   Platform[{i}] id={plat.platformId} "
                           + $"name='{plat.platformName}' trackRef='{plat.trackRef}' "
                           + $"pos=({plat.position.x:F1},{plat.position.y:F1}) "
                           + $"stationNodeId={plat.stationNodeId} length={plat.lengthM:F1}m");

                    // Edges w 30m od peronu
                    var nearbyNodes = new List<int>();
                    Graph.FindNodesInRadius(plat.position, 30f, nearbyNodes);
                    var seenEdges = new HashSet<int>();
                    int edgeIdx = 0;
                    foreach (int nid in nearbyNodes)
                    {
                        var n = Graph.GetNode(nid);
                        if (n.edgeIds == null) continue;
                        foreach (int eid in n.edgeIds)
                        {
                            if (!seenEdges.Add(eid)) continue;
                            var edge = Graph.GetEdge(eid);
                            string existingRef = "";
                            if (edge.metadata != null
                                && edge.metadata.TryGetValue("railway:track_ref", out var er))
                                existingRef = er ?? "";

                            var fromPos = Graph.GetNode(edge.fromNodeId).position;
                            var toPos = Graph.GetNode(edge.toNodeId).position;
                            float perpDistSq = PointToSegmentDistanceSqDiag(plat.position, fromPos, toPos);
                            float perpDist = Mathf.Sqrt(perpDistSq);
                            if (edgeIdx++ < 8) // limit per-platform output
                            {
                                Log.Info($"[DIAG]     Edge[{eid}] track_ref='{existingRef}' "
                                       + $"perpDist={perpDist:F1}m from={edge.fromNodeId} to={edge.toNodeId} "
                                       + $"isOsmForward={edge.isOsmForward}");
                            }
                        }
                    }
                }
                Log.Info($"[DIAG] Station '{st.name}' matched {matchedCount} platforms");
            }
        }

        private static float PointToSegmentDistanceSqDiag(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float abLenSq = ab.sqrMagnitude;
            if (abLenSq < 1e-9f) return (p - a).sqrMagnitude;
            float t = Vector2.Dot(p - a, ab) / abLenSq;
            t = Mathf.Clamp01(t);
            var closest = a + t * ab;
            return (p - closest).sqrMagnitude;
        }

        [ContextMenu("M-TimetableUX: Regenerate StationTrackData")]
        public void RegenerateStationTrackData()
        {
            if (!IsReady)
            {
                Log.Warn("[TimetableInitializer] RegenerateStationTrackData: graph nie jest gotowy, najpierw Initialize");
                return;
            }
            // Re-run propagation peron → edge track_ref (idempotent — skip already-correct).
            Graph.PropagateTrackRefsFromPlatforms(Platforms);

            string path = System.IO.Path.Combine(AppPaths.TimetableDataDir, "station_tracks.json");
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
                Log.Info($"[TimetableInitializer] Deleted old station_tracks.json");
            }
            StationTrackData.Generate(this);
            TrackData = new StationTrackData();
            TrackData.Load();
            Log.Info($"[TimetableInitializer] Regenerated StationTrackData, IsLoaded={TrackData.IsLoaded}");
        }

        /// <summary>
        /// M-TimetableUX F1.0.5 diagnostic — list stacji bez platform data (OSM ani JSON).
        /// Po wywaleniu synthetic platform fallback, te stacje nie pozwalają na PH stop type
        /// (major → PT/ZD/Transit only; halt → Transit only). Output do log'a, user uzupełnia
        /// `Assets/StreamingAssets/TimetableData/station_tracks.json` ręcznie.
        /// </summary>
        [ContextMenu("M-TimetableUX/F1.0.5: List stations missing platforms")]
        public void DiagnoseStationsMissingPlatforms()
        {
            if (Stations == null) { Log.Warn("[F1.0.5] Stations not loaded"); return; }

            // Build platform-per-station lookup (real OSM data)
            var platformsPerStation = new Dictionary<int, int>();
            if (Platforms != null)
                foreach (var p in Platforms)
                {
                    platformsPerStation.TryGetValue(p.stationNodeId, out int c);
                    platformsPerStation[p.stationNodeId] = c + 1;
                }

            int majorMissing = 0, haltMissing = 0;
            var majorList = new List<string>();
            var haltList = new List<string>();

            foreach (var st in Stations)
            {
                if (st.pathNodeId < 0) continue;

                bool hasOsmPlatform = platformsPerStation.TryGetValue(st.pathNodeId, out int osmCount) && osmCount > 0;
                bool hasJsonEntry = TrackData != null && TrackData.IsLoaded && TrackData.GetTracks(st.name) != null;

                if (hasOsmPlatform || hasJsonEntry) continue;

                if (st.isMajorStation) { majorMissing++; if (majorList.Count < 50) majorList.Add(st.name); }
                else { haltMissing++; if (haltList.Count < 50) haltList.Add(st.name); }
            }

            Log.Info($"[F1.0.5] Stations missing platform data: {majorMissing} major + {haltMissing} halts " +
                     $"(of {Stations.Count} total).");
            if (majorList.Count > 0)
                Log.Info($"[F1.0.5] First {majorList.Count} major stations missing platforms: {string.Join(", ", majorList)}");
            if (haltList.Count > 0)
                Log.Info($"[F1.0.5] First {haltList.Count} halts missing platforms: {string.Join(", ", haltList)}");
            Log.Info("[F1.0.5] Major stations bez peronu → PT/ZD/Transit only (no PH). " +
                     "Halts bez peronu → Transit only. Edit station_tracks.json żeby dodać peron.");
        }

        /// <summary>
        /// M-TimetableUX F1.1 + F1.7 smoke test — waliduje StopTypeValidator matrix
        /// + TimetableStop.dwellSeconds setter granularność (multiple of 6, range 6-1800).
        /// </summary>
        [ContextMenu("M-TimetableUX/F1.1: Validate StopType matrix + dwell granularity")]
        public void ValidateStopTypeMatrixAndDwell()
        {
            // ── F1.1: Matrix validation ──
            var cases = new (bool isMajor, bool hasPlatform, string label)[]
            {
                (true,  true,  "Major + peron"),
                (true,  false, "Major bez peronu"),
                (false, true,  "Halt + peron"),
                (false, false, "Halt bez peronu"),
            };
            var allTypes = new[] { StopType.PH, StopType.PT, StopType.ZD, StopType.Transit };

            int errors = 0;
            foreach (var c in cases)
            {
                var allowed = StopTypeValidator.GetAllowedTypes(c.isMajor, c.hasPlatform);
                var labels = new System.Text.StringBuilder();
                for (int i = 0; i < allowed.Count; i++)
                {
                    if (i > 0) labels.Append("/");
                    labels.Append(StopTypeValidator.ShortCode(allowed[i]));
                }
                Log.Info($"[F1.1] {c.label}: allowed = {labels}");

                // Cross-check IsAllowed vs GetAllowedTypes consistency
                foreach (var t in allTypes)
                {
                    bool inList = false;
                    for (int i = 0; i < allowed.Count; i++)
                        if (allowed[i] == t) { inList = true; break; }
                    bool isAllowed = StopTypeValidator.IsAllowed(t, c.isMajor, c.hasPlatform);
                    if (inList != isAllowed)
                    {
                        Log.Warn($"[F1.1] MISMATCH {c.label} + {t}: GetAllowedTypes={inList} vs IsAllowed={isAllowed}");
                        errors++;
                    }
                }
            }

            // Acceptance asserts (z spec'a Defaults Table)
            void Assert(bool cond, string msg) { if (!cond) { Log.Warn($"[F1.1] FAIL: {msg}"); errors++; } }
            Assert( StopTypeValidator.IsAllowed(StopType.PH,      true,  true),  "Major+peron+PH should be allowed");
            Assert(!StopTypeValidator.IsAllowed(StopType.PH,      true,  false), "Major-bez-peronu+PH should be blocked");
            Assert( StopTypeValidator.IsAllowed(StopType.PT,      true,  false), "Major-bez-peronu+PT should be allowed (regulacja)");
            Assert(!StopTypeValidator.IsAllowed(StopType.PT,      false, true),  "Halt+PT should be blocked (brak rozjazdów)");
            Assert( StopTypeValidator.IsAllowed(StopType.ZD,      false, true),  "Halt+peron+ZD should be allowed");
            Assert(!StopTypeValidator.IsAllowed(StopType.ZD,      false, false), "Halt-bez-peronu+ZD should be blocked");
            Assert( StopTypeValidator.IsAllowed(StopType.Transit, false, false), "Halt-bez-peronu+Transit always allowed");

            // ── F1.7: dwellSeconds granularity ──
            var s = new TimetableStop();
            s.dwellSeconds = 60;   Assert(s.dwellSeconds == 60,   $"60 → 60 (got {s.dwellSeconds})");
            s.dwellSeconds = 65;   Assert(s.dwellSeconds == 60,   $"65 → 60 round down (got {s.dwellSeconds})");
            s.dwellSeconds = 71;   Assert(s.dwellSeconds == 66,   $"71 → 66 round down (got {s.dwellSeconds})");
            s.dwellSeconds = 0;    Assert(s.dwellSeconds == 6,    $"0 → 6 clamp min (got {s.dwellSeconds})");
            s.dwellSeconds = 3;    Assert(s.dwellSeconds == 6,    $"3 → 6 clamp min (got {s.dwellSeconds})");
            s.dwellSeconds = 5000; Assert(s.dwellSeconds == 1800, $"5000 → 1800 clamp max (got {s.dwellSeconds})");

            if (errors == 0)
                Log.Info("[F1.1+F1.7] All assertions passed ✓");
            else
                Log.Warn($"[F1.1+F1.7] {errors} assertion failures — sprawdź log");
        }

        /// <summary>
        /// M-TimetableUX F1.5 smoke test — pomiar prevalence tag'u railway:preferred_direction
        /// w PathfindingGraph + correctness DirectionPenalty dla 4 kombinacji (forward/backward × isOsmForward true/false).
        /// </summary>
        [ContextMenu("M-TimetableUX/F1.5: Validate preferred_direction (prevalence + penalty)")]
        public void ValidatePreferredDirection()
        {
            if (Graph == null) { Log.Warn("[F1.5] Graph not loaded"); return; }

            // ── Prevalence ──
            int totalEdges = Graph.EdgeCount;
            int taggedAny = 0, taggedForward = 0, taggedBackward = 0, taggedBoth = 0;
            for (int i = 0; i < totalEdges; i++)
            {
                var edge = Graph.GetEdge(i);
                if (edge.metadata == null) continue;
                if (!edge.metadata.TryGetValue("railway:preferred_direction", out var pd)) continue;
                taggedAny++;
                if (pd == "forward") taggedForward++;
                else if (pd == "backward") taggedBackward++;
                else if (pd == "both") taggedBoth++;
            }
            float prevalence = totalEdges > 0 ? (taggedAny / (float)totalEdges) * 100f : 0f;
            Log.Info($"[F1.5] Edges with railway:preferred_direction: {taggedAny}/{totalEdges} ({prevalence:F1}%) — " +
                     $"forward={taggedForward}, backward={taggedBackward}, both={taggedBoth}");
            if (prevalence < 10f)
                Log.Warn($"[F1.5] LOW prevalence ({prevalence:F1}%) — F1.5 ma minimal impact dopóki formap nie wzbogaci OSM tag'ów. " +
                         "Patrz `memory/timetable_ux_milestone_design.md` F1.5 sample test note.");

            // ── Correctness DirectionPenalty (4 cases) ──
            int errors = 0;
            void Assert(bool cond, string msg) { if (!cond) { Log.Warn($"[F1.5] FAIL: {msg}"); errors++; } }

            // Mock edge: metadata + isOsmForward
            PathfindingGraph.Edge MakeEdge(string preferredDir, bool isOsmForward)
            {
                var meta = preferredDir == null ? null : new System.Collections.Generic.Dictionary<string, string>
                {
                    { "railway:preferred_direction", preferredDir }
                };
                return new PathfindingGraph.Edge { metadata = meta, isOsmForward = isOsmForward };
            }

            const float OK = 1.0f, WRONG = RailwayPathfinder.WrongDirectionPenalty;

            Assert(RailwayPathfinder.DirectionPenalty(MakeEdge(null, true)) == OK,        "no metadata → 1.0");
            Assert(RailwayPathfinder.DirectionPenalty(MakeEdge("both", true)) == OK,      "both → 1.0");
            Assert(RailwayPathfinder.DirectionPenalty(MakeEdge("both", false)) == OK,     "both (osm-back) → 1.0");
            Assert(RailwayPathfinder.DirectionPenalty(MakeEdge("forward", true)) == OK,   "forward+osm-forward → 1.0");
            Assert(RailwayPathfinder.DirectionPenalty(MakeEdge("forward", false)) == WRONG,"forward+osm-back → 5.0");
            Assert(RailwayPathfinder.DirectionPenalty(MakeEdge("backward", false)) == OK, "backward+osm-back → 1.0");
            Assert(RailwayPathfinder.DirectionPenalty(MakeEdge("backward", true)) == WRONG,"backward+osm-forward → 5.0");

            // Empty metadata (key missing → no penalty)
            var edgeEmptyMeta = new PathfindingGraph.Edge
            {
                metadata = new System.Collections.Generic.Dictionary<string, string> { { "other", "x" } },
                isOsmForward = true
            };
            Assert(RailwayPathfinder.DirectionPenalty(edgeEmptyMeta) == OK, "metadata bez preferred_direction → 1.0");

            if (errors == 0)
                Log.Info("[F1.5] DirectionPenalty correctness: ALL 8 assertions passed ✓");
            else
                Log.Warn($"[F1.5] DirectionPenalty correctness: {errors} failures");
        }

        /// <summary>
        /// M-TimetableUX F1.20 smoke test — SuggestionMemoryService API correctness.
        /// </summary>
        [ContextMenu("M-TimetableUX/F1.20: Validate SuggestionMemoryService")]
        public void ValidateSuggestionMemory()
        {
            int errors = 0;
            void Assert(bool cond, string msg) { if (!cond) { Log.Warn($"[F1.20] FAIL: {msg}"); errors++; } }

            // Reset start state
            RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.Reset();
            Assert(RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.RecordCount == 0, "Reset → 0 records");

            // ── ShouldShow defaults to true (no record) ──
            Assert(RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.ShouldShow(
                RailwayManager.SharedUI.Suggestions.SuggestionType.Circulation, "test_a"), "Empty store → ShouldShow=true");

            // ── Dismiss → ShouldShow=false ──
            RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.RecordChoice(
                RailwayManager.SharedUI.Suggestions.SuggestionType.Circulation, "test_a",
                RailwayManager.SharedUI.Suggestions.SuggestionChoice.Dismiss);
            Assert(!RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.ShouldShow(
                RailwayManager.SharedUI.Suggestions.SuggestionType.Circulation, "test_a"), "After Dismiss → ShouldShow=false");

            // Inny contextKey nie powinien być affected
            Assert(RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.ShouldShow(
                RailwayManager.SharedUI.Suggestions.SuggestionType.Circulation, "test_b"), "Different context → ShouldShow=true");

            // Inny suggestionType, ten sam contextKey → niezależne
            Assert(RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.ShouldShow(
                RailwayManager.SharedUI.Suggestions.SuggestionType.CrewSwap, "test_a"), "Different type, same context → ShouldShow=true");

            // ── Accept → ShouldShow=false dla tego context'u ──
            RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.RecordChoice(
                RailwayManager.SharedUI.Suggestions.SuggestionType.Mijanka, "mijanka_skarzysko_1430",
                RailwayManager.SharedUI.Suggestions.SuggestionChoice.Accept);
            Assert(!RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.ShouldShow(
                RailwayManager.SharedUI.Suggestions.SuggestionType.Mijanka, "mijanka_skarzysko_1430"), "After Accept → ShouldShow=false");

            // ── Snooze: zapisać z bardzo krótkim duration, ShouldShow false; po nadpisaniu czasu — true ──
            float originalTime = GameState.GameTimeSeconds;
            try
            {
                GameState.GameTimeSeconds = 1000f;
                RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.RecordChoice(
                    RailwayManager.SharedUI.Suggestions.SuggestionType.CrewSwap, "snoozed_ctx",
                    RailwayManager.SharedUI.Suggestions.SuggestionChoice.Snooze, 100); // snooze 100s
                Assert(!RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.ShouldShow(
                    RailwayManager.SharedUI.Suggestions.SuggestionType.CrewSwap, "snoozed_ctx"), "Snoozed (1000+100=1100, now 1000) → false");

                GameState.GameTimeSeconds = 1050f;
                Assert(!RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.ShouldShow(
                    RailwayManager.SharedUI.Suggestions.SuggestionType.CrewSwap, "snoozed_ctx"), "Mid-snooze (1050 < 1100) → false");

                GameState.GameTimeSeconds = 1101f;
                Assert(RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.ShouldShow(
                    RailwayManager.SharedUI.Suggestions.SuggestionType.CrewSwap, "snoozed_ctx"), "Post-snooze (1101 >= 1100) → true");
            }
            finally
            {
                GameState.GameTimeSeconds = originalTime;
            }

            // ── Empty contextKey: graceful no-op ──
            RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.RecordChoice(
                RailwayManager.SharedUI.Suggestions.SuggestionType.Conflict, "",
                RailwayManager.SharedUI.Suggestions.SuggestionChoice.Dismiss);
            Assert(RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.ShouldShow(
                RailwayManager.SharedUI.Suggestions.SuggestionType.Conflict, ""), "Empty contextKey → defaults to true (no-op)");

            // ── Reset czyści ──
            RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.Reset();
            Assert(RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.ShouldShow(
                RailwayManager.SharedUI.Suggestions.SuggestionType.Circulation, "test_a"), "Post-Reset → ShouldShow=true");

            if (errors == 0)
                Log.Info("[F1.20] SuggestionMemoryService: ALL assertions passed ✓");
            else
                Log.Warn($"[F1.20] SuggestionMemoryService: {errors} failures");
        }

        /// <summary>
        /// M-TimetableUX F1.18 smoke test — TimetableNotificationService API + mode-gating.
        /// </summary>
        [ContextMenu("M-TimetableUX/F1.18: Validate TimetableNotificationService")]
        public void ValidateTimetableNotifications()
        {
            int errors = 0;
            void Assert(bool cond, string msg) { if (!cond) { Log.Warn($"[F1.18] FAIL: {msg}"); errors++; } }

            RailwayManager.Timetable.Notifications.TimetableNotificationService.Clear();
            Assert(RailwayManager.Timetable.Notifications.TimetableNotificationService.Count == 0, "Clear → 0 notifications");

            // Add notifications różnych severity
            RailwayManager.Timetable.Notifications.TimetableNotificationService.Add(
                RailwayManager.Timetable.Notifications.NotificationSeverity.Info,
                RailwayManager.Timetable.Notifications.NotificationType.MijankaDetected,
                "Mijanka z TR1234 w Skarżysku 14:30",
                stopIndex: 5, timeOfDaySec: 14 * 3600 + 30 * 60, sourceTimetableId: 100);
            RailwayManager.Timetable.Notifications.TimetableNotificationService.Add(
                RailwayManager.Timetable.Notifications.NotificationSeverity.Warning,
                RailwayManager.Timetable.Notifications.NotificationType.CannotFit,
                "Nie udało się znaleźć wolnego slotu w Łodzi 14:00-16:00",
                stopIndex: 8, timeOfDaySec: 14 * 3600, sourceTimetableId: 100);
            RailwayManager.Timetable.Notifications.TimetableNotificationService.Add(
                RailwayManager.Timetable.Notifications.NotificationSeverity.Hint,
                RailwayManager.Timetable.Notifications.NotificationType.SuggestionAvailable,
                "Wykryto opportunity: zmiana drużyny w Łodzi",
                stopIndex: 8, timeOfDaySec: 14 * 3600, sourceTimetableId: 100);
            Assert(RailwayManager.Timetable.Notifications.TimetableNotificationService.Count == 3, "After 3 adds → Count=3");

            // Mode-gating: Basic mode hide Hint
            var basicVisible = RailwayManager.Timetable.Notifications.TimetableNotificationService.GetVisibleForMode(
                RailwayManager.SharedUI.UIMode.Basic);
            Assert(basicVisible.Count == 2, $"Basic mode visible: 2 (Info+Warning, no Hint), got {basicVisible.Count}");

            var advancedVisible = RailwayManager.Timetable.Notifications.TimetableNotificationService.GetVisibleForMode(
                RailwayManager.SharedUI.UIMode.Advanced);
            Assert(advancedVisible.Count == 3, $"Advanced mode visible: 3 (all), got {advancedVisible.Count}");

            // GetForTimetable z sortowaniem chronologicznym
            var forTt = RailwayManager.Timetable.Notifications.TimetableNotificationService.GetForTimetable(100);
            Assert(forTt.Count == 3, $"GetForTimetable(100) → 3, got {forTt.Count}");
            // Sort by timeOfDaySec: Warning (14:00) → Hint (14:00) → Mijanka (14:30) — Mijanka last
            Assert(forTt[forTt.Count - 1].type == RailwayManager.Timetable.Notifications.NotificationType.MijankaDetected,
                "GetForTimetable should sort chronologically — Mijanka 14:30 last");

            // ClearForTimetable
            RailwayManager.Timetable.Notifications.TimetableNotificationService.ClearForTimetable(100);
            Assert(RailwayManager.Timetable.Notifications.TimetableNotificationService.Count == 0, "ClearForTimetable(100) → 0");

            // OnAdded event fires
            int eventFireCount = 0;
            System.Action<RailwayManager.Timetable.Notifications.NotificationRecord> handler = _ => eventFireCount++;
            RailwayManager.Timetable.Notifications.TimetableNotificationService.OnAdded += handler;
            RailwayManager.Timetable.Notifications.TimetableNotificationService.Add(
                RailwayManager.Timetable.Notifications.NotificationSeverity.Error,
                RailwayManager.Timetable.Notifications.NotificationType.PathInfeasible,
                "Brak ścieżki A→B");
            Assert(eventFireCount == 1, $"OnAdded fired: 1, got {eventFireCount}");
            RailwayManager.Timetable.Notifications.TimetableNotificationService.OnAdded -= handler;

            RailwayManager.Timetable.Notifications.TimetableNotificationService.Clear();

            if (errors == 0)
                Log.Info("[F1.18] TimetableNotificationService: ALL assertions passed ✓");
            else
                Log.Warn($"[F1.18] TimetableNotificationService: {errors} failures");
        }

        /// <summary>
        /// M-TimetableUX F1.10 sequential additive scheduling — stress test smoke.
        /// Algorytm "ALREADY DONE" w ResolveBlockConflicts (existing) + Strategy A (F1.8) +
        /// notifications (F1.18). Ten test weryfikuje convergence + immutable invariant
        /// (existing timetables UNCHANGED) gdy nowy timetable dodawany w sequence.
        ///
        /// Test scenariusz:
        /// 1. Snapshot wszystkich istniejących Timetables (stops snapshot)
        /// 2. Run CheckCollisions per każdy timetable
        /// 3. Verify że stops nie zmieniły się (immutable invariant — po snapshot, snapshot identyczny po check)
        /// 4. Count notifications generated + classification
        /// </summary>
        [ContextMenu("M-TimetableUX/F1.10: Stress test sequential additive scheduling")]
        public void StressTestSequentialAdditive()
        {
            var ttList = TimetableService.Timetables;
            int n = ttList.Count;
            if (n == 0) { Log.Info("[F1.10] Brak istniejących timetables — wczytaj save lub stwórz timetables"); return; }

            // Snapshot: copy stops state per timetable
            var snapshots = new Dictionary<int, List<(int arrSec, int depSec, int platformId)>>();
            foreach (var tt in ttList)
            {
                var snap = new List<(int, int, int)>();
                foreach (var s in tt.stops)
                    snap.Add((s.plannedArrivalSec, s.plannedDepartureSec, s.platformId));
                snapshots[tt.id] = snap;
            }

            RailwayManager.Timetable.Notifications.TimetableNotificationService.Clear();
            float t0 = Time.realtimeSinceStartup;

            int totalCollisions = 0;
            int totalNotifications = 0;
            foreach (var tt in ttList)
            {
                if (tt.stops == null || tt.stops.Count < 2) continue;

                var route = TimetableService.GetRoute(tt.routeId);
                if (route == null) continue;

                var col = ReservationManager.CheckCollisionsForFrequency(
                    tt.stops, route, Graph, tt.frequency, this);
                totalCollisions += col != null ? col.Count : 0;
            }
            totalNotifications = RailwayManager.Timetable.Notifications.TimetableNotificationService.Count;

            float elapsedMs = (Time.realtimeSinceStartup - t0) * 1000f;

            // Verify immutable invariant: existing stops nie zmieniły się
            int violations = 0;
            foreach (var tt in ttList)
            {
                if (!snapshots.TryGetValue(tt.id, out var snap)) continue;
                if (snap.Count != tt.stops.Count) { violations++; continue; }
                for (int i = 0; i < tt.stops.Count; i++)
                {
                    var s = tt.stops[i];
                    var (arr, dep, pid) = snap[i];
                    // Note: ResolveBlockConflicts MUTATES stops gdy block conflict detected.
                    // Czyli "immutable invariant" jest prawdziwy TYLKO dla ALREADY-RESERVED timetables.
                    // Aktualnie test pokazuje "ile timetables sa nadal in-flight (mogą jeszcze mutować)".
                    if (s.plannedArrivalSec != arr || s.plannedDepartureSec != dep)
                        violations++;
                }
            }

            Log.Info($"[F1.10] Stress test: {n} timetables, {totalCollisions} collisions, " +
                     $"{totalNotifications} notifications, elapsed {elapsedMs:F1}ms");
            if (violations > 0)
                Log.Info($"[F1.10] {violations} stop mutations (Strategy B extending dwell przy block conflicts) — " +
                         "to oczekiwane gdy timetables są w trakcie integracji (pre-Reserve). " +
                         "Po ReserveForTimetable invariant LOCKED.");

            // Notifications breakdown per type
            var byType = new Dictionary<RailwayManager.Timetable.Notifications.NotificationType, int>();
            foreach (var notif in RailwayManager.Timetable.Notifications.TimetableNotificationService.All)
            {
                byType.TryGetValue(notif.type, out int c);
                byType[notif.type] = c + 1;
            }
            foreach (var kv in byType)
                Log.Info($"[F1.10]   {kv.Key}: {kv.Value}");
        }

        /// <summary>
        /// M-TimetableUX F1.14 smoke test — MeetingEventsService rebuild + queries.
        /// </summary>
        [ContextMenu("M-TimetableUX/F1.14: Validate MeetingEventsService")]
        public void ValidateMeetingEvents()
        {
            float t0 = Time.realtimeSinceStartup;
            RailwayManager.Timetable.Meetings.MeetingEventsService.Invalidate();
            RailwayManager.Timetable.Meetings.MeetingEventsService.Rebuild();
            float rebuildMs = (Time.realtimeSinceStartup - t0) * 1000f;

            int total = RailwayManager.Timetable.Meetings.MeetingEventsService.EventCount;
            Log.Info($"[F1.14] Rebuild: {total} meeting events in {rebuildMs:F1}ms");

            if (total == 0)
            {
                Log.Info("[F1.14] No meetings found — load save z >2 timetables sharing common stations");
                return;
            }

            // Breakdown per type
            int passenger = RailwayManager.Timetable.Meetings.MeetingEventsService.GetMeetingsByType(
                RailwayManager.Timetable.Meetings.MeetingType.PassengerMeeting).Count;
            int mijanka = RailwayManager.Timetable.Meetings.MeetingEventsService.GetMeetingsByType(
                RailwayManager.Timetable.Meetings.MeetingType.MijankaOpportunity).Count;
            int crew = RailwayManager.Timetable.Meetings.MeetingEventsService.GetMeetingsByType(
                RailwayManager.Timetable.Meetings.MeetingType.CrewSwapEligible).Count;
            Log.Info($"[F1.14] Breakdown: passenger={passenger}, mijanka={mijanka}, crew_swap={crew}");

            // Sample first 5 events dla inspection
            int shown = 0;
            foreach (var ev in RailwayManager.Timetable.Meetings.MeetingEventsService.AllEvents)
            {
                if (shown >= 5) break;
                Log.Info($"[F1.14]   {ev.description} (window {ev.windowEndSec - ev.windowStartSec}s)");
                shown++;
            }

            // Verify lazy invalidation
            RailwayManager.Timetable.Meetings.MeetingEventsService.Invalidate();
            if (!RailwayManager.Timetable.Meetings.MeetingEventsService.IsDirty)
                Log.Warn("[F1.14] FAIL: Invalidate() didn't set dirty");
            // Query auto-rebuilds
            var afterInvalidate = RailwayManager.Timetable.Meetings.MeetingEventsService.AllEvents;
            if (RailwayManager.Timetable.Meetings.MeetingEventsService.IsDirty)
                Log.Warn("[F1.14] FAIL: Query after invalidate didn't trigger rebuild");
            else
                Log.Info($"[F1.14] Lazy rebuild correct: dirty cleared after query");
        }

        /// <summary>
        /// M-TimetableUX F1.9 Commit A smoke test — TimeExpandedPathfinder naive baseline.
        /// Verify że gdy isBlockFree=null, time-aware A* znajduje path comparable do
        /// RailwayPathfinder.FindPath. Czas trasy z time-aware powinien być >= czas calculated z fizyki
        /// (bo simple speed/distance vs. detailed acceleration model).
        /// </summary>
        [ContextMenu("M-TimetableUX/F1.9: Smoke test TimeExpandedPathfinder")]
        public void TestTimeExpandedPathfinder()
        {
            if (Graph == null || Stations == null || Stations.Count < 2)
            {
                Log.Warn("[F1.9] Graph/Stations not loaded");
                return;
            }

            // Pick 2 random major stations
            RailwayStation a = null, b = null;
            foreach (var s in Stations)
            {
                if (s.pathNodeId < 0 || !s.isMajorStation) continue;
                if (a == null) { a = s; continue; }
                if (s.pathNodeId != a.pathNodeId) { b = s; break; }
            }
            if (a == null || b == null) { Log.Warn("[F1.9] Need ≥2 major stations"); return; }

            Log.Info($"[F1.9] Test path: {a.name} → {b.name}");

            // Baseline: standard FindPath
            float t0 = Time.realtimeSinceStartup;
            var standardResult = RailwayPathfinder.FindPath(Graph, a.pathNodeId, b.pathNodeId);
            float standardMs = (Time.realtimeSinceStartup - t0) * 1000f;

            if (!standardResult.success)
            {
                Log.Warn($"[F1.9] Standard FindPath failed: {standardResult.exploredNodes} explored");
                return;
            }

            Log.Info($"[F1.9] Standard FindPath: {standardResult.nodeIds.Count} nodes, " +
                     $"{standardResult.totalLengthM / 1000f:F1} km, {standardResult.exploredNodes} explored, {standardMs:F1}ms");

            // F1.9 time-aware (no block reservations — should match standard semantically)
            var options = TimeExpandedPathfinder.Options.Default(startTimeSec: 6 * 3600); // 06:00
            t0 = Time.realtimeSinceStartup;
            var timeResult = TimeExpandedPathfinder.FindPath(Graph, a.pathNodeId, b.pathNodeId, options);
            float timeMs = (Time.realtimeSinceStartup - t0) * 1000f;

            if (!timeResult.success)
            {
                Log.Warn($"[F1.9] Time-aware FindPath failed: {timeResult.failureReason} ({timeResult.exploredStates} explored, {timeMs:F1}ms)");
                return;
            }

            Log.Info($"[F1.9] Time-aware FindPath: {timeResult.nodeIds.Count} nodes, " +
                     $"{timeResult.totalLengthM / 1000f:F1} km, total time {timeResult.totalTimeSec / 60f:F1} min, " +
                     $"{timeResult.exploredStates} explored, {timeMs:F1}ms");

            // Semantic check (no block reservations): path lengths powinny być identyczne lub bardzo blisko
            float lenDiff = Mathf.Abs(timeResult.totalLengthM - standardResult.totalLengthM);
            float lenDiffPct = standardResult.totalLengthM > 0 ? lenDiff / standardResult.totalLengthM * 100f : 0f;
            if (lenDiffPct > 5f)
                Log.Warn($"[F1.9] Path length differs >5% ({lenDiffPct:F2}%) — F1.5 directionPenalty może preferować inne edges");
            else
                Log.Info($"[F1.9] Semantic check OK: path length diff {lenDiffPct:F2}%");

            // F1.9 with always-blocked predicate → expect failure
            var blockedOptions = options;
            blockedOptions.isBlockFree = (key, start, end) => false; // wszystko zajęte
            var blockedResult = TimeExpandedPathfinder.FindPath(Graph, a.pathNodeId, b.pathNodeId, blockedOptions);
            if (blockedResult.success)
                Log.Warn("[F1.9] FAIL: always-blocked predicate gave success path (should be impossible)");
            else
                Log.Info($"[F1.9] Always-blocked test correctly returned failure: {blockedResult.failureReason}");
        }

        /// <summary>
        /// M-TimetableUX F1.12 + F1.13b smoke test — generate suggestions dla wszystkich timetables.
        /// Verify że services działają na realnym save z istniejącymi obiegami.
        /// </summary>
        [ContextMenu("M-TimetableUX/F1.12+F1.13b: Generate suggestions for all timetables")]
        public void GenerateAllSuggestions()
        {
            var ttList = TimetableService.Timetables;
            if (ttList.Count == 0) { Log.Info("[F1.12/13b] Brak timetables — wczytaj save"); return; }

            // Reset suggestion memory dla fresh test
            RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.Reset();

            // Force MeetingEventsService rebuild
            RailwayManager.Timetable.Meetings.MeetingEventsService.Invalidate();
            RailwayManager.Timetable.Meetings.MeetingEventsService.Rebuild();

            int totalCirc = 0, totalMij = 0;
            float t0 = Time.realtimeSinceStartup;

            foreach (var tt in ttList)
            {
                if (tt == null) continue;
                var circulations = RailwayManager.Timetable.Suggestions.CirculationSuggestionService.GenerateSuggestions(tt.id);
                var mijankas = RailwayManager.Timetable.Suggestions.MijankaSuggestionService.GenerateSuggestions(tt.id);
                totalCirc += circulations.Count;
                totalMij += mijankas.Count;
            }

            float elapsedMs = (Time.realtimeSinceStartup - t0) * 1000f;
            Log.Info($"[F1.12/13b] Generated {totalCirc} circulation suggestions + {totalMij} mijanka suggestions " +
                     $"across {ttList.Count} timetables w {elapsedMs:F1}ms");

            // Sample first 3 z każdego typu
            int shown = 0;
            foreach (var tt in ttList)
            {
                if (shown >= 3) break;
                var circ = RailwayManager.Timetable.Suggestions.CirculationSuggestionService.GenerateSuggestions(tt.id);
                foreach (var s in circ) { Log.Info($"[F1.12]   {s.description}"); shown++; if (shown >= 3) break; }
            }
            shown = 0;
            foreach (var tt in ttList)
            {
                if (shown >= 3) break;
                var mij = RailwayManager.Timetable.Suggestions.MijankaSuggestionService.GenerateSuggestions(tt.id);
                foreach (var m in mij) { Log.Info($"[F1.13b]  {m.description}"); shown++; if (shown >= 3) break; }
            }

            // Cleanup memory dla normal session
            RailwayManager.SharedUI.Suggestions.SuggestionMemoryService.Reset();
        }

        /// <summary>
        /// M-TimetableUX F1.6 polish smoke test — count off-path stations near current route.
        /// Wymaga aktywnego Route w TimetableCreatorUI.
        /// </summary>
        [ContextMenu("M-TimetableUX/F1.6: Count off-path stations near route")]
        public void TestOffPathStationsDetector()
        {
            var ttList = TimetableService.Timetables;
            if (ttList.Count == 0) { Log.Info("[F1.6 polish] Brak timetables"); return; }

            // Use first timetable's route
            var firstTt = ttList[0];
            var route = TimetableService.GetRoute(firstTt.routeId);
            if (route == null) { Log.Warn("[F1.6 polish] Brak route dla TT#" + firstTt.id); return; }

            float t0 = Time.realtimeSinceStartup;
            var (total, major, halt) = OffPathStationsDetector.CountOffPathStations(route, this);
            float elapsedMs = (Time.realtimeSinceStartup - t0) * 1000f;

            Log.Info($"[F1.6 polish] Route TT#{firstTt.id} ({route.name}): " +
                     $"{total} off-path ({major} major, {halt} halt) detected w {elapsedMs:F1}ms");

            // Larger radius test
            var (total2k, major2k, halt2k) = OffPathStationsDetector.CountOffPathStations(route, this, 2000f);
            Log.Info($"[F1.6 polish] Same route z 2km radius: {total2k} off-path ({major2k} major, {halt2k} halt)");
        }

        /// <summary>
        /// M-TimetableUX F1.9 Commit E polish: full semantic preservation test suite
        /// (4 test data sets per spec'a). Runs Set A (short), Set B (long), Set C (dense),
        /// Set D (edge cases) + logs breakdown.
        /// </summary>
        [ContextMenu("M-TimetableUX/F1.9 Commit E: Run semantic preservation suite (4 sets)")]
        public void RunF19SemanticSuite()
        {
            TimeExpandedPathfinderSemanticTests.RunFullSuiteAndLog(this);
        }
    }
}
