using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    public partial class TrackBuildStateMachine
    {
        // ═══════════════════════════════════════════
        //  BUDOWANIE TORU — final placement A→B
        // ═══════════════════════════════════════════

        /// <summary>
        /// Buduje tor A→B z odpowiednią geometrią.
        /// </summary>
        private bool BuildTrackAB(out PlacedTrackSegment builtSegment, out Vector3 endDirection)
        {
            builtSegment = null;
            endDirection = Vector3.zero;
            if (trackBuilder == null || trackGraph == null) return false;

            bool bothSnapped = snappedNodeA >= 0 && snappedNodeB >= 0;

            List<Vector3> polyline = previewIsValid && previewPolyline != null && previewPolyline.Count >= 2
                ? new List<Vector3>(previewPolyline)
                : null;

            if (polyline == null && bothSnapped)
            {
                // Oba snappowane: CSC z surowych kierunków (U-turny, łączenie torów)
                Vector3 dirA = trackGraph.GetNodeDirection(snappedNodeA);
                Vector3 dirB = trackGraph.GetNodeDirection(snappedNodeB);

                // Blokuj gdy oba outward wskazują OD SIEBIE (duplikat istniejącego toru)
                Vector3 abDir = (pointB - pointA).normalized;
                if (Vector3.Dot(dirA, abDir) < -0.3f && Vector3.Dot(dirB, -abDir) < -0.3f)
                {
                    previewValidationReason = TrackBuildValidationReason.DuplicateExistingTrack;
                    Log.Warn("[TrackBuildStateMachine] Blocked: would duplicate existing track");
                    return false;
                }

                polyline = TrackGeometry.CalculateRouteAutoFit(pointA, dirA, pointB, dirB, minRadius, true);
            }
            else if (polyline == null)
            {
                // Jeden łuk — identyczna logika jak UpdatePreview
                Vector3 s = pointA; s.y = 0;
                Vector3 e = pointB; e.y = 0;

                // Jeśli tylko koniec jest snappowany — zamień perspektywę
                bool reverseResult = false;
                int activeSnap = snappedNodeA;

                if (snappedNodeA < 0 && snappedNodeB >= 0)
                {
                    Vector3 tmp = s; s = e; e = tmp;
                    activeSnap = snappedNodeB;
                    reverseResult = true;
                }

                Vector3 dirA = GetDirectionForPoint(s, activeSnap, e);
                Vector3 dA = dirA; dA.y = 0; dA.Normalize();
                Vector3 dB = (e - s).normalized;

                // Sprawdź prostą
                Vector3 toEnd = (e - s).normalized;
                float angle = Vector3.Angle(dA, toEnd);

                float distance = Vector3.Distance(s, e);

                bool isSnapped = activeSnap >= 0;

                // Kursor "za plecami" snap pointa — nie pozwól na prostą, wymuś łuk
                bool cursorBehind = isSnapped && Vector3.Dot(dA, toEnd) < 0f;

                float actualRadius = TrackGeometry.CalculateRequiredRadius(s, dA, e, dB);

                if (advancedMode && manualRadius > 0)
                    actualRadius = manualRadius;

                bool radiusInfinite = float.IsInfinity(actualRadius) || float.IsNaN(actualRadius)
                    || actualRadius > 100000f;

                if (cursorBehind && radiusInfinite)
                {
                    previewValidationReason = TrackBuildValidationReason.NoValidRoute;
                    return false;
                }

                bool canBeStraight = !cursorBehind
                    && ((isSnapped && (radiusInfinite || actualRadius > distance * 30f))
                        || (!isSnapped && (angle < TrackGeometry.STRAIGHT_THRESHOLD_DEG
                                           || actualRadius > distance * 5f)));

                if (canBeStraight)
                {
                    if (isSnapped)
                    {
                        // Rzutuj punkt końcowy na linię kierunku snap pointa → idealny kąt 0°
                        float projLen = Vector3.Dot(e - s, dA);
                        e = s + dA * projLen;
                    }
                    polyline = TrackGeometry.GenerateStraightLine(s, e);
                }
                else
                {
                    actualRadius = Mathf.Max(actualRadius, minRadius);
                    polyline = TrackGeometry.CalculateArcRoute(s, dA, e, dB, actualRadius);
                }

                // Odwróć polyline jeśli zamieniliśmy perspektywę (snap na końcu)
                if (reverseResult && polyline != null)
                    polyline.Reverse();
            }

            if (polyline == null || polyline.Count < 2)
            {
                previewValidationReason = TrackBuildValidationReason.NoValidRoute;
                Log.Warn("[TrackBuildStateMachine] Failed to calculate route");
                return false;
            }

            // M-Economy Faza 5: blokada „nie stać → nie buduj". Faktyczne pobranie (suppress-aware)
            // dzieje się w PrefabTrackBuilder.PlaceTrackVisuals — tu tylko sprawdzamy czy stać.
            long trackCost = ConstructionCosts.TrackGroszy(TrackGeometry.CalculatePolylineLength(polyline));
            if (!ConstructionBilling.CanAfford(trackCost))
            {
                Log.Warn($"[TrackBuildStateMachine] Brak srodkow na tor ({trackCost / 100} zl) → blocked");
                return false;
            }

            // Zbuduj tor
            var segment = trackBuilder.PlaceTrackWithPolyline(polyline, null, DepotTrackType.Parking);
            builtSegment = segment;

            if (segment != null && segment.GraphTrackId >= 0)
            {
                var resultPolyline = segment.Polyline != null && segment.Polyline.Count >= 2
                    ? segment.Polyline
                    : polyline;
                endDirection = TrackGeometry.GetEndTangent(resultPolyline).normalized;
                OnTrackBuilt?.Invoke(segment.GraphTrackId);
                Log.Info($"[TrackBuildStateMachine] Built track A→B, graphId={segment.GraphTrackId}, length={segment.Length:F1}m");
                return true;
            }

            previewValidationReason = TrackBuildValidationReason.NoValidRoute;
            return false;
        }

        /// <summary>
        /// Oblicza kierunek dla punktu budowy.
        /// Snapped: kierunek "na zewnątrz" od istniejącego toru (jedyny sensowny kierunek budowy).
        /// Nowy: kierunek do drugiego punktu.
        /// </summary>
        private Vector3 GetDirectionForPoint(Vector3 point, int snappedNodeId, Vector3 otherPoint)
        {
            if (hasPinnedStartDirection && IsCurrentStartPoint(point) && directionA.sqrMagnitude > 0.0001f)
                return directionA.normalized;

            if (snappedNodeId >= 0 && trackGraph != null)
                return trackGraph.GetNodeDirection(snappedNodeId);

            return (otherPoint - point).normalized;
        }
    }
}
