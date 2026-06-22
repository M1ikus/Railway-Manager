using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Economy;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// Centralny runtime state taboru — posiadane pojazdy, składy, rynek wtórny, koszyk.
    /// Statyczny singleton w stylu GameState — przeżywa sceny, żyje do końca sesji.
    /// </summary>
    public static class FleetService
    {
        // ── State ──────────────────────────────────────
        // BUG-035: encapsulacja — backing fields private, public access jako IReadOnlyList.
        // Mutacja przez dedykowane API (Add/Remove/LoadSnapshot) z auto-Notify.
        // Wcześniej był public mutable List → caller mógł mutate bypassing event = state desync.

        private static readonly List<FleetVehicleData> _ownedVehicles = new();
        // B2: index po id — O(1) GetOwnedById zamiast linear scan O(N).
        // Sync z _ownedVehicles przez Add/Remove/Load API + Reset.
        private static readonly Dictionary<int, FleetVehicleData> _ownedById = new();
        private static readonly List<FleetConsistData> _consists = new();
        private static readonly List<FleetMarketVehicle> _marketVehicles = new();
        private static readonly List<CartItem> _cart = new();

        public static IReadOnlyList<FleetVehicleData> OwnedVehicles => _ownedVehicles;
        public static IReadOnlyList<FleetConsistData> Consists => _consists;
        public static IReadOnlyList<FleetMarketVehicle> MarketVehicles => _marketVehicles;
        public static IReadOnlyList<CartItem> Cart => _cart;

        /// <summary>B2: O(1) lookup po id (Dictionary index synchronizowany z _ownedVehicles).</summary>
        public static FleetVehicleData GetOwnedById(int id)
        {
            return _ownedById.TryGetValue(id, out var v) ? v : null;
        }

        // ── Mutator API (BUG-035) ──────────────────────

        /// <summary>Dodaje pojazd do OwnedVehicles + emituje NotifyOwnedChanged.</summary>
        public static void AddOwnedVehicle(FleetVehicleData v)
        {
            if (v == null) return;
            _ownedVehicles.Add(v);
            _ownedById[v.id] = v; // B2: keep index in sync
            OnOwnedChanged?.Invoke();
        }

        /// <summary>Usuwa pojazd z OwnedVehicles po id. Zwraca true gdy usunięto.</summary>
        public static bool RemoveOwnedVehicle(int id)
        {
            int removed = _ownedVehicles.RemoveAll(v => v.id == id);
            if (removed > 0)
            {
                _ownedById.Remove(id); // B2: keep index in sync
                OnOwnedChanged?.Invoke();
            }
            return removed > 0;
        }

        // ── Sprzedaż pojazdu z floty ───────────────────

        /// <summary>
        /// Veto sprzedaży z wyższej warstwy (Timetable: pojazd przypisany do obiegu).
        /// Zwraca powód [PL] gdy NIE wolno sprzedać, null gdy OK. Instalowany przez
        /// <c>FleetSellCirculationBootstrapper</c> (wzór jak DepotMovementSimulator.CirculationWarnHook).
        /// </summary>
        public static System.Func<int, string> SellVetoHook;

        /// <summary>
        /// Czy pojazd #id można sprzedać. <paramref name="reason"/> = null gdy tak, inaczej powód PL.
        /// Blokuje gdy: produkcja/dostawa/naprawa, w trasie/manewruje (M9c), w składzie,
        /// w aktywnym serwisie (outdoor/modernizacja/modyfikacja/malowanie) lub veto z obiegu.
        /// </summary>
        public static bool CanSellVehicle(int id, out string reason)
        {
            reason = null;
            var v = GetOwnedById(id);
            if (v == null) { reason = "Pojazd nie istnieje"; return false; }

            switch (v.status)
            {
                case FleetVehicleStatus.InProduction: reason = "Pojazd jeszcze w produkcji"; return false;
                case FleetVehicleStatus.InTransit:    reason = "Pojazd w dostawie"; return false;
                case FleetVehicleStatus.InRepair:     reason = "Pojazd w naprawie"; return false;
            }

            // Runtime lokalizacja (M9c) — w trasie / manewruje przez bramę
            var loc = VehicleLocationService.Instance?.Get(id);
            if (loc != null)
            {
                switch (loc.type)
                {
                    case VehicleLocationType.OnRoute:
                    case VehicleLocationType.ExitingDepot:
                    case VehicleLocationType.EnteringDepot:
                    case VehicleLocationType.InTransit:
                        reason = "Pojazd jest w trasie / manewruje"; return false;
                }
            }

            // Skład taboru (Fleet consist)
            if (!string.IsNullOrEmpty(v.assignedConsist))
            {
                reason = $"Pojazd w składzie „{v.assignedConsist}” — rozłącz najpierw"; return false;
            }
            foreach (var c in _consists)
                if (c?.vehicleIds != null && c.vehicleIds.Contains(id))
                {
                    reason = $"Pojazd w składzie „{c.name}” — rozłącz najpierw"; return false;
                }

            // Aktywne serwisy (wszystkie job-services w Fleet asmdef)
            if (OutdoorEquipmentJobService.GetActiveJobForVehicle(id) != null)   { reason = "Pojazd w serwisie zewnętrznym"; return false; }
            if (ModernizationJobService.GetActiveJobForVehicle(id) != null)      { reason = "Pojazd w modernizacji"; return false; }
            if (VehicleModificationJobService.GetActiveJobForVehicle(id) != null){ reason = "Pojazd w modyfikacji"; return false; }
            if (PaintingJobService.GetActiveJobForVehicle(id) != null)           { reason = "Pojazd w malarni"; return false; }
            if (SelfPaintingService.GetActiveJobForVehicle(id) != null)          { reason = "Pojazd w malarni"; return false; }

            // Veto z obiegu (Timetable)
            var veto = SellVetoHook?.Invoke(id);
            if (!string.IsNullOrEmpty(veto)) { reason = veto; return false; }

            return true;
        }

        /// <summary>
        /// Sprzedaje pojazd #id: liczy wartość odsprzedaży (<see cref="FleetResaleMath"/>),
        /// dopisuje przychód do kasy i usuwa pojazd z floty (+ OnOwnedChanged).
        /// Zwraca uzyskaną kwotę [zł] lub -1 gdy sprzedaż niemożliwa (z <paramref name="reason"/>).
        /// </summary>
        public static long SellVehicle(int id, out string reason)
        {
            if (!CanSellVehicle(id, out reason)) return -1;
            var v = GetOwnedById(id);
            long valueGroszy = FleetResaleMath.ResaleValueGroszy(v);
            if (valueGroszy > 0)
                MoneyLedger.Earn(valueGroszy, "fleet_sale", v.number);
            RemoveOwnedVehicle(id);
            Log.Info($"[FleetService] Sprzedano pojazd {v.number} za {valueGroszy / 100:N0} zł");
            return valueGroszy / 100;
        }

        /// <summary>Add to cart + emit Notify.</summary>
        public static void AddToCart(CartItem item)
        {
            if (item == null) return;
            _cart.Add(item);
            OnCartChanged?.Invoke();
        }

        /// <summary>Remove from cart by predicate. Zwraca count usuniętych.</summary>
        public static int RemoveFromCart(System.Predicate<CartItem> predicate)
        {
            if (predicate == null) return 0;
            int removed = _cart.RemoveAll(predicate);
            if (removed > 0) OnCartChanged?.Invoke();
            return removed;
        }

        /// <summary>Clear cart + emit Notify.</summary>
        public static void ClearCart()
        {
            if (_cart.Count == 0) return;
            _cart.Clear();
            OnCartChanged?.Invoke();
        }

        /// <summary>Add consist + emit Notify (uses OnOwnedChanged — composition affects fleet view).</summary>
        public static void AddConsist(FleetConsistData c)
        {
            if (c == null) return;
            _consists.Add(c);
            OnOwnedChanged?.Invoke();
        }

        /// <summary>Add market vehicle + emit Notify.</summary>
        public static void AddMarketVehicle(FleetMarketVehicle v)
        {
            if (v == null) return;
            _marketVehicles.Add(v);
            OnMarketChanged?.Invoke();
        }

        /// <summary>Remove specific market vehicle (e.g. po dodaniu do koszyka).</summary>
        public static bool RemoveMarketVehicle(FleetMarketVehicle v)
        {
            if (v == null) return false;
            bool removed = _marketVehicles.Remove(v);
            if (removed) OnMarketChanged?.Invoke();
            return removed;
        }

        /// <summary>Lookup cart item by cartId. Zwraca null gdy nie ma.</summary>
        public static CartItem GetCartItemById(int cartId)
        {
            foreach (var ci in _cart) if (ci.cartId == cartId) return ci;
            return null;
        }

        /// <summary>Remove consist by predicate.</summary>
        public static int RemoveConsists(System.Predicate<FleetConsistData> predicate)
        {
            if (predicate == null) return 0;
            int removed = _consists.RemoveAll(predicate);
            if (removed > 0) OnOwnedChanged?.Invoke();
            return removed;
        }

        // ── Save/Load snapshot API (BUG-035) — bulk replace bez per-item notify ──

        /// <summary>BUG-035: load snapshot z save (clear + addrange + 1× notify).</summary>
        public static void LoadOwnedSnapshot(IList<FleetVehicleData> snapshot)
        {
            _ownedVehicles.Clear();
            _ownedById.Clear(); // B2: keep index in sync
            if (snapshot != null)
            {
                _ownedVehicles.AddRange(snapshot);
                foreach (var v in snapshot)
                {
                    if (v == null) continue;
                    _ownedById[v.id] = v;
                    EvnGenerator.RegisterExisting(v.evn); // B4
                }
            }
            OnOwnedChanged?.Invoke();
        }

        public static void LoadConsistsSnapshot(IList<FleetConsistData> snapshot)
        {
            _consists.Clear();
            if (snapshot != null) _consists.AddRange(snapshot);
            OnOwnedChanged?.Invoke();
        }

        public static void LoadMarketSnapshot(IList<FleetMarketVehicle> snapshot)
        {
            _marketVehicles.Clear();
            if (snapshot != null)
            {
                _marketVehicles.AddRange(snapshot);
                // B4: zarejestruj przywrócone EVN z rynku (kandydat do zakupu zachowuje EVN)
                foreach (var mv in snapshot) EvnGenerator.RegisterExisting(mv?.evn);
            }
            OnMarketChanged?.Invoke();
        }

        public static void LoadCartSnapshot(IList<CartItem> snapshot)
        {
            _cart.Clear();
            if (snapshot != null) _cart.AddRange(snapshot);
            OnCartChanged?.Invoke();
        }

        public static int NextCartId { get; set; } = 1;
        public static bool IsInitialized { get; private set; }

        /// <summary>
        /// C4 defensive: SaveLoad orchestracja podnosi flagę przed restore i opuszcza po
        /// <see cref="MarkInitializedFromSave"/>. <see cref="Initialize"/> early-exit gdy
        /// true — chroni przed race condition: nowa gra → ResetForNewGame → IsInitialized=false
        /// → user otwiera FleetPanelUI w trakcie load → Initialize ładuje starting fleet →
        /// nadpisuje świeżo zrestorowany state.
        /// </summary>
        private static bool _isRestoringFromSave;

        // ── Events ─────────────────────────────────────
        public static event Action OnOwnedChanged;
        public static event Action OnMarketChanged;
        public static event Action OnCartChanged;

        public static void NotifyOwnedChanged() => OnOwnedChanged?.Invoke();
        public static void NotifyMarketChanged() => OnMarketChanged?.Invoke();
        public static void NotifyCartChanged() => OnCartChanged?.Invoke();

        // ── Initialization ─────────────────────────────
        public static void ResetForNewGame()
        {
            _ownedVehicles.Clear();
            _ownedById.Clear(); // B2
            _consists.Clear();
            _marketVehicles.Clear();
            _cart.Clear();
            NextCartId = 1;
            IsInitialized = false;
            _isRestoringFromSave = false; // C4
            // B4: reset rejestru EVN — nowa gra startuje z czystą pulą
            EvnGenerator.ResetRegistry();
            OnOwnedChanged?.Invoke();
            OnMarketChanged?.Invoke();
            OnCartChanged?.Invoke();
        }

        /// <summary>C4: SaveLoad woła PRZED `Deserialize` żeby zablokować Initialize w trakcie restore.</summary>
        public static void BeginRestoreFromSave()
        {
            _isRestoringFromSave = true;
        }

        public static void MarkInitializedFromSave()
        {
            IsInitialized = true;
            _isRestoringFromSave = false;
        }

        public static void Initialize()
        {
            // C4: chroni przed wbiciem starting fleet w trakcie save restore
            if (IsInitialized || _isRestoringFromSave) return;

            if (!FleetCatalog.IsLoaded)
                FleetCatalog.LoadAll();

            // M7-1: inspection intervals per-seria
            if (!InspectionCatalog.IsLoaded)
                InspectionCatalog.LoadAll();

            // BUG-029: clear cart from previous session (klik "Nowa gra" bez load)
            _cart.Clear();

            LoadStartingFleet();

            // Market = initial pool from catalog (deep copy not needed — each vehicle is unique)
            _marketVehicles.Clear();
            _marketVehicles.AddRange(FleetCatalog.InitialMarket);
            // B4: zarejestruj EVN z katalogu market (initial_market.json)
            foreach (var mv in FleetCatalog.InitialMarket) EvnGenerator.RegisterExisting(mv?.evn);

            IsInitialized = true;

            // M-Fleet-3: bootstrap refresh watchdog (subscribes GameState.OnDayEnded)
            FleetMarketRefreshService.EnsureExists();
        }

        /// <summary>
        /// Startowy tabor gracza — ładowany z `StreamingAssets/Fleet/starting_fleet.json`.
        /// Wcześniej był 180-liniowy hardcoded mock w kodzie (A2 refactor zamyka 2-letni TODO).
        /// Pola derived (evn/components/inspections/monthlyMaintenanceCost/position/comfortFeatures)
        /// liczone tu na podstawie wczytanych danych źródłowych.
        /// </summary>
        private static void LoadStartingFleet()
        {
            _ownedVehicles.Clear();
            _ownedById.Clear();
            _consists.Clear();

            var data = StartingFleetCatalog.Load();
            if (data == null)
            {
                Log.Error("[FleetService] starting_fleet.json nie wczytany — gracz startuje bez taboru");
                return;
            }

            // RandomRegistry-based RNG dla determinizmu MP-9 (był UnityEngine.Random).
            var rng = RandomRegistry.GetRng("StartingFleet");

            int trackIndex = 1;
            foreach (var dto in data.vehicles)
            {
                var v = StartingFleetCatalog.ToVehicle(dto);
                FinalizeStartingVehicle(v, trackIndex++, rng);
                _ownedVehicles.Add(v);
                _ownedById[v.id] = v;
            }

            if (data.consists != null)
                foreach (var c in data.consists)
                    _consists.Add(StartingFleetCatalog.ToConsist(c));
        }

        /// <summary>Wypełnia pola derived dla nowo zaspawnowanego startowego pojazdu.</summary>
        static void FinalizeStartingVehicle(FleetVehicleData v, int trackIndex, DeterministicRng rng)
        {
            v.evn = EvnGenerator.Generate(v.type, v.id);
            v.cleanlinessPercent = rng.Range(60f, 95f);
            v.components = VehicleComponents.New(v.conditionPercent);
            v.purchaseGameTime = 0;
            v.history = new List<MaintenanceRecord>
            {
                new()
                {
                    gameTimeSeconds = 0,
                    recordType = MaintenanceRecordTypes.Purchase,
                    description = "Pojazd w posiadaniu od początku gry",
                    cost = 0,
                    mileageAtRecord = v.mileageKm,
                }
            };
            v.monthlyMaintenanceCost = MaintenanceCostCalculator.Calculate(v, 2024);

            v.position = new VehiclePosition();
            if (v.status == FleetVehicleStatus.StoppedInDepot || v.status == FleetVehicleStatus.MovingInDepot
                || v.status == FleetVehicleStatus.InRepair)
            {
                v.position.kind = VehicleLocationKind.InDepot;
                v.position.depotId = 1;
                v.position.depotTrackName = $"Tor {trackIndex}";
            }
            else if (v.status == FleetVehicleStatus.MovingOnMap || v.status == FleetVehicleStatus.StoppedOnMap)
            {
                v.position.kind = VehicleLocationKind.OnMap;
                v.position.currentLineId = v.assignedConsist != null ? "R1" : "S1";
            }

            if (v.type == FleetVehicleType.EMU || v.type == FleetVehicleType.DMU || v.type == FleetVehicleType.PassengerCar)
                v.comfortFeatures = new List<string> { ComfortCalculator.AirConditioning, ComfortCalculator.PowerSockets };

            int ageYears = UnityEngine.Mathf.Max(1, 2024 - v.productionYear);
            v.inspections = InspectionSchedule.Reconstruct(
                nowGameTime: 0,
                currentMileage: v.mileageKm,
                hoursSinceP1: rng.Range(6f, 60f),
                daysSinceP2:  rng.Range(3f, 22f),
                kmSinceP3:    UnityEngine.Mathf.Min(v.mileageKm, rng.Range(40_000f, 220_000f)),
                kmSinceP4:    UnityEngine.Mathf.Min(v.mileageKm, rng.Range(150_000f, 470_000f)),
                yearsSinceP4: UnityEngine.Mathf.Min(ageYears, rng.Range(0.5f, 4.2f)),
                kmSinceP5:    UnityEngine.Mathf.Min(v.mileageKm, rng.Range(500_000f, 2_500_000f)),
                yearsSinceP5: UnityEngine.Mathf.Min(ageYears, rng.Range(5f, 28f)));
        }

    }
}
