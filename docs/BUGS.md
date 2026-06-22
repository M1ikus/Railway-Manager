# Bugi

> **Cel dokumentu:** Rejestr znalezionych bugów które NIE są naprawione natychmiast. Bug naprawiony w tej samej sesji → tylko commit message wystarczy.
>
> **Convention:**
> - **BUG-XXX** numbering chronological (nie reorderować).
> - **Severity:** Critical (blocker) / High (workaround OK) / Medium (kosmetyczny+) / Low (nice-to-have fix).
> - **Status:** 🔴 Active / 🟡 Investigating / 🟢 Resolved / ⚫ Won't fix / 🔵 Cannot reproduce.

> **Vs `TECH_DEBT.md`:** bug = NIE działa zgodnie z designem (= regression / oversight). Tech debt = działa ale nie idealnie (= świadomy MVP shortcut).
> **Bug to błąd implementacji** — w odróżnieniu od otwartej decyzji design'owej, która jest jeszcze przed nami.

> **Migration:** przy M14 EA launch import do GitHub Issues (search/labels/assign lepsze dla testerów).

---

## Spis zawartości

### Active 🔴
- **BUG-086** — GameCreator/Difficulty: NRE `_ddDifficultyPreset.captionText.text` (captionText nigdy nie przypisany w `BuildDifficultyDropdownRow`, brak też template dla rozwijanej listy). Klik na zakładkę „Rozgrywka" crashuje. Wzór poprawny w `SettingsScreenUI.RowBuilders.DropdownRow`. Discovered 2026-05-14.

### Investigating 🟡
_(brak)_

### Resolved 🟢 (history)
- **BUG-001** — LocalizationService race Bootstrap vs Awake → BeforeSceneLoad + lazy init defense + emitEvent: true + MultiplayerScreenUI._createLbl field (2026-05-07)
- **BUG-002** — ScrollRect hierarchy fix (Help/Mods/LoadGame) → MenuScreenPrimitives.BuildVerticalScrollArea helper z poprawną hierarchią + Scrollbar Vertical (2026-05-07)
- **BUG-003** — Lokalizacja Mods intro/empty + LoadGame tag.auto/action.open_save (PL/EN/DE/CZ) (2026-05-07)
- **BUG-004** — ModsScreenUI.OnOpenModsFolder → Directory.CreateDirectory idempotent + Mac/Linux Application.OpenURL fallback (2026-05-07)
- **BUG-005** — EditorBuildSettings.asset GameCreator.unity enabled: 0 → 1 (2026-05-07)
- **BUG-006** — `IconGenerator.GetResetSprite()` rasteryzuje arc + arrowhead w runtime → Image component zamiast TMP `↺` (żaden z 4 TTF projektu nie ma U+21BA). Plan B side benefit: fallback chain runtime font naprawiony (NotoSans + NotoSansSymbols2 + NotoSansJP w Resources/Fonts) — działa dla glyphów które TTFs faktycznie zawierają (2026-05-07).
- **BUG-007** — MainMenuUI.OnNewSinglePlayer → GameCreatorContext.Mode = SinglePlayer + LoadScene("GameCreator") (2026-05-07)
- **BUG-008** — Credits + Multi server-list scroll → migracja na MenuScreenPrimitives.BuildVerticalScrollArea (2026-05-07)
- **BUG-009** — DifficultyConstants.cs w Core/Difficulty/ (nie EconomyConstants — GameCreator asmdef nie referuje Timetable). Przeniesione 3 stałe: BaseStartingBudgetPln (100M zł) + BaseDriverSalaryPln (9k zł) + BaseBreakdownPerKm (500). GameCreator partial używa aliasów (2026-05-07)
- **BUG-011** — MultiplayerScreenUI._refreshLbl + RebindModalUI._cancelLbl jako pola klasy + `RefreshLanguage()` re-applies labele (subscribe na OnLanguageChanged w RebindModalUI) (2026-05-07)
- **BUG-012** — Klucze i18n w PL/EN/DE/CZ: multiplayer.action.refresh + settings.rebind.change + rebind.modal.{cancel,prompt,timeout_format,title_format}. 5 hardkodowanych stringów PL → LocalizationService.Get (2026-05-07)
- **BUG-013** — PayrollConstants: QuickBonusSmall/Medium/Large Groszy (100k/500k/1M groszy = 1k/5k/10k zł). EmployeeDetailsUI buttons używają stałych z (int) cast (2026-05-07)
- **BUG-014** — IconGenerator.GetSearchSprite() — procedural lupa (kółko + uchwyt 45°) jako Image component zamiast emoji 🔍. MultiplayerScreenUI search bar używa Image z TextMuted tint (2026-05-07)
- **BUG-015** — SettingsScreenUI.BuildContentArea → MenuScreenPrimitives.BuildVerticalScrollArea (Scrollbar Vertical AutoHideAndExpandViewport) (2026-05-07)
- **BUG-016** — fleet.currency.{million_format,from_million_format} klucze i18n (PL "M zł" / EN/DE/CZ "M PLN"). FleetPanelUI.FamilyConfigurator: 2× hardkodowane currency stringi → LocalizationService.Get z InvariantCulture decimal formatowaniem (2026-05-07)
- **BUG-017** — fleet.detail.seat_preview.{title,placeholder} klucze i18n. FleetPanelUI.DetailPopup owned popup placeholder z neutralnym "Wkrótce" zamiast "(TODO)" (2026-05-07)
- **BUG-018** — SettingsService.Bootstrap → BeforeSceneLoad (zsynchronizowane z LocalizationService timing — formalizacja idempotent EnsureExists wywołań) (2026-05-07)
- **BUG-019** — RailwayGraph.FindPath(Vector2,Vector2) usunięte jako dead code (zero callerów; FindPath jest w RailwayPathfinder.FindPath statycznym + TrackGraph.FindPath instance dla DepotMovement) (2026-05-07)
- **BUG-010** — Personnel TODO M8-5/M8-11 (3 miejsca): cz.3 real fix — `PersonnelService.IsAvailableOnDate` + blokada w `GetAvailableFor` (urlop/L4/training NIE pozwalają na służbę), `CrewCirculationValidator` Warning → Error przy konflikcie. cz.1+2 placeholder — `EmployeeQualifications` data + `EmployeeQualificationsUI` popup z EA disclaimer (gameplay zostaje permissive, decyzja user'a "pracownicy nie potrzebują kwalifikacji w EA"); kwalifikacje rozbudowywane post-EA (2026-05-07)
- **BUG-020** — PersonnelSavable v2: dodane `qualifications` JObject (int→EmployeeQualifications). Save tylko aktywnych employees (BUG-026 fix free). Migrator v1→v2: brak pola = pusty dictionary (2026-05-07)
- **BUG-021** — MaintenanceJobsSavable nowy moduł: bundle 5 list + nextJobId per service (Modernization/Modification/Outdoor/SelfPainting/Painting). Każdy service dostał `RestoreFromSave(jobs, nextJobId)` + `GetNextJobId()` accessor (2026-05-07)
- **BUG-022** — PersonnelSavable v3: dodane `dispatchActive`/`dispatchPending`/`dispatchNextId`. DispatchActionService.RestoreFromSave + GetActive/PendingSnapshot + GetNextDispatchId (2026-05-07)
- **BUG-023** — PersonnelService.Qualifications.Clear() w Initialize() i ResetAll() (BUG-010 oversight fix) (2026-05-07)
- **BUG-024** — GameState.ActiveDlcCountries → IReadOnlyList property + AddDlcCountry/RemoveDlcCountry/ResetDlcCountries API. Backing field private. RemoveDlcCountry blokuje usunięcie ostatniego kraju (2026-05-07)
- **BUG-025** — DispatchActionService.ResetAll() unhook OnEmployeesChanged + reset _eventsHooked (free fix przy okazji BUG-022) (2026-05-07)
- **BUG-026** — PersonnelService.GetQualifications skip lazy-create dla nieaktywnych (transient empty) + Fire usuwa quals z dictionary. BUG-020 save filter już chronił output (2026-05-07)
- **BUG-027** — SceneController.Reset() → StopAllCoroutines + odhokuj OnSceneLoaded delegata (2026-05-07)
- **BUG-028** — TimetableInitializer.OnSceneLoaded: idempotent guard `FindAnyObjectByType<BootstrapDelayer>` przed spawn → no duplicate GameObject per scene load (2026-05-07)
- **BUG-029** — FleetService.Initialize() → `_cart.Clear()` przed LoadStartingFleet (2026-05-07)
- **BUG-030** — PersonnelService.IsAvailableOnDate normalizuje ISO przez `NormalizeIsoDate()` helper (Substring(0,10)) — kompatybilne z 10-char ISO date i 19-char datetime (2026-05-07)
- **BUG-031** — CrewCirculationValidator: TimeSpan.TryParse zamiast try/catch + Warning gdy fail (2026-05-07)
- **BUG-032** — WallBuildingSystem.Mesh.cs: usunięty silenced try/catch — tag "Wall" zarejestrowany w ProjectSettings/TagManager.asset, więc set działa (2026-05-07)
- **BUG-033** — TrainRunSimulator log rate limit: HashSet<int> _alreadyWarnedTrains. Reset przy despawn (2026-05-07)
- **BUG-034** — WorldCoordPicker: cache _cachedFallbackCam + invalidate przy zmianie sceny (2026-05-07)
- **BUG-035** — FleetService encapsulacja: 4 backing fields private + IReadOnlyList properties + mutator API (AddOwnedVehicle/RemoveOwnedVehicle/AddToCart/RemoveFromCart/ClearCart/AddConsist/RemoveConsists/Load*Snapshot). CartProcessor + FleetSavable callerzy zaktualizowani na nowe API (2026-05-07)
- **BUG-036** — JobPostingService: backing field `_activePostings` private + IReadOnlyList property + LoadSnapshot dla save/load. Internal mutations używają _activePostings direct (2026-05-07)
- **BUG-038** — `Core/IsoTime.cs` helper z `CultureInfo.InvariantCulture` + automatyczna replace 44 wywołań `DateTime.Parse`/`TimeSpan.Parse`/`TryParse` w 22 plikach (Personnel, Timetable, Economy, GameState). Eliminuje throw przy `tr-TR`/`fa-IR`/`de-DE` locale (2026-05-07)
- **BUG-039** — `VehicleLocationService.ResetAll()` static method + wywołanie w `FleetSavable.InitializeDefault` przy nowej grze. Eliminuje cross-session data leak singleton DontDestroyOnLoad (2026-05-07)
- **BUG-040** — EconomyManager: `HistoryMax = 365` const + `while (Count > HistoryMax) RemoveAt(0)` w EndDay (FIFO trim). Save bloat eliminated dla 5+ lat gameplay (2026-05-07)
- **BUG-041** — PersonnelNotificationToastUI hybrid expire: `gameTimeRemaining` (1h game, scaled) + `realTimeRemaining` (60s real, unscaled). Toast znika gdy KTÓRYŚ minie — działa przy pauzie (2026-05-07)
- **BUG-042** — SchemaThumbnailGenerator: `RenderTexture`/`Texture2D`/`GameObject camGO` deklarowane przed try, cleanup w finally. Exception-safe (2026-05-07)
- **BUG-043** — MapSetup: `_cachedMapLoader` field + `GetMapLoader()` lazy helper. 3× FindAnyObjectByType → 1× per session (2026-05-07)
- **BUG-044** — DepotSavable: 6 cache fields (TrackGraph/WallBuildingSystem/RoomDetectionSystem/FurniturePlacer/OutdoorEquipmentPlacer/CatenaryGenerator) + `Get*()` lazy methods + `InvalidateSceneRefs()` na początku Serialize/Deserialize/InitializeDefault. 15× FindAnyObjectByType → 6× per save/load (2026-05-07)
- **BUG-045** — CatenaryGenerator: `EnsureSceneRefs()` helper. 3× powtórzony block FindAny → 1× metoda (2026-05-07)
- **BUG-046** — FleetMarketRefreshService.ResetAll() + wywołanie w FleetSavable.InitializeDefault. Reset countdown przy nowej grze (2026-05-07)
- **BUG-047** — DepotMovementSimulator.Movement: `Mathf.Approximately` zamiast `==` na floatach (Unity best practice, NaN safety). Pre-filtering epsilon 0.01 zachowany (2026-05-07)
- **BUG-048** — WorkshopsPanelUI/PartsPanelUI: `_refreshTimer += Time.unscaledDeltaTime` zamiast `Time.deltaTime`. UI refresh działa przy pauzie (2026-05-07)
- **BUG-049** — CleaningService wash → `cleanlinessPercent` (osobny field) zamiast `conditionPercent` (nadpisywany przez Average). +20 cleanliness gdy < 80 (2026-05-07)
- **BUG-050** — PersonnelService morale bonus: `(int)Math.Round((amountGroszy/100f) / 1000.0)` zamiast int/int — 1990 zł teraz dostaje 2× zamiast 1× per-thousand bonus (2026-05-07)
- **BUG-051** — TrainRunSimulator delay cascade: `Math.Max(beforeDelay, propagatedDelay)` zamiast nadpisywania. NextRun's pre-existing delay nie jest skasowany (2026-05-07)
- **BUG-052** — CartProcessor `CurrentGameYear()` helper z `IsoTime.ParseDate(GameState.CurrentDateIso).Year` (try/catch fallback 2024). Wszystkie 9 callsites w pliku zaktualizowane (2026-05-07)
- **BUG-053** — DegradationService: usunięty mnożnik `× 100f` z 12 component formul. Stale `0.00008/km` itp. są w jednostkach % per km (engine 1.25M km, wheels 500k km) — sensible real-world rates (2026-05-07)
- **BUG-054** — EconomyManager `_pendingMoneyGroszy` akumulator + `FlushMoneyAccumulator()`. Sub-1zł amounts agregowane do flush'owania gdy >= 100 gr — bilet 99gr × 100 ticki = 99 zł zachowane (2026-05-07)
- **BUG-055** — WorkshopManager: `Mathf.Min(80, vehicle.maxSpeedKmh)` zamiast hardcoded 80 km/h. Pendolino/FLIRT 160 mogą szybciej dostarczać do ZNTK gdy ich max < 80 km/h (2026-05-07)
- **BUG-056** — ReputationManager: `TryGetValue` jako primary lookup zamiast `cur == 0 && !ContainsKey`. Reputacja sprowadzona do 0 (po Accident events) nie resetuje się do RepStart przy następnym pozytywnym event (2026-05-07)
- **BUG-057** — HotelBookingService.CreatePrivateFallback: cumulative cap 4.0× (`HotelPrivateFallbackMult × diffMult` clamped). Plus rozszerzony komentarz wyjaśniający multiplicative kary jako design intent (2026-05-07)
- **BUG-058** — PersonnelService.SetSalary cap 5× expected dla danej roli/skill. Defense przeciw save corruption / int.MaxValue. Log warn + clamp (2026-05-07)
- **BUG-059** — PersonnelService.Hire: walidacja firstName/lastName non-empty + skill clamp przesunięty przed capacity check (defensive ordering) (2026-05-07)
- **BUG-061** — SickLeaveService: rozszerzony komentarz wyjaśniający `Range(min, max+1)` exclusive end + `AddDays(daysSick - 1)` semantykę dnia bieżącego (2026-05-07)
- **BUG-062** — CostCalculator.DailyOverheadGroszy: poprawiony komentarz — overhead to fixed costs (utility/wynajem/ubezpieczenia), NIE pensje (te w osobnej `Personnel` kategorii). Działa równolegle z PayrollService (2026-05-07)
- **BUG-060 v2** — re-open z Won't fix → Resolved: morale rozdzielony na 4 buckets per source (salary 35 + fatigue 25 + overtime 25 + room 15 = 100). `MoraleBreakdown` data class + Employee.moraleBreakdown field + per-bucket caps stałe. FatigueMoraleTickService rebuild na per-bucket targets. 5 external mutations (HotelReject, PayrollMissed, ColleagueFired, Bonus, Raise) zaktualizowane → ApplyDeltaToSalary/Room. PersonnelSavable v3→v4 (lazy migration via FromLegacyMorale). User design: "salary max 35, fatigue 25, overtime 25, room 15" — eliminuje frustrację gracza (lvl 5 Supervisor zawsze ma value, niezależnie od salary stanu) (2026-05-07)
- **BUG-064** — CirculationAutoGenerator: `batchReservedVehicles` HashSet tracked w pętli `Generate()`. `AssignBestVehicle` skip pojazd już zarezerwowany w current batch. Po Apply nie ma duplikatów (2026-05-07)
- **BUG-065** — TrainRunGenerator: explicit `DateTime.SpecifyKind(date, DateTimeKind.Utc)` w `DateTimeOffset` constructor. Eliminuje desync między machinami w różnych timezone'ach (M10 MP, save share) (2026-05-07)
- **BUG-066** — ShiftManager Cycle4_2/Cycle6_2 → rolling pattern z `RollingCyclePosition(date, cycleLen)`. Epoch = GameStartDateIso. 4_2 = 6-day cycle (4 work + 2 rest), 6_2 = 8-day cycle (6+2). Cycle5_2/7_7 zostają week-based (design intent — synchronizowany cały zespół) (2026-05-07)
- **BUG-067** — TrackBuildStateMachine.IsPolylineOverlapping: dynamic sample count `Clamp(usableLen/5, 3, 50)`. Tor 200m dostaje ~40 samples zamiast 3 — kolizje przez środek toru wykryte (2026-05-07)
- **BUG-068** — DispatcherService rotation fairness: `_recentDispatchCount` tracker + `RecentDispatchPenalty 0.1` per-dyspozycja (cap 5). Plus `ResetRecentDispatchCounts()` daily reset method. Zespół rotuje się w użyciu zamiast jednego "championa" (2026-05-07)
- **BUG-069** — CirculationAutoGenerator: `GeneratorSettings.maxGapMinutes = 240` (default, user-konfigurowalne). Magic number 240 wycięty z `IsCompatibleNext`. UI może teraz expose tę wartość (2026-05-07)
- **BUG-072** — ModernizationJob.ScheduleExternal: dodane `job.durationSec = externalDurationSec` (defensive — wcześniej tylko Internal ustawiał) (2026-05-07)
- **BUG-073** — SubsidyCalculator.CalculateDailySubsidy: `(long)Math.Round(...)` + clamp do `int.MaxValue` z log warn. Modded gameplay z extreme subsidy nie owerfłowuje cicho (2026-05-07)
- **BUG-076** — BinaryFormat.cs: usunięte 4 dead duplicate metody/stałe (`TILE_SIZE`/`GetTileID`/`WorldToGrid`/`GetTileBounds`) — identyczne z `TileGrid.cs`. Grep `BinaryFormat.GetTileID/WorldToGrid/GetTileBounds/TILE_SIZE` = 0 hits. Single source of truth: `TileGrid.cs` (formap namespace) (2026-05-08)
- **BUG-077** — TileGrid.GetTileID: defensive range check ±100,000 (Cantor pairing requires bounded inputs). Log.Warn gdy gridX/gridY poza zakresem — zapobiega cichym kolizjom tileID. Polska mieści się w ~±70 grid units, więc 1400× zapas, ale przyszłe DLC EU może nieostrożnie pchać większe wartości (2026-05-08)
- **BUG-078** (High) — `CirculationsSavable.Deserialize` nie restorował `CirculationService._nextId` (zostawał 1 mimo loaded id=2..10) → nowy `AddCirculation` po load = id=1, **kolizja z istniejącym obiegiem #1**. Fix: `CirculationService.GetNextId()` + `RestoreFromSave(circs, nextId)` API + serialize `nextId` w savable + fallback `max(id)+1` dla starych saveów. Wzorzec analogiczny do `ModernizationJobService.RestoreFromSave` (2026-05-08)
- **BUG-079** (High) — `PersonnelSavable.Deserialize` nie restorował `PersonnelService._nextEmployeeId` → nowy `Hire` po load = employeeId=1, **kolizja**. Fix: `GetNextEmployeeId()` + `RestoreNextEmployeeId(int)` + serialize `nextEmployeeId` + fallback `max(employeeId)+1` (2026-05-08)
- **BUG-080** (High) — `JobPostingService.LoadSnapshot(snapshot)` nie restorował `_nextJobPostingId` → nowy `CreatePosting` po load = jobPostingId=1, **kolizja**. Fix: `GetNextPostingId()` + nowa `RestoreFromSave(snapshot, nextPostingId)` API + serialize `nextJobPostingId` + fallback `max+1`. Stara `LoadSnapshot` zachowana dla compat (2026-05-08)
- **BUG-081** (High) — `PersonnelSavable.Deserialize` direct `CrewCirculationService.All.Add()` bypass'ował restore `_nextId` → nowy `Create` po load = crewCirculationId=1, **kolizja**. Fix: `GetNextId()` + `RestoreFromSave(circs, nextId)` + serialize `nextCrewCirculationId` + fallback `max+1` (2026-05-08)
- **BUG-082** (Medium) — `PartInventoryService.OrderParts`: `int totalCost = info.priceGroszy * quantity` overflow. Engine 15M groszy × 144+ szt = 2.16B+ groszy → wraps int.MaxValue. UI nie ma cap na quantity. Fix: long arithmetic + clamp z log warn + reject order (analogicznie do BUG-073). Modded gameplay reachable scenario (2026-05-08)
- **BUG-084** (Medium) — `PayrollService.EstimateMonthlyTotalGroszy/EstimateMonthlyBruttoTotalGroszy/EstimateMonthlyByRoleGroszy`: `int total += ...` accumulator. Endgame fanatyk 1500+ employees × wysokie salary (po raise'ach) → overflow w int przy ~2.15B groszy. UI forecast nieprawidłowy (gracz nie widzi prawdziwego kosztu). Fix: long accumulator + clamp z log warn (3 metody) (2026-05-08)
- **BUG-085** (Medium) — `CrewCirculation.assignedEmployeeId` orphan po Fire/Retire: PersonnelService.Fire czyściło Qualifications + odpalało OnEmployeeFired (FurnitureAssignmentService podsłuchiwał i robił release biurka), ALE `CrewCirculation.assignedEmployeeId` zostawał stale. UI showed "obieg #X przypisany: Jan Kowalski" dla zwolnionego pracownika (visual zombie); save/load preservował orphan reference. CrewAssignmentService defended via `IsActive` check (no crash, train run dostawał null crew), ale UX wrong. Fix: `CrewCirculationService.ClearAssignmentsForEmployee(int)` + `CrewCirculationServiceBootstrap` z `[RuntimeInitializeOnLoadMethod]` subscribe na PersonnelService.OnEmployeeFired + PersonnelEvents.OnEmployeeRetired (2026-05-08)
- **BUG-087** (Medium) — `starting_fleet.json` wstrzykiwał 9 pojazdów + 2 składy przy KAŻDEJ nowej grze (`FleetService.Initialize`→`LoadStartingFleet`) z fikcyjnymi „zadaniami" jako orphan-stringi (`currentTask` "Linia R1: Warszawa→Kraków", "Przegląd P3", "Manewry"; `consist.route` — żaden Timetable/Circulation/TrainRun tego nie backuje). Gracz dostawał flotę, której nie kupił. Fix: wyzerowane `vehicles:[]`/`consists:[]` (kod `LoadStartingFleet` graceful na pustych tablicach) → nowa gra = 0 pojazdów. Audyt new-game-seed potwierdził, że reszta systemów (personel/rozkłady/zajezdnia/ekonomia) startuje czysto.
- **BUG-088** (High) — Przycisk SPRZEDAJ w popupie posiadanego pojazdu (`FleetPanelUI.DetailPopup.cs`) był martwy: `onClick` robił tylko `Log.Info` + zamknięcie — pojazd nie znikał, kasa się nie zmieniała (decorative-dead w żywym systemie Fleet). Fix: realna sprzedaż `FleetService.SellVehicle` + `FleetResaleMath` (cena zakupu z historii × kondycja × `FleetBalanceConstants.ResaleValueHaircut` 0.85) + guard `CanSellVehicle` (blokuje pojazd w trasie/manewrach M9c, produkcji/dostawie/naprawie, składzie, serwisie, lub obiegu via `SellVetoHook` ← `FleetSellCirculationBootstrapper` w Timetable) + UI 2-step confirm. Test `FleetResaleMathTests` (2026-06-19)
- **BUG-089** (Medium) — Koszt tankowania pokazywany w UI (`FleetPanelUI.DetailPopup.SendActions.cs`) = `Mathf.RoundToInt(v.lengthM)` (~długość), a z kasy pobierane było `FleetFuelMath.RefuelCostGroszy` (brakujące litry × cena) → gracz widział inną kwotę niż płacił. Fix: UI czyta `FleetFuelMath.RefuelCostGroszy/100` (2026-06-19)
- **BUG-090** (Medium) — Toggle „fake timetable" w `CrewAutoGeneratorModal` (debug helper) dostępny w produkcyjnym UI: po włączeniu `OnGenerateClicked` committował 5 fikcyjnych kursów (ID 1001-1005) do save przez `DebugCreateSampleTimetable`, mimo że realny adapter `BuildInputsFromTrainRuns` był już domyślny. Fix: usunięty toggle, ścieżka produkcyjna zawsze przez adapter (metoda debug zostaje pod `[ContextMenu]`). Przy okazji: placeholder budżetu „150 000"→`BASE_STARTING_BUDGET_PLN` (100 mln), 3 nieaktualne komentarze `stub` poprawione (2026-06-19)

### Won't fix ⚫
_(brak)_

### Cannot reproduce 🔵
- **BUG-037** — VehicleAssignmentModal singleton flicker: audit Round 2 niesłusznie raportował brak guard'u w Awake. Guard JEST w kodzie (`if (Instance != null && Instance != this) { Destroy(gameObject); return; }`) — sprawdzone manualnie 2026-05-07. Audit error.
- **BUG-063** — Newtonsoft.Json edge cases dla Job classes — defensive opportunity bez konkretnego repro. Wszystkie 7 nowych POCO są `[Serializable]` z public fields (auto-default ctor), Newtonsoft.Json deserializuje OK. Audit speculative — risk tylko dla manual JSON editing / mod data, nie typowy save/load.
- **BUG-070** — PassengerManager._agentBuffer shared bufor: defensive only. Audit przyznaje że "wszystkie wywołania ortogonalne czasowo (FixedUpdate sequence)". Brak konkretnego scenariusza nested call. Refactor wprowadziłby per-method local lists → +allocacje w hot path (M-Performance trade-off). Akceptujemy obecny pattern.
- **BUG-071** — CirculationValidator nocny rollover 24h: edge case dla DayMask z gap'em w środku tygodnia (np. piątek-niedziela skip soboty). User'owy use case rzadki (większość obiegów ma codzienne kalendarze). Fix wymaga rolling lookup po N+1, N+2 dni — komplikacja vs. impact mały.
- **BUG-074** — OD Matrix cutoff 800km dla DLC EU: defensive dla DLC architecture która jeszcze nie istnieje. EA = PL only, max station importance ≤ 30. Cap 800km poprawny dla EA. Refactor wymaga `CutoffM` parametryzowanego — odłożone do M-DLC (post-EA).
- **BUG-075** — ReputationManager.Awake duplicate Instance hook installation: scenariusz wymaga **dwóch** ReputationManager instances równocześnie (drugi przyjeżdża z DontDestroyOnLoad cross-scene). W obecnym setupie `EnsureExists()` zapobiega — Cannot reproduce w typowym flow. Defensive opportunity tylko dla edge case scenarios.

---

## Active 🔴

### BUG-086 — GameCreator Difficulty dropdown NRE (captionText null)

**Severity:** High
**Component:** GameCreator/Difficulty
**Discovered:** 2026-05-14 (po sesji audytu MainMenu — user kliknął zakładkę „Rozgrywka")
**Files:**
- `Assets/Scripts/GameCreator/GameCreatorUI.Difficulty.cs:296-355` (`BuildDifficultyDropdownRow`)
- `Assets/Scripts/GameCreator/GameCreatorUI.Difficulty.cs:350` (NRE site)

**Symptom:**
```
NullReferenceException: Object reference not set to an instance of an object
  GameCreatorUI.BuildDifficultyDropdownRow () at GameCreatorUI.Difficulty.cs:350
  GameCreatorUI.PopulateDifficultySection () at GameCreatorUI.Difficulty.cs:246
  GameCreatorUI.PopulateRozgrywka () at GameCreatorUI.Sections.cs:92
  GameCreatorUI.PopulateSection (idx) at GameCreatorUI.Sections.cs:41
  GameCreatorUI+<>c__DisplayClass63_0.<BuildSidebar>b__0 () at GameCreatorUI.Layout.cs:139
```
Wywołanie `_ddDifficultyPreset.captionText.text = ...` rzuca, bo `MultiLineDropdown` (subclass `TMP_Dropdown`) ma `captionText == null`. W proceduralnym buildzie nikt nie tworzy TMP_Text child'a i nie przypisuje go do `dd.captionText`.

**Repro:**
1. Uruchom grę.
2. MainMenu → „Nowa gra" → scena GameCreator.
3. Klik zakładka „Rozgrywka" w lewym sidebarze.
4. NRE w Unity Console.

**Expected:**
Sekcja Rozgrywka renderuje dropdown z 5 presetami (Łatwy/Normalny/Trudny/Realistyczny/Custom). Caption pokazuje „Normalny" (preset domyślny).

**Próbowane / Hipotezy:**
- `git blame` linii 350 → commit `830cc759` (2026-04-27, "MB-1 Phase A: GameCreator UX rebuild"). Linijka dodana razem z `MultiLineDropdown` integracją bez uzupełnienia captionText.
- Wzór poprawny istnieje w [SettingsScreenUI.RowBuilders.cs:184](Assets/Scripts/MainMenu/SettingsScreenUI.RowBuilders.cs:184): `dd.captionText = capLbl` + `BuildDropdownTemplate(dd, ddGO.transform)`. Plus arrow indicator („▼"), template (ScrollRect → Viewport → Content → Item Toggle), które wszystkie są wymagane przez `TMP_Dropdown`.
- W GameCreator brakuje:
  - TMP_Text child + `dd.captionText = ...` (pierwszy NRE — ten z stack trace'a)
  - Template hierarchii (drugi NRE czeka — gdyby naprawić tylko captionText, klik na dropdown żeby się rozwinął rzuci `TMP_Dropdown.Show()` bo template null)
- Bug latentny od kwietnia — sygnał że ścieżka „GameCreator → Rozgrywka" nie była testowana po MB-1 commicie. Sekcja Serwer (MP) używa tych samych presetów (`Difficulty.Modifiers`) ale potencjalnie inną kolejność build'u — sprawdzić czy też crashuje.

**Fix scope (gdy zrobione):**
- W `BuildDifficultyDropdownRow` po `ddGO` utwórz TMP_Text child (caption + opcjonalnie arrow), przypisz `dd.captionText = caption`.
- Zbuduj template przez analog `BuildDropdownTemplate` z SettingsScreen (lub wyrzucić ten helper do `MenuScreenPrimitives` jako shared utility żeby oba miejsca z niego korzystały).
- Smoke test: kliknij na każdą zakładkę GameCreator + otwórz dropdown z presetami.

**Workaround:**
Brak — sekcja Rozgrywka jest niedostępna, gracz nie ustawi difficulty. Można pominąć i ruszyć z domyślnym Normal (apply na Start), ale sliders/modifiers w tej zakładce też nie zbudują się (bo NRE w `PopulateDifficultySection` przerywa cały populate).

---

### BUG-040 — EconomyManager._history unbounded growth

**Severity:** Medium
**Component:** Timetable/Economy
**Discovered:** 2026-05-07 (Round 3 audit)
**Files:**
- `Assets/Scripts/Timetable/Runtime/Economy/EconomyManager.cs:38,207`
- `Assets/Scripts/SaveLoad/Modules/EconomySavable.cs:19` (komentarz "max ~365")

**Symptom:**
`_history: List<DailyBalance>` archiwizuje per-day bilans. Komentarz w EconomySavable mówi "max ~365" ale w runtime kod **nie ma trim'a**. 5 lat gameplay = 1825+ entries (każdy z `Dictionary<int, LineBalance>` pełny) → save file rośnie liniowo, RAM też.

**Fix scope:**
Wzorzec z `PartInventoryService.cs:170`:
```csharp
const int HistoryMax = 365;
while (_history.Count > HistoryMax) _history.RemoveAt(0);
```
Plus rozważyć przechowywać ostatnie 30 dni full + monthly aggregate dla starszych (post-EA optimization).

**Workaround:**
Brak — efekt narasta z czasem gry.

---

### BUG-041 — PersonnelNotificationToastUI scaled deltaTime (toast freeze przy pauzie)

**Severity:** Medium
**Component:** Personnel UI
**Discovered:** 2026-05-07 (Round 3 audit)
**Files:**
- `Assets/Scripts/Personnel/UI/PersonnelNotificationToastUI.cs:81`

**Symptom:**
Auto-expire toast'a używa `Time.deltaTime * GameState.TimeScale`. Pauza (TimeScale=0) → delta=0 → toast countdown stoi → toast nie znika nigdy. Po wznowieniu gry ekran zapełniony nieaktualnymi toast'ami z poprzedniej sesji pauzy.

**Fix scope:**
Zmiana na `Time.unscaledDeltaTime` (UI animacje powinny działać niezależnie od pauzy).

**Workaround:**
Manual close każdego toast'u.

---

### BUG-042 — SchemaThumbnailGenerator RenderTexture/Texture2D leak

**Severity:** Medium
**Component:** Depot/Schemas/Placement
**Discovered:** 2026-05-07 (Round 3 audit)
**Files:**
- `Assets/Scripts/Depot/Schemas/Placement/SchemaThumbnailGenerator.cs:85-105`

**Symptom:**
`RenderTexture.GetTemporary()` + `new Texture2D()` w try-block. `ReleaseTemporary` i `DestroyImmediate(tex)` są wewnątrz try. Exception po cam.Render fail → RT i tex pozostają zaalokowane na zawsze. Per-thumbnail leak (kilka MB).

**Fix scope:**
```csharp
RenderTexture rt = null; Texture2D tex = null;
try { ... }
finally {
    if (rt != null) RenderTexture.ReleaseTemporary(rt);
    if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
}
```

**Workaround:**
Brak (cichy memory leak).

---

### BUG-043 — MapSetup 4× FindAnyObjectByType<MapLoader>

**Severity:** Medium
**Component:** Map/Rendering
**Discovered:** 2026-05-07 (Round 3 audit)
**Files:**
- `Assets/Scripts/Map/Rendering/MapSetup.cs:84,153,257,292`

**Symptom:**
4 wywołania `FindAnyObjectByType<MapLoader>()` w różnych metodach tej samej klasy. Każde skanuje całą scenę. MapScene ma kilka tysięcy GameObject'ów → drogie API.

**Fix scope:**
`private MapLoader _cachedMapLoader;` w Awake/OnEnable, plus `if (_cachedMapLoader == null) _cachedMapLoader = FindAnyObjectByType<MapLoader>();` w każdej z 4 metod.

**Workaround:**
Brak — UX OK, perf cost.

---

### BUG-044 — DepotSavable 15× FindAnyObjectByType per save/load

**Severity:** Medium (perf)
**Component:** SaveLoad/Depot
**Discovered:** 2026-05-07 (Round 3 audit)
**Files:**
- `Assets/Scripts/SaveLoad/Modules/DepotSavable.cs:60,73,118,173,203,227,243,305,409,436,463,482,491,503,514`

**Symptom:**
15× `Object.FindAnyObjectByType<...>()` przy save/load (TrackGraph, WallBuildingSystem, RoomDetectionSystem, FurniturePlacer, CatenaryGenerator). Każdy skanuje całą scenę.

**Fix scope:**
Pre-cache wszystkich 5 komponentów raz na początku Serialize/Deserialize w lokalnych zmiennych. Albo lepiej: `DepotSceneRefs` static helper class z lazy cache.

**Workaround:**
Save/load działa, tylko slow.

---

### BUG-045 — CatenaryGenerator FindAnyObjectByType per call

**Severity:** Medium
**Component:** Depot/Catenary
**Discovered:** 2026-05-07 (Round 3 audit)
**Files:**
- `Assets/Scripts/Depot/Catenary/CatenaryGenerator.cs:32-66`

**Symptom:**
3× `FindAnyObjectByType<TrackGraph>()` (Start + 2× GenerateNetwork overload). Awake-cache opportunity.

**Fix scope:**
Cache `_trackGraph` w Start, fallback FindAny w GenerateNetwork tylko gdy null.

**Workaround:**
Brak.

---

### BUG-046 — FleetMarketRefreshService._daysSinceLastRefresh nie reset

**Severity:** Medium
**Component:** Fleet runtime
**Discovered:** 2026-05-07 (Round 3 audit)
**Files:**
- `Assets/Scripts/Fleet/Runtime/FleetMarketRefreshService.cs:22,54`

**Symptom:**
`_daysSinceLastRefresh` int field nie resetowany przy "Nowa gra" (singleton DontDestroyOnLoad). Gracz zaczyna nową grę ze starym countdown → market refresh w "losowej" liczbie dni od początku zamiast pełny cykl.

**Fix scope:**
`public static void ResetAll() { _daysSinceLastRefresh = 0; }` + wywołanie w "Nowa gra" bootstrap (analog BUG-023).

**Workaround:**
Restart gry między sesjami.

---

### BUG-047 — DepotMovementSimulator float == comparison

**Severity:** Low
**Component:** Depot/Movement
**Discovered:** 2026-05-07 (Round 3 audit)
**Files:**
- `Assets/Scripts/Depot/Movement/DepotMovementSimulator.Movement.cs:176-180`

**Symptom:**
`currentDir != 0f && desiredDir != 0f && currentDir != desiredDir` + `desiredDir == 0f`. Floats porównywane bitowo. Pojazd z residual velocity ≈ 0.0001f od momentum/braking nie wykryty jako "zatrzymany" → state machine może utknąć.

**Fix scope:**
`Mathf.Abs(currentDir) > 0.001f` zamiast `!= 0f`. Plus `Mathf.Approximately` dla strict equality.

**Workaround:**
Edge case rzadko trafia.

---

### BUG-048 — WorkshopsPanelUI/PartsPanelUI scaled deltaTime (UI freeze przy pauzie)

**Severity:** Low
**Component:** Maintenance UI
**Discovered:** 2026-05-07 (Round 3 audit)
**Files:**
- `Assets/Scripts/Timetable/Runtime/Maintenance/WorkshopsPanelUI.cs:88`
- `Assets/Scripts/Timetable/Runtime/Maintenance/PartsPanelUI.cs:76`

**Symptom:**
UI refresh timer używa `Time.deltaTime` zamiast `unscaledDeltaTime`. Pauza (TimeScale=0) → panel UI freeze (gracz widzi stale data, np. po naprawie awarii lista warsztatów nie odświeża się dopóki nie unpause).

**Fix scope:**
Zmiana na `Time.unscaledDeltaTime`.

**Workaround:**
Unpause + repause cycle aby wymusić refresh.

---

### BUG-049 — DepotSavable rekursja w 5/6 helperów cache (stack overflow przy autosave)

**Severity:** Critical (autosave crash)
**Component:** SaveLoad/Depot
**Discovered:** 2026-05-10 — wystąpił przy autosave w trakcie sesji audytu game loopu.
**Status:** 🟢 Resolved (ten sam dzień)
**Files:**
- `Assets/Scripts/SaveLoad/Modules/DepotSavable.cs:75-90`

**Symptom:**
`StackOverflowException` przy każdym `DepotSavable.Serialize()` gdy `_cachedX` było null (czyli pierwszy save w sesji albo po `InvalidateSceneRefs`). `AutoSaveService` crashuje, save'y przerwane.

**Root cause:**
BUG-044 fix (commit `3f68489` z 2026-05-07) zamienił 15× `FindAnyObjectByType<T>()` na lazy cache helpers. 5 z 6 helperów zaimplementowane jako expression-body wywołujące **samych siebie** zamiast `FindAnyObjectByType<T>()`:

```csharp
private TrackGraph GetGraph()
    => _cachedGraph != null ? _cachedGraph : (_cachedGraph = GetGraph());  // ← rekursja!
```

Tylko `GetFurniturePlacer()` był poprawny (full body z `Instance ?? FindAnyObjectByType`). Bug był latent przez 3 dni — auto-save w trakcie sesji bez wcześniejszego cached state'u eksplodował dopiero przy kolejnym autosave timer'ze.

**Fix:**
Wszystkie 6 helperów na ten sam wzór jak `GetFurniturePlacer()`: full body + `FindAnyObjectByType<T>()`. Dla typów z singletonem (`FurniturePlacer.Instance`, `OutdoorEquipmentPlacer.Instance`) wzór `Instance ?? FallbackFind`.

**Workaround:**
Brak — save crashuje. Naprawione w tym samym commit'cie co fix.

---

## Naprawione w sesji 2026-05-07 (oryginalne entries — historia)

### BUG-001 — MainMenu wyświetla placeholdery `[main_menu.title]` zamiast tłumaczeń

**Severity:** High
**Component:** M13/Localization + MainMenu
**Discovered:** 2026-05-07 (game loop verification session, screen z .exe)
**Files:**
- `Assets/Scripts/SharedUI/Localization/LocalizationService.cs` (Bootstrap order, emitEvent flag)
- `Assets/Scripts/MainMenu/MainMenuUI.cs` (Awake → Get(...) zanim strings załadowane)

**Symptom:**
Po uruchomieniu builda `.exe` MainMenu pokazuje wszystkie napisy jako placeholdery w nawiasach: `[main_menu.title]`, `[main_menu.buttons.load]`, `[main_menu.buttons.new_single_player]`, `[main_menu.buttons.multiplayer]`, `[main_menu.buttons.settings]`, `[main_menu.buttons.mods]`, `[main_menu.buttons.help]`, `[main_menu.buttons.credits]`, `[main_menu.buttons.exit]`, `[main_menu.version_format]`. Klucze ISTNIEJĄ w `Assets/Resources/Locale/pl/strings.json:1439-1455` i `en/strings.json` analogicznie — to nie jest brak tłumaczeń.

**Repro:**
1. Build `.exe` (lub Play w Editorze, jeśli SettingsService nie ma zapisanego locale w PlayerPrefs)
2. Start gry → automatycznie ładuje się scena `MainMenu.unity`
3. Wszystkie 8 przycisków + tytuł + version label wyświetla `[klucz]` zamiast tekstu

**Expected:**
Po starcie widać przetłumaczone napisy: "Railway Manager", "Załaduj", "Nowa gra jednoosobowa", itd. (PL) lub odpowiedniki EN/DE/CZ wg locale autodetect.

**Próbowane / Hipotezy:**
- ✅ Pliki `Resources/Locale/{pl,en}/strings.json` istnieją i mają wszystkie klucze `main_menu.*` (potwierdzone grep + Read).
- ✅ `LocalizationService.LoadStrings` używa `Resources.Load<TextAsset>("Locale/{folder}/strings")` → ścieżka poprawna.
- 🎯 **Root cause: race condition Bootstrap vs Awake.**
  - `LocalizationService.Bootstrap` ma `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` (`LocalizationService.cs:60`).
  - Unity AfterSceneLoad odpala się **PO** `Awake()` obiektów ze sceny startowej.
  - `MainMenuUI.Awake()` buduje UI i woła `LocalizationService.Get(...)` zanim Bootstrap załaduje strings → `_strings` puste → fallback `[key]` (`LocalizationService.cs:120`).
  - Bootstrap potem ładuje strings przez `SetLanguage(resolved, emitEvent: false)` (`LocalizationService.cs:69`) z **wyłączonym eventem** — komentarz mówi "UI jeszcze nie subscribed", co jest błędne (UI już subscribed w Awake przed bootstrap'em).
  - `MainMenuUI` subskrybuje `OnLanguageChanged` (`MainMenuUI.cs:96`) ale event nigdy nie leci → UI nie odświeża się → placeholdery na zawsze.
- Dotyka także sub-screenów buildowanych w `MainMenuUI.Awake()` linie 88-93: LoadGameScreen, Credits, Help, Settings, Mods, Multiplayer.
- **Update 2026-05-07:** potwierdzone reproducible w MultiplayerScreenUI — `BuildBottomBar` (line 420) ustawia `createLbl.text = LocalizationService.Get("multiplayer.action.create_game")` w czasie Build (Awake-time) → user widzi `[multiplayer.action.create_game]` mimo że klucz JEST w `pl/strings.json:1516`. Dodatkowy mini-bug: `createLbl` to local var w `BuildBottomBar`, brak ref pola w klasie → `RefreshLanguage()` (line 96) **nie aktualizuje** create button label nawet gdy zmienimy język w runtime. Każdy taki sub-screen z local-var TMP w Build powinien przejść audit.

**Możliwe fixy (do decyzji w impl):**
- **A)** `LocalizationService.Bootstrap` → `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` — strings załadowane przed Awake. Ryzyko: `SettingsService.EnsureExists()` w bootstrap może nie zadziałać poprawnie BeforeSceneLoad (też AfterSceneLoad), trzeba sprawdzić.
- **B)** Bootstrap zmienić na `emitEvent: true` — emit OnLanguageChanged po załadowaniu, MainMenuUI dostanie event i odświeży labele przez `OnLocaleChanged` → `RefreshLabels()`. Najprostsza zmiana, działa bo subscribe leci w Awake przed AfterSceneLoad.
- **C)** Lazy init w `LocalizationService.Get()` — gdy `_strings.Count == 0`, wywołać `Bootstrap()` synchronously przed lookup. Defense in depth, działa nawet gdy bootstrap zawiódł.
- **D)** MainMenuUI explicit `EnsureBootstrapped()` w Awake przed pierwszym Get(). Wymaga publikacji metody.

Najprawdopodobniej **B+C** kombinacja (event emit + lazy guard).

**Workaround:**
Brak — gracz w buildzie widzi placeholdery i nie wie co kliknąć. **Critical-adjacent UX issue** mimo statusu High (nie blokuje przejścia dalej, bo struktura przycisków jest stała i może zgadywać).

---

### BUG-002 — HelpScreenUI + ModsScreenUI + LoadGameScreenUI scroll nie działa + brak Scrollbar

**Severity:** Medium
**Component:** MainMenu/HelpScreen + MainMenu/ModsScreen + MainMenu/LoadGameScreen (wszystkie ten sam pattern)
**Discovered:** 2026-05-07 (game loop verification session)
**Files:** `Assets/Scripts/MainMenu/HelpScreenUI.cs` (BuildScrollArea, linie 106-152), `Assets/Scripts/MainMenu/ModsScreenUI.cs` (BuildScrollArea, linie 135-180), `Assets/Scripts/MainMenu/LoadGameScreenUI.cs` (BuildScrollArea, linie 173-224)
**Update 2026-05-07:** rozszerzone o ModsScreenUI po code review przy Mods step — identyczny błąd hierarchii. Sprawdzić czy SettingsScreenUI / MultiplayerScreenUI też dotknięte.
**Update 2026-05-07 (2):** rozszerzone o LoadGameScreenUI po code review przy Load step — kolejny identyczny pattern. **3/6 sub-screens MainMenu dotknięte.** Coraz bardziej prawdopodobne że jest to systemic issue — może warto wyciągnąć helper `MenuScreenPrimitives.BuildVerticalScrollArea(parent, paddingTop, paddingBottom)` zamiast 3-4 razy kopiować błędny pattern.

**Symptom:**
Po kliknięciu "Pomoc" w MainMenu otwiera się Help screen z 5 topic'ami (navigation/build_modes/train_management/keybindings/save_load). Content jest za długi żeby zmieścić się w viewport, ale **scroll wheel nie działa** i **brak suwaka po prawej** — user nie może zobaczyć dolnych topic'ów.

**Repro:**
1. Start gra → MainMenu
2. Klik "Pomoc"
3. Próbować scroll'ować mouse wheel nad content area → brak reakcji
4. Sprawdzić prawą krawędź panelu → brak Scrollbar GameObject'u

**Expected:**
Mouse wheel scroll'uje content w pionie, scrollbar po prawej stronie pokazuje pozycję i pozwala na drag.

**Próbowane / Hipotezy:**
- 🎯 **Root cause #1: Hierarchy ScrollRect zbudowane niepoprawnie.** W `BuildScrollArea` (linie 106-152):
  - `Viewport` jest child of `root` (line 109): `viewport.transform.SetParent(root.transform, false)`
  - `ScrollRect` jest **osobnym GameObject'em**, sibling Viewport (line 119): `scrollObj.transform.SetParent(root.transform, false)` — ScrollRect komponent "zawieszony", bez własnych dzieci
  - `Content` jest child of **Viewport** (line 132): `content.transform.SetParent(viewport.transform, false)` — nie pod ScrollRect
  - Ustawienie referencji `scrollRect.viewport = viewportRT` (line 129) i `scrollRect.content = contentRT` (line 150) **nie wystarcza** — Unity ScrollRect wymaga że obiekty są w jego hierarchii bo handler `IScrollHandler` triggeruje na komponencie który ma raycast target pod kursorem, a tym jest Image na Viewport (line 115) który leży poza ScrollRect GameObject.
- 🎯 **Root cause #2: Brak Scrollbar.** Żadne `AddComponent<Scrollbar>()` w pliku, brak `scrollRect.verticalScrollbar = ...`.
- Standard fix: Viewport powinien być child ScrollRect, Content child Viewport, plus dodatkowy Scrollbar GameObject jako sibling Viewport pod ScrollRect, połączony przez `scrollRect.verticalScrollbar` + `scrollRect.verticalScrollbarVisibility = AutoHideAndExpandViewport`.
- Sprawdzić czy podobny pattern jest w innych screenach z scroll (LoadGameScreenUI, SettingsScreenUI, ModsScreenUI, MultiplayerScreenUI) — możliwy wider issue.

**Workaround:**
Brak — content niewidoczny pod fold. User może jedynie zmienić rozdzielczość ekranu, by content się zmieścił (50/50 czy zadziała przy 5 topicach).

---

### BUG-003 — ModsScreenUI + LoadGameScreenUI hardkodowane teksty zamiast LocalizationService

**Severity:** Medium
**Component:** MainMenu/ModsScreen + MainMenu/LoadGameScreen + i18n (M13)
**Discovered:** 2026-05-07 (game loop verification session)
**Files:** `Assets/Scripts/MainMenu/ModsScreenUI.cs` (PopulateMods, linie 224-298), `Assets/Scripts/MainMenu/LoadGameScreenUI.cs` (CreateSaveRow, linie 474, 485)
**Update 2026-05-07:** rozszerzone o LoadGameScreenUI po code review przy Load step:
- Line 474: `tagTmp.text = "[Auto]"` — komentarz "language-agnostic tag" świadomie nie tłumaczy, ALE inne języki (DE/CZ/JP/RU/UK) nie mają lokalnego ekwiwalentu autosave wskaźnika
- Line 485: `actionHint.text = "Otwórz zapis"` — z polskim ż OK ale brak EN/DE/CZ/JP/RU/UK
- Klucze do dodania: `load_game.tag.auto`, `load_game.action.open_save`

**Symptom:**
W panelu "Dodatki" texty intro i empty-state wyświetlają teksty bez polskich znaków (`dodatkow`, `bedzie`, `Mozesz juz`, `przejsc`, `zarzadzania`), wyglądają "łamane" / nieprofesjonalnie. Plus nie są tłumaczone na żaden inny język — ZH/DE/CZ/JP user widzi PL bez ogonków.

**Repro:**
1. MainMenu → "Dodatki"
2. Brak modów (zawsze, bo `PopulateMockMods` jest puste — line 109 komentarz "Empty on purpose")
3. Sprawdzić texty intro card (Eyebrow / Title / Body) i empty state (Eyebrow / Hint)

**Expected:**
Wszystkie 5 napisów (intro eyebrow + title + body, empty eyebrow + hint) ładowane z LocalizationService z poprawnymi polskimi znakami + multi-locale support (PL/EN/DE/CZ/JP/RU/UK).

**Próbowane / Hipotezy:**
- 🎯 **Hardkodowane stringi w kodzie** — bypassują system i18n M13:
  - Line 238: `introEyebrow.text = "WORKSHOP I MODY";`
  - Line 245: `introTitle.text = "Jedno miejsce dla dodatkow lokalnych i Steam Workshop.";`
  - Line 252: `introBody.text = "Gdy loader modow bedzie gotowy, tutaj zobaczysz liste aktywnych dodatkow, ich wersje i szybkie przejscie do zarzadzania.";`
  - Line 274: `emptyEyebrow.text = "AKTUALNIE PUSTO";`
  - Line 291: `hintObj.text = "Mozesz juz teraz otworzyc lokalny folder Mods albo przejsc do strony Workshop z dolnego paska.";`
- Pozostałe stringi (title, button labels, mod tags, noModsLabel) **prawidłowo** używają LocalizationService.
- `mods.empty` istnieje w `pl/strings.json:1500` ("Brak zainstalowanych modów..."), reszta intro+hint kluczy NIE ISTNIEJE w stringach — trzeba dodać.

**Fix scope:**
- Dodać klucze `mods.intro.eyebrow`, `mods.intro.title`, `mods.intro.body`, `mods.empty_state.eyebrow`, `mods.empty_state.hint` do wszystkich 7 lokali (PL/EN/DE/CZ/JP/RU/UK).
- Zamienić hardkodowane `text = "..."` na `text = LocalizationService.Get("mods.intro.title")` itd.
- Plus: noModsLabel.text setowany jest tylko w `RefreshLanguage` (line 77), nie w `PopulateMods` — zapewnić że `RefreshLanguage` leci po `Populate` (`Show()` line 64-65 to robi, więc OK).

**Workaround:**
Brak — gracz polski widzi łamane słowa, gracz nie-polski widzi PL bez ogonków.

---

### BUG-004 — ModsScreenUI "Otwórz folder modów" próbuje otworzyć nieistniejący folder

**Severity:** Medium
**Component:** MainMenu/ModsScreen
**Discovered:** 2026-05-07 (game loop verification session)
**Files:** `Assets/Scripts/MainMenu/ModsScreenUI.cs` (OnOpenModsFolder, linie 416-424)

**Symptom:**
Klik "Otwórz folder modów" w bottom barze panelu Dodatki → `explorer.exe` otwiera ścieżkę `<gra>/Mods/`, ale **folder nie istnieje** (sprawdzone: brak `Mods/` w root projektu). Explorer pokazuje błąd "Nie można znaleźć ścieżki" lub otwiera folder rodzica.

**Repro:**
1. MainMenu → "Dodatki"
2. Klik "Otwórz folder modów" w bottom barze
3. Obserwować: brak folderu `<gra>/Mods/`, explorer error/fallback

**Expected:**
- Folder `Mods/` istnieje przy każdym uruchomieniu gry (auto-create)
- Explorer otwiera **istniejący**, pusty folder z opcjonalnym `README.txt` ("Place mod folders here / Każdy mod w osobnym podfolderze")
- Działa cross-platform (obecnie tylko `#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN` — Mac/Linux user dostaje no-op bez feedback)

**Próbowane / Hipotezy:**
- 🎯 **Brak `Directory.CreateDirectory(path)` przed `Process.Start`.** Path `<dataPath>/../Mods` jest komputowany ale folder nigdy nie tworzony.
- Plus: brak fallback dla Mac/Linux (`xdg-open` na Linux, `open` na Mac) — projekt jest Windows-first wg STATUS, ale post-launch może być Steam Deck (PROTON Linux).
- Plus: brak placeholder README.txt z instrukcją dla gracza.

**Fix scope:**
1. `Directory.CreateDirectory(path)` przed `Process.Start` (idempotent — no-op gdy istnieje).
2. Opcjonalnie: utworzyć `README.txt` w folderze z krótką instrukcją (lokalizowana z `mods.folder_readme.content` klucza).
3. Cross-platform: `Application.OpenURL("file://" + path)` jako fallback poza Windows (Unity standardowy sposób).
4. Side-thought: `Directory.CreateDirectory` powinno też lecieć **przy starcie gry** (np. w `WorldSavable` bootstrap lub osobny `ModsBootstrap`), żeby gracz nie musi klikać żeby folder zaistniał — można copy-paste mody nawet bez wchodzenia w UI.

**Workaround:**
Player musi ręcznie utworzyć folder `Mods/` obok `.exe`, dopiero wtedy explorer otworzy go poprawnie.

---

### BUG-005 — Scene 'GameCreator' couldn't be loaded — disabled w build settings

**Severity:** Critical
**Component:** MainMenu/MultiplayerScreen + GameCreator
**Discovered:** 2026-05-07 (już wcześniej zauważone w step 1 jako BUG-CANDIDATE-1, teraz reproducible podczas testu MP screen)
**Files:**
- `ProjectSettings/EditorBuildSettings.asset` (linie 20-22, GameCreator.unity z `enabled: 0`)
- `Assets/Scripts/MainMenu/MultiplayerScreenUI.cs:603` (LoadScene call)
- `Assets/Scripts/MainMenu/MainMenuUI.cs:506-509` (też pomija GameCreator dla SP — patrz follow-up)

**Symptom:**
Klik "Utwórz grę" w MainMenu → "Gra wieloosobowa" → bottom bar → przycisk "Utwórz grę" rzuca w konsoli/logu:
```
Scene 'GameCreator' couldn't be loaded because it has not been added to the active build profile or shared scene list or the AssetBundle has not been loaded.
To add a scene to the active build profile or shared scene list use the menu File->Build Profiles
UnityEngine.SceneManagement.SceneManager:LoadScene (string)
MainMenu.MultiplayerScreenUI:OnCreateGame () (at Assets/Scripts/MainMenu/MultiplayerScreenUI.cs:603)
```
Scena nie ładuje się, gracz utknął w MP screen.

**Repro:**
1. Build `.exe`
2. MainMenu → "Gra wieloosobowa"
3. Klik "Utwórz grę" w bottom barze (po prawej)
4. Konsola: error scena nie załadowana, brak transition

**Expected:**
GameCreator scene ładuje się (z `GameCreatorContext.Mode = Multiplayer` setowanym w line 602), gracz konfiguruje swoją grę MP (host settings, modyfikatory), po Confirm → Depot scene + lobby.

**Próbowane / Hipotezy:**
- 🎯 **Root cause: scena `GameCreator.unity` jest w build settings ale `enabled: 0`** (`EditorBuildSettings.asset:20-22`):
  ```yaml
  - enabled: 0
    path: Assets/Scenes/GameCreator.unity
    guid: a3f7c82e1d054b6e8f91c3a5b2d60e47
  ```
- W Editorze działa (Editor pozwala ładować dowolne sceny w SceneManager), w buildzie tylko `enabled: 1` sceny są zbundle'owane.
- Plik sceny istnieje (`Assets/Scenes/GameCreator.unity`), tylko checkbox w build profile odhaczony.

**Fix:**
Otworzyć `File > Build Profiles` → checkbox enable dla `GameCreator.unity`. Lub edytować `EditorBuildSettings.asset:20` z `enabled: 0` → `enabled: 1`.

**Follow-up:**
- `MainMenuUI.cs:506-509` (`OnNewSinglePlayer`) ładuje **bezpośrednio Depot** zamiast GameCreator → sprzeczne z designem M13-13/D33 ("trudność wybierana per-save w GameCreator"). To **osobny bug**: SP gracz pomija kreator difficulty/modyfikatorów. Po enabled GameCreator scene fix powinno też zmienić `OnNewSinglePlayer` na `LoadScene("GameCreator")` z `GameCreatorContext.Mode = Singleplayer`. Decyzja: patrz BUG-CANDIDATE-2 z step 1 raportu.

**Workaround:**
Brak — gracz nie może utworzyć MP gry w buildzie.

---

### BUG-006 — TMP brak glyphu ↺ (U+21BA) w runtime font / fallback chain

**Severity:** Low (kosmetyczny — pokazywał □ zamiast ↺)
**Component:** SharedUI/TopBarUI (font runtime) + Settings (rebind reset button)
**Discovered:** 2026-05-07

**Files:**
- `Assets/Scripts/MainMenu/SettingsScreenUI.RowBuilders.cs:432` — text fallback "R" zamiast `↺`
- `Assets/Scripts/SharedUI/TopBarUI.cs` — `CreateRuntimeFontAsset` + `TryAddFallback` helper (Plan B: poprawa fallback chain dla innych glyphów)
- `Assets/Resources/Fonts/NotoSans-Regular SDF.asset` (przeniesione z `Assets/Fonts/SDF/`)
- `Assets/Resources/Fonts/NotoSansJP-Regular SDF.asset` (przeniesione z `Assets/Fonts/SDF/`)
- `Assets/Resources/Fonts/NotoSansSymbols2-Regular SDF.asset` (przeniesione z `Assets/Fonts/SDF/`)

**Root cause (właściwy, po deep diagnostyce):**
**Żaden** z 4 source TTF w projekcie (LiberationSans, NotoSans, NotoSansJP, NotoSansSymbols2) **nie zawiera** glyphu U+21BA — zweryfikowane przez direct dump cmap table (Python parser, format 12). Dotyczy też U+21BB, U+27F2, U+27F3, U+2939, U+2940, U+2941 (rotation arrows). Symbols 2 TTF pokrywa inny range (math, technical, ✓, ⚠), nie circle arrows. Dynamic atlas mode TMP nie pomoże — glyph nie istnieje w source font, więc nie ma czego dorzucić do atlasu.

**Pierwszy fix (Plan A — przez Unity Editor):**
User wykonał `Tools > TMP > Setup Font Fallbacks`. NIE pomogło — `LiberationSans SDF.asset` miał `m_FallbackFontAssetTable` z 5 pustymi slotami (`{fileID: 0}`) — Setup nie zapisał referencji.

**Drugi fix (Plan B — code-side strukturalny):**
1. Przeniesione 3 SDF fonty z `Assets/Fonts/SDF/` do `Assets/Resources/Fonts/` (z `.meta` files — GUIDs zachowane).
2. `CreateRuntimeFontAsset` explicit ładuje fonty przez `Resources.Load` i dodaje do `fontAsset.fallbackFontAssetTable` (NotoSans → NotoSansSymbols2 → NotoSansJP). Defense in depth: dziedziczymy też z LiberationSans SDF + deduplicate.
3. NIE pomogło dla U+21BA bo problem nie był w fallback chain a w **braku glyphu w source TTF**.
4. **Side benefit Plan B:** runtime font ma teraz funkcjonalny fallback chain. Glyphy które SĄ w fontach (✓ U+2713 w NotoSansJP/Symbols2, ⚠ U+26A0 w Symbols2, ← U+2190 w LiberationSans/JP, polskie diacritics, CJK, etc.) renderują się poprawnie.

**Trzeci fix (Plan C — procedural icon, FINAL):**
Stworzony `Assets/Scripts/SharedUI/IconGenerator.cs` z `GetResetSprite()` — generuje 64×64 Texture2D w runtime: rasteryzuje 300° arc (z gap 60° u góry) + arrowhead trójkąt na końcu CCW arc'u. Sub-pixel 2×2 AA dla krawędzi trójkąta. Sprite cache (statyczne pole, single-instance). W `SettingsScreenUI.RowBuilders.cs` reset button ma teraz child `Icon` GameObject z `Image` component i procedural sprite. Kolor przez Image.color tint = TextMuted (theme-aware).

Niezależne od fontów + zewnętrznych assetów + licencji. Znika warning U+21BA bo żaden TMP text nie próbuje renderować tego znaku.

Long-term M-UIPolish MUI-11: zastąpienie procedural sprite przez wektorowy SVG/PNG icon set (Unity AI Generators).

**Workaround (historyczny):**
Brak — funkcjonalnie reset działał, tylko glyph wyglądał jak pusty kwadrat.

---

### BUG-007 — MainMenu "Nowa gra jednoosobowa" pomija GameCreator (ładuje od razu Depot)

**Severity:** High
**Component:** MainMenu + GameCreator
**Discovered:** 2026-05-07 (już zauważone w step 1 jako BUG-CANDIDATE-2 raportu wstępnego, user explicit "trzeba podpiąć GameCreator po naciśnięciu nowej gry")
**Files:**
- `Assets/Scripts/MainMenu/MainMenuUI.cs:506-509` (OnNewSinglePlayer)
- `Assets/Scripts/GameCreator/GameCreatorUI.cs` (czeka na wywołanie)
- Powiązane: BUG-005 (GameCreator scene disabled w build)

**Symptom:**
Klik "Nowa gra jednoosobowa" w MainMenu → `SceneManager.LoadScene("Depot")` od razu, bez przejścia przez GameCreator. Gracz nigdy nie wybiera **trudności / modyfikatorów / nazwy save'a / mapy / game rules** — od razu trafia do pustego depotu z domyślną konfiguracją.

```csharp
private void OnNewSinglePlayer()
{
    SceneManager.LoadScene("Depot");
}
```

**Repro:**
1. MainMenu → "Nowa gra jednoosobowa"
2. Brak ekranu GameCreator → od razu Depot
3. `GameState.Money` = wartość default (`100M zł × 1.0`), brak custom difficulty / game rules

**Expected:**
Klik → `LoadScene("GameCreator")` z `GameCreatorContext.Mode = SinglePlayer` → gracz konfiguruje grę → klik "Rozpocznij" w GameCreator → `ApplyDifficultyAndRulesOnStart` + `LoadScene("Depot")` z poprawnym GameState.

Sprzeczne z designem M13-13: "Trudność NIE w Settings — wybierana per-save w GameCreator (D33)". MP flow już wywołuje GameCreator (`MultiplayerScreenUI.cs:600-604`).

**Próbowane / Hipotezy:**
- 🎯 **Root cause: oversight w wire'owaniu MainMenuUI.OnNewSinglePlayer** — kiedy GameCreator powstał (M13-13), zapomnieli zmienić MainMenu z direct LoadScene na GameCreator path.
- ⚠️ Plus zależność od BUG-005 — GameCreator scene musi być najpierw enabled w build settings, inaczej ten fix wywali "Scene 'GameCreator' couldn't be loaded" w buildzie.

**Fix:**
```csharp
private void OnNewSinglePlayer()
{
    GameCreatorContext.Mode = GameCreatorContext.GameMode.SinglePlayer;
    SceneManager.LoadScene("GameCreator");
}
```
Plus enable scene `GameCreator.unity` w `EditorBuildSettings.asset` (BUG-005 fix).

**Workaround:**
Brak — gracz nie może wybrać difficulty/modyfikatorów/nazwy save'a/game rules. Domyślny GameState jest Normal preset z 100M zł budżetem.

---

## Won't fix ⚫

_Brak._

---

## Cannot reproduce 🔵

_Brak._

---

## Template dla nowego buga

```markdown
### BUG-XXX — Krótki opis (1 linia)

**Severity:** Critical / High / Medium / Low
**Component:** M<N>/<System> (np. M5/Circulations, M-DepotTools/Schemas)
**Discovered:** YYYY-MM-DD (sesja X / playtest / itd.)
**Files:** Assets/Scripts/...

**Symptom:**
[Co user widzi / co się dzieje. 1-3 zdania.]

**Repro:**
1. Krok pierwszy
2. Krok drugi
3. ...

**Expected:**
[Co powinno się stać.]

**Próbowane / Hipotezy:**
- [Co już sprawdzono]
- [Hipoteza co może być przyczyną]

**Workaround:**
[Jeśli player może obejść, opisz. Jeśli nie — "brak".]
```

---

## Workflow dodawania nowego buga

1. **Bug znaleziony w trakcie sesji:**
   - **Naprawiony tej samej sesji?** → commit message wystarczy, NIE dodajemy do BUGS.md.
   - **Odłożony / nie reproduce'owalny / wymaga research?** → dodaj jako BUG-XXX (= następny wolny number).

2. **Bug zgłoszony przez playtester'a:**
   - Dodaj zawsze (= context będzie potrzebny później).

3. **Bug naprawiony post-discovery:**
   - Przenieś z `Active` do `Resolved` jako jednoliniowy entry: `BUG-XXX - krótki opis - fix commit hash (data)`.
   - NIE usuwaj numeru — chronologia ma być stała.

4. **Bug nie reproduce'owalny po próbach:**
   - Przenieś do `Cannot reproduce`. Może wróci później.

5. **Świadoma decyzja "nie fixujemy":**
   - Przenieś do `Won't fix` z uzasadnieniem.

6. Commit z prefiksem `docs(bugs):` (dodanie / update) lub po prostu `fix:` (gdy fix + przeniesienie do resolved w jednym commit).

---

## Status conventions

- 🔴 **Active** — bug znany, nieadresowany.
- 🟡 **Investigating** — w trakcie research / repro / hipotez.
- 🟢 **Resolved** — naprawiony, jednoliniowy entry w sekcji history.
- ⚫ **Won't fix** — świadoma decyzja że NIE fixujemy (= rare edge case, post-EA scope, wymaga refactora niewartego czasu).
- 🔵 **Cannot reproduce** — nie udało się odtworzyć po próbach. Może wróci.

## Severity conventions

- **Critical** — blokuje gameplay / corruption save'u / crash. Fix ASAP, blocker dla milestone'u.
- **High** — feature nie działa, ale jest workaround. Fix przed end milestone'u.
- **Medium** — kosmetyczny+ (UI off, niedopasowane teksty, niedoskonałości UX bez blokowania). Fix przy okazji.
- **Low** — nice-to-have fix, nie krytyczny dla doświadczenia.

## Powiązania z innymi dokumentami

- **`TECH_DEBT.md`** — tech debt = działa ale świadomie nie idealnie. Bug = nie działa zgodnie z designem. Granica niejasna: jeśli "fix" = polish UX, to tech debt. Jeśli "fix" = przywrócenie zgodnego z designem zachowania, to bug.
