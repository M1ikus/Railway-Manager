using System.Collections.Generic;
using UnityEngine;
using formap;
using MapSystem;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Renderuje szary overlay na tile'ach CAŁKOWICIE poza granicą Polski (M-PL-4).
    ///
    /// MVP approach: per-tile heurystyka — sprawdza 4 corners + center tile'a, jeśli wszystkie
    /// poza PL polygon (via <see cref="CountryOverlayService"/>) → spawn szary quad mesh jako child.
    /// Tile'e częściowo zachodzące na granicę NIE dostają overlay'a — granica rysuje się
    /// naturalnie (10km tile resolution = granica "pixelowa" w skali Polski, akceptowalna).
    ///
    /// Szary quad jest na Y = -10 (pod najniższą warstwą mapy: water -5), więc widać go
    /// tylko gdy nad nim nie ma innych warstw (czyli faktycznie poza Polską — w Polish PBF
    /// poza PL jest prawie zero features).
    ///
    /// Wpinka: dodaj komponent do dowolnego GameObject'a w MapScene — referencje autowire'ują się.
    /// </summary>
    public class CountryBorderOverlayRenderer : MonoBehaviour
    {
        [Header("References (autowire jeśli null)")]
        public TileManager tileManager;

        [Header("Overlay Settings")]
        [Tooltip("Kolor overlay'a dla obszaru poza Polską (RGB). Alpha ignorowane — mesh jest opaque.")]
        public Color overlayColor = new Color(0.45f, 0.45f, 0.48f, 1f);

        [Tooltip("Y position overlay'a — poniżej wszystkich warstw mapy (water = -5). Domyślnie -10.")]
        public float overlayY = -10f;

        [Tooltip("Włącz/wyłącz overlay runtime. Wygodne do porównań A/B.")]
        public bool renderingEnabled = true;

        private Material overlayMaterial;
        private readonly Dictionary<long, GameObject> tileOverlays = new();
        private Mesh sharedQuadMesh;
        private bool isSubscribed;

        void Start()
        {
            if (tileManager == null) tileManager = FindAnyObjectByType<TileManager>();

            if (tileManager == null)
            {
                Log.Warn("[CountryBorderOverlayRenderer] TileManager not found — overlay disabled.");
                return;
            }

            overlayMaterial = CreateOverlayMaterial();

            tileManager.OnTileLoaded += HandleTileLoaded;
            tileManager.OnTileUnloaded += HandleTileUnloaded;
            CountryOverlayService.OnInitialized += HandleServiceInitialized;
            isSubscribed = true;

            // Jeśli service już ready (TimetableInitializer szybszy niż nasz Start) — rescan
            if (CountryOverlayService.IsInitialized)
                HandleServiceInitialized();
        }

        void OnDestroy()
        {
            if (isSubscribed && tileManager != null)
            {
                tileManager.OnTileLoaded -= HandleTileLoaded;
                tileManager.OnTileUnloaded -= HandleTileUnloaded;
                CountryOverlayService.OnInitialized -= HandleServiceInitialized;
            }

            ClearAllOverlays();

            if (overlayMaterial != null) Destroy(overlayMaterial);
            if (sharedQuadMesh != null) Destroy(sharedQuadMesh);
        }

        private Material CreateOverlayMaterial()
        {
            // Built-in pipeline (projekt nie używa URP — patrz manifest.json + MapRenderer)
            var mat = MaterialFactory.CreateUnlit();
            mat.name = $"CountryOverlay_{mat.shader.name}";
            MaterialFactory.SetBaseColor(mat, overlayColor);
            return mat;
        }

        private void HandleServiceInitialized()
        {
            if (tileManager == null) return;

            // Rescan wszystkich aktualnie loaded tile'i — generate overlay dla tych outside PL
            int generated = 0;
            foreach (var tile in tileManager.GetAllLoadedTiles())
            {
                if (tile == null || !tile.IsLoaded) continue;
                if (tileOverlays.ContainsKey(tile.TileID)) continue;
                if (CountryOverlayService.IsTileFullyOutsidePoland(tile.Bounds))
                {
                    tileOverlays[tile.TileID] = CreateTileOverlayGO(tile.TileID, tile.Bounds);
                    generated++;
                }
            }
            Log.Info($"[CountryBorderOverlayRenderer] Service initialized — rescan generated {generated} overlays.");
        }

        private void HandleTileLoaded(long tileID, TileData tileData)
        {
            if (!renderingEnabled) return;
            if (!CountryOverlayService.IsInitialized) return; // rescan kiedy service się zainicjalizuje
            if (tileOverlays.ContainsKey(tileID)) return;
            if (tileData == null) return;

            if (CountryOverlayService.IsTileFullyOutsidePoland(tileData.Bounds))
                tileOverlays[tileID] = CreateTileOverlayGO(tileID, tileData.Bounds);
        }

        private void HandleTileUnloaded(long tileID)
        {
            if (tileOverlays.TryGetValue(tileID, out var go))
            {
                if (go != null) Destroy(go);
                tileOverlays.Remove(tileID);
            }
        }

        private GameObject CreateTileOverlayGO(long tileID, BBox bounds)
        {
            var go = new GameObject($"CountryOverlay_Tile_{tileID}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.layer = gameObject.layer; // inherit MapLayer 31 — widoczny tylko przez Map Camera

            float cx = (bounds.MinX + bounds.MaxX) * 0.5f;
            float cz = (bounds.MinY + bounds.MaxY) * 0.5f;
            float sizeX = bounds.MaxX - bounds.MinX;
            float sizeZ = bounds.MaxY - bounds.MinY;

            go.transform.position = new Vector3(cx, overlayY, cz);
            go.transform.localScale = new Vector3(sizeX, 1f, sizeZ);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = GetOrCreateQuadMesh();
            mr.sharedMaterial = overlayMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            return go;
        }

        private Mesh GetOrCreateQuadMesh()
        {
            if (sharedQuadMesh != null) return sharedQuadMesh;

            // Unit quad 1×1 w płaszczyźnie XZ (Y=0), centered — skalowany do rozmiaru tile'a
            sharedQuadMesh = new Mesh
            {
                name = "CountryOverlay_UnitQuad",
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3( 0.5f, 0f, -0.5f),
                    new Vector3( 0.5f, 0f,  0.5f),
                    new Vector3(-0.5f, 0f,  0.5f)
                },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
                normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) }
            };
            sharedQuadMesh.RecalculateBounds();
            return sharedQuadMesh;
        }

        private void ClearAllOverlays()
        {
            foreach (var go in tileOverlays.Values)
                if (go != null) Destroy(go);
            tileOverlays.Clear();
        }

        /// <summary>Liczba aktywnych overlayów — dla debug / M-PL-3 overlay metryk.</summary>
        public int ActiveOverlayCount => tileOverlays.Count;
    }
}
