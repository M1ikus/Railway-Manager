using System;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// Ilość miejsc konkretnego typu w pojeździe.
    /// Używane w FleetVehicleData.seatBreakdown i FleetMarketVehicle.seatBreakdown.
    /// </summary>
    [Serializable]
    public class SeatCount
    {
        public SeatZoneType type;
        public int count;
    }
}
