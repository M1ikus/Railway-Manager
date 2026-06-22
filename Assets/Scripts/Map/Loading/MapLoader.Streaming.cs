using System.Collections.Generic;
using UnityEngine;
using formap;
using RailwayManager.Core;

namespace MapSystem
{
    public partial class MapLoader
    {
        // ═══════════════════════════════════════════
        //  BULK / STREAMING — used by TimetableInitializer extraction
        // ═══════════════════════════════════════════

        /// <summary>
        /// Synchronously loads every tile in the tile index into memory. Used by debug tools
        /// before bulk feature aggregation (AdminBoundaryLoader, pathfinding graph build, etc.).
        /// Expensive — reads entire binary file, may take 10-30s on full Poland map.
        /// Returns number of tiles loaded.
        ///
        /// forceReloadAll=true: wymuś pełne przeładowanie nawet już-loaded tile (gwarantuje
        /// spójny LOD level dla wszystkich). Używane przed bulk data aggregation żeby
        /// uniknąć mix LOD0/LOD5 data z camera-frustum pre-loadów.
        /// </summary>
        public int EnsureAllTilesLoadedSync(bool forceReloadAll = false)
        {
            if (!isV7Format && version != 6)
            {
                Log.Info("[MapLoader] EnsureAllTilesLoadedSync: v5 format, skipping (all data already in legacy layers)");
                return 0;
            }
            if (tileIndex.Count == 0)
            {
                Log.Warn("[MapLoader] EnsureAllTilesLoadedSync: no tile index (map not loaded?)");
                return 0;
            }
            if (tileManager == null)
            {
                Log.Warn("[MapLoader] EnsureAllTilesLoadedSync: no TileManager assigned");
                return 0;
            }

            int total = LoadAllTilesSyncImpl(forceReloadAll);
            return total;
        }

        /// <summary>
        /// **Streaming extraction (SYNCHRONOUS)** — iteruje tile po tile, parsuje TYLKO logic layers
        /// (Railways/AdminBoundaries/Places/POIs/Platforms), wywołuje <paramref name="processor"/>
        /// dla każdego, tile data goes out of scope (no cache, no MarkTileLoaded, no events,
        /// no rendering side effects). Memory constant ~kilka MB.
        ///
        /// Główny production path dla <c>TimetableInitializer</c>. Blokuje main thread 30-60s
        /// na pełnej Polsce — Editor "not responding" modal może się pojawić → Wait.
        /// Coroutine wariant z yieldami próbowano w M-PL ale powodował Editor degradation
        /// (yield → asset refresh tick → coraz dłuższy hang). Sync prostszy + predictable.
        /// </summary>
        public void StreamAllTilesSync(
            System.Action<long, Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>> processor,
            int maxTilesToProcess = -1)
        {
            if (!isV7Format && version != 6) { Log.Warn("[MapLoader] StreamSync: v5 format not supported"); return; }
            if (tileIndex.Count == 0) { Log.Warn("[MapLoader] StreamSync: no tile index"); return; }

            float t0 = Time.realtimeSinceStartup;
            int processed = 0, parsed = 0, skipped = 0, failed = 0;
            int totalTiles = tileIndex.Count;

            Log.Info($"[MapLoader] StreamAllTilesSync: START — {totalTiles} tiles (BLOCKING main thread)");

            var tileIds = new List<long>(tileIndex.Keys);
            int effectiveMax = maxTilesToProcess > 0
                ? System.Math.Min(maxTilesToProcess, tileIds.Count)
                : tileIds.Count;

            for (int tIdx = 0; tIdx < effectiveMax; tIdx++)
            {
                long tileID = tileIds[tIdx];

                // Empty tile fast-path
                int quickMask = tileIndex[tileID].LayerMask;
                int quickSize = tileIndex[tileID].CompressedSize;
                if (isV7Format && tileIndexV7.TryGetValue(tileID, out var v7Quick))
                {
                    int lod = Mathf.Clamp(currentLOD, 0, BinaryFormat.LODCount - 1);
                    quickMask = v7Quick.LODs[lod].LayerMask;
                    quickSize = v7Quick.LODs[lod].CompressedSize;
                }
                if (quickMask == 0 || quickSize <= 0)
                {
                    skipped++;
                    processed++;
                    continue;
                }

                Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> layers = null;
                try { layers = ParseTileLogicLayers(tileID); }
                catch (System.Exception ex)
                {
                    failed++;
                    Log.Error($"[MapLoader] Tile {tileID} parse failed: {ex.Message}");
                }

                if (layers != null && layers.Count > 0)
                {
                    try { processor(tileID, layers); parsed++; }
                    catch (System.Exception ex)
                    {
                        Log.Error($"[MapLoader] Tile {tileID} processor threw: {ex.Message}");
                    }
                }
                else if (layers == null && failed == 0)
                {
                    skipped++;
                }

                processed++;
            }

            float elapsed = Time.realtimeSinceStartup - t0;
            Log.Info($"[MapLoader] StreamAllTilesSync: END — parsed {parsed}, skipped {skipped}, failed {failed} (total {processed}/{effectiveMax}) in {elapsed:F2}s");
        }

        /// <summary>Sync impl per-tile via TileManager — używane przez <see cref="EnsureAllTilesLoadedSync"/>.</summary>
        private int LoadAllTilesSyncImpl(bool forceReloadAll)
        {
            float t0 = Time.realtimeSinceStartup;
            int loaded = 0;
            int reloaded = 0;
            int processed = 0;

            const int GcEveryNTiles = 500;

            foreach (var tileID in tileIndex.Keys)
            {
                if (tileManager.IsTileLoaded(tileID))
                {
                    if (!forceReloadAll) { processed++; continue; }
                    tileManager.ForceReloadTile(tileID);
                    LoadTile(tileID);
                    reloaded++;
                }
                else
                {
                    tileManager.RequestTileLoad(tileID);
                    LoadTile(tileID);
                    loaded++;
                }

                processed++;
                if (_suppressRenderForExtraction && processed % GcEveryNTiles == 0)
                {
                    System.GC.Collect();
                    if (showDebugInfo)
                    {
                        long memMB = System.GC.GetTotalMemory(false) / (1024 * 1024);
                        Log.Info($"[MapLoader] EnsureAllTilesLoadedSync: {processed}/{tileIndex.Count} tiles, GC mem={memMB} MB");
                    }
                }
            }
            float elapsed = Time.realtimeSinceStartup - t0;
            Log.Info($"[MapLoader] EnsureAllTilesLoadedSync: loaded {loaded} new, reloaded {reloaded} "
                     + $"(total {loaded + reloaded}/{tileIndex.Count}) in {elapsed:F2}s");
            return loaded + reloaded;
        }
    }
}
