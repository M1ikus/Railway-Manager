using System.Collections.Generic;
using UnityEngine;
using DepotSystem.Schemas.Generators;
using DepotSystem.Schemas.Snapshot;
using RailwayManager.Core;

namespace DepotSystem.Schemas.Placement
{
    /// <summary>
    /// Partial: preview transform (cursor → world pos z snap offset + anchor convention),
    /// preview snap visualization, geometry regenerate, preview renderer lifecycle.
    /// </summary>
    public partial class TurnoutSchemaPlacer
    {
        // ════════════════════════════════════════
        //  PREVIEW TRANSFORM
        // ════════════════════════════════════════

        /// <summary>
        /// Zwraca pozycję preview w world coords (cursor + snap translation).
        /// </summary>
        private Vector3 GetPreviewWorldPosition()
        {
            return _cursorWorldPos + _snapTranslation;
        }

        private void UpdatePreviewTransform()
        {
            if (_previewRenderer == null) return;

            // Pozycja = cursor (+ snap offset jeśli aktywny).
            //
            // Anchor convention DYNAMIC:
            // - Brak snap → anchor = endpoint[0] (= wjazd, default cursor representation).
            // - Snap aktywny → anchor = snap endpoint (= _lastSnapResult.anchorEndpointIdx).
            //   Schemat rotuje wokół snap endpoint, NIE wjazd. Snap endpoint pozostaje
            //   magnetycznie przyklejony do target podczas rotacji. Cursor "trzyma" snap
            //   endpoint na swojej pozycji + translation.
            Vector3 worldPos = GetPreviewWorldPosition();
            Quaternion rot = Quaternion.Euler(0, _currentRotationDeg, 0);

            int anchorIdx = _hasSnap && _lastSnapResult != null
                && _lastSnapResult.anchorEndpointIdx >= 0
                && _lastSnapResult.anchorEndpointIdx < _currentGeometry.endpoints.Count
                ? _lastSnapResult.anchorEndpointIdx : 0;
            Vector3 anchorLocal = _currentGeometry.endpoints != null && _currentGeometry.endpoints.Count > 0
                ? _currentGeometry.endpoints[anchorIdx]
                : _currentGeometry.centroid;

            _previewRenderer.transform.position = worldPos - rot * anchorLocal;
            _previewRenderer.transform.rotation = rot;
        }

        private void UpdatePreviewSnapVisualization()
        {
            if (_previewRenderer == null) return;

            if (_lastSnapResult != null && _lastSnapResult.snappedEndpoints.Count > 0)
            {
                var pairs = new List<(Vector3 schemaEndpoint, Vector3 target)>(_lastSnapResult.snappedEndpoints.Count);
                foreach (var info in _lastSnapResult.snappedEndpoints)
                {
                    pairs.Add((info.schemaEndpointWorld, info.targetWorld));
                }
                _previewRenderer.SetSnapTargets(pairs);
            }
            else
            {
                _previewRenderer.ClearSnapTargets();
            }
        }

        // ════════════════════════════════════════
        //  GEOMETRY + PREVIEW lifecycle
        // ════════════════════════════════════════

        private void RegenerateGeometry()
        {
            if (_currentSchema == null) return;

            if (_currentSchema.IsGenerative)
            {
                _currentGeometry = TurnoutSchemaGeneratorRegistry.GenerateFromDefinition(_currentSchema);
            }
            else if (_currentSchema.IsSnapshot)
            {
                if (_currentSchema.snapshotGeometry != null)
                {
                    _currentGeometry = SnapshotToSchemaGeometryConverter.Convert(_currentSchema.snapshotGeometry);
                }
                else
                {
                    Log.Error($"[TurnoutSchemaPlacer] Snapshot schema '{_currentSchema.id}' has null snapshotGeometry");
                    _currentGeometry = null;
                }
            }

            if (_previewRenderer != null && _currentGeometry != null)
            {
                _previewRenderer.SetGeometry(_currentGeometry);
            }
        }

        private void CreatePreviewRenderer()
        {
            if (_previewRenderer != null) return;
            if (_currentGeometry == null) return;

            var go = new GameObject("SchemaPreview");
            go.transform.SetParent(transform, worldPositionStays: false);
            _previewRenderer = go.AddComponent<SchemaPreviewRenderer>();
            _previewRenderer.SetGeometry(_currentGeometry);
        }

        private void CleanupPreview()
        {
            if (_previewRenderer != null)
            {
                _previewRenderer.Clear();
                Destroy(_previewRenderer.gameObject);
                _previewRenderer = null;
            }
        }

        /// <summary>
        /// Deprecated MD-3 placeholder — gizmos usunięte po MD-9 real placement (real tory
        /// są widoczne w scenie). Metoda zostaje jako no-op dla kompatybilności z SmokeTest
        /// ContextMenu "Clear last confirmed" — nic nie robi.
        /// </summary>
        public void ClearLastConfirmed()
        {
            // no-op — real tory są w PrefabTrackBuilder.PlacedTracks od MD-9
        }
    }
}
