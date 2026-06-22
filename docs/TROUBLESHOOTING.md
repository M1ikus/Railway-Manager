# Troubleshooting & FAQ

> Praktyczne rozwiązania typowych problemów. Uzupełniaj gdy napotkasz coś
> nowego — następny deweloper/Claude nie musi tracić czasu na ten sam debug.

---

## Unity + IDE

### ❌ Unity "Compilation errors" po pull'u
**Objawy:** Czerwone błędy w konsoli, gra nie startuje.

**Rozwiązania (kolejność):**
1. **Assets → Reimport All** (wolne ale częste rozwiązanie)
2. Zamknij Unity, usuń `Library/` i `Temp/`, otwórz ponownie
3. Sprawdź `RailwayManager.*.csproj` czy nie masz ręcznych modyfikacji
4. `git status` — czy są nieoczekiwane zmiany w `Packages/manifest.json`

### ❌ Cyclic assembly reference
**Objawy:** `Assembly with name 'X' has already been imported` / `Cyclic assembly reference detected`

**Przyczyna:** Dodałeś referencję w `.asmdef` tworząc cykl (np. `Depot` → `Map` → `Depot`)

**Rozwiązanie:**
1. Sprawdź graf w `docs/conventions.md` sekcja "Assembly Definitions"
2. Zdejmij dodaną referencję, przenieś wspólny typ do `Core`
3. Lub użyj interface w Core (ISomething) i implementuj gdzie trzeba

### ❌ "Missing script" na GameObject
**Objawy:** Komponenty z pomarańczowym "Missing" w Inspektorze.

**Przyczyna:** Skrypt został usunięty/przemianowany, scena referuje stary GUID.

**Rozwiązania:**
1. Check `git log` — co zostało usunięte?
2. Przywróć usunięty skrypt, zmień nazwę/namespace, Unity zregeneruje referencje
3. Lub usuń komponent z GameObject i dodaj nowy

### ❌ Scena wygląda inaczej po pull'u (konflikty YAML)
**Objawy:** Obiekty zniknęły/pojawiły się, komponenty się rozsypały.

**Rozwiązanie:**
1. Upewnij się że masz skonfigurowany Unity Smart Merge (patrz `docs/setup/git-smartmerge.md`)
2. Jeśli merge już się rozpadł — zrób reset do ostatniego działającego commit'u, zrób zmiany na nowo

---

## Map (mapa 2D OSM)

### ❌ Mapa się nie ładuje / czarny ekran
**Objawy:** Po wejściu do MapScene nic nie widać, mapa jest czarna.

**Rozwiązania:**
1. **Sprawdź `StreamingAssets/Maps/Poland/`** — czy jest `poland.bin` lub `warminsko-mazurskie-v7.bin`?
2. Console → szukaj `[MapLoader] Failed to load...`
3. Sprawdź kamera — może jest poza mapą, spróbuj `F` (reset view)
4. `MapLoader.SetLODLevel(0)` — może się zablokowało na zbyt wysokim LOD

### ❌ "Missing tiles" albo czarne kwadraty
**Objawy:** Część mapy załadowana, reszta czarna.

**Przyczyna:** Tile'y jeszcze się ładują albo zbyt agresywne unload.

**Rozwiązania:**
1. Poczekaj chwilę (tile loading jest async)
2. Sprawdź `TileManager.cacheRadius` / `unloadDistance` — może zbyt mały
3. Check `Library/Logs/` pod kątem błędów tile reading

### ❌ "Can't find railway graph" przy tworzeniu rozkładu
**Objawy:** `TimetableInitializer` log `"Graph is null"` lub pusta lista stacji.

**Przyczyna:** Pipeline inicjalizacji nie ukończony.

**Rozwiązania:**
1. Upewnij się że scena MapScene jest załadowana i wszystkie tile LOD0 ready
2. `EnsureAllTilesLoadedSync(forceReloadAll: true)` — force load
3. Sprawdź `[TimetableInitializer] Ready in X.XXs` w konsoli — jeśli go nie ma, init się nie skończył

---

### Publiczny klon repo nie ma pelnej mapy Polski

**Objawy:** Projekt otwiera sie poprawnie, ale `poland-v7.bin` albo
`init-state-pl.bin` nie sa obecne.

**Wyjasnienie:** To moze byc zachowanie zamierzone. Publiczne repozytorium kodu
nie jest glownym kanalem dystrybucji shipping data.

**Co sprawdzic:**
1. Czy pracujesz na publicznym klonie repo, a nie na wewnetrznej paczce danych?
2. Czy masz lokalnie wymagane pliki w `Assets/StreamingAssets/Maps/Poland/`?
3. Szczegoly modelu dystrybucji danych: `docs/DATA_LICENSES.md`

---

## Depot (3D)

### ❌ Scena Depot czarna, brak terenu
**Objawy:** Po wejściu do Depot.unity widzisz tylko kamerę i niebo.

**Rozwiązania:**
1. `DepotManager` na scenie? Sprawdź GameObject `DepotSystem`
2. `generateDefaultLayout = true` w Inspektorze?
3. Right-click `GroundGenerator` → `Generate Ground` — ręczne wygenerowanie terenu
4. `ProceduralLighting` / Skybox ustawione?

### ❌ Tor się nie stawia, klik nie działa
**Objawy:** W trybie BuildTrack klikasz na teren, nic się nie dzieje.

**Rozwiązania:**
1. Sprawdź `DepotUIManager.CurrentTool` — czy jest faktycznie `BuildTrack`?
2. Sprawdź warstwę `Ground` — raycast musi trafić w collider na Y=0
3. `TrackBuildStateMachine` enabled?
4. Przełącz tool w toolbar: `Select` → `BuildTrack` (force state reset)

### ❌ `DepotRailwayIntegration` nie łączy się z mapą
**Objawy:** Zajezdnia nie widzi sieci kolejowej z mapy.

**Rozwiązania:**
1. `autoConnectToNetwork` musi być `true` w Inspektorze
2. `MapLoader` referencja przypisana?
3. Mapa musi być załadowana PRZED próbą połączenia (MapScene aktywna)

---

## Input System

### ❌ ESC nie zamyka popupa
**Objawy:** Naciskasz ESC, popup nie reaguje.

**Rozwiązanie:**
- Sprawdź czy używasz `Keyboard.current.escapeKey.wasPressedThisFrame` (direct access)
- NIE używaj action map Close — ma race condition z PauseMenu
- Patrz `docs/conventions.md` sekcja "Pułapki z migracji" pkt 2

### ❌ Ctrl+Z nie działa (undo)
**Objawy:** Naciskasz Ctrl+Z, nic się nie dzieje.

**Rozwiązania:**
1. Sprawdź czy jesteś w trybie który ma undo (BuildTrack/BuildCatenary/BuildPath/BuildRoom)
2. `UndoManager.Count(CurrentCategory)` > 0? (może nic nie ma do undo)
3. `UndoInputHandler` na scenie? Powinien być na `DepotSystem`

### ❌ Kamera Depot oddzielnie nie działa — zoom, pan, orbit
**Objawy:** Ruch kamery zerwany po zmianie narzędzia.

**Rozwiązanie:**
- `DepotOrbitCamera` używa `CameraDepot` action map — musi być zawsze enabled
- Sprawdź `_cameraActions.Enable()` w `OnEnable`
- State machines nie powinny disable'ować Camera action map

---

## Timetable / Rozkłady

### ❌ "Oblicz postoje" nic nie pokazuje
**Objawy:** Klikasz przycisk, stops się nie generują.

**Rozwiązania:**
1. Route musi mieć ≥2 stacje (start + koniec)
2. Pathfinding musi się udać — sprawdź konsolę `RouteBuilder` / `A*`
3. `TimetableInitializer.Instance != null` i graf załadowany?

### ❌ Kolizja kolizja kolizja — wszystkie rozkłady czerwone
**Objawy:** Każdy nowy rozkład ma collision z poprzednim, nawet o inne godziny.

**Przyczyna:** Bug w block reservations — prawdopodobnie klucz bloku zły.

**Rozwiązania:**
1. Sprawdź `ComputeBlockKey` w `ReservationManager` — czy używa segmentId?
2. Restart gry — może stare rezerwacje w pamięci
3. Debug: `BlockSectionReservations.GetReservations(key)` — ile jest reservations?

### ❌ Dropdown torów pusty na dużej stacji
**Objawy:** Przy postoju na Olsztyn Główny brak torów do wyboru.

**Rozwiązania:**
1. Sprawdź `station_tracks.json` — czy zawiera tę stację?
2. Usuń plik, pozwól na regenerację przy starcie (`StationTrackData.Generate`)
3. OSM data dla stacji — może nie ma `railway:track_ref` na edge'ach

---

## Fleet / Tabor

### ❌ Pojazd w koszyku nie pojawia się w "Moja flota" po zakupie
**Objawy:** Klikasz "Zamów", koszyk się czyści, ale nowego pojazdu nie ma.

**Rozwiązania:**
1. Sprawdź `CartProcessor.FinalizeOrder` — czy został wywołany
2. `FleetService.OwnedVehicles.Count` — może się zwiększyło ale UI nie zrefreshował
3. `FleetService.OnFleetChanged` event — czy jest subskrybent (FleetPanelUI)?

### ❌ EVN format nieprawidłowy
**Objawy:** Wygenerowany EVN ma błędną cyfrę kontrolną.

**Rozwiązanie:**
- `EvnGenerator.IsValid(evn)` do debug
- Algorytm Luhna — patrz `EvnGenerator.CalculateLuhnCheckDigit`
- Sprawdź format: `XX XX XXXX XXX X` (spacje!) vs raw digits

---

## Performance

### ❌ Niski FPS na mapie
**Rozwiązania:**
1. LOD level: Map przełącza się automatycznie na LOD 1+ przy oddalaniu
2. `CameraController.maxZoom` — nie pozwalaj na zbyt daleki widok
3. Profiler: CPU usage — gdzie jest bottleneck?
4. Tile count: ile tile'ów aktywnych? `TileManager.loadedTiles.Count`
5. Odłóż M12a — performance milestone

### ❌ Wysokie zużycie RAM (>6GB)
**Objawy:** Unity alert o niskim RAM, swap'owanie.

**Rozwiązania:**
1. LOD — upewnij się że wyższe LOD zwalniają lower LOD
2. Tile cache size — może za duży
3. Block sections — 103k sekcji na warmińsko-mazurskie zajmowało dużo, teraz 29-40 per rozkład
4. Check `StationTrackData` — nie za duży JSON?
5. Profile memory w Unity Profiler → Memory Usage

---

## Git / merge

### ❌ Konflikt w `.unity` scene file
**Rozwiązania:**
1. Upewnij się że Unity Smart Merge jest skonfigurowany (`docs/setup/git-smartmerge.md`)
2. Jeśli Smart Merge zwrócił błąd — ręcznie rozwiąż w edytorze tekstu (YAML)
3. Ostateczność: `git checkout --theirs` lub `--ours`, zrób zmiany na nowo

### ❌ "fatal: refusing to merge unrelated histories"
**Rozwiązanie:** `git pull --allow-unrelated-histories` — tylko raz przy pierwszym pulli po fresh clone

---

## Jak zgłaszać nowe problemy

Gdy napotkasz problem nie opisany tutaj:
1. **Zapisz rozwiązanie** tutaj po rozwiązaniu — następny raz nie musisz tracić czasu
2. Format: `❌ Objaw → Przyczyna → Rozwiązanie`
3. W komicie: `docs: troubleshooting — <co dodane>`
