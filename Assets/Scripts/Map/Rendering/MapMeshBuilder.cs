using System.Collections.Generic;
using UnityEngine;
using formap;

namespace MapSystem
{
    /// <summary>
    /// Buduje pojedynczy <see cref="Mesh"/> z listy <see cref="MeshGeometry"/> (warstwa kafla),
    /// bez tworzenia GameObjectów / dotykania hierarchii głównego renderera. Dla mini-mapy OSM
    /// (RouteMapPreview), która renderuje WŁASNE kafle w wybranym LOD na warstwie MapPreview.
    ///
    /// Składanie mesha jest celowo zduplikowane (krótkie, stabilne) z
    /// <c>MapRenderer.CreateMeshObject</c> zamiast refaktorować krytyczną ścieżkę renderu
    /// głównej mapy (której nie weryfikujemy wizualnie headless). Mapowanie 2D→3D i topologia
    /// muszą pozostać zgodne z MapRenderer (X→X, Y→Z, height→Y; UInt32; Lines/Triangles).
    /// </summary>
    public static class MapMeshBuilder
    {
        /// <summary>
        /// Zwraca Mesh dla warstwy (wszystkie features sklejone) albo null gdy brak geometrii.
        /// height = Y warstwy (jak GetLayerHeight), isLine = czy topologia Lines (jak IsLineLayer).
        /// <paramref name="reverseWinding"/> = odwróć kolejność trójkątów (jak główna mapa robi dla
        /// fillów: <c>!isLine &amp;&amp; !isHighway &amp;&amp; !isWaterway</c>) — bez tego fille są nawinięte
        /// odwrotnie niż na głównej mapie i przy single-sided materiale znikają (backface cull).
        /// </summary>
        public static Mesh BuildMesh(IReadOnlyList<MeshGeometry> features, float height, bool isLine, bool reverseWinding = false)
        {
            if (features == null || features.Count == 0) return null;

            var verts = new List<Vector3>();
            var indices = new List<int>();

            for (int f = 0; f < features.Count; f++)
            {
                var feature = features[f];
                if (feature == null) continue;
                int vertexOffset = verts.Count;

                var fv = feature.Vertices;
                for (int i = 0; i < fv.Count; i++)
                    verts.Add(new Vector3(fv[i].x, height, fv[i].y)); // 2D→3D: X→X, Y→Z, height→Y

                var fi = feature.Indices;
                for (int i = 0; i < fi.Count; i++)
                    indices.Add(vertexOffset + fi[i]);
            }

            if (verts.Count == 0 || indices.Count == 0) return null;

            // Lustro MapRenderer.ReverseTriangleWindingOrder — swap 1. i 3. wierzchołka per trójkąt.
            if (!isLine && reverseWinding)
                for (int i = 0; i + 2 < indices.Count; i += 3)
                    (indices[i], indices[i + 2]) = (indices[i + 2], indices[i]);

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetIndices(indices, isLine ? MeshTopology.Lines : MeshTopology.Triangles, 0);
            if (!isLine) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
