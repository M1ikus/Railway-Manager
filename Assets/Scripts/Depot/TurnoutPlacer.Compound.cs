using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    public partial class TurnoutPlacer
    {
        // ═══════════════════════════════════════════
        //  COMPOUND PLACEMENTS — pair (parallel) + branch with return
        // ═══════════════════════════════════════════

        /// <summary>
        /// Stawia parę rozjazdów na dwóch równoległych torach + wstawkę prostą między nimi.
        /// Rozjazd A: normalny, Rozjazd B: odwrócony (flipDirection odwrócony, divergeLeft odwrócony).
        /// defA i defB mogą być różnych typów (R190, R300, Crossover).
        /// </summary>
        public void PlaceTurnoutPairOnChains(
            StraightChain chainA, float distA,
            StraightChain chainB, float distB,
            TurnoutData.TurnoutDefinition defA, TurnoutData.TurnoutDefinition defB,
            bool divergeLeft, bool flipDirection)
        {
            // TD-035: pre-check SUMY mechanizmów pary (typ-aware: krzyżownica liczy 900M gr) —
            // bez tego po postawieniu A mogło zabraknąć na B (pół pary). Atomowość affordability.
            long pairCost = ConstructionCosts.TurnoutGroszy(defA.Name) + ConstructionCosts.TurnoutGroszy(defB.Name);
            if (!ConstructionBilling.SuppressCharging && !ConstructionBilling.CanAfford(pairCost))
            {
                Log.Warn($"[TurnoutPlacer] Brak srodkow na pare rozjazdow {defA.Name}+{defB.Name} " +
                         $"({pairCost / 100} zl) → blocked");
                return;
            }

            // 1. Postaw rozjazd A (główny)
            var (originA, dirA) = TrackGeometry.GetPointAtDistance(chainA.MergedPolyline, distA);
            Vector3 effectiveDirA = flipDirection ? -dirA : dirA;
            Vector3 divEndA = TurnoutData.GetDivergingEndpoint(originA, effectiveDirA, defA, divergeLeft);

            bool isCrossoverA = defA.Name == TurnoutData.Crossover_R190.Name;
            if (isCrossoverA)
                PlaceCrossoverOnChain(chainA, distA, defA, flipDirection, divergeLeft);
            else
                PlaceTurnoutOnChain(chainA, distA, defA, divergeLeft, flipDirection);

            // 2. Oblicz parametry rozjazdu B
            // Pożądany kierunek B = naprzeciw A
            Vector3 desiredDirB = -effectiveDirA;

            var (originB, dirB) = TrackGeometry.GetPointAtDistance(chainB.MergedPolyline, distB);

            // Ustal flipB na podstawie rzeczywistego kierunku chain B
            // Jeśli dirB jest zgodny z desiredDirB → nie flipuj; jeśli odwrotny → flipuj
            bool flipB = Vector3.Dot(dirB, desiredDirB) < 0f;
            Vector3 effectiveDirB = flipB ? -dirB : dirB;

            // Ustal stronę odgałęzienia B: odnoga ma iść ku odnodze A
            // divEndA to endpoint odnogi A — B powinno mieć odnogę po tej samej stronie świata
            Vector3 perp = Vector3.Cross(effectiveDirB, Vector3.up).normalized;
            Vector3 toA = (divEndA - originB).normalized;
            bool divergeLeftB = Vector3.Dot(perp, toA) < 0f;

            Vector3 divEndB = TurnoutData.GetDivergingEndpoint(originB, effectiveDirB, defB, divergeLeftB);

            // 3. Postaw rozjazd B (odwrócony) — może być innego typu niż A
            bool isCrossoverB = defB.Name == TurnoutData.Crossover_R190.Name;
            if (isCrossoverB)
                PlaceCrossoverOnChain(chainB, distB, defB, flipB, divergeLeftB);
            else
                PlaceTurnoutOnChain(chainB, distB, defB, divergeLeftB, flipB);

            // 4. Wstawka prosta: łączy endpoints obu odnóg
            if (Vector3.Distance(divEndA, divEndB) >= TrackGeometry.MIN_TRACK_LENGTH)
            {
                var insertPoly = TrackGeometry.GenerateStraightLine(divEndA, divEndB);
                var insertSeg = trackBuilder.PlaceTrackWithPolyline(insertPoly, "Wstawka prosta", DepotTrackType.Maneuver);

            }

            // Odśwież snap pointy
            if (snapSystem != null) snapSystem.RefreshAllSnapPoints();

            Log.Info($"[TurnoutPlacer] Placed turnout pair: A={defA.Name}, B={defB.Name}");
        }

        /// <summary>
        /// Stawia rozjazd + wstawkę prostą + łuk/rozjazd powrotny do toru równoległego.
        /// returnType: 0=łuk (ręczny promień), 1=R190 1:9, 2=R300 1:9.
        /// </summary>
        public void PlaceBranchWithReturn(
            StraightChain chain, float distAlong,
            TurnoutData.TurnoutDefinition mainDef, bool divergeLeft, bool flipDirection,
            float trackSpacing, int returnType, float returnRadius)
        {
            if (trackBuilder == null || trackGraph == null) return;

            // Zachowaj dane przed placement (PlaceTurnoutOnChain zmienia grafy)
            var (origin, dir) = TrackGeometry.GetPointAtDistance(chain.MergedPolyline, distAlong);
            Vector3 effectiveDir = flipDirection ? -dir : dir;
            Vector3 divEnd = TurnoutData.GetDivergingEndpoint(origin, effectiveDir, mainDef, divergeLeft);
            Vector3 divDir = TurnoutData.GetDivergingEndDirection(effectiveDir, mainDef, divergeLeft);

            // Oblicz insert length
            TurnoutData.TurnoutDefinition? returnDef = null;
            if (returnType == 1)
                returnDef = TurnoutData.R190_1_9;
            else if (returnType == 2)
                returnDef = TurnoutData.R300_1_9;

            var (insertLen, valid) = TurnoutData.ComputeBranchReturnInsert(
                mainDef, trackSpacing, returnRadius, returnDef);

            if (!valid || insertLen < -0.01f)
            {
                Log.Warn("[BranchReturn] Insert length invalid, aborting.");
                return;
            }

            // TD-035: pre-check SUMY mechanizmów (główny + powrotny; returnType 0=łuk → tylko główny)
            // przed postawieniem czegokolwiek — atomowość affordability.
            long branchCost = ConstructionCosts.TurnoutGroszy(mainDef.Name)
                + (returnDef.HasValue ? ConstructionCosts.TurnoutGroszy(returnDef.Value.Name) : 0L);
            if (!ConstructionBilling.SuppressCharging && !ConstructionBilling.CanAfford(branchCost))
            {
                Log.Warn($"[BranchReturn] Brak srodkow na rozjazdy brancha ({branchCost / 100} zl) → blocked");
                return;
            }

            float safeInsert = Mathf.Max(0f, insertLen);

            // 1. Postaw główny rozjazd
            PlaceTurnoutOnChain(chain, distAlong, mainDef, divergeLeft, flipDirection);

            // 2. Wstawka prosta (jeśli > min length)
            Vector3 insertEnd = divEnd + divDir * safeInsert;
            if (safeInsert >= TrackGeometry.MIN_TRACK_LENGTH)
            {
                var insertPoly = TrackGeometry.GenerateStraightLine(divEnd, insertEnd);
                trackBuilder.PlaceTrackWithPolyline(insertPoly, "Wstawka", DepotTrackType.Maneuver);
            }

            // 3. Powrót do toru równoległego
            if (returnType == 0)
            {
                // Łuk powrotny
                bool turnLeft = !divergeLeft;
                float alpha = mainDef.FrogAngle;
                var arcPoly = TurnoutData.GenerateReturnArc(insertEnd, divDir, returnRadius, alpha, turnLeft);

                if (arcPoly.Count >= 2)
                {
                    trackBuilder.PlaceTrackWithPolyline(arcPoly, $"Luk powrotny R={returnRadius:F0}", DepotTrackType.Maneuver);

                    // Krótki prosty segment za łukiem (nowy tor równoległy, 5m)
                    Vector3 arcEnd = arcPoly[arcPoly.Count - 1];
                    Vector3 parallelEnd = arcEnd + effectiveDir * 5f;
                    var parallelPoly = TrackGeometry.GenerateStraightLine(arcEnd, parallelEnd);
                    trackBuilder.PlaceTrackWithPolyline(parallelPoly, "Tor rownlegly", DepotTrackType.Maneuver);
                }
            }
            else
            {
                // Rozjazd powrotny — tworzymy segmenty bezpośrednio (bez PlaceTurnoutOnChain),
                // bo FindStraightChain mógłby wciągnąć istniejące sąsiednie tory i je skasować.
                PlaceReturnTurnoutDirectly(insertEnd, effectiveDir, divergeLeft, returnType);
            }

            // Odśwież snap pointy
            if (snapSystem != null) snapSystem.RefreshAllSnapPoints();

            Log.Info($"[BranchReturn] Placed: {mainDef.Name}, spacing={trackSpacing:F1}m, return={(returnType == 0 ? $"arc R={returnRadius:F0}" : (returnType == 1 ? "R190" : "R300"))}");
        }

        /// <summary>
        /// Tworzy rozjazd powrotny bezpośrednio z segmentów (bez FindStraightChain/PlaceTurnoutOnChain),
        /// żeby nie kasować istniejących sąsiednich torów.
        /// insertEnd = punkt połączenia wstawki z rozjazdem powrotnym (= koniec odnogi powrotnej).
        /// effectiveDir = kierunek prostego toru głównego rozjazdu.
        /// divergeLeft = strona odgałęzienia głównego rozjazdu.
        /// returnType = 1=R190, 2=R300.
        /// </summary>
        private void PlaceReturnTurnoutDirectly(Vector3 insertEnd, Vector3 effectiveDir,
            bool divergeLeft, int returnType)
        {
            var retDef = returnType == 1 ? TurnoutData.R190_1_9 : TurnoutData.R300_1_9;

            // Rozjazd powrotny: kierunek = -effectiveDir, divergeLeft = taki sam jak główny
            Vector3 retEffDir = -effectiveDir;
            bool retDivergeLeft = divergeLeft;

            // Origin tak, żeby koniec odnogi = insertEnd
            Vector3 divOffset = TurnoutData.GetDivergingEndpoint(Vector3.zero, retEffDir, retDef, retDivergeLeft);
            Vector3 retOrigin = insertEnd - divOffset;
            Vector3 retFarEnd = retOrigin + retEffDir * retDef.Length;

            // Generuj geometrię nogi odgałęziającej (= łącznik do wstawki)
            var (_, divergingLeg) = TurnoutData.GenerateTurnoutGeometry(
                retOrigin, retEffDir, retDef, retDivergeLeft);

            if (divergingLeg != null && divergingLeg.Count >= 2)
                divergingLeg[0] = retOrigin;

            const float MIN_SEGMENT = 0.01f;
            float margin = 5f;

            int bodyTrackId = -1;
            int divergingTrackId = -1;

            // 1. Pre-segment: tor równoległy PRZED rozjazdem (w kierunku effectiveDir od retOrigin)
            Vector3 preEnd = retOrigin + effectiveDir * margin;
            if (Vector3.Distance(retOrigin, preEnd) >= MIN_SEGMENT)
            {
                var prePoly = TrackGeometry.GenerateStraightLine(preEnd, retOrigin);
                trackBuilder.PlaceTrackWithPolyline(prePoly, "Tor rownlegly", DepotTrackType.Maneuver);
            }

            // 2. Body: prosta noga rozjazdu powrotnego (retOrigin → retFarEnd)
            if (Vector3.Distance(retOrigin, retFarEnd) >= MIN_SEGMENT)
            {
                var bodyPoly = TrackGeometry.GenerateStraightLine(retOrigin, retFarEnd);
                var bodySeg = trackBuilder.PlaceTrackWithPolyline(
                    bodyPoly, $"Rozjazd {retDef.Name} (prosta)", DepotTrackType.Maneuver);
                if (bodySeg != null) bodyTrackId = bodySeg.GraphTrackId;
            }

            // 3. Post-segment: tor równoległy ZA rozjazdem (w kierunku retEffDir za retFarEnd)
            Vector3 postEnd = retFarEnd + retEffDir * margin;
            if (Vector3.Distance(retFarEnd, postEnd) >= MIN_SEGMENT)
            {
                var postPoly = TrackGeometry.GenerateStraightLine(retFarEnd, postEnd);
                trackBuilder.PlaceTrackWithPolyline(postPoly, "Tor rownlegly", DepotTrackType.Maneuver);
            }

            // 4. Noga odgałęziająca (łącznik do wstawki)
            if (divergingLeg != null && divergingLeg.Count >= 2)
            {
                string side = retDivergeLeft ? "lewo" : "prawo";
                var divSeg = trackBuilder.PlaceTrackWithPolyline(
                    divergingLeg, $"Rozjazd {retDef.Name} (powrot-{side})", DepotTrackType.Maneuver);
                if (divSeg != null) divergingTrackId = divSeg.GraphTrackId;
            }

            // 5. Rejestruj TurnoutEntity
            var turnoutEntity = new TurnoutEntity(retDef.Name, TurnoutEntityType.Regular)
            {
                Origin = retOrigin,
                Direction = retEffDir.normalized,
                DivergeLeft = retDivergeLeft,
                Definition = retDef
            };
            if (bodyTrackId >= 0) turnoutEntity.MemberTrackIds.Add(bodyTrackId);
            if (divergingTrackId >= 0) turnoutEntity.MemberTrackIds.Add(divergingTrackId);
            if (turnoutEntity.MemberTrackIds.Count > 0)
            {
                trackBuilder.RegisterTurnout(turnoutEntity);
                // TD-035: koszt mechanizmu rozjazdu powrotnego (wcześniej 0 zł — usunięcie refundowało
                // niezapłacony mechanizm). Affordability pre-checked sumą w PlaceBranchWithReturn.
                ConstructionBilling.Charge(ConstructionCosts.TurnoutGroszy(retDef.Name), "construction_turnout", retDef.Name);
            }

            Log.Info($"[BranchReturn] PlaceReturnTurnoutDirectly: {retDef.Name} at {retOrigin}, dir={retEffDir}");
        }
    }
}
