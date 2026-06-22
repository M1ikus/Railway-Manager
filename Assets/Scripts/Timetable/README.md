# Timetable — przewodnik

> Dokumentacja modułu `Assets/Scripts/Timetable/`.

## Asmdef + warstwa

**`RailwayManager.Timetable`**, refs: `Core, Depot, Fleet, Map, SharedUI` (+ TMP + InputSystem).
**`rootNamespace` pusty** — pliki używają mixed namespaces (`RailwayManager.Timetable`,
`RailwayManager.Timetable.Economy`, `RailwayManager.Timetable.Simulation`, …).

**Drugi największy moduł projektu po split'cie 2026-05-15** — ~150 plików. Łączy
rozkłady (M4-M5), ruch (M9a), ekonomię (M6). **M7 Maintenance wyniesione do
osobnego asmdef `RailwayManager.Maintenance`** (2026-05-15) — Maintenance referuje
Timetable, NIE odwrotnie. Cykl zerwany przez event `TrainRunSimulator.OnSimulatorBootstrapped`
+ `MaintenanceBootstrapper` (Maintenance asmdef). Asymetria pozostała:
Personnel referuje Timetable dla hook'ów (`CrewCheckHook`, `CrewAssignmentService`).

## Sub-systemy (czytaj wg zadania)

| Sub-system | Pliki | Po co |
|---|---|---|
| **Rozkłady (M4-M4.5)** | `Data/Timetable.cs`, `Route`, `FrequencySpec`, `DayMask`, `IrjCategory`, `CommercialCategory`, `PlannedComposition`, `ReservationRegistry`, `Catalogs/IrjCategoryCatalog`, `TrainNumberValidator`, `CategoryClassifier`, `UI/TimetableCreatorUI.*`, `UI/CategoryEditorUI.*`, `RouteBuildStateMachine` | Kreator rozkładów: kategorie IRJ (EI/EN/RO/MP/…), trasy, częstotliwość, kalendarz dni, cykliczność tygodniowa. |
| **Pathfinding** | `Runtime/TimeExpandedPathfinder.cs` (+ semantic tests), `MinHeap.cs`, `Runtime/BackgroundPathPrecomputeService.cs`, `RouteBuildStateMachine.cs` | Time-aware A* + bounded horizon + block awareness + F1.5 directionPenalty. K-shortest paths + auto-background generation alternatyw tras (commit 872f835). |
| **Bloki semaforowe (M4 commit 6f34744 fix v3)** | `Data/BlockSection.cs`, `Data/SignalInfo.cs`, `Runtime/BlockSectionGraph.cs`, `Runtime/BlockSectionBuilder.cs`, `Runtime/SignalsStreamProcessor.cs`, `Runtime/SegmentSpeedResolver.cs`, `Catalogs/LineUsageSpeedCatalog.cs`, `Catalogs/ConstructionAreaCatalog.cs` | Kaskadowy fallback (exit→SBL→entry → exit→station → station→station). Track separation via `segmentId`. |
| **Obiegi (M5)** | `Data/TrainRun.cs`, `Runtime/CirculationValidator.cs`, `Runtime/ConsistValidator.cs`, `UI/CirculationListUI.*` (5 partials), `UI/VehicleAssignmentModal.*` (5 partials), `UI/CirculationDropTarget`/`CirculationDraggableTile`/`VehicleDraggableTile`/`CirculationDayDropTarget` | Per-day vehicle assignment z drag&drop, lock/unlock composition, auto-generator z settings modal. |
| **Symulacja ruchu (M9a)** | `Runtime/Simulation/TrainRunSimulator.cs` (+ partials: Blocks/Visuals/Breakdowns/**Restore** TD-037) + 9 ProfilerMarkerów (MP-2), `SimulatedTrain`, `TrainMarker`, `BlockDebugOverlay`, `MapClickHandler`, `DispatchService`, `DepotMapHandshakeService`, `DepotLocationPickerUI.*` (4 partials — namespace fix 2026-05-15: `MapSystem` → `RailwayManager.Timetable.Simulation`) | Ruch pociągów 2D w FixedUpdate (50Hz, deterministyczne, MP-friendly). M9c handshake z Depot przez `DepotMapHandshakeService`. `IsActive(int)` hook dla Personnel embed maszynisty (TD-025). `OnSimulatorBootstrapped` event dla Maintenance asmdef (split 2026-05-15). |
| **Save/load żywego świata (TD-037, 2026-06-10)** | `Runtime/Simulation/TrainRunSimulator.Restore.cs` (`ActiveRunSnapshot` + pending + `RestoreActiveRun` + orphan-guard), `Runtime/TrainRunWindowTopUp.cs`, `Runtime/Economy/PassengerPoolSnapshot.cs`; moduły w SaveLoad: `TrainRunsSavable` ("trainruns"), `PassengersSavable` ("passengers") | **Pending-restore pattern:** Deserialize jest synchroniczny, graf mapy buduje się ASYNC → moduły odkładają payload static (`SetPendingRestore`), FixedUpdate konsumuje pod gate'em gotowości. Kolejność: runy → pasażerowie (gate `TrainRunSimulator.PendingRestoreCount == 0` w PassengerManager). TrainRuns serializowane **Z ID** (CrewDuty linkuje po id — NIE regenerować!). Rolling window: `TrainRunGenerator.TopUpForCirculation` (bez Clear) na OnDayEnded + po restore. Rescue: `rescueOngoing` w MaintenanceSavable → pending w RescueService. |
| **Ekonomia (M6) + dotacje** | `Runtime/Economy/PassengerAgent` (struct-ready POCO, 14 pól POD), `PassengerManager` (671 linii, hot path), `TicketSystem`, `StationImportance`, `Catalogs/SubsidyRulesCatalog`/`SubsidyRule`, `Runtime/EconomyManager` (z UIIntents subskrypcja), `SubsidyCalculator`, `ReputationManager` (global + per woj.) | Agent-based passengers, OD Matrix, demand modifiers (rush hour/weekends/sezony), dotacje wojewódzkie. Spec: `docs/design/m6-economy.md`. |
| **Utrzymanie (M7)** | **PRZENIESIONE do `RailwayManager.Maintenance` asmdef** (split 2026-05-15) — patrz `Assets/Scripts/Maintenance/README.md`. Wszystkie pliki `WorkshopManager`, `RescueService`, `PartInventoryService`, `WorkshopsPanelUI`, `PartsPanelUI`, `MaintenanceAlertsUI`, `RescueDispatchUI`, bridges (PartInventoryFurniture/ServicePitFurniture/OutdoorEquipmentMovement) żyją teraz w Maintenance asmdef. |
| **Mapa state (renderery)** | `OutsidePolandStyledMarker`, `OutsidePolandPOIHider`, `VoivodeshipGroundRenderer`, `CountryOutsideMeshRenderer`, `SyntheticWaterRenderer`, `CountryBorderOverlayRenderer`, `DlcLockRenderer`/`DlcLockMarker`, `MapCameraBoundsLimiter`, `WaterFeatureDiagnostics`, `AdminBoundaryLoader`, `StationLoader`, `PlaceLoader`, `CountryOverlayService`, `VoivodeshipResolver`, `AdminRegion`, `AgglomerationDetector`, `CityPlace`, `Catalogs/DlcCityCatalog` | Markery i overlaye renderowane na mapie OSM. **Tu, nie w Map** — wymaga ekonomii/category/DLC state. M-PL features + M-DLC foundation. |
| **M-TimetableUX 2026-05-10** | `Runtime/Suggestions/CirculationSuggestionService`, `MijankaSuggestionService`, `Runtime/Meetings/MeetingEventsService`, `Runtime/Notifications/TimetableNotificationService`, `Runtime/Workflows/TimetableWorkflowOrchestrator`, `Runtime/GhostStationMarkersOverlay`, `Runtime/WaypointMarkersOverlay`, `Runtime/OffPathStationsDetector`, `UI/WorkflowStepIndicatorWidget` | 6 suggestion services + workflow orchestrator + auto-trigger po save + StopType taxonomy (PH/PT/ZD/Transit). |
| **Perf (M-Performance)** | `Runtime/Performance/PerfStressBootstrap.cs` | MP-1 framework: `MonoBehaviour` + `PassengerManager.DebugForceSpawn` API + cap override + CSV output. |
| **Mini-mapa OSM (RMP + LOD, 2026-06-13)** | `UI/RouteMapPreview.cs` (widget), `UI/RouteMapPreviewTiles.cs` (własne kafle OSM), `UI/RouteMapPreviewInput.cs` (pan/zoom + klik-pick), `UI/RouteMapPreviewMath.cs` (czysta matematyka, EditMode), `UI/TimetableCreatorUI.Preview.cs` (dok + live-refresh + picker stacji) | **Reuzywalny, kompozytowalny podgląd trasy na mini-mapie OSM.** Druga kamera ortho renderuje **WYŁĄCZNIE warstwę MapPreview (29)** (`SceneController.PreviewCameraCullingMask`) → RenderTexture → RawImage. **Własne kafle OSM we WŁASNYM LOD** (`RouteMapPreviewTiles` buduje meshe przez `MapLoader.ParseTileRenderLayers(tileID, lod)` + `MapMeshBuilder.BuildMesh`; LOD z `MapLod.LodForOrtho(orthoSize)` — grube przy oddaleniu, niezależne od globalnego LOD głównej mapy), batchowane przez klatki. API: `SetPolylines`/`SetMarkers`/`ClearOverlays` + `FitToContent` + `Pan`/`Zoom` + `OnMapClicked`/`OnViewChanged`. W kreatorze: bieżąca trasa live (`GenerateRoute`/`ComputeStops`) + **wybór stacji klikiem** (przyciski „Na mapie" uzbrajają mini-pick, kropki stacji w widoku → klik = najbliższa stacja; zastępuje pełnoekranowy `RouteBuildStateMachine`). Providery „wszystkie rozkłady kolorami"/„pozycje pojazdów" = później. Tabor (Depot asmdef) wymaga przyszłego bridge'a. |
| **Tuning** | `Catalogs/TimetableTuningConstants.cs` | Magic numbers strictly forbidden — wszystko tu. |

## Cross-system glue

- **`TrainRunSimulator.OnRunSpawned/OnRunDespawned`** events → Personnel `DriverConductorWorkflow` (embed kapsuły maszynisty w pojezdzie).
- **`TrainRunSimulator.CrewCheckHook`** ← Personnel `CrewAssignmentService` (M5 obiegi × M8 załoga).
- **`WorkshopManager.SlotSpeedMultiplierHook`/`BreakdownService.SelfRepairBonusHook`** ← Personnel `WorkshopAssignmentService`.
- **`DepotMovementSimulator.PriorityProvider`** ← Personnel `TrafficControlService` (TD-022 ↔ M10).
- **`UIIntents.OnIntent`** ← `EconomyManager`, `WorkshopsPanelUI`, `PartsPanelUI` subskrypcje.
- **`GameState.OnDayEnded`** ← `EconomyManager` (archiwizacja bilansu), `SubsidyCalculator`, `ReputationManager`.

## Gotchas

- **Pusty `rootNamespace` w asmdef** — namespace mixed (`RailwayManager.Timetable`, `.Economy`, `.Simulation`, `.UI`, `.Maintenance`). Sprawdź konkretny plik.
- **Hot paths (kod-reading 2026-05-06)**: `PassengerManager` (`List<PassengerAgent>` + `RemoveAt` shift O(n), `CountOffersOnPair` w `MaybeSpawnAgents` = 5M comparisons/tick przy stress, `CalculateCapacity` per board iteruje całe `OwnedVehicles`), `BreakdownService.Roll` (~55k random rolls/s — MP-9 must seed via `RandomRegistry`), `TrainRunSimulator.Advance` single thread foreach `Dictionary<int, SimulatedTrain>`.
- **`TrainRunSimulator` jest partial × 4** (root + Blocks + Visuals + Breakdowns) — modyfikacje muszą leżeć w odpowiednim partial.
- **`TimetableInitializer` jest partial × 4** (god class split 2026-05-15, 1506 → ~140 LOC root): `.cs` (pola + lifecycle + UIIntent router), `.Bootstrap.cs` (scene hook + EnsureBootstrapped factory), `.Init.cs` (Initialize coroutine + 8-step streaming build + M-PL B2 fast-path), `.Diagnostics.cs` (13 ContextMenu DEBUG/Validate metod).
- **MP-9 deterministic seed**: 7 plików wymaga `RandomRegistry.GetRng("…")` zamiast `UnityEngine.Random` (BreakdownService, PassengerManager, TrainRunSimulator.Breakdowns hot + FleetService/DispatcherService/SickLeave/Retirement cold). GroundGenerator naprawiony 2026-05-15 ale przez **hardcoded fixed seed per block name** (nie RandomRegistry — user wymaga tła IDENTYCZNEGO niezależnie od `GameState.Seed`).
- **Schema bump v1→v2 (M-TimetableUX 2026-05-10)**: `TimetableSavable` + `CirculationsSavable` cross-module. Migrator chain ZAWSZE — nie zmieniaj formatu bez migrationStep.
- **Symulacja w FixedUpdate 50Hz** — wszystko w `TrainRunSimulator/DepotMovementSimulator` musi być deterministyczne. Nie używaj `Time.deltaTime` w hot path, używaj `Time.fixedDeltaTime` lub `GameState.GameTimeSeconds` snapshot.
- **OD matrix building** używa spatial grid + SqrMagnitude pre-reject + cutoff 800km (TD-016 ✅ done 2026-05-06, commit `c24d865`). Wzorzec analogiczny do M-PL fix'u `FindTracksForStop`/`FindNodeOnTrack`. Patrz `OriginDestinationMatrix.Build`.
- **Maintenance bridges (`PartInventoryFurnitureBridge`/`ServicePitFurnitureBridge`/`OutdoorEquipmentMovementBridge`) żyją w `RailwayManager.Maintenance` asmdef** (split 2026-05-15) — bo Depot nie może referować Timetable/Maintenance (hierarchia asmdef). Bridges subskrybują eventy Depot.
- **TopBarUI scena**: nawigacja "Mapa 2D" / "Zajezdnia" idzie przez `SceneController.SwitchToMap/SwitchToDepot` — cooldown 0.3s (ping-pong prevention).

## Powiązane docs

- `docs/design/m6-economy.md` — ekonomia
- `docs/design/balance-constants.md` — TimetableTuningConstants/balance lookup
