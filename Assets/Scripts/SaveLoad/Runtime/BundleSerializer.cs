using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-6: Serializacja SaveBundle ↔ binary stream (gzip + JSON).
    ///
    /// Format na dysku (binary):
    ///   gzip(
    ///     "RMSAVE\0" magic (7 bytes)
    ///   + "\x01" version byte (1 byte) — bumpowane gdy zmienia się format
    ///   + length-prefixed manifest JSON (uint32 LE + bytes)
    ///   + length-prefixed module count (uint32 LE)
    ///   + (length-prefixed moduleId + length-prefixed JSON) × count
    ///   )
    ///
    /// Po dekompresji plik jest binary (nie czytelny tekstowo), ale każdy moduł
    /// można wyciągnąć osobno przez parser. Decision: nie używamy tar bo .NET
    /// nie ma built-in tar, a third-party byłaby kolejna zależność.
    ///
    /// Inwariant: ToBytes(FromBytes(b)) == b dla validnych bundle'ów.
    /// </summary>
    public static class BundleSerializer
    {
        private static readonly byte[] Magic = Encoding.UTF8.GetBytes("RMSAVE\0");
        private const byte FormatVersion = 1;

        // Limity ochronne przed maliciously crafted bundle (lub akcydentalny corruption).
        // Bez tych guardów `r.ReadUInt32()` * `int.MaxValue` mogłoby alokować arbitrary
        // memory podczas Deserialize → OOM crash gry.
        private const uint MaxModuleCount = 1024;             // 11 realnie + duża bufer dla DLC
        private const uint MaxLengthPrefixedStringBytes = 64 * 1024 * 1024; // 64 MB per string

        /// <summary>Serializuje bundle do bytes (gzipped binary). Zawiera manifest + wszystkie moduły.
        /// HMAC manifestu MUSI być wyliczone PRZED wywołaniem (HmacService.ComputeHmac).</summary>
        public static byte[] Serialize(SaveBundle bundle)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));

            using var memOut = new MemoryStream();
            using (var gz = new GZipStream(memOut, CompressionLevel.Optimal, leaveOpen: true))
            using (var w = new BinaryWriter(gz, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(Magic);
                w.Write(FormatVersion);

                // Manifest as JSON
                string manifestJson = JsonConvert.SerializeObject(bundle.Manifest, Formatting.None);
                WriteLengthPrefixedString(w, manifestJson);

                // Module count
                w.Write((uint)bundle.Modules.Count);

                // Each module: id + JSON
                foreach (var kv in bundle.Modules)
                {
                    WriteLengthPrefixedString(w, kv.Key);
                    string moduleJson = kv.Value?.ToString(Formatting.None) ?? "{}";
                    WriteLengthPrefixedString(w, moduleJson);
                }
            }
            return memOut.ToArray();
        }

        /// <summary>Deserializuje bundle z bytes (gzipped binary).
        /// Throws na corrupt magic / unknown version / IO error / JSON parse error.</summary>
        public static SaveBundle Deserialize(byte[] bytes)
        {
            if (bytes == null || bytes.Length < Magic.Length + 1)
                throw new InvalidDataException("Bundle bytes too short");

            using var memIn = new MemoryStream(bytes);
            using var gz = new GZipStream(memIn, CompressionMode.Decompress);
            using var r = new BinaryReader(gz, Encoding.UTF8);

            // Magic check
            byte[] magic = r.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length)
                throw new InvalidDataException("Bundle truncated (no magic)");
            for (int i = 0; i < Magic.Length; i++)
                if (magic[i] != Magic[i])
                    throw new InvalidDataException($"Bundle magic mismatch (byte {i}: expected {Magic[i]:X2}, got {magic[i]:X2})");

            // Version check
            byte version = r.ReadByte();
            if (version != FormatVersion)
                throw new InvalidDataException($"Bundle format version {version} not supported (expected {FormatVersion})");

            // Manifest
            string manifestJson = ReadLengthPrefixedString(r);
            var manifest = JsonConvert.DeserializeObject<SaveManifest>(manifestJson)
                           ?? throw new InvalidDataException("Manifest deserialize null");

            var bundle = new SaveBundle { Manifest = manifest };

            // Modules
            uint moduleCount = r.ReadUInt32();
            if (moduleCount > MaxModuleCount)
                throw new InvalidDataException($"Bundle module count {moduleCount} exceeds max {MaxModuleCount} — corrupt or malicious bundle.");
            for (uint i = 0; i < moduleCount; i++)
            {
                string moduleId = ReadLengthPrefixedString(r);
                string moduleJson = ReadLengthPrefixedString(r);
                JObject obj;
                try { obj = JObject.Parse(moduleJson); }
                catch (JsonReaderException ex)
                {
                    throw new InvalidDataException($"Module '{moduleId}' JSON parse failed: {ex.Message}");
                }
                bundle.Modules[moduleId] = obj;
            }

            return bundle;
        }

        /// <summary>Deserializuje TYLKO manifest z bundle bytes — pomija parsowanie module sections.
        /// Używane przez ListAsync gdzie potrzebujemy tylko meta-info (slot name, savedAt, gameVersion).
        ///
        /// Optymalizacja: GzipStream dekompresuje streaming, więc czytamy z niego TYLKO pierwsze
        /// kilkanaście-set bajtów (magic + version + manifest length-prefix + manifest JSON), reszta
        /// pliku jest dekompresowana lazily TYLKO jeśli czytamy dalej (czego tu nie robimy).
        /// Plus omijamy `JObject.Parse` modułów (~ms × moduł × N save'ów = sekundy).
        ///
        /// Wcześniej (M13-6 simplification) ListAsync wywoływał pełen `Deserialize` — dla 100
        /// save'ów ~10s zamiast ~100ms.</summary>
        public static SaveManifest DeserializeManifestOnly(byte[] bytes)
        {
            if (bytes == null || bytes.Length < Magic.Length + 1)
                throw new InvalidDataException("Bundle bytes too short");

            using var memIn = new MemoryStream(bytes);
            using var gz = new GZipStream(memIn, CompressionMode.Decompress);
            using var r = new BinaryReader(gz, Encoding.UTF8);

            // Magic + version sanity check (skip module count + module sections)
            byte[] magic = r.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length)
                throw new InvalidDataException("Bundle truncated (no magic)");
            for (int i = 0; i < Magic.Length; i++)
                if (magic[i] != Magic[i])
                    throw new InvalidDataException($"Bundle magic mismatch (byte {i})");

            byte version = r.ReadByte();
            if (version != FormatVersion)
                throw new InvalidDataException($"Bundle format version {version} not supported (expected {FormatVersion})");

            string manifestJson = ReadLengthPrefixedString(r);
            return JsonConvert.DeserializeObject<SaveManifest>(manifestJson)
                   ?? throw new InvalidDataException("Manifest deserialize null");
        }

        // ── Helpers ────────────────────────────────────────────

        private static void WriteLengthPrefixedString(BinaryWriter w, string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s ?? "");
            w.Write((uint)bytes.Length);
            w.Write(bytes);
        }

        private static string ReadLengthPrefixedString(BinaryReader r)
        {
            uint len = r.ReadUInt32();
            if (len > MaxLengthPrefixedStringBytes)
                throw new InvalidDataException($"Length-prefixed string too long ({len} bytes, max {MaxLengthPrefixedStringBytes / (1024*1024)}MB)");
            byte[] bytes = r.ReadBytes((int)len);
            if (bytes.Length != len)
                throw new InvalidDataException($"Read {bytes.Length} bytes, expected {len}");
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
