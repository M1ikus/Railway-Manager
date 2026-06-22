using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public partial class TurnoutPlacementStateMachine
    {
        // ═══════════════════════════════════════════
        //  EXECUTION — final placement + branch collision check
        // ═══════════════════════════════════════════

        private void ExecuteTurnoutPlacement(TrackBuildSubMode subMode)
        {
            if (turnoutPlacer == null || hoveredChain == null) return;
            var def = GetDefinition(subMode);

            // Branch return mode: rozjazd + wstawka + łuk/rozjazd powrotny
            if (branchReturnMode)
            {
                turnoutPlacer.PlaceBranchWithReturn(
                    hoveredChain, hoveredDistAlongChain,
                    def, divergeLeft, flipDirection,
                    branchSpacing, branchReturnType, branchReturnRadius);
                branchReturnMode = false;
                ClearBranchReturnPreview();
            }
            // Pair mode: postaw parę rozjazdów + wstawkę
            else if (pairMode && isPairValid && pairChain != null)
            {
                var defB = GetPairSecondaryDefinition(def);
                turnoutPlacer.PlaceTurnoutPairOnChains(
                    hoveredChain, hoveredDistAlongChain,
                    pairChain, pairDistAlongChain,
                    def, defB, pairDivergeLeft, flipDirection);
            }
            else
            {
                // Normalny pojedynczy rozjazd
                turnoutPlacer.PlaceTurnoutOnChain(hoveredChain, hoveredDistAlongChain, def, divergeLeft, flipDirection);
            }
        }

        private void ExecuteDoubleCrossover()
        {
            if (turnoutPlacer == null || hoveredChain == null) return;
            var def = TurnoutData.Crossover_R190;

            // Pair mode: krzyżowy jako główny + para na równoległym
            if (pairMode && isPairValid && pairChain != null)
            {
                var defB = GetPairSecondaryDefinition(def);
                turnoutPlacer.PlaceTurnoutPairOnChains(
                    hoveredChain, hoveredDistAlongChain,
                    pairChain, pairDistAlongChain,
                    def, defB, pairDivergeLeft, flipDirection);
            }
            else
            {
                turnoutPlacer.PlaceCrossoverOnChain(hoveredChain, hoveredDistAlongChain, def, false, divergeLeft);
            }
        }

        /// <summary>
        /// Sprawdza czy kluczowe punkty brancha (wstawka, łuk/rozjazd powrotny) nie kolidują
        /// z istniejącymi torami. Minimalny dystans = 1.5m (mniej niż międzytorze).
        /// </summary>
        private bool CheckBranchCollision(Vector3 insertEnd, Vector3 effectiveDir,
            Vector3 divDir, TurnoutData.TurnoutDefinition mainDef)
        {
            if (trackBuilder == null) return false;

            const float minDist = 1.5f;

            // Punkty kontrolne do sprawdzenia
            var checkPoints = new List<Vector3>();

            // Środek wstawki
            Vector3 divEnd = insertEnd - divDir * 0.5f; // approx middle, nie musi być dokładny
            checkPoints.Add(insertEnd);

            if (branchReturnType == 0)
            {
                // Łuk — sprawdź koniec łuku
                bool turnLeft = !divergeLeft;
                float alpha = mainDef.FrogAngle;
                var arcPoly = TurnoutData.GenerateReturnArc(insertEnd, divDir, branchReturnRadius, alpha, turnLeft);
                if (arcPoly.Count >= 2)
                {
                    checkPoints.Add(arcPoly[arcPoly.Count / 2]); // środek łuku
                    checkPoints.Add(arcPoly[arcPoly.Count - 1]);  // koniec łuku
                }
            }
            else
            {
                // Rozjazd powrotny — sprawdź origin i far end
                var retDef = branchReturnType == 1 ? TurnoutData.R190_1_9 : TurnoutData.R300_1_9;
                Vector3 retEffDir = -effectiveDir;
                Vector3 divOffset = TurnoutData.GetDivergingEndpoint(Vector3.zero, retEffDir, retDef, divergeLeft);
                Vector3 retOrigin = insertEnd - divOffset;
                Vector3 retFarEnd = retOrigin + retEffDir * retDef.Length;
                checkPoints.Add(retOrigin);
                checkPoints.Add(retFarEnd);
            }

            // Sprawdź czy którykolwiek punkt jest za blisko istniejącego toru
            foreach (var point in checkPoints)
            {
                foreach (var track in trackBuilder.PlacedTracks)
                {
                    if (track.TrackObject == null) continue;
                    if (track.Polyline == null || track.Polyline.Count < 2) continue;

                    // Pomiń tor na którym stawiamy rozjazd
                    if (hoveredChain != null && hoveredChain.Segments.Contains(track)) continue;

                    float projDist = TrackGeometry.ProjectPointOnPolyline(track.Polyline, point);
                    var (nearestPoint, _) = TrackGeometry.GetPointAtDistance(track.Polyline, projDist);
                    float dist = Vector3.Distance(point, nearestPoint);

                    if (dist < minDist)
                        return true;
                }
            }

            return false;
        }
    }
}
