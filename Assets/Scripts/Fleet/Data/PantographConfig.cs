using System;
using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-1: Konfiguracja pantografów dla EZT/loko. Ignorowane dla DMU/wagon.
    /// Liczba pantografów wyliczana automatycznie z liczby członów (1 dla ≤3, 2 dla ≥4).
    /// Lokalizacja default = pudła kabinowe; opcja zaawansowana pozwala na custom.
    /// W EA pantografy są czysto wizualne — brak wpływu na gameplay.
    /// </summary>
    [Serializable]
    public class PantographConfig
    {
        public int count;                       // 1 lub 2 (auto z memberCount)
        public PantographPlacement placement = PantographPlacement.CabSegments;
        public List<int> customSegmentIndices = new(); // override gdy placement == Custom
    }

    /// <summary>
    /// M-FC-1: Lokalizacja pantografów w składzie EZT.
    /// <c>CabSegments</c> — pudła kabinowe (skrajne członu, default).
    /// <c>MiddleSegments</c> — środkowe człony (mniej kołysania sieci przy v=200).
    /// <c>Custom</c> — gracz wybiera segment indices ręcznie.
    /// </summary>
    public enum PantographPlacement { CabSegments, MiddleSegments, Custom }
}
