using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public partial class TurnoutPlacementStateMachine
    {
        // ═══════════════════════════════════════════
        //  VALIDATION — overlap, buildable area, snap, endpoint extensibility
        // ═══════════════════════════════════════════

        /// <summary>
        /// Sprawdza czy noga odgałęziająca nachodziłaby geometrycznie na inne tory
        /// (nie używa fizyki — porównuje polyline z PlacedTracks).
        /// Segmenty chain są wykluczone bo zostaną zastąpione.
        /// </summary>
        private bool IsTurnoutOverlappingBuildings(StraightChain chain, float dist,
            TurnoutData.TurnoutDefinition def)
        {
            if (chain.Segments.Count == 0) return false;

            var (origin, dir) = TrackGeometry.GetPointAtDistance(chain.MergedPolyline, dist);
            Vector3 effectiveDir = flipDirection ? -dir : dir;

            var (straightLeg, divergingLeg) = TurnoutData.GenerateTurnoutGeometry(origin, effectiveDir, def, divergeLeft);

            // Sprawdź oba ramiona rozjazdu
            foreach (var leg in new[] { straightLeg, divergingLeg })
            {
                if (leg == null || leg.Count < 2) continue;
                float totalLen = TrackGeometry.CalculatePolylineLength(leg);
                for (float t = 0f; t <= totalLen; t += 2f)
                {
                    var (pt, _) = TrackGeometry.GetPointAtDistance(leg, t);
                    Collider[] hits = Physics.OverlapBox(
                        new Vector3(pt.x, 1.5f, pt.z),
                        new Vector3(1.2f, 2f, 1.2f),
                        Quaternion.identity);
                    foreach (var hit in hits)
                        if (hit.CompareTag("Wall")) return true;
                }
            }
            return false;
        }

        private bool IsTurnoutOverlapping(StraightChain chain, float dist,
            TurnoutData.TurnoutDefinition def, float overlapDist = 0.9f, float sampleStep = 1.5f)
        {
            if (trackBuilder == null) return false;
            if (chain.Segments.Count == 0) return false; // freestanding

            var (origin, dir) = TrackGeometry.GetPointAtDistance(chain.MergedPolyline, dist);
            Vector3 effectiveDir = flipDirection ? -dir : dir;

            var (_, divergingLeg) = TurnoutData.GenerateTurnoutGeometry(origin, effectiveDir, def, divergeLeft);
            if (divergingLeg == null || divergingLeg.Count < 2) return false;

            float totalLen = TrackGeometry.CalculatePolylineLength(divergingLeg);
            const float endExclude = 1.5f;
            if (totalLen <= endExclude * 2f) return false;

            // Zbierz polyline chain segmentów do wykluczenia (będą usunięte)
            var excludedPolylines = new System.Collections.Generic.HashSet<List<Vector3>>();
            foreach (var seg in chain.Segments)
                if (seg.Polyline != null) excludedPolylines.Add(seg.Polyline);

            float t = endExclude;
            while (t < totalLen - endExclude)
            {
                var (pt, _) = TrackGeometry.GetPointAtDistance(divergingLeg, t);
                Vector2 pt2 = new Vector2(pt.x, pt.z);

                foreach (var placed in trackBuilder.PlacedTracks)
                {
                    if (placed.Polyline == null || placed.Polyline.Count < 2) continue;
                    if (excludedPolylines.Contains(placed.Polyline)) continue;

                    if (IsPointNearPolyline2D(pt2, placed.Polyline, overlapDist, endExclude))
                        return true;
                }
                t += sampleStep;
            }
            return false;
        }

        private static bool IsPointNearPolyline2D(Vector2 pt, List<Vector3> poly,
            float threshold, float endExclude)
        {
            float polyLen = 0f;
            for (int i = 1; i < poly.Count; i++)
                polyLen += Vector2.Distance(new Vector2(poly[i-1].x, poly[i-1].z),
                                            new Vector2(poly[i].x,   poly[i].z));

            float walked = 0f;
            for (int i = 1; i < poly.Count; i++)
            {
                Vector2 a = new Vector2(poly[i-1].x, poly[i-1].z);
                Vector2 b = new Vector2(poly[i].x,   poly[i].z);
                float segLen = Vector2.Distance(a, b);

                float midWalked = walked + segLen * 0.5f;
                if (midWalked < endExclude || midWalked > polyLen - endExclude)
                {
                    walked += segLen;
                    continue;
                }

                float d = DistPointSeg2D(pt, a, b);
                if (d < threshold) return true;
                walked += segLen;
            }
            return false;
        }

        private static float DistPointSeg2D(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + t * ab);
        }

        /// <summary>
        /// Walidacja z uwzględnieniem flipDirection:
        /// Przód = rozjazd rośnie w kierunku chain (potrzeba miejsca ZA origin)
        /// Tył = rozjazd rośnie w tył (potrzeba miejsca PRZED origin)
        /// </summary>
        private bool CanPlaceWithFlip(StraightChain chain, float dist, TurnoutData.TurnoutDefinition def)
        {
            if (turnoutPlacer == null) return false;

            // Freestanding chain (brak połączonych segmentów) → zawsze valid (jeśli mieści się w obszarze)
            if (chain.Segments.Count == 0)
                return IsTurnoutInsideBuildableArea(chain, dist, def, divergeLeft);

            bool atChainEnd = Mathf.Abs(dist - chain.TotalLength) < 0.01f;
            bool atChainStart = dist < 0.01f;

            // Sprawdź czy endpoint na danym końcu chain jest wolny (extensible)
            bool startFree = atChainStart && IsChainEndpointFree(chain.StartPos, chain);
            bool endFree = atChainEnd && IsChainEndpointFree(chain.EndPos, chain);

            if (flipDirection)
            {
                // Flip: body rośnie do tyłu. Potrzeba def.Length PRZED origin.
                // Wolny endpoint na początku LUB na końcu chain → body wychodzi za chain → OK
                if (startFree || endFree)
                { /* OK - body extends beyond free endpoint */ }
                else if (dist < def.Length || dist > chain.TotalLength)
                    return false;
                // Flip: body startuje dokładnie na chain.StartPos? Musi być wolny koniec.
                else if (Mathf.Abs(dist - def.Length) < 0.1f && !IsChainEndpointFree(chain.StartPos, chain))
                    return false;
            }
            else
            {
                // Forward: body rośnie do przodu. Potrzeba def.Length ZA origin.
                // Wolny endpoint na końcu LUB na początku chain → body wychodzi za chain → OK
                if (endFree || startFree)
                { /* OK - body extends beyond free endpoint */ }
                else if (!turnoutPlacer.CanPlaceTurnoutOnChain(chain, dist, def))
                    return false;
            }

            // Sprawdź czy cały rozjazd mieści się w buildable area
            return IsTurnoutInsideBuildableArea(chain, dist, def, divergeLeft);
        }

        private bool IsTurnoutInsideBuildableArea(StraightChain chain, float dist,
            TurnoutData.TurnoutDefinition def, bool divLeft)
        {
            if (!buildableArea.HasValue) return true;
            var ba = buildableArea.Value;

            var (origin, dir) = TrackGeometry.GetPointAtDistance(chain.MergedPolyline, dist);
            Vector3 effectiveDir = flipDirection ? -dir : dir;
            Vector3 farEnd = origin + effectiveDir * def.Length;
            Vector3 divEnd = TurnoutData.GetDivergingEndpoint(origin, effectiveDir, def, divLeft);

            return IsPointInBA(ba, origin) && IsPointInBA(ba, farEnd) && IsPointInBA(ba, divEnd);
        }

        private static bool IsPointInBA(Bounds ba, Vector3 p)
        {
            return p.x >= ba.min.x && p.x <= ba.max.x
                && p.z >= ba.min.z && p.z <= ba.max.z;
        }

        private float SnapToChainEndpoint(StraightChain chain, float distAlong)
        {
            // Freestanding chain (brak segmentów) — zawsze stawiaj od początku
            if (chain.Segments.Count == 0) return 0f;

            // Wolny koniec toru (degree 1): większa strefa snapu (def ~15m, więc snap 3m)
            const float endpointSnapRange = 3f;

            if (distAlong < endpointSnapRange && IsChainEndpointFree(chain.StartPos, chain))
                return 0f;
            if (chain.TotalLength - distAlong < endpointSnapRange && IsChainEndpointFree(chain.EndPos, chain))
                return chain.TotalLength;

            // Junction (degree 3+): snap do 1m od końca (nie do dokładnego 0/TotalLength)
            if (distAlong < 1f)
                return Mathf.Min(1f, chain.TotalLength * 0.5f);
            if (chain.TotalLength - distAlong < 1f)
                return Mathf.Max(0f, chain.TotalLength - Mathf.Min(1f, chain.TotalLength * 0.5f));

            return distAlong;
        }

        /// <summary>
        /// Sprawdza czy punkt końcowy chain nadaje się do przedłużenia rozjazdem.
        /// True jeśli: degree-1 (wolny koniec), LUB degree>1 ale wszystkie dodatkowe
        /// krawędzie to łuki odgałęziające (nie ma prostego toru w kierunku chain).
        /// </summary>
        private bool IsChainEndpointFree(Vector3 position, StraightChain chain = null)
        {
            var tg = DepotServices.Get<TrackGraph>();
            if (tg == null) return false;
            int nodeId = tg.FindNodeAtPosition(position);
            if (nodeId < 0) return false;
            if (tg.Nodes[nodeId].EdgeIds.Count == 1) return true;
            // Degree > 1: sprawdź czy żaden sąsiad spoza chain nie jest prostą współliniową
            if (chain != null && turnoutPlacer != null)
                return turnoutPlacer.IsEndpointExtensible(position, chain.Direction, chain);
            return false;
        }
    }
}
