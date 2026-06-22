using System.Collections.Generic;
using UnityEngine;
using formap;
using RailwayManager.Core;

namespace MapSystem
{
    public partial class MapRenderer
    {
        // ═══════════════════════════════════════════
        //  LAYER RENDERING — material/height/queue config + batching + meshes
        // ═══════════════════════════════════════════

        /// <summary>
        /// Checks if a layer should be rendered based on visibility flags
        /// </summary>
        private bool ShouldRenderLayer(BinaryFormat.LayerType type)
        {
            return type switch
            {
                BinaryFormat.LayerType.Highways => renderHighways,
                BinaryFormat.LayerType.Railways => renderRailways,
                BinaryFormat.LayerType.Buildings => renderBuildings,
                BinaryFormat.LayerType.Water => renderWater,
                BinaryFormat.LayerType.Waterways => renderWaterways,
                BinaryFormat.LayerType.Industrial => renderIndustrial,
                BinaryFormat.LayerType.Military => renderMilitary,
                BinaryFormat.LayerType.Platforms => renderPlatforms,
                BinaryFormat.LayerType.Forests => renderForests,
                BinaryFormat.LayerType.POIs => renderPOIs,
                BinaryFormat.LayerType.Places => renderPOIs, // Places share renderPOIs toggle
                _ => false
            };
        }

        /// <summary>
        /// Creates a parent GameObject for a layer
        /// </summary>
        private GameObject CreateLayerObject(BinaryFormat.LayerType type)
        {
            var obj = new GameObject($"Layer_{type}");
            obj.transform.SetParent(transform);
            obj.transform.localPosition = Vector3.zero;
            return obj;
        }

        /// <summary>
        /// Renders a single layer with batching
        /// </summary>
        private (int meshCount, int vertexCount, int triangleCount, int featuresWithHoles) RenderLayer(
            GameObject layerObj, BinaryFormat.LayerType type, List<MeshGeometry> features)
        {
            Material material = GetMaterialForLayer(type);
            if (material == null)
            {
                Log.Warn($"[MapRenderer] No material assigned for layer {type}");
                return (0, 0, 0, 0);
            }

            // Get or create cached material with render queue + ZWrite settings
            if (!layerMaterialCache.TryGetValue(type, out var cachedMat))
            {
                cachedMat = new Material(material);
                cachedMat.renderQueue = GetLayerRenderQueue(type);

                // Disable ZWrite on layers that overlap themselves (roads at intersections,
                // waterway segments). Render queue alone determines draw order.
                if (type == BinaryFormat.LayerType.Highways ||
                    type == BinaryFormat.LayerType.Waterways ||
                    type == BinaryFormat.LayerType.Railways)
                {
                    cachedMat.SetInt("_ZWrite", 0);
                }

                layerMaterialCache[type] = cachedMat;
            }
            material = cachedMat;

            bool isLine = IsLineLayer(type);
            bool isPoint = IsPointLayer(type);
            bool isHighway = (type == BinaryFormat.LayerType.Highways);
            float height = GetLayerHeight(type);

            // Log.Info($"[MapRenderer] Rendering layer {type}: {features.Count} features at Y={height}");

            // Special handling for POIs (points)
            if (isPoint)
            {
                var stats = RenderPOILayer(layerObj, material, features, height);
                return (stats.meshCount, stats.vertexCount, stats.triangleCount, 0);
            }

            // Standard rendering
            // Highways and Waterways have correct winding from converter (triangle strips), other polygons need reversal
            bool isWaterway = (type == BinaryFormat.LayerType.Waterways);
            bool reverseWinding = !isLine && !isHighway && !isWaterway;
            int meshCount = 0;

            // Special handling dla Railways: rozdziel na 3 grupy po metadata railway tag
            // - mainline (rail/light_rail/preserved/etc) → railwayMaterial, zawsze widoczne
            // - disused/abandoned → disusedRailwayMaterial (szare), zawsze widoczne
            // - tram/subway/monorail → railwayMaterial, tylko od LOD2+
            if (type == BinaryFormat.LayerType.Railways)
            {
                var mainline = new List<MeshGeometry>();
                var disused = new List<MeshGeometry>();
                var transitOnly = new List<MeshGeometry>(); // tram/subway/monorail
                // formap już filtruje disused tram/narrow_gauge (skip w IsRailway),
                // więc w bin zostają: mainline rail (czarny) + disused mainline rail (szary) + active tram/narrow_gauge (LOD≤2).
                foreach (var f in features)
                {
                    string rt = null;
                    if (f.Metadata != null) f.Metadata.TryGetValue("railway", out rt);
                    if (rt == "disused" || rt == "abandoned") disused.Add(f);
                    else if (rt == "tram" || rt == "subway" || rt == "monorail"
                          || rt == "narrow_gauge" || rt == "light_rail") transitOnly.Add(f);
                    else mainline.Add(f);
                }

                int curLOD = lastLODLevel >= 0 ? lastLODLevel : 0;
                bool showTransit = curLOD <= 2; // tylko bliskie LOD-y (0,1,2)

                int totalV = 0, totalT = 0;
                if (mainline.Count > 0)
                {
                    var s = RenderFeatureBatch(layerObj, material, mainline, height, isLine, reverseWinding, ref meshCount);
                    totalV += s.vertices; totalT += s.triangles;
                }
                if (disused.Count > 0)
                {
                    var disusedMat = GetDisusedRailwayMaterial(material);
                    var s = RenderFeatureBatch(layerObj, disusedMat, disused, height, isLine, reverseWinding, ref meshCount);
                    totalV += s.vertices; totalT += s.triangles;
                }
                if (transitOnly.Count > 0 && showTransit)
                {
                    var s = RenderFeatureBatch(layerObj, material, transitOnly, height, isLine, reverseWinding, ref meshCount);
                    totalV += s.vertices; totalT += s.triangles;
                }
                return (meshCount, totalV, totalT, 0);
            }

            var batchStats = RenderFeatureBatch(layerObj, material, features, height, isLine, reverseWinding, ref meshCount);
            return (meshCount, batchStats.vertices, batchStats.triangles, batchStats.featuresWithHoles);
        }

        /// <summary>
        /// Lazy-cached materiał dla disused/abandoned railways. Bierze <see cref="disusedRailwayMaterial"/>
        /// z Inspectora, inaczej klonuje railwayMaterial i ustawia szarą półprzezroczystą color.
        /// </summary>
        private Material GetDisusedRailwayMaterial(Material railwayMat)
        {
            if (_cachedDisusedRailwayMaterial != null)
                return _cachedDisusedRailwayMaterial;

            Material m;
            if (disusedRailwayMaterial != null)
            {
                m = new Material(disusedRailwayMaterial);
            }
            else
            {
                // Auto-default: klon railwayMaterial z szarą color (opacity 0.6)
                m = new Material(railwayMat);
                var grey = new Color(0.5f, 0.5f, 0.5f, 0.6f);
                if (m.HasProperty("_Color")) m.SetColor("_Color", grey);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", grey);
                m.color = grey;
            }
            m.renderQueue = GetLayerRenderQueue(BinaryFormat.LayerType.Railways);
            m.SetInt("_ZWrite", 0);
            _cachedDisusedRailwayMaterial = m;
            return _cachedDisusedRailwayMaterial;
        }

        /// <summary>
        /// Renders a batch of features with the same geometry type (all lines or all triangles).
        /// Wszystkie features layer'a trafiają do jednego mesh — split na 60k vertex usunięty
        /// (Mesh.indexFormat = UInt32 w CreateMeshObject eliminuje stary UInt16 limit 65535).
        /// </summary>
        private (int vertices, int triangles, int featuresWithHoles) RenderFeatureBatch(
            GameObject layerObj, Material material, List<MeshGeometry> features,
            float height, bool isLine, bool reverseWinding, ref int meshCount)
        {
            List<Vector3> currentVertices = new();
            List<int> currentIndices = new();
            int featuresWithHoles = 0;

            foreach (var feature in features)
            {
                if (feature.HoleStarts.Count > 0)
                    featuresWithHoles++;

                AddFeatureToBatch(currentVertices, currentIndices, feature, height);
            }

            if (currentVertices.Count == 0)
                return (0, 0, featuresWithHoles);

            var stats = FlushBatchToMesh(layerObj, material, currentVertices, currentIndices,
                                        reverseWinding, isLine, meshCount++);
            return (stats.vertices, stats.triangles, featuresWithHoles);
        }

        /// <summary>
        /// Adds a feature's geometry to the current batch
        /// </summary>
        private void AddFeatureToBatch(List<Vector3> vertices, List<int> indices,
                                      MeshGeometry feature, float height)
        {
            int vertexOffset = vertices.Count;

            foreach (var v in feature.Vertices)
            {
                // Map 2D coordinates to 3D: X → X, Y → Z, height → Y
                vertices.Add(new Vector3(v.x, height, v.y));
            }

            foreach (var idx in feature.Indices)
            {
                indices.Add(vertexOffset + idx);
            }
        }

        /// <summary>
        /// Flushes the current batch to a mesh and clears the batch
        /// </summary>
        private (int vertices, int triangles) FlushBatchToMesh(
            GameObject layerObj, Material material, List<Vector3> vertices, List<int> indices,
            bool reverseWinding, bool isLine, int meshIndex)
        {
            if (reverseWinding)
            {
                ReverseTriangleWindingOrder(indices);
            }

            var stats = CreateMeshObject(layerObj, material, vertices, indices, meshIndex, isLine);

            vertices.Clear();
            indices.Clear();

            return stats;
        }

        /// <summary>
        /// Creates a Unity mesh object from vertices and indices
        /// </summary>
        private (int vertices, int triangles) CreateMeshObject(
            GameObject parent, Material material,
            List<Vector3> vertices, List<int> indices,
            int meshIndex, bool isLine)
        {
            var meshObj = new GameObject($"Mesh_{meshIndex}");
            meshObj.transform.SetParent(parent.transform);
            meshObj.transform.localPosition = Vector3.zero;
            meshObj.layer = gameObject.layer;

            var meshFilter = meshObj.AddComponent<MeshFilter>();
            var meshRenderer = meshObj.AddComponent<MeshRenderer>();

            var mesh = new Mesh();
            mesh.name = $"{parent.name}_Mesh_{meshIndex}";

            // UInt32 index format eliminuje stary UInt16 limit (65535 vertices per mesh).
            // Bez tego dense tile Warszawa/Kraków Buildings layer wymagał split na ~5 osobnych
            // mesh GO per layer per tile (~45k GO total na Polskę). Single mesh per layer = ~2.5k.
            // MUSI być przed SetVertices — Unity rzuca error gdy >65k vertices i indexFormat=UInt16.
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            // Set vertices
            mesh.SetVertices(vertices);

            // Set indices with appropriate topology
            mesh.SetIndices(indices,
                isLine ? MeshTopology.Lines : MeshTopology.Triangles,
                0);

            // Calculate normals for polygons
            if (!isLine)
            {
                mesh.RecalculateNormals();
            }

            mesh.RecalculateBounds();

            meshFilter.mesh = mesh;
            meshRenderer.material = material;

            // Disable shadow casting for 2D map
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            int triangleCount = isLine ? indices.Count / 2 : indices.Count / 3;
            return (vertices.Count, triangleCount);
        }

        /// <summary>
        /// Gets the material for a specific layer type
        /// </summary>
        public Material GetMaterialForLayer(BinaryFormat.LayerType type)
        {
            return type switch
            {
                BinaryFormat.LayerType.Highways => highwayMaterial,
                BinaryFormat.LayerType.Railways => railwayMaterial,
                BinaryFormat.LayerType.Buildings => buildingMaterial,
                BinaryFormat.LayerType.Water => waterMaterial,
                BinaryFormat.LayerType.Waterways => waterwayMaterial ?? waterMaterial, // Fallback to water material
                BinaryFormat.LayerType.Industrial => industrialMaterial,
                BinaryFormat.LayerType.Military => militaryMaterial,
                BinaryFormat.LayerType.Platforms => platformMaterial,
                BinaryFormat.LayerType.Forests => forestMaterial,
                BinaryFormat.LayerType.POIs => poiMaterial,
                BinaryFormat.LayerType.Places => poiMaterial, // Places reuse POI material (text labels)
                _ => null
            };
        }

        /// <summary>
        /// Determines if a layer should be rendered as lines
        /// Railways and Waterways are rendered as lines (actually triangle strips)
        /// </summary>
        public bool IsLineLayer(BinaryFormat.LayerType type)
        {
            // Highways, waterways are generated as triangle strips
            // They should NOT use Lines topology
            return type == BinaryFormat.LayerType.Railways;
        }

        /// <summary>
        /// Determines if a layer consists of points (POIs)
        /// </summary>
        private bool IsPointLayer(BinaryFormat.LayerType type)
        {
            return type == BinaryFormat.LayerType.POIs
                || type == BinaryFormat.LayerType.Places;
        }

        /// <summary>
        /// Gets render queue for layer — higher = rendered later (on top)
        /// </summary>
        private int GetLayerRenderQueue(BinaryFormat.LayerType type)
        {
            return type switch
            {
                BinaryFormat.LayerType.Water => 2000,
                BinaryFormat.LayerType.Forests => 2001,
                BinaryFormat.LayerType.Industrial => 2002,
                BinaryFormat.LayerType.Military => 2003,
                BinaryFormat.LayerType.Buildings => 2004,
                BinaryFormat.LayerType.Platforms => 2005,
                BinaryFormat.LayerType.Waterways => 2006,
                BinaryFormat.LayerType.Highways => 2007,
                BinaryFormat.LayerType.Railways => 2008,
                BinaryFormat.LayerType.POIs => 2009,
                BinaryFormat.LayerType.Places => 2009,
                _ => 2000
            };
        }

        /// <summary>
        /// Gets the Y-height for a layer (prevents Z-fighting)
        /// </summary>
        public float GetLayerHeight(BinaryFormat.LayerType type)
        {
            return type switch
            {
                BinaryFormat.LayerType.Water => waterHeight,
                BinaryFormat.LayerType.Waterways => waterwayHeight,
                BinaryFormat.LayerType.Forests => forestHeight,
                BinaryFormat.LayerType.Buildings => buildingHeight,
                BinaryFormat.LayerType.Industrial => industrialHeight,
                BinaryFormat.LayerType.Military => militaryHeight,
                BinaryFormat.LayerType.Platforms => platformHeight,
                BinaryFormat.LayerType.Highways => highwayHeight,
                BinaryFormat.LayerType.Railways => railwayHeight,
                BinaryFormat.LayerType.POIs => poiHeight,
                BinaryFormat.LayerType.Places => poiHeight,
                _ => 0f
            };
        }

        /// <summary>
        /// Reverses triangle winding order to fix backface culling
        /// Swaps first and third vertex of each triangle
        /// </summary>
        private void ReverseTriangleWindingOrder(List<int> indices)
        {
            // Process triangles (groups of 3 indices)
            for (int i = 0; i < indices.Count; i += 3)
            {
                if (i + 2 < indices.Count)
                {
                    // Swap first and last vertex of triangle
                    // Changes clockwise to counter-clockwise (or vice versa)
                    int temp = indices[i];
                    indices[i] = indices[i + 2];
                    indices[i + 2] = temp;
                }
            }
        }
    }
}
