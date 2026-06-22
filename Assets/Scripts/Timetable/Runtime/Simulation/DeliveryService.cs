using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Economy;
using RailwayManager.Fleet;
using DepotSystem;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M9c-D: domknięcie pipeline dostawy taboru. Watchdog (tick) + API dostawy.
    ///
    /// Cykl życia pojazdu od zakupu:
    ///   InProduction (nowy, ~30 dni niewidoczny)
    ///     → AwaitingPickup + AtStation(punkt zakupu)   ← pojazd na torze stacji sprzedawcy/fabryki
    ///     → gracz wybiera dostawę:
    ///         (F3) RequestExpressDelivery → InTransit (timer) → wjazd do depot
    ///         (F4) RequestScheduledDelivery → delivery TrainRun (osobny plik, DepotMapHandshake)
    ///     → StoppedInDepot (klikalny consist w zajezdni)
    ///
    /// Logika przejść jest w publicznych metodach (Materialize/TryCompleteProduction/
    /// TryCompleteExpressDelivery/RequestExpressDelivery) żeby smoke test CLI mógł je
    /// wołać bez play mode (edit mode, fake dane). <see cref="ProcessAll"/> to jeden krok ticka.
    ///
    /// Bootstrap: <see cref="EnsureExists"/> w TrainRunSimulator.Awake (jak DispatchService).
    /// </summary>
    public partial class DeliveryService : MonoBehaviour, IDeliveryActionsProvider
    {
        public static DeliveryService Instance { get; private set; }

        const float CheckIntervalSec = 1f;
        float _checkTimer;
        bool _subscribed;

        public static DeliveryService EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("DeliveryService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DeliveryService>();
            Log.Info("[DeliveryService] Bootstrapped");
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DeliveryActionsHook.Register(this);
            TrainRunSimulator.OnRunDespawned += HandleDeliveryRunDespawned; // F4 cleanup
        }

        void OnEnable() => SubscribeLocation();
        void OnDisable() => UnsubscribeLocation();
        void OnDestroy()
        {
            UnsubscribeLocation();
            TrainRunSimulator.OnRunDespawned -= HandleDeliveryRunDespawned;
            if (Instance == this)
            {
                DeliveryActionsHook.Unregister();
                Instance = null;
            }
        }

        void SubscribeLocation()
        {
            if (_subscribed) return;
            var svc = VehicleLocationService.Instance;
            if (svc == null) return;
            svc.OnLocationChanged += HandleLocationChanged;
            _subscribed = true;
        }

        void UnsubscribeLocation()
        {
            if (!_subscribed) return;
            if (VehicleLocationService.Instance != null)
                VehicleLocationService.Instance.OnLocationChanged -= HandleLocationChanged;
            _subscribed = false;
        }

        void Update()
        {
            if (GameState.IsPaused) return;

            // Late subscribe — VehicleLocationService mógł nie istnieć w OnEnable
            if (!_subscribed) SubscribeLocation();

            _checkTimer += Time.deltaTime * GameState.TimeScale;
            if (_checkTimer < CheckIntervalSec) return;
            _checkTimer = 0f;

            // Krytyczny: absolutny czas (nie goły GameTimeSeconds) — estimatedCompletionGameTime jest
            // absolutne (CartProcessor), więc porównanie też musi być, inaczej ETA nigdy nieosiągalne.
            ProcessAll(GameState.TotalGameSeconds);
        }

        // ── Tick step (publiczne dla smoke testu CLI) ────────────────

        /// <summary>
        /// Jeden krok watchdoga: przegląda wszystkie pojazdy i przeprowadza przejścia
        /// czasowe (produkcja zakończona → punkt zakupu; ekspres dostarczony → depot).
        /// Wywoływane co tick z <see cref="Update"/> oraz bezpośrednio ze smoke testu.
        /// </summary>
        public void ProcessAll(float nowSec)
        {
            var owned = FleetService.OwnedVehicles;
            // ToList-free: iterujemy po indeksie (kolekcja nie mutuje w trakcie tej pętli —
            // przejścia zmieniają tylko pola pojazdu + VehicleLocationService, nie OwnedVehicles).
            for (int i = 0; i < owned.Count; i++)
            {
                var v = owned[i];
                if (v == null) continue;
                ProcessVehicle(v, nowSec);
            }

            // F2: materializacja startowego taboru w zajezdni (gdy scena Depot + graf gotowe)
            TryParkInitialDepotFleet();
        }

        /// <summary>Przejścia dla jednego pojazdu. Publiczne — smoke test woła per fake-vehicle.</summary>
        public void ProcessVehicle(FleetVehicleData v, float nowSec)
        {
            switch (v.status)
            {
                case FleetVehicleStatus.InProduction:
                    if (nowSec >= v.estimatedCompletionGameTime)
                        CompleteProduction(v);
                    break;

                case FleetVehicleStatus.AwaitingPickup:
                    // Pojazd ma czekać na torze punktu zakupu — upewnij się że jest zmaterializowany
                    // (np. używany świeżo kupiony od razu AwaitingPickup, albo po wczytaniu save).
                    EnsureMaterializedAtPurchaseLocation(v);
                    break;

                case FleetVehicleStatus.InTransit:
                    // Dostawa ekspresowa w toku — sprawdź czy dotarła (timer).
                    if (nowSec >= v.estimatedCompletionGameTime)
                        CompleteExpressDelivery(v);
                    break;

                case FleetVehicleStatus.MovingOnMap:
                    // F7 recovery: scheduled delivery run nie jest persystowany. Po wczytaniu save
                    // pojazd zostaje MovingOnMap + OnRoute, ale delivery run zniknął → dokończ dostawę.
                    if (v.deliveryInProgress)
                    {
                        var rec = VehicleLocationService.Instance?.Get(v.id);
                        int runId = rec?.currentTrainRunId ?? -1;
                        var sim = TrainRunSimulator.Instance;
                        bool runActive = runId >= 0 && sim != null && sim.IsActive(runId);
                        // rec.type==OnRoute (nie EnteringDepot) chroni przed double-entry przy normalnym dojeździe.
                        if (rec != null && rec.type == VehicleLocationType.OnRoute && !runActive)
                        {
                            Log.Info($"[DeliveryService] Recovery: dostawa rozkładem pojazdu #{v.id} przerwana " +
                                     "(wczytanie save) → wjazd do depot");
                            v.deliveryInProgress = false;
                            TriggerDepotEntry(v);
                        }
                    }
                    break;
            }
        }

        // ── F1: produkcja → punkt zakupu ─────────────────────────────

        /// <summary>Nowy pojazd skończył produkcję → pojawia się gotowy na torze stacji fabryki.</summary>
        public void CompleteProduction(FleetVehicleData v)
        {
            v.status = FleetVehicleStatus.AwaitingPickup;
            EnsureMaterializedAtPurchaseLocation(v);
        }

        /// <summary>
        /// Materializuje pojazd na stacji punktu zakupu (AtStation na mapie 2D), jeśli jeszcze
        /// nie ma rekordu lokalizacji. IdleVehicleVisualizer pokaże go jako ikonę na peronie.
        /// Idempotentne — bezpieczne do wołania co tick.
        /// </summary>
        public void EnsureMaterializedAtPurchaseLocation(FleetVehicleData v)
        {
            var locSvc = VehicleLocationService.Instance;
            if (locSvc == null) return;

            var existing = locSvc.Get(v.id);
            if (existing != null && existing.type == VehicleLocationType.AtStation)
                return; // już na stacji

            var loc = DeliveryLocator.ResolvePurchaseLocation(v.position?.externalLocation);
            if (!loc.IsValid)
                return; // graf/stacje jeszcze nie gotowe — retry następny tick

            locSvc.SetAtStation(v.id, loc.nodeId, loc.position);
            v.position ??= new VehiclePosition();
            v.position.kind = VehicleLocationKind.OnMap;
            v.deliveryInProgress = false; // pojazd czeka na odbiór — nie jest w trakcie dostawy
            v.currentTask = loc.exact
                ? $"Oczekuje na odbiór: {loc.stationName}"
                : $"Oczekuje na odbiór: {loc.stationName} (lokalizacja przybliżona)";
            Log.Info($"[DeliveryService] Pojazd #{v.id} '{v.series}' gotowy w punkcie zakupu " +
                     $"'{loc.stationName}' (node#{loc.nodeId}) — czeka na wybór dostawy");
        }

        // ── F3: dostawa ekspresowa (płatna) ──────────────────────────

        /// <summary>
        /// Szacowany koszt [PLN] dostawy ekspresowej dla pojazdu (dystans punkt zakupu → home).
        /// </summary>
        public int EstimateExpressCostZl(FleetVehicleData v)
        {
            float km = ComputeDeliveryDistanceKm(v);
            return DeliveryConstants.ExpressBaseCostZl + Mathf.RoundToInt(km * DeliveryConstants.ExpressCostPerKmZl);
        }

        /// <summary>Szacowany czas [sekundy gry] dostawy ekspresowej.</summary>
        public long EstimateExpressTimeSec(FleetVehicleData v)
        {
            float km = ComputeDeliveryDistanceKm(v);
            return System.Math.Max(DeliveryConstants.ExpressMinTimeSec,
                (long)(km * DeliveryConstants.ExpressTimeSecPerKm));
        }

        /// <summary>
        /// Gracz zamawia dostawę ekspresową. Pobiera koszt, ustawia InTransit + ETA.
        /// Zwraca false gdy pojazd nie jest gotowy do odbioru lub brak gotówki.
        /// </summary>
        public bool RequestExpressDelivery(FleetVehicleData v)
        {
            if (v == null) return false;
            if (v.status != FleetVehicleStatus.AwaitingPickup)
            {
                Log.Warn($"[DeliveryService] RequestExpressDelivery: pojazd #{v.id} nie jest AwaitingPickup ({v.status})");
                return false;
            }
            if (!GameState.IsHomeDepotSet)
            {
                Log.Warn("[DeliveryService] RequestExpressDelivery: brak home depot");
                return false;
            }

            int cost = EstimateExpressCostZl(v);
            if (GameState.Money < cost)
            {
                Log.Warn($"[DeliveryService] RequestExpressDelivery: brak gotówki ({GameState.Money} < {cost} zł)");
                return false;
            }

            MoneyLedger.Spend(cost * 100L, "delivery", "dostawa pojazdu");
            v.status = FleetVehicleStatus.InTransit;
            // Krytyczny: baza absolutna — ekspres >1 dnia (np. 90000s dla dalekiej trasy) z gołym
            // GameTimeSeconds nigdy by nie dotarł (target > 86400, a now resetuje się o północy).
            v.estimatedCompletionGameTime = GameState.TotalGameSeconds + EstimateExpressTimeSec(v);
            v.currentTask = "Dostawa ekspresowa do zajezdni";

            // Zdejmij z mapy (był AtStation w punkcie zakupu) — "w transporcie", niewidoczny.
            VehicleLocationService.Instance?.SetInTransit(v.id, Vector2.zero);

            Log.Info($"[DeliveryService] Ekspres zamówiony: pojazd #{v.id} '{v.series}', koszt {cost} zł, " +
                     $"ETA {EstimateExpressTimeSec(v) / 3600}h");
            return true;
        }

        /// <summary>Dostawa ekspresowa dobiegła końca → wjazd do depot (brama).</summary>
        public void CompleteExpressDelivery(FleetVehicleData v)
        {
            TriggerDepotEntry(v);
        }

        // ── F4: dostawa własnym rozkładem (delivery TrainRun) ────────

        /// <summary>
        /// F4: zamawia dostawę własnym rozkładem — pojazd jedzie z punktu zakupu do home depot
        /// jako delivery TrainRun, po dojeździe wjeżdża do zajezdni (handshake endStation==home).
        /// Ciało rozbudowywane w kroku F4 (buduje Route punkt zakupu → home przez pathfinder).
        /// </summary>
        public bool RequestScheduledDelivery(FleetVehicleData v)
        {
            if (v == null || v.status != FleetVehicleStatus.AwaitingPickup) return false;
            return BuildAndSpawnDeliveryRun(v); // impl w partial DeliveryService.ScheduledRun.cs
        }

        /// <summary>
        /// F5: dostawa wagonu pasywnego lokomotywą producenta — płatna. Wagon jedzie delivery runem
        /// (dealer zapewnia trakcję, widoczny przejazd), po dostawie do depot nic nie zostaje
        /// (loko producenta było konceptualne). Pobiera opłatę transportową (jak ekspres).
        /// </summary>
        public bool RequestDealerWagonDelivery(FleetVehicleData v)
        {
            if (v == null || v.status != FleetVehicleStatus.AwaitingPickup) return false;
            if (!GameState.IsHomeDepotSet)
            {
                Log.Warn("[DeliveryService] Dostawa producenta: brak home depot");
                return false;
            }
            int cost = EstimateExpressCostZl(v);
            if (GameState.Money < cost)
            {
                Log.Warn($"[DeliveryService] Dostawa producenta: brak gotówki ({GameState.Money} < {cost} zł)");
                return false;
            }
            // Wagon jedzie delivery runem (dealerProvided pomija guard samojezdności). Spawn → pobierz kasę.
            if (!BuildAndSpawnDeliveryRun(v, dealerProvided: true)) return false;
            MoneyLedger.Spend(cost * 100L, "delivery", "dostawa pojazdu");
            Log.Info($"[DeliveryService] Wagon #{v.id} '{v.series}' — dostawa lokomotywą producenta ({cost} zł)");
            return true;
        }

        // ── IDeliveryActionsProvider (adapter vehicleId → FleetVehicleData dla UI w Depot) ──

        int IDeliveryActionsProvider.EstimateExpressCostZl(int vehicleId)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            return v != null ? EstimateExpressCostZl(v) : 0;
        }

        long IDeliveryActionsProvider.EstimateExpressTimeSec(int vehicleId)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            return v != null ? EstimateExpressTimeSec(v) : 0L;
        }

        bool IDeliveryActionsProvider.RequestExpressDelivery(int vehicleId)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            return v != null && RequestExpressDelivery(v);
        }

        bool IDeliveryActionsProvider.RequestScheduledDelivery(int vehicleId)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            return v != null && RequestScheduledDelivery(v);
        }

        bool IDeliveryActionsProvider.RequestDealerWagonDelivery(int vehicleId)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            return v != null && RequestDealerWagonDelivery(v);
        }

        bool IDeliveryActionsProvider.RequestOwnLocoWagonDelivery(int vehicleId)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            return v != null && RequestOwnLocoWagonDelivery(v);
        }

        bool IDeliveryActionsProvider.HasAvailableLocoForFetch() => FindAvailableFetchLoco() != null;

        // ── Wjazd do depot (wspólne dla ekspresu i rozkładu po dojechaniu) ──

        /// <summary>
        /// Wprowadza pojazd do zajezdni przez bramę (SpawnConsistAtEntry → handshake → InDepot).
        /// Status finalny (StoppedInDepot) ustawia <see cref="HandleLocationChanged"/> gdy pojazd
        /// faktycznie wjedzie (OnConsistEnteredDepot → SetInDepot). Fallback gdy brak symulatora.
        /// </summary>
        public void TriggerDepotEntry(FleetVehicleData v)
        {
            var sim = DepotMovementSimulator.Instance;
            var locSvc = VehicleLocationService.Instance;

            if (sim != null)
            {
                int consistId = sim.GenerateConsistId();
                locSvc?.SetEnteringDepot(v.id, consistId);
                bool spawned = sim.SpawnConsistAtEntry(consistId, new List<int> { v.id });
                if (spawned)
                {
                    v.currentTask = "Wjazd do zajezdni";
                    Log.Info($"[DeliveryService] Pojazd #{v.id} '{v.series}' dostarczony — wjazd do depot (consist#{consistId})");
                    return;
                }
                Log.Warn($"[DeliveryService] SpawnConsistAtEntry nie powiódł się dla pojazdu #{v.id} — fallback InDepot");
            }
            else
            {
                Log.Warn("[DeliveryService] DepotMovementSimulator.Instance null (scena Depot nie załadowana?) — fallback InDepot bez animacji");
            }

            // Fallback: bezpośrednio do depot (bez animacji wjazdu)
            locSvc?.SetInDepot(v.id, depotTrackId: -1);
            MarkInDepot(v);
        }

        void HandleLocationChanged(int vehicleId, VehicleLocationType oldType, VehicleLocationType newType)
        {
            if (newType != VehicleLocationType.InDepot) return;
            var v = FleetService.GetOwnedById(vehicleId);
            if (v == null) return;
            // Pojazd właśnie wjechał do zajezdni (po dostawie lub powrocie z trasy) → finalny status.
            if (v.status == FleetVehicleStatus.InTransit
                || v.status == FleetVehicleStatus.AwaitingPickup
                || v.status == FleetVehicleStatus.MovingOnMap
                || v.status == FleetVehicleStatus.StoppedOnMap)
            {
                MarkInDepot(v);
            }
        }

        void MarkInDepot(FleetVehicleData v)
        {
            v.status = FleetVehicleStatus.StoppedInDepot;
            v.deliveryInProgress = false; // F7: dostawa zakończona
            v.position ??= new VehiclePosition();
            v.position.kind = VehicleLocationKind.InDepot;
            v.position.depotId = 1;
            if (string.IsNullOrEmpty(v.currentTask) || v.currentTask.Contains("dostaw") || v.currentTask.Contains("Wjazd"))
                v.currentTask = "W zajezdni";
        }

        // ── F2: initial park startowego taboru ───────────────────────

        readonly HashSet<int> _parkWarnedNoTrack = new();

        /// <summary>
        /// F2: materializuje w zajezdni pojazdy StoppedInDepot bez rekordu lokalizacji
        /// (startowy tabor na nową grę). Po wczytaniu save pojazdy mają już rekord ze snapshotu,
        /// więc są pomijane (warunek <c>locSvc.Get == null</c>). Grupuje wg FleetConsistData —
        /// lokomotywa + wagony lądują jako jeden klikalny ConsistMarker na wspólnym torze.
        /// </summary>
        public void TryParkInitialDepotFleet()
        {
            var locSvc = VehicleLocationService.Instance;
            var sim = DepotMovementSimulator.Instance;
            if (locSvc == null || sim == null || !sim.IsGraphReady) return;

            List<FleetVehicleData> unparked = null;
            var owned = FleetService.OwnedVehicles;
            for (int i = 0; i < owned.Count; i++)
            {
                var v = owned[i];
                if (v == null || v.status != FleetVehicleStatus.StoppedInDepot) continue;
                if (locSvc.Get(v.id) != null) continue; // już zmaterializowany (load / wcześniejszy park)
                (unparked ??= new List<FleetVehicleData>()).Add(v);
            }
            if (unparked == null) return;

            var handled = new HashSet<int>();
            foreach (var v in unparked)
            {
                if (handled.Contains(v.id)) continue;

                // Grupuj wg składu (loko + wagony razem); solo gdy nie w żadnym FleetConsistData.
                var group = new List<int>();
                var consist = FindConsistFor(v.id);
                if (consist?.vehicleIds != null)
                    foreach (int vid in consist.vehicleIds)
                        if (FleetService.GetOwnedById(vid) != null && locSvc.Get(vid) == null) group.Add(vid);
                if (group.Count == 0) group.Add(v.id);

                int consistId = sim.GenerateConsistId();
                int trackId = sim.ParkConsistOnFreeTrack(consistId, group);
                if (trackId < 0)
                {
                    if (_parkWarnedNoTrack.Add(v.id))
                        Log.Warn($"[DeliveryService] Brak wolnego toru dla startowego składu (pojazd #{v.id}) — " +
                                 "ponowię gdy zwolni się tor parkingowy");
                    return; // przerwij — brak toru, spróbuj w następnym ticku
                }

                foreach (int vid in group)
                {
                    locSvc.SetInDepot(vid, trackId);
                    handled.Add(vid);
                    _parkWarnedNoTrack.Remove(vid);
                }
            }
        }

        static FleetConsistData FindConsistFor(int vehicleId)
        {
            foreach (var c in FleetService.Consists)
                if (c?.vehicleIds != null && c.vehicleIds.Contains(vehicleId)) return c;
            return null;
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>Dystans [km] punkt zakupu → home depot (linia prosta, na potrzeby wyceny).</summary>
        float ComputeDeliveryDistanceKm(FleetVehicleData v)
        {
            var from = DeliveryLocator.ResolvePurchaseLocation(v?.position?.externalLocation);
            var home = DeliveryLocator.ResolveHome();
            if (!from.IsValid || !home.IsValid || from.position == Vector2.zero || home.position == Vector2.zero)
                return DeliveryConstants.FallbackDistanceKm;
            return Vector2.Distance(from.position, home.position) / 1000f;
        }
    }
}
