using UnityEngine;
using DepotSystem.Schemas.Generators;
using RailwayManager.Core;

namespace DepotSystem.Schemas.Placement
{
    /// <summary>
    /// Partial: multi-endpoint snap detection (MD-4) + A13 adaptive proposal API.
    /// </summary>
    public partial class TurnoutSchemaPlacer
    {
        // ════════════════════════════════════════
        //  SNAP DETECTION (multi-endpoint MD-4)
        // ════════════════════════════════════════

        private void UpdateSnapV2()
        {
            if (_snapPointSystem == null) _snapPointSystem = DepotServices.Get<SnapPointSystem>();
            if (_trackGraph == null) _trackGraph = DepotServices.Get<TrackGraph>();

            // Sticky snap — zachowaj anchorNodeId I anchorEndpointIdx z poprzedniej klatki PRZED
            // ResetSnapState (ten zerowałby _lastSnapResult zanim odczytamy id).
            // Sticky NIE wybiera już dynamicznie najbliższego endpointu — używa SAMEGO endpoint
            // co poprzednia klatka, żeby snap się NIE odpychał (= anchor stable across frames).
            int previousAnchorNodeId = _lastSnapResult != null && _lastSnapResult.hasAnySnap
                ? _lastSnapResult.anchorNodeId
                : -1;
            int previousAnchorEndpointIdx = _lastSnapResult != null && _lastSnapResult.hasAnySnap
                ? _lastSnapResult.anchorEndpointIdx
                : -1;

            ResetSnapState();
            // Note: ResetSnapState czyści też _autoRotationApplied — to OK, snap się dezaktywował
            // i auto-rotation może ponownie zadziałać przy nowym snap.

            if (_snapPointSystem == null || _currentGeometry == null) return;

            // A13 throttling: sprawdzaj adaptive co N frame'ów (heavy: re-generates geometry per candidate)
            bool checkAdaptive = enableAdaptivePrompt
                && _currentSchema != null
                && _currentSchema.IsGenerative
                && (_adaptiveCheckCounter++ % adaptiveCheckFrameThrottle) == 0;

            ITurnoutSchemaGenerator generator = null;
            if (checkAdaptive)
            {
                generator = TurnoutSchemaGeneratorRegistry.Get(_currentSchema.ParseCategory());
            }

            var result = SchemaSnapDetector.DetectSnaps(
                _currentGeometry,
                _cursorWorldPos,
                _currentRotationDeg,
                snapToleranceMeters,
                _snapPointSystem,
                _trackGraph,
                _currentSchema?.parameters,
                generator,
                checkAdaptive,
                previousAnchorNodeId,
                stickyReleaseDistance,
                previousAnchorEndpointIdx);

            // Debug log — co 10 frame'ów (żeby nie zalewać Console)
            if (showSnapDebugLog && (_debugLogCounter++ % 10) == 0)
            {
                if (result.hasAnySnap)
                {
                    bool wasSticky = previousAnchorNodeId == result.anchorNodeId;
                    string anchorLabel = result.anchorEndpointIdx == 0
                        ? "wjazd"
                        : $"endpoint[{result.anchorEndpointIdx}] (snap z drugiej strony)";
                    Log.Info($"[Snap] {(wasSticky ? "STICKY" : "ACQUIRE")} active: anchor={anchorLabel}, anchorNodeId={result.anchorNodeId}, snappedCount={result.snappedEndpointCount}, translation={result.translation.magnitude:F1}m, prevAnchor={previousAnchorNodeId}");
                }
                else
                {
                    Log.Info($"[Snap] No snap — prevAnchor={previousAnchorNodeId}, cursor={_cursorWorldPos}");
                }
            }

            _lastSnapResult = result;
            _hasSnap = result.hasAnySnap;
            _snappedEndpointCount = result.snappedEndpointCount;
            _snapTranslation = result.translation;

            // A13 prompt — placeholder MVP w postaci log + state.
            // Real modal "Dostosować schemat?" w MD-5+ (przy panelu parametrów).
            if (result.hasAdaptivePromptCandidate
                && (!_hasAdaptiveProposal || !Mathf.Approximately(_proposedSpacingMeters, result.proposedSpacingMeters)))
            {
                _hasAdaptiveProposal = true;
                _proposedSpacingMeters = result.proposedSpacingMeters;
                Log.Info($"[TurnoutSchemaPlacer] A13 candidate: zmiana spacing {AverageCurrentSpacing():F2}m → {result.proposedSpacingMeters:F2}m daje {result.proposedSnappedCount} snap'ów (obecnie {result.snappedEndpointCount}). Wywołaj AcceptAdaptiveProposal() żeby apply.");
            }
            else if (!result.hasAdaptivePromptCandidate && _hasAdaptiveProposal)
            {
                // Cofnięto — propozycja nie jest już aktualna
                _hasAdaptiveProposal = false;
                _proposedSpacingMeters = 0f;
            }
        }

        private float AverageCurrentSpacing()
        {
            if (_currentSchema?.parameters?.trackSpacings == null
                || _currentSchema.parameters.trackSpacings.Length == 0)
                return _currentSchema?.parameters?.trackSpacing ?? SchemaParameters.DefaultSpacing;
            float sum = 0f;
            for (int i = 0; i < _currentSchema.parameters.trackSpacings.Length; i++)
                sum += _currentSchema.parameters.trackSpacings[i];
            return sum / _currentSchema.parameters.trackSpacings.Length;
        }

        // ════════════════════════════════════════
        //  A13 Adaptive proposal API (MD-4)
        // ════════════════════════════════════════

        /// <summary>
        /// Akceptuje A13 propozycję — zmienia <see cref="SchemaParameters.trackSpacing"/> na
        /// proposed value + regeneruje geometrię. Wywoływane z UI prompt'u "Dostosować schemat?".
        ///
        /// W MD-5+ zostanie podpięte do real modal'a. W MD-4 MVP używane przez SmokeTest ContextMenu.
        /// </summary>
        public void AcceptAdaptiveProposal()
        {
            if (!_hasAdaptiveProposal || _currentSchema?.parameters == null)
            {
                Log.Warn("[TurnoutSchemaPlacer] AcceptAdaptiveProposal: brak aktywnej propozycji");
                return;
            }

            float oldSpacing = AverageCurrentSpacing();
            _currentSchema.parameters.trackSpacing = _proposedSpacingMeters;
            _currentSchema.parameters.trackSpacings = null;  // force shorthand → expand all to proposed

            Log.Info($"[TurnoutSchemaPlacer] A13 ACCEPT: spacing {oldSpacing:F2}m → {_proposedSpacingMeters:F2}m");

            RegenerateGeometry();

            _hasAdaptiveProposal = false;
            _proposedSpacingMeters = 0f;
            _autoRotationApplied = false;  // re-apply auto-rotation z nową geometrią
        }

        /// <summary>
        /// Odrzuca A13 propozycję — schemat zostaje przy obecnym spacing.
        /// Propozycja może pojawić się ponownie jeśli warunki się powtórzą.
        /// </summary>
        public void RejectAdaptiveProposal()
        {
            if (!_hasAdaptiveProposal)
            {
                Log.Warn("[TurnoutSchemaPlacer] RejectAdaptiveProposal: brak aktywnej propozycji");
                return;
            }
            Log.Info($"[TurnoutSchemaPlacer] A13 REJECT: zostajemy przy spacing {AverageCurrentSpacing():F2}m");
            _hasAdaptiveProposal = false;
            _proposedSpacingMeters = 0f;
        }

        public bool HasAdaptiveProposal => _hasAdaptiveProposal;
        public float ProposedSpacingMeters => _proposedSpacingMeters;
    }
}
