// Force recompile - updated POI rendering
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using formap;
using RailwayManager.Core;

namespace MapSystem
{
    /// <summary>
    /// Renders map layers as Unity meshes with culling support
    /// Creates batched meshes respecting Unity's 65k vertex limit
    ///
    /// UPDATED: Compatible with HoleStarts in format v5
    /// NOTE: HoleStarts are currently not used for rendering (simple triangulation)
    ///       Future enhancement: use HoleStarts for proper multipolygon holes
    ///
    /// Klasa rozbita na partial files:
    /// - <c>MapRenderer.cs</c>         — pola (Inspector + state), Awake/Update, LOD/visibility,
    ///                                   RenderMap (main public), ClearLayers, ToggleLayer,
    ///                                   GetLayerObject, FindCameraInScene (ten plik)
    /// - <c>MapRenderer.Layers.cs</c>  — ShouldRenderLayer, CreateLayerObject, RenderLayer
    ///                                   (z railway grouping), batching (Feature/Flush), CreateMeshObject,
    ///                                   per-layer config (Material/Height/Queue/IsLine/IsPoint),
    ///                                   ReverseTriangleWindingOrder
    /// - <c>MapRenderer.Markers.cs</c> — RenderPOILayer (Places/Stations/Signals dispatcher) +
    ///                                   CreatePlaceLabel, CreateStationMarker, CreateHiddenSignal
    /// - <c>MapRenderer.Tiles.cs</c>   — Tiled rendering: SetTiledMode, RenderTile×2,
    ///                                   SetTileVisible, UnloadTile, ClearAllTiles, SetLayerRecursive
    /// </summary>
    public partial class MapRenderer : MonoBehaviour
    {
        [Header("Layer Materials")]
        public Material highwayMaterial;
        public Material railwayMaterial;
        [Tooltip("Material dla disused/abandoned railways. Domyślnie szary. Jeśli null, używa railwayMaterial.")]
        public Material disusedRailwayMaterial;
        public Material buildingMaterial;
        public Material waterMaterial;
        public Material waterwayMaterial; // Rivers, streams as lines
        public Material industrialMaterial;
        public Material militaryMaterial;
        public Material platformMaterial;
        public Material forestMaterial;
        public Material poiMaterial;

        [Header("Layer Visibility")]
        public bool renderHighways = true;
        public bool renderRailways = true;
        public bool renderBuildings = true;
        public bool renderWater = true;
        public bool renderWaterways = true; // Rivers, streams
        public bool renderIndustrial = true;
        public bool renderMilitary = true;
        public bool renderPlatforms = true;
        public bool renderForests = true;
        public bool renderPOIs = true;

        // Legacy field combineMeshes + maxVerticesPerMesh usunięte 2026-05-16 (TD-026 Krok A).
        // Mesh.indexFormat = UInt32 w CreateMeshObject eliminuje stary UInt16 limit 65535 vertices,
        // więc split na 60k nie jest już potrzebny — wszystkie features layer'a w jednym mesh.

        [Header("Height Settings — large offsets to eliminate Z-fighting at far zoom")]
        public float waterHeight = 0.1f; // M-PL-4: same Y co SyntheticWater (był -5 = pod ground=0, niewidoczne)
        public float forestHeight = -3f;
        public float industrialHeight = 1f;
        public float militaryHeight = 1.5f;
        public float buildingHeight = 2f;
        public float platformHeight = 3f;
        public float waterwayHeight = 4f;
        public float highwayHeight = 5f;
        public float railwayHeight = 8f;
        public float poiHeight = 10f;

        [Header("POI Settings")]
        [Tooltip("Size of POI points in meters")]
        public float poiSize = 50f;

        [Tooltip("Font for place names (cities, towns, villages) - leave empty for built-in font")]
        public TMP_FontAsset placeNameFont;

        [Tooltip("Scale for city names (larger = bigger text)")]
        public float cityFontSize = 12f;

        [Tooltip("Scale for town names")]
        public float townFontSize = 8f;

        [Tooltip("Scale for village names")]
        public float villageFontSize = 5f;

        [Tooltip("Scale for station names")]
        public float stationFontSize = 3f;

        [Tooltip("Color for place names")]
        public Color placeNameColor = new Color(0.1f, 0.1f, 0.1f, 1f);

        [Tooltip("Color for station names")]
        public Color stationNameColor = new Color(0.0f, 0.2f, 0.5f, 1f);

        [Tooltip("Station icon sprite - leave empty for colored cube (current placeholder).")]
        // TODO (M12 Polish): custom ikony stacji — patrz OPEN_QUESTIONS.md "Custom ikony stacji na mapie 2D".
        // Aktualnie nieprzypisane w MapScene.unity → fallback shared cached cube (pkt 11 commit e9c6100).
        // Designerska decyzja TBD: jeden sprite vs per-typ (station/halt) vs per-kategoria (główna/aglo/peryferyjna).
        public Sprite stationIconSprite;

        [Tooltip("Station icon size in world units")]
        public float stationIconSize = 30f;

        [Header("Debug")]
        public bool showDebugInfo = false;
        public bool showHoleStatsInfo = false;  // ✅ NEW: Show holes statistics

        [Header("Zoom-Based LOD Thresholds")]
        public float zoomLOD1 = 1000f;   // LOD1: residential+, all buildings
        public float zoomLOD2 = 2000f;   // LOD2: residential+, big buildings
        public float zoomLOD3 = 4000f;   // LOD3: motorway-tertiary, no buildings
        public float zoomLOD4 = 8000f;   // LOD4: motorway/trunk/primary
        public float zoomLOD5 = 16000f;  // LOD5: no roads at all

        [Header("Place Name Scaling")]
        [Tooltip("orthoSize at which place names are at their base scale")]
        public float nameScaleReferenceZoom = 500f;
        [Tooltip("Minimum scale multiplier for place names. Default 1 — labels nigdy mniejsze niż baseScale (floor visual size przy zoom in/mid range).")]
        public float nameScaleMin = 1f;
        [Tooltip("Maximum scale multiplier for place names. Default 100 — cover LOD 5 do max zoom (ortho 50000 / ref 500 = mult 100) żeby cities skalowały się constant pixel size na całym zakresie. Niższy clamp powoduje że cities maleją pixelowo przy max zoom out.")]
        public float nameScaleMax = 100f;

        // Layer containers
        private Dictionary<BinaryFormat.LayerType, GameObject> layerObjects = new();
        private UnityEngine.Camera mainCamera;

        // Tiled rendering
        private bool isTiledMode = false;
        private Dictionary<long, Dictionary<BinaryFormat.LayerType, GameObject>> tileRenderObjects = new();
        private Dictionary<long, GameObject> tileContainers = new();

        // Layer visibility LOD cache
        private int lastLODLevel = -1;

        // Cached instanced materials per layer (avoid creating new Material per tile)
        private Dictionary<BinaryFormat.LayerType, Material> layerMaterialCache = new();

        // Place name labels — tracked for zoom-based scaling + per-type LOD visibility.
        // labelType: "city" / "town" / "village" / "hamlet" / "station" / "halt" / "" (unknown).
        private struct LabelEntry
        {
            public Transform t;
            public float baseScale;
            public string labelType;
        }
        private List<LabelEntry> placeLabels = new();

        // Cached last computed scale multiplier — UpdatePlaceNameScales skipuje pełną pętlę
        // gdy zoom się nie zmienił (typowo użytkownik nie skroluje co frame, więc skip 99%+).
        // NaN initial żeby pierwszy frame zawsze przeszedł update i zsynchronizował state.
        private float _lastPlaceNameScaleMult = float.NaN;

        // Cached resources dla CreateStationMarker fallback (gdy stationIconSprite=null).
        // ~3000 stacji × Shader.Find + new Material było waste — wszystkie używają tego
        // samego cube mesh + materiału z stationNameColor.
        private Mesh _cachedFallbackCubeMesh;
        private Material _cachedFallbackStationMaterial;

        // Cached material dla disused/abandoned railways. Wcześniej trzymane w layerMaterialCache
        // pod magic key (LayerType)999 — fragile (kolizja gdyby kiedyś enum LayerType dostał
        // wartość 999) i mylące w czytaniu. Osobny field = clean lookup, jawny lifecycle.
        private Material _cachedDisusedRailwayMaterial;

        // Shared TMP material dla place + station labels — clone z TMP_FontAsset.material
        // z dodanym white outline. Bez outline ciemnoszary tekst (placeNameColor 0.1) jest
        // niewidoczny na czarnym bg mapy (TMP SDF alpha blending). Jedna instancja shared
        // dla wszystkich ~50k labels (place + station) zamiast per-text material instance.
        private Material _cachedMapLabelMaterial;

        // LOD change event — MapLoader subscribes to reload tiles with new LOD
        public event System.Action<int> OnLODLevelChanged;
        private MapLoader mapLoader;

        void Awake()
        {
            // Find camera in the same scene (not Camera.main which may return Depot camera)
            mainCamera = GetComponentInParent<Camera>()
                ?? FindCameraInScene()
                ?? UnityEngine.Camera.main;
            mapLoader = FindAnyObjectByType<MapLoader>();

            if (showDebugInfo)
            {
                Log.Info("[MapRenderer] Initialized with layer heights:");
                Log.Info($"  Water: {waterHeight}, Forests: {forestHeight}");
                Log.Info($"  Buildings: {buildingHeight}, Highways: {highwayHeight}");
                Log.Info($"  Railways: {railwayHeight}, POIs: {poiHeight}");
            }
        }

        void Update()
        {
            UpdateLayerVisibility();
            UpdatePlaceNameScales();
        }

        void OnDestroy()
        {
            // Destroy cached materials — new Material() to UnityEngine.Object które żyje
            // aż explicit Destroy. Bez tego GPU resource leak przy każdym scene reload.
            foreach (var mat in layerMaterialCache.Values)
                if (mat != null) Destroy(mat);
            layerMaterialCache.Clear();

            if (_cachedFallbackStationMaterial != null) Destroy(_cachedFallbackStationMaterial);
            _cachedFallbackStationMaterial = null;

            if (_cachedDisusedRailwayMaterial != null) Destroy(_cachedDisusedRailwayMaterial);
            _cachedDisusedRailwayMaterial = null;

            if (_cachedMapLabelMaterial != null) Destroy(_cachedMapLabelMaterial);
            _cachedMapLabelMaterial = null;

            // _cachedFallbackCubeMesh = Resources.GetBuiltinResource — NIE destroy,
            // to shared Unity builtin asset, zwolnienie psuje inne use'y.
        }

        // ═══════════════════════════════════════════
        //  LOD / VISIBILITY
        // ═══════════════════════════════════════════

        /// <summary>
        /// Scales place name labels proportionally to zoom level
        /// so they remain readable at all zoom levels (with min/max clamp).
        /// </summary>
        private void UpdatePlaceNameScales()
        {
            if (mainCamera == null || placeLabels.Count == 0) return;

            float ortho = mainCamera.orthographic ? mainCamera.orthographicSize : nameScaleReferenceZoom;
            float scaleMult = Mathf.Clamp(ortho / nameScaleReferenceZoom, nameScaleMin, nameScaleMax);

            // Skip pełnej pętli gdy zoom się nie zmienił (typowy frame). Pełna Polska ma ~50k
            // place labels — co frame iteracja + Vector3 alokacja per label była waste.
            // Epsilon 0.001 = scale diff < 0.1% imperceptible visually, oszczędność znacząca.
            if (!float.IsNaN(_lastPlaceNameScaleMult)
                && Mathf.Abs(scaleMult - _lastPlaceNameScaleMult) < 0.001f)
                return;
            _lastPlaceNameScaleMult = scaleMult;

            for (int i = placeLabels.Count - 1; i >= 0; i--)
            {
                var entry = placeLabels[i];
                if (entry.t == null)
                {
                    placeLabels.RemoveAt(i);
                    continue;
                }
                float s = entry.baseScale * scaleMult;
                entry.t.localScale = new Vector3(s, s, s);
            }
        }

        /// <summary>
        /// Aplikuje current zoom scaling do pojedynczego label transform — używane przy spawn
        /// nowo loadowanych labels (tile load), żeby nie zostawały z initial baseScale × 1
        /// gdy cache <see cref="_lastPlaceNameScaleMult"/> mówi "no change" i UpdatePlaceNameScales
        /// pomija pełną pętlę.
        /// </summary>
        private void ApplyCurrentZoomScale(Transform t, float baseScale)
        {
            float mult = float.IsNaN(_lastPlaceNameScaleMult) ? 1f : _lastPlaceNameScaleMult;
            float s = baseScale * mult;
            t.localScale = new Vector3(s, s, s);
        }

        /// <summary>
        /// Per-type LOD visibility dla labels (Places + POIs). Stopniowy schemat —
        /// większy obiekt = dłużej widoczny przy zoom out.
        ///
        /// | type    | LOD 0 | LOD 1 | LOD 2 | LOD 3 | LOD 4 | LOD 5 |
        /// |---------|-------|-------|-------|-------|-------|-------|
        /// | city    |  ✅   |  ✅   |  ✅   |  ✅   |  ✅   |  ✅   |
        /// | town    |  ✅   |  ✅   |  ✅   |  ✅   |  ❌   |  ❌   |
        /// | village |  ✅   |  ✅   |  ✅   |  ❌   |  ❌   |  ❌   |
        /// | hamlet  |  ✅   |  ✅   |  ❌   |  ❌   |  ❌   |  ❌   |
        /// | station |  ✅   |  ✅   |  ✅   |  ✅   |  ❌   |  ❌   |
        /// | halt    |  ✅   |  ✅   |  ✅   |  ❌   |  ❌   |  ❌   |
        /// </summary>
        private static bool IsLabelVisibleAtLOD(string labelType, int lod)
        {
            return labelType switch
            {
                "city"    => true,        // do LOD 5 — wielkie miasta zawsze
                "town"    => lod <= 3,    // do LOD 3
                "village" => lod <= 2,    // do LOD 2
                "hamlet"  => lod <= 1,    // do LOD 1
                "station" => lod <= 3,    // do LOD 3
                "halt"    => lod <= 2,    // do LOD 2
                _         => lod <= 2,    // unknown default — okolice village
            };
        }

        /// <summary>
        /// Aplikuje per-type LOD visibility — wywoływane przy każdej zmianie LOD level.
        /// </summary>
        private void UpdateLabelsVisibility(int lodLevel)
        {
            for (int i = placeLabels.Count - 1; i >= 0; i--)
            {
                var entry = placeLabels[i];
                if (entry.t == null) { placeLabels.RemoveAt(i); continue; }
                bool vis = IsLabelVisibleAtLOD(entry.labelType, lodLevel);
                if (entry.t.gameObject.activeSelf != vis)
                    entry.t.gameObject.SetActive(vis);
            }
        }

        /// <summary>
        /// Ukrywa/pokazuje warstwy na podstawie zoomu kamery.
        /// LOD 0 (bliski): wszystko widoczne
        /// LOD 1 (sredni): bez POI
        /// LOD 2 (daleki): tylko woda, lasy, drogi, tory
        /// </summary>
        private void UpdateLayerVisibility()
        {
            if (mainCamera == null) return;

            float ortho = mainCamera.orthographic ? mainCamera.orthographicSize : 1000f;

            int lodLevel = MapLod.LodForOrtho(ortho, zoomLOD1, zoomLOD2, zoomLOD3, zoomLOD4, zoomLOD5);

            if (lodLevel == lastLODLevel) return;
            lastLODLevel = lodLevel;

            if (showDebugInfo)
                Log.Info($"[MapRenderer] LOD level changed to {lodLevel} (orthoSize={ortho:F0})");

            // Notify MapLoader to reload tiles with new LOD (v7 only)
            if (mapLoader != null)
                mapLoader.SetLODLevel(lodLevel);

            OnLODLevelChanged?.Invoke(lodLevel);

            // Apply layer visibility to tiled mode
            foreach (var tileKvp in tileRenderObjects)
            {
                foreach (var layerKvp in tileKvp.Value)
                {
                    if (layerKvp.Value != null)
                        layerKvp.Value.SetActive(IsLayerVisibleAtLOD(layerKvp.Key, lodLevel));
                }
            }

            // Apply to non-tiled mode (v5)
            foreach (var layerKvp in layerObjects)
            {
                if (layerKvp.Value != null)
                    layerKvp.Value.SetActive(IsLayerVisibleAtLOD(layerKvp.Key, lodLevel));
            }

            // Per-type label visibility (cities zawsze, towns/villages/halts progresywnie).
            // Container layer Places/POIs zostaje always-active — granular control per label.
            UpdateLabelsVisibility(lodLevel);
        }

        /// <summary>
        /// Czy warstwa jest widoczna na danym LOD level?
        /// LOD 0 (bliski): wszystko — drogi, chodniki, budynki, POI
        /// LOD 1 (sredni): glowne drogi, tory, woda, lasy, duze budynki, waterways (bez POI, bez chodnikow)
        /// LOD 2 (daleki): tylko tory, woda, lasy — BEZ DROG (eliminuje shimmer calkowicie)
        ///
        /// Labels (Places, POIs) — container layer always-active, per-label filtering
        /// w <see cref="UpdateLabelsVisibility"/> + <see cref="IsLabelVisibleAtLOD"/>.
        /// Stopniowy schemat: city LOD 5, town LOD 3, village LOD 2, hamlet LOD 1,
        /// station LOD 3, halt LOD 2.
        /// </summary>
        private bool IsLayerVisibleAtLOD(BinaryFormat.LayerType type, int lodLevel)
        {
            // Labels containers always-active (per-label filtering w UpdateLabelsVisibility).
            if (type == BinaryFormat.LayerType.POIs) return true;
            if (type == BinaryFormat.LayerType.Places) return true;

            return lodLevel switch
            {
                0 => true, // LOD0: everything
                1 => type != BinaryFormat.LayerType.Military, // LOD1: no military
                2 => type is not (BinaryFormat.LayerType.Military or BinaryFormat.LayerType.Waterways), // LOD2: no military, no waterways
                3 => type is BinaryFormat.LayerType.Highways or BinaryFormat.LayerType.Railways // LOD3
                    or BinaryFormat.LayerType.Water or BinaryFormat.LayerType.Forests
                    or BinaryFormat.LayerType.Waterways,
                4 => type is BinaryFormat.LayerType.Highways or BinaryFormat.LayerType.Railways // LOD4
                    or BinaryFormat.LayerType.Water or BinaryFormat.LayerType.Forests,
                _ => type is BinaryFormat.LayerType.Railways or BinaryFormat.LayerType.Water // LOD5
                    or BinaryFormat.LayerType.Forests,
            };
        }

        // ═══════════════════════════════════════════
        //  RENDER MAP — main public entry (v5 non-tiled)
        // ═══════════════════════════════════════════

        /// <summary>
        /// Renders all map layers
        /// ✅ Compatible with format v5 (HoleStarts present but not used for rendering yet)
        /// </summary>
        public void RenderMap(Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> layers, BBox bounds)
        {
            Log.Info("[MapRenderer] Starting map rendering...");
            float startTime = Time.realtimeSinceStartup;

            // Clear existing layers
            ClearLayers();

            // Render each layer
            int totalMeshes = 0;
            int totalVertices = 0;
            int totalTriangles = 0;
            int totalFeaturesWithHoles = 0;  // ✅ NEW: Track features with holes

            foreach (var kvp in layers)
            {
                if (!ShouldRenderLayer(kvp.Key))
                {
                    Log.Info($"[MapRenderer] Skipping layer {kvp.Key} (disabled in settings)");
                    continue;
                }

                var layerObj = CreateLayerObject(kvp.Key);
                var stats = RenderLayer(layerObj, kvp.Key, kvp.Value);

                totalMeshes += stats.meshCount;
                totalVertices += stats.vertexCount;
                totalTriangles += stats.triangleCount;
                totalFeaturesWithHoles += stats.featuresWithHoles;

                layerObjects[kvp.Key] = layerObj;

                // Per-layer stats — wyłączone (spamuje konsolę)
            }

            float renderTime = Time.realtimeSinceStartup - startTime;
            Log.Info($"[MapRenderer] ✓ Map rendered in {renderTime:F2}s");
            Log.Info($"[MapRenderer]   Total: {totalMeshes} meshes, " +
                     $"{totalVertices:N0} vertices, {totalTriangles:N0} triangles");

            // ✅ NEW: Show hole statistics if enabled
            if (showHoleStatsInfo && totalFeaturesWithHoles > 0)
            {
                Log.Info($"[MapRenderer]   ℹ️ {totalFeaturesWithHoles} features have holes (not rendered yet)");
                Log.Info($"[MapRenderer]   💡 Future enhancement: implement hole rendering");
            }
        }

        // ═══════════════════════════════════════════
        //  PUBLIC API HELPERS — clear/toggle/get
        // ═══════════════════════════════════════════

        /// <summary>
        /// Clears all rendered layers
        /// </summary>
        private void ClearLayers()
        {
            foreach (var layer in layerObjects.Values)
            {
                if (layer != null)
                    Destroy(layer);
            }
            layerObjects.Clear();
        }

        /// <summary>
        /// Toggles visibility of a specific layer
        /// </summary>
        public void ToggleLayer(BinaryFormat.LayerType type, bool visible)
        {
            if (layerObjects.TryGetValue(type, out var layerObj))
            {
                layerObj.SetActive(visible);
            }
        }

        /// <summary>
        /// Returns the GameObject for a specific layer
        /// </summary>
        public GameObject GetLayerObject(BinaryFormat.LayerType type)
        {
            return layerObjects.TryGetValue(type, out var obj) ? obj : null;
        }

        /// <summary>
        /// Sets layer on GameObject and all its children recursively
        /// </summary>
        private Camera FindCameraInScene()
        {
            var scene = gameObject.scene;
            if (!scene.IsValid()) return null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var cam = root.GetComponentInChildren<Camera>(true);
                if (cam != null) return cam;
            }
            return null;
        }
    }
}
