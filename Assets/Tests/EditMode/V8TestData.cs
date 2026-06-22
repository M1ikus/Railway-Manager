using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using formap;
using MapSystem;
using K4os.Compression.LZ4;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// Test-only syntetyczny ENKODER v8 (FORMAP04). Lustro write-side z D:\Gry\formap\BinaryFormatV8/FeatureCodecV8
    /// (tam System.Numerics.Vector2 → tu UnityEngine.Vector2), na typach RM (formap.MeshGeometry/BinaryFormat).
    /// Pozwala testom round-tripować produkcyjny READER (FeatureCodecV8/BlockDecoderV8/ReadHeaderV8/
    /// ReadTileIndexEntryV8/MapSignatureVerifier) bez realnego poland-v8.bin. Pickluje bloki tym samym
    /// K4os LZ4Pickler co reader → uczciwy round-trip łącznie z brakiem length-prefiksu.
    /// </summary>
    public static class V8TestData
    {
        public const float TileSize = 10000f;

        public sealed class TileDef
        {
            public long TileID;
            public int GridX, GridY;
            public BBox Bounds;
            public Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>[] Lods; // length == lodCount
        }

        // --- varint / shuffle (write side; ReadVarint/Unshuffle są w produkcyjnym FeatureCodecV8) ---

        public static void WriteVarint(BinaryWriter w, uint value)
        {
            while (value >= 0x80) { w.Write((byte)(value | 0x80)); value >>= 7; }
            w.Write((byte)value);
        }

        public static byte[] Shuffle(byte[] data, int stride)
        {
            int n = data.Length / stride;
            var o = new byte[n * stride];
            for (int i = 0; i < n; i++) { int s = i * stride; for (int b = 0; b < stride; b++) o[b * n + i] = data[s + b]; }
            return o;
        }

        public static void WriteFeatureStructure(BinaryWriter w, MeshGeometry g, Func<string, int> intern)
        {
            bool hasIdx = g.Indices.Count > 0, hasHole = g.HoleStarts.Count > 0, hasSeg = g.SegmentIds.Count > 0,
                 hasJunc = g.JunctionIndices.Count > 0, hasMeta = g.Metadata.Count > 0;
            bool wide = g.Vertices.Count > FeatureCodecV8.WideIndexVertexThreshold;

            byte flags = 0;
            if (hasIdx) flags |= 1; if (hasHole) flags |= 2; if (hasSeg) flags |= 4;
            if (hasJunc) flags |= 8; if (hasMeta) flags |= 16; if (wide) flags |= 32;
            w.Write(flags);

            WriteVarint(w, (uint)g.Vertices.Count);

            if (hasIdx)
            {
                WriteVarint(w, (uint)g.Indices.Count);
                if (wide) foreach (var i in g.Indices) w.Write(i);
                else foreach (var i in g.Indices) w.Write((ushort)i);
            }
            if (hasHole) { WriteVarint(w, (uint)g.HoleStarts.Count); foreach (var h in g.HoleStarts) WriteVarint(w, (uint)h); }
            if (hasSeg) { WriteVarint(w, (uint)g.SegmentIds.Count); foreach (var s in g.SegmentIds) WriteVarint(w, (uint)s); }
            if (hasJunc) { WriteVarint(w, (uint)g.JunctionIndices.Count); foreach (var j in g.JunctionIndices) WriteVarint(w, (uint)j); }
            if (hasMeta)
            {
                WriteVarint(w, (uint)g.Metadata.Count);
                foreach (var kv in g.Metadata) { WriteVarint(w, (uint)intern(kv.Key)); WriteVarint(w, (uint)intern(kv.Value)); }
            }
        }

        /// <summary>Koduje ciało bloku (UNcompressed): varint structLen + struct section + shuffled X + shuffled Y.
        /// Pusty blok → tablica zerowej długości. layerMask = OR (1&lt;&lt;layerType) po niepustych warstwach.</summary>
        public static byte[] EncodeBlockBody(Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> lodFeatures,
            Func<string, int> intern, out int layerMask)
        {
            layerMask = 0;
            var structMs = new MemoryStream();
            var sw = new BinaryWriter(structMs);
            var xMs = new MemoryStream(); var xw = new BinaryWriter(xMs);
            var yMs = new MemoryStream(); var yw = new BinaryWriter(yMs);

            foreach (var kv in lodFeatures)
            {
                if (kv.Value.Count == 0) continue;
                layerMask |= 1 << (int)kv.Key;
                sw.Write((int)kv.Key);
                sw.Write(kv.Value.Count);
                foreach (var g in kv.Value)
                {
                    WriteFeatureStructure(sw, g, intern);
                    foreach (var v in g.Vertices) { xw.Write(v.x); yw.Write(v.y); }
                }
            }
            sw.Flush(); xw.Flush(); yw.Flush();

            byte[] structBytes = structMs.ToArray();
            if (structBytes.Length == 0) return Array.Empty<byte>();

            byte[] xShuf = Shuffle(xMs.ToArray(), 4);
            byte[] yShuf = Shuffle(yMs.ToArray(), 4);

            var blockMs = new MemoryStream();
            var bw = new BinaryWriter(blockMs);
            WriteVarint(bw, (uint)structBytes.Length);
            bw.Write(structBytes);
            bw.Write(xShuf);
            bw.Write(yShuf);
            bw.Flush();
            return blockMs.ToArray();
        }

        public static void WriteHeader(BinaryWriter w, BBox globalBounds, int tilesX, int tilesY, int totalTiles,
            long indexOffset, int layerCount, int lodCount, int compressionType, int signatureLength)
        {
            w.Write(Encoding.ASCII.GetBytes(BinaryFormat.MagicV8));
            w.Write(8);
            w.Write(TileSize);
            w.Write(globalBounds.MinX); w.Write(globalBounds.MinY); w.Write(globalBounds.MaxX); w.Write(globalBounds.MaxY);
            w.Write(tilesX); w.Write(tilesY); w.Write(totalTiles);
            w.Write(indexOffset);
            w.Write(layerCount); w.Write(lodCount); w.Write(compressionType); w.Write(signatureLength);
            w.Write(new byte[60]);
        }

        /// <summary>Buduje kompletny plik v8 w pamięci. privKey != null → podpisuje (Ed25519 nad regionem indexu).</summary>
        public static byte[] BuildFile(List<TileDef> tiles, int tilesX, int tilesY, BBox globalBounds,
            byte[] privKey, int layerCount = BinaryFormat.LayerCount, int lodCount = 2)
        {
            // string table — intern wszystkich metadanych (superset).
            var table = new List<string>();
            var map = new Dictionary<string, int>();
            int Intern(string s) { if (!map.TryGetValue(s, out var i)) { i = table.Count; table.Add(s); map[s] = i; } return i; }
            foreach (var t in tiles)
                foreach (var lod in t.Lods)
                    foreach (var kv in lod)
                        foreach (var g in kv.Value)
                            foreach (var m in g.Metadata) { Intern(m.Key); Intern(m.Value); }

            int signatureLength = privKey != null ? MapSigning.SignatureSize : 0;

            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);

            WriteHeader(w, globalBounds, tilesX, tilesY, tiles.Count, 0, layerCount, lodCount, 0, signatureLength);

            WriteVarint(w, (uint)table.Count);
            foreach (var s in table) { var b = Encoding.UTF8.GetBytes(s); WriteVarint(w, (uint)b.Length); w.Write(b); }

            var zeroHash = new byte[32];
            // per tile: LODInfo[], hashes[], featureCounts[]
            var lodInfos = new BinaryFormat.LODInfo[tiles.Count][];
            var hashes = new byte[tiles.Count][][];
            var featureCounts = new int[tiles.Count][];

            using (var sha = SHA256.Create())
            {
                for (int ti = 0; ti < tiles.Count; ti++)
                {
                    var t = tiles[ti];
                    lodInfos[ti] = new BinaryFormat.LODInfo[lodCount];
                    hashes[ti] = new byte[lodCount][];
                    featureCounts[ti] = new int[layerCount];
                    for (int lod = 0; lod < lodCount; lod++)
                    {
                        var lodFeatures = t.Lods[lod] ?? new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>();
                        byte[] body = EncodeBlockBody(lodFeatures, Intern, out int layerMask);
                        if (lod == 0)
                            foreach (var kv in lodFeatures)
                                if ((int)kv.Key < layerCount) featureCounts[ti][(int)kv.Key] = kv.Value.Count;

                        long fileOffset = ms.Position;
                        int compressedSize = 0;
                        byte[] hash = zeroHash;
                        if (body.Length > 0)
                        {
                            byte[] pickle = LZ4Pickler.Pickle(body);
                            w.Write(pickle);
                            compressedSize = pickle.Length;
                            hash = sha.ComputeHash(pickle);
                        }
                        hashes[ti][lod] = hash;
                        lodInfos[ti][lod] = new BinaryFormat.LODInfo
                        {
                            FileOffset = fileOffset,
                            CompressedSize = compressedSize,
                            UncompressedSize = body.Length,
                            LayerMask = layerMask
                        };
                    }
                }
            }

            long indexOffset = ms.Position;
            for (int ti = 0; ti < tiles.Count; ti++)
            {
                var t = tiles[ti];
                w.Write(t.TileID);
                w.Write(t.GridX); w.Write(t.GridY);
                w.Write(t.Bounds.MinX); w.Write(t.Bounds.MinY); w.Write(t.Bounds.MaxX); w.Write(t.Bounds.MaxY);
                for (int lod = 0; lod < lodCount; lod++)
                {
                    w.Write(lodInfos[ti][lod].FileOffset);
                    w.Write(lodInfos[ti][lod].CompressedSize);
                    w.Write(lodInfos[ti][lod].UncompressedSize);
                    w.Write(lodInfos[ti][lod].LayerMask);
                }
                for (int lod = 0; lod < lodCount; lod++) w.Write(hashes[ti][lod]);
                for (int k = 0; k < layerCount; k++) w.Write(featureCounts[ti][k]);
            }
            long indexEnd = ms.Position;
            w.Flush();

            // przepisz header z prawdziwym indexOffset
            long endPos = ms.Position;
            ms.Position = 0;
            WriteHeader(w, globalBounds, tilesX, tilesY, tiles.Count, indexOffset, layerCount, lodCount, 0, signatureLength);
            w.Flush();
            ms.Position = endPos;

            if (privKey != null)
            {
                byte[] full = ms.ToArray();
                var indexBytes = new byte[indexEnd - indexOffset];
                Array.Copy(full, indexOffset, indexBytes, 0, indexBytes.Length);
                byte[] sig = MapSigning.Sign(privKey, indexBytes);
                w.Write(sig);
                w.Flush();
            }

            return ms.ToArray();
        }

        // --- fabryki MeshGeometry (UnityEngine.Vector2) — analogiczne do self-testów formap ---

        public static MeshGeometry Poly()
        {
            var g = new MeshGeometry();
            g.Vertices.AddRange(new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) });
            g.Indices.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
            g.Metadata["building"] = "yes"; g.Metadata["building:levels"] = "4";
            g.ComputeBoundingBox(); return g;
        }

        public static MeshGeometry Rail()
        {
            var g = new MeshGeometry();
            g.Vertices.AddRange(new[] { new Vector2(0, 0), new Vector2(5, 1), new Vector2(9, 2) });
            g.Indices.AddRange(new[] { 0, 1, 1, 2 });
            g.SegmentIds.AddRange(new[] { 42, 42 });
            g.JunctionIndices.AddRange(new[] { 0, 2 });
            g.Metadata["railway"] = "rail"; g.Metadata["maxspeed"] = "120";
            g.ComputeBoundingBox(); return g;
        }

        public static MeshGeometry Point()
        {
            var g = new MeshGeometry();
            g.Vertices.Add(new Vector2(3.3f, 4.4f));
            g.Metadata["railway"] = "station"; g.Metadata["name"] = "Test";
            g.ComputeBoundingBox(); return g;
        }

        public static MeshGeometry PolyHole()
        {
            var g = new MeshGeometry();
            g.Vertices.AddRange(new[] { new Vector2(0, 0), new Vector2(8, 0), new Vector2(8, 8), new Vector2(0, 8), new Vector2(2, 2), new Vector2(4, 2), new Vector2(4, 4), new Vector2(2, 4) });
            g.Indices.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
            g.HoleStarts.AddRange(new[] { 4 });
            g.Metadata["natural"] = "water";
            g.ComputeBoundingBox(); return g;
        }

        /// <summary>Fixture: 2 kafle, lodCount=2. A: LOD0 {Buildings, Railways, POIs}, LOD1 {Railways};
        /// B: LOD0 {Forests}, LOD1 {} (pusty blok). Ostatni NIEpusty blok = B.LOD0.</summary>
        public static List<TileDef> MakeSampleTiles()
        {
            var a = new TileDef
            {
                TileID = 1, GridX = 0, GridY = 0,
                Bounds = new BBox { MinX = 0, MinY = 0, MaxX = TileSize, MaxY = TileSize },
                Lods = new[]
                {
                    new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>
                    {
                        { BinaryFormat.LayerType.Buildings, new List<MeshGeometry> { PolyHole() } },
                        { BinaryFormat.LayerType.Railways, new List<MeshGeometry> { Rail() } },
                        { BinaryFormat.LayerType.POIs, new List<MeshGeometry> { Point() } },
                    },
                    new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>
                    {
                        { BinaryFormat.LayerType.Railways, new List<MeshGeometry> { Rail() } },
                    },
                }
            };
            var b = new TileDef
            {
                TileID = 2, GridX = 1, GridY = 0,
                Bounds = new BBox { MinX = TileSize, MinY = 0, MaxX = 2 * TileSize, MaxY = TileSize },
                Lods = new[]
                {
                    new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>
                    {
                        { BinaryFormat.LayerType.Forests, new List<MeshGeometry> { Poly() } },
                    },
                    new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>(),
                }
            };
            return new List<TileDef> { a, b };
        }
    }
}
