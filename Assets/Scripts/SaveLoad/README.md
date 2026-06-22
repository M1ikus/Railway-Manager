# SaveLoad — przewodnik

> Dokumentacja modułu `Assets/Scripts/SaveLoad/`.

## Asmdef + warstwa

**`RailwayManager.SaveLoad`** (rootNamespace: `RailwayManager.SaveLoad`),
refs: `Core, SharedUI, Fleet, Timetable, Personnel, Depot` (+ InputSystem).

**Najwyższa warstwa hierarchii asmdef** — referuje WSZYSTKO niższe, **nikt nie
referuje SaveLoad**. Konsekwencja: moduły niższe nie mogą importować `SaveOrchestrator`
ani `ISavable<T>` z Personnel/Timetable/etc. Wzorzec inversed:
**niższe moduły implementują `ISavable` z lokalnym DTO**, SaveLoad reflection-discovers
przez `SaveRegistry`.

M13-6 zamknięte 2026-04-26. Format: **JSON + gzip + HMAC**, schemaVersion + migrator chain.

## Schema versioning (zasady, 2026-05-15)

**Pre-EA:** wszystkie moduły mają `SchemaVersion = 1`. Reset został zrobiony 2026-05-15
po audycie — wcześniej system bumpował SchemaVersion przy każdej zmianie schema (DepotSavable
doszedł do v6, PersonnelSavable do v5), ale **wszystkie migratory były identity**
(`Migrate(input) => input`). Cała backward compat polegała na fallbackach `?? default`
w `Deserialize`. Bumpowanie bez real migracji = teatr → usunięte 12 identity migratorów.

**Strategia pre-EA:**
- Dodajesz/usuwasz pole? Daj `?? default` w `Deserialize`. Nie ruszaj `SchemaVersion`.
- Stary save z >v1 (sprzed reset'u) ładuje się przez direct deserialize (orchestrator
  wykrywa sourceVersion > current=1 i próbuje, fallback'i obsługują braki).
- Real semantic break (np. enum reorder, jednostki) → akceptujemy że stare save'y nie
  zadziałają (decyzja user'a 2026-05-08: "save'y są nieznaczące").

**Strategia post-EA:**
- Każdy bump `SchemaVersion` MUSI mieć **real `IMigrator`** (nie identity).
- Identity migrator ⇔ nic nie bumpować (zostaw fallback w Deserialize).
- Real migrator powinien mieć test smoke który: (1) tworzy save w starej wersji,
  (2) ładuje nową, (3) sprawdza że migration data jest poprawne (nie zerowy default).
- `MigrationRunner` mechanizm zachowany — gotowy gdy będzie potrzebny.

## Co tu siedzi

| Folder | Pliki | Po co |
|---|---|---|
| **`Runtime/`** | `SaveOrchestrator.cs` (centralny pipeline Save/Load async + SemaphoreSlim opGate), `ISavable.cs` (kontrakt — `Serialize` → JObject, `Deserialize` → state, `Identifier`), `SaveRegistry.cs` (rejestr modułów + walidacja kompletności ModuleOrder), `IMigrator.cs` + `MigrationRunner.cs` (chain v1→v2→v3 per moduł, post-EA only), `SaveBundle.cs` (multi-module container), `BundleSerializer.cs` (JSON+gzip+HMAC pack/unpack + DeserializeManifestOnly streaming), `HmacService.cs` (signature gen + 3-stanowy VerifyDetailed z legacy fallback), `UnityJsonConverters.cs` (Vector2/Vector3/Vector2Int JsonConverters — merge mode), `ISaveStorage.cs` (interfejs), `LocalDiskStorage.cs` (default, fsync via FileOptions.WriteThrough + ioGate + slotId regex validation), `SteamCloudStorage.cs` (M14 launch), `SaveTypes.cs` (const'y zamiast magic strings), `SaveSlotInfo.cs`/`SaveResult.cs`, `AutoSaveService.cs` (timer-based + non-blocking quit pattern), `LoadingScreenManager.cs`, `SaveLoadUI.cs`, `SaveLoadServiceBootstrap.cs` (wczesny init przed innymi services), `SaveLoadDiagnostics.cs`, `SaveLoadSmokeTest.cs` + `SaveLoadEdgeCaseTests.cs` (ContextMenu) |
| **`Modules/`** | 13 implementacji `ISavable` (wszystkie SchemaVersion=1 po pre-EA reset 2026-05-15): `WorldSavable` (GameState core + RandomRegistry snapshot + accumulatedPlaytime), `FleetSavable` (vehicles + consists + market + locations), `MaintenanceSavable` (parts inventory + workshop slots + **rescueOngoing** TD-037), `MaintenanceJobsSavable` (5 long-running job services modernization/modification/outdoor/painting), `TimetableSavable` (Routes + Timetables + categories + counters), `CirculationsSavable` (per-day vehicle assignments), **`TrainRunsSavable`** (TD-037: pełna lista TrainRuns Z ID + ActiveRunSnapshot aktywnych pociągów → pending-restore w TrainRunSimulator), **`PassengersSavable`** (TD-037: pełna pula agentów kolumnowo → pending-restore w PassengerManager), `PersonnelSavable` (employees + schedules + R&D + assignments + dispatch queue), `DepotSavable` (track graph + walls + rooms + furniture + outdoor equipment + active moves TD-031), `EconomySavable` (bilans + history + per-line balance + reputation), `SharedUISavable` (PlayerProgressService + suggestion records). **Wszystkie używają public `RestoreFromSave` API** zamiast reflection na private fields (refactor 2026-05-15). **Pending-restore pattern (TD-037):** moduł, którego restore wymaga async-budowanego świata (graf mapy), odkłada payload przez static `SetPendingRestore` — konsumpcja w FixedUpdate konsumenta pod gate'em gotowości. |

## Pipeline (skrót)

### Save
1. Iteruj `SaveRegistry.All`
2. Per moduł: `ISavable.Serialize()` → JObject
3. `Bundle.AddModule` + `Manifest.ModuleVersions` (schemaVersion per moduł)
4. `ComputeHmac` na końcu (po wszystkich modułach)
5. `Storage.SaveAsync` (default: `LocalDiskStorage` → `%AppData%/.../saves/<slotId>.rmsave`)

### Load
1. `Storage.LoadAsync` → bundle (null = `NotFound`)
2. Verify HMAC (mismatch = `ModifiedSave` — manual edit detected)
3. Check GameVersion (newer = `NewerVersion`, soft-warn)
4. Per moduł: znajdź w Registry → migrator chain (per moduł `IMigrator`) → `Deserialize`
5. Per-module exception → `InitializeDefault` + add to `FailedModules`
6. `Success` lub `PartialLoad` z listą failed modułów

## Cross-system glue

- **`SaveLoadServiceBootstrap`** — first MonoBehaviour init przed innymi service. Rejestruje wszystkie 11 modułów w `SaveRegistry` + waliduje kompletność (warning gdy któryś moduł z ModuleOrder NIE jest zarejestrowany — typowy bug z zakomentowanym `[RuntimeInitializeOnLoadMethod]`).
- **`SaveActionsHook`** (Core) — abstract hook żeby MainMenu/PauseMenuUI mogły triggerować save bez referencji do SaveLoad.
- **`AutoSaveService`** — timer-based, subskrybuje `GameState.OnDayEnded` (auto-save daily). Cap'd na 3 auto-save slots z rotacją.
- **Wszystkie moduły niższe** implementują `ISavable` z lokalnym DTO. SaveLoad reflection-discovers — nigdy `using RailwayManager.SaveLoad;` w niższych asmdef.
- **`RandomRegistry.ToJson/ApplyFromJson`** (Core MP-9) ← serializowane przez `WorldSavable`.

## Gotchas

- **HMAC fail = `ModifiedSave`** result, nie auto-reject. Soft warn dialog "save modyfikowany — kontynuować?". Pattern anti-cheat dla SP, w MP (M10) hard-reject host-side. **Legacy HMAC auto-resign** (2026-05-15): jeśli stary algorithm matchuje, po successful Deserialize SaveOrchestrator robi fire-and-forget storage write z aktualnym HMAC — następne load nie warning'uje. NIE robi tego gdy `ignoreHmac=true` ani gdy `failedModules.Count > 0` (state może być uszkodzony).
- **Schema versioning** — patrz sekcja "Schema versioning" wyżej. Pre-EA: wszystkie moduły v1, brak migratorów (fallback w Deserialize). Post-EA: bump = real migrator z testem.
- **Pre-init guards**: `SaveOrchestrator` może być wywołany przed `SaveRegistry.Initialize` (np. main menu load slot summary). Wszystkie publiczne metody muszą guard'ować na null registry.
- **`DepotSavable`** — najbardziej skomplikowany Deserialize (944 linii, reflection na private fieldy WallBuildingSystem/RoomDetectionSystem). Po reset SchemaVersion → 1: stare save'y v3-v6 ładują się direct deserialize z fallback per field.
- **`SteamCloudStorage` jeszcze nie wired** — M14 launch. Pre-EA: `LocalDiskStorage` only. Steamworks SDK integration czeka.
- **`SaveResult.PartialLoad` ≠ Success** — UI musi pokazać listę `FailedModules` userowi. Gracz może continue (z fallback defaults) lub reject.
- **`InitializeDefault()` ≠ "new game from scratch"** — używane w 2 sytuacjach: (1) fallback po failed load (graceful degradation, gracz dostaje grywalny stan), (2) `SaveLoadServiceBootstrap.ResetRegisteredModulesForNewGame` przed GameCreator. Po (2) **GameCreator nadpisuje** money/seed/depotName/HomeDepotStationId z user choices (`ApplyOnStart`). Defaulty w `InitializeDefault` (zob. `WorldSavableDefaults`) służą tylko jako fallback. Nie staraj się tu wpisywać user-specific values — to robi GameCreator.
- **`UnityJsonConverters`** dla Vector2/Vector3/Vector2Int — Newtonsoft.Json domyślnie wpada w infinite loop na `.normalized`/`.magnitude` properties tych structów. Bootstrap **merge mode** (od 2026-05-15) — jeśli inny system ustawił `JsonConvert.DefaultSettings`, dodajemy nasze converter'y zamiast nadpisać. Dla Quaternion: nie ma converter'a, ale aktualnie nikt nie serializuje rotation jako Quaternion (`DepotSavable.rotation` to int kąt 0/90/180/270). Dorzucić jeśli kiedyś będzie potrzebny.
- **Async w main thread**: `SaveAsync/LoadAsync` używają Task — Unity 2018+ obsługuje await na main thread. NIE blokuj `Task.Wait()` ani `.Result` (deadlock risk). Quit-time exit-save używa **non-blocking Application.wantsToQuit re-entrant pattern** (return false na pierwszy fire, async save w tle, `Application.Quit()` ponownie z continuation finally → drugi fire zwraca true) zamiast `task.Wait`.
- **Hardening 2026-05-11 (commit de28c6e)** — save load orchestration zabezpieczone na recovery przy niepełnym save (orphan modules, missing manifest fields).
- **Hardening 2026-05-15** — pełen audit + 20 fix'ów: prawdziwy fsync (FileOptions.WriteThrough), SemaphoreSlim w Orchestrator + Storage (race protection), guard F5 w MainMenu, non-blocking Application.wantsToQuit (zamiast Task.Wait), playtime accumulator (manifest.Playtime nie resetuje się przy restartcie), pre-EA reset SchemaVersion → 1, refactor 5 modułów z reflection na public RestoreFromSave, ListAsync parallel + manifest-only stream (10s → 100ms dla 100 save'ów), HMAC legacy auto-resign, walidacja slotId regex, walidacja kompletności ModuleOrder, SaveTypes consts. Patrz git log filter `fix(saveload):` / `refactor(saveload):` / `perf(saveload):`.

## Powiązane docs

- `docs/design/data-formats.md` — format `.rmsave` + HMAC + gzip
- `docs/TECH_DEBT.md` — TD-006 (PauseMenuUI save buttons ✅ DONE 2026-05-08)
- `docs/BUGS.md` — BUG-024/BUG-027/BUG-035/BUG-039 (state encapsulacja + reset cross-session)
