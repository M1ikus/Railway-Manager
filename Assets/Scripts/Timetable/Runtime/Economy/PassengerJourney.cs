using System.Collections.Generic;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M-PaxV2 Faza C: krawędź bezpośredniej osiągalności — z jednej stacji do drugiej JEDNYM
    /// kursem (istnieje rozkład mający obie stacje w kolejności). Niesie dystans + kategorię
    /// (do liczenia ceny per odcinek / through-fare).
    /// </summary>
    public readonly struct DirectEdge
    {
        public readonly int toStationId;
        public readonly float distanceKm;
        public readonly string commercialCategoryId;

        public DirectEdge(int toStationId, float distanceKm, string commercialCategoryId)
        {
            this.toStationId = toStationId;
            this.distanceKm = distanceKm;
            this.commercialCategoryId = commercialCategoryId;
        }
    }

    /// <summary>M-PaxV2 Faza C: pojedynczy odcinek podróży (jeden kurs) — wsiadanie→wysiadanie.</summary>
    public readonly struct JourneyLeg
    {
        public readonly int boardStationId;
        public readonly int alightStationId;
        public readonly float distanceKm;
        public readonly string commercialCategoryId;

        public JourneyLeg(int boardStationId, int alightStationId, float distanceKm, string commercialCategoryId)
        {
            this.boardStationId = boardStationId;
            this.alightStationId = alightStationId;
            this.distanceKm = distanceKm;
            this.commercialCategoryId = commercialCategoryId;
        }
    }

    /// <summary>M-PaxV2 Faza C: zaplanowana podróż = sekwencja odcinków (1 = bezpośrednia,
    /// 2 = 1 przesiadka, 3 = 2 przesiadki). Stacje przesiadkowe = alight odcinków poza ostatnim.</summary>
    public sealed class PassengerJourney
    {
        public readonly List<JourneyLeg> legs;
        public PassengerJourney(List<JourneyLeg> legs) { this.legs = legs ?? new List<JourneyLeg>(); }

        public int TransferCount => System.Math.Max(0, legs.Count - 1);
        public float TotalDistanceKm
        {
            get { float t = 0f; for (int i = 0; i < legs.Count; i++) t += legs[i].distanceKm; return t; }
        }
    }
}
