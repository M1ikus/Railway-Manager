using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using formap;

namespace MapSystem
{
    /// <summary>
    /// Manages tile loading and unloading based on camera view frustum
    /// Implements spatial culling for efficient map rendering
    /// </summary>
    public class TileManager : MonoBehaviour
    {
        public const float TILE_SIZE = TileGrid.TILE_SIZE; // 10km x 10km tiles (must match converter)

        [Header("Tile Loading Settings")]
        [Tooltip("Use camera frustum for tile loading (recommended). If false, uses fixed radius.")]
        public bool useFrustumCulling = true;

        [Tooltip("Distance in tiles from camera to keep loaded (only used if useFrustumCulling = false)")]
        public int tileLoadRadius = 2;

        [Tooltip("Extra margin in tiles around visible area (prevents pop-in)")]
        public int frustumMargin = 1;

        [Tooltip("Tiles beyond frustum + this margin are unloaded from memory")]
        public int unloadMargin = 2;

        [Tooltip("Minimum camera movement (meters) to trigger update in frustum mode")]
        public float frustumUpdateThreshold = 500f;

        [Tooltip("Update tile loading this many times per second")]
        public float updateFrequency = 1f;

        [Header("Memory Cap (M-PL-3)")]
        [Tooltip("Maksymalna liczba tile'i w pamięci. Gdy przekroczone — LRU eviction najstarszych (nie w visible zone).")]
        public int maxLoadedTiles = 256;

        [Header("References")]
        public Camera mainCamera;

        [Header("Debug")]
        public bool showDebugInfo = false;
        public bool drawTileGizmos = true;

        // Tile data
        private Dictionary<long, TileData> loadedTiles = new();
        private Dictionary<long, BinaryFormat.TileIndexEntry> tileIndex = new();
        private BBox globalBounds;
        private float tileSize;
        private int tilesX, tilesY;

        // Current camera tile
        private (int gridX, int gridY) currentTile = (-1, -1);
        private float updateTimer = 0f;
        private bool forceNextUpdate = false;

        // Current LOD — pushed from MapLoader via SetCurrentLOD(). Sterowane przez MapLoader,
        // ale TileManager musi wiedzieć żeby skalować frustumMargin (zoom out = więcej tile'i widać).
        private int currentLOD = 0;

        // Track camera state for frustum culling
        private Vector3 lastCameraPosition = Vector3.zero;
        private float lastCameraHeight = 0f;
        private float lastOrthographicSize = 0f;
        private Quaternion lastCameraRotation = Quaternion.identity;

        // M-PL: pause Update podczas extraction (streaming używa shared file, race conditions
        // z UpdateTileLoading + LoadTilesCoroutine powodowały hang po N tile).
        public bool ExtractionPaused { get; set; }

        // Events
        public event Action<long, TileData> OnTileLoaded;
        public event Action<long> OnTileUnloaded;

        // Reference to renderer for cleanup
        private MapRenderer mapRenderer;

        // Scratch buffers reused per UpdateTileLoading — eliminuje per-update HashSet/List
        // allocation (4 alokacje × ~1Hz × cały session = GC pressure). Clear() przed użyciem.
        private readonly HashSet<long> _scratchVisible = new();
        private readonly HashSet<long> _scratchCache = new();
        private readonly List<long> _scratchTilesToUnload = new();
        private readonly List<(long id, float time)> _scratchEvictionCandidates = new();

        // --- Pin-set (RouteMapPreview) ---
        // Kafle PINOWANE przez druga kamerę (mini-podgląd trasy) muszą być załadowane i
        // widoczne NIEZALEŻNIE od frustum głównej kamery oraz chronione przed LRU eviction.
        // TileManager pozostaje single-camera (mainCamera) + ten zbiór "interest points".
        private readonly HashSet<long> _pinnedTiles = new();
        private readonly Dictionary<string, HashSet<long>> _pinGroups = new();

        /// <summary>Liczba kafli aktualnie pinowanych (suma wszystkich grup).</summary>
        public int PinnedTileCount => _pinnedTiles.Count;

        /// <summary>Czy dany kafel jest pinowany przez którąkolwiek grupę.</summary>
        public bool IsTilePinned(long tileID) => _pinnedTiles.Contains(tileID);

        void Start()
        {
            if (mainCamera == null)
                mainCamera = Camera.main;
        }

        void Update()
        {
            // M-PL: pause podczas extraction — streaming używa shared file handle,
            // równoległe UpdateTileLoading + LoadTile może race condition.
            if (ExtractionPaused) return;

            updateTimer += Time.deltaTime;
            if (updateTimer >= 1f / updateFrequency)
            {
                updateTimer = 0f;
                UpdateTileLoading();
            }
        }

        /// <summary>
        /// Initializes the tile manager with tile index from file
        /// </summary>
        public void Initialize(Dictionary<long, BinaryFormat.TileIndexEntry> index, BBox bounds,
            float tileSz, int gridX, int gridY, MapRenderer renderer = null)
        {
            tileIndex = index;
            globalBounds = bounds;
            tileSize = tileSz;
            tilesX = gridX;
            tilesY = gridY;
            mapRenderer = renderer;

            if (showDebugInfo)
            {
                Log.Info($"[TileManager] Initialized with {tileIndex.Count} tiles");
                Log.Info($"[TileManager] Grid: {tilesX} x {tilesY}, Tile size: {tileSize}m");
            }
        }

        /// <summary>
        /// Updates which tiles should be loaded based on camera position or frustum
        /// </summary>
        private void UpdateTileLoading()
        {
            if (mainCamera == null || tileIndex.Count == 0)
                return;

            // Get camera position and current tile
            Vector3 camPos = mainCamera.transform.position;
            var (gridX, gridY) = TileGrid.WorldToGrid(camPos.x, camPos.z);

            // For frustum culling, also check if camera moved significantly or rotated
            bool shouldUpdate = false;

            if (useFrustumCulling)
            {
                // Update if camera position changed significantly
                float positionDelta = Vector3.Distance(camPos, lastCameraPosition);

                // Update if camera rotated (affects frustum)
                float rotationDelta = Quaternion.Angle(mainCamera.transform.rotation, lastCameraRotation);

                // Adaptive threshold based on camera type
                float adaptiveThreshold = frustumUpdateThreshold;
                bool zoomChanged = false;

                if (mainCamera.orthographic)
                {
                    // For orthographic: check if zoom level changed (orthographicSize)
                    float orthoSizeDelta = Mathf.Abs(mainCamera.orthographicSize - lastOrthographicSize);

                    // CRITICAL FIX: At large orthographic sizes, even small changes matter
                    // Update if absolute change > 500 OR percentage change > 5%
                    float orthoSizeChangePercent = lastOrthographicSize > 0
                        ? orthoSizeDelta / lastOrthographicSize
                        : 1f;

                    zoomChanged = orthoSizeDelta > 500f || orthoSizeChangePercent > 0.05f;

                    // Threshold scales with orthographic size, but with smaller multiplier for large sizes
                    if (mainCamera.orthographicSize > 10000f)
                    {
                        // At extreme zoom, use very small threshold
                        adaptiveThreshold = Mathf.Min(frustumUpdateThreshold, mainCamera.orthographicSize * 0.1f);
                    }
                    else
                    {
                        adaptiveThreshold = Mathf.Min(frustumUpdateThreshold, mainCamera.orthographicSize * 0.5f);
                    }
                }
                else
                {
                    // For perspective: check if height changed significantly
                    float heightDelta = Mathf.Abs(camPos.y - lastCameraHeight);
                    zoomChanged = heightDelta > adaptiveThreshold;

                    // Threshold scales with height
                    adaptiveThreshold = Mathf.Min(frustumUpdateThreshold, camPos.y * 0.1f);
                }

                shouldUpdate = positionDelta > adaptiveThreshold
                            || zoomChanged
                            || rotationDelta > 5f;

                if (shouldUpdate)
                {
                    lastCameraPosition = camPos;
                    lastCameraHeight = camPos.y;
                    lastOrthographicSize = mainCamera.orthographicSize;
                    lastCameraRotation = mainCamera.transform.rotation;
                }
            }
            else
            {
                // For radius mode, only update when changing tiles
                shouldUpdate = (gridX, gridY) != currentTile;
            }

            if (shouldUpdate || currentTile == (-1, -1) || forceNextUpdate)
            {
                currentTile = (gridX, gridY);
                forceNextUpdate = false;

                if (showDebugInfo)
                {
                    string cameraInfo = mainCamera.orthographic
                        ? $"orthoSize={mainCamera.orthographicSize:F0}"
                        : $"height={camPos.y:F0}m";
                    Log.Info($"[TileManager] Update triggered - Camera in tile ({gridX}, {gridY}), {cameraInfo}");
                }

                // Per-LOD margin: zoom out = wyższy LOD = więcej tile'i w polu widzenia
                // Baseline skaluje się +1 per 2 LOD (LOD0-1: +0, LOD2-3: +1, LOD4-5: +2)
                int lodBonus = Mathf.FloorToInt(currentLOD / 2f);
                int effectiveFrustumMargin = frustumMargin + lodBonus;
                int effectiveCacheMargin = unloadMargin + lodBonus;

                // 3 strefy: visible (render), cache (hidden but in memory), unload (destroy).
                // Frustum mode: single raycast + jeden grid loop fillet obie sety (cacheTiles ⊇ visibleTiles).
                // Radius mode: cache zone nieobecna — cacheTiles pozostaje pusty (else if Contains zwróci false → unload).
                _scratchVisible.Clear();
                _scratchCache.Clear();
                _scratchTilesToUnload.Clear();

                if (useFrustumCulling)
                {
                    FillVisibleAndCacheTilesFromFrustum(effectiveFrustumMargin, effectiveCacheMargin,
                        _scratchVisible, _scratchCache);
                }
                else
                {
                    FillTilesInRadius(gridX, gridY, _scratchVisible);
                }

                // Request loading for visible tiles
                foreach (var tileID in _scratchVisible)
                {
                    if (!loadedTiles.ContainsKey(tileID))
                    {
                        RequestTileLoad(tileID);
                    }
                }

                // Pin-set (RouteMapPreview): zapewnij załadowanie pinowanych kafli niezależnie
                // od frustum głównej kamery — koroutyna MapLoader.LoadTilesCoroutine podejmie je
                // przez GetTilesToLoad() (nie potrzeba BeginExtraction).
                if (_pinnedTiles.Count > 0)
                {
                    foreach (var tileID in _pinnedTiles)
                        if (!loadedTiles.ContainsKey(tileID))
                            RequestTileLoad(tileID);
                }

                // Manage loaded tiles: show/hide/unload + bump LastAccessTime (LRU bookkeeping).
                // Polityka per-kafel w TilePinPolicy (pinned = zawsze Render — widzi je kamera podglądu).
                float now = Time.realtimeSinceStartup;
                foreach (var tileID in loadedTiles.Keys)
                {
                    var tile = loadedTiles[tileID];
                    var action = TilePinPolicy.ResolveAction(
                        _scratchVisible.Contains(tileID),
                        _pinnedTiles.Contains(tileID),
                        _scratchCache.Contains(tileID));
                    switch (action)
                    {
                        case TilePinPolicy.TileAction.Render:
                            tile.LastAccessTime = now;
                            if (mapRenderer != null)
                                mapRenderer.SetTileVisible(tileID, true);
                            break;
                        case TilePinPolicy.TileAction.CacheHide:
                            tile.LastAccessTime = now;
                            if (mapRenderer != null)
                                mapRenderer.SetTileVisible(tileID, false);
                            break;
                        default: // Unload — ale NIE gdy tile w trakcie async load (IsLoaded=false)
                            if (tile.IsLoaded)
                                _scratchTilesToUnload.Add(tileID);
                            break;
                    }
                }

                foreach (var tileID in _scratchTilesToUnload)
                {
                    UnloadTile(tileID);
                }

                // Hard cap + LRU eviction — gdy cache wciąż przekracza limit (np. ogromny frustum przy zoom out)
                EvictOldestIfOverCap(_scratchVisible);
            }
        }

        /// <summary>
        /// LRU eviction najstarszych loaded tile'i (po <see cref="TileData.LastAccessTime"/>) gdy
        /// <see cref="loadedTiles"/>.Count przekracza <see cref="maxLoadedTiles"/>.
        /// Nigdy nie usuwa tile'i w <paramref name="visibleTiles"/> (gracz właśnie na nie patrzy)
        /// ani pending (<see cref="TileData.IsLoaded"/>=false).
        /// </summary>
        private void EvictOldestIfOverCap(HashSet<long> visibleTiles)
        {
            int toEvict = loadedTiles.Count - maxLoadedTiles;
            if (toEvict <= 0) return;

            // Kandydaci: loaded, nie w visible zone. Cache zone jest fair game (najstarsze z niej lecą).
            // Scratch list reused across updates — Clear() przed użyciem.
            _scratchEvictionCandidates.Clear();
            foreach (var kvp in loadedTiles)
            {
                // Chronione: visible zone, PINOWANE (RouteMapPreview), pending (IsLoaded=false).
                if (!TilePinPolicy.CanEvict(visibleTiles.Contains(kvp.Key), _pinnedTiles.Contains(kvp.Key), kvp.Value.IsLoaded))
                    continue;
                _scratchEvictionCandidates.Add((kvp.Key, kvp.Value.LastAccessTime));
            }

            if (_scratchEvictionCandidates.Count == 0) return;

            _scratchEvictionCandidates.Sort((a, b) => a.time.CompareTo(b.time)); // ascending — najstarsze pierwsze

            int evicted = 0;
            for (int i = 0; i < _scratchEvictionCandidates.Count && evicted < toEvict; i++)
            {
                UnloadTile(_scratchEvictionCandidates[i].id);
                evicted++;
            }

            if (evicted > 0 && showDebugInfo)
                Log.Info($"[TileManager] LRU eviction: {evicted} tiles (cap={maxLoadedTiles}, now={loadedTiles.Count})");
        }

        /// <summary>
        /// Sync LOD level z MapLoader. Używane do skalowania frustum margin (LOD out = szerszy margin).
        /// Wywołanie powoduje natychmiastowy forceUpdate — tile'e zostaną przekalkulowane.
        /// </summary>
        public void SetCurrentLOD(int lod)
        {
            int clamped = Mathf.Clamp(lod, 0, BinaryFormat.LODCount - 1);
            if (clamped == currentLOD) return;
            currentLOD = clamped;
            forceNextUpdate = true;
        }

        /// <summary>
        /// Wypełnia <paramref name="set"/> tile'ami w prostokątnym promieniu wokół (centerX, centerY).
        /// Używane gdy <see cref="useFrustumCulling"/>=false (legacy radius mode).
        /// </summary>
        private void FillTilesInRadius(int centerX, int centerY, HashSet<long> set)
        {
            for (int dx = -tileLoadRadius; dx <= tileLoadRadius; dx++)
            {
                for (int dy = -tileLoadRadius; dy <= tileLoadRadius; dy++)
                {
                    int tx = centerX + dx;
                    int ty = centerY + dy;
                    long tileID = TileGrid.GetTileID(tx, ty);

                    if (tileIndex.ContainsKey(tileID))
                    {
                        set.Add(tileID);
                    }
                }
            }
        }

        /// <summary>
        /// Wylicza world bounds widoku kamery przez projekcję 4 viewport/frustum corners
        /// na ground plane Y=0. Jedno wywołanie raycastu zamiast wcześniejszych dwóch
        /// (gdy oddzielnie liczono visible i cache margin range).
        /// </summary>
        private void ComputeFrustumWorldBounds(out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = float.MaxValue; maxX = float.MinValue;
            minZ = float.MaxValue; maxZ = float.MinValue;

            if (mainCamera.orthographic)
            {
                // For orthographic camera, project 4 viewport corners onto ground plane
                Vector3 c0 = new Vector3(0, 0, mainCamera.nearClipPlane);
                Vector3 c1 = new Vector3(1, 0, mainCamera.nearClipPlane);
                Vector3 c2 = new Vector3(0, 1, mainCamera.nearClipPlane);
                Vector3 c3 = new Vector3(1, 1, mainCamera.nearClipPlane);
                Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
                ProjectCornerOntoGround(c0, groundPlane, ref minX, ref maxX, ref minZ, ref maxZ);
                ProjectCornerOntoGround(c1, groundPlane, ref minX, ref maxX, ref minZ, ref maxZ);
                ProjectCornerOntoGround(c2, groundPlane, ref minX, ref maxX, ref minZ, ref maxZ);
                ProjectCornerOntoGround(c3, groundPlane, ref minX, ref maxX, ref minZ, ref maxZ);
            }
            else
            {
                // For perspective camera, use frustum corner projection method
                Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
                Vector3[] frustumCorners = new Vector3[4];
                mainCamera.CalculateFrustumCorners(
                    new Rect(0, 0, 1, 1),
                    mainCamera.farClipPlane,
                    Camera.MonoOrStereoscopicEye.Mono,
                    frustumCorners
                );

                Vector3 camPos = mainCamera.transform.position;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 worldCorner = mainCamera.transform.TransformPoint(frustumCorners[i]);
                    Ray ray = new Ray(camPos, worldCorner - camPos);

                    if (groundPlane.Raycast(ray, out float distance))
                    {
                        Vector3 hitPoint = ray.GetPoint(distance);
                        if (hitPoint.x < minX) minX = hitPoint.x;
                        if (hitPoint.x > maxX) maxX = hitPoint.x;
                        if (hitPoint.z < minZ) minZ = hitPoint.z;
                        if (hitPoint.z > maxZ) maxZ = hitPoint.z;
                    }
                }
            }
        }

        private void ProjectCornerOntoGround(Vector3 viewportCorner, Plane groundPlane,
            ref float minX, ref float maxX, ref float minZ, ref float maxZ)
        {
            Ray ray = mainCamera.ViewportPointToRay(viewportCorner);
            if (!groundPlane.Raycast(ray, out float distance)) return;
            Vector3 p = ray.GetPoint(distance);
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        /// <summary>
        /// Wypełnia obie sety jednym przebiegiem: <paramref name="visSet"/> dla tile'i w
        /// <paramref name="visMargin"/>, <paramref name="cacheSet"/> dla tile'i w
        /// <paramref name="cacheMargin"/>. Założenie: cacheMargin ≥ visMargin, więc
        /// cacheSet ⊇ visSet. Pojedynczy compute frustum bounds + pojedynczy grid loop.
        /// </summary>
        private void FillVisibleAndCacheTilesFromFrustum(int visMargin, int cacheMargin,
            HashSet<long> visSet, HashSet<long> cacheSet)
        {
            ComputeFrustumWorldBounds(out float minX, out float maxX, out float minZ, out float maxZ);

            // Convert world bounds to tile grid coordinates (raw, bez margin)
            var (minGridX, minGridY) = TileGrid.WorldToGrid(minX, minZ);
            var (maxGridX, maxGridY) = TileGrid.WorldToGrid(maxX, maxZ);

            // Visible range (mniejszy margin)
            int visMinX = minGridX - visMargin;
            int visMaxX = maxGridX + visMargin;
            int visMinY = minGridY - visMargin;
            int visMaxY = maxGridY + visMargin;

            // Cache range (większy margin) — loop boundary
            int cacheMinX = minGridX - cacheMargin;
            int cacheMaxX = maxGridX + cacheMargin;
            int cacheMinY = minGridY - cacheMargin;
            int cacheMaxY = maxGridY + cacheMargin;

            // Single grid loop fills both sets — cacheSet to superset, visSet jest inner band.
            for (int gx = cacheMinX; gx <= cacheMaxX; gx++)
            {
                for (int gy = cacheMinY; gy <= cacheMaxY; gy++)
                {
                    long tileID = TileGrid.GetTileID(gx, gy);
                    if (!tileIndex.ContainsKey(tileID)) continue;

                    cacheSet.Add(tileID);
                    if (gx >= visMinX && gx <= visMaxX && gy >= visMinY && gy <= visMaxY)
                        visSet.Add(tileID);
                }
            }

            if (showDebugInfo)
            {
                Log.Info($"[TileManager] Frustum: vis X=[{visMinX},{visMaxX}] Y=[{visMinY},{visMaxY}] " +
                         $"cache X=[{cacheMinX},{cacheMaxX}] Y=[{cacheMinY},{cacheMaxY}] " +
                         $"-> vis={visSet.Count} cache={cacheSet.Count}");
            }
        }

        /// <summary>
        /// Requests a tile to be loaded (called by MapLoader)
        /// </summary>
        public void RequestTileLoad(long tileID)
        {
            if (loadedTiles.ContainsKey(tileID))
                return;

            if (!tileIndex.TryGetValue(tileID, out var indexEntry))
            {
                Log.Warn($"[TileManager] Tile {tileID} not found in index");
                return;
            }

            // Create placeholder - actual data loaded by MapLoader
            var tileData = new TileData(tileID, indexEntry.GridX, indexEntry.GridY, indexEntry.Bounds);

            loadedTiles[tileID] = tileData;

            if (showDebugInfo)
                Log.Info($"[TileManager] Requested tile ({indexEntry.GridX}, {indexEntry.GridY})");
        }

        // --- Pin API (RouteMapPreview) ---

        /// <summary>
        /// Pinuje zbiór kafli pod danym <paramref name="pinId"/> (np. "preview-route"): zapewnia
        /// ich załadowanie i widoczność niezależnie od frustum głównej kamery oraz ochronę przed
        /// LRU eviction. Ponowne wywołanie z tym samym pinId ZASTĘPUJE poprzedni zbiór grupy
        /// (idempotentne dla pan/zoom — każda zmiana widoku przekazuje nowy viewport). Kafle spoza
        /// indeksu (morze, poza granicą) są pomijane bez ostrzeżenia. Cap <see cref="maxPinnedTiles"/>
        /// chroni pamięć przy bardzo długiej trasie — nadmiar odcinany z logiem (uczciwy limit;
        /// linia trasy rysuje się i tak w całości jako wektor).
        /// </summary>
        public void RequestPinnedTilesForRegion(IEnumerable<long> tileIds, string pinId)
        {
            if (string.IsNullOrEmpty(pinId) || tileIds == null) return;

            if (!_pinGroups.TryGetValue(pinId, out var group))
            {
                group = new HashSet<long>();
                _pinGroups[pinId] = group;
            }
            group.Clear();
            // Bez capa — pinujemy WSZYSTKIE kafle regionu (viewport podglądu jest naturalnym
            // ograniczeniem). Pinned są chronione przed eviction, streaming dociąga je w tle.
            foreach (var id in tileIds)
                if (tileIndex.ContainsKey(id))
                    group.Add(id);

            RebuildPinnedUnion();

            // Natychmiast: request-load brakujących + pokaż już-załadowane (nie czekaj na next Update).
            foreach (var id in group)
            {
                if (!loadedTiles.ContainsKey(id))
                    RequestTileLoad(id);
                else if (mapRenderer != null && loadedTiles[id].IsLoaded)
                    mapRenderer.SetTileVisible(id, true);
            }
            forceNextUpdate = true;
        }

        /// <summary>
        /// Zdejmuje pin grupy. Kafle nie-pinowane już przez inną grupę wracają do normalnego
        /// zarządzania (następny Update ukryje/odładuje je wg frustum głównej kamery).
        /// </summary>
        public void UnpinTilesForRegion(string pinId)
        {
            if (string.IsNullOrEmpty(pinId)) return;
            if (_pinGroups.Remove(pinId))
            {
                RebuildPinnedUnion();
                forceNextUpdate = true;
            }
        }

        private void RebuildPinnedUnion()
        {
            _pinnedTiles.Clear();
            foreach (var g in _pinGroups.Values)
                foreach (var id in g)
                    _pinnedTiles.Add(id);
        }

        /// <summary>
        /// Marks a tile as loaded with data
        /// </summary>
        public void MarkTileLoaded(long tileID, Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> layers)
        {
            if (loadedTiles.TryGetValue(tileID, out var tileData))
            {
                tileData.Layers = layers;
                tileData.IsLoaded = true;
                OnTileLoaded?.Invoke(tileID, tileData);

                if (showDebugInfo)
                {
                    int featureCount = 0;
                    foreach (var layer in layers.Values)
                        featureCount += layer.Count;
                    Log.Info($"[TileManager] Tile {tileID} loaded with {featureCount} features");
                }
            }
        }

        /// <summary>
        /// Unloads a tile
        /// </summary>
        private void UnloadTile(long tileID)
        {
            if (!loadedTiles.ContainsKey(tileID))
                return;

            // Notify renderer to destroy mesh objects
            if (mapRenderer != null)
            {
                mapRenderer.UnloadTile(tileID);
            }

            loadedTiles.Remove(tileID);
            OnTileUnloaded?.Invoke(tileID);

            if (showDebugInfo)
                Log.Info($"[TileManager] Unloaded tile {tileID}");
        }

        /// <summary>
        /// Gets list of tiles that should be loaded
        /// </summary>
        public List<long> GetTilesToLoad()
        {
            List<long> result = new List<long>();

            foreach (var kvp in loadedTiles)
            {
                if (!kvp.Value.IsLoaded)
                    result.Add(kvp.Key);
            }

            return result;
        }

        /// <summary>
        /// Gets tile index entry
        /// </summary>
        public bool GetTileIndexEntry(long tileID, out BinaryFormat.TileIndexEntry entry)
        {
            return tileIndex.TryGetValue(tileID, out entry);
        }

        /// <summary>
        /// Forces tile loading update on next frame (used after LOD change)
        /// </summary>
        public void ForceUpdate()
        {
            forceNextUpdate = true;
        }

        /// <summary>
        /// Forces a tile to be reloaded (used when LOD level changes).
        /// Keeps old visuals alive — caller is responsible for destroying them after new data arrives.
        /// Marks tile as not-loaded so async loader picks it up, but keeps it in loadedTiles
        /// to prevent UpdateTileLoading from unloading it.
        /// </summary>
        public void ForceReloadTile(long tileID)
        {
            if (loadedTiles.TryGetValue(tileID, out var tile))
            {
                // Mark as not loaded — async loader will pick it up via GetTilesToLoad()
                // Old visuals stay in scene until MapLoader.OnTileLoadedFromManager destroys them
                tile.IsLoaded = false;
                tile.Layers?.Clear();
                tile.Layers = null;
            }
            else
            {
                // Not in loadedTiles — just request normally
                RequestTileLoad(tileID);
            }
        }

        /// <summary>
        /// Checks if tile is loaded
        /// </summary>
        public bool IsTileLoaded(long tileID)
        {
            return loadedTiles.TryGetValue(tileID, out var tile) && tile.IsLoaded;
        }

        /// <summary>
        /// Gets loaded tile data
        /// </summary>
        public TileData GetTileData(long tileID)
        {
            return loadedTiles.TryGetValue(tileID, out var tile) ? tile : null;
        }

        /// <summary>
        /// Returns all currently loaded tiles — for debug tools and bulk feature aggregation.
        /// </summary>
        public IEnumerable<TileData> GetAllLoadedTiles() => loadedTiles.Values;

        /// <summary>Number of tiles currently in memory (loaded or pending).</summary>
        public int LoadedTileCount => loadedTiles.Count;

        /// <summary>Total tiles in the index (the whole map).</summary>
        public int TotalTileCount => tileIndex.Count;

        /// <summary>
        /// Bulk-unload wszystkich tile'i (data + meshes via mapRenderer.UnloadTile).
        /// Używane po <c>MapLoader.EnsureAllTilesLoadedSync</c> w TimetableInitializer —
        /// Loader'y skopiowali co potrzebują, a tile cache na pełnej Polsce zajmuje
        /// 30+ GB RAM (5624 tile'e × decompressed + Mesh objects). Po wywołaniu
        /// <see cref="UpdateTileLoading"/> w next tick re-loads tylko visible tile'e
        /// (~30-50 zamiast 5624). Zwraca liczbę zwolnionych tile'i.
        /// </summary>
        public int UnloadAllTiles()
        {
            var allKeys = new List<long>(loadedTiles.Keys);
            foreach (var tileID in allKeys)
                UnloadTile(tileID);
            forceNextUpdate = true;
            if (showDebugInfo)
                Log.Info($"[TileManager] UnloadAllTiles: cleared {allKeys.Count} tiles");
            return allKeys.Count;
        }

        /// <summary>Number of tiles requested but still waiting for data (IsLoaded=false).</summary>
        public int PendingLoadCount
        {
            get
            {
                int count = 0;
                foreach (var kvp in loadedTiles)
                    if (!kvp.Value.IsLoaded) count++;
                return count;
            }
        }


        [ContextMenu("RMP: Pin 3x3 wokol kamery (diag)")]
        private void DiagPinAroundCamera()
        {
            if (mainCamera == null) { Log.Warn("[TileManager] DiagPin: brak mainCamera"); return; }
            var (cx, cy) = TileGrid.WorldToGrid(mainCamera.transform.position.x, mainCamera.transform.position.z);
            var ids = new List<long>();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    ids.Add(TileGrid.GetTileID(cx + dx, cy + dy));
            RequestPinnedTilesForRegion(ids, "diag");
            Log.Info($"[TileManager] DiagPin wokol ({cx},{cy}): pinned={PinnedTileCount} loaded={LoadedTileCount}");
        }

        [ContextMenu("RMP: Unpin diag")]
        private void DiagUnpin()
        {
            UnpinTilesForRegion("diag");
            Log.Info($"[TileManager] DiagUnpin: pinned={PinnedTileCount}");
        }

        void OnDrawGizmos()
        {
            if (!drawTileGizmos || !Application.isPlaying)
                return;

            // 256 wireCubes × 12 edges = ~3000 lines/paint w play mode (LRU cap maxLoadedTiles).
            // Frustum clip pozwala dev'owi zachować responsywny Scene view podczas play; hard cap
            // jako backup. Current camera tile rysowany zawsze (1 gizmo, użyteczny anchor).
            const int MaxGizmosToDraw = 200;

            Camera cam = Camera.current;
            bool useFrustumClip = cam != null && cam.orthographic;
            float minX = 0f, maxX = 0f, minZ = 0f, maxZ = 0f;
            if (useFrustumClip)
            {
                float halfH = cam.orthographicSize;
                float halfW = halfH * cam.aspect;
                Vector3 p = cam.transform.position;
                minX = p.x - halfW; maxX = p.x + halfW;
                minZ = p.z - halfH; maxZ = p.z + halfH;
            }

            // Draw loaded tile bounds — clip out-of-view + cap.
            Gizmos.color = Color.green;
            int drawn = 0;
            foreach (var kvp in loadedTiles)
            {
                if (drawn >= MaxGizmosToDraw) break;
                var bounds = kvp.Value.Bounds;
                if (useFrustumClip)
                {
                    // BBox overlap test (tile bounds × camera view bounds w XZ)
                    if (bounds.MaxX < minX || bounds.MinX > maxX
                     || bounds.MaxY < minZ || bounds.MinY > maxZ) continue;
                }
                Vector3 center = new Vector3(
                    (bounds.MinX + bounds.MaxX) / 2f,
                    0,
                    (bounds.MinY + bounds.MaxY) / 2f
                );
                Vector3 size = new Vector3(
                    bounds.MaxX - bounds.MinX,
                    10,
                    bounds.MaxY - bounds.MinY
                );
                Gizmos.DrawWireCube(center, size);
                drawn++;
            }

            // Draw current camera tile — zawsze (anchor info, 1 gizmo, brak point w clip'owaniu).
            if (currentTile.gridX != -1)
            {
                var bounds = TileGrid.GetTileBounds(currentTile.gridX, currentTile.gridY);
                Vector3 center = new Vector3(
                    (bounds.MinX + bounds.MaxX) / 2f,
                    5,
                    (bounds.MinY + bounds.MaxY) / 2f
                );
                Vector3 size = new Vector3(
                    bounds.MaxX - bounds.MinX,
                    20,
                    bounds.MaxY - bounds.MinY
                );
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(center, size);
            }
        }
    }
}
