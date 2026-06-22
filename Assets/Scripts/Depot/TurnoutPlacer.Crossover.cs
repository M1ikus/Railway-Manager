using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    public partial class TurnoutPlacer
    {
        // ═══════════════════════════════════════════
        //  CROSSOVER (ROZJAZD KRZYŻOWY R190) — X-pattern
        // ═══════════════════════════════════════════

        /// <summary>
        /// Stawia rozjazd krzyżowy na jednym prostym torze.
        /// Tworzy: pre-segment + body (prosta) + post-segment + 2 nogi odgałęziające (lewo + prawo).
        /// TD-035: billing lustrem <see cref="PlaceTurnoutOnChain"/> — pre-check + charge mechanizmu
        /// (KrzyzownicaPodwojnaGroszy przez ConstructionCosts.TurnoutGroszy), silence undo (1 komenda),
        /// Original* metadata na encji (RemoveTurnout odtwarza tor + undo działa).
        /// </summary>
        public bool PlaceCrossoverOnChain(StraightChain chain, float distAlongChain,
            TurnoutData.TurnoutDefinition def, bool flip = false, bool divergeLeft = true)
        {
            if (trackBuilder == null) trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
            if (trackGraph == null) trackGraph = DepotServices.Get<TrackGraph>();
            if (trackBuilder == null || trackGraph == null) return false;
            if (chain == null) return false;

            // TD-035: koszt MECHANIZMU krzyżownicy (geometria torów liczona osobno w track wiring).
            // Blokada „nie stać → nie buduj"; pomijana przy load (suppress). Charge dopiero po sukcesie.
            long turnoutCost = ConstructionCosts.TurnoutGroszy(def.Name);
            if (!ConstructionBilling.SuppressCharging && !ConstructionBilling.CanAfford(turnoutCost))
            {
                Log.Warn($"[TurnoutPlacer] Brak srodkow na krzyzownice {def.Name} ({turnoutCost / 100} zl) → blocked");
                return false;
            }

            // End-of-track: rozjazd może wystawać poza chain (body przedłuża tor)
            bool atEnd = Mathf.Abs(distAlongChain - chain.TotalLength) < 0.01f;
            bool atStart = distAlongChain < 0.01f;
            bool atBoundary = atEnd || atStart;

            // Walidacja zależy od kierunku
            if (flip)
            {
                if (!atBoundary && distAlongChain < def.Length) return false;
            }
            else
            {
                if (!atBoundary && distAlongChain + def.Length > chain.TotalLength) return false;
            }

            // Zachowaj metadane z pierwszego segmentu (może być syntetyczny — bez segmentów)
            DepotTrackData firstTrackData = chain.Segments.Count > 0
                ? trackGraph.GetTrack(chain.Segments[0].GraphTrackId) : null;
            string baseName = firstTrackData != null ? firstTrackData.Name : "Tor";
            DepotTrackType trackType = firstTrackData != null ? firstTrackData.TrackType : DepotTrackType.Parking;

            // TD-035: zachowaj oryginalną polyline — do odtworzenia po usunięciu krzyżownicy
            var originalPolyline = new List<Vector3>(chain.MergedPolyline);

            // Oblicz punkt wstawienia
            var (turnoutOrigin, trackDir) = TrackGeometry.GetPointAtDistance(chain.MergedPolyline, distAlongChain);
            Vector3 effectiveDir = flip ? -trackDir : trackDir;
            Vector3 turnoutFarEnd = turnoutOrigin + effectiveDir * def.Length;

            // X-pattern: 2 łuki krzyżujące się + 2 proste (główna + przekątna)
            var (_, frontLeg) = TurnoutData.GenerateTurnoutGeometry(turnoutOrigin, effectiveDir, def, divergeLeft);
            var (_, backLeg) = TurnoutData.GenerateTurnoutGeometry(turnoutFarEnd, -effectiveDir, def, divergeLeft);

            // Wymuś pierwszy punkt = origin/farEnd
            if (frontLeg != null && frontLeg.Count >= 2) frontLeg[0] = turnoutOrigin;
            if (backLeg != null && backLeg.Count >= 2) backLeg[0] = turnoutFarEnd;

            // Przekątna prosta: łączy koniec obu łuków
            Vector3 frontLegEnd = TurnoutData.GetDivergingEndpoint(turnoutOrigin, effectiveDir, def, divergeLeft);
            Vector3 backLegEnd = TurnoutData.GetDivergingEndpoint(turnoutFarEnd, -effectiveDir, def, divergeLeft);
            List<Vector3> diagonalStraight = TrackGeometry.GenerateStraightLine(frontLegEnd, backLegEnd);

            // TD-035: silence undo — cała operacja to jeden logiczny krok, 1 TurnoutPlacedCommand na końcu
            bool wasSilenced = DepotSystem.Undo.UndoManager.Silenced;
            DepotSystem.Undo.UndoManager.Silenced = true;
            TurnoutEntity turnoutEntity;
            var memberIds = new List<int>();
            try
            {
                // === Usuń WSZYSTKIE segmenty łańcucha ===
                foreach (var seg in chain.Segments)
                {
                    if (seg.GraphTrackId >= 0)
                        trackBuilder.RemoveTrack(seg.GraphTrackId);
                }

                // === Twórz nowe segmenty ===
                // Pre/body/post zależą od kierunku (jak w PlaceTurnoutOnChain)
                Vector3 preStart = chain.StartPos;
                Vector3 preEnd, bodyStart, bodyEnd, postStart, postEnd;

                if (!flip)
                {
                    preEnd = turnoutOrigin;
                    bodyStart = turnoutOrigin;
                    bodyEnd = turnoutFarEnd;
                    postStart = turnoutFarEnd;
                    postEnd = chain.EndPos;
                }
                else
                {
                    preEnd = turnoutFarEnd;
                    bodyStart = turnoutFarEnd;
                    bodyEnd = turnoutOrigin;
                    postStart = turnoutOrigin;
                    postEnd = chain.EndPos;
                }

                const float MIN_SEGMENT = 0.01f;

                // 1. Pre-segment (dziedziczy sieć z oryginalnego toru)
                // Pomiń gdy rozjazd wystaje poza chain start → pre szedłby do tyłu
                if (Vector3.Distance(preStart, preEnd) >= MIN_SEGMENT
                    && Vector3.Dot(preEnd - preStart, chain.Direction) > 0f)
                {
                    var prePoly = TrackGeometry.GenerateStraightLine(preStart, preEnd);
                    var preSeg = trackBuilder.PlaceTrackWithPolyline(prePoly, baseName, trackType);
                }

                // 2. Body: prosta noga rozjazdu (member — BEZ pre/post)
                if (Vector3.Distance(bodyStart, bodyEnd) >= MIN_SEGMENT)
                {
                    var bodyPoly = TrackGeometry.GenerateStraightLine(bodyStart, bodyEnd);
                    var bodySeg = trackBuilder.PlaceTrackWithPolyline(
                        bodyPoly, $"Rozjazd {def.Name} (prosta)", trackType);
                    if (bodySeg != null)
                    {
                        memberIds.Add(bodySeg.GraphTrackId);
                    }
                }

                // 3. Post-segment
                // Pomiń gdy rozjazd wystaje poza chain end → post szedłby do tyłu
                if (Vector3.Distance(postStart, postEnd) >= MIN_SEGMENT
                    && Vector3.Dot(postEnd - postStart, chain.Direction) > 0f)
                {
                    var postPoly = TrackGeometry.GenerateStraightLine(postStart, postEnd);
                    var postSeg = trackBuilder.PlaceTrackWithPolyline(postPoly, baseName, trackType);
                }

                // 4. Łuki odgałęziające — 2 sztuki tworzące X
                if (frontLeg != null && frontLeg.Count >= 2)
                {
                    var seg = trackBuilder.PlaceTrackWithPolyline(frontLeg, $"{def.Name} (przód)", DepotTrackType.Maneuver);
                    if (seg != null) memberIds.Add(seg.GraphTrackId);
                }

                if (backLeg != null && backLeg.Count >= 2)
                {
                    var seg = trackBuilder.PlaceTrackWithPolyline(backLeg, $"{def.Name} (tył)", DepotTrackType.Maneuver);
                    if (seg != null) memberIds.Add(seg.GraphTrackId);
                }

                // 5. Przekątna prosta (przejazd lewo↔prawo, łączy końce łuków)
                if (diagonalStraight != null && diagonalStraight.Count >= 2
                    && Vector3.Distance(frontLegEnd, backLegEnd) >= MIN_SEGMENT)
                {
                    var seg = trackBuilder.PlaceTrackWithPolyline(
                        diagonalStraight, $"Rozjazd {def.Name} (przekątna)", DepotTrackType.Maneuver);
                    if (seg != null) memberIds.Add(seg.GraphTrackId);
                }

                // Rejestruj rozjazd krzyżowy jako TurnoutEntity (TD-035: + Original* metadata do
                // restore po usunięciu + undo — wcześniej brak → dziura w torze po remove)
                turnoutEntity = new TurnoutEntity(def.Name, TurnoutEntityType.Crossover)
                {
                    Origin = turnoutOrigin,
                    Direction = effectiveDir.normalized,
                    DivergeLeft = divergeLeft,
                    Definition = def,
                    MemberTrackIds = memberIds,
                    OriginalPolyline = originalPolyline,
                    OriginalTrackName = baseName,
                    OriginalTrackType = trackType,
                    FlipDirection = flip,
                    DistAlongChain = distAlongChain
                };
                if (memberIds.Count > 0)
                    trackBuilder.RegisterTurnout(turnoutEntity);
            }
            finally
            {
                DepotSystem.Undo.UndoManager.Silenced = wasSilenced;
            }

            // TD-035: charge mechanizmu + 1 komenda undo PO sukcesie (jak PlaceTurnoutOnChain)
            if (memberIds.Count > 0)
            {
                ConstructionBilling.Charge(turnoutCost, "construction_turnout", def.Name);
                DepotSystem.Undo.UndoManager.Record(
                    DepotSystem.Undo.UndoCategory.Tory,
                    new DepotSystem.Undo.TurnoutPlacedCommand(turnoutEntity.TurnoutId));
            }

            // Odśwież snap pointy
            if (snapSystem != null) snapSystem.RefreshAllSnapPoints();

            return true;
        }
    }
}
