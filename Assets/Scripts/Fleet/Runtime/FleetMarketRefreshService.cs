using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-Fleet-3: Watchdog rynku wtórnego — co
    /// <see cref="FleetBalanceConstants.MarketRefreshIntervalDays"/> dni gry wywołuje
    /// <see cref="MarketGenerator.Refresh"/> usuwając N najstarszych niekupionych
    /// pozycji i dodając M losowych nowych.
    ///
    /// Subskrybuje <see cref="GameState.OnDayEnded"/> — przy każdej dobie sprawdza
    /// licznik dni od ostatniego refresh'u. Gdy osiągnie interval, wywołuje generator.
    ///
    /// B3: static class (wcześniej MonoBehaviour z DontDestroyOnLoad — niespójnie
    /// z resztą Fleet Runtime, plus BUG-046 wymagał ręcznego ResetAll bo singleton
    /// żył między sesjami). Subskrypcja idempotent przez flagę _subscribed.
    /// </summary>
    public static class FleetMarketRefreshService
    {
        static int _daysSinceLastRefresh;
        static bool _subscribed;

        /// <summary>API kompat: bootstrap subskrypcji (idempotentne). Wywołać przy starcie sesji.</summary>
        public static void EnsureExists()
        {
            if (_subscribed) return;
            GameState.OnDayEnded += OnDayEnded;
            _subscribed = true;
            Log.Info("[FleetMarketRefreshService] Subscribed OnDayEnded");
        }

        /// <summary>
        /// BUG-046: reset countdown przy „Nowa gra".
        /// </summary>
        public static void ResetAll()
        {
            _daysSinceLastRefresh = 0;
            Log.Info("[FleetMarketRefreshService] Reset countdown");
        }

        public static int GetDaysSinceLastRefresh() => _daysSinceLastRefresh;

        public static void RestoreDaysSinceLastRefresh(int daysSinceLastRefresh)
        {
            _daysSinceLastRefresh = Mathf.Max(0, daysSinceLastRefresh);
            Log.Info($"[FleetMarketRefreshService] Restored countdown: {_daysSinceLastRefresh}d");
        }

        static void OnDayEnded(string dateIsoJustEnded)
        {
            // M-FC-9: sprawdź ukończone paint jobs co dzień (pojazdy z ZNTK wracają)
            long nowGameTime = GameState.GameDay * 86400L + (long)GameState.GameTimeSeconds;
            PaintingJobService.CheckCompletions(nowGameTime);

            _daysSinceLastRefresh++;
            if (_daysSinceLastRefresh < FleetBalanceConstants.MarketRefreshIntervalDays)
                return;

            _daysSinceLastRefresh = 0;
            DoRefresh();
        }

        static void DoRefresh()
        {
            if (!FleetCatalog.IsLoaded || !FleetService.IsInitialized)
            {
                Log.Warn("[FleetMarketRefreshService] FleetCatalog/Service nie załadowany — pomijam refresh");
                return;
            }

            long nowGameTime = GameState.GameDay * 86400L + (long)GameState.GameTimeSeconds;

            // BUG-035: kopiujemy snapshot, MarketGenerator.Refresh modyfikuje in-place,
            // potem aplikujemy przez LoadMarketSnapshot (auto-NotifyMarketChanged).
            var marketCopy = new System.Collections.Generic.List<FleetMarketVehicle>(FleetService.MarketVehicles);
            MarketGenerator.Refresh(
                market: marketCopy,
                templatePool: FleetCatalog.InitialMarket,
                nowGameTime: nowGameTime,
                removeOldest: FleetBalanceConstants.MarketRefreshRemoveCount,
                addCount: FleetBalanceConstants.MarketRefreshAddCount,
                cap: FleetBalanceConstants.MarketMaxSize);
            FleetService.LoadMarketSnapshot(marketCopy);
        }

        /// <summary>Diagnostyka: wymuś refresh teraz (smoke test / debug).</summary>
        public static void DebugForceRefresh() => DoRefresh();
    }
}
