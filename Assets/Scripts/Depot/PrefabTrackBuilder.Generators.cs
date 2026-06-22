using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    public partial class PrefabTrackBuilder
    {
        // ═══════════════════════════════════════════
        //  GENERATORY WIZUALNE — ballast, sleepers, rails, colliders
        // ═══════════════════════════════════════════

        // Przekrój NASYPU podsypki (offset poprzeczny do toru, wysokość) — trapez ze skarpami.
        // Dół 3.5m / korona 2.9m na wys 0.105m (wchodzi ~2/3 wysokości podkładu, podkłady wystają).
        private static readonly (float off, float h)[] BallastCross =
        {
            (-1.75f, 0f), (-1.45f, 0.105f), (1.45f, 0.105f), (1.75f, 0f),
        };

        /// <summary>
        /// Generuje podsypkę jako MESH NASYPU (loft przekroju trapezowego wzdłuż polyline).
        /// Wcześniej płaski Quad — teraz nasyp 3D ze skarpami (skarpa opada za końcami podkładów).
        /// </summary>
        private void GenerateBallast(Transform parent, List<Vector3> polyline)
        {
            if (polyline == null || polyline.Count < 2) return;

            int cn = BallastCross.Length;
            var verts = new List<Vector3>(polyline.Count * cn);
            var uvs = new List<Vector2>(polyline.Count * cn);
            var tris = new List<int>();

            float accum = 0f;
            for (int i = 0; i < polyline.Count; i++)
            {
                if (i > 0) accum += Vector3.Distance(polyline[i - 1], polyline[i]);
                Vector3 pos = polyline[i];
                Vector3 tangent = TrackGeometry.GetTangentAtIndex(polyline, i);
                Vector3 perp = Vector3.Cross(tangent, Vector3.up).normalized;
                foreach (var (off, h) in BallastCross)
                {
                    verts.Add(new Vector3(pos.x, 0f, pos.z) + perp * off + Vector3.up * h);
                    // UV tile ~2m (tłuczeń powtarzalny): U wzdłuż toru, V w poprzek nasypu
                    uvs.Add(new Vector2(accum / 2f, (off + 1.75f) / 2f));
                }
            }

            for (int i = 0; i < polyline.Count - 1; i++)
            {
                int b0 = i * cn;
                int b1 = (i + 1) * cn;
                for (int j = 0; j < cn - 1; j++)
                {
                    // winding dla normalnej w GÓRĘ (powierzchnia widoczna z góry kamery)
                    tris.Add(b0 + j); tris.Add(b1 + j + 1); tris.Add(b0 + j + 1);
                    tris.Add(b0 + j); tris.Add(b1 + j); tris.Add(b1 + j + 1);
                }
            }

            var mesh = new Mesh { name = "BallastMesh" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // długie tory > 65k vty
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GameObject ballast = new GameObject("Ballast");
            ballast.transform.SetParent(parent, worldPositionStays: true);
            ballast.transform.position = Vector3.zero;   // verts są w world coords -> obiekt w world origin
            ballast.AddComponent<MeshFilter>().sharedMesh = mesh;
            ballast.AddComponent<MeshRenderer>().sharedMaterial =
                ballastMaterialOverride != null ? ballastMaterialOverride : ballastMaterial;
        }

        /// <summary>
        /// Generuje podkłady wzdłuż polyline, orientowane prostopadle do tangenty.
        /// </summary>
        private void GenerateSleepers(Transform parent, List<Vector3> polyline, float totalLength)
        {
            int sleeperCount = Mathf.FloorToInt(totalLength / sleeperSpacing);

            for (int i = 0; i <= sleeperCount; i++)
            {
                float distance = i * sleeperSpacing;
                var (pos, tangent) = TrackGeometry.GetPointAtDistance(polyline, distance);
                Vector3 perpendicular = Vector3.Cross(tangent, Vector3.up).normalized;

                if (sleeperPrefab != null)
                {
                    Quaternion rot = Quaternion.LookRotation(perpendicular, Vector3.up);
                    GameObject sleeper = Instantiate(sleeperPrefab, pos, rot);
                    sleeper.name = $"Sleeper_{i}";
                    sleeper.transform.SetParent(parent, true);
                }
                else
                {
                    // Placeholder: Cube - długa oś (2.6m) prostopadle do toru
                    // Forward (Z) = perpendicular do toru, więc Z = 2.6m (długość podkładu)
                    float angleY = Mathf.Atan2(perpendicular.x, perpendicular.z) * Mathf.Rad2Deg;

                    GameObject sleeper = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    sleeper.name = $"Sleeper_{i}";
                    sleeper.transform.SetParent(parent, false);
                    sleeper.transform.position = new Vector3(pos.x, 0.075f, pos.z);
                    sleeper.transform.rotation = Quaternion.Euler(0f, angleY, 0f);
                    sleeper.transform.localScale = new Vector3(0.25f, 0.15f, 2.6f);
                    sleeper.GetComponent<MeshRenderer>().material = sleeperMaterial;
                    Destroy(sleeper.GetComponent<BoxCollider>());
                }
            }
        }

        /// <summary>
        /// Generuje szyny jako LineRenderer podążający za polyline z offsetem.
        /// </summary>
        private void GenerateRailLineRenderer(Transform parent, List<Vector3> polyline, float offset, string name)
        {
            List<Vector3> railPolyline = TrackGeometry.OffsetPolyline(polyline, offset);

            GameObject railObj = new GameObject(name);
            railObj.transform.SetParent(parent, false);

            LineRenderer lr = railObj.AddComponent<LineRenderer>();
            lr.material = railMaterial;
            lr.startColor = railMaterial.color;
            lr.endColor = railMaterial.color;
            lr.startWidth = 0.07f;
            lr.endWidth = 0.07f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            // Podnieś szyny nad podkłady
            Vector3[] positions = new Vector3[railPolyline.Count];
            for (int i = 0; i < railPolyline.Count; i++)
            {
                positions[i] = railPolyline[i] + Vector3.up * 0.17f;
            }

            lr.positionCount = positions.Length;
            lr.SetPositions(positions);
        }

        /// <summary>
        /// Generuje szyny z prefabów wzdłuż polyline z offsetem.
        /// </summary>
        private void GenerateRailPrefabs(Transform parent, List<Vector3> polyline, float offset, string side)
        {
            List<Vector3> railPolyline = TrackGeometry.OffsetPolyline(polyline, offset);
            float totalLength = TrackGeometry.CalculatePolylineLength(railPolyline);
            int railCount = Mathf.CeilToInt(totalLength / 1f); // 1m per rail

            for (int i = 0; i < railCount; i++)
            {
                float dist = i * 1f;
                var (pos, tangent) = TrackGeometry.GetPointAtDistance(railPolyline, dist);
                Quaternion rot = Quaternion.LookRotation(tangent, Vector3.up);

                GameObject rail = Instantiate(railPrefab, pos, rot);
                rail.name = $"Rail_{side}_{i}";
                rail.transform.SetParent(parent, true);
            }
        }

        /// <summary>
        /// Generuje collidery wzdłuż polyline.
        /// Proste odcinki: 1 BoxCollider. Krzywe: seria BoxColliderów.
        /// </summary>
        private void GenerateColliders(GameObject trackObj, List<Vector3> polyline, float totalLength)
        {
            Vector3 startToEnd = polyline[polyline.Count - 1] - polyline[0];
            float directDist = startToEnd.magnitude;
            bool isStraight = Mathf.Abs(totalLength - directDist) < 0.5f;

            if (isStraight)
            {
                // Jeden BoxCollider jako child (nie modyfikuj parenta!)
                Vector3 mid = (polyline[0] + polyline[polyline.Count - 1]) / 2f;
                Vector3 dir = startToEnd.normalized;
                float angleY = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

                GameObject colObj = new GameObject("Collider_0");
                colObj.transform.SetParent(trackObj.transform, false);
                colObj.transform.position = mid;
                colObj.transform.rotation = Quaternion.Euler(0, angleY, 0);
                colObj.tag = trackTag;

                BoxCollider col = colObj.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, colliderHeight / 2f, 0f);
                col.size = new Vector3(colliderWidth, colliderHeight, directDist);
            }
            else
            {
                // Seria BoxColliderów na łukach
                float dist = 0f;
                int colIdx = 0;

                while (dist < totalLength)
                {
                    float segLen = Mathf.Min(colliderSegmentLength, totalLength - dist);
                    if (segLen < 0.5f) break;

                    var (pos, tangent) = TrackGeometry.GetPointAtDistance(polyline, dist + segLen / 2f);
                    float angleY = Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg;

                    GameObject colObj = new GameObject($"Collider_{colIdx}");
                    colObj.transform.SetParent(trackObj.transform, false);
                    colObj.transform.position = new Vector3(pos.x, 0f, pos.z);
                    colObj.transform.rotation = Quaternion.Euler(0, angleY, 0);
                    colObj.tag = trackTag;

                    BoxCollider col = colObj.AddComponent<BoxCollider>();
                    col.center = new Vector3(0f, colliderHeight / 2f, 0f);
                    col.size = new Vector3(colliderWidth, colliderHeight, segLen);

                    dist += colliderSegmentLength;
                    colIdx++;
                }
            }
        }
    }
}
