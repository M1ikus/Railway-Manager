using UnityEngine;

namespace RailwayManager.Fleet
{
    /// <summary>Opis typu strefy (kolor, układ, rozmiary). Dane wizualne statyczne.</summary>
    public class ZoneDescriptor
    {
        public SeatZoneType type;
        public string label;
        public Color color;
        public int seatsPerRow;       // fotele w jednym rzędzie wzdłuż wagonu
        public bool isCompartment;
        public int compartmentSize;   // miejsc per przedział (6, 4, ...)
        public float slotsPerSeat;    // ile slotów zajmuje rząd foteli
    }

}
