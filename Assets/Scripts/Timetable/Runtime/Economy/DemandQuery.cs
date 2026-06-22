namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M11 AS-P3: statyczna fasada popytu — null-safe dostęp do PassengerManager dla
    /// plannera (AS-5) i reguł advisora. Przed bootstrapem sceny / w EditMode wszystkie
    /// metody zwracają wartości puste (0/false) zamiast NRE — konsument nie musi
    /// sprawdzać Instance.
    /// </summary>
    public static class DemandQuery
    {
        /// <summary>Bazowy popyt dzienny [pax/dzień] z gravity model. 0 bez OD matrix.</summary>
        public static float BaseDailyDemand(int fromStationId, int toStationId)
        {
            var pm = PassengerManager.Instance;
            return pm != null ? pm.GetBaseDemandBetween(fromStationId, toStationId) : 0f;
        }

        /// <summary>Popyt dzienny × reputacja pary × mnożnik trudności (bez profilu godzinowego).</summary>
        public static float EstimatedDailyDemand(int fromStationId, int toStationId)
        {
            var pm = PassengerManager.Instance;
            return pm != null ? pm.GetEstimatedDailyDemand(fromStationId, toStationId) : 0f;
        }

        /// <summary>Czy para jest już obsługiwana ofertą (≤2 przesiadki). False bez managera/grafu.</summary>
        public static bool IsPairServedByOffer(int fromStationId, int toStationId)
        {
            var pm = PassengerManager.Instance;
            return pm != null && pm.IsPairServedByOffer(fromStationId, toStationId);
        }
    }
}
