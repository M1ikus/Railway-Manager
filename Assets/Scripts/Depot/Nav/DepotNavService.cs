using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using DepotSystem.Furniture.Placement;
using DepotSystem.OutdoorEquipment;

namespace DepotSystem.Nav
{
    /// <summary>
    /// TD-033: fasada nawigacji pracowników. Cache'uje zbiór przeszkód (NavObstacleSetBuilder),
    /// invaliduje na edycję zajezdni (eventy ścian/mebli/terenu + count-check equipment),
    /// liczy trasę obstacle-free (VisibilityGraphRouter) z OPCJONALNĄ preferencją malowanego PathGraph.
    /// MonoBehaviour singleton (bootstrap w DepotManager). Wołane przez Personnel `EmployeeWalkPathfinder`.
    /// </summary>
    public class DepotNavService : MonoBehaviour
    {
        public static DepotNavService Instance { get; private set; }

        public float WorkerRadius = NavObstacleSetBuilder.DefaultWorkerRadius;
        const float PathGraphSnapM = 20f;

        NavObstacleSet _set;
        bool _built;
        bool _dirty = true;
        int _lastEquipCount = -1;
        bool _subWalls, _subFurn, _subGround;

        WallBuildingSystem _walls;
        OutdoorEquipmentPlacer _equip;
        FurniturePlacer _furn;
        GroundGenerator _ground;
        PathGraph _pathGraph;
        TrackGraph _trackGraph;
        float _lastFallbackWarnTime = -999f;

        // TD-033 G: yield przed pociągiem — gdy pracownik wszedłby w pas zajętego toru, czeka.
        public const float YieldLateralClearM = 2.5f; // bok od osi toru
        public const float YieldLongClearM = 3.0f;    // bufor wzdłuż toru (przód/tył składu)

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        public void Invalidate() => _dirty = true;
        void MarkDirty() => _dirty = true;

        // ── Build / cache ─────────────────────────────────────────────

        void Resolve()
        {
            if (_walls == null) _walls = DepotServices.Get<WallBuildingSystem>();
            if (_equip == null) _equip = OutdoorEquipmentPlacer.Instance;
            if (_furn == null) _furn = FurniturePlacer.Instance;
            if (_ground == null) _ground = DepotServices.Get<GroundGenerator>();
            if (_pathGraph == null) _pathGraph = DepotServices.Get<PathGraph>();
            if (_trackGraph == null) _trackGraph = DepotServices.Get<TrackGraph>();
        }

        void Subscribe()
        {
            // Per-system: każdy event podpinamy gdy jego system PIERWSZY raz się pojawi (mógł powstać
            // po pierwszym EnsureBuilt). Idempotentne.
            if (!_subWalls && _walls != null) { _walls.OnWallsChanged += MarkDirty; _subWalls = true; }
            if (!_subFurn && _furn != null) { _furn.OnPlacementStateChanged += MarkDirty; _subFurn = true; }
            if (!_subGround && _ground != null) { _ground.OnBoundsChanged += MarkDirty; _subGround = true; }
        }

        void Unsubscribe()
        {
            if (_subWalls && _walls != null) _walls.OnWallsChanged -= MarkDirty;
            if (_subFurn && _furn != null) _furn.OnPlacementStateChanged -= MarkDirty;
            if (_subGround && _ground != null) _ground.OnBoundsChanged -= MarkDirty;
            _subWalls = _subFurn = _subGround = false;
        }

        void EnsureBuilt()
        {
            Resolve();
            Subscribe();
            int equipCount = _equip != null ? _equip.Placed.Count : 0;
            if (_dirty || !_built || equipCount != _lastEquipCount)
            {
                _set = NavObstacleSetBuilder.Build(_walls, _equip, _furn, WorkerRadius);
                _lastEquipCount = equipCount;
                _dirty = false;
                _built = true;
            }
        }

        /// <summary>Aktualny zbiór przeszkód (rebuild jeśli dirty). Dla G (train-yield) i gizmos.</summary>
        public NavObstacleSet ObstacleSet { get { EnsureBuilt(); return _set; } }

        // ── Routing ───────────────────────────────────────────────────

        /// <summary>Trasa obstacle-free start→dest. Preferuje malowany PathGraph (jeśli czysty), inaczej pełny visibility-route.</summary>
        public List<Vector3> BuildRoute(Vector3 start, Vector3 dest)
        {
            EnsureBuilt();

            var painted = TryPathGraphRoute(start, dest);
            if (painted != null) return painted;

            var res = VisibilityGraphRouter.Route(
                new Vector2(start.x, start.z), new Vector2(dest.x, dest.z), _set.Obstacles, _set.DoorWaypoints);
            // F: guard „cel nieosiągalny" (np. pokój bez drzwi = błąd projektowy gracza). Warn (throttled),
            // zwracamy degradowaną linię prostą — gameplay trwa, dev widzi że trzeba dodać drzwi.
            if (res.ViaFallback && Time.time - _lastFallbackWarnTime > 5f)
            {
                _lastFallbackWarnTime = Time.time;
                Log.Warn($"[DepotNavService] Brak trasy obstacle-free {start}→{dest} (cel odcięty? pokój bez drzwi). " +
                         "Degraded: linia prosta. Sprawdź drzwi/dostępność.");
            }
            return Lift(res.Path, start, dest);
        }

        /// <summary>Preferencja malowanego PathGraph: konektory (start→węzeł, węzeł→dest) nav-routed,
        /// malowany środek dosłownie. Walidacja: jeśli którakolwiek noga klipuje przeszkodę → null (pełny nav).</summary>
        List<Vector3> TryPathGraphRoute(Vector3 start, Vector3 dest)
        {
            if (_pathGraph == null) return null;
            int sn = _pathGraph.GetNearestNode(start, PathGraphSnapM);
            int en = _pathGraph.GetNearestNode(dest, PathGraphSnapM);
            if (sn < 0 || en < 0) return null;
            var nodePath = _pathGraph.FindPath(sn, en);
            if (nodePath == null || nodePath.Count == 0) return null;

            var result = new List<Vector3>();
            AddUnique(result, new Vector3(start.x, 0f, start.z));

            var first = _pathGraph.GetNode(nodePath[0]);
            if (first == null) return null;
            AppendNavConnector(result, start, first.Position);

            for (int i = 0; i < nodePath.Count; i++)
            {
                var n = _pathGraph.GetNode(nodePath[i]);
                if (n != null) AddUnique(result, new Vector3(n.Position.x, 0f, n.Position.z));
            }

            var last = _pathGraph.GetNode(nodePath[nodePath.Count - 1]);
            if (last == null) return null;
            AppendNavConnector(result, last.Position, dest);

            if (result.Count > 0) result[result.Count - 1] = dest;

            // Guard „nigdy przez obiekt": malowana trasa mogła być wytyczona przez miejsce gdzie później
            // postawiono ścianę/mebel. Jeśli któraś noga klipuje → odrzuć, użyj pełnego nav.
            for (int i = 1; i < result.Count; i++)
                if (!NavObstacles.SegmentClear(
                        new Vector2(result[i - 1].x, result[i - 1].z),
                        new Vector2(result[i].x, result[i].z), _set.Obstacles))
                    return null;

            return result;
        }

        void AppendNavConnector(List<Vector3> outp, Vector3 a, Vector3 b)
        {
            var r = VisibilityGraphRouter.Route(
                new Vector2(a.x, a.z), new Vector2(b.x, b.z), _set.Obstacles, _set.DoorWaypoints);
            for (int i = 0; i < r.Path.Count; i++)
                AddUnique(outp, new Vector3(r.Path[i].x, 0f, r.Path[i].y));
        }

        static void AddUnique(List<Vector3> l, Vector3 p)
        {
            if (l.Count == 0 || (l[l.Count - 1] - p).sqrMagnitude > 1e-4f) l.Add(p);
        }

        static List<Vector3> Lift(List<Vector2> path2, Vector3 start, Vector3 dest)
        {
            var outp = new List<Vector3>(path2.Count);
            for (int i = 0; i < path2.Count; i++)
                outp.Add(new Vector3(path2[i].x, 0f, path2[i].y));
            if (outp.Count > 0) outp[outp.Count - 1] = dest; // zachowaj dokładny cel (Y też)
            return outp;
        }

        // ── G: train-yield (dynamiczne pociągi) ───────────────────────

        /// <summary>
        /// TD-033: czy world-point leży w pasie ZAJĘTEGO toru (parked lub jadący skład — occupancy TD-031).
        /// Pracownik nie wchodzi w taki pas → czeka na krawędzi (level-crossing). Sprawdzane runtime przy
        /// każdym kroku (trasa statyczna, blokada dynamiczna).
        /// </summary>
        public bool IsBlockedByConsist(Vector3 worldPos)
        {
            Resolve();
            if (_trackGraph == null) return false;
            Vector2 p = new Vector2(worldPos.x, worldPos.z);
            foreach (var kv in _trackGraph.Tracks)
            {
                var poly = _trackGraph.GetTrackPolyline(kv.Key);
                if (poly == null || poly.Count < 2) continue;
                ProjectOntoPolyline(p, poly, out float lateral, out float along);
                if (lateral <= YieldLateralClearM &&
                    !_trackGraph.IsRangeFreeFor(kv.Key, along - YieldLongClearM, along + YieldLongClearM, -1))
                    return true;
            }
            return false;
        }

        /// <summary>Rzut punktu na polyline: lateral = odległość prostopadła do najbliższego segmentu,
        /// along = dystans wzdłuż polyline w track-local (= układ occupancy TD-031).</summary>
        static void ProjectOntoPolyline(Vector2 p, List<Vector3> poly, out float lateral, out float along)
        {
            lateral = float.MaxValue;
            along = 0f;
            float cum = 0f;
            for (int i = 1; i < poly.Count; i++)
            {
                Vector2 a = new Vector2(poly[i - 1].x, poly[i - 1].z);
                Vector2 b = new Vector2(poly[i].x, poly[i].z);
                Vector2 ab = b - a;
                float segLen = ab.magnitude;
                float t = segLen > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / (segLen * segLen)) : 0f;
                Vector2 proj = a + ab * t;
                float d = Vector2.Distance(p, proj);
                if (d < lateral) { lateral = d; along = cum + t * segLen; }
                cum += segLen;
            }
        }
    }
}
