using UnityEngine;

namespace DepotSystem.Schemas
{
    /// <summary>
    /// Mapowanie nazw rozjazdów (string w JSON) na <see cref="TurnoutData.TurnoutDefinition"/>.
    ///
    /// Format JSON używa nazw stringowych (np. <c>"R190"</c>) zamiast referencji do struct'ów —
    /// stabilne forward-compat (gdy dorzucimy R300 1:7.5 czy R500 1:9, JSON-y starych user
    /// schematów dalej działają, dopóki nazwy "R190"/"R300" nie znikną).
    /// </summary>
    public static class SchemaTurnoutType
    {
        public const string R190 = "R190";
        public const string R300 = "R300";
        public const string Crossover_R190 = "Crossover_R190";

        /// <summary>
        /// Rozwiązuje string na konkretną definicję rozjazdu.
        /// Zwraca null gdy nazwa nieznana.
        /// </summary>
        public static TurnoutData.TurnoutDefinition? Resolve(string typeName)
        {
            switch (typeName)
            {
                case R190: return TurnoutData.R190_1_9;
                case R300: return TurnoutData.R300_1_9;
                case Crossover_R190: return TurnoutData.Crossover_R190;
                default: return null;
            }
        }

        /// <summary>
        /// Sprawdza czy nazwa to znany typ rozjazdu.
        /// </summary>
        public static bool IsKnown(string typeName)
        {
            return Resolve(typeName).HasValue;
        }
    }
}
