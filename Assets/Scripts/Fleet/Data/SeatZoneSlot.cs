using System;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-1: Strefa siedzeń wzdłuż długości pudła/członu.
    /// Definiuje slot od <c>startPercent</c> do <c>endPercent</c> (0-100) wypełniony
    /// strefą typu <c>SeatZoneType</c>. Lista slotów w VehicleConfiguration.interiorMix
    /// musi spełniać: suma długości == 100%, brak overlap'ów, kolejność rosnąca.
    /// </summary>
    [Serializable]
    public class SeatZoneSlot
    {
        public float startPercent;             // 0.0 - 100.0
        public float endPercent;               // 0.0 - 100.0
        public SeatZoneType type;
    }
}
