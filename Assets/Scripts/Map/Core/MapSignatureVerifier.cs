using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using formap;
using RailwayManager.Core;

namespace MapSystem
{
    public enum MapSignatureResult
    {
        Valid,                   // podpis indexu OK (+ wszystkie hashe bloków OK gdy checkAllBlocks)
        Unsigned,                // signatureLength == 0
        PublicKeyNotConfigured,  // plik podpisany, ale brak embedded klucza
        SignatureInvalid,        // podpis Ed25519 indexu nie zgadza się (lub plik ucięty)
        BlockHashMismatch,       // podpis OK, ale ≥1 blok ma niezgodny SHA-256
        NotV8                    // magic != FORMAP04
    }

    /// <summary>
    /// Weryfikuje podpisaną mapę v8 (FORMAP04, Pillar 2). Domyślnie (otwarcie mapy) sprawdza TYLKO
    /// (1) podpis Ed25519 nad regionem indexu [indexOffset .. fileLen-signatureLength) — tani (~2,2 MB dla Polski),
    /// daje provenance + autentyczność listy hashy (hashe bloków SĄ w podpisanym indexie).
    /// checkAllBlocks=true dodaje (2) per-LOD SHA-256 KAŻDEGO bloku (audyt/test) — to czyta CAŁY plik (~11 GB),
    /// więc NIGDY przy normalnym LoadMap (byłby mega lag co wejście do gry). Klucz publiczny WBUDOWANY.
    /// Mirror D:\Gry\formap\BinaryFormatV8.VerifySignatureV8; zwraca wynik strukturalny.
    /// </summary>
    public static class MapSignatureVerifier
    {
        // Klucz publiczny Ed25519 (32 B) produkcyjnej mapy v8, z `formap --gen-key` (D:/Gry/keys/poland.pub).
        // Hex z D:\Gry\formap\docs\rm-v8-integration.md §4. Przy rotacji pary kluczy — podmień ten hex.
        // Prywatny klucz (poland.priv) NIGDY nie trafia do repo.
        private const string EmbeddedPublicKeyHex = "8d045ef753730aa48e7e2118aa394ac104800ad117c804b5546d7c7fc74afbac";
        private static readonly byte[] EmbeddedPublicKey = HexToBytes(EmbeddedPublicKeyHex);

        private static byte[] HexToBytes(string hex)
        {
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++)
                b[i] = System.Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        private static bool IsConfigured(byte[] key)
        {
            if (key == null || key.Length != MapSigning.PublicKeySize) return false;
            foreach (var b in key) if (b != 0) return true;
            return false;
        }

        /// <summary>
        /// Weryfikuje plik v8 pod <paramref name="path"/>. <paramref name="overridePublicKey"/> != null podmienia
        /// wbudowany klucz (używane przez testy EditMode z parą kluczy testowych).
        /// </summary>
        public static MapSignatureResult VerifyFile(string path, out string detail, byte[] overridePublicKey = null,
            bool checkAllBlocks = false)
        {
            byte[] pub = overridePublicKey ?? EmbeddedPublicKey;
            bool configured = overridePublicKey != null || IsConfigured(EmbeddedPublicKey);

            using var input = File.OpenRead(path);
            using var reader = new BinaryReader(input);

            input.Position = 0;
            string magic = Encoding.ASCII.GetString(reader.ReadBytes(8));
            if (magic != BinaryFormat.MagicV8)
            {
                detail = $"magic '{magic}' != {BinaryFormat.MagicV8}";
                return MapSignatureResult.NotV8;
            }

            BinaryFormat.ReadHeaderV8(reader, out _, out _, out _, out _, out int totalTiles,
                out long indexOffset, out int layerCount, out int lodCount, out _, out int signatureLength);

            if (signatureLength <= 0)
            {
                detail = "plik niepodpisany (signatureLength=0)";
                return MapSignatureResult.Unsigned;
            }
            if (!configured)
            {
                detail = "brak embedded public key — wklej .pub z 'formap --gen-key' do MapSignatureVerifier.EmbeddedPublicKey";
                return MapSignatureResult.PublicKeyNotConfigured;
            }

            long fileLen = input.Length;
            long indexEnd = fileLen - signatureLength;
            if (indexEnd < indexOffset)
            {
                detail = $"plik ucięty (indexEnd {indexEnd} < indexOffset {indexOffset})";
                return MapSignatureResult.SignatureInvalid;
            }

            // (1) podpis Ed25519 nad całym regionem indexu
            input.Position = indexOffset;
            byte[] indexBytes = reader.ReadBytes((int)(indexEnd - indexOffset));
            input.Position = indexEnd;
            byte[] sig = reader.ReadBytes(signatureLength);
            if (!MapSigning.Verify(pub, indexBytes, sig))
            {
                detail = "podpis Ed25519 indexu nie zgadza się";
                return MapSignatureResult.SignatureInvalid;
            }

            // Przy OTWARCIU mapy kończymy na podpisie indexu — tani, daje provenance + autentyczność listy hashy.
            // NIE hashujemy bloków tutaj: czytałoby cały ~11 GB plik synchronicznie przy każdym LoadMap (mega lag).
            // Content-integrity per blok = opcjonalny audyt (checkAllBlocks=true) lub przyszła weryfikacja w streamingu.
            if (!checkAllBlocks)
            {
                detail = $"OK (podpis indexu, {totalTiles} kafli)";
                return MapSignatureResult.Valid;
            }

            // (2) per-LOD SHA-256 bloków vs hash w indexie — najpierw sparsuj index do listy bloków, potem hashuj.
            input.Position = indexOffset;
            var blocks = new List<(long off, int csz, byte[] hash)>();
            for (int t = 0; t < totalTiles; t++)
            {
                reader.ReadInt64();                                   // TileID
                reader.ReadInt32(); reader.ReadInt32();               // GridX, GridY
                reader.ReadSingle(); reader.ReadSingle();             // bounds Min
                reader.ReadSingle(); reader.ReadSingle();             // bounds Max
                var offs = new long[lodCount];
                var cszs = new int[lodCount];
                for (int lod = 0; lod < lodCount; lod++)
                {
                    offs[lod] = reader.ReadInt64();
                    cszs[lod] = reader.ReadInt32();
                    reader.ReadInt32();                               // UncompressedSize
                    reader.ReadInt32();                               // LayerMask
                }
                for (int lod = 0; lod < lodCount; lod++)
                {
                    byte[] h = reader.ReadBytes(32);
                    if (cszs[lod] > 0) blocks.Add((offs[lod], cszs[lod], h));
                }
                for (int k = 0; k < layerCount; k++) reader.ReadInt32(); // feature counts
            }

            int mismatches = 0;
            using (var sha = SHA256.Create())
            {
                foreach (var (off, csz, expected) in blocks)
                {
                    input.Position = off;
                    byte[] comp = reader.ReadBytes(csz);
                    byte[] actual = sha.ComputeHash(comp);
                    if (!actual.AsSpan().SequenceEqual(expected)) mismatches++;
                }
            }

            if (mismatches > 0)
            {
                detail = $"{mismatches}/{blocks.Count} bloków z niezgodnym SHA-256";
                return MapSignatureResult.BlockHashMismatch;
            }

            detail = $"OK (podpis indexu + {blocks.Count} bloków SHA-256)";
            return MapSignatureResult.Valid;
        }
    }
}
