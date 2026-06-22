using System.Collections.Generic;
using UnityEngine;
using formap;
using RailwayManager.Core;

namespace MapSystem
{
    public partial class MapRenderer
    {
        // ═══════════════════════════════════════════
        //  TILED RENDERING (v6/v7) — per-tile lifecycle
        // ═══════════════════════════════════════════

        /// <summary>
        /// Sets whether renderer should use tiled mode
        /// </summary>
        public void SetTiledMode(bool enabled)
        {
            isTiledMode = enabled;
            if (showDebugInfo)
            {
                Log.Info($"[MapRenderer] Tiled mode: {(enabled ? "ENABLED" : "DISABLED")}");
            }
        }

        /// <summary>
        /// Renders a single tile (for ChunkLoadingManager compatibility)
        /// </summary>
        public GameObject RenderTile(formap.TileData tileData)
        {
            if (tileData == null || tileData.Layers == null)
            {
                Log.Warn($"[MapRenderer] Cannot render tile {tileData?.TileID} - no data");
                return null;
            }

            // Create container for this tile (inherit layer from parent for scene separation)
            var tileContainer = new GameObject($"Tile_{tileData.GridX}_{tileData.GridY}");
            tileContainer.transform.SetParent(transform);
            tileContainer.transform.localPosition = Vector3.zero;
            tileContainer.layer = gameObject.layer;

            // Track tile container for visibility management
            tileContainers[tileData.TileID] = tileContainer;

            // Create layer render objects for this tile
            var tileLayerObjects = new Dictionary<BinaryFormat.LayerType, GameObject>();

            int totalMeshes = 0;
            int totalVertices = 0;
            int totalTriangles = 0;

            foreach (var kvp in tileData.Layers)
            {
                if (!ShouldRenderLayer(kvp.Key))
                    continue;

                var layerObj = new GameObject($"Layer_{kvp.Key}");
                layerObj.transform.SetParent(tileContainer.transform);
                layerObj.transform.localPosition = Vector3.zero;

                // Apply current LOD visibility
                layerObj.SetActive(IsLayerVisibleAtLOD(kvp.Key, lastLODLevel >= 0 ? lastLODLevel : 0));

                var stats = RenderLayer(layerObj, kvp.Key, kvp.Value);

                totalMeshes += stats.meshCount;
                totalVertices += stats.vertexCount;
                totalTriangles += stats.triangleCount;

                tileLayerObjects[kvp.Key] = layerObj;
            }

            tileRenderObjects[tileData.TileID] = tileLayerObjects;

            // Set layer on entire tile hierarchy (for scene separation culling)
            SetLayerRecursive(tileContainer, gameObject.layer);

            if (showDebugInfo)
            {
                Log.Info($"[MapRenderer] Rendered tile ({tileData.GridX}, {tileData.GridY}): " +
                         $"{totalMeshes} meshes, {totalVertices} vertices");
            }

            return tileContainer;
        }

        /// <summary>
        /// Renders a single tile (for TileManager compatibility)
        /// </summary>
        public void RenderTile(long tileID, formap.TileData tileData)
        {
            RenderTile(tileData);
        }

        /// <summary>
        /// Włącza/wyłącza widoczność tile'a (tańsze niż destroy/rebuild)
        /// </summary>
        public void SetTileVisible(long tileID, bool visible)
        {
            if (tileContainers.TryGetValue(tileID, out var container) && container != null)
            {
                container.SetActive(visible);
            }
        }

        /// <summary>
        /// Unloads/destroys a rendered tile
        /// </summary>
        public void UnloadTile(long tileID)
        {
            // Mesh instances utworzone przez RenderTile (new Mesh() per FlushBatchToMesh)
            // nie są asset'ami z AssetDatabase — Destroy(mesh) async wystarcza, zwalnia
            // GPU memory przy następnym GC pass. DestroyImmediate w runtime jest wprost
            // odradzane przez Unity (potencjalne crashes/leaks) — async safe path.
            // Bez explicit destroy runtime Mesh-y wyciekają przy każdym tile unload / LOD switch.
            //
            // ALE: station icon fallback (pkt 11) używa shared `_cachedFallbackCubeMesh` z
            // Resources.GetBuiltinResource<Mesh>("Cube.fbx") — to Unity BUILTIN ASSET.
            // Destroy() na asset rzuca "Destroying assets is not permitted to avoid data loss".
            // Plus to shared resource — destroyowanie zniszczyłoby je dla wszystkich stacji.
            // Skip filter: pomijaj jeśli sharedMesh == cached builtin.
            if (tileContainers.TryGetValue(tileID, out var container) && container != null)
            {
                var meshFilters = container.GetComponentsInChildren<MeshFilter>(true);
                foreach (var mf in meshFilters)
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null) continue;
                    if (mesh == _cachedFallbackCubeMesh) continue; // shared builtin asset, NIE niszczyć
                    Destroy(mesh);
                }

                Destroy(container);
            }
            tileContainers.Remove(tileID);
            tileRenderObjects.Remove(tileID);

            if (showDebugInfo)
                Log.Info($"[MapRenderer] Unloaded tile {tileID}");
        }

        /// <summary>
        /// Clears all rendered tiles
        /// </summary>
        public void ClearAllTiles()
        {
            // Snapshot kluczy do listy zanim modyfikujemy Dictionary w pętli (UnloadTile.Remove).
            // new List<long>(...) zamiast .Keys.ToList() — bez LINQ extension allocation.
            var keys = new List<long>(tileRenderObjects.Keys);
            foreach (var tileID in keys)
                UnloadTile(tileID);
            tileRenderObjects.Clear();
        }

        /// <summary>
        /// Sets layer on GameObject and all its children recursively
        /// </summary>
        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }
    }
}
