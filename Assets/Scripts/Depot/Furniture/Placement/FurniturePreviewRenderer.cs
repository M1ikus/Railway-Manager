using UnityEngine;
using RailwayManager.Core.Rendering;

namespace DepotSystem.Furniture.Placement
{
    /// <summary>
    /// MF-4 — ghost cuboid preview dla furniture placement.
    ///
    /// MVP: solid color cuboid (kolor z <see cref="FurnitureItem.color"/>), wysokość
    /// 0.6m placeholder (M-Models swap na real meshes per <c>assetTag</c>).
    ///
    /// Tworzony jako child <see cref="FurniturePlacer"/>, niszczony przy CleanupPreview.
    /// Position/rotation update'owane co frame przez placera.
    ///
    /// MF-5 dorzuci wizualizację <c>accessSide</c> (strzałka na froncie). MF-6 dorzuci
    /// validation color override (zielony OK / żółty warning / czerwony error).
    /// </summary>
    public class FurniturePreviewRenderer : MonoBehaviour
    {
        /// <summary>Wysokość cuboida placeholder w metrach (M-Models swap → real mesh).</summary>
        public const float PlaceholderHeight = 0.6f;

        /// <summary>Wymiary strzałki accessSide (długość×szerokość×wysokość) — placeholder.</summary>
        private static readonly Vector3 ArrowSize = new Vector3(0.2f, 0.05f, 0.5f);

        /// <summary>Kolor strzałki accessSide (jasnożółty, dobrze widoczny na każdym tle).</summary>
        private static readonly Color ArrowColor = new Color(1f, 0.85f, 0.1f, 0.95f);

        private GameObject _cuboid;
        private MeshRenderer _renderer;
        private Material _material;
        private FurnitureItem _currentItem;
        private int _currentRotationDeg;

        // ── MF-5: accessSide arrow ──
        private GameObject _arrow;
        private Material _arrowMaterial;

        // ── MF-6: validation color override ──
        private ValidationLevel _validationLevel = ValidationLevel.Ok;

        // ════════════════════════════════════════
        //  PUBLIC API
        // ════════════════════════════════════════

        /// <summary>Tworzy cuboid dla danego itemu, ustawia kolor, rozmiar i strzałkę accessSide.</summary>
        public void SetItem(FurnitureItem item)
        {
            _currentItem = item;
            EnsureCuboid();
            ApplyColor();
            ApplyScale();
            RebuildAccessSideArrow();
        }

        /// <summary>Aktualizuje pozycję cuboida (snap world position).</summary>
        public void SetPosition(Vector3 worldPos)
        {
            // Pivot cuboida na pozycji worldPos, Y = wysokość/2 (cuboid stoi na ziemi Y=0)
            transform.position = new Vector3(worldPos.x, PlaceholderHeight * 0.5f, worldPos.z);
        }

        /// <summary>Aktualizuje rotację (Y axis, 0/90/180/270).</summary>
        public void SetRotation(int rotationDeg)
        {
            _currentRotationDeg = rotationDeg;
            transform.rotation = Quaternion.Euler(0f, rotationDeg, 0f);
            // ApplyScale się nie zmienia — Unity rotuje cube, scale zostaje w lokalnych axes
        }

        /// <summary>
        /// MF-6 — ustawia kolor preview override wg poziomu walidacji:
        /// - Ok = oryginalny color z FurnitureItem (alpha 0.7)
        /// - Warning = żółty (alpha 0.7) — brak dojścia, można postawić ale funkcja zablokowana
        /// - Error = czerwony (alpha 0.7) — placement zablokowany
        /// </summary>
        public void SetValidationLevel(ValidationLevel level)
        {
            if (_validationLevel == level) return;
            _validationLevel = level;
            ApplyColor();  // re-apply z uwzględnieniem _validationLevel
        }

        /// <summary>Cleanup — usuwa cuboid, strzałkę i materiały.</summary>
        public void Clear()
        {
            if (_cuboid != null) Destroy(_cuboid);
            _cuboid = null;
            _renderer = null;
            if (_material != null) Destroy(_material);
            _material = null;

            if (_arrow != null) Destroy(_arrow);
            _arrow = null;
            if (_arrowMaterial != null) Destroy(_arrowMaterial);
            _arrowMaterial = null;

            _currentItem = null;
        }

        void OnDestroy()
        {
            Clear();
        }

        // ════════════════════════════════════════
        //  PRIVATE
        // ════════════════════════════════════════

        private void EnsureCuboid()
        {
            if (_cuboid != null) return;

            _cuboid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cuboid.name = "PreviewCuboid";
            _cuboid.transform.SetParent(transform, worldPositionStays: false);

            // Usuń BoxCollider — preview nie powinien blokować raycast'ów ani fizyki
            var collider = _cuboid.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            _renderer = _cuboid.GetComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
        }

        private void ApplyColor()
        {
            if (_renderer == null || _currentItem == null) return;

            // Sprites/Default działa na 3D mesh i poprawnie respektuje alpha (placeholder
            // unlit — bez shadowów, idealne dla ghost preview). M-Models swap na real
            // shader z lighting.
            if (_material == null)
            {
                _material = MaterialFactory.CreateLine();
            }

            // MF-6: override kolor wg validation level (Ok = item color, Warning = żółty, Error = czerwony)
            Color color = _validationLevel switch
            {
                ValidationLevel.Warning => new Color(1f, 0.85f, 0.1f),     // żółty
                ValidationLevel.Error   => new Color(0.9f, 0.2f, 0.2f),    // czerwony
                _                       => ParseHexColor(_currentItem.color, fallback: new Color(0.5f, 0.5f, 0.5f)),
            };
            color.a = 0.7f;
            MaterialFactory.SetBaseColor(_material, color);
            _renderer.material = _material;
        }

        private void ApplyScale()
        {
            if (_cuboid == null || _currentItem == null) return;

            // Scale cuboid wg footprintCells × CellSize. Wysokość fixed PlaceholderHeight.
            // Cube primitive ma wymiary 1×1×1 default → scale jest bezpośrednio w metrach.
            int cellsX = Mathf.Max(1, _currentItem.footprintCells.x);
            int cellsY = Mathf.Max(1, _currentItem.footprintCells.y);
            _cuboid.transform.localScale = new Vector3(
                cellsX * FurnitureSnapDetector.CellSize,
                PlaceholderHeight,
                cellsY * FurnitureSnapDetector.CellSize
            );
        }

        private static Color ParseHexColor(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return fallback;
        }

        // ════════════════════════════════════════
        //  MF-5 — accessSide arrow (visualization gdzie pracownik podchodzi)
        // ════════════════════════════════════════

        /// <summary>
        /// Tworzy/odtwarza strzałkę wskazującą stronę <c>accessSide</c> obiektu.
        /// Cuboid 0.2×0.05×0.5m jako placeholder — jasnożółty, dobrze widoczny.
        ///
        /// Pozycja w lokalnych axes (parent = preview cuboid, rotuje się razem z nim):
        /// - Front: +Z (przed obiektem)
        /// - Back:  -Z (za obiektem, lokalna rotation Y=180)
        /// - Left:  -X (po lewej, lokalna rotation Y=270)
        /// - Right: +X (po prawej, lokalna rotation Y=90)
        /// - All:   brak strzałki (każda strona OK)
        /// </summary>
        private void RebuildAccessSideArrow()
        {
            if (_arrow != null) { Destroy(_arrow); _arrow = null; }
            if (_currentItem == null || _cuboid == null) return;

            AccessSide side = _currentItem.ParseAccessSide();
            if (side == AccessSide.All) return;  // brak strzałki dla "każda strona"

            // Cuboid strzałki — child cuboida placeholderowego, więc rotuje się z nim
            _arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _arrow.name = "AccessSideArrow";
            _arrow.transform.SetParent(_cuboid.transform, worldPositionStays: false);

            // Usuń BoxCollider — strzałka nie powinna blokować raycast'ów
            var collider = _arrow.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            // Strzałka jest child cuboida który ma scale = footprint × CellSize.
            // Local scale strzałki musi być ratio'd przez parent scale, bo Unity composite scale.
            Vector3 parentScale = _cuboid.transform.localScale;
            _arrow.transform.localScale = new Vector3(
                ArrowSize.x / Mathf.Max(0.001f, parentScale.x),
                ArrowSize.y / Mathf.Max(0.001f, parentScale.y),
                ArrowSize.z / Mathf.Max(0.001f, parentScale.z)
            );

            // Pozycja + rotacja w lokalnych axes parent'a (jednostka = parent scale, więc 0.5 = krawędź parent cuboida)
            // Centroid strzałki = krawędź parenta + 0.5 strzałki dalej w tym kierunku
            float arrowLenLocal = ArrowSize.z / Mathf.Max(0.001f, parentScale.z);
            float halfArrowLocal = arrowLenLocal * 0.5f;

            // Y = 0 (środek wysokości parent cuboida) — strzałka w tej samej płaszczyźnie co parent
            switch (side)
            {
                case AccessSide.Front:
                    _arrow.transform.localPosition = new Vector3(0f, 0f, 0.5f + halfArrowLocal);
                    _arrow.transform.localRotation = Quaternion.identity;
                    break;
                case AccessSide.Back:
                    _arrow.transform.localPosition = new Vector3(0f, 0f, -0.5f - halfArrowLocal);
                    _arrow.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    break;
                case AccessSide.Left:
                    _arrow.transform.localPosition = new Vector3(-0.5f - halfArrowLocal, 0f, 0f);
                    _arrow.transform.localRotation = Quaternion.Euler(0f, 270f, 0f);
                    break;
                case AccessSide.Right:
                    _arrow.transform.localPosition = new Vector3(0.5f + halfArrowLocal, 0f, 0f);
                    _arrow.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    break;
            }

            // Material — Sprites/Default + alpha
            var arrowRenderer = _arrow.GetComponent<MeshRenderer>();
            arrowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arrowRenderer.receiveShadows = false;

            if (_arrowMaterial == null)
            {
                _arrowMaterial = MaterialFactory.CreateLine();
            }
            MaterialFactory.SetBaseColor(_arrowMaterial, ArrowColor);
            arrowRenderer.material = _arrowMaterial;
        }
    }
}
