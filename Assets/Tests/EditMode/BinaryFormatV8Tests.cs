using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using formap;
using MapSystem;
using K4os.Compression.LZ4;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// Round-trip readera v8 (FORMAP04) przeciw syntetycznemu enkoderowi <see cref="V8TestData"/>.
    /// Waliduje produkcyjne prymitywy: FeatureCodecV8 (varint/unshuffle/struct), BlockDecoderV8 (pooled SoA + bbox
    /// derived + filtr warstw), BinaryFormat.ReadHeaderV8/ReadTileIndexEntryV8 (stride z hashami SHA-256, brak
    /// length-prefiksu), MapSigning (Ed25519) i MapSignatureVerifier (podpis indexu + per-LOD SHA-256, detekcja
    /// manipulacji). Nie wymaga realnego poland-v8.bin — gdy plik dojdzie, dochodzi smoke + cross-check formap.
    /// </summary>
    public class BinaryFormatV8Tests
    {
        // ───────────────────────── prymitywy ─────────────────────────

        [Test]
        public void Varint_RoundTrip()
        {
            uint[] vals = { 0u, 1u, 127u, 128u, 255u, 300u, 16383u, 16384u, 70000u, 1_000_000u, uint.MaxValue };
            foreach (var v in vals)
            {
                using var ms = new MemoryStream();
                using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) V8TestData.WriteVarint(w, v);
                ms.Position = 0;
                using var r = new BinaryReader(ms);
                Assert.AreEqual(v, FeatureCodecV8.ReadVarint(r), $"varint {v}");
            }
        }

        [Test]
        public void Unshuffle_InvertsShuffle()
        {
            var data = new byte[4 * 1000];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)((i * 2654435761u) >> 24); // deterministyczny pseudo-random
            var round = FeatureCodecV8.Unshuffle(V8TestData.Shuffle(data, 4), 4);
            CollectionAssert.AreEqual(data, round, "shuffle→unshuffle musi być bit-exact");
        }

        [Test]
        public void Header_RoundTrip_Consumes128Bytes()
        {
            using var ms = new MemoryStream();
            var bounds = new BBox { MinX = 1f, MinY = 2f, MaxX = 3f, MaxY = 4f };
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
                V8TestData.WriteHeader(w, bounds, 7, 9, 11, 12345L, 13, 6, 0, 64);

            ms.Position = 0;
            using var r = new BinaryReader(ms);
            r.ReadBytes(8); // magic (konsumuje caller)
            BinaryFormat.ReadHeaderV8(r, out float ts, out BBox gb, out int tx, out int ty, out int tt,
                out long io, out int lc, out int lodc, out int ct, out int sl);

            Assert.AreEqual(V8TestData.TileSize, ts, 0f);
            Assert.AreEqual(1f, gb.MinX, 0f); Assert.AreEqual(2f, gb.MinY, 0f);
            Assert.AreEqual(3f, gb.MaxX, 0f); Assert.AreEqual(4f, gb.MaxY, 0f);
            Assert.AreEqual(7, tx); Assert.AreEqual(9, ty); Assert.AreEqual(11, tt);
            Assert.AreEqual(12345L, io);
            Assert.AreEqual(13, lc); Assert.AreEqual(6, lodc); Assert.AreEqual(0, ct); Assert.AreEqual(64, sl);
            Assert.AreEqual(BinaryFormat.HeaderSizeV8, ms.Position, "header musi mieć dokładnie 128 B (reserved 60)");
        }

        // ───────────────────────── blok ─────────────────────────

        [Test]
        public void DecodeBlock_BitExact()
        {
            var table = new List<string>();
            var map = new Dictionary<string, int>();
            int Intern(string s) { if (!map.TryGetValue(s, out var i)) { i = table.Count; table.Add(s); map[s] = i; } return i; }

            var lod = new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>
            {
                { BinaryFormat.LayerType.Railways, new List<MeshGeometry> { V8TestData.Rail() } },
                { BinaryFormat.LayerType.Buildings, new List<MeshGeometry> { V8TestData.PolyHole(), V8TestData.Poly() } },
                { BinaryFormat.LayerType.POIs, new List<MeshGeometry> { V8TestData.Point() } },
            };

            byte[] body = V8TestData.EncodeBlockBody(lod, Intern, out int _);
            var dict = BlockDecoderV8.DecodeBlock(body, table);
            AssertLayersEqual(lod, dict);
        }

        [Test]
        public void DecodeBlock_Filtered_SkipsUnwanted_ButKeepsRightVertices()
        {
            var table = new List<string>();
            var map = new Dictionary<string, int>();
            int Intern(string s) { if (!map.TryGetValue(s, out var i)) { i = table.Count; table.Add(s); map[s] = i; } return i; }

            // Buildings wstawione PRZED Railways → ich wierzchołki są wcześniej w poolu.
            // Filtr pomija Buildings, ale musi przesunąć indeks poola, żeby Railways trafiły poprawnie.
            var lod = new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>
            {
                { BinaryFormat.LayerType.Buildings, new List<MeshGeometry> { V8TestData.PolyHole(), V8TestData.Poly() } },
                { BinaryFormat.LayerType.Railways, new List<MeshGeometry> { V8TestData.Rail() } },
            };
            byte[] body = V8TestData.EncodeBlockBody(lod, Intern, out int _);

            var dict = BlockDecoderV8.DecodeBlock(body, table, lt => lt == BinaryFormat.LayerType.Railways);

            Assert.IsTrue(dict.ContainsKey(BinaryFormat.LayerType.Railways), "Railways powinno zostać");
            Assert.IsFalse(dict.ContainsKey(BinaryFormat.LayerType.Buildings), "Buildings powinno być pominięte");
            // bit-exact match Railways = dowód że vi przeskoczyło wierzchołki pominiętych Buildings
            AssertFeatureEqual(V8TestData.Rail(), dict[BinaryFormat.LayerType.Railways][0]);
        }

        // ───────────────────────── podpis ─────────────────────────

        [Test]
        public void Signature_Sign_Verify_DetectsTamper()
        {
            MapSigning.GenerateKeypair(out byte[] priv, out byte[] pub);
            Assert.AreEqual(MapSigning.PrivateKeySize, priv.Length);
            Assert.AreEqual(MapSigning.PublicKeySize, pub.Length);

            byte[] data = Encoding.UTF8.GetBytes("v8 index region bytes — provenance test");
            byte[] sig = MapSigning.Sign(priv, data);
            Assert.AreEqual(MapSigning.SignatureSize, sig.Length);
            Assert.IsTrue(MapSigning.Verify(pub, data, sig), "świeży podpis musi się zweryfikować");

            byte[] d2 = (byte[])data.Clone(); d2[0] ^= 0xFF;
            Assert.IsFalse(MapSigning.Verify(pub, d2, sig), "zmodyfikowane dane → podpis nieważny");

            byte[] s2 = (byte[])sig.Clone(); s2[0] ^= 0xFF;
            Assert.IsFalse(MapSigning.Verify(pub, data, s2), "zmodyfikowany podpis → nieważny");
        }

        // ───────────────────────── pełny plik ─────────────────────────

        [Test]
        public void FullFile_DecodeRoundTrip_BitExact()
        {
            var tiles = V8TestData.MakeSampleTiles();
            var bounds = new BBox { MinX = 0, MinY = 0, MaxX = 2 * V8TestData.TileSize, MaxY = V8TestData.TileSize };
            byte[] file = V8TestData.BuildFile(tiles, 2, 1, bounds, privKey: null, lodCount: 2);

            var parsed = ParseFileV8(file);

            // tile A (id=1): LOD0 {Buildings, Railways, POIs}, LOD1 {Railways}
            AssertLayersEqual(tiles[0].Lods[0], parsed[1][0]);
            AssertLayersEqual(tiles[0].Lods[1], parsed[1][1]);
            // tile B (id=2): LOD0 {Forests}, LOD1 pusty — dowód poprawnego stride'u indexu (2. wpis czytany dobrze)
            AssertLayersEqual(tiles[1].Lods[0], parsed[2][0]);
            Assert.AreEqual(0, parsed[2][1].Count, "pusty LOD → pusty słownik");
        }

        [Test]
        public void FullFile_SignedVerify_AndTamperDetection()
        {
            MapSigning.GenerateKeypair(out byte[] priv, out byte[] pub);
            var tiles = V8TestData.MakeSampleTiles();
            var bounds = new BBox { MinX = 0, MinY = 0, MaxX = 2 * V8TestData.TileSize, MaxY = V8TestData.TileSize };
            byte[] signed = V8TestData.BuildFile(tiles, 2, 1, bounds, privKey: priv, lodCount: 2);
            long idxOff = ReadIndexOffset(signed);

            string path = Path.Combine(Application.temporaryCachePath, "rm_v8_sig_test.bin");
            try
            {
                File.WriteAllBytes(path, signed);
                var ok = MapSignatureVerifier.VerifyFile(path, out string okDetail, pub);
                Assert.AreEqual(MapSignatureResult.Valid, ok, okDetail);

                // manipulacja bloku (bajt tuż przed indexem = w ostatnim niepustym bloku) → hash mismatch.
                // checkAllBlocks:true bo przy otwarciu (default) sprawdzamy tylko podpis indexu (perf) — blok byłby Valid.
                byte[] tamperBlock = (byte[])signed.Clone();
                tamperBlock[idxOff - 1] ^= 0xFF;
                File.WriteAllBytes(path, tamperBlock);
                Assert.AreEqual(MapSignatureResult.BlockHashMismatch,
                    MapSignatureVerifier.VerifyFile(path, out _, pub, checkAllBlocks: true));
                // a default (otwarcie mapy) NIE czyta bloków → tamper bloku przechodzi jako Valid (tani open path)
                Assert.AreEqual(MapSignatureResult.Valid,
                    MapSignatureVerifier.VerifyFile(path, out _, pub));

                // manipulacja indexu (pierwszy bajt regionu podpisanego) → podpis nieważny
                byte[] tamperIndex = (byte[])signed.Clone();
                tamperIndex[idxOff] ^= 0xFF;
                File.WriteAllBytes(path, tamperIndex);
                Assert.AreEqual(MapSignatureResult.SignatureInvalid,
                    MapSignatureVerifier.VerifyFile(path, out _, pub));

                // zły klucz publiczny → podpis nieważny
                MapSigning.GenerateKeypair(out _, out byte[] wrongPub);
                File.WriteAllBytes(path, signed);
                Assert.AreEqual(MapSignatureResult.SignatureInvalid,
                    MapSignatureVerifier.VerifyFile(path, out _, wrongPub));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void FullFile_Unsigned_ReportedUnsigned()
        {
            MapSigning.GenerateKeypair(out _, out byte[] pub);
            var tiles = V8TestData.MakeSampleTiles();
            var bounds = new BBox { MinX = 0, MinY = 0, MaxX = 2 * V8TestData.TileSize, MaxY = V8TestData.TileSize };
            byte[] unsigned = V8TestData.BuildFile(tiles, 2, 1, bounds, privKey: null, lodCount: 2);

            string path = Path.Combine(Application.temporaryCachePath, "rm_v8_unsigned_test.bin");
            try
            {
                File.WriteAllBytes(path, unsigned);
                Assert.AreEqual(MapSignatureResult.Unsigned, MapSignatureVerifier.VerifyFile(path, out _, pub));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ───────────────────────── helpers ─────────────────────────

        /// <summary>Parsuje plik v8 produkcyjnymi prymitywami (jak MapLoader.LoadMapV8, bez MonoBehaviour):
        /// header → string table → index (ReadTileIndexEntryV8) → per blok seek+ReadBytes(CompressedSize)+
        /// Unpickle+DecodeBlock. tileID → tablica per-LOD słowników warstw.</summary>
        private static Dictionary<long, Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>[]> ParseFileV8(byte[] file)
        {
            using var ms = new MemoryStream(file, false);
            using var r = new BinaryReader(ms);
            ms.Position = 0;
            string magic = Encoding.ASCII.GetString(r.ReadBytes(8));
            Assert.AreEqual(BinaryFormat.MagicV8, magic);
            BinaryFormat.ReadHeaderV8(r, out _, out _, out _, out _, out int totalTiles,
                out long indexOffset, out int layerCount, out int lodCount, out int compressionType, out _);
            Assert.AreEqual(0, compressionType, "test pisze LZ4-HC");

            int strCount = (int)FeatureCodecV8.ReadVarint(r);
            var table = new string[strCount];
            for (int i = 0; i < strCount; i++)
            {
                int len = (int)FeatureCodecV8.ReadVarint(r);
                table[i] = Encoding.UTF8.GetString(r.ReadBytes(len));
            }

            ms.Position = indexOffset;
            var entries = new List<BinaryFormat.TileIndexEntryV7>(totalTiles);
            for (int t = 0; t < totalTiles; t++)
                entries.Add(BinaryFormat.ReadTileIndexEntryV8(r, lodCount, layerCount));

            var result = new Dictionary<long, Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>[]>();
            foreach (var e in entries)
            {
                var lods = new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>[lodCount];
                for (int lod = 0; lod < lodCount; lod++)
                {
                    var li = e.LODs[lod];
                    if (li.CompressedSize <= 0)
                    {
                        lods[lod] = new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>();
                        continue;
                    }
                    ms.Position = li.FileOffset; // brak int32 length-prefiksu — seek + ReadBytes(CompressedSize)
                    byte[] comp = r.ReadBytes(li.CompressedSize);
                    byte[] body = LZ4Pickler.Unpickle(comp);
                    lods[lod] = BlockDecoderV8.DecodeBlock(body, table);
                }
                result[e.TileID] = lods;
            }
            return result;
        }

        private static long ReadIndexOffset(byte[] file)
        {
            using var ms = new MemoryStream(file, false);
            using var r = new BinaryReader(ms);
            r.ReadBytes(8);
            BinaryFormat.ReadHeaderV8(r, out _, out _, out _, out _, out _, out long io, out _, out _, out _, out _);
            return io;
        }

        private static void AssertLayersEqual(
            Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> exp,
            Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> got)
        {
            foreach (var kv in exp)
            {
                if (kv.Value.Count == 0) continue; // puste warstwy nie są zapisywane
                Assert.IsTrue(got.ContainsKey(kv.Key), $"brak warstwy {kv.Key}");
                Assert.AreEqual(kv.Value.Count, got[kv.Key].Count, $"liczba feature w {kv.Key}");
                for (int i = 0; i < kv.Value.Count; i++)
                    AssertFeatureEqual(kv.Value[i], got[kv.Key][i]);
            }
            foreach (var kv in got)
                Assert.IsTrue(exp.ContainsKey(kv.Key) && exp[kv.Key].Count > 0, $"nieoczekiwana warstwa {kv.Key}");
        }

        private static void AssertFeatureEqual(MeshGeometry e, MeshGeometry g)
        {
            Assert.AreEqual(e.Vertices.Count, g.Vertices.Count, "vertex count");
            for (int i = 0; i < e.Vertices.Count; i++)
            {
                Assert.AreEqual(e.Vertices[i].x, g.Vertices[i].x, 0f, $"vx[{i}]");
                Assert.AreEqual(e.Vertices[i].y, g.Vertices[i].y, 0f, $"vy[{i}]");
            }
            CollectionAssert.AreEqual(e.Indices, g.Indices, "indices");
            CollectionAssert.AreEqual(e.HoleStarts, g.HoleStarts, "holeStarts");
            CollectionAssert.AreEqual(e.SegmentIds, g.SegmentIds, "segmentIds");
            CollectionAssert.AreEqual(e.JunctionIndices, g.JunctionIndices, "junctionIndices");
            Assert.AreEqual(e.Metadata.Count, g.Metadata.Count, "metadata count");
            foreach (var kv in e.Metadata)
                Assert.IsTrue(g.Metadata.TryGetValue(kv.Key, out var v) && v == kv.Value, $"metadata '{kv.Key}'");
            // bbox derived (ComputeBoundingBox) — deterministyczny min/max tych samych wierzchołków
            Assert.AreEqual(e.BoundingBox.MinX, g.BoundingBox.MinX, 0f, "bbox.MinX");
            Assert.AreEqual(e.BoundingBox.MinY, g.BoundingBox.MinY, 0f, "bbox.MinY");
            Assert.AreEqual(e.BoundingBox.MaxX, g.BoundingBox.MaxX, 0f, "bbox.MaxX");
            Assert.AreEqual(e.BoundingBox.MaxY, g.BoundingBox.MaxY, 0f, "bbox.MaxY");
        }
    }
}
