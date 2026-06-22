using System.Collections.Generic;
using RailwayManager.Core;

namespace DepotSystem.Schemas.Generators
{
    /// <summary>
    /// Statyczny registry mapujący <see cref="TurnoutSchemaCategory"/> na konkretny generator.
    /// Używany przez <c>TurnoutSchemaPlacer</c> (MD-3+) do dispatch'u "wygeneruj geometrię"
    /// na podstawie pola <c>category</c> w <see cref="TurnoutSchemaDefinition"/>.
    ///
    /// Snapshot (Custom) NIE używa generatora — geometria jest zaszyta literalnie w JSON.
    /// </summary>
    public static class TurnoutSchemaGeneratorRegistry
    {
        private static readonly Dictionary<TurnoutSchemaCategory, ITurnoutSchemaGenerator> _generators
            = new Dictionary<TurnoutSchemaCategory, ITurnoutSchemaGenerator>
            {
                { TurnoutSchemaCategory.Ladder, new LadderSchemaGenerator() },
                { TurnoutSchemaCategory.Throat, new ThroatSchemaGenerator() },
                { TurnoutSchemaCategory.Scissors, new ScissorsSchemaGenerator() },
                { TurnoutSchemaCategory.Trapez, new TrapezSchemaGenerator() },
            };

        /// <summary>
        /// Zwraca generator dla podanej kategorii. Null gdy kategoria nie ma generatora
        /// (np. Custom = snapshot, geometria w JSON).
        /// </summary>
        public static ITurnoutSchemaGenerator Get(TurnoutSchemaCategory category)
        {
            if (_generators.TryGetValue(category, out var gen)) return gen;
            return null;
        }

        /// <summary>
        /// Generuje geometrię z definicji. Wybiera generator per category, normalizuje parameters,
        /// wywołuje Generate. Zwraca null gdy definicja jest snapshot (geometria gdzie indziej)
        /// lub gdy generator nie istnieje.
        /// </summary>
        public static SchemaGeometry GenerateFromDefinition(TurnoutSchemaDefinition def)
        {
            if (def == null)
            {
                Log.Error("[TurnoutSchemaGeneratorRegistry] definition is null");
                return null;
            }
            if (!def.IsGenerative)
            {
                Log.Warn($"[TurnoutSchemaGeneratorRegistry] '{def.id}' is snapshot, not generative — use snapshot deserializer instead");
                return null;
            }
            if (def.parameters == null)
            {
                Log.Error($"[TurnoutSchemaGeneratorRegistry] '{def.id}' has no parameters");
                return null;
            }

            var category = def.ParseCategory();
            var generator = Get(category);
            if (generator == null)
            {
                Log.Error($"[TurnoutSchemaGeneratorRegistry] No generator for category '{category}'");
                return null;
            }

            // Normalize parameters (expand shorthand → array)
            int turnoutCount = generator.ComputeTurnoutCount(def.parameters.trackCount);
            def.parameters.Normalize(turnoutCount);

            return generator.Generate(def.parameters);
        }
    }
}
