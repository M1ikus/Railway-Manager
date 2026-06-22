using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M9c-D (domknięcie pipeline dostawy taboru): rozwiązuje nazwę lokalizacji zakupu
    /// (<c>NewVehicleModel.factoryLocation</c> / <c>FleetMarketVehicle.location</c>,
    /// np. "Bydgoszcz", "Nowy Sącz", "Warszawa Grochów") na konkretny węzeł grafu
    /// pathfindingu (<c>pathNodeId</c>) + pozycję 2D na mapie OSM.
    ///
    /// KONWENCJA ID (krytyczna — patrz DepotLocationPickerUI.OnConfirmClicked):
    /// zwracany <see cref="Resolved.nodeId"/> jest w przestrzeni PathfindingGraph node,
    /// czyli spójny z <see cref="GameState.HomeDepotStationId"/>,
    /// <c>RouteStation.stationNodeId</c> oraz <c>VehicleLocationRecord.stationId</c>.
    /// To NIE jest <c>RailwayStation.stationId</c> (sekwencyjny indeks kolekcji).
    /// </summary>
    public static class DeliveryLocator
    {
        public struct Resolved
        {
            /// <summary>pathNodeId (węzeł grafu). -1 gdy nierozwiązane.</summary>
            public int nodeId;
            /// <summary>Pozycja 2D na mapie (świat gry).</summary>
            public Vector2 position;
            /// <summary>Pełna nazwa dopasowanej stacji (do UI/logów).</summary>
            public string stationName;
            /// <summary>true = dopasowano po nazwie; false = fallback (home / brak).</summary>
            public bool exact;

            public bool IsValid => nodeId >= 0;
        }

        /// <summary>
        /// Rozwiązuje nazwę lokalizacji zakupu na stację na mapie.
        /// Strategia: (1) <see cref="TimetableInitializer.FindStation"/> — fuzzy contains po nazwie
        /// (np. "Bydgoszcz" trafia w "Bydgoszcz Główna"); (2) fallback do home depot station
        /// (pojazd "dostarczony do huba domowego") z <c>exact=false</c> żeby caller mógł ostrzec gracza.
        /// </summary>
        public static Resolved ResolvePurchaseLocation(string locationName)
        {
            var init = TimetableInitializer.Instance;
            if (init != null && !string.IsNullOrEmpty(locationName))
            {
                var st = init.FindStation(locationName);
                if (st != null && st.pathNodeId >= 0)
                    return new Resolved
                    {
                        nodeId = st.pathNodeId,
                        position = st.position,
                        stationName = st.name,
                        exact = true
                    };
            }

            // Fallback → home depot station ("dostawa do najbliższego huba własnego")
            var home = ResolveHome();
            if (home.IsValid)
            {
                Log.Warn($"[DeliveryLocator] Nie znaleziono stacji dla lokalizacji '{locationName}' — " +
                         $"fallback do home depot ('{home.stationName}')");
                return new Resolved
                {
                    nodeId = home.nodeId,
                    position = home.position,
                    stationName = home.stationName,
                    exact = false
                };
            }

            Log.Warn($"[DeliveryLocator] Nie rozwiązano lokalizacji '{locationName}' i brak home depot — nodeId=-1");
            return new Resolved { nodeId = -1, position = Vector2.zero, stationName = locationName, exact = false };
        }

        /// <summary>
        /// Rozwiązuje home depot station (<see cref="GameState.HomeDepotStationId"/>) na pozycję 2D.
        /// Pozycja z <see cref="RailwayStation.position"/> (preferowane) lub z grafu jako fallback.
        /// </summary>
        public static Resolved ResolveHome()
        {
            int homeNode = GameState.HomeDepotStationId;
            if (homeNode < 0)
                return new Resolved { nodeId = -1, exact = false };

            var init = TimetableInitializer.Instance;
            if (init?.Stations != null)
            {
                foreach (var s in init.Stations)
                    if (s.pathNodeId == homeNode)
                        return new Resolved
                        {
                            nodeId = homeNode,
                            position = s.position,
                            stationName = s.name,
                            exact = true
                        };
            }

            // Stacja nieznaleziona po nodeId — pozycja z węzła grafu.
            // Node to struct + GetNode rzuca przy złym id → bounds-check przez NodeCount.
            Vector2 pos = Vector2.zero;
            if (init?.Graph != null && homeNode < init.Graph.NodeCount)
                pos = init.Graph.GetNode(homeNode).position;
            return new Resolved
            {
                nodeId = homeNode,
                position = pos,
                stationName = "(zajezdnia)",
                exact = pos != Vector2.zero
            };
        }
    }
}
