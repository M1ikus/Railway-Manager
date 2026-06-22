using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M6.5-6: Loader dla <c>subsidy_rules.json</c>. Pattern analogiczny do FleetCatalog.
    /// Lazy load przy pierwszym użyciu, statyczny cache.
    ///
    /// 🌍 ARCHITEKTURA PER-KRAJ (DLC ready, post-EA):
    ///   - W EA: tylko PL (16 województw). Plik <c>subsidy_rules.json</c>.
    ///   - W DLC: per-kraj pliki (<c>subsidy_rules_PL.json</c>, <c>subsidy_rules_DE.json</c>) +
    ///     resolver wg kraju depotu/circulation. Implementacja post-EA.
    /// </summary>
    public static class SubsidyRulesCatalog
    {
        private static SubsidyRulesData _data;
        private static Dictionary<string, SubsidyRule> _byCode;

        /// <summary>Globalne warunki kwalifikacji (uniform dla wszystkich województw).</summary>
        public static SubsidyGlobalDefaults Defaults => _data?.globalDefaults ?? new SubsidyGlobalDefaults();

        /// <summary>Wszystkie województwa (16 PL).</summary>
        public static IReadOnlyList<SubsidyRule> Voivodeships => _data?.voivodeships ?? new List<SubsidyRule>();

        /// <summary>Pobierz regułę per kod województwa (np. "MZ", "MA"). Null jeśli brak / catalog nie załadowany.</summary>
        public static SubsidyRule GetByVoivodeshipCode(string code)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(code)) return null;
            return _byCode != null && _byCode.TryGetValue(code, out var rule) ? rule : null;
        }

        /// <summary>Pobierz stawkę per-run dla województwa. Fallback 0 jeśli brak.</summary>
        public static int GetSubsidyPerRunGroszy(string code)
        {
            var rule = GetByVoivodeshipCode(code);
            return rule?.subsidyPerRunGroszy ?? 0;
        }

        /// <summary>Załaduj catalog z StreamingAssets/Economy/. Idempotent — wielokrotne wywołanie OK.</summary>
        public static void EnsureLoaded()
        {
            if (_data != null) return;

            string filePath = Path.Combine(AppPaths.EconomyCatalogDir, "subsidy_rules.json");

            if (!File.Exists(filePath))
            {
                Log.Warn($"[SubsidyRulesCatalog] subsidy_rules.json not found at {filePath} — using empty catalog");
                _data = new SubsidyRulesData
                {
                    globalDefaults = new SubsidyGlobalDefaults(),
                    voivodeships = new List<SubsidyRule>()
                };
                _byCode = new Dictionary<string, SubsidyRule>();
                return;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                _data = JsonUtility.FromJson<SubsidyRulesData>(json);
                if (_data == null) _data = new SubsidyRulesData();
                if (_data.globalDefaults == null) _data.globalDefaults = new SubsidyGlobalDefaults();
                if (_data.voivodeships == null) _data.voivodeships = new List<SubsidyRule>();

                _byCode = new Dictionary<string, SubsidyRule>();
                foreach (var v in _data.voivodeships)
                    if (!string.IsNullOrEmpty(v.code))
                        _byCode[v.code] = v;

                Log.Info($"[SubsidyRulesCatalog] Loaded {_data.voivodeships.Count} województw rules.");
            }
            catch (System.Exception e)
            {
                Log.Warn($"[SubsidyRulesCatalog] Failed to parse subsidy_rules.json: {e.Message}");
                _data = new SubsidyRulesData
                {
                    globalDefaults = new SubsidyGlobalDefaults(),
                    voivodeships = new List<SubsidyRule>()
                };
                _byCode = new Dictionary<string, SubsidyRule>();
            }
        }

        /// <summary>Reset cache (dla testów / hot reload).</summary>
        public static void Reset()
        {
            _data = null;
            _byCode = null;
        }
    }
}
