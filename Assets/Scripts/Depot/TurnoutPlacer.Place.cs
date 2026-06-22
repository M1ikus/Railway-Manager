using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    public partial class TurnoutPlacer
    {
        // ═══════════════════════════════════════════
        //  TURNOUT PLACEMENT — single turnout (R190/R300)
        // ═══════════════════════════════════════════

        /// <summary>
        /// Sprawdza czy można postawić rozjazd na łańcuchu w danym punkcie.
        /// </summary>
        public bool CanPlaceTurnoutOnChain(StraightChain chain, float distAlongChain,
            TurnoutData.TurnoutDefinition def)
        {
            if (chain == null || chain.MergedPolyline.Count < 2) return false;
            float remaining = chain.TotalLength - distAlongChain;
            if (remaining < def.Length || distAlongChain < 0f)
                return false;

            // Noga ciała nie może kończyć się dokładnie na junctionie (tylko na wolnym końcu toru)
            if (Mathf.Abs(remaining - def.Length) < 0.1f)
            {
                if (trackGraph == null) trackGraph = DepotServices.Get<TrackGraph>();
                int endNodeId = trackGraph != null ? trackGraph.FindNodeAtPosition(chain.EndPos) : -1;
                if (endNodeId >= 0 && trackGraph.Nodes.ContainsKey(endNodeId)
                    && trackGraph.Nodes[endNodeId].EdgeIds.Count > 2)
                    return false;
            }

            // Sprawdź czy nowy rozjazd nie nachodzi na istniejący
            var (newOrigin, _) = TrackGeometry.GetPointAtDistance(chain.MergedPolyline, distAlongChain);
            float newStart = distAlongChain;
            float newEnd = distAlongChain + def.Length;

            foreach (var seg in chain.Segments)
            {
                if (!IsTurnoutMember(seg.GraphTrackId)) continue;

                // Oblicz zakres tego segmentu w chain distance
                float segStart = TrackGeometry.ProjectPointOnPolyline(chain.MergedPolyline, seg.StartPosition);
                float segEnd = TrackGeometry.ProjectPointOnPolyline(chain.MergedPolyline, seg.EndPosition);
                if (segStart > segEnd) (segStart, segEnd) = (segEnd, segStart);

                // Sprawdź overlap
                if (newStart < segEnd && newEnd > segStart)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Stawia rozjazd na łańcuchu połączonych odcinków prostych.
        /// Rozjazd całkowicie zastępuje łańcuch: jedna prosta od chainStart do chainEnd
        /// + noga odgałęziająca od punktu wstawienia.
        /// </summary>
        public int PlaceTurnoutOnChain(StraightChain chain, float distAlongChain,
            TurnoutData.TurnoutDefinition def, bool divergeLeft, bool flipDirection = false)
        {
            // M-Economy Faza 5: koszt MECHANIZMU rozjazdu (geometria torów liczona osobno w track wiring).
            // Blokada „nie stać → nie buduj"; pomijana przy load (suppress). Charge dopiero po sukcesie.
            long turnoutCost = ConstructionCosts.TurnoutGroszy(def.Name);
            if (!ConstructionBilling.SuppressCharging && !ConstructionBilling.CanAfford(turnoutCost))
            {
                Log.Warn($"[TurnoutPlacer] Brak srodkow na rozjazd {def.Name} ({turnoutCost / 100} zl) → blocked");
                return -1;
            }

            // Silence undo — cała operacja to jeden logiczny krok, nagrywamy 1 TurnoutPlacedCommand na końcu
            bool wasSilenced = DepotSystem.Undo.UndoManager.Silenced;
            DepotSystem.Undo.UndoManager.Silenced = true;
            lastCreatedTurnoutId = -1;
            int result;
            int createdTurnoutId;
            try
            {
                result = PlaceTurnoutOnChainInternal(chain, distAlongChain, def, divergeLeft, flipDirection);
                createdTurnoutId = lastCreatedTurnoutId;
            }
            finally
            {
                DepotSystem.Undo.UndoManager.Silenced = wasSilenced;
            }

            // Nagraj undo PO odblokowaniu silence
            if (createdTurnoutId >= 0)
            {
                ConstructionBilling.Charge(turnoutCost, "construction_turnout", def.Name);
                DepotSystem.Undo.UndoManager.Record(
                    DepotSystem.Undo.UndoCategory.Tory,
                    new DepotSystem.Undo.TurnoutPlacedCommand(createdTurnoutId));
            }

            return result;
        }

        private int lastCreatedTurnoutId = -1;

        private int PlaceTurnoutOnChainInternal(StraightChain chain, float distAlongChain,
            TurnoutData.TurnoutDefinition def, bool divergeLeft, bool flipDirection = false)
        {
            if (trackBuilder == null) trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
            if (trackGraph == null) trackGraph = DepotServices.Get<TrackGraph>();
            if (trackBuilder == null || trackGraph == null) return -1;

            // Walidacja zależy od kierunku
            // End-of-track: rozjazd może wystawać poza chain (body przedłuża tor)
            bool atEnd = Mathf.Abs(distAlongChain - chain.TotalLength) < 0.01f;
            bool atStart = distAlongChain < 0.01f;

            bool atBoundary = atEnd || atStart;

            if (flipDirection)
            {
                // Flip: body rośnie do tyłu. Bypass jeśli na granicy chain (endpoint).
                if (!atBoundary && distAlongChain < def.Length) return -1;
                // Body startuje dokładnie na chain.StartPos z junctionem → zablokuj
                if (!atBoundary && Mathf.Abs(distAlongChain - def.Length) < 0.1f)
                {
                    int startNodeId = trackGraph.FindNodeAtPosition(chain.StartPos);
                    if (startNodeId >= 0 && trackGraph.Nodes.ContainsKey(startNodeId)
                        && trackGraph.Nodes[startNodeId].EdgeIds.Count > 2)
                        return -1;
                }
            }
            else
            {
                // Forward: body rośnie do przodu. Bypass jeśli na granicy chain (endpoint).
                if (!atBoundary && !CanPlaceTurnoutOnChain(chain, distAlongChain, def)) return -1;
            }

            // Zachowaj metadane z pierwszego segmentu (chain może być syntetyczny — bez segmentów)
            DepotTrackData firstTrackData = chain.Segments.Count > 0
                ? trackGraph.GetTrack(chain.Segments[0].GraphTrackId) : null;
            string baseName = firstTrackData != null ? firstTrackData.Name : "Tor";
            DepotTrackType trackType = firstTrackData != null ? firstTrackData.TrackType : DepotTrackType.Maneuver;

            // Zachowaj oryginalną polyline — do odtworzenia po usunięciu rozjazdu
            var originalPolyline = new List<Vector3>(chain.MergedPolyline);

            // Oblicz punkt wstawienia rozjazdu na merged polyline
            var (turnoutOrigin, trackDir) = TrackGeometry.GetPointAtDistance(chain.MergedPolyline, distAlongChain);

            // Flip: rozjazd rośnie w tył → odwróć kierunek generowania geometrii
            Vector3 effectiveDir = flipDirection ? -trackDir : trackDir;

            // Generuj geometrię nogi odgałęziającej
            var (_, divergingLeg) = TurnoutData.GenerateTurnoutGeometry(
                turnoutOrigin, effectiveDir, def, divergeLeft);

            // Wymuś pierwszy punkt diverging = turnoutOrigin
            if (divergingLeg != null && divergingLeg.Count >= 2)
                divergingLeg[0] = turnoutOrigin;

            // === Usuń WSZYSTKIE segmenty łańcucha ===
            foreach (var seg in chain.Segments)
            {
                if (seg.GraphTrackId >= 0)
                    trackBuilder.RemoveTrack(seg.GraphTrackId);
            }

            // === Twórz nowe segmenty ===
            // Łańcuch dzielony na 3 części: pre | body (rozjazd) | post
            // + noga odgałęziająca z junction node.
            //
            // Forward: pre=[chainStart → origin], body=[origin → farEnd], post=[farEnd → chainEnd]
            //   junction node = origin (3 krawędzie: pre, body, diverging)
            //
            // Backward: pre=[chainStart → farEnd], body=[farEnd → origin], post=[origin → chainEnd]
            //   junction node = origin (3 krawędzie: body, post, diverging)

            int divergingTrackId = -1;
            int bodyTrackId = -1;
            Vector3 turnoutFarEnd = turnoutOrigin + effectiveDir * def.Length;

            // Pozycje segmentów zależą od kierunku
            Vector3 preStart = chain.StartPos;
            Vector3 preEnd, bodyStart, bodyEnd, postStart, postEnd;

            if (!flipDirection)
            {
                preEnd = turnoutOrigin;
                bodyStart = turnoutOrigin;
                bodyEnd = turnoutFarEnd;
                postStart = turnoutFarEnd;
                postEnd = chain.EndPos;
            }
            else
            {
                preEnd = turnoutFarEnd;     // farEnd jest PRZED origin na łańcuchu
                bodyStart = turnoutFarEnd;
                bodyEnd = turnoutOrigin;
                postStart = turnoutOrigin;
                postEnd = chain.EndPos;
            }

            const float MIN_SEGMENT = 0.01f;

            // 1. Pre-segment: chainStart → początek rozjazdu (dziedziczy sieć z oryginalnego toru)
            // Pomiń gdy rozjazd wystaje poza chain start → pre szedłby do tyłu
            if (Vector3.Distance(preStart, preEnd) >= MIN_SEGMENT
                && Vector3.Dot(preEnd - preStart, chain.Direction) > 0f)
            {
                List<Vector3> prePoly = TrackGeometry.GenerateStraightLine(preStart, preEnd);
                var preSeg = trackBuilder.PlaceTrackWithPolyline(prePoly, baseName, trackType);
            }

            // 2. Body: prosta noga rozjazdu
            if (Vector3.Distance(bodyStart, bodyEnd) >= MIN_SEGMENT)
            {
                List<Vector3> bodyPoly = TrackGeometry.GenerateStraightLine(bodyStart, bodyEnd);
                var bodySeg = trackBuilder.PlaceTrackWithPolyline(
                    bodyPoly, $"Rozjazd {def.Name} (prosta)", trackType);
                if (bodySeg != null)
                {
                    bodyTrackId = bodySeg.GraphTrackId;
                }
            }

            // 3. Post-segment: koniec rozjazdu → chainEnd (dziedziczy sieć z oryginalnego toru)
            // Pomiń gdy rozjazd wystaje poza chain → post szedłby do tyłu
            if (Vector3.Distance(postStart, postEnd) >= MIN_SEGMENT
                && Vector3.Dot(postEnd - postStart, chain.Direction) > 0f)
            {
                List<Vector3> postPoly = TrackGeometry.GenerateStraightLine(postStart, postEnd);
                var postSeg = trackBuilder.PlaceTrackWithPolyline(postPoly, baseName, trackType);
            }

            // 4. Noga odgałęziająca — junction node na turnoutOrigin (3+ krawędzie)
            if (divergingLeg != null && divergingLeg.Count >= 2)
            {
                string side = divergeLeft ? "lewo" : "prawo";
                string dir = flipDirection ? "tył" : "przód";
                var divSeg = trackBuilder.PlaceTrackWithPolyline(
                    divergingLeg, $"Rozjazd {def.Name} ({dir}-{side})", DepotTrackType.Maneuver);
                if (divSeg != null)
                    divergingTrackId = divSeg.GraphTrackId;
            }

            // Rejestruj rozjazd jako TurnoutEntity
            var turnoutEntity = new TurnoutEntity(def.Name, TurnoutEntityType.Regular)
            {
                Origin = turnoutOrigin,
                Direction = effectiveDir.normalized,
                DivergeLeft = divergeLeft,
                Definition = def,
                OriginalPolyline = originalPolyline,
                OriginalTrackName = baseName,
                OriginalTrackType = trackType,
                FlipDirection = flipDirection,
                DistAlongChain = distAlongChain
            };
            if (bodyTrackId >= 0) turnoutEntity.MemberTrackIds.Add(bodyTrackId);
            if (divergingTrackId >= 0) turnoutEntity.MemberTrackIds.Add(divergingTrackId);
            if (turnoutEntity.MemberTrackIds.Count > 0)
                trackBuilder.RegisterTurnout(turnoutEntity);

            lastCreatedTurnoutId = turnoutEntity.TurnoutId;

            // Odśwież snap pointy
            if (snapSystem != null)
                snapSystem.RefreshAllSnapPoints();

            Log.Info($"[TurnoutPlacer] Placed {def.Name} on chain ({chain.Segments.Count} segments, " +
                      $"total={chain.TotalLength:F1}m), at dist={distAlongChain:F1}m, " +
                      $"diverge={(divergeLeft ? "left" : "right")}, flip={flipDirection}");

            return divergingTrackId;
        }

        /// <summary>
        /// Convenience: stawia rozjazd na pojedynczym torze (buduje chain automatycznie).
        /// </summary>
        public int PlaceTurnout(PlacedTrackSegment track, float distAlongTrack,
            TurnoutData.TurnoutDefinition def, bool divergeLeft)
        {
            var chain = FindStraightChain(track);
            if (chain == null) return -1;

            // Przelicz dist na merged chain
            float chainDist = ConvertDistToChain(chain, track, distAlongTrack);
            return PlaceTurnoutOnChain(chain, chainDist, def, divergeLeft);
        }

        /// <summary>
        /// Przelicza dystans na pojedynczym segmencie na dystans na merged chain.
        /// </summary>
        public float ConvertDistToChain(StraightChain chain, PlacedTrackSegment track, float distOnTrack)
        {
            float accumulated = 0f;
            foreach (var seg in chain.Segments)
            {
                if (seg == track)
                {
                    // Sprawdź czy segment jest odwrócony w łańcuchu
                    Vector3 segDir = (seg.Polyline[seg.Polyline.Count - 1] - seg.Polyline[0]).normalized;
                    bool reversed = Vector3.Dot(segDir, chain.Direction) < 0;

                    if (reversed)
                    {
                        float segLen = TrackGeometry.CalculatePolylineLength(seg.Polyline);
                        return accumulated + (segLen - distOnTrack);
                    }
                    return accumulated + distOnTrack;
                }
                accumulated += TrackGeometry.CalculatePolylineLength(seg.Polyline);
            }
            return distOnTrack; // fallback
        }
    }
}
