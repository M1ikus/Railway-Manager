using System.Collections.Generic;
using UnityEngine;
using LibTessDotNet;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Renderuje syntetyczne wody (Bałtyk + Zatoki + Zalewy) używając admin polygons + coastlines.
    ///
    /// **Etap 1 — Bałtyk otwarty:** PL country polygon (admin_level=2) MINUS suma voivodeships
    /// = morze terytorialne PL (12 mil od coast).
    ///
    /// **Etap 2 — Zatoki/Zalewy:** OSM coastline mainline robi "zigzag" wokół półwyspów (Hel, Mierzeja).
    /// Self-loop detection: szukamy `chain[i]` blisko `chain[j]` (j > i+min_skip, distance < neck) — to neck zatoki.
    /// Polygon `chain[i..j]` close + centroid IN PL country = bay! (Zatoka Gdańska, Zalew Wiślany itd.)
    /// </summary>
    public class SyntheticWaterRenderer : MonoBehaviour
    {
        [Header("Render Settings")]
        [Tooltip("Źródło materiału. UseOwn = własny Unlit/Color z waterColor. " +
                 "MapRendererWater/Waterway = pożycz materiał z MapRenderer (spójny styl z OSM water/rzek).")]
        public MaterialSource materialSource = MaterialSource.MapRendererWaterway;

        public enum MaterialSource { UseOwn, MapRendererWater, MapRendererWaterway }

        public Color waterColor = new Color(0.18f, 0.42f, 0.68f, 1f);
        public float waterY = 0.1f;
        public bool renderingEnabled = true;

        [Header("Bay detection (coastline self-loops)")]
        [Tooltip("Tolerancja łączenia coastline endpoint'ów w mainline chain (m). Default 15000m " +
                 "(15km) — agresywne łączenie luk dla OSM coastline z drobnymi gap'ami przy tile boundaries.")]
        public float chainConnectToleranceM = 15000f;

        [Tooltip("Maximum 'neck' bay (m): odległość między chain[i] i chain[j] żeby uznać za zatokę. " +
                 "Default 20000m (20km) — łapie Zatokę Gdańską (Hel-Władysławowo ~10km) i Zalew Wiślany (~15km).")]
        public float bayNeckMaxM = 20000f;

        [Tooltip("Minimum vertex skip (j > i+N) dla bay detection. Default 50.")]
        public int bayMinSkipVertices = 50;

        [Tooltip("Minimalny obwód bay polygon (m). Default 20km — filtruje drobne pętle.")]
        public float bayMinPerimeterM = 20000f;

        [Header("Toggles (debug)")]
        public bool renderOpenSeaMesh = true;        // country-voivodeships (Bałtyk otwarty 12 mil)
        public bool renderBays = false;               // self-loop detection (DISABLED — false positives)
        public bool renderManualPolygons = false;    // manual user-edited (Zatoka/Zalew) — niepotrzebne po formap fix (natural=bay)

        [Header("Manual Water Polygons (user-edited)")]
        [Tooltip("Lista zamknniętych polygonów wody które są zbyt skomplikowane dla auto-detection " +
                 "(Zatoka Gdańska, Zalew Wiślany itp.). Każdy element to lista vertices Vector2 " +
                 "w world coords (X=east, Y=north). Min 3 vertices per polygon. Kolejność CCW lub CW " +
                 "obojętna (LibTess radzi sobie).")]
        public ManualPolygon[] manualPolygons = new ManualPolygon[]
        {
            // Zatoka Gdańska — przybliżenie (10 vertices). Tweak w Inspector po wizualizacji.
            // Bbox ok (200k..380k) X, (220k..290k) Y w world coords.
            new ManualPolygon
            {
                name = "Zatoka Gdańska",
                vertices = new Vector2[]
                {
                    new Vector2(200000, 230000),  // SW corner (Sopot/Gdynia coast)
                    new Vector2(230000, 225000),  // S coast Gdańsk
                    new Vector2(280000, 225000),  // S coast east of Gdańsk
                    new Vector2(330000, 235000),  // SE (Mierzei west tip)
                    new Vector2(335000, 270000),  // E (Mierzei west side)
                    new Vector2(310000, 290000),  // NE (otwarte morze)
                    new Vector2(250000, 290000),  // N (otwarte morze)
                    new Vector2(190000, 270000),  // NW (od Helu — tip Helu)
                    new Vector2(180000, 250000),  // W (Hel south side blisko Władysławowa)
                    new Vector2(190000, 235000),  // SW return (przy Władysławowie)
                }
            },
            // Zalew Wiślany — przybliżenie (8 vertices). Tweak w Inspector po wizualizacji.
            new ManualPolygon
            {
                name = "Zalew Wiślany",
                vertices = new Vector2[]
                {
                    new Vector2(330000, 250000),  // SW (mainland coast Mierzei west)
                    new Vector2(380000, 248000),  // S (mainland coast Elbląg)
                    new Vector2(440000, 250000),  // SE (granica RU)
                    new Vector2(450000, 270000),  // NE (granica RU on Mierzei)
                    new Vector2(420000, 275000),  // N (Mierzei south coast east)
                    new Vector2(380000, 268000),  // N (Mierzei south coast central)
                    new Vector2(345000, 263000),  // NW (Mierzei south coast west)
                    new Vector2(335000, 255000),  // W (Mierzei tip → mainland)
                }
            }
        };

        [System.Serializable]
        public class ManualPolygon
        {
            public string name = "Water";
            public Vector2[] vertices;
        }

        private Material waterMaterial;
        private readonly List<GameObject> waterMeshes = new();
        private AdminRegion _polandCountry;

        void Start()
        {
            waterMaterial = CreateWaterMaterial();
            StartCoroutine(WaitForReady());
        }

        void OnDestroy()
        {
            ClearMeshes();
            // Destroy TYLKO own material (UseOwn). MapRenderer shared materials NIE destroyujemy
            // bo są używane przez OSM water layer rendering.
            if (waterMaterial != null && materialSource == MaterialSource.UseOwn) Destroy(waterMaterial);
        }

        private System.Collections.IEnumerator WaitForReady()
        {
            float elapsed = 0f;
            while (elapsed < 60f)
            {
                var init = TimetableInitializer.Instance;
                if (init != null && init.IsReady && init.Coastlines != null && CountryOverlayService.IsInitialized)
                {
                    BuildAll(init.Coastlines);
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }
            Log.Warn("[SyntheticWaterRenderer] Timeout waiting for init");
        }

        private Material CreateWaterMaterial()
        {
            // Spróbuj pożyczyć materiał z MapRenderer (spójny styl z OSM water/rzek)
            if (materialSource == MaterialSource.MapRendererWater || materialSource == MaterialSource.MapRendererWaterway)
            {
                var mr = FindAnyObjectByType<MapSystem.MapRenderer>();
                if (mr != null)
                {
                    var src = materialSource == MaterialSource.MapRendererWater ? mr.waterMaterial : mr.waterwayMaterial;
                    if (src != null)
                    {
                        Log.Info($"[SyntheticWaterRenderer] Using MapRenderer.{materialSource} material: '{src.name}'");
                        return src; // shared instance — nie tworzymy kopii
                    }
                    Log.Warn($"[SyntheticWaterRenderer] MapRenderer found ale {materialSource} field is null — fallback do UseOwn");
                }
                else
                {
                    Log.Warn("[SyntheticWaterRenderer] No MapRenderer w scenie — fallback do UseOwn material");
                }
            }

            // UseOwn fallback
            var mat = MaterialFactory.CreateUnlit();
            mat.name = $"SyntheticWater_{mat.shader.name}";
            MaterialFactory.SetBaseColor(mat, waterColor);
            MaterialFactory.SetDoubleSided(mat);
            return mat;
        }

        private void BuildAll(List<List<Vector2>> coastlines)
        {
            if (!renderingEnabled || waterMaterial == null) return;
            ClearMeshes();

            // Cache PL country (admin_level=2) i voivodeships (admin_level=4)
            _polandCountry = null;
            var voivodeships = new List<AdminRegion>();
            foreach (var r in CountryOverlayService.GetPolandRegions())
            {
                if (r.adminLevel == 2) _polandCountry = r;
                else if (r.adminLevel == 4) voivodeships.Add(r);
            }

            // ETAP 1: country MINUS voivodeships = open sea (Bałtyk 12 mil)
            if (renderOpenSeaMesh && _polandCountry != null && voivodeships.Count > 0)
            {
                var go = BuildOpenSeaMesh(_polandCountry, voivodeships);
                if (go != null) waterMeshes.Add(go);
            }

            // ETAP 2: bay detection z coastlines (DISABLED by default — false positives)
            if (renderBays && coastlines != null && coastlines.Count > 0 && _polandCountry != null)
            {
                BuildBayMeshes(coastlines);
            }

            // ETAP 3: manual polygons (Zatoka Gdańska, Zalew Wiślany — user-edited)
            if (renderManualPolygons && manualPolygons != null)
            {
                int built = 0;
                foreach (var mp in manualPolygons)
                {
                    if (mp == null || mp.vertices == null || mp.vertices.Length < 3) continue;
                    var verts = new List<Vector2>(mp.vertices);

                    // Compute bbox dla log — user może zweryfikować gdzie polygon jest
                    float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                    foreach (var v in verts)
                    {
                        if (v.x < minX) minX = v.x; if (v.x > maxX) maxX = v.x;
                        if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y;
                    }
                    Log.Info($"[SyntheticWaterRenderer] Manual '{mp.name}': {verts.Count} verts, "
                             + $"bbox=({minX:F0},{minY:F0})-({maxX:F0},{maxY:F0}), "
                             + $"center=({(minX + maxX) * 0.5f:F0},{(minY + maxY) * 0.5f:F0})");

                    var go = TriangulatePolygon($"SyntheticWater_Manual_{mp.name}", verts);
                    if (go != null) { waterMeshes.Add(go); built++; }
                }
                Log.Info($"[SyntheticWaterRenderer] Manual polygons: {built}/{manualPolygons.Length} rendered");
            }

            Log.Info($"[SyntheticWaterRenderer] Done — total {waterMeshes.Count} water meshes");
        }

        // === ETAP 1: open sea (country MINUS voivodeships) ===

        private GameObject BuildOpenSeaMesh(AdminRegion country, List<AdminRegion> voivodeships)
        {
            var countryOutline = ExtractPolygonOutline(country.vertices, country.indices);
            if (countryOutline == null || countryOutline.Count < 3)
            {
                Log.Warn("[SyntheticWaterRenderer] Failed to extract country outline");
                return null;
            }

            var voivOutlines = new List<List<Vector2>>();
            int outlineFails = 0;
            foreach (var v in voivodeships)
            {
                var o = ExtractPolygonOutline(v.vertices, v.indices);
                if (o != null && o.Count >= 3) voivOutlines.Add(o);
                else outlineFails++;
            }

            Log.Info($"[SyntheticWaterRenderer] OpenSea: PL outline {countryOutline.Count} verts, "
                     + $"{voivOutlines.Count} voivodeship holes ({outlineFails} fail)");

            var tess = new Tess();
            AddContour(tess, countryOutline);
            foreach (var o in voivOutlines) AddContour(tess, o);

            try { tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3); }
            catch (System.Exception ex) { Log.Warn($"[SyntheticWaterRenderer] OpenSea LibTess: {ex.Message}"); return null; }

            return MeshFromTess(tess, "SyntheticWater_OpenSea");
        }

        // === ETAP 2: bay detection (coastline self-loops) ===

        private void BuildBayMeshes(List<List<Vector2>> coastlines)
        {
            // Chain merge z dużą tolerancją żeby dostać max kilka chain'ów (idealnie 1 mainline)
            var chains = ConnectChains(coastlines, chainConnectToleranceM);

            // Sortuj po długości malejąco, log top 5
            chains.Sort((a, b) => b.Count.CompareTo(a.Count));
            Log.Info($"[SyntheticWaterRenderer] Bay detection: {coastlines.Count} ways → {chains.Count} chains "
                     + $"(top 5 by verts: {string.Join(", ", chains.GetRange(0, System.Math.Min(5, chains.Count)).ConvertAll(c => c.Count.ToString()))})");

            // Per chain: znajdź self-loops (bays)
            int totalBays = 0, totalBaysSkipped = 0;
            for (int ci = 0; ci < chains.Count; ci++)
            {
                var chain = chains[ci];
                if (chain.Count < bayMinSkipVertices + 5) continue;

                var bays = DetectSelfLoops(chain);
                int chainBays = 0;
                foreach (var bay in bays)
                {
                    float perimeter = ChainPerimeter(bay);
                    var centroid = Centroid(bay);
                    bool inPL = _polandCountry.ContainsPoint(centroid);

                    string status;
                    if (perimeter < bayMinPerimeterM) status = $"SKIP perim<{bayMinPerimeterM / 1000f:F0}km";
                    else if (!inPL) status = "SKIP centroid OUT PL";
                    else status = "RENDER";

                    Log.Info($"[SyntheticWaterRenderer]   chain {ci}: bay perim={perimeter / 1000f:F0}km "
                             + $"centroid=({centroid.x:F0},{centroid.y:F0}) → {status}");

                    if (status != "RENDER") { totalBaysSkipped++; continue; }

                    var go = TriangulatePolygon($"SyntheticWater_Bay_{ci}_{chainBays}", bay);
                    if (go != null) { waterMeshes.Add(go); chainBays++; }
                }
                totalBays += chainBays;
            }
            Log.Info($"[SyntheticWaterRenderer] Bays: {totalBays} rendered, {totalBaysSkipped} skipped");
        }

        /// <summary>
        /// Self-loop detection: szuka par (i, j) w chain gdzie distance(chain[i], chain[j]) < neck
        /// AND j > i + min_skip. Polygon chain[i..j] = bay/peninsula loop.
        /// Greedy — po znalezieniu loop, skip do j+1 żeby nie reuse.
        /// </summary>
        private List<List<Vector2>> DetectSelfLoops(List<Vector2> chain)
        {
            var result = new List<List<Vector2>>();
            float neckSq = bayNeckMaxM * bayNeckMaxM;

            int i = 0;
            while (i < chain.Count - bayMinSkipVertices)
            {
                int foundJ = -1;
                // Find first j > i+min_skip gdzie distance < neck
                for (int j = i + bayMinSkipVertices; j < chain.Count; j++)
                {
                    if ((chain[j] - chain[i]).sqrMagnitude < neckSq)
                    {
                        foundJ = j;
                        break;
                    }
                }
                if (foundJ < 0) { i++; continue; }

                // Build bay polygon = chain[i..foundJ] (close it implicitly via tessellation)
                var bay = new List<Vector2>(foundJ - i + 1);
                for (int k = i; k <= foundJ; k++) bay.Add(chain[k]);
                result.Add(bay);

                i = foundJ + 1; // skip past this loop
            }
            return result;
        }

        // === Helpers ===

        private static List<List<Vector2>> ConnectChains(List<List<Vector2>> ways, float tolerance)
        {
            var result = new List<List<Vector2>>();
            if (ways == null || ways.Count == 0) return result;
            float toleranceSq = tolerance * tolerance;

            var remaining = new List<LinkedList<Vector2>>();
            foreach (var way in ways)
            {
                if (way == null || way.Count < 2) continue;
                var ll = new LinkedList<Vector2>();
                foreach (var v in way) ll.AddLast(v);
                remaining.Add(ll);
            }

            while (remaining.Count > 0)
            {
                var current = remaining[0];
                remaining.RemoveAt(0);
                bool extended;
                do
                {
                    extended = false;
                    var head = current.First.Value;
                    var tail = current.Last.Value;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        var other = remaining[i];
                        var oHead = other.First.Value;
                        var oTail = other.Last.Value;
                        if ((oHead - tail).sqrMagnitude <= toleranceSq)
                        { var n = other.First.Next; while (n != null) { current.AddLast(n.Value); n = n.Next; } remaining.RemoveAt(i); extended = true; break; }
                        if ((oTail - head).sqrMagnitude <= toleranceSq)
                        { var n = other.Last.Previous; while (n != null) { current.AddFirst(n.Value); n = n.Previous; } remaining.RemoveAt(i); extended = true; break; }
                        if ((oHead - head).sqrMagnitude <= toleranceSq)
                        { var n = other.First.Next; while (n != null) { current.AddFirst(n.Value); n = n.Next; } remaining.RemoveAt(i); extended = true; break; }
                        if ((oTail - tail).sqrMagnitude <= toleranceSq)
                        { var n = other.Last.Previous; while (n != null) { current.AddLast(n.Value); n = n.Previous; } remaining.RemoveAt(i); extended = true; break; }
                    }
                } while (extended);
                result.Add(new List<Vector2>(current));
            }
            return result;
        }

        private static float ChainPerimeter(List<Vector2> chain)
        {
            float len = 0f;
            for (int i = 1; i < chain.Count; i++) len += Vector2.Distance(chain[i - 1], chain[i]);
            return len;
        }

        private static Vector2 Centroid(List<Vector2> chain)
        {
            Vector2 sum = Vector2.zero;
            foreach (var v in chain) sum += v;
            return sum / chain.Count;
        }

        private static void AddContour(Tess tess, List<Vector2> verts)
        {
            var c = new ContourVertex[verts.Count];
            for (int i = 0; i < verts.Count; i++)
                c[i] = new ContourVertex { Position = new Vec3 { X = verts[i].x, Y = verts[i].y, Z = 0 } };
            tess.AddContour(c, ContourOrientation.Original);
        }

        private GameObject TriangulatePolygon(string name, List<Vector2> vertices)
        {
            if (vertices.Count < 3) return null;
            var tess = new Tess();
            AddContour(tess, vertices);
            try { tess.Tessellate(WindingRule.NonZero, ElementType.Polygons, 3); }
            catch (System.Exception ex) { Log.Warn($"[SyntheticWaterRenderer] {name} LibTess: {ex.Message}"); return null; }
            return MeshFromTess(tess, name);
        }

        private GameObject MeshFromTess(Tess tess, string name)
        {
            int vc = tess.VertexCount, ec = tess.ElementCount;
            if (vc == 0 || ec == 0) return null;

            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.layer = gameObject.layer; // inherit MapLayer 31 — widoczny tylko przez Map Camera
            go.transform.position = new Vector3(0f, waterY, 0f);

            var meshVerts = new Vector3[vc];
            for (int i = 0; i < vc; i++)
            { var p = tess.Vertices[i].Position; meshVerts[i] = new Vector3(p.X, 0f, p.Y); }

            var triangles = new int[ec * 3 * 2];
            for (int i = 0; i < ec; i++)
            {
                int t0 = tess.Elements[i * 3], t1 = tess.Elements[i * 3 + 1], t2 = tess.Elements[i * 3 + 2];
                triangles[i * 3] = t0; triangles[i * 3 + 1] = t1; triangles[i * 3 + 2] = t2;
                triangles[(ec + i) * 3] = t0; triangles[(ec + i) * 3 + 1] = t2; triangles[(ec + i) * 3 + 2] = t1;
            }

            var mesh = new Mesh
            {
                name = $"{name}_Mesh",
                indexFormat = vc > 65535 || triangles.Length > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16,
                vertices = meshVerts,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(mesh.bounds.center, new Vector3(20_000_000f, 1000f, 20_000_000f));

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;
            mr.sharedMaterial = waterMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        // === Boundary edge extraction (kopia) ===

        private static List<Vector2> ExtractPolygonOutline(List<Vector2> vertices, List<int> indices)
        {
            var edgeCount = new Dictionary<long, int>();
            var edgeOrient = new Dictionary<long, (int from, int to)>();
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                AddEdge(edgeCount, edgeOrient, a, b);
                AddEdge(edgeCount, edgeOrient, b, c);
                AddEdge(edgeCount, edgeOrient, c, a);
            }
            var boundaryEdges = new Dictionary<int, int>();
            foreach (var kv in edgeCount)
                if (kv.Value == 1) { var (from, to) = edgeOrient[kv.Key]; boundaryEdges[from] = to; }
            if (boundaryEdges.Count < 3) return null;

            var outline = new List<Vector2>();
            var visited = new HashSet<int>();
            int startVertex = -1;
            foreach (var kv in boundaryEdges) { startVertex = kv.Key; break; }
            int current = startVertex;
            while (current >= 0 && !visited.Contains(current))
            {
                visited.Add(current);
                outline.Add(vertices[current]);
                if (!boundaryEdges.TryGetValue(current, out int next)) break;
                current = next;
                if (current == startVertex) break;
            }
            return outline.Count >= 3 ? outline : null;
        }

        private static void AddEdge(Dictionary<long, int> count, Dictionary<long, (int, int)> orient, int from, int to)
        {
            int lo = System.Math.Min(from, to), hi = System.Math.Max(from, to);
            long key = ((long)lo << 32) | (uint)hi;
            if (count.TryGetValue(key, out int c)) count[key] = c + 1;
            else { count[key] = 1; orient[key] = (from, to); }
        }

        private void ClearMeshes()
        {
            foreach (var go in waterMeshes)
            {
                if (go == null) continue;
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
                Destroy(go);
            }
            waterMeshes.Clear();
        }

        [ContextMenu("DEBUG: Rebuild")]
        public void DebugRebuild()
        {
            var init = TimetableInitializer.Instance;
            if (init?.Coastlines != null) BuildAll(init.Coastlines);
        }
    }
}
