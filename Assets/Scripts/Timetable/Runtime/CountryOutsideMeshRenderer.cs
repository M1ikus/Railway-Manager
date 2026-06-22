using System.Collections.Generic;
using UnityEngine;
using LibTessDotNet;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Renderuje szary "świat poza Polską" jako JEDEN duży mesh z dziurą w kształcie PL country polygon (M-PL-4 ulepszone).
    ///
    /// Geometria: outer rectangle (np. 5000×5000 km wokół centrum PL) + inner ring = country polygon Polski (jako hole).
    /// LibTess triangulate outer-with-hole → mesh pokrywa wszystko POZA PL z idealną precyzją granicy
    /// (vertex-perfect — używa dokładnych vertices z OSM admin_level=2).
    ///
    /// Zastępuje per-tile heurystykę z <see cref="CountryBorderOverlayRenderer"/> (10km granularność, schodkowate brzegi).
    /// Zalety:
    /// - Idealny kontur granicy (8987 vertices precision)
    /// - Jeden mesh zamiast tysięcy GO
    /// - Eliminuje czarne dziury na tile'ach przygranicznych
    ///
    /// Wymaga `CountryOverlayService.HasCountryPolygon == true` — bez country polygon (admin_level=2)
    /// nie ma jak wyciąć dziury, więc zostaje fallback do <see cref="CountryBorderOverlayRenderer"/>.
    ///
    /// Wpinka: dodaj komponent do dowolnego GameObject'a w MapScene — działa razem z VoivodeshipGroundRenderer.
    /// </summary>
    public class CountryOutsideMeshRenderer : MonoBehaviour
    {
        [Header("Outside Settings")]
        [Tooltip("Kolor obszaru poza Polską (RGB). Domyślnie neutralny szary.")]
        public Color outsideColor = new Color(0.45f, 0.45f, 0.48f, 1f);

        [Tooltip("Y position outside mesh. Default -1 (poniżej ground=0, ale powyżej background camera).")]
        public float outsideY = -1f;

        [Tooltip("Margin (km) wokół Polski bbox dla outer rectangle. 2000km = z głębi pokrywa Berlin/Praha/Mińsk/Lwów.")]
        public float outerMarginKm = 2000f;

        [Tooltip("Włącz/wyłącz outside mesh runtime. False = pure ground bez szarego.")]
        public bool renderingEnabled = true;

        private Material outsideMaterial;
        private GameObject outsideMeshGO;
        private bool isSubscribed;

        void Start()
        {
            outsideMaterial = CreateOutsideMaterial();

            CountryOverlayService.OnInitialized += HandleServiceInitialized;
            isSubscribed = true;

            if (CountryOverlayService.IsInitialized)
                HandleServiceInitialized();
        }

        void OnDestroy()
        {
            if (isSubscribed)
                CountryOverlayService.OnInitialized -= HandleServiceInitialized;

            ClearMesh();

            if (outsideMaterial != null) Destroy(outsideMaterial);
        }

        private Material CreateOutsideMaterial()
        {
            var mat = MaterialFactory.CreateUnlit();
            mat.name = $"CountryOutside_{mat.shader.name}";
            MaterialFactory.SetBaseColor(mat, outsideColor);
            MaterialFactory.SetDoubleSided(mat);
            mat.doubleSidedGI = true;

            return mat;
        }

        private void HandleServiceInitialized()
        {
            if (!renderingEnabled) return;

            ClearMesh();

            if (!CountryOverlayService.HasCountryPolygon)
            {
                Log.Warn("[CountryOutsideMeshRenderer] No country polygon (admin_level=2 'Polska') — renderer disabled. "
                         + "Use CountryBorderOverlayRenderer (per-tile fallback) instead.");
                return;
            }

            // Znajdź country polygon Polski w regionach
            AdminRegion polandPolygon = null;
            foreach (var r in CountryOverlayService.GetPolandRegions())
            {
                if (r.adminLevel == 2)
                {
                    polandPolygon = r;
                    break;
                }
            }

            if (polandPolygon == null)
            {
                Log.Warn("[CountryOutsideMeshRenderer] HasCountryPolygon=true ale region not found — bug.");
                return;
            }

            outsideMeshGO = BuildOutsideMesh(polandPolygon);
            if (outsideMeshGO != null)
            {
                Log.Info($"[CountryOutsideMeshRenderer] Built outside mesh — outer bbox + Polska hole "
                         + $"({polandPolygon.vertices.Count} hole verts).");
            }
        }

        private GameObject BuildOutsideMesh(AdminRegion poland)
        {
            if (outsideMaterial == null) return null;

            // 1. Outer rectangle: bbox PL + margin (np. 2000km z każdej strony — pokrywa Niemcy, Czechy, etc.)
            var bbox = poland.boundingBox;
            float marginM = outerMarginKm * 1000f;
            float outerMinX = bbox.MinX - marginM;
            float outerMaxX = bbox.MaxX + marginM;
            float outerMinY = bbox.MinY - marginM;
            float outerMaxY = bbox.MaxY + marginM;

            // 2. LibTess triangulation: outer rectangle CCW + hole (PL polygon) CW.
            // LibTess obsługuje multi-contour z różnym winding — outer determinuje orientację final mesh,
            // hole jest "wycinany" jeśli winding przeciwny do outer.
            var tess = new Tess();

            // Outer contour — rectangle CCW
            var outerContour = new ContourVertex[]
            {
                new ContourVertex { Position = new Vec3 { X = outerMinX, Y = outerMinY, Z = 0 } },
                new ContourVertex { Position = new Vec3 { X = outerMaxX, Y = outerMinY, Z = 0 } },
                new ContourVertex { Position = new Vec3 { X = outerMaxX, Y = outerMaxY, Z = 0 } },
                new ContourVertex { Position = new Vec3 { X = outerMinX, Y = outerMaxY, Z = 0 } }
            };
            tess.AddContour(outerContour, ContourOrientation.CounterClockwise);

            // Hole contour — PL polygon (extract outline z triangulated mesh)
            // poland.vertices są wszystkimi unique points (po LibTess re-triangulacji w formap),
            // a poland.indices to lista trójkątów. Żeby znaleźć outline, musimy znaleźć edges które
            // są tylko w jednym trójkącie (boundary edges).
            var outline = ExtractPolygonOutline(poland.vertices, poland.indices);
            if (outline == null || outline.Count < 3)
            {
                Log.Warn($"[CountryOutsideMeshRenderer] Failed to extract outline from {poland.vertices.Count} verts / "
                         + $"{poland.indices.Count / 3} triangles. Mesh nie zbudowany.");
                return null;
            }

            var holeContour = new ContourVertex[outline.Count];
            for (int i = 0; i < outline.Count; i++)
            {
                holeContour[i] = new ContourVertex { Position = new Vec3 { X = outline[i].x, Y = outline[i].y, Z = 0 } };
            }
            tess.AddContour(holeContour, ContourOrientation.Clockwise); // CW = hole

            // Tessellate
            try
            {
                tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3);
            }
            catch (System.Exception ex)
            {
                Log.Warn($"[CountryOutsideMeshRenderer] LibTess failed: {ex.Message}");
                return null;
            }

            int vertexCount = tess.VertexCount;
            int elementCount = tess.ElementCount;
            if (vertexCount == 0 || elementCount == 0)
            {
                Log.Warn($"[CountryOutsideMeshRenderer] LibTess produced empty result (verts={vertexCount}, tris={elementCount}).");
                return null;
            }

            // 3. Build Unity mesh
            var go = new GameObject("CountryOutside_Mesh");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.layer = gameObject.layer; // inherit MapLayer 31 — widoczny tylko przez Map Camera
            go.transform.position = new Vector3(0f, outsideY, 0f);

            var verts = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var p = tess.Vertices[i].Position;
                verts[i] = new Vector3(p.X, 0f, p.Y); // Y=0 lokalnie, transform doda outsideY
            }

            // Triangle list — duplicate reverse order dla double-sided (LibTess winding może się różnić)
            var triangles = new int[elementCount * 3 * 2];
            for (int i = 0; i < elementCount; i++)
            {
                int t0 = tess.Elements[i * 3];
                int t1 = tess.Elements[i * 3 + 1];
                int t2 = tess.Elements[i * 3 + 2];
                triangles[i * 3] = t0;
                triangles[i * 3 + 1] = t1;
                triangles[i * 3 + 2] = t2;
                // Back face
                triangles[(elementCount + i) * 3] = t0;
                triangles[(elementCount + i) * 3 + 1] = t2;
                triangles[(elementCount + i) * 3 + 2] = t1;
            }

            var mesh = new Mesh
            {
                name = "CountryOutsideMesh",
                indexFormat = vertexCount > 65535 || triangles.Length > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.vertices = verts;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // Defensive huge bounds
            var b = mesh.bounds;
            mesh.bounds = new Bounds(b.center, new Vector3(20_000_000f, 1000f, 20_000_000f));

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;
            mr.sharedMaterial = outsideMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = -2000; // Pod ground (sortingOrder=-1000 ma VoivodeshipGround)
            return go;
        }

        /// <summary>
        /// Wyciąga outline (boundary edges) z triangulated polygon. Boundary edges to te które
        /// występują tylko w jednym trójkącie (interior edges są shared między 2 trójkątami).
        /// Wynikiem jest uporządkowany ciąg vertices tworzących closed ring.
        ///
        /// Założenie: triangulated polygon jest one connected component bez holes
        /// (formap robi separate features per outer ring, więc to powinno być prawda).
        /// </summary>
        private static List<Vector2> ExtractPolygonOutline(List<Vector2> vertices, List<int> indices)
        {
            // Edge → count map. Edge zapisujemy jako (lower_idx, higher_idx) żeby (a,b) i (b,a) były tym samym.
            var edgeCount = new Dictionary<long, int>();
            var edgeOrient = new Dictionary<long, (int from, int to)>(); // zachowaj original orientation pierwszego napotkania

            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int a = indices[i];
                int b = indices[i + 1];
                int c = indices[i + 2];

                AddEdge(edgeCount, edgeOrient, a, b);
                AddEdge(edgeCount, edgeOrient, b, c);
                AddEdge(edgeCount, edgeOrient, c, a);
            }

            // Boundary edges: count == 1
            var boundaryEdges = new Dictionary<int, int>(); // from → to
            foreach (var kv in edgeCount)
            {
                if (kv.Value == 1)
                {
                    var (from, to) = edgeOrient[kv.Key];
                    boundaryEdges[from] = to;
                }
            }

            if (boundaryEdges.Count < 3) return null;

            // Build outline by walking edges
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
                if (current == startVertex) break; // Ring closed
            }

            return outline.Count >= 3 ? outline : null;
        }

        private static void AddEdge(Dictionary<long, int> count, Dictionary<long, (int, int)> orient, int from, int to)
        {
            int lo = System.Math.Min(from, to);
            int hi = System.Math.Max(from, to);
            long key = ((long)lo << 32) | (uint)hi;

            if (count.TryGetValue(key, out int c))
                count[key] = c + 1;
            else
            {
                count[key] = 1;
                orient[key] = (from, to);
            }
        }

        private void ClearMesh()
        {
            if (outsideMeshGO != null)
            {
                var mf = outsideMeshGO.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
                Destroy(outsideMeshGO);
                outsideMeshGO = null;
            }
        }

        [ContextMenu("DEBUG: Rebuild")]
        public void DebugRebuild()
        {
            ClearMesh();
            HandleServiceInitialized();
        }
    }
}
