using System;

namespace RailwayManager.Timetable
{
    /// <summary>Funkcja semafora z OSM railway:signal:*:function.</summary>
    public enum SignalFunction : byte
    {
        Unknown,
        Entry,          // wjazdowy — wjazd na stację
        Exit,           // wyjazdowy — wyjazd ze stacji
        Block,          // blokowy (SBL) — dzieli szlak na mniejsze bloki
        Intermediate    // pośredni — wewnątrz stacji, nie dzieli bloków szlakowych
    }

    /// <summary>Kierunek obowiązywania semafora (railway:signal:direction).</summary>
    public enum SignalDirection : byte
    {
        Both,           // obowiązuje w obu kierunkach (domyślnie jeśli brak tagu)
        Forward,        // w kierunku digitizacji OSM way
        Backward        // w kierunku przeciwnym
    }

    /// <summary>
    /// Semafor z OSM — snapped do węzła PathfindingGraph.
    /// Używany do wyznaczania granic odcinków blokowych.
    /// </summary>
    [Serializable]
    public struct SignalInfo
    {
        public int nodeId;
        public SignalFunction function;
        public SignalDirection direction;
        public string refNum;  // ref tag z OSM (numer semafora)
    }
}
