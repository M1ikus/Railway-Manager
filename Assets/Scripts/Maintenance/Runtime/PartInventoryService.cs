using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Maintenance
{
    /// <summary>M7-6: Pending order części w drodze do home depotu.</summary>
    [Serializable]
    public class PendingPartOrder
    {
        public ComponentType type;
        public int quantity;
        public int daysRemaining;
        public string orderedDateIso;
        public int totalCostGroszy;
    }

    /// <summary>M7-6: Wpis historii zakupów (do UI).</summary>
    [Serializable]
    public class PartPurchaseRecord
    {
        public string dateIso;
        public ComponentType type;
        public int quantity;
        public int totalCostGroszy;
    }

    /// <summary>
    /// M7-6: Magazyn części + zamówienia + dostawa.
    ///
    /// - Stock per ComponentType (int count)
    /// - Zamówienia przez <see cref="OrderParts"/> — koszt do EconomyManager, dostawa N dni gry
    /// - Per OnDayEnded — decrement daysRemaining, gdy 0 → add to stock
    /// - <see cref="ConsumePart"/> — zmniejsza stock (dla WorkshopManager P4/P5)
    ///
    /// Żyje w Maintenance asmdef (split 2026-05-15) — używa EconomyManager z Timetable.Economy.
    /// </summary>
    public class PartInventoryService : MonoBehaviour
    {
        public static PartInventoryService Instance { get; private set; }

        /// <summary>Limit historii zakupów (ostatnie N wpisów w UI).</summary>
        public const int HistoryMax = 30;

        readonly Dictionary<ComponentType, int> _stock = new();
        readonly List<PendingPartOrder> _pending = new();
        readonly List<PartPurchaseRecord> _history = new();

        // MF-8: per-depot capacity (units = max parts that depot can store, summed from
        // active StorageGoods furniture via PartInventoryFurnitureBridge).
        // Stock pozostaje global w EA (single depot) — pełny per-depot stock refactor
        // odłożony do M-Modernization gdy multi-depot się materializuje.
        readonly Dictionary<int, int> _depotCapacities = new();

        /// <summary>Default capacity dla depota bez postawionych regałów (placeholder budget M-Balance).</summary>
        public const int DefaultDepotCapacity = 500;

        public IReadOnlyList<PendingPartOrder> PendingOrders => _pending;
        public IReadOnlyList<PartPurchaseRecord> History => _history;

        public event Action OnStockChanged;
        /// <summary>MF-8: emitowany gdy capacity któregoś z depot się zmieni (bridge update).</summary>
        public event Action<int> OnCapacityChanged;

        public static PartInventoryService EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PartInventoryService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PartInventoryService>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // Initialize empty stock
            foreach (var type in PartCatalog.AllTypes)
                _stock[type] = 0;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable() { GameState.OnDayEnded += HandleDayEnded; }
        void OnDisable() { GameState.OnDayEnded -= HandleDayEnded; }

        void HandleDayEnded(string dateJustEnded)
        {
            bool anyChange = false;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var po = _pending[i];
                po.daysRemaining--;
                if (po.daysRemaining <= 0)
                {
                    _stock[po.type] = GetStock(po.type) + po.quantity;
                    _pending.RemoveAt(i);
                    Log.Info($"[PartInventory] Dostawa: {po.quantity}× {PartCatalog.Get(po.type).displayName} " +
                             $"(nowy stan: {_stock[po.type]})");
                    anyChange = true;
                }
                else
                {
                    _pending[i] = po;
                }
            }
            if (anyChange) OnStockChanged?.Invoke();
        }

        public int GetStock(ComponentType type)
            => _stock.TryGetValue(type, out int n) ? n : 0;

        public bool HasStock(ComponentType type, int count) => GetStock(type) >= count;

        /// <summary>
        /// Zamawia parts — odejmuje koszt, dodaje pending order z licznikiem dostawy.
        /// Zwraca false gdy brak kasy.
        /// </summary>
        public bool OrderParts(ComponentType type, int quantity)
        {
            if (quantity <= 0) return false;

            var info = PartCatalog.Get(type);

            // BUG-082: long arithmetic + clamp dla int.MaxValue. Engine 15M groszy × 144+ szt
            // = overflow do ujemnego/małego int → totalCost się załamuje, refund/kasa nieprawidłowe.
            // Wzorzec analogiczny do BUG-073 (SubsidyCalculator).
            long totalCostLong = (long)info.priceGroszy * quantity;
            if (totalCostLong > int.MaxValue)
            {
                Log.Warn($"[PartInventory] Overflow przy zamówieniu {quantity}× {info.displayName}: " +
                         $"{totalCostLong}gr > int.MaxValue. Zamówienie odrzucone.");
                return false;
            }
            if (totalCostLong < 0) return false; // sanity check
            int totalCost = (int)totalCostLong;

            long cashGr = (long)(GameState.Money * 100L);
            if (cashGr < totalCost)
            {
                Log.Warn($"[PartInventory] Brak kasy na zamówienie: potrzeba {totalCost / 100f:F0}zł, mamy {GameState.Money}zł");
                return false;
            }

            var econ = RailwayManager.Timetable.Economy.EconomyManager.Instance;
            if (econ != null)
            {
                econ.AddCost(-1, totalCost, "parts",
                    $"Zamówienie {quantity}× {info.displayName}");
            }
            else
            {
                // Fallback — odejmij bezpośrednio GameState.Money
                GameState.Money -= totalCost / 100;
            }

            var order = new PendingPartOrder
            {
                type = type,
                quantity = quantity,
                daysRemaining = info.deliveryDays,
                orderedDateIso = GameState.CurrentDateIso,
                totalCostGroszy = totalCost,
            };
            _pending.Add(order);

            _history.Add(new PartPurchaseRecord
            {
                dateIso = GameState.CurrentDateIso,
                type = type,
                quantity = quantity,
                totalCostGroszy = totalCost,
            });
            while (_history.Count > HistoryMax) _history.RemoveAt(0);

            Log.Info($"[PartInventory] Zamówienie: {quantity}× {info.displayName}, " +
                     $"koszt {totalCost / 100f:F0}zł, dostawa za {info.deliveryDays} dni");

            OnStockChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// M7-6: zużywa części ze stanu. Zwraca false gdy brak wymaganej ilości.
        /// Per M7-6 MVP — WorkshopManager wywołuje przy completion P4/P5, ale na razie
        /// tylko loguje brak (nie blokuje przeglądu). Pełna blokada w M7-7.
        /// </summary>
        public bool ConsumePart(ComponentType type, int count = 1)
        {
            if (count <= 0) return true;
            int avail = GetStock(type);
            if (avail < count)
            {
                Log.Warn($"[PartInventory] Brak części {PartCatalog.Get(type).displayName}: " +
                         $"potrzeba {count}, mamy {avail}");
                return false;
            }
            _stock[type] = avail - count;
            OnStockChanged?.Invoke();
            return true;
        }

        // ════════════════════════════════════════
        //  MF-8 — per-depot capacity API
        // ════════════════════════════════════════

        /// <summary>
        /// MF-8: zwraca capacity (max parts) dla danego depota.
        /// Brak entry → DefaultDepotCapacity (placeholder na regały MF-8 bridge).
        /// </summary>
        public int GetCapacity(int depotId)
        {
            return _depotCapacities.TryGetValue(depotId, out int cap) ? cap : DefaultDepotCapacity;
        }

        /// <summary>
        /// MF-8: ustawia capacity dla depota. Wywoływane przez PartInventoryFurnitureBridge
        /// po recompute (sum aktywnych StorageGoods × capacityPerCell).
        /// Zero/negative → default capacity.
        /// </summary>
        public void SetCapacity(int depotId, int capacity)
        {
            int oldCap = GetCapacity(depotId);
            int newCap = capacity > 0 ? capacity : DefaultDepotCapacity;
            if (oldCap == newCap) return;
            _depotCapacities[depotId] = newCap;
            OnCapacityChanged?.Invoke(depotId);
        }

        /// <summary>
        /// MF-8: aktualny stock total per-depot. W EA single-depot zawsze zwraca _stock summed.
        /// W M-Modernization gdy multi-depot wejdzie, stock też będzie per-depot.
        /// </summary>
        public int GetTotalStock(int depotId)
        {
            int total = 0;
            foreach (var kv in _stock) total += kv.Value;
            return total;
        }

        /// <summary>MF-8: czy depot jest przepełniony (stock > capacity).</summary>
        public bool IsOverCapacity(int depotId)
        {
            return GetTotalStock(depotId) > GetCapacity(depotId);
        }

        // ════════════════════════════════════════
        //  Save/Load API (zamiast reflection w MaintenanceSavable)
        // ════════════════════════════════════════

        /// <summary>Snapshot stock per ComponentType — dla save/load (read-only view).</summary>
        public IReadOnlyDictionary<ComponentType, int> StockSnapshot => _stock;

        /// <summary>
        /// Restore'uje stock + pending + history z save'a. Public API zamiast reflection
        /// na private fieldy (`_stock`, `_pending`, `_history`) — rename pola łapany w
        /// compile zamiast silently no-op.
        ///
        /// Re-init brakujących typów na 0 (gdyby PartCatalog miał nowe od save'a).
        /// </summary>
        public void RestoreFromSave(IDictionary<ComponentType, int> stock,
                                    IList<PendingPartOrder> pending,
                                    IList<PartPurchaseRecord> history)
        {
            _stock.Clear();
            if (stock != null)
            {
                foreach (var kv in stock)
                    _stock[kv.Key] = kv.Value;
            }
            // Re-init brakujących typów na 0 (gdyby PartCatalog miał nowe od save'a)
            foreach (var type in PartCatalog.AllTypes)
                if (!_stock.ContainsKey(type)) _stock[type] = 0;

            _pending.Clear();
            if (pending != null)
            {
                foreach (var po in pending)
                    if (po != null) _pending.Add(po);
            }

            _history.Clear();
            if (history != null)
            {
                foreach (var rec in history)
                    if (rec != null) _history.Add(rec);
            }

            OnStockChanged?.Invoke();
        }

        /// <summary>Reset stock + pending + history (jak nowa gra).</summary>
        public void ResetRuntime()
        {
            _stock.Clear();
            foreach (var type in PartCatalog.AllTypes)
                _stock[type] = 0;
            _pending.Clear();
            _history.Clear();
            _depotCapacities.Clear();
            OnStockChanged?.Invoke();
        }

        [ContextMenu("Debug: Add 10 of each part")]
        public void DebugAddAll()
        {
            foreach (var type in PartCatalog.AllTypes)
                _stock[type] = GetStock(type) + 10;
            OnStockChanged?.Invoke();
            Log.Info("[PartInventory] Debug: +10 each part");
        }

        [ContextMenu("Debug: Dump inventory")]
        public void DebugDump()
        {
            Log.Info("[PartInventory] Stock:");
            foreach (var type in PartCatalog.AllTypes)
                Log.Info($"  {type}: {GetStock(type)} szt");
            Log.Info($"[PartInventory] Pending orders: {_pending.Count}");
            foreach (var po in _pending)
                Log.Info($"  {po.quantity}× {po.type}, {po.daysRemaining} dni");

            // MF-8: dump per-depot capacity
            Log.Info($"[PartInventory] Per-depot capacity:");
            if (_depotCapacities.Count == 0)
            {
                Log.Info($"  (brak entries — wszystkie depoty defaultują do {DefaultDepotCapacity})");
            }
            else
            {
                foreach (var kv in _depotCapacities)
                {
                    int totalStock = GetTotalStock(kv.Key);
                    bool over = totalStock > kv.Value;
                    Log.Info($"  depot={kv.Key}: capacity={kv.Value}, totalStock={totalStock}{(over ? " [OVER]" : "")}");
                }
            }
        }
    }
}
