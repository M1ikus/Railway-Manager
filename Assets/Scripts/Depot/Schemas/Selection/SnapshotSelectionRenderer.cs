using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core.Rendering;

namespace DepotSystem.Schemas.Selection
{
    /// <summary>
    /// Visualization rectangle drag selection + highlight wybranych obiektów.
    ///
    /// Komponenty:
    /// - LineRenderer dla rectangle (4 segmenty XZ, cyan, lekko podniesiony nad ground żeby był widoczny)
    /// - LineRenderer per zaznaczony tor (kopia polyline z cyan podświetleniem)
    /// - LineRenderer per zaznaczony rozjazd (krótki marker przy origin)
    ///
    /// Y-offset: 0.05m nad ground żeby uniknąć z-fighting'u z istniejącymi torami.
    /// </summary>
    public class SnapshotSelectionRenderer : MonoBehaviour
    {
        [Header("Rectangle drag visualization")]
        public Color rectangleColor = new Color(0.2f, 1.0f, 1.0f, 0.9f);  // cyan
        public float rectangleLineWidth = 0.4f;
        public float rectangleYOffset = 0.05f;

        [Header("Highlight wybranych obiektów")]
        public Color highlightColor = new Color(0.5f, 1.0f, 1.0f, 0.85f);  // lighter cyan
        public float highlightLineWidth = 0.35f;
        public float highlightYOffset = 0.06f;
        public float turnoutMarkerLength = 2.0f;

        // ── Cache ──
        private LineRenderer _rectLineRenderer;
        private List<LineRenderer> _highlightLines = new();
        private Material _rectMaterial;
        private Material _highlightMaterial;

        void Awake()
        {
            EnsureMaterials();
            CreateRectangleLineRenderer();
        }

        void OnDestroy()
        {
            ClearAll();
            if (_rectMaterial != null) Destroy(_rectMaterial);
            if (_highlightMaterial != null) Destroy(_highlightMaterial);
        }

        private void EnsureMaterials()
        {
            if (_rectMaterial == null)
            {
                _rectMaterial = MaterialFactory.CreateLine();
                MaterialFactory.SetBaseColor(_rectMaterial, rectangleColor);
                _highlightMaterial = MaterialFactory.CreateLine();
                MaterialFactory.SetBaseColor(_highlightMaterial, highlightColor);
            }
        }

        private void CreateRectangleLineRenderer()
        {
            var go = new GameObject("RectangleOverlay");
            go.transform.SetParent(transform, false);

            _rectLineRenderer = go.AddComponent<LineRenderer>();
            _rectLineRenderer.useWorldSpace = true;
            _rectLineRenderer.startWidth = rectangleLineWidth;
            _rectLineRenderer.endWidth = rectangleLineWidth;
            _rectLineRenderer.startColor = rectangleColor;
            _rectLineRenderer.endColor = rectangleColor;
            _rectLineRenderer.material = _rectMaterial;
            _rectLineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _rectLineRenderer.loop = true;  // closed rectangle
            _rectLineRenderer.positionCount = 0;
        }

        // ════════════════════════════════════════
        //  PUBLIC API
        // ════════════════════════════════════════

        /// <summary>
        /// Update rectangle drag overlay — przekątnymi corner'ami A i C.
        /// Generuje 4 punkty rectangle XZ z Y = rectangleYOffset.
        /// </summary>
        public void SetRectangle(Vector3 cornerA, Vector3 cornerC)
        {
            if (_rectLineRenderer == null) return;

            float minX = Mathf.Min(cornerA.x, cornerC.x);
            float maxX = Mathf.Max(cornerA.x, cornerC.x);
            float minZ = Mathf.Min(cornerA.z, cornerC.z);
            float maxZ = Mathf.Max(cornerA.z, cornerC.z);
            float y = rectangleYOffset;

            _rectLineRenderer.positionCount = 4;
            _rectLineRenderer.SetPosition(0, new Vector3(minX, y, minZ));
            _rectLineRenderer.SetPosition(1, new Vector3(maxX, y, minZ));
            _rectLineRenderer.SetPosition(2, new Vector3(maxX, y, maxZ));
            _rectLineRenderer.SetPosition(3, new Vector3(minX, y, maxZ));
        }

        /// <summary>
        /// Wyczyść rectangle overlay.
        /// </summary>
        public void ClearRectangle()
        {
            if (_rectLineRenderer != null) _rectLineRenderer.positionCount = 0;
        }

        /// <summary>
        /// Update highlight wybranych obiektów. Tworzy LineRenderer per zaznaczony tor + rozjazd.
        /// Wywoływać przy każdej zmianie selekcji (drag update lub po confirm).
        /// </summary>
        public void SetHighlight(SnapshotSelectionResult selection)
        {
            ClearHighlights();
            if (selection == null) return;
            EnsureMaterials();

            // Highlight tracks
            for (int i = 0; i < selection.selectedTracks.Count; i++)
            {
                var track = selection.selectedTracks[i];
                if (track.Polyline == null || track.Polyline.Count < 2) continue;

                var go = new GameObject($"HighlightTrack_{i}");
                go.transform.SetParent(transform, false);

                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.startWidth = highlightLineWidth;
                lr.endWidth = highlightLineWidth;
                lr.startColor = highlightColor;
                lr.endColor = highlightColor;
                lr.material = _highlightMaterial;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.positionCount = track.Polyline.Count;

                // Y offset żeby był nad oryginalnym torem
                var positions = new Vector3[track.Polyline.Count];
                for (int p = 0; p < track.Polyline.Count; p++)
                {
                    var pt = track.Polyline[p];
                    positions[p] = new Vector3(pt.x, highlightYOffset, pt.z);
                }
                lr.SetPositions(positions);

                _highlightLines.Add(lr);
            }

            // Highlight turnouts (markery przy origin)
            for (int i = 0; i < selection.selectedTurnouts.Count; i++)
            {
                var t = selection.selectedTurnouts[i];

                var go = new GameObject($"HighlightTurnout_{i}");
                go.transform.SetParent(transform, false);

                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.startWidth = highlightLineWidth * 1.5f;
                lr.endWidth = highlightLineWidth * 0.5f;
                lr.startColor = highlightColor;
                lr.endColor = highlightColor;
                lr.material = _highlightMaterial;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.positionCount = 2;

                Vector3 originY = new Vector3(t.Origin.x, highlightYOffset, t.Origin.z);
                Vector3 dirEnd = originY + t.Direction.normalized * turnoutMarkerLength;
                lr.SetPosition(0, originY);
                lr.SetPosition(1, dirEnd);

                _highlightLines.Add(lr);
            }
        }

        /// <summary>Wyczyść wszystkie highlights.</summary>
        public void ClearHighlights()
        {
            foreach (var lr in _highlightLines)
                if (lr != null) Destroy(lr.gameObject);
            _highlightLines.Clear();
        }

        /// <summary>Wyczyść wszystko (rectangle + highlights).</summary>
        public void ClearAll()
        {
            ClearRectangle();
            ClearHighlights();
        }
    }
}
