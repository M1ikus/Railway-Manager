using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RailwayManager.Core
{
    /// <summary>
    /// MP-9 (M-Performance milestone): centralny rejestr deterministycznych RNG per-system.
    ///
    /// Każdy system gameplay'owy używający randomness powinien dostać własny <see cref="System.Random"/>
    /// instance z seedem pochodnym od <see cref="GameState.Seed"/>. Substream'y zapewniają że
    /// systemy są niezależne (zmiana RNG state w PassengerManager NIE wpływa na BreakdownService).
    ///
    /// <para><b>Use case:</b></para>
    /// <code>
    /// var rng = RandomRegistry.GetRng("BreakdownService");
    /// if (rng.NextDouble() &lt; failureChance) { ... }
    /// </code>
    ///
    /// <para><b>Kontrakt:</b></para>
    /// - Identyczny <see cref="GameState.Seed"/> + identyczny systemId = identyczna sekwencja RNG
    /// - Reset całego registry w <see cref="ResetAll"/> przy load save'a (żeby seed odpowiadał save'owemu)
    /// - **NIE używać** w UI/visual code — to deterministic gameplay RNG. UI flavor losowość → `UnityEngine.Random` OK
    ///
    /// <para><b>Wybór backend'u:</b></para>
    /// - Mały xorshift32 w <see cref="DeterministicRng"/> zamiast <see cref="System.Random"/>.
    ///   Powód: stan RNG musi być jawny i persystowalny w save/load, a <c>System.Random</c>
    ///   nie daje stabilnego snapshotu.
    /// - Adapter pattern w <see cref="DeterministicRng"/> daje fluent API kompatybilne
    ///   z <c>UnityEngine.Random.Range/value</c> żeby ułatwić migrację.
    /// </summary>
    public static class RandomRegistry
    {
        static readonly Dictionary<string, DeterministicRng> _registry = new();
        static readonly Dictionary<string, RngSnapshot> _pendingSnapshots = new();

        /// <summary>
        /// Pobiera (lub tworzy) deterministic RNG dla danego systemu. Seed = <see cref="GameState.Seed"/>
        /// XOR stabilny hash(systemId) — niezależne substream'y.
        /// </summary>
        public static DeterministicRng GetRng(string systemId)
        {
            if (string.IsNullOrEmpty(systemId))
                throw new ArgumentException("systemId required", nameof(systemId));

            if (_registry.TryGetValue(systemId, out var existing))
                return existing;

            var rng = new DeterministicRng(ComputeSeed(systemId));
            if (_pendingSnapshots.TryGetValue(systemId, out var pending))
            {
                rng.RestoreState(pending.Seed, pending.State, pending.DrawCount);
                _pendingSnapshots.Remove(systemId);
            }
            _registry[systemId] = rng;
            return rng;
        }

        /// <summary>
        /// MP-9: reset całego registry — wywoływać przy nowej grze (po ustawieniu <see cref="GameState.Seed"/>),
        /// żeby per-system RNG zaczęły od początku z nowym seed. Istniejące referencje są reseedowane
        /// in-place, bo wiele serwisów trzyma je w static readonly fieldach.
        /// </summary>
        public static void ResetAll()
        {
            _pendingSnapshots.Clear();
            foreach (var kv in _registry)
                kv.Value.Reset(ComputeSeed(kv.Key));
        }

        /// <summary>Snapshot stanów RNG do save/load. Zawiera też pending states dla systemów lazy.</summary>
        public static JObject ToJson()
        {
            var root = new JObject();
            foreach (var kv in _registry)
                root[kv.Key] = kv.Value.ToJson();
            foreach (var kv in _pendingSnapshots)
                if (!root.ContainsKey(kv.Key))
                    root[kv.Key] = kv.Value.ToJson();
            return root;
        }

        /// <summary>
        /// Restore stanów RNG po load. Istniejące referencje są aktualizowane in-place,
        /// a systemy jeszcze niezainicjalizowane dostaną swój snapshot przy <see cref="GetRng"/>.
        /// Brak snapshotu (stary save) = reset do seed-derived initial state.
        /// </summary>
        public static void ApplyFromJson(JObject json)
        {
            _pendingSnapshots.Clear();

            if (json == null)
            {
                ResetAll();
                return;
            }

            var restored = new HashSet<string>();
            foreach (var prop in json.Properties())
            {
                var snap = RngSnapshot.FromJson(prop.Value as JObject, ComputeSeed(prop.Name));
                restored.Add(prop.Name);

                if (_registry.TryGetValue(prop.Name, out var existing))
                {
                    existing.RestoreState(snap.Seed, snap.State, snap.DrawCount);
                }
                else
                {
                    _pendingSnapshots[prop.Name] = snap;
                }
            }

            // Newer code may introduce RNG users absent from older saves. Reset those to
            // seed-derived initial state so stale pre-load references cannot leak through.
            foreach (var kv in _registry)
            {
                if (!restored.Contains(kv.Key))
                    kv.Value.Reset(ComputeSeed(kv.Key));
            }
        }

        /// <summary>Diagnostyka — lista zarejestrowanych systemów (do debug).</summary>
        public static IEnumerable<string> RegisteredSystems => _registry.Keys;

        static int ComputeSeed(string systemId) => unchecked(GameState.Seed ^ StableHash32(systemId));

        static int StableHash32(string text)
        {
            unchecked
            {
                uint hash = 2166136261u; // FNV-1a
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619u;
                }
                return (int)hash;
            }
        }

        struct RngSnapshot
        {
            public int Seed;
            public uint State;
            public long DrawCount;

            public JObject ToJson() => new()
            {
                ["seed"] = Seed,
                ["state"] = State,
                ["drawCount"] = DrawCount
            };

            public static RngSnapshot FromJson(JObject json, int fallbackSeed)
            {
                uint fallbackState = DeterministicRng.InitialStateForSeed(fallbackSeed);
                return new RngSnapshot
                {
                    Seed = json?.Value<int?>("seed") ?? fallbackSeed,
                    State = json?.Value<uint?>("state") ?? fallbackState,
                    DrawCount = json?.Value<long?>("drawCount") ?? 0L
                };
            }
        }
    }

    /// <summary>
    /// MP-9: lekki, jawnie persystowalny RNG z API kompatybilnym z <c>UnityEngine.Random</c>
    /// żeby ułatwić migrację (Range/value semantics).
    ///
    /// <para>NIE thread-safe. Single-threaded use w main Unity thread.</para>
    /// </summary>
    public sealed class DeterministicRng
    {
        int _seed;
        uint _state;
        long _drawCount;

        public DeterministicRng(int seed)
        {
            Reset(seed);
        }

        /// <summary>Float [min, max). Kompatybilne z <c>UnityEngine.Random.Range(float, float)</c>.</summary>
        public float Range(float minInclusive, float maxExclusive)
        {
            return (float)(NextUnitDouble() * (maxExclusive - minInclusive) + minInclusive);
        }

        /// <summary>
        /// Int [min, max). Kompatybilne z <c>UnityEngine.Random.Range(int, int)</c>.
        ///
        /// Implementacja: rejection sampling z biased modulo elimination. Naiwne
        /// <c>NextUInt() % span</c> ma modulo bias gdy span nie dzieli 2^32 — wartości
        /// bliżej min są nieznacznie częstsze. Przy małych span (&lt;~10k) bias praktycznie
        /// niewykrywalny, ale dla szerokich zakresów (np. ID hash buckets) zauważalny.
        /// Rejection sampling daje uniform distribution kosztem średnio &lt;2 dodatkowych
        /// drawów w worst case.
        /// </summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint span = (uint)(maxExclusive - minInclusive);
            // Threshold: największa wielokrotność `span` mieszcząca się w uint range.
            // Wartości >= threshold odrzucamy i losujemy ponownie — to gwarantuje uniform.
            uint threshold = uint.MaxValue - (uint.MaxValue % span);
            uint sample;
            do { sample = NextUInt(); } while (sample >= threshold);
            return minInclusive + (int)(sample % span);
        }

        /// <summary>Float [0, 1). Kompatybilne z <c>UnityEngine.Random.value</c>.</summary>
        public float Value => (float)NextUnitDouble();

        /// <summary>Double [0, 1). Native System.Random API.</summary>
        public double NextDouble() => NextUnitDouble();

        /// <summary>Int dowolny non-negative.</summary>
        public int NextInt() => (int)(NextUInt() & 0x7FFFFFFFu);

        public void Reset(int seed)
        {
            _seed = seed;
            _state = InitialStateForSeed(seed);
            _drawCount = 0L;
        }

        public void RestoreState(int seed, uint state, long drawCount)
        {
            _seed = seed;
            _state = state != 0u ? state : InitialStateForSeed(seed);
            _drawCount = Math.Max(0L, drawCount);
        }

        public JObject ToJson() => new()
        {
            ["seed"] = _seed,
            ["state"] = _state,
            ["drawCount"] = _drawCount
        };

        internal static uint InitialStateForSeed(int seed)
        {
            unchecked
            {
                uint x = (uint)seed;
                x ^= 0x9E3779B9u;
                x *= 0x85EBCA6Bu;
                x ^= x >> 13;
                x *= 0xC2B2AE35u;
                x ^= x >> 16;
                return x != 0u ? x : 0x6D2B79F5u;
            }
        }

        uint NextUInt()
        {
            unchecked
            {
                uint x = _state != 0u ? _state : 0x6D2B79F5u;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                _state = x != 0u ? x : 0x6D2B79F5u;
                _drawCount++;
                return _state;
            }
        }

        double NextUnitDouble() => NextUInt() / ((double)uint.MaxValue + 1.0);
    }
}
