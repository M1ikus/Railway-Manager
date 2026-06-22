using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public enum TurnoutEntityType
    {
        Regular,    // Zwykły rozjazd (1 odnoga)
        Crossover   // Krzyżowy (2 odnogi + przekątna)
    }

    /// <summary>
    /// Pozycja zwrotnicy — na którą trasę ustawiony jest rozjazd.
    /// </summary>
    public enum SwitchPosition
    {
        Straight,   // Jazda na wprost (prosta noga)
        Diverging   // Jazda na odgałęzienie (łuk)
    }

    /// <summary>
    /// Logiczne grupowanie segmentów toru tworzących jeden rozjazd.
    /// Przechowuje referencje do body + odnóg (BEZ pre/post segmentów).
    /// Przygotowane na podmianę na model 3D z ruchomymi iglicami.
    /// </summary>
    public class TurnoutEntity
    {
        public int TurnoutId;
        public string DefinitionName;           // np. "R190 1:9", "Krzyżowy R190"
        public TurnoutEntityType Type;
        public List<int> MemberTrackIds;        // GraphTrackIds: body + odnogi (BEZ pre/post)

        // === Geometria (do pozycjonowania modelu 3D) ===
        public Vector3 Origin;                  // Punkt początkowy rozjazdu (junction node)
        public Vector3 Direction;               // Kierunek prostej nogi (znormalizowany)
        public bool DivergeLeft;                // Strona odgałęzienia
        public TurnoutData.TurnoutDefinition Definition; // Pełna definicja (R, kąt, długości)

        // === Oryginalny tor (do odtworzenia po usunięciu rozjazdu) ===
        public List<Vector3> OriginalPolyline;     // Polyline toru który został zastąpiony
        public string OriginalTrackName;            // Nazwa oryginalnego toru
        public DepotTrackType OriginalTrackType;    // Typ oryginalnego toru
        public bool FlipDirection;                  // flipDirection użyty przy stawianiu (do undo)
        public float DistAlongChain;                // distAlongChain użyty przy stawianiu (do undo)

        // === Stan zwrotnicy (do animacji iglic) ===
        public SwitchPosition CurrentPosition = SwitchPosition.Straight;

        // === Model 3D (do podmiany w przyszłości) ===
        /// <summary>
        /// Referencja do instancji modelu 3D rozjazdu (null = generowany proceduralnie).
        /// Gdy != null, segmenty proceduralne (MemberTrackIds) mogą być ukryte.
        /// </summary>
        public GameObject ModelInstance;

        public TurnoutEntity(string definitionName, TurnoutEntityType type)
        {
            DefinitionName = definitionName;
            Type = type;
            MemberTrackIds = new List<int>();
        }

        /// <summary>
        /// Przełącza pozycję zwrotnicy.
        /// </summary>
        public void ToggleSwitch()
        {
            CurrentPosition = CurrentPosition == SwitchPosition.Straight
                ? SwitchPosition.Diverging
                : SwitchPosition.Straight;
        }
    }
}
