# Core — przewodnik

> Dokumentacja modułu `Assets/Scripts/Core/`.

## Asmdef + warstwa

**`RailwayManager.Core`** (rootNamespace: `RailwayManager.Core`).
Najniższa warstwa — **bez zależności wewnętrznych**. Refs tylko `Unity.InputSystem`.

Wszystkie inne assemblies referują Core. Konsekwencja: **nigdy nie dodawaj
referencji w drugą stronę** (Core nie może widzieć Fleet/Depot/Map/itd.).
Złamanie = cykl, gra się nie skompiluje.

**2026-05-13 housekeeping:** wszystkie pliki w Core są w namespace
`RailwayManager.Core` (5 plików — GameState/GameClock/SceneController/
VehicleLocationService/UIIntents — przeniesione z bare `RailwayManager`).

## Co tu siedzi

Czysty „kernel" gry — to co musi być dostępne wszędzie:

| Plik | Po co |
|---|---|
| `Log.cs` | `Log.Info/Warn/Error/Debug` — wrapper na UnityEngine.Debug. `Debug` ma `[Conditional("DEBUG_LOG_VERBOSE")]` → 0 cost w release. **Konwencja prefix `[ClassName]`** w message. |
| `GameState.cs` | Static container — czas gry, TimeScale, Money, GameDay, HomeDepotStationId, Seed, ActiveDlcCountries, GlobalReputation. **Money i GlobalReputation są properties** z eventami `OnMoneyChanged`/`OnGlobalReputationChanged` (2026-05-13). **`IsHomeDepotSet` helper** zamiast magicznego `-1`. **Statyczne pola** żyją cross-scene. **Polski-aware:** zawiera DLC API (Add/Remove/IsCountryActive) z protection na ostatni kraj. |
| `GameClock.cs` | Singleton MonoBehaviour, `DontDestroyOnLoad`. Tick `Update` 60Hz inkrementuje `GameState.GameTimeSeconds * TimeScale`, day rollover emituje `OnDayEnded`. Wcześniej siedział w `TopBarUI.Update` (do 2026-05-10) — wyciągnięty żeby tikał bez SharedUI. **2026-05-13: while-loop z capem 30/tick** (multi-day rollover gdy lag spike × x500 — nie traci OnDayEnded). |
| `SceneController.cs` | Switch Depot ↔ MapScene (additive load), camera/UI/EventSystem toggling, lighting save/restore. Static `partial class` rozbity 2026-05-13 na 4 partials: main (flow + state) + `.Lighting.cs` + `.Cameras.cs` + `.Layers.cs`. **Owner** `TimetablePopupOpen` (input gating) i `LastEscConsumedFrame` (popup ↔ PauseMenu race). |
| `VehicleLocationService.cs` | M9c handshake — single source of truth gdzie jest każdy pojazd (InDepot/AtStation/OnRoute/Exiting/Entering/InTransit). Singleton `DontDestroyOnLoad`. **Mutowany przez simulatory** (TrainRunSimulator/DepotMovementSimulator/Fleet), czytany przez UI. **2026-05-13: per-type index** (`GetByType/GetInDepot/GetOnRoute` zero-alokacji, internal `Dictionary<VehicleLocationType, List<>>` maintained w Emit/GetOrCreate). |
| `UIIntents.cs` | Event-driven cross-asmdef intent bus (`UIIntent` enum + `UIIntents.Emit`/`OnIntent`). Zastąpił 5 `Pending*` flag w SceneController (2026-05-10). Używaj dla cross-scene panel open/close. |
| `RandomRegistry.cs` | MP-9 deterministic RNG per system (`GetRng("BreakdownService")`). Seed = `GameState.Seed ^ stableHash(systemId)`. Snapshot do save/load (`ToJson/ApplyFromJson`). **Krytyczne dla M10 MP determinizm.** Adapter API kompat. z `UnityEngine.Random.Range`. **2026-05-13: `Range(int,int)` z rejection sampling** (uniform distribution, brak modulo bias). |
| `IsoTime.cs` | Parsing dat ISO (`yyyy-MM-dd`), `DayOfWeek`. Używane przy `CurrentDateIso` w `GameState`. |
| `AppPaths.cs` | Centralna prawda o ścieżkach plików (2026-05-13). Eliminuje hardcoded `Application.persistentDataPath` / `streamingAssetsPath` z 17+ plików. Properties: `PersistentRoot/StreamingRoot`, `SavesDir/LogsDir/CustomFurnitureDir/CustomSchemasDir`, `FleetCatalogDir/EconomyCatalogDir/TimetableDataDir/BuiltinSchemasDir/BuiltinFurnitureCatalogPath/MapsDir/PolandMapsDir`. `EnsureCreated()` na bootstrap. |
| `Bootstrap.cs` | Centralny orchestrator init Core services (2026-05-13). 2 fazy: `EarlyInit` (`BeforeSceneLoad`) — AppPaths + SettingsService + emit `OnEarlyInit`; `LateInit` (`AfterSceneLoad`) — GameClock + VehicleLocationService + emit `OnLateInit`. Wyższe asmdef mogą subscribe'ować zamiast utrzymywać własne `[RuntimeInitializeOnLoadMethod]`. Flagi `EarlyInitFired`/`LateInitFired` dla late subscribers. |
| `PauseStack.cs` | Stack-based pause ownership (2026-05-13). Eliminuje bug-pattern „zamknięcie popup'a odpauzuje świat z aktywnym alertem". `Push(owner)`/`Pop(owner)` (idempotent), `Clear()` na save-load/new-game/MainMenu exit. `GameState.IsPaused` = `_legacyPause OR PauseStack.HasOwners`. Migracja write-callerów (TopBar/GameCreator) na Push/Pop = opt-in. |
| `SaveSlotSummary.cs` | DTO podsumowania save slot (do SaveMenuUI). |
| `SaveActionsHook.cs` | Provider pattern dla cross-asmdef save/load (Core ↔ SaveLoad bez cyclic dep). **2026-05-13: `ISaveActionsProvider` interface + atomic `Register/Unregister`** — wcześniej 12 publicznych mutowalnych delegatów (każdy mógł je nullować silently). Public properties z private setterami — callsites nadal `SaveActionsHook.QuickSave?.Invoke()`. AutoSaveService implementuje `ISaveActionsProvider`. |
| `Settings/`, `Difficulty/`, `GameRules/` | 3 sub-systemy: settings PlayerPrefs + audio/video/i18n (M13), DifficultyPreset (Easy/Normal/Hard/Realistic + Custom), GameRulesService (toggle features per ruleset). **2026-05-13: SettingsService Load/Save uproszczony** przez helper `GetBool/SetBool/GetEnum<T>/SetEnum<T>` zamiast `? 1 : 0` boilerplate. |
| `InputActions.cs` | Wygenerowany z `InputSystem_Actions.inputactions`. **Nie edytuj ręcznie** — Unity regeneruje. |
| `UndoSettings.cs` | Lekki config dla M-DepotTools undo stack. |

## Cross-system glue (co Core eksportuje)

- **`Bootstrap.OnEarlyInit/OnLateInit`** events (2026-05-13) → opt-in subscribe dla SaveLoad/SharedUI/gameplay zamiast własne `[RuntimeInitializeOnLoadMethod]`
- **`GameState.OnDayEnded`** event → subskrybują `EconomyManager`, `SubsidyCalculator`, `ReputationManager`
- **`GameState.OnMoneyChanged`** event (2026-05-13) → TopBarUI bilans, NotificationService kasa-low alert
- **`GameState.OnGlobalReputationChanged`** event (2026-05-13) → TopBarUI pasek, NotificationService spadek
- **`VehicleLocationService.OnLocationChanged`** → handshake Map↔Depot (DispatchService, UI notifications)
- **`UIIntents.OnIntent`** → cross-asmdef panel coordination (FinancePanelUI, WorkshopsPanelUI, PartsPanelUI, etc.)
- **`RandomRegistry.GetRng(id)`** → BreakdownService, PassengerManager spawn, TrainRunSimulator breakdowns
- **`SaveActionsHook.Register(ISaveActionsProvider)`** (2026-05-13) → AutoSaveService atomic install. Callsite'y konsumentów (PauseMenuUI/GameCreator/LoadGameScreenUI) nadal `SaveActionsHook.QuickSave?.Invoke()`.

## Gotchas

- **`GameState.HomeDepotStationId = -1`** = gra przed GameCreator completion. Preferuj `GameState.IsHomeDepotSet` zamiast ręcznego porównania (jednolity kontrakt zamiast sentinel rozrzucony po 6+ plikach).
- **`GameState.Money / GlobalReputation` to properties** (od 2026-05-13) — `+=/-=` działa identycznie, ale setter emituje event. Jeśli mutujesz w wielu miejscach jednej operacji, scal je inaczej `OnXxxChanged` poleci wielokrotnie.
- **`DepotTimeScale = Min(TimeScale, 5f)`** — w zajezdni 3D nie wolno przekraczać x5 (decyzja M9 design). Czytaj z tego property, nie z `TimeScale` bezpośrednio.
- **`VehicleLocationService.ResetAll()`** — wywołać przy „Nowa gra" / save load. Singleton `DontDestroyOnLoad` utrzymuje state cross-session inaczej (BUG-039). Resetuje też per-type index.
- **`VehicleLocationService.GetByType/...`** zwraca **referencję do internal listy** (zero-alokacji). Nie mutuj zwróconej kolekcji, nie wywołuj `Set*` w trakcie iteracji — invalidate'uje enumerator. Wzorzec: `foreach + use record + done`.
- **`RandomRegistry` NIE używać dla UI flavor losowości** — to deterministic gameplay RNG. UI losowość → `UnityEngine.Random` OK.
- **`SceneController.Reset()`** zatrzymuje uciekające coroutines (BUG-027) — gdy dodajesz nowy coroutine w SceneController, dorzuć go do scope `StopAllCoroutines` flow. **Partial split (2026-05-13):** jeśli dodajesz nowe państwo statyczne, zdecyduj który partial (main/Lighting/Cameras/Layers).
- **`GameClock` zostaje na `Update` (60Hz)**, nie `FixedUpdate` (50Hz). To wall-clock zegar, simulatory same czytają migawki z `GameState.GameTimeSeconds`. Multi-day rollover safety (2026-05-13): while-loop z capem 30/tick — przy ekstremalnym lag spike × x500 nie traci dni.
- **`ActiveDlcCountries` jest read-only view** + dedicated mutator API (BUG-024). Nigdy nie odwołuj się do prywatnego list'a, używaj `AddDlcCountry`/`RemoveDlcCountry`/`ResetDlcCountries`.
- **`SaveActionsHook` ma private settery** — jedyna droga install to `Register(ISaveActionsProvider)`. Wcześniej każdy mógł `SaveActionsHook.QuickSave = null` i wywalić system.
- **`PauseStack.Push/Pop` zamiast `GameState.IsPaused = true/false`** (od 2026-05-13) — Push/Pop daje explicit ownership, eliminuje popup ↔ alert conflict. Legacy setter zachowany (kompat), ale nowe popupy/alerty MUSZĄ używać Push/Pop z unikalnym `owner` string'em.
- **`Bootstrap.OnEarlyInit/OnLateInit` vs `[RuntimeInitializeOnLoadMethod]`** — subskrybenci muszą się zarejestrować PRZED Bootstrap'em (`static MyService() { Bootstrap.OnLateInit += ...; }`). Late subscribery powinny sprawdzić `Bootstrap.LateInitFired` i wywołać manualnie jeśli już po fakcie.

## Powiązane docs

- `docs/design/balance-constants.md` — gdzie żyją Core-level constants
- `docs/design/data-formats.md` — IsoTime format, save format
