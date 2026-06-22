using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace formap
{
    /// <summary>
    /// v8 (FORMAP04) per-feature codec — READ side. Port 1:1 z D:\Gry\formap\FeatureCodecV8.cs
    /// (tam System.Numerics.Vector2 → tu UnityEngine.Vector2).
    ///
    /// Wierzchołki NIE są czytane tutaj — idą do block-level poola (SoA X|Y, byte-shuffle stride 4);
    /// patrz <see cref="BlockDecoderV8"/>. Rekord struktury feature:
    ///   flags(1B) — bit0 hasIndices, 1 hasHoleStarts, 2 hasSegmentIds, 3 hasJunctionIndices,
    ///               4 hasMetadata, 5 wideIndices (int32 indices vs uint16, gdy &gt; 65536 wierzchołków)
    ///   vertexCount : varint
    ///   [hasIndices]         indexCount:varint + indices × (uint16 | int32 per bit5)
    ///   [hasHoleStarts]      count:varint + values × varint
    ///   [hasSegmentIds]      count:varint + values × varint
    ///   [hasJunctionIndices] count:varint + values × varint
    ///   [hasMetadata]        count:varint + count × (keyIdx:varint, valIdx:varint)  → string table
    /// bbox NIE jest zapisany — liczony z wierzchołków przez callera (ComputeBoundingBox).
    /// </summary>
    public static class FeatureCodecV8
    {
        /// <summary>Indeksy &lt; Vertices.Count; uint16 trzyma 0..65535, więc 32-bit potrzebne dopiero
        /// gdy feature ma &gt; 65536 wierzchołków.</summary>
        public const int WideIndexVertexThreshold = 65536;

        // --- Unsigned LEB128 varint (little-endian grupy 7-bit, bit 0x80 = kontynuacja) ---
        public static uint ReadVarint(BinaryReader r)
        {
            uint result = 0; int shift = 0; byte b;
            do { b = r.ReadByte(); result |= (uint)(b & 0x7F) << shift; shift += 7; } while ((b & 0x80) != 0);
            return result;
        }

        // --- Odwrotny byte-shuffle (de-planaryzacja): wejście to `stride` planów po `n` bajtów
        //     (plan b zajmuje [b*n .. b*n+n)); wyjście składa `stride` bajtów każdej wartości obok siebie.
        //     Bit-exact, w pełni odwracalny. data.Length musi być wielokrotnością stride. ---
        public static byte[] Unshuffle(byte[] data, int stride)
        {
            int n = data.Length / stride;
            var o = new byte[n * stride];
            for (int i = 0; i < n; i++) { int s = i * stride; for (int b = 0; b < stride; b++) o[s + b] = data[b * n + i]; }
            return o;
        }

        /// <summary>Czyta rekord struktury feature. Wierzchołki dolewa caller z block poola;
        /// <paramref name="vertexCount"/> mówi ile wziąć.</summary>
        public static MeshGeometry ReadFeatureStructure(BinaryReader r, IReadOnlyList<string> table, out int vertexCount)
        {
            var g = new MeshGeometry(); // bbox liczony z wierzchołków przez callera po wypełnieniu poola

            byte flags = r.ReadByte();
            bool hasIdx = (flags & 1) != 0, hasHole = (flags & 2) != 0, hasSeg = (flags & 4) != 0,
                 hasJunc = (flags & 8) != 0, hasMeta = (flags & 16) != 0, wide = (flags & 32) != 0;

            vertexCount = (int)ReadVarint(r);

            if (hasIdx)
            {
                int ic = (int)ReadVarint(r);
                for (int i = 0; i < ic; i++) g.Indices.Add(wide ? r.ReadInt32() : r.ReadUInt16());
            }
            if (hasHole) { int n = (int)ReadVarint(r); for (int i = 0; i < n; i++) g.HoleStarts.Add((int)ReadVarint(r)); }
            if (hasSeg) { int n = (int)ReadVarint(r); for (int i = 0; i < n; i++) g.SegmentIds.Add((int)ReadVarint(r)); }
            if (hasJunc) { int n = (int)ReadVarint(r); for (int i = 0; i < n; i++) g.JunctionIndices.Add((int)ReadVarint(r)); }
            if (hasMeta)
            {
                int n = (int)ReadVarint(r);
                for (int i = 0; i < n; i++) { int k = (int)ReadVarint(r); int v = (int)ReadVarint(r); g.Metadata[table[k]] = table[v]; }
            }
            return g;
        }

        /// <summary>Przewija rekord struktury feature BEZ alokacji (dla niechcianych warstw w extraction/preview —
        /// odpowiednik v7 SkipFeatureBytes, bo pooling wierzchołków uniemożliwia per-feature seek w bloku).
        /// Konsumuje DOKŁADNIE te same bajty co <see cref="ReadFeatureStructure"/>. Zwraca vertexCount, żeby
        /// caller mógł przesunąć indeks w X/Y plane.</summary>
        public static int SkipFeatureStructure(BinaryReader r)
        {
            byte flags = r.ReadByte();
            bool hasIdx = (flags & 1) != 0, hasHole = (flags & 2) != 0, hasSeg = (flags & 4) != 0,
                 hasJunc = (flags & 8) != 0, hasMeta = (flags & 16) != 0, wide = (flags & 32) != 0;

            int vertexCount = (int)ReadVarint(r);

            if (hasIdx)
            {
                int ic = (int)ReadVarint(r);
                // indices: stała szerokość (uint16=2B / int32=4B) → seek bez czytania.
                r.BaseStream.Seek((long)ic * (wide ? 4 : 2), SeekOrigin.Current);
            }
            if (hasHole) { int n = (int)ReadVarint(r); for (int i = 0; i < n; i++) ReadVarint(r); }
            if (hasSeg) { int n = (int)ReadVarint(r); for (int i = 0; i < n; i++) ReadVarint(r); }
            if (hasJunc) { int n = (int)ReadVarint(r); for (int i = 0; i < n; i++) ReadVarint(r); }
            if (hasMeta) { int n = (int)ReadVarint(r); for (int i = 0; i < n * 2; i++) ReadVarint(r); }
            return vertexCount;
        }
    }
}
