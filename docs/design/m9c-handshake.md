# M9c — Handshake Map ↔ Depot

> **Status:** Plan (pre-implementation), 2026-04-18
> **Zależności:** M9a ✅ (TrainRunSimulator), M9b ✅ (DepotMovementSimulator), M5 ✅ (Circulations)
> **Cel:** Połączyć symulator mapy 2D (OSM) z symulatorem zajezdni 3D w jeden ciągły cykl życia pojazdu.

---

## 1. Kontekst i filozofia

Pojazd w Railway Manager ma **swoje życie**. Nie jest to tylko "pocisk który spawn się w godzinie X i despawn w godzinie Y" — pojazd ma stałą pozycję w świecie gry:

- Stoi na torach w depocie (po zakupie, serwisowaniu, między obiegami)
- Stoi na peronie stacji (po zakończeniu kursu, czeka na następny krok obiegu)
- Jest w trasie (wykonuje TrainRun)
- Jest na pasie dostawczym (świeżo kupiony, przed dostarczeniem)

Pojazd zmienia lokalizację tylko przez **zdefiniowane tranzycje** (exit z depot, arrival na stację, entry do depot, itd.). Nie ma teleportacji. Jeśli obieg wymaga żeby pojazd startował na stacji X, to pojazd MUSI tam być fizycznie przed czasem odjazdu — inaczej kurs jest cancelled/delayed.

### 1.1 Home depot

Gracz wybiera lokalizację depot na początku gry (GameCreator scene). Tej lokalizacji odpowiada **jedna stacja kolejowa** na mapie OSM. Ta stacja = `GameState.HomeDepotStationId`.

**Konwencja M9c:** wszystkie obiegi gracza zaczynają się i kończą swój cykl dzienny w home depocie (startowa stacja pierwszego rozkładu = `HomeDepotStationId`). M5 Circulations w walidacji sprawdza tę regułę.

Wyjątek: obieg wielokrokowy może mieć kroki zaczynające się gdziekolwiek — ale pojazd musi już tam być (poprzedni krok tam skończył).

---

## 2. Rozstrzygnięte decyzje designowe

| ID | Pytanie | Decyzja |
|---|---|---|
| **D1** | Home station | = stacja gdzie gracz postawił depot (`GameState.HomeDepotStationId`). Walidacja w M5: pierwszy krok obiegu MUSI startować z home. |
| **D2** | Authority dla state'u pojazdu | `VehicleLocationService` w Core (cross-scene, singleton, DontDestroyOnLoad). |
| **D3** | Auto vs manual dispatch z depot | **Manual + notification.** 5min przed odjazdem: UI popup "Pociąg X powinien wyjechać na rozkład nr Y". Gracz sam klika "Wyjedź". W przyszłości (M8 Personel) AI dyżurny ruchu będzie to robił. **Brak daily reset** żadnego stanu. |
| **D4** | Co po kursie | Pojazd ma niezależne pozycje (mapa + depot). Jeśli `endStation == HomeDepotStationId` AND brak kolejnego kroku → entry do depot. W przeciwnym razie → `AtStation(endStation)`, widoczny na mapie, czeka na kolejny kurs albo idle. |
| **D5** | Cross-scene visuals | Każdy simulator zarządza własnymi wizualizacjami. MapScene pokazuje pojazdy na mapie (w trasie + idle na stacjach). DepotScene pokazuje pojazdy w zajezdni. Scena aktywna = render aktywny; ConsistLifecycleService trzyma authoritative state. |

---

## 3. Architektura

### 3.1 Nowe typy

#### `Core/Services/VehicleLocationService.cs` (nowy)

Singleton, authority dla lokalizacji każdego pojazdu.

```csharp
public enum VehicleLocationType
{
    InDepot,           // pojazd fizycznie w zajezdni (na torze parkingowym/manewrowym/etc.)
    ExitingDepot,      // jedzie do bramy (DepotMovementSimulator active)
    AtStation,         // stoi na torze stacyjnym (mapa 2D), peron
    OnRoute,           // w trasie (TrainRunSimulator active)
    EnteringDepot,     // wjeżdża przez bramę (DepotMovementSimulator active)
    InTransit,         // edge case — między stacjami bez aktywnego TrainRun (np. relokacja)
}

public class VehicleLocationRecord
{
    public int vehicleId;
    public VehicleLocationType type;
    public int stationId = -1;         // valid dla AtStation, InDepot (= HomeDepotStationId)
    public int depotTrackId = -1;      // valid dla InDepot — który tor w zajezdni
    public int currentTrainRunId = -1; // valid dla OnRoute, Exiting/EnteringDepot (jeśli związane z konkretnym kursem)
    public int currentConsistId = -1;  // valid dla Exiting/EnteringDepot/InDepot-Moving (tymczasowy ID z M9b)
    public Vector2 worldMapPosition;   // pozycja 2D, update'owana przez TrainRunSim gdy OnRoute
}

public class VehicleLocationService : MonoBehaviour
{
    public static VehicleLocationService Instance;

    // ── Queries ─────────────────────────────────────────────
    public VehicleLocationRecord Get(int vehicleId);
    public IReadOnlyList<VehicleLocationRecord> GetAtStation(int stationId);
    public IReadOnlyList<VehicleLocationRecord> GetInDepot();
    public VehicleLocationRecord GetByConsist(int consistId); // returns one record; consist może mieć wiele vehicles

    // ── Transitions (wywoływane przez simulatory) ───────────
    public void SetInDepot(int vehicleId, int depotTrackId);
    public void SetExitingDepot(int vehicleId, int consistId, int trainRunId);
    public void SetOnRoute(int vehicleId, int trainRunId, Vector2 pos);
    public void UpdateRoutePosition(int vehicleId, Vector2 pos); // per-tick update z TrainRunSim
    public void SetAtStation(int vehicleId, int stationId);
    public void SetEnteringDepot(int vehicleId, int consistId);

    // ── Events ──────────────────────────────────────────────
    public event Action<int vehicleId, VehicleLocationType oldState, VehicleLocationType newState> OnLocationChanged;
}
```

#### `Core/Services/DispatchService.cs` (nowy)

Watchdog nad obiegami — co tick sprawdza czy jakiś pojazd ma wkrótce odjazd i emituje notyfikację.

```csharp
public class DispatchService : MonoBehaviour
{
    public static DispatchService Instance;

    /// <summary>Ile sekund przed odjazdem emitować notification (domyślnie 5 min = 300s).</summary>
    public float NotificationLeadTimeSec = 300f;

    public event Action<TrainRun, List<int> vehicleIds> OnDepartureImminent;
    public event Action<TrainRun> OnDepartureReady; // exact time — ostrzeżenie że TrainRun zaraz startuje

    // Called per tick (FixedUpdate)
    void Check();
    // Znajduje TrainRun'y dla dziś, gdzie (departure - now) ∈ [0, leadTime]
    // i których pojazdy są jeszcze InDepot.
    // Emituje OnDepartureImminent raz per run (deduplikacja).
}
```

### 3.2 Modyfikacje istniejących klas

#### `Fleet/Data/FleetVehicleData.cs`
- **Bez zmian** — pozycja trzymana w `VehicleLocationService` (nie w FleetVehicleData), żeby nie mutować danych sklepowych/konfiguracji. Service serializuje się w save/load niezależnie.

#### `Core/GameState.cs`
- Nowe pole: `public static int HomeDepotStationId = -1;`
- Set'owane w GameCreator przy wyborze depot (przez gracza).
- Persyst w save/load.

#### `Timetable/Runtime/Simulation/TrainRunSimulator.cs`
- Nowe eventy:
  - `public event Action<TrainRun> OnRunSpawned` (po udanym spawn)
  - `public event Action<TrainRun> OnRunDespawned` (przy `DespawnTrain`)
- Nowa metoda:
  - `public bool SpawnTrainFromVehicles(TrainRun run, List<int> vehicleIds, Vector2 worldPos)`
    — wariant publiczny do wywołania przez handshake (nie czeka na `ShouldStart`, spawn natychmiastowy; używa podanej pozycji zamiast routowej startowej).
- Modyfikacja `CheckForNewTrains`:
  - Nie auto-spawnuje pociągu którego pojazdy są w stanie `InDepot` lub `ExitingDepot` (handshake musi najpierw wyprowadzić je z depot). Spawnuje tylko gdy wszystkie pojazdy są `AtStation(run.startStationId)` albo już handshake wywołał `SpawnTrainFromVehicles`.
- Per-tick update pozycji → `VehicleLocationService.UpdateRoutePosition(vehicleId, pos)`.

#### `Timetable/Data/TrainRun.cs`
- Nowe pole: `public List<int> runningVehicleIds = new();` — wypełniany przy spawn, użyty przy despawn do notyfikacji service'u.

#### `Depot/Movement/DepotMovementSimulator.cs`
- Eventy istnieją (M9b Etap 5) — subskrybowane teraz przez `VehicleLocationService`:
  - `OnConsistExitedDepot(consistId)` → service pobiera vehicleIds z ConsistMarker, set'uje je na `OnRoute` + woła `TrainRunSimulator.SpawnTrainFromVehicles`
  - `OnConsistEnteredDepot(consistId)` → service set'uje vehicleIds na `InDepot`
- Rozszerzenie `SpawnConsistAtEntry`: zwraca `consistId` (nowy lub reused), żeby service mógł zmapować.

#### `Timetable/Runtime/CirculationService.cs`
- Dodać `ValidateCirculationHomeStation(Circulation c)` — sprawdzenie że pierwszy rozkład startuje z `HomeDepotStationId`. Wywoływane przy `SetStatus(Active)`.
- Dodać `GetRunsForDate(string dateIso, int circulationId)` — convenience accessor, potrzebne do DispatchService.

### 3.3 Bootstrap

Service'y żyją na dedykowanym GO `GameServices` z `DontDestroyOnLoad`. Tworzony w `MainMenu → GameCreator → GameScene` transition, przed załadowaniem Depot/Map scen. Przechowywany w `Core` assembly, accessible przez static `Instance`.

---

## 4. Flow scenariuszy

### 4.1 Scenariusz A: Start dnia — pojazd w depot → kurs → powrót

```
T-∞         vehicle#101 InDepot (track#5, home_stationId=olsztyn_glowny)
T-∞         circulation#7: steps=[run#123 (Olsztyn→Ełk @08:00), run#124 (Ełk→Olsztyn @14:00)]
            dla jutra (2026-03-16) assignment: run#123={101,102}, run#124={101,102}

T=07:55    DispatchService.Check() → wykrywa run#123 start za 5min, pojazdy InDepot
           → OnDepartureImminent(run#123, [101,102])
           → UI: "Pociąg do Ełku (TLK 38103) wyjeżdża z depot o 08:00. Wyślij go teraz!"

T=07:55..  Gracz klika przycisk "Wyjedź" na consist'cie w depocie (albo znajduje go sam w 3D)
           → DepotMovementSimulator.EnqueueExit(consistId=3, vehicleIds=[101,102])
           → state: InDepot → ExitingDepot
           → DepotMovementSimulator animuje: manewruje do toru zewnętrznego, jedzie do bramy

T=07:58    Consist przekracza granicę depot
           → DepotMovementSimulator.OnConsistExitedDepot(3)
           → VehicleLocationService: vehicleIds=[101,102] wszystkie on OnRoute
           → TrainRunSimulator.SpawnTrainFromVehicles(run#123, [101,102], olsztyn_stacja.worldPos)
           → pociąg spawnuje na mapie 2D, czeka na start time

T=08:00    TrainRun startuje (zgodnie z rozkładem)
           → jeśli spawned later than 08:00 → run.currentDelaySec += late_by

T=08:00..11:30  Pociąg jedzie po bloku sygnałowym, update pozycji
                → VehicleLocationService.UpdateRoutePosition per tick

T=11:30    Pociąg dociera do Ełku (endStation run#123)
           → TrainRunSimulator.DespawnTrain, OnRunDespawned(run#123)
           → VehicleLocationService.HandleRunCompleted
             → check: kolejny krok dla [101,102]? TAK (run#124 @14:00)
             → endStation=Ełk ≠ HomeDepotStationId (Olsztyn) → AtStation(Ełk)
             → pojazdy widoczne na peronie Ełku (mała ikona)

T=13:55    DispatchService.Check → run#124 za 5min, pojazdy AtStation(Ełk)
           → AtStation jest "dobry" state, auto-spawn przy T=14:00 bez handshake

T=14:00    TrainRunSimulator.CheckForNewTrains → spawn run#124 z pozycji Ełku (pojazdy tam są)
           → vehicleIds na OnRoute

T=17:30    Pociąg dociera do Olsztyna (endStation run#124)
           → OnRunDespawned
           → VehicleLocationService.HandleRunCompleted
             → kolejny krok? NIE
             → endStation=Olsztyn = HomeDepotStationId → trigger entry
             → DepotMovementSimulator.SpawnConsistAtEntry(consistId=nowy, vehicleIds=[101,102])
             → pojazdy state: OnRoute → EnteringDepot

T=17:32    Consist zatrzymuje się przy bramie depot
           → OnConsistEnteredDepot → state: EnteringDepot → InDepot (track = jeszcze brak, na pieńku bramy)
           → gracz kieruje na parking (M9b normalnym flow)
```

### 4.2 Scenariusz B: Pojazd nie w home depot na start dnia (edge)

Pojazd został na stacji X (nie home) po wczorajszym ostatnim kursie (bo obieg nie kończył się w home). Dziś ma kurs zaczynający się w X — OK, spawn normalny. Ale jeśli dzisiaj obieg wymaga startu w home (Olsztyn) a pojazd jest w X → **run.isCancelled = true** + notification "Brak pojazdu dla kursu #38103".

To odpowiedzialność playera żeby obiegi były spójne (circulation validator w M5 powinien to wykryć przy aktywacji obiegu).

### 4.3 Scenariusz C: Świeżo kupiony pojazd

Pojazd kupiony w Fleet menu → `VehicleLocationService.SetInDepot(vehicleId, -1)` (jeszcze nie przypisany do toru — "dostarczony, stoi gdzieś"). Gracz musi go rozmieścić (M9b ma już SpawnConsistAtEntry — można użyć jako mechanizm "dostawy").

Alternatywa (prosta): pojazd pojawia się `AtStation(HomeDepotStationId)` — czyli na peronie obok depot, skąd gracz klika i wsyła do środka. Do przedyskutowania.

### 4.4 Scenariusz D: Gracz w innej scenie w czasie handshake

Gracz ogląda Depot, a pociąg właśnie dociera do Ełku (nie-home). `TrainRunSimulator` mimo tego symuluje — DespawnTrain, OnRunDespawned. `VehicleLocationService.HandleRunCompleted` → set AtStation(Ełk). **Brak akcji wizualnej** (gracz nie widzi mapy). Gdy przełączy scenę — pojazd jest tam, MapScene renderuje go na peronie.

Odwrotnie: gracz w Map, consist opuszcza depot. DepotMovementSimulator w Depot scene jednak symuluje (obie sceny żyją jednocześnie — sprawdzone w SceneController). OnConsistExitedDepot odpala handshake → TrainRunSim spawn na mapie → widoczny w aktywnej MapScene od razu.

---

## 5. Podetapy implementacji

### M9c-1 — Foundation (~2 sesje)

**Cel:** szkielet service'ów, state tracking, eventy — bez wizualizacji idle pojazdów.

- [ ] `GameState.HomeDepotStationId` + setter w GameCreator
- [ ] `VehicleLocationService` (enum + record + dictionaries + queries + transitions)
- [ ] `TrainRun.runningVehicleIds` field
- [ ] `TrainRunSimulator.OnRunSpawned / OnRunDespawned` eventy
- [ ] `TrainRunSimulator.SpawnTrainFromVehicles` public method
- [ ] `DepotMovementSimulator.SpawnConsistAtEntry` — rozszerzyć o zwracanie consistId
- [ ] Bootstrap `GameServices` GO w `Core` z DontDestroyOnLoad
- [ ] Smoke test: ContextMenu methods — SetInDepot, SetAtStation, query z UI panelu

**Exit criteria:** można skryptowo ustawić lokalizację pojazdu, event przechodzi, query zwraca poprawne dane.

### M9c-2 — Depot → Map (~2-3 sesje)

**Cel:** pełny flow wyjazdu z depot na mapę.

- [ ] `DispatchService.OnDepartureImminent` event + watchdog per tick
- [ ] `TrainNotificationUI` — corner popup "Pociąg X wyjeżdża o HH:MM. [Wyślij teraz]"
- [ ] `ConsistPopupUI` extension — pokaż "Kurs #X odjazd za N min" gdy consist przypisany do nadchodzącego runu
- [ ] `DepotMovementSimulator.OnConsistExitedDepot` handler w `VehicleLocationService`:
  - Pobierz vehicleIds z ConsistMarker
  - Znajdź matching TrainRun (imminent, dla tych vehicleIds, dziś)
  - `SetOnRoute(vehicleIds, trainRunId, worldPos=home_station.position)`
  - `TrainRunSimulator.SpawnTrainFromVehicles(run, vehicleIds, pos)`
- [ ] Edge case: spawn po departure time → run.currentDelaySec = (now - departureTime)
- [ ] Edge case: brak matching run (consist wyjechał "samowolnie") → flag vehicleIds jako `InTransit`, log warn

**Exit criteria:** zasłużenie testowy obieg — click "Wyjedź" w depocie → consist opuszcza depot → spawn na mapie → jedzie kurs. Wszystko w jednej rozgrywce, bez reloadu.

### M9c-3 — Map → Depot (~2 sesje)

**Cel:** powrót z kursu do home depot.

- [ ] `TrainRunSimulator.OnRunDespawned` handler w `VehicleLocationService`:
  - Dla każdego vehicleId z run.runningVehicleIds
  - Sprawdź czy run.endStationId == HomeDepotStationId AND brak kolejnego kroku dla tego pojazdu dziś
  - **TAK:** `SetEnteringDepot(vehicleId, consistId)` + `DepotMovementSimulator.SpawnConsistAtEntry(consistId, vehicleIds)`
  - **NIE:** `SetAtStation(vehicleId, endStationId)`
- [ ] `DepotMovementSimulator.OnConsistEnteredDepot` handler:
  - Set vehicleIds na `InDepot`
- [ ] Test pełnego cyklu: exit → travel → arrival → entry → parking. Wszystkie notyfikacje.

**Exit criteria:** 1-run obieg wraca do depot kompletnie. Consist pojawia się przy bramie, gracz kieruje na parking.

### M9c-4 — Multi-step + inne kursy (~1-2 sesje)

**Cel:** obieg z kilkoma krokami dziennie (Olsztyn→Ełk→Olsztyn).

- [ ] `CirculationService.GetNextStepForVehicle(vehicleId, afterTimeSec)` — convenience
- [ ] Handler OnRunDespawned: jeśli kolejny krok → `SetAtStation(endStation)`, czeka na `CheckForNewTrains` które auto-spawnuje z tej pozycji
- [ ] Walidacja w `CirculationService.SetStatus(Active)`: pierwszy krok startuje z home station, multi-step chain jest consistent (step[i+1].startStation == step[i].endStation)
- [ ] Test obiegu Olsztyn→Ełk→Olsztyn w jednym dniu

**Exit criteria:** obieg z 2+ krokami działa, pojazd nie wraca do depot między krokami.

### M9c-5 — UI + polish (~1-2 sesje)

**Cel:** widoczność + edge cases.

- [x] **FleetPanelUI.MyFleet** — kolumna "Zadanie" pokazuje aktualny state z `VehicleLocationService`
      (W zajezdni / Wyjazd / W trasie #kurs / Stacja #id / Wjazd / Między stacjami)
- [x] Pojazdy idle widoczne na mapie 2D (mała ikona przy stacji z liczbą pojazdów — IdleVehicleVisualizer)
- [ ] `ConsistPopupUI` — pokaż przypisany circulationId + next run info
- [ ] Scene switch rehydration test — wizualizacje poprawnie pokazane po zmianie scen
- [ ] Delay propagation test: consist zaspał w depocie, run.currentDelaySec update'owany + propagowany na kolejne
- [ ] Run cancellation test: pojazd w złym miejscu, run.isCancelled, log + notification

**Exit criteria:** gracz widzi co się dzieje — w zajezdni (popup depot), na mapie (ikony idle), w fleet UI (location panel), w notyfikacjach.

---

## 6. Pytania pozostawione na później

- **Świeżo kupiony pojazd** (Scenariusz C): czy wjeżdża przez bramę jak w handshake, czy spawnuje prosto w depot (ContextMenu-style)? Propozycja: wjeżdża przez bramę (realistic delivery), ale można skonfigurować auto-park.
- **Pojazdy "stored"** (na magazynie, nie-aktywne): nowy state `Stored` poza scope M9c? Lub wystarczy `InDepot` bez przypisania do circulation?
- **Wypadki / awarie w trasie:** pojazd utknął między stacjami → state `InTransit` bez aktywnego run. Ratownictwo, cofanie, serwisu. Post-M9c (M7 maintenance?).
- **AI dyżurny ruchu** (M8 Personel): automatyzuje manual dispatch. Post-M9c.

---

## 7. Testy ręczne (smoke tests)

Każdy etap kończy się manualnym testem przy Unity Play mode:

1. **M9c-1:** Debug ContextMenu na `VehicleLocationService` → SetInDepot / SetAtStation / dump state do konsoli. Sprawdź eventy.
2. **M9c-2:** Circulation z 1 runem dziś. Ustaw `GameState.TimeScale = 100` żeby szybko dojść do T-5min. Expected: notification popup → gracz klika "Wyślij" → flow handshake.
3. **M9c-3:** Poczekaj aż pociąg dotrze do Olsztyna (home). Expected: entry do depot, pociąg stoi przy bramie.
4. **M9c-4:** Circulation Olsztyn→Ełk→Olsztyn. Expected: między krokami pojazd na peronie Ełku, nie wraca do depot.
5. **M9c-5:** Przełączaj sceny w różnych momentach cyklu, sprawdź consistency visuals.
