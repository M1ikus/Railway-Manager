using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    /// <summary>
    /// Generuje proceduralny tube mesh wzdłuż ścieżki (path).
    /// Używany do: drut jezdny, linka nośna, wieszaki, wysięgniki.
    /// </summary>
    public static class TubeMeshGenerator
    {
        /// <summary>
        /// Generuje tube mesh wzdłuż podanej ścieżki.
        /// </summary>
        /// <param name="path">Lista punktów definiujących oś tube'a</param>
        /// <param name="radius">Promień rury</param>
        /// <param name="radialSegments">Liczba segmentów dookoła (6 = wystarczające dla cienkiego drutu)</param>
        /// <returns>Wygenerowany Mesh</returns>
        public static Mesh GenerateTube(List<Vector3> path, float radius, int radialSegments = 6)
        {
            if (path == null || path.Count < 2)
                return new Mesh();

            int pathCount = path.Count;
            int vertCount = pathCount * radialSegments;
            int triCount = (pathCount - 1) * radialSegments * 6;

            var vertices = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var triangles = new int[triCount];

            for (int i = 0; i < pathCount; i++)
            {
                // Tangent w punkcie i
                Vector3 tangent;
                if (i == 0)
                    tangent = (path[1] - path[0]).normalized;
                else if (i == pathCount - 1)
                    tangent = (path[pathCount - 1] - path[pathCount - 2]).normalized;
                else
                    tangent = (path[i + 1] - path[i - 1]).normalized;

                if (tangent.sqrMagnitude < 0.001f)
                    tangent = Vector3.forward;

                // Bazowe wektory prostopadłe (Frenet frame)
                Vector3 up = Vector3.up;
                if (Mathf.Abs(Vector3.Dot(tangent, up)) > 0.99f)
                    up = Vector3.right;

                Vector3 normal = Vector3.Cross(tangent, up).normalized;
                Vector3 binormal = Vector3.Cross(tangent, normal).normalized;

                int baseIdx = i * radialSegments;
                for (int j = 0; j < radialSegments; j++)
                {
                    float angle = (j / (float)radialSegments) * Mathf.PI * 2f;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);

                    Vector3 offset = (normal * cos + binormal * sin) * radius;
                    vertices[baseIdx + j] = path[i] + offset;
                    normals[baseIdx + j] = offset.normalized;
                }
            }

            // Trójkąty: łączymy pierścienie
            int ti = 0;
            for (int i = 0; i < pathCount - 1; i++)
            {
                int ring0 = i * radialSegments;
                int ring1 = (i + 1) * radialSegments;

                for (int j = 0; j < radialSegments; j++)
                {
                    int j1 = (j + 1) % radialSegments;

                    // Trójkąt 1
                    triangles[ti++] = ring0 + j;
                    triangles[ti++] = ring1 + j;
                    triangles[ti++] = ring1 + j1;

                    // Trójkąt 2
                    triangles[ti++] = ring0 + j;
                    triangles[ti++] = ring1 + j1;
                    triangles[ti++] = ring0 + j1;
                }
            }

            var mesh = new Mesh();
            if (vertCount > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            return mesh;
        }

        /// <summary>
        /// Generuje tube mesh z jednym wspólnym materiałem, jako gotowy GameObject.
        /// </summary>
        public static GameObject CreateTubeObject(string name, List<Vector3> path,
            float radius, Material material, Transform parent = null, int radialSegments = 6)
        {
            Mesh mesh = GenerateTube(path, radius, radialSegments);
            if (mesh.vertexCount == 0) return null;

            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.material = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            return go;
        }
    }
}
