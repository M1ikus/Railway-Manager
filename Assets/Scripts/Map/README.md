# Map — przewodnik

> Dokumentacja modułu `Assets/Scripts/Map/`.

## Asmdef + warstwa

**`RailwayManager.Map`**, refs: `Core, SharedUI, Depot` (+ TMP + InputSystem).
**`rootNamespace` pusty** — pliki używają `namespace MapSystem` (legacy).

Mapa 2D Polski z OSM binary format v7. **Map widzi Depot, nie odwrotnie**
(cykl zerwany). Konsumenci: Timetable (rozkłady na mapie).

**Scena:** `MapScene.unity` — additive loaded przez `SceneController` w Depot.
UI/camera/EventSystem disabled gdy `ActiveScene = Depot`, logika tikuje cały czas.

## Co tu siedzi

| Folder | Pliki | Po co |
|---|---|---|
| **`Core/`** | `BinaryFormat.cs`, `MeshGeometry.cs`, `TileGrid.cs` | Parser OSM binary v7 (12 warstw, 5 LOD per tile), generic mesh containers, tile coordinates. |
| **`Loading/`** | `MapLoader.cs` (+ 4 partials: LoadFormats/Tiles/Streaming/Helpers), `TileManager.cs`, `TileData.cs` | Ładowanie i decompresja (LZ4). Wspiera v5 (legacy whole-map), v6 (tiled), v7 (multi-LOD tiled). `TileManager` zarządza streaming on-demand wg kamery. |
| **`Rendering/`** | `MapRenderer.cs` (+ 3 partials: Layers/Markers/Tiles), `CameraController.cs`, `MapSetup.cs` | Renderowanie warstw (rzeki/lasy/miasta/koleje), camera pan/zoom z bounds outline, markery POI/dworców/sygnałów. |
| **`Railway/`** | `RailwayGraph.cs` | Graf sieci kolejowej OSM — stacje, tory, połączenia. Konsumowany przez `TimeExpandedPathfinder` (Timetable). |
| **`POI/`** | `StationMarker.cs`, `SignalMarker.cs` | Klikalne markery stacji + sygnałów na mapie. |
| **`UI/`** | `MapUIManager.cs`, `MapZoomSliderUI.cs`, `MapTrainListUI.cs` | UI mapy — slider zoomu, lista aktywnych pociągów (M9a). |
| **`Debug/`** | `MapPerfOverlay.cs` | Profilowanie streamingu tile'ów. |
| **Roots** | `IdleVehicleVisualizer.cs` | Wizualizacja idle pojazdów na peronach. M9c handshake idzie przez `DepotMapHandshakeService` (Timetable asmdef). |

## Cross-system glue

- **M9c Depot↔Map handshake** — `DepotMapHandshakeService` w Timetable asmdef koordynuje exit/entry między Depot 3D a światem 2D mapy. Stara klasa `DepotRailwayIntegration` (pre-M9c relict) usunięta 2026-05-14 jako dead code.
- **`MapLoader.OnMapLoaded`** event → Timetable `StationLoader`/`PlaceLoader`/`AdminBoundaryLoader` (te są w Timetable bo wymagają wyższych zależności).
- **`RailwayGraph` jest read-only po load** — Timetable `TimeExpandedPathfinder` konsumuje, nie modyfikuje.
- **Camera input gating**: `CameraController.Update` sprawdza `cam.enabled` + `SceneController.FullscreenOverlayOpen` żeby nie konsumować inputu gdy Depot scene aktywna.

## Gotchas

- **Namespace `MapSystem`** (nie `RailwayManager.Map`). `using MapSystem;` w callerach.
- **`MapScene` jest additive loaded i ŻYJE cały czas** — `TileManager.Update` tikuje gdy `Depot` jest aktywne. Wyłączona jest tylko kamera + UI + EventSystem.
- **MapLayer = 31** (constant w `SceneController.MapLayer`). Kamera Depot wyklucza, kamera Map renderuje layer 31 + UI (5) + **MapOverlay (30)**. Nowy GameObject w MapScene MUSI dostać layer 31 (`SetLayerRecursive` w SceneController) — w skrypcie pamiętaj o `gameObject.layer = 31` przy spawn.
- **Warstwy podglądu (RMP, 2026-06-13)**: `SceneController.MapOverlayLayer = 30` (nakładki DUŻEJ mapy: `RoutePreviewOverlay` itp. — przeniesione z 31, żeby mini-podgląd ich nie pokazywał) + `MapPreviewLayer = 29` (zawartość TYLKO mini-podglądu). Maski w `SceneController.Cameras.cs`: `MapCameraCullingMask` (31+5+30), `PreviewCameraCullingMask` (**TYLKO 29** — podgląd ma WŁASNE kafle, nie renderuje głównych 31), `ApplyDepotCullingExclusions` (czyści 31/30/29) — **czyste helpery, testowane w `SceneCullingMaskTests`**. Markery `WaypointMarkersOverlay`/`GhostStationMarkersOverlay` zostają na Default (0) — klik to `Physics.Raycast(~0)`, więc warstwa bez znaczenia.
- **Mini-mapa OSM = WŁASNE kafle we własnym LOD (RMP Opcja A, 2026-06-13)**: podgląd NIE pinuje współdzielonych kafli — renderuje WŁASNE na warstwie 29. `RouteMapPreviewTiles` (Timetable) buduje meshe przez `MapLoader.ParseTileRenderLayers(tileID, lod)` (parse warstw tła Water/Forests/Waterways/Highways/Railways w JAWNYM LOD, zero side-effectów, globalny `currentLOD` nietknięty) + `MapMeshBuilder.BuildMesh` (czysty mesh, bez GO/layer) + public gettery `MapRenderer.GetLayerHeight`/`IsLineLayer`/`GetMaterialForLayer`. LOD z `MapLod.LodForOrtho(orthoSize)` (`Map/Core/MapLod.cs`, wydzielone z `UpdateLayerVisibility`, EditMode `MapLodTests`) — grube kafle przy oddaleniu, niezależnie od głównej kamery. Build batchowany przez klatki. **Mesh trzeba `Destroy` przy odładowaniu kafla (leak)** — robi to `RouteMapPreviewTiles.DestroyTileGo`.
- **Pin API zostaje jako ogólne infra, ale podgląd go NIE używa**: `TileManager.RequestPinnedTilesForRegion`/`UnpinTilesForRegion` + `TilePinPolicy` (eviction guard) działają i są testowane (`TilePinPolicyTests`), lecz po Opcji A nikt ich nie woła (zastąpione własnymi kaflami). Zostawione pod ewentualne przyszłe użycie (preload regionu, inne minimapy).
- **Streaming kafli — budżet czasu/klatkę**: `MapLoader.LoadTilesCoroutine` ładuje kilka kafli na klatkę aż do `tileLoadFrameBudgetMs` (5 ms) / `tileLoadMaxPerFrame` (8), zamiast 1/klatkę — szybsze pokrycie na mocnym CPU bez hitchy. Dotyczy normalnego streamingu (poza ekstrakcją, która ma exclusive file access przez `ExtractionPaused`).
- **Lighting save/restore** — Map ma własne ambient/skybox settings które nadpisują Depot. `SceneController.SaveDepotLighting/RestoreDepotLighting/KeepRestoringLighting` (60 frames) — to dziwactwo Unity z asynchroniczną aplikacją scene lighting po additive load.
- **OSM binary format v7 (`FORMAP03`) + v8 (`FORMAP04`)** — szczegóły w `docs/design/data-formats.md`. Format generator zewnętrzny: projekt `D:\Gry\formap`. **NIE rób parsing ręcznie**, używaj `BinaryFormat`/`MapLoader`. Dispatch po magicu w `LoadMap` (v8→`LoadMapV8`, v7→`LoadMapV7`). v8 reader: `FeatureCodecV8`+`BlockDecoderV8` (Core/), podpis Ed25519 `MapSigning`/`MapSignatureVerifier` (BouncyCastle DLL w Plugins). v8 różni się: brak block length-prefiksu, global string table, index z per-LOD SHA-256, bbox liczony z wierzchołków, SoA shuffled vertices. **Pliki na dysku nadal v7** — podmiana na `poland-v8.bin` gdy dojdzie (patrz checklist niżej / `data-formats.md`). Testy: `Assets/Tests/EditMode/BinaryFormatV8Tests.cs`.
- **Init state for full Poland**: ~600s naive → <15s przez `init-state-pl.bin` (M-PL-1..3).
- **Bottleneck'i performance** (M-PL-7 znalezione): `FindTracksForStop` fallback, `FindNodeOnTrack`, generator JSON — wszystkie miały brute-force O(N) → spatial grid lookup → 38× speedup.
- **Map nie renderuje markerów Timetable** (`OutsidePolandStyledMarker`, `DlcLockMarker`, `GhostStationMarkersOverlay` etc.) — to wszystko w Timetable bo wymaga ekonomii/categorii/state'u rozkładów.
- **Station icons = placeholder** — `MapRenderer.stationIconSprite` jest [SerializeField] ale w `MapScene.unity` nieprzypisany (`fileID: 0`). Fallback: shared cached cube mesh + Unlit material (pkt 11 commit e9c6100). **Docelowo: custom ikony stacji** (per typ station/halt? per kategoria główna/aglomeracyjna/peryferyjna? styl PKP-themed?) — do zaprojektowania w M12 Polish. Dopóki nie zdecydowane: cube placeholder zostaje.

## Powiązane docs

- `docs/design/data-formats.md` — OSM binary v7/v8 format
