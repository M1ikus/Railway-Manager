using System.Collections.Generic;
using RailwayManager.Core;

namespace DepotSystem.RoomLevel
{
    /// <summary>
    /// MM-1 — static catalog wymagań lvla per RoomType.
    ///
    /// Wszystkie liczby z `memory/modernization_design.md` sekcja 2.2 — propozycyjne,
    /// do iteracji w M-Balance / playtestach. Magic numbers świadomie tu (nie w
    /// `*Constants.cs`) — to są wartości designerskie konkretnie dla M-Modernization,
    /// nie balansowe stałe gameplay'u.
    ///
    /// Lookup: <see cref="GetRequirements(RoomType, int)"/> dla konkretnego (typ, lvl).
    /// <see cref="GetMaxLevel(RoomType)"/> = 5 dla wszystkich obsługiwanych typów,
    /// 0 dla nielvlable (Storage, Locker, Corridor, None).
    ///
    /// Future scope (MM-D22): TrainingRoom dorzucone z <c>disabled=true</c>, jako placeholder
    /// architectoniczny. <see cref="GetRequirements"/> ignoruje disabled entries.
    /// </summary>
    public static class RoomLevelCatalog
    {
        private static readonly Dictionary<(RoomType, int), RoomLevelRequirements> _byKey = new();
        private static readonly Dictionary<RoomType, int> _maxLevels = new();
        private static bool _initialized;

        /// <summary>Lista typów pokoi które mają lvlowanie (cap = 5).</summary>
        public static readonly RoomType[] LvlableRoomTypes =
        {
            RoomType.Hall,
            RoomType.Office,
            RoomType.Dispatcher,
            RoomType.Supervisor,
            RoomType.TrafficController,
            RoomType.Social,
            RoomType.Bathroom,
        };

        /// <summary>Domyślny lvl dla nowo wykrytego pokoju (przed pierwszym awansem, MM-D2).</summary>
        public const int DefaultLevel = 1;

        /// <summary>Max lvl dla wszystkich obsługiwanych typów (5).</summary>
        public const int MaxLevel = 5;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            BuildCatalog();
            Log.Info($"[RoomLevelCatalog] Loaded {_byKey.Count} requirement entries " +
                     $"(7 lvlable types × 5 lvl + TrainingRoom×5 disabled future).");
        }

        public static RoomLevelRequirements GetRequirements(RoomType roomType, int level)
        {
            EnsureInitialized();
            if (!_byKey.TryGetValue((roomType, level), out var req)) return null;
            if (req.disabled) return null; // future scope (TrainingRoom) niewidoczne w runtime
            return req;
        }

        /// <summary>Max lvl osiągalny dla danego typu pokoju (5 dla lvlable, 0 dla pozostałych).</summary>
        public static int GetMaxLevel(RoomType roomType)
        {
            EnsureInitialized();
            return _maxLevels.TryGetValue(roomType, out int max) ? max : 0;
        }

        /// <summary>Czy ten typ pokoju ma lvlowanie (np. Hall=true, Storage=false).</summary>
        public static bool IsLvlable(RoomType roomType) => GetMaxLevel(roomType) > 0;

        // ════════════════════════════════════════════════════════
        //  CATALOG DEFINITION (z spec'a sekcja 2.2)
        // ════════════════════════════════════════════════════════

        private static void Add(RoomLevelRequirements req)
        {
            _byKey[(req.roomType, req.level)] = req;
            if (!req.disabled)
            {
                if (!_maxLevels.TryGetValue(req.roomType, out int cur) || req.level > cur)
                    _maxLevels[req.roomType] = req.level;
            }
        }

        // Helpers żeby zwięzłe definicje były czytelne:
        private static FurnitureRequirement Item(string itemId, int n)
            => new FurnitureRequirement(FurnitureReqKind.ItemId, itemId, n);
        private static FurnitureRequirement Fn(string functionName, int n)
            => new FurnitureRequirement(FurnitureReqKind.Function, functionName, n);
        private static FurnitureRequirement Compound(string compoundName, int n)
            => new FurnitureRequirement(FurnitureReqKind.Compound, compoundName, n);

        private static void BuildCatalog()
        {
            // ─────────────────────────────────────────────────
            //  HALL (warsztat) — lvl1-5
            //  Bonus: P-poziom inspection (lvl1=P1, lvl5=P5 + modernizacje internal MM-D13)
            // ─────────────────────────────────────────────────
            Add(new RoomLevelRequirements(RoomType.Hall, 1, 100f,
                Item("pit_small", 1),
                Item("tool_cabinet", 1),
                Item("locker_personal", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Hall, 2, 250f,
                Fn("ServicePit", 2),
                Item("tool_cabinet", 2),
                Item("locker_personal", 2),
                Item("tool_cart", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Hall, 3, 500f,
                Fn("ServicePit", 3),
                Item("tool_cabinet", 3),
                Item("tool_cart", 2),
                Item("lift_aux", 1),
                Item("bench_long", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Hall, 4, 1000f,
                Fn("ServicePit", 4),
                Item("tool_cabinet", 4),
                Item("tool_cart", 3),
                Item("lift_aux", 2)
                // Plus outdoor (Turntable LUB WashZone) — walidowane osobno w RoomLevelService
                // (MM-2), bo outdoor to DepotSystem.OutdoorEquipment, nie furniture.
            ));
            Add(new RoomLevelRequirements(RoomType.Hall, 5, 2000f,
                Fn("ServicePit", 5),
                Item("tool_cabinet", 5),
                Item("tool_cart", 4),
                Item("lift_aux", 3)
                // Plus outdoor Turntable + WashZone + per-depot StorageGoods ≥1500 units
                // — walidowane w RoomLevelService.
            ));

            // ─────────────────────────────────────────────────
            //  OFFICE (biuro) — lvl1-5
            //  Bonus: cap biurowych (2/4/6/8/12) + R&D speed × (1.0/1.2/1.5/1.8/2.2)
            // ─────────────────────────────────────────────────
            Add(new RoomLevelRequirements(RoomType.Office, 1, 9f,
                Compound("WorkstationOfficeComplete", 1),
                Item("cabinet_archive", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Office, 2, 16f,
                Compound("WorkstationOfficeComplete", 2),
                Item("cabinet_archive", 2),
                Item("printer_office", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Office, 3, 25f,
                Compound("WorkstationOfficeComplete", 3),
                Item("cabinet_archive", 3),
                Item("printer_office", 1),
                Item("vending_machine", 1),
                Item("sofa_office", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Office, 4, 40f,
                Compound("WorkstationOfficeComplete", 5),
                Item("cabinet_archive", 4),
                Item("printer_office", 2),
                Item("sofa_office", 1),
                Item("plant_decor", 2)
            ));
            Add(new RoomLevelRequirements(RoomType.Office, 5, 60f,
                Compound("WorkstationOfficeComplete", 8),
                Item("cabinet_archive", 6),
                Item("printer_office", 3),
                Item("sofa_office", 2),
                Item("plant_decor", 3),
                Item("clock_wall", 1)
            ));

            // ─────────────────────────────────────────────────
            //  DISPATCHER (dyspozytor) — lvl1-5
            //  Bonus: cap dyspozytorów (1/2/3/4/5) + onboarding speed (2.0×/1.5×/1.0×/0.7×/0.5×)
            // ─────────────────────────────────────────────────
            Add(new RoomLevelRequirements(RoomType.Dispatcher, 1, 9f,
                Compound("WorkstationOfficeComplete", 1),
                Item("cabinet_archive", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Dispatcher, 2, 12f,
                Compound("WorkstationOfficeComplete", 2),
                Item("cabinet_archive", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Dispatcher, 3, 18f,
                Compound("WorkstationOfficeComplete", 3),
                Item("cabinet_archive", 2),
                Item("printer_office", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Dispatcher, 4, 25f,
                Compound("WorkstationOfficeComplete", 4),
                Item("cabinet_archive", 3),
                Item("vending_machine", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Dispatcher, 5, 35f,
                Compound("WorkstationOfficeComplete", 5),
                Item("cabinet_archive", 4),
                Item("printer_office", 2),
                Item("sofa_office", 1)
            ));

            // ─────────────────────────────────────────────────
            //  TRAFFIC CONTROLLER (dyżurny ruchu) — lvl1-5
            //  Bonus: cap dyżurnych (1/2/3/4/5). Akcje per dyżurny = 1+skill (MM-D12).
            // ─────────────────────────────────────────────────
            Add(new RoomLevelRequirements(RoomType.TrafficController, 1, 12f,
                Compound("WorkstationTrafficComplete", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.TrafficController, 2, 18f,
                Compound("WorkstationTrafficComplete", 2),
                Item("cabinet_archive", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.TrafficController, 3, 25f,
                Compound("WorkstationTrafficComplete", 3),
                Item("cabinet_archive", 2),
                Item("printer_office", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.TrafficController, 4, 35f,
                Compound("WorkstationTrafficComplete", 4),
                Item("cabinet_archive", 3),
                Item("vending_machine", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.TrafficController, 5, 50f,
                Compound("WorkstationTrafficComplete", 5),
                Item("cabinet_archive", 4),
                Item("sofa_office", 1)
            ));

            // ─────────────────────────────────────────────────
            //  SUPERVISOR (naczelnik) — lvl1-5
            //  Bonus: globalny morale +1/+2/+3/+4/+5 (best-lvl wins MM-D15)
            // ─────────────────────────────────────────────────
            Add(new RoomLevelRequirements(RoomType.Supervisor, 1, 9f,
                Compound("WorkstationOfficeComplete", 1),
                Item("cabinet_archive", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Supervisor, 2, 12f,
                Compound("WorkstationOfficeComplete", 1),
                Item("cabinet_archive", 2),
                Item("printer_office", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Supervisor, 3, 16f,
                Compound("WorkstationOfficeComplete", 1),
                Item("cabinet_archive", 3),
                Item("sofa_office", 1),
                Item("vending_machine", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Supervisor, 4, 20f,
                Compound("WorkstationOfficeComplete", 1),
                Item("cabinet_archive", 4),
                Item("sofa_office", 1),
                Item("plant_decor", 2),
                Item("clock_wall", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Supervisor, 5, 25f,
                Compound("WorkstationOfficeComplete", 1),
                Item("cabinet_archive", 5),
                Item("sofa_office", 2),
                Item("plant_decor", 3),
                Item("lamp_floor", 1),
                Item("board_wall", 1)
            ));

            // ─────────────────────────────────────────────────
            //  SOCIAL (socjalny) — lvl1-5
            //  Bonus: morale +1/+2/+3/+4/+5 per pracownik z accessSide free
            // ─────────────────────────────────────────────────
            Add(new RoomLevelRequirements(RoomType.Social, 1, 6f,
                Item("chair_basic", 2),
                Item("table_small", 1),
                Item("vending_machine", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Social, 2, 10f,
                Item("chair_basic", 4),
                Item("table_small", 2),
                Item("vending_machine", 1),
                Item("sofa_office", 1),
                Item("kitchen_unit", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Social, 3, 15f,
                Item("chair_basic", 6),
                Item("table_small", 3),
                Item("vending_machine", 2),
                Item("sofa_office", 2),
                Item("kitchen_unit", 1),
                Item("bench_long", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Social, 4, 22f,
                Item("chair_basic", 8),
                Item("table_small", 4),
                Item("vending_machine", 2),
                Item("sofa_office", 3),
                Item("kitchen_unit", 2),
                Item("bench_long", 2),
                Item("plant_decor", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Social, 5, 30f,
                Item("chair_basic", 10),
                Item("table_small", 5),
                Item("vending_machine", 3),
                Item("sofa_office", 4),
                Item("kitchen_unit", 2),
                Item("bench_long", 3),
                Item("plant_decor", 2),
                Item("clock_wall", 1)
            ));

            // ─────────────────────────────────────────────────
            //  BATHROOM (łazienka) — lvl1-5
            //  Bonus: morale +0.5/+1/+1.5/+2/+2.5 per pracownik z accessSide free
            // ─────────────────────────────────────────────────
            Add(new RoomLevelRequirements(RoomType.Bathroom, 1, 4f,
                Item("wc_cabin", 1),
                Item("sink_row", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Bathroom, 2, 6f,
                Item("wc_cabin", 2),
                Item("sink_row", 1),
                Item("shower_cabin", 1)
            ));
            Add(new RoomLevelRequirements(RoomType.Bathroom, 3, 10f,
                Item("wc_cabin", 3),
                Item("sink_row", 2),
                Item("shower_cabin", 2)
            ));
            Add(new RoomLevelRequirements(RoomType.Bathroom, 4, 15f,
                Item("wc_cabin", 4),
                Item("sink_row", 3),
                Item("shower_cabin", 3)
            ));
            Add(new RoomLevelRequirements(RoomType.Bathroom, 5, 20f,
                Item("wc_cabin", 6),
                Item("sink_row", 4),
                Item("shower_cabin", 4)
            ));

            // ─────────────────────────────────────────────────
            //  TRAINING ROOM (sala szkoleń) — FUTURE SCOPE (post-EA, MM-D22)
            //  Disabled w EA, uruchamiane przez M8.5 zmianę disabled→false bez refactoru.
            // ─────────────────────────────────────────────────
            // Wymagania szczątkowe (do dopracowania w M8.5):
            //   lvl1 ≥15m² + desk_office×4 + chair_basic×8 + board_wall×1
            //   lvl5 ≥50m² + desk_office×12 + chair_basic×24 + monitor_desk×4 +
            //         printer_office×2 + cabinet_archive×4 + board_wall×3
            // (RoomType.TrainingRoom enum jeszcze nie istnieje — będzie dodany przy
            //  starcie M8.5 razem z aktywacją catalog'u. W EA placeholder pominięty.)
        }
    }
}
