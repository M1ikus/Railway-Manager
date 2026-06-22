using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using K4os.Compression.LZ4;
using formap;
using RailwayManager.Core;

namespace MapSystem
{
    /// <summary>
    /// Loads and decompresses binary map files
    /// Supports both FORMAP v5 (legacy) and v6/v7 (tiled) formats
    /// v5: Loads entire map at once
    /// v6: Loads tiles on-demand based on camera position
    /// v7: Multi-LOD tiled (5 LOD levels per tile)
    ///
    /// Klasa rozbita na partial files:
    /// - <c>MapLoader.cs</c>             — pola, lifecycle, LoadMap dispatcher,
    ///                                     LOD/extraction mode, public API getters (ten plik)
    /// - <c>MapLoader.LoadFormats.cs</c> — LoadMapV5/V6/V7 + LoadLayer (binary parsers)
    /// - <c>MapLoader.Tiles.cs</c>       — LoadTile + OnTileLoadedFromManager + ParseTileLogicLayers
    /// - <c>MapLoader.Streaming.cs</c>   — bulk: EnsureAllTilesLoadedSync (debug tools),
    ///                                     StreamAllTilesSync (production), LoadAllTilesSyncImpl
    /// - <c>MapLoader.Helpers.cs</c>     — static helpers: ClearNonLogicLayer, ClearMeshGeometryFully,
    ///                                     IsLogicLayer, SkipFeatureBytes, ValidateCount
    /// </summary>
    public partial class MapLoader : MonoBehaviour
    {
        [Header("Map File")]
        [Tooltip("Filename relative to StreamingAssets folder")]
        public string mapFileName = "Maps/Poland/poland-v8.bin";

        [Header("References")]
        public MapRenderer mapRenderer;
        public TileManager tileManager;

        [Header("Loading Settings")]
        public bool loadOnStart = true;
        public bool showDebugInfo = false;

        [Header("Map signature (v8 / FORMAP04)")]
        [Tooltip("Weryfikuj podpis Ed25519 podpisanej mapy v8 (refuse-to-load przy niepowodzeniu).")]
        public bool verifyV8Signature = true;
        [Tooltip("Wymagaj podpisanej mapy v8 — niepodpisany plik (signatureLength=0) zostanie odrzucony.")]
        public bool requireSignedV8 = true;

        // Loaded map data (v5 legacy)
        private Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> layers = new();
        private BBox globalBounds;
        private int version;

        // Tile data (v6/v7)
        private string mapFilePath;
        private Dictionary<long, BinaryFormat.TileIndexEntry> tileIndex = new();
        private Dictionary<long, BinaryFormat.TileIndexEntryV7> tileIndexV7 = new();
        private float tileSize;
        private int tilesX, tilesY;
        private int currentLOD = 0;
        private bool isV7Format = false;

        // v8 (FORMAP04)
        private bool isV8Format = false;
        private string[] stringTableV8;        // global string table — trzymana na czas życia pliku, używana przez decode kafli
        private int lodCountV8, layerCountV8;  // z headera v8 (guard przeciw v7-trap desync)

        // Tiles pending LOD swap — old visuals kept until new data arrives
        private HashSet<long> pendingLODReload = new();

        // M-PL: extraction mode suppresses MapRenderer.RenderTile podczas EnsureAllTilesLoadedSync
        // (5624 tiles × Mesh generation = ~20 GB RAM, freeze). Loader'y odczytują features
        // z tile cache normalnie; po EndExtraction tile data zwalniana, TileManager re-loads visible.
        private bool _suppressRenderForExtraction;
        private Coroutine _loadTilesRoutine;
        private bool _subscribedToTileManager;
        private TileManager _subscribedTileManager;

        // Extraction watchdog — jeśli BeginExtraction() bez następującego EndExtraction()
        // w timeout window, Update() wymusza cleanup + log error. Bez tego exception
        // w pipeline po BeginExtraction zostawia TileManager.Update zapauzowany na zawsze
        // → mapa pusta wizualnie bez stack trace developera.
        private float _extractionStartedAt = -1f;

        // 15 min daje margin dla pełnej Polski (Finalize 5-15 min realne). Krócej = false alarm,
        // dłużej = developer czeka długo na clear signal jeśli pipeline padł cicho.
        private const float ExtractionWatchdogTimeoutSeconds = 900f;

        // Public accessors
        public Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> Layers => layers;
        public BBox GlobalBounds => globalBounds;
        public int Version => version;
        public bool IsTiledFormat => version == 6 || version == 7 || version == 8;
        public int CurrentLOD => currentLOD;

        void Start()
        {
            if (loadOnStart)
            {
                LoadMap();
            }
        }

        void OnDestroy()
        {
            StopTileLoadingLoop();
            UnsubscribeFromTileManager();
        }

        void Update()
        {
            // Watchdog: detect orphaned extraction (BeginExtraction bez EndExtraction).
            // Trigger przy exception w TimetableInitializer pipeline — _suppressRenderForExtraction
            // zostaje true na zawsze, TileManager.Update wisi (pętla `if (ExtractionPaused) return;`),
            // mapa pusta. Watchdog wymusza EndExtraction po ExtractionWatchdogTimeoutSeconds.
            if (_suppressRenderForExtraction && _extractionStartedAt > 0f)
            {
                float elapsed = Time.realtimeSinceStartup - _extractionStartedAt;
                if (elapsed > ExtractionWatchdogTimeoutSeconds)
                {
                    Log.Error($"[MapLoader] ⚠ Extraction watchdog: {elapsed:F0}s bez EndExtraction() — wymuszam cleanup. Sprawdź TimetableInitializer/inne callery BeginExtraction (exception?).");
                    EndExtraction();
                }
            }
        }

        private void PrepareForMapReload()
        {
            StopTileLoadingLoop();
            UnsubscribeFromTileManager();

            _suppressRenderForExtraction = false;
            _extractionStartedAt = -1f;
            pendingLODReload.Clear();
            layers.Clear();
            tileIndex.Clear();
            tileIndexV7.Clear();
            currentLOD = 0;
            isV7Format = false;
            isV8Format = false;
            stringTableV8 = null;
            version = 0;

            if (tileManager != null)
            {
                tileManager.ExtractionPaused = false;
                tileManager.UnloadAllTiles();
            }

            if (mapRenderer != null)
            {
                mapRenderer.ClearAllTiles();
                mapRenderer.SetTiledMode(false);
            }
        }

        private void SubscribeToTileManager()
        {
            if (tileManager == null)
                return;
            if (_subscribedToTileManager && _subscribedTileManager == tileManager)
                return;

            UnsubscribeFromTileManager();

            tileManager.OnTileLoaded += OnTileLoadedFromManager;
            _subscribedTileManager = tileManager;
            _subscribedToTileManager = true;
        }

        private void UnsubscribeFromTileManager()
        {
            if (!_subscribedToTileManager)
                return;

            if (_subscribedTileManager != null)
                _subscribedTileManager.OnTileLoaded -= OnTileLoadedFromManager;
            _subscribedTileManager = null;
            _subscribedToTileManager = false;
        }

        private void StartTileLoadingLoop()
        {
            StopTileLoadingLoop();
            _loadTilesRoutine = StartCoroutine(LoadTilesCoroutine());
        }

        private void StopTileLoadingLoop()
        {
            if (_loadTilesRoutine == null)
                return;

            StopCoroutine(_loadTilesRoutine);
            _loadTilesRoutine = null;
        }

        /// <summary>
        /// Loads the map file from StreamingAssets
        /// Detects format version and loads accordingly
        /// </summary>
        public void LoadMap()
        {
            mapFilePath = Path.Combine(AppPaths.StreamingRoot, mapFileName);

            if (!File.Exists(mapFilePath))
            {
                Log.Error($"[MapLoader] Map file not found: {mapFilePath}");
                return;
            }

            PrepareForMapReload();

            Log.Info($"[MapLoader] Loading map from: {mapFilePath}");
            Log.Info($"[MapLoader] File size: {new FileInfo(mapFilePath).Length / 1024 / 1024}MB");

            float startTime = Time.realtimeSinceStartup;

            try
            {
                using var fileStream = File.OpenRead(mapFilePath);
                using var reader = new BinaryReader(fileStream);

                // Read magic number to detect version
                byte[] magicBytes = reader.ReadBytes(8);
                string magic = System.Text.Encoding.ASCII.GetString(magicBytes);

                // Reset to start for actual reading
                fileStream.Seek(0, SeekOrigin.Begin);

                if (magic == BinaryFormat.MagicV8)
                {
                    LoadMapV8(reader);
                }
                else if (magic == BinaryFormat.MagicV7)
                {
                    LoadMapV7(reader);
                }
                else if (magic == BinaryFormat.MagicV6)
                {
                    LoadMapV6(reader);
                }
                else if (magic == BinaryFormat.MagicV5)
                {
                    LoadMapV5(reader);
                }
                else
                {
                    throw new InvalidDataException($"Unknown map format: {magic}");
                }

                float loadTime = Time.realtimeSinceStartup - startTime;
                Log.Info($"[MapLoader] ✓ Map loaded successfully in {loadTime:F2}s");
            }
            catch (Exception ex)
            {
                Log.Error($"[MapLoader] Failed to load map: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Changes LOD level — gradually reloads tiles across frames (no lag spike).
        /// Old tiles stay visible until new LOD version is loaded (double-buffer).
        /// </summary>
        public void SetLODLevel(int newLOD)
        {
            if (newLOD == currentLOD) return;
            int oldLOD = currentLOD;
            currentLOD = Mathf.Clamp(newLOD, 0, BinaryFormat.LODCount - 1);

            if (showDebugInfo)
                Log.Info($"[MapLoader] LOD changed: {oldLOD} -> {currentLOD}");

            if (tileManager == null) return;

            tileManager.SetCurrentLOD(currentLOD); // skalowanie frustum margin (M-PL-3)

            // Mark all loaded tiles for reload with new LOD
            var tilesToReload = new List<long>();
            foreach (var tileID in tileIndex.Keys)
            {
                if (tileManager.IsTileLoaded(tileID))
                    tilesToReload.Add(tileID);
            }

            foreach (var tileID in tilesToReload)
            {
                pendingLODReload.Add(tileID);
                tileManager.ForceReloadTile(tileID);
            }

            tileManager.ForceUpdate();
        }

        /// <summary>
        /// Włącza tryb ekstrakcji: <see cref="OnTileLoadedFromManager"/> nie wywołuje
        /// <c>MapRenderer.RenderTile</c>, ale nadal czyści non-logic layer geometry.
        /// Loader'y mogą czytać tile data przez <see cref="GetAllFeaturesAcrossTiles"/>.
        /// **Wymagane przed bulk loading na pełnej Polsce — bez tego ~20 GB RAM na meshes.**
        /// </summary>
        public void BeginExtraction()
        {
            if (_suppressRenderForExtraction)
            {
                float prevElapsed = _extractionStartedAt > 0f
                    ? Time.realtimeSinceStartup - _extractionStartedAt
                    : -1f;
                Log.Warn($"[MapLoader] BeginExtraction() wywołane gdy extraction już active od {prevElapsed:F0}s — duplicate call? Resetuję timestamp.");
            }
            _suppressRenderForExtraction = true;
            _extractionStartedAt = Time.realtimeSinceStartup;
            // Pause TileManager.Update + LoadTilesCoroutine — streaming ma exclusive access
            if (tileManager != null) tileManager.ExtractionPaused = true;
            Log.Info("[MapLoader] BeginExtraction — RenderTile suppressed, TileManager paused");
        }

        /// <summary>
        /// Kończy tryb ekstrakcji + bulk unload tile cache + GC + force update.
        /// Po tym TileManager normalnie żąda visible tile'i — RenderTile zadziała poprawnie.
        /// </summary>
        public void EndExtraction()
        {
            _suppressRenderForExtraction = false;
            _extractionStartedAt = -1f;
            if (tileManager != null) tileManager.ExtractionPaused = false;
            Log.Info("[MapLoader] EndExtraction — releasing tile cache, TileManager resumed");
            ReleaseAllTilesAfterExtraction();
        }

        // Budżetowane ładowanie kafli: zamiast 1 kafel/klatkę ładuj kilka aż do budżetu czasu,
        // potem yield. Adaptuje się do sprzętu (drogi kafel >budżet → i tak yield po 1, jak dawniej),
        // a na mocnym CPU znacząco przyspiesza pokrycie (kafle dociągają się dużo szybciej).
        [Tooltip("Maks. czas (ms) na ładowanie kafli w jednej klatce zanim yield.")]
        public float tileLoadFrameBudgetMs = 5f;
        [Tooltip("Twardy limit kafli na klatkę (zapobiega hitchom przy tanich kaflach).")]
        public int tileLoadMaxPerFrame = 8;

        /// <summary>
        /// Coroutine: ładuje kafle z budżetem czasu na klatkę (kilka kafli/klatkę do limitu).
        /// **Paused podczas extraction** — streaming używa shared file handle, równoległe
        /// ładowanie powodowało hang po N tiles (file contention / state interference);
        /// dlatego batch dotyczy WYŁĄCZNIE normalnego streamingu (poza ekstrakcją).
        /// </summary>
        private System.Collections.IEnumerator LoadTilesCoroutine()
        {
            while (true)
            {
                // M-PL: pause podczas extraction, streaming ma exclusive access do file
                if (_suppressRenderForExtraction)
                {
                    yield return null;
                    continue;
                }

                var tilesToLoad = tileManager.GetTilesToLoad();
                if (tilesToLoad.Count == 0)
                {
                    yield return null;
                    continue;
                }

                float frameStart = Time.realtimeSinceStartup;
                int inFrame = 0;
                foreach (var tileID in tilesToLoad)
                {
                    LoadTile(tileID);
                    inFrame++;

                    float elapsedMs = (Time.realtimeSinceStartup - frameStart) * 1000f;
                    if (inFrame >= tileLoadMaxPerFrame || elapsedMs >= tileLoadFrameBudgetMs)
                    {
                        yield return null;
                        frameStart = Time.realtimeSinceStartup;
                        inFrame = 0;
                    }
                }

                yield return null;
            }
        }

        /// <summary>
        /// Decompresses LZ4 data using Unity's built-in decoder
        /// Compatible with K4os.Compression.LZ4.LZ4Pickler format
        /// </summary>
        private byte[] DecompressLZ4(byte[] compressed)
        {
            try
            {
                return LZ4Pickler.Unpickle(compressed);
            }
            catch (Exception ex)
            {
                Log.Error($"[MapLoader] LZ4 decompression error: {ex.Message}");
                return null;
            }
        }

        // ═══════════════════════════════════════════
        //  PUBLIC QUERY API
        // ═══════════════════════════════════════════

        /// <summary>
        /// Returns all features of a specific layer type (v5 legacy non-tiled format only).
        /// For v6/v7 tiled maps use GetAllFeaturesAcrossTiles().
        /// </summary>
        public List<MeshGeometry> GetLayer(BinaryFormat.LayerType layerType)
        {
            return layers.TryGetValue(layerType, out var layer) ? layer : new List<MeshGeometry>();
        }

        /// <summary>
        /// Returns all features of a layer across all currently-loaded tiles (v6/v7) plus v5 legacy.
        /// For debug tools / bulk operations after EnsureAllTilesLoadedSync().
        /// Features replicated across multiple tiles (e.g. AdminBoundaries polygon spanning 400 tiles)
        /// will appear multiple times — caller is responsible for deduplication.
        /// </summary>
        public List<MeshGeometry> GetAllFeaturesAcrossTiles(BinaryFormat.LayerType layerType)
        {
            var result = new List<MeshGeometry>();

            // v5 legacy — whole map in single layers dict
            if (layers.TryGetValue(layerType, out var legacy))
                result.AddRange(legacy);

            // v6/v7 — aggregate across all tiles currently in TileManager
            if (tileManager != null)
            {
                foreach (var tile in tileManager.GetAllLoadedTiles())
                {
                    if (tile.Layers != null && tile.Layers.TryGetValue(layerType, out var tileLayer))
                        result.AddRange(tileLayer);
                }
            }

            return result;
        }

        /// <summary>
        /// Bulk release tile cache + meshes po <see cref="EnsureAllTilesLoadedSync"/>.
        /// Loader'y już skopiowali potrzebne dane (AdminBoundaries, Places, Stations,
        /// Platforms, Railways → graph cache), więc 30+ GB tile data na pełnej Polsce
        /// jest zbędne. Wywołuje GC żeby zwolnić pamięć natychmiast.
        ///
        /// **Wymagane na pełnej Polsce** — bez tego po `EnsureAllTilesLoadedSync` RAM
        /// rośnie do 30-40 GB i Unity zamarza. TileManager re-loads tylko visible tile'e
        /// w next tick UpdateTileLoading.
        /// </summary>
        public void ReleaseAllTilesAfterExtraction()
        {
            if (tileManager == null) return;
            int unloaded = tileManager.UnloadAllTiles();
            pendingLODReload.Clear();
            Resources.UnloadUnusedAssets();
            System.GC.Collect();
            Log.Info($"[MapLoader] ReleaseAllTilesAfterExtraction: unloaded {unloaded} tiles + GC");
        }

        /// <summary>
        /// Returns total feature count across all layers
        /// </summary>
        public int GetTotalFeatureCount()
        {
            int total = 0;
            foreach (var layer in layers.Values)
                total += layer.Count;
            return total;
        }

        /// <summary>
        /// Returns features with holes (for debugging)
        /// ✅ NEW: Shows which features have multipolygon holes
        /// </summary>
        public int GetFeaturesWithHolesCount()
        {
            int count = 0;
            foreach (var layer in layers.Values)
            {
                foreach (var feature in layer)
                    if (feature.HoleStarts.Count > 0) count++;
            }
            return count;
        }
    }
}
