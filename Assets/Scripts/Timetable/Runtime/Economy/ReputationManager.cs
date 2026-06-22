using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// Typ zdarzenia wpływającego na reputację. Deltas w <see cref="ReputationManager"/>.
    /// </summary>
    public enum ReputationEventType
    {
        DelayMinor,           // opóźnienie 1-5 min
        DelayMedium,          // opóźnienie 5-15 min
        DelayMajor,           // opóźnienie >15 min
        CanceledRun,          // odwołanie kursu
        BreakdownOnRoute,     // niesprawny skład w trasie
        Accident,             // wypadek (kolizja/wykolejenie)
        PunctualDay,          // dzień bez opóźnień
        VehicleUpgrade,       // zamiana old→new pojazdu na linii
    }

    /// <summary>
    /// M6-5: Multi-level reputation (D7).
    ///
    /// - Globalna: jeden int 0-100, start 50
    /// - Per-województwo: Dictionary string → int
    /// - Per-stacja: Dictionary int (stationId) → int
    ///
    /// Brak decay. Zmieniana tylko przez ApplyEvent().
    ///
    /// Używana przez <see cref="DemandModifiers"/> — stacje z niższą reputacją
    /// przyciągają mniej pasażerów. Wpływ obliczany w PassengerManager.
    /// </summary>
    public class ReputationManager : MonoBehaviour
    {
        public static ReputationManager Instance { get; private set; }

        // Deltas per event (do BalanceConstants / M6.5 Rebalance)
        public const int DeltaDelayMinor = -1;
        public const int DeltaDelayMedium = -2;
        public const int DeltaDelayMajor = -5;
        public const int DeltaCanceledRun = -10;
        public const int DeltaBreakdownOnRoute = -15;
        public const int DeltaAccident = -30;
        public const int DeltaPunctualDay = +1;
        public const int DeltaVehicleUpgrade = +3;

        public const int RepStart = 50;
        public const int RepMin = 0;
        public const int RepMax = 100;

        int _global = RepStart;
        readonly Dictionary<string, int> _byVoivodeship = new();
        readonly Dictionary<int, int> _byStation = new();

        public int Global => _global;
        public IReadOnlyDictionary<string, int> ByVoivodeship => _byVoivodeship;
        public IReadOnlyDictionary<int, int> ByStation => _byStation;

        public event Action<ReputationEventType, int /*delta*/, string /*reason*/> OnReputationChanged;

        // ── Bootstrap ────────────────────────────────────────────────

        public static ReputationManager EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ReputationManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ReputationManager>();
            Log.Info("[ReputationManager] Bootstrapped");
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // MM-10: hook na completion modernizacji (Fleet.asmdef nie referuje Timetable,
            // hook-based DI). Bonus reputation = VehicleUpgrade event z opisem ścieżki.
            RailwayManager.Fleet.ModernizationJobService.OnModernizationCompletedReputationHook
                = (vehicleId, pathDisplayName) =>
                {
                    Instance?.ApplyEvent(ReputationEventType.VehicleUpgrade,
                                         null, null,
                                         $"Modernizacja {pathDisplayName} vehicle#{vehicleId}");
                };
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                // MM-10: uninstall hook
                RailwayManager.Fleet.ModernizationJobService.OnModernizationCompletedReputationHook = null;
            }
        }

        // ── API ──────────────────────────────────────────────────────

        /// <summary>
        /// Aplikuje zmianę reputacji w wyniku zdarzenia.
        /// </summary>
        /// <param name="type">Typ zdarzenia — determinuje deltę</param>
        /// <param name="affectedStationIds">Stacje dotknięte (route, origin+dest, itd). Mogą być puste — wtedy tylko global.</param>
        /// <param name="affectedVoivodeships">Województwa dotknięte (route crossing). Mogą być puste.</param>
        /// <param name="reason">Tekst do log/UI (opcjonalnie).</param>
        public void ApplyEvent(ReputationEventType type,
                               IReadOnlyList<int> affectedStationIds,
                               IReadOnlyList<string> affectedVoivodeships,
                               string reason = null)
        {
            int delta = GetDelta(type);
            if (delta == 0) return;

            _global = Mathf.Clamp(_global + delta, RepMin, RepMax);
            GameState.GlobalReputation = _global; // mirror do Core dla TopBarUI

            // BUG-056 fix: kolejność `TryGetValue` → potem `ContainsKey` — przy rep już
            // sprowadzonej do 0 (po wielu Accident events) `cur == 0` było prawdziwe ALE
            // klucz był obecny. Lex pattern interpretował to jako "first-time" → reset
            // do RepStart. Teraz check `ContainsKey` najpierw — gdy klucz brak, init.
            if (affectedVoivodeships != null)
            {
                foreach (var w in affectedVoivodeships)
                {
                    if (string.IsNullOrEmpty(w)) continue;
                    int cur = _byVoivodeship.TryGetValue(w, out int v) ? v : RepStart;
                    _byVoivodeship[w] = Mathf.Clamp(cur + delta, RepMin, RepMax);
                }
            }

            if (affectedStationIds != null)
            {
                foreach (int sid in affectedStationIds)
                {
                    if (sid < 0) continue;
                    int cur = _byStation.TryGetValue(sid, out int v) ? v : RepStart;
                    _byStation[sid] = Mathf.Clamp(cur + delta, RepMin, RepMax);
                }
            }

            Log.Info($"[Reputation] {type} ({delta:+#;-#;0}): global={_global}, " +
                     $"stations={affectedStationIds?.Count ?? 0}, " +
                     $"voivodeships={affectedVoivodeships?.Count ?? 0}" +
                     (reason != null ? $" — {reason}" : ""));

            OnReputationChanged?.Invoke(type, delta, reason);
        }

        /// <summary>
        /// Reputation factor dla pary stacji (used by PassengerManager for demand spawn).
        /// Średnia ważona: global × 0.3 + avg(stations) × 0.7. Wynik 0.5 (rep 0) do 1.5 (rep 100).
        /// </summary>
        public float GetDemandFactor(int fromStationId, int toStationId)
        {
            float fromRep = GetForStation(fromStationId);
            float toRep = GetForStation(toStationId);
            float avgStation = (fromRep + toRep) * 0.5f;
            float blended = _global * 0.3f + avgStation * 0.7f;
            // Mapuj 0→0.5, 50→1.0, 100→1.5
            return 0.5f + (blended / 100f);
        }

        public int GetForStation(int stationId)
        {
            if (_byStation.TryGetValue(stationId, out int v)) return v;
            return RepStart; // domyślnie 50 gdy brak eventów
        }

        public int GetForVoivodeship(string voivodeship)
        {
            if (string.IsNullOrEmpty(voivodeship)) return RepStart;
            if (_byVoivodeship.TryGetValue(voivodeship, out int v)) return v;
            return RepStart;
        }

        static int GetDelta(ReputationEventType type) => type switch
        {
            ReputationEventType.DelayMinor => DeltaDelayMinor,
            ReputationEventType.DelayMedium => DeltaDelayMedium,
            ReputationEventType.DelayMajor => DeltaDelayMajor,
            ReputationEventType.CanceledRun => DeltaCanceledRun,
            ReputationEventType.BreakdownOnRoute => DeltaBreakdownOnRoute,
            ReputationEventType.Accident => DeltaAccident,
            ReputationEventType.PunctualDay => DeltaPunctualDay,
            ReputationEventType.VehicleUpgrade => DeltaVehicleUpgrade,
            _ => 0
        };

        // ── Save/Load API ────────────────────────────────────────────

        /// <summary>
        /// Restore'uje stan ReputationManager z save'a. Public API zamiast reflection
        /// na private fieldy (`_global`, `_byVoivodeship`, `_byStation`) — rename pola
        /// łapany w compile zamiast silently no-op.
        ///
        /// Mirror do <see cref="GameState.GlobalReputation"/> wykonany dla TopBarUI.
        /// </summary>
        public void RestoreFromSave(int global,
                                    IEnumerable<KeyValuePair<string, int>> byVoivodeship,
                                    IEnumerable<KeyValuePair<int, int>> byStation)
        {
            _global = Mathf.Clamp(global, RepMin, RepMax);
            GameState.GlobalReputation = _global;

            _byVoivodeship.Clear();
            if (byVoivodeship != null)
            {
                foreach (var kv in byVoivodeship)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    _byVoivodeship[kv.Key] = Mathf.Clamp(kv.Value, RepMin, RepMax);
                }
            }

            _byStation.Clear();
            if (byStation != null)
            {
                foreach (var kv in byStation)
                {
                    if (kv.Key < 0) continue;
                    _byStation[kv.Key] = Mathf.Clamp(kv.Value, RepMin, RepMax);
                }
            }
        }

        /// <summary>Reset reputation do start state (50/100 global, brak per-region).</summary>
        public void ResetRuntime()
        {
            _global = RepStart;
            GameState.GlobalReputation = _global;
            _byVoivodeship.Clear();
            _byStation.Clear();
        }

        // ── Debug ────────────────────────────────────────────────────

        [ContextMenu("Debug: Dump reputation")]
        public void DebugDump()
        {
            Log.Info($"[Reputation] Global: {_global}/100");
            if (_byVoivodeship.Count > 0)
            {
                Log.Info("  Województwa:");
                foreach (var kvp in _byVoivodeship)
                    Log.Info($"    {kvp.Key}: {kvp.Value}/100");
            }
            if (_byStation.Count > 0)
            {
                Log.Info($"  Stacje (z non-default reputation): {_byStation.Count}");
                int shown = 0;
                foreach (var kvp in _byStation)
                {
                    if (shown++ >= 10) break;
                    Log.Info($"    station#{kvp.Key}: {kvp.Value}/100");
                }
                if (_byStation.Count > 10) Log.Info($"    ...i {_byStation.Count - 10} więcej");
            }
        }

        [ContextMenu("Debug: Apply test delay major event")]
        public void DebugApplyTestDelay()
        {
            ApplyEvent(ReputationEventType.DelayMajor,
                new List<int>() /*no specific stations*/,
                new List<string>() /*no voivodeships*/,
                "Debug test");
        }
    }
}
