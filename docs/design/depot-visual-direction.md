# Depot — Visual Direction (CBM-style)

> **Status:** wstępna wizja, ustalona 2026-05-17.
> **Zakres:** decyzje wizualne i kamerowe dla sceny `Depot.unity` (EA target).
> **Reference:** *City Bus Manager* (Doublequote Studio) — wskazany przez user'a jako visual target.
> **Relacja do innych doc'ów:** dopełnia `visual-style-guide.md` (override sekcji materiały dla Depot), uzupełnia `depot-3d.md` (specyfikacja architektury Depot).

> **⚠️ Render pipeline:** projekt jest na **URP (Universal Render Pipeline 17.4, aktywny od 2026-06-17 / M-URP)** — wcześniej Built-in RP. Render proceduralny idzie przez `RailwayManager.Core.Rendering.MaterialFactory`; materiały są URP/Lit (PBR) lub URP/Unlit. Wzmianki o „URP lighting" / „URP DecalProjector" / bloom poniżej opisują CBM-style kierunek wizualny — sam pipeline jest już URP, więc te funkcje (post-processing/bloom, DecalProjector, lepsze światło) są teraz **dostępne na URP, ale ich konkretna implementacja jest TODO (M12)**. Migracja = pipeline + materiały (zrobione), nie te features. „Ładność" budujemy na URP/Lit PBR + lightmapy + skybox; lighting polish CBM-style nadal TODO (M12). To jest też kanoniczne miejsce, do którego odsyłają inne doc'i w sprawie render pipeline.

---

## 1. Cztery decyzje fundamentalne (z sesji 2026-05-17)

| # | Decyzja | Status |
|---|---|---|
| 1 | **Kamera CBM-style auto-pitch + orbit yaw**. **Pitch auto-coupled do zoom**: 40° przy min zoom (mocno boczny) → 70° przy max zoom (elevated 3/4). Zakres 30°. **Yaw kontrolowany przez gracza** (MMB drag + Q/E), pitch nie. Zoom 5-100m, pan + scaled by zoom. | ✅ ustalone (uściślone 2026-05-17 po playtest) |
| 2 | **Otoczenie poza fence: industrial hale/magazyny** (EA). Post-EA: inne presety. | ✅ ustalone (EA) |
| 3 | **Pora dnia: static daylight** (EA). Post-EA: rozważyć day/night cycle. | ✅ ustalone (EA) |
| 4 | **Skala buildable area: 2000×400m** (większa niż CBM, bo pociągi do 400m + głowice rozjazdowe + infra). Wymaga klikalnej minimapy do nawigacji. | ✅ ustalone |

---

## 2. Co widzimy w CBM (style breakdown ze screenów)

Tylko obserwacje ze screenów dostarczonych przez user'a — bez heurestyki gatunku.

### Materiały
- **Asfalt:** ciemnoszary z noise/wear, lekki gradient. NIE flat color.
- **Beton (chodnik, podłoga hali):** jasnoszary z **krakelurą i spękaniami**, 2 odcienie. Tekstura widoczna przy zoom in.
- **Cegła czerwona** (parapety hal, ozdobne pillars fence): faktyczny wzór brick — moduł cegły jest rozpoznawalny.
- **Trawa:** zielona z gradientem/teksturą — nie flat green plane.
- **Metal kuty** (pręty fence, słupki bramowe top): solid color + highlights/shading.
- **Biały trim** (parapet hali, akcenty fence): solid jasny, czysty.
- **Szyby pojazdów:** **solid black**, no transparency, no reflection. (Potwierdzone przez user'a jako planowane też w RM.)

**Wniosek:** materiały to **stylized PBR** — albedo + light normal + minimal roughness variation. NIE flat low-poly z vertex colors. NIE hyper-real PBR z heavy detail. Pomiędzy, bliżej "stylized PBR".

### Geometria
- Średnia ilość detalu — ani Synty ultra-low, ani hyper-detailed.
- Dekoracyjne elementy na poziomie wzroku przy zoom in (ozdobne słupki bramowe = brick + biały beton + szary metalowy chochlik).
- **Hale: walls + floor, BEZ dachu (open top).** Eleganckie rozwiązanie problemu "jak widzieć wnętrze przy fixed elevated camera" bez slice plane / transparent roof. **Już to mamy w RM** (`WallBuildingSystem` nie generuje dachów, grep potwierdza).

### Kolory
- Naturalistyczne, muted. Zieleń trawy nasycona ale nie neonowa.
- Beton/asfalt — naturalne odcienie szarego.
- Cegła — naturalna czerwień brick.
- Akcenty kolorowe **tylko na UI ikonach** (pomarańcz/żółty/zielony/fiolet), nie w world objects.

### Oświetlenie
- Static daylight, kąt ~popołudniowy (cienie długie, czytelne).
- Soft shadows (miękkie krawędzie).
- Brak day/night cycle, brak weather effects.

### Kamera
- **Auto-pitch coupled do zoom** (uściślone 2026-05-17 po playtest).
  - Min zoom (blisko, 5m): pitch 40° — mocno boczny widok, widać ściany.
  - Mid zoom (~50m): pitch ~55° — boczna perspektywa 3/4.
  - Max zoom (daleko, 100m): pitch 70° — elevated 3/4, widać dachy.
  - Zakres 30° daje wyraźną różnicę przy scrollowaniu.
- **Orbit yaw przywrócony** (sesja 5 2026-05-17): MMB drag + Q/E keys obracają kamerę wokół pionowej osi. Pitch nadal automatyczny (gracz NIE kontroluje, T/F keys ignorowane).
- Zoom + pan (pan scaled by zoom dla precyzji przy max zoom in).
- **Max zoom = 100m** — gracz nigdy nie zobaczy całego 2000×400m yardu w kadrze, nawigacja przez klikalną minimapę.

### UI panel budowania
- Kategorie collapsable (`Na zewnątrz / Ulica / Hala`).
- Ikony obiektów: monochromatyczne (niebieski na białym tle).
- Ceny per-item w panelu.
- Drag rectangle preview footprint dla nowego budynku.
- Grid cursor placement (mały zielony kwadracik dla items).

---

## 3. Konsekwencje skali 2000×400m

CBM ma plot, który mieści się w max zoom out. **RM nie**. To wywołuje 3 wymagania, których CBM nie ma:

1. **Klikalna minimapa** jako główny tool nawigacji (nie ozdoba). Klik na minimapie = teleport kamery.
2. **Pan scaled by zoom** — przy max zoom 400m pan szybki (400-500 units/s), przy min zoom 15m pan wolny (precyzja).
3. **Opcjonalne presety zoom-to-area** (post-EA polish): "skup na warsztatach", "skup na bramie", "fit yard". Nie krytyczne na EA.

---

## 4. Konkretne zmiany w kodzie

### `DepotOrbitCamera.cs` (wariant A)

Aktualne wartości (linie 11-22):
```
minPitch = 20f, maxPitch = 85f       // full orbit range
orbitSpeed = 100f                     // full rotation
minZoom = 5f, maxZoom = 150f          // CBM-scale
keyPanSpeed = 100f                    // CBM-scale
```

Zmiany zaimplementowane 2026-05-17:
```
orbitSpeed = 100f                     // yaw rotation speed (pitch jest auto, T/F ignored)
minPitch = 40f, maxPitch = 70f        // auto-pitch range (CBM-style, 30° zakres)
minZoom = 5f, maxZoom = 100f          // gracz nigdy nie widzi całego yardu, nawigacja przez minimapę
keyPanSpeed = 400f                    // szybki pan dla skali 2000m
currentPitch default = 55f            // środek auto-pitch (faktyczna wartość liczona z zoom)
+ HandleKeyboardPan: pan scaling by zoom (0.15× przy min zoom, 4× przy max zoom)
+ UpdateAutoPitch(): targetPitch = Lerp(minPitch, maxPitch, normalize(targetDistance))
+ HandleOrbit (yaw only): MMB drag, pitch.y ignorowane
+ HandleKeyboardOrbit (yaw only): Q/E, T/F ignorowane
```

**Inspector override gotcha (lekcja 2026-05-17):** zmiana `[SerializeField] default value` w kodzie NIE updateuje istniejących instancji komponentu w scenie. Trzeba **manualnie** zmienić wartości w `Depot.unity` (edycja sceny lub Reset w Editor Inspector). Anti-skróty entry: po zmianie defaultów grep'uj sceny i sync.

**Auto-pitch logic:**
- Liczone na `targetDistance` (nie `currentDistance`), żeby pitch wyprzedzał smooth damp zoomu — kamera nie czuje się "opóźniona".
- Wywoływane w `LateUpdate` przed `ApplySmoothDamp` — smooth damp interpoluje currentPitch → targetPitch normalnie.

**Hook dla minimapy:** już istnieje publiczne API `FocusOn(Vector3 worldPos, bool instant = false)` (linia 244) — minimapa wywoła `camera.FocusOn(worldPos, instant: true)`. Plus `FocusOnObject(GameObject)` dla klikalnych markerów.

**Status:** ✅ DONE 2026-05-17. ~35 linii zmiany. _cameraActions.OrbitHeld/Yaw/Pitch bindings zostają w InputActions.inputactions (forward compat, nieużywane).

### `GroundGenerator.cs`

Aktualnie (linie 22-23):
```
buildableAreaSize = Vector2(2000f, 400f)
```

Zostaje 2000×400m (potwierdzone przez user'a).

Do zmiany / podpięcia:
- `generateBackgroundBuildings = true`
- `backgroundBuildingPrefabs[]` — podpiąć industrial warehouse pack
- `useFog = false` na EA

### Scena `Depot.unity`

- **Directional Light:** angle ~45-60° od horyzontu (popołudniowy kąt, cienie długie czytelne).
- **Skybox:** Unity built-in default albo HDRi z [polyhaven.com](https://polyhaven.com) (free, "afternoon" / "overcast" warianty).
- **Baked GI** dla statycznych obiektów (perimeter fence, ground, distant buildings) — szybciej w runtime.
- **Realtime shadows** tylko od jednej directional light.

### Nowy komponent: minimapa klikalna

- Plik: `Assets/Scripts/Depot/UI/DepotMinimapUI.cs` (proponowany)
- Klik na minimapie → wywołuje `DepotOrbitCamera.JumpToPoint(worldPos)`.
- Renderuje top-down ortho view zajezdni (osobna kamera + render texture).
- Markery (POI): brama, hale warsztatowe, dyspozytornia, biuro. **Real-time pociągi / pracownicy → odłożone, decyzja TBD.**

---

## 5. Asset hunting / modeling lista

**Na EA (placeholder + asset store, przed nauczeniem się Blendera):**
- Industrial warehouse pack (Asset Store, ~20-50 USD) — 5-8 magazynów wystarczy dla otoczenia.
- HDRi skybox afternoon (polyhaven.com, free).
- Materiały placeholder dla hal/podłogi/asfaltu — Asset Store "stylized building materials" pack, lub Quixel Megascans (free do non-commercial, koszt licencji TBD do sprawdzenia przed shipping).

**Post-EA / po nauczeniu Blendera (M-Models):**
- Własne modele taboru + hal w finalnym CBM-style.
- Własne tekstury materiałów (brick wzór, beton krakelura, asfalt wear).

---

## 6. Pipeline kolejność prac

Proponowana kolejność dla implementacji visual direction (każdy etap weryfikowalny):

1. **Kamera fixed-pitch** ✅ **DONE 2026-05-17** — `DepotOrbitCamera.cs` zmieniony (pitch lock 70-75°, zoom 15-400m, keyPanSpeed 400 + scaling by zoom, HandleOrbit/HandleKeyboardOrbit/orbitSpeed usunięte). FocusOn API już istnieje dla minimapy.
2. **Dynamic resize GroundGenerator** ✅ **DONE 2026-05-17** — Q1: 5 tier rozbudowy (Tier 0 start 800×300 → Tier 4 max 2000×400). NW corner = fixed origin. API: `ExpandToNextTier()`, `CanExpand()`, `RestoreTier()`, `PreviewNextTierSize()`, event `OnBoundsChanged`. Save persistence w `DepotSavable` (1 int `currentTier`, fallback 0). ContextMenu items dla manual test. **Brakuje:** UI gracza (1 przycisk "Rozszerz zajezdnię" z preview kolejnego tier'a) — TBD osobna sesja gdy będzie pewność co do cennika/M-Balance.
3. **Greybox lighting** ✅ **DONE 2026-05-17** — Directional Light color zmieniony na ciepły daylight (1, 0.96, 0.88), shadows ON (Soft Shadows). RenderSettings: AmbientMode 0→1 (Trilight gradient), AmbientSky błękit (0.6, 0.75, 0.92), AmbientEquator pastelowy (0.7, 0.72, 0.7), AmbientGround ciepły brąz (0.45, 0.42, 0.38). Skybox material null (gracz może podpiąć HDRi z polyhaven.com później). Baked GI **TBD** (wymaga "Generate Lighting" w Unity Editor — manual click przez user'a w odpowiednim momencie).
4. **Materiały hal/ścian placeholder** (asset hunting + apply, 1-2 sesje) — brick + concrete + biały trim, Asset Store pack. **Q3 ustalany w tym kroku.**
5. **Industrial otoczenie** (asset hunting + apply, 1 sesja) — kupić 1 warehouse pack, podpiąć do `backgroundBuildingPrefabs[]`.
6. **Minimapa real-time** (2-3 sesje) — `DepotMinimapUI` + ortho camera + RenderTexture + integracja z `DepotOrbitCamera.JumpToPoint` + hooks na `DepotMovementSimulator` i `EmployeeWalkSimulator`.

**Po tych krokach:** mamy prototyp CBM-style visual direction na obecnych placeholder'ach pojazdów + funkcjonalna rozbudowywalna zajezdnia + real-time minimapa. Wtedy dopiero podejście do nauki Blendera (M-Models) z konkretnym target wizualnym przed oczami.

---

## 7. Pytania otwarte i decyzje uzupełniające

### Rozstrzygnięte 2026-05-17

- **Q1 (gameplay): Rozbudowywalna zajezdnia.** Gracz startuje z Tier 0 (800×300m), kupuje kolejne pakiety (Tier 1-4) do max Tier 4 (2000×400m). **Każdy pakiet to kombinowany expand E + S** (length + width równocześnie). Konsekwencje techniczne:
  - `GroundGenerator` ma **runtime tier API** (`ExpandToNextTier`, `RestoreTier`, `CanExpand`).
  - Perimeter fence regeneruje się po każdym zakupie (`DepotFenceSystem.RegenerateFence` już istniało).
  - Save format: 1 int `currentTier` (z fallback 0).
  - Buildable area expand triggeruje `OnBoundsChanged` event (subscribers: minimapa, spatial indexes w przyszłości).
  - **Sub-decyzje zamknięte** (sekcja 7a poniżej).

- **Q2 (minimapa): Real-time pociągi + pracownicy.** Konsekwencje techniczne:
  - Osobna ortho camera + render texture top-down view zajezdni.
  - Hook do `DepotMovementSimulator` (pozycje pociągów) + `EmployeeWalkSimulator` (pozycje pracowników).
  - Throttle update do 10Hz wystarczy (nie 50Hz physics) — minimapa to UI, gracz nie zauważy.
  - Markery klikalne (klik = camera jump + opcjonalnie popup).
  - **Sub-pytania do rozstrzygnięcia** (sekcja 7a poniżej).

- **Q4 (multi-presets otoczenia):** Post-EA TBD. Przeniesione do sekcji 8.

### Nadal otwarte

- **Q3 (asset licencjonowanie):** Niepilne, do rozstrzygnięcia w momencie konkretnego asset huntingu. User stwierdza "nie wiem co dokładnie potrzebujemy, co uda bądź nie mi się zrobić" — Q3 zostaje **deferred do startu pipeline kroku 3** (materiały hal/ścian) i kroku 4 (industrial otoczenie). Wtedy konkret wymusi decyzję.

---

## 7a. Sub-decyzje dla rozbudowywalnej zajezdni (Q1)

Rozstrzygnięte 2026-05-17:

- **Q1.1: Starting plot size = 800×300m** (Tier 0). ✅ Mieści 2-3 tory długości ~250m + 1 mała hala + brama.
- **Q1.2: Max plot size = 2000×400m** (Tier 4). ✅ Mieści najdłuższy pociąg (400m) × 5 z marginesami.
- **Q1.3: Granularność = pakiety kombinowane (5 tier, interpretacja A z rysunku 2026-05-17).** ✅
  - **Góra (Z+) zablokowana** — tam idzie droga zewnętrzna.
  - **Lewa (X-) zablokowana** — tam wchodzą tory wyjazdowe (external tracks generowane w `DepotManager.GenerateExternalTracks`).
  - **NW corner plot'a = fixed origin.** Rośnie tylko na E + S.
  - **Każdy tier to kombinowany pakiet** (length + width równocześnie). Tabela:
    | Tier | Length (X) | Width (Z) |
    |---|---|---|
    | 0 (start) | 800 m | 300 m |
    | 1 | 1100 m | 325 m |
    | 2 | 1400 m | 350 m |
    | 3 | 1700 m | 375 m |
    | 4 (max) | 2000 m | 400 m |
  - Length step: 300 m/tier. Width step: 25 m/tier.
  - UI: 1 przycisk "Rozszerz zajezdnię" (kupuje cały następny pakiet) z preview kolejnego tier'a.
- **Q1.4: Ciągłość = wymagana** (implicite — pakiety zawsze przylegają, NW corner fixed).
- **Q1.5: Cennik = M-Balance.** ✅ API przyjmuje cenę z constants (per tier), wartości tuningowane w M-Balance.

**Implikacja dla GroundGenerator:**
- Origin plot'a fixed (`nwCornerWorld`).
- Aktualny rozmiar w `currentTier` (0..4) + cached `buildableAreaSize` (sync z tier w Awake/SetTier).
- Tylko E + S edges fence regenerowane przy expand.
- Save: 1 int `currentTier` (mały save footprint, fallback do 0 dla pre-Q1 save'ów).

---

## 7b. Sub-decyzje dla real-time minimapy (Q2)

Rozstrzygnięte 2026-05-17:

- **Q2.1: Co pokazujemy.** ✅
  - Pociągi w zajezdni (z `DepotMovementSimulator`)
  - Pracownicy (z `EmployeeWalkSimulator`)
  - Outdoor equipment status — **NIE** real-time status, ale TAK statycznie w render (kafelki).
  - Stan torów per blok — **NIE** (out of scope EA).
  - **Układ torów (tracks)** — TAK w render texture.
  - **Catenary** — NIE (za gęsto wizualnie).
- **Q2.2: Filtry warstw.** ✅ **NIE na EA**, post-EA polish.
- **Q2.3: Klikalność.** ✅ Klik na marker = camera jump (instant teleport, `FocusOn(pos, instant: true)`). **Bez popup** pojazdu/pracownika (out of scope EA).
- **Q2.4: Update rate.** ✅ Markery 10Hz (każde 5 fixed frame'ów). Render texture re-render co 1s (świat statyczny zmienia się rzadko — GPU savings).

**Specyfikacja pełna (sesja 10 2026-05-17):**

| Aspekt | Decyzja |
|---|---|
| Plik | `Assets/Scripts/Depot/UI/DepotMinimapUI.cs` |
| Architektura | Ortho cam (child GO) + RenderTexture 512×128 |
| Pozycja UI | Prawy górny róg, anchor top-right offset (-20, -20) |
| Rozmiar UI | 400px szerokość, **height adaptywny** z current bounds aspect |
| Adaptive aspect | Tier 0=2.67:1 (400×150), Tier 4=5:1 (400×80), interpolowane |
| Background render | Ground, tracks, buildings, outdoor equipment (statyczne) |
| Markery pociągów | Żółte UI sprites, pool pattern, pull z `DepotMovementSimulator` |
| Markery pracowników | Niebieskie UI sprites, pool pattern, pull z `EmployeeWalkSimulator` |
| Frustum ramka | Biała półprzezroczysta, poll z `DepotOrbitCamera` position/yaw |
| Klik | UV → world via ortho projection → `FocusOn(pos, instant: true)` |
| Render texture refresh | Co 1s lub on `OnBoundsChanged` event |
| Markery update rate | 10Hz (5 fixed frame'ów) |
| Reaktywność na resize | `GroundGenerator.OnBoundsChanged` → refit ortho cam + resize UI height |
| Reaktywność na kolizje UI | Wariant A: subscribe `DepotUIManager.OnToolChanged/OnRoomSubModeChanged` + fullscreen overlays → offset position |
| Spawn | `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` w scenie Depot |

**Plan implementacji w 7 sub-steps:**
1. Bare-bones: ortho cam + RT + RawImage w rogu, statyczny render
2. Adaptive aspect: `OnBoundsChanged` → fit ortho + resize UI height
3. Frustum ramka: biała ramka pokazująca camera view
4. Markery pociągów: pull z `DepotMovementSimulator` co 10Hz
5. Markery pracowników: pull z `EmployeeWalkSimulator` co 10Hz
6. Click handler: UV → world → `FocusOn(instant)`
7. Reactive position: subscribe na UI events, offset przy kolizji

**Kolory markerów MVP:** wszystkie pociągi żółte, wszyscy pracownicy niebiescy. Filtry/kolory per kategoria/rola → post-EA polish.

---

## 8. Post-EA visual roadmap

- Day/night cycle
- Inne presety otoczenia (Q4 — countryside, urban Polish town, mountain dla DLC krajów)
- Sezonowość (śnieg zimą, jesień)
- Weather effects (deszcz, mgła)
- Day/night cycle pojazdów (światła pozycyjne wieczorem)
- Minimapa: filtry warstw (Q2.2), update rate skalowane do zoom (Q2.4)
- Zoom-to-area presety kamery ("skup na warsztatach", "skup na bramie", "fit yard")

Te elementy są **explicit out of scope** dla EA. Static afternoon daylight + jeden preset otoczenia industrial + real-time minimapa z 3 warstwami (no filtry) = baseline EA.

---

## 8a. Tła ekranów (menu / esc / ustawienia) — recon CBM 2026-06-13

> Jak CBM robi „ładne tła" ekranów (recon `cbm-menu-backgrounds`). Wniosek: to nie magia frameworka —
> pre-render wideo + ładne sceny gry (Synty + URP — u nas URP też aktywny od 2026-06-17 / M-URP) + freeze pod esc.

**Menu główne = ZAPĘTLONE WIDEO** [potwierdzone]. Scena `MainMenu.unity` u CBM to **pusta skorupa ~4.8 KB**
(Directional Light + EventSystem + VideoPlayer + Image) — zero geometrii 3D, zero kamery. Tło = pre-renderowany
`VideoClip` (`CBM_Menu_Day`) → RenderTexture + flat UI deco (logo, panele deco). CBM ma ~19 VideoClipów
(menu/loading/transitions/medale) — mocno używa wideo do polishu UI. **NIE żywa scena 3D, NIE statyczny obrazek.**

**Esc/pauza = overlay na ŻYWEJ, ZAMROŻONEJ scenie gry** [potwierdzone]. `timeScale=0` zamraża grę, panel UI na
wierzchu, **BEZ blura, bez snapshotu** (zero blur/DoF shadera na overlayach — grep=0). Widać zamrożoną klatkę
ładnej sceny pod spodem. Ustawienia analogicznie (overlay; brak dedykowanego tła).

**Dlaczego ładnie:** (1) menu = pre-render wideo (pełna jakość off-line, tanie w runtime); (2) sceny gry =
**Synty Studios Polygon** packi (Nature/Town/Tree, LOD) + URP lighting (directional + cienie + lightmapy + mgła +
reflection probe) + **day/night skybox blend** (`SkyboxBlended` shader). Tło pod esc to po prostu ładna gra zamrożona.

**Dla RM (kierunek):**
- **Esc/settings = freeze (`timeScale=0`) + panel, BEZ blura** — CBM potwierdza że blur niepotrzebny. „Ładne tło
  pod esc" przyjdzie ZA DARMO gdy scena zajezdni będzie ładna (M-Models + to visual direction).
- **Menu = pre-renderowany loop wideo** zajezdni/mapy — największy lever na ładne menu, ale wymaga najpierw ładnej
  sceny 3D do nagrania → **post-M-Models**. Dziś nasze menu = procedural UI na płaskim `#14141F`.
- Fundament „ładności" = Synty-style lowpoly + światło + skybox (= ten doc, CBM-target). *(„URP światło" tu = URP jest już aktywny — pipeline + materiały zrobione, M-URP; konkretny lighting polish CBM-style — lightmapy/cienie/mgła/skybox tuning — nadal TODO M12. Patrz nota o render pipeline na górze doc'a.)* Znów **ART/content,
  nie framework** (spójne z całym reconem CBM). Synty Polygon jako źródło otoczenia → patrz `asset-needs-by-system.md` matryca.

---

## 9. Historia decyzji

- **2026-04-28:** `visual-style-guide.md` v1 z kierunkiem "low poly nowoczesna makieta" (przed sprecyzowaniem CBM jako target).
- **2026-05-17 (sesja 1):** User precyzuje target jako CBM, ujawnia rozjazd z `visual-style-guide.md` (CBM ma więcej tekstur niż "niski szum"). Ustalono: idziemy w CBM-style, `visual-style-guide.md` zostanie zaktualizowany w sekcjach materiałowych. Powstaje ten doc z 4 decyzjami fundamentalnymi + 4 pytaniami otwartymi.
- **2026-05-17 (sesja 2):** Rozstrzygnięto Q1 (rozbudowywalna zajezdnia), Q2 (real-time minimapa), Q4 (post-EA TBD). Q3 (asset licensing) deferred do momentu konkretnego asset huntingu. Dorzucone sub-pytania Q1.1-Q1.5 i Q2.1-Q2.4 wymagające rozstrzygnięcia przed implementacją dynamic resize i real-time minimapy.
- **2026-05-17 (sesja 3):** Rozstrzygnięto wszystkie sub-pytania Q1.x i Q2.x. Q1: start 800×300m → max 2000×400m, expand kierunkowy tylko E+S (góra=droga, lewa=external tracks fixed), NW corner = origin, krok 100m, cennik w M-Balance. Q2: pociągi+pracownicy only (no outdoor/tracks), klik=camera jump (no popup), 10Hz update, no filtry na EA. Pipeline (sekcja 6) zaktualizowany o krok 2 "dynamic resize GroundGenerator" przed minimapą. **Wizja kompletna dla EA depot — gotowe do implementacji.**
- **2026-05-17 (sesja 4):** Krok 1 pipeline'u (kamera) zaimplementowany. Pitch lock 70-75° **odrzucony** po obserwacji screenów CBM gameplay — okazało się że CBM ma **auto-pitch coupled do zoom**. Iteracja wartości:
  - Wstępna: 50-80°, zoom 15-400m. **Problem:** żadne zmiany w runtime nie były widoczne — Inspector w `Depot.unity` miał wciąż stare wartości (20-85°, zoom 5-100), które overrideowały defaulty z kodu.
  - **Naprawione:** edycja `Depot.unity` bezpośrednio (sceny Unity to YAML). Lesson learned: `[SerializeField]` default w kodzie nie updateuje istniejących instancji.
  - Druga iteracja: 65-75°, zoom 15-400m. **Za subtelne** wg user'a.
  - **Trzecia (po playtest):** minPitch=50, maxPitch=75, minZoom=15, maxZoom=100. Zakres 25° daje czytelną różnicę.
- **2026-05-17 (sesja 5):** Korekta po dalszym playtest:
  - `minZoom` 15→**5** (bliższy zoom dla detalu).
  - **Orbit yaw przywrócony** (poprzednio całkowicie usunięty w wariancie A). MMB drag + Q/E klawisze obracają kamerę wokół pionowej osi. **Pitch nadal automatyczny** (T/F klawisze ignorowane, gracz nie ma kontroli nad pitch — to dalej coupled do zoom). To uściślenie wariantu A: "wymiana orbit-camera na CBM-style" znaczy *wymiana wolnej kamery orbitalnej z pełną kontrolą pitch/yaw na CBM-style z auto-pitch + yaw-only orbit*, nie *całkowite usunięcie rotacji*.
- **2026-05-17 (sesja 6):** Pitch range zmieniony 50-75°→**40-70°** po dalszym playtest. Zakres 30°, bardziej boczny widok przy zoom in (mocniejsza perspektywa ścian), mniej top-down przy zoom out. To finalna iteracja kamery — jeśli będą dalsze tweaki, traktować jako tuning post-EA.
- **2026-05-17 (sesja 7):** Krok 2 pipeline'u (dynamic resize GroundGenerator) zaimplementowany — pierwsza wersja z osobnymi `ExpandLength/ExpandWidth` (krok 100m, niezależnie length i width). Stare pole `buildableAreaOffset` usunięte.
- **2026-05-17 (sesja 8):** Refactor Q1.3 modelu po obrazku od user'a — **pakiety kombinowane (interpretacja A)** zamiast niezależnych length/width expand'ów. 5 tier (0..4), każdy tier to predefined (length, width) pair. Length step 300m/tier, width step 25m/tier. Public API: `ExpandToNextTier`, `RestoreTier`, `CanExpand`, `PreviewNextTierSize`. Save persistence uproszczone do 1 int `currentTier` (zamiast 2 floatów). Inspector: `[SerializeField] private int currentTier`. `Awake()` sync `buildableAreaSize` z tier. Scena `Depot.unity` dostała `currentTier: 4` (obecna zajezdnia testowa = Tier 4 max). **TBD:** UI gracza (1 przycisk z preview kolejnego tier'a) + cennik per tier (M-Balance).
- **2026-05-17 (sesja 9):** Krok 3 lighting + UI gracza dla expand. Lighting: Directional Light ciepły daylight (1, 0.96, 0.88) + Soft Shadows ON; AmbientMode 0→1 (Trilight gradient) z błękitnym sky + pastelowym equator + ciepłym ground. UI: nowy `DepotExpandPanelUI.cs` w `Assets/Scripts/Depot/UI/`. Auto-spawn przez `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`. Floating panel lewy-dolny róg (nad BuildMenuUI). Pokazuje current tier + dimensions + preview kolejnego + cena. Subscribe na `GroundGenerator.OnBoundsChanged` + `GameState.OnMoneyChanged`. Klik = `GameState.Money -= cost` + `ExpandToNextTier`. Cennik placeholder w `TIER_COSTS = [0, 50k, 100k, 200k, 400k]` (TODO M-Balance). Czułość kamery zmniejszona 2× (orbitSpeed 50, panSpeed 0.25, keyPanSpeed 200) po playtest.
- **2026-05-17 (sesja 10):** Krok 6 minimapa real-time — discussion + spec. Architektura A (ortho cam + RT). Pozycja prawy górny róg, 400px szer, height adaptywny z current bounds aspect (Tier 0=2.67:1, Tier 4=5:1). W render texture: tracks TAK, catenary NIE, outdoor equipment TAK statycznie. Markery pociągów żółte + pracowników niebieskie (MVP, kolory per kategoria/rola = post-EA). Frustum ramka TAK (biała półprzezroczysta). Klik = instant teleport via `FocusOn`. Re-render świat co 1s, markery 10Hz. Adaptywny aspect minimapy resize'uje się przy `OnBoundsChanged`. Reaktywne przesuwanie (wariant A) przy kolizji z UI sekcji budowania. Spec sekcja 7b zaktualizowana z pełną specyfikacją + 7 sub-steps implementacji.
