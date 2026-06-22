using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace formap
{
    /// <summary>
    /// v8 (FORMAP04) dekoder bloku tile-LOD — port z D:\Gry\formap\BinaryFormatV8.DecodeBlock.
    ///
    /// <paramref name="body"/> = ROZPAKOWANY blok (po LZ4 <c>LZ4Pickler.Unpickle</c>):
    ///   varint structLen + struct section + shuffled X plane + shuffled Y plane.
    /// Struct section: per warstwa <c>int32 layerType, int32 featureCount</c>, potem per feature rekord
    /// <see cref="FeatureCodecV8.ReadFeatureStructure"/>. X/Y planes: wszystkie wierzchołki bloku w kolejności
    /// feature, X i Y jako dwa osobne, byte-shuffled (stride 4) plany float32. totalVerts = suma vertexCount
    /// (NIE zapisane — odtwarzane z sumy). bbox liczony z wierzchołków per feature (ComputeBoundingBox).
    /// </summary>
    public static class BlockDecoderV8
    {
        /// <summary>Dekoduje CAŁY blok (wszystkie warstwy) — dla normalnego streamingu renderu.</summary>
        public static Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> DecodeBlock(
            byte[] body, IReadOnlyList<string> table)
        {
            return DecodeBlock(body, table, null);
        }

        /// <summary>
        /// Dekoduje blok, opcjonalnie filtrując warstwy. Gdy <paramref name="wantLayer"/> != null, warstwy dla
        /// których predykat zwraca false są przewijane bez alokacji (SkipFeatureStructure) i ich wierzchołki NIE są
        /// materializowane (tylko przesuwany indeks poola). Cały blok i tak musi być przeczytany — pooling
        /// uniemożliwia per-feature seek (jak v7 SkipFeatureBytes), ale unikamy alokacji ciężkich warstw (Buildings).
        /// </summary>
        public static Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> DecodeBlock(
            byte[] body, IReadOnlyList<string> table, Func<BinaryFormat.LayerType, bool> wantLayer)
        {
            var dict = new Dictionary<BinaryFormat.LayerType, List<MeshGeometry>>();
            var order = new List<(MeshGeometry g, int vc)>(); // g == null → warstwa pominięta, tylko advance poola

            using var ms = new MemoryStream(body, false);
            var br = new BinaryReader(ms);

            int structLen = (int)FeatureCodecV8.ReadVarint(br);
            long structEnd = ms.Position + structLen;
            while (ms.Position < structEnd)
            {
                int layerType = br.ReadInt32();
                int featCount = br.ReadInt32();
                bool keep = wantLayer == null || wantLayer((BinaryFormat.LayerType)layerType);

                List<MeshGeometry> list = null;
                if (keep)
                {
                    list = new List<MeshGeometry>(featCount);
                    dict[(BinaryFormat.LayerType)layerType] = list;
                }

                for (int f = 0; f < featCount; f++)
                {
                    if (keep)
                    {
                        var g = FeatureCodecV8.ReadFeatureStructure(br, table, out int vc);
                        list.Add(g);
                        order.Add((g, vc));
                    }
                    else
                    {
                        int vc = FeatureCodecV8.SkipFeatureStructure(br);
                        order.Add((null, vc));
                    }
                }
            }

            long totalVerts = 0;
            foreach (var (_, vc) in order) totalVerts += vc;

            byte[] xB = FeatureCodecV8.Unshuffle(br.ReadBytes((int)totalVerts * 4), 4);
            byte[] yB = FeatureCodecV8.Unshuffle(br.ReadBytes((int)totalVerts * 4), 4);

            int vi = 0;
            foreach (var (g, vc) in order)
            {
                if (g != null)
                {
                    for (int k = 0; k < vc; k++)
                    {
                        int o = (vi + k) * 4;
                        g.Vertices.Add(new Vector2(BitConverter.ToSingle(xB, o), BitConverter.ToSingle(yB, o)));
                    }
                    g.ComputeBoundingBox(); // bbox NIE zapisany w v8 — liczony z wierzchołków (bit-exact)
                }
                vi += vc;
            }
            return dict;
        }
    }
}
