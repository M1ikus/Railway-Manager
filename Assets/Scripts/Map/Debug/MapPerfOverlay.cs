using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using formap;
using RailwayManager.Core;

namespace MapSystem
{
    /// <summary>
    /// Nakładka debug perf dla mapy — toggle klawiszem F3.
    /// Pokazuje loaded tiles / LOD / szacowany RAM / FPS / load queue / pozycję kamery.
    /// Zrzuca też metryki do Console co <see cref="logIntervalSeconds"/>.
    ///
    /// Użycie: dodaj komponent do dowolnego GameObject'u w MapScene — referencje
    /// autowire'ują się w Start() jeśli są null.
    /// </summary>
    public class MapPerfOverlay : MonoBehaviour
    {
        [Header("References (autowire jeśli null)")]
        public MapLoader mapLoader;
        public TileManager tileManager;
        public Camera mainCamera;

        [Header("Settings")]
        [Tooltip("Start z widocznym overlayem")]
        public bool showOverlay = true;

        [Tooltip("Interwał zrzutu metryk do Console (sekundy)")]
        public float logIntervalSeconds = 10f;

        [Tooltip("Pozycja overlaya (px od lewego górnego rogu ekranu)")]
        public Vector2 overlayOrigin = new Vector2(10, 10);

        private float fpsSmoothed;
        private const float FpsSmoothingFactor = 0.1f;

        // RAM estimate — incremental tracking przez OnTileLoaded/OnTileUnloaded events.
        // Wcześniej co 1s skan O(loaded tiles × layers × features) — na pełnej Polsce
        // 256 × 8 × ~100 = 200k iteracji/s tylko żeby pokazać estimate. Teraz O(1) read.
        private readonly Dictionary<long, (long bytes, int features)> _perTileEstimate = new();
        private long _totalBytes;
        private int _totalFeatures;
        private float ramEstimateMB;
        private int ramEstimateFeatureCount;
        private bool _subscribedToTileManager;

        private float logTimer;
        private GUIStyle boxStyle;

        void Start()
        {
            if (mapLoader == null) mapLoader = FindAnyObjectByType<MapLoader>();
            if (tileManager == null) tileManager = FindAnyObjectByType<TileManager>();
            if (mainCamera == null) mainCamera = Camera.main;

            if (mapLoader == null || tileManager == null)
            {
                Log.Warn("[MapPerfOverlay] Brak referencji do MapLoader lub TileManager — overlay będzie pusty.");
                return;
            }

            SubscribeToTileManager();
        }

        void OnDestroy()
        {
            UnsubscribeFromTileManager();
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current[Key.F3].wasPressedThisFrame)
                showOverlay = !showOverlay;

            float frameFps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            fpsSmoothed = Mathf.Lerp(fpsSmoothed, frameFps, FpsSmoothingFactor);

            logTimer += Time.unscaledDeltaTime;
            if (logTimer >= logIntervalSeconds)
            {
                logTimer = 0f;
                DumpMetricsToLog();
            }
        }

        void OnGUI()
        {
            if (!showOverlay) return;
            if (mapLoader == null || tileManager == null) return;

            if (boxStyle == null)
            {
                boxStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 12,
                    padding = new RectOffset(8, 8, 6, 6),
                    richText = true
                };
                boxStyle.normal.textColor = Color.white;
            }

            int loaded = tileManager.LoadedTileCount;
            int total = tileManager.TotalTileCount;
            int pending = tileManager.PendingLoadCount;
            int lod = mapLoader.CurrentLOD;
            int version = mapLoader.Version;

            Vector3 camPos = mainCamera != null ? mainCamera.transform.position : Vector3.zero;
            string camZoom = "n/a";
            string camTile = "n/a";
            if (mainCamera != null)
            {
                camZoom = mainCamera.orthographic
                    ? $"ortho={mainCamera.orthographicSize:F0}"
                    : $"h={camPos.y:F0}m";
                var (gx, gy) = TileGrid.WorldToGrid(camPos.x, camPos.z);
                camTile = $"tile=({gx},{gy})";
            }

            var sb = new StringBuilder(512);
            sb.AppendLine("<b>Map Perf</b>  <color=#aaaaaa>[F3]</color>");
            sb.AppendLine($"Format:  v{version}");
            sb.AppendLine($"Tiles:   {loaded}/{total} loaded  <color=#ffa500>{pending} pending</color>");
            sb.AppendLine($"LOD:     {lod}");
            sb.AppendLine($"Est RAM: {ramEstimateMB:F1} MB  ({ramEstimateFeatureCount:N0} feats)");
            sb.AppendLine($"FPS:     {ColorizedFps(fpsSmoothed)}");
            sb.AppendLine($"Cam:     ({camPos.x:F0},{camPos.z:F0})  {camZoom}");
            sb.Append($"         {camTile}");

            const float width = 300f;
            const float height = 160f;
            GUI.Box(new Rect(overlayOrigin.x, overlayOrigin.y, width, height), sb.ToString(), boxStyle);
        }

        private static string ColorizedFps(float fps)
        {
            string color = fps >= 55f ? "#7fff7f" : fps >= 30f ? "#ffd75f" : "#ff6666";
            return $"<color={color}>{fps:F0}</color>";
        }

        // ─────────────────────────────────────────────
        //  RAM estimate — incremental tracking
        // ─────────────────────────────────────────────

        private void SubscribeToTileManager()
        {
            if (_subscribedToTileManager || tileManager == null) return;
            tileManager.OnTileLoaded += HandleTileLoaded;
            tileManager.OnTileUnloaded += HandleTileUnloaded;
            _subscribedToTileManager = true;

            // Backfill: tile'e które już są loaded zanim subscribe (lifecycle order).
            foreach (var tile in tileManager.GetAllLoadedTiles())
                if (tile.IsLoaded)
                    HandleTileLoaded(tile.TileID, tile);
        }

        private void UnsubscribeFromTileManager()
        {
            if (!_subscribedToTileManager || tileManager == null) return;
            tileManager.OnTileLoaded -= HandleTileLoaded;
            tileManager.OnTileUnloaded -= HandleTileUnloaded;
            _subscribedToTileManager = false;
        }

        private void HandleTileLoaded(long tileID, TileData tile)
        {
            // Remove previous estimate gdy LOD reload (tile already in cache).
            if (_perTileEstimate.TryGetValue(tileID, out var prev))
            {
                _totalBytes -= prev.bytes;
                _totalFeatures -= prev.features;
            }

            long bytes = 0;
            int features = 0;
            if (tile?.Layers != null)
            {
                foreach (var kvp in tile.Layers)
                {
                    foreach (var geom in kvp.Value)
                    {
                        features++;
                        // Szacunek: Vector3 ≈ 12B, int ≈ 4B. Narzut Unity Mesh pominięty.
                        bytes += (long)geom.Vertices.Count * 12L;
                        bytes += (long)geom.Indices.Count * 4L;
                    }
                }
            }
            _perTileEstimate[tileID] = (bytes, features);
            _totalBytes += bytes;
            _totalFeatures += features;
            UpdateDisplayValues();
        }

        private void HandleTileUnloaded(long tileID)
        {
            if (!_perTileEstimate.TryGetValue(tileID, out var prev)) return;
            _totalBytes -= prev.bytes;
            _totalFeatures -= prev.features;
            _perTileEstimate.Remove(tileID);
            UpdateDisplayValues();
        }

        private void UpdateDisplayValues()
        {
            ramEstimateMB = _totalBytes / (1024f * 1024f);
            ramEstimateFeatureCount = _totalFeatures;
        }

        private void DumpMetricsToLog()
        {
            if (mapLoader == null || tileManager == null) return;

            Log.Info(
                $"[MapPerfOverlay] tiles={tileManager.LoadedTileCount}/{tileManager.TotalTileCount} " +
                $"pending={tileManager.PendingLoadCount} LOD={mapLoader.CurrentLOD} " +
                $"ramMB={ramEstimateMB:F1} feats={ramEstimateFeatureCount:N0} fps={fpsSmoothed:F0}"
            );
        }
    }
}
