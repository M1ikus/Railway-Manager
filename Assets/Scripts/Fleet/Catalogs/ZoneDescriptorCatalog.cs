using System.Collections.Generic;
using UnityEngine;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// Statyczny katalog opisów stref — kolory, układy, rozmiary.
    /// Pozostaje w kodzie bo to wizualne stałe, nie dane do edycji.
    /// </summary>
    public static class ZoneDescriptorCatalog
    {
        public static readonly Dictionary<SeatZoneType, ZoneDescriptor> All = new()
        {
            { SeatZoneType.SecondClassOpen,        new() { type=SeatZoneType.SecondClassOpen,        label="2 kl. otwarta",     color=new Color(0.30f,0.55f,0.85f), seatsPerRow=4, isCompartment=false, compartmentSize=0, slotsPerSeat=1f } },
            { SeatZoneType.SecondClassCompartment, new() { type=SeatZoneType.SecondClassCompartment, label="2 kl. przedzia\u0142", color=new Color(0.25f,0.50f,0.75f), seatsPerRow=3, isCompartment=true,  compartmentSize=6, slotsPerSeat=1f } },
            { SeatZoneType.FirstClassOpen,         new() { type=SeatZoneType.FirstClassOpen,         label="1 kl. otwarta",     color=new Color(0.85f,0.65f,0.20f), seatsPerRow=3, isCompartment=false, compartmentSize=0, slotsPerSeat=1.2f } },
            { SeatZoneType.FirstClassCompartment,  new() { type=SeatZoneType.FirstClassCompartment,  label="1 kl. przedzia\u0142", color=new Color(0.75f,0.55f,0.15f), seatsPerRow=3, isCompartment=true,  compartmentSize=6, slotsPerSeat=1f } },
            { SeatZoneType.Bicycle,                new() { type=SeatZoneType.Bicycle,                label="Rowerowa",          color=new Color(0.50f,0.80f,0.40f), seatsPerRow=4, isCompartment=false, compartmentSize=0, slotsPerSeat=1f } },
            { SeatZoneType.SmallCatering,          new() { type=SeatZoneType.SmallCatering,          label="Gastr. ma\u0142a",     color=new Color(0.85f,0.45f,0.30f), seatsPerRow=0, isCompartment=false, compartmentSize=0, slotsPerSeat=0 } },
            { SeatZoneType.LargeCatering,          new() { type=SeatZoneType.LargeCatering,          label="Gastr. du\u017ca",     color=new Color(0.90f,0.35f,0.25f), seatsPerRow=4, isCompartment=false, compartmentSize=0, slotsPerSeat=1.5f } },
            { SeatZoneType.Sleeping,               new() { type=SeatZoneType.Sleeping,               label="Sypialna",          color=new Color(0.55f,0.35f,0.75f), seatsPerRow=4, isCompartment=true,  compartmentSize=4, slotsPerSeat=2f } },
            { SeatZoneType.Reclining,              new() { type=SeatZoneType.Reclining,              label="Le\u017c\u0105ca",         color=new Color(0.65f,0.45f,0.80f), seatsPerRow=4, isCompartment=true,  compartmentSize=4, slotsPerSeat=1.8f } },
            { SeatZoneType.Family,                 new() { type=SeatZoneType.Family,                 label="Rodzinna",          color=new Color(0.80f,0.60f,0.70f), seatsPerRow=4, isCompartment=true,  compartmentSize=4, slotsPerSeat=2f } },
            { SeatZoneType.WheelchairAccessible,   new() { type=SeatZoneType.WheelchairAccessible,   label="W\u00f3zek inw.",       color=new Color(0.30f,0.75f,0.75f), seatsPerRow=2, isCompartment=false, compartmentSize=0, slotsPerSeat=2f } },
            { SeatZoneType.ManagerCompartment,     new() { type=SeatZoneType.ManagerCompartment,     label="Przedz. menad\u017c.", color=new Color(0.85f,0.75f,0.30f), seatsPerRow=2, isCompartment=true,  compartmentSize=4, slotsPerSeat=1f } },
        };
    }
}
