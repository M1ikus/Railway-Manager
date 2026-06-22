using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using RailwayManager.Core;
using UnityEngine;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-6: Serializer dla PaintDefinition do shareable string'a (base64+gzip+JSON).
    /// Format pozwala graczowi skopiować malowanie i wkleić u drugiego gracza w MP czat
    /// lub w między-save'ach. Maximum ~500 znaków per pojazd typowy.
    /// </summary>
    public static class PaintSerializer
    {
        public const int CURRENT_SCHEMA_VERSION = 1;
        public const int MAX_STRIPES_PER_SEGMENT = 5;
        public const int MAX_DECALS_PER_SEGMENT = 8;
        public const int MAX_SEGMENTS = 12;

        /// <summary>
        /// Serializuje PaintDefinition do pojedynczego string'a (base64-encoded gzip-compressed JSON).
        /// Zwraca pusty string przy błędzie.
        /// </summary>
        public static string Serialize(PaintDefinition def)
        {
            if (def == null) return "";

            try
            {
                def.schemaVersion = CURRENT_SCHEMA_VERSION;
                string json = JsonUtility.ToJson(def, prettyPrint: false);

                using var ms = new MemoryStream();
                using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    gz.Write(bytes, 0, bytes.Length);
                }
                return Convert.ToBase64String(ms.ToArray());
            }
            catch (Exception e)
            {
                Log.Error($"[PaintSerializer] Serialize failed: {e.Message}");
                return "";
            }
        }

        /// <summary>
        /// Deserializuje shareable string z powrotem na PaintDefinition.
        /// Zwraca null przy błędzie (np. bad base64, bad gzip, bad JSON, schemaVersion mismatch).
        /// </summary>
        public static PaintDefinition Deserialize(string serialized)
        {
            if (string.IsNullOrEmpty(serialized)) return null;

            try
            {
                byte[] compressed = Convert.FromBase64String(serialized);
                using var msIn = new MemoryStream(compressed);
                using var gz = new GZipStream(msIn, CompressionMode.Decompress);
                using var msOut = new MemoryStream();
                gz.CopyTo(msOut);
                string json = Encoding.UTF8.GetString(msOut.ToArray());

                var def = JsonUtility.FromJson<PaintDefinition>(json);
                if (def == null)
                {
                    Log.Warn("[PaintSerializer] Deserialize: JsonUtility returned null");
                    return null;
                }

                if (def.schemaVersion > CURRENT_SCHEMA_VERSION)
                {
                    Log.Warn($"[PaintSerializer] Schema version {def.schemaVersion} > current {CURRENT_SCHEMA_VERSION} — może niekompatybilne");
                }

                if (!Validate(def, out string error))
                {
                    Log.Warn($"[PaintSerializer] Validation failed: {error}");
                    return null;
                }

                return def;
            }
            catch (Exception e)
            {
                Log.Error($"[PaintSerializer] Deserialize failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Walidacja PaintDefinition — limity stripes/decals/segments + schemaVersion.
        /// </summary>
        public static bool Validate(PaintDefinition def, out string error)
        {
            error = "";
            if (def == null) { error = "null"; return false; }
            if (def.schemaVersion < 1) { error = $"invalid schemaVersion: {def.schemaVersion}"; return false; }
            if (def.segments == null) { error = "segments == null"; return false; }
            if (def.segments.Count > MAX_SEGMENTS)
            {
                error = $"too many segments: {def.segments.Count} (max {MAX_SEGMENTS})";
                return false;
            }

            for (int i = 0; i < def.segments.Count; i++)
            {
                var seg = def.segments[i];
                if (seg == null) continue;
                if (seg.stripes != null && seg.stripes.Count > MAX_STRIPES_PER_SEGMENT)
                {
                    error = $"segment {i}: too many stripes ({seg.stripes.Count} > {MAX_STRIPES_PER_SEGMENT})";
                    return false;
                }
                if (seg.decals != null && seg.decals.Count > MAX_DECALS_PER_SEGMENT)
                {
                    error = $"segment {i}: too many decals ({seg.decals.Count} > {MAX_DECALS_PER_SEGMENT})";
                    return false;
                }
            }
            return true;
        }

        /// <summary>Tworzy domyślne malowanie (biały bottom paint, brak pasków/dekorów) dla N segmentów.</summary>
        public static PaintDefinition CreateDefault(int segmentCount, string baseColor = "#FFFFFF")
        {
            var def = new PaintDefinition
            {
                schemaVersion = CURRENT_SCHEMA_VERSION,
                applyMode = "AllSegments"
            };
            for (int i = 0; i < Mathf.Max(1, segmentCount); i++)
            {
                def.segments.Add(new SegmentPaint
                {
                    segmentIndex = i,
                    baseColor = baseColor
                });
            }
            return def;
        }
    }
}
