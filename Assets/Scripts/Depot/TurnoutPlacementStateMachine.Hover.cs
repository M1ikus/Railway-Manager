using UnityEngine;

namespace DepotSystem
{
    public partial class TurnoutPlacementStateMachine
    {
        // ═══════════════════════════════════════════
        //  HOVER HANDLERS — dispatch do Show*Preview
        // ═══════════════════════════════════════════

        private void HandleTurnoutHover(TrackBuildSubMode subMode)
        {
            if (hoveredChain == null)
            {
                HidePreview();
                return;
            }

            var def = GetDefinition(subMode);

            // Snap do końców łańcucha
            float dist = SnapToChainEndpoint(hoveredChain, hoveredDistAlongChain);
            hoveredDistAlongChain = dist;

            isValidPlacement = turnoutPlacer != null
                && CanPlaceWithFlip(hoveredChain, dist, def)
                && !IsTurnoutOverlapping(hoveredChain, dist, def)
                && !IsTurnoutOverlappingBuildings(hoveredChain, dist, def);

            // Pair mode: oblicz pairDivergeLeft PRZED renderowaniem primary preview
            if (pairMode && isValidPlacement && hoveredTrack != null)
            {
                var defB = GetPairSecondaryDefinition(def);
                ShowPairPreview(hoveredChain.MergedPolyline, dist, def, defB);
                ShowTurnoutPreview(hoveredChain.MergedPolyline, dist, def, isValidPlacement, isPairValid ? pairDivergeLeft : divergeLeft);
                HideBranchReturnPreview();
            }
            else if (branchReturnMode && isValidPlacement)
            {
                HidePairPreview();
                ShowTurnoutPreview(hoveredChain.MergedPolyline, dist, def, isValidPlacement, divergeLeft);
                ShowBranchReturnPreview(hoveredChain.MergedPolyline, dist, def);
            }
            else
            {
                HidePairPreview();
                HideBranchReturnPreview();
                ShowTurnoutPreview(hoveredChain.MergedPolyline, dist, def, isValidPlacement, divergeLeft);
            }
        }

        private void HandleDoubleCrossoverHover()
        {
            if (hoveredChain == null)
            {
                HidePreview();
                return;
            }

            var def = TurnoutData.Crossover_R190;

            float dist = SnapToChainEndpoint(hoveredChain, hoveredDistAlongChain);
            hoveredDistAlongChain = dist;

            isValidPlacement = turnoutPlacer != null
                && CanPlaceWithFlip(hoveredChain, dist, def)
                && !IsTurnoutOverlapping(hoveredChain, dist, def)
                && !IsTurnoutOverlappingBuildings(hoveredChain, dist, def);

            ShowCrossoverPreview(hoveredChain.MergedPolyline, dist, def, isValidPlacement);

            // Pair mode dla krzyżowego
            if (pairMode && isValidPlacement && hoveredTrack != null)
            {
                var defB = GetPairSecondaryDefinition(def);
                ShowPairPreview(hoveredChain.MergedPolyline, dist, def, defB);
            }
            else
            {
                HidePairPreview();
            }
        }
    }
}
