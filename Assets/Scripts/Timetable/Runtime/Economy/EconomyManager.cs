using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;
using RailwayManager.Economy;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M6-3: Centralny tracker przychodów i kosztów.
    /// - Dodaje do <see cref="GameState.Money"/> na żywo przy każdym evencie (revenue/cost)
    /// - Trzyma running balance dziś + historia dziennych bilansów
    /// - Per-line breakdown (per circulationId)
    ///
    /// Koszty per km, overhead, subsidies — doklejane w kolejnych podetapach (M6-4, M6-6).
    /// M6-3 MVP: TYLKO revenue z biletów.
    /// </summary>
    public class EconomyManager : MonoBehaviour
    {
        public static EconomyManager Instance { get; private set; }

        // ── Running state (dziś) ────────────────────────────────────

        // M-Economy: sumy dzienne + ruch Money delegowane do MoneyLedger (asmdef Economy, jedyny
        // ruchacz pieniędzy). EconomyManager pozostaje fasadą per-obieg/historia/dotacje na tym
        // prymitywie. Pass-through props → 19 callerów + FinancePanelUI + EconomySavable bez zmian.
        public long RevenueTodayGroszy => MoneyLedger.RevenueTodayGroszy;
        public long CostsTodayGroszy => MoneyLedger.CostsTodayGroszy;
        public long SubsidiesTodayGroszy => MoneyLedger.SubsidiesTodayGroszy;
        public long NetTodayGroszy => MoneyLedger.NetTodayGroszy;

        /// <summary>Per-linia (circulationId) breakdown aktualnego dnia.</summary>
        readonly Dictionary<int, LineBalance> _lineBalances = new();

        public IReadOnlyDictionary<int, LineBalance> LineBalances => _lineBalances;

        // ── Historia ─────────────────────────────────────────────────

        readonly List<DailyBalance> _history = new();
        public IReadOnlyList<DailyBalance> History => _history;

        /// <summary>
        /// BUG-040: max długość historii (~1 rok). Bez trim'a 5 lat gameplay = 1825+ entries
        /// × Dictionary<int, LineBalance> → save bloat + RAM. Wzorzec z PartInventoryService.
        /// Post-EA: rozważyć monthly aggregate dla starszych dni (longer-term retention).
        /// </summary>
        const int HistoryMax = 365;

        // ── Bootstrap ────────────────────────────────────────────────

        public static EconomyManager EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("EconomyManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<EconomyManager>();
            Log.Info("[EconomyManager] Bootstrapped");
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            GameState.OnDayEnded += OnDayEnded;
            RailwayManager.Timetable.Simulation.TrainRunSimulator.OnRunDespawned += OnRunDespawned;
        }

        void OnDisable()
        {
            GameState.OnDayEnded -= OnDayEnded;
            RailwayManager.Timetable.Simulation.TrainRunSimulator.OnRunDespawned -= OnRunDespawned;
        }

        /// <summary>TD-007: threshold definicji "on-time" — ±5 min od planowanego czasu.</summary>
        const int PunctualityThresholdSec = 300;

        void OnRunDespawned(TrainRun tr)
        {
            if (tr == null || tr.isCancelled) return;
            if (tr.circulationId < 0) return;
            if (!_lineBalances.TryGetValue(tr.circulationId, out var lb))
            {
                lb = new LineBalance { circulationId = tr.circulationId };
                _lineBalances[tr.circulationId] = lb;
            }
            lb.runsCompletedToday++;

            // TD-007 (M-Balance 2026-05-06): punctuality classification dla dotacji.
            if (tr.currentDelaySec <= PunctualityThresholdSec)
                lb.punctualOnTimeToday++;
            else
                lb.punctualLateToday++;
        }

        // ── API ──────────────────────────────────────────────────────

        /// <summary>
        /// Dodaje przychód (bilet pasażera, fracht etc.). GameState.Money++ na żywo.
        /// </summary>
        public void AddRevenue(int circulationId, long amountGroszy, string source)
        {
            if (amountGroszy <= 0) return;

            MoneyLedger.AddRevenue(amountGroszy); // ruch Money + suma dzienna

            if (circulationId >= 0)
            {
                if (!_lineBalances.TryGetValue(circulationId, out var lb))
                {
                    lb = new LineBalance { circulationId = circulationId };
                    _lineBalances[circulationId] = lb;
                }
                lb.revenueGroszy += amountGroszy;
                lb.passengerCount++;
            }
        }

        /// <summary>Dodaje koszt. Ruch Money + difficulty mult (kategorie operacyjne) + suma dzienna
        /// delegowane do MoneyLedger (asmdef Economy); dokleja per-line attribution FAKTYCZNIE pobranej
        /// kwoty (po mnożniku). Whitelist kategorii operacyjnych + mnożnik żyją teraz w MoneyLedger.</summary>
        public void AddCost(int circulationId, long amountGroszy, string category, string source)
        {
            if (amountGroszy <= 0) return;
            long actual = MoneyLedger.AddCost(amountGroszy, category);
            if (actual <= 0) return;

            if (circulationId >= 0 && _lineBalances.TryGetValue(circulationId, out var lb))
                lb.costsGroszy += actual;
        }

        /// <summary>Dotacja wojewódzka (M6-6).</summary>
        public void AddSubsidy(int circulationId, long amountGroszy, string source)
        {
            if (amountGroszy <= 0) return;
            MoneyLedger.AddSubsidy(amountGroszy);

            if (circulationId >= 0 && _lineBalances.TryGetValue(circulationId, out var lb))
                lb.subsidiesGroszy += amountGroszy;
        }

        /// <summary>
        /// Archiwizuje running do <see cref="History"/> + reset. Wywoływane przy przejściu
        /// na kolejny GameDay.
        /// </summary>
        public void OnDayEnded(string dateIso)
        {
            // M6-4: daily overhead — placeholder do M8 realne pensje
            AddCost(-1, CostCalculator.DailyOverheadGroszy, "overhead", "daily");

            // M6-6: dotacje wojewódzkie dla kwalifikujących się obiegów
            // (iter kopia — AddSubsidy modifikuje _lineBalances w trakcie)
            var linesSnapshot = new List<LineBalance>(_lineBalances.Values);
            foreach (var lb in linesSnapshot)
            {
                int subsidyGroszy = SubsidyCalculator.CalculateDailySubsidy(lb);
                if (subsidyGroszy > 0)
                    AddSubsidy(lb.circulationId, subsidyGroszy, "regional_daily");
            }

            var daily = new DailyBalance
            {
                dateIso = dateIso,
                revenueGroszy = RevenueTodayGroszy,
                costsGroszy = CostsTodayGroszy,
                subsidiesGroszy = SubsidiesTodayGroszy,
                perLine = new Dictionary<int, LineBalance>(_lineBalances),
            };
            _history.Add(daily);
            // BUG-040: trim do HistoryMax (FIFO) — usuwamy najstarsze dni
            while (_history.Count > HistoryMax) _history.RemoveAt(0);

            Log.Info($"[EconomyManager] Day {dateIso} ended: " +
                     $"revenue={daily.revenueGroszy / 100f:F0}zł, " +
                     $"costs={daily.costsGroszy / 100f:F0}zł, " +
                     $"subsidies={daily.subsidiesGroszy / 100f:F0}zł, " +
                     $"net={daily.NetGroszy / 100f:F0}zł");

            MoneyLedger.ResetDayTotals();
            _lineBalances.Clear();
        }

        // ── Save/Load API ────────────────────────────────────────────

        /// <summary>
        /// Restore'uje stan EconomyManager z save'a. Wywoływane przez `EconomySavable.Deserialize`.
        /// Zastępuje wcześniejszy reflection-based dostęp do private fieldów (`_lineBalances`,
        /// `_history`, `<RevenueTodayGroszy>k__BackingField` etc.). Public API zamiast
        /// reflection żeby rename pola był łapany w compile, nie silently no-op.
        ///
        /// Wymaga <see cref="EnsureExists"/> przed wywołaniem.
        /// </summary>
        public void RestoreFromSave(long revenueToday, long costsToday, long subsidiesToday,
                                    IEnumerable<LineBalance> lineBalances,
                                    IEnumerable<DailyBalance> history)
        {
            MoneyLedger.RestoreTotals(revenueToday, costsToday, subsidiesToday);

            _lineBalances.Clear();
            if (lineBalances != null)
            {
                foreach (var lb in lineBalances)
                    if (lb != null) _lineBalances[lb.circulationId] = lb;
            }

            _history.Clear();
            if (history != null)
            {
                foreach (var db in history)
                    if (db != null) _history.Add(db);
            }

            // Akumulator sub-zł wyczyszczony w MoneyLedger.RestoreTotals (resztki <1zł nie persistowane).
        }

        /// <summary>Reset runtime state (jak nowa gra). Wywoływane przez `EconomySavable.InitializeDefault`.</summary>
        public void ResetRuntime()
        {
            MoneyLedger.ResetAll();
            _lineBalances.Clear();
            _history.Clear();
        }

        // ── Debug ────────────────────────────────────────────────────

        [ContextMenu("Debug: Dump today balance")]
        public void DebugDumpToday()
        {
            Log.Info($"[EconomyManager] Today: rev={RevenueTodayGroszy / 100f:F0}zł " +
                     $"cost={CostsTodayGroszy / 100f:F0}zł " +
                     $"sub={SubsidiesTodayGroszy / 100f:F0}zł " +
                     $"NET={NetTodayGroszy / 100f:F0}zł. " +
                     $"Money={GameState.Money}zł");
            foreach (var kvp in _lineBalances)
            {
                var lb = kvp.Value;
                Log.Info($"  Line#{lb.circulationId}: rev={lb.revenueGroszy / 100f:F0}zł " +
                         $"cost={lb.costsGroszy / 100f:F0}zł " +
                         $"pax={lb.passengerCount}");
            }
        }

        [ContextMenu("Debug: Dump history")]
        public void DebugDumpHistory()
        {
            Log.Info($"[EconomyManager] History: {_history.Count} days");
            int show = Mathf.Min(_history.Count, 7);
            for (int i = _history.Count - show; i < _history.Count; i++)
            {
                var d = _history[i];
                Log.Info($"  {d.dateIso}: rev={d.revenueGroszy / 100f:F0}zł " +
                         $"cost={d.costsGroszy / 100f:F0}zł " +
                         $"sub={d.subsidiesGroszy / 100f:F0}zł " +
                         $"net={d.NetGroszy / 100f:F0}zł");
            }
        }
    }

    // ── DTOs ─────────────────────────────────────────────────────────

    [Serializable]
    public class LineBalance
    {
        public int circulationId;
        public long revenueGroszy;
        public long costsGroszy;
        public long subsidiesGroszy;
        public int passengerCount;
        public int runsCompletedToday;

        // TD-007 (M-Balance 2026-05-06): punctuality tracking dla dotacji wojewódzkich.
        // Threshold ±5 min (300s) z TrainRun.currentDelaySec przy despawn.
        public int punctualOnTimeToday;  // delay ≤ 300s
        public int punctualLateToday;    // delay > 300s

        public long NetGroszy => revenueGroszy - costsGroszy + subsidiesGroszy;

        /// <summary>
        /// TD-007: punctuality ratio [0..1] — % kursów on-time. Zwraca 1.0 gdy brak danych
        /// (nie penalizujemy nowych obiegów bez historii).
        /// </summary>
        public float PunctualityRatio
        {
            get
            {
                int total = punctualOnTimeToday + punctualLateToday;
                if (total <= 0) return 1f;
                return (float)punctualOnTimeToday / total;
            }
        }
    }

    [Serializable]
    public class DailyBalance
    {
        public string dateIso;
        public long revenueGroszy;
        public long costsGroszy;
        public long subsidiesGroszy;
        public Dictionary<int, LineBalance> perLine;

        public long NetGroszy => revenueGroszy - costsGroszy + subsidiesGroszy;
    }
}
