using System.Collections.Generic;
using UnityEngine;
using formap;
using MapSystem;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Renderuje kłódki DLC na TILE'ach outside PL (M-PL-5).
    ///
    /// 1 kłódka per tile który jest fully outside Polski (tile'e szarym overlay'em z CountryBorderOverlayRenderer).
    /// Placeholder = czerwony kubik w centroidzie tile'a. Gracz widzi że obszar jest "zablokowany"
    /// (niedostępny bez DLC), klik → przyszły tooltip "DLC [kraj]".
    ///
    /// Dynamic spawning: subscribe do TileManager.OnTileLoaded/OnTileUnloaded — kłódki pojawiają się/znikają
    /// razem z tile'ami przy panowaniu kamery.
    ///
    /// Model DLC: kupując DLC dla regionu (np. "Niemcy") odblokowuje się cały zagraniczny region —
    /// kłódki znikają w tym obszarze + init-state-de.bin ładuje się jako nowe terytorium grywalne.
    /// </summary>
    public class DlcLockRenderer : MonoBehaviour
    {
        [Header("References (autowire jeśli null)")]
        public TileManager tileManager;

        [Header("Visual (placeholder do M-Models)")]
        [Tooltip("Rozmiar placeholder kłódki w metrach world. Default 6000m (60% tile 10km) — widoczne " +
                 "przy zoom-out na całą Polskę.")]
        public float iconSize = 6000f;

        [Tooltip("Y pozycja kłódki. Default 1.1 — tuż nad szarym CountryOutsideMesh (Y=1).")]
        public float iconY = 1.1f;

        [Tooltip("Kolor placeholdera.")]
        public Color lockColor = new Color(0.85f, 0.25f, 0.25f, 1f);

        [Tooltip("Włącz/wyłącz runtime.")]
        public bool renderingEnabled = true;

        private Material sharedLockMaterial;
        private Mesh sharedCubeMesh;
        private readonly Dictionary<long, GameObject> tileLocks = new();
        private bool isSubscribed;

        void Start()
        {
            if (tileManager == null) tileManager = FindAnyObjectByType<TileManager>();
            if (tileManager == null) { Log.Warn("[DlcLockRenderer] TileManager not found — disabled."); return; }

            sharedLockMaterial = CreateLockMaterial();

            tileManager.OnTileLoaded += HandleTileLoaded;
            tileManager.OnTileUnloaded += HandleTileUnloaded;
            CountryOverlayService.OnInitialized += HandleServiceInitialized;
            isSubscribed = true;

            if (CountryOverlayService.IsInitialized) HandleServiceInitialized();
        }

        void OnDestroy()
        {
            if (isSubscribed && tileManager != null)
            {
                tileManager.OnTileLoaded -= HandleTileLoaded;
                tileManager.OnTileUnloaded -= HandleTileUnloaded;
                CountryOverlayService.OnInitialized -= HandleServiceInitialized;
            }
            ClearAllLocks();
            if (sharedLockMaterial != null) Destroy(sharedLockMaterial);
            // sharedCubeMesh = reference do Unity built-in Primitive Cube mesh (asset) —
            // NIE destroyujemy (asset destroy zabroniony w edytorze + shared między wszystkimi primitive cube).
        }

        private Material CreateLockMaterial()
        {
            var mat = MaterialFactory.CreateUnlit();
            mat.name = "DlcLock_SharedMaterial";
            MaterialFactory.SetBaseColor(mat, lockColor);
            return mat;
        }

        private void HandleServiceInitialized()
        {
            if (tileManager == null) return;
            int generated = 0;
            foreach (var tile in tileManager.GetAllLoadedTiles())
            {
                if (tile == null || !tile.IsLoaded) continue;
                if (tileLocks.ContainsKey(tile.TileID)) continue;
                if (CountryOverlayService.IsTileFullyOutsidePoland(tile.Bounds))
                {
                    tileLocks[tile.TileID] = CreateLockAtTile(tile.TileID, tile.Bounds);
                    generated++;
                }
            }
            Log.Info($"[DlcLockRenderer] Service initialized — spawned {generated} DLC locks on outside-PL tiles.");
        }

        private void HandleTileLoaded(long tileID, TileData tileData)
        {
            if (!renderingEnabled) return;
            if (!CountryOverlayService.IsInitialized) return;
            if (tileLocks.ContainsKey(tileID)) return;
            if (tileData == null) return;

            if (CountryOverlayService.IsTileFullyOutsidePoland(tileData.Bounds))
            {
                tileLocks[tileID] = CreateLockAtTile(tileID, tileData.Bounds);
            }
        }

        void LateUpdate()
        {
            // Defensive periodic rescan — łapie tile'e przegapione przez OnTileLoaded event
            if (!renderingEnabled || !CountryOverlayService.IsInitialized || tileManager == null) return;
            if (Time.unscaledTime < _nextRescanTime) return;
            _nextRescanTime = Time.unscaledTime + 2f;

            foreach (var tile in tileManager.GetAllLoadedTiles())
            {
                if (tile == null || !tile.IsLoaded) continue;
                if (tileLocks.ContainsKey(tile.TileID)) continue;
                if (CountryOverlayService.IsTileFullyOutsidePoland(tile.Bounds))
                    tileLocks[tile.TileID] = CreateLockAtTile(tile.TileID, tile.Bounds);
            }
        }

        private float _nextRescanTime;

        private void HandleTileUnloaded(long tileID)
        {
            if (tileLocks.TryGetValue(tileID, out var go))
            {
                if (go != null) Destroy(go);
                tileLocks.Remove(tileID);
            }
        }

        private GameObject CreateLockAtTile(long tileID, BBox bounds)
        {
            if (sharedLockMaterial == null) return null;

            var go = new GameObject($"DlcLock_Tile_{tileID}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.layer = gameObject.layer; // inherit MapLayer 31 — widoczny tylko przez Map Camera
            float cx = (bounds.MinX + bounds.MaxX) * 0.5f;
            float cz = (bounds.MinY + bounds.MaxY) * 0.5f;
            go.transform.position = new Vector3(cx, iconY, cz);
            // Flat quad XZ, wysokość Y=1m — nie wystaje pionowo, widoczny top-down jako czerwony kwadrat.
            go.transform.localScale = new Vector3(iconSize, 1f, iconSize);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = GetOrCreateCubeMesh();
            mr.sharedMaterial = sharedLockMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private Mesh GetOrCreateCubeMesh()
        {
            if (sharedCubeMesh != null) return sharedCubeMesh;
            // Prosty unit cube (1x1x1) — pozycja + scale z GO transform
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sharedCubeMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            // Ukryj temp GO — użyliśmy tylko dla jego shared mesh reference
            temp.SetActive(false);
            DestroyImmediate(temp);
            return sharedCubeMesh;
        }

        private void ClearAllLocks()
        {
            foreach (var go in tileLocks.Values)
                if (go != null) Destroy(go);
            tileLocks.Clear();
        }

        public int ActiveLockCount => tileLocks.Count;

        [ContextMenu("DEBUG: Rescan tiles")]
        public void DebugRescan()
        {
            ClearAllLocks();
            HandleServiceInitialized();
        }
    }
}
