using System;
using UnityEngine;

namespace DepotSystem.Schemas.Snapshot
{
    /// <summary>
    /// Serializowalna POCO — pojedynczy rozjazd w snapshot schemacie.
    /// Origin w lokalnych współrzędnych (względem snapshot anchorPoint).
    /// Direction w world coords (znormalizowany kierunek prostej nogi w momencie capture).
    ///
    /// Po deserialize w MD-9 placement: rotacja schematu (gracz Ctrl+Scroll) obraca
    /// też direction tego rozjazdu.
    /// </summary>
    [Serializable]
    public class SnapshotTurnoutEntry
    {
        /// <summary>
        /// Mapowane przez <c>SchemaTurnoutType</c>: "R190" / "R300" / "Crossover_R190".
        /// Konwersja z <c>TurnoutEntity.DefinitionName</c> ("R190 1:9", "R300 1:9", "Krzyżowy R190")
        /// w <c>SnapshotSerializer.MapTurnoutTypeName</c>.
        /// </summary>
        public string turnoutTypeName = "R190";

        /// <summary>Origin w lokalnych współrzędnych (subtract anchor).</summary>
        public Vector3 originLocal;

        /// <summary>Kierunek prostej nogi (znormalizowany, world coords w momencie capture).</summary>
        public Vector3 direction = Vector3.right;

        /// <summary>Strona odgałęzienia (true=lewo, false=prawo).</summary>
        public bool divergeLeft = true;

        /// <summary>Czy rozjazd był stawiany w odwróconym kierunku (do undo placement).</summary>
        public bool flipDirection = false;

        /// <summary>Nazwa rozjazdu (informacyjne).</summary>
        public string name = "";
    }
}
