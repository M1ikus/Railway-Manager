using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    public partial class TurnoutPlacementStateMachine
    {
        // ═══════════════════════════════════════════
        //  PREVIEW — Show/Hide dla turnouta + crossover + pair + branch return
        // ═══════════════════════════════════════════

        private void ShowTurnoutPreview(List<Vector3> polyline, float distAlong,
            TurnoutData.TurnoutDefinition def, bool valid, bool useDivergeLeft)
        {
            var (origin, dir) = TrackGeometry.GetPointAtDistance(polyline, distAlong);
            Color color = valid ? previewValidColor : previewInvalidColor;

            // Flip: rozjazd rośnie w tył → odwróć kierunek
            Vector3 effectiveDir = flipDirection ? -dir : dir;

            // Generuj geometrię preview
            var (straightLeg, divergingLeg) = TurnoutData.GenerateTurnoutGeometry(origin, effectiveDir, def, useDivergeLeft);

            // Straight preview
            EnsurePreviewLine(ref straightPreview, ref straightLine, "StraightPreview");
            SetLinePositions(straightLine, straightLeg, color);

            // Diverging preview
            EnsurePreviewLine(ref divergingPreview, ref divergingLine, "DivergingPreview");
            SetLinePositions(divergingLine, divergingLeg, color);

            // Origin marker
            ShowOriginMarker(origin, valid);
        }

        /// <summary>
        /// Preview rozjazdu krzyżowego: X-pattern z 2 krzyżujących się nóg odgałęziających.
        /// frontLeft (z origin, dir, lewo) + backLeft (z farEnd, -dir, lewo) = krzyżują się.
        /// </summary>
        private void ShowCrossoverPreview(List<Vector3> polyline, float distAlong,
            TurnoutData.TurnoutDefinition def, bool valid)
        {
            var (origin, dir) = TrackGeometry.GetPointAtDistance(polyline, distAlong);
            Color color = valid ? previewValidColor : previewInvalidColor;

            Vector3 farEnd = origin + dir * def.Length;

            // Prosta noga główna (origin → farEnd)
            var (straightLeg, _) = TurnoutData.GenerateTurnoutGeometry(origin, dir, def, divergeLeft);

            // X-pattern: 2 krzyżujące się łuki (front + back, ten sam divergeLeft)
            var (_, frontLeg) = TurnoutData.GenerateTurnoutGeometry(origin, dir, def, divergeLeft);
            var (_, backLeg) = TurnoutData.GenerateTurnoutGeometry(farEnd, -dir, def, divergeLeft);

            // Przekątna prosta: łączy końce obu łuków
            Vector3 frontLegEnd = TurnoutData.GetDivergingEndpoint(origin, dir, def, divergeLeft);
            Vector3 backLegEnd = TurnoutData.GetDivergingEndpoint(farEnd, -dir, def, divergeLeft);
            List<Vector3> diagonalStraight = TrackGeometry.GenerateStraightLine(frontLegEnd, backLegEnd);

            // Straight preview (główna prosta)
            EnsurePreviewLine(ref straightPreview, ref straightLine, "StraightPreview");
            SetLinePositions(straightLine, straightLeg, color);

            // Straight preview B (przekątna prosta łącząca końce łuków)
            EnsurePreviewLine(ref straightPreviewB, ref straightLineB, "StraightPreviewB");
            SetLinePositions(straightLineB, diagonalStraight, color);

            // 2 nogi krzyżowe (łuki)
            EnsurePreviewLine(ref divergingPreview, ref divergingLine, "DivFrontLeft");
            SetLinePositions(divergingLine, frontLeg, color);

            EnsurePreviewLine(ref divergingPreviewB, ref divergingLineB, "DivBackLeft");
            SetLinePositions(divergingLineB, backLeg, color);

            // Ukryj nieużywane
            if (divergingPreviewC != null) divergingPreviewC.SetActive(false);
            if (divergingPreviewD != null) divergingPreviewD.SetActive(false);
            if (originMarkerB != null) originMarkerB.SetActive(false);

            // Origin marker
            ShowOriginMarker(origin, valid);
        }

        /// <summary>
        /// Szuka równoległego toru i pokazuje preview odwróconego rozjazdu + wstawki prostej.
        /// </summary>
        private void ShowPairPreview(List<Vector3> polyline, float distAlong,
            TurnoutData.TurnoutDefinition defA, TurnoutData.TurnoutDefinition defB)
        {
            var (origin, dir) = TrackGeometry.GetPointAtDistance(polyline, distAlong);
            Vector3 effectiveDir = flipDirection ? -dir : dir;

            // Strona pary: auto = wynika z divergeLeft+flipDirection, albo ręczny override (O)
            int sideFilter = pairSidePreference;
            if (sideFilter == 0)
            {
                // divergeLeft=true + flip=false → odnoga w lewo → szukaj toru po lewej (1)
                // divergeLeft=true + flip=true  → odnoga w prawo → szukaj toru po prawej (2)
                // itd. — fizyczna strona = divergeLeft XOR flipDirection ? prawo : lewo
                sideFilter = (divergeLeft != flipDirection) ? 1 : 2;
            }
            var parallelTrack = turnoutPlacer.FindParallelTrack(hoveredTrack, 10f, sideFilter);
            if (parallelTrack == null)
            {
                HidePairPreview();
                return;
            }

            // Chain na równoległym torze
            pairChain = turnoutPlacer.FindStraightChain(parallelTrack);
            if (pairChain == null)
            {
                HidePairPreview();
                return;
            }

            // Auto-detekcja strony: odnoga musi iść KU równoległemu torowi
            pairDivergeLeft = divergeLeft;
            Vector3 divEndA = TurnoutData.GetDivergingEndpoint(origin, effectiveDir, defA, pairDivergeLeft);
            Vector3 divDirA = TurnoutData.GetDivergingEndDirection(effectiveDir, defA, pairDivergeLeft);

            float projTest = TrackGeometry.ProjectPointOnPolyline(pairChain.MergedPolyline, divEndA);
            var (nearTest, _) = TrackGeometry.GetPointAtDistance(pairChain.MergedPolyline, projTest);
            Vector3 toChainBTest = nearTest - divEndA;
            toChainBTest.y = 0;
            float testComponent = Vector3.Dot(divDirA, toChainBTest.normalized);

            if (testComponent < 0.01f)
            {
                pairDivergeLeft = !pairDivergeLeft;
                divEndA = TurnoutData.GetDivergingEndpoint(origin, effectiveDir, defA, pairDivergeLeft);
                divDirA = TurnoutData.GetDivergingEndDirection(effectiveDir, defA, pairDivergeLeft);
            }

            Vector3 effectiveDirB = -effectiveDir;
            bool divergeLeftB = pairDivergeLeft;

            // Oblicz offset od originB do divEndB z defB (może być inny typ rozjazdu)
            Vector3 divOffsetB = TurnoutData.GetDivergingEndpoint(Vector3.zero, effectiveDirB, defB, divergeLeftB);

            // === Analityczne rozwiązanie 2D ===
            Vector3 chainBStart = pairChain.MergedPolyline[0];
            Vector3 chainBDir = pairChain.Direction.normalized;
            Vector3 rhs = divEndA - chainBStart - divOffsetB;

            float det = chainBDir.x * (-divDirA.z) - (-divDirA.x) * chainBDir.z;

            float distB, insertLen;
            if (Mathf.Abs(det) < 0.0001f)
            {
                HidePairPreview();
                return;
            }

            distB = (rhs.x * (-divDirA.z) - (-divDirA.x) * rhs.z) / det;
            insertLen = (chainBDir.x * rhs.z - chainBDir.z * rhs.x) / det;

            if (insertLen < 0.01f)
            {
                HidePairPreview();
                return;
            }

            // Walidacja z defB.Length
            bool flipB = !flipDirection;
            if (flipB)
                isPairValid = distB >= defB.Length && distB <= pairChain.TotalLength;
            else
                isPairValid = distB >= 0f && (distB + defB.Length) <= pairChain.TotalLength;

            if (isPairValid)
            {
                // Dla flipped B, rozjazd rośnie wstecz → sprawdź overlap od początku ciała
                float checkDist = flipB ? distB - defB.Length : distB;
                isPairValid = turnoutPlacer.CanPlaceTurnoutOnChain(pairChain, checkDist, defB);
            }

            // Sprawdź buildable area dla B
            if (isPairValid && buildableArea.HasValue)
            {
                var (oB, dB) = TrackGeometry.GetPointAtDistance(pairChain.MergedPolyline, distB);
                Vector3 effDirB = flipB ? dB : -dB;
                Vector3 farEndB2 = oB + effDirB * defB.Length;
                Vector3 divEndB2 = TurnoutData.GetDivergingEndpoint(oB, effDirB, defB, divergeLeftB);
                var ba = buildableArea.Value;
                if (!IsPointInBA(ba, oB) || !IsPointInBA(ba, farEndB2) || !IsPointInBA(ba, divEndB2))
                    isPairValid = false;
            }

            if (!isPairValid)
            {
                HidePairPreview();
                return;
            }

            pairDistAlongChain = distB;

            // Oblicz geometrię rozjazdu B z defB
            var (originB, dirB) = TrackGeometry.GetPointAtDistance(pairChain.MergedPolyline, distB);

            Vector3 divEndB = TurnoutData.GetDivergingEndpoint(originB, effectiveDirB, defB, divergeLeftB);

            // Wstawka prosta: divEndA → divEndB
            List<Vector3> insertStraight = TrackGeometry.GenerateStraightLine(divEndA, divEndB);

            Color pairColor = previewValidColor;

            bool isCrossoverB = defB.Name == TurnoutData.Crossover_R190.Name;

            if (isCrossoverB)
            {
                // Crossover B: X-pattern (prosta + 2 łuki + przekątna)
                Vector3 farEndB = originB + effectiveDirB * defB.Length;

                var (straightLegB, frontLegB) = TurnoutData.GenerateTurnoutGeometry(
                    originB, effectiveDirB, defB, divergeLeftB);
                var (_, backLegB) = TurnoutData.GenerateTurnoutGeometry(
                    farEndB, -effectiveDirB, defB, divergeLeftB);

                Vector3 frontLegEndB = TurnoutData.GetDivergingEndpoint(originB, effectiveDirB, defB, divergeLeftB);
                Vector3 backLegEndB = TurnoutData.GetDivergingEndpoint(farEndB, -effectiveDirB, defB, divergeLeftB);
                List<Vector3> diagonalB = TrackGeometry.GenerateStraightLine(frontLegEndB, backLegEndB);

                EnsurePreviewLine(ref pairStraightPreview, ref pairStraightLine, "PairStraightPreview");
                SetLinePositions(pairStraightLine, straightLegB, pairColor);

                EnsurePreviewLine(ref pairDivergingPreview, ref pairDivergingLine, "PairDivFrontPreview");
                SetLinePositions(pairDivergingLine, frontLegB, pairColor);

                EnsurePreviewLine(ref pairDivBackPreview, ref pairDivBackLine, "PairDivBackPreview");
                SetLinePositions(pairDivBackLine, backLegB, pairColor);

                EnsurePreviewLine(ref pairDiagonalPreview, ref pairDiagonalLine, "PairDiagonalPreview");
                SetLinePositions(pairDiagonalLine, diagonalB, pairColor);
            }
            else
            {
                // Zwykły rozjazd B: prosta + 1 odnoga
                var (straightLegB, divergingLegB) = TurnoutData.GenerateTurnoutGeometry(
                    originB, effectiveDirB, defB, divergeLeftB);

                EnsurePreviewLine(ref pairStraightPreview, ref pairStraightLine, "PairStraightPreview");
                SetLinePositions(pairStraightLine, straightLegB, pairColor);

                EnsurePreviewLine(ref pairDivergingPreview, ref pairDivergingLine, "PairDivergingPreview");
                SetLinePositions(pairDivergingLine, divergingLegB, pairColor);

                // Ukryj crossover-only linie
                if (pairDivBackPreview != null) pairDivBackPreview.SetActive(false);
                if (pairDiagonalPreview != null) pairDiagonalPreview.SetActive(false);
            }

            // Preview: wstawka prosta
            EnsurePreviewLine(ref pairInsertPreview, ref pairInsertLine, "PairInsertPreview");
            SetLinePositions(pairInsertLine, insertStraight, pairColor);
        }

        private void HidePairPreview()
        {
            isPairValid = false;
            pairChain = null;
            if (pairStraightPreview != null) pairStraightPreview.SetActive(false);
            if (pairDivergingPreview != null) pairDivergingPreview.SetActive(false);
            if (pairInsertPreview != null) pairInsertPreview.SetActive(false);
            if (pairDivBackPreview != null) pairDivBackPreview.SetActive(false);
            if (pairDiagonalPreview != null) pairDiagonalPreview.SetActive(false);
        }

        private void HidePreview()
        {
            isValidPlacement = false;
            isPairValid = false;
            pairChain = null;
            if (straightPreview != null) straightPreview.SetActive(false);
            if (divergingPreview != null) divergingPreview.SetActive(false);
            if (originMarker != null) originMarker.SetActive(false);
            if (straightPreviewB != null) straightPreviewB.SetActive(false);
            if (divergingPreviewB != null) divergingPreviewB.SetActive(false);
            if (divergingPreviewC != null) divergingPreviewC.SetActive(false);
            if (divergingPreviewD != null) divergingPreviewD.SetActive(false);
            if (originMarkerB != null) originMarkerB.SetActive(false);
            if (pairStraightPreview != null) pairStraightPreview.SetActive(false);
            if (pairDivergingPreview != null) pairDivergingPreview.SetActive(false);
            if (pairInsertPreview != null) pairInsertPreview.SetActive(false);
            if (pairDivBackPreview != null) pairDivBackPreview.SetActive(false);
            if (pairDiagonalPreview != null) pairDiagonalPreview.SetActive(false);
            HideBranchReturnPreview();
        }

        // ═══════════════════════════════════════════
        //  BRANCH RETURN MODE (U) — preview / dialog
        // ═══════════════════════════════════════════

        private void ShowBranchReturnDialog(TurnoutData.TurnoutDefinition def)
        {
            var mgr = DepotUIManager.Instance;
            if (mgr == null || mgr.branchReturnDialog == null) return;

            branchReturnDialogOpen = true;
            var dialog = mgr.branchReturnDialog;

            dialog.OnConfirmed = (spacing, returnType, radius) =>
            {
                branchSpacing = spacing;
                branchReturnType = returnType;
                branchReturnRadius = radius;
                branchReturnMode = true;
                branchReturnDialogOpen = false;
                Log.Info($"[BranchReturn] Tryb U: spacing={spacing:F1}m, type={returnType}, R={radius:F0}m");
            };

            dialog.OnCancelled = () =>
            {
                branchReturnDialogOpen = false;
            };

            dialog.Show(def);
        }

        private void ShowBranchReturnPreview(List<Vector3> polyline, float distAlong,
            TurnoutData.TurnoutDefinition def)
        {
            var (origin, dir) = TrackGeometry.GetPointAtDistance(polyline, distAlong);
            Vector3 effectiveDir = flipDirection ? -dir : dir;

            Vector3 divEnd = TurnoutData.GetDivergingEndpoint(origin, effectiveDir, def, divergeLeft);
            Vector3 divDir = TurnoutData.GetDivergingEndDirection(effectiveDir, def, divergeLeft);

            // Oblicz insert length
            TurnoutData.TurnoutDefinition? returnDef = null;
            float returnRadius = branchReturnRadius;
            if (branchReturnType == 1)
                returnDef = TurnoutData.R190_1_9;
            else if (branchReturnType == 2)
                returnDef = TurnoutData.R300_1_9;

            var (insertLen, valid) = TurnoutData.ComputeBranchReturnInsert(
                def, branchSpacing, returnRadius, returnDef);

            if (!valid || insertLen < -0.01f)
            {
                HideBranchReturnPreview();
                isValidPlacement = false;
                return;
            }

            float safeInsert = Mathf.Max(0f, insertLen);

            // Sprawdź czy geometria brancha nie koliduje z istniejącymi torami
            Vector3 insertEnd = divEnd + divDir * safeInsert;
            bool branchCollides = CheckBranchCollision(insertEnd, effectiveDir, divDir, def);
            if (branchCollides)
                isValidPlacement = false;

            Color color = isValidPlacement ? previewValidColor : previewInvalidColor;

            // Wstawka prosta
            if (safeInsert > 0.1f)
            {
                var insertPoly = TrackGeometry.GenerateStraightLine(divEnd, insertEnd);
                EnsurePreviewLine(ref branchInsertPreview, ref branchInsertLine, "BranchInsertPreview");
                SetLinePositions(branchInsertLine, insertPoly, color);
            }
            else
            {
                if (branchInsertPreview != null) branchInsertPreview.SetActive(false);
            }

            if (branchReturnType == 0)
            {
                // Łuk powrotny
                // Skręt: przeciwny do divergeLeft (jeśli rozjazd odgałęzia w lewo, łuk skręca w prawo)
                bool turnLeft = !divergeLeft;
                float alpha = def.FrogAngle;
                var arcPoly = TurnoutData.GenerateReturnArc(insertEnd, divDir, returnRadius, alpha, turnLeft);

                EnsurePreviewLine(ref branchReturnPreview, ref branchReturnLine, "BranchReturnPreview");
                SetLinePositions(branchReturnLine, arcPoly, color);

                // Krótki prosty odcinek za łukiem (nowy tor równoległy)
                if (arcPoly.Count >= 2)
                {
                    Vector3 arcEnd = arcPoly[arcPoly.Count - 1];
                    Vector3 parallelEnd = arcEnd + effectiveDir * 5f;
                    var parallelPoly = TrackGeometry.GenerateStraightLine(arcEnd, parallelEnd);

                    EnsurePreviewLine(ref branchParallelPreview, ref branchParallelLine, "BranchParallelPreview");
                    SetLinePositions(branchParallelLine, parallelPoly, color);
                }

                // Ukryj return turnout lines
                if (branchReturnStraightPreview != null) branchReturnStraightPreview.SetActive(false);
                if (branchReturnDivPreview != null) branchReturnDivPreview.SetActive(false);
            }
            else
            {
                // Rozjazd powrotny — preview z GenerateTurnoutGeometry
                var retDef = branchReturnType == 1 ? TurnoutData.R190_1_9 : TurnoutData.R300_1_9;
                Vector3 retEffDir = -effectiveDir;
                bool retDivergeLeft = divergeLeft;

                // Oblicz origin rozjazdu powrotnego
                Vector3 divOffset = TurnoutData.GetDivergingEndpoint(Vector3.zero, retEffDir, retDef, retDivergeLeft);
                Vector3 retOrigin = insertEnd - divOffset;

                var (straightLeg, divergingLeg) = TurnoutData.GenerateTurnoutGeometry(
                    retOrigin, retEffDir, retDef, retDivergeLeft);

                EnsurePreviewLine(ref branchReturnStraightPreview, ref branchReturnStraightLine, "BranchRetStraightPreview");
                SetLinePositions(branchReturnStraightLine, straightLeg, color);

                EnsurePreviewLine(ref branchReturnDivPreview, ref branchReturnDivLine, "BranchRetDivPreview");
                SetLinePositions(branchReturnDivLine, divergingLeg, color);

                // Krótki prosty odcinek za rozjazdem (nowy tor równoległy)
                Vector3 retFarEnd = retOrigin + retEffDir * retDef.Length;
                // Tor równoległy rozciąga się przed i za rozjazdem
                Vector3 parallelStart = retOrigin + effectiveDir * 5f; // Za retOrigin w kierunku effectiveDir
                var parallelPoly = TrackGeometry.GenerateStraightLine(retFarEnd, parallelStart);
                EnsurePreviewLine(ref branchParallelPreview, ref branchParallelLine, "BranchParallelPreview");
                SetLinePositions(branchParallelLine, parallelPoly, color);

                // Ukryj arc preview
                if (branchReturnPreview != null) branchReturnPreview.SetActive(false);
            }
        }

        private void HideBranchReturnPreview()
        {
            if (branchInsertPreview != null) branchInsertPreview.SetActive(false);
            if (branchReturnPreview != null) branchReturnPreview.SetActive(false);
            if (branchReturnStraightPreview != null) branchReturnStraightPreview.SetActive(false);
            if (branchReturnDivPreview != null) branchReturnDivPreview.SetActive(false);
            if (branchParallelPreview != null) branchParallelPreview.SetActive(false);
        }

        private void ClearBranchReturnPreview()
        {
            if (branchInsertPreview != null) { Destroy(branchInsertPreview); branchInsertPreview = null; branchInsertLine = null; }
            if (branchReturnPreview != null) { Destroy(branchReturnPreview); branchReturnPreview = null; branchReturnLine = null; }
            if (branchReturnStraightPreview != null) { Destroy(branchReturnStraightPreview); branchReturnStraightPreview = null; branchReturnStraightLine = null; }
            if (branchReturnDivPreview != null) { Destroy(branchReturnDivPreview); branchReturnDivPreview = null; branchReturnDivLine = null; }
            if (branchParallelPreview != null) { Destroy(branchParallelPreview); branchParallelPreview = null; branchParallelLine = null; }
        }
    }
}
