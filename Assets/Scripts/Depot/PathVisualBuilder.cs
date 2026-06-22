using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    [System.Serializable]
    public class PlacedPathSegment
    {
        public GameObject SegmentObject;
        public Vector3 StartPosition;
        public Vector3 EndPosition;
        public PathEdgeType Type;
        public int GraphEdgeId = -1;
        public float Width;
    }

    /// <summary>
    /// Generuje proceduralne meshe dla ścieżek, dróg i parkingów.
    /// </summary>
    public class PathVisualBuilder : MonoBehaviour
    {
        private Transform pathsParent;
        private Material pathMaterial;
        private Material roadMaterial;
        private Material parkingMaterial;
        private Material markingMaterial;

        private List<PlacedPathSegment> placedSegments = new();
        public IReadOnlyList<PlacedPathSegment> PlacedSegments => placedSegments;

        void Awake()
        {
            CreateMaterials();
        }

        public void EnsureParent()
        {
            if (pathsParent == null)
            {
                var go = GameObject.Find("PathsRoot");
                if (go == null) go = new GameObject("PathsRoot");
                pathsParent = go.transform;
            }
        }

        private void CreateMaterials()
        {
            pathMaterial = CreateMat(new Color(0.75f, 0.72f, 0.65f), 0.2f);
            roadMaterial = CreateMat(new Color(0.35f, 0.35f, 0.35f), 0.3f);
            parkingMaterial = CreateMat(new Color(0.30f, 0.30f, 0.32f), 0.3f);
            markingMaterial = CreateMat(Color.white, 0.1f);
        }

        private static Material CreateMat(Color color, float glossiness)
        {
            var mat = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(mat, color);
            MaterialFactory.SetMetallicSmoothness(mat, 0f, glossiness);
            return mat;
        }

        // ─── Komórka siatki (ścieżka/droga) ───

        public GameObject PlaceCell(Vector3 center, float size, PathEdgeType type)
        {
            EnsureParent();

            string name = type == PathEdgeType.Path ? "PathCell" : "RoadCell";
            var cellObj = new GameObject(name);
            cellObj.transform.SetParent(pathsParent, false);
            cellObj.transform.position = new Vector3(center.x, 0.02f, center.z);

            // Powierzchnia
            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Surface";
            surface.transform.SetParent(cellObj.transform, false);
            surface.transform.localPosition = Vector3.zero;
            surface.transform.localRotation = Quaternion.Euler(90, 0, 0);
            surface.transform.localScale = new Vector3(size, size, 1f);
            Object.Destroy(surface.GetComponent<MeshCollider>());

            var renderer = surface.GetComponent<MeshRenderer>();
            renderer.material = type == PathEdgeType.Path ? pathMaterial : roadMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Collider
            var col = cellObj.AddComponent<BoxCollider>();
            col.center = Vector3.zero;
            col.size = new Vector3(size, 0.1f, size);

            // Tag — wymaga zdefiniowania w Unity (Edit > Project Settings > Tags)
            try { cellObj.tag = type == PathEdgeType.Path ? "Path" : "Road"; }
            catch { /* Tag niezdefiniowany — ignoruj */ }

            return cellObj;
        }

        // ─── Ścieżka / Droga (liniowa — legacy) ───

        public PlacedPathSegment PlaceLinearSegment(Vector3 a, Vector3 b, PathEdgeType type, float width, int graphEdgeId)
        {
            EnsureParent();

            Vector3 dir = (b - a).normalized;
            float length = Vector3.Distance(a, b);
            Vector3 center = (a + b) / 2f;
            center.y = 0.02f;

            string name = type == PathEdgeType.Path ? "Path" : "Road";
            var parent = new GameObject($"{name}_{graphEdgeId}");
            parent.transform.SetParent(pathsParent, false);
            parent.transform.position = center;

            float angleY = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            parent.transform.rotation = Quaternion.Euler(0, angleY, 0);

            // Powierzchnia
            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Surface";
            surface.transform.SetParent(parent.transform, false);
            surface.transform.localPosition = Vector3.zero;
            surface.transform.localRotation = Quaternion.Euler(90, 0, 0);
            surface.transform.localScale = new Vector3(width, length, 1f);
            Object.Destroy(surface.GetComponent<MeshCollider>());

            var renderer = surface.GetComponent<MeshRenderer>();
            renderer.material = type == PathEdgeType.Path ? pathMaterial : roadMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Collider
            var col = parent.AddComponent<BoxCollider>();
            col.center = Vector3.zero;
            col.size = new Vector3(width, 0.1f, length);
            parent.tag = type == PathEdgeType.Path ? "Path" : "Road";

            // Oznakowanie drogi (linia przerywana)
            if (type == PathEdgeType.Road)
                GenerateRoadMarkings(length, parent.transform);

            var segment = new PlacedPathSegment
            {
                SegmentObject = parent,
                StartPosition = a,
                EndPosition = b,
                Type = type,
                GraphEdgeId = graphEdgeId,
                Width = width
            };
            placedSegments.Add(segment);
            return segment;
        }

        private void GenerateRoadMarkings(float length, Transform parent)
        {
            float dashLength = 3f;
            float gapLength = 3f;
            float markingWidth = 0.15f;
            float step = dashLength + gapLength;
            float halfLength = length / 2f;

            for (float d = -halfLength + 1f; d < halfLength - 1f; d += step)
            {
                var dash = GameObject.CreatePrimitive(PrimitiveType.Quad);
                dash.name = "Marking";
                dash.transform.SetParent(parent, false);
                dash.transform.localPosition = new Vector3(0f, 0.01f, d + dashLength / 2f);
                dash.transform.localRotation = Quaternion.Euler(90, 0, 0);
                dash.transform.localScale = new Vector3(markingWidth, dashLength, 1f);
                Object.Destroy(dash.GetComponent<MeshCollider>());
                dash.GetComponent<MeshRenderer>().material = markingMaterial;
                dash.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // ─── Parking ───

        public PlacedPathSegment PlaceParkingLot(Vector3 cornerA, Vector3 cornerB, int graphEdgeId)
        {
            EnsureParent();

            float x0 = Mathf.Min(cornerA.x, cornerB.x);
            float x1 = Mathf.Max(cornerA.x, cornerB.x);
            float z0 = Mathf.Min(cornerA.z, cornerB.z);
            float z1 = Mathf.Max(cornerA.z, cornerB.z);

            float width = x1 - x0;
            float depth = z1 - z0;
            Vector3 center = new Vector3((x0 + x1) / 2f, 0.02f, (z0 + z1) / 2f);

            var parent = new GameObject($"Parking_{graphEdgeId}");
            parent.transform.SetParent(pathsParent, false);
            parent.transform.position = center;

            // Powierzchnia
            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Surface";
            surface.transform.SetParent(parent.transform, false);
            surface.transform.localPosition = Vector3.zero;
            surface.transform.localRotation = Quaternion.Euler(90, 0, 0);
            surface.transform.localScale = new Vector3(width, depth, 1f);
            Object.Destroy(surface.GetComponent<MeshCollider>());

            surface.GetComponent<MeshRenderer>().material = parkingMaterial;
            surface.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Collider
            var col = parent.AddComponent<BoxCollider>();
            col.center = Vector3.zero;
            col.size = new Vector3(width, 0.1f, depth);
            parent.tag = "Parking";

            // Linie parkingowe
            GenerateParkingLines(width, depth, parent.transform);

            var segment = new PlacedPathSegment
            {
                SegmentObject = parent,
                StartPosition = cornerA,
                EndPosition = cornerB,
                Type = PathEdgeType.Parking,
                GraphEdgeId = graphEdgeId,
                Width = Mathf.Max(width, depth)
            };
            placedSegments.Add(segment);
            return segment;
        }

        private void GenerateParkingLines(float width, float depth, Transform parent)
        {
            float spaceWidth = 2.5f;
            float lineWidth = 0.1f;
            float halfW = width / 2f;
            float halfD = depth / 2f;

            // Linie prostopadłe do dłuższego boku
            bool horizontal = width >= depth;
            float lineCount = horizontal
                ? Mathf.Floor(width / spaceWidth)
                : Mathf.Floor(depth / spaceWidth);

            for (int i = 0; i <= lineCount; i++)
            {
                float offset = i * spaceWidth;

                var line = GameObject.CreatePrimitive(PrimitiveType.Quad);
                line.name = $"ParkLine_{i}";
                line.transform.SetParent(parent, false);
                line.transform.localRotation = Quaternion.Euler(90, 0, 0);
                Object.Destroy(line.GetComponent<MeshCollider>());
                line.GetComponent<MeshRenderer>().material = markingMaterial;
                line.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                if (horizontal)
                {
                    line.transform.localPosition = new Vector3(-halfW + offset, 0.01f, 0f);
                    line.transform.localScale = new Vector3(lineWidth, depth * 0.8f, 1f);
                }
                else
                {
                    line.transform.localPosition = new Vector3(0f, 0.01f, -halfD + offset);
                    line.transform.localScale = new Vector3(width * 0.8f, lineWidth, 1f);
                }
            }
        }

        // ─── Usuwanie ───

        public void RemoveSegment(int graphEdgeId)
        {
            var seg = placedSegments.Find(s => s.GraphEdgeId == graphEdgeId);
            if (seg == null) return;
            if (seg.SegmentObject != null) Destroy(seg.SegmentObject);
            placedSegments.Remove(seg);
        }

        public void ClearAll()
        {
            EnsureParent();
            for (int i = pathsParent.childCount - 1; i >= 0; i--)
                Destroy(pathsParent.GetChild(i).gameObject);
            placedSegments.Clear();
        }

        public List<PathVisualSegmentSnapshot> GetSnapshot()
        {
            var result = new List<PathVisualSegmentSnapshot>(placedSegments.Count);
            foreach (var seg in placedSegments)
            {
                if (seg == null) continue;
                result.Add(new PathVisualSegmentSnapshot
                {
                    startPosition = seg.StartPosition,
                    endPosition = seg.EndPosition,
                    type = seg.Type,
                    graphEdgeId = seg.GraphEdgeId,
                    width = seg.Width
                });
            }
            return result;
        }

        public void RestoreFromSave(IList<PathVisualSegmentSnapshot> snapshots)
        {
            ClearAll();
            if (snapshots == null) return;

            foreach (var snap in snapshots)
            {
                if (snap == null) continue;
                if (snap.type == PathEdgeType.Parking)
                    PlaceParkingLot(snap.startPosition, snap.endPosition, snap.graphEdgeId);
                else
                    PlaceLinearSegment(snap.startPosition, snap.endPosition, snap.type, snap.width > 0f ? snap.width : 2f, snap.graphEdgeId);
            }
        }
    }

    [System.Serializable]
    public class PathVisualSegmentSnapshot
    {
        public Vector3 startPosition;
        public Vector3 endPosition;
        public PathEdgeType type;
        public int graphEdgeId = -1;
        public float width;
    }
}
