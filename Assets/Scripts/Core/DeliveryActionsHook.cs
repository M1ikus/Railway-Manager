using System;

namespace RailwayManager.Core
{
    /// <summary>
    /// M9c-D: kontrakt dostawy taboru. Implementowany przez <c>DeliveryService</c> (Timetable asmdef).
    ///
    /// FleetPanelUI żyje w Depot asmdef, który NIE widzi Timetable. Hook w Core (oba widzą Core)
    /// daje UI dostęp do akcji dostawy bez cyclic dependency — wzorzec analogiczny do
    /// <see cref="SaveActionsHook"/> / <c>TrainRunSimulator.CrewCheckHook</c>.
    /// </summary>
    public interface IDeliveryActionsProvider
    {
        /// <summary>Szacowany koszt [PLN] dostawy ekspresowej dla pojazdu (0 gdy pojazd nieznany).</summary>
        int EstimateExpressCostZl(int vehicleId);

        /// <summary>Szacowany czas [sekundy gry] dostawy ekspresowej.</summary>
        long EstimateExpressTimeSec(int vehicleId);

        /// <summary>Zamawia dostawę ekspresową (pobiera koszt, ustawia InTransit). False gdy nie można.</summary>
        bool RequestExpressDelivery(int vehicleId);

        /// <summary>F4: zamawia dostawę własnym rozkładem (delivery TrainRun). False gdy nie można.</summary>
        bool RequestScheduledDelivery(int vehicleId);

        /// <summary>F5: dostawa wagonu pasywnego lokomotywą producenta (płatna, widoczny przejazd,
        /// loko znika po dostawie). False gdy nie można.</summary>
        bool RequestDealerWagonDelivery(int vehicleId);

        /// <summary>F5: wysyła własną lokomotywę (z home depot) po wagon — round-trip. False gdy nie można.</summary>
        bool RequestOwnLocoWagonDelivery(int vehicleId);

        /// <summary>F5: czy w home depot jest wolna lokomotywa do wysłania po wagon (dla UI).</summary>
        bool HasAvailableLocoForFetch();
    }

    /// <summary>
    /// Cross-asmdef hook dla akcji dostawy taboru wywoływanych z UI (FleetPanelUI w Depot).
    /// Bootstrap: <c>DeliveryService.Awake</c> woła <see cref="Register"/>. Brak provider'a
    /// (np. scena bez Timetable) → wszystkie hooki null → UI pokazuje fallback / disabled.
    /// </summary>
    public static class DeliveryActionsHook
    {
        private static IDeliveryActionsProvider _provider;

        /// <summary>True gdy provider zainstalowany (DeliveryService żyje).</summary>
        public static bool IsRegistered => _provider != null;

        public static void Register(IDeliveryActionsProvider provider)
        {
            if (provider == null) { Unregister(); return; }
            _provider = provider;
            EstimateExpressCostZl     = provider.EstimateExpressCostZl;
            EstimateExpressTimeSec    = provider.EstimateExpressTimeSec;
            RequestExpressDelivery    = provider.RequestExpressDelivery;
            RequestScheduledDelivery  = provider.RequestScheduledDelivery;
            RequestDealerWagonDelivery = provider.RequestDealerWagonDelivery;
            RequestOwnLocoWagonDelivery = provider.RequestOwnLocoWagonDelivery;
            HasAvailableLocoForFetch  = provider.HasAvailableLocoForFetch;
        }

        public static void Unregister()
        {
            _provider = null;
            EstimateExpressCostZl     = null;
            EstimateExpressTimeSec    = null;
            RequestExpressDelivery    = null;
            RequestScheduledDelivery  = null;
            RequestDealerWagonDelivery = null;
            RequestOwnLocoWagonDelivery = null;
            HasAvailableLocoForFetch  = null;
        }

        // Public read-only — callsites wywołują przez `?.Invoke()`. Settery prywatne (tylko Register).
        public static Func<int, int> EstimateExpressCostZl { get; private set; }
        public static Func<int, long> EstimateExpressTimeSec { get; private set; }
        public static Func<int, bool> RequestExpressDelivery { get; private set; }
        public static Func<int, bool> RequestScheduledDelivery { get; private set; }
        public static Func<int, bool> RequestDealerWagonDelivery { get; private set; }
        public static Func<int, bool> RequestOwnLocoWagonDelivery { get; private set; }
        public static Func<bool> HasAvailableLocoForFetch { get; private set; }
    }
}
