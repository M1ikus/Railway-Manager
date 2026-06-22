using System;
using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>Skład złożony z kilku pojazdów.</summary>
    [Serializable]
    public class FleetConsistData
    {
        public string name;             // np. "IC Krakowiak"
        public List<int> vehicleIds;    // IDs pojazdów w składzie
        public string route;            // np. "Linia R1"
        public FleetVehicleStatus status;
    }
}
