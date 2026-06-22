using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Renderuje "zielony ground" Polski jako sumę polygonów województw (M-PL-4 enhancement).
    ///
    /// Po co: stary Plane "Ground" (50000×50000 = 500 km) nie pokrywa całej Polski (~700×600 km),
    /// więc rejony skrajne (np. Bieszczady, Hel) wyglądały jak "dziury" w ziemi. Plus na Bałtyku
    /// było zielone pole zamiast morza.
    ///
    /// Rozwiązanie: 16 mesh'ów (po jednym na województwo) z dokładną geometrią z OSM AdminBoundaries
    /// (admin_level=4, już triangulowane przez LibTess w formap). Zielony renderuje się dokładnie
    /// tam gdzie powinien — pod lądem PL. Bałtyk + zagranica zostają bez zielonego.
    ///
    /// Synergiczne z <see cref="CountryBorderOverlayRenderer"/> (szary outside PL):
    /// - Pod PL: zielony ground (Y=-1, ten renderer)
    /// - Poza PL (tile fully outside): szary overlay (Y=-10, CountryBorderOverlayRenderer)
    /// - Bałtyk (tile partially outside / fully inside ale nie pokryty województwem): czarne tło kamery
    ///
    /// Wpinka: dodaj komponent do dowolnego GameObject'a w MapScene — referencje autowire'ują się
    /// gdy <see cref="CountryOverlayService"/> się zainicjalizuje (event OnInitialized).
    /// </summary>
    public class VoivodeshipGroundRenderer : MonoBehaviour
    {
        [Header("Ground Settings")]
        [Tooltip("Kolor zielonego ground'u (RGB). Domyślnie naturalna zieleń podobna do starego Plane'a.")]
        public Color groundColor = new Color(0.45f, 0.55f, 0.30f, 1f);

        [Tooltip("Y position ground'u. Default -10 (PONIŻEJ water=-5 i forest=-3 — ground działa jako 'dno' pod " +
                 "wszystkimi warstwami mapy).\n" +
                 "WAŻNE: musi być NIŻEJ niż water (-5) żeby Bałtyk + Zalew Wiślany + jeziora były widoczne " +
                 "jako niebieskie. Jeśli ustawisz Y=0 lub wyżej, ground zasłoni wszystkie warstwy mapy.\n" +
                 "DEBUG TIP: jeśli ground niewidoczny w pustych obszarach, sprawdź czy Tile_*/Layer_* mają " +
                 "transparentne tile — może camera background (czarne) jest nad ground.")]
        public float groundY = -10f;

        [Tooltip("Czy automatycznie wyłączyć stary Unity Plane GameObject o nazwie 'Ground' w scenie. " +
                 "Bez tego oba renderują się i z dolnej kamery widać dwie warstwy zieleni.")]
        public bool autoDisableLegacyGroundPlane = true;

        [Tooltip("Strategia wyboru polygonów:\n" +
                 "• PreferCountryPolygon (zalecane) — jeśli OSM ma 'Polska' (admin_level=2), użyj go (1 mesh, " +
                 "brak luk). Inaczej fallback do województw (admin_level=4).\n" +
                 "• VoivodeshipsOnly — zawsze 16 mesh'ów województw (mogą być luki gdy PBF niekompletny).\n" +
                 "• CountryPolygonOnly — tylko admin_level=2, ignoruj województwa (najczystszy outline ale " +
                 "wymaga że PBF zawiera 'Polska').")]
        public GroundStrategy strategy = GroundStrategy.PreferCountryPolygon;

        public enum GroundStrategy
        {
            PreferCountryPolygon,
            VoivodeshipsOnly,
            CountryPolygonOnly
        }

        [Tooltip("Włącz/wyłącz ground runtime. Wygodne do A/B porównań z legacy Ground plane'em.")]
        public bool renderingEnabled = true;

        private Material groundMaterial;
        private readonly List<GameObject> groundMeshes = new();
        private bool isSubscribed;

        void Start()
        {
            groundMaterial = CreateGroundMaterial();

            CountryOverlayService.OnInitialized += HandleServiceInitialized;
            isSubscribed = true;

            // Jeśli service już ready (TimetableInitializer szybszy niż nasz Start) — od razu build
            if (CountryOverlayService.IsInitialized)
                HandleServiceInitialized();
        }

        void OnDestroy()
        {
            if (isSubscribed)
                CountryOverlayService.OnInitialized -= HandleServiceInitialized;

            ClearAllMeshes();

            if (groundMaterial != null) Destroy(groundMaterial);
        }

        private Material CreateGroundMaterial()
        {
            // Projekt jest na Built-in Render Pipeline (mimo CLAUDE.md "URP" — manifest.json nie ma
            // URP packages, MapRenderer używa Unlit/Color). Używamy Built-in shaderów.
            // Priorytet: Unlit/Color (działa dla MapRenderer, brak interakcji z lighting).
            // Cull Off uzyskujemy przez reverse-duplicate triangles w mesh (patrz BuildMesh).
            var mat = MaterialFactory.CreateUnlit();
            mat.name = "VoivodeshipGround";
            MaterialFactory.SetBaseColor(mat, groundColor);

            MaterialFactory.SetDoubleSided(mat); // _Cull = 0 (Off)
            mat.doubleSidedGI = true;

            return mat;
        }

        private void HandleServiceInitialized()
        {
            if (!renderingEnabled) return;

            ClearAllMeshes();

            var allRegions = CountryOverlayService.GetPolandRegions();
            if (allRegions == null || allRegions.Count == 0)
            {
                Log.Warn("[VoivodeshipGroundRenderer] No regions from CountryOverlayService — ground disabled.");
                return;
            }

            // Wybór regionów wg strategii
            var regionsToRender = SelectRegions(allRegions);

            int built = 0, skipped = 0, totalTriangles = 0, totalVerts = 0;
            foreach (var region in regionsToRender)
            {
                if (region.vertices == null || region.vertices.Count < 3 || region.indices == null || region.indices.Count < 3)
                {
                    Log.Warn($"[VoivodeshipGroundRenderer] Skip '{region.name}' — empty geometry "
                             + $"(verts={region.vertices?.Count ?? 0}, indices={region.indices?.Count ?? 0})");
                    skipped++;
                    continue;
                }

                var go = CreateRegionGroundGO(region);
                if (go != null)
                {
                    groundMeshes.Add(go);
                    built++;
                    totalTriangles += region.indices.Count / 3;
                    totalVerts += region.vertices.Count;
                }
            }

            if (autoDisableLegacyGroundPlane)
                DisableLegacyGroundPlane();

            Log.Info($"[VoivodeshipGroundRenderer] DONE: {built} mesh'ów ({totalVerts} verts, "
                     + $"{totalTriangles} triangles, {skipped} skipped) — strategia={strategy}");
        }

        private List<AdminRegion> SelectRegions(IReadOnlyList<AdminRegion> all)
        {
            var country = new List<AdminRegion>();
            var voivs = new List<AdminRegion>();
            foreach (var r in all)
            {
                if (r.adminLevel == 2) country.Add(r);
                else if (r.adminLevel == 4) voivs.Add(r);
            }

            switch (strategy)
            {
                case GroundStrategy.CountryPolygonOnly:
                    if (country.Count == 0)
                        Log.Warn("[VoivodeshipGroundRenderer] CountryPolygonOnly ale brak admin_level=2 w danych — pusto.");
                    return country;

                case GroundStrategy.VoivodeshipsOnly:
                    return voivs;

                case GroundStrategy.PreferCountryPolygon:
                default:
                    return country.Count > 0 ? country : voivs;
            }
        }

        private GameObject CreateRegionGroundGO(AdminRegion region)
        {
            if (groundMaterial == null) return null;

            var go = new GameObject($"Ground_{region.name}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.layer = gameObject.layer; // inherit MapLayer 31 — widoczny tylko przez Map Camera
            go.transform.position = new Vector3(0f, groundY, 0f);

            // Konwersja AdminRegion vertices (Vector2 X,Y w world coords) na Unity Vector3 (X, groundY, Z=Y2D)
            // Triangulacja z LibTess już jest w region.indices (każde 3 to trójkąt).
            var verts = new Vector3[region.vertices.Count];
            for (int i = 0; i < region.vertices.Count; i++)
            {
                var v = region.vertices[i];
                verts[i] = new Vector3(v.x, 0f, v.y); // Y=0 lokalnie, Y=groundY przez transform
            }

            // Double-sided geometry: duplikuj każdy trójkąt w odwrotnej kolejności (a,b,c → +a,c,b).
            // To gwarantuje widoczność niezależnie od:
            // - LibTess winding direction (może być CCW lub CW, nieznane przy build)
            // - Shader cull mode (Built-in Unlit/Color domyślnie cull back)
            // - Camera angle (nawet patrząc z dołu mesh jest widoczny)
            // Koszt: 2× triangle count. Dla Polski ~2-10k triangles to nieistotne.
            int origTriCount = region.indices.Count;
            var triangles = new int[origTriCount * 2];
            for (int i = 0; i < origTriCount; i += 3)
            {
                // Front side (oryginalny winding)
                triangles[i] = region.indices[i];
                triangles[i + 1] = region.indices[i + 1];
                triangles[i + 2] = region.indices[i + 2];
                // Back side (reverse winding)
                triangles[origTriCount + i] = region.indices[i];
                triangles[origTriCount + i + 1] = region.indices[i + 2];
                triangles[origTriCount + i + 2] = region.indices[i + 1];
            }

            var mesh = new Mesh
            {
                name = $"GroundMesh_{region.name}",
                indexFormat = region.vertices.Count > 65535 || triangles.Length > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.vertices = verts;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // Defensive: explicit huge bounds — eliminuje potencjalne frustum culling
            // dla mesh'ów z dziwną topologią (long-thin polygons gdzie auto-bounds jest źle).
            // 2000km × 1000m × 2000km cube around mesh center = na pewno w view kamery.
            var autoB = mesh.bounds;
            mesh.bounds = new Bounds(autoB.center, new Vector3(2_000_000f, 1000f, 2_000_000f));

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;
            mr.sharedMaterial = groundMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = -1000; // Renderuj PRZED innymi mesh'ami mapy (Y order czasem nie wystarcza)

            return go;
        }

        private void ClearAllMeshes()
        {
            foreach (var go in groundMeshes)
            {
                if (go == null) continue;
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
                Destroy(go);
            }
            groundMeshes.Clear();
        }

        /// <summary>
        /// Szuka GameObject'a o nazwie "Ground" w scenie i wyłącza go (SetActive(false)).
        /// Stary Plane jest serializowany w MapScene.unity (Transform pos=0,-1,0, scale=50000×1×50000 = 50km).
        /// Bez wyłączenia: stary plane przykrywa nasz mesh (Z-fighting / occlusion) w centralnych 50km Polski.
        /// </summary>
        private void DisableLegacyGroundPlane()
        {
            var scene = gameObject.scene;
            if (!scene.IsValid())
            {
                Log.Warn("[VoivodeshipGroundRenderer] DisableLegacyGroundPlane: scene not valid — skipping.");
                return;
            }

            // Search 1: bezpośrednio root GO o nazwie "Ground" (case-insensitive)
            int rootCount = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                rootCount++;
                if (string.Equals(root.name, "Ground", System.StringComparison.OrdinalIgnoreCase))
                {
                    root.SetActive(false);
                    Log.Info($"[VoivodeshipGroundRenderer] Legacy ROOT 'Ground' disabled "
                             + $"(was at {root.transform.position}, scale {root.transform.localScale}).");
                    return;
                }
            }

            // Search 2: deep search po wszystkich GO (transform Find rekurencyjny)
            // Niektóre projekty mogą mieć Ground jako child np. pod "Map" GO.
            var allTransforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            int matchCount = 0;
            foreach (var t in allTransforms)
            {
                if (t == null || t.gameObject.scene != scene) continue;
                if (string.Equals(t.name, "Ground", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (t.gameObject.activeSelf)
                    {
                        t.gameObject.SetActive(false);
                        string parentName = t.parent != null ? t.parent.name : "<root>";
                        Log.Info($"[VoivodeshipGroundRenderer] Legacy 'Ground' (deep, parent={parentName}) disabled "
                                 + $"(was at {t.position}, scale {t.localScale}).");
                        matchCount++;
                    }
                }
            }

            if (matchCount == 0)
                Log.Warn($"[VoivodeshipGroundRenderer] No 'Ground' GameObject found in scene "
                         + $"(searched {rootCount} roots + {allTransforms.Length} all transforms). "
                         + $"Stary plane może być w prefabach lub pod innym name'em — sprawdź Hierarchy MapScene.");
        }

        /// <summary>Liczba aktywnych ground mesh'ów — dla debug.</summary>
        public int ActiveGroundCount => groundMeshes.Count;

        [ContextMenu("DEBUG: Rebuild")]
        public void DebugRebuild()
        {
            ClearAllMeshes();
            HandleServiceInitialized();
        }
    }
}
