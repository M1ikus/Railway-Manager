using UnityEngine;
using RailwayManager.Core;
using DepotSystem.Furniture;
using DepotSystem.OutdoorEquipment;

namespace DepotSystem
{
    /// <summary>
    /// MM-18: helper findowanie target track + access position dla service jobs.
    ///
    /// Outdoor equipment (WashZone/Turntable/PitLift/FuelStation/WaterService) i indoor
    /// furniture (wash_gate/fuel_pump/water_service/paint_bay w Hall) — pojazd musi
    /// fizycznie podjechać do stanowiska. Resolver znajduje najbliższy tor i punkt
    /// stop'u na nim.
    ///
    /// Threshold: max 5m od centroidu (Ok), 5-10m (Warning, postaw bliżej), >10m (Fail).
    /// </summary>
    public static class AccessTrackResolver
    {
        /// <summary>Próg odległości tor↔stanowisko: do 5m = Ok, accept gameplay.</summary>
        public const float MaxAccessDistanceMeters = 5f;
        /// <summary>Powyżej tego stanowisko jest w praktyce niedostępne torowo.</summary>
        public const float MaxAcceptableDistanceMeters = 10f;

        /// <summary>Wynik resolvowania.</summary>
        public struct ResolveResult
        {
            /// <summary>TrackId bestmatch'u (-1 = nie znaleziono w threshold).</summary>
            public int trackId;
            /// <summary>Punkt stopu na polyline toru (worldPos) — pojazd zatrzymuje się tutaj.</summary>
            public Vector3 accessPos;
            /// <summary>Odległość centroidu stanowiska od projekcji na tor [m].</summary>
            public float distanceMeters;
            /// <summary>true gdy distanceMeters &lt;= <see cref="MaxAcceptableDistanceMeters"/>.</summary>
            public bool isReachable;
            /// <summary>true gdy distanceMeters &lt;= <see cref="MaxAccessDistanceMeters"/> (no warning).</summary>
            public bool isClean;

            public override string ToString()
                => $"ResolveResult(track#{trackId}, pos={accessPos}, dist={distanceMeters:F2}m, " +
                   $"reachable={isReachable}, clean={isClean})";
        }

        // ── API ───────────────────────────────────────────────────

        /// <summary>
        /// Znajduje najbliższy tor do centroidu outdoor equipment.
        /// </summary>
        public static ResolveResult FindAccessTrackFor(PlacedOutdoorEquipment equipment, TrackGraph graph)
        {
            if (equipment == null || graph == null)
                return Empty();

            Vector3 centroid = (equipment.cornerA + equipment.cornerB) * 0.5f;
            return FindClosestTrack(centroid, graph);
        }

        /// <summary>
        /// Znajduje najbliższy tor do centroidu furniture (np. wash_gate w Hall).
        /// Dla furniture w Hall tor zwykle jest wewnątrz pomieszczenia (po MM-15 TrackGate).
        /// </summary>
        public static ResolveResult FindAccessTrackFor(PlacedFurnitureItem furniture, TrackGraph graph)
        {
            if (furniture == null || graph == null)
                return Empty();

            return FindClosestTrack(furniture.position, graph);
        }

        /// <summary>
        /// Generic — znajdź najbliższy tor od dowolnego world position.
        /// </summary>
        public static ResolveResult FindClosestTrack(Vector3 worldPos, TrackGraph graph)
        {
            var result = Empty();
            if (graph == null) return result;

            float bestDistSq = float.MaxValue;
            Vector3 bestProj = Vector3.zero;
            int bestTrackId = -1;

            foreach (var kvp in graph.Tracks)
            {
                var polyline = graph.GetTrackPolyline(kvp.Key);
                if (polyline == null || polyline.Count < 2) continue;

                for (int i = 0; i < polyline.Count - 1; i++)
                {
                    Vector3 a = polyline[i];
                    Vector3 b = polyline[i + 1];
                    Vector3 ab = b - a;
                    float lenSq = ab.sqrMagnitude;
                    if (lenSq < 0.001f) continue;

                    float t = Mathf.Clamp01(Vector3.Dot(worldPos - a, ab) / lenSq);
                    Vector3 proj = a + ab * t;
                    float dSq = (worldPos - proj).sqrMagnitude;

                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestProj = proj;
                        bestTrackId = kvp.Key;
                    }
                }
            }

            if (bestTrackId < 0)
                return result;

            float dist = Mathf.Sqrt(bestDistSq);
            result.trackId = bestTrackId;
            result.accessPos = bestProj;
            result.distanceMeters = dist;
            result.isReachable = dist <= MaxAcceptableDistanceMeters;
            result.isClean = dist <= MaxAccessDistanceMeters;
            return result;
        }

        // ── Cache + lookup convenience ────────────────────────────

        /// <summary>Lazy lookup TrackGraph przez DepotServices cache (zero-cost on hit).</summary>
        public static TrackGraph FindGraph()
            => DepotServices.Get<TrackGraph>();

        /// <summary>Convenience overload bez explicit graph (lazy lookup).</summary>
        public static ResolveResult FindAccessTrackFor(PlacedOutdoorEquipment equipment)
            => FindAccessTrackFor(equipment, FindGraph());

        /// <summary>Convenience overload bez explicit graph (lazy lookup).</summary>
        public static ResolveResult FindAccessTrackFor(PlacedFurnitureItem furniture)
            => FindAccessTrackFor(furniture, FindGraph());

        // ── Internal ──────────────────────────────────────────────

        static ResolveResult Empty() => new ResolveResult
        {
            trackId = -1,
            accessPos = Vector3.zero,
            distanceMeters = float.MaxValue,
            isReachable = false,
            isClean = false,
        };
    }
}
