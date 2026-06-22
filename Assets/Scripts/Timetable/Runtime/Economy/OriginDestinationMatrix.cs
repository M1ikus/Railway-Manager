using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M6-1: Macierz popytu bazowego między parami stacji.
    ///
    /// Obliczana jednorazowo przy starcie gry z <see cref="StationImportance"/>
    /// i dystansu. Plus override'y z JSON file (strategiczne relacje tuningowane
    /// manualnie — np. Warszawa↔Kraków Pendolino effect wyższy niż sama geografia
    /// sugeruje).
    ///
    /// Formula: <c>baseDemand(from, to) = imp(from) × imp(to) × distFactor(km) / K</c>
    /// gdzie distFactor spada dla bardzo krótkich (commute walk zamiast pociągu)
    /// i bardzo długich tras (ludzie wybierają samolot).
    ///
    /// Target: zwracane wartości to "pasażerów dziennie BASE" — dalej modyfikowane
    /// przez DemandModifiers (time/price/reputation/...) w PassengerManager.
    /// </summary>
    public class OriginDestinationMatrix
    {
        /// <summary>Key: hash(min(a,b), max(a,b)) — OD symetryczne (A→B tak samo częste jak B→A).</summary>
        readonly Dictionary<long, float> _baseDemand = new();

        readonly Dictionary<long, float> _overrides = new();

        const float DistancePeakKm = 80f;      // pik popytu ~80km (podmiejskie + regionalne)
        const float DistanceSigma = 150f;      // jak szybko demand spada dla dłuższych tras
        const float CalibrationK = 3.0f;       // normalizacja żeby wartości były sensowne

        // ── TD-016 (M-Performance follow-up 2026-05-06): spatial grid + early reject ──
        /// <summary>Cell size dla spatial grid. 100km = ~PL ma ~6×9 cells, DLC EU ~20×15 cells.</summary>
        const float CellSizeM = 100_000f;
        /// <summary>Cutoff distance — par dalszych niż to nie ma sensownego demand'u (NA PEWNO &lt;0.1 nawet
        /// dla największych importance). Bezpieczna granica wyznaczona z math: max imp × imp × Gaussian
        /// musi dać &lt;0.1, dla imp=30 (max szacowane) cutoff ~680km, używamy 800km z marginesem.</summary>
        const float CutoffM = 800_000f;
        const float CutoffMSqr = CutoffM * CutoffM;
        /// <summary>Cells radius — ceil(CutoffM / CellSizeM). Dla 800km / 100km = 8 cells.</summary>
        const int CutoffCells = 8;

        /// <summary>
        /// Buduje macierz z listy stacji + ich importance factor.
        /// Tylko pary ze stacji z pathNodeId >= 0 (reachable w graph).
        ///
        /// TD-016 fix 2026-05-06: spatial grid bin'ing + SqrMagnitude pre-reject + pre-filter pass.
        /// Eliminuje 10s startup freeze na pełnej Polsce (4.3M par brute-force).
        /// Wzorzec analogiczny do M-PL fix'u FindTracksForStop / FindNodeOnTrack.
        ///
        /// Semantic preservation: bezpieczny cutoff 800km — pary >800km mają demand &lt;0.1 nawet
        /// dla największego importance × importance, więc i tak były odrzucone w starym kodzie.
        /// </summary>
        public void Build(
            IReadOnlyList<RailwayStation> stations,
            IReadOnlyDictionary<int, float> importance)
        {
            _baseDemand.Clear();
            if (stations == null || importance == null) return;

            float t0 = Time.realtimeSinceStartup;

            // ── Step 1: pre-filter pass O(N) ──
            // Eliminuje wewnętrzne TryGetValue(importance) + pathNodeId<0 checks (były ~9M razy w starym).
            int n = stations.Count;
            var valid = new List<ValidStation>(n);
            for (int k = 0; k < n; k++)
            {
                var s = stations[k];
                if (s.pathNodeId < 0) continue;
                if (!importance.TryGetValue(s.stationId, out float imp) || imp <= 0f) continue;
                valid.Add(new ValidStation { stationId = s.stationId, position = s.position, importance = imp });
            }

            // ── Step 2: spatial grid bin'ing ──
            // Każda cell trzyma listę indeksów valid stations. Lookup neighbor cells eliminuje
            // pary z cells > cutoff radius (~80km cutoff ÷ 100km cell = 8 cells).
            var grid = new Dictionary<long, List<int>>();
            for (int k = 0; k < valid.Count; k++)
            {
                Vector2 pos = valid[k].position;
                int cx = Mathf.FloorToInt(pos.x / CellSizeM);
                int cy = Mathf.FloorToInt(pos.y / CellSizeM);
                long cellKey = ((long)cx << 32) | (uint)cy;
                if (!grid.TryGetValue(cellKey, out var list))
                {
                    list = new List<int>();
                    grid[cellKey] = list;
                }
                list.Add(k);
            }

            // ── Step 3: iter pairs cell × neighbor cells (forward-only żeby uniknąć duplikatów) ──
            int pairs = 0;
            int processedPairs = 0; // pre-filter na cutoff, potem demand check

            foreach (var entry in grid)
            {
                long cellKey = entry.Key;
                int cx = (int)(cellKey >> 32);
                int cy = (int)(uint)(cellKey & 0xFFFFFFFF);
                var list = entry.Value;

                // 3a. Pary wewnątrz tej samej cell (a < b — unikalne pary)
                for (int a = 0; a < list.Count; a++)
                {
                    int idxA = list[a];
                    for (int b = a + 1; b < list.Count; b++)
                    {
                        if (TryAddPair(valid, idxA, list[b])) pairs++;
                        processedPairs++;
                    }
                }

                // 3b. Pary z neighbor cells — tylko forward (dx > 0 || dx == 0 && dy > 0)
                // żeby każda para cell-A × cell-B była zaprocesowana DOKŁADNIE RAZ.
                for (int dy = -CutoffCells; dy <= CutoffCells; dy++)
                {
                    for (int dx = -CutoffCells; dx <= CutoffCells; dx++)
                    {
                        if (dx < 0) continue;
                        if (dx == 0 && dy <= 0) continue; // skip self + backward

                        int ncx = cx + dx;
                        int ncy = cy + dy;
                        long nKey = ((long)ncx << 32) | (uint)ncy;
                        if (!grid.TryGetValue(nKey, out var nlist)) continue;

                        for (int a = 0; a < list.Count; a++)
                        {
                            int idxA = list[a];
                            for (int b = 0; b < nlist.Count; b++)
                            {
                                if (TryAddPair(valid, idxA, nlist[b])) pairs++;
                                processedPairs++;
                            }
                        }
                    }
                }
            }

            float elapsed = Time.realtimeSinceStartup - t0;
            Log.Info($"[ODMatrix] Built {pairs} station pairs from {valid.Count}/{n} valid stations " +
                     $"(processed {processedPairs} pairs in spatial grid vs {(long)valid.Count * (valid.Count - 1) / 2L} brute force, " +
                     $"~{(processedPairs * 100.0 / Mathf.Max(1L, (long)valid.Count * (valid.Count - 1) / 2L)):F0}%), " +
                     $"avg demand={(pairs > 0 ? TotalDemand() / pairs : 0f):F1}, elapsed {elapsed:F2}s");
        }

        /// <summary>
        /// TD-016: SqrMagnitude pre-reject (cutoff 800km) + Gaussian + demand check.
        /// Returns true gdy para została dodana do _baseDemand.
        /// </summary>
        bool TryAddPair(List<ValidStation> valid, int idxA, int idxB)
        {
            var a = valid[idxA];
            var b = valid[idxB];

            // SqrMagnitude pre-reject — eliminuje sqrt dla par > cutoff
            Vector2 delta = a.position - b.position;
            float distSqrM = delta.x * delta.x + delta.y * delta.y;
            if (distSqrM > CutoffMSqr) return false;

            float distKm = Mathf.Sqrt(distSqrM) / 1000f;
            if (distKm < 1f) return false; // zbyt blisko

            float distFactor = GaussianDistanceFactor(distKm);
            float demand = (a.importance * b.importance * distFactor) / CalibrationK;
            if (demand < 0.1f) return false;

            _baseDemand[Key(a.stationId, b.stationId)] = demand;
            return true;
        }

        struct ValidStation
        {
            public int stationId;
            public Vector2 position;
            public float importance;
        }

        /// <summary>
        /// Ładuje override'y z pliku JSON (strategiczne relacje tuningowane ręcznie).
        /// Format: `[{ "from": stationId, "to": stationId, "demand": 8000 }, ...]`
        /// </summary>
        public void LoadOverrides(string streamingAssetsRelPath = "Economy/demand_overrides.json")
        {
            _overrides.Clear();
            string fullPath = Path.Combine(AppPaths.StreamingRoot, streamingAssetsRelPath);
            if (!File.Exists(fullPath))
            {
                Log.Info($"[ODMatrix] Brak override file ({streamingAssetsRelPath}) — pomijam");
                return;
            }

            try
            {
                string json = File.ReadAllText(fullPath);
                var parsed = JsonUtility.FromJson<OverrideList>(json);
                if (parsed?.entries != null)
                {
                    foreach (var e in parsed.entries)
                        _overrides[Key(e.from, e.to)] = e.demand;
                    Log.Info($"[ODMatrix] Załadowano {parsed.entries.Length} overrides z {streamingAssetsRelPath}");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn($"[ODMatrix] Błąd parsowania {streamingAssetsRelPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Zwraca base demand dla pary stacji. Override wygrywa nad gravity.
        /// </summary>
        public float GetBaseDemand(int fromStationId, int toStationId)
        {
            long k = Key(fromStationId, toStationId);
            if (_overrides.TryGetValue(k, out float ov)) return ov;
            if (_baseDemand.TryGetValue(k, out float gravity)) return gravity;
            return 0f;
        }

        /// <summary>Liczba niezerowych par w macierzy.</summary>
        public int PairCount => _baseDemand.Count;

        public float TotalDemand()
        {
            float sum = 0f;
            foreach (var v in _baseDemand.Values) sum += v;
            return sum;
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Dystans factor — Gauss-like krzywa z peak'em przy DistancePeakKm.
        /// - 1km: mały (ludzie chodzą)
        /// - 80km: max (commute + regionalne)
        /// - 400km: mały (samolot, auto)
        /// </summary>
        static float GaussianDistanceFactor(float km)
        {
            float delta = km - DistancePeakKm;
            return Mathf.Exp(-(delta * delta) / (2f * DistanceSigma * DistanceSigma));
        }

        /// <summary>Symetryczny klucz dla pary stacji.</summary>
        static long Key(int a, int b)
        {
            if (a > b) (a, b) = (b, a);
            return ((long)a << 32) | (uint)b;
        }

        // JSON schema dla override file
        [System.Serializable]
        class OverrideList
        {
            public OverrideEntry[] entries = System.Array.Empty<OverrideEntry>();
        }

        [System.Serializable]
        class OverrideEntry
        {
            public int from = 0;
            public int to = 0;
            public float demand = 0f;
        }
    }
}
