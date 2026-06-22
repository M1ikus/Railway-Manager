# QA regression matrix

> Cel: szybka decyzja "co testowac po tej zmianie?". Dokument uzupelnia
> `docs/TESTING.md` i `docs/RELEASE_RUNBOOK.md`.
>
> Stan na 2026-05-29: M-UIPolish jest w toku, wiekszosc systemow gameplay jest
> juz zaimplementowana, ale QA nadal jest glownie manualne.

---

## Reguly bazowe

### Minimalny gate dla kazdej zmiany w kodzie

1. `dotnet restore RM-0.2.slnx` jezeli pakiety/projekt byly ruszane.
2. `dotnet build RM-0.2.slnx --no-restore`.
3. Unity Editor: brak compile errors.
4. Uruchom scene, ktorej dotyka zmiana.
5. Zapisz w notatce/PR, ktore smoke testy wykonano.

### Minimalny gate dla dokumentacji

1. Linki lokalne istnieja.
2. Nowy dokument jest podlinkowany z README albo dokumentu nadrzednego.
3. Nie ma sprzecznosci z `docs/TECH_DEBT.md`, `docs/BUGS.md`.

### Minimalny gate dla release candidate

1. Wykonaj `docs/RELEASE_RUNBOOK.md`.
2. Uruchom build `.exe`, nie tylko Editor.
3. Przejdz Tier 0, Tier 1, Tier 2 i wszystkie dotkniete obszary z Tier 3.
4. Brak High/Critical bugow bez zapisanej decyzji "ship with known issue".

---

## Tier 0 - compile and project health

| Test | Kiedy | Sposob | Pass |
|---|---|---|---|
| .NET restore/build | kazda zmiana C# | `dotnet restore`, `dotnet build` | 0 errors |
| Unity EditMode pilot | Core/GameCreator/SaveLoad | `tools/run-editmode-tests.ps1` | XML bez failed tests |
| Unity compile | kazda zmiana C#/asmdef/assets | otworz projekt w Unity | Console bez compile errors |
| Build settings sanity | release / sceny | sprawdz `EditorBuildSettings.asset` | aktywne sceny istnieja |
| Git hygiene | przed commitem/release | `git status --short` | tylko zamierzone zmiany |
| Link check manual | docs | klik/grep nowych linkow | linki lokalne dzialaja |

---

## Tier 1 - boot and navigation

| Flow | Kiedy | Kroki | Pass |
|---|---|---|---|
| MainMenu boot | kazda zmiana UI/core/save | start gry | menu bez NRE, wersja widoczna |
| New Game path | GameCreator/MainMenu/save | MainMenu -> Nowa gra -> GameCreator -> Depot | Depot laduje |
| GameCreator tabs | GameCreator/UI/localization | kliknij wszystkie zakladki | brak NRE, dropdowny rozwijaja sie |
| Scene transitions | Map/Depot/TopBar | Depot -> MapScene -> Depot | brak duplikatow bootstrap/singleton |
| Pause menu | SaveLoad/input/UI | ESC, save/load, back | ESC nie odpala dwoch akcji naraz |
| Language switch | i18n/UI | PL/EN/DE/CZ | brak missing key w glownych ekranach |

**Known current blocker:** `docs/BUGS.md` wskazuje `BUG-086` - GameCreator
Difficulty dropdown NRE. Dopoki bug istnieje, GameCreator tabs sa obowiazkowym
testem po kazdej zmianie w GameCreator/MainMenu/UI builders.

---

## Tier 2 - critical gameplay path

| Flow | Systemy | Kroki | Pass |
|---|---|---|---|
| Depot build basics | Depot, UI, undo | postaw tor, cofnij, postaw ponownie, postaw pomieszczenie | brak overlap bugow, undo dziala |
| Fleet purchase | Fleet, economy, UI | otworz rynek, wybierz pojazd, kup/dodaj do floty | saldo i lista floty aktualne |
| Timetable create | Timetable, map data | wybierz 2 stacje, oblicz postoje, zatwierdz | rozklad na liscie |
| Circulation assign | M5, Fleet, Timetable | utworz obieg z 2 rozkladow, przypisz pojazd | pojazd zablokowany dla obiegu |
| Run simulation | M9, Economy | przyspiesz czas, pociag rusza, przychod/koszt tick | brak deadlock, brak spam error |
| Save/load roundtrip | SaveLoad, all modules | zapisz, wyjdz do menu, wczytaj | stan floty/depot/rozkladow wraca |
| Map load | Map, OSM data | wejdz na MapScene, pan/zoom | mapa laduje bez black screen |

---

## Tier 3 - change-to-test matrix

| Zmieniony obszar | Obowiazkowe testy | Dodatkowe ryzyko |
|---|---|---|
| `Assets/Scripts/Core/` | Tier 0, Tier 1, save/load roundtrip | singletons, `AppPaths`, input maps |
| MainMenu | MainMenu boot, Help/Credits/Mods/Load, language switch | scroll rect, missing i18n keys |
| GameCreator | wszystkie zakladki, difficulty dropdown, start game | `BUG-086`, apply-on-start rules |
| SharedUI/UITheme/UIBuilders | MainMenu, Depot toolbar, Fleet panel, Personnel panel | layout overflow, hover/focus, tooltips |
| Localization files | language switch, main flows, missing key grep | plural/currency/date formatting |
| SaveLoad | `SaveLoadSmokeTest`, manual roundtrip, old save if schema changed | migration chain `TD-012` |
| Depot track/tools | build track, turnout/schema placement, undo/redo | snap coupling, invalid geometry |
| Furniture/rooms | place furniture, move/rotate/delete, save/load | room access, wall doors, capacity |
| Maintenance/Modernization | ServicePit, ZNTK job, refuel/wash/manual dispatch | gameplay UI still polish-limited |
| Fleet configurator | wagon config, paint preset, decals, cart/order | decal placeholders, JSON catalogs |
| Economy/balance | buy train, daily/monthly tick, subsidies/reputation | overflow, negative balance UX |
| Timetable | route calc, stops, categories, collision/waiting | pathfinding regressions |
| Circulations | assign/unassign vehicle, duplicate prevention | nextId after load |
| M9 movement | train spawn, block occupancy, depot handshake | stuck trains, platform release |
| Map/formap data | warm-maz and full PL load, camera bounds | missing `.bin`, OSM data license |
| Personnel | hire/fire, shifts, crew assignment, depot movement | orphan refs, morale/fatigue |
| Performance | `PerfStressBootstrap`, profiler snapshot | auto-save GC noise, full PL scaling |
| Assets 3D | import prefab, materials, scene render, LOD | broken metas, scale/origin/colliders |
| Icons/sprites | `docs/design/icon-sprite-atlas.md` acceptance | license/source attribution |
| Release packaging | `docs/RELEASE_RUNBOOK.md` | missing StreamingAssets, wrong branch |

---

## Dostepne smoke/diagnostic tools

### Automatyczne EditMode tests

Pilot CLI:

```powershell
tools/run-editmode-tests.ps1
```

Testy sa w `Assets/Tests/EditMode/` i generuja wynik XML do
`Logs/editmode-results.xml`.

| Tool | Plik | Uzycie |
|---|---|---|
| `GameCreatorMappingTests` | `Assets/Tests/EditMode/GameCreatorMappingTests.cs` | mappingi dropdownow, preset keys, difficulty modifiers, seed formatting |
| `CoreGameStateTests` | `Assets/Tests/EditMode/CoreGameStateTests.cs` | eventy `GameState`, reputation clamp, home depot sentinel |
| `SaveLoadRoundtripTests` | `Assets/Tests/EditMode/SaveLoadRoundtripTests.cs` | `SaveOrchestrator` roundtrip na dummy modules |
| `SaveRegistryTests` | `Assets/Tests/EditMode/SaveRegistryTests.cs` | deterministyczna kolejnosc modulow save, duplicate register, missing modules |
| `SaveSerializationTests` | `Assets/Tests/EditMode/SaveSerializationTests.cs` | `BundleSerializer`, HMAC tamper detection, `LocalDiskStorage` save/list/load/delete |
| `CorePauseAndDlcTests` | `Assets/Tests/EditMode/CorePauseAndDlcTests.cs` | `PauseStack`, legacy pause OR, DLC country invariants |
| `CoreTimeAndRandomTests` | `Assets/Tests/EditMode/CoreTimeAndRandomTests.cs` | invariant ISO parsing, deterministic RNG replay, RNG snapshot restore |
| `VehicleLocationServiceTests` | `Assets/Tests/EditMode/VehicleLocationServiceTests.cs` | location transitions, indexes, snapshots, reset |
| `MigrationRunnerTests` | `Assets/Tests/EditMode/MigrationRunnerTests.cs` | migration chain discovery, version short-circuit, gap exception |
| `CoreUtilityContractsTests` | `Assets/Tests/EditMode/CoreUtilityContractsTests.cs` | undo settings, UI intents, minimap registry, game clock rollover |

### Reczne ContextMenu smoke tests

| Tool | Plik | Uzycie |
|---|---|---|
| `CoreSmokeTest` | `Assets/Scripts/Core/CoreSmokeTest.cs` | podstawowe kontrakty Core |
| `DepotSmokeTest` | `Assets/Scripts/Depot/DepotSmokeTest.cs` | depot foundation |
| `GameCreatorSmokeTest` | `Assets/Scripts/GameCreator/GameCreatorSmokeTest.cs` | mappingi dropdownow, preset keys, hooks |
| `SaveLoadSmokeTest` | `Assets/Scripts/SaveLoad/Runtime/SaveLoadSmokeTest.cs` | save/load roundtrip test modules |
| `SaveLoadDiagnostics` | `Assets/Scripts/SaveLoad/Runtime/SaveLoadDiagnostics.cs` | save/load/list/delete/manual dump |
| `FurnitureMilestoneSmokeTest` | `Assets/Scripts/Personnel/Runtime/FurnitureMilestoneSmokeTest.cs` | furniture + personnel assignment |
| `MModernizationSmokeTest` | `Assets/Scripts/Personnel/Runtime/MModernizationSmokeTest.cs` | MM services/hooks/catalogs |
| `PersonnelLifeLoopSmokeTest` | `Assets/Scripts/Personnel/Runtime/PersonnelLifeLoopSmokeTest.cs` | movement loop i workflow personelu |
| `WorkshopHallLevelSmokeTest` | `Assets/Scripts/Maintenance/Runtime/WorkshopHallLevelSmokeTest.cs` | ServicePit / hall level |
| `PerfStressBootstrap` | `Assets/Scripts/Timetable/Runtime/Performance/PerfStressBootstrap.cs` | performance stress |
| `TimetableInitializer.Diagnostics` | `Assets/Scripts/Timetable/Runtime/TimetableInitializer.Diagnostics.cs` | diagnostyka grafu/rozkladow |

Te testy sa glownie `[ContextMenu]`, wiec uruchamiane recznie w Unity Editor.
Jesli test wymaga sceny, dodaj komponent do pustego GameObjectu albo uzyj
auto-spawnu, jezeli dany test go ma.

---

## Manualne karty regresji

### Karta A - 15 minut gracza

1. Start nowej gry.
2. Wybierz depot/home setup.
3. Kup 1-2 pojazdy.
4. Zbuduj prosty tor i hale.
5. Utworz rozklad na 2-3 stacje.
6. Przypisz pojazd/obieg.
7. Przyspiesz czas i obserwuj przejazd.
8. Zapisz, wyjdz, wczytaj.

### Karta B - Depot/modernization

1. Postaw hale, ServicePit, fuel/wash outdoor equipment.
2. Wyslij pojazd na maintenance albo refuel/wash.
3. Sprawdz lifecycle joba i teksty UI.
4. Zapisz/wczytaj w trakcie joba.

### Karta C - UI polish

1. Przejdz glowne panele Depot: Build, Fleet, Timetable, Personnel.
2. Przetestuj hover/tooltip, scroll, dropdowny, modal close.
3. Zmien rozdzielczosc i skalowanie UI.
4. Sprawdz najdluzsze teksty PL/DE/CZ.

### Karta D - Data/release

1. Uruchom build z pelna Polska data bundle.
2. Sprawdz `StreamingAssets` w folderze builda.
3. Potwierdz OSM attribution i release notes.
4. Uruchom smoke po spakowaniu/rozpakowaniu zipa.

---

## Known QA gaps

| Gap | Ryzyko | Kiedy domknac |
|---|---|---|
| Brak CI/automatycznych Unity tests | regresje wykrywane pozno | M14 Beta albo wczesniej, gdy buildy ida do testerow |
| Brak build smoke automatu | build `.exe` moze pasc mimo Editor pass | przed pierwszym Steam beta |
| `BUG-086` GameCreator NRE | nowa gra moze blokowac difficulty flow | przed kolejnym QA buildem |
| Migration chain nie battle-tested | stare save'y moga peknac przy schema bump | M-Balance trusted playtest |
| Performance baseline nie na minimum/recommended | ryzyko optymistycznych targetow | M-Balance/M-Performance validation |
| Localization DE/CZ bez native review | jakosc tekstu | post-EA albo przed publicznym marketing push |
| UI icons/decals placeholdery | niski polish, ale nie crash | MUI-11/MUI-12 |

---

## Evidence template

```md
QA date: YYYY-MM-DD
Commit: <sha>
Build: Editor / Local exe / Steam beta
Unity: 6000.4.0f1
Data: warm-maz / full PL / both

Tests:
- Tier 0: pass/fail
- Tier 1: pass/fail
- Tier 2: pass/fail
- Tier 3 touched areas: ...

Findings:
- ...

Known issues accepted:
- ...
```
