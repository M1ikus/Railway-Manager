using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using K4os.Compression.LZ4;
using formap;
using RailwayManager.Core;

namespace MapSystem
{
    public partial class MapLoader
    {
        // ═══════════════════════════════════════════
        //  TILE LOADING (v6/v7) — single tile + callback
        // ═══════════════════════════════════════════

        /// <summary>
        /// Dekoduje blok kafla v8 (FORMAP04): seek do fileOffset → ReadBytes(compressedSize) BEZ int32
        /// length-prefiksu (v8 lokalizuje blok wyłącznie przez index) → LZ4 Unpickle → BlockDecoderV8 z opcjonalnym
        /// filtrem warstw (<paramref name="wantLayer"/>==null = wszystkie). Pooling wierzchołków = dekodujemy cały
        /// blok, ale filtr pomija alokację niechcianych warstw (odpowiednik v7 SkipFeatureBytes).
        /// </summary>
        private Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> DecodeTileBlockV8(
            BinaryReader reader, long fileOffset, int compressedSize, System.Func<BinaryFormat.LayerType, bool> wantLayer)
        {
            reader.BaseStream.Seek(fileOffset, SeekOrigin.Begin);
            byte[] compressed = reader.ReadBytes(compressedSize);
            byte[] body = LZ4Pickler.Unpickle(compressed);
            return BlockDecoderV8.DecodeBlock(body, stringTableV8, wantLayer);
        }

        /// <summary>
        /// Loads a single tile synchronously (disk I/O + LZ4 + parse + mark loaded)
        /// </summary>
        private void LoadTile(long tileID)
        {
            if (!tileIndex.TryGetValue(tileID, out var entry))
                return;

            // For v7: get the correct LOD offset
            long fileOffset = entry.FileOffset;
            int layerMask = entry.LayerMask;
            int compressedSizeFromIndex = entry.CompressedSize;

            if (isV7Format && tileIndexV7.TryGetValue(tileID, out var v7Entry))
            {
                int lod = Mathf.Clamp(currentLOD, 0, BinaryFormat.LODCount - 1);
                fileOffset = v7Entry.LODs[lod].FileOffset;
                layerMask = v7Entry.LODs[lod].LayerMask;
                compressedSizeFromIndex = v7Entry.LODs[lod].CompressedSize;
            }

            // Empty tile guard — tile bez warstw (np. nad morzem) ma layerMask=0 lub compressedSize=0.
            // LZ4Pickler.Unpickle(empty) rzuca wyjątek → coroutine umiera cicho. Skip + mark empty.
            if (layerMask == 0 || compressedSizeFromIndex <= 0)
            {
                tileManager?.MarkTileLoaded(tileID, new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>());
                return;
            }

            // v8: dekoduj blok przez BlockDecoderV8 (string table + SoA shuffled vertices), bez int32 length-prefiksu.
            if (isV8Format)
            {
                try
                {
                    System.Func<BinaryFormat.LayerType, bool> wantLayer = null;
                    if (_suppressRenderForExtraction) wantLayer = IsLogicLayer; // extraction: tylko logic layers
                    // formap §8 whole-block skip: filtrujemy a tile nie ma żadnej chcianej warstwy → pomiń I/O+decompress
                    if (wantLayer != null && (layerMask & LogicLayerMask) == 0)
                    {
                        tileManager.MarkTileLoaded(tileID, new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>());
                        return;
                    }
                    Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> tileLayers;
                    using (var v8fs = File.OpenRead(mapFilePath))
                    using (var v8reader = new BinaryReader(v8fs))
                        tileLayers = DecodeTileBlockV8(v8reader, fileOffset, compressedSizeFromIndex, wantLayer);
                    tileManager.MarkTileLoaded(tileID, tileLayers);
                }
                catch (Exception ex)
                {
                    Log.Error($"[MapLoader] Failed to load v8 tile {tileID}: {ex.Message}");
                    tileManager?.MarkTileLoaded(tileID, new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>());
                }
                return;
            }

            try
            {
                using var fileStream = File.OpenRead(mapFilePath);
                using var reader = new BinaryReader(fileStream);

                fileStream.Seek(fileOffset, SeekOrigin.Begin);

                int compressedSize = reader.ReadInt32();
                if (compressedSize <= 0 || compressedSize > 200 * 1024 * 1024)
                {
                    Log.Warn($"[MapLoader] Tile {tileID}: invalid compressedSize={compressedSize} at offset {fileOffset}, skipping");
                    tileManager.MarkTileLoaded(tileID, new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>());
                    return;
                }
                byte[] compressed = reader.ReadBytes(compressedSize);
                byte[] decompressed = LZ4Pickler.Unpickle(compressed);

                var tileLayers = new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>();

                using var memStream = new MemoryStream(decompressed);
                using var tileReader = new BinaryReader(memStream);

                for (int layerIdx = 0; layerIdx < BinaryFormat.LayerCount; layerIdx++)
                {
                    if ((layerMask & (1 << layerIdx)) == 0)
                        continue;

                    int layerTypeInt = tileReader.ReadInt32();
                    var layerType = (BinaryFormat.LayerType)layerTypeInt;

                    int featureCount = tileReader.ReadInt32();

                    // Sanity check — corrupt featureCount może być INT_MAX z garbage bytes
                    if (featureCount < 0 || featureCount > 1_000_000)
                    {
                        Log.Warn($"[MapLoader] Tile {tileID} layer {layerType}: invalid featureCount={featureCount}, skipping tile");
                        tileManager.MarkTileLoaded(tileID, new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>());
                        return;
                    }

                    // M-PL: w extraction mode skip non-logic layers (Buildings/Forests/Highways/Water/
                    // Industrial/Military/Waterways) — RenderTile jest suppressed, Loader'y nie czytają.
                    // Skip via SkipFeature (stream seek bez allocation) zamiast Read+discard. Buildings
                    // 17M features × 1 fat tile może zająć 30-60s parsing → ~3s skip.
                    if (_suppressRenderForExtraction && !IsLogicLayer(layerType))
                    {
                        for (int i = 0; i < featureCount; i++)
                            SkipFeatureBytes(tileReader);
                        continue;
                    }

                    // Per-layer log dla "fat" tile (>50k features)
                    bool isFatLayer = featureCount > 50_000;
                    float t0Layer = isFatLayer ? Time.realtimeSinceStartup : 0f;

                    var features = new List<MeshGeometry>(featureCount);
                    for (int i = 0; i < featureCount; i++)
                    {
                        features.Add(MeshGeometry.Read(tileReader));
                    }

                    if (isFatLayer)
                    {
                        float dt = Time.realtimeSinceStartup - t0Layer;
                        Log.Info($"[MapLoader] Tile {tileID} layer {layerType}: parsed {featureCount} features in {dt:F2}s");
                    }

                    tileLayers[layerType] = features;
                }

                tileManager.MarkTileLoaded(tileID, tileLayers);
            }
            catch (Exception ex)
            {
                Log.Error($"[MapLoader] Failed to load tile {tileID}: {ex.Message}");
                tileManager?.MarkTileLoaded(tileID, new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>());
            }
        }

        /// <summary>
        /// Called when TileManager finishes loading a tile.
        /// If this is a LOD reload, destroy old visuals AFTER new ones are created.
        /// </summary>
        private void OnTileLoadedFromManager(long tileID, TileData tileData)
        {
            if (mapRenderer == null) return;

            // M-PL: extraction mode — skip RenderTile (zatrzymuje wzrost RAM przy bulk load).
            // Loader'y odczytują tile.Layers przez GetAllFeaturesAcrossTiles. Non-logic
            // layer geometry CZYŚCIMY mimo wszystko żeby zaoszczędzić ~3 GB tile data.
            if (_suppressRenderForExtraction)
            {
                ClearNonLogicLayerGeometry(tileData);
                return;
            }

            // LOD reload: destroy old visuals, then create new.
            // MapRenderer.UnloadTile robi explicit Destroy(mf.sharedMesh) na każdy mesh
            // — nie ma leaked assets do Resources.UnloadUnusedAssets cleanup. Poprzednio
            // wywoływane Resources.UnloadUnusedAssets() + GC.Collect() po ostatnim pending
            // tile dawały hicup 200-800ms na KAŻDY próg LOD (gracz scrollujący zoom = lag
            // na każdym progu zoomLOD1..5). Usunięte 2026-05-14.
            if (pendingLODReload.Contains(tileID))
            {
                mapRenderer.UnloadTile(tileID);
                pendingLODReload.Remove(tileID);
            }

            mapRenderer.RenderTile(tileID, tileData);

            ClearNonLogicLayerGeometry(tileData);
        }

        /// <summary>
        /// Parsuje TYLKO logic layers z tile (skip Buildings/Forests/Highways/etc. via
        /// <see cref="SkipFeatureBytes"/>). Zwraca Dictionary z parsed features lub null
        /// dla empty/invalid tile. NIE wywołuje TileManager ani eventów.
        /// </summary>
        private Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> ParseTileLogicLayers(long tileID)
        {
            if (!tileIndex.TryGetValue(tileID, out var entry))
                return null;

            long fileOffset = entry.FileOffset;
            int layerMask = entry.LayerMask;
            int compressedSizeFromIndex = entry.CompressedSize;

            if (isV7Format && tileIndexV7.TryGetValue(tileID, out var v7Entry))
            {
                int lod = Mathf.Clamp(currentLOD, 0, BinaryFormat.LODCount - 1);
                fileOffset = v7Entry.LODs[lod].FileOffset;
                layerMask = v7Entry.LODs[lod].LayerMask;
                compressedSizeFromIndex = v7Entry.LODs[lod].CompressedSize;
            }

            if (layerMask == 0 || compressedSizeFromIndex <= 0)
                return null; // empty tile

            if (isV8Format)
            {
                // formap §8 whole-block skip: brak logic layer w masce → pomiń I/O+decompress
                if ((layerMask & LogicLayerMask) == 0)
                    return new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>();
                using var v8fs = File.OpenRead(mapFilePath);
                using var v8reader = new BinaryReader(v8fs);
                return DecodeTileBlockV8(v8reader, fileOffset, compressedSizeFromIndex, IsLogicLayer);
            }

            using var fileStream = File.OpenRead(mapFilePath);
            using var reader = new BinaryReader(fileStream);
            fileStream.Seek(fileOffset, SeekOrigin.Begin);

            int compressedSize = reader.ReadInt32();
            if (compressedSize <= 0 || compressedSize > 200 * 1024 * 1024)
            {
                Log.Warn($"[MapLoader] ParseTileLogicLayers: tile {tileID} invalid compressedSize={compressedSize}");
                return null;
            }
            byte[] compressed = reader.ReadBytes(compressedSize);
            byte[] decompressed = LZ4Pickler.Unpickle(compressed);

            var result = new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>();
            using var memStream = new MemoryStream(decompressed);
            using var tileReader = new BinaryReader(memStream);

            for (int layerIdx = 0; layerIdx < BinaryFormat.LayerCount; layerIdx++)
            {
                if ((layerMask & (1 << layerIdx)) == 0) continue;

                int layerTypeInt = tileReader.ReadInt32();
                var layerType = (BinaryFormat.LayerType)layerTypeInt;
                int featureCount = tileReader.ReadInt32();

                if (featureCount < 0 || featureCount > 1_000_000)
                {
                    Log.Warn($"[MapLoader] ParseTileLogicLayers: tile {tileID} layer {layerType}: invalid featureCount={featureCount}");
                    return null;
                }

                if (!IsLogicLayer(layerType))
                {
                    // Skip non-logic via stream seek (no allocation, fast)
                    for (int i = 0; i < featureCount; i++)
                        SkipFeatureBytes(tileReader);
                    continue;
                }

                var features = new List<MeshGeometry>(featureCount);
                for (int i = 0; i < featureCount; i++)
                    features.Add(MeshGeometry.Read(tileReader));

                result[layerType] = features;
            }

            return result;
        }

        /// <summary>
        /// Warstwy tła renderowane w mini-mapie OSM (RouteMapPreview). TD-041: rozszerzone o fille
        /// (Buildings/Industrial/Military/Platforms) dla pełniejszego obrazu — bez POI/Places/labeli
        /// (punkty/markery, nie mesh). Musi być spójne z RouteMapPreviewTiles.PreviewLayers.
        /// </summary>
        private static bool IsPreviewRenderLayer(BinaryFormat.LayerType type)
        {
            return type == BinaryFormat.LayerType.Railways
                || type == BinaryFormat.LayerType.Highways
                || type == BinaryFormat.LayerType.Water
                || type == BinaryFormat.LayerType.Waterways
                || type == BinaryFormat.LayerType.Forests
                || type == BinaryFormat.LayerType.Buildings
                || type == BinaryFormat.LayerType.Industrial
                || type == BinaryFormat.LayerType.Military
                || type == BinaryFormat.LayerType.Platforms;
        }

        /// <summary>
        /// Parsuje warstwy TŁA kafla w JAWNYM <paramref name="lod"/> (NIE globalnym currentLOD)
        /// — dla mini-mapy OSM, która ma własny LOD niezależny od głównej kamery. Zwraca tylko
        /// warstwy z <see cref="IsPreviewRenderLayer"/>. Czysta: brak side-effectów (TileManager/
        /// eventy/render nietknięte, globalny LOD nietknięty). Null dla empty/invalid tile.
        /// Wzorzec z <see cref="ParseTileLogicLayers"/>.
        /// </summary>
        public Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> ParseTileRenderLayers(long tileID, int lod)
        {
            if (!tileIndex.TryGetValue(tileID, out var entry))
                return null;

            long fileOffset = entry.FileOffset;
            int layerMask = entry.LayerMask;
            int compressedSizeFromIndex = entry.CompressedSize;

            if (isV7Format && tileIndexV7.TryGetValue(tileID, out var v7Entry))
            {
                int clampedLod = Mathf.Clamp(lod, 0, BinaryFormat.LODCount - 1);
                fileOffset = v7Entry.LODs[clampedLod].FileOffset;
                layerMask = v7Entry.LODs[clampedLod].LayerMask;
                compressedSizeFromIndex = v7Entry.LODs[clampedLod].CompressedSize;
            }

            if (layerMask == 0 || compressedSizeFromIndex <= 0)
                return null; // empty tile

            if (isV8Format)
            {
                // formap §8 whole-block skip: brak preview-render layer w masce → pomiń I/O+decompress
                if ((layerMask & PreviewRenderLayerMask) == 0)
                    return new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>();
                using var v8fs = File.OpenRead(mapFilePath);
                using var v8reader = new BinaryReader(v8fs);
                return DecodeTileBlockV8(v8reader, fileOffset, compressedSizeFromIndex, IsPreviewRenderLayer);
            }

            using var fileStream = File.OpenRead(mapFilePath);
            using var reader = new BinaryReader(fileStream);
            fileStream.Seek(fileOffset, SeekOrigin.Begin);

            int compressedSize = reader.ReadInt32();
            if (compressedSize <= 0 || compressedSize > 200 * 1024 * 1024)
            {
                Log.Warn($"[MapLoader] ParseTileRenderLayers: tile {tileID} invalid compressedSize={compressedSize}");
                return null;
            }
            byte[] compressed = reader.ReadBytes(compressedSize);
            byte[] decompressed = LZ4Pickler.Unpickle(compressed);

            var result = new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>();
            using var memStream = new MemoryStream(decompressed);
            using var tileReader = new BinaryReader(memStream);

            for (int layerIdx = 0; layerIdx < BinaryFormat.LayerCount; layerIdx++)
            {
                if ((layerMask & (1 << layerIdx)) == 0) continue;

                int layerTypeInt = tileReader.ReadInt32();
                var layerType = (BinaryFormat.LayerType)layerTypeInt;
                int featureCount = tileReader.ReadInt32();

                if (featureCount < 0 || featureCount > 1_000_000)
                {
                    Log.Warn($"[MapLoader] ParseTileRenderLayers: tile {tileID} layer {layerType}: invalid featureCount={featureCount}");
                    return null;
                }

                if (!IsPreviewRenderLayer(layerType))
                {
                    for (int i = 0; i < featureCount; i++)
                        SkipFeatureBytes(tileReader);
                    continue;
                }

                var features = new List<MeshGeometry>(featureCount);
                for (int i = 0; i < featureCount; i++)
                    features.Add(MeshGeometry.Read(tileReader));

                result[layerType] = features;
            }

            return result;
        }
    }
}
