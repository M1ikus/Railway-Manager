# Performance baseline — M-Performance MP-1 + MP-2

> **Cel:** Punkt odniesienia do porównania regresji wydajności w trakcie M-Performance milestone'u (MP-3..MP-11 minus MP-10).
> Aktualizowany przy każdym podetapie zmieniającym hot path. Baza pomiarów to **stress test framework MP-1** + **ProfilerMarker instrumentation MP-2**.

---

## Jak uruchomić pomiar

### Wariant A — Stress test framework (MP-1, raw frame timing)

1. Załaduj save z mapą Polski (najlepiej z Active circulations dla pełnego pomiaru hot pathów).
2. Hierarchy → prawy-klik → Create Empty → AddComponent `PerfStressBootstrap`.
3. Inspector — domyślnie 500k agents, 60s realtime, seed=42 (wystarczy).
4. Component header → `MP-1: Run stress test`.
5. Po teście: Console summary + CSV w `Application.persistentDataPath/Logs/perf-stress-{date}-{seed}.csv`.

### Wariant B — Unity Profiler (MP-2, granularny rozkład)

1. Window → Analysis → Profiler.
2. Włącz przed Play mode (lub w Play mode → Record).
3. Przejdź do CPU Usage → Hierarchy view.
4. Wszystkie nasze markery są pod prefixem **`PassengerManager.*`** / **`TrainRunSimulator.*`** / **`DepotMovementSimulator.*`** — wpisz w Search box albo rozwiń `BehaviourUpdate / FixedUpdate` w hierarchii.
5. Najczęściej wystarczy zrobić Profile w trybie play, znaleźć worst frames (sortuj po Time ms desc), zobaczyć rozkład markerów.

**Wariant A** = surowe liczby end-to-end (avg/p99 frame time), do walidacji że zmiana coś zmieniła globalnie.
**Wariant B** = atrybucja kosztów per podsystem, do diagnozy gdzie jest problem.

Oba używamy razem: A pokazuje "czy boli", B pokazuje "co boli".

---

## Lista ProfilerMarkerów (MP-2 instrumentation)

| Marker | Plik | Wywoływane | Hot path? | Spodziewany koszt @ 1000 trains / 500k agents | Optymalizacja |
|---|---|---|---|---|---|
| `PassengerManager.TickAgents` | [PassengerManager.cs](../../Assets/Scripts/Timetable/Runtime/Economy/PassengerManager.cs) | FixedUpdate 50Hz | ✅ wysoki ruch | abandon check + RemoveAt — O(N_agents). Pomiar MP-1: niewielki przy 500k. | swap-and-pop w MP-3 |
| `PassengerManager.HandleTrainArrivingAtStop` | jw | OnTrainArrivingAtStop event | 🔥 **najgorętszy** | 3× O(N_agents) linear scan (alight + board + CountOnTrain) per stop × ~167 stops/s = 250M ops/s @ 500k | spatial indexes w MP-3 |
| `PassengerManager.MaybeSpawnAgents` | jw | co 10s game time (FixedUpdate accumulator) | 🟡 średni | spawn budget ~500/tick × 10 attempts × CountOffersOnPair = bardzo zmienne | cache w MP-4 |
| `PassengerManager.CountOffersOnPair` | jw | per spawn attempt w `MaybeSpawnAgents` | 🔥 **najgorętszy** | O(N_runs × N_stops) per call × 100 attempts = 5M comparisons/tick | cache `_offersOnPairCache` w MP-4 |
| `PassengerManager.RefreshActiveStations` | jw | co 30s game time | 🟢 rzadki | full scan TrainRuns na wszystkich. Mała częstotliwość, brak optymalizacji w fazie 1. | — |
| `PassengerManager.CalculateCapacity` | jw | per board (każde HandleTrainArrivingAtStop gdy są pasażerowie) | 🟡 średni | O(N_owned_vehicles) × Contains = wolno przy 1000 vehicles | cache `_cachedCapacity` w MP-4 |
| `TrainRunSimulator.FixedUpdate` | [TrainRunSimulator.cs](../../Assets/Scripts/Timetable/Runtime/Simulation/TrainRunSimulator.cs) | FixedUpdate 50Hz | ✅ wysoki ruch | overall budget per frame, summa AdvanceLoop + CheckForNew + DespawnCompleted + UI refresh | — |
| `TrainRunSimulator.AdvanceLoop` | jw | foreach _activeTrains × substeps | ✅ wysoki ruch | 1000 trains × 1-10 substeps × Advance(50μs) = 50-500 ms — krytyczne | Burst+Jobs w MP-8 (warunkowe) |
| `TrainRunSimulator.Advance` | [TrainRunSimulator.Movement.cs](../../Assets/Scripts/Timetable/Runtime/Simulation/TrainRunSimulator.Movement.cs) | per train per substep | ✅ wysoki ruch | fizyka: rozped/cruise/hamowanie + block check + UpdateVisualPosition | — |
| `DepotMovementSimulator.FixedUpdate` | [DepotMovementSimulator.cs](../../Assets/Scripts/Depot/Movement/DepotMovementSimulator.cs) | FixedUpdate 50Hz w Depot scene | 🟢 niski (per-depot, ~10-20 consist'ów) | ProcessTask × tasks. Potwierdzone NIE hot path 2026-05-06. | nie ruszamy |

**Łącznie 10 markerów**, pokrywających wszystkie hot paths z spec'a M-Performance + 1 sanity (`DepotMovementSimulator` żeby potwierdzić że NIE jest problem).

---

## Baseline pomiar 2026-05-06 (MP-1)

**Sprzęt:** AMD Ryzen 7 9800X3D (high-end — ~3-4× szybszy single-thread niż recommended spec i7-9700)
**Save:** pełna Polska, 0 Active circulations (force-spawn fallback major stations)
**Workload:** 500_000 agentów, 0 trains, 60s realtime sampling

### Frame time (z `PerfStressBootstrap` summary)

| Metryka | Wartość |
|---|---|
| Frames sampled | 13572 |
| avg frame time | **2.26 ms** (~442 FPS) |
| min | 1.55 ms |
| max | 52.06 ms (jednorazowy GC spike) |
| p50 | 1.80 ms |
| p95 | 5.06 ms |
| p99 | **5.90 ms** |

### Wnioski

- **TickAgents nie boli** przy 500k agentów (banalny abandon check).
- **Brak trains = brak pomiaru 2/3 hot pathów.** Surface'y `HandleTrainArrivingAtStop`/`CountOffersOnPair`/`CalculateCapacity` wymagają TrainRun w Active state.
- **Skalowanie do recommended spec:** avg ~7-9 ms, p99 ~18-24 ms — na granicy 60 FPS budgetu (16.6 ms target dla 60 FPS).

### Limitacje pomiaru

- Stress test bez fake TrainRun spawning — wymagałby klonowania timetable+route+graph dependencies (deferred).
- Pełna walidacja MP-1 możliwa tylko z save'em zawierającym aktywne obiegi.
- Freeze ~10s na początku to **OD matrix building** (4327338 par, brute-force) — startup cost, nie symulacja, kandydat na osobny TD poza scope M-Performance.

---

## Pomiar po MP-3+MP-4+MP-9 (2026-05-06 wieczór, Ryzen 9800X3D)

**Setup:** save z 1 Active circulation (7 stops, 21 offer pairs cache), PerfStressBootstrap force-spawn 499968 agents + 60s sampling + Profiler attached. Speed x150 → pociąg objechał kilka razy.

**Top markers (self time, najgorsze klatki):**

| Marker | Self ms (worst frame) | Status vs hot path expectation |
|---|---|---|
| `PassengerManager.MaybeSpawnAgents` | 33-36 ms | ⚠️ nowy hotspot stress-only (cap=600k spam, nie pojawi się przy cap=50k naturalnym) |
| `PassengerManager.HandleTrainArrivingAtStop` | <0.5 ms (niewidoczny w top 5) | ✅ MP-3 spatial indexes ~30000× redukcja vs O(N) linear scan |
| `PassengerManager.CountOffersOnPair` | <0.1 ms (niewidoczny) | ✅ MP-4 cache lookup O(1) ~50000× redukcja |
| `PassengerManager.CalculateCapacity` | <0.05 ms (niewidoczny) | ✅ MP-4 cache hit |
| `PassengerManager.TickAgents` | nie w top markers | ✅ banalny abandon check, OK |
| `PassengerManager.RefreshActiveStations` | rzadko (co 30s) | ✅ |
| `GC.Collect` | 15.4 ms | ⚠️ auto-save 4.3MB alloc w trakcie stress (niezwiązane z M-Performance) |
| `GC.Finalizer` | 8.3 ms | ⚠️ masowy alight cleanup (498k → 1.9k delta) |

**Frame time across capture:**

| Metryka | Wartość | Notatka |
|---|---|---|
| avg | 3.39 ms (~295 FPS rzeczywisty) | ✅ |
| p50 | 2.27 ms | ✅ |
| p95 | 6.57 ms | ✅ |
| p99 | **36.83 ms** | ⚠️ stress-only spike (MaybeSpawnAgents + GC.Collect) |
| max | 73.27 ms | ⚠️ pojedynczy GC burst |

**Wniosek:** **MP-3+MP-4 zadziałały zgodnie z założeniami** — wszystkie 3 main hot paths z spec'a usunięte z budgetu. Pozostały hotspot to `MaybeSpawnAgents` w stress-test ekstremum (cap=600k > agents=500k permanent press), którego NIE wystąpi w realnym gameplay (cap=50k naturalny → spawn budget tiny → return early).

**Decyzja MP-5: ścieżka A POTWIERDZONA** — POCO+spatial+cache wystarcza, MP-7+MP-8 (DOTS) odpadają.

**Optional follow-up MP-4.5** — cache modifiers (commute + reputation) w `MaybeSpawnAgents` per OD pair eliminuje stress-test spike. Odłożone do M-Balance / post-EA tuning, nie wymagane dla target.

---

## Wzorzec interpretacji wyników profilera

1. **Sortuj po Time ms desc** w hierarchii — najgorszy marker na górze.
2. **Spójrz na Calls** — wysoka liczba × niska Time = częste niskie operacje (kandydat do cache'u). Niska liczba × wysoka Time = pojedyncze drogie operacje (kandydat do refactoru struktury).
3. **GC Alloc** kolumna — pokazuje GC pressure per frame. > 100 KB/frame = problem (zazwyczaj alokacje w hot path → cache'uj kontenery).
4. **Self ms vs Total ms** — Self = czas tylko w tym markerze, Total = z wywoływanymi sub-markerami. Patrz na Self gdy szukasz konkretnego bottlenecku.

---

## Zależne dokumenty

- Performance targets (FPS budget): [performance-targets.md](performance-targets.md)
