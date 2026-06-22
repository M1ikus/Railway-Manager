# M-TimetableUX F1.0 — Pre-implementation Recon

**Status:** ✅ Done | **Date:** 2026-05-10 | **Output of:** F1.0 sub-task

Read-only verification of 8 critical assumptions o existing codebase. Locks F1.X estimates pre-implementation. Read this przed startem każdego F1.X.

---

## Verification Results

### 1. TimetableCreatorUI auto-discovery (TD-019 root cause)

**Status:** ✅ Confirmed (poprawione vs initial agent report z 2026-05-10)

**Faktyczne miejsce:** `Assets/Scripts/Timetable/UI/TimetableCreatorUI.Routing.cs:119-164` — metoda `FindStationsPerSegment()`.

```csharp
const float maxDistSq = 500f * 500f;  // line 122 — 500m radius
for each station in init.Stations:
    for each path node in segment:
        if (distSq < maxDistSq) → station added to route.stations
```

**Spatial-only**, brak topological check. Stacja w 500m od jakiegokolwiek path node'a w segmencie jest dodawana — nawet jeśli faktycznie jest na bocznej gałęzi (stąd TD-019 "trasa A→B zahacza o stacje na boki").

**Initial agent report błędnie wskazywał `FindTracksForStop` (linia 197) jako TD-019 root cause** — to jest per-stop track lookup (radius 300m dla edges z `railway:track_ref`), nie waypoint auto-discovery. Faktyczny TD-019 fix targetuje `FindStationsPerSegment`.

**F1.6 ghost suggestions refactor scope:**
- Compute pure A→B path (already done w `BuildRoute` linia 17-117)
- W `FindStationsPerSegment`: zamiast spatial radius scan, iterate `init.Stations` i dodać tylko jeśli `station.pathNodeId ∈ allNodeIds` (topological criterion)
- Pozostałe (off-path) → ghost markers (semi-transparent), klik = explicit waypoint add

**F1.6 estymata:** ~2 sesje — bez zmiany.

---

### 2. TimetableService event surface (F1.12 circulation suggestion at-save)

**Status:** 🟡 Partial — `OnTimetablesChanged` exists, brak granularnego event'u z payloadem

**File:line:** `Assets/Scripts/Timetable/Runtime/TimetableService.cs:48-49`

```csharp
public static event Action OnTimetablesChanged;  // line 48 — payload-less
public static event Action OnRoutesChanged;       // line 49
```

Invocations w pliku:
- `AddTimetable` linia 135 (po `Timetables.Add(tt)`)
- (Audit całego pliku przy F1.12 implementacji żeby znaleźć inne invokes — może być w `RemoveTimetable`, `UpdateTimetable`, etc.)

**Gap dla F1.12:** event jest payload-less (`Action` zamiast `Action<Timetable>`). F1.12 potrzebuje wiedzieć WHICH timetable został zapisany żeby uruchomić bounded query "czy łączy z istniejącym obiegiem".

**Decyzja F1.12:** dodać precyzyjny event `public static event Action<Timetable> OnTimetableAdded` invokowany w `AddTimetable` obok generic'a (zachować `OnTimetablesChanged` dla istniejących subscriberów). Subscribe w nowym `Timetable/Runtime/Suggestions/CirculationSuggestionService.cs`.

**F1.12 estymata revision:** ~3-4 sesje → ~3.25-4 sesje (+0.25 sesji za event addition).

---

### 3. CrewCirculation + CrewAssignmentService hooks (F1.13a crew swap)

**Status:** ✅ Personnel module istnieje w pełni — initial agent report ("NOT FOUND") był błędny

**Files (Personnel module exists):**
- `Assets/Scripts/Personnel/Runtime/CrewCirculationService.cs:25` — `static class CrewCirculationService`, `Create(name, EmployeeRole)` linia 76
- Events: `OnCreated`, `OnUpdated`, `OnDeleted`, `OnStatusChanged`, `OnAnyChange` (linie 30-34)
- `Assets/Scripts/Personnel/Runtime/CrewAssignmentService.cs:55` — `MonoBehaviour`, instaluje `TrainRunSimulator.CrewCheckHook = CheckCrewAvailability` w `Awake`
- `Assets/Scripts/Timetable/Runtime/Simulation/TrainRunSimulator.cs:140` — hook signature: `static Func<int trainRunId, string dateIso, bool> CrewCheckHook`
- Feature flag `RequireCrewForCirculation` default OFF (M8 dev mode — pociągi zawsze startują)
- `Assets/Scripts/Personnel/Data/CrewCirculation.cs` — data type z `assignedEmployeeId`
- `Assets/Scripts/Personnel/UI/CrewCirculationEditorUI.cs` + `CrewCirculationListUI.cs` — UI

**F1.13a integration plan:**
- New service `Personnel/Runtime/Suggestions/CrewSwapSuggestionService.cs` (Personnel asmdef bo widzi Timetable)
- Trigger: `TimetableService.OnTimetableAdded` (po F1.12 event addition) — save trigger wystarcza, stop-level event nie jest potrzebny pre-F1.13a
- Query `MeetingEventsService` (F1.14 dependency) dla candidate trains at stopS
- License compatibility check via `EmployeeQualifications` (data exists in `Personnel/Data/`)
- Side effect on Accept: tworzy `CrewSwapEvent` w `CrewCirculationService` linking dwóch turnusów + updates `assignedEmployeeId` w obu

**Open question RESOLVED:** stop-level event `OnStopAdded(Timetable, TimetableStop)` NIE potrzebny dla F1.13a — save trigger wystarcza, suggestions nie muszą firować w live edit (mogą nawet być irytujące).

**F1.13a estymata:** ~3-4 sesje — bez zmiany.

---

### 4. EconomyManager.OnRunDespawned + per-stop events (F1.2 simulator stop handler)

**Status:** ✅ Confirmed + bonus discovery — per-stop events ALREADY EXIST

**Existing events (`Assets/Scripts/Timetable/Runtime/Simulation/TrainRunSimulator.cs`):**
- `:132` — `public static event Action<TrainRun> OnRunSpawned`
- `:144` — `public static event Action<TrainRun> OnRunDespawned`
- `:149` — `public static event Action<TrainRun, int, int> OnTrainArrivingAtStop` ⭐
- `:153` — `public static event Action<TrainRun, int, int> OnTrainDepartingFromStop` ⭐

(Plus `DispatchService.OnDepartureImminent` na `DispatchService.cs:34` dla M9c handshake T-5min.)

**Subscribers `OnRunDespawned`:**
- `Timetable/Runtime/Economy/EconomyManager.cs:92` (revenue/cost tracking)
- `Timetable/Runtime/Economy/PassengerManager.cs:127` (cleanup agents)
- `Timetable/Runtime/Simulation/DepotMapHandshakeService.cs:58` (vehicle return)

**Bonus dla F1.2:** `OnTrainArrivingAtStop` i `OnTrainDepartingFromStop` ALREADY EXIST z signature `(TrainRun, int stopIdx, int trainId)` — F1.2 nie musi dodawać nowych eventów. Refactor wnętrza handler'a per StopType:
- **PH** → boarding (PassengerManager already does this w `HandleTrainArrivingAtStop` — przeniesienie logiki conditional na StopType.PH)
- **PT** → no boarding, dwell only (regulacja ruchu)
- **ZD** → emit nowy `OnCrewSwap` event (Personnel subskrybuje dla crew handover analytics)
- **Transit** → no-op (track tracking only)

**F1.2 estymata revision:** 2-3 sesje → ~2 sesje (events istnieją, scope mniejszy, save -0.5 sesji avg).

---

### 5. VehicleAssignmentModal entry points (F1.15 workflow consolidation)

**Status:** ✅ Confirmed

**Files:**
- `Assets/Scripts/Timetable/UI/VehicleAssignmentModal.cs:93` — `public void Open(Circulation circulation)`
- `Assets/Scripts/Timetable/UI/VehicleAssignmentModal.Save.cs:46-48` — `OnSaveClicked` persists do `_target.vehicleAssignmentsPerDay`

**Workflow modal:**
1. `Open(circulation)` → `RefreshDays/Pool` (drag&drop UI building)
2. Player drags vehicles into circulation step rows (per day)
3. `OnSaveClicked` → walidacja (`CountConflicts`, `ConsistValidator`) → persist `circulation.vehicleAssignmentsPerDay` dict + sync `assignedVehicleIds`

**API surface:** `Open(Circulation)`, `Close()`, internal validation. Persistence in-memory (do object), zewnętrzny `TimetableService.Save()` całość. Brak completion callback aktualnie.

**F1.15 workflow consolidation entry plan:**
- Step 4 (vehicle assignment) w `WorkflowOrchestratorUI` (lub partial w TimetableCreatorUI) wywołuje `VehicleAssignmentModal.Open(circulation)`
- Modal sygnalizuje completion via lokalny callback: dodać `Action<bool> onComplete` w `Open(circulation, Action<bool> onComplete = null)`
- Default current behavior preserved (modal może być wywoływany zewnętrznie bez callback'a — backward compat)

**F1.15 estymata:** ~6-10 sesji — bez zmiany.

---

### 6. StopType enum + consumers (F1.1 backward-compat)

**Status:** ✅ Confirmed

**File:** `Assets/Scripts/Timetable/Data/TimetableEnums.cs:21-30`

```csharp
public enum StopType
{
    Passenger,    // → PH (postój handlowy)
    Technical,    // → PT (postój techniczny)
    PassThrough   // → Transit (przelot)
}
```

**Consumers (8 call sites grep `StopType.`):**
- `ReservationManager.cs` — 3× `if (stop.stopType == StopType.PassThrough) continue;` (skip rezerwacji peronów)
- `TimetableBuilder.cs` — 1× `stopType == StopType.Passenger` (count pass stops)
- `TimetableCreatorUI.Stops.cs` — 2× `stopType == StopType.Passenger`
- `TimetableCreatorUI.Workflow.cs` — 1× `stopType == StopType.Passenger`

**Wszystkie call sites to proste conditional check'i** (nie complex switch). Backward-compat plan trywialny:
- Rename enum values: `Passenger → PH`, `Technical → PT`, `PassThrough → Transit`, dodać `ZD`
- Update 8 call sites z replace_all w grep'u
- Schema bump (F1.11) drop'uje stare save'y, brak migrator chain

**F1.1 estymata:** ~1-2 sesje — bez zmiany.

---

### 7. Platform data integrity (F1.0.5 cleanup scope)

**Status:** ✅ Confirmed (full audit zrobiony 2026-05-10)

**Synthetic call sites (`EnsureSyntheticPlatforms` removal):**
- `Assets/Scripts/Timetable/Runtime/ReservationManager.cs:31` — method definition
- `Assets/Scripts/Timetable/Runtime/TimetableInitializer.cs:368, 490, 558` — 3 call sites

**isSynthetic field usages (`StationPlatform.isSynthetic` removal):**
- `Assets/Scripts/Timetable/Data/StationPlatform.cs:27` — field declaration
- `Assets/Scripts/Timetable/UI/TimetableCreatorUI.Routing.cs:176` (cache build skip)
- `Assets/Scripts/Timetable/UI/TimetableCreatorUI.Routing.cs:435` (`HasPlatformForTrack` skip)
- `Assets/Scripts/Timetable/Runtime/StationTrackData.cs:101` (Generate skip)
- `Assets/Scripts/Timetable/Runtime/GraphDataUnityAdapter.cs:287` (`isSynthetic = false` literal — pre-built nigdy nie ma syntetycznych)

**JSON file:** `Assets/StreamingAssets/TimetableData/station_tracks.json` — exists, real PKP refs (sample: Aleksandrów Kujawski `1, 2, 5, 15`, Andrychów `1, 2, 4`, Anieliny `1, 2`, Antoniówka `1`).

**Diagnostic command target:** `[ContextMenu("M-TimetableUX/F1.0.5: List stations missing platforms")]` w `TimetableInitializer` lub bootstrap script — list stacji gdzie major + 0 platforms in `init.Platforms` + 0 entries in `StationTrackData`.

**FindPlatformsForStation behavior post-cleanup:** zwraca empty list dla stacji bez data. Call sites:
- `ReservationManager.CheckCollisions` — istniejący kontrakt `if (platforms.Count == 0) continue;` (linia 121) już handle'uje ten case → block-section reservation only.
- `ReservationManager.AutoAssignPlatforms` — istniejący kontrakt `if (platforms.Count == 0) skip` (linia 220-228, foreach over empty = no-op) → `platformId` zostaje -1.

**F1.0.5 estymata:** ~0.5-1 sesja — confirmed.

---

### 8. railway:preferred_direction tag prevalence (F1.5)

**Status:** ✅ Tag parsed for UI ranking, gap dla pathfinder weight modifier

**Existing usage:**
- `Assets/Scripts/Timetable/UI/TimetableCreatorUI.Routing.cs:413-417` — `EvaluateTrackPreference` reads `edge.metadata["railway:preferred_direction"]`, compares z `edge.isOsmForward` field
- `Assets/Scripts/Timetable/Runtime/PathfindingGraph.cs:40` — `Edge.isOsmForward` bool field
- Result: `+1` (preferred), `0` (no tag), `-1` (wrong direction) — used dla **track ranking w UI dropdown**

**Gap dla F1.5:** logika istnieje **tylko dla per-stop track ranking**. Pathfinder (`RailwayPathfinder.FindPath`) NIE używa preferred_direction w edge weights → ścieżka A→B może iść "wrong direction" na dwutorowej linii (przeciwnie do preferowanego kierunku) bo ma niższy koszt fizyczny.

**F1.5 implementation:**
- W `RailwayPathfinder.FindPath` (lub `Edge.weight` computation) dodać `directionPenalty` do A* edge weight:
  ```
  if preferredDir == "forward" AND !edge.isOsmForward → weight × 5.0
  if preferredDir == "backward" AND edge.isOsmForward → weight × 5.0
  if preferredDir == null OR "both" → weight × 1.0 (no penalty)
  ```
- Heuristic admissibility preserved (penalty ≥ 1.0 → real cost ≥ heuristic).

**Sample test (TBD przed F1.5 implementation):** zmierzyć w runtime (debug script) jaki % edges ma tag `railway:preferred_direction` w aktualnym poland-v7.bin. Jeśli <50%, F1.5 ma minimalny impact bez formap update (osobny concern).

**F1.5 estymata:** ~1-2 sesje — bez zmiany.

---

## Bottom Line

**Foundation health:** 7/8 punktów ✅ confirmed jako ready, 1/8 🟡 partial (`OnTimetablesChanged` payload-less — F1.12 dodaje granularny event +0.25 sesji).

**Bonus discoveries:**
- Per-stop events `OnTrainArrivingAtStop` + `OnTrainDepartingFromStop` already exist na `TrainRunSimulator.cs:149, 153` → F1.2 estymata zmniejszona z 2-3 do ~2 sesji.
- Personnel module (CrewCirculationService + CrewAssignmentService + CrewCheckHook hook) w pełni operacyjny per M8 ✅ done. Initial agent report "NOT FOUND" był błędny — wszystkie pliki istnieją.
- `railway:preferred_direction` tag już parsowany w UI track ranking (`EvaluateTrackPreference`). F1.5 dodaje tylko pathfinder weight modifier.
- StopType enum consumers to 8 prostych conditional check'ów (nie complex switch) — F1.1 backward-compat trywialny.

**Estimate revisions:**

| Sub-task | Original | Revised | Delta | Reason |
|---|---|---|---|---|
| F1.2 | 2-3 sesje | ~2 sesje | -0.5 avg | Per-stop events exist, mniej work |
| F1.12 | 3-4 sesje | 3.25-4 sesje | +0.25 | Dodać `OnTimetableAdded(Timetable)` granular event |
| Inne | — | — | 0 | Bez zmian |

**Net effect:** **~46.5-70 sesji** (utrzymane, F1.2 saving offset by F1.12 +0.25).

**Open questions blocking F1.X start:** brak. F1.0.5 może startować immediately.

---

## Next Action

**F1.0.5** — Drop synthetic platforms (Phase 0 cleanup). Scope F1.0.5:
1. Remove `ReservationManager.EnsureSyntheticPlatforms` + 3 call sites
2. Remove `StationPlatform.isSynthetic` field + 5 użycia
3. Audit `FindPlatformsForStation` callers (kontrakt empty-list-OK confirmed w pkt 7 powyżej)
4. Diagnostic ContextMenu command
5. Smoke test (load full Polska, count stations missing platforms)

Estymata 0.5-1 sesja.

---

**Recon status:** ✅ Done | **Ready for F1.0.5 start.**
