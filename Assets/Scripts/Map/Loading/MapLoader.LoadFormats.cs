using System.Collections.Generic;
using System.IO;
using UnityEngine;
using formap;
using RailwayManager.Core;

namespace MapSystem
{
    public partial class MapLoader
    {
        // ═══════════════════════════════════════════
        //  FORMAT PARSERS — v5 (legacy), v6 (tiled), v7 (multi-LOD tiled)
        // ═══════════════════════════════════════════

        /// <summary>
        /// Loads v5 format (legacy - entire map at once)
        /// </summary>
        private void LoadMapV5(BinaryReader reader)
        {
            // Read file header
            BinaryFormat.ReadHeader(reader, out version, out int layerCount, out globalBounds);

            if (showDebugInfo)
            {
                Log.Info($"[MapLoader] Format: v5 (legacy, non-tiled)");
                Log.Info($"[MapLoader] Layer count: {layerCount}");
                Log.Info($"[MapLoader] Bounds: [{globalBounds.MinX:F2}, {globalBounds.MinY:F2}] to " +
                         $"[{globalBounds.MaxX:F2}, {globalBounds.MaxY:F2}]");

                float width = globalBounds.MaxX - globalBounds.MinX;
                float height = globalBounds.MaxY - globalBounds.MinY;
                Log.Info($"[MapLoader] Map size: {width / 1000:F2}km × {height / 1000:F2}km");
            }

            // Clear existing data
            layers.Clear();

            // Read each layer
            for (int i = 0; i < layerCount; i++)
            {
                LoadLayer(reader);
            }

            // Trigger rendering
            if (mapRenderer != null)
            {
                mapRenderer.RenderMap(layers, globalBounds);
            }
            else
            {
                Log.Warn("[MapLoader] No MapRenderer assigned!");
            }
        }

        /// <summary>
        /// Loads v6 format (tiled - loads index, tiles loaded on-demand)
        /// </summary>
        private void LoadMapV6(BinaryReader reader)
        {
            // Skip magic, read header
            reader.ReadBytes(8);
            BinaryFormat.ReadHeaderV6(reader, out tileSize, out globalBounds,
                out tilesX, out tilesY, out int totalTiles, out long indexTableOffset);

            version = 6;

            if (showDebugInfo)
            {
                Log.Info($"[MapLoader] Format: v6 (tiled)");
                Log.Info($"[MapLoader] Tile size: {tileSize}m");
                Log.Info($"[MapLoader] Grid: {tilesX} x {tilesY} = {totalTiles} tiles");
                Log.Info($"[MapLoader] Bounds: [{globalBounds.MinX:F2}, {globalBounds.MinY:F2}] to " +
                         $"[{globalBounds.MaxX:F2}, {globalBounds.MaxY:F2}]");
            }

            // Read tile index
            reader.BaseStream.Seek(indexTableOffset, SeekOrigin.Begin);
            tileIndex.Clear();

            for (int i = 0; i < totalTiles; i++)
            {
                var entry = BinaryFormat.ReadTileIndexEntry(reader);
                tileIndex[entry.TileID] = entry;
            }

            if (showDebugInfo)
            {
                Log.Info($"[MapLoader] Loaded {tileIndex.Count} tile index entries");
            }

            // Notify renderer about tiled format
            if (mapRenderer != null)
            {
                mapRenderer.SetTiledMode(true);
            }

            // Initialize TileManager
            if (tileManager != null)
            {
                tileManager.Initialize(tileIndex, globalBounds, tileSize, tilesX, tilesY, mapRenderer);

                // Subscribe to tile load requests
                SubscribeToTileManager();

                // Start loading initial tiles
                StartTileLoadingLoop();
            }
            else
            {
                Log.Warn("[MapLoader] No TileManager assigned! Tiles won't be loaded.");
            }
        }

        /// <summary>
        /// Loads v7 format (multi-LOD tiled)
        /// </summary>
        private void LoadMapV7(BinaryReader reader)
        {
            reader.ReadBytes(8); // Skip magic
            BinaryFormat.ReadHeaderV7(reader, out tileSize, out globalBounds,
                out tilesX, out tilesY, out int totalTiles, out long indexTableOffset);

            version = 7;
            isV7Format = true;

            if (showDebugInfo)
            {
                Log.Info($"[MapLoader] Format: v7 (multi-LOD tiled, {BinaryFormat.LODCount} LODs)");
                Log.Info($"[MapLoader] Tile size: {tileSize}m, Grid: {tilesX}x{tilesY} = {totalTiles} tiles");
            }

            // Read v7 tile index
            reader.BaseStream.Seek(indexTableOffset, SeekOrigin.Begin);
            tileIndexV7.Clear();
            tileIndex.Clear();

            for (int i = 0; i < totalTiles; i++)
            {
                var entry = BinaryFormat.ReadTileIndexEntryV7(reader);
                tileIndexV7[entry.TileID] = entry;

                // Create v6-compatible index entry (for TileManager compatibility)
                // Uses LOD0 data by default — LoadTile will pick the right LOD
                tileIndex[entry.TileID] = new BinaryFormat.TileIndexEntry
                {
                    TileID = entry.TileID,
                    GridX = entry.GridX,
                    GridY = entry.GridY,
                    Bounds = entry.Bounds,
                    FileOffset = entry.LODs[0].FileOffset,
                    CompressedSize = entry.LODs[0].CompressedSize,
                    UncompressedSize = entry.LODs[0].UncompressedSize,
                    LayerMask = entry.LODs[0].LayerMask,
                    FeatureCounts = entry.FeatureCounts
                };
            }

            if (showDebugInfo)
                Log.Info($"[MapLoader] Loaded {tileIndexV7.Count} tile index entries (v7)");

            if (mapRenderer != null)
                mapRenderer.SetTiledMode(true);

            if (tileManager != null)
            {
                tileManager.Initialize(tileIndex, globalBounds, tileSize, tilesX, tilesY, mapRenderer);
                tileManager.SetCurrentLOD(currentLOD);
                SubscribeToTileManager();

                // M-PL: PAUSE tile loading initial — TimetableInitializer wywoła BeginExtraction
                // i przejmie kontrolę. Bez tego TileManager od razu request'uje wszystkie visible
                // tile (na pełnej Polsce z default kamerą = wszystkie 5624) → MapRenderer.RenderTile
                // dla fat tile (Warszawa 158k features) blokuje main thread przed Initialize startuje.
                _suppressRenderForExtraction = true;
                tileManager.ExtractionPaused = true;
                Log.Info("[MapLoader] Initial tile loading PAUSED (waiting for Initialize/EndExtraction)");

                StartTileLoadingLoop();
            }
            else
            {
                Log.Warn("[MapLoader] No TileManager assigned! Tiles won't be loaded.");
            }
        }

        /// <summary>
        /// Loads v8 format (FORMAP04) — multi-LOD tiled, global string table + SoA byte-shuffled vertices,
        /// opcjonalny podpis Ed25519. Reużywa machinerii v7 (LODInfo[] + dual-dict tileIndex/tileIndexV7);
        /// różni się tylko dekodowaniem bloku (BlockDecoderV8) + brakiem int32 length-prefiksu + string table.
        /// </summary>
        private void LoadMapV8(BinaryReader reader)
        {
            reader.ReadBytes(8); // Skip magic
            BinaryFormat.ReadHeaderV8(reader, out tileSize, out globalBounds, out tilesX, out tilesY,
                out int totalTiles, out long indexTableOffset, out int layerCount, out int lodCount,
                out int compressionType, out int signatureLength);

            version = 8;
            isV7Format = true;   // reużyj LOD override + dual-dict (kształt indexu zgodny)
            isV8Format = true;
            layerCountV8 = layerCount;
            lodCountV8 = lodCount;

            // Guard kompresji — RM wspiera tylko LZ4-HC (0). Zstd (1) celowo nieobsługiwany (patrz rm-v8-integration §6).
            if (compressionType != 0)
            {
                Log.Error($"[MapLoader] v8: compressionType={compressionType} (oczekiwano 0=LZ4-HC). " +
                          "Zstd nieobsługiwany w RM — wygeneruj mapę z LZ4-HC. Przerywam ładowanie.");
                return;
            }

            // Guard v7-trap: runtime LOD/layer logic zakłada stałe 13/6. Header niesie wartości dynamicznie,
            // ale gdy się różnią od stałych — głośny błąd zamiast cichego desync (zaktualizuj LayerType/LODCount).
            if (layerCount != BinaryFormat.LayerCount || lodCount != BinaryFormat.LODCount)
            {
                Log.Error($"[MapLoader] v8: header layerCount={layerCount}/lodCount={lodCount} != oczekiwane " +
                          $"{BinaryFormat.LayerCount}/{BinaryFormat.LODCount}. Przerywam (format zmienił liczbę warstw/LOD?).");
                return;
            }

            // Weryfikacja podpisu (Pillar 2) PRZED parsowaniem reszty.
            if (verifyV8Signature)
            {
                float tSig = Time.realtimeSinceStartup;
                var sigResult = MapSignatureVerifier.VerifyFile(mapFilePath, out string sigDetail);
                float sigMs = (Time.realtimeSinceStartup - tSig) * 1000f;
                switch (sigResult)
                {
                    case MapSignatureResult.Valid:
                        Log.Info($"[MapLoader] v8 podpis: OK — {sigDetail} ({sigMs:F0}ms)");
                        break;
                    case MapSignatureResult.Unsigned:
                        if (requireSignedV8)
                        {
                            Log.Error($"[MapLoader] v8 plik NIEPODPISANY a requireSignedV8=true — przerywam. {sigDetail}");
                            return;
                        }
                        Log.Warn($"[MapLoader] v8 plik niepodpisany (requireSignedV8=false) — ładuję bez weryfikacji. {sigDetail}");
                        break;
                    case MapSignatureResult.PublicKeyNotConfigured:
                        Log.Error($"[MapLoader] v8 weryfikacja niemożliwa: {sigDetail}. Ustaw klucz w MapSignatureVerifier " +
                                  "albo wyłącz verifyV8Signature. Przerywam.");
                        return;
                    default:
                        Log.Error($"[MapLoader] v8 weryfikacja podpisu FAIL ({sigResult}): {sigDetail}. Przerywam ładowanie mapy.");
                        return;
                }
            }

            if (showDebugInfo)
                Log.Info($"[MapLoader] Format: v8 (FORMAP04, {lodCount} LODs, {layerCount} warstw, " +
                         $"signed={(signatureLength == 64)})");

            // Global string table — zaraz po 128-B headerze (stream jest tu na właściwej pozycji).
            int strCount = (int)FeatureCodecV8.ReadVarint(reader);
            stringTableV8 = new string[strCount];
            for (int i = 0; i < strCount; i++)
            {
                int len = (int)FeatureCodecV8.ReadVarint(reader);
                stringTableV8[i] = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(len));
            }
            if (showDebugInfo)
                Log.Info($"[MapLoader] v8 string table: {strCount} stringów");

            // Index v8 (przy indexTableOffset).
            reader.BaseStream.Seek(indexTableOffset, SeekOrigin.Begin);
            tileIndexV7.Clear();
            tileIndex.Clear();

            for (int i = 0; i < totalTiles; i++)
            {
                var entry = BinaryFormat.ReadTileIndexEntryV8(reader, lodCount, layerCount);
                tileIndexV7[entry.TileID] = entry;

                // v6-compat entry z LOD0 (TileManager/LoadTile lookup najpierw tileIndex).
                tileIndex[entry.TileID] = new BinaryFormat.TileIndexEntry
                {
                    TileID = entry.TileID,
                    GridX = entry.GridX,
                    GridY = entry.GridY,
                    Bounds = entry.Bounds,
                    FileOffset = entry.LODs[0].FileOffset,
                    CompressedSize = entry.LODs[0].CompressedSize,
                    UncompressedSize = entry.LODs[0].UncompressedSize,
                    LayerMask = entry.LODs[0].LayerMask,
                    FeatureCounts = entry.FeatureCounts
                };
            }

            if (showDebugInfo)
                Log.Info($"[MapLoader] Loaded {tileIndexV7.Count} tile index entries (v8)");

            if (mapRenderer != null)
                mapRenderer.SetTiledMode(true);

            if (tileManager != null)
            {
                tileManager.Initialize(tileIndex, globalBounds, tileSize, tilesX, tilesY, mapRenderer);
                tileManager.SetCurrentLOD(currentLOD);
                SubscribeToTileManager();

                // M-PL: pauza initial streaming — TimetableInitializer przejmie kontrolę (jak v7).
                _suppressRenderForExtraction = true;
                tileManager.ExtractionPaused = true;
                Log.Info("[MapLoader] Initial tile loading PAUSED (waiting for Initialize/EndExtraction)");

                StartTileLoadingLoop();
            }
            else
            {
                Log.Warn("[MapLoader] No TileManager assigned! Tiles won't be loaded.");
            }
        }

        /// <summary>
        /// Loads a single layer from the binary stream
        /// ✅ MeshGeometry.Read() handles HoleStarts automatically
        /// </summary>
        private void LoadLayer(BinaryReader reader)
        {
            // Read layer header
            BinaryFormat.ReadLayerHeader(reader, out var layerType, out int featureCount);

            if (showDebugInfo)
            {
                Log.Info($"[MapLoader] Loading layer {layerType}: {featureCount} features");
            }

            float layerStartTime = Time.realtimeSinceStartup;

            // Read compressed data size
            int compressedSize = reader.ReadInt32();

            // Read compressed data
            byte[] compressed = reader.ReadBytes(compressedSize);

            // Decompress using Unity's built-in LZ4
            byte[] decompressed = DecompressLZ4(compressed);

            if (decompressed == null)
            {
                Log.Error($"[MapLoader] Failed to decompress layer {layerType}");
                return;
            }

            // Parse features from decompressed data
            var features = new List<MeshGeometry>(featureCount);

            using var memoryStream = new MemoryStream(decompressed);
            using var tempReader = new BinaryReader(memoryStream);

            for (int i = 0; i < featureCount; i++)
            {
                // ✅ MeshGeometry.Read() now correctly reads:
                // BBox → Vertices → Indices → HoleStarts → SegmentIds → JunctionIndices → Metadata
                features.Add(MeshGeometry.Read(tempReader));
            }

            // Store layer
            layers[layerType] = features;

            if (showDebugInfo)
            {
                float layerTime = Time.realtimeSinceStartup - layerStartTime;
                float compressionRatio = 100f * compressedSize / decompressed.Length;

                // Count features with HoleStarts — foreach zamiast LINQ.Count(predicate) (closure alloc).
                int featuresWithHoles = 0;
                foreach (var f in features)
                    if (f.HoleStarts.Count > 0) featuresWithHoles++;
                string holeInfo = featuresWithHoles > 0 ? $", {featuresWithHoles} with holes" : "";

                Log.Info($"[MapLoader]   ✓ Layer {layerType}: {features.Count} features{holeInfo}, " +
                         $"{decompressed.Length / 1024}KB (compressed: {compressedSize / 1024}KB, {compressionRatio:F1}%) " +
                         $"in {layerTime:F2}s");
            }
        }
    }
}
