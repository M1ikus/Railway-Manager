using System.Collections.Generic;
using RailwayManager.Core.Rendering;
using RailwayManager.Fleet;
using UnityEngine;

namespace DepotSystem
{
    /// <summary>
    /// M-FC-7: Runtime 3D scene dla paint editor preview. Tworzy proporcjonalny cuboid
    /// (jak M9b/M9c placeholders) z N segmentami (członami), aplikuje PaintDefinition
    /// na material'e (bottom color + stripes jako quady na boku), renderuje do RenderTexture
    /// pokazywanego w UI przez RawImage.
    ///
    /// Po dostawie M-Models real mesh'y → podmiana cuboid'a na real prefab; reszta API
    /// (camera orbit, ApplyPaint) zostaje.
    ///
    /// Lifecycle: gracz otwiera paint editor → instantiate PaintPreview3D w hidden world
    /// position (5000, 5000, 5000) → on destroy editor zamyka komponent.
    /// </summary>
    public class PaintPreview3D : MonoBehaviour
    {
        public const int TEXTURE_SIZE = 384;
        public const float WIDTH_M = 2.85f;
        public const float HEIGHT_M = 4.0f;
        public const float HIDDEN_OFFSET = 5000f; // poza widzialnym zakresem main camera

        public RenderTexture RT => _rt;

        private RenderTexture _rt;
        private Camera _camera;
        private GameObject _cuboidRoot;
        private GameObject _lightGO;
        private List<GameObject> _segments = new();
        private List<List<GameObject>> _stripeQuadsPerSegment = new();
        private List<List<GameObject>> _decalQuadsPerSegment = new();

        private int _segmentCount = 1;
        private float _segmentLength = 25f;

        // Orbit state
        private float _yaw = 30f;
        private float _pitch = 18f;
        private float _zoom = 1f;
        private float _baseDistance;

        public void Init(int segmentCount, float totalLengthM)
        {
            int newSegmentCount = Mathf.Max(1, segmentCount);
            float newSegmentLength = totalLengthM / newSegmentCount;

            // Skip rebuild jeśli parametry niezmienione i scena już istnieje
            bool sceneAlreadyBuilt = _camera != null && _cuboidRoot != null;
            bool paramsChanged = _segmentCount != newSegmentCount
                || Mathf.Abs(_segmentLength - newSegmentLength) > 0.01f;

            _segmentCount = newSegmentCount;
            _segmentLength = newSegmentLength;
            _baseDistance = totalLengthM * 1.4f + 8f;

            if (sceneAlreadyBuilt && !paramsChanged)
            {
                UpdateCameraPosition();
                return;
            }

            // Hidden world position
            transform.position = new Vector3(HIDDEN_OFFSET, HIDDEN_OFFSET, HIDDEN_OFFSET);

            // RT
            if (_rt == null)
            {
                _rt = new RenderTexture(TEXTURE_SIZE, TEXTURE_SIZE, 16, RenderTextureFormat.ARGB32)
                {
                    name = "PaintPreview3D_RT",
                    antiAliasing = 2
                };
                _rt.Create();
            }

            // Camera
            if (_camera == null)
            {
                var camGO = new GameObject("PreviewCamera");
                camGO.transform.SetParent(transform, worldPositionStays: false);
                _camera = camGO.AddComponent<Camera>();
                _camera.targetTexture = _rt;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0.13f, 0.15f, 0.18f, 1f);
                _camera.fieldOfView = 35f;
                _camera.nearClipPlane = 0.5f;
                _camera.farClipPlane = 500f;
                _camera.depth = -10; // niski priority (renderuje przed main)
                _camera.enabled = true;
            }

            // Light
            if (_lightGO == null)
            {
                _lightGO = new GameObject("PreviewLight");
                _lightGO.transform.SetParent(transform, worldPositionStays: false);
                _lightGO.transform.localRotation = Quaternion.Euler(45f, -30f, 0f);
                var light = _lightGO.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = 1.0f;
            }

            RebuildCuboid();
            UpdateCameraPosition();
        }

        public void ApplyPaint(PaintDefinition def)
        {
            if (def == null || _segments == null || _segments.Count == 0) return;

            for (int i = 0; i < _segments.Count; i++)
            {
                SegmentPaint sp = null;
                if (def.segments != null && i < def.segments.Count) sp = def.segments[i];

                ApplyToSegment(i, sp);
            }
        }

        public void OrbitDelta(float deltaYaw, float deltaPitch)
        {
            _yaw += deltaYaw;
            _pitch = Mathf.Clamp(_pitch + deltaPitch, -75f, 75f);
            UpdateCameraPosition();
        }

        public void ZoomDelta(float scrollDelta)
        {
            _zoom = Mathf.Clamp(_zoom * (1f - scrollDelta * 0.1f), 0.4f, 2.5f);
            UpdateCameraPosition();
        }

        private void OnDestroy()
        {
            if (_rt != null)
            {
                if (_camera != null) _camera.targetTexture = null;
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
        }

        // ── Cuboid construction ──────────────────────────────

        private void RebuildCuboid()
        {
            // Clear existing
            if (_cuboidRoot != null)
            {
                Destroy(_cuboidRoot);
                _cuboidRoot = null;
            }
            _segments.Clear();
            _stripeQuadsPerSegment.Clear();
            _decalQuadsPerSegment.Clear();

            _cuboidRoot = new GameObject("CuboidRoot");
            _cuboidRoot.transform.SetParent(transform, worldPositionStays: false);
            _cuboidRoot.transform.localPosition = Vector3.zero;

            float totalLen = _segmentLength * _segmentCount;
            float startX = -totalLen / 2f + _segmentLength / 2f;

            for (int i = 0; i < _segmentCount; i++)
            {
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = $"Segment_{i}";
                seg.transform.SetParent(_cuboidRoot.transform, worldPositionStays: false);
                seg.transform.localPosition = new Vector3(startX + i * _segmentLength, HEIGHT_M / 2f, 0);
                seg.transform.localScale = new Vector3(_segmentLength * 0.95f, HEIGHT_M, WIDTH_M);

                // Default material — szary, żeby było widoczne
                var mr = seg.GetComponent<MeshRenderer>();
                mr.material = CreateBasicMaterial(new Color(0.85f, 0.85f, 0.85f));

                _segments.Add(seg);
                _stripeQuadsPerSegment.Add(new List<GameObject>());
                _decalQuadsPerSegment.Add(new List<GameObject>());
            }
        }

        private void ApplyToSegment(int segmentIdx, SegmentPaint paint)
        {
            if (segmentIdx < 0 || segmentIdx >= _segments.Count) return;
            var segGO = _segments[segmentIdx];
            var mr = segGO.GetComponent<MeshRenderer>();

            // Bottom paint (base color)
            Color baseColor = paint != null && !string.IsNullOrEmpty(paint.baseColor)
                ? ParseHex(paint.baseColor, new Color(0.85f, 0.85f, 0.85f))
                : new Color(0.85f, 0.85f, 0.85f);
            mr.material = CreateBasicMaterial(baseColor);

            // Clear old stripes/decals
            foreach (var q in _stripeQuadsPerSegment[segmentIdx]) if (q != null) Destroy(q);
            _stripeQuadsPerSegment[segmentIdx].Clear();
            foreach (var q in _decalQuadsPerSegment[segmentIdx]) if (q != null) Destroy(q);
            _decalQuadsPerSegment[segmentIdx].Clear();

            if (paint == null) return;

            // Stripes — quad'y na obu bokach segmentu
            if (paint.stripes != null)
            {
                foreach (var s in paint.stripes)
                {
                    BuildStripe(segmentIdx, s);
                }
            }

            // Decals — quad'y na boku z color jako placeholder (brak sprite'ów pre-EA)
            if (paint.decals != null)
            {
                foreach (var d in paint.decals)
                {
                    BuildDecal(segmentIdx, d);
                }
            }
        }

        private void BuildStripe(int segmentIdx, StripeLayer stripe)
        {
            var segGO = _segments[segmentIdx];
            float segLen = _segmentLength;
            float yLocal = HEIGHT_M * (0.5f - stripe.positionY); // positionY 0=góra, 1=dół
            float thickness = HEIGHT_M * stripe.thickness;
            if (thickness < 0.01f) thickness = 0.05f;

            Color stripeColor = ParseHex(stripe.color, Color.white);

            // Front side (Z+)
            var frontQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            frontQuad.name = $"Stripe_{stripe.presetId}_front";
            frontQuad.transform.SetParent(segGO.transform, worldPositionStays: false);
            frontQuad.transform.localScale = new Vector3(segLen * 0.95f, thickness, 1f);
            // localPosition: front side z = +0.5 of segment thickness (segment scale.z = WIDTH_M)
            frontQuad.transform.localPosition = new Vector3(0, yLocal, 0.51f);
            frontQuad.transform.localRotation = Quaternion.identity;
            // Quad size in local = segment.scale × this.scale → effective stripe size correct
            frontQuad.GetComponent<MeshRenderer>().material = CreateBasicMaterial(stripeColor);
            Destroy(frontQuad.GetComponent<MeshCollider>());
            _stripeQuadsPerSegment[segmentIdx].Add(frontQuad);

            // Back side (Z-, rotated 180)
            var backQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backQuad.name = $"Stripe_{stripe.presetId}_back";
            backQuad.transform.SetParent(segGO.transform, worldPositionStays: false);
            backQuad.transform.localScale = new Vector3(segLen * 0.95f, thickness, 1f);
            backQuad.transform.localPosition = new Vector3(0, yLocal, -0.51f);
            backQuad.transform.localRotation = Quaternion.Euler(0, 180, 0);
            backQuad.GetComponent<MeshRenderer>().material = CreateBasicMaterial(stripeColor);
            Destroy(backQuad.GetComponent<MeshCollider>());
            _stripeQuadsPerSegment[segmentIdx].Add(backQuad);
        }

        private void BuildDecal(int segmentIdx, DecalLayer decal)
        {
            // Pre-EA: decal renderowany jako mały kolorowy kwadrat — placeholder przed real sprite atlas
            var segGO = _segments[segmentIdx];
            float segLen = _segmentLength;
            float xLocal = segLen * (decal.positionX - 0.5f);
            float yLocal = HEIGHT_M * (0.5f - decal.positionY);

            Color decalColor = ParseHex(decal.color, Color.black);
            float decalSize = 0.5f * decal.scale;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"Decal_{decal.symbolId}";
            quad.transform.SetParent(segGO.transform, worldPositionStays: false);
            quad.transform.localScale = new Vector3(decalSize, decalSize, 1f);
            quad.transform.localPosition = new Vector3(xLocal, yLocal, 0.52f);
            quad.transform.localRotation = Quaternion.Euler(0, 0, decal.rotation);
            quad.GetComponent<MeshRenderer>().material = CreateBasicMaterial(decalColor);
            Destroy(quad.GetComponent<MeshCollider>());
            _decalQuadsPerSegment[segmentIdx].Add(quad);
        }

        private void UpdateCameraPosition()
        {
            if (_camera == null || _cuboidRoot == null) return;

            Vector3 target = _cuboidRoot.transform.position + new Vector3(0, HEIGHT_M / 2f, 0);
            float distance = _baseDistance * _zoom;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0);
            Vector3 offset = rot * new Vector3(0, 0, -distance);
            _camera.transform.position = target + offset;
            _camera.transform.LookAt(target);
        }

        // ── Helpers ──────────────────────────────────────────

        private static Material CreateBasicMaterial(Color color)
        {
            // Lit shader z prostą barwą (Built-in Standard / URP Lit przez fabrykę)
            var mat = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(mat, color);
            return mat;
        }

        private static Color ParseHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return fallback;
        }
    }
}
