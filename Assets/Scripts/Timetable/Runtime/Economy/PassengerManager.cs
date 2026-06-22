using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;
using RailwayManager.Fleet;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M6-1: Centralny manager agentów-pasażerów.
    ///
    /// - Spawn'uje nowe agenty co <see cref="SpawnIntervalSec"/> według OD matrix × modifiers
    ///   (w MVP M6-1: tylko OD matrix; time/price/rep modifiers → M6-5+)
    /// - Tick'uje istniejące agenty (abandon check — patrz <see cref="AbandonPatienceMinutes"/>)
    /// - Subskrybuje eventy TrainRunSimulator (boarding/alighting → M6-2)
    ///
    /// Target: 50 000 aktywnych agentów przy 60 FPS. Spawn demand-driven —
    /// tylko tam gdzie istnieją rozkłady pokrywające daną OD pair.
    ///
    /// Bootstrap: z <see cref="TrainRunSimulator.Awake"/> (jak inne M9c services).
    /// </summary>
    public class PassengerManager : MonoBehaviour
    {
        public static PassengerManager Instance { get; private set; }

        // Konstanty — finalne wartości w EconomyBalance (M6-5)
        const int MaxActiveAgents = 50_000;
        const float SpawnIntervalSec = 10f;             // game time
        const float AbandonPatienceMinutes = 60f;       // D14

        // ── MP-2: Profiler markers (no-op poza profilingiem) ────────
        static readonly ProfilerMarker s_TickAgents = new("PassengerManager.TickAgents");
        static readonly ProfilerMarker s_HandleTrainArrivingAtStop = new("PassengerManager.HandleTrainArrivingAtStop");
        static readonly ProfilerMarker s_MaybeSpawnAgents = new("PassengerManager.MaybeSpawnAgents");
        static readonly ProfilerMarker s_CountOffersOnPair = new("PassengerManager.CountOffersOnPair");
        static readonly ProfilerMarker s_RefreshActiveStations = new("PassengerManager.RefreshActiveStations");
        static readonly ProfilerMarker s_CalculateCapacity = new("PassengerManager.CalculateCapacity");

        readonly List<PassengerAgent> _agents = new();
        OriginDestinationMatrix _odMatrix;
        Dictionary<int, float> _stationImportance;

        /// <summary>TD-036a: importance stacji (StationImportance.CalculateAll) dla konsumentów
        /// spoza demand modelu (opłaty postojowe per kategoria). -1 gdy OD matrix jeszcze
        /// nie zbudowana lub stacja nieznana → caller używa fallbacku.</summary>
        public float GetStationImportance(int stationId)
            => _stationImportance != null && _stationImportance.TryGetValue(stationId, out float v) ? v : -1f;

        // ── M11 AS-P3: publiczne API popytu dla plannera (AS-5) i advisora ──
        // OD matrix jest STATYCZNA (gravity model z importance × dystans) — dostępna PRZED
        // uruchomieniem kursów, dokładnie tego potrzebuje planner rozkład-od-relacji.

        /// <summary>Bazowy popyt dzienny pary stacji [pax/dzień] z gravity model (bez modyfikatorów).
        /// 0 gdy OD matrix niezbudowana / para poza zasięgiem (cutoff 800 km).</summary>
        public float GetBaseDemandBetween(int fromStationId, int toStationId)
            => _odMatrix?.GetBaseDemand(fromStationId, toStationId) ?? 0f;

        /// <summary>
        /// Szacowany popyt DZIENNY z modyfikatorami stanu gry: reputacja pary × mnożnik trudności.
        /// CELOWO bez profilu godzinowego (to wielkość dobowa) — arytmetyka częstotliwości
        /// w plannerze (AS-5) nakłada <see cref="DemandModifiers.GetHourOfDayModifier"/> osobno.
        /// </summary>
        public float GetEstimatedDailyDemand(int fromStationId, int toStationId)
        {
            float base0 = GetBaseDemandBetween(fromStationId, toStationId);
            if (base0 <= 0f) return 0f;

            float reputation = ReputationManager.Instance != null
                ? ReputationManager.Instance.GetDemandFactor(fromStationId, toStationId)
                : 1f;
            float difficulty = RailwayManager.Core.Difficulty.DifficultyService.Modifiers.PassengerDemandMultiplier;
            return base0 * reputation * difficulty;
        }

        /// <summary>
        /// Czy para stacji jest JUŻ obsługiwana bieżącą ofertą rozkładów (≤2 przesiadki).
        /// Uwaga semantyka: to osiągalność PO OFERCIE (graf z Timetables) — dla NOWEJ relacji
        /// planner używa pathfindera po sieci (RailwayPathfinder), nie tego.
        /// </summary>
        public bool IsPairServedByOffer(int fromStationId, int toStationId)
            => _reachGraph != null
               && PassengerJourneyPlanner.FindJourney(fromStationId, toStationId, _reachGraph) != null;

        // ── MP-3: Spatial indexes (O(N) hot paths → O(k≈50)) ────────
        /// <summary>agentId → index w <see cref="_agents"/> List. Aktualizowane przy swap-and-pop.</summary>
        readonly Dictionary<int, int> _agentIdToIdx = new();
        /// <summary>stationId → set agentIds aktualnie w stanie WaitingAtStation na tej stacji.</summary>
        readonly Dictionary<int, HashSet<int>> _agentsByStation = new();
        /// <summary>trainRunId → set agentIds aktualnie w stanie OnTrain na tym kursie.</summary>
        readonly Dictionary<int, HashSet<int>> _agentsOnTrain = new();
        /// <summary>Reusable bufor agentIds do alight/despawn (avoid alloc per call).</summary>
        readonly List<int> _agentBuffer = new(256);

        // ── MP-4: Cache'e per-TrainRun ─────────────────────────────
        /// <summary>trainRunId → suma passengerSeats runningVehicleIds. Lazy-built przy first
        /// CalculateCapacity, invalidated w HandleRunDespawned. Composition lock w gameplay
        /// gwarantuje że run.runningVehicleIds nie zmienia się od spawn do despawn.</summary>
        readonly Dictionary<int, int> _cachedCapacity = new();

        /// <summary>M-PaxV2 Faza A: cache pojemności PER KLASA (SeatZoneType) per run.
        /// Liczone z seatBreakdown wagonów (fallback: pusty breakdown → wszystko 2. klasa).
        /// Invalidated razem z _cachedCapacity (HandleRunDespawned / ClearAgents).</summary>
        readonly Dictionary<int, Dictionary<SeatZoneType, int>> _cachedCapacityByClass = new();
        /// <summary>Pakowany klucz (fromStationId, toStationId) → liczba offers (TrainRuns mających
        /// from przed to w stops). Built w RefreshActiveStations co 30s. Klucz packed long (high=from, low=to)
        /// żeby uniknąć Tuple allocation.</summary>
        readonly Dictionary<long, int> _offersOnPairCache = new();
        /// <summary>TD-015: memo per-call MaybeSpawnAgents — kierunkowy klucz (from,to) → scalar
        /// commute×reputation×offerFreq. Czyszczony na początku każdego MaybeSpawnAgents (NIE 30s),
        /// bo commute zależy od gameTime.Hour/DayOfWeek (per-call stałe → wynik identyczny jak
        /// per-attempt compute, zero dryfu semantycznego). Eliminuje 3 compute/lookup per powtórzoną
        /// parę w pętli spawn (do spawnBudget×10 attempts).</summary>
        readonly Dictionary<long, float> _demandModifierCache = new();
        /// <summary>Reusable HashSet do GetRemainingStopStationIds — zero alloc per arrival.</summary>
        readonly HashSet<int> _remainingStopsBuffer = new();

        static long PackStationPair(int from, int to) => ((long)from << 32) | (uint)to;

        // ── MP-9: deterministic RNG per-service ──────────────────────
        static readonly DeterministicRng s_rng = RandomRegistry.GetRng("PassengerManager");

        /// <summary>Cache RailwayStation by stationId — do szybkich lookup'ów.</summary>
        Dictionary<int, RailwayStation> _stationById;

        /// <summary>Cache stationId by pathNodeId — do match'u TimetableStop.stationNodeId → RailwayStation.stationId.</summary>
        Dictionary<int, int> _stationIdByPathNode;

        /// <summary>M-PaxV2 Faza C.2c: graf bezpośredniej osiągalności (stacja→DirectEdge) z rozkładów.
        /// Budowany w TryBuildODMatrix (rebuild razem z OD matrix). Podstawa planowania przesiadek.</summary>
        Dictionary<int, List<DirectEdge>> _reachGraph;

        /// <summary>M-PaxV2 Faza C.2c: cache zaplanowanych podróży per para OD (klucz = (origin&lt;&lt;32)|dest).
        /// Wartość null = brak trasy ≤2 przesiadki (też cache'owane → planer wołany raz na parę).
        /// Czyszczony przy rebuild grafu.</summary>
        readonly Dictionary<long, PassengerJourney> _journeyCache = new();

        /// <summary>Snapshot stacji które mają rozkład JAKI PRZECHODZI przez nie (demand-driven spawning).</summary>
        HashSet<int> _activeStations = new();

        int _nextAgentId = 1;
        float _spawnAccumulator;
        float _activeStationsRefreshAccumulator;
        const float ActiveStationsRefreshSec = 30f;

        /// <summary>
        /// MP-1 stress override: gdy &gt; 0, nadpisuje <see cref="MaxActiveAgents"/> dla stress testów.
        /// -1 = use default. Ustawiać przez <see cref="DebugSetStressOverrideCap"/>.
        /// </summary>
        int _stressOverrideMaxAgents = -1;

        public IReadOnlyList<PassengerAgent> Agents => _agents;
        public int ActiveAgentCount => _agents.Count;

        /// <summary>MP-1: efektywny cap agentów (override jeśli ustawiony, inaczej domyślny 50k).</summary>
        public int EffectiveMaxAgents => _stressOverrideMaxAgents > 0 ? _stressOverrideMaxAgents : MaxActiveAgents;

        /// <summary>MP-1: snapshot active station IDs (do stress test framework).</summary>
        public IReadOnlyCollection<int> ActiveStationIds => _activeStations;

        // ── Bootstrap ────────────────────────────────────────────────

        public static PassengerManager EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PassengerManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PassengerManager>();
            Log.Info("[PassengerManager] Bootstrapped");
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
            RailwayManager.Timetable.Simulation.TrainRunSimulator.OnTrainArrivingAtStop
                += HandleTrainArrivingAtStop;
            RailwayManager.Timetable.Simulation.TrainRunSimulator.OnRunDespawned
                += HandleRunDespawned;
        }

        void OnDisable()
        {
            RailwayManager.Timetable.Simulation.TrainRunSimulator.OnTrainArrivingAtStop
                -= HandleTrainArrivingAtStop;
            RailwayManager.Timetable.Simulation.TrainRunSimulator.OnRunDespawned
                -= HandleRunDespawned;
        }

        void Start()
        {
            TryBuildODMatrix();
        }

        /// <summary>
        /// Buduje OD matrix + station importance z danych TimetableInitializer.
        /// Wywoływane w Start() (po initializer'ze) lub manualnie przez ContextMenu.
        /// </summary>
        public bool TryBuildODMatrix()
        {
            var init = TimetableInitializer.Instance;
            if (init == null || init.Stations == null || init.Graph == null)
            {
                Log.Warn("[PassengerManager] TimetableInitializer nie gotowy — OD matrix nie zbudowana. " +
                         "Wywołaj TryBuildODMatrix ręcznie gdy inicjalizacja się zakończy.");
                return false;
            }

            _stationImportance = StationImportance.CalculateAll(
                init.Stations, init.Places, init.Platforms, init.Graph);

            _odMatrix = new OriginDestinationMatrix();
            _odMatrix.Build(init.Stations, _stationImportance);
            _odMatrix.LoadOverrides();

            _stationById = new Dictionary<int, RailwayStation>();
            _stationIdByPathNode = new Dictionary<int, int>();
            foreach (var s in init.Stations)
            {
                _stationById[s.stationId] = s;
                if (s.pathNodeId >= 0)
                    _stationIdByPathNode[s.pathNodeId] = s.stationId;
            }

            // M-PaxV2 Faza C.2c: graf osiągalności (stacja→DirectEdge) z rozkładów — baza planowania
            // przesiadek. Rebuild razem z OD matrix (zmiana rozkładów). Cache podróży inwalidujemy.
            _reachGraph = JourneyGraphBuilder.Build(TimetableService.Timetables, _stationIdByPathNode);
            _journeyCache.Clear();

            RefreshActiveStations();

            Log.Info($"[PassengerManager] OD matrix gotowa: {_odMatrix.PairCount} par, " +
                     $"total demand={_odMatrix.TotalDemand():F0} pax/day");
            return true;
        }

        // ── Update loop ──────────────────────────────────────────────

        void FixedUpdate()
        {
            if (GameState.IsPaused) return;

            // TD-037: konsumpcja pending-restore puli (deterministycznie w FixedUpdate). Gate'y:
            // OD matrix + mapy stacji zbudowane ORAZ symulator skonsumował swój pending (inaczej
            // guard ubiłby pasażerów OnTrain zanim ich kursy się wznowią).
            if (_pendingPoolRestore != null && _odMatrix != null && _stationById != null
                && Simulation.TrainRunSimulator.PendingRestoreCount == 0)
            {
                ConsumePendingPoolRestore();
            }

            if (_odMatrix == null) return;

            float deltaGameSec = Time.fixedDeltaTime * GameState.TimeScale;

            _spawnAccumulator += deltaGameSec;
            if (_spawnAccumulator >= SpawnIntervalSec)
            {
                _spawnAccumulator = 0f;
                MaybeSpawnAgents();
            }

            _activeStationsRefreshAccumulator += deltaGameSec;
            if (_activeStationsRefreshAccumulator >= ActiveStationsRefreshSec)
            {
                _activeStationsRefreshAccumulator = 0f;
                RefreshActiveStations();
            }

            TickAgents();
        }

        void TickAgents()
        {
            using (s_TickAgents.Auto())
            {
                // Crash-hunt #3: absolutny czas — spójny z spawnTimeSec/abandonTimeSec (też absolutne).
                float now = GameState.TotalGameSeconds;
                // Iterate reversed for safe swap-and-pop of Arrived/Abandoned (MP-3)
                for (int i = _agents.Count - 1; i >= 0; i--)
                {
                    var a = _agents[i];
                    if (a.state == PassengerState.WaitingAtStation && now >= a.abandonTimeSec)
                    {
                        a.state = PassengerState.Abandoned;
                        // MP-3: remove from station index (był waiting, teraz abandoned)
                        if (a.currentStationId >= 0 &&
                            _agentsByStation.TryGetValue(a.currentStationId, out var stationSet))
                        {
                            stationSet.Remove(a.agentId);
                        }
                    }

                    if (!a.IsAlive)
                    {
                        // MP-3: defensive cleanup — agent powinien już być usunięty z indexów
                        // gdy state stał się Arrived/Abandoned, ale dla bezpieczeństwa double-check.
                        RemoveAgentByIdx(i);
                    }
                }
            }
        }

        // ── Spawn ────────────────────────────────────────────────────

        /// <summary>
        /// Odświeża listę "aktywnych" stacji — tych przez które przechodzą obecnie
        /// aktywne TrainRun'y (obieg Active, runDateIso=today). Spawnujemy agenty
        /// tylko na parach obu-aktywnych stacji (demand-driven).
        /// </summary>
        void RefreshActiveStations()
        {
            using (s_RefreshActiveStations.Auto())
            {
                _activeStations.Clear();
                _offersOnPairCache.Clear();  // MP-4: rebuild offers cache (single pass z _activeStations)
                if (_stationIdByPathNode == null) return;

                string todayIso = GameState.CurrentDateIso;
                int runsToday = 0, runsOther = 0, stopsMatched = 0, stopsUnmatched = 0;

                foreach (var tr in TimetableService.TrainRuns)
                {
                    if (tr.isCancelled) continue;
                    if (tr.runDateIso != todayIso) { runsOther++; continue; }
                    runsToday++;

                    var tt = TimetableService.GetTimetable(tr.timetableId);
                    if (tt == null) continue;

                    // Pierwsze przejście: zbierz active stations + bufor stationIds tego rozkładu (do offers)
                    var stationsInRun = _agentBuffer; // reuse buffer
                    stationsInRun.Clear();
                    foreach (var stop in tt.stops)
                    {
                        if (stop.stationNodeId >= 0
                            && _stationIdByPathNode.TryGetValue(stop.stationNodeId, out int sid))
                        {
                            _activeStations.Add(sid);
                            stationsInRun.Add(sid);
                            stopsMatched++;
                        }
                        else
                        {
                            stopsUnmatched++;
                        }
                    }

                    // MP-4: build offers cache — pairs (i, j) gdzie i < j w kolejności stops
                    int n = stationsInRun.Count;
                    for (int i = 0; i < n - 1; i++)
                    {
                        int from = stationsInRun[i];
                        for (int j = i + 1; j < n; j++)
                        {
                            int to = stationsInRun[j];
                            long key = PackStationPair(from, to);
                            _offersOnPairCache[key] = _offersOnPairCache.TryGetValue(key, out int c) ? c + 1 : 1;
                        }
                    }
                }

                Log.Info($"[PassengerManager] RefreshActiveStations: today='{todayIso}', " +
                         $"runsToday={runsToday}, runsOther={runsOther}, " +
                         $"stopsMatched={stopsMatched}, stopsUnmatched={stopsUnmatched}, " +
                         $"activeStations={_activeStations.Count}, offerPairsCached={_offersOnPairCache.Count}");
            }
        }

        void MaybeSpawnAgents()
        {
            using (s_MaybeSpawnAgents.Auto())
            {
                if (_agents.Count >= EffectiveMaxAgents) return;
                if (_odMatrix == null || _stationById == null) return;

                int spawnBudget = Mathf.Max(1, (EffectiveMaxAgents - _agents.Count) / 100);
                int spawned = 0;
                int attempts = 0;
                int maxAttempts = spawnBudget * 10;

                if (_activeStations.Count < 2) return;
                var activeArr = new int[_activeStations.Count];
                int idx = 0;
                foreach (int sid in _activeStations) activeArr[idx++] = sid;

                // M6-5: modifiers wspólne dla całej iteracji
                var gameTime = DemandModifiers.GetCurrentGameDateTime();
                float timeCombined = DemandModifiers.GetTimeCombined(gameTime);
                var rep = ReputationManager.Instance;

                // TD-015: memo per-call (gameTime stały w tej iteracji) — powtórzone pary nie liczą
                // commute/reputation/offerFreq ponownie. Czyszczony tu, bo następne wywołanie ma inny gameTime.
                _demandModifierCache.Clear();

                while (spawned < spawnBudget && attempts < maxAttempts)
                {
                    attempts++;
                    // MP-9: seedowane RNG (zamiast UnityEngine.Random.Range) — niezależny substream od BreakdownService
                    int fromIdx = s_rng.Range(0, activeArr.Length);
                    int toIdx = s_rng.Range(0, activeArr.Length);
                    if (fromIdx == toIdx) continue;

                    int from = activeArr[fromIdx];
                    int to = activeArr[toIdx];

                    float base0 = _odMatrix.GetBaseDemand(from, to);
                    if (base0 < 1f) continue;

                    // TD-015: scalar commute×reputation×offerFreq per kierunkowa para — memo per-call.
                    // Wynik identyczny jak osobne compute (gameTime stały w iteracji), tylko bez powtórek.
                    long pairKey = PackStationPair(from, to);
                    if (!_demandModifierCache.TryGetValue(pairKey, out float demandMod))
                    {
                        float fromImp = _stationImportance != null && _stationImportance.TryGetValue(from, out float fi) ? fi : 1f;
                        float toImp = _stationImportance != null && _stationImportance.TryGetValue(to, out float ti) ? ti : 1f;
                        float commute = DemandModifiers.GetCommuteModifier(fromImp, toImp, gameTime.Hour, gameTime.DayOfWeek);
                        float reputation = rep != null ? rep.GetDemandFactor(from, to) : 1f;
                        int offersOnPair = CountOffersOnPair(from, to);
                        float offerFreq = DemandModifiers.GetOfferFrequencyModifier(offersOnPair);
                        demandMod = commute * reputation * offerFreq;
                        _demandModifierCache[pairKey] = demandMod;
                    }

                    // MB-1 Phase B: difficulty PassengerDemandMultiplier (Easy 1.3x, Realistic 0.7x)
                    float diffDemand = DifficultyService.Modifiers.PassengerDemandMultiplier;
                    float effectiveDemand = base0 * timeCombined * demandMod * diffDemand;
                    if (effectiveDemand < 0.1f) continue;

                    // Normalizacja: demand pax/day, SpawnIntervalSec ticków na dzień = 86400/interval
                    float perTickProbability = effectiveDemand * SpawnIntervalSec / 86400f;
                    // MP-9: seedowane RNG
                    if (s_rng.Value > perTickProbability) continue;

                    // M-PaxV2 Faza C.2c: spawn TYLKO gdy istnieje trasa (≤2 przesiadki). Brak trasy →
                    // nie spawnuj (decyzja user'a — pasażer bez połączenia się nie pojawia). Planer
                    // cache'owany per para, więc kolejne próby tej samej pary są O(1).
                    if (GetOrPlanJourney(from, to) == null) continue;

                    SpawnAgent(from, to, gameTime);
                    spawned++;
                }
            }
        }

        /// <summary>
        /// Liczy ile TrainRun'ów dziś ma zarówno station 'from' jak i 'to' w stops (offer frequency).
        /// </summary>
        /// <summary>
        /// MP-4: Lookup z _offersOnPairCache (built w RefreshActiveStations co 30s) — O(1).
        /// Wcześniej O(N_runs × N_stops) per call × ~100 spawn attempts/tick = ~5M comparisons/tick.
        /// </summary>
        int CountOffersOnPair(int fromStationId, int toStationId)
        {
            using (s_CountOffersOnPair.Auto())
            {
                long key = PackStationPair(fromStationId, toStationId);
                return _offersOnPairCache.TryGetValue(key, out int c) ? c : 0;
            }
        }

        void SpawnAgent(int fromStationId, int toStationId, System.DateTime gameTime)
        {
            // M-PaxV2 Faza B: cel podróży (z pory) → klasa + budżet (zamiast losowego portfela).
            var purpose = PassengerPurposeModel.Pick(s_rng, gameTime.Hour, gameTime.DayOfWeek);
            var agent = new PassengerAgent
            {
                agentId = _nextAgentId++,
                originStationId = fromStationId,
                destinationStationId = toStationId,
                currentStationId = fromStationId,
                preference = (PassengerPreference)s_rng.Range(0, 3),     // MP-9: seedowane
                purpose = purpose,
                desiredClass = PassengerPurposeModel.PreferredClass(s_rng, purpose),
                walletGroszy = PassengerPurposeModel.WillingnessGroszy(s_rng, purpose),
                state = PassengerState.WaitingAtStation,
                // Crash-hunt #3: czas ABSOLUTNY (nie within-day GameTimeSeconds). Spawn blisko północy
                // → abandonTimeSec = now+3600 mógł przekroczyć 86400, a GameTimeSeconds resetuje się
                // o północy → agent wisiał wiecznie WaitingAtStation. TotalGameSeconds monotoniczny.
                spawnTimeSec = GameState.TotalGameSeconds,
                abandonTimeSec = GameState.TotalGameSeconds + AbandonPatienceMinutes * 60f,
            };
            int idx = _agents.Count;
            _agents.Add(agent);
            // MP-3: spatial indexes
            _agentIdToIdx[agent.agentId] = idx;
            GetOrCreateStationSet(fromStationId).Add(agent.agentId);
        }

        // ── TD-037: save/restore CAŁEJ puli pasażerów ─────────────────
        //
        // Decyzja user 2026-06-10: pełna serializacja (de facto M13-8 Phase 2 dla pasażerów).
        // Format kolumnowy (PassengerPoolSnapshot). Restore deferred: moduł "passengers" odkłada
        // payload (static — manager może nie istnieć przy Deserialize), FixedUpdate konsumuje gdy
        // OD matrix gotowa i symulator wznowił swoje kursy (kolejność: runy przed pasażerami).

        static PassengerPoolSnapshot _pendingPoolRestore;

        /// <summary>TD-037: odkłada pulę do wznowienia (konsumpcja w FixedUpdate pod gate'ami).</summary>
        public static void SetPendingRestore(PassengerPoolSnapshot snapshot)
        {
            _pendingPoolRestore = (snapshot != null && snapshot.Count > 0) ? snapshot : null;
        }

        /// <summary>Liczba agentów czekających na wznowienie (diagnostyka + testy).</summary>
        public static int PendingPoolRestoreCount => _pendingPoolRestore?.Count ?? 0;

        /// <summary>TD-037: kolumnowy snapshot całej puli (do save). Pomija Arrived/Abandoned (defensive
        /// — usuwane natychmiast, ale save mógłby trafić między mutacją a sweep'em).</summary>
        public PassengerPoolSnapshot BuildSaveSnapshot()
        {
            int n = 0;
            for (int i = 0; i < _agents.Count; i++)
                if (_agents[i].IsAlive) n++;

            var s = new PassengerPoolSnapshot
            {
                nextAgentId = _nextAgentId,
                spawnAccumulator = _spawnAccumulator,
                agentId = new int[n],
                originStationId = new int[n],
                destinationStationId = new int[n],
                preference = new int[n],
                walletGroszy = new int[n],
                state = new int[n],
                currentStationId = new int[n],
                currentTrainRunId = new int[n],
                spawnTimeSec = new float[n],
                abandonTimeSec = new float[n],
                paidTotalGroszy = new int[n],
                transferCount = new int[n],
                purpose = new int[n],
                desiredClass = new int[n],
                currentLegIndex = new int[n],
            };

            int w = 0;
            for (int i = 0; i < _agents.Count; i++)
            {
                var a = _agents[i];
                if (!a.IsAlive) continue;
                s.agentId[w] = a.agentId;
                s.originStationId[w] = a.originStationId;
                s.destinationStationId[w] = a.destinationStationId;
                s.preference[w] = (int)a.preference;
                s.walletGroszy[w] = a.walletGroszy;
                s.state[w] = (int)a.state;
                s.currentStationId[w] = a.currentStationId;
                s.currentTrainRunId[w] = a.currentTrainRunId;
                s.spawnTimeSec[w] = a.spawnTimeSec;
                s.abandonTimeSec[w] = a.abandonTimeSec;
                s.paidTotalGroszy[w] = a.paidTotalGroszy;
                s.transferCount[w] = a.transferCount;
                s.purpose[w] = (int)a.purpose;
                s.desiredClass[w] = (int)a.desiredClass;
                s.currentLegIndex[w] = a.currentLegIndex;
                w++;
            }
            return s;
        }

        /// <summary>
        /// TD-037: odbudowa puli + WSZYSTKICH indeksów z pending snapshotu. Stany przejściowe
        /// (Boarding/Alighting nie występują między tickami — zmieniane synchronicznie) defensywnie
        /// normalizowane. Agent OnTrain z runem, który się NIE wznowił → Abandoned (zbiorczy log).
        /// </summary>
        void ConsumePendingPoolRestore()
        {
            var s = _pendingPoolRestore;
            _pendingPoolRestore = null;
            if (s == null) return;
            if (!s.IsConsistent())
            {
                Log.Warn("[PassengerManager] TD-037: snapshot puli niespójny (różne długości kolumn) — pomijam restore");
                return;
            }

            // Czysty stan — pula + indeksy + cache per-run
            _agents.Clear();
            _agentIdToIdx.Clear();
            _agentsByStation.Clear();
            _agentsOnTrain.Clear();
            _cachedCapacity.Clear();
            _cachedCapacityByClass.Clear();

            var sim = Simulation.TrainRunSimulator.Instance;
            int restored = 0, abandonedOrphans = 0;

            for (int i = 0; i < s.Count; i++)
            {
                var st = (PassengerState)s.state[i];
                if (st == PassengerState.Arrived || st == PassengerState.Abandoned) continue;

                var a = new PassengerAgent
                {
                    agentId = s.agentId[i],
                    originStationId = s.originStationId[i],
                    destinationStationId = s.destinationStationId[i],
                    preference = (PassengerPreference)s.preference[i],
                    walletGroszy = s.walletGroszy[i],
                    state = st,
                    currentStationId = s.currentStationId[i],
                    currentTrainRunId = s.currentTrainRunId[i],
                    spawnTimeSec = s.spawnTimeSec[i],
                    abandonTimeSec = s.abandonTimeSec[i],
                    paidTotalGroszy = s.paidTotalGroszy[i],
                    transferCount = s.transferCount[i],
                    purpose = (TripPurpose)s.purpose[i],
                    desiredClass = (RailwayManager.Fleet.SeatZoneType)s.desiredClass[i],
                    currentLegIndex = s.currentLegIndex[i],
                };

                // Defensive normalizacja stanów przejściowych (nie powinny wystąpić w snapshotcie)
                if (a.state == PassengerState.Boarding || a.state == PassengerState.Alighting)
                {
                    a.state = a.currentTrainRunId >= 0 && a.currentStationId < 0
                        ? PassengerState.OnTrain : PassengerState.WaitingAtStation;
                }

                if (a.state == PassengerState.OnTrain)
                {
                    // Guard: run musi być AKTYWNY po restore (wznowiony przez symulator); inaczej agent
                    // przepada jak przy HandleRunDespawned (kasa za bilet była przy boarding).
                    bool runAlive = sim != null && a.currentTrainRunId >= 0 && sim.IsActive(a.currentTrainRunId);
                    if (!runAlive) { abandonedOrphans++; continue; }
                }

                int idx = _agents.Count;
                _agents.Add(a);
                _agentIdToIdx[a.agentId] = idx;

                if (a.state == PassengerState.WaitingAtStation)
                    GetOrCreateStationSet(a.currentStationId).Add(a.agentId);
                else if (a.state == PassengerState.OnTrain)
                    GetOrCreateTrainSet(a.currentTrainRunId).Add(a.agentId);

                restored++;
            }

            _nextAgentId = s.nextAgentId > 0 ? s.nextAgentId : 1;
            _spawnAccumulator = s.spawnAccumulator;

            Log.Info($"[PassengerManager] TD-037 restore puli: {restored} agentów wznowionych" +
                     (abandonedOrphans > 0 ? $", {abandonedOrphans} OnTrain bez żywego kursu → przepadli" : ""));
        }

        // ── Queries (dla UI, debug, M6-2 boarding) ───────────────────

        /// <summary>Lista agentów czekających na stacji w kierunku konkretnej destynacji.
        /// MP-3: lookup po _agentsByStation index — O(k) zamiast O(N_agents).</summary>
        public List<PassengerAgent> GetWaitingAt(int stationId, int toStationId = -1)
        {
            var result = new List<PassengerAgent>();
            if (!_agentsByStation.TryGetValue(stationId, out var set)) return result;
            foreach (int agentId in set)
            {
                if (!_agentIdToIdx.TryGetValue(agentId, out int idx)) continue;
                var a = _agents[idx];
                if (a.state != PassengerState.WaitingAtStation) continue;
                if (toStationId >= 0 && a.destinationStationId != toStationId) continue;
                result.Add(a);
            }
            return result;
        }

        /// <summary>Liczba agentów czekających na danej stacji (wszelkie kierunki).
        /// MP-3: O(1) z _agentsByStation index.</summary>
        public int CountWaitingAt(int stationId)
        {
            return _agentsByStation.TryGetValue(stationId, out var set) ? set.Count : 0;
        }

        // ── M6-2: Boarding / Alighting ──────────────────────────────

        /// <summary>
        /// Pociąg zatrzymał się na stacji. Alight (wysiadają agenty z celem = ta stacja)
        /// + Board (wsiadają agenty czekający tu z celem w dalszej części trasy).
        /// M-TimetableUX F1.2: tylko dla <see cref="StopType.PH"/> — PT/ZD/Transit nie mają
        /// operacji pasażerskich (PT = regulacja ruchu, ZD = crew swap, Transit = przelot).
        /// </summary>
        void HandleTrainArrivingAtStop(TrainRun run, int stopIndex, int stationNodeId)
        {
            using var _profMarker = s_HandleTrainArrivingAtStop.Auto();

            if (_stationIdByPathNode == null) return;
            if (!_stationIdByPathNode.TryGetValue(stationNodeId, out int stationId)) return;

            // M-TimetableUX F1.2: filter per StopType — boarding/alighting tylko dla PH.
            // PT (regulacja), ZD (crew swap), Transit (przelot) skip wszystko poza track tracking.
            var tt = TimetableService.GetTimetable(run.timetableId);
            if (tt?.stops == null || stopIndex < 0 || stopIndex >= tt.stops.Count) return;
            if (tt.stops[stopIndex].stopType != StopType.PH) return;

            // ── Alight (MP-3: lookup w _agentsOnTrain[run.id]) ──
            // M-PaxV2 Faza C.2c: wysiadka gdy stacja == cel BIEŻĄCEGO odcinka. Finalny cel → Arrived;
            // węzeł przesiadkowy → currentLegIndex++ + transferCount++ + wraca WaitingAtStation tu
            // (czeka na kurs następnego odcinka).
            int alighted = 0, transferred = 0;
            if (_agentsOnTrain.TryGetValue(run.id, out var onTrainSet) && onTrainSet.Count > 0)
            {
                // Snapshot do bufora (modyfikacja set'u w trakcie iter)
                _agentBuffer.Clear();
                foreach (int agentId in onTrainSet)
                {
                    if (!_agentIdToIdx.TryGetValue(agentId, out int idx)) continue;
                    var a = _agents[idx];
                    if (CurrentLegTarget(a) == stationId)
                        _agentBuffer.Add(agentId);
                }
                // Iter snapshot reverse → bezpieczne swap-and-pop (RemoveAgentByIdx tylko dla Arrived)
                for (int b = _agentBuffer.Count - 1; b >= 0; b--)
                {
                    int agentId = _agentBuffer[b];
                    if (!_agentIdToIdx.TryGetValue(agentId, out int idx)) continue;
                    var a = _agents[idx];
                    if (a.destinationStationId == stationId)
                    {
                        // Finalny cel — dotarł.
                        a.state = PassengerState.Arrived;
                        onTrainSet.Remove(agentId);
                        RemoveAgentByIdx(idx);
                        alighted++;
                    }
                    else
                    {
                        // Węzeł przesiadkowy — zejdź z pociągu, czekaj tu na następny odcinek.
                        a.currentLegIndex++;
                        a.transferCount++;
                        a.state = PassengerState.WaitingAtStation;
                        a.currentStationId = stationId;
                        a.currentTrainRunId = -1;
                        onTrainSet.Remove(agentId);
                        GetOrCreateStationSet(stationId).Add(agentId);
                        transferred++;
                    }
                }
            }

            // ── Board ──
            // Znajdź pozostałe stopy na tym kursie (po bieżącym) — cele do których ten pociąg jedzie
            var remainingStops = GetRemainingStopStationIds(run, stopIndex);
            if (remainingStops == null || remainingStops.Count == 0)
            {
                if (alighted > 0 || transferred > 0)
                    Log.Info($"[PassengerManager] Run#{run.id} @ station#{stationId}: {alighted} alighted, {transferred} transfer (terminal)");
                return;
            }

            int capacity = CalculateCapacity(run);   // total — tylko do logu
            int onboard = CountOnTrain(run.id);

            // M-PaxV2 Faza A: dostępne miejsca PER KLASA = pojemność klasy − już zajęte tej klasy.
            // capByClass jest cache'owany → kopiujemy do mutowalnego availByClass.
            var capByClass = CalculateCapacityByClass(run);
            var availByClass = new Dictionary<SeatZoneType, int>(capByClass);
            if (_agentsOnTrain.TryGetValue(run.id, out var alreadyOnboard))
            {
                foreach (int oaid in alreadyOnboard)
                {
                    if (!_agentIdToIdx.TryGetValue(oaid, out int oidx)) continue;
                    var oc = _agents[oidx].desiredClass;
                    if (availByClass.TryGetValue(oc, out int av)) availByClass[oc] = av - 1;
                }
            }

            int boarded = 0, wantedToBoard = 0, totalFareGroszy = 0;
            var econ = EconomyManager.Instance;

            // MP-3: lookup waiting agentów w _agentsByStation[stationId] zamiast linear scan
            if (_agentsByStation.TryGetValue(stationId, out var waitingSet) && waitingSet.Count > 0)
            {
                _agentBuffer.Clear();
                // Snapshot agentIds którzy chcą wsiąść (matching destinationStationId)
                foreach (int agentId in waitingSet)
                {
                    if (!_agentIdToIdx.TryGetValue(agentId, out int idx)) continue;
                    var a = _agents[idx];
                    if (a.state != PassengerState.WaitingAtStation) continue;
                    // M-PaxV2 Faza C.2c: match do celu BIEŻĄCEGO odcinka (nie finalnego — przesiadki).
                    if (!remainingStops.Contains(CurrentLegTarget(a))) continue;
                    _agentBuffer.Add(agentId);
                }

                // Apply boarding decision per kandydata (miejsce w KLASIE / wallet check)
                HashSet<int> trainSet = null;
                for (int b = 0; b < _agentBuffer.Count; b++)
                {
                    int agentId = _agentBuffer[b];
                    if (!_agentIdToIdx.TryGetValue(agentId, out int idx)) continue;
                    var a = _agents[idx];
                    wantedToBoard++;

                    // M-PaxV2 Faza A: musi być wolne miejsce w KLASIE pasażera (inaczej czeka).
                    var cls = a.desiredClass;
                    if (!availByClass.TryGetValue(cls, out int availInClass) || availInClass <= 0) continue;

                    // MB-1 Phase B: TicketPriceToleranceMultiplier — wyższy = pasażer akceptuje wyższe ceny.
                    float tolerance = DifficultyService.Modifiers.TicketPriceToleranceMultiplier;

                    // M-PaxV2 Faza C.2c: cena + budżet. Multi-leg (przesiadki) → through-fare (base raz,
                    // per-km per odcinek); pasażer płaci wkład BIEŻĄCEGO odcinka, a stać-na-całość
                    // sprawdzamy RAZ przy pierwszym odcinku (nie utknie w połowie). Direct (1 odcinek /
                    // brak zaplanowanej trasy) → TicketSystem (tiers-aware, cena do finalnego celu).
                    var journey = GetOrPlanJourney(a.originStationId, a.destinationStationId);
                    int fareGroszy;
                    if (journey != null && journey.legs.Count > 1)
                    {
                        if (a.currentLegIndex == 0)
                        {
                            int throughTotal = ThroughFareCalculator.ComputeTotalGroszy(journey, cls, ResolveCategoryFunc);
                            if (throughTotal > a.walletGroszy * tolerance) continue;
                        }
                        fareGroszy = ThroughFareCalculator.LegContributionGroszy(journey, a.currentLegIndex, cls, ResolveCategoryFunc);
                    }
                    else
                    {
                        float distKm = GetSegmentDistanceKm(run, stopIndex, a.destinationStationId);
                        fareGroszy = TicketSystem.CalculatePriceGroszy(run, cls, distKm);
                        if (fareGroszy > a.walletGroszy * tolerance) continue;
                    }

                    // Transition Waiting → OnTrain (MP-3: aktualizuj indexy)
                    a.state = PassengerState.OnTrain;
                    a.currentTrainRunId = run.id;
                    a.currentStationId = -1;
                    a.walletGroszy -= fareGroszy;
                    a.paidTotalGroszy += fareGroszy;
                    availByClass[cls] = availInClass - 1;
                    boarded++;
                    totalFareGroszy += fareGroszy;

                    waitingSet.Remove(agentId);
                    if (trainSet == null) trainSet = GetOrCreateTrainSet(run.id);
                    trainSet.Add(agentId);

                    econ?.AddRevenue(run.circulationId, fareGroszy, "ticket");
                }
            }

            if (alighted > 0 || transferred > 0 || boarded > 0 || wantedToBoard > 0)
            {
                Log.Info($"[PassengerManager] Run#{run.id} @ station#{stationId}: " +
                         $"alight={alighted}, transfer={transferred}, board={boarded}/{wantedToBoard}, " +
                         $"fare={totalFareGroszy / 100f:F0}zł " +
                         $"(capacity={capacity}, onboard={onboard + boarded})");
            }
        }

        /// <summary>
        /// Dystans [km] od stacji boardingu (stopIndex) do stacji docelowej agenta.
        /// Wyliczany z TimetableStop.distanceFromStartM różnicy.
        /// </summary>
        float GetSegmentDistanceKm(TrainRun run, int boardStopIndex, int destStationId)
        {
            var tt = TimetableService.GetTimetable(run.timetableId);
            if (tt == null || boardStopIndex >= tt.stops.Count) return 0f;

            float boardDistM = tt.stops[boardStopIndex].distanceFromStartM;
            for (int i = boardStopIndex + 1; i < tt.stops.Count; i++)
            {
                var stop = tt.stops[i];
                if (!_stationIdByPathNode.TryGetValue(stop.stationNodeId, out int sid)) continue;
                if (sid == destStationId)
                    return Mathf.Max(0f, (stop.distanceFromStartM - boardDistM) / 1000f);
            }
            return 0f;
        }

        // ── M-PaxV2 Faza C.2c: planowanie podróży (przesiadki) ───────

        /// <summary>Zwraca (cache'owaną) podróż origin→dest wg grafu osiągalności. null = brak trasy
        /// (≤2 przesiadki). Wynik (też null) cache'owany per para OD — planer wołany raz na parę.</summary>
        PassengerJourney GetOrPlanJourney(int origin, int dest)
        {
            long key = ((long)origin << 32) | (uint)dest;
            if (_journeyCache.TryGetValue(key, out var cached)) return cached;
            PassengerJourney journey = _reachGraph != null
                ? PassengerJourneyPlanner.FindJourney(origin, dest, _reachGraph)
                : null;
            _journeyCache[key] = journey;   // cache też null (brak trasy)
            return journey;
        }

        /// <summary>Stacja wysiadki BIEŻĄCEGO odcinka agenta (= cel do którego teraz jedzie/wsiada).
        /// Direct lub brak zaplanowanej podróży → finalny cel (zachowanie z Fazy A).</summary>
        int CurrentLegTarget(PassengerAgent a)
        {
            var journey = GetOrPlanJourney(a.originStationId, a.destinationStationId);
            if (journey == null || a.currentLegIndex < 0 || a.currentLegIndex >= journey.legs.Count)
                return a.destinationStationId;
            return journey.legs[a.currentLegIndex].alightStationId;
        }

        // static readonly Func → brak alokacji delegata per board (przekazywany do ThroughFareCalculator).
        static readonly System.Func<string, CommercialCategory> ResolveCategoryFunc = ResolveCategory;
        static CommercialCategory ResolveCategory(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var cats = TimetableService.CommercialCategories;
            for (int i = 0; i < cats.Count; i++)
                if (cats[i] != null && cats[i].id == id) return cats[i];
            return null;
        }

        /// <summary>
        /// Kurs się zakończył — agenty które jeszcze były on-train muszą być oczyszczone
        /// (edge case: pasażer nie dotarł do celu — flag jako Abandoned).
        /// </summary>
        void HandleRunDespawned(TrainRun run)
        {
            int stuck = 0;
            // MP-3: lookup w _agentsOnTrain[run.id] zamiast linear scan
            if (_agentsOnTrain.TryGetValue(run.id, out var onTrainSet) && onTrainSet.Count > 0)
            {
                _agentBuffer.Clear();
                foreach (int agentId in onTrainSet) _agentBuffer.Add(agentId);

                for (int b = _agentBuffer.Count - 1; b >= 0; b--)
                {
                    int agentId = _agentBuffer[b];
                    if (!_agentIdToIdx.TryGetValue(agentId, out int idx)) continue;
                    var a = _agents[idx];
                    a.state = PassengerState.Abandoned;
                    onTrainSet.Remove(agentId);
                    RemoveAgentByIdx(idx);
                    stuck++;
                }
            }
            // Cleanup empty trainSet (zwolnij dictionary slot)
            if (_agentsOnTrain.TryGetValue(run.id, out var s) && s.Count == 0)
                _agentsOnTrain.Remove(run.id);

            // MP-4: invalidate capacity cache (run się skończył)
            _cachedCapacity.Remove(run.id);
            _cachedCapacityByClass.Remove(run.id);

            if (stuck > 0)
                Log.Warn($"[PassengerManager] Run#{run.id} despawned z {stuck} agentami on-train — flagged Abandoned");
        }

        /// <summary>
        /// Zwraca zbiór stationId wszystkich stopów po bieżącym (exclusive) — cele
        /// do których ten pociąg jeszcze pojedzie.
        /// </summary>
        /// <summary>
        /// MP-4: zwraca reusable bufor <see cref="_remainingStopsBuffer"/> — zero alloc per call.
        /// Caller NIE może trzymać reference za długo — kolejne wywołanie nadpisze.
        /// HandleTrainArrivingAtStop używa tylko w obrębie jednego call'a, OK.
        /// </summary>
        HashSet<int> GetRemainingStopStationIds(TrainRun run, int afterStopIndex)
        {
            _remainingStopsBuffer.Clear();
            var tt = TimetableService.GetTimetable(run.timetableId);
            if (tt == null) return _remainingStopsBuffer;

            for (int i = afterStopIndex + 1; i < tt.stops.Count; i++)
            {
                var stop = tt.stops[i];
                if (_stationIdByPathNode.TryGetValue(stop.stationNodeId, out int sid))
                    _remainingStopsBuffer.Add(sid);
            }
            return _remainingStopsBuffer;
        }

        /// <summary>
        /// Sumaryczna pojemność pociągu = suma <see cref="FleetVehicleData.passengerSeats"/>
        /// wszystkich runningVehicleIds.
        /// </summary>
        int CalculateCapacity(TrainRun run)
        {
            using (s_CalculateCapacity.Auto())
            {
                if (run.runningVehicleIds == null || run.runningVehicleIds.Count == 0)
                    return 0;

                // MP-4: cache lookup (composition lock w gameplay gwarantuje stabilność od spawn do despawn)
                if (_cachedCapacity.TryGetValue(run.id, out int cached))
                    return cached;

                int total = 0;
                foreach (var v in RailwayManager.Fleet.FleetService.OwnedVehicles)
                {
                    if (run.runningVehicleIds.Contains(v.id))
                        total += v.passengerSeats;
                }
                _cachedCapacity[run.id] = total;
                return total;
            }
        }

        /// <summary>
        /// M-PaxV2 Faza A: pojemność PER KLASA (SeatZoneType) dla runu. Sumuje seatBreakdown
        /// wagonów; wagon bez breakdown (legacy) → całe passengerSeats jako SecondClassOpen
        /// (backward-compat). Cache jak CalculateCapacity (composition lock gameplay).
        /// UWAGA: zwraca cache'owaną referencję — caller NIE może mutować (boarding kopiuje).
        /// </summary>
        Dictionary<SeatZoneType, int> CalculateCapacityByClass(TrainRun run)
        {
            if (run.runningVehicleIds == null || run.runningVehicleIds.Count == 0)
                return new Dictionary<SeatZoneType, int>();

            if (_cachedCapacityByClass.TryGetValue(run.id, out var cached))
                return cached;

            var byClass = new Dictionary<SeatZoneType, int>();
            foreach (var v in RailwayManager.Fleet.FleetService.OwnedVehicles)
            {
                if (!run.runningVehicleIds.Contains(v.id)) continue;

                if (v.seatBreakdown != null && v.seatBreakdown.Count > 0)
                {
                    foreach (var sc in v.seatBreakdown)
                    {
                        if (sc == null || sc.count <= 0) continue;
                        byClass.TryGetValue(sc.type, out int cur);
                        byClass[sc.type] = cur + sc.count;
                    }
                }
                else if (v.passengerSeats > 0)
                {
                    byClass.TryGetValue(SeatZoneType.SecondClassOpen, out int cur);
                    byClass[SeatZoneType.SecondClassOpen] = cur + v.passengerSeats;
                }
            }
            _cachedCapacityByClass[run.id] = byClass;
            return byClass;
        }

        /// <summary>Liczba agentów aktualnie na pokładzie danego kursu.
        /// MP-3: O(1) z _agentsOnTrain index. Public (M-Dispatch Faza 4c): predykcyjny dispatcher
        /// waży obłożeniem (pełny pociąg = większa waga w decyzji trzymaj/puść).</summary>
        public int CountOnTrain(int trainRunId)
        {
            return _agentsOnTrain.TryGetValue(trainRunId, out var set) ? set.Count : 0;
        }

        // ── Debug ────────────────────────────────────────────────────

        [ContextMenu("Debug: Dump passenger stats")]
        // ── M-PaxV2 Faza A: hooki testowe (end-to-end board→fare→przychód) ──

        /// <summary>Test/debug: wstrzykuje mapowanie pathNodeId→stationId (normalnie z TimetableInitializer).</summary>
        public void DebugSetStationMapping(Dictionary<int, int> nodeToStation) => _stationIdByPathNode = nodeToStation;

        /// <summary>Test/debug: wstrzykuje graf osiągalności + czyści cache podróży (normalnie buduje
        /// TryBuildODMatrix z TimetableInitializer, niedostępnego w EditMode). M-PaxV2 Faza C.2c.</summary>
        public void DebugSetReachGraph(Dictionary<int, List<DirectEdge>> graph)
        {
            _reachGraph = graph;
            _journeyCache.Clear();
        }

        /// <summary>Test/debug: tworzy N agentów czekających na stationId, jadących do destStationId,
        /// z konkretną klasą i portfelem (omija OD matrix / random). Nie abandonują (długi patience).</summary>
        public void DebugSpawnWaiting(int stationId, int destStationId, SeatZoneType cls, int walletGroszy, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var agent = new PassengerAgent
                {
                    agentId = _nextAgentId++,
                    originStationId = stationId,
                    destinationStationId = destStationId,
                    currentStationId = stationId,
                    desiredClass = cls,
                    walletGroszy = walletGroszy,
                    state = PassengerState.WaitingAtStation,
                    spawnTimeSec = GameState.TotalGameSeconds,
                    abandonTimeSec = GameState.TotalGameSeconds + 1_000_000f,
                };
                int idx = _agents.Count;
                _agents.Add(agent);
                _agentIdToIdx[agent.agentId] = idx;
                GetOrCreateStationSet(stationId).Add(agent.agentId);
            }
        }

        /// <summary>Test/debug: wywołuje boarding/alighting dla przyjazdu (normalnie z OnTrainArrivingAtStop).</summary>
        public void DebugSimulateArrival(TrainRun run, int stopIndex, int stationNodeId)
            => HandleTrainArrivingAtStop(run, stopIndex, stationNodeId);

        public void DebugDumpStats()
        {
            int waiting = 0, onTrain = 0, boarding = 0, alighting = 0;
            foreach (var a in _agents)
            {
                switch (a.state)
                {
                    case PassengerState.WaitingAtStation: waiting++; break;
                    case PassengerState.OnTrain: onTrain++; break;
                    case PassengerState.Boarding: boarding++; break;
                    case PassengerState.Alighting: alighting++; break;
                }
            }
            Log.Info($"[PassengerManager] {_agents.Count} active agents: " +
                     $"waiting={waiting}, onTrain={onTrain}, boarding={boarding}, alighting={alighting}. " +
                     $"OD pairs={(_odMatrix?.PairCount ?? 0)}, active stations={_activeStations.Count}");
        }

        [ContextMenu("Debug: Rebuild OD matrix")]
        public void DebugRebuildMatrix() => TryBuildODMatrix();

        [ContextMenu("Debug: Refresh active stations")]
        public void DebugRefreshActiveStations() => RefreshActiveStations();

        [ContextMenu("Debug: Dump timetable state (circulations + runs)")]
        public void DebugDumpTimetableState()
        {
            string today = GameState.CurrentDateIso;
            var todayDate = IsoTime.ParseDate(today);
            Log.Info($"[Diag] today={today} ({todayDate.DayOfWeek}), GameDay={GameState.GameDay}, " +
                     $"GameStartDate={GameState.GameStartDateIso}");
            Log.Info($"[Diag] HomeDepotStationId={GameState.HomeDepotStationId}");

            var circs = CirculationService.Circulations;
            Log.Info($"[Diag] Total circulations: {circs.Count}");
            foreach (var c in circs)
            {
                int assignments = 0;
                if (c.vehicleAssignmentsPerDay != null)
                    foreach (var kvp in c.vehicleAssignmentsPerDay)
                        if (kvp.Value != null && kvp.Value.Count > 0) assignments++;
                Log.Info($"  Circulation#{c.id} '{c.name}' status={c.status} " +
                         $"steps={c.steps?.Count ?? 0} calendar={c.calendar.ToString()} " +
                         $"oneTime={c.isOneTime} weeksValid={c.weeksValid} " +
                         $"assignmentsPerDayCount={assignments}");
            }

            var runs = TimetableService.TrainRuns;
            Log.Info($"[Diag] Total TrainRuns: {runs.Count}");
            int show = Mathf.Min(runs.Count, 10);
            for (int i = 0; i < show; i++)
            {
                var r = runs[i];
                Log.Info($"  Run#{r.id} date={r.runDateIso} circ={r.circulationId} " +
                         $"step={r.circulationStepIndex} tt={r.timetableId} " +
                         $"completed={r.isCompleted} cancelled={r.isCancelled}");
            }
            if (runs.Count > show)
                Log.Info($"  ... i {runs.Count - show} więcej");

            // Zlicz daty aby pokazać spread
            var dateGroups = new Dictionary<string, int>();
            foreach (var r in runs)
            {
                if (!dateGroups.ContainsKey(r.runDateIso)) dateGroups[r.runDateIso] = 0;
                dateGroups[r.runDateIso]++;
            }
            if (dateGroups.Count > 0)
            {
                Log.Info($"[Diag] TrainRuns rozkład na daty (pierwsze 5):");
                int shown = 0;
                foreach (var kvp in dateGroups)
                {
                    if (shown++ >= 5) break;
                    Log.Info($"  {kvp.Key}: {kvp.Value} runs");
                }
            }
        }

        [ContextMenu("Debug: Force regenerate TrainRuns for all Active circulations")]
        public void DebugRegenerateTrainRuns()
        {
            int generated = 0;
            foreach (var c in CirculationService.Circulations)
            {
                if (c.status != CirculationStatus.Active) continue;
                TrainRunGenerator.GenerateForCirculation(c);
                generated++;
            }
            Log.Info($"[Diag] Wygenerowano runs dla {generated} Active circulations. " +
                     $"Total TrainRuns teraz: {TimetableService.TrainRuns.Count}");
        }

        [ContextMenu("Debug: Spawn 100 test agents (first 2 active stations)")]
        public void DebugSpawnTestAgents()
        {
            if (_activeStations.Count < 2)
            {
                Log.Warn("[PassengerManager] Mniej niż 2 aktywne stacje — użyj " +
                         "'Spawn 100 test agents (BYPASS — any 2 major stations)' zamiast");
                return;
            }
            var arr = new int[_activeStations.Count];
            int i = 0; foreach (int s in _activeStations) arr[i++] = s;
            var gt = DemandModifiers.GetCurrentGameDateTime();
            for (int k = 0; k < 100; k++)
                SpawnAgent(arr[0], arr[1], gt);
            Log.Info($"[PassengerManager] Spawned 100 test agents {arr[0]}→{arr[1]}");
        }

        [ContextMenu("Debug: Spawn 100 test agents (BYPASS — any 2 major stations)")]
        public void DebugSpawnTestAgentsBypass()
        {
            var init = TimetableInitializer.Instance;
            if (init == null || init.Stations == null || init.Stations.Count < 2)
            {
                Log.Warn("[PassengerManager] TimetableInitializer nie gotowy lub <2 stacji");
                return;
            }

            int from = -1, to = -1;
            foreach (var s in init.Stations)
            {
                if (!s.isMajorStation || s.pathNodeId < 0) continue;
                if (from < 0) from = s.stationId;
                else if (to < 0) { to = s.stationId; break; }
            }
            if (from < 0 || to < 0)
            {
                Log.Warn("[PassengerManager] Nie znaleziono 2 major stations w grafie");
                return;
            }

            var gt = DemandModifiers.GetCurrentGameDateTime();
            for (int k = 0; k < 100; k++)
                SpawnAgent(from, to, gt);

            var fromStation = _stationById != null && _stationById.TryGetValue(from, out var fs) ? fs.name : $"#{from}";
            var toStation = _stationById != null && _stationById.TryGetValue(to, out var ts) ? ts.name : $"#{to}";
            Log.Info($"[PassengerManager] BYPASS: Spawned 100 test agents " +
                     $"'{fromStation}' (#{from}) → '{toStation}' (#{to})");
        }

        [ContextMenu("Debug: List first 10 agents")]
        public void DebugListAgents()
        {
            int n = Mathf.Min(10, _agents.Count);
            for (int i = 0; i < n; i++)
                Log.Info($"  {_agents[i]}");
            if (_agents.Count > n)
                Log.Info($"  ... i {_agents.Count - n} więcej");
        }

        // ── MP-1: Stress test API ────────────────────────────────────

        /// <summary>
        /// MP-1: nadpisuje cap aktywnych agentów. Wywoływać tylko z <c>PerfStressBootstrap</c>.
        /// Pass <c>-1</c> żeby wrócić do default (<see cref="MaxActiveAgents"/>).
        /// </summary>
        public void DebugSetStressOverrideCap(int cap)
        {
            _stressOverrideMaxAgents = cap > 0 ? cap : -1;
            // Pre-alokacja backing array żeby uniknąć GC spike z resize'ów List przy 500k Add.
            if (_stressOverrideMaxAgents > 0 && _agents.Capacity < _stressOverrideMaxAgents)
                _agents.Capacity = _stressOverrideMaxAgents;
            Log.Info($"[PassengerManager] Stress cap override = {(_stressOverrideMaxAgents > 0 ? _stressOverrideMaxAgents.ToString() : "default 50k")}");
        }

        /// <summary>
        /// MP-1: force-spawn N agentów z <paramref name="fromStationId"/> do <paramref name="toStationId"/>,
        /// omijając OD matrix probabilistic rejection w <see cref="MaybeSpawnAgents"/>. Wymaga zbudowanej OD matrix.
        /// Honoruje <see cref="EffectiveMaxAgents"/>. Zwraca liczbę faktycznie spawnowanych.
        /// </summary>
        public int DebugForceSpawn(int fromStationId, int toStationId, int count)
        {
            if (_odMatrix == null || _stationById == null)
            {
                Log.Warn("[PassengerManager] DebugForceSpawn: OD matrix nie gotowy — abort");
                return 0;
            }
            if (fromStationId == toStationId) return 0;

            int budget = Mathf.Max(0, EffectiveMaxAgents - _agents.Count);
            int actualCount = Mathf.Min(count, budget);
            var gt = DemandModifiers.GetCurrentGameDateTime();
            for (int i = 0; i < actualCount; i++)
                SpawnAgent(fromStationId, toStationId, gt);
            return actualCount;
        }

        /// <summary>
        /// MP-1: czyszczenie wszystkich agentów (po stress run, gdy nie chcemy już płacić TickAgents
        /// kosztu w editorze). MP-3: czyści też wszystkie spatial indexy.
        /// </summary>
        [ContextMenu("MP-1: Clear all agents (stress reset)")]
        public void DebugClearAgents()
        {
            int n = _agents.Count;
            _agents.Clear();
            _agentIdToIdx.Clear();
            _agentsByStation.Clear();
            _agentsOnTrain.Clear();
            _cachedCapacity.Clear();      // MP-4
            _cachedCapacityByClass.Clear(); // M-PaxV2 Faza A
            _offersOnPairCache.Clear();   // MP-4
            Log.Info($"[PassengerManager] DebugClearAgents: cleared {n} agents + indexes + caches");
        }

        // ── MP-3: spatial index helpers ──────────────────────────────

        HashSet<int> GetOrCreateStationSet(int stationId)
        {
            if (!_agentsByStation.TryGetValue(stationId, out var set))
            {
                set = new HashSet<int>();
                _agentsByStation[stationId] = set;
            }
            return set;
        }

        HashSet<int> GetOrCreateTrainSet(int trainRunId)
        {
            if (!_agentsOnTrain.TryGetValue(trainRunId, out var set))
            {
                set = new HashSet<int>();
                _agentsOnTrain[trainRunId] = set;
            }
            return set;
        }

        /// <summary>
        /// MP-3: swap-and-pop usunięcie agenta z <see cref="_agents"/> + update <see cref="_agentIdToIdx"/>.
        /// O(1) zamiast O(n) shift z `RemoveAt(i)`. Caller odpowiada za usunięcie z _agentsByStation/_agentsOnTrain
        /// PRZED tym wywołaniem (state-dependent cleanup).
        /// </summary>
        void RemoveAgentByIdx(int idx)
        {
            int last = _agents.Count - 1;
            int removedId = _agents[idx].agentId;
            if (idx != last)
            {
                var moved = _agents[last];
                _agents[idx] = moved;
                _agentIdToIdx[moved.agentId] = idx;
            }
            _agents.RemoveAt(last);
            _agentIdToIdx.Remove(removedId);
        }
    }
}
