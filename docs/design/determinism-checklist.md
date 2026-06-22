# Determinism checklist — M-Performance MP-9

> **Cel:** Lista miejsc w kodzie symulacji gdzie nondeterminism może powodować desync między klientami (M10 MP) lub regresję semantic preservation w M-Performance refactorach.
> **Status:** 🟡 in progress (MP-9 done 2026-05-06, audit kontynuowany przed M10).

---

## Architektura — RandomRegistry + DeterministicRng

Wszystkie systemy gameplay'owe używające randomness przeszły na **per-system seedowane RNG**:

```csharp
using RailwayManager.Core;

// W static class lub instance class:
static readonly DeterministicRng s_rng = RandomRegistry.GetRng("MyService");

// Użycie kompatybilne z UnityEngine.Random:
if (s_rng.Value < probability) { ... }
int idx = s_rng.Range(0, list.Count);
float val = s_rng.Range(0f, 1f);
```

**Mechanizm:** każdy `systemId` dostaje seed `GameState.Seed XOR systemId.GetHashCode()` — niezależne substream'y, zmiana RNG w jednym systemie nie wpływa na inne.

**Reset:** `RandomRegistry.ResetAll()` przy load save'a (TODO: integracja z GameStateSavable w M13).

---

## Audit RNG (10 plików, 18 wystąpień)

### ✅ Refactored (hot path / gameplay-critical, MP-9 2026-05-06)

| Plik | Wystąpienia | systemId | Notes |
|---|---|---|---|
| `BreakdownService.Roll` | 1 | `BreakdownService` | **Najgorętsze**: ~55k rolls/s przy 1000 trains × 11 components × 1Hz |
| `PassengerManager.MaybeSpawnAgents` + `SpawnAgent` | 5 | `PassengerManager` | Spawn loop + per-agent state init |
| `TrainRunSimulator.Breakdowns` | 2 | `TrainRunSimulator.Breakdowns` | Self-repair delay + success roll |
| `SickLeaveService.ApplyDailyTick` | 2 | `SickLeaveService` | Daily L4 chance + days roll |
| `RetirementService.RollChance` | 1 | `RetirementService` | Retirement probability |
| `DispatcherService.TryAutoAssign` | 2 | `DispatcherService` | Critical miss + delay range |

**Total refactored:** 13 wystąpień w 6 plikach, ~99% hot path coverage.

### ⏸️ Niezrefaktorowane — udokumentowane jako low-priority

| Plik | Wystąpienia | Powód odroczenia |
|---|---|---|
| `FleetService.SeedDevFleet` | 2 | Dev seeding na startupie. W release game fleet idzie z save'a (`GameStateSavable`), nie wywoływane w MP cycle. **Zostawiamy** — refactor gdy real GameCreator dispatch będzie używał. |
| `PerfStressBootstrap` | 3 | Debug stress test, ma własny `Random.InitState(seed)`. **Zostawiamy** — debug only, deterministic w obrębie testu. |

**Total deferred:** 5 wystąpień w 2 plikach. Refactor "on-demand" gdy któryś z nich wejdzie w gameplay path.

**Refactored 2026-05-15:** `GroundGenerator.GenerateCityBlock` (6 wystąpień Random.Range na pozycje/scale/rotation background buildings) zmigrowane na **hardcoded fixed seed per block name** (`new System.Random(blockSeed)`, gdzie blockSeed = explicit switch dla "Block_North"/"West"/"East" → 1001/1002/1003). User decision: tło zajezdni ma być IDENTYCZNE między grami niezależnie od `GameState.Seed` — nie variant per seed. Side effect: MP host/klient dostają to samo tło bo seed jest hardcoded, nie zależy od `GameState.Seed`. Wcześniej UnityEngine.Random.Range bez seed dawał inne tło co run (user raportował jako bug).

---

## Dictionary / IEnumerable ordering audit

W .NET `Dictionary<TKey, TValue>` enumeration order **NIE jest gwarantowany**. Identyczny set kluczy może dać inną kolejność na różnych klientach (różne CLR / runtime version) — co prowadzi do desync gdy iteracja ma side effects.

### Krytyczne miejsca w hot path

| Plik | Iteracja | Wpływ na semantic | Status |
|---|---|---|---|
| `TrainRunSimulator.FixedUpdate` `foreach kvp in _activeTrains` | per train Advance | Block reservation order zmienia delays → finanse | 🟡 **TODO M10 MP prep** — sortuj po trainRunId przed iter (~1000 trains × O(n log n) raz per FixedUpdate, OK) |
| `PassengerManager._agents` List | swap-and-pop after MP-3 | Agent order deterministic (insertion + swap-and-pop), OK | ✅ deterministic |
| `PassengerManager._agentsByStation` HashSet | foreach agentIds | Snapshot do `_agentBuffer` per call, kolejność matters tylko jeśli capacity exhaust mid-iter — wantedToBoard count może się różnić | 🟡 **TODO M10 MP prep** — sort by agentId przed snapshot lub use SortedSet |
| `PassengerManager._agentsOnTrain` HashSet | analogiczne | Alight: kto znika pierwszy nie matter (wszyscy idą Arrived) — OK | ✅ logicznie deterministic |
| `EconomyManager._lineBalances` Dict | foreach in `OnDayEnded` snapshot | Snapshot do `linesSnapshot` + per-element AddSubsidy. Order wpływa na float associativity sum | 🟡 **TODO** — sort by circulationId |
| `FleetService.OwnedVehicles` List | per CalculateCapacity, ComputeConsistScale | List ma insertion order, deterministic | ✅ deterministic |

### Plan dla M10 MP prep

Pre-M10 trzeba refactor:
1. `TrainRunSimulator.FixedUpdate` — `foreach kvp in _activeTrains.OrderBy(kvp => kvp.Key)` lub `_sortedActiveTrainIds: List<int>` aktualizowane przy Spawn/Despawn.
2. `PassengerManager` HashSet iteracje — snapshot do bufora po sortowaniu.
3. `EconomyManager.OnDayEnded` snapshot — sort linesSnapshot by circulationId.

**Trade-off:** sortowanie per-FixedUpdate to O(n log n) overhead. Z 1000 trains = ~10k ops/sortuj × 50Hz = 500k ops/s — OK budget. Akceptowalne.

**Status MP-9:** **NIE refactorujemy w M-Performance scope.** To jest M10 MP prep — wprowadzimy sortowanie tylko gdy faktycznie wchodzi infra MP, żeby nie płacić performance hit dla nic w SP.

---

## FixedUpdate consistency

Wszystkie symulacje wpięte w `FixedUpdate` (50Hz, fixed delta) — NIE `Update` z `deltaTime` (zmienne klatki):

| System | Loop | Status |
|---|---|---|
| `PassengerManager` | `FixedUpdate` ✓ | ✅ |
| `TrainRunSimulator` | `FixedUpdate` ✓ | ✅ |
| `DepotMovementSimulator` | `FixedUpdate` ✓ | ✅ |
| `EmployeeWalkSimulator` | `FixedUpdate` ✓ | ✅ |
| `EconomyManager` | event-driven (`OnDayEnded`, `OnRunDespawned`) | ✅ |

`Time.fixedDeltaTime * GameState.TimeScale` jako delta game time — deterministic w obrębie sesji, semantic preservation gwarantowana.

---

## Float ordering

| Sumacja | Order | Float associativity safe? |
|---|---|---|
| `CalculateCapacity` sum `passengerSeats` per `OwnedVehicles` | List insertion order (deterministic) | ✅ |
| `ComputeConsistScale` sum `lengthM` | List insertion order | ✅ |
| `RefreshActiveStations` build `_offersOnPairCache` | Foreach `TrainRuns` (List) + nested foreach stops (List) | ✅ deterministic |
| `EconomyManager.AddRevenue` accumulator | sequential events | ✅ |

Brak sumacji po Dictionary/HashSet enumerations w hot path — float ordering OK.

---

## Co dalej (post-MP-9)

1. ~~**Save/load Seed integration**~~ ✅ **DONE 2026-05-06** — `WorldSavable` (`Assets/Scripts/SaveLoad/Modules/WorldSavable.cs`) serializuje `GameState.Seed`, deserialize czyta z fallback `?? 0` dla starych saves + wywołuje `RandomRegistry.ResetAll()` (musi być przed innymi modulami żeby per-system substream'y re-init z save'owym seed). `InitializeDefault` także resetuje. Schema v1 zostaje (backward-compatible, fallback pattern).
2. **GameCreator UI dla seeda** — opcja "random / user-chosen seed" przy New Game. Default `Environment.TickCount` lub `0`.
3. **24h symulacja determinism test** — uruchom 2× stress test z tym samym seedem, porównaj `EconomyManager.DailyBalance` bit-exact. Wymaga PerfStressBootstrap refactor żeby ustawiał `GameState.Seed` zamiast `UnityEngine.Random.InitState`.
4. **Dictionary ordering refactor** — przed M10 MP, nie wcześniej.
5. **FleetService refactor** — gdy GameCreator dispatch przez FleetService.SeedDevFleet w MP cycle.

---

## Zależne dokumenty

- Decision log: [performance-decision-log.md](performance-decision-log.md)
