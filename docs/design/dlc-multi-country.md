# DLC multi-country readiness

> **Cel dokumentu:** Plan przygotowania kodu pod DLC z innymi krajami (Niemcy, Czechy, Słowacja, Litwa, Białoruś, Ukraina, Obwód kaliningradzki). Lista wymienionych krajów + miast + OSM aliasów: [DlcCityCatalog.cs](../../Assets/Scripts/Timetable/Catalogs/DlcCityCatalog.cs).
>
> **Stan na 2026-05-04:** podstawowa Polska zamknięta w M-PL (2026-04-25). Audit DLC-readiness wykazał że **formap (generator)** jest w 80% gotowy (CLI `--country DE`, per-country `init-state-<XX>.bin`), ale Unity-side w 60% wymaga refactor'u.

---

## ✅ Co już jest gotowe (pre-2026-05-04)

### formap (generator binarnych plików OSM)
- **CLI z flagą `--country <ISO>`** — `formap germany.osm.pbf --country DE` produkuje `germany.bin` + `init-state-de.bin`. Patrz [D:/Gry/formap/Program.cs:11-14](file:///D:/Gry/formap/Program.cs).
- **`InitStateHeader.CountryCode`** — pole istnieje, persistowane w `init-state-<XX>.bin`. Patrz [D:/Gry/formap/InitStateBuilder.cs:39-45](file:///D:/Gry/formap/InitStateBuilder.cs).
- **`init-state-only` mode** — przebudowanie init-state bez re-konwersji 19GB `.bin`. Pomocne przy iteracjach.

### Unity — data structures
- **`AdminRegion`** ma `iso3166_1` (kod kraju) + `iso3166_2` (kod regionu) + `adminLevel` (2=country, 4=voivodeship). Multi-country ready w samej strukturze.
- **`VoivodeshipResolver`** ma `GetCountry(pos)` (zwraca name) — gotowy do rozszerzenia o `GetCountryCode(pos)`.
- **`DlcCityCatalog`** — pełen plan 7 regionów DLC + 19 miast z OSM aliasami w cyrylicy. Gotowy katalog do tooltipów / store UI.
- **`OutsidePolandPOIHider.ForeignStationStyle.Highlight`** — default mode renderuje zagraniczne stacje jako **czerwone klikalne DLC markery** (już teraz w UI gracze widzą "co będzie DLC").
- **Cantor-pair TileID** (`(gridX, gridY) → int64`) — globalne, bez kolizji między plikami `.bin`.

---

## 🟢 Co dodano w sesji 2026-05-04 (Unity-side foundation)

Nie zmieniają zachowania gry (default `ActiveDlcCountries = ["PL"]`), ale eliminują "PL hardcoded" mental model w kodzie + przygotowują do faktycznego DLC:

1. **`GameState.ActiveDlcCountries: List<string>`** — domyślnie `["PL"]`. Persistowane w save/load. Inne systemy mogą czytać żeby filter content.
2. **`VoivodeshipResolver.GetCountryCode(pos)`** — zwraca `iso3166_1` (np. "PL", "DE") zamiast tylko nazwy.
3. **`RailwayStation.countryCode` + `CityPlace.countryCode`** — wypełniane w Finalize() przez `resolver.GetCountryCode(pos)`. Stacje/miasta wiedzą do którego kraju należą.
4. **`AdminBoundaryLoader` deduplikacja per `(name, iso3166_2)`** — wcześniej drugi region "Bayern" z `germany.bin` byłby odrzucony bo `seen.Add("Bayern")` widziałby duplikat z PL. Teraz multi-country safe.
5. **`CountryOverlayService.IsInActiveCountries(pos)`** — alias do istniejącego `IsInsidePoland(pos)` na razie (bo aktywny tylko PL), ale call-sites mogą migrować do nowej nazwy. Refactor wewnętrzny w przyszłości bez breaking API.
6. **`DepotLocationPickerUI` filter per `ActiveDlcCountries`** — wybór home depot pokazuje tylko stacje w aktywnych krajach DLC.

---

## 🔴 Co jeszcze zostaje (M-DLC milestone, planowane)

### Unity-side bigger refactors (~10-15 dni)

| # | Zadanie | Estymacja | Komentarz |
|---|---------|-----------|-----------|
| 1 | **Multi-file MapLoader/TileManager** | 2-3 dni | `MapLoader.mapFileName` (single string) → `List<MapSource> { fileName, countryCode, tileIndex, bounds }`. Merge tile lookup. Risk: dużo callsite'ów. |
| 2 | **DLC state runtime swap** | 1-2 dni | Hot-load `germany.bin` po zakupie DLC bez restart gry. Trigger refresh AdminBoundaryLoader/StationLoader/PlaceLoader. |
| 3 | **CountryOverlayService faktyczna multi-country logic** | 1 dzień | `IsInActiveCountries(pos)` aktualnie alias do PL — zaimplementować PIP test contra wszystkie active country regions. |
| 4 | **OutsidePolandPOIHider → OutsideActiveCountriesPOIHider** | 0.5 dnia | Rename + użyj `IsInActiveCountries`. |
| 5 | **DlcLockRenderer per-region locks** | 1 dzień | Aktualnie pokazuje jeden czerwony lock per outside-PL tile. Trzeba per-region (Niemcy locked vs Czechy unlocked dają różne lockery). |
| 6 | **Pathfinding multi-source dedup** | 0.5-1 dzień | Granica PL/DE — ten sam tor w 2 plikach. UnionFind 10m tolerance pewnie sobie poradzi, ale weryfikacja + ewentualnie `(segmentId, countryCode)` dedup key. |
| 7 | **Save migration dla DLC state** | 0.5 dnia | Save z DLC Niemcy załadowany bez DLC → graceful fallback (alert "Brak DLC, niektóre dane ignorowane"). |
| 8 | **DLC store UI** | 2-3 dni | Panel "Sklep DLC" z listą paczek, cena, status (locked/unlocked/purchased), preview. Stub w EA, finalna implementacja przy faktycznym DLC release post-EA. |
| 9 | **Edge cases + QA** | 5-7 dni | Cross-border pociągi, save/load round-trip, performance multi-country. |

### formap-side (do dodania w `D:\Gry\formap`)

| # | Zadanie | Estymacja | Komentarz |
|---|---------|-----------|-----------|
| F1 | **`countryCode` w header `.bin`** | 0.5 dnia | Aktualnie `BinaryFormat.WriteHeaderV6/V7` ma 72 bytes reserved — wpakować tam ISO code (3 bytes UTF8 + null terminator). Unity loader odczyta — eliminuje konieczność zgadywania kraju z nazwy pliku. |
| F2 | **Border merge między krajami** | 1-2 dni | Gdy generujemy `germany.bin`, OSM features na granicy DE/PL są częściowo wspólne — replikowane w obu plikach. Można dorzucić CLI `--clip-to-country` żeby clipnąć geometrię na granicach kraju. Polish, nie blocker (Unity Pathfinding UnionFind sobie poradzi). |
| F3 | **Multi-input merge mode** | 2-3 dni | `formap merge poland.bin germany.bin czechia.bin -o central-europe.bin` — produkuje single `.bin` z multi-country content. Alternative do multi-file Unity loadera. Odłożone — single-file z `--country` per kraj jest prostsze. |

### Kluczowa decyzja architektoniczna

Mamy **2 ścieżki** dla MVP DLC:

**Ścieżka A — Multi-file Unity loader** (rekomendowana):
- Każdy DLC = osobny `.bin` + `init-state-<XX>.bin` w `StreamingAssets/Maps/<XX>/`
- Unity ładuje aktywne DLC files i merge'uje runtime
- Plus: hot-swap przy zakupie DLC bez restart gry
- Minus: większy refactor MapLoader/TileManager (#1 wyżej)

**Ścieżka B — Pre-merged `.bin` per build** (uproszczenie):
- formap generuje `central-europe.bin` z wszystkimi krajami (przez F3 merge mode)
- Unity ładuje single `.bin` jak dziś, nie wymaga refactor
- Plus: minimalna zmiana Unity-side (tylko `ActiveDlcCountries` filtering)
- Minus: gracz pobiera całą Centralną Europę (kilka GB) nawet jeśli nie kupił wszystkich DLC; brak hot-swap

Decyzja **TBD** — zależy od strategii steamowej (czy DLC = osobny package z `.bin`, czy DLC = unlock w core game). **Rekomendacja**: A dla pełnej kontroli + lepszy UX + standardowy DLC pattern, mimo większego refactor'u.

---

## Kolejność wdrażania M-DLC milestone

Sugerowana kolejność (po M-Performance, przed M-Models):

1. **M-DLC-1** — formap-side: F1 `countryCode` w header, F2 border clipping (1-2 dni)
2. **M-DLC-2** — Unity multi-file MapLoader/TileManager refactor (3-4 dni)
3. **M-DLC-3** — runtime DLC swap + CountryOverlayService faktyczna multi-country (2 dni)
4. **M-DLC-4** — DlcLockRenderer per-region + OutsidePolandPOIHider rename (1.5 dnia)
5. **M-DLC-5** — Save migration + DLC store UI stub (2 dni)
6. **M-DLC-6** — Cross-border QA + perf test (5-7 dni)

**Total**: ~15-20 dni roboczych dla pełnego MVP.

**Pre-EA scope**: pozostawić DLC infrastrukturę "ready" bez aktywnego content'u — wszystkie `ActiveDlcCountries = ["PL"]`, store UI puste z komunikatem "Wkrótce". Pierwsze DLC (Niemcy) post-EA jako pierwsza expansion paczka.

---

## Powiązane dokumenty

- [DlcCityCatalog.cs](../../Assets/Scripts/Timetable/Catalogs/DlcCityCatalog.cs) — katalog miast DLC + OSM aliasy
- [DlcLockRenderer.cs](../../Assets/Scripts/Timetable/Runtime/DlcLockRenderer.cs) — renderowanie kłódek na unowned regions
- [OutsidePolandPOIHider.cs](../../Assets/Scripts/Timetable/Runtime/OutsidePolandPOIHider.cs) — DLC marker mode dla zagranicznych stacji
- [docs/TECH_DEBT.md](../TECH_DEBT.md) TD-014 — Multi-file MapLoader bloker
