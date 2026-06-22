using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core.Rendering;

namespace DepotSystem.Schemas.Placement
{
    /// <summary>
    /// Renderer ghost mesh schematu — wizualizacja pre-placement.
    ///
    /// Konwencja taka sama jak <c>TrackBuildStateMachine</c> preview toru:
    /// - LineRenderer z <c>Sprites/Default</c> shader
    /// - Grubość 0.25m (taka jak prawdziwy preview toru)
    /// - 2 kolory: zielony (OK, schemat valid + można postawić) / czerwony (invalid)
    /// - Wszystkie odcinki schematu tym samym kolorem (tor przewodni / wstawki / łuki / postojowe
    ///   nie różnią się — to ghost preview, nie real rendering)
    ///
    /// Transform GameObjectu = pozycja+rotacja schematu w world coords. Lokalne polyline'y
    /// w SchemaGeometry są renderowane "as-is" jako children — Unity transform automatycznie
    /// obraca je razem z parent'em.
    ///
    /// Snap target visualization: żółta linia od snapped endpointu schematu do istniejącego
    /// endpointu toru (Gizmos w OnDrawGizmos).
    /// </summary>
    public class SchemaPreviewRenderer : MonoBehaviour
    {
        [Header("Preview colors (analogiczne do TrackBuildStateMachine)")]
        public Color validColor = new Color(0.3f, 1.0f, 0.3f, 0.8f);     // zielony (OK)
        public Color invalidColor = new Color(1.0f, 0.3f, 0.3f, 0.8f);   // czerwony (invalid)

        [Header("Line width (grubsze niz track preview 0.25m dla widocznosci schematu)")]
        public float lineWidth = 0.6f;

        [Header("Markers (Gizmos in OnDrawGizmos)")]
        public Color turnoutOriginColor = new Color(1.0f, 0.4f, 0.4f, 0.9f);
        public Color endpointColor = new Color(0.2f, 1.0f, 0.4f, 0.9f);
        public Color snapTargetColor = new Color(1.0f, 1.0f, 0.0f, 1.0f);  // żółty
        public float turnoutOriginRadius = 0.25f;
        public float endpointRadius = 0.5f;

        // ── Cache ──
        private SchemaGeometry _geometry;
        private bool _isValid = true;
        private List<LineRenderer> _lineRenderers = new();
        private Material _validMaterial;
        private Material _invalidMaterial;

        // ── Snap visualization (multi-endpoint) ──
        private List<(Vector3 schemaEndpoint, Vector3 target)> _snapPairs = new();

        void Awake()
        {
            EnsureMaterials();
        }

        private void EnsureMaterials()
        {
            if (_validMaterial == null)
            {
                _validMaterial = MaterialFactory.CreateLine();
                MaterialFactory.SetBaseColor(_validMaterial, validColor);
                _invalidMaterial = MaterialFactory.CreateLine();
                MaterialFactory.SetBaseColor(_invalidMaterial, invalidColor);
            }
        }

        /// <summary>
        /// Ustawia geometrię i odbudowuje LineRenderers.
        /// </summary>
        public void SetGeometry(SchemaGeometry geometry)
        {
            _geometry = geometry;
            RebuildLineRenderers();
        }

        /// <summary>
        /// Update validity state. Zmienia kolor wszystkich linii (zielony OK, czerwony invalid).
        /// </summary>
        public void SetValid(bool valid)
        {
            if (_isValid == valid) return;
            _isValid = valid;
            ApplyMaterialToAllLines();
        }

        /// <summary>
        /// Multi-endpoint snap visualization — lista par (schemaEndpoint, target).
        /// Każda para renderowana jako żółta linia + sphere na targetcie.
        /// Pusta lista = brak snap.
        /// </summary>
        public void SetSnapTargets(List<(Vector3 schemaEndpoint, Vector3 target)> pairs)
        {
            _snapPairs.Clear();
            if (pairs != null) _snapPairs.AddRange(pairs);
        }

        /// <summary>Wyłącza snap visualization (równoważne SetSnapTargets(null)).</summary>
        public void ClearSnapTargets()
        {
            _snapPairs.Clear();
        }

        /// <summary>Czyści wszystkie LineRenderers + state.</summary>
        public void Clear()
        {
            _geometry = null;
            _snapPairs.Clear();
            foreach (var lr in _lineRenderers)
                if (lr != null) Destroy(lr.gameObject);
            _lineRenderers.Clear();
        }

        private void RebuildLineRenderers()
        {
            EnsureMaterials();

            // Cleanup old
            foreach (var lr in _lineRenderers)
                if (lr != null) Destroy(lr.gameObject);
            _lineRenderers.Clear();

            if (_geometry == null) return;

            Material mat = _isValid ? _validMaterial : _invalidMaterial;

            for (int i = 0; i < _geometry.tracks.Count; i++)
            {
                var track = _geometry.tracks[i];
                if (track.polyline == null || track.polyline.Count < 2) continue;

                var go = new GameObject($"GhostTrack_{i}");
                go.transform.SetParent(transform, worldPositionStays: false);

                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;  // local coords, transform parent handles position+rotation
                lr.startWidth = lineWidth;
                lr.endWidth = lineWidth;
                lr.startColor = mat.color;
                lr.endColor = mat.color;
                lr.material = mat;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.positionCount = track.polyline.Count;
                lr.SetPositions(track.polyline.ToArray());

                _lineRenderers.Add(lr);
            }
        }

        private void ApplyMaterialToAllLines()
        {
            Material mat = _isValid ? _validMaterial : _invalidMaterial;
            for (int i = 0; i < _lineRenderers.Count; i++)
            {
                if (_lineRenderers[i] == null) continue;
                _lineRenderers[i].material = mat;
                _lineRenderers[i].startColor = mat.color;
                _lineRenderers[i].endColor = mat.color;
            }
        }

        void OnDrawGizmos()
        {
            if (_geometry == null) return;

            // MD-X fix: turnout origin gizmos usunięte — w fan junction algorytmie wszystkie
            // rozjazdy mają origin=EntryEnd (overlapping markery w jednym punkcie), które wyglądały
            // jak "dziwny rozjazd niepasujący do niczego". Polyline tracks już renderują wszystkie
            // tory wjazdowe + postojowe — wystarczy do wizualizacji.

            // Snap targets — żółta linia + sphere per snap pair (zachowane, kluczowy feedback)
            Gizmos.color = snapTargetColor;
            foreach (var pair in _snapPairs)
            {
                Gizmos.DrawLine(pair.schemaEndpoint, pair.target);
                Gizmos.DrawSphere(pair.target, 0.4f);
            }
        }

        void OnDestroy()
        {
            if (_validMaterial != null) Destroy(_validMaterial);
            if (_invalidMaterial != null) Destroy(_invalidMaterial);
        }
    }
}
