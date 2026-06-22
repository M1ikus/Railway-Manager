using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    public partial class DepotMovementSimulator
    {
        // ── Pathfinding ──────────────────────────────────────────────

        void ExecutePathfinding(DepotMoveTask task)
        {
            var fromTrack = _graph.GetTrack(task.fromTrackId);
            var toTrack = _graph.GetTrack(task.toTrackId);
            if (fromTrack == null || toTrack == null)
            {
                Fail(task, $"track not found (from#{task.fromTrackId} or to#{task.toTrackId})");
                return;
            }

            // Zbierz wszystkie end-node'y obu torów (start/end w obie strony).
            // Tory mogą być połączone (end toru A = start toru B to ten sam node) —
            // próbujemy wszystkich kombinacji i bierzemy najdłuższą ścieżkę (nie zdegenerowaną).
            var fromNodes = GetTrackEndpointNodes(fromTrack);
            var toNodes = GetTrackEndpointNodes(toTrack);

            if (fromNodes.Count == 0 || toNodes.Count == 0)
            {
                Fail(task, "could not resolve endpoint nodes from tracks");
                return;
            }

            // Pobierz bieżącą pozycję visual'u (dla heurystyki kierunku)
            Vector3? currentVisualPos = null;
            if (_consistVisuals.TryGetValue(task.consistId, out var curVisual) && curVisual != null)
            {
                var p = curVisual.transform.position;
                p.y -= VehicleYHeight;
                currentVisualPos = p;
            }

            // Dla każdej kombinacji — policz ścieżkę + wybierz najlepszą wg heurystyki:
            // - Jeśli targetWorldPos set: pick path gdzie projekcja target jest DALEJ niż projekcja visual'u
            //   (consist jedzie do przodu, nie do tyłu).
            // - Fallback (brak target pos): najdłuższa ścieżka.
            List<int> bestPath = null;
            int bestStart = -1, bestEnd = -1;
            float bestScore = float.MinValue;

            foreach (var s in fromNodes)
            {
                foreach (var e in toNodes)
                {
                    if (s == e) continue;
                    var candidate = _graph.FindPath(s, e);
                    if (candidate == null || candidate.Count < 2) continue;

                    // Policz score dla heurystyki kierunku
                    float score;
                    if (task.targetWorldPos.HasValue && currentVisualPos.HasValue)
                    {
                        // Zbuduj tymczasową polyline do projekcji
                        var tmpPoly = BuildPolylineFromNodes(candidate);
                        if (tmpPoly == null || tmpPoly.Count < 2) continue;

                        var tmpCumDist = new float[tmpPoly.Count];
                        for (int j = 1; j < tmpPoly.Count; j++)
                            tmpCumDist[j] = tmpCumDist[j - 1] + Vector3.Distance(tmpPoly[j - 1], tmpPoly[j]);

                        float visProj = ProjectPositionOnPolyline(tmpPoly, tmpCumDist, currentVisualPos.Value);
                        float tgtProj = ProjectPositionOnPolyline(tmpPoly, tmpCumDist, task.targetWorldPos.Value);

                        // Score = dystans forward (target przed visualem); ujemny gdy target z tyłu
                        score = tgtProj - visProj;
                    }
                    else
                    {
                        // Fallback: najdłuższa ścieżka
                        score = candidate.Count;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPath = candidate;
                        bestStart = s;
                        bestEnd = e;
                    }
                }
            }

            if (bestPath == null)
            {
                Fail(task, $"no path between any endpoints of track#{task.fromTrackId} and track#{task.toTrackId} " +
                           $"(from nodes: [{string.Join(",", fromNodes)}], to nodes: [{string.Join(",", toNodes)}])");
                return;
            }

            var path = bestPath;
            Log.Info($"[DepotMovementSim] Path chosen: node#{bestStart} → node#{bestEnd} ({path.Count} nodes, score={bestScore:F1})");

            task.pathNodeIds = path;

            // Zbierz tory na ścieżce (fromTrack + intermediate + toTrack)
            var tracksOnPath = CollectTracksOnPath(path);
            task.reservedTrackIds = tracksOnPath; // TD-031: zbiór dotkniętych torów (info/debug; occupancy = interwały)

            // Auto-switch rozjazdów wzdłuż ścieżki
            int bladeSwitches = AutoSwitchBladesAlongPath(path);

            // Polyline + segmenty per-tor (TD-031: mapowanie polyline↔track-local)
            BuildPolylineFromPath(task);

            // Oblicz stopDistanceM — pozycja gdzie consist ma się zatrzymać (cel gracza lub koniec polyline)
            task.stopDistanceM = task.totalLengthM;
            if (task.targetWorldPos.HasValue)
                task.stopDistanceM = ProjectPositionOnPolyline(task.polyline, task.cumDistM, task.targetWorldPos.Value);

            // Smooth continuation — zacznij currentDistanceM od bieżącej pozycji visual'u (nie teleportuj).
            if (_consistVisuals.TryGetValue(task.consistId, out var visual) && visual != null)
            {
                var curPos = visual.transform.position;
                curPos.y -= VehicleYHeight; // polyline na Y=0, visual na Y=VehicleYHeight
                task.currentDistanceM = ProjectPositionOnPolyline(task.polyline, task.cumDistM, curPos);
            }

            // TD-031: ADMISSION pozycyjny (zamiast whole-path binary). Wjazd na częściowo zajęty tor jest
            // OK jeśli jest miejsce na ruch; cap = najbliższa INNA jednostka na ścieżce. Gdy ktoś stoi
            // dokładnie tam gdzie musimy ruszyć (brak postępu) → zostań Queued + retry (jak dawniej,
            // ale pozycyjnie — nie blokujemy całego toru).
            float planned = task.stopDistanceM > 0f ? task.stopDistanceM : task.totalLengthM;
            bool forward = planned >= task.currentDistanceM;
            float cap = ComputeDynamicStopCap(task, forward);
            task.dynamicStopCapM = cap;
            float effEnd = forward
                ? (float.IsPositiveInfinity(cap) ? planned : Mathf.Min(planned, cap))
                : (float.IsNegativeInfinity(cap) ? planned : Mathf.Max(planned, cap));

            if (Mathf.Abs(effEnd - task.currentDistanceM) < 0.5f && Mathf.Abs(planned - task.currentDistanceM) >= 0.5f)
            {
                task.state = DepotMoveState.Queued;
                task.pathNodeIds = null;
                task.reservedTrackIds = null;
                task.trackSegments = null;
                task.queuedTicks++;
                if (task.queuedTicks % 150 == 1)
                    Log.Warn($"[DepotMovementSim] Task waiting: consist#{task.consistId} zablokowany przez " +
                             $"jednostkę#{task.dynamicStopBlockerId} na ścieżce (brak miejsca na ruch), " +
                             $"waitingTicks={task.queuedTicks}");
                return;
            }

            // Admitted — wyczyść stary footprint (np. zaparkowany) i zapisz bieżący (by inni nas widzieli).
            _graph.RemoveConsistEverywhere(task.consistId);
            UpdateOccupantIntervalsForTask(task);

            task.state = DepotMoveState.Moving;
            float remainingToMove = task.stopDistanceM - task.currentDistanceM;
            Log.Info($"[DepotMovementSim] Pathfinding OK: consist#{task.consistId}, " +
                     $"{path.Count} nodes, {task.polyline?.Count ?? 0} polyline pts, " +
                     $"total={task.totalLengthM:F1}m, start={task.currentDistanceM:F1}m, stop={task.stopDistanceM:F1}m, " +
                     $"REMAINING={remainingToMove:F1}m, " +
                     $"{bladeSwitches} blade switches, {tracksOnPath.Count} tracks reserved");

            if (remainingToMove < 1f)
            {
                Log.Warn($"[DepotMovementSim] Task will INSTANT COMPLETE — remaining {remainingToMove:F1}m < 1m. " +
                         $"Visual pos probably overlaps target. Check direction heuristic.");
            }
        }

        /// <summary>
        /// Buduje polyline z listy node'ów ścieżki (bez zapisywania do task'a).
        /// Używane do scoring kandydatów w pathfinding.
        /// </summary>
        List<Vector3> BuildPolylineFromNodes(List<int> pathNodes)
        {
            var polyline = new List<Vector3>();
            for (int i = 0; i < pathNodes.Count - 1; i++)
            {
                int edgeId = FindEdgeBetween(pathNodes[i], pathNodes[i + 1]);
                if (edgeId < 0) continue;

                var edge = _graph.GetEdge(edgeId);
                if (edge?.Polyline == null || edge.Polyline.Count < 2) continue;

                bool reversed = (edge.FromNodeId == pathNodes[i + 1]);
                int count = edge.Polyline.Count;
                for (int j = 0; j < count; j++)
                {
                    var pt = reversed ? edge.Polyline[count - 1 - j] : edge.Polyline[j];
                    if (polyline.Count > 0 && (polyline[polyline.Count - 1] - pt).sqrMagnitude < 0.0001f)
                        continue;
                    polyline.Add(pt);
                }
            }
            return polyline;
        }

        /// <summary>
        /// Rzutuje pozycję 3D na polyline — zwraca dystans kumulatywny najbliższego punktu.
        /// </summary>
        static float ProjectPositionOnPolyline(List<Vector3> polyline, float[] cumDistM, Vector3 worldPos)
        {
            if (polyline == null || polyline.Count < 2) return 0f;

            float bestDistSq = float.MaxValue;
            float bestCumDist = 0f;

            for (int i = 0; i < polyline.Count - 1; i++)
            {
                var a = polyline[i];
                var b = polyline[i + 1];
                var ab = b - a;
                float lenSq = ab.sqrMagnitude;
                if (lenSq < 0.001f) continue;

                float t = Vector3.Dot(worldPos - a, ab) / lenSq;
                t = Mathf.Clamp01(t);
                var proj = a + ab * t;
                float distSq = (worldPos - proj).sqrMagnitude;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestCumDist = cumDistM[i] + Mathf.Sqrt(lenSq) * t;
                }
            }

            return bestCumDist;
        }

        /// <summary>
        /// Auto-przełączanie iglic wzdłuż ścieżki. Dla każdego pośredniego węzła
        /// z blade'em ustawia pozycję zgodnie z wychodzącą krawędzią w ścieżce.
        /// </summary>
        /// <returns>Liczba przełączonych zwrotnic.</returns>
        int AutoSwitchBladesAlongPath(List<int> pathNodeIds)
        {
            int switched = 0;
            for (int i = 1; i < pathNodeIds.Count - 1; i++)
            {
                int nodeId = pathNodeIds[i];
                var node = _graph.GetNode(nodeId);
                if (node?.Blade == null) continue;

                int nextNodeId = pathNodeIds[i + 1];
                int outgoingEdgeId = FindEdgeBetween(nodeId, nextNodeId);
                if (outgoingEdgeId < 0) continue;

                bool wantDiverging = (outgoingEdgeId == node.Blade.DivergingEdgeId);
                if (node.Blade.IsDiverging != wantDiverging)
                {
                    _graph.SetSwitchBladePosition(nodeId, wantDiverging);
                    switched++;
                }
            }
            return switched;
        }

        /// <summary>
        /// Buduje polyline z konkatenacji edge.Polyline wszystkich krawędzi na ścieżce.
        /// Uwzględnia kierunek krawędzi (może być reversed w stosunku do kierunku jazdy).
        /// Deduplikuje punkty na styku krawędzi.
        /// </summary>
        void BuildPolylineFromPath(DepotMoveTask task)
        {
            var polyline = new List<Vector3>();
            var pathNodes = task.pathNodeIds;

            // TD-031: równolegle z polyline buduj segmenty per-tor (mapowanie polyline↔track-local).
            var segments = new List<TaskTrackSegment>();
            TaskTrackSegment curSeg = null;
            float runningDist = 0f;

            for (int i = 0; i < pathNodes.Count - 1; i++)
            {
                int edgeId = FindEdgeBetween(pathNodes[i], pathNodes[i + 1]);
                if (edgeId < 0) continue;

                var edge = _graph.GetEdge(edgeId);
                if (edge?.Polyline == null || edge.Polyline.Count < 2) continue;

                // Kierunek krawędzi vs kierunek jazdy
                bool reversed = (edge.FromNodeId == pathNodes[i + 1]);
                int count = edge.Polyline.Count;

                // Długość geometryczna krawędzi (niezależna od kierunku jazdy)
                float edgeLen = 0f;
                for (int j = 1; j < count; j++)
                    edgeLen += Vector3.Distance(edge.Polyline[j - 1], edge.Polyline[j]);

                // TD-031: nowy segment gdy zmienia się tor (grupuj kolejne krawędzie tego samego toru)
                int trackId = FindTrackContainingEdge(edgeId);
                if (curSeg == null || curSeg.trackId != trackId)
                {
                    curSeg = new TaskTrackSegment
                    {
                        trackId = trackId,
                        polyStartM = runningDist,
                        polyEndM = runningDist,
                        reversedVsTrack = IsTrackReversedAtEntry(trackId, pathNodes[i]),
                        trackLenM = _graph.GetTrack(trackId)?.Length ?? 0f
                    };
                    segments.Add(curSeg);
                }

                for (int j = 0; j < count; j++)
                {
                    var pt = reversed ? edge.Polyline[count - 1 - j] : edge.Polyline[j];
                    // Pomiń duplikat na styku krawędzi
                    if (polyline.Count > 0 && (polyline[polyline.Count - 1] - pt).sqrMagnitude < 0.0001f)
                        continue;
                    polyline.Add(pt);
                }

                runningDist += edgeLen;
                curSeg.polyEndM = runningDist;
            }

            task.polyline = polyline;
            task.cumDistM = new float[polyline.Count];
            task.cumDistM[0] = 0f;
            for (int i = 1; i < polyline.Count; i++)
                task.cumDistM[i] = task.cumDistM[i - 1] + Vector3.Distance(polyline[i - 1], polyline[i]);
            task.totalLengthM = polyline.Count > 0 ? task.cumDistM[polyline.Count - 1] : 0f;

            task.trackSegments = segments;
        }

        /// <summary>
        /// TD-031: czy jazda po torze (wejście w entryNodeId) jest PRZECIWNA do osi track-local
        /// (Start→End wg <see cref="TrackGraph.GetTrackPolyline"/>). Wejście od End-node toru → reversed.
        /// </summary>
        bool IsTrackReversedAtEntry(int trackId, int entryNodeId)
        {
            var track = _graph.GetTrack(trackId);
            if (track?.EdgeIds == null || track.EdgeIds.Count == 0) return false;

            var firstEdge = _graph.GetEdge(track.EdgeIds[0]);
            var lastEdge = _graph.GetEdge(track.EdgeIds[track.EdgeIds.Count - 1]);
            if (firstEdge == null || lastEdge == null) return false;

            if (entryNodeId == firstEdge.FromNodeId) return false;   // wejście od Start → zgodnie z osią
            if (entryNodeId == lastEdge.ToNodeId) return true;       // wejście od End → reversed
            return false;                                            // nietypowy układ — domyślnie wzdłuż osi
        }

        // ── Helpers (track / node / edge lookup) ─────────────────────

        /// <summary>
        /// Zwraca wszystkie "końcowe" node'y toru — node'y mające tylko jedną krawędź z EdgeIds toru
        /// (tj. nie są wewnętrzne). Dla prostego toru A-B-C to {A, C}. Dla toru zapętlonego — zwraca wspólne.
        /// </summary>
        List<int> GetTrackEndpointNodes(DepotTrackData track)
        {
            var result = new List<int>();
            if (track?.EdgeIds == null || track.EdgeIds.Count == 0) return result;

            // Zlicz wystąpienia node'ów w krawędziach toru
            var nodeCount = new Dictionary<int, int>();
            foreach (int eid in track.EdgeIds)
            {
                var edge = _graph.GetEdge(eid);
                if (edge == null) continue;
                nodeCount[edge.FromNodeId] = nodeCount.GetValueOrDefault(edge.FromNodeId, 0) + 1;
                nodeCount[edge.ToNodeId] = nodeCount.GetValueOrDefault(edge.ToNodeId, 0) + 1;
            }

            // Endpoint = node występujący tylko RAZ (koniec toru)
            foreach (var kvp in nodeCount)
                if (kvp.Value == 1)
                    result.Add(kvp.Key);

            // Fallback: jeśli żaden (np. pętla) — dodaj wszystkie
            if (result.Count == 0)
            {
                foreach (var k in nodeCount.Keys) result.Add(k);
            }

            return result;
        }

        /// <summary>
        /// Zbiera wszystkie tory (trackId) na ścieżce od startu do końca.
        /// Każda krawędź na ścieżce → DepotTrackData zawierająca tę krawędź → dodaj trackId.
        /// Unikalne, zachowuje kolejność pierwszego wystąpienia.
        /// </summary>
        List<int> CollectTracksOnPath(List<int> pathNodeIds)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();

            for (int i = 0; i < pathNodeIds.Count - 1; i++)
            {
                int edgeId = FindEdgeBetween(pathNodeIds[i], pathNodeIds[i + 1]);
                if (edgeId < 0) continue;

                int trackId = FindTrackContainingEdge(edgeId);
                if (trackId >= 0 && seen.Add(trackId))
                    result.Add(trackId);
            }

            return result;
        }

        /// <summary>Reverse lookup: która DepotTrackData zawiera dany edgeId. -1 jeśli brak.</summary>
        int FindTrackContainingEdge(int edgeId)
        {
            // Liniowy scan wszystkich torów — w zajezdni torów jest niewiele (~20-50), OK
            foreach (var kvp in _graph.Tracks)
            {
                var trackData = kvp.Value;
                if (trackData.EdgeIds != null && trackData.EdgeIds.Contains(edgeId))
                    return trackData.TrackId;
            }
            return -1;
        }

        /// <summary>Znajduje najbliższy node w podanej liście ID do pozycji. -1 jeśli lista pusta.</summary>
        int FindNearestNodeInList(Vector3 pos, List<int> candidateNodeIds)
        {
            int best = -1;
            float bestDistSq = float.MaxValue;
            foreach (int nid in candidateNodeIds)
            {
                var node = _graph.GetNode(nid);
                float dSq = (node.Position - pos).sqrMagnitude;
                if (dSq < bestDistSq) { bestDistSq = dSq; best = nid; }
            }
            return best;
        }

        /// <summary>
        /// Zwraca trackId toru, którego polyline jest najbliżej danej pozycji 3D.
        /// Używane gdy consist jest mid-move i `marker.currentTrackId` jest stale.
        /// </summary>
        int FindNearestTrackToPosition(Vector3 worldPos)
        {
            if (_graph == null) return -1;

            float bestDistSq = float.MaxValue;
            int bestTrackId = -1;

            foreach (var kvp in _graph.Tracks)
            {
                var polyline = _graph.GetTrackPolyline(kvp.Key);
                if (polyline == null || polyline.Count < 2) continue;

                for (int i = 0; i < polyline.Count - 1; i++)
                {
                    var a = polyline[i];
                    var b = polyline[i + 1];
                    var ab = b - a;
                    float lenSq = ab.sqrMagnitude;
                    if (lenSq < 0.001f) continue;

                    float t = Mathf.Clamp01(Vector3.Dot(worldPos - a, ab) / lenSq);
                    var proj = a + ab * t;
                    float dSq = (worldPos - proj).sqrMagnitude;

                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestTrackId = kvp.Key;
                    }
                }
            }

            return bestTrackId;
        }

        /// <summary>Znajduje krawędź łączącą dwa sąsiednie węzły. -1 jeśli brak.</summary>
        int FindEdgeBetween(int nodeA, int nodeB)
        {
            var node = _graph.GetNode(nodeA);
            if (node?.EdgeIds == null) return -1;
            foreach (int eid in node.EdgeIds)
            {
                var edge = _graph.GetEdge(eid);
                if (edge == null) continue;
                if ((edge.FromNodeId == nodeA && edge.ToNodeId == nodeB) ||
                    (edge.FromNodeId == nodeB && edge.ToNodeId == nodeA))
                    return eid;
            }
            return -1;
        }
    }
}
