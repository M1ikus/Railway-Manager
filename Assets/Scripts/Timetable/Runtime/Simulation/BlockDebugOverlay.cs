using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M9a Etap 4: Debug overlay bloków semaforowych na mapie.
    /// Rysuje kolorowe linie wzdłuż trasy aktywnego pociągu — kolor wg occupancy:
    /// zielony = wolny, niebieski = zajęty przez tego pociąga, czerwony = zajęty przez innego.
    /// Toggle: F9.
    ///
    /// Wzorzec renderowania z RoutePreviewOverlay: LineRenderer, Sprites/Default,
    /// adaptive width wg orthographicSize, layer 31 (MapLayer).
    /// </summary>
    public class BlockDebugOverlay : MonoBehaviour
    {
        // ── Kolory ──────────────────────────────────────────────────

        static readonly Color ColorFree     = new(0.2f, 0.9f, 0.2f, 0.7f);   // zielony
        static readonly Color ColorMyTrain  = new(0.2f, 0.5f, 0.9f, 0.7f);   // niebieski
        static readonly Color ColorOccupied = new(0.9f, 0.2f, 0.2f, 0.7f);   // czerwony

        // ── Adaptive width (ta sama formuła co RoutePreviewOverlay) ──

        const float WidthScale = 0.008f;
        const float MinWidthM = 2f;
        const float MaxWidthM = 200f;
        const float OverlayY = 20f;       // ponad railways(8), POI(10), train(12), routePreview(18)
        const int SortingOrder = 105;      // pod RoutePreviewOverlay(110)

        // ── State ───────────────────────────────────────────────────

        readonly List<LineRenderer> _lines = new();
        readonly List<(LineRenderer lr, int platformId)> _stationLines = new();
        GameObject _container;
        Camera _mapCamera;
        bool _isVisible;

        /// <summary>ID pociągu którego bloki pokazujemy. -1 = pierwszy aktywny.</summary>
        int _selectedTrainRunId = -1;

        /// <summary>Cache — żeby nie przebudowywać co frame.</summary>
        int _lastRebuiltForTrainId = -1;
        int _lastRebuiltBlockCount;

        TrainRunSimulator _simulator;
        PathfindingGraph _graph;

        // ── Unity lifecycle ─────────────────────────────────────────

        void Start()
        {
            _simulator = GetComponent<TrainRunSimulator>();
            if (_simulator == null)
                _simulator = GetComponentInParent<TrainRunSimulator>();
        }

        void Update()
        {
            // F9 toggle (New Input System)
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
                Toggle();
        }

        void LateUpdate()
        {
            if (!_isVisible || _simulator == null) return;

            // Znajdź pociąg do wizualizacji
            var st = GetSelectedTrain();
            if (st == null)
            {
                ClearLines();
                return;
            }

            // Przebuduj jeśli zmienił się pociąg lub liczba bloków
            if (st.trainRun.id != _lastRebuiltForTrainId ||
                st.routeBlockCount != _lastRebuiltBlockCount)
            {
                RebuildOverlay(st);
            }

            // Aktualizuj kolory wg bieżącej occupancy
            UpdateColors(st);

            // Adaptive width
            UpdateWidth();
        }

        // ── Toggle ──────────────────────────────────────────────────

        [ContextMenu("Toggle Block Debug Overlay (F9)")]
        public void Toggle()
        {
            _isVisible = !_isVisible;

            if (_isVisible)
            {
                Log.Info("[BlockDebugOverlay] ON (F9)");
                var st = GetSelectedTrain();
                if (st != null)
                    RebuildOverlay(st);
            }
            else
            {
                Log.Info("[BlockDebugOverlay] OFF (F9)");
                ClearLines();
            }
        }

        // ── Rebuild ─────────────────────────────────────────────────

        void RebuildOverlay(SimulatedTrain st)
        {
            ClearLines();
            EnsureContainer();
            EnsureGraph();

            if (_graph == null || st.routeBlockCount == 0) return;

            var nodeIds = st.effectiveNodeIds ?? st.route.nodeIds;
            if (nodeIds == null || nodeIds.Count < 2) return;

            // Użyj szczegółowej polyline z edge geometry (zamiast node-to-node)
            var poly = st.cachedPolyline;
            var cumDist = st.polylineCumulDist;
            var nodeToPolyMap = st.nodeIdxToPolyIdx;
            bool hasDetailedPoly = poly != null && poly.Length >= 2
                                   && nodeToPolyMap != null && nodeToPolyMap.Length == nodeIds.Count;

            for (int b = 0; b < st.routeBlockCount; b++)
            {
                int startNodeIdx = Mathf.Clamp(st.blockStartRouteIdx[b], 0, nodeIds.Count - 1);
                int endNodeIdx = Mathf.Clamp(st.blockEndRouteIdx[b], 0, nodeIds.Count - 1);
                if (endNodeIdx <= startNodeIdx) endNodeIdx = startNodeIdx + 1;
                if (endNodeIdx >= nodeIds.Count) endNodeIdx = nodeIds.Count - 1;

                int polyStart, polyEnd;
                if (hasDetailedPoly)
                {
                    polyStart = nodeToPolyMap[startNodeIdx];
                    polyEnd = nodeToPolyMap[Mathf.Min(endNodeIdx, nodeToPolyMap.Length - 1)];
                    if (polyEnd <= polyStart) polyEnd = Mathf.Min(polyStart + 1, poly.Length - 1);
                }
                else
                {
                    polyStart = startNodeIdx;
                    polyEnd = endNodeIdx;
                }

                int pointCount = polyEnd - polyStart + 1;
                if (pointCount < 2) continue;

                var lr = CreateLineRenderer();
                lr.positionCount = pointCount;
                for (int i = 0; i < pointCount; i++)
                {
                    int pi = polyStart + i;
                    Vector2 pos;
                    if (hasDetailedPoly && pi < poly.Length)
                        pos = poly[pi];
                    else
                        pos = _graph.GetNode(nodeIds[Mathf.Clamp(startNodeIdx + i, 0, nodeIds.Count - 1)]).position;
                    lr.SetPosition(i, new Vector3(pos.x, OverlayY, pos.y));
                }

                _lines.Add(lr);
            }

            // Tory stacyjne — polyline wypełniająca luki:
            // - Pierwsza stacja: nodeIds[0] → blockStartRouteIdx[0] (przed 1. blokiem)
            // - Luki między kolejnymi blokami: blockEndRouteIdx[b] → blockStartRouteIdx[b+1]
            // - Ostatnia stacja: blockEndRouteIdx[last] → nodeIds[last] (po ostatnim bloku)
            // Bloki są exit→entry (20% od końca stacji) — reszta toru stacyjnego = tu.
            var stops = st.timetable.stops;

            // Pierwsza stacja — luka przed pierwszym blokiem
            int firstPlatformId = stops.Count > 0 ? stops[0].platformId : -1;
            RenderStationGap(0, st.blockStartRouteIdx[0], firstPlatformId,
                             poly, nodeToPolyMap, nodeIds, hasDetailedPoly);

            // Ostatnia stacja — luka po ostatnim bloku
            int lastIdx = st.routeBlockCount - 1;
            int lastPlatformId = stops.Count > 0 ? stops[stops.Count - 1].platformId : -1;
            RenderStationGap(st.blockEndRouteIdx[lastIdx], nodeIds.Count - 1, lastPlatformId,
                             poly, nodeToPolyMap, nodeIds, hasDetailedPoly);

            for (int b = 0; b < st.routeBlockCount - 1; b++)
            {
                int gapStartNodeIdx = st.blockEndRouteIdx[b];
                int gapEndNodeIdx = st.blockStartRouteIdx[b + 1];

                // Brak luki — bloki stykają się (brak toru stacyjnego do rysowania)
                if (gapEndNodeIdx <= gapStartNodeIdx) continue;

                // Znajdź platformId stacji w tej luce (do kolorowania wg occupancy)
                int gapPlatformId = -1;
                for (int s = 0; s < stops.Count; s++)
                {
                    if (stops[s].platformId < 0) continue;
                    float stopDist = st.stopDistancesM[s];
                    float gapEntryDist = st.blockExitDistM[b];
                    float gapExitDist = st.blockEntryDistM[b + 1];
                    if (stopDist >= gapEntryDist && stopDist <= gapExitDist)
                    {
                        gapPlatformId = stops[s].platformId;
                        break;
                    }
                }

                if (hasDetailedPoly)
                {
                    int polyStart = nodeToPolyMap[Mathf.Clamp(gapStartNodeIdx, 0, nodeToPolyMap.Length - 1)];
                    int polyEnd = nodeToPolyMap[Mathf.Clamp(gapEndNodeIdx, 0, nodeToPolyMap.Length - 1)];
                    if (polyEnd <= polyStart) polyEnd = Mathf.Min(polyStart + 1, poly.Length - 1);

                    int pointCount = polyEnd - polyStart + 1;
                    if (pointCount < 2) continue;

                    var slr = CreateLineRenderer();
                    slr.sortingOrder = SortingOrder + 1;
                    slr.positionCount = pointCount;
                    for (int i = 0; i < pointCount; i++)
                    {
                        int pi = polyStart + i;
                        var pos = poly[Mathf.Clamp(pi, 0, poly.Length - 1)];
                        slr.SetPosition(i, new Vector3(pos.x, OverlayY + 0.5f, pos.y));
                    }
                    _stationLines.Add((slr, gapPlatformId));
                }
                else
                {
                    // Fallback: node-to-node przez lukę
                    int pointCount = gapEndNodeIdx - gapStartNodeIdx + 1;
                    if (pointCount < 2) continue;

                    var slr = CreateLineRenderer();
                    slr.sortingOrder = SortingOrder + 1;
                    slr.positionCount = pointCount;
                    for (int i = 0; i < pointCount; i++)
                    {
                        int ni = Mathf.Clamp(gapStartNodeIdx + i, 0, nodeIds.Count - 1);
                        var pos = _graph.GetNode(nodeIds[ni]).position;
                        slr.SetPosition(i, new Vector3(pos.x, OverlayY + 0.5f, pos.y));
                    }
                    _stationLines.Add((slr, gapPlatformId));
                }
            }

            _lastRebuiltForTrainId = st.trainRun.id;
            _lastRebuiltBlockCount = st.routeBlockCount;

            void RenderStationGap(int gapStartNodeIdx, int gapEndNodeIdx, int platformId,
                Vector2[] polyArr, int[] polyMap, System.Collections.Generic.List<int> nIds, bool hasPoly)
            {
                if (gapEndNodeIdx <= gapStartNodeIdx) return;

                if (hasPoly)
                {
                    int polyStart = polyMap[Mathf.Clamp(gapStartNodeIdx, 0, polyMap.Length - 1)];
                    int polyEnd = polyMap[Mathf.Clamp(gapEndNodeIdx, 0, polyMap.Length - 1)];
                    if (polyEnd <= polyStart) polyEnd = Mathf.Min(polyStart + 1, polyArr.Length - 1);
                    int pointCount = polyEnd - polyStart + 1;
                    if (pointCount < 2) return;

                    var slr = CreateLineRenderer();
                    slr.sortingOrder = SortingOrder + 1;
                    slr.positionCount = pointCount;
                    for (int i = 0; i < pointCount; i++)
                    {
                        var pos = polyArr[Mathf.Clamp(polyStart + i, 0, polyArr.Length - 1)];
                        slr.SetPosition(i, new Vector3(pos.x, OverlayY + 0.5f, pos.y));
                    }
                    _stationLines.Add((slr, platformId));
                }
                else
                {
                    int pointCount = gapEndNodeIdx - gapStartNodeIdx + 1;
                    if (pointCount < 2) return;

                    var slr = CreateLineRenderer();
                    slr.sortingOrder = SortingOrder + 1;
                    slr.positionCount = pointCount;
                    for (int i = 0; i < pointCount; i++)
                    {
                        int ni = Mathf.Clamp(gapStartNodeIdx + i, 0, nIds.Count - 1);
                        var pos = _graph.GetNode(nIds[ni]).position;
                        slr.SetPosition(i, new Vector3(pos.x, OverlayY + 0.5f, pos.y));
                    }
                    _stationLines.Add((slr, platformId));
                }
            }

            Log.Info($"[BlockDebugOverlay] Rebuilt: {_lines.Count} block lines, " +
                     $"{_stationLines.Count} station lines " +
                     $"for train '{st.trainRun.trainNumberSnapshot}'" +
                     (hasDetailedPoly ? $" (detailed polyline: {poly.Length} pts)" : " (node positions fallback)"));
        }

        // ── Update colors ───────────────────────────────────────────

        void UpdateColors(SimulatedTrain st)
        {
            var blockOcc = _simulator.BlockOccupancy;
            var platOcc = _simulator.PlatformOccupancy;
            int myId = st.trainRun.id;
            var stops = st.timetable.stops;

            for (int b = 0; b < _lines.Count && b < st.routeBlockCount; b++)
            {
                int blockKey = st.routeBlockKeys[b];

                // Bazowy kolor z block occupancy
                Color color;
                if (blockOcc.TryGetValue(blockKey, out int blockOwner))
                    color = (blockOwner == myId) ? ColorMyTrain : ColorOccupied;
                else
                    color = ColorFree;

                // Sprawdź czy w tym bloku jest stacja z zajętym torem (override)
                float blockStart = st.blockEntryDistM[b];
                float blockEnd = st.blockExitDistM[b];
                for (int s = 0; s < stops.Count; s++)
                {
                    float stopDist = st.stopDistancesM[s];
                    if (stopDist < blockStart || stopDist > blockEnd) continue;

                    int platformId = stops[s].platformId;
                    if (platformId < 0) continue;

                    if (platOcc.TryGetValue(platformId, out int platOwner))
                    {
                        color = (platOwner == myId) ? ColorMyTrain : ColorOccupied;
                        break; // jeden override wystarczy
                    }
                }

                var lr = _lines[b];
                lr.startColor = color;
                lr.endColor = color;
            }

            // Kolory stacji (nałożone segmenty)
            for (int s = 0; s < _stationLines.Count; s++)
            {
                var (slr, platformId) = _stationLines[s];
                if (slr == null) continue;

                Color sColor;
                if (platformId < 0)
                    sColor = ColorFree;
                else if (platOcc.TryGetValue(platformId, out int platOwner))
                    sColor = (platOwner == myId) ? ColorMyTrain : ColorOccupied;
                else
                    sColor = ColorFree;

                slr.startColor = sColor;
                slr.endColor = sColor;
            }
        }

        // ── Adaptive width ──────────────────────────────────────────

        void UpdateWidth()
        {
            if (_mapCamera == null || !_mapCamera.isActiveAndEnabled)
                _mapCamera = FindMapCamera();
            if (_mapCamera == null || !_mapCamera.orthographic) return;

            float w = Mathf.Clamp(_mapCamera.orthographicSize * WidthScale, MinWidthM, MaxWidthM);
            foreach (var lr in _lines)
            {
                lr.startWidth = w;
                lr.endWidth = w;
            }
            foreach (var (slr, _) in _stationLines)
            {
                if (slr == null) continue;
                slr.startWidth = w;
                slr.endWidth = w;
            }
        }

        // ── Helpers ─────────────────────────────────────────────────

        SimulatedTrain GetSelectedTrain()
        {
            var trains = _simulator.ActiveTrains;
            if (trains.Count == 0) return null;

            if (_selectedTrainRunId >= 0 && trains.TryGetValue(_selectedTrainRunId, out var selected))
                return selected;

            // Fallback: pierwszy aktywny
            foreach (var kvp in trains)
                return kvp.Value;

            return null;
        }

        void EnsureContainer()
        {
            if (_container != null) return;
            _container = new GameObject("BlockDebugOverlay");
            _container.layer = 31; // MapLayer
            _container.transform.SetParent(transform);
        }

        void EnsureGraph()
        {
            if (_graph != null) return;
            var init = TimetableInitializer.Instance;
            if (init != null) _graph = init.Graph;
        }

        LineRenderer CreateLineRenderer()
        {
            var go = new GameObject("BlockLine");
            go.layer = 31;
            go.transform.SetParent(_container.transform);

            var lr = go.AddComponent<LineRenderer>();
            lr.material = MaterialFactory.CreateLine();
            lr.useWorldSpace = true;
            lr.sortingOrder = SortingOrder;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.startWidth = 10f;
            lr.endWidth = 10f;

            return lr;
        }

        void ClearLines()
        {
            foreach (var lr in _lines)
            {
                if (lr != null && lr.gameObject != null)
                    Destroy(lr.gameObject);
            }
            _lines.Clear();

            foreach (var (slr, _) in _stationLines)
            {
                if (slr != null && slr.gameObject != null)
                    Destroy(slr.gameObject);
            }
            _stationLines.Clear();

            _lastRebuiltForTrainId = -1;
            _lastRebuiltBlockCount = 0;
        }

        static Camera FindMapCamera()
        {
            int mapLayerMask = 1 << 31;
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || !cam.orthographic) continue;
                if ((cam.cullingMask & mapLayerMask) != 0)
                    return cam;
            }
            return Camera.main;
        }
    }
}
