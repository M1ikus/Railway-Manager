using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    /// <summary>
    /// System wizualizacji snap pointów na końcach torów.
    /// Endpoint: cyan sfera (1-2 krawędzi)
    /// Junction: żółta sfera + Y-shape z LineRendererów (3+ krawędzi)
    /// Widoczne TYLKO w trybie BuildTrack.
    /// </summary>
    public class SnapPointSystem : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TrackGraph trackGraph;

        [Header("Snap Visual Settings")]
        [Tooltip("Promień sfery snap pointu")]
        public float markerRadius = 0.4f;

        [Tooltip("Wysokość markera nad ziemią")]
        public float markerHeight = 0.25f;

        [Tooltip("Skala highlight (hover)")]
        public float highlightScale = 1.3f;

        [Tooltip("Długość ramion Y-shape junction")]
        public float junctionArmLength = 3f;

        [Tooltip("Szerokość linii Y-shape")]
        public float junctionLineWidth = 0.15f;

        [Header("Colors")]
        public Color endpointColor = Color.cyan;
        public Color junctionColor = Color.yellow;
        public Color highlightColor = Color.green;

        private Transform snapParent;
        private Dictionary<int, SnapMarker> markers = new();
        private Material endpointMaterial;
        private Material junctionMaterial;
        private Material highlightMaterial;
        private Material junctionLineMaterial;

        private int highlightedNodeId = -1;
        private bool isVisible = false;

        // ── Spatial grid (TD-009: O(1) lookup zamiast O(N) skan trackGraph.Nodes) ──
        // Chunk-based 50×50m bins (płaszczyzna XZ). Każdy chunk zawiera listę nodeIds
        // których Position wpada w jego prostokąt. FindNearestSnapPoint iteruje tylko
        // chunki w obrębie maxDistance/chunkSize promieniu zamiast pełnego grafu.
        // Rebuild przy każdej zmianie topologii (OnTopologyChanged) — koszt amortyzowany.
        private const float GridChunkSize = 50f;
        private readonly Dictionary<Vector2Int, List<int>> _spatialGrid = new();

        /// <summary>Aktualnie podświetlony node (hover)</summary>
        public int HighlightedNodeId => highlightedNodeId;

        void Awake()
        {
            snapParent = new GameObject("SnapPoints").transform;
            snapParent.SetParent(transform);
            CreateMaterials();
        }

        void Start()
        {
            if (trackGraph == null) trackGraph = DepotServices.Get<TrackGraph>();
            if (trackGraph != null)
            {
                trackGraph.OnTopologyChanged += RefreshAllSnapPoints;
                // Grid musi być gotowy przed pierwszym FindNearestSnapPoint — SchemaSnapDetector
                // pyta nawet gdy markery nie są visible. Build raz na starcie, rebuild na topo change.
                RebuildSpatialGrid();
            }

            // Ukryj na start
            SetVisible(false);
        }

        void OnDestroy()
        {
            if (trackGraph != null)
                trackGraph.OnTopologyChanged -= RefreshAllSnapPoints;
        }

        void Update()
        {
            // Widoczność zależy od trybu narzędzia
            bool shouldBeVisible = DepotUIManager.Instance != null &&
                                   DepotUIManager.Instance.CurrentTool == ToolMode.BuildTrack;

            if (shouldBeVisible != isVisible)
                SetVisible(shouldBeVisible);
        }

        private void CreateMaterials()
        {
            endpointMaterial = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(endpointMaterial, endpointColor);
            MaterialFactory.SetMetallicSmoothness(endpointMaterial, 0.3f, 0.7f);
            // Emission dla lepszej widoczności
            MaterialFactory.SetEmission(endpointMaterial, endpointColor * 0.3f);

            junctionMaterial = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(junctionMaterial, junctionColor);
            MaterialFactory.SetMetallicSmoothness(junctionMaterial, 0.3f, 0.7f);
            MaterialFactory.SetEmission(junctionMaterial, junctionColor * 0.3f);

            highlightMaterial = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(highlightMaterial, highlightColor);
            MaterialFactory.SetMetallicSmoothness(highlightMaterial, 0.3f, 0.8f);
            MaterialFactory.SetEmission(highlightMaterial, highlightColor * 0.5f);

            junctionLineMaterial = MaterialFactory.CreateLine();
            MaterialFactory.SetBaseColor(junctionLineMaterial, junctionColor);
        }

        // ═══════════════════════════════════════════
        //  SNAP DETECTION
        // ═══════════════════════════════════════════

        /// <summary>
        /// Szuka najbliższego snap pointu do podanej pozycji.
        /// Zwraca (nodeId, nodePosition) lub (-1, Vector3.zero).
        ///
        /// TD-009: spatial grid lookup — iteruje tylko chunki w obrębie maxDistance,
        /// nie pełen graf. Filter (EdgeIds.Count, Type) wykonywany per-node w trakcie iteracji
        /// — grid trzyma wszystkie nodes, bo filter zmienia się gdy node dostaje nowe krawędzie.
        /// </summary>
        public (int nodeId, Vector3 position) FindNearestSnapPoint(Vector3 worldPosition, float maxDistance = 3f)
        {
            if (trackGraph == null) return (-1, Vector3.zero);

            int nearest = -1;
            float minDist = maxDistance;
            Vector3 nearestPos = Vector3.zero;

            // Sąsiednie chunki w obrębie maxDistance. Dla maxDistance=3f i chunk=50f → radius=1
            // (= 3×3 = 9 chunków). Dla maxDistance=20f → radius=1 (wciąż 9). Dla =80f → radius=2.
            int chunkRadius = Mathf.CeilToInt(maxDistance / GridChunkSize);
            Vector2Int centerChunk = ChunkKey(worldPosition);

            for (int dx = -chunkRadius; dx <= chunkRadius; dx++)
            {
                for (int dz = -chunkRadius; dz <= chunkRadius; dz++)
                {
                    var key = new Vector2Int(centerChunk.x + dx, centerChunk.y + dz);
                    if (!_spatialGrid.TryGetValue(key, out var nodeIds)) continue;

                    foreach (int nodeId in nodeIds)
                    {
                        if (!trackGraph.Nodes.TryGetValue(nodeId, out var node)) continue;
                        if (node.EdgeIds.Count == 0) continue; // Pomiń izolowane node'y
                        if (node.Type == NodeType.Throughput) continue; // Pomiń w pełni połączone (gładkie przejście)
                        if (node.Type == NodeType.Junction && node.EdgeIds.Count >= 3) continue; // Pomiń pełne rozjazdy (3+ krawędzi)

                        float dist = Vector3.Distance(worldPosition, node.Position);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            nearest = node.Id;
                            nearestPos = node.Position;
                        }
                    }
                }
            }

            return (nearest, nearestPos);
        }

        private static Vector2Int ChunkKey(Vector3 pos)
        {
            return new Vector2Int(
                Mathf.FloorToInt(pos.x / GridChunkSize),
                Mathf.FloorToInt(pos.z / GridChunkSize));
        }

        /// <summary>
        /// Rebuild spatial grid z aktualnego stanu trackGraph.Nodes. Wywoływane przy
        /// topology change (event). Koszt O(N) ale rzadko (= per edycja toru, nie per frame).
        /// </summary>
        private void RebuildSpatialGrid()
        {
            _spatialGrid.Clear();
            if (trackGraph == null) return;

            foreach (var node in trackGraph.Nodes.Values)
            {
                var key = ChunkKey(node.Position);
                if (!_spatialGrid.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    _spatialGrid[key] = list;
                }
                list.Add(node.Id);
            }
        }

        /// <summary>
        /// Podświetla node (hover). Przekaż -1 aby wyłączyć highlight.
        /// </summary>
        public void SetHighlight(int nodeId)
        {
            if (nodeId == highlightedNodeId) return;

            // Wyłącz stary highlight
            if (highlightedNodeId >= 0 && markers.ContainsKey(highlightedNodeId))
            {
                var old = markers[highlightedNodeId];
                if (old.Sphere != null)
                {
                    old.Sphere.GetComponent<MeshRenderer>().material =
                        old.IsJunction ? junctionMaterial : endpointMaterial;
                    old.Sphere.transform.localScale = Vector3.one * markerRadius * 2f;
                }
            }

            highlightedNodeId = nodeId;

            // Włącz nowy highlight
            if (nodeId >= 0 && markers.ContainsKey(nodeId))
            {
                var marker = markers[nodeId];
                if (marker.Sphere != null)
                {
                    marker.Sphere.GetComponent<MeshRenderer>().material = highlightMaterial;
                    marker.Sphere.transform.localScale = Vector3.one * markerRadius * 2f * highlightScale;
                }
            }
        }

        // ═══════════════════════════════════════════
        //  GENEROWANIE MARKERÓW
        // ═══════════════════════════════════════════

        /// <summary>
        /// Odświeża wszystkie snap pointy. Wywoływane po zmianie topologii.
        /// </summary>
        public void RefreshAllSnapPoints()
        {
            // TD-009: rebuild spatial grid przed wizualami — FindNearestSnapPoint
            // może być wywoływane między topology change a SetVisible(true).
            RebuildSpatialGrid();

            // Wyczyść stare
            ClearAllMarkers();

            if (trackGraph == null) return;

            foreach (var node in trackGraph.Nodes.Values)
            {
                if (node.EdgeIds.Count == 0) continue;

                // Throughput = gładkie przejście, nie pokazuj snap pointa
                if (node.Type == NodeType.Throughput) continue;

                // Pełne rozjazdy (3+ krawędzi) — pokaż marker info, ale nie snap
                if (node.Type == NodeType.Junction && node.EdgeIds.Count >= 3)
                {
                    CreateMarker(node.Id, node.Position, true);
                    continue;
                }

                bool isJunction = node.Type == NodeType.Junction;
                CreateMarker(node.Id, node.Position, isJunction);
            }

            // Zachowaj widoczność
            SetMarkersActive(isVisible);
        }

        private void CreateMarker(int nodeId, Vector3 position, bool isJunction)
        {
            GameObject markerObj = new GameObject($"Snap_{nodeId}");
            markerObj.transform.SetParent(snapParent);
            markerObj.transform.position = position + Vector3.up * markerHeight;

            // Sfera
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "SnapSphere";
            sphere.transform.SetParent(markerObj.transform, false);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localScale = Vector3.one * markerRadius * 2f;
            sphere.GetComponent<MeshRenderer>().material = isJunction ? junctionMaterial : endpointMaterial;
            // Wyłącz collider sfery (nie chcemy blokować raycastów)
            Destroy(sphere.GetComponent<SphereCollider>());

            // Y-shape dla junction
            GameObject yShape = null;
            if (isJunction && trackGraph != null && trackGraph.Nodes.ContainsKey(nodeId))
            {
                yShape = CreateJunctionYShape(markerObj.transform, nodeId);
            }

            markers[nodeId] = new SnapMarker
            {
                NodeId = nodeId,
                Root = markerObj,
                Sphere = sphere,
                YShape = yShape,
                IsJunction = isJunction
            };
        }

        /// <summary>
        /// Tworzy Y-shape z LineRendererów dla rozjazdu (junction).
        /// </summary>
        private GameObject CreateJunctionYShape(Transform parent, int nodeId)
        {
            var node = trackGraph.Nodes[nodeId];
            if (node.EdgeIds.Count < 3) return null;

            GameObject yShape = new GameObject("YShape");
            yShape.transform.SetParent(parent, false);
            yShape.transform.localPosition = Vector3.zero;

            foreach (int edgeId in node.EdgeIds)
            {
                if (!trackGraph.Edges.ContainsKey(edgeId)) continue;
                var edge = trackGraph.Edges[edgeId];

                // Kierunek od node'a do drugiego końca
                int otherNode = edge.FromNodeId == nodeId ? edge.ToNodeId : edge.FromNodeId;
                if (!trackGraph.Nodes.ContainsKey(otherNode)) continue;

                Vector3 direction;
                if (edge.Polyline != null && edge.Polyline.Count >= 2)
                {
                    if (edge.FromNodeId == nodeId)
                        direction = TrackGeometry.GetStartTangent(edge.Polyline);
                    else
                        direction = -TrackGeometry.GetEndTangent(edge.Polyline);
                }
                else
                {
                    direction = (trackGraph.Nodes[otherNode].Position - node.Position).normalized;
                }

                // LineRenderer: od centrum do junctionArmLength w kierunku
                GameObject arm = new GameObject($"Arm_{edgeId}");
                arm.transform.SetParent(yShape.transform, false);

                LineRenderer lr = arm.AddComponent<LineRenderer>();
                lr.material = junctionLineMaterial;
                lr.startColor = junctionColor;
                lr.endColor = junctionColor;
                lr.startWidth = junctionLineWidth;
                lr.endWidth = junctionLineWidth * 0.5f;
                lr.positionCount = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                Vector3 armStart = node.Position + Vector3.up * markerHeight;
                Vector3 armEnd = armStart + direction * junctionArmLength;
                lr.SetPosition(0, armStart);
                lr.SetPosition(1, armEnd);
            }

            return yShape;
        }

        // ═══════════════════════════════════════════
        //  WIDOCZNOŚĆ
        // ═══════════════════════════════════════════

        public void SetVisible(bool visible)
        {
            isVisible = visible;
            SetMarkersActive(visible);

            // Odśwież markery gdy stają się widoczne
            if (visible)
                RefreshAllSnapPoints();
        }

        private void SetMarkersActive(bool active)
        {
            if (snapParent != null)
                snapParent.gameObject.SetActive(active);
        }

        private void ClearAllMarkers()
        {
            foreach (var marker in markers.Values)
            {
                if (marker.Root != null) Destroy(marker.Root);
            }
            markers.Clear();
            highlightedNodeId = -1;
        }

        private class SnapMarker
        {
            public int NodeId;
            public GameObject Root;
            public GameObject Sphere;
            public GameObject YShape;
            public bool IsJunction;
        }
    }
}
