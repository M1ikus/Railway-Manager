# M10 Multiplayer — plan implementacji (model A2 competitive)

> **Status:** 📋 planowane, nie zaczęte. Ten dokument = **detaliczny plan implementacji** (mapa zmian per plik/klasa, pułapki, testy).
> Decyzje projektowe (model, rozstrzygnięcia 8-11, dołączanie) są ustalone osobno — tu ich nie powtarzamy, tylko realizujemy.
> Powstał z głębokiego reconu kodu 2026-06-17 (9 podsystemów). Wszystkie cytaty plik:linia były aktualne na ten dzień — **zweryfikuj przed edycją**.

---

## 0. Jak czytać ten plan / teza

**Model:** competitive multi-operator (A2) — jeden świat, każdy gracz osobna firma, **host-authoritative**.

Dwa mentalne modele, które trzeba mieć w głowie przy każdej zmianie:

1. **`operatorId` to kręgosłup.** Prawie wszystko, co dziś jest „jedną firmą", dostaje tag `operatorId`. To nie jest opcjonalne — bez tego host nie wie, czyj jest pociąg/pojazd/przychód.
2. **Systemy świata vs systemy firmy.** Część singletonów *chce* zostać jedną instancją (jedna kolej, jedna pula pasażerów) — tagujemy je `operatorId`. Część jest genuinie per-gracz — te robimy per-operator.

**Strategia de-riskowania (najważniejsze):**
> **Etapy 1-6 budujemy i testujemy EditMode w JEDNYM procesie, udając 2+ operatorów (fake `operatorId`). Mirror/sieć dochodzi dopiero w Etapie 7.**
> Czyli: cały model danych, rezerwacje kolizyjne, wspólny rynek, ekonomia per-operator, dotacje proporcjonalne i competitive pasażerowie są w pełni testowalne (Unity CLI batchmode) **zanim** dotkniemy netcode. To eliminuje 80% ryzyka „coś dziwnego" — bo dziwne rzeczy w multi-tenant modelu złapiemy testem, nie debugowaniem desyncu po sieci.

**Determinizm:** pod host-authoritative cross-machine determinizm **NIE jest wymagany** (liczy się sim hosta). RNG jest już seedowany (`RandomRegistry`, MP-9). Iteracja po `Dictionary` w hot-pathach = problem tylko reproducibility save/load (snapshot sort w `TrainRunSimulator.Restore.cs` to już załatwia). To zdejmuje ogromną presję.

---

## 1. Architektura: systemy świata vs systemy firmy

| System | Klasa | Klasa po MP | Uwaga |
|---|---|---|---|
| Ruch pociągów | `TrainRunSimulator` (singleton scenowy) | **ŚWIAT** — jedna instancja, runy tagowane `operatorId`, host-only tick | bloki/perony globalnie kolizyjne |
| Pasażerowie + popyt | `PassengerManager` + `OriginDestinationMatrix` | **ŚWIAT** — jedna pula, przychód tagowany `operatorId` | OD-matrix global (geometria popytu), wybór operatora w boardingu |
| Rezerwacje bloków/peronów | `TimetableService.*Reservations` (`ReservationRegistry<int>`) | **ŚWIAT** — globalny rejestr na hoście + `operatorId` w `Reservation` | aktywacja = transakcja host-walidowana |
| Rynek wtórny | `FleetService._marketVehicles` | **ŚWIAT** — jedna pula, kupno = transakcja | znika dla wszystkich, restock host-side |
| Kondycja/awarie | `BreakdownService`, `DegradationService` | **ŚWIAT** — host odpala na całej sieci | stan `VehicleComponents` = prawda hosta |
| Zegar | `GameClock` | **ŚWIAT** — host dyktuje czas | dziś tyka lokalnie w Update → desync |
| Lokalizacje pojazdów | `VehicleLocationService` | **ŚWIAT** — global + `operatorId` w rekordzie | |
| Saldo/ekonomia | `GameState.Money`, `EconomyManager`, `MoneyLedger` | **FIRMA** — per-operator, host settles | klient nigdy nie mutuje autorytatywnie |
| Reputacja | `ReputationManager` (global+woj+stacja) | **FIRMA** — per-operator (każde z trzech) | `GameState.GlobalReputation` mirror wyłączyć |
| Tabor (własność) | `FleetService._ownedVehicles` | **FIRMA** — per-operator, host = prawda własności | |
| Rozkłady/trasy | `TimetableService.Routes/Timetables` | **FIRMA** — autorstwo per-operator | feedują wspólny rejestr rezerwacji |
| Obiegi | `CirculationService` | **FIRMA** — per-operator | |
| Personel | `PersonnelService` (+ turnusy) | **FIRMA** — per-operator | slice crew-assignment replikowany do hosta |
| Warsztat | `WorkshopManager` | **FIRMA** — per-operator/per-depot, host tyka naprawy | |
| Zajezdnia 3D | scena Depot | **FIRMA** — prywatna; host dostaje tylko podsumowanie zdolności | |

**Zasada integralności (decyzja 2026-06-17):** host egzekwuje WSZYSTKIE reguły (załoga, rezerwacje, kasa, zakupy). Klient = prezentacja + szkice + budowanie zajezdni 3D. Nigdy autorytet nad regułą.

---

## 2. Kręgosłup: `operatorId` — co dostaje tag, gdzie countery per-operator

### 2.1. Typy danych dostające pole `operatorId` (Etap 1)

| Plik | Typ | Pole | Źródło wartości |
|---|---|---|---|
| `Timetable/Data/Timetable.cs` | `Timetable` | `int ownerId` | ustawiane w UI przy tworzeniu (EA: 0) |
| `Timetable/Data/Circulation.cs` | `Circulation` | `int ownerId` | UI przy tworzeniu obiegu |
| `Timetable/Data/TrainRun.cs` | `TrainRun` | `int operatorId = -1` | snapshot z `Circulation.ownerId` / `Timetable.ownerId` w `TrainRunGenerator` |
| `Timetable/.../Simulation/SimulatedTrain.cs` | `SimulatedTrain` | `int operatorId` | kopiowane z `trainRun.operatorId` w konstruktorze |
| `Timetable/.../Simulation/TrainRunSimulator.Restore.cs` | `ActiveRunSnapshot` | `int operatorId = -1` | `BuildActiveSnapshots` |
| `Timetable/.../Economy/PassengerAgent.cs` | `PassengerAgent` (struct) | `int operatorId` | wybór oferty w spawn/boarding |
| `Fleet/Data/FleetVehicleData.cs` | `FleetVehicleData` | `int operatorId = -1` | przy `AddOwnedVehicle`/zakupie |
| `Personnel/.../Employee.cs` | `Employee` | `int operatorId` | przy `Hire` (z `HireTerms`) |
| `Personnel/.../CrewCirculation` | `CrewCirculation` | `int operatorId` | przy `Create` |
| `Timetable/Data/ReservationRegistry.cs` | `Reservation` (struct) | `int operatorId` | przy rezerwacji |
| `Core/VehicleLocationService.cs` | `VehicleLocationRecord` | `int operatorId` (`ownerOperatorId`) | przy set lokalizacji |
| `Maintenance/.../WorkshopManager.cs` | `WorkshopSlot` | `int operatorId` | per-depot (właściciel zajezdni) |
| `Timetable/.../Economy/EconomyManager.cs` | `LineBalance` | `int operatorId` (lub klucz `Dict<op,Dict<circ,LB>>`) | per-obieg per-operator |

**Backward-compat:** `operatorId == -1` lub `0` = legacy single-player → traktować jako host/operator domyślny. Każdy lookup musi mieć fallback `if (operatorId <= 0) return <legacy-behavior>`.

### 2.2. ID-countery → per-operator (Etap 1)

Dziś globalne countery powodowałyby kolizje ID między operatorami. Zmiana na `Dictionary<int operatorId, int>` + API alokacji:

| Plik | Dziś | Po MP |
|---|---|---|
| `TimetableService.cs` | `NextRouteId`, `NextTimetableId`, `NextTrainRunId` (static int) | `AllocateRouteId(operatorId)` itp. (per-operator counter) |
| `CirculationService.cs` | `_nextId` (static int) | `GetNextId(operatorId)` |
| `PersonnelService.cs` | `_nextEmployeeId` (static int) | `GetNextEmployeeId(operatorId)` |

**Decyzja techniczna (otwarta):** ID pojazdów (`FleetVehicleData.id`) — **globalnie unikalne** (centralny alokator), `operatorId` redundantny ale jawny. Rekomendacja: globalnie unikalne, żeby `GetOwnedById(vid)` był jednoznaczny.

### 2.3. Kolekcje per-operator (Etap 1)

`FleetService`, `TimetableService`, `CirculationService`, `PersonnelService` — surowe listy (`OwnedVehicles`, `Routes`, `Circulations`, `Employees`) **nie mogą być zwracane raw** (UI gracza A zobaczyłby dane gracza B). Wzorzec: `GetXForOperator(operatorId)` filtruje. Wewnętrznie albo `Dictionary<operatorId, List<>>`, albo flat lista z filtrem po `operatorId`.

> **Otwarta decyzja architektoniczna:** per-operator **instancje** managerów (`Dict<operatorId, State>` na hoście) vs flat z filtrem. Rekomendacja reconu: hybrid — pojedynczy manager (czyste API), wewnętrznie `Dictionary<operatorId, snapshot>`. Mniej refactoru subscribe-patternów.

---

## 3. Etapy implementacji

> Każdy etap ma **kontrakt semantyczny**: co NIE może zmienić zachowania single-player (regresja = bug). Etapy 1-6 = single-process, EditMode-testowalne. Etapy 7+ = wymaga sieci/PlayMode.

### Etap 1 — Identity (kręgosłup `operatorId` + model danych per-operator)
**Cel:** wprowadzić `operatorId` wszędzie (sekcja 2), per-operator countery i kolekcje, `GameState.OperatorSessions` + `LocalPlayerId`, `PlayerDepotRegistry` zamiast `OwnershipService` stub, save modules per-operator, dokończyć `GameState.Seed` save/load (M13 TODO).
**Kontrakt:** SP gra działa bez zmian (operatorId=0/-1 fallback). Stare save'y ładują się (migracja: wszystko → operator 0).
**Kluczowe zmiany:**
- `Core/GameState.cs`: `LocalPlayerId`, `OperatorSessions: Dictionary<int, PlayerSession>` (money, reputation, homeDepotStationId, companyName, companyColor). `Money`/`GlobalReputation` → helper properties dla `LocalPlayerId`.
- `Depot/Furniture/OwnershipService.cs`: `IsOwnedByLocalPlayer(depotId)` → `PlayerDepotRegistry.GetOwner(depotId) == GameState.LocalPlayerId`. Nowy `PlayerDepotRegistry` (`Dictionary<depotId,operatorId>`).
- Countery + kolekcje per-operator (sekcja 2.2/2.3).
- Save: `WorldSavable` (OperatorSessions zamiast pojedynczych Money/HomeDepot), `TimetableSavable`/`PersonnelSavable`/`CirculationsSavable`/`FleetSavable`/`EconomySavable` — per-operator listy + per-operator countery. `GameState.Seed` w `WorldSavable` + `RandomRegistry.ApplyFromJson`.
**Pułapki:**
- `HomeDepotStationId` single → per-operator (`PlayerSession.HomeDepotStationId`); legacy kod woła `GameState.HomeDepotStationId` → helper `GetHomeDepotStationId(operatorId)`.
- `GameState.Money += x` (np. `MoneyLedger.Flush`) zakłada globalny side-effect → musi być per `LocalPlayerId` + event UI. Zgrepuj wszystkie settery Money.
- Save: `Deserialize` per-operator musi być idempotentny — `ResetForSaveLoad()` per-operator, NIE globalny reset (inaczej gracz B kasuje dane gracza A).
- ID-counter restore: gracz 1 route id=5, gracz 2 po load musi dostać niezależną sekwencję (test poniżej).
**Testy EditMode:**
- `AllocateRouteId(1)` vs `(2)` → niezależne sekwencje, brak kolizji.
- `GetOwnedVehicles(1)` vs `(2)` → rozłączne listy; `AddOwnedVehicle{operatorId:1}` trafia do op1.
- SaveLoad round-trip: 2 operatorów (op1: 3 pojazdy + route id=5; op2: 2 pojazdy + route id=5 — ta sama id OK), load → countery niezależne.
- Backward-compat: stary SP save (Money=50000, HomeDepot=42, brak operatorId) → `LocalPlayerId=1`, `OperatorSessions[1]={money:50000,…}`, gra działa.
- `PlayerDepotRegistry.RegisterOwner(1,2)`; `IsOwnedByLocalPlayer(1)` gdy `LocalPlayerId=2`→true, gdy =1→false.

### Etap 2 — World layer host-authoritative (tagowanie symulacji)
**Cel:** `TrainRunSimulator` i symulacja kondycji jako jedna instancja obsługująca runy wszystkich operatorów; `ReputationManager` per-operator (hookowany z awarii).
**Kontrakt:** ruch, bloki, awarie zachowują się identycznie dla 1 operatora. Brak zmian fizyki (`Advance`).
**Kluczowe zmiany:**
- `SimulatedTrain.operatorId`, `ActiveRunSnapshot.operatorId`, logi z `op#{operatorId}`.
- `TrainRunGenerator`: ustawić `TrainRun.operatorId` (z `Circulation.ownerId` → fallback `Timetable.ownerId`).
- `_blockOccupancy`/`_platformOccupancy` — **bez zmian struktury** (klucze działają cross-operator; blok globalnie zajęty przez którykolwiek pociąg — to poprawne).
- `BreakdownService`/`DegradationService` — host odpala na całej sieci (hook z `TrainRunSimulator.UpdateVisualPosition`); `VehicleComponents` = prawda hosta.
- `ReputationManager.ApplyEvent` → dodać `operatorId`; per-operator `_global`/`_byVoivodeship`/`_byStation`. `Breakdowns.cs` woła z `st.operatorId` zamiast `null`.
**Pułapki:**
- **`PropagateDelayToNextRun` musi filtrować same-operator** (`if nextRun.operatorId != tr.operatorId skip`) — inaczej delay kaskaduje między firmami. ⚠ klasyczne „coś dziwnego".
- `SelfRepairBonusHook`/`SlotSpeedMultiplierHook` — czy liczą skill mechaników **danego** operatora, czy wszystkich? Dziś global → bug pod MP (mechanik gracza A „pomaga" pojazdowi gracza B). Patrz Etap 7 (hooki operator-aware).
- `EconomyManager.AddCost(circulationId, …)` z `Visuals.cs` — per-circulation jest OK (obieg należy do operatora), ale wymaga `Circulation.ownerId` ustawionego.
- Dictionary order w `FixedUpdate` foreach — **zostaw** (pod host-auth OK; snapshot sort w Restore.cs zapewnia save-repro).
**Testy EditMode:**
- `SimulatedTrain` ctor: `operatorId == trainRun.operatorId`.
- Delay cascade: spawn z opóźnieniem → propaguje TYLKO do same-operator next runs.
- Breakdown reputation: `ApplyBreakdown` → `ReputationManager` dostaje poprawny `operatorId`, op2 reputacja niezmieniona.
- Pending restore: save z runami op A/B/C → wszystkie restore z własnym operatorId.

### Etap 3 — Rezerwacje globalne + aktywacja transakcyjna
**Cel:** rejestr rezerwacji globalny + `operatorId`; aktywacja rozkładu = transakcja (accept/reject); konflikt live = priorytet IRJ + FCFS.
**Kontrakt:** dla 1 operatora rezerwacje działają jak dziś (brak fałszywych konfliktów ze sobą).
**Kluczowe zmiany:**
- `ReservationRegistry.cs`: `Reservation.operatorId`; `IsFree(key, start, end, operatorId = -1)` (filtruje — rezerwacje innych = konflikt, własne = OK).
- `TimetableService.ReservePlatform/ReserveBlockSection` + `operatorId` param.
- `ReservationManager.CheckCollisions`/`ReserveForTimetable` + `operatorId`.
- `TimetableCreatorUI.Workflow.cs Confirm()`: pod MP → RPC do hosta (`TimetableActivationRequest`), host waliduje vs wszyscy operatorzy, zwraca accept/reject+conflicts; lokalna rezerwacja TYLKO na accept. (Single-process Etap 3: bezpośrednie wywołanie z `operatorId`, RPC dochodzi w Etapie 7.)
- `BlockConflictDetector.Detect()`: sort `(enter, operatorPriority, delaySec, trainRunId)` zamiast `(enter, trainRunId)`. `operatorPriority` z kategorii IRJ.
**Pułapki:**
- **`ResolveBlockConflicts` dziś modyfikuje `stops[]` w planning-time** (przedłuża postoje). Pod MP to NIE działa — nie zmieniamy rozkładu klienta live. Zamiast tego: host determinuje kolejność w runtime, pociąg czeka w stanie `BlockedBySignal`. Modyfikacja `stops[]` tylko w kreatorze. ⚠ duża zmiana semantyki.
- `trainRunId=0` w `Reserve*` to legacy non-functional — zostaje 0, ale `operatorId` MUSI być aktualny.
- Guard UI: `ConfirmBtn.interactable=false` do odpowiedzi hosta (double-submit race).
- Migracja save M9: rezerwacje bez `operatorId` → `operatorId=0`.
**Testy EditMode:**
- `IsFree(key, …, operatorId)`: ignoruje własne rezerwacje, traktuje cudze jako konflikt.
- `CheckCollisions` z 2 operatorami na tym samym peronie, nakładające czasy → oba dostają CollisionInfo z perspektywy drugiego.
- `BlockConflictDetector`: Międzynarodowy przed Osobowym niezależnie od trainRunId.

### Etap 4 — Wspólny rynek wtórny
**Cel:** jedna pula `_marketVehicles` na hoście; kupno = transakcja walidowana (saldo + dostępność); znika dla wszystkich.
**Kontrakt:** SP — kupno działa jak dziś.
**Kluczowe zmiany:**
- `FleetService._marketVehicles` — global (bez `operatorId`). `RemoveMarketVehicle` → host-only.
- `CartProcessor.TryCheckout`: pod MP → RPC; host waliduje `MoneyLedger.CanAfford(operatorId)`, `ProcessOrder` ustawia `v.operatorId`. Broadcast `OnMarketChanged` do wszystkich.
- `FleetMarketRefreshService` — host-only tick.
- `initial_market.json` — ładowany na hoście (deterministycznie), klient dostaje snapshot.
**Pułapki:**
- Race: dwóch graczy klika ten sam pojazd → host: pierwszy bierze, drugi dostaje „pojazd już niedostępny".
- `_cart` — per-operator (lokalny staging, nie sync).
**Testy EditMode:**
- Po `ProcessOrder` pojazd ma `operatorId`; `RemoveMarketVehicle` usuwa z puli (nie tylko z local view).
- Cross-operator: gracz A nie widzi pojazdów gracza B w Owned, widzi ten sam `_marketVehicles`.

### Etap 5 — Ekonomia + reputacja + dotacje per-operator
**Cel:** saldo/koszty/przychód/dotacje per-operator; dotacje wg modelu **stała pula per relacja ÷ proporcjonalnie do udziału**.
**Kontrakt:** SP bilans dzienny identyczny.
**Kluczowe zmiany:**
- `EconomyManager`: `_lineBalances`/`_history` per-operator (`Dict<op, Dict<circ, LineBalance>>`). `OnDayEnded`: host zbiera ALL operatorów → liczy dotacje → settlement per operator.
- `MoneyLedger`: **nie dotyka `GameState.Money` bezpośrednio** — zwraca akumulowane grosze; Flush (zapis salda) tylko host po rozliczeniu. Client-side = read-only tracking dla UI.
- `SubsidyCalculator`: z `subsidy_per_run * runs` (uncapped) → **stała pula per relacja**: `(1)` `SubsidyPoolPerRoute` [zł/dzień] per linia wojewódzka, `(2)` udział operatora = jego runy / suma runów wszystkich na linii, `(3)` `allocation = pool * share`, `(4)` difficulty multiplier skaluje pulę (wspólny). Anti-money-printer.
- `ReputationManager.GetDemandFactor(operatorId, from, to)` — per-operator; `GameState.GlobalReputation` mirror **wyłączyć**.
- `DemandModifiers` — bez zmian (czysta logika czasu); reputacja per-operator wchodzi przez `GetDemandFactor`.
- `CostCalculator.DailyOverheadGroszy` — per-operator (każda firma płaci swój overhead).
- `EconomySavable` — per-operator (revenue/costs/lineBalances/history/reputation indexed by operatorId).
**Pułapki:**
- Dotacje muszą być **deterministyczne** (czysta matematyka, bez RNG) — by host i ewentualny replay dawały to samo.
- Cap per-operator (opcjonalny, design): czy max 50% puli? → otwarte, M-Balance.
- `OnDayEnded` agregacja `RevenueTodayGroszy` (globalna) → musi być per-operator, nie suma.
**Testy EditMode:**
- `CalculateOperatorSubsidyAllocation(routeId, lineBalances_per_op, pool, difficulties)`: total ≤ pool; 1 operator → 100%; 2 równe → 50/50; udział pro-rata.
- `ReputationManager` per-operator: event na op1 nie rusza op2; `GetDemandFactor` różne per operator.
- `EconomySavable` round-trip 2 operatorów.

### Etap 6 — Pasażerowie competitive (single-operator v1)
**Cel:** pasażer wybiera ofertę JEDNEGO operatora; przychód tagowany; capacity per-operator.
**Kontrakt:** SP — spawn/boarding/revenue jak dziś.
**Kluczowe zmiany:**
- `PassengerAgent.operatorId`; `MaybeSpawnAgents`/`HandleTrainArrivingAtStop` — boarding tylko gdy `run.operatorId == agent.operatorId` (lub agent dziedziczy przy pierwszym boardingu).
- `CalculateCapacity(run)` — liczyć tylko pojazdy `vehicle.operatorId == run.operatorId` (dziś iteruje całe `OwnedVehicles` bez sprawdzenia).
- `PassengerJourneyPlanner.FindJourney(origin, dest, operatorId)` + `JourneyGraphBuilder` — `DirectEdge.operatorId`; planowanie po ofertach jednego operatora (subgraf). Cache key `(origin, dest, operatorId)`.
- `OriginDestinationMatrix` — **global** (geometria popytu), allocation do operatorów w boardingu.
- `EconomyManager.AddRevenue(operatorId, circulationId, fare, source)`.
**Pułapki:**
- Spawn z `operatorId` vs boarding-time: rekomendacja **boarding-time** (agent dziedziczy `run.operatorId` przy pierwszym wsiadaniu) — prostsze, ale cache journey musi być per-operator.
- Capacity: defensywnie sprawdzać `operatorId` nawet jeśli `runningVehicleIds` „powinno" już być poprawne.
- Cross-operator transfers = **post-EA** (decyzja) — w v1 relacja nieobsłużona przez jednego operatora = stracony popyt (okazja dla konkurencji).
**Testy EditMode:**
- Boarding: agent wsiada tylko na `run.operatorId == agent.operatorId`.
- `CalculateCapacity`: liczy z `vehicle.operatorId == run.operatorId`.
- `FindJourney`: szuka w subgrafie operatora.
- Multi-operator conflict: 2 agentów na stacji, run A (op0) i B (op1) w tym samym ticku → boarding separuje per operatorId.

### Etap 7 — Netcode (Mirror + Steamworks + TimeSync + command/replication)
**Cel:** warstwa sieciowa; host-authoritative egzekwowane realnie.
**Wymaga sieci/PlayMode.**
**Kluczowe zmiany:**
- `GameClock.Update`: gate `if (!IsMultiplayer || IsHost) tick`. Klient: `GameClockSync` co ~0.2s real-time (host → `GameTimeSeconds` + resync `_spawnAccumulator`). ⚠ **bez tego symulacja rozjeżdża się w parę sekund.**
- `NetworkGameManager : NetworkManager` (custom), transport **Steamworks P2P** (+ TCP fallback do testów). DontDestroyOnLoad po `LoadScene(Depot)`.
- Command pattern klient→host (akcje walidowane): `ReservationCommand`, `EconomyCommand`, `FleetCommand.PurchaseVehicle`, `TimetableActivationRequest`. Rate limiting (~10 cmd/s/klient), host odrzuca nieznane `operatorId`.
- Replikacja host→klient: `NetworkedTrainRunSimulator` (snapshot per run ~100ms, klient interpoluje, **nie** tickuje `Advance`); `FleetSnapshot` per operator; `EconomyDelta`/`SubsidyAllocation`/`ReputationDelta` (tylko swoje saldo/reputacja); pasażerowie = **agregaty** (kolejka na stacji, load factor), NIE 50k agentów.
- Host-only guards: `TrainRunSimulator`, `PassengerManager`, `BreakdownService`, `ReservationManager.Reserve*`, `FleetMarketRefreshService` — `if (!IsHost) return;`.
- **Crew check host-enforce + hooki operator-aware:**
  - `CrewCheckHook` — opcja B (bez zmiany sygnatury): hook lookupuje `trainRunId → TrainRun → Timetable.operatorId`; sprawdza obsadę TYLKO tego operatora. (Alternatywa A: dodać `operatorId` do sygnatury — rekomendacja: B, bo `CrewAssignmentService` i tak szuka TrainRun.)
  - Replikacja TYLKO slice crew-assignment (`run → employeeIds + kwalifikacje`), nie cały personel.
  - `SelfRepairBonusHook`/`SlotSpeedMultiplierHook`/`PriorityProvider` → wrapper-proxy: hook pobiera `operatorId` z kontekstu (`vehicleId → FleetService.operatorId`, `slotId → WorkshopSlot.operatorId`) i filtruje pracowników danego operatora.
- `WorkshopManager`: `AssignVehicle`/`AdvanceInspections` host-side tick; klient = UI + komenda „zacznij naprawę".
- Handshake: `VersionCheck {buildVersion, minCompatible}` zaraz po connect.
- Pauza: lokalna per gracz (świat żyje) — albo brak pauzy w MP. `PauseStack` dziś global → per-klient UI.
- `operatorId` allocation: deterministyczny po `SteamID` (host restart → ten sam SteamID → ten sam operatorId).
**Pułapki:** patrz cała sekcja gotchas reconu netcode — najważniejsze: GameClock desync, host-only spawn (inaczej konfliktujące `_activeTrains`), ID collision (scoped `operatorId||localId` lub global alokator na hoście).
**Testy:** PlayMode z `LocalNetworkManager` mock; EditMode dla command-walidacji (mock IsHost).

### Etap 8 — Lobby + dołączanie + save MP
**Cel:** dwie ścieżki dołączania + join-in-progress.
**Kluczowe zmiany:**
- `MultiplayerScreenUI`: zamiast `MockServers` → `MirrorLobbyManager` (Steamworks lobby browser). `OnJoinServer` → `StartClient(steamId)`. `OnCreateGame` → GameCreator `Mode=Multiplayer`.
- **Steam quick-join:** TAB → panel znajomych/graczy → „Dołącz" po SteamID + Steam Invites.
- `GameCreatorContext` + `operatorId`; `GameCreatorUI.ApplyServerOnStart` → `NetworkGameManager.Host(serverName, maxPlayers, password)` przed `LoadScene`.
- `SceneController`: klient defer `TimetableInitializer.Initialize` do `GameStateReady` (host streamuje stan świata); host spawnuje world systems natychmiast.
- **Save MP — skład operatorów (decyzja 2026-06-17):**
  - **EA: stały skład.** Wczytanie save'a (N operatorów) = host ładuje **pełny bundle** (wszystkie operator slices + shared world), dołączający gracze **przejmują istniejące sloty** (reclaim po SteamID → `operatorId` z save). **Brak dodawania nowego operatora-od-zera do wczytanego save'a.** Upraszcza Etap 8: **nie potrzeba** `ExtractSharedWorldSliceAsync` + bootstrapu świeżego operatora przy join — to ścieżka post-EA.
  - **Post-EA:** `ExtractSharedWorldSliceAsync()` + bootstrap operatora N+1 od zera (`ExtractOperatorSliceAsync` dla nowego = pusty/startowy slice), reszta zachowuje postępy.
  - **Tylko host zapisuje** pełny bundle (wszyscy operatorzy, jeden plik). HMAC composite, weryfikacja przy join. `SaveManifest` + `operatorId`/`bundleType` (struktura gotowa pod post-EA slicing).
  - **Detal otwarty:** wczytanie z <N obecnych graczy → nieobecni operatorzy **dormant** (świat żyje host-authoritative po zapisanych rozkładach), slot reclaim'owalny gdy gracz dołączy. Rekomendacja: tak.
- Identyfikacja na mapie: `NetworkPlayer.Color` (z lobby), hover „Operator: [kolor] [nazwa]".
- Player identification: kolory firm w lobby.
**Pułapki:**
- `ModuleOrder` zachowany per-operator (trainruns przed personnel, passengers po trainruns).
- Pending-restore: host czeka na graf OSM przed restore `TrainRunSimulator`; klient bez grafu.
- Konflikt rezerwacji przy join: pre-flight check po stronie hosta przed `Deserialize` rozkładów dołączającego.
- `WorldSavable.Money` w MP — pomijać (to robota `EconomySavable` per-operator), inaczej duplikat.
**Testy:** EditMode `ExtractOperatorSliceAsync`/`LoadOperatorSliceAsync` (slice zawiera tylko per-operator moduły, brakujące shared → fallback bez błędu); drop-in persistence; HMAC composite reject modified.

### Etap 9 — Ranking i statystyki live
Ranking: przychody / punktualność / wielkość floty (publiczne). Konkurenci anonimizowani w szczegółach (reputacja: „Operator A: 65", bez drill-down).

### Etap 10 (M10.5) — Polish competitive
Cross-operator transfers (+ podział przychodu z biletu), kontestowane kontrakty dotacyjne (PSC), spectator mode, chat — wszystko **post-EA**.

---

## 4. Pułapki przekrojowe (TOP „coś dziwnego")

1. **`GameClock` lokalny tick** → desync w sekundy. Host-dictated PRZED czymkolwiek innym w Etapie 7.
2. **`PropagateDelayToNextRun` cross-operator** → opóźnienia kaskadują między firmami. Filtr same-operator.
3. **`ReservationRegistry` bez `operatorId`** → dwóch operatorów rezerwuje ten sam tor bez wykrycia konfliktu.
4. **`ResolveBlockConflicts` modyfikuje `stops[]` live** → pod MP nie wolno; runtime `BlockedBySignal` zamiast tego.
5. **Hooki static (`CrewCheck`/`SelfRepairBonus`/`SlotSpeed`/`Priority`)** → muszą być operator-aware przez lookup kontekstu (wrapper-proxy), inaczej zasoby gracza A wpływają na gracza B.
6. **`GameState.Money` mutowane lokalnie** → race w MP; host = jedyne źródło prawdy, klient read-only.
7. **`ReputationManager` global + `GameState.GlobalReputation` mirror** → bez sensu per-operator; wyłączyć mirror.
8. **`SubsidyCalculator` uncapped** → money-printer przy wielu operatorach; stała pula ÷ proporcja.
9. **Surowe listy (`OwnedVehicles`/`Routes`/`Employees`/`Circulations`)** → UI gracza A widzi dane gracza B; zawsze filtr `operatorId`. Konkretnie HUD do przefiltrowania (sekcja 8.2): `TopBarUI` (kasa/reputacja), `MapTrainListUI` (pociągi na mapie), `FinancePanelUI` (per-linia). `DepotMinimapUI` naturalnie OK (zajezdnia prywatna).
10. **Kolizja ID między operatorami** → per-operator countery (Etap 1) lub scoped ID.
11. **`CalculateCapacity` iteruje całe `OwnedVehicles`** → policzy pojazdy konkurenta do capacity; sprawdzać `operatorId`.
12. **Save `ModuleOrder`** (trainruns→personnel, passengers po trainruns) — zachować per-operator, inaczej `CrewDuty` lookup rzuca.

---

## 5. Determinizm, backward-compat, performance

- **Determinizm:** cross-machine NIE wymagany (host-auth). RNG seed **wspólny** (host→klienci ten sam `Seed`), żeby replay/debug był identyczny. `RandomRegistry` per-system seed = `Seed XOR systemId`. Dictionary order = tylko save-repro (snapshot sort już jest).
- **Backward-compat:** stare SP save'y → `operatorId=0`, `LocalPlayerId=1`, `OperatorSessions[1]`. Migracja rezerwacji `operatorId=0`. Każdy lookup `if (operatorId <= 0) → legacy`.
- **Performance cap:** rekomendacja **15k pasażerów/operator** (60k w grze 4-os.), nie 50k×4. Do potwierdzenia stress-testem (`PerfStressBootstrap`, M-Performance).
- **Bandwidth:** pasażerowie jako agregaty (nie agenci); pozycje pociągów ~20Hz + interpolacja; ekonomia/reputacja = delty tylko swojego operatora.

---

## 6. Otwarte decyzje (do podjęcia przy implementacji / w M-Balance)

| # | Decyzja | Rekomendacja | Kiedy |
|---|---|---|---|
| D1 | Miara proporcji dotacji (liczba pociągów / pociągokm / przychód) | pociągokm | M-Balance |
| D2 | Cap dotacji per-operator (max % puli) | brak capa v1, sprawdzić testem | M-Balance |
| D3 | Per-operator instancje managerów vs flat+filtr | hybrid (single manager, `Dict<op,state>`) | Etap 1 |
| D4 | `CrewCheckHook` sygnatura (lookup vs param `operatorId`) | B (lookup, bez zmiany sygnatury) | Etap 7 |
| D5 | `Employee.operatorId` single vs lista (transfery) | single w v1 | Etap 1 |
| D6 | Depot per-operator: 1 czy wiele | 1 home depot/operator w v1, multi post-EA | Etap 1 |
| D7 | Cap pasażerów per-operator | 15k | Etap 6/7 (stress test) |
| D8 | Cross-operator transfers + podział przychodu | post-EA | M10.5 |
| D9 | Pauza w MP | brak (świat zawsze żyje) lub soft-pause UI | Etap 7 |
| D10 | Backport capu dotacji do SP (spójność) | sprawdzić jak SP liczy dziś | Etap 5 |
| D11 | Persystencja kamery w save (pozycja/zoom przy load) | nie — default po load OK; rozważyć pod screenshot/replay | post-EA |

---

## 7. Strategia testów

- **Etapy 1-6:** EditMode NUnit w `Assets/Tests/EditMode/` (asmdef `RailwayManager.Tests.EditMode`), uruchamiane samodzielnie przez Unity CLI batchmode (`D:\Unity\6000.4.0f1\Editor\Unity.exe`, sprawdź lockfile). Cele testów wymienione per-etap wyżej. To jest główny mechanizm „nie wyjdzie coś dziwnego" — multi-tenant model walidowany przed siecią.
- **Etap 7-8:** PlayMode + `LocalNetworkManager` mock (host+klient w jednym procesie); EditMode dla logiki command-walidacji (mock `IsHost`).
- **Cross-cutting:** SaveLoad round-trip 2 operatorów; stress test 4 operatorów × 15k pasażerów (`PerfStressBootstrap`).
- **Semantic preservation:** każdy etap ma test regresji SP (1 operator zachowuje się jak przed MP).

---

## 8. Warstwa prezentacji: kamera, HUD, minimapa (per-operator) — recon 2026-06-17

### 8.1. Kamera przy zmianach scen (zweryfikowane na kodzie)

**SP — kamera JEST poprawnie zachowywana przy zmianach scen. Nie gubi się, nie miesza, nie resetuje.**
- Obie sceny (Depot + MapScene) żyją **jednocześnie** (additive load, `SceneController.cs:9`). Przełączanie Depot↔Map = wyłącznie toggle `cam.enabled` (`SceneController.Cameras.cs:81-101`) — sceny się **NIE wyładowują**.
- `Start()` kamer odpala się **raz** (przy load sceny), więc pola (`currentYaw/pitch/distance/pivotPoint` w `DepotOrbitCamera`, `targetZoom`/transform w map `CameraController`) **przetrwają toggle** → powrót do Depot pokazuje ostatnią pozycję/kąt/zoom.
- Dwie kamery są czysto rozdzielone (orbit 3D Depot vs ortho 2D Map) — **brak mieszania**.
- ⚠ **Korekta reconu:** automatyczny recon zgłosił „bug — reset do default przy switchu" — to była **błędna inferencja** (zobaczył `Start()` ustawiający default, bez sprawdzenia że sceny są additive-persistent i `Start()` nie re-runuje). Zweryfikowane czytaniem `SceneController` + `.Cameras` → resetu przy switchu **nie ma**.
- **Jedyny realny reset:** save→load / pełny reload sceny (świeży `Start()` → default). Brak `cameraState` w save bundle. Patrz decyzja D11 — niekrytyczne.

**MP — kamera czysto client-local, NIGDY nie sieciowana.** `DepotOrbitCamera`/`CameraController` nie czytają/piszą stanu globalnego (input → lokalne pola). Każdy gracz steruje swoją kamerą niezależnie — przy multi nic się nie zmienia względem SP.
- **Zasada (twarda):** **nigdy** nie trzymać stanu kamery w `static` ani w shared-world state. Stan kamery = lokalne pole MonoBehaviour, ewentualnie client-local/save bundle przy persystencji — **nie replikować** do innych operatorów. (Gdyby ktoś dodał `static LastCameraPosition` „dla quick-save", przeciekłoby między scenami/sesją.) Spectate (jeśli kiedyś) = sync OBIEKTU gry (pociąg/budynek), nie transformu kamery.

### 8.2. HUD / minimapa — filtrowanie per-operator (TU jest robota MP)

Konkretna instancja pułapki „surowe listy" (sekcja 4 #9). 4 komponenty czytają dziś globalny stan i w MP pokazałyby dane konkurenta:

| Komponent | Plik | Czyta dziś | MP — wymóg |
|---|---|---|---|
| `TopBarUI` (kasa, reputacja) | `SharedUI/TopBarUI.cs:61,315` | `GameState.Money`, `GameState.GlobalReputation` (globalne) | per-operator (saldo/reputacja `LocalPlayerId`) |
| `MapTrainListUI` (lista pociągów) | `Map/UI/MapTrainListUI.cs:96-108` | wszystkie `_activeTrains` | filtr `TrainRun.operatorId == LocalPlayerId` (konkurenci: ukryci/anonimizowani wg decyzji visibility) |
| `FinancePanelUI` (bilans + per-linia) | `Timetable/.../FinancePanelUI.cs:130` | wszystkie `EconomyManager.LineBalances` | filtr per `operatorId` |
| `DepotMinimapUI` (markery pociągów) | `Depot/UI/DepotMinimapUI.cs:330-347` | `FindObjectsByType<ConsistMarker>` (scena) | **naturalnie OK** — zajezdnia to prywatna scena klienta (tylko własne obiekty); filtr tylko gdyby zajezdnia była współdzielona |

- **Minimapa zajezdni naturalnie zakresowana:** zajezdnia jest prywatna per-gracz (osobna scena klienta) → `FindObjectsByType` widzi tylko własne obiekty. OK bez filtra.
- **Wyświetlenia na WSPÓLNEJ mapie + HUD ekonomii** (lista pociągów, markery na mapie 2D, panel finansów, top bar) → **wymagają filtra per-operator**, bo źródła (`_activeTrains`, `LineBalances`, `GameState.Money`) to world/global.
- Filtry są **testowalne EditMode** (mock 2 operatorów → widok pokazuje tylko `LocalPlayerId`) — robione w Etapach 1-6 razem z modelem danych, nie czekają na netcode.
