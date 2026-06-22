using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    public class GroundGenerator : MonoBehaviour
    {
        [Header("Ground Settings")]
        public float groundWidth = 5000f;
        public float groundDepth = 2500f;
        public Material customGroundMaterial;
        public Material backgroundGroundMaterial;
        // 50m per tile → przy groundWidth=5000 / groundDepth=2500 = 100×50 tiles łącznie.
        // Mniejsze wartości (np. 5 — historyczny default do 2026-05-15) powodują 500 000+
        // tiles → mip mapping zlewa teksturę grass w beżowy noise, ground wygląda jak
        // ziemia/piasek mimo że LushGrass_Light material jest assigned. User feedback
        // 2026-05-15: "podłoże wygląda jak DepotGroundRealistic mimo że LushGrass_Light
        // jest w Inspector" — to był ten problem.
        public float textureTileSize = 50f;
        public Color grassColor = new Color(0.4f, 0.7f, 0.3f);

        [Header("Buildable Area — rozbudowywalna pakietami (Q1 depot-visual-direction.md)")]
        [Tooltip("Current tier (0..MAX_TIER). Każdy tier to pakiet length+width. NW corner fixed, expand E+S. Edytowalne w Inspector dla setup sceny, w runtime gracz używa ExpandToNextTier().")]
        [SerializeField] private int currentTier = 0;

        [Tooltip("Cached buildable size (X=length, Z=width). Recalc z currentTier w Awake i po Expand. NIE edytuj ręcznie w Inspector — używaj currentTier.")]
        public Vector2 buildableAreaSize = new Vector2(800f, 300f);

        [Tooltip("NW corner zajezdnii w world space (fixed origin). Góra (maxZ) = droga zewnętrzna, lewa (minX) = external tracks. Expand idzie na E (X+) i S (Z-).")]
        public Vector2 nwCornerWorld = new Vector2(-1000f, 200f);

        // Pakiety rozbudowy (Q1 sub-decisions, 2026-05-17 interpretacja A).
        // Tier 0 = start, Tier 4 = max. Każdy tier dodaje proporcjonalnie length + width.
        // Length step: (2000-800)/4 = 300m/tier. Width step: (400-300)/4 = 25m/tier.
        public const int MAX_TIER = 4;
        public static readonly float[] TIER_LENGTHS = { 800f, 1100f, 1400f, 1700f, 2000f };
        public static readonly float[] TIER_WIDTHS  = { 300f, 325f,  350f,  375f,  400f  };

        /// <summary>Event triggered po zmianie tier (expand/restore). Subscribers: minimapa, spatial indexes itp.</summary>
        public event System.Action OnBoundsChanged;

        public int CurrentTier => currentTier;
        public int MaxTier => MAX_TIER;
        public float CurrentLengthX => buildableAreaSize.x;
        public float CurrentWidthZ => buildableAreaSize.y;
        public bool CanExpand() => currentTier < MAX_TIER;
        public Vector2 PreviewNextTierSize() =>
            currentTier < MAX_TIER
                ? new Vector2(TIER_LENGTHS[currentTier + 1], TIER_WIDTHS[currentTier + 1])
                : buildableAreaSize;

        [Header("Urban Atmosphere")]
        public bool useFog = false;
        public Color fogColor = new Color(0.75f, 0.85f, 0.9f);
        public float fogDensity = 0.002f;

        [Header("Road")]
        public bool generateRoad = false;
        public float roadWidth = 14f;
        public float roadDistanceFromFence = 15f;
        public Material roadMaterial;
        public float curbHeight = 0.3f;
        public float curbWidth = 0.5f;

        [Header("Background City")]
        public bool generateBackgroundBuildings = false;
        public int cityDensity = 60;
        public GameObject[] backgroundBuildingPrefabs;

        [Header("Horizon Cityscape")]
        public bool generateSkyline = false;
        public GameObject skylineBackdropPrefab;
        public float skylineDistance = 1500f;
        public float skylineScale = 1500f;

        [Header("Grid Settings")]
        public bool showGrid = true;
        public float gridCellSize = 1f;
        public Color gridColor = new Color(0.2f, 0.5f, 0.2f, 0.3f);
        public float gridLineWidth = 0.02f;

        [Header("3D Models")]
        public GameObject terrainPrefab;
        public GameObject roadPrefab;

        private GameObject groundPlane;
        private GameObject backgroundGroundPlane;
        private Material groundMaterial;
        private GameObject gridObject;
        private GameObject roadObject;
        private GameObject backgroundBuildingsParent;
        private GameObject skylineParent;

        /// <summary>
        /// Bounds buildable area kotwiczone w NW corner (góra-lewa).
        /// Expand idzie:
        ///   E (X+, w prawo) — bo lewa (X-) = external tracks fixed
        ///   S (Z-, w dół)   — bo góra (Z+) = droga zewnętrzna fixed
        /// NW corner = nwCornerWorld (constant origin).
        /// </summary>
        public Bounds BuildableArea
        {
            get
            {
                float minX = nwCornerWorld.x;
                float maxX = nwCornerWorld.x + buildableAreaSize.x;
                float maxZ = nwCornerWorld.y;
                float minZ = nwCornerWorld.y - buildableAreaSize.y;
                Vector3 center = new Vector3((minX + maxX) / 2f, 0f, (minZ + maxZ) / 2f);
                Vector3 size = new Vector3(buildableAreaSize.x, 10f, buildableAreaSize.y);
                return new Bounds(center, size);
            }
        }

        // ── Expand API (Q1, interpretacja A — pakiety kombinowane) ────────────────

        void Awake()
        {
            // Sync buildableAreaSize z currentTier (Inspector może mieć stale wartości).
            buildableAreaSize = TierSize(currentTier);
        }

        private static Vector2 TierSize(int tier)
        {
            int t = Mathf.Clamp(tier, 0, MAX_TIER);
            return new Vector2(TIER_LENGTHS[t], TIER_WIDTHS[t]);
        }

        /// <summary>Rozszerz zajezdnię do następnego tier (cały pakiet: +length AND +width). Zwraca true gdy się udało.</summary>
        [ContextMenu("Expand To Next Tier")]
        public bool ExpandToNextTier()
        {
            if (currentTier >= MAX_TIER) return false;
            int prev = currentTier;
            currentTier++;
            buildableAreaSize = TierSize(currentTier);
            Log.Info($"[GroundGenerator] Tier: {prev} → {currentTier} ({buildableAreaSize.x:F0}×{buildableAreaSize.y:F0}m)");
            OnSizeChanged();
            return true;
        }

        /// <summary>Restore z save (DepotSavable). Clampuje do [0, MAX_TIER]. NIE triggeruje OnBoundsChanged eventu (save load to nie progresja gracza).</summary>
        public void RestoreTier(int tier)
        {
            currentTier = Mathf.Clamp(tier, 0, MAX_TIER);
            buildableAreaSize = TierSize(currentTier);
            Log.Info($"[GroundGenerator] Restored from save: Tier {currentTier} ({buildableAreaSize.x:F0}×{buildableAreaSize.y:F0}m)");
            // Regenerate ground + fence + camera bounds, ale NIE wywołuj OnBoundsChanged
            // (restore to nie jest "rozbudowa" — minimapa/UI nie muszą reagować jakby gracz coś kupił).
            GenerateAll();
            var fence = DepotServices.Get<DepotFenceSystem>();
            fence?.RegenerateFence();
        }

        /// <summary>Reset do Tier 0 (start). Diagnostic / new game restart.</summary>
        [ContextMenu("Reset to Tier 0 (start)")]
        public void ResetToStartTier()
        {
            currentTier = 0;
            buildableAreaSize = TierSize(currentTier);
            Log.Info($"[GroundGenerator] Reset to Tier 0 ({buildableAreaSize.x:F0}×{buildableAreaSize.y:F0}m)");
            OnSizeChanged();
        }

        private void OnSizeChanged()
        {
            // Regenerate ground (re-tile material, re-position grid, camera bounds via GenerateAll).
            GenerateAll();
            // Regenerate fence (czyta bounds z GroundGenerator.BuildableArea).
            var fence = DepotServices.Get<DepotFenceSystem>();
            fence?.RegenerateFence();
            // Notify subscribers (minimapa, spatial indexes, save dirty flag itp).
            OnBoundsChanged?.Invoke();
        }

        void Start() { GenerateAll(); }

        [ContextMenu("Generate All")]
        public void GenerateAll()
        {
            ClearGround();
            GenerateGround();
            if (showGrid) GenerateGrid();
            if (generateRoad) GenerateRoad();
            if (generateBackgroundBuildings) GenerateBackgroundBuildings();
            if (generateSkyline) GenerateSkyline();
            UpdateAtmosphere();

            var camera = DepotServices.Get<DepotOrbitCamera>();
            if (camera != null) camera.UpdateBounds(BuildableArea);
        }

        [ContextMenu("Clear Ground")]
        public void ClearGround()
        {
            if (groundPlane != null) DestroyImmediate(groundPlane);
            if (backgroundGroundPlane != null) DestroyImmediate(backgroundGroundPlane);
            if (gridObject != null) DestroyImmediate(gridObject);
            if (roadObject != null) DestroyImmediate(roadObject);
            if (backgroundBuildingsParent != null) DestroyImmediate(backgroundBuildingsParent);
            if (skylineParent != null) DestroyImmediate(skylineParent);

            // Dodatkowe czyszczenie po nazwie dla pewności
            string[] targets = { "Background_City_Blocks", "Horizon_Cityscape", "Road_System", "Horizon_Skyline", "Background_City", "Grid_Lines", "Ground_Depot_Grass", "Ground_City_Asphalt" };
            foreach (var t in targets)
            {
                var obj = GameObject.Find(t);
                if (obj != null && obj.transform.parent == transform) DestroyImmediate(obj);
            }
        }

        public void GenerateGround()
        {
            if (groundPlane != null) DestroyImmediate(groundPlane);
            if (backgroundGroundPlane != null) DestroyImmediate(backgroundGroundPlane);

            // Główne podłoże (Trawa)
            groundPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundPlane.name = "Ground_Depot_Grass";
            groundPlane.transform.SetParent(transform);
            groundPlane.transform.localPosition = Vector3.zero;
            groundPlane.transform.localScale = new Vector3(groundWidth / 10f, 1f, groundDepth / 10f);
            
            // 2026-05-15: User raport "materiał zmienia się przy recompile skryptów/reload
            // sceny". Stary kod robił `new Material(customGroundMaterial)` clone + override
            // mainTextureScale w runtime. Każdy recompile (Unity Editor hot reload) niszczy
            // runtime-created Material instances → sharedMaterial wskazuje na limbo/default
            // (beżowy noise mosaic mimo że LushGrass_Light wygląda zielono w Inspector).
            //
            // Fix: bezpośrednie sharedMaterial = customGroundMaterial (asset, nie clone).
            // mainTextureScale embedded w assecie (LushGrass_Light.mat: x:48 y:16 dla
            // groundWidth=2400 / groundDepth=800 / tile=50). Recompile bez wpływu bo asset
            // jest static.
            if (customGroundMaterial != null)
            {
                groundMaterial = customGroundMaterial;
                groundPlane.GetComponent<MeshRenderer>().sharedMaterial = customGroundMaterial;
            }
            else
            {
                // Fallback: gdy gracz świadomie usuwa asset → jednolity grassColor.
                groundMaterial = MaterialFactory.CreateLit();
                MaterialFactory.SetBaseColor(groundMaterial, grassColor);
                groundMaterial.mainTextureScale = new Vector2(groundWidth / textureTileSize, groundDepth / textureTileSize);
                groundPlane.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
            }

            // Dodatkowe podłoże miejskie (Beton) - jeśli jest przypisany materiał
            if (backgroundGroundMaterial != null) {
                backgroundGroundPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                backgroundGroundPlane.name = "Ground_City_Asphalt";
                backgroundGroundPlane.transform.SetParent(transform);
                // Nieco wyżej by nie mrugało
                backgroundGroundPlane.transform.localPosition = new Vector3(0, 0.005f, 0); 
                backgroundGroundPlane.transform.localScale = new Vector3(groundWidth / 10f, 1f, groundDepth / 10f);
                backgroundGroundPlane.GetComponent<MeshRenderer>().sharedMaterial = backgroundGroundMaterial;
                backgroundGroundPlane.GetComponent<MeshRenderer>().sharedMaterial.mainTextureScale = new Vector2(groundWidth / 5f, groundDepth / 5f);
            }
        }

        public void GenerateRoad()
        {
            if (roadObject != null) DestroyImmediate(roadObject);
            Bounds ba = BuildableArea;
            roadObject = new GameObject("Road_System");
            roadObject.transform.SetParent(transform);
            float roadCenterZ = ba.max.z + roadDistanceFromFence + roadWidth / 2f;
            
            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Road_Surface";
            surface.transform.SetParent(roadObject.transform);
            surface.transform.position = new Vector3(ba.center.x, 0.02f, roadCenterZ);
            surface.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            surface.transform.localScale = new Vector3(groundWidth, roadWidth, 1f);
            if (roadMaterial != null) {
                surface.GetComponent<MeshRenderer>().sharedMaterial = roadMaterial;
                surface.GetComponent<MeshRenderer>().sharedMaterial.mainTextureScale = new Vector2(groundWidth / 10f, 1f);
            }
            DestroyImmediate(surface.GetComponent<MeshCollider>());

            CreateCurb(ba.center.x, roadCenterZ - roadWidth / 2f - curbWidth / 2f, groundWidth, "Curb_Inner");
            CreateCurb(ba.center.x, roadCenterZ + roadWidth / 2f + curbWidth / 2f, groundWidth, "Curb_Outer");
        }

        private void CreateCurb(float x, float z, float length, string name)
        {
            GameObject curb = GameObject.CreatePrimitive(PrimitiveType.Cube);
            curb.name = name;
            curb.transform.SetParent(roadObject.transform);
            curb.transform.position = new Vector3(x, curbHeight / 2f, z);
            curb.transform.localScale = new Vector3(length, curbHeight, curbWidth);
            Material m = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(m, new Color(0.7f, 0.7f, 0.7f));
            curb.GetComponent<MeshRenderer>().sharedMaterial = m;
            DestroyImmediate(curb.GetComponent<BoxCollider>());
        }

        public void GenerateBackgroundBuildings()
        {
            if (backgroundBuildingsParent != null) DestroyImmediate(backgroundBuildingsParent);
            backgroundBuildingsParent = new GameObject("Background_City_Blocks");
            backgroundBuildingsParent.transform.SetParent(transform);

            Bounds ba = BuildableArea;
            float northStart = ba.max.z + roadDistanceFromFence + roadWidth + 20f;
            float northEnd = groundDepth / 2f - 100f;

            GenerateCityBlock(new Vector3(0, 0, (northStart + northEnd) / 2f), 
                             new Vector3(groundWidth - 200f, 0, northEnd - northStart), 
                             cityDensity, "Block_North");

            float sideW = (groundWidth - buildableAreaSize.x) / 2f - 100f;
            if (sideW > 50f) {
                GenerateCityBlock(new Vector3(-groundWidth / 2f + sideW / 2f + 50f, 0, ba.center.z), 
                                 new Vector3(sideW, 0, groundDepth - 200f), cityDensity / 2, "Block_West");
                GenerateCityBlock(new Vector3(groundWidth / 2f - sideW / 2f - 50f, 0, ba.center.z), 
                                 new Vector3(sideW, 0, groundDepth - 200f), cityDensity / 2, "Block_East");
            }
        }

        private void GenerateCityBlock(Vector3 center, Vector3 size, int count, string prefix)
        {
            GameObject block = new GameObject(prefix);
            block.transform.SetParent(backgroundBuildingsParent.transform);
            block.transform.position = center;
            // User decision 2026-05-15: tło zajezdni (background buildings za płotem)
            // ma być IDENTYCZNE niezależnie od GameState.Seed — jeden statyczny układ
            // zawsze. Nie variant per seed.
            //
            // Fixed seed per block name (explicit switch zamiast string.GetHashCode bo
            // ten jest randomized per-run w .NET Core / nie-stable w Mono). Block_North/
            // West/East dostają różne seedy żeby wyglądały inaczej, ale ten sam seed
            // = identyczny układ między run'ami.
            //
            // Wcześniej UnityEngine.Random.Range bez seed dawał inne tło co run (Unity
            // globalny RNG state przesuwa się), user raportował jako bug.
            int blockSeed = prefix switch
            {
                "Block_North" => 1001,
                "Block_West"  => 1002,
                "Block_East"  => 1003,
                _             => 1000,
            };
            var rng = new System.Random(blockSeed);
            for (int i = 0; i < count; i++) {
                float x = (float)(rng.NextDouble() * size.x - size.x / 2f);
                float z = (float)(rng.NextDouble() * size.z - size.z / 2f);
                if (backgroundBuildingPrefabs != null && backgroundBuildingPrefabs.Length > 0) {
                    GameObject prefab = backgroundBuildingPrefabs[rng.Next(0, backgroundBuildingPrefabs.Length)];
                    var b = Instantiate(prefab, center + new Vector3(x, 0, z), Quaternion.Euler(0, rng.Next(0, 4) * 90f, 0), block.transform);
                    float s = 60f + (float)(rng.NextDouble() * 40f);
                    b.transform.localScale = new Vector3(s, s * (1f + (float)(rng.NextDouble() * 2f)), s);
                }
            }
        }

        public void GenerateSkyline()
        {
            if (skylineBackdropPrefab == null) return;
            if (skylineParent != null) DestroyImmediate(skylineParent);
            skylineParent = new GameObject("Horizon_Cityscape");
            skylineParent.transform.SetParent(transform);
            int n = 16;
            for (int i = 0; i < n; i++) {
                float angle = i * (360f / n) * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(Mathf.Cos(angle) * skylineDistance, -50f, Mathf.Sin(angle) * skylineDistance);
                var s = Instantiate(skylineBackdropPrefab, pos, Quaternion.identity, skylineParent.transform);
                s.transform.localScale = Vector3.one * skylineScale;
                s.transform.LookAt(new Vector3(0, -50f, 0));
                s.transform.Rotate(0, 180f, 0);
            }
        }

        public void UpdateAtmosphere()
        {
            RenderSettings.fog = useFog;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = fogDensity;
        }

        public void GenerateGrid()
        {
            if (gridObject != null) DestroyImmediate(gridObject);
            gridObject = new GameObject("Grid_Lines");
            gridObject.transform.SetParent(transform);
            gridObject.transform.localPosition = new Vector3(0, 0.01f, 0);
        }

        public void SetGridVisible(bool visible) { showGrid = visible; if (gridObject != null) gridObject.SetActive(visible); }
        public void ToggleGrid() { SetGridVisible(!showGrid); }
        public void RefreshGround() { GenerateAll(); }
    }
}
