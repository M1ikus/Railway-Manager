# Dług technologiczny

> **Cel dokumentu:** Centralny rejestr znanych niedoskonałości technicznych, MVP shortcuts, "działa ale nie idealnie" rozwiązań. Per item: stan, próbowane fix'y, idealne rozwiązanie, decyzja kiedy fixować.
>
> **Convention:**
> - **TD-XXX** numbering chronological (nie reorderować).
> - **Status:** 🔴 Active / 🟡 Partially fixed / 🟢 Resolved / ⚫ Won't fix.
> - **Priorytet:** kiedy fixować — konkretny milestone (np. `M-UIPolish`) lub `post-EA`.
> - **Estymata:** rough, w sesjach lub liniach kodu.

> **Vs open questions:** tech debt = COŚ DZIAŁA ale nie idealnie + mamy plan poprawy. Open question = NIE ZDECYDOWANE jeszcze (= kierunek do podjęcia).

---

## Spis zawartości

### UX / Player-facing
- [TD-001 — Snap schematów "odpychanie"](#td-001--snap-schematów--odpychanie-perception)
- [TD-002 — SchemaPanelUI per-pair foldout placeholder ✅ DONE 2026-05-13](#td-002--schemapanelui-per-pair-foldout-placeholder--done-2026-05-13)
- [TD-039 — Brak loading screenu przy wejściu do gry (lag bez feedbacku)](#td-039--brak-loading-screenu-przy-wejściu-do-gry-lag-bez-feedbacku)
- [TD-040 — Trawa łapie szeroki specular od słońca (placeholder ground sheen)](#td-040--trawa-łapie-szeroki-specular-od-słońca-placeholder-ground-sheen)
- [TD-041 — Podgląd trasy (mini-mapa OSM) renderuje tylko 5 warstw + brak widocznych lasów](#td-041--podgląd-trasy-mini-mapa-osm-renderuje-tylko-5-warstw--brak-widocznych-lasów)
- [TD-042 — Etykieta nazwy kafla toolbara (tooltip) za mała i za daleko od przycisku](#td-042--etykieta-nazwy-kafla-toolbara-tooltip-za-mała-i-za-daleko-od-przycisku)
- TD-045 — Szyny na łukach: proste segmenty 1 m nie spasowują się gładko z krzywizną toru
- TD-046 — Szyny/podkłady nie dociągają do samego końca toru

### Architecture / Integration
- [TD-003 — Rozjazdy ze schematu nie split'ują grafu w miejscach pre/post](#td-003--rozjazdy-ze-schematu-nie-splitują-grafu-w-miejscach-prepost)
- [TD-004 — Snapshot serializer brak clipping torów na granicy selekcji](#td-004--snapshot-serializer-brak-clipping-torów-na-granicy-selekcji)
- [TD-012 — Migration chain nie battle-tested pre-EA (M-Balance playtest exposure)](#td-012--migration-chain-nie-battle-tested-pre-ea-m-balance-playtest-exposure)
- [TD-014 — MapLoader/TileManager single-file (multi-country DLC bloker)](#td-014--maploadertilemanager-single-file-multi-country-dlc-bloker)
- [TD-029 — UIPrimitives brakuje helperów dla scroll content + LayoutGroup defaults (6× bug pattern 2026-05-17)](#td-029--uiprimitives-brakuje-helperów-dla-scroll-content--layoutgroup-defaults)

### Modules incomplete
- [TD-005 — HallActionStateMachine sub-modes 🟡 Partial (1/4 done MM-19, 1/4 OQ-gated, 2/4 scope removed M-Furniture)](#td-005--hallactionstatemachine-sub-modes-catenary-washing-lift-todo)
- [TD-006 — PauseMenuUI Save/Load przyciski ✅ DONE 2026-05-08](#td-006--pausemenuui-saveload-przyciski-todo)
- [TD-007 — SubsidyCalculator punctuality KPI multiplier TODO](#td-007--subsidycalculator-punctuality-kpi-multiplier-todo)
- [TD-008 — FleetPanelUI inspection column hardcoded show](#td-008--fleetpanelui-inspection-column-hardcoded-show)
- [TD-025 — Personnel depot movement loop 3D ✅ DONE 2026-05-11](#td-025--personnel-depot-movement-loop-3d--done-2026-05-11)

### Performance
- [TD-009 — Multi-endpoint snap brute force O(N) per frame ✅ DONE 2026-05-13](#td-009--multi-endpoint-snap-brute-force-on-per-frame--done-2026-05-13)
- [TD-011 — Tile parsing sync na main thread (1-3s freeze fat tile)](#td-011--tile-parsing-sync-na-main-thread-1-3s-freeze-fat-tile)
- [TD-013 — N² block contention search w TrainRunSimulator.Priority](#td-013--n-block-contention-search-w-trainrunsimulator-priority)
- [TD-015 — MaybeSpawnAgents per-attempt modifiers compute](#td-015--maybespawnagents-per-attempt-modifiers-compute-mp-45-followup)
- [TD-016 — OD matrix building brute force O(N²) na pełnej Polsce](#td-016--od-matrix-building-brute-force-on-na-pełnej-polsce)
- [TD-017 — Auto-save GC pressure 4.3MB burst](#td-017--auto-save-gc-pressure-43mb-burst)
- [TD-026 — MapRenderer hierarchia ~45k GameObjects 🟡 Krok A done 2026-05-16](#td-026--maprenderer-hierarchia-45k-gameobjects-na-pełnej-polsce)
- [TD-027 — StationMarker 3000 colliders + OnMouseEnter/Down legacy event handlers](#td-027--stationmarker-3000-colliders--onmouseenterdown-legacy-event-handlers)
- [TD-028 — ReverseTriangleWindingOrder per flush (fix w formap generator)](#td-028--reversetrianglewindingorder-per-flush-fix-w-formap-generator)

### Gameplay correctness
- [TD-019 — Pathfinding rozkładów: trasa A→B zahacza o stacje na boki ✅ DONE 2026-05-10](#td-019--pathfinding-rozkładów-trasa-ab-zahacza-o-stacje-na-boki)
- [TD-023 — Punctuality threshold mismatch: subsidy ±5min vs reputation 1/5/15min ✅ DONE 2026-05-15](#td-023--punctuality-threshold-mismatch-subsidy-5min-vs-reputation-1515min--done-2026-05-15)
- [TD-024 — Subsidy minRunsPerDay cliff (4 runs = wszystko/nic, brak gradacji)](#td-024--subsidy-minrunsperday-cliff-4-runs--wszystkonic-brak-gradacji)

### Symulacja zajezdni — głębia (pociągi + pracownicy)
- [TD-031 — Zajętość toru binarna (1 skład/tor) zamiast pozycyjnej](#td-031--zajętość-toru-binarna-1-składtor-zamiast-pozycyjnej)
- [TD-032 — Brak sprzęgania/rozprzęgania składów w zajezdni](#td-032--brak-sprzęganiarozprzęgania-składów-w-zajezdni)
- [TD-033 — Pracownicy na zewnątrz: ścieżka lub skrót, NIE przez obiekty](#td-033--pracownicy-na-zewnątrz-ścieżka-lub-skrót-nie-przez-obiekty)
- [TD-034 — Cykl pracownika: brak łazienki/przerw + brak kolizji między pracownikami](#td-034--cykl-pracownika-brak-łazienkiprzerw--brak-kolizji-między-pracownikami)
- [TD-035 — Koszty budowy: crossover bez 2× mechanizmu + turnout-removal geometry refund](#td-035--koszty-budowy-crossover-bez-2-mechanizmu--turnout-removal-geometry-refund)
- [TD-036 — Kalibracja budżet startowy vs realne ceny budowy/taboru](#td-036--kalibracja-budżet-startowy-vs-realne-ceny-budowytaboru)
- [TD-037 — Pociągi na mapie nie kontynuują po save/load (runtime TrainRuns nie persystowany)](#td-037--pociągi-na-mapie-nie-kontynuują-po-saveload-runtime-trainruns-nie-persystowany)
- [TD-038 — PlayMode cross-class izolacja (DepotEntryTests zatruwa kolejne klasy) ✅ DONE 2026-06-09](#td-038--playmode-cross-class-izolacja-depotentrytests-zatruwa-kolejne-klasy)
- [TD-044 — Tory wyjazdowe zajezdni = sztywny placeholder (ExitTrackController) zamiast player-configurable konfiguratora](#td-044--tory-wyjazdowe-zajezdni--sztywny-placeholder-exittrackcontroller-zamiast-player-configurable-konfiguratora)

### QoL / Future
- [TD-010 — Atomic undo schematu (post-EA)](#td-010--atomic-undo-schematu-post-ea)
- [TD-018 — Real recommended/minimum spec hardware validation](#td-018--real-recommendedminimum-spec-hardware-validation)

### Code quality / Consistency
- [TD-030 — Ujednolicenie języka kodu (mieszanka PL/ENG)](#td-030--ujednolicenie-języka-kodu-mieszanka-pleng)
- [TD-043 — Konwencja rogów UI: full-bleed ostre / popupy zaokrąglone (+ full UI sweep)](#td-043--konwencja-rogów-ui-full-bleed-ostre--popupy-zaokrąglone)

### Modules incomplete (placeholder data)
- [TD-020 — MultiplayerScreenUI MockServers do usunięcia (M10 placeholder)](#td-020--multiplayerscreenui-mockservers-do-usunięcia-m10-placeholder)
- [TD-021 — LoadGameScreenUI mock data → integracja z SaveOrchestrator ✅ DONE 2026-05-08](#td-021--loadgamescreenui-mock-data--integracja-z-saveorchestrator-m13-backend-gotowy)
- [TD-022 — GameCreator większość opcji NIE aplikuje wartości ✅ DONE 2026-05-08](#td-022--gamecreator-większość-opcji-nie-aplikuje-wartości-placeholder-ui-bez-wireowania)

---

## UX / Player-facing

### TD-001 — Snap schematów "odpychanie" perception

**Status:** 🟢 Resolved (2026-06-15 — rdzeń naprawiony przez 8 iteracji `430b9d3..0b73db3`, potwierdzone code-readingiem)
**Priorytet:** M-UIPolish lub M-Balance (= post-EA polish)
**Estymata:** ~30 linii w `TurnoutSchemaPlacer.cs`, plus UX feedback
**Files:** `Assets/Scripts/Depot/Schemas/Placement/SchemaSnapDetector.cs`, `Assets/Scripts/Depot/Schemas/Placement/TurnoutSchemaPlacer.cs`

**Rozstrzygnięcie (2026-06-15, code-reading TD-triage):**
Oryginalny symptom („rotacja przesuwa snapnięty schemat") **już nie występuje**. Detektor liczy
`translation = bestNodePos − cursorWorldPos` (prosta delta, `SchemaSnapDetector.cs:255` + sticky `:177`),
więc `GetPreviewWorldPosition() = cursor + translation = target` — kursor się kasuje, **anchor endpoint
jest przyklejony do targetu niezależnie od kursora i rotacji** (rotacja obraca korpus wokół anchora =
pivot), a auto-rotation (threshold 180°) wymusza współliniowość z torem. Pozostały „feel" (skok przy
acquire = poprawny; snap-back pod kursor przy sticky-release >8m) uznany za akceptowalny. Proponowany
pełny decouple `_previewWorldPos` odrzucony: dla stanu snapped daje identyczny wynik (target), a dla
free-placement zmieniłby działające zachowanie (schemat przestałby śledzić mysz) = ryzyko regresji za
zero zysku. Decyzja usera: pomijamy, zostaje wąski sticky-release tweak jako ewentualny post-EA QoL.

**Symptom user'a:**
> "Rotacja powoduje przesunięcie więc nie jest zesnapowane" — z perspektywy player'a cursor zostaje gdzie był, ale schemat się przesuwa, co czytane jako "schemat odpycha się od cursor".

**Stan obecny:**
Snap schematów działa technicznie:
- Multi-anchor (każdy endpoint schematu może snap'ować).
- Outward "półendpoint" validation (= snap tylko gdy schemat extending OUTWARD od target).
- Auto-rotation 180° threshold (= zawsze wymusza kąt 0° z istniejącym torem).
- Per-endpoint interior tangent (= dynamic schemaDirLocal computed z polyline).
- Sticky preserve previous anchor endpoint.
- Translation = simple `bestNodePos - cursor` delta (= invariant od rotacji).
- Anchor convention dynamic (= `endpoints[anchorEndpointIdx]` zamiast wjazd dla preview pivot).

ALE schemat coupled z cursor convention — gdy snap aktywuje, schemat "skacze" do snap target. Player widzi to jako "odpychanie".

**Próbowane rozwiązania (8 iteracji w git log commits `430b9d3..0b73db3`, ~2 godz pracy):** żaden nie satisfactory.

**Idealne rozwiązanie:**
**Decouple schemat całkowicie od pozycji myszki.**
1. `_previewWorldPos` field (= explicit world position, niezależny od cursor).
2. Schemat appears at initial position (= scene center / cursor at click time).
3. Mysz NIE rusza schematem — cursor only used dla snap detection (= proximity check).
4. Cursor blisko valid snap target → `_previewWorldPos = target`. Schemat **skacze** do target. Snap aktywny.
5. Cursor odjeżdża — schemat zostaje na ostatnim snap target (lub release = wraca do default).
6. Optional: hold Shift+drag dla manual reposition. Tab key dla cycling endpoints jako anchor preference.

To eliminuje "odpychanie" perception bo schemat NIGDY nie follow cursor — only jumps to snap targets.

---

### TD-002 — SchemaPanelUI per-pair foldout placeholder ✅ DONE 2026-05-13

**Status:** 🟢 Resolved
**Priorytet:** zrobione przy okazji god class split (ITER 11 sesji 2026-05-13)
**Estymata:** ~50 linii UI builder → faktycznie ~120 linii (Params.cs RebuildAdvancedRows + 2 handlers + Build.cs container refactor + List.cs hook)
**Files:** `Assets/Scripts/Depot/Schemas/UI/SchemaPanelUI.{Build,Params,List,cs}`

**Implementacja:**
- `Build.cs:CreateAdvancedSectionPlaceholder` zmienione z static placeholder TODO label na container z `VerticalLayoutGroup` + `ContentSizeFitter`. Dwa children: `_advancedEmptyLabel` (italics tekst dla pustego stanu) + `_advancedRowsContainer` (pusty kontener którego dzieci są generowane dynamicznie).
- `Params.cs:RebuildAdvancedRows` (nowa metoda) — generuje N-1 wierszy. Per wiersz: label "Para i+1:" + slider `trackSpacings[i]` (4.0-6.0m, krok 0.1m) + value label + dropdown `turnoutTypes[i]` (R190/R300/Crossover_R190). Listenery wire'd przez closure capture `capturedIndex`. Empty state pokazywany gdy schema niezaznaczona, niegenerative, lub rowCount=0.
- `Params.cs:OnAdvancedSpacingChanged(index, value)` — clamp do 4.0-6.0m, step 0.1m, write do `_editParams.trackSpacings[index]`. Po edycji per-pair shorthand `trackSpacing=0f` (=array source of truth). Update value label + `RegenerateLivePreviewIfActive`.
- `Params.cs:OnAdvancedTurnoutTypeChanged(index, dropdownIdx)` — analogicznie dla `turnoutTypes[index]`, shorthand `turnoutType=""`.
- `RebuildAdvancedRows` wywoływane z: `OnSchemaSelected` (List.cs), `OnTrackCountChanged` (Params.cs), `OnAdvancedToggled` gdy `state=true` (Params.cs lazy build).
- `Normalize(expectedTurnoutCount)` wywoływany przed odczytem `trackSpacings`/`turnoutTypes` — bezpieczne nawet gdy shorthand używany.

---

## Architecture / Integration

### TD-003 — Rozjazdy ze schematu nie split'ują grafu w miejscach pre/post

**Status:** 🟡 Partially fixed (= TurnoutEntity tworzona, ale split chain logic niedoskonały dla edge cases)
**Priorytet:** M-Balance (= playtest stress test)
**Estymata:** ~100 linii test edge cases + fixes
**Files:** `Assets/Scripts/Depot/Schemas/Placement/TurnoutSchemaPlacer.cs` (PHASE 2)

**Stan obecny:**
PHASE 2 placement schematu używa `TurnoutPlacer.PlaceTurnoutOnChain` per `SchemaTurnoutEntry` → tworzy real `TurnoutEntity` z body split + diverging arc. Działa dla standard cases (Ladder, Throat, Trapez, Scissors).

ALE edge cases mogą dawać nieprzewidziane skutki:
- Snapshot z nietypowymi konfiguracjami rozjazdów (= np. flip+divergeLeft kombinacja nieprzetestowana).
- Bardzo gęste schematy (= rozjazdy bezpośrednio sąsiadujące, body collision check może fail).
- Schematy z odgałęzieniami które same są segmentami innego rozjazdu (= recursive turnout in turnout).

**Idealne rozwiązanie:**
- Test suite z każdą permutacją (snapshot z 4-X turnout combination, edge spacing values, flip combinations).
- Defensive validation w PlaceTurnoutOnChain (= reject placement gdy collision).
- Logi diagnostyczne PHASE 2 już są (commit `f009ed0`), ale brak strukturalnego raportu placement issues.

---

### TD-004 — Snapshot serializer brak clipping torów na granicy selekcji

**Status:** 🟢 Resolved (2026-06-15 — Wariant A clipping + EditMode `SnapshotClipperTests`)
**Priorytet:** post-EA polish (= mało praktycznych use cases)
**Estymata:** ~80 linii w `SnapshotSerializer.cs` + new `SnapshotClipper.cs`
**Files:** `Assets/Scripts/Depot/Schemas/Snapshot/SnapshotSerializer.cs`, `Assets/Scripts/Depot/Schemas/Snapshot/SnapshotClipper.cs`

**Implementacja (2026-06-15):**
`SnapshotClipper.ClipPolylineToRectXZ(polyline, rectXZ)` — pure static, Liang-Barsky per segment w
płaszczyźnie XZ (Y zachowane przez `Vector3.LerpUnclamped`), zwraca 0..N fragmentów (tor wchodzący-
wychodzący-wchodzący = wiele fragmentów). Fast-path: polyline w całości wewnątrz → niezmieniona kopia
(gwarancja nietykania). `SnapshotSerializer.Serialize` clip'uje każdy tor do `selection.selectionBounds`
(= rectangle drag z `SnapshotSelectionTool`) PRZED odjęciem anchora i `DetectEndpoints` — punkty cięcia
liczą się jako open ends. Fragmenty zachowują `originalGraphTrackId`, nazwy `Track_{id}_{f}` przy splicie.
EditMode `SnapshotClipperTests`: fully-inside-unchanged, single-edge-clip, enter-exit-enter→2 fragmenty,
fully-outside→0, zdegenerowany prostokąt→0, Y-interpolation.

**Stan obecny:**
Gdy player drag-select fragment torów, niektóre tory mogą "wystawać" poza zaznaczony rectangle. Aktualnie SnapshotSerializer **NIE clip'uje** ich na granicy — tory są zachowane in full lub odrzucone całkiem (per `IsTrackInRectangle` algorithm).

Wynik: snapshot może mieć tory dłuższe niż user oczekuje (= "wystają poza rectangle").

**Idealne rozwiązanie:**
Wariant A z spec'a: clip tory na granicy selekcji + utwórz nowy endpoint na linii cięcia. Snapshot ma czyste granice.

**Powód odłożenia:**
- Większość player'ów drag-selectuje precyzyjnie (= rectangle obejmuje całe tory).
- Wariant prosty (no clipping) działa dla 90% use cases.
- Implementation complex (= geometric line-rectangle intersection + new endpoint creation).

---

### TD-012 — Migration chain nie battle-tested pre-EA (M-Balance playtest exposure)

**Status:** 🟢 Resolved (2026-06-15 — duplicate-detection dodane; reszta "Idealnego rozwiązania" już istniała; battle-test na realnym chainie naturalnie materializuje się przy 1. realnym SchemaVersion bump post-EA)
**Priorytet:** M-Balance (= przed trusted playtest 3-5 osób, gdy save'y zaczną żyć między iteracjami)
**Estymata:** ~1 sesja → faktycznie ~0.5 (większość już była)
**Files:** `Assets/Scripts/SaveLoad/Runtime/MigrationRunner.cs`, `Assets/Scripts/SaveLoad/Runtime/IMigrator.cs`

**Rozstrzygnięcie (2026-06-15, code-reading TD-triage):**
"Stan obecny" poniżej był NIEAKTUALNY — wymienione `DepotMigrator_v1_v2`/`v2_v3` zostały usunięte
2026-05-15 (pre-EA reset SchemaVersion→1, 12 identity migratorów skasowanych). Dziś ZERO produkcyjnych
`IMigrator` (wszystkie moduły v1, chain martwy do 1. realnego bumpu post-EA). Większość "Idealnego
rozwiązania" już była pokryta: `MigrationRunnerTests` (chain v1→v2→v3, gap, sort) + `SaveLoadEdgeCaseTests`
(realny E2E save v1 → auto-discovered migrator → load). Jedyna realna luka — **silent first-wins przy
duplikacie `(ModuleId, SourceVersion)`** w `FindMigrator` — naprawiona: `EnsureDiscovered` ostrzega o
duplikatach (sąsiednich po sort), EditMode `Discovery_WarnsOnDuplicateSourceVersion`. Battle-test na
realnym chainie wymaga 1. realnego bumpu i tak materializuje się dopiero post-EA — mechanizm utwardzony.

**Stan obecny:**
M13 dostarczyło pełną infrastrukturę migrator chain (`MigrationRunner` z auto-discovery przez reflection, `IMigrator` interface, per-moduł chain v1→v2→v3). Aktualnie istnieją 2 migratory:
- `DepotMigrator_v1_v2` (identity — v2 dodało walls/rooms, Deserialize obsługuje brakujące pola)
- `DepotMigrator_v2_v3` (identity — v3 dodało placedFurniture/doorCells, Deserialize obsługuje brakujące pola)

**Oba są no-op** bo aktualnie nikt nie ma "ważnego" save'a do migracji (pre-EA solo dev, save/load istnieje tylko do testów ad-hoc — restart gry przy każdej iteracji).

**Risk wzmacniający (2026-05-04):**
M-Balance trusted playtest (3-5 osób) wprowadzi pierwsze save'y które **żyją między iteracjami gry** — gdy build dostaje patch, gracze nie chcą tracić postępu. Każda kolejna schema bump (`SchemaVersion = 4` w jakimkolwiek module) wymusi chain `v1→v2→v3→v4`. Jeśli w identity migratorach v1_v2 / v2_v3 jest **subtle bug** (np. założenie o private field name które się zmieni przy refactor) — ujawni się dopiero przy realnym chain'ie, nie wcześniej.

Plus: `MigrationRunner` używa reflection auto-discovery (skanowanie `IMigrator` w assemblies). **Edge case nie testowany**: jeśli ktoś doda 2 migratory z tym samym `(ModuleId, SourceVersion)` — silent first-wins przez `FindMigrator` linear scan. Brak walidacji duplicates przy discovery.

**Idealne rozwiązanie:**
Smoke test `MigrationChainSmokeTest` w `Assets/Scripts/SaveLoad/Runtime/`:
1. Tworzy fake save v1 z minimalnym validnym JSON per moduł
2. Uruchamia chain v1→v2→v3 (current SchemaVersion)
3. Waliduje że output deserializuje bez exception + state ma sensible defaults
4. Symuluje fake v4 bump (mock `IMigrator` na chwilę testu) — sprawdza że v1→v2→v3→v4 chain działa
5. Walidacja duplicate detection — 2 migratory z tym samym `(ModuleId, SourceVersion)` → warning lub error w discovery
6. `[ContextMenu]` runner do uruchomienia ad-hoc

Plus: dodać do M-Balance pre-playtest checklist'y "uruchom migration smoke test, przejrzyj log".

**Decyzja:** odłożone do M-Balance. Pre-EA gra solo dev nie ma exposed risku. Gdy zaczynają się testers (3-5 osób z M-Balance roundu) — risk realny, robić wtedy.

### TD-005 — HallActionStateMachine sub-modes (catenary, washing, lift) TODO

**Status:** 🟡 Partial (1/4 zaimplementowane, 1/4 świadomy stub gated na OQ, 2/4 scope removed)
**Priorytet:** ElectrifyHall — czeka na decyzję projektową "Sieć trakcyjna w hali" (M-Balance lub M12). Reszta scope-out.
**Estymata:** ElectrifyHall ~30-50 linii reuse z `ElectrificationStateMachine` GDY decyzja OQ podjęta
**Files:** `Assets/Scripts/Depot/HallActionStateMachine.cs:438-443` (ElectrifyHall stub)

**Status per oryginalne TODO (audit 2026-05-08):**

| # | Original TODO | Status | Wyjaśnienie |
|---|---|---|---|
| 1 | "Kliknij-i-przeciągnij tor wzdłuż hali" | ✅ DONE MM-19 (2026-05-08) | Pełna 2-state machine `HallTrackState.{Idle, PreviewingEnd}`, 2-click placement, walidacja sampling co 0.5m wzdłuż linii (in-hall lub near-wall), auto-create `TrackGate` przez `WallBuildingSystem.TryAddTrackGateOpening` na każdym wall crossing, deduplikacja po `wallId`, snap to grid 1m, `MinTrackLengthMeters=2f`, preview visuals (sphere marker punkt A + LineRenderer A→B z 3 kolorami: red/yellow/green wg validation state), cancel handling. ~330 linii. |
| 2 | "Klik na tor w hali → toggle catenary" | ❌ Stub świadomy (linia 438) | TODO oznaczony `(OQ)` — gated na otwartą decyzję "Sieć trakcyjna w hali (button EL w `RoomBuildPanelUI[Hall]`)". Decyzja design'owa TBD: czy w ogóle robić elektryfikację w hali (PRO: realizm, niektóre PL hale mają sieć żeby pojazd EMU mógł zjechać własną mocą; CONTRA: konflikt z `CatenaryGenerator` open-sky assumption, mesh ścian + supports complications). Implementacja gdy OQ rozstrzygnięte = reuse `ElectrificationStateMachine` na hall track segments, ~30-50 linii. |
| 3 | "Klik na tor → oznacz strefę myjni" | 🔄 Scope removed (M-Furniture) | `WashZone` (8×6m, niebieski) zaimplementowany jako **outdoor equipment** w sub-mode TOR (`Depot/OutdoorEquipment/`, M-Furniture 2026-05-03). Wash bay w realnych zajezdniach jest outside hali, nie w środku — design intent zmieniony, hala TODO zbędne. |
| 4 | "Klik → postaw podnośnik" | 🔄 Scope removed (M-Furniture + M-Modernization) | `PitLift` (6×4m, szary) jako outdoor equipment (M-Furniture). `ServicePit` jako furniture wewnątrz hali (M-Modernization compound furniture pattern, pracownik mechanik wchodzi do konkretnego ServicePit). Te systemy zastąpiły dedicated "podnośnik sub-mode w hali". |

**Co pozostało faktycznie aktywne:**
Tylko `HandleElectrifyHall` stub — gated na OQ decyzję. Gdy decyzja podjęta, ~30-50 linii reuse `ElectrificationStateMachine` na hall track segments (analogicznie do toggle catenary na external tracks).

**Decyzja:** TD-005 zostaje w 🟡 Partial state. Re-evaluation gdy OQ "Sieć trakcyjna w hali" zostanie rozstrzygnięte (przewidywane M-Balance lub M12 polish, gdy więcej danych z testów). Jeśli decyzja "NIE" → button EL znika z `RoomBuildPanelUI[Hall]`, TD-005 → 🟢 Resolved (Won't fix gałąź). Jeśli "TAK" → ~30-50 linii implementacji + `ElectrifyHall` switch case w `Update()` dispatcher, TD-005 → 🟢 Resolved.

---

### TD-006 — PauseMenuUI Save/Load przyciski ✅ DONE 2026-05-08

**Status:** 🟢 Resolved
**Priorytet:** szybki fix (zrobione w jednej sesji)
**Estymata:** ~30 linii — wykonane w ~50 (hook'i + button rewrite + AutoSaveService InstallSaveActionsHooks rozszerzenie)
**Files:** `Assets/Scripts/Core/SaveActionsHook.cs`, `Assets/Scripts/SaveLoad/Runtime/AutoSaveService.cs`, `Assets/Scripts/Depot/UI/PauseMenuUI.cs:392-430`

**Historia:**
TD-006 został napisany gdy PauseMenuUI miał `// TODO: FileManager.Save()` placeholdery. Częściowy fix poprzedni (~2026-05-08 wcześniej) wired `SaveActionsHook.QuickSave/QuickLoad/SaveAndExitToMainMenu/SaveAndQuitApplication` przez `AutoSaveService.InstallSaveActionsHooks`. Buttons "Zapisz stan" / "Załaduj stan" już woływały QuickSave/QuickLoad — funkcjonalnie działało, ale z UX perspective gracz oczekuje **slot picker** (wybiera nazwę save'a / istniejący slot) gdy klika menu button, a F5/F9 wykonują quick-save/load bez UI (power user shortcut).

**Implementacja 2026-05-08 (final):**

1. **`SaveActionsHook` rozszerzony** o 2 nowe `Action`:
   - `ShowSaveSlotPicker` — otwiera SaveLoadUI w trybie Save (player picks slot+name)
   - `ShowLoadSlotPicker` — otwiera SaveLoadUI w trybie Load (player picks save z listy)

2. **`AutoSaveService.InstallSaveActionsHooks`** wires nowe Action'y na `SaveLoadUI.EnsureExists().ShowForSave()` / `.ShowForLoad()` (master-detail panel z M13-11).

3. **`PauseMenuUI.OnSaveState`/`OnLoadState`** preferują slot picker hook:
   ```csharp
   if (ShowSaveSlotPicker != null) → slot picker (preferred UX)
   else if (QuickSave != null) → fallback quicksave (legacy/smoke test)
   else → log warn (bootstrap problem)
   ```

**Behavior teraz:**
- Klik "Zapisz stan" w pause menu → SaveLoadUI overlay z listą slotów, gracz nazywa save i zapisuje. Hide po success.
- Klik "Załaduj stan" w pause menu → SaveLoadUI z listą zapisów, gracz wybiera, klika Load. Stan zrestorowany w ramach current scene (Depot DontDestroyOnLoad services).
- F5 hotkey → QuickSave (instant, bez UI, slot "quicksave")
- F9 hotkey → QuickLoad
- "Wyjdź do menu" / "Wyjdź z gry" → save best-effort + transition (już wired BUG era)

**Dlaczego nie SaveLoadUI bezpośrednio z Depot asmdef:**
Depot.asmdef NIE referuje SaveLoad (per layering convention). Hook pattern w Core utrzymuje cycle-free DI: SaveLoad bootstrap rejestruje hooks, niższe asmdefy (Core/Depot/MainMenu) wywołują przez statyczne Action delegates.

---

### TD-007 — SubsidyCalculator punctuality KPI multiplier ✅ DONE 2026-05-06

**Status:** 🟢 Resolved (M-Balance pre-EA cleanup)
**Priorytet:** M-Balance (= rebalans ekonomii)
**Estymata:** ~20 linii w `SubsidyCalculator.cs` + integracja z `TimetableExecutor` → faktycznie ~30 linii w 2 plikach
**Files:** `Assets/Scripts/Timetable/Runtime/Economy/SubsidyCalculator.cs`, `Assets/Scripts/Timetable/Runtime/Economy/EconomyManager.cs`

**Implementacja 2026-05-06:**
- `LineBalance` rozszerzony o `punctualOnTimeToday` + `punctualLateToday` + computed `PunctualityRatio`.
- `EconomyManager.OnRunDespawned` klasyfikuje run on-time (`currentDelaySec ≤ 300s` = ±5 min) vs late.
- `SubsidyCalculator.PunctualityMultiplier(lb)` curve:
  - ≥80% on-time → 1.0× (full subsidy)
  - 60-80% → 0.5× (penalty)
  - <60% → 0× (gracz traci dotację)
  - 0 ukończonych runów → 1.0× (nie penalizujemy nowo aktywowanych obiegów)
- `CalculateDailySubsidy` używa `subsidyPerRun × runs × diffMult × punctualityMult`.
- `Explain` UI text pokazuje breakdown: `punkt 75% (3/4) × 0.5`.
- Constants: `PunctualityFullThreshold = 0.80f`, `PunctualityZeroThreshold = 0.60f`, `PunctualityHalfMultiplier = 0.5f`.

---

### TD-008 — FleetPanelUI inspection column hardcoded show

**Status:** 🟢 Resolved (2026-06-15 — kod już wpięty; dodano EditMode test kontraktu)
**Priorytet:** M-Balance (= podpięcie z difficulty)
**Estymata:** ~10 linii — faktycznie już zrobione (housekeeping + test)
**Files:** `Assets/Scripts/Depot/UI/FleetPanelUI.cs:118`, `Assets/Scripts/Core/Difficulty/DifficultyService.cs`

**Rozstrzygnięcie (2026-06-15, code-reading TD-triage):**
Rejestr był nieaktualny — kod NIE jest już hardcoded. `FleetPanelUI.cs:118` to computed property
`public bool ShowInspectionColumn => DifficultyService.ShowInspectionColumnHint;`, a
`DifficultyService.ShowInspectionColumnHint => _preset != DifficultyPreset.Easy` (Easy=false, reszta=true).
Preset ustawiany przez GameCreator (`ApplyNewGameConfig`) i persistowany przez `WorldSavable`, konsumowany
warunkowo w 4 call-site'ach (FleetPanelUI.MyFleet header+row, FleetPanelUI.Market header+row). Dodano
EditMode `DifficultyServiceTests` (Easy→false, Normal/Hard/Realistic/Custom→true). Uwaga: property jest
read-only computed — NIE przywracać mutowalnego settera ani fallbacku `= true` (to był anti-pattern).
Cytowany w rejestrze `GameRules.DifficultyConfig` nie istnieje — trudność żyje w `Core.Difficulty.DifficultyService`.

---

### TD-025 — Personnel depot movement loop 3D ✅ DONE 2026-05-11

**Status:** 🟢 Resolved (full scope: PathGraph routing + state machine per rola + embed maszynisty + animacja pracy)
**Priorytet:** zrobione przed M-Models (foundation dla podmiany modeli)
**Estymata:** ~1-2 sesje → faktycznie ~1 duża sesja, 8 faz A-H
**Files:** `Assets/Scripts/Personnel/Runtime/PersonnelDispatcher3D.cs`, `Assets/Scripts/Personnel/Runtime/EmployeeWalkSimulator.cs`, `Assets/Scripts/Personnel/Runtime/CrewAssignmentService.cs`, `Assets/Scripts/Personnel/Runtime/WorkshopAssignmentService.cs`, `Assets/Scripts/Personnel/Runtime/CleaningService.cs`, `Assets/Scripts/Personnel/Data/CrewDuty.cs`, `docs/design/m8-personnel.md`

**Implementacja 2026-05-11 (full scope, decyzje user'a):**
- **Skala:** zaprojektowane pod ~200 postaci w rozwiniętej zajezdni (LOD threshold 100m już istniał z M8-10, dodano cache pathfinder per scena)
- **Animacja minimalna** — placeholder pod M-Models swap, nacisk na ruch (rotacja sway ±15° + bob ±0.03m dla `WorkingAtStation`)
- **Lead time 30 min** stała w `PersonnelBalanceConstants.CrewReportLeadMinutes` (future hook na morale)
- **Brak chodników** → fallback "z końca grafu straight-line do celu" (warning log raz per employee)
- **Idle** → `RoomType.Social` random cell (deterministic seed = employeeId)
- **Driver/Conductor embed** w pojezdzie (kapsuła hidden gdy `TrainRunSimulator.OnRunSpawned`, reappear gdy `OnRunDespawned`)
- **Universal meldunek u dyspozytora** dla wszystkich oprócz Cleaner i TicketClerk (Dispatcher sam siebie też pomija, 8s `MeldunekDurationSec`)

**Architektura:**
- Nowy enum `EmployeeWorkflowState` (10 stanów) ortogonalny do `EmployeeStatus` — `Employee.workflowState` jest `[JsonIgnore]` (NOT persisted, rebuild on load z `Employee.status`)
- 5 klas workflow `IEmployeeWorkflow` w `Personnel/Runtime/Workflows/`:
  - `DriverConductorWorkflow` — pre-duty trigger via `CrewDuty.IsImminent` + meldunek + AwaitingDeparture + GoingToVehicle + embed
  - `MechanicWorkflow` — lookup `WorkshopSlot.state == Inspecting` z `assignedWorkshopSlotIds`, walk do ServicePit
  - `CleanerWorkflow` — bez meldunku, lookup brudnych pojazdów z `VehicleLocationService.GetInDepot()`, 60s timer, `CleaningService.CompleteCleaning`
  - `WashBayWorkflow` — meldunek + lookup `OutdoorJob` typu Wash w state Servicing
  - `StaticDeskWorkflow` — wspólny Office/Research/Dispatcher/TrafficController (meldunek + biurko z `FurnitureAssignmentService` lub Social fallback)
- `PersonnelDispatcher3D` jako orchestrator — `FixedUpdate` sub-sampled co `WorkflowTickIntervalSec=1f`, dispatchuje per pracownik do workflow.Tick wg `HandlesRole`
- `EmployeeWalkPathfinder` — wrapper na PathGraph BFS, polyline waypoints, fallback straight-line gdy brak grafu
- `EmployeeVisual.StartWalk(List<Vector3> polyline, ...)` overload — interpolacja po segmentach (single-waypoint backwards compat zachowany)
- `EmployeeWalkTask.nextTask` — chain support (kolejny task auto-enqueue po onArrive)

**Cleaning realny:** `CleaningService.ApplyDailyTick` zmienione — gdy jest `Cleaner` OnShift, daily tick nie czyści. `CleanerWorkflow` per-pojazd 60s walk + clean. Fallback dla graczy bez cleanera = stary instant clean (zachowane gameplay dla niezatrudniających).

**Smoke test:** `PersonnelLifeLoopSmokeTest` MonoBehaviour z 6 ContextMenu (Setup 1-4 + Report + ForceResolve + PathGraph status).

**Debug UI:** `PersonnelMainTabUI.MyStaff` row pokazuje "Czyni: {stan}" dla OnShift agentów (PL display via `GetWorkflowStateDisplayName`).

**Następne:** post-EA polish — morale-driven lead time (przychodzi wcześniej gdy zmotywowany), per-rola animation swap w M-Models, PathGraph integration dla TrainRun parking pos lookup (obecnie `TrackGraph.GetTrackPolyline` midpoint).

**Stan obecny:**
Zatrudnienie pracownika tworzy `Employee` z domyślnym `EmployeeSchedule`.
Część ról ma już przypisania logiczne: `CrewCirculation` dla maszynisty/konduktora,
sloty warsztatowe dla mechaników, kasy, R&D, nastawnia, sprzątanie/myjnia.

`PersonnelDispatcher3D` umie spawnować pracowników ogólnie dla stanu `OnShift`,
ale nie ma jeszcze pełnego workflow poruszania się po depot: pracownik przychodzi
przez bramę/chodnik, pathfinduje do miejsca pracy lub zadania, wykonuje akcję,
zmienia destination w trakcie dnia i wraca do domu.

**Symptom user-facing:**
Pracownik po zatrudnieniu jest głównie logicznym headcountem. Nawet jeśli ma grafik
i przypisanie, nie widać pełnego rytuału pracy: wejścia do zajezdni, pathfindingu,
dojścia do stanowiska lub zadania, wykonywania pracy i powrotu do domu. Maszynista
jest najbardziej widocznym przykładem, bo powinien dodatkowo przejść odprawę
u dyspozytora przed konkretnym `CrewDuty`.

**Idealne rozwiązanie:**
Wprowadzić wspólny runtime loop postaci w depot:
- `EmployeeSchedule` decyduje, czy pracownik może dziś pracować.
- Przypisanie roli decyduje, po co przychodzi: `CrewDuty`, slot warsztatowy,
  stanowisko biurowe/R&D/dyspozytorskie, nastawnia, cleaning/wash task itd.
- Przed zmianą albo zadaniem pracownik przechodzi w `ComingToDepot`.
- Postać 3D spawnuje się przy `DepotGateMarker`/chodniku i idzie po `PathGraph`
  do celu dnia.
- Runtime obsługuje stany typu `GoingToWorkstation`, `WorkingAtStation`,
  `ReportingToDispatcher`, `AwaitingTask`, `GoingToVehicle`, `GoingHome`.
- Maszynista/konduktor mają dodatkowy pre-duty trigger:
  `CrewDuty.startTimeIso - CrewReportLeadMinutes` → odprawa u dyspozytora →
  `AwaitingDeparture`/`GoingToVehicle` → powiązanie z `TrainRun`.
- Gdy scena Depot jest nieaktywna, flow instant-resolve'uje się logicznie bez utraty
  stanu i opóźnień.

**Dokumentacja design:** szczegóły w `docs/design/m8-personnel.md`, sekcja
`9.1a Docelowy loop postaci w depot`.

---

## Performance

### TD-009 — Multi-endpoint snap brute force O(N) per frame ✅ DONE 2026-05-13

**Status:** 🟢 Resolved
**Priorytet:** zrobione przy okazji god class split (ITER 10 sesji 2026-05-13)
**Estymata:** ~50 linii w `SnapPointSystem.cs`
**Files:** `Assets/Scripts/Depot/SnapPointSystem.cs`

**Implementacja:**
Spatial grid `Dictionary<Vector2Int, List<int>> _spatialGrid` (chunk-based 50×50m bins, płaszczyzna XZ). `FindNearestSnapPoint` iteruje tylko chunki w obrębie `Mathf.CeilToInt(maxDistance/50f)` radius zamiast pełnego grafu — dla typowego `maxDistance=3f` to 9 chunków (3×3), dla `maxDistance=20f` wciąż 9. Grid build raz w `Start` po lookup `trackGraph`, rebuild w `RefreshAllSnapPoints` (= przy każdym `trackGraph.OnTopologyChanged`).

Filter (`EdgeIds.Count==0`, `Type==Throughput`, `Junction && EdgeIds.Count>=3`) wykonywany per-node w trakcie iteracji — grid trzyma wszystkie nodes, bo filter zmienia się gdy node dostaje nowe krawędzie (rebuild grid przy każdej zmianie i tak invaliduje).

Efekt:
- EA (~50 segmentów ~100 nodes): brute O(100) → grid O(~9 chunków × max 10 nodes per chunk) — neutralnie do +marginal lepiej.
- Post-EA (1000+ nodes): brute O(1000) per `FindNearestSnapPoint` × 4-12 wywołań per frame (multi-endpoint + adaptive) = 4-12k ops → grid O(9 × 10 × 4-12) = 360-1080 ops. ~10× speedup.

---

### TD-011 — Tile parsing sync na main thread (1-3s freeze fat tile)

**Status:** 🟡 Not fixed (sync stable, decyzja odłożona do M-Performance po pomiarze)
**Priorytet:** M-Performance MP-X lub post-EA (jeśli profile pokaże potrzebę)
**Estymata:** ~3-5 sesji pełny (dedicated thread + Channel queue + back-pressure + walidacja).
**Files:** `Assets/Scripts/Map/Loading/MapLoader.Streaming.cs` (StreamAllTilesSync), `Assets/Scripts/Map/Loading/MapLoader.Tiles.cs` (ParseTileLogicLayers)

**2026-05-06 → 2026-05-14: experimental async path porzucony.**
- 2026-05-06 dodano `StreamAllTilesAsyncCoroutine` (Task.Factory.StartNew LongRunning + max 2 concurrent + manual back-pressure + NoGCRegion 2GB bound).
- Zero callerów (smoke test nigdy nie wykonany) → 2026-05-14 cleanup usunął ścieżkę async wraz z `StreamAllTilesCoroutine` (yield-based, dead post-M-PL) i `EnsureAllTilesLoadedCoroutine` (Obsolete). Plik 580 → 169 linii.
- NoGCRegion 2GB bound usunięty razem z metodami — produkcyjny `StreamAllTilesSync` go nie używał.
- Wniosek: gdy faktycznie potrzebny async, lepiej dedykowany `System.Threading.Thread` + Channel queue z walidacją (semantic preservation contract M-Performance), nie kolejny opt-in eksperyment bez callerów.

**Stan obecny:**
Tile parsing dla pełnej Polski (5624 tiles) wykonuje się **synchronicznie na main thread** w `StreamAllTilesSync`. Większość tile to empty (Bałtyk/okrajowe) — fast-path skip. Non-empty tile ("fat tile" — 1-3s parse time) blokuje main thread przez cały parse — Unity nie respondueje przez ten okres. Editor "not responding" modal może się pojawić → Wait.

Workarounds w obecnym kodzie żeby żyło:
- Empty-tile fast-path (mask=0 || size=0 → skip bez parse). Bałtyk/okrajowe = ~3000/5624 tile skip'owanych bez kosztu.
- **Brak `GC.Collect()`** w pętli — to **BYŁA przyczyna hangu** (heap fragmentation, każda kolejna iteracja blokowała coraz dłużej).
- **Brak `GC.GetTotalMemory(false)`** w hot path — może triggerować GC.
- Sync (bez yieldów) — coroutine yield powodował Editor degradation (asset refresh tick na każdym yieldzie → coraz dłuższy hang).

**Próbowane rozwiązanie (porzucone):**
Background thread parsing via `Task.Run(() => ParseTileLogicLayers(tileID))` — `ParseTileLogicLayers` nie używa Unity API, bezpieczne na worker. **Hang po ~3500 tasks** — Mono ThreadPool scheduling degradation (krótkotrwałe taski się gromadzą). Sync prostsze + predictable, M-PL load <15s wystarcza pre-EA.

**Idealne rozwiązanie (post-EA / M-Performance):**
Dedykowany **`System.Threading.Thread`** (long-running zamiast ThreadPool) + **Channel queue** dla tile work items + back-pressure (max N tiles in-flight). Eliminuje hang ThreadPool'a + utrzymuje 1-2 worker'y stabilnie.

Alternatywa: `Task.Run` z `TaskCreationOptions.LongRunning` (skipuje ThreadPool, każdy task na dedykowanym thread'zie).

Wymaga:
- Stress test framework (M-Performance MP-1) żeby zmierzyć faktyczny zysk vs sync.
- Walidacja semantic preservation (M-Performance contract) — output identyczny przed/po.
- Decyzja czy 1-3s freeze faktycznie boli graczy (playtest M-Balance). Jeśli nie — sync wygrywa prostotą.

**Decyzja:** odłożone do M-Performance. Pre-EA sync wystarcza (M-PL load <15s, freeze tylko podczas initial extraction nie podczas gameplay'u). Profile-driven decision wymagana — bez stress test framework decyzja byłaby na ślepo.

---

### TD-013 — N² block contention search w TrainRunSimulator.Priority ✅ DONE 2026-05-06

**Status:** 🟢 Resolved (M-Performance follow-up post-MP-9)
**Priorytet:** M-Performance (wpisał się w MP-3 spatial indexes wzorzec)
**Estymata:** ~30 linii → faktycznie ~80 linii (helper SyncBlockWaitIndex + UnregisterFromBlockWaitIndex + lookup refactor + cleanup w DespawnTrain)
**Files:** `Assets/Scripts/Timetable/Runtime/Simulation/TrainRunSimulator.Priority.cs`, `TrainRunSimulator.cs`, `TrainRunSimulator.Movement.cs`, `TrainRunSimulator.Spawn.cs`

**Stan obecny:**
`HasHigherPriorityWaiting(blockKey, myTrainRunId, myPriority)` iteruje **wszystkie aktywne pociągi** żeby znaleźć te które czekają na ten sam blok z wyższym priorytetem:

```csharp
foreach (var kvp in _activeTrains)  // O(N)
{
    var other = kvp.Value;
    if (other.trainRun.id == myTrainRunId) continue;
    if (other.state != TrainState.BlockedBySignal) continue;
    int otherNextBlkIdx = other.currentBlockIndex + 1;
    if (otherNextBlkIdx >= other.routeBlockCount) continue;
    if (other.routeBlockKeys[otherNextBlkIdx] != blockKey) continue;
    ...
}
```

Wywoływane per-block contention check w `Advance` (FixedUpdate 50Hz). Przy 50 pociągach = max 50 comparisons per check, ~kilka checks per frame. Akceptowalne.

**Skala problemu przy endgame fanatyk** (M-Performance target = 1000+ pociągów):
- 1000 trains × 50Hz × kilka checks/frame = miliony comparisons/sec
- Większość zwraca `continue` na drugim if (`state != BlockedBySignal`) — 95% jednak żyje
- Realne O(N²) tylko na zatłoczonych blokach (rush hour aglomeracja)

**Implementacja 2026-05-06:**
- 2 indexy w `TrainRunSimulator`:
  - `_trainsWaitingForBlock: Dictionary<int blockKey, HashSet<int trainIds>>` — set kandydatów per blok
  - `_currentlyWaitingForBlock: Dictionary<int trainId, int blockKey>` — backing index dla lazy sync (skąd unregister)
- **Lazy sync per Advance:** `SyncBlockWaitIndex(SimulatedTrain st)` wywoływany na początku `Advance`. Sprawdza target blockKey vs stored, dodaje/usuwa różnicę. O(1) ops per call.
- **Wybrany approach lazy zamiast wrapper'owania state setters** — `state = TrainState.X` jest set w 15+ miejscach (Movement.cs/Breakdowns.cs/Spawn.cs), wrapper'owanie wszystkich = inwazyjne + risky regresji. Lazy sync per-frame eliminuje 100% inconsistency window (frame-by-frame).
- **Cleanup w `DespawnTrain`:** `UnregisterFromBlockWaitIndex(trainId)` zwalnia entry z indexów.
- **HasHigherPriorityWaiting refactor:** `_trainsWaitingForBlock.TryGetValue(blockKey, out candidates)` → iter tylko kandydatów (zazwyczaj 0-3). Defensive re-validate (state + block) wewnątrz pętli na wypadek race między sync a lookup.

**Speedup:**
- Skala O(N²) → O(k²) gdzie k = pociągów czekających *na ten konkretny blok* (rzadko >2-3, bo bloków jest dużo)
- Przy 1000 trains target endgame: ~50M ops/s → ~150k ops/s = **~330× redukcja**

**Wzorzec analogiczny do MP-3** (`PassengerManager._agentsByStation`/`_agentsOnTrain` lookup po stationId/runId). Semantic preservation OK: lazy sync gwarantuje że indeks i state są zsynchronizowane na każdej klatce (within frame budget).

---

### TD-014 — MapLoader/TileManager single-file (multi-country DLC bloker)

**Status:** 🔴 Active — bloker dla M-DLC milestone'u
**Priorytet:** M-DLC (planowane po M-Performance, przed M12 Polish)
**Estymata:** 2-3 sesje (refactor `MapLoader.mapFileName: string` → `List<MapSource>` + merge tile lookup w `TileManager` + cross-source feature aggregation)
**Files:** `Assets/Scripts/Map/Loading/MapLoader.cs`, `Assets/Scripts/Map/Loading/TileManager.cs`, `Assets/Scripts/Map/Loading/MapLoader.LoadFormats.cs`, `Assets/Scripts/Map/Loading/MapLoader.Tiles.cs`

**Stan obecny:**
[MapLoader.cs:33](../Assets/Scripts/Map/Loading/MapLoader.cs:33): `public string mapFileName = "Maps/Poland/poland-v7.bin";` — **single string, single file**. Jeden `tileIndex: Dictionary<long, BinaryFormat.TileIndexEntry>`, jedne `globalBounds`. Cały MapLoader API zakłada single source.

DLC scenariusz wymaga:
- Załadować równocześnie `poland-v7.bin` + `germany-v7.bin` + `czechia-v7.bin` itd.
- Merge tile indexes (TileID są globalne — Cantor-pair, no kolizji)
- Merge global bounds → unified bbox
- Cross-source feature aggregation w `GetAllFeaturesAcrossTiles` (już używane przez `RailwayFeatureCollector`/`AdminBoundaryLoader`/`StationLoader`/`PlaceLoader`)

**Dependency:**
- Foundation (data structures) zrobione w sesji 2026-05-04 — [docs/design/dlc-multi-country.md](design/dlc-multi-country.md) "Co dodano w sesji 2026-05-04".
- formap (generator) już 80% gotowy (CLI `--country DE`, per-country `init-state-<XX>.bin`).

**Idealne rozwiązanie (M-DLC-2):**
```csharp
// Zamiast single string:
public List<MapSource> mapSources = new() { new MapSource("Maps/Poland/poland-v7.bin", "PL") };

public class MapSource {
    public string fileName;
    public string countryCode;
    public Dictionary<long, BinaryFormat.TileIndexEntryV7> tileIndex;
    public BBox bounds;
}
```
- `MapLoader.LoadAllMaps()` ładuje każdy plik osobno, agregując bounds + tile indexes
- `TileManager` pyta multi-source lookup (TileID może należeć do dowolnego active mapSource)
- `GetAllFeaturesAcrossTiles()` iteruje po wszystkich active map sources
- Hot-swap: dodaj/usuń `MapSource` runtime gdy gracz kupi DLC → re-trigger initialization

**Decyzja:** odłożone do M-DLC milestone'u (post M-Performance, pre M12 Polish). Pełen plan etapów: [docs/design/dlc-multi-country.md](design/dlc-multi-country.md). Sweep "ActiveCountriesOverlayService faktyczna multi-country logic" + "DlcLockRenderer per-region" + "Save migration" idą w pakiecie.

---

### TD-029 — UIPrimitives brakuje helperów dla scroll content + LayoutGroup defaults

**Status:** 🟢 Resolved (2026-06-15 — 4 helpery dodane: `StretchTop`/`AddHLG`/`AddVLG`/`MakeSlider` + EditMode `UIPrimitivesTests`)
**Priorytet:** M-UIPolish lub okazyjnie (gdy następna sesja dotyka UI z innego powodu)
**Estymata:** ~50 linii w `UIPrimitives.cs` (helpers) + opcjonalnie refactor 6 callsite (~30 linii)
**Files:** `Assets/Scripts/SharedUI/UIPrimitives.cs` + 6 wystąpień bug pattern w callsite (patrz niżej)

**Implementacja (2026-06-15):**
Dodane do `UIPrimitives` (low-level, bez theme padding): `StretchTop(go/rt)` (top-stretch dla scroll
content — pełna szerokość, pivot y=1, sizeDelta zero), `AddHLG`/`AddVLG` (LayoutGroup na istniejący GO
z `childControlWidth/Height=true` defaultem — pułapka #2), `MakeSlider` (BG+Fill Area/Fill+Handle Slide
Area/Handle z poprawnymi kotwicami — **wyciągnięte ze sprawdzonego `SettingsScreenUI.BuildSliderInternal`**:
symmetric offsets 10/-10 + handle anchors center, czyli fixy z 2026-05-17). 6 callsite z bug-patternem
było JUŻ naprawione inline (2026-05-17) — to czysta ekstrakcja prewencyjna, callsite NIE migrowane
(zero ryzyka regresji). EditMode `UIPrimitivesTests` pokrywa `StretchTop` (kotwice/pivot/sizeDelta +
null-safety); AddHLG/AddVLG/MakeSlider używają typów `UnityEngine.UI` niedostępnych w test asmdef
(overrideReferences=true) — defaulty są explicit, slider z proven kodu. Dla themed kontenera dalej
`UIBuilders.MakeContainer` (jasny podział: UIPrimitives=low-level, UIBuilders=themed).

**Kontekst (sesja UI polish 2026-05-17):**
W jednej sesji naprawiłem **6 wystąpień tego samego bug pattern** w 6 różnych plikach UI. Brak shared helpers powoduje że każdy nowy panel UI **trafia w te same dwie pułapki**.

**Pattern 1: ScrollRect content `sizeDelta` default (2 wystąpienia):**
```csharp
// Powtórzony pattern w DepotLocationPickerUI._resultsContent + SaveLoadUI._slotsContent:
var content = new GameObject("Content", typeof(RectTransform));
content.transform.SetParent(viewport.transform, false);
_resultsContent = content.GetComponent<RectTransform>();
_resultsContent.anchorMin = new Vector2(0, 1);
_resultsContent.anchorMax = new Vector2(1, 1);
_resultsContent.pivot = new Vector2(0.5f, 1);
// CRITICAL: brak `_resultsContent.sizeDelta = Vector2.zero` → Unity default (100, 100)
//          → content.width = viewport.width + 100, pivot (0.5, 1) → przesunięty 50px na lewo
//          → Mask viewport clipuje → items wyglądają jak ucięte od lewej (50% width)
```

Bug ten sam, debugowany 2 razy w jednej sesji (commits `eef9f7d`, `cc22efc`).

**Pattern 2: HLG/VLG `childControlWidth=false` (4 wystąpienia):**
```csharp
// Powtórzony pattern w MainTabBarUI.BottomBar + SettingsScreenUI.BottomBar +
// SettingsScreenUI.RebindRow + SettingsScreenUI.MakeRow:
var hl = bar.AddComponent<HorizontalLayoutGroup>();
hl.padding = new RectOffset(20, 20, 12, 12);
hl.spacing = 12f;
hl.childAlignment = TextAnchor.MiddleRight;
hl.childControlWidth = false;   // ← bug
hl.childControlHeight = false;
hl.childForceExpandWidth = false;
hl.childForceExpandHeight = false;
```

Z `childControlWidth=false`, HLG **ignoruje** `LayoutElement.preferredWidth` na children. Children dostają default `sizeDelta (100, 100)` z `new GameObject(typeof(RectTransform))`. Skutek:
- Buttons "Resetuj sekcję" / "Resetuj wszystko" / "Anuluj" / "Zastosuj" miały być po 180px → faktyczne 100px → tekst nie mieścił się, buttons nakładały się.
- Klawiszologia: DisplayShell (literka klawisza) 180px → 100px → przykrywany przez ChangeBtn "Zmień".

Naprawione w commitach `b1638f7`, `6476bf6`.

**Plus slider builder (3 osobne bugi w jednym sesji):**
Slider w `SettingsScreenUI.RowBuilders.BuildSliderInternal` miał:
1. Fill Area offsets (5/-15) ≠ Handle Slide Area offsets (10/-10) → asymetria, fill visual nie matchował thumb position
2. Handle anchors NIE set explicit → default (z `new GameObject`) lub stretched → handle wyglądał jak podłużny prostokąt zamiast kółka 20×20
3. Value text init `getter().ToString("F2")` było OK, ale **standard slider** wymaga manual setup wszystkich sub-components

Naprawione w commitach `a0649a6`, `b11bfa5`.

**Idealne rozwiązanie:** dodać 3 helpery w `UIPrimitives.cs`:

```csharp
/// <summary>Top-anchored stretch dla scroll content. Eliminuje sizeDelta default trap.</summary>
public static RectTransform StretchTop(GameObject content)
{
    var rt = content.GetComponent<RectTransform>() ?? content.AddComponent<RectTransform>();
    rt.anchorMin = new Vector2(0, 1);
    rt.anchorMax = new Vector2(1, 1);
    rt.pivot = new Vector2(0.5f, 1);
    rt.sizeDelta = Vector2.zero;
    rt.anchoredPosition = Vector2.zero;
    return rt;
}

/// <summary>HLG z sane defaults — childControlWidth/Height=true (LayoutElement respektowany).</summary>
public static HorizontalLayoutGroup AddHLG(
    GameObject go,
    int padLeft = 0, int padRight = 0, int padTop = 0, int padBottom = 0,
    float spacing = 0f,
    TextAnchor alignment = TextAnchor.MiddleLeft)
{
    var hl = go.AddComponent<HorizontalLayoutGroup>();
    hl.padding = new RectOffset(padLeft, padRight, padTop, padBottom);
    hl.spacing = spacing;
    hl.childAlignment = alignment;
    hl.childControlWidth = true;        // sane default (LayoutElement honored)
    hl.childControlHeight = true;
    hl.childForceExpandWidth = false;
    hl.childForceExpandHeight = false;
    return hl;
}

/// <summary>Slider z handle 20×20 kółkiem, fill area symmetric, value text. Single source.</summary>
public static (Slider slider, TextMeshProUGUI valueText) MakeSlider(
    Transform parent,
    float min, float max, float value,
    bool wholeNumbers = false,
    float preferredWidth = 260f)
{
    // ... wszystkie sub-components z poprawnymi anchors + offsets ...
}
```

Plus opcjonalnie: refactor 6 callsite żeby używały nowych helpers (proof-of-concept że eliminują boilerplate).

**Decyzja kiedy:** nie krytyczne teraz (22 commity polish UI dziś, foundation visual ma większy ROI), ALE każdy następny panel UI (Fleet/Timetable polish, M-UIPolish kontynuacja) trafi w te pułapki. Naturalna okazja: **gdy następnym razem ruszam UI z innego powodu**, dorzucić helpers + zrefaktoryzować ten panel jako proof-of-concept.

**Powiązane commity (sesja 2026-05-17):**
- `eef9f7d`, `cc22efc` — sizeDelta default fix (2×)
- `b1638f7`, `6476bf6` — HLG childControlWidth fix (4×)
- `a0649a6`, `b11bfa5` — slider Fill Area / Handle anchors (3 bugi w slider builder)

---

## QoL / Future

### TD-010 — Atomic undo schematu (post-EA)

**Status:** 🟢 Resolved (2026-06-15 — `SchemaPlacementCommand` diff-based + PlayMode test; przy okazji naprawiony latentny bug zombie-tor)
**Priorytet:** post-EA QoL feature
**Estymata:** ~200 linii (= group tracking + atomic undo command) — faktycznie ~120 linii
**Files:** `Assets/Scripts/Depot/Undo/UndoCommands.cs`, `Assets/Scripts/Depot/Schemas/Placement/TurnoutSchemaPlacer.Placement.cs`

**Implementacja (2026-06-15):**
`ConfirmPlacement` silence'uje per-element undo (`UndoManager.Silenced=true`) na czas `PlaceSchemaInWorld`
i nagrywa JEDEN `SchemaPlacementCommand` (jeden Ctrl+Z cofa cały schemat). Komenda trzyma finalne
GraphTrackId schematu (standalone + members) + id rozjazdów.

**Undo jest diff-based — bo odkryto latentny bug:** `PrefabTrackBuilder.RemoveTurnout` AUTO-ODTWARZA
skonsumowany chain jako NOWY tor (`PlaceTrackWithPolyline`, nowy id). Naiwne "usuń rozjazdy + usuń tory
po id" (i tak działał stary per-element undo) zostawiało ten odtworzony chain jako **osieroconą
geometrię (zombie-tor)** po pełnym cofnięciu schematu z rozjazdem. `SchemaPlacementCommand.Undo`:
(1) snapshot żywych track-id, (2) usuń rozjazdy w odwrotnej kolejności (każdy odtwarza chain = nowe id),
(3) usuń chainy nieobecne w snapshocie (= odtworzone, schema-owned), (4) usuń pozostałe standalone tory.
Refundy lecą przez istniejące RemoveTrack/RemoveTurnout → net-zero (TD-035). PlayMode
`DepotSchemaUndoPlayTests`: schemat z rozjazdem → 0 torów po undo (asercja braku zombie) + net-zero;
schemat samych torów → wszystko cofnięte + net-zero. Odwraca wcześniejszą decyzję won't-fix.

---

### TD-015 — MaybeSpawnAgents per-attempt modifiers compute (MP-4.5 followup)

**Status:** 🟢 Resolved (2026-06-15 — memo per-call `_demandModifierCache`, output-identyczne, zero dryfu)
**Priorytet:** M-Balance lub post-EA tuning (=opcjonalny, nie wymagane dla target endgame)
**Estymata:** ~1 sesja (~50 linii w `PassengerManager.cs`) → faktycznie ~20 linii
**Files:** `Assets/Scripts/Timetable/Runtime/Economy/PassengerManager.cs`

**Implementacja (2026-06-15):**
`MaybeSpawnAgents` memoizuje scalar `commute×reputation×offerFreq` per **kierunkowa** para (from,to)
w `_demandModifierCache` (klucz `PackStationPair` = `from<<32|to`, kierunkowy bo commute zależy od
asymetrii fromImp/toImp). **Wybrano memo PER-CALL (Clear na początku MaybeSpawnAgents), NIE cache w
RefreshActiveStations co 30s** jak sugerował rejestr — bo commute zależy od `gameTime.Hour/DayOfWeek`,
a per-call gameTime jest stały, więc wynik jest **identyczny** z poprzednim per-attempt compute (zero
dryfu na granicach godzin peak — zgodne z kontraktem semantic-preservation M-Performance). Powtórzone
pary w pętli (do `spawnBudget×10` attempts) nie liczą 3× modyfikatorów ponownie. Pure memoizacja
(output-identyczny z konstrukcji) → brak osobnego testu; ścieżka pomiaru = `PerfStressBootstrap`.

**Stan obecny:**
Po MP-3+MP-4 hot paths z spec'a (HandleTrainArrivingAtStop / CountOffersOnPair / CalculateCapacity) usunięte z budgetu. Pomiar 2026-05-06 ujawnił **nowy hotspot stress-only**: `MaybeSpawnAgents` 33-36ms self time × 5 worst frames przy stress test (cap=600k > agents=500k → spawn budget 1000 × 10000 max attempts × per-attempt sub-operations: GetCommuteModifier compute, GetDemandFactor lookup, GetOfferFrequencyModifier compute, 2× Random calls). Cache `_offersOnPairCache` (MP-4) dał już O(1) dla offers, ale modifiers wciąż compute per-attempt.

**Real-world impact:** **niewielki** — w gameplay cap=50k naturalny → spawn budget tiny → return early po jednym frame, brak persistent loop. Hotspot widoczny tylko w stress test extremum.

**Idealne rozwiązanie:**
Cache `_demandModifierCache: Dictionary<long packedKey, float>` z commute × reputation × timeOfDay scalar per OD pair. Refresh w `RefreshActiveStations` co 30s razem z `_offersOnPairCache`. `MaybeSpawnAgents` per-attempt: `effectiveDemand = baseDemand * cachedModifier * difficultyMult` zamiast 4 compute calls. Spodziewany speedup w stress: 30ms → ~3ms (~10× redukcja).

**Decyzja:** odłożone do **M-Balance lub post-EA tuning**. Target endgame (1000/500k recommended/minimum) osiągnięty bez tego — patrz [docs/design/performance-decision-log.md](design/performance-decision-log.md) MP-5 ścieżka A.

---

### TD-016 — OD matrix building brute force O(N²) na pełnej Polsce ✅ DONE 2026-05-06

**Status:** 🟢 Resolved (M-Performance follow-up, post-M-Performance closeout)
**Priorytet:** post-M-Performance (był: startup cost, nie symulacja)
**Estymata:** ~2-3 sesje → faktycznie ~1 sesja (~120 linii w `OriginDestinationMatrix.Build`)
**Files:** `Assets/Scripts/Timetable/Runtime/Economy/OriginDestinationMatrix.cs`

**Symptom user'a:**
Przy `TryBuildODMatrix()` na pełnej Polsce (kilka tysięcy stacji) ~10s freeze z `Logs: "OD matrix gotowa: 4327338 par"`. Zauważone podczas MP-1 stress test (PerfStressBootstrap.RunStressTest) ale to **startup cost**, nie symulacja runtime — wystąpi też przy każdym TryBuildODMatrix call (np. po nowej dacie startu gry).

**Stan obecny:**
- `OriginDestinationMatrix.Build(stations, importance)` iteruje wszystkie pary stacji (N×N) — ~3000² = 9M par teoretyczne (po filtrach 4.3M aktualnych)
- `StationImportance.CalculateAll(stations, places, platforms, graph)` też iteruje brute force
- Brak spatial grid lookup — analogiczny problem do tych z M-PL (generator JSON, FindTracksForStop, FindNodeOnTrack), wszystkie naprawione przez spatial grid

**Implementacja 2026-05-06:**
- **Pre-filter pass** O(N): `validStations: List<ValidStation>` w jednym przebiegu eliminuje wewnętrzne TryGetValue(importance) + pathNodeId<0 checks (były ~9M razy w starym brute force).
- **Spatial grid bin'ing**: `Dictionary<long cellKey, List<int validIdx>>` z 100 km cells. CellKey packed long (high32=cx, low32=cy).
- **Forward-only neighbor iter** żeby uniknąć duplikatów: `dx > 0 || (dx == 0 && dy > 0)` — każda para cell-A × cell-B procesowana dokładnie raz. Plus pary wewnątrz tej samej cell (a < b).
- **SqrMagnitude pre-reject** (CutoffMSqr = 800km²): eliminuje sqrt dla par dalszych niż cutoff. Bezpieczny cutoff 800km — pary dalsze mają demand <0.1 nawet dla max importance × importance, więc były odrzucone w starym kodzie po `if (demand < 0.1f) continue`.
- **DLC ready**: cell size 100km + cutoff 800km wystarczają dla EU multi-country (Niemcy + Polska + Czechy ~2000×1500 km, gdzie spatial grid eliminuje 80%+ par).

**Speedup (oczekiwany):**
- PL (~3000 stacji, 4.3M par teoretyczne): ~10s → ~3-5s (~2-3× redukcja). Polska to mały dense graph, większość par wciąż w cutoff.
- DLC EU: ~30s → ~3-5s (~10× redukcja, bo spatial grid eliminuje pary z odległych krajów).

**Walidacja semantic preservation:**
- Bezpieczny cutoff 800km gwarantuje że żadna para legalna w starym kodzie nie została wycięta (demand <0.1 ≡ odrzucona w obu wersjach).
- Forward-only iter zapewnia każda para zaprocesowana raz (brak duplikatów ani gubienia).
- Sanity check: `pairs` count + `TotalDemand()` bit-exact identyczne pre/post (z floating-point tolerance ~10⁻⁶). Log per Build pokazuje counts + processed pairs ratio do brute force.

---

### TD-017 — Auto-save GC pressure 4.3MB burst

**Status:** 🟢 Resolved (2026-06-15 — offload serializacji/gzip/HMAC na worker thread; wariant zachowawczy, nie pełny two-phase)
**Priorytet:** post-EA polish
**Estymata:** ~1-2 sesje (async / throttle save) → faktycznie ~10 linii
**Files:** `Assets/Scripts/SaveLoad/Runtime/SaveOrchestrator.cs`, `Assets/Scripts/SaveLoad/Runtime/LocalDiskStorage.cs`

**Implementacja (2026-06-15) — wariant zachowawczy (opcja #2 częściowo):**
Dwa najcięższe przebiegi serializacji są **czystymi funkcjami** (JObject → bajty, zero Unity API)
operującymi na niemutowalnym już bundlu → przeniesione na worker thread przez `Task.Run`:
`HmacService.ComputeHmac(bundle)` (`SaveOrchestrator.SaveAsyncInner`) + `BundleSerializer.Serialize`
(JSON+gzip, `LocalDiskStorage.SaveAsync`). Output **byte-identyczny** (te same funkcje, inny wątek) →
HMAC/determinizm/round-trip nietknięte; main-thread burst spada o część serialized-bytes + gzip +
HMAC. Bezpieczeństwo: bundle jest prywatny dla operacji save i zamrożony po pętli `module.Serialize()`,
główny wątek czeka na `await` → brak race z game loopem (JObjecty to deep-copy stanu z momentu Serialize).

**Świadomie NIE zrobione (pełny two-phase ISavable):** budowa JObjectów w `module.Serialize()` zostaje
na main (nieuniknione dla modułów z Unity API: Depot/Maintenance/Fleet używają `.Instance`/
`FindAnyObjectByType` + mutujących statics). Pełny CaptureSnapshot(main)→SerializeFromSnapshot(worker)
dla 13 modułów = duże ryzyko dla najbardziej zahardenowanego systemu (20-fix audit 2026-05-15) przy
low-impact/post-EA itemie — over-engineering. Zostaje jako udokumentowana opcja gdyby profiler po EA
pokazał, że JObject-building POD modułów (PassengersSavable kolumnowo) nadal boli. Throttle (opcja #1)
już istnieje (`AutoSaveIntervalGameSec`).

**Symptom:**
Profiler pomiar 2026-05-06 (PerfStressBootstrap stress + Profiler attached) pokazał **4.3 MB allocation w jednym frame** z stack trace `RailwayManager.SaveLoad.dll!RailwayManager.SaveLoad::AutoSave...`. Plus `GC.Collect 15ms` + `GC.Finalizer 8ms` w worst frames jako konsekwencja.

**Stan obecny:**
Auto-save odpala się synchronicznie na main thread — full snapshot serialization (Fleet + Timetable + Personnel + Depot + Economy + Maintenance via reflection-discovered ISavable) w jednym ticku. Powoduje GC spike + frame hitch. W gameplay user widzi krótki freeze (~50-100ms na recommended spec).

**Idealne rozwiązanie:**
1. **Throttle:** redukować częstotliwość auto-save (np. raz na 30 min game time vs current N min)
2. **Async serialization:** wykonać serialize na worker thread (Task.Run), main thread tylko stop-the-world dla snapshot deep-copy
3. **Streaming write:** zapisywać do tymczasowego pliku partami zamiast pełny w pamięci

**Decyzja:** **post-EA polish**. W EA gracz akceptuje auto-save hitch jako standard pattern w grach symulacyjnych. Throttle/async to QoL, nie blocking.

**Tymczasowo:** **PerfStressBootstrap recommendation:** wyłącz auto-save w settings przed stress testem (eliminuje GC noise w pomiarze). Dodać do `docs/design/performance-baseline.md` jako setup tip.

---

### TD-026 — MapRenderer hierarchia ~45k GameObjects na pełnej Polsce

**Status:** 🟡 Krok A done 2026-05-16 (45k → ~2.5k GO), Krok B follow-up jeśli profile pokaże potrzebę
**Priorytet:** M-Performance lub post-EA
**Estymata:** Krok A done (~30 min, commit a56385a). Krok B: 1-2 sesje (deep flatten).
**Files:** `Assets/Scripts/Map/Rendering/MapRenderer.Tiles.cs` (RenderTile), `Assets/Scripts/Map/Rendering/MapRenderer.Layers.cs` (CreateLayerObject, FlushBatchToMesh), `Assets/Scripts/Map/Rendering/MapRenderer.Markers.cs` (RenderPOILayer)

**2026-05-16 — Krok A wykonany (commit a56385a):**
`Mesh.indexFormat = UInt32` w CreateMeshObject + usunięcie split na 60k vertex w RenderFeatureBatch. Wszystkie features layer'a trafiają do jednego mesh. Hierarchia spada ~45k → ~2.5k GO geometry (94% redukcja). Inspector fields `maxVerticesPerMesh` i `combineMeshes` (dead) wyrzucone. LOD visibility logic per `Layer_X` SetActive zachowana bez zmian. Bonus: mniej draw calls per layer.

**Stan obecny (post-Krok A):**
Każdy tile:
```
Tile_X_Y
├── Layer_Highways         (1 GO, MeshFilter+MeshRenderer)
├── Layer_Railways         (1 GO, ale 3 child mesh GO dla mainline/disused/transit grouping)
├── Layer_Buildings        (1 GO)
├── Layer_POIs
│   ├── Places             (sub-container z 50k labels — TD-027 zakres)
│   ├── Stations           (sub-container z 3k markers — TD-027 zakres)
│   └── Signals_Hidden     (sub-container, disabled)
└── Layer_... (8 layer types × 1 GO)
```

Pełna Polska (256 cache LRU) × ~10 layer GO/tile = **~2.5k GameObjects** geometry. Plus POI labels (50k+ Places, 3k Stations) — to TD-027 zakres.

**Krok B (potencjalny follow-up): deep flatten do 1 GO per tile z multi-submesh**

Wymagałby:
- `Tile_X_Y` ma bezpośrednio MeshFilter+MeshRenderer (zamiast Layer sub-children)
- Mesh ma N submeshes (po jednym per LayerType), MeshRenderer ma N materials
- LOD visibility musi być przez **material swap** zamiast SetActive (Unity nie wspiera disable per submesh natywnie)

**Trade-offs Krok B:**
- ✅ Dalsza redukcja 2.5k → ~256 GO geometry (90% additional)
- ✅ POI hierarchy pozostaje osobno (TD-027 osobny temat)
- ✅ Internal-only API — ToggleLayer/GetLayerObject mają 0 external callerów (grep 2026-05-16), bezpieczna reimplementacja
- ❌ **LOD visibility material swap** — wymaga 256 tiles × 10 layers × material array re-set per LOD change. Realne ryzyko visual regression przy progu zoom.
- ❌ Vertex limit 65k znika (Krok A już dał UInt32) — submesh wzdłuż całego tile bez limitu.
- ❌ Wymaga **Unity walidacji per LOD level** (6 thresholds zoomLOD1..5 + default) — nie da się "blindly" jak Krok A.

**Trzy opcje implementacji LOD visibility w Krok B:**
| Opcja | Co | Ryzyko |
|---|---|---|
| Material swap | per LOD change set materials[i] = transparent/real | visual regression możliwe (kolejność material vs submesh) |
| Selective mesh rebuild | przy LOD change SetIndices z tylko visible layers | kosztowne (300k vertex SetIndices = lag spike na progu zoom) |
| Pre-build per-LOD meshes | N meshów per tile, swap MeshFilter.mesh | pamięć × LOD count — niedopuszczalne |

**Decyzja Krok B (2026-05-16):** **świadomie odłożone** — user potwierdza brak problemów wydajnościowych w aktualnym workloadzie. Włączenie wymaga:
1. **Profile evidence** z M-Performance benchmark pokazujący że 2.5k GO geometry to bottleneck (mało prawdopodobne — POI labels 50k są większym problemem, ale to TD-027).
2. **1-2 sesje refactor** + **1-2h Unity walidacji** per LOD level.
3. Wybór jednej z opcji A/B/C powyżej (preferowane: material swap z explicit "hidden" material per layer).

Jeśli kiedyś włączymy: priorytet niski post-EA, raczej M-Balance lub publish polish.

---

### TD-027 — StationMarker 3000 colliders + OnMouseEnter/Down legacy event handlers

**Status:** 🔴 Active
**Priorytet:** M-Performance lub post-EA (gdy player zauważy lag w high-density Warszawa view)
**Estymata:** 2-3 sesje (centralny raycaster + spatial index + migracja `StationMarker` API)
**Files:** `Assets/Scripts/Map/POI/StationMarker.cs`, `Assets/Scripts/Map/Rendering/MapRenderer.Markers.cs` (CreateStationMarker), `Assets/Scripts/Map/UI/MapUIManager.cs` (nowy centralny raycaster)

**Stan obecny:**
~3000 stacji na pełnej Polsce, każda z `BoxCollider` size = `Vector3.one * stationIconSize * 1.5f` (default 45m). Plus każda ma `StationMarker` MonoBehaviour z legacy Unity event handlers (`OnMouseEnter`, `OnMouseExit`, `OnMouseDown`).

**Problemy:**
- **Per-click raycast** Unity skanuje 3000 colliderów żeby znaleźć clickable target. Spatial hash w Unity Physics2D/Physics jest, ale przy gęstej aglomeracji (Warszawa, Śląsk) wiele colliderów może być w jednym raycast hit.
- **`OnMouseEnter/Exit/Down`** to legacy API — wymaga `Camera.eventMask` setupu + bardzo wolny w gęstych scenach.
- **3000 MonoBehaviours** to overhead — każdy Awake/Start/OnDestroy. Plus `Start()` w StationMarker robi `transform.Find("Icon")` + `GetComponent<Renderer>` — × 3000 to ~10-50ms cumulative przy spawn'ie.

**Idealne rozwiązanie:**
1. **Centralny raycaster** w `MapUIManager`:
   - Pojedyncze `Update()` z `Mouse.current.position` → `Camera.ScreenPointToRay`.
   - Spatial index dla stacji (kdtree po `Vector2` world position) — O(log N) lookup zamiast O(N) raycast.
   - Manual hit detection: ray ↔ ground plane intersection + spatial lookup najbliższej stacji w radius `stationIconSize * 1.5f`.
2. **`StationMarker` jako lekki POCO** (struct) zamiast MonoBehaviour:
   - Pole `stationName/stationType/position` w `Dictionary<int, StationInfo>` w MapUIManager.
   - Visual (icon GO) bez collider, bez MonoBehaviour — tylko `Transform + MeshRenderer + sharedMaterial`.
   - Event `OnAnyStationClicked` zostaje statyczny, emitowane przez centralny raycaster.
3. **Hover highlight** obsłużone podobnie — pojedyncze update z najbliższą stacją w hit radius.

**Bonus:**
- Per-tile colliders cleanup — `UnloadTile` destroys 3000 colliders + 3000 MonoBehaviours przy LOD switch. Centralny approach eliminuje ten overhead.

**Wymagana walidacja:**
- Czy `StationMarker.OnAnyStationClicked` event jest subskrybowany skądinąd? (grep) — caller dostaje `StationMarker` reference, więc API musi zostać kompatybilne (passing POCO ref zamiast component).
- Czy ktoś używa `StationMarker.Select()`/`Deselect()` zewnętrznie?

**Decyzja:** **odłożone**. Aktualnie 3000 colliderów wystarcza (Unity sobie radzi w 60fps), ale player z dużym monitor + zoom out na Polskę może doświadczyć lag przy szybkim mouseove'rowaniu. Profile-driven decision wymagana.

**Walidacja kierunku (recon CBM 2026-06-13):** City Bus Manager (nasz target wizualny, plugin GO Map) **NIE** używa per-obiekt colliderów do zapytań przestrzennych na mapie — ma `SpatialHashGrid` + `DynamicQuadtree` + KD-tree (`_busStopsTree`/`_buildingsTree` z lockami) do lookupu przystanków/budynków + cache (`_routerPointsCache` itd.). To **dokładnie nasze „idealne rozwiązanie"** (centralny lookup + spatial index O(log N) zamiast O(N) colliderów). Komercyjny sim tej skali potwierdza, że spatial-index to właściwa droga — nie eksperyment. Wzmacnia kierunek gdy dojdzie do realizacji. Dowód: workflow `cbm-scale-models-recon` + `map-pipeline-compare-cbm-rm`, `GOMap.dll` (`Assets\Scripts\GO Map\GO Map\Core\PeDePe\` — SpatialHashGrid/DynamicQuadtree).

---

### TD-028 — ReverseTriangleWindingOrder per flush (fix w formap generator)

**Status:** 🟡 Workaround in place (per-render reverse)
**Priorytet:** post-EA cleanup (wymaga commitów w external repo)
**Estymata:** ~1 godz w `D:\Gry\formap` + regeneracja `poland-v7.bin`
**Files (this repo, workaround):** `Assets/Scripts/Map/Rendering/MapRenderer.Layers.cs` (ReverseTriangleWindingOrder + RenderFeatureBatch)
**Files (formap repo, prawdziwy fix):** trianglator / polygon-to-mesh converter w generatorze

**Stan obecny:**
`MapRenderer.Layers.RenderFeatureBatch` wywołuje `ReverseTriangleWindingOrder(indices)` per każdy flush (gdy `reverseWinding = true` — non-line, non-highway, non-waterway = budynki/water/forests/industrial/military/platforms). To swap'uje pierwszy i ostatni vertex każdego trianglea — ~100k operacji per render pełnej Polski.

**Powód workaround'u:**
Format `formap` v7 generator produkuje polygon indices z winding order odwrotnym do tego co Unity oczekuje (Unity = clockwise = front-facing; formap = counter-clockwise lub odwrotnie). Bez reverse polygony renderują się tyłem (backface culling = niewidoczne).

**Idealne rozwiązanie:**
W generatorze `D:\Gry\formap`:
1. Wykryć aktualne winding order produkowane przez triangulator (Earcut.NET lub własny algorytm).
2. Reverse w generator output zamiast w runtime renderer.
3. Bump version `MagicV8` żeby reader (`MapRenderer`) wiedział "v8 ma poprawne winding".
4. W `MapRenderer.Layers.cs` warunek `reverseWinding` zależny od version: `version >= 8 ? false : oldLogic`.

**Korzyści:**
- ~100k swap ops znika z runtime render hot path.
- Cleaner mapowanie geometry → mesh (no transformacja).
- Inne narzędzia czytające `.bin` (np. zewnętrzne wizualizatory) dostają standardowy winding.

**Ryzyko:**
- Wymaga regeneracji `poland-v7.bin` → `poland-v8.bin` (3-10 min generation).
- Stare `.bin` files dalej działają (version check w MapLoader → fallback do reverseWinding=true).

**Decyzja:** **post-EA cleanup**. Aktualnie workaround działa, koszt jest stały (~100k swap per render to <1ms na nowoczesnym CPU), nie blokuje gameplay'u. Fix wymaga koordynacji 2 repos (`RM-0.2` + `formap`) — wartościowe gdy będzie M-Performance lub okazja do format v8 bumpu.

---

## Gameplay correctness

### TD-019 — Pathfinding rozkładów: trasa A→B zahacza o stacje na boki

**Status:** 🟢 **Resolved 2026-05-10** (M-TimetableUX F1.6 implementation, commit TBD).
**Priorytet:** pre-EA — wpięte w M-TimetableUX scope
**Estymata:** wpięte w F1.6 (~2 sesje) — zrealizowane minimal scope (logic-only) w 1 sesji. Ghost markers UI (semi-transparent off-path stations w UI) odłożone do F1.3 / F1.16 progressive disclosure.

**Implementation:** `TimetableCreatorUI.Routing.cs:FindStationsPerSegment` przebudowany z spatial 500m radius scan na **topological filter** — stacja dodawana TYLKO jeśli `station.pathNodeId ∈ allNodeIds` (path z A* search). Off-branch stations są pomijane — nie zmuszają detoura. Player może opcjonalnie dodać je jako explicit waypoints przez F1.3 multi-stage UI (gdy zaimplementowane).

Performance bonus: O(stations + path length) z dict lookup, vs. stary O(stations × segLen) z spatial scan.

**Re-analiza 2026-05-08 (per user clarification + drawing):**

Original hipoteza (siding/spur penalty) była **niepoprawna**. User dostarczył drawing pokazujący:
- A i B leżą na poziomej głównej linii
- C leży na **prostopadłym odgałęzieniu** (perpendicular branch)
- Pathfinder wybiera trasę A → main → branch UP do C → branch DOWN → main → B (detour off main line)

**Real root cause:** `TimetableCreatorUI` ma stop auto-discovery z **spatial radius criterion** — stacja C jest spatially blisko main (POI ma euklidesowo małą odległość do junction węzła głównej linii), więc auto-discovery ją zgarnia jako required waypoint. Topologicznie jednak C jest na branchu — dotarcie tam wymaga objazdu.

**Pathfinder działa POPRAWNIE** — dostaje A→C→B i zwraca optimal path. **Bug jest w "co dodać jako stops"**, nie w "jak znaleźć ścieżkę".

**Resolution: M-TimetableUX F1.6 ghost suggestions** (zamiast spatial auto-discovery):
- Compute pure A→B path first (no waypoints)
- Show stations **ON the direct path** (`station.pathNodeId ∈ directPath.nodeIds`) jako ghost markers
- Klik ghost = explicit add → re-compute
- **Stacje OFF the path (na branchach) NIE są suggested** ← C nie zostanie dodane
- Player może opcjonalnie dodać C jako explicit waypoint przez drag/click — wtedy świadomy detour

**Plus M-TimetableUX dostarcza** PH/PT/ZD taxonomy + track-within-station + preferred direction + multi-stage waypoints + proactive at-action suggestions + Basic mode "klik A → klik B → working rozkład bez ingerencji".

---

[Original analiza below for reference — superseded by M-TimetableUX]


**Files:** `Assets/Scripts/Timetable/Runtime/PathfindingGraph.cs`, `Assets/Scripts/Timetable/Runtime/RailwayPathfinder.cs`, `Assets/Scripts/Timetable/UI/TimetableCreatorUI.Routing.cs`

**Symptom user'a (2026-05-06):**
> "Stacja A → Stacja B i zahacza o stacje na boki które nie powinny, przynajmniej z tego co widać w podglądzie rozkładu na mapie"

W kreatorze rozkładów (`TimetableCreatorUI`) podgląd trasy na mapie pokazuje że pathfinding wybiera trasę przez nadprogramowe stacje (boczne ramiączka grafu) zamiast najkrótszej / "naturalnej" trasy między A a B. Wpływa to na:
- Wyświetlaną długość trasy (km)
- Czas przejazdu (timetable computation)
- Ostatecznie: realną trasę pociągu w M9 (TrainRunSimulator używa tego samego grafu)

**Stan obecny:**
- M4.5 fixował **endpoint merge** + **rescue merge Step 2.5** (commit history) — naprawiło część cases gdzie graf był disconnected
- M-PL znalazł 3 brute-force performance bottlenecki w pathfinding pipeline (FindTracksForStop fallback, FindNodeOnTrack, generator JSON) — naprawione przez spatial grid lookup
- **Wciąż widoczne** nieoptymalne trasy w podglądzie — graf jest spójny i szybki, ale algorytm wybiera nieoptymalne ścieżki

**Możliwe przyczyny (zaktualizowane po code reading 2026-05-06):**

**Najprawdopodobniejsza ZIDENTYFIKOWANA:** ⭐ **Edge weight = czysta `lengthM` (długość fizyczna w metrach)** — `RailwayPathfinder.FindPath` używa A* z weight = `edge.lengthM`. Heurystyka euklidesowa (admissible). **Brak klasyfikacji edges** — boczne tory (bocznice, towarówki, dojazdy do warsztatów) traktowane identycznie z main line. Ponieważ **boczne tory są geometrycznie krótsze niż objazd po main line**, A* je preferuje.

```csharp
// RailwayPathfinder.cs:151
float tentativeG = currentG + edge.lengthM;  // WEIGHT = pure length, brak klasyfikacji typu toru
```

`PathfindingGraph.Edge` ma już `metadata: Dictionary<string, string>` które **MOŻE** zawierać OSM tagi (`usage=main/branch`, `service=siding/spur/yard`) — ale nie są używane w pathfinding.

**Inne hipotezy (mniej prawdopodobne):**
1. ~~Heurystyka nieadmissible~~ ✅ Sprawdzone — `Heuristic = Vector2.Distance` jest admissible dla grafu metrycznego
2. ~~Node merging zbyt agresywny~~ ✅ Sprawdzone — `BuildFromFeaturesUnionFind(junctionOnlyMerge: true)` + cellSize=3m (ścisła tolerancja). M4.5 fix endpoint merge zaadresował disconnected components.
3. **Track segments na rozjazdach** — `JunctionNodeIds` set jest tracked ale nie używany w pathfinding penalty
4. **Brak transit penalty** — pathfinder nie wie że stacja-przejazdowa vs stacja-postojowa. Każdy node jest tylko fizyczny, brak `isTransit` semantic distinction.

**Próbowane (M4.5):**
- Endpoint merge fix + rescue merge Step 2.5 — naprawiło spójność grafu (disconnected component issues)
- M-PL spatial grid w 3 brute-force lookup'ach — naprawiło performance + poprawność `FindNodeOnTrack`

**Idealne rozwiązanie (TBD — wymaga debugu):**
1. **Repro test case:** zidentyfikować konkretną parę A→B z widocznym objazdem (user'a save + screenshot trasy w podglądzie)
2. **Visual graph debug:** rozszerzyć `TimetableDebugTools` o overlay grafu z weight'ami per edge — łatwiej zobaczyć który edge "kradnie" trasę
3. **Przeanalizować weight function:** `RailwayPathfinder` algorithm + ewentualne penalty/heuristic
4. **Testy regresji:** save z 5-10 znanymi parami stacji + expected route patterns, smoke test `[ContextMenu]`

**Decyzja kiedy fixować:**
- **pre-EA** (visible UX issue, gracz traci zaufanie do silnika rozkładów)
- Wartościowy do naprawienia bo: 1) używane w M4 kreator + M9 ruch + M7-3c rescue + M-Balance trasa cost calc, 2) viewable bezpośrednio w UI (gracz widzi)
- ~2-4 sesje: 1 debug + 1-2 fix + 1 walidacja regresji

**TODO request od user'a:**
- Zapisz save z konkretną parą A→B gdzie widać objazd
- Albo screenshot podglądu z opisem "powinno iść tu, a idzie tam"

To umożliwi reprodukcję i debug.

---

### TD-023 — Punctuality threshold mismatch: subsidy ±5min vs reputation 1/5/15min ✅ DONE 2026-05-15

**Status:** 🟢 Resolved — wybrana opcja (B) Align reputation thresholds do subsidy ±5min
**Priorytet:** zrobione przy audicie modułu Timetable 2026-05-15
**Estymata:** ~30 min faktycznie (3-linia bump w `TrainRunSimulator.Delays.cs`)
**Files (zmienione):** `Assets/Scripts/Timetable/Runtime/Simulation/TrainRunSimulator.Delays.cs:24-26`

**Implementacja:**
Bump progów reputation events w `ApplyDelayReputationEvent`:
- DelayMinor: 60s → **300s** (5 min)
- DelayMedium: 300s → **900s** (15 min)
- DelayMajor: 900s → **1800s** (30 min)

Skutek: ≤5min = brak event (= on-time w obu systemach). Source of truth: `EconomyManager.PunctualityThresholdSec=300`.

**Decyzja warianty:** wybrano (B) zamiast (A) align subsidy do 1min (= utrudnia full subsidy) lub (C) tylko UI tooltip (= utrwala dual standard).
Real-world grounding: PKP IC + Polregio statystyki "punktualność" używają ~5min tolerance — gameplay zgadza się z intuicją gracza znającego polską kolej.

**Co NIE zostało zmienione:**
- Wartości penalty per event type (-1/-3/-5) w `ReputationManager` — bez zmian (tylko progi triggera)
- `SubsidyCalculator.PunctualityMultiplier` curve (<60%/60-80%/>80%) — bez zmian
- `EconomyManager.PunctualityThresholdSec=300` — pozostaje source of truth (wcześniej tylko subsidy używała tej constanty, teraz reputation też respektuje)

**Symptom design'u:**

Dwa systemy klasyfikują "punktualność pociągu" wg różnych progów:

| System | <1 min | 1-5 min | 5-15 min | 15+ min |
|---|---|---|---|---|
| **Subsidy** (`PunctualityRatio` → `SubsidyMultiplier`) | on-time | **on-time** | late | late |
| **Reputation** (`ReputationEventType`) | brak event | **DelayMinor** -1 | DelayMedium | DelayMajor -5 |

**Konflikt:**

- Pociąg spóźniony 4 min → subsidy: +1 on-time count (full subsidy gracz utrzymuje), reputation: -1 per voivodeship (cichy spadek)
- Gracz patrzy na UI "punktualność 75%" → "OK, subsidy bezpieczne". Patrzy na reputację → spada. Brak intuicji że "5min margin" jest tylko dla subsidy.
- Optymalny gameplay = maksymalna prędkość rozkładów (push to delay) → subsidy zachowane ale reputation drop'uje przez DelayMinor +-1

**Idealne rozwiązanie (3 opcje):**

**(A) Align thresholds — subsidy używa ≤1min jako on-time** (= tylko prawdziwie punktualne pociągi liczą się). Pro: spójność z reputation. Con: trudniej osiągnąć full subsidy (real Polregio dopuszcza ~5min).

**(B) Align reputation — DelayMinor zaczyna od 5min** (= 1-5 min "tolerance window" w obu systemach). Pro: spójność, real Polregio. Con: weakens reputation feedback loop.

**(C) Status quo + UI tooltip** w Subsidy panel: "Subsidy on-time = ≤5min. Reputation traci 1/5/15+min." Pro: minimum effort. Con: gracz nadal może być confused przy pierwszym kontakcie.

**Decyzja kiedy fixować:**
- **M-Balance trusted playtest** — zobaczyć czy gracze realnie się gubią przed final tuning'iem
- (B) wydaje się najbardziej grounded w rzeczywistości (PKP IC tolerance to ~5 min, Polregio podobnie). Jeśli wybór: bump `DelayMinor threshold` z 60s do 300s.

---

### TD-024 — Subsidy minRunsPerDay cliff (4 runs = wszystko/nic, brak gradacji)

**Status:** 🔴 Active (gameplay design cliff — punishing nieliniowo dla małych obiegów)
**Priorytet:** **M-Balance** (decyzja design'owa: gradient vs cliff)
**Estymata:** ~15 linii w `SubsidyCalculator.cs` jeśli prosty gradient (1 sesja)
**Files:** `Assets/Scripts/Timetable/Runtime/Economy/SubsidyCalculator.cs:32-33` (const `MinRunsPerDay=4`), `Assets/Scripts/Timetable/Runtime/Economy/SubsidyCalculator.cs:106` (`if (lb.runsCompletedToday < minRuns) return 0;`), `Assets/StreamingAssets/Economy/subsidy_rules.json:7` (`minRunsPerDayForFullSubsidy: 4`)

**Symptom design'u:**

Obieg z **3 runs/day** = **0 zł dotacji**. Obieg z **4 runs/day** = pełna dotacja (np. `1500 zł × 4 × diffMult × punctualityMult` ≈ ~30000 zł na obieg dziennie).

Pojedynczy run wpływa na ~30k zł — **cliff effect**. Brak gradacji.

**Konflikt z reality:**

Real Polregio wypłaca dotację **per pociągokilometr** (PSC contracts: ~20.92 zł/poc-km dla Łódzkiego). Liniowe — każdy run dolicza się proporcjonalnie. Nie ma "minimum 4 runs/day or zero".

**Konflikt z gameplay:**

- Mała zajezdnia z krótką trasą (np. 30km regionalna) może mieć fizyczny limit 3 runs/day (czas obrotu, fatigue maszynistów, bloki signalingu na shared line)
- Gracz traci całą dotację bez intuicji że "jeden run więcej = tysiące zł"
- Brak feedback'u w UI (Explain pokazuje "Za mało kursów (3/4)" — ale nie kwantyfikuje cliff'a)

**Idealne rozwiązanie (3 opcje):**

**(A) Liniowy gradient** — `subsidyMultiplier = min(runs / minRuns, 1.0)`. 1 run = 25%, 2 = 50%, 3 = 75%, 4+ = 100%. Pro: smooth, real-life proportional. Con: gracz może abuse'ować "1 run/day per circulation" dla całkowitej kwoty.

**(B) Step gradient** — 0 runs = 0%, 1-2 = 25%, 3 = 50%, 4+ = 100%. Cliff smoothed ale wciąż preferuje 4+ runs. Pro: balance między linearity a "minimum effort threshold".

**(C) Lower minimum to 2** — `minRunsPerDayForFullSubsidy = 2`, kept binary. Pro: minimum change, mała zajezdnia może uciec dotacja przy 2 runs. Con: nadal cliff (1 run = 0 zł).

**Decyzja kiedy fixować:**
- **M-Balance trusted playtest** — zobaczyć ile pojedynczych obiegów ma <4 runs/day (raczej rzadkie ale nie wykluczone w niche scenariuszach)
- Per-województwo `subsidyRule.minRunsPerDayForFullSubsidy` już parameterizowane (catalog) — można differentiate (mazowieckie centralne 6, peryferyjne 2)
- Sugestia: zacząć od (B) Step gradient + per-województwo override w `subsidy_rules.json`

**🌟 Future expansion potential (audit 2026-05-15):** TD-024 to **fajny punkt do większego rozwinięcia** — dotacje to potencjalnie cała pod-mechanika gameplay'u z głębią porównywalną z M7 Maintenance. Pomysły na M-Balance lub post-EA expansion milestone "M-Subsidies":

1. **PSC contracts emulation (per-pociągokilometr)** — zamiast `subsidyPerRunGroszy`, modelować realny Polregio standard ~20.92 zł/poc-km. Każda dotacja = `pricePerKm × routeLengthKm × runsCompleted × multipliers`. Daje organicznie liniową gradację (długie regionalne kursy = więcej dotacji niż krótkie podmiejskie — zgodnie z PKP/PSC).
2. **Per-województwo polityka dotacji** — każde z 16 województw ma własny budżet roczny + preferowane kategorie pociągów. Mazowieckie preferuje SKM/aglomeracyjne, peryferyjne (Podkarpackie/Świętokrzyskie) preferują wojewódzkie EI/IR żeby utrzymać dostępność. Gracz musi rozumieć "kogo zadowolić" w jakim regionie.
3. **Per-line subsidy levels** — niektóre relacje (np. EIP Pendolino) NIE są dotowane (komercyjne PKP IC), niektóre regionalne TAK (Polregio). Dotacja na poziomie `SubsidyRule` per voivodeship × per IRJ kategoria.
4. **Negocjacje wieloletnie z urzędami marszałkowskimi** — periodyczne kontrakty (3/5/10 lat) zamiast daily payout. Gracz negocjuje pulę + commit'uje min/max runów. Niewykonanie = kara, nadprodukcja = premia. Sym strategic depth.
5. **UI Explain breakdown** — `SubsidyCalculator.Explain` rozszerzyć o "ile zarobiłbyś gdyby kolejny run". Inline what-if calculator. Łagodzi cliff feedback issue.
6. **Time-based gradient zamiast hard min runs** — dotacja decay'uje z każdym kursem opóźnionym / poniżej średniej, ale nigdy zero. Smooth curve zamiast cliff.
7. **Subsidy auctions / lobbying mini-game** — endgame: gracz licytuje pulę na konkurencyjnym rynku przewoźników (multi-player tycoon meta).

**TL;DR**: TD-024 fix może być prostym Step gradientem (~15 linii, M-Balance), ale realny potencjał designerski to **osobny milestone "M-Subsidies"** (post-EA, ~10-20 sesji) który dorobiłby polskiej kolei symulacyjną głębię na poziomie m6-economy.md.

---

## QoL / Future (ciąg dalszy)

### TD-018 — Real recommended/minimum spec hardware validation

**Status:** 🟡 Partial (high-end PASS, real spec TBD)
**Priorytet:** pre-EA (M-Balance trusted playtest faza)
**Estymata:** ~1-2 sesje + dostęp do hardware
**Files:** brak code change — czysto walidacyjne

**Stan obecny:**
M-Performance MP-11 walidacja końcowa wykonana **tylko na high-end** (Ryzen 9800X3D). Skalowanie do recommended (i7-9700) i minimum (i5-6600) to **extrapolacja matematyczna** (~3-4× wolniej / ~5-6× wolniej w single-thread):
- Recommended estimate: avg ~10-13ms (75-100 FPS) ✅
- Minimum estimate: avg ~17-22ms (45-60 FPS) ✅
- p99 spike na słabszych spec może wystąpić rzadko ale nie crashuje

**Wymaga:**
1. Dostęp do realnego sprzętu (i7-9700 lub equivalent + i5-6600 lub equivalent)
2. **Lub:** CPU throttling przez Windows power profile / Process Affinity (single-core simulation single-thread spec)
3. Odpalić `PerfStressBootstrap` na recommended/minimum + porównać z target z `docs/design/performance-targets.md`

**Decyzja:** odłożone do **M-Balance trusted playtest faza** (pre-EA). 3-5 osób testerów może mieć różny sprzęt — naturalny pool dla validation. Plus M-Balance wprowadza save/load cycle który jest też performance-relevant.

---

## Modules incomplete (placeholder data)

### TD-020 — MultiplayerScreenUI MockServers do usunięcia (M10 placeholder)

**Status:** 🔴 Active
**Priorytet:** M10 Multiplayer (razem z real Mirror integration)
**Estymata:** ~10-15 linii kodu (usunąć MockServers + foreach + count fallback) + integracja z real source servers
**Files:** `Assets/Scripts/MainMenu/MultiplayerScreenUI.cs:27-36, 441, 570`

**Stan obecny:**
`MultiplayerScreenUI` pokazuje 7 hardkodowanych mock serverów (`MockServers` array linia 27-36) jako `ServerEntry { name, map, players, maxPlayers, ping }`:
- "Kolej Śląska #1" / Depot Alpha / 3/8 / 22ms
- "PKP Intercity Pub" / Depot Beta / 7/8 / 48ms
- "Tramwajarze z Krakowa" / Central Hub / 1/4 / 110ms
- "Railway Legends EU" / Depot Alpha / 5/12 / 35ms
- "Friendly Beginners" / Tutorial Map / 2/6 / 67ms
- "Hardcore Railroaders" / Mountain Pass / 8/8 / 19ms
- "Test Server (priv)" / Depot Alpha / 1/2 / 5ms

`PopulateServers` (line 433-457) iteruje `MockServers` przy każdym `Show()` i przy każdej zmianie filtra. `UpdateServerCount` fallback to `MockServers.Length` (line 570).

Świadomy MVP placeholder dla rozwoju UI przed M10 — pozwolił testować layout/empty state/search filter bez zależności od Mirror infrastructury.

**Idealne rozwiązanie:**
W M10 Multiplayer (po implementacji Mirror NetworkDiscovery / external master server / Steamworks lobby — decyzja otwarta):
1. Usunąć `MockServers` array i `ServerEntry` struct (lub przenieść struct do real model warstwy).
2. Zastąpić `PopulateServers` przez query do real source (async, z loading state).
3. Dodać "Refresh" button (ikona ↺ — patrz BUG-006) z polling/manual refresh.
4. Real-time updates dla player count + ping (periodic refresh co N sekund? push z master server?).
5. Empty state pokazywany tylko gdy real query zwraca 0, nie tylko gdy MockServers przefilrowane.

**Decyzja kiedy:** **M10 Multiplayer** razem z reszta MP infrastructury. NIE wcześniej — placeholder spełnia rolę dla UI dev'u, usunięcie bez real source = pusta lista bez sensu.

---

### TD-021 — LoadGameScreenUI mock data → integracja z SaveOrchestrator ✅ DONE 2026-05-08

**Status:** 🟢 Resolved
**Priorytet:** HIGH — pre-EA polish, backend już istnieje
**Estymata:** ~50-100 linii (zastąpić PopulateMockData → query SaveOrchestrator + wire click → LoadAsync + ewentualnie thumbnail/metadata rendering)

**Implementacja 2026-05-08:**
- `PopulateMockData` (6 fake save'ów) usunięte. Zastąpione `RefreshSavesAsync()` query'jącą `SaveLoadServiceBootstrap.Storage.ListAsync()` przy `Show()` (fire-and-forget z `_ = ...`).
- `SaveEntryData` rozszerzone o `slotId` (real identifier dla LoadAsync).
- Auto-save detection: `SaveType ∈ {"auto", "quick", "exit"}` → `isAutosave=true` (wcześniej hardkodowane).
- Sortowanie po `SavedAt` desc — gwarantowane przez `ISaveStorage.ListAsync` contract.
- `FormatSavedAt(string)` parsuje UTC ISO → local "yyyy-MM-dd HH:mm". `FormatPlaytime(double)` sekundy → "Xh YYmin" lub "YYmin" gdy <1h.
- Status label states: `load_game.status.{loading, empty, error}` (PL/EN/DE/CZ) — gracz widzi progres + empty state ("Brak zapisów. Rozpocznij nową grę.").
- Klik save → `OnLoadSaveAsync(slotId)` → `LoadingScreenManager.Show("Wczytywanie...")` → `Orchestrator.LoadAsync` → switch po `LoadStatus`:
  - `Success/PartialLoad` → `SceneManager.LoadScene("Depot")` (statyczne services Fleet/Timetable/Personnel/... = DontDestroyOnLoad, scene Awake pickup'uje state)
  - `NotFound` → toast + auto-refresh listy
  - `ModifiedSave` → log warn (TODO: confirm modal "/ load mimo to" — odłożone do M-UIPolish)
  - `NewerVersion` / `Failed` → log error
- `isLoading` / `isRefreshing` flags zapobiegają multi-click + concurrent ListAsync.
- `MainMenu.asmdef` reference do `RailwayManager.SaveLoad` dodany. Cycle-free (SaveLoad nie referuje MainMenu).

**Nie zrobione w TD-021 (świadomie odłożone, post-EA polish):**
- Thumbnail rendering (M13-10 spec) — `SaveSlotInfo` nie ma thumbnail field jeszcze
- Build version warning gdy mismatch
- Save size MB, Steam Cloud sync icon
- Delete save z context menu / dedicated button
- ModifiedSave confirm modal (in-game `SaveLoadUI` ma; main menu czeka)

**Files changed:**
- `Assets/Scripts/MainMenu/LoadGameScreenUI.cs` (+~190 linii, -55 mock data)
- `Assets/Resources/Locale/{pl,en,de,cz}/strings.json` (+`load_game.status.*` 4 langs × 3 keys = 12 entries)

**2026-05-10 cyclic dependency fix:**
Pierwsza implementacja użyła direct asmdef ref `MainMenu → SaveLoad` co spowodowało cyclic dependency (Depot → MainMenu existing + SaveLoad → Depot existing → MainMenu → SaveLoad NEW = cykl). Refactor na **Core hook pattern** (analogicznie jak `SaveActionsHook.QuickSave/QuickLoad/ShowSaveSlotPicker/ShowLoadSlotPicker` z TD-006):
- `SaveActionsHook.EnumerateSavesAsync: Func<Task<List<SaveSlotSummary>>>` — Core DTO (`SaveSlotSummary`) parallel do `SaveLoad.SaveSlotInfo`, mapping w `AutoSaveService.InstallSaveActionsHooks`
- `SaveActionsHook.LoadSaveByIdAsync: Func<string, bool, Task<LoadOutcome>>` — Core enum `LoadOutcome` parallel do `SaveLoad.LoadStatus`, switch mapping
- `SaveActionsHook.ShowLoadingScreen / HideLoadingScreen` — `LoadingScreenManager` sterowanie via hook
- `MainMenu.asmdef` — usunięta `RailwayManager.SaveLoad` reference (powrót do oryginalnych 4 ref: Core/SharedUI/TMP/InputSystem)
- `LoadGameScreenUI.cs` — usunięte `using RailwayManager.SaveLoad;`, wszystkie `SaveLoadServiceBootstrap.*` calls zastąpione `SaveActionsHook.*` invokes

Zachowuje invariant architektury: "**SaveLoad to najwyższa warstwa — referuje wszystko, nikt nie referuje SaveLoad**".
**Files:**
- `Assets/Scripts/MainMenu/LoadGameScreenUI.cs:104-149` (PopulateMockData), `:391-394` (klik → tylko Log.Info)
- Backend gotowy: `Assets/Scripts/SaveLoad/Runtime/SaveOrchestrator.cs`, `ISaveStorage.cs`, `LocalDiskStorage.cs`, `SteamCloudStorage.cs`

**Stan obecny:**
`LoadGameScreenUI` (panel "Załaduj grę" w MainMenu) hardkoduje **6 fake save'ów** w `PopulateMockData` (line 104-149):
- "Depot Alpha — Zapis 3" / 2026-03-28 14:30 / 2h 15min
- "Depot Alpha — Autozapis" / 2026-03-28 14:25 / 2h 10min (autosave)
- "Depot Alpha — Zapis 2" / 2026-03-27 20:10 / 1h 45min
- ... (4 inne)

`SaveEntryData` struct (line 17-23): name + dateTime + playTime + isAutosave (4 pola, brak: thumbnail, build version, scenariusz, save size, Steam Cloud sync status).

Klik save → tylko `Log.Info($"[LoadGame] Wybrano: {saveName}")` (line 393), **NIE** wywołuje `SaveOrchestrator.LoadAsync` ani transition do Depot.

Świadomy MVP placeholder z czasów dev'u UI (M13 spec mówi o SaveLoadUI master-detail z slot management — to istnieje w `Assets/Scripts/SaveLoad/Runtime/SaveLoadUI.cs`, prawdopodobnie używany in-game z PauseMenu, ale **MainMenu.LoadGameScreenUI nie jest podpięty do tego samego API**).

**Idealne rozwiązanie:**
1. **Usunąć `PopulateMockData`**, zastąpić query do `SaveOrchestrator.EnumerateSaves()` (lub podobnego API — sprawdzić co dokładnie udostępnia, zobaczyć jak `SaveLoadUI` z runtime to robi).
2. **Rozszerzyć `SaveEntryData`** o brakujące pola wg M13 spec: thumbnail texture (renderowany w wątku save'a per M13-10 "thumbnail render performance 50-100ms freeze"), build version (warning gdy mismatch), scenariusz/seed, save size MB, Steam Cloud sync icon.
3. **Wire klik → `SaveOrchestrator.LoadAsync(savePath)`** + transition do `SceneManager.LoadScene("Depot")` z LoadingScreenManager overlay (M13-10).
4. **Sortowanie:** wg dateTime desc (ostatni na górze) — obecne hardkodowane w mock data, real ma być wg file mtime.
5. **Empty state:** gdy brak save'ów (np. fresh install) — pokazać "Brak zapisów. Rozpocznij nową grę." z linkiem do "Nowa gra".
6. **Auto-save sekcja toggle:** już istnieje (`autosaveToggle` line 252) — sprawdzić czy działa po fix BUG-001 (placeholdery) + BUG-002 (scroll).
7. **Delete save:** prawym kliknięciem context menu? Albo dedykowany delete button per row? Decyzja UX.
8. **Cross-screen consistency:** `SaveLoadUI` (in-game) vs `LoadGameScreenUI` (main menu) — czy mają identyczny look/feel/funkcje? Best-case: wspólny komponent reuse'owany.

**Decyzja kiedy:** **najlepiej teraz/wkrótce** (pre-EA, nie czeka na żaden milestone) bo:
- Backend M13 GOTOWY (różnica vs TD-020 MP gdzie czekamy na M10 Mirror integration)
- Save/Load to **must-have feature** dla EA (gracz musi kontynuować grę)
- Prosty integration task (~50-100 linii)
- Aktualnie LoadGame screen jest **całkowicie nieużyteczny** — pokazuje fake save'y, nie ładuje nic
- Możemy traktować jako **mini-milestone "M13-15: MainMenu LoadGame integration"** — domyślnie chyba do wykonania w jakiejś sesji M-UIPolish lub ad-hoc (sub-godzina pracy).

---

### TD-022 — GameCreator większość opcji NIE aplikuje wartości ✅ DONE 2026-05-08

**Status:** 🟢 Resolved
**Priorytet:** HIGH — pre-EA, blokowało sensowne użycie GameCreator
**Estymata:** ~80-150 linii — wykonane w ~210 liniach (refaktor row builders + ApplyOnStart partial + GameState/AutoSave config)

**Implementacja 2026-05-08:**

**1. Row builder overloads** w `GameCreatorUI.Rows.cs` (4 metody, każda z wariantem out-parameter):
- `InputRow(..., out TMP_InputField field)` — InputRow base woła GetComponentInChildren po build
- `DropdownRow(..., out TMP_Dropdown dropdown)`
- `SliderRow(..., out Slider slider, formatter)`
- `ToggleRow(..., out Toggle toggle)`

Wzorzec: existing call site bez out param zostaje (back-compat dla Difficulty section custom UI). Nowe call sites z out param zachowują handle.

**2. Field handles** w `GameCreatorUI.cs`:
- Ogólnie: `_fieldGameName`
- Rozgrywka: `_sliderSpeed`, `_togglePauseOnStart`, `_toggleAutosave`, `_ddAutosaveInterval`
- Serwer: `_fieldSrvName`, `_ddMaxPlayers`, `_fieldPassword`, `_ddVisibility`

Reset w `PopulateSection` (handles destroyed razem z `_contentParent` children, więc null'ujemy refs).

**3. Usunięte duplikaty** z sekcji **Ogólnie** (decyzja MM-D… single source of truth):
- Difficulty dropdown (Easy/Normal/Hard/Expert) — duplikat z Rozgrywka section (gdzie pełny `DifficultyService` preset selector + 10 modifierów żywych)
- Funds dropdown (Low/Normal/High/Unlimited) — duplikat StartBudgetMultiplier z difficulty Custom modifierów
- Pre-fill GameName z aktualnego `GameState.DepotName` (default "Zajezdnia Mokotow")

**4. ApplyOnStart partial** (`GameCreatorUI.ApplyOnStart.cs`, nowy plik) wywoływane z `StartBtn.onClick` zamiast tylko `ApplyDifficultyAndRulesOnStart`:
- `ApplyGeneralOnStart` → `GameState.DepotName = _fieldGameName.text`
- `ApplyGameplayOnStart` (SP only) → `GameState.TimeScale`, `GameState.IsPaused`, `AutoSaveService.IsAutoSaveEnabled`, `AutoSaveService.AutoSaveIntervalGameSec` (5/10/15/30 min mapping)
- `ApplyDifficultyAndRulesOnStart` (M13-13, bez zmian) — preset + 10 modifiers + 6 rules + Money
- `ApplyServerOnStart` (MP only) → `GameCreatorContext.Server*` (placeholder dla M10 Mirror)

**5. Backend additions:**
- `AutoSaveService.IsAutoSaveEnabled` property (default `true`) + check w `Update()` tick (`if (!IsAutoSaveEnabled) return;`)
- `GameCreatorContext.ServerName/ServerMaxPlayers/ServerPassword/ServerVisibilityValue` static fields (M10 ready)

**6. Asmdef:** `GameCreator.asmdef` reference do `RailwayManager.SaveLoad` dodany (dla `AutoSaveService.Instance` access).

**Maps section pozostaje placeholder:** mock data 5 map (Depot Alpha / Centralny Węzeł / etc.) — real maps to M-PL DLC tile system (poza scope TD-022). `_selectedMap` zachowane ale nie applied (TODO: gdy M-PL ready, `GameState.SelectedMapId = Maps[_selectedMap].id`).

**Files changed:**
- `Assets/Scripts/GameCreator/GameCreatorUI.Rows.cs` (+44 linii — 4 overloads)
- `Assets/Scripts/GameCreator/GameCreatorUI.cs` (+15 linii — handle field declarations)
- `Assets/Scripts/GameCreator/GameCreatorUI.Sections.cs` (~30 linii zmiana — populate calls z out params, removed Difficulty/Funds duplikatów)
- `Assets/Scripts/GameCreator/GameCreatorUI.Layout.cs` (1 linia — `ApplyOnStart()` zamiast `ApplyDifficultyAndRulesOnStart()`)
- `Assets/Scripts/GameCreator/GameCreatorUI.ApplyOnStart.cs` (+115 linii — nowy partial)
- `Assets/Scripts/MainMenu/GameCreatorContext.cs` (+8 linii — Server* fields)
- `Assets/Scripts/SaveLoad/Runtime/AutoSaveService.cs` (+5 linii — IsAutoSaveEnabled property + tick check)

**2026-05-10 cyclic dependency fix (analogicznie do TD-021):**
Initial impl używała `GameCreator → SaveLoad` direct ref (`AutoSaveService.Instance.IsAutoSaveEnabled = ...`). Po cyclic dep error, refactor na hook pattern:
- `SaveActionsHook.SetAutoSaveEnabled: Action<bool>` — wywołuje `AutoSaveService.Instance.IsAutoSaveEnabled = value` od strony SaveLoad
- `SaveActionsHook.SetAutoSaveIntervalSec: Action<float>` — wywołuje `AutoSaveService.Instance.AutoSaveIntervalGameSec = value`
- `GameCreator.asmdef` — usunięta `RailwayManager.SaveLoad` reference
- `GameCreatorUI.ApplyOnStart.cs` — usunięte `using RailwayManager.SaveLoad;` + `AutoSaveService.Instance` calls zastąpione `SaveActionsHook.SetAutoSave*` invokes
**Files:**
- `Assets/Scripts/GameCreator/GameCreatorUI.Rows.cs` (InputRow/DropdownRow/SliderRow/ToggleRow zwracają tylko label)
- `Assets/Scripts/GameCreator/GameCreatorUI.Sections.cs` (PopulateOgolnie/PopulateMapa/PopulateRozgrywka/PopulateSerwer)
- `Assets/Scripts/GameCreator/GameCreatorUI.Difficulty.cs:408-452` (ApplyDifficultyAndRulesOnStart — tylko difficulty+rules+budget)
- `Assets/Scripts/GameCreator/GameCreatorUI.Layout.cs:292-296` (StartBtn onClick wire)

**Stan obecny:**
GameCreator ma 11+ kontrolek UI rozproszonych po 4 sekcjach. **Tylko 3 kontrolki realnie aplikują:**
- ✅ Difficulty preset (sekcja Rozgrywka, `_ddDifficultyPreset` — osobne pole z `Difficulty.cs`) → `DifficultyService.ApplyNewGameConfig(preset)`
- ✅ Custom modifiers — 10 sliderów (sekcja Rozgrywka) → `DifficultyService.ApplyNewGameConfig(Custom, mods)`
- ✅ Game rules toggles (sekcja Rozgrywka) → `GameRulesService.ApplyNewGameConfig(rulesConfig)` + `GameState.Money = 100M × StartBudgetMultiplier`

**10 placeholder kontrolek (UI istnieje, wartość ignorowana przy LoadScene):**

| Sekcja | Kontrolka | Status |
|---|---|---|
| Ogólnie | GameName (input field) | ❌ Wartość ginie — brak field handle, tylko label |
| Ogólnie | Difficulty dropdown (Easy/Normal/Hard/Expert) | ❌ Duplikat Difficulty z Rozgrywka, brak sync, label only |
| Ogólnie | Funds dropdown (Low/Normal/High/Unlimited) | ❌ Duplikuje StartBudgetMultiplier z difficulty, label only |
| Mapa | 5 mock map (Depot Alpha / Centralny Węzeł / Górska Przełęcz / Delta Rzeczna / Mapa samouczka) | ❌ `_selectedMap` nigdzie nie odczytywane przy Start. Plus mapy są mock — real to M-PL DLC tile system |
| Rozgrywka | Speed slider (0.5×–3×) | ❌ Label only — `Time.timeScale` nie ustawiane |
| Rozgrywka | PauseOnStart toggle | ❌ Label only |
| Rozgrywka | Autosave toggle | ❌ Label only — `AutoSaveService` nie dostaje config |
| Rozgrywka | AutosaveInt dropdown (5/10/15/30 min) | ❌ Label only |
| Serwer | SrvName, MaxPlayers, Password, Visibility | ❌ Wszystkie 4 — label only, MP infra (M10) i tak ich jeszcze nie używa |

**Root cause:**
`InputRow/DropdownRow/SliderRow/ToggleRow` w `GameCreatorUI.Rows.cs` **zwracają tylko `TextMeshProUGUI` label** — nie field/dropdown/slider/toggle handle. `field` w `InputRow` (line 49: `var field = inputGO.AddComponent<TMP_InputField>();`) jest local var, gubi się po returnie. Klasa nigdzie nie utrzymuje ref do real input componentów (poza Difficulty section która robi to inaczej — własne `_ddDifficultyPreset` / `_customSliders[]` / `_ruleToggles`).

`StartBtn.onClick` (Layout.cs:292-296) woła tylko `ApplyDifficultyAndRulesOnStart()` + `LoadScene("Depot")` — żaden generic apply z innych sekcji.

**Plus duplikacja Difficulty:**
- Sekcja Ogólnie: "Difficulty" dropdown (Easy/Normal/Hard/Expert) — label only
- Sekcja Rozgrywka: pełen `PopulateDifficultySection` (preset + custom modifier + rules)
- Brak synchronizacji, brak jasności którą ma user wybrać. Sygnał że Ogólnie.Difficulty powstał wcześniej, potem dorzucono real system w Rozgrywka — Ogólnie zostało jako stale UI.

**Idealne rozwiązanie:**
1. **Refactor row builders** aby zwracały tuple `(TextMeshProUGUI lbl, TMP_InputField field)`, `(TextMeshProUGUI lbl, TMP_Dropdown dd)`, etc. Zachowanie ref w polach klasy: `private TMP_InputField _fieldGameName; private TMP_Dropdown _ddFunds; ...`.
2. **Wire ApplyOnStart** — przed `LoadScene("Depot")` zebrać wartości i propagować:
   - `GameState.GameName = _fieldGameName.text` (musi być pole na GameState)
   - `GameState.SelectedMapId = Maps[_selectedMap].id` (gdy real maps wymienia mock)
   - `Time.timeScale = SliderValue(_sliderSpeed)` lub do ekonomii speed multiplier
   - `GameState.PauseOnStart = _toggle.isOn` (handle przy Depot Awake)
   - `AutoSaveService.Configure(_toggleAutosave.isOn, _ddAutosaveInt.value)` — service już istnieje, prosty config
   - MP fields → `GameCreatorContext` lub osobny `ServerConfig` static (czeka na M10)
3. **Usunąć duplikat Difficulty** z sekcji Ogólnie + Funds (oba duplikują StartBudgetMultiplier z Custom modifierów). Zostaw tylko sekcję Rozgrywka jako single source of truth.
4. **Maps section** — to NIE TD-022 fix (mapy są w M-PL DLC tile system z osobnej infra), TBD osobne.

**Decyzja kiedy:** **HIGH pre-EA**:
- Bez tego GameCreator jest 70% atrapą — gracz wybiera ale nic się nie dzieje
- Po fix BUG-007 + BUG-005 GameCreator będzie wreszcie używany w SP flow → musi działać
- Estymata 80-150 linii — sub-sesja pracy
- Decyzja: zrobić W TYM SAMYM milestone'cie co BUG-005/007 (pre-EA polish), albo jako część M13-15 (MainMenu integration package).

---

## Code quality / Consistency

### TD-030 — Ujednolicenie języka kodu (mieszanka PL/ENG)

**Status:** 🔴 Active
**Priorytet:** post-EA (duży refactor — nie all-at-once, ryzyko regresji)
**Estymata:** duża — 657/711 plików `.cs` zawiera polski (komentarze/stringi/nazwy)
**Files:** cały `Assets/Scripts/` (cross-cutting)

**Kontekst:**
Kod miesza polski i angielski. Konwencja (`conventions.md`) zakłada: polski dla nazewnictwa
domenowego (kody IRJ `EI`/`EN`/`RO`/`MP`, plus `Pojazd`, `Obieg`, `Rozkład`, `Stacja`) +
komentarze, angielski dla generic infra (`Service`/`Manager`/`Factory`). W praktyce polski
rozlał się szerzej niż konwencja zakładała — także na nazwy klas/metod, nie tylko kody IRJ.
Skala: **657/711 plików `.cs`** ma polskie znaki.

**Problem:** niespójność (raz PL, raz ENG w nazwach) obniża czytelność; dla publicznego repo
i międzynarodowych odbiorców komentarze + nazwy PL to bariera. Decyzja (user 2026-06-07):
**ujednolicić na jeden język**, kierunek docelowy **angielski** (zob. README „Language convention").

**Idealne rozwiązanie:**
Stopniowa migracja na angielski, moduł po module (NIE all-at-once — rename publicznych nazw
klas/metod w działającej grze = ryzyko regresji + ogromny diff). Zasady: nowy kod od razu ENG;
przy okazji dotykania modułu z innego powodu — rename jego nazw + tłumaczenie komentarzy.
Sugerowana kolejność od warstw niższych (Core/Fleet) w górę. Odrzucona alternatywa: zostawić
mieszankę (user chce spójności).

**Decyzja kiedy:** post-EA (przed 1.0, lub gdy dojdą kontrybutorzy ENG). Nie blokuje EA ani
publicznego repo — README jest dwujęzyczny + sekcja „Language convention" tłumaczy stan przejściowy.

**Walidacja kierunku (recon CBM 2026-06-13):** PeDePe (twórcy City Bus Manager — nasz genre-peer/target wizualny)
to **niemieckie studio**, a ich kod jest **w 100% angielski**. Byte-scan `ProjectAssembly.dll`: **ZERO** niemieckich
słów-identyfikatorów (Fahrer/Mitarbeiter/Gehalt/Haltestelle/Werkstatt/Schranke/Bewerber/Kosten...), za to angielskie
domenowe setki razy (`Employee` 387×, `BusDriver` 139×, `Workshop` 72×, `BusRouteOrder` 66×). Niemiecki żyje
**wyłącznie w danych** (nazwy prefabów busów `(Gelenk)`/`(Doppeldecker)`) + **lokalizacji** (UI stringi w
`Unity.Localization`, nie w kodzie). To potwierdza kierunek TD-030: nie-anglojęzyczne studio → **angielski kod +
język ojczysty tylko dla terminów domenowych (nasze IRJ `EI`/`EN`/`RO` ≈ ich `Gelenk`) i i18n**. Nasza mieszanka
PL/ENG w identyfikatorach/komentarzach to odchylenie od standardu branżowego. Dowód: dedykowany skan języka
+ setki angielskich symboli z reconów `cbm-*`.

---

## Symulacja zajezdni — głębia (pociągi + pracownicy)

> Audyt 2026-06-08 (user): ruch po torach działa, ale „żywa zajezdnia" (sprzęganie,
> kilka składów na torze, chodzenie pracowników bez przenikania przez obiekty) to MVP/braki.
> **Decyzja user 2026-06-08: WSZYSTKIE PRE-EA — to powinno być zrobione przed EA** (nie post-EA).
> Cztery TD poniżej = dwa milestone'y: ruch składów v2 + chodzenie pracowników v2.

### TD-031 — Zajętość toru binarna (1 skład/tor) zamiast pozycyjnej

**Status:** 🟢 Zweryfikowane (2026-06-08, Unity 6000.4.0f1 batch): compile clean + **EditMode 545/545** + **PlayMode 6/6** (`DepotMovementPlayTests`). Pokryte runtime: dojazd do styku ≈ ContactGapM + zero przenikania (konwencja center ±L/2), dojazd do czystego celu, reguła napędu, re-route same-dir (bez ghost-occupantów) + opposite-dir (cofanie: brake→pending→reverse), save→load (pozycja zachowana + resume do celu). Etapy A–H + polish + integracja service-resume innego czatu. Zostają (niższy risk, pokryte logiką/architekturą): pełny `DepotSavable` JObject round-trip (TrackOccupant JSON ✅ EditMode + resume-mechanizm ✅ PlayMode osobno), watchdog serwisowy runtime, scene-switch (additive — zweryfikowane reading `SceneController`).
**Priorytet:** PRE-EA — milestone „żywa zajezdnia" (prereq dla TD-032)
**Estymata:** ~3-5 sesji
**Files:** `Assets/Scripts/Depot/TrackGraph.Tracks.cs` (`IsTrackFreeFor`/`OccupyingConsistId`), `Movement/DepotMovementSimulator.{Pathfinding,Movement,Tasks}.cs`

**Stan obecny:**
Tor rezerwowany BINARNIE — `IsTrackFreeFor(trackId, consistId)` zwraca wolny tylko gdy pusty
lub zajęty przez TEN SAM consist (`OccupyingConsistId`). Skutki:
- Zapobiega przenikaniu między składami (dwa nie wejdą na ten sam tor) — ✅ ale zgrubnie.
- ❌ Nie da się zaparkować kilku krótkich składów nos-w-nos na długim torze.
- ❌ Blokuje sprzęganie (TD-032): B zajmuje tor → A nie ma jak wjechać do B.

**Idealne rozwiązanie:**
Zajętość **POZYCYJNA** — tor trzyma listę zajętych interwałów `[distFrom, distTo]` per consist;
`IsTrackFreeFor` sprawdza overlap żądanego interwału, nie całego toru. Ruch (`AdvanceMovement`)
sprawdza dystans do składu przed sobą → nieprzenikanie wg odległości, nie blokada toru.
Umożliwia kilka składów na torze + jest prerekwizytem sprzęgania.

**Doprecyzowanie wymagań (user 2026-06-08):**
Na jednym torze ma móc stać **kilka/kilkanaście jednostek** — w tym **pojedyncze wagony** jako osobne
jednostki, nie tylko kompletne składy. Twarde reguły docelowego modelu:
- **Zero przenikania** — jednostki trzymają separację dystansową (styk zderzaków, nigdy nakładanie).
- **Pełny realny ruch w obrębie toru** — jednostka **z napędem** (EZT / lokomotywa / lok.+wagony) może
  przejechać od swojej pozycji do innej jednostki stojącej na tym samym torze. Ruch ciągły, kinematyczny:
  **nie teleport, nie przeskok pozycji** — dojazd z hamowaniem aż do styku.
- Jednostka **bez napędu** (luźny wagon) nie rusza się sama — wymaga lokomotywy (spina się z TD-032:
  lok. podjeżdża pozycyjnie → sprzęga → odjeżdża z wagonem).

> **Kontrakt jakości (cały blok TD-031..034 „żywa zajezdnia"):** implementujemy w pełni i dokładnie,
> bez MVP-shortcutów / stubów / fallbacków-bypassów — rozmiar kodu nie jest powodem do cięcia scope.

**Implementacja (2026-06-08, Etapy A–G — kod gotowy, czeka na weryfikację w Unity):**
- **Dane:** `TrackOccupant` (ConsistId/VehicleIds/FrontDistM/RearDistM/DirSign) + `List<TrackOccupant> Occupants`
  na `DepotTrackData`. Stare pola `IsOccupied`/`Occupying*` zostają jako pochodny **legacy-mirror** (display/UI).
  Czysta algebra w `Depot/TrackOccupancyMath.cs` (overlap/gap/find-gap/nearest-ahead + konwersje polyline↔track-local).
- **API grafu** (`TrackGraph.Tracks.cs`): `SetOccupantInterval`/`RemoveOccupant`/`RemoveConsistEverywhere`/
  `IsRangeFreeFor`/`TryFindFreeGapForLength`/`GetOccupant(s)`; `OccupyTrackByConsist`/`FreeTrackForConsist`/`IsTrackFreeFor` = kompat-wrappery.
- **Ruch** (`DepotMovementSimulator.Occupancy.cs` + `.Movement.cs` + `.Pathfinding.cs`): footprint **center ± L/2**
  zapisywany co tick; dynamiczny cap = pozycja za najbliższą jednostką − (L/2 + styk); `effectiveEnd = min/max(cel, cap)`;
  hamowanie aż do styku + **crawl ~1.5 m/s** na ostatnich 5 m; podążanie za jadącym liderem wychodzi naturalnie
  (complete dopiero gdy staje). **Dwuprzebiegowy FixedUpdate** (cap ze snapshotu początku ticku → determinizm, MP-9).
  Admission pozycyjny zastąpił whole-path binary reservation.
- **Reguła napędu:** `ConsistHasTraction` — self-move bez napędu odrzucony (`EnqueueMove`/`EnqueueExit`); wjazd z zewn./parking = `isSelfMove:false` bypass.
- **Parking/placement** (`.Tasks.cs` + `.Visuals.cs`): `ParkConsistOnFreeTrack` pakuje wiele składów w wolne luki;
  `SpawnParkedVisual`/`RestoreParkedVisualsFromGraph` z anchora interwału (nie środek toru), multi-occupant per tor.
- **Save:** **bez bump SchemaVersion/migratora** (zgodnie z `SaveLoad/README.md` pre-EA) — fallback
  `TrackOccupancyMath.SynthesizeLegacyOccupant` w `TrackGraph.RestoreFromSave` (stary save binarny → footprint = cały tor, środek jak dawniej).
- **Scene-switch + ruch w tle + resume (Etap H, 2026-06-08):** sceny Depot/Map ładowane **additive i nigdy nie odładowywane**
  (zweryfikowane w `SceneController`) → przełączanie scen niczego nie przesuwa, a `DepotMovementSimulator.FixedUpdate` tika
  cały czas (blok tylko `GameState.IsPaused`; >x5 = cap x5). **Ruch w zajezdni działa w tle na MapScene = już spełnione.**
  **Resume manewru po save/load:** `DepotSavable` persistuje lekki rekord aktywnych zadań (`depotMoveTasks`:
  consistId/vehicleIds/toTrackId/targetWorldPos/exit/entry; **pomija serwisowe `onCompleted!=null`**), a
  `DepotMovementSimulator.RestoreActiveMove` (zwraca bool) re-enqueue'uje po `RestoreParkedVisualsFromGraph` → consist
  jedzie dalej do celu (pozycja exact, prędkość od 0). **Ruchy serwisowe — DONE (inny czat, 2026-06-08):**
  `RestoreActiveMove` zwraca bool + `GetActiveTask`/`HasTaskForConsist`/`HasConsistVisual` dodane; watchdog
  `OutdoorEquipmentMovementBridge.RecoverInterruptedServiceMovements` (throttled z `WorkshopManager.Update`,
  idempotentny — orphan = visual bez taska) re-issue'uje manewr serwisowy + podpina onCompleted; fallback do
  natychmiastowego Servicing gdy nieosiągalne. Stary teleport-skrót `RecoverEnRouteAsServicing` USUNIĘTY →
  `MarkPendingMovementRecovery` (zostaw EnRoute, brak desync visual↔logika).
- **Stałe:** `Depot/Movement/DepotOccupancyConstants.cs` (ContactGapM 0.05 = styk, CouplingApproachSpeedMps 1.5, ApproachSlowdownDistM 5, MinParkingGapM 0.5).
- **Decyzje user 2026-06-08:** bufor = **styk (~0 m)**; **wolny dojazd (crawl)**; **reguła napędu egzekwowana teraz**.
- **Testy EditMode:** `DepotTrackOccupancyTests` (algebra + legacy synth + round-trip), `DepotTaskMappingTests` (polyline↔track), `ConsistTractionTests` (napęd).
  ContextMenu w `Debug.cs`: „Park 3 consists on one track", „Two consists — follow to contact".
- **Weryfikacja — EditMode ✅ DONE (2026-06-08):** Unity batch `-runTests EditMode` → **545/545 passed, 0 failed, compile clean**
  (`DepotTrackOccupancyTests` + `DepotTaskMappingTests` 5/5 + `ConsistTractionTests` 8/8; reszta projektu bez regresji).
- **Weryfikacja — PlayMode ✅ DONE (2026-06-08):** `Assets/Tests/PlayMode/DepotMovementPlayTests.cs` (**6/6 passed**, batch `-testPlatform PlayMode`):
  `MoverStopsAtContactBehindBlocker` (gap ≈ ContactGapM, gap ≥ 0 — center ±L/2 + brak przenikania), `MoverReachesClearTarget`,
  `NoTractionConsistRejected`, `ReRouteSameDirection_StopsAtNewTarget` (mid-move + brak ghostów), `ReRouteOppositeDirection_Reverses`
  (brake→pending→cofanie), `SaveLoad_ResumesMove_PositionPreserved` (pozycja zachowana przez load + `RestoreActiveMove` dojeżdża do celu).
  Buduje minimalny graf programowo (`AddNode/AddEdgeWithPolyline/AddTrack`) + pełny `DepotMovementSimulator` + drive przez `WaitForFixedUpdate`.
- **Weryfikacja — opcjonalne dalsze (niższy risk):** pełny `DepotSavable` JObject round-trip (scena-zależny; TrackOccupant JSON ✅ EditMode,
  resume-mechanizm ✅ PlayMode), watchdog serwisowy runtime (kod innego czatu, wiring zweryfikowany reading), manualne ContextMenu (`Debug.cs`).

**Decyzja:** PRE-EA (user 2026-06-08 — powinno być zrobione przed EA).

---

### TD-032 — Brak sprzęgania/rozprzęgania składów w zajezdni

**Status:** 🟢 Done (2026-06-09) — zweryfikowane EditMode 565/565 + PlayMode `DepotCouplingPlayTests` 10/10
**Priorytet:** PRE-EA, PO TD-031 (wymaga zajętości pozycyjnej)
**Files (zrealizowane):** NEW `Movement/ConsistCouplingMath.cs`, `Movement/DepotMovementSimulator.Coupling.cs`, `Movement/ConsistCouplingPromptUI.cs`, `UI/VehicleChipStyle.cs`, `Timetable/Runtime/CouplingCirculationBootstrapper.cs`, `Tests/EditMode/ConsistCouplingMathTests.cs`, `Tests/PlayMode/DepotCouplingPlayTests.cs`; EDIT `Movement/ConsistPopupUI.cs`, `Movement/DepotConsistSelectionHandler.cs`, `Movement/DepotMoveTask.cs`, `Movement/DepotMovementSimulator.{cs,Movement.cs}`, `Timetable/Runtime/CirculationService.cs`, `UI/FleetPanelUI.{cs,Helpers.cs}`, locale ×4

**Zrobione (2026-06-09, sesje TD-032 A–I) — bez skrótów, zgodnie z decyzjami usera:**
- **Algebra (`ConsistCouplingMath.cs`, pure/EditMode-testowalna):** merge footprint (span → re-anchor do sumy długości, styk domknięty do 0, nos-anchored wg DirSign), kolejność `vehicleIds` nos→tył wg geometrii (chasing/head-on z reversal), split footprint (front=nos), `FindConsistByVehicleId`, `DedupConsistName`. 14 testów.
- **Fasady (`DepotMovementSimulator.Coupling.cs`):** `CoupleConsists(mover, blocker)` (survivor=mover, guard stationary+wspólny tor, merge occupant+visual+FleetConsistData), `DecoupleConsist(consistId, cutIndex)` (front zachowuje consistId+FCD, tail=nowy id+„(2)"), `FindAdjacentCouplableConsist` (ręczny couple po wczytaniu — stojące stykające się składy nie odpalają eventu), `GetConsistDisplayName`, hook `CirculationWarnHook`.
- **Couple-at-contact event:** `OnConsistArrivedAtContact` w `FinalizeTask` gdy dojazd skończył się DO STYKU za innym (capBinding + `dynamicStopBlockerId≥0`, ruch gracza). `DepotMoveTask.arrivedContactBlockerId/WorldPos`.
- **UI:** `ConsistCouplingPromptUI` (popup „Połączyć X z Y?" śledzący punkt styku przez WorldToScreenPoint, Tak/Nie, re-walidacja stationary, warn obiegu); `ConsistPopupUI` + picker rozprzęgania (modal: chip-strip pojazdów + klikalne przerwy = cutIndex, scroll dla długich składów, warn) + przyciski „Rozprzęgnij" / „Połącz z sąsiednim"; wiring w `DepotConsistSelectionHandler`.
- **Warn obiegu (pozwól+ostrzeż):** `CirculationService.IsConsistInActiveCirculationToday` (Active+Paused, `GetVehiclesForDate(CurrentDateIso)`) + `CouplingCirculationBootstrapper` (Timetable) instaluje hook (Depot nie widzi Timetable). Warn w obu flow.
- **DRY:** `UI/VehicleChipStyle.cs` (kolor wg typu + krótki label) wyciągnięte z FleetPanelUI (delegacja, usunięto duplikat).
- **Save 1:1:** bez nowego kodu save — occupancy (`TrackOccupant`, TD-031) + `FleetConsistData` round-trip. PlayMode potwierdza: po couple→1 occupant, po decouple→2, footprinty 1:1.
- **Locale:** `popup_couple.*` + `popup_decouple.*` ×4 (pl/en/de/cz).
- **Decyzje inżynierskie (zrealizowane):** kolejność po merge = geometria nos→tył (DirSign); survivor=mover (couple) / front-nos zachowuje consistId (split); `FleetConsistData` merge (mover FCD scala ids, blocker FCD usuwany) / split (front zachowuje, tail nowy z dedup nazwą).
- **Testy:** EditMode 565/565 (w tym `ConsistCouplingMathTests` 14 + locale parity). PlayMode `DepotCouplingPlayTests` 10/10 (couple geometria+fleet, decouple partycja, guardy stationary couple+decouple, 1-pojazd/zły cut, wagon-only couple OK ale merged self-move rejected, save/load 1:1 ×2, find-adjacent ×2).
- **POST-EA (zapisane, NIE w MVP):** rozpinanie przez pracownika (czas/animacja zamiast natychmiastowego splitu); typy sprzęgów (śrubowy/Scharfenberg/auto + walidacja kompatybilności).
- **Znane osobne (pre-existing, NIE z TD-032):** pełny PlayMode suite miał 2 cross-class fails niezwiązane z coupling — **rozwiązane w TD-038** (2026-06-09, suite 40/40 zielony).

**Stan obecny (przed TD-032):**
Składy ruszały się jako STAŁY zbiór `vehicleIds`. Brak mechaniki łączenia dwóch consistów w jeden
ani dzielenia. Tylko `components.coupling` (komponent sprzęgu do awarii M7) — nie ruch. Model
1-skład/tor (TD-031) dodatkowo blokował dojazd A do stojącego B.

**Idealne rozwiązanie:**
`CoupleTask` reużywa kinematykę `AdvanceMovement` (pixel-perfect stop już jest):
1. Cel = punkt styku zderzaka B (rzut pozycji B na ścieżkę A → `stopDistanceM`).
2. Wolne podejście na finiszu (`DepotCouplingSpeedMps` ~1 m/s, analogicznie do `DepotReverseSpeedMps`).
3. Na dojechaniu → **logiczny merge**: `A.vehicleIds + B.vehicleIds` → jeden consist, przeliczony
   `ConsistMarker` (długość = suma), update `FleetConsistData`. Rozprzęganie = odwrotność (split).
**Doprecyzowanie / decyzje (user 2026-06-08, omówienie pre-plan):**
- **TD-031 już daje dojazd do styku** (crawl + stop przy ContactGap, zero przenikania) — CoupleTask reużywa to;
  TD-032 = merge/split logic + dane + UI + cross-system, NIE kinematyka (estymata realnie mniejsza).
- **Trigger sprzęgu = popup kontekstowy na styku.** Gdy składy podjadą do siebie (dojazd do styku TD-031), nad
  punktem sprzęgu popup „Połączyć [X] z [Y]?" → Tak = merge, Nie = zostają osobno nos-w-nos. Nie auto-merge,
  nie pre-komenda — potwierdzenie diegetyczne.
- **Rozprzęganie = picker na popupie składu.** Klik na skład → popup pokazuje sekwencję pojazdów → gracz wybiera
  miejsce „przecięcia" (między pojazdami) → split na dwa składy.
- **Obiegi: pozwól + ostrzeż.** Sprzęg/rozprzęg zawsze dozwolony; gdy pojazd ma `assignedCirculationId` (aktywny
  obieg) → warning „to zmieni kompozycję kursu". NIE auto-resolve (akceptowany możliwy niespójny stan — gracz
  odpowiada; re-assign w UI obiegów ręcznie).
- **Wizual:** jeden marker per (scalony) consist (scale = suma długości) — spójne z modelem TD-031.
- **Geometria styku:** ~0 (ContactGap z TD-031 domyka do styku przy merge).
- **Tylko gdy stoi (MVP, user 2026-06-08):** sprzęg i rozprzęg dozwolone wyłącznie dla NIErychomych składów
  (nie w trakcie manewru).
- **Save/load 1:1 (user 2026-06-08):** po wczytaniu składy DOKŁADNIE jak przy zapisie — pozycja (TD-031 occupancy)
  **oraz stan sprzęgnięć** (kompozycja `vehicleIds`). Scalony zostaje scalony, rozłączony rozłączony, na tej samej pozycji.
- **Kompatybilność sprzęgów (MVP):** dowolne dwa składy można sprząc (typy sprzęgów → POST-EA, niżej).

**POST-EA (zapisane, NIE w MVP 032 — user 2026-06-08):**
- **Rozpinanie przez pracownika** — rozprzęganie wykonuje pracownik (ustawiacz/maszynista): dojście do punktu sprzęgu
  + czas/animacja, zamiast natychmiastowego splitu. MVP: split natychmiastowy po wyborze cięcia na popupie składu.
- **Inne sprzęgi** — typy sprzęgów (śrubowy / Scharfenberg / automatyczny) + walidacja kompatybilności przy łączeniu.
  MVP: dowolne dwa składy.

**Do rozstrzygnięcia w planie (inżynierskie, bez decyzji usera):** orientacja/kolejność `vehicleIds` po merge
(wg geometrii na torze), który `consistId` zostaje vs nowy przy merge/split, update `FleetConsistData` (nazwane
składy), hook couple-popup (event „dojazd do styku za consistem" = completion z `dynamicStopBlockerId>=0`),
rozszerzenie `ConsistPopupUI` o tryb split, persistencja identity przez save/load, ostrzeżenie obieg (gdzie liczone).

**Decyzja:** PRE-EA, po TD-031.

---

### TD-033 — Pracownicy na zewnątrz: ścieżka lub skrót, NIE przez obiekty

**Status:** 🟢 Done (2026-06-09) — zweryfikowane EditMode 585/585 + PlayMode 42/42
**Priorytet:** PRE-EA
**Files (zrealizowane):** NEW `Depot/Nav/{NavObstacle,VisibilityGraphRouter,NavObstacleSetBuilder,DepotNavService,NavSeparation}.cs` + testy `Tests/EditMode/{NavObstacle,VisibilityGraphRouter,NavObstacleSetBuilder,NavSeparation}Tests.cs` + `Tests/PlayMode/DepotNavPlayTests.cs`; EDIT `Personnel/Runtime/{EmployeeWalkPathfinder,EmployeeWalkSimulator,EmployeeVisual}.cs`, `Depot/DepotManager.cs` (bootstrap)

**Zrobione (2026-06-09, sesje TD-033 A–J) — bez skrótów; SCOPE ROZSZERZONY decyzją usera 2026-06-09 z „tylko zewnątrz" do PEŁNEJ nawigacji NPC (wewnątrz+zewnątrz, drzwi, pociągi, separacja):**
- **Pure core (EditMode-testowalny, styl TrackOccupancyMath):** `NavObstacle` (oriented rect AABB+ściana; IntersectsSegment=penetracja wnętrza, grazing-OK; Inflate; ContainsPointStrict), `VisibilityGraphRouter` (deterministyczny A* po napompowanych rogach + waypointy drzwi; brak ścieżki → fallback+flag), `NavObstacleSetBuilder` (ściana→solid split na Door/TrackGate, Window=solid parapet; equipment/furniture→AABB), `NavSeparation` (miękka separacja).
- **Service (`DepotNavService`, bootstrap w DepotManager):** cache obstacle-set + invalidacja (OnWallsChanged/OnPlacementStateChanged/OnBoundsChanged + count-check equipment); `BuildRoute` (preferencja malowanego PathGraph z guard leg-validation „nigdy przez obiekt", inaczej pełny visibility-route); `IsBlockedByConsist` (train-yield przez occupancy TD-031 + projekcja na polyline toru).
- **Integracja (Personnel):** `EmployeeWalkPathfinder.BuildPolyline` → `DepotNavService.BuildRoute` (zastąpiło 4 straight-line fallbacki + konektory; legacy zostaje gdy brak nav-service); `EmployeeVisual.Tick` train-yield (pauza na krawędzi zajętego toru); `EmployeeWalkSimulator.FixedUpdate` soft separation.
- **Drzwi:** otwór = luka (split Door/TrackGate); routing przez światło (jamb-waypointy); `DoorAnimator` (istniejący, proximity) otwiera na podejściu. Guard pokój-bez-drzwi: warn throttled + degradowana linia, nie ciche przenikanie.
- **Decyzje (user 2026-06-09):** visibility graph + AABB (nie NavMesh/steering); pełny routing przez drzwi; pociągi = yield twarde; NPC = soft; PathGraph = opcjonalna preferencja; drzwi bez czekania (otwór zawsze przejezdny).
- **Determinizm (MP/test):** A* sortowany bez hash-order; separacja po posortowanym employeeId dwuprzebiegowo; yield deterministyczny.
- **Save/scene:** nav STATELESS (rebuild z systemów sceny na event/dirty) — brak nowego save kodu; po load/scene-switch eventy odbudowują obstacle-set.
- **Testy:** EditMode +20 (NavObstacle 6 / VisibilityGraphRouter 5 / NavObstacleSetBuilder 5 / NavSeparation 4) = 585/585. PlayMode `DepotNavPlayTests` 2 (BuildRoute wiring + yield z occupancy) = 42/42, zero regresji.
- **Wchłonięte z TD-034:** kolizje/separacja NPC (soft) — zrobione tu.

**Stan obecny (przed TD-033):**
`PathGraph` = chodniki malowane RĘCZNIE przez gracza (`PathBuildStateMachine`, tryb „Buduj ścieżkę").
`EmployeeWalkPathfinder` robi BFS po nim + **STRAIGHT-LINE fallback** w wielu przypadkach (brak grafu /
brak node'a w pobliżu / BFS fail / finalny segment „z końca chodnika do celu"). Fallback idzie PROSTO
**przez ściany**. Drzwi (`doorCells`) zapisane jako dane, ale pathfinder ich NIE używa (DoorPlacer
komentarz: „PathGraph integration ... data prep only"). Kapsuły bez collidera → przenikają ściany/meble.
W typowej zajezdni (gracz nie maluje chodników) → pracownicy chodzą przez ściany.

**Idealne rozwiązanie (doprecyzowanie user 2026-06-08 — poprzedni zapis był błędny):**
Rzecz dotyczy ruchu **na zewnątrz** (teren zajezdni). Reguła:
> **Jeśli jest dostępna ścieżka → idź po ścieżce. Jeśli nie → skrót (najkrótszą drogą przez otwarty
> teren), ALE nigdy przez obiekt.**

- To NIE pełen grid routing przez drzwi (poprzednie „auto-walkability grid + doorCells" było nadmiarowym
  błędnym założeniem). Tylko: ścieżka preferowana + skrót dozwolony + **twarda reguła „nie przenikaj obiektu"**.
- Implementacja: **object/obstacle avoidance na segmencie skrótu** — lista przeszkód (bounding boxy budynków
  + outdoor equipment + ściany) + raycast; gdy skrót przecina przeszkodę → routing dookoła (graf po rogach
  przeszkód / visibility graph albo lokalne steering), zamiast straight-line przez nią. `PathGraph` (malowane
  ścieżki) zostaje jako preferowana trasa gdy istnieje; w innym wypadku skrót-z-omijaniem.

**Decyzja:** PRE-EA.

---

### TD-034 — Żywy cykl pracownika: łazienka/przerwy/przebranie/meldunek + korzystanie z mebli

**Status:** 🟢 Done (2026-06-09) — EditMode 597/597, PlayMode 47/47. SCOPE ROZSZERZONY decyzją usera z „łazienka/przerwy" do pełnego żywego cyklu (occupancy mebli + przebranie przy szafce + łazienka + przerwy + meldunek urealniony).
**Priorytet:** PRE-EA
**Files (zrealizowane):** NEW `Depot/Furniture/FurnitureOccupancyMath.cs` + `Depot/Furniture/Placement/FurnitureOccupancyService.cs`; NEW `Personnel/Runtime/Workflows/{IPersonalNeedProvider,ScheduledNeedProvider,PersonalNeedSchedule,PersonalActivities,MeldunekFlow}.cs`; testy `Tests/EditMode/{FurnitureOccupancyMath,ScheduledNeedProvider}Tests.cs` + `Tests/PlayMode/DepotPersonnelActivityPlayTests.cs`; EDIT `Personnel/Data/{Employee,EmployeeWorkflowState}.cs`, 5× `Runtime/Workflows/*Workflow.cs` + `WorkflowLocations.cs`, `Runtime/{EmployeeVisual,PersonnelDispatcher3D}.cs`, `Catalogs/PersonnelBalanceConstants.cs`, `Depot/DepotManager.cs`.

**Zrobione (decyzje usera 2026-06-09):**
- **Sloty per-mebel** (#2): `FurnitureOccupancyService` (asmdef Depot, bootstrap obok DepotNavService) rezerwuje konkretną instancję mebla na czas czynności; `FurnitureOccupancyMath.PickNearestFree` deterministyczny (dystans + tie-break id). Idle Social też siada na realnym `SeatingRest`.
- **Przebranie przy szafce** (#3): role operacyjne (Mechanic/Driver/Conductor/Cleaner/WashBay) przebierają się przy `StoragePersonal` na wejściu (brama→szafka→meldunek→praca) i wyjściu (praca→szafka→brama; chain bez timera, bo off-shift nie jest tickowany). Tint kapsuły prywatny/roboczy w `EmployeeVisual`. Biurowi nie. Degraded: brak szafki → przebranie „przy bramie".
- **Łazienka + przerwy** (#1): deterministyczny `ScheduledNeedProvider` (seed employeeId+start zmiany) → 1-2× łazienka (`Sanitary`) + 1× przerwa (`SeatingRest`/Social). Lekki addytywny efekt: przerwa −fatigue (placeholder → M-Balance); łazienka/szafka diegetyczne. Seam `IPersonalNeedProvider` pod przyszły SimulatedNeedProvider (potrzeby) BEZ ruszania workflowów.
- **Meldunek urealniony** (#4): `MeldunekFlow` — wymaga dyspozytora OnShift (brak → skip + jednorazowa notyfikacja/dzień; degraded, nie cichy bypass); kolejka 1-naraz przez occupancy biurka, reszta czeka w `QueuingForDispatcher`. Zrefaktoryzowane 4 workflowy (Mechanic/WashBay/Driver/StaticDesk) z inline meldunku na `MeldunekFlow`.
- **Interruptibility:** Driver/Conductor przerwy tylko z idle-Social (nigdy AwaitingDeparture/DrivingTrain — 5 min ≪ 30 min lead); reszta między zadaniami / z WorkingAtStation.
- **Anti-zombie:** koniec zmiany/zwolnienie w trakcie dojścia → `OnArrive`/`ArriveAtDesk`/`StartMeldunek` sprawdzają stan, zwalniają mebel, nie wchodzą w doing-state; `AbortAndRelease` w `OnEmployeeLost`/`HandleEndOfShift`.
- **Save/scene:** wszystko transient (`[JsonIgnore]`: 5 nowych stanów + wearingWorkClothes/lastBathroom/lastBreak/activeActivityKind; occupancy nie zapisywane) → rebuild on load, ZERO zmian schematu save.
- **Determinizm:** harmonogram + find-free + kolejka bez Random/hash-order (seed/sort/stabilna iteracja).
- **Wchłonięte z oryginalnego TD-034:** kolizje NPC = soft separation zrobiona w TD-033.

**Limitacje (świadome, EA):** efekt gameplay lekki (tylko przerwa −fatigue) — pełne potrzeby (bladder/hunger/intra-shift fatigue) = przyszły `SimulatedNeedProvider` (seam gotowy). Pełny łańcuch wizyty + kolejka meldunku weryfikowane manualnie (Depot.unity); automaty pokrywają occupancy/math/harmonogram. Notyfikacja braku dyspozytora = Log.Warn (toast = polish). Tint kapsuły placeholder (M-Models swap).

---

### TD-035 — Koszty budowy: krzyżownica/return-turnout bez mechanizmu + lustrzany refund przy remove

**Status:** 🟢 Done (2026-06-10) — EditMode 598/598, PlayMode 52/52
**Priorytet:** PRE-EA (domknięcie M-Economy Faza 5)
**Files (zrealizowane):** EDIT `Depot/ConstructionCosts.cs`, `TurnoutPlacer.Crossover.cs`, `TurnoutPlacer.Compound.cs`, `PrefabTrackBuilder.Removal.cs`, `PrefabTrackBuilder.cs`, `Depot/Undo/UndoCommands.cs`, `Tests/EditMode/ConstructionCostsBillingTests.cs`, `Tests/PlayMode/RailwayManager.Tests.PlayMode.asmdef` (+Economy ref); NEW `Tests/PlayMode/DepotConstructionBillingPlayTests.cs`

**Korekta stanu faktycznego (research 2026-06-10 — wpis był częściowo nieaktualny):** para REGULARNYCH
rozjazdów JUŻ pobierała 2× mechanizm (`PlaceTurnoutPairOnChains` woła `PlaceTurnoutOnChain` per sztuka).
Realni winowajcy: `PlaceCrossoverOnChain` (krzyżownica) + `PlaceReturnTurnoutDirectly` (rozjazd powrotny
brancha) + asymetria remove. Skala była GORSZA niż „niedoszacowanie" — **dwie drukarki pieniędzy**:
1. **Drukarka #1 (zwykły rozjazd):** remove refundował pre/post (plain) przy darmowym restore i stłumionym
   refundzie members → cykl place→remove = **+T(pre+post) − T(odnóg)** (np. +2.17 mln zł na środku 500 m
   toru), stan torów wracał do wyjścia. Undo place'a drukowało tak samo.
2. **Drukarka #2 (krzyżownica):** place bez charge mechanizmu, a `RemoveTurnout` refundował `TurnoutGroszy`
   bezwarunkowo → **+3.5 mln zł/cykl** + dziura w torze (brak `Original*` metadata → brak restore).
   Wariant: rozjazd powrotny brancha (return turnout) też 0 zł przy place, refund przy remove.

**Zrobione (decyzje user 2026-06-10: krzyżownica=900M gr / pełne lustro / 100% refund):**
- **Cennik:** `ConstructionCosts.TurnoutGroszy` mapuje „Krzyż"/„Crossover" → `KrzyzownicaPodwojnaGroszy`
  (900M gr — martwa stała ożyła) PRZED checkami liczbowymi („Krzyżowy R190" łapał „190"→350M). Refund
  automatycznie spójny (ten sam wzór).
- **`PlaceCrossoverOnChain` = lustro wzorca z `PlaceTurnoutOnChain`:** pre-check CanAfford → silence undo
  (koniec wycieku ~7 mikro-komend) → `Original*` metadata na encji (restore po remove + undo działa;
  snapshot save JUŻ serializował originalPolyline → zero zmian schematu) → po sukcesie Charge + 1
  `TurnoutPlacedCommand`. `TurnoutRemovedCommand.Undo` dostał branch: krzyżownica wraca jako krzyżownica
  (uwaga: odwrócona kolejność argumentów flip/divergeLeft między Place*OnChain).
- **`PlaceReturnTurnoutDirectly`:** charge mechanizmu. **Atomowość:** `PlaceTurnoutPairOnChains` i
  `PlaceBranchWithReturn` pre-check SUMY mechanizmów przed postawieniem czegokolwiek (koniec pół-pary).
- **Pełne lustro remove:** `RemoveTrackInternal` refunduje KAŻDY tor (members też — idą tędy wyłącznie
  z `RemoveTurnout`, brak podwójnego zwrotu; flaga `wasMemberOfTurnout` zostaje dla skip-undo-record);
  restore oryginału CHARGED (refundy lądują wcześniej → stać). Remove zwraca netto mechanizm + T(odnóg)
  = dokładna odwrotność place → **cykl i undo net-zero z konstrukcji**. Kłamliwe komentarze poprawione,
  dead-code `TryMergeTracksAtPosition` skasowany (restore robi tę robotę).
- **Testy:** EditMode +1 (mapowanie krzyżownicy, oba stringi defName/schema) = 598/598. PlayMode NOWE
  `DepotConstructionBillingPlayTests` 5 (pętla place→remove ×3 net-zero ±1 zł; krzyżownica 9 mln + restore
  + net-zero; para atomowa przy braku środków; para 2× mech; branch-return 2× mech) = 52/52, zero regresji.
- **Known-adjacent (świadomie poza scope):** schema głowic stawia N rozjazdów bez pre-check sumy (partial
  przy braku środków — zachowanie jak dotąd); procent odzysku <100% przy rozbiórce → ew. M-Balance
  (jedna stała na cały system budowy).

---

### TD-036 — Kalibracja budżet startowy vs realne ceny budowy/taboru

**Status:** 🟡 Zredukowane (2026-06-10) — research + mini-pakiet analityczny DONE; zostaje WYŁĄCZNIE kalibracja playtestowa → M-Balance
**Priorytet:** M-Balance (= pre-EA milestone — z natury wymaga playtestu)
**Estymata:** tuning iteracyjny (M-Balance)
**Files:** `Core/Difficulty/DifficultyConstants.cs`, `Depot/ConstructionConstants.cs`, `Timetable/Runtime/Economy/{EconomyConstants,CostCalculator,PassengerManager}.cs`, `Fleet/*.json`

**Research 2026-06-10 (pełna mapa liczb zweryfikowana w kodzie):**
- **Wiring DZIAŁA:** budżet = `100M × StartBudgetMultiplier` (preset+slider 0.5-3.0, Normal=1.0 neutral,
  aplikacja `GameCreatorUI.Difficulty.cs:434`); bilet per pasażer (`PassengerManager:622`), koszty per-km
  w locie (TUI 6.31/km + energia 1.10/kWh + paliwo 5.75/L), dotacje dzienne per województwo
  (800-2500 zł/run × punktualność), overhead 5k/dzień, pensje miesięczne ×1.21 ZUS. Mnożniki trudności
  konsumowane w 9 plikach gameplay (StartBudget/Subsidy/Breakdown/Salary/HotelCost/DelayPropagation/
  Demand/OperationalCost).
- **Matematyka startu (Normal 100M):** minimalny start spalinowy ~19M (SA138 nowy) / ~8M (SA134 używany);
  elektryczny ~15M (EN57 Ryba + sieć 2 km — podstacje niewymagane, patrz niżej). Bilans 1 linii regionalnej
  ≈ **+11k zł/dzień** (bilety ~6k + dotacja ~7.2k − koszty ~2k) → zwrot pojazdu ~2 lata; ekspansję finansuje
  budżet startowy. Czy to dobre TEMPO — rozstrzygnie playtest (M-Balance), baza liczb gotowa.
- **Tabor JUŻ zrebalansowany (audyt 2026-06-10):** `new_models.json` + `initial_market.json` mają per-sztuka
  `_priceRebalanceComment` z realnymi źródłami (M6.5 sesje 1-8): ED160 107→135M, SA138 17→14M, EU07 wtórne
  1.5/2.4M→0.6/0.9M, Ryba 9.5→7.5M, SM42 0.7→0.4M. **Zero outlierów do korekty.**

**Mini-pakiet analityczny DONE (2026-06-10, TD-036a):**
- **Postoje per kategoria stacji wpięte:** `CostCalculator.GetPlatformFeeGroszy(station, importance)` mapuje
  `StationImportance` → tiery OIU PLK (Premium 130 / Kat I 50 / Kat II 20 / Kat III 5 / halt 1 zł — stałe
  istniały, były martwe; wcześniej binarna heurystyka 2/10 zł). Progi importance w `EconomyConstants`
  (kalibracja → M-Balance). Akcesor `PassengerManager.GetStationImportance`; fallback `isMajorStation`
  zanim OD matrix wstanie. Call-site `TrainRunSimulator.Movement`. Testy w `EconomyCalculatorTests`.

**Decyzja — podstacje trakcyjne (user 2026-06-10): POST-EA, odnotowane.**
`PodstacjaTrakcyjnaNowaGroszy` (70M) / `Modernizacja` (22M) / `ZasiegKm` (18) to martwe stałe — elektryfikacja
ich dziś NIE wymaga (tylko 1-1.5M/km sieci). Świadomie zostaje tak w EA (prostota); stałe zostają jako
przyszły wiring. **To decyzja gating dla kalibracji budżetu:** z podstacjami start elektryczny ~85M (napięty),
bez nich ~15M (luźny) — kalibracja M-Balance liczy się dla świata BEZ podstacji.

**Zostaje (M-Balance):** tuning budżetu/pacingu/progów importance + 5 placeholderów cen (mebel 8k flat,
obrotnica 8M, pitlift 1.5M, myjnia outdoor 5M, wodowanie 2M) w trusted playtest (save→tweak→load→compare).

---

### TD-037 — Żywy świat przeżywa save/load (runtime TrainRuns + pasażerowie + awarie/rescue + rolling window)

**Status:** 🟢 Done (2026-06-10) — EditMode 615/615, PlayMode 52/52; E2E manual checklist poniżej
**Priorytet:** PRE-EA — symetria z TD-031
**Files (zrealizowane):** NEW `SaveLoad/Modules/{TrainRunsSavable,PassengersSavable}.cs`,
`Timetable/Runtime/Simulation/TrainRunSimulator.Restore.cs` (ActiveRunSnapshot + pending + RestoreActiveRun + orphan-guard),
`Timetable/Runtime/TrainRunWindowTopUp.cs`, `Timetable/Runtime/Economy/PassengerPoolSnapshot.cs`;
EDIT `SaveLoad/Runtime/SaveRegistry.cs` (ModuleOrder +2), `SaveLoad/Modules/MaintenanceSavable.cs` (+rescueOngoing),
`Timetable/Runtime/{TimetableService,TrainRunGenerator}.cs`, `TrainRunSimulator.{cs,Spawn}.cs`,
`Timetable/Runtime/Economy/PassengerManager.cs`, `Maintenance/Runtime/RescueService.cs`;
testy `Tests/EditMode/{TrainRunsRoundTrip,PassengerPoolRoundTrip,RescueRoundTrip,RunWindowTopUp}Tests.cs` (15 testów)

**Korekta stanu faktycznego (research 2026-06-10 — wpis był zbyt optymistyczny):** po load świat był
**MARTWY**, nie „re-derive'owany z rozkładu" — TrainRuns nie były ani zapisywane, ani regenerowane
(`CirculationService.RestoreFromSave` tylko przywracał listę obiegów; jedyne call-sites `GenerateForCirculation`
= aktywacja Draft→Active + diag ContextMenu). Kaskada: VehicleLocationService z OnRoute-duchami, wznowiony
exit-manewr TD-031 bez runa w handshake'u, pasażerowie OnTrain ubici. „Respawn wg czasu odjazdu" działał
wyłącznie przy przełączaniu scen. **Latentny bug przy okazji:** generacja runów była one-shot na okno
(default 4 tyg) — świat umierał po upłynięciu okna NAWET bez save/load.

**Zrobione (decyzje user 2026-06-10: pełna serializacja pasażerów / wznowić awarie+rescue / rolling window w scope):**
- **Moduł `trainruns`** (ModuleOrder po circulations, przed personnel): pełna lista TrainRuns **Z ID**
  (statyczne + runtime: delay/position/segmentId/isCompleted/isCancelled/runningVehicleIds) — serializacja
  zamiast regeneracji, bo `CrewDuty.referencedTrainRunId` (personnel) linkuje załogi po id runa —
  + `ActiveRunSnapshot` per aktywny pociąg (10 pól nie-derived SimulatedTrain: speed/state/stopIndex/
  blockIndex + breakdown×6; CAŁA geometria/bloki/visual = derived w konstruktorze).
- **Pending-restore w symulatorze** (graf mapy buduje się ASYNC po synchronicznym Deserialize):
  snapshoty → `TrainRunSimulator.SetPendingRestore` (static), konsumpcja w FixedUpdate pod gate'em
  gotowości (graf+stacje), PRZED CheckForNewTrains. `RestoreActiveRun` = lustro SpawnTrain bez resetu pól:
  rebuild SimulatedTrain → nadpis stanu → occupy bieżącego bloku (+peron gdy StoppedAtStation) → visual →
  `OnRunSpawned` (załoga re-embed przez CrewAssignmentService, VehicleLocationService SetOnRoute).
  `lastCostDistanceM := position` (zero double-charge km). **Stale-policy:** nieukończony run z innego dnia
  → isCancelled (nie spawnować duchów); dzisiejsze nieaktywne → naturalny ShouldStart (spóźnione startują z delay).
- **Orphan-guard:** po restore sweep OnRoute bez żywego runa → SetAtStation fallback + warn (nie zombie).
  Handshake save-mid-exit działa: manewr (TD-031) + run (TD-037) oba w save → FindMatchingRunForVehicles trafia.
- **Moduł `passengers`:** PEŁNA pula agentów (też czekający na peronach) w formacie KOLUMNOWYM
  (`PassengerPoolSnapshot` — 15 tablic per pole; perf zmierzony: **50k = 134 ms serialize / 53 ms deserialize**)
  + nextAgentId + spawnAccumulator. Konsumpcja w FixedUpdate managera pod gate'ami: OD matrix zbudowana
  ORAZ symulator skonsumował swój pending (kolejność: runy przed pasażerami). Rebuild wszystkich indeksów
  (_agentIdToIdx/_agentsByStation/_agentsOnTrain) + czyste cache capacity. Guard: OnTrain z niewznowionym
  runem → przepada jak przy HandleRunDespawned (zbiorczy log). De facto realizuje M13-8 Phase 2 dla pasażerów.
- **Awarie + rescue:** breakdown w snapshotcie (timery absolutne — HandleBrokenDown/self-repair podejmuje
  po load; AwaitingRescue wznawia się czekając). `OngoingRescue` (czysto timerowe fazy Inbound/Returning)
  → pole `rescueOngoing` w MaintenanceSavable (fallback `?? brak` — bez bump schema), pending w RescueService
  konsumowany w Update. Stany pojazdów (loco/broken InRepair) już w fleet.
- **Rolling window:** `TrainRunGenerator.TopUpForCirculation` — dogenerowuje TYLKO brakujące pary
  (stepIndex, dateIso) BEZ Clear (ID istniejących nietknięte). Hook `TrainRunWindowTopUp` na
  `GameState.OnDayEnded` + wywołanie po restore (save starszy niż okno odżywa od dziś). **Deep-dive crew:**
  duty linkuje po KONKRETNYM run-id, a turnus tygodniowy reużywa duty na wszystkie daty wzorca → crew-link
  działa tylko w dniu generacji (ograniczenie architektury turnusów, istniejące); dla dogenerowanych dat
  działa istniejący fallback dispatcher auto-assign w `CrewAssignmentService.CheckCrew`.
- **Stare save'y bez nowych modułów** → `InitializeDefault` = dotychczasowe zachowanie + top-up dogeneruje
  okno od dziś (świat odżywa). Zero bump schema, zero migratorów.

**E2E manual checklist (do odhaczenia w Unity):** (1) save w trakcie jazdy → load → pociąg kontynuuje
z pozycji/prędkości/opóźnienia, pasażerowie w środku, załoga embedded; (2) save przy awarii → load → nadal
zepsuty, self-repair tika; (3) save przy rescue → misja kontynuuje; (4) save mid-exit-manewr → po load
consist dojeżdża do bramy i run startuje; (5) stary save → świat odżywa przez top-up; (6) spóźnione runy
po load startują natychmiast z delay.

**Testy automatyczne:** EditMode +15 (round-trip runs/snapshot/pasażerowie-kolumnowo/rescue + top-up
date-math + perf 50k) = 615/615. PlayMode 52/52 zero regresji (scaffolding pełnego symulatora w PlayMode
świadomie pominięty — Awake spawnuje 10+ DontDestroyOnLoad singletonów = wzorzec zatrucia suite z TD-038;
pełny łańcuch restore pokrywa E2E manual).

---

### TD-038 — PlayMode cross-class izolacja (DepotEntryTests zatruwa kolejne klasy)

**Status:** 🟢 Resolved (2026-06-09)
**Priorytet:** PRE-EA (higiena test framework) — zrobione
**Files:** `Tests/PlayMode/PlayModeSimTestIsolation.cs` (nowy), `Tests/PlayMode/DepotEntryTests.cs`, `Tests/PlayMode/DepotMovementPlayTests.cs`, `Tests/PlayMode/DepotCouplingPlayTests.cs`

**Symptom (oryginalny):**
`Unity -runTests -testPlatform PlayMode` (cały suite) → 2 fails niezależne od logiki gameplay:
- `DepotEntryTests.EnterThenExit_ManeuversOut_FiresExitedEvent` — `EnqueueExit` zwraca false; **pada też SOLO** (2/3). Klasa to deklarowana „PRÓBA wykonalności headless" — wjazd działa w `-nographics`, wyjazd z bramy nie. Możliwy realny bug exit-z-bramy przy pustej zajezdni LUB limitacja headless.
- `DepotMovementPlayTests.MoverReachesClearTarget` — skład „nie dojeżdża" (center≈start zamiast celu). Pada **tylko w pełnym runie po DepotEntryTests**; SOLO 6/6, z `DepotCouplingPlayTests` 14/14. Pierwszy ruchowy test po ciężkim load/unload `Depot.unity`.

**Root cause (zdiagnozowany 2026-06-09 z logu pełnego runu, NIE physics warm-up):**
- **Problem 1:** rój sim-singletonów `DontDestroyOnLoad` (DeliveryService, PassengerManager, EconomyManager, DispatchService, DepotMapHandshakeService…) bootstrapowanych przez `TrainRunSimulator.Awake` przy ładowaniu `Depot.unity` **przeżywa `UnloadSceneAsync`** i dalej tyka Update/FixedUpdate w kolejnych klasach. Konkretnie `DeliveryService.Update → TryParkInitialDepotFleet` parkuje wyciekłą flotę startową (#1000 @ [20.7,36.6], #1001 @ [37.1,61.6]) na `DepotMovementSimulator.Instance` NASTĘPNEJ klasy (programmatic graf+sim) → fantomowe occupanty na świeżym torze#0 testu → `dynamicStopCap` movera wiąże się na ~10.65m (myśli, że ktoś stoi z przodu) → task **Completed za wcześnie** na center≈10.65 zamiast 75. Tylko pierwszy ruchowy test obrywa, bo `TryParkInitialDepotFleet` parkuje jednorazowo; kolejne dostają świeży tor i nie ma już czego parkować.
- **Problem 2:** fałszywy `vehicleId` 54321 nie istniał w `FleetService` → `ConsistHasTraction({54321})=false` → `EnqueueExit` odrzuca (wyjazd to ruch własnym napędem, `isSelfMove`, wymaga lokomotywy). Wjazd działał bo `SpawnConsistAtEntry` jest `isSelfMove:false`. **NIE limitacja headless** — realny wyjazd z bramy domyka się w `-nographics` (consist cofa za granicę BuildableArea → `EXITED` → `OnConsistExitedDepot`), potwierdzone w logu po nadaniu napędu.

**Rozwiązanie (tylko test-infra, zero zmian gameplay):**
- Nowy `PlayModeSimTestIsolation.HardReset()` — niszczy wyciekłe gameplay-singletony z warstwy DontDestroyOnLoad (skan po namespace: `RailwayManager.*` poza Core + `DepotSystem.*`; **chroni** Core infra GameClock/VehicleLocationService oraz obiekty Unity Test Framework) + resetuje stale `DepotMovementSimulator.Instance` / `PauseStack` / `GameState.IsPaused`+`TimeScale` / `DepotServices` / `VehicleLocationService.ResetAll`. Idempotentny, no-op gdy klasa solo. Wołany w SetUp klas programmatic (Movement/Coupling — zastąpił doraźne czyszczenia) oraz w `[UnityTearDown]` `DepotEntryTests` (sprzątanie u źródła po unload).
- `EnterThenExit` seeduje lokomotywę (EU07 2000kW, status `MovingInDepot` — niewidoczny dla `DeliveryService.ProcessVehicle`/`TryParkInitialDepotFleet`) dla `vehicleId` składu i sprząta w `finally` → testuje realny wyjazd, nie `Assert.Ignore`.

**Wynik:** **pełny PlayMode suite 40/40 zielony** (0 fail, 0 skip), bez świadomych Ignore. Solo, w parach i w pełnym runie. Gameplay coupling/movement nietknięty (TD-031/TD-032 dalej zielone).

**Decyzja:** Zrobione 2026-06-09 (test-infra, nie gameplay).

---

### TD-039 — Brak loading screenu przy wejściu do gry (lag bez feedbacku)

**Status:** 🔴 Active (acute v8 cause naprawiony 2026-06-15; UX-gap loading screen zostaje)
**Priorytet:** M12 Polish / M-UIPolish (loading screen); acute perf już zaadresowany
**Estymata:** ~1-2 sesje (loading overlay + progress events z TimetableInitializer/MapLoader)
**Files:** `Assets/Scripts/Core/SceneController.cs` (scene switch), `Assets/Scripts/Timetable/Runtime/TimetableInitializer.Init.cs` (init), `Assets/Scripts/Map/Loading/MapLoader.cs` (LoadMap)

**Symptom:**
Wejście do gry z menu/kreatora robi zauważalną pracę inicjalizacyjną (load init-state ~80 MB + build/convert grafów + setup mapy + pierwsze renderowanie kafli) BEZ ekranu ładowania ani paska postępu → wygląda jak freeze, frustrujące zwłaszcza że dzieje się CO wejście (nie raz).

**Acute cause v8 (naprawione 2026-06-15):**
`MapSignatureVerifier.VerifyFile` przy KAŻDYM `LoadMap` czytał + SHA-256-ował WSZYSTKIE bloki = cały ~11 GB plik synchronicznie (per-block content-integrity robiony upfront zamiast „during streaming" jak w `rm-v8-integration §3`). Fix: przy otwarciu tylko podpis indexu Ed25519 (~2,2 MB, milisekundy); pełny per-block za flagą `checkAllBlocks=true` (audyt). Log `[MapLoader] v8 podpis: OK (...ms)` pokazuje czas.

**Stan obecny (po acute fix):**
Pozostała praca wejścia (init-state deserialize + graph build + first-frame tile render) nie ma feedbacku UI. Czas pre-v8 był akceptowalny („jakby raz to ok") ale wciąż per-wejście i bez loading screenu. Pokrewne: TD-011 (tile parse sync freeze), TD-026 (MapRenderer hierarchia).

**Idealne rozwiązanie:**
Loading screen / progress overlay podczas scene init (SceneController switch → game) z eventami postępu z `TimetableInitializer` (fazy: load init-state / build graph / setup map / ready) + `MapLoader.OnMapLoaded`. Maskuje hitch + daje feel polish. Rozważyć osobno: czy init-state musi się przeładowywać CO wejście (cache między menu↔gra dla tego samego świata) — to oddzielny wątek perf, może być realnym kawałkiem rezydualnego laga.

**Decyzja:** Acute v8 lag fix zrobiony 2026-06-15. Loading screen + ewentualny init-state cache → M12 Polish / M-UIPolish.

---

### TD-040 — Trawa łapie szeroki specular od słońca (placeholder ground sheen)

**Status:** 🟢 Resolved (2026-06-18 — quick-win: `_Glossiness`+`_Smoothness` → 0 w `LushGrass_Light.mat`)
**Priorytet:** M12 Polish / rework podłoża (M-Models) — lub quick-win wcześniej (1 property)

**Rozwiązanie (2026-06-18):**
Zmatowiono `LushGrass_Light.mat` — `_Glossiness: 0.5 → 0` (Built-in Standard) + `_Smoothness: 0.5 → 0`
(URP/Lit), pipeline-agnostycznie. `GroundGenerator` przypisuje ten asset bezpośrednio jako
`sharedMaterial` (nie runtime clone), więc edycja assetu działa. Docelowy materiał podłoża (PBR matowa
trawa z wear) dalej w M-Models / ground rework — to był quick-win, nie finalny look.
**Estymata:** ~5 min (zjechanie Smoothness) lub przy wymianie materiału podłoża
**Files:** `Assets/Materials/LushGrass_Light.mat` (+ pokrewne `*Ground*.mat`), `Assets/Scripts/Depot/GroundGenerator.cs`

**Symptom:**
Directional light tworzy szeroki, „mokry" specular sheen na trawie zajezdni — wygląda jak dziwne odbicie słońca na płaskim podłożu (zgłoszone przez usera 2026-06-17, screen Depot).

**Stan obecny:**
Placeholder materiał trawy ma za wysoki Smoothness/Glossiness → Built-in Standard rzuca broad specular od słońca. **Pre-existing, NIE z migracji URP** (render Built-in niezmieniony — potwierdzone: po rollbacku flipu 768/768 i scena identyczna). Po przyszłej migracji na URP specular będzie mocniejszy (URP BRDF) → ten sam fix.

**Idealne rozwiązanie:**
Krótkoterminowo: zjechać `Smoothness` trawy ~0 (matowa) — natychmiastowy fix niezależny od pipeline. Docelowo: nowy materiał podłoża (M-Models / ground rework) z właściwym PBR (matowa trawa, ew. delikatny wear), spójny z CBM-target z `depot-visual-direction.md`.

**Decyzja:** Defer do M12 Polish / wymiany podłoża. Quick-win (matowa trawa) opcjonalnie kiedykolwiek wcześniej — to jedna właściwość, nie wymaga reworku geometrii podłoża.

---

### TD-041 — Podgląd trasy (mini-mapa OSM) renderuje tylko 5 warstw + brak widocznych lasów

**Status:** 🟢 Resolved (2026-06-18 — 9 warstw + winding-fix fillów; ⚠ wymaga potwierdzenia wizualnego nad obszarem leśnym)
**Priorytet:** post-EA / M12 Polish (lub gdy ktoś dotyka podglądu trasy)

**Rozwiązanie (2026-06-18):**
(1) **Rozszerzono z 5 do 9 warstw** — `RouteMapPreviewTiles.PreviewLayers` + whitelista
`MapLoader.IsPreviewRenderLayer` zsynchronizowane: dodane fille Buildings/Industrial/Military/Platforms
(kolejność = render queue głównej mapy). POIs/Places (punkty/markery) i AdminBoundaries pominięte — nie mesh.
(2) **Winding-fix fillów** — `MapMeshBuilder.BuildMesh` dostało param `reverseWinding`; `RouteMapPreviewTiles`
liczy go jak główna mapa (`!isLine && !Highways && !Waterways`) i odwraca trójkąty fillów. Bez tego fille
były nawinięte odwrotnie niż główna mapa → przy single-sided materiale backface-culled (podejrzany powód
„lasów niewidocznych"). EditMode `MapMeshBuilderTests` (winding fill/line + height→Y). **⚠ Hipoteza
winding niezweryfikowana headless** — skoro woda (też fill) była widoczna, możliwe że materiały są
double-sided i przyczyna „lasów" to po prostu brak lasów w widoku Łódź-centrum; rozszerzone warstwy +
winding-fix to pokrywają obie ścieżki. Finalne potwierdzenie: user patrzy na podgląd nad lasem.
**Estymata:** ~1-2h (rozszerzenie listy warstw + test wizualny); więcej jeśli okaże się że wypełnienia nie renderują
**Files:** `Assets/Scripts/Timetable/UI/RouteMapPreviewTiles.cs` (`PreviewLayers`), `RouteMapPreview.cs`

**Symptom:**
Podgląd trasy w kreatorze rozkładów (mini-mapa OSM) pokazuje tylko tory, wodę i drogi — brak budynków, landuse, POI. Dodatkowo **lasów nie widać** (zgłoszone przez usera 2026-06-17, podczas weryfikacji wizualnej M-URP).

**Stan obecny:**
`RouteMapPreviewTiles.PreviewLayers` ma **na sztywno 5 warstw**: `Water, Forests, Waterways, Highways, Railways` (linie ~22). Z założenia lekki podgląd — celowo bez budynków/POI/landuse. `Forests` to jedyna warstwa **wypełniona** (poligon) w tym zestawie; reszta to linie. Lasy niewidoczne → albo brak lasów w testowym widoku (Łódź, centrum), albo **wypełnienia (fill) nie renderują się w podglądzie**. Mało prawdopodobne że to regresja URP (Built-in `Unlit/Color` ↔ `URP/Unlit` cullują tak samo, a główna Mapa 2D wyświetla się bez magenty po M-URP), ale **niezweryfikowane** — trzeba zerknąć nad obszar leśny.

**Idealne rozwiązanie:**
(1) Rozszerzyć `PreviewLayers` o pełny zestaw warstw (Buildings/Landuse/...) jeśli podgląd ma pokazywać „wszystko" — z uwagą na koszt (więcej meshy per kafel). LUB świadomie zostawić lekki podgląd i udokumentować decyzję. (2) **Przy okazji zweryfikować że wypełnienia (Forests, Water-jako-poligon) renderują się** w podglądzie i na głównej Mapie 2D na URP — jeśli nie, sprawdzić cull/winding/material (`MapRenderer.GetMaterialForLayer`).

**Decyzja:** Defer post-EA / M12. User chce „minimapa powinna wyświetlać wszystko żeby zbadać temat" — rozszerzenie listy warstw jednocześnie zadziała jako diagnostyka czy wypełnienia w ogóle się renderują.

---

### TD-042 — Etykieta nazwy kafla toolbara (tooltip) za mała i za daleko od przycisku

**Status:** 🔴 Active
**Priorytet:** M-UIPolish (MUI-11/12 — dobicie stanów CBM toolbara + ujednolicenie `ToolbarButtonStates`)
**Estymata:** ~0.5-1 sesja (dedykowana pigułka nad kaflem: pozycja + rozmiar + styl)
**Files:** `Assets/Scripts/SharedUI/ToolbarButtonStates.cs`, `Assets/Scripts/SharedUI/TooltipManager.cs` + `TooltipTrigger.cs`, `Assets/Scripts/SharedUI/UIBuilders.cs` (`AttachTooltip`), `Assets/Scripts/Depot/UI/BuildMenuUI.cs`

**Symptom:**
Po wpięciu stanów CBM (`ToolbarButtonStates`: hover-skala + press) nazwa kafla na hover to wciąż **istniejący generyczny tooltip** — jest **mały i renderuje się dość daleko od realnego przycisku** (przy kursorze / pozycja generyczna), zamiast pigułki w stylu CBM wyśrodkowanej tuż nad kaflem (zgłoszone przez usera 2026-06-17 podczas testu stanów toolbara budowania).

**Stan obecny:**
Hover-skala (`ToolbarButtonStates`) + press działają i wyglądają OK (zaakceptowane). Stan „nazwa na hover" leci przez `UIBuilders.AttachTooltip` → `TooltipManager` (generyczny tooltip), który nie odpowiada ani pozycją, ani stylem pigułce CBM (nad kaflem, większy font, tło akcentu, zaokrąglona). To brakujący kawałek **stanu #2** z modelu 4-stanowego CBM (base / hover / press / selected).

**Idealne rozwiązanie:**
Dedykowana pigułka nazwy nad kaflem (centered, blisko przycisku, większy font, tło akcentu, zaokrąglona) — najlepiej hostowana w `ToolbarButtonStates` lub jako osobny lekki hover-label, zamiast generycznego cursor-tooltipa. Spójna dla wszystkich toolbarów przy ujednoliceniu komponentu (budowanie → główny pasek menu → pod-toolbary).

---

### TD-043 — Konwencja rogów UI: full-bleed ostre / popupy zaokrąglone

**Status:** 🔴 Active
**Priorytet:** M-UIPolish (część re-skinu dark-native, sesja 2026-06-19)
**Estymata:** ~2-4 sesje (13 mismatchów Wzorce 1-3 + konwersja 4 hubów Personnel na full-bleed [Decyzja A] + pełny sweep elementów UI przy okazji)
**Files:** patrz lista per-mismatch (file:line) poniżej

**Kontekst / decyzja (2026-06-19, sesja design-critique re-skin UI; pamięć `ui-corner-system`):**
Mieszany system rogów, kryterium „stały vs przemieszczalny":
- **Stałe panele** otwierane z nav railu, dociągnięte do krawędzi ekranu → **OSTRE rogi (square)**. Zaokrąglony narożnik przy krawędzi prześwituje scenę = źle.
- **Kontekstowe popupy/modale** wywołane kliknięciem encji, pływające na dim → **ZAOKRĄGLONE**.

Reguła „square" jest **wąska**: dotyczy WYŁĄCZNIE full-bleed dotykających krawędzi. Zaokrąglenie zostaje domyślne dla popupów/kart/przycisków/wewnętrznych (zgodne z tezą style-guide „miękkie, zaokrąglone formy"). **Decyzja A (2026-06-19):** duże panele zarządzania ujednolicić do full-bleed (jak Tabor) — w tym huby Personnel obecnie zbudowane jako karty-na-dim.

Sygnał techniczny: `UITheme.ApplySurface(img, color, UIShapePreset.X)` ZAWSZE nakłada zaokrąglony sprite (PanelLarge=18/Panel=16/Inset=12/Button=12/Pill=20/Tab=16). Zwykły `Image` (`img.color=...`, bez sprite) = ostre rogi.

**Audyt 2026-06-19** (workflow `audyt-rogow-ui`, 7 obszarów, ~50 powierzchni):

**Wzorzec 1 — full-bleed panele z rounded rogami → square** (rogi i tak schodzą poza ekran = wizualnie prawie niewidoczne; fix = czystość intencji):
- ✅ **DONE 2026-06-19** FleetPanelUI root (Tabor) — `FleetPanelUI.Layout.cs:39` — ApplySurface PanelLarge → `background.color = PanelBg` (square)
- MainTabBar nav rail — `MainTabBarUI.Build.cs:29` — PanelLarge → square (bgColor)
- RoomBuildPanelUI — `RoomBuildPanelUI.cs:257` — Panel → square (panelBg)
- TimetableCreatorUI — `TimetableCreatorUI.View.cs:25-26` — PanelLarge → square
- CirculationListUI — `CirculationListUI.View.cs:24-27` — PanelLarge → square
- CategoryEditorUI — `CategoryEditorUI.BuildUI.cs:27-30` — PanelLarge → square
- TimetableListUI — `TimetableListUI.cs:465-466` — PanelLarge → square
- FinancePanelUI — `FinancePanelUI.cs:228-229` — PanelLarge → square
- PartsPanelUI — `PartsPanelUI.cs:271-272` — PanelLarge → square
- GameCreatorUI root — `GameCreatorUI.Layout.cs:35` — PanelLarge → square (low-pri)

**Wzorzec 2 — popupy z square rogami → rounded** (WIDOCZNE, realny wizualny zgrzyt):
- Timetable TrackPopupUI — `Timetable/Runtime/Simulation/TrackPopupUI.cs:148-149` — plain Image square + hardcoded kolor → `ApplySurface(bg, WithAlpha(OverlayPanelStrong,0.97f), PanelLarge)` + paleta UITheme (jak siostry Train/StationPopupUI)
- RebindModalUI — `MainMenu/RebindModalUI.cs:62` — plain Image square karta na dim → `ApplySurface(card, CardBg, PanelLarge)` (jak siostrzany GameCreator CancelConfirmation)

**Wzorzec 3 — niespójność architektury (2 siostrzane panele z 1 nav railu):**
- WorkshopsPanelUI vs PartsPanelUI — `Maintenance/Runtime/WorkshopsPanelUI.BuildUI.cs:42-53` — Workshops ma dimmer 0.72 + pływającą kartę 24px margines rounded-18 (wygląda jak modal); Parts = full-bleed nieprzezroczysty. Ujednolicić Workshops do wzorca Parts (usunąć dimmer+shell, root full-bleed square).

**Wzorzec 4 — Decyzja A: Personnel huby card-on-dim → full-bleed** (4 duże huby; małe modale Personnel ZOSTAJĄ rounded card-on-dim):
- PersonnelMainTabUI (hub 1200×800 na dim) — `Personnel/UI/PersonnelMainTabUI.cs:260,269` — full-bleed + zdjąć dim + square
- RecruitmentUI (1000×700) — `Personnel/UI/RecruitmentUI.cs:131,142`
- CrewCirculationListUI (1200×780) — `Personnel/UI/CrewCirculationListUI.cs:108,117`
- EmployeeScheduleEditorUI (900×720) — `Personnel/UI/EmployeeScheduleEditorUI.cs:146,155` (borderline — zweryfikować czy hub czy kontekstowy edytor)
- **UWAGA HUD przy konwersji:** huby Personnel obecnie zasłaniają minimapę/alerty swoim dim overlayem; po przejściu na full-bleed (bez dim) trzeba dodać `SceneController.FullscreenOverlayOpen = true/false` w open/close, inaczej HUD wyjdzie nad panel (jak Tabor przed fixem 2026-06-19 — `FleetPanelUI.Show/Hide`).
- ZOSTAJĄ rounded (kontekstowe): EmployeeDetailsUI (drill-down), EmployeeQualificationsUI, HotelBookingModal, CrewAutoGeneratorModal, RecruitmentUI.PostingModal, CrewCirculationEditorUI, EmployeeScheduleEditorUI.ContextMenu.

**Poprawne — referencja, NIE ruszać:** popupy Depot (Track/Turnout/Building/Consist/CouplePrompt, RoomType/RoomLevel/Branch/Schema dialogs, PauseMenu, FurnitureContextMenu), Fleet detal+koszyk, modale Timetable (Deadhead/ReturnTrip/Options/Assign/AutoGen/VehicleAssignment), RelationPlanner, DepotLocationPicker, RescueDispatch, Assistant Whisper/Panel/PlanPreview, GameCreator CancelConfirmation, ekrany MainMenu (square via `CreateFullscreenRoot` — wzorzec referencyjny full-bleed).

**WAŻNE — przy wykonaniu (uwaga usera 2026-06-19):** audyt pokrył ROOT-bg powierzchni. Przy realnym fixie **przejść przez WSZYSTKIE elementy UI** (nie tylko root), bo przy okazji wyłapiemy: martwy kod (nieużywane panele), wcześniej przeoczone niespójności (kolory/spacing/presety), elementy poza audytem. Wstępni kandydaci: rozwijane listy dropdownów (`templateImg.color` square → rounded-12 dla spójności floating UI), `SettingsScreenUI.BottomBar` (rounded-18 docked — niespójne z innymi paskami), GameCreator Sidebar/BottomBar (rounded docked), TrackSubToolbarUI (poza zakresem audytu — domknąć).

**Idealne rozwiązanie:**
Pass M-UIPolish: (1) Wzorce 1+4 — helper `UITheme.ApplyFullBleedPanel(img, color)` (zwykły Image square, jawna intencja) zamiast ad-hoc; przepiąć 10 paneli + huby Personnel. (2) Wzorce 2-3 — punktowe fixy (2 popupy + ujednolicenie Workshops). (3) Pełny sweep elementów UI przy okazji. Konwencja w `docs/design/visual-style-guide.md` §9 + pamięć `ui-corner-system`.

**Decyzja:** Defer do dobicia stanów CBM w M-UIPolish. Hover-skala/press zaakceptowane 2026-06-17; pigułka-nazwa to następny krok polish razem z ujednoliceniem `ToolbarButtonStates`.

---

### TD-044 — Tory wyjazdowe zajezdni = sztywny placeholder (ExitTrackController) zamiast player-configurable konfiguratora

**Status:** 🔴 Active
**Priorytet:** post-EA (gameplay/UX) — większość klocków już istnieje, więc to „spięcie + UI", nie greenfield; rozważyć wcześniej jeśli wejdzie w scope reveal/slice
**Estymata:** ~2-3 sesje (UI panel + integracja z istniejącymi systemami + billing + save)
**Files:** `Assets/Scripts/Depot/ExitTrackController.cs` (przebudowa) + integracja `PrefabTrackBuilder`/`ParallelTrackGenerator`, `Catenary/ElectrificationStateMachine`, ekonomia (wzór billing TD-035), `DepotSavable` (persist config)

**Stan obecny:**
`ExitTrackController` buduje tory wyjazdowe **automatycznie w `Start()`** (`BuildExitTracks`) ze sztywnych parametrów Inspektora: `numberOfExitTracks=3`, `exitTrackSpacing=6m` (= międzytorze), `firstTrackZ`, `exitTrackLength=200m`, `exitEdgeX=-2000`. Pociągi jadą w lewo do krawędzi mapy i despawnują. **Brak UI dla gracza** (parametry tylko w Inspektorze = dev-time), **brak elektryfikacji** torów wyjazdowych, **brak kosztu w walucie**, prymitywny fallback (Cube rails/sleepers gdy brak prefabu), **niezintegrowane** z właściwym `PrefabTrackBuilder`/`TrackGraph`/`Catenary` (osobny, równoległy system — pociągi nie jadą po TrackGraph). Z `Depot/CLAUDE.md`: „Permanentne tory zewnętrzne (`GenerateExternalTracks`) idą zawsze, niezależnie od flag." Konfiguracja nie jest zapisywana (auto-rebuild on load).

**Idealne rozwiązanie (= feature target, słowa usera 2026-06-19):**
Panel/UI + działający **konfigurator torów wyjazdowych**, gdzie gracz **za walutę gry** może: (a) przestawić tor góra/dół (Z), (b) dodać kolejne tory z parametrami (**międzytorze**/spacing), (c) **elektryfikować**. Spiąć z **istniejącymi** systemami zamiast budować od zera: `ParallelTrackGenerator` (już ma równoległe tory + spacing), `ElectrificationStateMachine`/`Catenary` (elektryfikacja już istnieje), billing jak **TD-035** (charge/refund + undo, net-zero), `DepotSavable` (persist konfiguracji). Wizualnie spójne z resztą torów (real rail/sleeper z M-Models zamiast prymitywnego fallbacku).

**Decyzja kiedy:**
Post-EA polish — **nie EA-blocker** (auto-tory działają, gameplay nieblokowany). ALE większość klocków już jest (parallel + spacing + electrification + billing pattern TD-035 + undo), więc koszt to głównie **UI + spięcie + save**, nie nowy system. Gameplay sub-decyzje (ile kosztuje przestawienie/dodanie/elektryfikacja, czy limit torów) = **OQ przy implementacji**.

---

### TD-045 — Szyny na łukach: proste segmenty 1 m nie spasowują się gładko z krzywizną toru

**Status:** 🔴 Active
**Priorytet:** post-EA / M-Models polish (wizualne, nie blokuje gameplayu)
**Estymata:** ~1-2 sesje
**Files:** `Assets/Scripts/Depot/PrefabTrackBuilder.Generators.cs` (`GenerateRailPrefabs`)

**Symptom:** Na łukach toru szyny wyglądają na „kanciaste"/odstające od idealnego łuku — widoczne załamania między kolejnymi segmentami (screen usera 2026-06-20).

**Stan obecny:** `GenerateRailPrefabs` instancjonuje **prosty prefab szyny 1 m** co 1 m wzdłuż `railPolyline`, z `LookRotation(tangent)` per segment. Na łuku proste segmenty tworzą wielokąt aproksymujący krzywą — im ostrzejszy łuk, tym wyraźniejsze załamania na stykach + szyna potrafi odstawać od osi łuku. (Dotyczyło placeholdera LineRenderer mniej, bo on podążał za polyline; prefab-segmenty są sztywne.)

**Idealne rozwiązanie:** (a) krótsze segmenty na łukach (adaptacyjna długość wg krzywizny), albo (b) **gięcie mesha** szyny wzdłuż polyline (bend/curve deform per segment), albo (c) mesh sweptowy generowany wzdłuż całej krzywej zamiast instancjonowania (jak ballast quady podążają za polyline). Wariant (a) najtańszy, (c) najładniejszy.

**Decyzja kiedy:** Post-EA / przy dopracowaniu M-Models toru. Nie blokuje — proste tory wyglądają dobrze, łuki w zajezdni rzadkie i łagodne.

---

### TD-046 — Szyny/podkłady nie dociągają do samego końca toru

**Status:** 🔴 Active
**Priorytet:** post-EA / M-Models polish
**Estymata:** ~0.5 sesji
**Files:** `Assets/Scripts/Depot/PrefabTrackBuilder.Generators.cs` (`GenerateRailPrefabs` railCount, `GenerateSleepers` sleeperCount)

**Symptom:** Na końcu toru brakuje kawałka — szyna/podkłady kończą się przed faktycznym końcem segmentu (screen usera 2026-06-20).

**Stan obecny:** Liczba elementów liczona przez zaokrąglenie: `sleeperCount = FloorToInt(totalLength / sleeperSpacing)`, `railCount = CeilToInt(totalLength / 1f)`. Floor dla podkładów = ostatni fragment (do 0,6 m) bez podkładu. Rail ceil + instancjonowanie na `dist = i*1` może wystawać lub nie domykać przy resztach niecałkowitych. Brak „domknięcia" ostatniego elementu do końca polyline.

**Idealne rozwiązanie:** Dociągnąć ostatni element do końca — np. dodatkowy podkład na `totalLength` (koniec) jeśli reszta > próg; dla szyn ostatni segment skalować/przyciąć do reszty albo gwarantować pokrycie do `totalLength`. Drobny fix w pętlach generatorów.

**Decyzja kiedy:** Post-EA / M-Models polish. Kosmetyczne, nie blokuje (środek toru OK; widoczne tylko na samych końcach).

---

## Workflow dodawania nowego TD

1. Znaleźć problem (= "działa ale nie idealnie", MVP shortcut, niedopolerowany UX).
2. Sprawdzić czy to nie open question (= decyzja jeszcze nie podjęta — wtedy to nie tech debt).
3. Dodać tutaj jako `TD-XXX` (= następny wolny number) z sekcjami:
   - Status / Priorytet / Estymata / Files
   - Symptom (jeśli user-facing)
   - Stan obecny
   - Próbowane rozwiązania (jeśli były)
   - Idealne rozwiązanie
   - Decyzja kiedy fixować
4. Update spis zawartości na górze pliku.
5. Commit z prefiksem `docs(tech-debt):`.

## Status conventions

- 🔴 **Active** — problem nieadresowany, znany.
- 🟡 **Partially fixed** — częściowo rozwiązany, edge cases zostają.
- 🟢 **Resolved** — naprawiony, można przenieść do `RESOLVED_TECH_DEBT.md` (= history) lub usunąć.
- ⚫ **Won't fix** — świadoma decyzja że NIE fixujemy (= MVP shortcut accepted, QoL not worth effort).
