using System;
using System.Collections.Generic;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-15 / §3.7: Globalny state odblokowanych bonusow z ukonczonych <see cref="ResearchPath"/>.
    ///
    /// Mapping effectKey → effectValue (cumulative jesli wiele sciezek tego samego typu,
    /// ale w M8-15 EA kazda sciezka unique).
    ///
    /// Hook dla innych systemow:
    /// - M7 WorkshopManager — "MaintenanceTimeReduction" (-10% czasu P3-P5)
    /// - M9a TrainRunSimulator — "TractionEnergyReduction" (-5% kosztu paliwa)
    /// - M6 Economy — "PassengerRevenueBonus" (post-EA)
    /// </summary>
    [Serializable]
    public class ResearchUnlocks
    {
        public static ResearchUnlocks Global { get; } = new();

        readonly Dictionary<string, float> _effects = new();

        public static event Action<string, float> OnUnlockChanged;

        /// <summary>Pobierz wartosc efektu, 0 jesli nie odblokowany.</summary>
        public float Get(string effectKey)
        {
            return _effects.TryGetValue(effectKey, out var v) ? v : 0f;
        }

        public bool IsUnlocked(string effectKey) => _effects.ContainsKey(effectKey);

        public IReadOnlyDictionary<string, float> AllEffects => _effects;

        public Dictionary<string, float> GetSnapshot() => new(_effects);

        /// <summary>
        /// Ustaw wartosc efektu. Jesli juz obecny, nadpisuje (post-EA moze byc sumowanie).
        /// </summary>
        public void Apply(string effectKey, float value)
        {
            _effects[effectKey] = value;
            OnUnlockChanged?.Invoke(effectKey, value);
        }

        public void Reset()
        {
            _effects.Clear();
            OnUnlockChanged?.Invoke(null, 0f);
        }

        public void RestoreFromSave(IDictionary<string, float> effects)
        {
            _effects.Clear();
            if (effects != null)
            {
                foreach (var kv in effects)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    _effects[kv.Key] = kv.Value;
                }
            }
            OnUnlockChanged?.Invoke(null, 0f);
        }
    }
}
