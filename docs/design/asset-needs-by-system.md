# Asset Needs by System

> **Cel:** Single source of truth dla "co konkretnie potrzebujemy dostarczyć i w jakim stylu".
> Zsyntezowane z realnych systemów w kodzie + decyzji wizualnych.
> **Ostatnia aktualizacja:** 2026-05-17.
>
> **NIE wymyśla kategorii** — każda sekcja odpowiada subsystemowi w `Assets/Scripts/Depot/` per README.md.
> Powiązane: `asset-pipeline.md` (technical specs), `depot-visual-direction.md` (Depot CBM-style), `visual-style-guide.md` (general).

---

## Filozofia stylu — w 5 punktach

1. **Stylized PBR** — albedo + light normal, NIE flat low-poly z vertex colors, NIE hyper-real. Pomiędzy. **NIE fototekstury (photoscan / ambientCG-style)** — wszystkie tekstury (modele + powierzchnie tile-able) robione **proceduralnie w Blender Shader Editor → bake do PNG** lub stylized hand-paint. Decyzja 2026-05-31 (user): spójny stylized język w całej grze, zero photoscan. Patrz "Historia decyzji".
2. **Skala 1:1 realistic** — wagon 24.5m, hala 30×120m, słupy trakcyjne co 55m. Nie kompresuj.
3. **Kamera auto-pitch 40-70°** coupled do zoom — gracz widzi z perspektywą na ściany przy zoom in, top-down przy zoom out.
4. **Open-top hale** — `WallBuildingSystem` NIE generuje dachów. Gracz widzi wnętrza bez slice/transparent.
5. **Muted naturalistic kolory** — akcenty kolorystyczne tylko w UI (pomarańcz/żółty/zielony ikony), świat spokojny.

Reference target: **City Bus Manager (Doublequote Studio)**. Stylized PBR z umiarkowanym szumem tekstur (krakelura beton, wzór cegły, asfalt wear).

---

## Lista assetów per system

### 1. Tracks — `PrefabTrackBuilder` + `TrackGraph`

**Sub-system w kodzie:** `Assets/Scripts/Depot/PrefabTrackBuilder.*.cs` (+ `TrackGraph` partials, `TrackGeometry.*`)

**Jak działa:** procedural placement rail+sleeper prefabów wzdłuż polyline (Bezier/Arc/Dubins). Ballast generated jako mesh strip. Track gauge 1.435m, sleeper spacing 0.6m.

**Co potrzeba (slot w kodzie → asset):**
- `railPrefab` → **`Rail_1m.fbx`** — 1 segment szyny 1m (tile-able)
- `sleeperPrefab` → **`Sleeper.fbx`** — 1 podkład
- **Materiał ballast** (gravel gradient pomarańczowo-szary)

**Opis stylu:**
- **Rail:** profil S49 PKP (uproszczone — bez detali mocowań), stalowy ciemnoszary z subtelnym połyskiem wzdłuż wierzchu szyny (visible przy zoom in). Roughness ~0.25.
- **Sleeper:** drewno preferred dla zajezdni (PKP wzorzec bocznic), brązowy z subtelną teksturą wzoru drewna. Wymiary 2.6×0.26×0.16m. Beton dla linii głównych (Map) — później.
- **Ballast:** gravel pomarańczowo-szary, dwa odcienie, lekki noise. Wysokość 0.4m. Tile texture 512×512 lub procedural noise w shader.

**Polycount:** rail 30-50 tris, sleeper 100-200 tris. Ballast procedural ~50 tris/m.

**Status:** ✅ **DONE 2026-06-20 (bpy-first)** — `Sleeper.fbx` (drewno IIB, ~228 tris, węzeł K), `Rail.fbx` (S49, ~62 tris, rdza + pasek toczny), podsypka = **nasyp 3D mesh proceduralny** w `GenerateBallast` (trapez ze skarpą) + tekstura tłucznia tile (granit/bazalt). Wszystkie z teksturami z bake (procedural, nie photo), w grze. Pełne parametry + pipeline: **`asset-creation-guide.md`**. TODO: normale one-sided (#13), szyny na łukach (TD-045/046), seamless tile + smar pod szyną.

---

### 2. Turnouts (rozjazdy + schemas) — `TurnoutPlacer` + `Schemas/`

**Sub-system w kodzie:** `Assets/Scripts/Depot/TurnoutEntity.cs`, `TurnoutPlacer.*.cs`, `Schemas/Generators/`

**Jak działa:** rozjazdy używają tych samych rail+sleeper prefabów co Tracks. Iglice = polylines w `TrackGraph.Switches.cs`. Schemas (ladder/throat/scissors) = generative geometria w polylines.

**Co potrzeba:** **brak nowych assetów** — wykorzystuje rail/sleeper z sekcji 1.

**Status:** ✅ funkcjonalne na placeholder rail/sleeper.

---

### 3. Walls & Rooms — `WallBuildingSystem` + `RoomDetectionSystem`

**Sub-system w kodzie:** `Assets/Scripts/Depot/WallBuildingSystem.{Mesh,Build,Demolish,...}.cs`, `RoomDetectionSystem.cs`, `DoorPlacer.cs`

**Jak działa:** pełen procedural mesh ścian (wysokość `wallHeight=3m`), procedural floor cells per pomieszczenie, drzwi przez `DoorPlacer` (Cube primitives). 11 RoomBuildSubMode types (Hall/Storage/Dispatcher/Office/Social/Supervisor/Bathroom/Locker/Corridor/TrafficController).

**Co potrzeba (materiały PBR tile-able):**
- **Brick_Red** — wall mesh (zewnętrzna strona)
- **Concrete_Floor** — floor cells wewnątrz pomieszczeń
- **White_Trim** (opcjonalnie) — biały band na top parapetu (Y=2.9m→3.0m)
- **Metal_Door** — drzwi z DoorPlacer
- **Glass_Window** (TBD) — okna jeśli dodamy

**Opis stylu (z CBM screen 2/4):**
- **Brick:** naturalna czerwień, wzór cegły widoczny (block size ~24×6cm w tile texture 512×512). Bez heavy grunge, bez plam. Lekki bevel na krawędziach mesh.
- **Concrete floor:** jasnoszary (~70% lightness), 2-3 odcienie z subtelnymi krakelurami i spękaniami. Bez plam oleju (zajezdnia, ale czysto). Tile 512×512.
- **White trim:** czysty biały band na top ścian (kontrast z brick). 0.1m wysokości.
- **Door:** stylized metal door 1m×2.1m, szary lub biały, lekka glossiness. Bez frame detail (placeholder cuboid OK na EA).

**Status:** procedural mesh ✅, materiały ❌ (placeholder zielony cube przy build).

---

### 4. Catenary (sieć trakcyjna) — `CatenaryVisualBuilder` + folder

**Sub-system w kodzie:** `Assets/Scripts/Depot/Catenary/CatenaryVisualBuilder.cs` (+ `WirePathGenerator`, `SupportOptimizer`, `ZoneClassifier`)

**Jak działa:** pełen procedural z Unity primitives:
- Słupy = `CreatePrimitive(Cylinder)` 7-8m wysokie
- Izolatory = `CreatePrimitive(Sphere)` 0.1m diameter
- Druty = `LineRenderer` z sag (zawieszenie)
- Spacing słupków per `CatenarySpacing.cs` (wzorzec PKP)

**Co potrzeba (materiały):**
- **Metal_Painted_Catenary** — ciemnoszary RAL 7012 Basalt Grey (słupy + przęsła naprężania)
- **Porcelain_White** — izolatory (biała/jasnoszara, lekki połysk)
- **Wire_Steel** — druty (cienki metal matowy, ciemny)

**Opis stylu:**
- **Słupy:** ciemnoszary metal, brak detali (latarnie, wsporniki — stylized abstraction). Cylinder primitive zostaje.
- **Izolatory:** białe/jasnoszare sfery, standardowe izolatory PKP (porcelana).
- **Druty:** cienkie ~2mm, matowy metal ciemny.
- **Brak przęseł kratowych ozdobnych** — uproszczenie celowe (CBM-style).

**Status:** ✅ funkcjonalny procedural + szary placeholder material.

---

### 5. Outdoor equipment — `WashZone` / `Turntable` / `PitLift` / `FuelStation`

**Sub-system w kodzie:** `Assets/Scripts/Depot/OutdoorEquipment/` folder

**Jak działa:** rect-z-2-klik placement (wzór z `WallBuildingSystem.HandleWallBuild`). Obecny visual = `CreatePrimitive(Cube)` z kolorem per typ.

**Co potrzeba (4 FBX modele):**

| # | Element | Wymiary | Polycount | Opis stylu |
|---|---|---|---|---|
| 1 | **WashZone** (myjnia bramowa) | 8×6×4.5m | 200-500 tris | Dwie pionowe bramki z spray nozzles na ramie poziomej. Niebiesko-biały industrial sprzęt. Nozzles widoczne na 4 stronach. |
| 2 | **Turntable** (obrotnica) | 12m diameter | 300-600 tris | Okrągła platforma z radial rusztem trakcyjnym. Brązowy beton (ring) + szare metal rails (kratowy ruszt). Pivot środek widoczny. |
| 3 | **PitLift** (podnośnik kanałowy) | 6×4×3m | 400-800 tris | 4-stojakowy podnośnik. Szare metal kolumny ~3m wys + górna platforma. Funkcjonalny industrial. |
| 4 | **FuelStation** (dystrybutor paliwa DMU) | 2×2×3m | 200-400 tris | Kolumna z dyszą + cysterna olejowa za. Pomarańczowo-szary (typowy diesel station). |

**Opis stylu wspólny:** industrial functional, bez ozdób, jasna sygnatura "do czego służy". Stylized PBR, materiały generic (metal + plastic + farba).

**Status:** placeholder cuboid (różne kolory per typ), placement działa.

---

### 6. Furniture — 38 obiektów, `FurnitureCatalogLoader` + JSON

**Sub-system w kodzie:** `Assets/Scripts/Depot/Furniture/` folder, `FurniturePlacer.*.cs`, `furniture_catalog.json` w StreamingAssets

**Jak działa:** multi-source loader (StreamingAssets + AppData + Workshop placeholder). JSON catalog → każdy entry wskazuje `meshFile` (FBX path). Obecnie placeholder cuboidy (różne kolory per `ObjectFunction`).

**Co potrzeba: 38 FBX modeli.**

**Podział per RoomType:**
- **Universal (10):** krzesło, ławka, kosz, donica, lampa stojąca, dywanik, obraz, zegar, wieszak, śmieci recykling
- **Office (5):** desk_office, chair_office, monitor_desk, szafa biurowa, gablota dokumentów
- **Workshop (6):** workbench, tool_cabinet, tire_balance, wózek narzędziowy, tablica narzędziowa, szafa części
- **WashingPlant (6):** węże, wiadra, kanistry, szczotki, oznakowanie
- **Storage (2):** regały, palety
- **Social (3):** stolik kawowy, kanapa, ekspres do kawy
- **Sanitary (3):** umywalka, prysznic, toaleta (placeholder)
- **Locker (2):** szafki pracownicze, ławka szatniana
- **Door (1):** drzwi pomieszczenia (override DoorPlacer cube)

**Opis stylu (generic dla furniture):**
- Standardy office/industrial. Kolory: szary, biały, czarny, akcent niebieski/zielony/pomarańczowy per kategoria.
- Bez ozdób, funkcjonalne, lekki bevel na krawędziach.
- Polycount low: 100-500 tris per obiekt (gracz patrzy z góry, mało detalu widać).
- Tekstury 256×256 (vertex color też OK dla najprostszych).

**Compound furniture pattern:** `WorkstationOfficeComplete` = desk_office + monitor_desk + chair_basic. Greedy-counts w pokoju. Compound to TRZY osobne meshe assembled w kodzie, nie jeden duży.

**Status:** wszystkie 38 = placeholder cuboidy.

---

### 7. Path system — `PathBuildStateMachine` + `PathVisualBuilder`

**Sub-system w kodzie:** `Assets/Scripts/Depot/PathBuildStateMachine.*.cs`, `PathVisualBuilder.cs`, `PathGraph.cs`

**Jak działa:** procedural mesh strips wzdłuż klikniętej polyline. Sub-modes: Chodnik, Parking, Demolish. PathGraph używany przez Personnel `EmployeeWalkPathfinder`.

**Co potrzeba (materiały PBR tile-able):**
- **Concrete_Paving** — chodniki (płyty 1×1m lub kostka brukowa)
- **Asphalt_Path** — share z `Asphalt_City` (sekcja 9)
- **Lane_Markings** — białe linie 0.1m szerokości (parking spots, lane separators) — albedo + alpha mask

**Opis stylu:**
- **Concrete paving:** jasnoszary z subtelnym pattern płyt (linie spojeń 1×1m). Można dodać kolor wear na krawędziach (subtle). 512×512 tile.
- **Lane markings:** białe linie 0.1m szerokości, czyste (nie zmywające się). Stylized — wyraźne, czytelne z góry.

**Status:** procedural mesh ✅, materiał placeholder szary.

---

### 8. Fence — `DepotFenceSystem`

**Sub-system w kodzie:** `Assets/Scripts/Depot/DepotFenceSystem.cs`

**Jak działa:** procedural słupki (`Cylinder`) + siatka między (mesh) + kotwy bram (`Cube`). Spacing słupków 3.5m. 2 bramy (top employee, left railway).

**Co potrzeba:**
- Materiały współdzielone z sekcji 3/4 (`Brick_Red` na podmurówkę, `Metal_Painted` na siatkę)
- **Decorative_Metal_Cap** — biało-szary cap na top słupka (CBM-style)
- **2 prefab bram (opcjonalne):** `railwayGatePrefab` + `employeeGatePrefab` (null = procedural fallback)

**Opis stylu (z CBM screen 4):**
- **Słupki ozdobne:** brick podmurówka 0.5m + biały beton 0.3m middle band + szary metalowy ozdobny cap (decorative) na górze. Procedural composition działa, wystarczy materials.
- **Siatka między słupkami:** kute pręty pionowe ciemny metal (czarny/graphite). Wysokość 1.5m. Spacing 3.5m.
- **Railway gate (lewa):** wysoka rama z poprzeczkami (szlaban czerwono-biały + pionowe pręty). Stylized industrial.
- **Employee gate (góra):** mniejsza brama wahadłowa z pionowymi prętami.

**Status:** procedural działa, materiały placeholder szare.

---

### 9. Ground & otoczenie — `GroundGenerator`

**Sub-system w kodzie:** `Assets/Scripts/Depot/GroundGenerator.cs`

**Jak działa:** plane primitives (główny grass + opcjonalnie background city asphalt) + opcjonalnie procedural road + lista FBX magazynów + opcjonalnie skyline backdrop. Plot rozbudowywalny tier 0-4 (Q1).

**Co potrzeba:**

| Slot | Asset | Status |
|---|---|---|
| `customGroundMaterial` | **`LushGrass_Light`** (grass PBR — Albedo+Normal bpy, dopasowane numerycznie do ref CBM) | ✅ bpy-made (shader→bake→seamless→color-match→normal→URP/Lit; polish odłożony, patrz `asset-creation-guide.md` §5.1) |
| `backgroundGroundMaterial` | **`Asphalt_City`** (ulice poza fence) | ❌ |
| `roadMaterial` | **`Road_Asphalt`** + lane markings | ❌ |
| `backgroundBuildingPrefabs[]` | **5-8 magazynów FBX** industrial pack | ❌ |
| `skylineBackdropPrefab` | **1 FBX skyline** (opcjonalnie, distant city silhouette) | ❌ |
| `terrainPrefab` / `roadPrefab` | TBD (opcjonalne 3D models) | ❌ |

**Opis stylu:**
- **Grass:** ✅ already in place, pastel-naturalny gradient.
- **Asphalt city:** ciemny szary z noise wear, 50m tile. Lekkie kolor variations (oil patches subtle, NIE heavy).
- **Road:** jasny szary asfalt + białe linie pasów + szare krawężniki betonowe (wzór CBM screen 1).
- **Magazyny industrial (5-8 wariantów):**
  - Forma: prosty box + dach płaski lub dwuspadowy. Bez ozdób.
  - Kolory: szary (50%), biały (20%), jasnoniebieski (15%), czerwony brick (15%).
  - Wymiary: 30×40×8-12m wysokie.
  - Polycount: 200-800 tris per warehouse.
  - Tekstura: 512×512 tile (corrugated metal / brick / concrete).
  - **NIE są to nasze hale gracza** — to są budynki **poza fence**, mają dachy bo gracz widzi z zewnątrz.
- **Skyline backdrop:** dalekie sylwetki budynków/kominy/dźwigi, niski poly (~200 tris total), gradient dystans (mgła). Opcjonalnie — może być pomijane na MVP.

**Status:** grass ✅, reszta placeholder/null.

---

### 10. Tabor (pojazdy) — `VehicleController` + `DepotMovementSimulator.Visuals`

**Sub-system w kodzie:** `Assets/Scripts/Depot/VehicleController.cs`, `Movement/DepotMovementSimulator.Visuals.cs`, `ConsistMarker.cs`

**Jak działa:** prefab slots na `VehicleController`. Fallback: `CreatePrimitive(Cube)` = pomarańczowy cuboid. Source-of-truth danych: `Assets/StreamingAssets/Fleet/` (`new_models.json`, `initial_market.json`, `families.json`, `starting_fleet.json`, `wagon_bodies.json`, `wagon_bogies.json`).

**Wagony towarowe — NIE MA w grze.** Pre-EA scope tylko pasażerski (potwierdzone 2026-05-23).

#### Modular kit pattern (zweryfikowane w danych 2026-05-23)

Wszystkie pojazdy w grze są **modular** (mesh composition w kodzie z osobnych klocków):

**a) Wagon = konfigurator (M-FleetConfigurator).** Gracz nie kupuje "111A" tylko składa: pudło + wózki + seats mix + opcje + paint.
- **2 pudła** (`wagon_bodies.json`):
  - **UIC-X 24.5m** (Vmax 160 km/h, klasyczne PKP Bdhpumn/B11/A9)
  - **UIC-Z 26.4m** (Vmax 200 km/h, nowoczesne PKP IC dalekobieżne)
- **3 wózki** (`wagon_bogies.json`):
  - klockowy (Vmax 140, basePricePair 400k zł)
  - tarczowy (Vmax 160, 600k zł)
  - tarczowy-szynowy z magnetycznym hamulcem (Vmax 200+, 800k zł)
- Plus pantografy (n/a dla wagonu), zderzaki, drzwi (`defaultDoorType`: SwingFolding dla UIC-X / SlidingPlugDoor dla UIC-Z).
- **Legacy entries `Coach_111A` / `Coach_112A` / `Coach_152A` / `Coach_156A`** w `initial_market.json` to **fizycznie ten sam mesh UIC-X 24.5m** z różnymi seats wewnątrz + paint. Czarne szyby (decyzja D8) ukrywają wnętrze — gracz nie widzi różnicy między przedziałowy/sypialny/barowy z zewnątrz.

**b) EZT/SZT = osobne pozycje w sklepie, ale modularne fizycznie**: cab end member + N× middle member + wózki.
- **Rodzina EN57_family**: EN57 (3-czł.) + EN71 (4-czł.) + EN76 (post-EA) = 2 mesh'e (cab + middle), różna kompozycja.
- **FLIRT family**: 2/3/5/8 czł. warianty = 2 mesh'e (cab + middle), różna kompozycja N.
- **SA_base** (Bydgostia SA134/SA136): 2 mesh'e (cab + middle).
- **SA_new** (Pesa Link SA137/SA138): 2 mesh'e (cab + middle).

**c) Lokomotywy rodzinne — reuse mesh w obrębie rodziny.**
- **Rodzina EU07_family**: EP07 + EU07 + EP08 = 1 mesh body (różny shader/decals/voltage config per wariant).
- **EU160 Griffin**: 4 voltage warianty (3kV / 3kV+25kV / 3kV+15kV+25kV / 4-systemowa) = 1 mesh body.
- **SM42**, **754 Brejlovec** — każdy 1 mesh body.

**d) Modular components wspólne dla wszystkich pojazdów:**
- Wózki lokomotyw (Bo'Bo' dla większości), wózki SZT (2'B'B'2' dla Bydgostii itp.)
- Pantografy (1 dla lokomotyw 3-członowych, 2 dla 4-członowych — wzór z `families.json`)
- Zderzaki (UIC standard)
- Drzwi pasażerskie (3 typy: SwingFolding klasyczne / SlidingPlugDoor nowoczesne / dla EZT/SZT — wewnętrzne)

#### Pipeline modeli i status (2026-05-23)

**EA blocker (real mesh wymagany):**

| Pojazd / Component | Status | Modelarz |
|--------------------|--------|----------|
| EP07 body (pokrywa rodzinę EU07_family) | ✅ prototyp | freelance (płatne) |
| 754 body | 🔄 in progress | matseb (kolega) |
| EU160 Griffin body (4 voltage warianty reuse mesh) | 🔄 in progress | newkamil (kolega) — **HIGH risk** od zera, after-hours |
| Wagon UIC-X 24.5m pudło | 🟡 planowany | matseb/newkamil whoever-first |
| Wózki pasażerskie (3 typy) + pantografy + zderzaki + drzwi (3 typy) | ❌ brak | **OPEN — wymaga przypisania** |

**EA acceptable jako placeholder cuboid** (gameplay działa, wizualnie pomarańczowy cube z numerem pociągu):

> **AKTUALIZACJA 2026-06-21 (nadrzędna):** EA target = realne family meshe dla całego taboru (ambicja, decyzja użytkownika). **Robimy je MY w bpy** (research-driven), EP07 = head start; lista poniżej = **fallback per rodzina**. Partnerzy/zakupy zewnętrzne = osobny tor (bonus), nie podstawa. Wyjątek: personel → Asset Store. Patrz `docs/design/fleet-catalog-ea.md` §2.
- Wagon UIC-Z 26.4m (rynek IC dalekobieżny)
- EN57/EN71 (EZT — rodzina EN57_family)
- SA134/SA136 (Bydgostia) + SA137/SA138 (Pesa Link)
- SM42 manewrowa
- FLIRT family (2/3/5/8 czł.)

**Post-EA content updates** (kolejność roadmapowa): EZT EN57_family → SZT SA family → FLIRT family → wagon UIC-Z dalekobieżny.

**Opis stylu (z visual-style-guide + CBM screen 5):**
- **Hero modele PBR**, 5k-10k tris (LOD 0).
- **Czarne szyby solid** (no transparency, no interior) — decyzja D8 M-FleetConfigurator.
- Skala 1:1.
- **Kolory body:** muted naturalistic PKP. Gracz konfiguruje przez paint editor (M-FleetConfigurator: 5-10 kolorów PL paleta + decals + paskowy procedural editor + per-człon + per-seria override).
- Decals (logos, numery EVN, oznakowanie kategorii) on top.

**Pełen technical spec:** `asset-pipeline.md` sekcja "Tabor kolejowy".

**Status:** wszystko Cube placeholder pomarańczowy oprócz EP07 prototyp od freelance.

---

### 11. Personel — `EmployeeVisual`

**Sub-system w kodzie:** `Assets/Scripts/Personnel/Runtime/EmployeeVisual.cs`

**Jak działa:** `CreatePrimitive(Capsule)` per pracownik, height 1.8m. Kolor z `RoleDefinitions.GetCapsuleColorRgb` per rola. Animacja sway+bob (procedural transform). Bez modeli 3D.

**Co potrzeba:**
- **1 base mesh** ludzkiej postaci (rigged, animation-ready)
- **10 outfits per rola:**
  1. Maszynista — granat z czapką PKP
  2. Konduktor — mundur granatowy z paskami
  3. Mechanik — niebieski overall + kask
  4. Sprzątacz — szary mundur + miotła
  5. Myjnia (wash bay) — overall + gumowce
  6. Biurowy — biała koszula + krawat
  7. R&D researcher — biały fartuch lab + okulary
  8. Kasjer — biała koszula + identyfikator
  9. Dyspozytor — koszula + krawat
  10. Dyżurny ruchu — pomarańczowa kamizelka odblaskowa
- **Animacje minimum:** idle, walk, work (one cycle per rola opcjonalnie)

**Opis stylu:**
- **Stylized low-poly humans** (Synty POLYGON People style benchmark). Bez detal twarzy.
- Polycount ~2k tris per outfit.
- Kolory outfit **nasycone** żeby gracz rozróżnił role z high zoom (CBM auto-pitch 40-70° = z góry).
- Sylwetka czytelna z dystansu.

**Status:** kapsuły kolorowe (placeholder M8). **Decyzja EA 2026-06-21: na EA realne postacie** (Asset Store Synty POLYGON People + reskin, NIE bpy), kapsuła = fallback. Wcześniej planowane post-EA.

---

### 12. UI — procedural + UITheme

**Sub-system w kodzie:** wszystkie pliki `*UI.cs` w `Assets/Scripts/Depot/UI/`, `Assets/Scripts/SharedUI/`. Panele procedural z UIBuilders, TMP_Text wszędzie.

**Jak działa:** ikony obecnie jako TMP labels (np. "TOR", "EL", "POM", "X") zamiast graficznych. UITheme palette w SharedUI.

**Co potrzeba: ~60-80 ikon 64×64 SVG/PNG.**

**Canonical spec:** `docs/design/icon-sprite-atlas.md` - formaty, sciezki,
nazewnictwo, lista pilot MUI-11/MUI-12 i decals.

**Kategorie ikon:**

| Set | Użycie | Ilość | Przykład labels obecnie |
|---|---|---|---|
| Tool icons | `BuildMenuUI` | 5 | TOR, EL, DR, POM, X |
| RoomBuildSubMode | `RoomBuildPanelUI` | 10 | HAL, MAG, DSP, BIO, SOC, NAC, LAZ, SZT, KOR, DR |
| RoomActionMode | `HallActionStateMachine` | 5-8 | TOR-w-hali, ELEKT, ... |
| TrackBuildSubMode | `TrackSubToolbarUI` | 4-6 | Tory, Parallel, Schemas, Demolish |
| Catenary sub-modes | `CatenarySubToolbarUI` | 3-4 | Single, Parallel, Demolish |
| Path sub-modes | `PathSubToolbarUI` | 3-4 | Chodnik, Parking, Demolish |
| Action icons (Modernization) | `DispatchActionService` UI | 4-6 | Out, Refuel, Workshop, Modernization |
| Maintenance icons | Workshop/Parts panels | 8-12 | Per component icons + part categories |
| Personnel role icons | `PersonnelMainTabUI` | 10 | Per rola (maszynista, mechanik...) |
| Misc | różne | 10-15 | settings, save, load, exit, money, time, speed, pause, play, alert |

**Opis stylu (z CBM screen 1 dolny pasek):**
- **Monochromatyczne** sylwetki na jasnym tle (niebieski/szary 1 kolor pattern dominujący).
- **Czytelne sylwetki**, bez nadmiaru detalu.
- **Outline 2-3px**, jasny shape language (geometric vs organic — geometric preferred dla industrial app).
- 64×64 SVG bazowo (scalable do różnych rozmiarów panelu).
- Plus: **kolorowe akcenty w dolnym pasku akcji** (jak CBM ma pomarańcz/żółty/zielony/fiolet) — gracz rozpoznaje akcję z koloru.

**Status:** TMP labels placeholder. Ikony graficzne ❌.

---

### 13. HDRi / Skybox

**Sub-system w kodzie:** `RenderSettings.skyboxMaterial` w scene `Depot.unity`

**Jak działa:** obecnie null → Unity default solid color skybox.

**Co potrzeba:**
- **1 HDRi** afternoon overcast / clear sky
- Format: HDR (.hdr) lub EXR
- Resolution: 2048 (lub 4096 dla high quality)

**Opis stylu:**
- **Popołudniowy kąt słońca** (~45-60° od horyzontu, ciepły żółtawy — dopasowany do Directional Light color (1, 0.96, 0.88) z lighting decision sesja 9).
- **Lekkie chmury** (overcast preferred — softer shadows, bardziej "CBM mood").
- Brak agresywnego kontrastu (clear sunny day = za ostre cienie dla stylized look).

**Status:** ❌ null, Unity default. Sourcing free: polyhaven.com.

---

## Matryca źródeł — co robić jak

| Asset category | Recommended source | Koszt |
|---|---|---|
| Materiały PBR tile-able (Brick_Red, Concrete_Floor, Asphalt, Metal, Wood, Steel, Ballast, Lane_Markings) | **Self-made procedural Blender Shader → bake PNG** (decyzja 2026-05-31: nie fototekstury) | $0 + czas nauki |
| Stylized variant materials (fallback jeśli procedural za wolny) | **Asset Store STYLIZED materials pack** (NIE photoscan) | $20-40 |
| ~~ambientCG~~ | ❌ **ODRZUCONE 2026-05-31** — photoscan, łamie regułę "nie fototekstury" | — |
| Rail_1m + Sleeper FBX | **Self learn Blender** (proste meshe, modular kit start) lub **freelance** | $0 lub ~500 zł |
| 4 hero pojazdy FBX (lokomotywa EU07/SM42 + 2 wagony) | **Freelance** (Polish train fan community, ArtStation, kolejomania.pl) | ~4-6k zł |
| 5-8 magazynów industrial FBX | **Asset Store warehouse pack** | $20-50 |
| 4 outdoor equipment FBX | **Freelance lub Asset Store mix** | $30-100 lub 2-3k zł |
| 38 mebli FBX | **Asset Store packs** (Synty industrial/office) + custom hero 5-10 | $30-80 + opcjonalnie |
| Personel base + 10 outfits | **Asset Store Synty POLYGON People** + manual reskin | $60 + 1-2 dni |
| 60-80 UI ikon | **AI generation** (Midjourney/DALL-E + vectorize w Inkscape/Affinity) + manual polish | ~3-5 dni pracy |
| Otoczenie / zieleń / ambient town props (drzewa, krzaki, ławki, latarnie, parking deco) | **Asset Store Synty POLYGON Nature / Town** (potwierdzone recon CBM 2026-06-13: genre-peer używa dokładnie tych packów dla otoczenia + `SkyboxBlended`) | $30-60 |
| Tło menu głównego | **Pre-renderowany loop wideo** zajezdni/mapy (off-line render → VideoClip; wzorzec CBM `CBM_Menu_Day`) — post-M-Models | $0 + czas (wymaga gotowej sceny) |
| HDRi skybox | **polyhaven.com (free CC0)** | $0 |

---

## Realna shopping list dla EA (recalibracja 2026-05-23)

**Strategia EA potwierdzona przez user'a:** hero models (subset taboru) jako real mesh + reszta initial_market akceptowalna jako placeholder cuboid. Gracz świadomie wybiera między ceną a wizualem.

### EA blocker (real mesh wymagany — bez tego ship'ujemy z placeholderami w core gameplay):

1. **Rail_1m.fbx + Sleeper.fbx** — bez tego tory wyglądają jak nic.
2. **Hero tabor** (4 mesh'e body + modular components):
   - **EP07 body** (✅ prototyp freelance, pokrywa rodzinę EU07_family = EP07/EU07/EP08)
   - **754 body** (🔄 matseb, kolega)
   - **EU160 Griffin body** (🔄 newkamil, kolega — HIGH risk fallback: jeśli ślizg, post-EA)
   - **Wagon UIC-X 24.5m pudło** (🟡 matseb/newkamil whoever-first, pokrywa wszystkie wagony osobowe gry)
   - **Wózki pasażerskie (3 typy)** + **pantografy** + **zderzaki** + **drzwi (3 typy)** — ❌ **OPEN, wymaga przypisania**
3. **7 materiałów PBR** (Brick_Red, Concrete_Floor, Asphalt_City, Metal_Painted, Steel_Rail, Wood_Sleeper, Gravel_Ballast) — **self-made procedural Blender Shader → bake** (decyzja 2026-05-31: nie ambientCG/photoscan). Koszt $0 ale realny czas nauki (Faza 1 Tekstury w `blender_learning_roadmap.md` staje się pracą, nie downloadem).
4. **HDRi skybox** afternoon overcast — **free na polyhaven.com**, ~15 min.

**Total EA blocker monetarny: ~0 zł** (4 hero pojazdy in pipeline za darmo od kolegów + płatny prototyp EP07 już opłacony). Free assety materiałów + HDRi. Pozostaje wyłącznie **risk lead time modelarzy** + open assignment dla wózków/pantografów/zderzaków.

### EA acceptable placeholder (gameplay działa, wizualnie pomarańczowy cuboid z label):

Reszta `initial_market.json` (z którą gracz świadomie wybiera ekonomicznie):
- Wagon UIC-Z 26.4m (IC dalekobieżny)
- EN57/EN71 (EZT)
- SA134/SA136 (Bydgostia) + SA137/SA138 (Pesa Link)
- SM42 (manewrowa)
- FLIRT family (2/3/5/8 czł., rynek nowy)

Marketing positioning: "EZT/SZT/FLIRT models w pierwszych aktualizacjach content'owych."

### Nice to have EA (placeholder akceptowalny, ale lepsza opcja istnieje):

5. **5-8 magazynów industrial FBX** — Asset Store $20-50, budynki tła poza fence (`backgroundBuildingPrefabs[]`)
6. **38 mebli FBX** — Asset Store packs (Synty Office/Industrial), $30-80
7. **4 outdoor equipment FBX** (WashZone/Turntable/PitLift/FuelStation) — Asset Store $30-100 lub freelance 2-3k zł
8. **Personel base + 10 outfits** — Asset Store Synty POLYGON People $60 + manual reskin 1-2 dni. **Decyzja 2026-06-21: to teraz INTENCJA EA (realne postacie, nie kapsuły)**, nie tylko nice-to-have — kapsuła zostaje fallbackiem. Tania, off-the-shelf → realne na EA bez ryzyka budżetu.
9. **60-80 UI ikon** — AI generation (Midjourney/DALL-E) + vectorize + manual polish, ~3-5 dni

**Total nice-to-have monetarny: ~$200-400 + 5-7 dni AI/Asset hunting + assignment dla outdoor equipment.**

### Post-EA content updates (M-Models gated, kolejność roadmapowa):

10. **EZT** — EN57 + EN71 cab+middle members (rodzina EN57_family)
11. **SZT** — SA_base (Bydgostia SA134/SA136) + SA_new (Pesa Link SA137/SA138) cab+middle
12. **FLIRT family** — Stadler 2/3/5/8 czł. cab+middle
13. **Wagon UIC-Z 26.4m** (IC dalekobieżny pudło)
14. **SM42** (manewrowa body) — opcjonalnie, niski priorytet
15. **Personel finalne stylized humans + animacje** (~2-4k zł)
16. **Inne presety otoczenia** (countryside, mountain) — DLC budgets

### Open questions (do rozstrzygnięcia z modelarzami):

- **Kto robi wózki pasażerskie (3 typy)** — czy razem z wagonem UIC-X (matseb/newkamil), czy zlecone freelancerowi?
- **Kto robi pantografy + zderzaki** — modular components dla EP07/EU160/754 — czy w scope każdego modelarza per pojazd, czy unified?
- **Drzwi (3 typy SwingFolding/SlidingPlugDoor + wewnętrzne EZT/SZT)** — czy modular kit, czy per body?

---

## Co NIE jest w obecnym kodzie (nie wymyślamy)

- ❌ **Particles (smoke/dust/iskry)** — brak ParticleSystem w Depot. Może post-EA polish M12.
- ❌ **Wnętrza pojazdów** — czarne szyby decyzja, brak interior meshes.
- ❌ **Day/night cycle** — static lighting EA, decyzja sesja 9.
- ❌ **Animacje pojazdów** (pantograf w górę/dół) — TBD post-EA.
- ❌ **Weather effects** (deszcz, mgła) — post-EA roadmap.
- ❌ **Audio assets** — niezwiązane z visual, osobny milestone (M12b).

---

## Powiązane docs

- `docs/design/asset-pipeline.md` — technical specs (skala, format FBX, pivot, polycount per typ)
- `docs/design/performance-targets.md` sekcja "Triangles" — budżet trójkątów (per-asset × LOD × ile-na-ekranie + sufity sceny)
- `docs/design/depot-visual-direction.md` — Depot CBM-style decyzje (kamera, lighting, tier)
- `docs/design/visual-style-guide.md` — general style guide (UI, kolory, ton)

## Historia decyzji

- **2026-05-17:** Initial draft — konsolidacja z realnych systemów w kodzie (NIE wymyślamy kategorii). Per subsystem CLAUDE.md Depot/Personnel. Po feedback'u user'a "pisz według tego jak realnie mamy systemy".
- **2026-05-31 (wieczór):** Decyzja produkcji (user) — **bpy-first + GUI hybryda** dla infrastruktury. Modele infrastruktury (tor/katenaria/hardware/budynki/meble) generowane **skryptami `bpy` headless** (`blender --background --python`), nie ręcznym GUI. Stałe skryptu zsynchronizowane z kodem gry. Dowód: `gen_sleeper.py` → `Sleeper.fbx` (84 tris, wymiary z kodu, sekundy). Partnerzy nadal robią tabor hero (organiczne, poza bpy). Self-learn Blender GUI zostaje przydatny (review/ocena/poprawki), nie ścieżka krytyczna.
- **2026-05-31:** Decyzja stylu tekstur (user) — **NIE fototekstury / photoscan**. Wszystkie tekstury (modele + powierzchnie tile-able grunt/ściany/asfalt) robione **proceduralnie w Blender Shader Editor → bake do PNG** (metoda potwierdzona), spójny stylized język w całej grze. **ambientCG odrzucone** (photoscan). Konsekwencja: materiały tile-able przechodzą z "free download ~0 zł 1-2h" do **self-made (czas nauki)** — Faza 1 Tekstury w `blender_learning_roadmap.md` staje się realną pracą. Zaktualizowane: filozofia stylu pkt 1, matryca źródeł (ambientCG → procedural), shopping list EA blocker pkt 3. Trigger: pytanie user'a przy starcie modelowania podkładu (Sleeper) — czy plan uwzględnia tekstury nie-photo (nie uwzględniał, luka w spec Fazy T).
- **2026-05-23:** Recalibracja sekcji 10 "Tabor" — modular kit pattern explicit (wagon UIC-X/Z + 3 wózki, EZT/SZT cab+middle, lokomotywy rodzinne reuse mesh). Usunięto błędne odniesienia ("Eaos/Habbins wagon towarowy" — fracht NIE istnieje w grze, "4 hero pojazdy FBX" generyczny → konkretny pipeline EP07/754/EU160/UIC-X). Shopping list update — EA blocker monetarny ~0 zł (pipeline kolegów + freelance prototyp EP07 opłacony), real cost = lead time + open assignment dla wózków/pantografów/zderzaków. Risk mitigation: hero models real mesh + reszta initial_market placeholder cuboid (gameplay trade-off, NIE forced limitation).
