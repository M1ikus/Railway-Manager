using UnityEngine;

namespace DepotSystem.Schemas
{
    /// <summary>
    /// Runtime POCO — pojedynczy rozjazd wygenerowany przez schema generator.
    /// W lokalnych współrzędnych schematu.
    ///
    /// Po placement'cie konwertowany na <c>TurnoutEntity</c> przez wywołanie
    /// <c>TurnoutPlacer.PlaceTurnoutOnChain</c> / <c>PlaceCrossoverOnChain</c>
    /// (transformacja lokalne → global po snap'ie + rotacji w MD-3+).
    /// </summary>
    public class SchemaTurnoutEntry
    {
        /// <summary>
        /// Typ rozjazdu (mapuje na <see cref="TurnoutData.TurnoutDefinition"/> przez
        /// <see cref="SchemaTurnoutType.Resolve"/>): "R190" / "R300" / "Crossover_R190".
        /// </summary>
        public string turnoutTypeName = SchemaTurnoutType.R190;

        /// <summary>Punkt rozgałęzienia (origin) rozjazdu w lokalnych współrzędnych.</summary>
        public Vector3 origin;

        /// <summary>
        /// Kierunek toru prostego (znormalizowany). W lokalnych współrzędnych zwykle
        /// (1,0,0) lub (-1,0,0) dla rozjazdów ułożonych wzdłuż toru przewodniego.
        /// </summary>
        public Vector3 direction = Vector3.right;

        /// <summary>
        /// true = odgałęzienie w lewo (perpendiculary +), false = w prawo (-).
        /// Dla schematów z mirror=true wartość jest zinwertowana.
        /// </summary>
        public bool divergeLeft = true;

        /// <summary>
        /// false = rozjazd w przód (origin = wjazd), true = rozjazd w tył (origin = wyjście,
        /// odgałęzienie idzie w przeciwnym kierunku). Używane dla scissors / złożonych układów.
        /// </summary>
        public bool flipDirection = false;

        /// <summary>Nazwa rozjazdu (np. "Rozjazd 1", "Crossover A").</summary>
        public string name = "";

        /// <summary>Resolves type name na konkretną definicję rozjazdu (lub null gdy nieznana).</summary>
        public TurnoutData.TurnoutDefinition? ResolveDefinition()
        {
            return SchemaTurnoutType.Resolve(turnoutTypeName);
        }
    }
}
