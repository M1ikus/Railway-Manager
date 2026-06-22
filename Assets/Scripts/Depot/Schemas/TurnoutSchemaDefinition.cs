using System;
using UnityEngine;
using DepotSystem.Schemas.Snapshot;
using RailwayManager.Core;

namespace DepotSystem.Schemas
{
    /// <summary>
    /// Pełna definicja schematu głowicy rozjazdowej (deserializowalna z JSON).
    ///
    /// Format polimorficzny — pole <see cref="type"/> dispatch'uje:
    /// - <c>"generative"</c> → używa <see cref="parameters"/> + generator per <see cref="category"/>
    /// - <c>"snapshot"</c> → używa <see cref="snapshotGeometry"/> (literal geometria, MD-7+)
    ///
    /// Plik źródłowy: <c>Resources/DepotSchemas/Builtin/*.rmschema.json</c> (built-in)
    /// lub <c>%AppData%/RailwayManager/CustomSchemas/*.rmschema.json</c> (user custom).
    /// </summary>
    [Serializable]
    public class TurnoutSchemaDefinition
    {
        // ── Wersjonowanie format'u ────────────────────────
        public int schemaFormatVersion = 1;

        // ── Identyfikacja ─────────────────────────────────
        public string id = "";
        public string name = "";
        public string description = "";

        // ── Klasyfikacja ──────────────────────────────────
        public string category = "Ladder";   // mapuje na TurnoutSchemaCategory enum
        public string type = "generative";   // "generative" | "snapshot"

        // ── Metadata Workshop-ready ───────────────────────
        public string author = "";
        public string[] tags;
        public string version = "1.0";
        public string createdAt = "";        // ISO 8601 string
        public string modifiedAt = "";       // ISO 8601 string
        public long workshopId = 0;          // 0 = nie published na Workshop, >0 = ID Steam

        // ── Preview thumbnail ─────────────────────────────
        public string previewPngBase64 = "";  // embedded 256×256 PNG, base64

        // ── Generative payload ────────────────────────────
        public SchemaParameters parameters;

        // ── Snapshot payload (MD-8+) ───────────────────────
        public SnapshotGeometry snapshotGeometry;

        /// <summary>
        /// Parsuje <see cref="category"/> string na enum. Fallback Custom jeśli nieznana.
        /// </summary>
        public TurnoutSchemaCategory ParseCategory()
        {
            if (Enum.TryParse<TurnoutSchemaCategory>(category, ignoreCase: true, out var result))
                return result;
            Log.Warn($"[TurnoutSchemaDefinition] Unknown category '{category}', fallback to Custom");
            return TurnoutSchemaCategory.Custom;
        }

        /// <summary>
        /// Sprawdza czy to schemat generative (parametryzowany).
        /// </summary>
        public bool IsGenerative => type == "generative";

        /// <summary>
        /// Sprawdza czy to snapshot (literalna geometria, MD-7+).
        /// </summary>
        public bool IsSnapshot => type == "snapshot";

        /// <summary>
        /// Deserializuje z JSON string'a. Używa Unity JsonUtility (spójność z FleetCatalog).
        /// </summary>
        public static TurnoutSchemaDefinition FromJson(string json)
        {
            try
            {
                var def = JsonUtility.FromJson<TurnoutSchemaDefinition>(json);
                if (def == null)
                {
                    Log.Error("[TurnoutSchemaDefinition] FromJson returned null");
                    return null;
                }
                if (def.parameters == null && def.IsGenerative)
                {
                    Log.Warn($"[TurnoutSchemaDefinition] Generative schema '{def.id}' has no parameters, creating defaults");
                    def.parameters = new SchemaParameters();
                }
                return def;
            }
            catch (Exception e)
            {
                Log.Error($"[TurnoutSchemaDefinition] FromJson failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Serializuje do JSON string'a (pretty print).
        /// </summary>
        public string ToJson()
        {
            return JsonUtility.ToJson(this, prettyPrint: true);
        }
    }
}
