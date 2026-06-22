using System;

namespace RailwayManager.Timetable
{
    /// <summary>Typ granicy odcinka blokowego.</summary>
    public enum BoundaryType : byte
    {
        /// <summary>Rozjazd (node z 3+ edge'ami).</summary>
        Junction,
        /// <summary>Semafor z OSM (railway=signal).</summary>
        Signal,
        /// <summary>Koniec linii (dead-end, node z 1 edge).</summary>
        LineEnd,
        /// <summary>Stacja (railway=station/halt).</summary>
        Station
    }

    /// <summary>
    /// Odcinek blokowy — struct (nie class!) dla minimalnego narzutu pamięci.
    /// Jednostka rezerwacji szlakowej. Edge→section mapping jest w BlockSectionGraph.
    /// </summary>
    public struct BlockSection
    {
        public int id;
        public int startNodeId;
        public int endNodeId;
        public float lengthM;
        public int maxSpeedKmh;
        public int edgeCount;
        public BoundaryType startBoundary;
        public BoundaryType endBoundary;
    }
}
