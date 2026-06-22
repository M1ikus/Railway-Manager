using System;
using UnityEngine;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M6-1: Stan pojedynczego pasażera-agenta w symulacji ekonomicznej.
    /// </summary>
    public enum PassengerState
    {
        /// <summary>Na peronie stacji, czeka na pociąg w swoim kierunku.</summary>
        WaitingAtStation,

        /// <summary>Wsiada do pociągu (krótka faza, 1 tick).</summary>
        Boarding,

        /// <summary>Jedzie pociągiem.</summary>
        OnTrain,

        /// <summary>Wysiada z pociągu.</summary>
        Alighting,

        /// <summary>Dojechał do celu — do usunięcia z puli.</summary>
        Arrived,

        /// <summary>Zrezygnował z podróży (przekroczona cierpliwość).</summary>
        Abandoned,
    }

    /// <summary>
    /// Preferencja pasażera przy wyborze pociągu (gdy kilku operatorów / kilka opcji).
    /// Wpływa na elastyczność cenową (Cheapest = bardziej wrażliwy), tolerancję na
    /// opóźnienia (Fastest = mniej cierpliwy).
    /// </summary>
    public enum PassengerPreference
    {
        /// <summary>Wybiera najtańszego, toleruje opóźnienia i niski komfort.</summary>
        Cheapest,
        /// <summary>Wybiera najszybszego, płaci więcej za czas.</summary>
        Fastest,
        /// <summary>Wybiera najwygodniejszego (kategoria wyższa = + comfort).</summary>
        MostComfort,
    }

    /// <summary>
    /// M6-1: Lekki POCO reprezentujący jednego pasażera.
    /// NIE MonoBehaviour — trzymany w pool'u <see cref="PassengerManager"/>
    /// (target 50 000 aktywnych agentów na mapie Polska).
    /// </summary>
    [Serializable]
    public class PassengerAgent
    {
        // ── Identyfikacja ────────────────────────────────────────────

        public int agentId;

        // ── Cel podróży (niezmienne po spawn) ───────────────────────

        /// <summary>Stacja pochodzenia (gdzie się pojawia).</summary>
        public int originStationId;

        /// <summary>Stacja docelowa (gdzie chce się znaleźć).</summary>
        public int destinationStationId;

        // ── Preferencje (niezmienne po spawn) ───────────────────────

        public PassengerPreference preference;

        /// <summary>Budżet pasażera na podróż [gr]. Nie wsiada jeśli bilet przekracza.</summary>
        public int walletGroszy;

        // ── Stan dynamiczny ──────────────────────────────────────────

        public PassengerState state = PassengerState.WaitingAtStation;

        /// <summary>Aktualna pozycja: stationId gdy waiting/alighting, -1 gdy OnTrain.</summary>
        public int currentStationId;

        /// <summary>ID kursu na którym jedzie (valid gdy state=OnTrain). -1 w pozostałych stanach.</summary>
        public int currentTrainRunId = -1;

        // ── Czas ─────────────────────────────────────────────────────

        /// <summary>Game time (sec) kiedy agent się pojawił.</summary>
        public float spawnTimeSec;

        /// <summary>Game time (sec) kiedy agent abandon'uje jeśli jeszcze nie wsiądzie.</summary>
        public float abandonTimeSec;

        // ── Statystyki (do debug / UI) ──────────────────────────────

        /// <summary>Ile gr zapłacił za bilety (sumarycznie, ze wszystkimi przesiadkami).</summary>
        public int paidTotalGroszy;

        /// <summary>Ile przesiadek wykonał (0 = jechał bezpośrednio).</summary>
        public int transferCount;

        // ── Cel podróży + klasa biletowa (M-PaxV2 Faza A/B) ─────────

        /// <summary>Cel podróży (M-PaxV2 Faza B) — wyprowadza klasę i budżet. Default Commute.</summary>
        public TripPurpose purpose = TripPurpose.Commute;

        /// <summary>Preferowana/wykupiona klasa biletu (SeatZoneType). Wsiada na miejsce tej klasy
        /// i płaci jej stawkę z taryfy. Przydzielana z celu podróży przy spawnie (Faza B).</summary>
        public RailwayManager.Fleet.SeatZoneType desiredClass = RailwayManager.Fleet.SeatZoneType.SecondClassOpen;

        /// <summary>M-PaxV2 Faza C.2c: indeks bieżącego odcinka w zaplanowanej podróży (0 = pierwszy).
        /// Cel bieżącego odcinka (stacja wysiadki/przesiadki) bierzemy z cache'owanej PassengerJourney
        /// (PassengerManager) per para origin→destination. Przy przesiadce ++ i wraca WaitingAtStation.
        /// Podróż bezpośrednia (1 odcinek) → zostaje 0 całą drogę.</summary>
        public int currentLegIndex;

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>Czy agent jest w stanie aktywnym (nie Arrived / Abandoned).</summary>
        public bool IsAlive => state != PassengerState.Arrived && state != PassengerState.Abandoned;

        /// <summary>Dystans Manhattan od origin do destination (dla debug — nie do decyzji algorytmicznych).</summary>
        public float GetStraightLineDistance(Func<int, Vector2> stationPosLookup)
        {
            var from = stationPosLookup(originStationId);
            var to = stationPosLookup(destinationStationId);
            return Vector2.Distance(from, to);
        }

        public override string ToString() =>
            $"Agent#{agentId} ({originStationId}→{destinationStationId}) " +
            $"state={state} pref={preference} wallet={walletGroszy / 100f:F0}zł " +
            $"paid={paidTotalGroszy / 100f:F0}zł transfers={transferCount}";
    }
}
