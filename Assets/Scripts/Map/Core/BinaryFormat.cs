using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace formap
{
    /// <summary>
    /// Specifies the binary format for the output file
    ///
    /// FORMAT v5 (FORMAP01):
    /// Header (64 bytes):
    ///   - Magic number: "FORMAP01" (8 bytes)
    ///   - Version: 5 (int32)
    ///   - Layer count: int32
    ///   - Spatial bounds: float[4] (16 bytes: minX, minY, maxX, maxY)
    ///   - Reserved: 32 bytes
    /// For each layer:
    ///   - Layer type: int32
    ///   - Feature count: int32
    ///   - Compressed layer length: int32
    ///   - Compressed layer bytes (LZ4)
    ///
    /// FORMAT v6 (FORMAP02) - TILED:
    /// Header (128 bytes):
    ///   - Magic number: "FORMAP02" (8 bytes)
    ///   - Version: 6 (int32)
    ///   - Tile size: float32 (10000.0 = 10km)
    ///   - Global bounds: float[4] (16 bytes: minX, minY, maxX, maxY)
    ///   - Grid dimensions: int32 tilesX, int32 tilesY
    ///   - Total tiles: int32
    ///   - Index table offset: int64
    ///   - Reserved: 72 bytes
    ///
    /// Tile Index Table (at index_table_offset):
    ///   For each tile:
    ///     - Tile ID: int64
    ///     - Grid X: int32, Grid Y: int32
    ///     - Tile bounds: float[4]
    ///     - File offset: int64
    ///     - Compressed size: int32
    ///     - Uncompressed size: int32
    ///     - Layer mask: int32
    ///     - Feature counts per layer: int32[LayerCount]
    ///
    /// Tile Data:
    ///   For each tile (at file_offset):
    ///     - LZ4 compressed block (compressed_size bytes)
    ///       Decompressed tile body:
    ///         For each layer (if bit set in layer_mask):
    ///           - Layer type: int32
    ///           - Feature count: int32
    ///           - Features... (same format as v5)
    /// </summary>
    public static class BinaryFormat
    {
        public const string MagicV5 = "FORMAP01";
        public const string MagicV6 = "FORMAP02";
        public const string MagicV7 = "FORMAP03";
        public const string MagicV8 = "FORMAP04"; // v8: header niesie LayerCount/LODCount/compressionType/signatureLength dynamicznie
        public const int HeaderSizeV5 = 64;
        public const int HeaderSizeV6 = 128;
        public const int HeaderSizeV7 = 128;
        public const int HeaderSizeV8 = 128;
        public const int LODCount = 6;

        // Legacy support for v5
        public const string Magic = MagicV5;
        public const int HeaderSize = HeaderSizeV5;

        public enum LayerType : int
        {
            Highways = 0,         // Roads - polylines (rendered as triangles)
            Railways = 1,         // Railway tracks - polylines
            Buildings = 2,        // Filled polygons
            Water = 3,            // Lakes, ponds - filled polygons
            Industrial = 4,       // Industrial areas - filled polygons
            Military = 5,         // Military areas - filled polygons
            Platforms = 6,        // Railway platforms - filled polygons
            Forests = 7,          // Forests - filled polygons
            POIs = 8,             // Railway stations/halts/signals - single points
            Waterways = 9,        // Rivers, streams, canals - polylines (rendered as triangles)
            AdminBoundaries = 10, // Administrative borders (country + voivodeships) - filled polygons
            Places = 11,          // Cities, towns, villages (place=*) - single points
            Coastlines = 12       // OSM natural=coastline ways (used for synthetic water Bałtyk/Zalew)
        }

        /// <summary>Liczba wartości w enum LayerType — używaj zamiast hardcoded 13.</summary>
        public const int LayerCount = 13;

        public static void WriteHeader(BinaryWriter writer, int layerCount, BBox spatialBounds)
        {
            // Magic number
            writer.Write(System.Text.Encoding.ASCII.GetBytes(Magic));

            // Version
            writer.Write(5); // Version 5: per-layer LZ4-compressed layer body

            // Layer count
            writer.Write(layerCount);

            // Write spatial bounds (16 bytes: minX, minY, maxX, maxY)
            writer.Write(spatialBounds.MinX);
            writer.Write(spatialBounds.MinY);
            writer.Write(spatialBounds.MaxX);
            writer.Write(spatialBounds.MaxY);

            // Reserved bytes
            writer.Write(new byte[32]);
        }

        public static void ReadHeader(BinaryReader reader, out int version, out int layerCount, out BBox spatialBounds)
        {
            byte[] magicBytes = reader.ReadBytes(8);
            string magic = System.Text.Encoding.ASCII.GetString(magicBytes);

            if (magic != Magic)
            {
                throw new InvalidDataException($"Invalid magic number. Expected {Magic}, got {magic}");
            }

            version = reader.ReadInt32();
            layerCount = reader.ReadInt32();

            // Read spatial bounds
            spatialBounds = new BBox
            {
                MinX = reader.ReadSingle(),
                MinY = reader.ReadSingle(),
                MaxX = reader.ReadSingle(),
                MaxY = reader.ReadSingle()
            };

            reader.ReadBytes(32); // Skip reserved
        }

        public static void WriteLayerHeader(BinaryWriter writer, LayerType layerType, int featureCount)
        {
            writer.Write((int)layerType);
            writer.Write(featureCount);
        }

        public static void ReadLayerHeader(BinaryReader reader, out LayerType layerType, out int featureCount)
        {
            layerType = (LayerType)reader.ReadInt32();
            featureCount = reader.ReadInt32();
        }

        // ==================== FORMAT v6 METHODS ====================

        /// <summary>
        /// Writes v6 header with tile information
        /// </summary>
        public static void WriteHeaderV6(BinaryWriter writer, BBox globalBounds, float tileSize,
            int tilesX, int tilesY, int totalTiles, long indexTableOffset)
        {
            // Magic number
            writer.Write(System.Text.Encoding.ASCII.GetBytes(MagicV6));

            // Version
            writer.Write(6);

            // Tile size
            writer.Write(tileSize);

            // Global bounds
            writer.Write(globalBounds.MinX);
            writer.Write(globalBounds.MinY);
            writer.Write(globalBounds.MaxX);
            writer.Write(globalBounds.MaxY);

            // Grid dimensions
            writer.Write(tilesX);
            writer.Write(tilesY);

            // Total tiles
            writer.Write(totalTiles);

            // Index table offset
            writer.Write(indexTableOffset);

            // Reserved bytes (72 bytes to make header 128 bytes total)
            writer.Write(new byte[72]);
        }

        /// <summary>
        /// Reads v6 header
        /// </summary>
        public static void ReadHeaderV6(BinaryReader reader, out float tileSize, out BBox globalBounds,
            out int tilesX, out int tilesY, out int totalTiles, out long indexTableOffset)
        {
            // Magic already read before calling this
            // Read version
            int version = reader.ReadInt32();
            if (version != 6)
                throw new InvalidDataException($"Expected version 6, got {version}");

            // Tile size
            tileSize = reader.ReadSingle();

            // Global bounds
            globalBounds = new BBox
            {
                MinX = reader.ReadSingle(),
                MinY = reader.ReadSingle(),
                MaxX = reader.ReadSingle(),
                MaxY = reader.ReadSingle()
            };

            // Grid dimensions
            tilesX = reader.ReadInt32();
            tilesY = reader.ReadInt32();

            // Total tiles
            totalTiles = reader.ReadInt32();

            // Index table offset
            indexTableOffset = reader.ReadInt64();

            // Skip reserved
            reader.ReadBytes(72);
        }

        /// <summary>
        /// Writes a tile index entry
        /// </summary>
        public static void WriteTileIndexEntry(BinaryWriter writer, TileIndexEntry tile)
        {
            // Tile ID
            writer.Write(tile.TileID);

            // Grid coordinates
            writer.Write(tile.GridX);
            writer.Write(tile.GridY);

            // Tile bounds
            writer.Write(tile.Bounds.MinX);
            writer.Write(tile.Bounds.MinY);
            writer.Write(tile.Bounds.MaxX);
            writer.Write(tile.Bounds.MaxY);

            // File offset
            writer.Write(tile.FileOffset);

            // Compressed size
            writer.Write(tile.CompressedSize);

            // Uncompressed size
            writer.Write(tile.UncompressedSize);

            // Layer mask
            writer.Write(tile.LayerMask);

            // Feature counts per layer
            for (int i = 0; i < LayerCount; i++)
            {
                int count = tile.FeatureCounts != null && i < tile.FeatureCounts.Length ? tile.FeatureCounts[i] : 0;
                writer.Write(count);
            }
        }

        /// <summary>
        /// Reads a tile index entry
        /// </summary>
        public static TileIndexEntry ReadTileIndexEntry(BinaryReader reader)
        {
            var entry = new TileIndexEntry
            {
                TileID = reader.ReadInt64(),
                GridX = reader.ReadInt32(),
                GridY = reader.ReadInt32(),
                Bounds = new BBox
                {
                    MinX = reader.ReadSingle(),
                    MinY = reader.ReadSingle(),
                    MaxX = reader.ReadSingle(),
                    MaxY = reader.ReadSingle()
                },
                FileOffset = reader.ReadInt64(),
                CompressedSize = reader.ReadInt32(),
                UncompressedSize = reader.ReadInt32(),
                LayerMask = reader.ReadInt32()
            };

            // Read feature counts
            entry.FeatureCounts = new int[LayerCount];
            for (int i = 0; i < LayerCount; i++)
            {
                entry.FeatureCounts[i] = reader.ReadInt32();
            }

            return entry;
        }

        /// <summary>
        /// Tile index entry structure (for reading/writing)
        /// </summary>
        public struct TileIndexEntry
        {
            public long TileID;
            public int GridX;
            public int GridY;
            public BBox Bounds;
            public long FileOffset;
            public int CompressedSize;
            public int UncompressedSize;
            public int LayerMask;
            public int[] FeatureCounts;
        }

        // ==================== FORMAT v7 METHODS (Multi-LOD) ====================

        public struct LODInfo
        {
            public long FileOffset;
            public int CompressedSize;
            public int UncompressedSize;
            public int LayerMask;
        }

        public struct TileIndexEntryV7
        {
            public long TileID;
            public int GridX;
            public int GridY;
            public BBox Bounds;
            public LODInfo[] LODs; // [0]=LOD0(full), [1]=LOD1(medium), [2]=LOD2(low)
            public int[] FeatureCounts;
        }

        public static void ReadHeaderV7(BinaryReader reader, out float tileSize, out BBox globalBounds,
            out int tilesX, out int tilesY, out int totalTiles, out long indexTableOffset)
        {
            int version = reader.ReadInt32();
            if (version != 7)
                throw new InvalidDataException($"Expected version 7, got {version}");

            tileSize = reader.ReadSingle();
            globalBounds = new BBox
            {
                MinX = reader.ReadSingle(),
                MinY = reader.ReadSingle(),
                MaxX = reader.ReadSingle(),
                MaxY = reader.ReadSingle()
            };
            tilesX = reader.ReadInt32();
            tilesY = reader.ReadInt32();
            totalTiles = reader.ReadInt32();
            indexTableOffset = reader.ReadInt64();
            reader.ReadBytes(72);
        }

        public static TileIndexEntryV7 ReadTileIndexEntryV7(BinaryReader reader)
        {
            var entry = new TileIndexEntryV7
            {
                TileID = reader.ReadInt64(),
                GridX = reader.ReadInt32(),
                GridY = reader.ReadInt32(),
                Bounds = new BBox
                {
                    MinX = reader.ReadSingle(),
                    MinY = reader.ReadSingle(),
                    MaxX = reader.ReadSingle(),
                    MaxY = reader.ReadSingle()
                },
                LODs = new LODInfo[LODCount]
            };

            for (int i = 0; i < LODCount; i++)
            {
                entry.LODs[i] = new LODInfo
                {
                    FileOffset = reader.ReadInt64(),
                    CompressedSize = reader.ReadInt32(),
                    UncompressedSize = reader.ReadInt32(),
                    LayerMask = reader.ReadInt32()
                };
            }

            entry.FeatureCounts = new int[LayerCount];
            for (int i = 0; i < LayerCount; i++)
            {
                entry.FeatureCounts[i] = reader.ReadInt32();
            }

            return entry;
        }

        // ==================== FORMAT v8 METHODS (FORMAP04) ====================
        //
        // Różnice vs v7 (źródło prawdy: D:\Gry\formap\BinaryFormat.cs + BinaryFormatV8.cs + docs/design/data-formats.md):
        //  - header niesie layerCount/lodCount/compressionType/signatureLength (reserved 60 B zamiast 72),
        //  - PO headerze global string table (varint count + per-string varint len + UTF-8) — czytana przez MapLoader,
        //  - index entry: po LODInfo[] dochodzi lodCount×32 B SHA-256 (ZAWSZE, też dla unsigned) PRZED featureCounts,
        //  - bloki BEZ int32 length-prefix (lokalizowane wyłącznie przez FileOffset/CompressedSize z indexu),
        //  - blok = varint structLen + struct section + shuffled X plane + shuffled Y plane (FeatureCodecV8/BlockDecoderV8),
        //  - bbox NIE zapisany — liczony z wierzchołków (MeshGeometry.ComputeBoundingBox).
        // Reużywamy TileIndexEntryV7/LODInfo (ten sam kształt LODs[]+FeatureCounts[]); hashe SHA-256 są tylko
        // pomijane przy parsie indexu (weryfikacja w MapSignatureVerifier, który robi własny surowy parse).

        /// <summary>
        /// Czyta v8 header. Magic (8 B) konsumuje CALLER przed wywołaniem — ta metoda startuje od pola version.
        /// </summary>
        public static void ReadHeaderV8(BinaryReader reader, out float tileSize, out BBox globalBounds,
            out int tilesX, out int tilesY, out int totalTiles, out long indexTableOffset,
            out int layerCount, out int lodCount, out int compressionType, out int signatureLength)
        {
            int version = reader.ReadInt32();
            if (version != 8)
                throw new InvalidDataException($"Expected version 8, got {version}");

            tileSize = reader.ReadSingle();
            globalBounds = new BBox
            {
                MinX = reader.ReadSingle(),
                MinY = reader.ReadSingle(),
                MaxX = reader.ReadSingle(),
                MaxY = reader.ReadSingle()
            };
            tilesX = reader.ReadInt32();
            tilesY = reader.ReadInt32();
            totalTiles = reader.ReadInt32();
            indexTableOffset = reader.ReadInt64();
            layerCount = reader.ReadInt32();
            lodCount = reader.ReadInt32();
            compressionType = reader.ReadInt32();   // 0 = LZ4-HC (K4os pickle), 1 = Zstd (RM nie wspiera)
            signatureLength = reader.ReadInt32();   // 0 = unsigned, 64 = Ed25519-signed
            reader.ReadBytes(60); // reserved — łącznie 128 B
        }

        /// <summary>
        /// Czyta jeden wpis indexu v8. Rozmiary z headera (<paramref name="lodCount"/>/<paramref name="layerCount"/>),
        /// NIE ze stałych. lodCount×32 B SHA-256 (po LODInfo, przed featureCounts) są CZYTANE i pomijane.
        /// Zwraca <see cref="TileIndexEntryV7"/> (ten sam kształt: LODs[] + FeatureCounts[]).
        /// </summary>
        public static TileIndexEntryV7 ReadTileIndexEntryV8(BinaryReader reader, int lodCount, int layerCount)
        {
            var entry = new TileIndexEntryV7
            {
                TileID = reader.ReadInt64(),
                GridX = reader.ReadInt32(),
                GridY = reader.ReadInt32(),
                Bounds = new BBox
                {
                    MinX = reader.ReadSingle(),
                    MinY = reader.ReadSingle(),
                    MaxX = reader.ReadSingle(),
                    MaxY = reader.ReadSingle()
                },
                LODs = new LODInfo[lodCount]
            };

            for (int i = 0; i < lodCount; i++)
            {
                entry.LODs[i] = new LODInfo
                {
                    FileOffset = reader.ReadInt64(),
                    CompressedSize = reader.ReadInt32(),
                    UncompressedSize = reader.ReadInt32(),
                    LayerMask = reader.ReadInt32()
                };
            }

            // lodCount × 32 B SHA-256 (Pillar 2) — zawsze obecne; pomijamy (weryfikacja osobno w MapSignatureVerifier).
            for (int i = 0; i < lodCount; i++)
                reader.ReadBytes(32);

            entry.FeatureCounts = new int[layerCount];
            for (int i = 0; i < layerCount; i++)
                entry.FeatureCounts[i] = reader.ReadInt32();

            return entry;
        }

        // BUG-076: Tile utility methods (TILE_SIZE/GetTileID/WorldToGrid/GetTileBounds)
        // moved to TileGrid.cs (canonical location). Confirmed unused — grep dla
        // BinaryFormat.GetTileID/WorldToGrid/GetTileBounds/TILE_SIZE = zero hits.

    }

    public struct BBox
    {
        public float MinX;
        public float MinY;
        public float MaxX;
        public float MaxY;

        public bool IsValid => MinX <= MaxX && MinY <= MaxY;

        public static BBox Empty => new BBox { MinX = float.MaxValue, MinY = float.MaxValue, MaxX = float.MinValue, MaxY = float.MinValue };

        public void Expand(float x, float y)
        {
            if (x < MinX) MinX = x;
            if (x > MaxX) MaxX = x;
            if (y < MinY) MinY = y;
            if (y > MaxY) MaxY = y;
        }

        public void Expand(BBox other)
        {
            if (!other.IsValid) return;
            Expand(other.MinX, other.MinY);
            Expand(other.MaxX, other.MaxY);
        }

        public bool Intersects(BBox other)
        {
            return !(MaxX < other.MinX || MinX > other.MaxX || MaxY < other.MinY || MinY > other.MaxY);
        }
    }
}