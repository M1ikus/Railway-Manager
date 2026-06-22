using System;
using System.Collections.Generic;
using UnityEngine;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Statyczna trasa — geometria łącząca sekwencję stacji przez graf kolejowy.
    /// Reuzywalna przez wiele rozkładów (np. trasa Wa→Kr jako osobowy, pospieszny, ekspres).
    /// </summary>
    [Serializable]
    public class Route
    {
        public int id;
        public string name;                     // np. "R1: Warszawa Centralna → Kraków Główny"

        /// <summary>Uporządkowana lista ID węzłów z RailwayGraph tworzących polyline trasy.</summary>
        public List<int> nodeIds = new();

        /// <summary>Tylko stacje/przystanki na trasie (z metadanymi — kolejność i pozycja na polylinie).</summary>
        public List<RouteStation> stations = new();

        /// <summary>Łączna długość trasy w metrach (suma segmentów polylinii).</summary>
        public float totalLengthM;

        /// <summary>Czy trasa przekracza granicę województwa (dla klasyfikacji międzywojewódzki).</summary>
        public bool crossesVoivodeshipBorder;

        /// <summary>Województwo stacji startowej (do klasyfikacji obszaru konstrukcyjnego numeru).</summary>
        public string startVoivodeship;

        /// <summary>Czy trasa mieści się w obrębie jednej aglomeracji miejskiej (dla kategorii RA).</summary>
        public bool isInSingleAgglomeration;

        /// <summary>Nazwa aglomeracji jeśli isInSingleAgglomeration = true.</summary>
        public string agglomerationName;

        /// <summary>M9c-D F7: syntetyczna trasa dostawcza (efemeryczna) — pomijana w save,
        /// sprzątana po dostawie. Nie zaśmieca listy tras gracza.</summary>
        public bool isDeliveryRoute;

        /// <summary>
        /// Hash trasy do lookup pamięci postojów per (Route, CommercialCategory).
        /// Stabilny identyfikator niezależny od id (ta sama sekwencja stacji = ten sam hash).
        /// </summary>
        public string RouteHash
        {
            get
            {
                if (stations == null || stations.Count == 0) return "empty";
                var sb = new System.Text.StringBuilder(stations.Count * 8);
                for (int i = 0; i < stations.Count; i++)
                {
                    if (i > 0) sb.Append('-');
                    sb.Append(stations[i].stationNodeId);
                }
                return sb.ToString();
            }
        }
    }

    /// <summary>Stacja/przystanek leżąca na trasie.</summary>
    [Serializable]
    public class RouteStation
    {
        public int stationNodeId;              // ID węzła z RailwayGraph
        public string stationName;
        public float distanceFromStartM;       // km-trasy liczone od początku polylinii
        public Vector2 position;               // współrzędne świata (cache, do UI)

        /// <summary>Czy to jest "ważna" stacja (station) czy przystanek (halt). Z OSM railway=station|halt.</summary>
        public bool isMajorStation;

        /// <summary>Województwo w którym leży ta stacja (do detekcji międzywojewódzkiego).</summary>
        public string voivodeship;

        /// <summary>Miasto w którym leży ta stacja (do detekcji aglomeracji).</summary>
        public string cityName;
    }
}
