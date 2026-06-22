# Zajezdnia 3D — pełna dokumentacja

> **Status:** Skonsolidowany dokument z 3 plików (2026-04-14).
> Oryginalne pliki: Kolejnosc_Zajezdnia.md, TODO_Zajezdnia_3D.md, Assety_Zajezdnia_3D.md
>
> **⚠️ UWAGA:** Niektóre odniesienia do skryptów mogą być nieaktualne. Np. DepotDemo.cs już nie istnieje.
> Przy implementacji sprawdzaj aktualny stan w Assets/Scripts/Depot/.

---

# CZĘŚĆ 1: KOLEJNOŚĆ IMPLEMENTACJI

# Kolejnosc implementacji - Zajezdnia 3D

Instrukcja krok po kroku do implementacji sceny zajezdni 3D.
Kazdy krok jest opisany tak, zeby kolejny Claude w nowej sesji mogl go zrozumiec i wykonac.

## ZANIM ZACZNIESZ - przeczytaj te dokumenty:
1. `Zrodla wiedzy/TODO_Zajezdnia_3D.md` — pelna specyfikacja co trzeba zrobic
2. `Zrodla wiedzy/Assety_Zajezdnia_3D.md` — lista wszystkich potrzebnych assetow/modeli
3. `Zrodla wiedzy/Koncepcja zajezdni.png` i `Koncepcjazajezdni2.png` — screeny z koncepcja layoutu

## KONTEKST PROJEKTU:
- Projekt Unity, C#, namespace `DepotSystem`
- Scena: `Assets/Scenes/Depot.unity`
- Skrypty: `Assets/Scripts/Depot/`
- UI: `Assets/Scripts/Depot/UI/`
- Modele: `Assets/Models/Depot/`
- Prefaby: `Assets/Prefabs/`
- Rendering: **URP (Universal Render Pipeline 17.4, aktywny od 2026-06-17 / M-URP)** — materiały tworzone przez `RailwayManager.Core.Rendering.MaterialFactory` jako URP/Lit (PBR) i URP/Unlit (wcześniej Built-in RP / shader `Standard`/`Unlit`). Post-processing (bloom), decale (`DecalProjector`) i lepsze światło są teraz dostępne na URP, ale implementacja jest jeszcze TODO (M12) — sama migracja objęła pipeline + materiały, nie te features. Patrz `depot-visual-direction.md`.
- Siatka: 1x1m snap, kolizje przez raycast na Plane Y=0, warstwa "Ground"

## ISTNIEJACE SKRYPTY (do wyrzucenia/przebudowy — patrz TODO sekcja III):
- `DepotManager.cs` — do przebudowy (usunac auto-placement)
- `GroundGenerator.cs` — do zostawienia, dostosowac rozmiar
- `PrefabTrackBuilder.cs` — do przebudowy (zintegrowac z UI)
- `ExitTrackController.cs` — do przebudowy (tory z lewej)
- `FreeCameraController.cs` — DO WYRZUCENIA (zastepuje DepotOrbitCamera)
- `BuildingPlacer.cs` — DO WYRZUCENIA (zastepuje WallBuildingSystem)
- `TrackBuilder.cs` — DO WYRZUCENIA (zastepuje PrefabTrackBuilder)
- `DepotDemo.cs` — DO WYRZUCENIA (tymczasowy)
- `DepotVehicle.cs` — zostawic
- `DepotVehicleManager.cs` — do przebudowy (panel UI)
- `VehicleController.cs` — zostawic, zamienic proceduralne modele na FBX
- `DepotRailwayIntegration.cs` — zostawic

## ZASADA: UI RAZEM Z FUNKCJA
Kazda nowa funkcja od razu dostaje swoj kawalek interfejsu. Nie budujemy niczego "na klawiszach tymczasowych" — od poczatku jest toolbar, panele i pop-upy. Dzieki temu kazdy krok mozna od razu recznie testowac jak gracz.

---

## FAZA 1 — FUNDAMENT

Cel: dzialajaca scena z kamera, terenem, torami, siecia trakcyjna, pociagiami i bazowym UI.

---

### KROK 1.1: Szkielet UI + kamera orbitujaca
**Referencja:** TODO punkt 1, 15.1, 15.2
**Tworzy:**
- `Assets/Scripts/Depot/DepotOrbitCamera.cs`
- `Assets/Scripts/Depot/UI/DepotUIManager.cs`
- `Assets/Scripts/Depot/UI/TopBarUI.cs`
- `Assets/Scripts/Depot/UI/ToolbarUI.cs`
**Usuwa:** `FreeCameraController.cs` (po zakonczeniu)

**Co zrobic:**

**A) Szkielet UI (najpierw to!):**
1. Stworz Canvas w scenie Depot (Screen Space - Overlay)
2. `DepotUIManager.cs` — centralny manager UI:
   - Referencje do wszystkich paneli UI
   - Enum `ToolMode` (Select, BuildWall, BuildDoor, BuildWindow, BuildTrack, BuildCatenary, BuildPath, PlaceFurniture, Demolish)
   - Property `CurrentTool` — aktualnie wybrane narzedzie
   - Eventy: `OnToolChanged`, `OnObjectSelected`, `OnObjectDeselected`
3. `TopBarUI.cs` — gorny pasek:
   - Nazwa zajezdni (InputField, edytowalna)
   - Zegar gry: "06:00" + przyciski predkosci (1x, 2x, 3x, ||)
   - Saldo pieniedzy (Text: "150 000 zl")
   - Przycisk "Mapa 2D" (na razie nieaktywny)
4. `ToolbarUI.cs` — dolny pasek narzedziowy:
   - Rząd przyciskow (Button z Image):
     - Selekcja (strzalka) — domyslny, aktywny
     - Buduj sciane — nieaktywny (odblokuje sie w kroku 2.1)
     - Buduj drzwi — nieaktywny
     - Buduj okno — nieaktywny
     - Postaw tor — nieaktywny (odblokuje sie w kroku 1.4)
     - Siec trakcyjna — nieaktywny (odblokuje sie w kroku 1.5)
     - Postaw chodnik — nieaktywny
     - Meble — nieaktywny
     - Wyburz — nieaktywny
   - Klikniecie przycisku = zmiana `DepotUIManager.CurrentTool`
   - Aktywny przycisk podswietlony (inny kolor tla)
   - Przyciski zaczynaja sie jako nieaktywne (szare) i sa odblokowywane w kolejnych krokach

**B) Kamera orbitujaca:**
1. Stworz `DepotOrbitCamera.cs` w namespace `DepotSystem`
2. Kamera orbituje wokol punktu centralnego (pivot point na Y=0)
3. Sterowanie:
   - LPM przytrzymany = orbit (obrot wokol pivotu) — ALE tylko gdy kursor nie jest nad UI
   - PPM przytrzymany = pan (przesuwanie pivotu)
   - Scroll = zoom (odleglosc od pivotu)
   - WSAD = alternatywne pan
4. Ograniczenia:
   - Kat: min ~20 stopni od horyzontu, max ~85 stopni (prawie z gory)
   - Zoom: min ~5m, max ~300m od ziemi
   - Granice pan: kamera nie wychodzi poza obszar budowlany + margines 200m
5. Smooth damp / lerp na wszystkich ruchach
6. Podwojne klikniecie na obiekt = centrowanie kamery na nim
7. Granice dynamicznie sie zmieniaja przy rozszerzaniu terenu (public metoda UpdateBounds)
8. WAZNE: kamera ignoruje input gdy kursor jest nad UI (EventSystem.IsPointerOverGameObject)

**Jak testowac:** Uruchom scene — widac gorny pasek z zegarem i dolny toolbar z przyciskami (wiekszosci szarych/nieaktywnych). Kamera plynnie orbituje, panuje, zoomuje. Klikanie w UI nie obraca kamery.

**Gotowe gdy:** Szkielet UI dziala (gorny pasek + toolbar), kamera orbituje plynnie, nie reaguje na klikanie w UI.

---

### KROK 1.2: Teren i uklad sceny
**Referencja:** TODO punkt 2
**Modyfikuje:** `GroundGenerator.cs`

**Co zrobic:**
1. Zmien `GroundGenerator.cs`:
   - Rozmiar terenu glownego: 2400m (X) x 800m (Z) — z marginesem na tlo
   - Obszar budowlany (wewnatrz plotu): 2000m (X) x 400m (Z), poczatek np. (200, 0, 200)
   - Siatka 1x1m widoczna TYLKO wewnatrz obszaru budowlanego
   - Major lines co 10m
2. Dodaj droge na gorze sceny:
   - Szary pas (Quad/Plane) na gornej krawedzi, biegnie horyzontalnie na calej szerokosci
   - Material: szary asfalt
   - Pozycja Z: powyzej gornej krawedzi obszaru budowlanego
3. Dodaj placeholder tla:
   - Proste BoxMesh (prostopadlosciany) roznych wysokosci za granicami plotu
   - Rozne kolory (cegla, beton, szklo) — na razie losowe
   - Nieinteraktywne, warstwa "Background"
4. Ustaw directional light (slonce) i skybox

**Parametry do wystawienia w Inspectorze:**
- `buildableAreaSize` (Vector2: 2000, 400)
- `buildableAreaOffset` (Vector2: 200, 200)
- `gridCellSize` (float: 1)
- `majorGridInterval` (int: 10)
- `gridRenderDistance` (float: 500)

**Gotowe gdy:** Widac zielony teren, siatke 1x1 wewnatrz prostokatu budowlanego, droge na gorze, placeholderowe budynki tla wokol.

---

### KROK 1.3: Ogrodzenie i bramy
**Referencja:** TODO punkt 3
**Tworzy:** `Assets/Scripts/Depot/DepotFenceSystem.cs`

**Co zrobic:**
1. Stworz `DepotFenceSystem.cs` — proceduralnie generuje plot wokol obszaru budowlanego
2. Plot:
   - Segmenty co 3-4m wzdluz granic obszaru budowlanego
   - Slupki (Cylinder, wys. 2.5m) + siatka miedzy nimi (Quad z alpha cutout material)
   - Plot generowany automatycznie na podstawie rozmiaru z GroundGenerator
3. Brama gorna (od drogi):
   - Przerwa w plocie na gornej krawedzi (szer. ~4m)
   - Placeholder szlabanu/bramy (Box + Cylinder)
   - Pozycja: srodek gornej krawedzi obszaru budowlanego
4. Brama lewa (od torow):
   - Przerwa w plocie na lewej krawedzi (szer. ~8m — na szerokosc torow)
   - Placeholder bramy (dwa slupki + belka gorna)
   - Pozycja: lewa krawedz, na wysokosci torow
5. Plot automatycznie odswierza sie gdy obszar budowlany sie rozszerza (public metoda RegenerateFence)

**Gotowe gdy:** Widac metalowy plot wokol calego prostokatu budowlanego z dwiema bramami. Plot nie koliduje z torami w bramie.

---

### KROK 1.4: System torow + przycisk w toolbarze
**Referencja:** TODO punkt 4, 15.2
**Modyfikuje:** `PrefabTrackBuilder.cs`, `ExitTrackController.cs`, `ToolbarUI.cs`
**Tworzy:** `Assets/Scripts/Depot/TrackGraph.cs`

**Co zrobic:**

**A) Odblokuj przycisk "Postaw tor" w toolbarze:**
1. W `ToolbarUI.cs`: odblokuj przycisk toru (juz nie szary)
2. Klikniecie przycisku = `DepotUIManager.CurrentTool = ToolMode.BuildTrack`
3. Kursor zmienia sie na celownik/krzyzyk gdy narzedzie aktywne

**B) System torow:**
1. Przebuduj `PrefabTrackBuilder.cs`:
   - Reaguje na `DepotUIManager.CurrentTool == BuildTrack`
   - Gdy aktywne: klikniecie na siatke = poczatek toru, drugie klikniecie = koniec
   - Preview toru miedzy kliknięciami (polprzezroczysty)
   - Tory snap-uja do siatki 1x1m
   - Kazdy tor ma wlasciwosci: `trackId`, `trackName`, `hasCatenary` (domyslnie false)
   - Tory uzywaja istniejacego prefabu `szyna_podklad.fbx`
2. Przebuduj `ExitTrackController.cs`:
   - Tory zewnetrzne wychodza w LEWO (ujemny X) z bramy lewej
   - Tory wjazdowe/wyjazdowe lacza sie z torami wewnetrznymi przez rozjazdy
   - Uzyj prefabu `Rozjazd50mr300.fbx` w miejscach rozgalezien
3. Dodaj `TrackGraph.cs` — graf polaczen torow:
   - Kazdy tor to node, rozjazdy to edges
   - Metoda `FindPath(trackA, trackB)` — prosty pathfinding (A* lub BFS)
   - Kazdy edge ma flage `hasCatenary`
4. Stworz defaultowy layout:
   - 8 torow postojowych (rownolegych, wewnatrz obszaru budowlanego)
   - 1 tor wjazdowy/wyjazdowy (z lewej, przez brame, z rozjazdem laczacym z torami postojowymi)
   - Numeracja torow (1-8 + "wjazdowy")

**C) Pop-up toru (po kliknieciu narzedziem selekcji):**
1. Klikniecie na tor w trybie Select = pop-up z danymi:
   - Nazwa/numer toru (edytowalna)
   - Czy ma siec trakcyjna (ikona blyskawicy)
   - Przyciski: "Zmien nazwe", "Usun tor"
2. Stworz `Assets/Scripts/Depot/UI/TrackPopupUI.cs`

**Jak testowac:** Kliknij przycisk "tor" w toolbarze, kliknij na siatke — powinien sie pojawic tor. Przelacz na selekcje, kliknij na tor — pop-up z danymi.

**Gotowe gdy:** Mozna budowac tory z toolbara. Klikniecie na tor w trybie selekcji pokazuje pop-up. 8 torow + wjazdowy juz istnieja. Graf torow zwraca pathfinding.

---

### KROK 1.5: Siec trakcyjna + przycisk w toolbarze
**Referencja:** TODO punkt 5, 15.2
**Tworzy:** `Assets/Scripts/Depot/CatenarySystem.cs`
**Modyfikuje:** `ToolbarUI.cs`, `TrackPopupUI.cs`

**Co zrobic:**

**A) Odblokuj przycisk "Siec trakcyjna" w toolbarze:**
1. W `ToolbarUI.cs`: odblokuj przycisk sieci trakcyjnej
2. Klikniecie przycisku = `DepotUIManager.CurrentTool = ToolMode.BuildCatenary`

**B) System sieci trakcyjnej:**
1. Stworz `CatenarySystem.cs`:
   - Gdy `CurrentTool == BuildCatenary`: klikniecie na tor = dodanie sieci trakcyjnej
   - Klikniecie na tor z siecia = usuniecie sieci
   - Metoda `AddCatenary(TrackSegment track)`, `RemoveCatenary(TrackSegment track)`
   - Property `bool HasCatenary` na kazdym segmencie toru
2. Wizualizacja:
   - Slupy trakcyjne: proste Cylindry (wys. ~7m, srednicy 0.15m) + ramie (Box, dlug. ~2m)
   - Generowane proceduralnie co 50m wzdluz toru
   - Przewod jezdny: LineRenderer miedzy slupami na wysokosci ~5.5m
3. Integracja z TrackGraph:
   - Kazdy edge w grafie ma flage `hasCatenary`
   - Metoda `HasContinuousCatenary(path)` — sprawdza czy cala trasa ma siec
4. Domyslnie: dodaj siec trakcyjna na torach postojowych 1-4 (reszta bez sieci)

**C) Rozszerz pop-up toru:**
1. W `TrackPopupUI.cs` dodaj:
   - Ikona blyskawicy jesli tor ma siec trakcyjna
   - Przycisk "Dodaj siec trakcyjna" / "Usun siec trakcyjna"

**Jak testowac:** Kliknij przycisk sieci trakcyjnej w toolbarze, kliknij na tor 5 — powinny pojawic sie slupy i przewody. Kliknij jeszcze raz — siec znika. Kliknij na tor w trybie selekcji — pop-up pokazuje status sieci.

**Gotowe gdy:** Tory 1-4 maja widoczna siec trakcyjna. Mozna dodawac/usuwac siec z toolbara. Pop-up toru pokazuje status sieci.

---

### KROK 1.6: Spawn/wyjazd pociagow + panel boczny
**Referencja:** TODO punkt 9, 10.3, 15.3, 15.4
**Modyfikuje:** `VehicleController.cs`, `ExitTrackController.cs`
**Tworzy:**
- `Assets/Scripts/Depot/TrainSpawnSystem.cs`
- `Assets/Scripts/Depot/UI/TrainListPanelUI.cs`
- `Assets/Scripts/Depot/UI/TrainPopupUI.cs`

**Co zrobic:**

**A) System spawn/despawn:**
1. Stworz `TrainSpawnSystem.cs`:
   - Metoda `SpawnTrain(TrainType type, string targetTrack)`:
     - Tworzy instancje pociagu na torach zewnetrznych (z lewej, poza widokiem)
     - Pociag jedzie po torze wjazdowym do zajezdni
     - Zatrzymuje sie na wyznaczonym torze postojowym
     - Plynne hamowanie (ease-out)
   - Metoda `DespawnTrain(TrainInstance train)`:
     - Pociag rusza z toru postojowego
     - Jedzie na tor wjazdowy/wyjazdowy
     - Wyjezdza w lewo, znika za krawedzia
   - Kolejka wjazdowa: jesli brama zajeta, pociag czeka na torze zewnetrznym
2. Zmodyfikuj `VehicleController.cs`:
   - Przygotuj prefaby (na razie placeholder modele):
     - `TrainType.ElectricLocomotive` — lokomotywa elektryczna (Pociag.fbx)
     - `TrainType.DieselLocomotive` — lokomotywa spalinowa (Pociag.fbx z innym kolorem)
     - `TrainType.PassengerWagon` — wagon osobowy (Box placeholder)
   - Kazdy pojazd ma: `TractionType` (Electric/Diesel), `TrainState` (enum: Ready, InTransit, NeedsCleaning, NeedsWashing, NeedsInspection, InRepair, InWashbay, BeingShunted, Damaged)
3. Predkosc:
   - Na torach zajezdni: max 25 km/h (6.944 m/s)
   - Przez myjnie: max 5 km/h (1.389 m/s)
   - Na torach zewnetrznych: 40 km/h (11.11 m/s)
4. Logika trakcji:
   - Pojazd elektryczny moze jechac sam TYLKO po torach z HasCatenary=true
   - Pojazd spalinowy moze jechac po WSZYSTKICH torach
   - Jesli pojazd elektryczny musi jechac po torze bez sieci: komunikat w UI

**B) Panel boczny lewy — lista pociagow:**
1. Stworz `TrainListPanelUI.cs`:
   - Panel po lewej stronie ekranu (skladany/rozkladany)
   - Lista wszystkich pociagow w zajezdni
   - Kazdy wpis: ikona typu + nazwa + numer + stan (kolorowy tekst/ikona)
   - Ikona trakcji: blyskawica (elektryczny) lub silnik (spalinowy)
   - Klikniecie na wpis = selekcja pociagu + centrowanie kamery na nim
   - Przyciski przy kazdym wpisie: "Wyslij na tor", "Do myjni", "Do warsztatu"

**C) Pop-up pociagu:**
1. Stworz `TrainPopupUI.cs`:
   - Wyswietla sie po kliknieciu na pociag na scenie (tryb Select)
   - Dane: model, numer, stan techniczny (%), czystosc (%), aktualny tor, typ trakcji
   - Przyciski akcji: wyslij na tor, wyslij do myjni/warsztatu, odepnij wagon
   - Pop-up znika po kliknieciu w inne miejsce

**D) Spawn testowych pociagow na starcie:**
1. W `DepotManager.cs` lub osobnym skrypcie: na starcie spawaj 3-4 testowe pociagi:
   - Lok elektryczna na torze 1
   - Lok spalinowa (manewrowa) na torze 5
   - 2 wagony osobowe na torze 2

**Jak testowac:** Po uruchomieniu sceny widac pociagi na torach. Po lewej stronie panel z lista pociagow. Klikniecie na pociag w panelu = kamera leci do niego. Klikniecie na pociag na scenie = pop-up z danymi. Przycisk "Wyslij na tor" -> klikniecie na tor -> pociag jedzie.

**Gotowe gdy:** Pociagi stoja na torach, panel boczny je listuje, klikniecie na pociag otwiera pop-up, mozna wysylac pociagi na inne tory. Elektryczne nie jada po torach bez sieci.

---

### >>> CHECKPOINT FAZY 1 <<<
**Co powinno dzialac po Fazie 1:**
- Kamera orbitujaca z plynnym sterowaniem i granicami
- Pelny szkielet UI: gorny pasek (zegar, pieniadze), dolny toolbar, panel boczny z pociagiami
- Teren 2000x400m z siatka, droga, ogrodzenie z bramami, budynki tla
- 8 torow postojowych + tor wjazdowy z rozjazdem — budowalne z toolbara
- Siec trakcyjna nad torami 1-4 — budowalna z toolbara
- Pociagi na torach z pop-upami, panel boczny, wysylanie na tory
- Graf torow z pathfindingiem i logika trakcji

---

## FAZA 2 — BUDOWANIE

Cel: gracz moze budowac sciany, tworzyc pomieszczenia, wstawiac drzwi i okna — wszystko przez toolbar.

---

### KROK 2.1: System budowania scian + UI
**Referencja:** TODO punkt 6.1, 6.2, 15.2
**Tworzy:** `Assets/Scripts/Depot/WallBuildingSystem.cs`
**Modyfikuje:** `ToolbarUI.cs`

**Co zrobic:**

**A) Odblokuj przyciski w toolbarze:**
1. Odblokuj: "Buduj sciane", "Wyburz/usun"
2. Buduj sciane = `ToolMode.BuildWall`
3. Wyburz = `ToolMode.Demolish` (universalny — dziala na sciany, tory, chodniki, meble)

**B) System scian:**
1. Stworz `WallBuildingSystem.cs`:
   - Reaguje na `CurrentTool == BuildWall`
   - Klikniecie na siatke = poczatek sciany (snap do siatki 1x1m)
   - Przesuniecie myszy = preview sciany (polprzezroczysty Box, snap do 0/90/180/270)
   - Drugie klikniecie = potwierdzenie sciany
   - **TYLKO katy 0, 90, 180, 270** — sciany automatycznie snap-uja do najblizszego ortogonalnego kata
   - Sciana: Box Mesh, grubosc 0.2m, domyslna wysokosc 3m
   - Material: na razie jednolity kolor (szary beton)
2. Edycja scian (w trybie Select):
   - Klikniecie na sciane = selekcja (outline/highlight)
   - Drag konca = wydluzanie/skracanie (snap do siatki)
   - Drag srodka = przesuwanie rownolegle (snap do siatki)
   - Delete = usuwanie
3. Wyburzanie (w trybie Demolish):
   - Klikniecie na sciane = usuwanie (z potwierdzeniem — zmiana koloru na czerwony + drugie klikniecie)
4. Walidacja:
   - Sciana nie moze nachodzic na tory
   - Sciana nie moze nachodzic na inna sciane
   - Sciana musi byc wewnatrz obszaru budowlanego
   - Preview zmienia kolor na czerwony jesli pozycja jest nieprawidlowa
5. Dane sciany (serializowalne): `wallId`, `startPos` (Vector2Int), `endPos` (Vector2Int), `height`, `materialType`

**Jak testowac:** Kliknij przycisk "Sciana" w toolbarze. Kliknij na siatke — widac polprzezroczysty preview. Przesuniecie myszy = sciana rosnie. Drugie klikniecie = potwierdzenie. Przelacz na Selekcje — kliknij sciane — podswietla sie. Delete = usuwanie.

**Gotowe gdy:** Mozna budowac sciany z toolbara, sciany snap-uja do siatki 0/90/180/270. Mozna je wybierac, przesuwac, usuwac. Wyburzanie dziala.

---

### KROK 2.2: Drzwi i okna + UI
**Referencja:** TODO punkt 6.4, 15.2
**Modyfikuje:** `WallBuildingSystem.cs`, `ToolbarUI.cs`

**Co zrobic:**

**A) Odblokuj przyciski:**
1. Odblokuj: "Buduj drzwi", "Buduj okno"
2. Drzwi = `ToolMode.BuildDoor`, Okno = `ToolMode.BuildWindow`

**B) Drzwi:**
1. Gdy `CurrentTool == BuildDoor`:
   - Klikniecie na sciane = wstawienie drzwi (otwor 1m szer. x 2m wys.)
   - Drzwi snap-uja do siatki wzdluz sciany
   - Wizualnie: usun czesc mesha sciany i wstaw framuge (Box obramowanie)
   - Preview pozycji drzwi przed kliknieciem (polprzezroczysty marker na scianie)

**C) Okna:**
1. Gdy `CurrentTool == BuildWindow`:
   - Klikniecie na sciane = wstawienie okna (1m x 1m, na wys. 1m od podlogi)
   - Wizualnie: otwor w scianie z polprzezroczysta szyba

**D) Edycja i usuwanie:**
1. W trybie Select: klikniecie na drzwi/okno = selekcja, mozna drag-owac wzdluz sciany
2. W trybie Demolish: klikniecie na drzwi/okno = usuwanie (otwor sie zamyka)
3. Dane: kazda sciana ma liste `openings` z typem (door/window) i pozycja

**Jak testowac:** Kliknij "Drzwi" w toolbarze, najedz na sciane — widac preview. Kliknij — drzwi sie pojawiaja. To samo z oknami.

**Gotowe gdy:** Mozna wstawiac drzwi i okna w sciany z toolbara. Widac otwory. Mozna przesuwac i usuwac.

---

### KROK 2.3: Wykrywanie pomieszczen + pop-up typu
**Referencja:** TODO punkt 6.3, 6.5, 7, 15.7
**Tworzy:**
- `Assets/Scripts/Depot/RoomDetectionSystem.cs`
- `Assets/Scripts/Depot/UI/RoomTypePopupUI.cs`
- `Assets/Scripts/Depot/UI/BuildingPopupUI.cs`

**Co zrobic:**

**A) Wykrywanie pomieszczen:**
1. Stworz `RoomDetectionSystem.cs`:
   - Po kazdej zmianie scian skanuj zamkniete kontury
   - Algorytm: flood-fill na siatce 1x1m — jesli obszar jest zamkniety scianami = pokoj
   - Kazdy pokoj ma: `roomId`, `roomType` (enum: None, Workshop, Washbay, Storage, TicketOffice, DispatcherOffice, ResearchOffice, SocialRoom, Bathroom, Locker, Kitchen), `area` (m2), `bounds` (Rect)
2. Automatyczna podloga:
   - Wewnatrz zamknietego pokoju generuj Quad na Y=0.01 z materialem podlogi
   - BEZ DACHU — widac wnetrze z gory

**B) Pop-up przypisywania typu:**
1. Stworz `RoomTypePopupUI.cs`:
   - Wyswietla sie automatycznie gdy wykryto nowy zamkniety pokoj
   - Lista typow do wyboru (przyciski/dropdown): Warsztat, Myjnia, Magazyn, Punkt sprzedazy, Biuro dyspozytora, Biuro badan, Socjalne, Lazienka, Szatnia, Kuchnia
   - Przy kazdym typie: wymagany min. rozmiar
   - Jesli pokoj za maly na wybrany typ = czerwony tekst "Za maly! Wymaga min. XxYm"
   - Przycisk "Potwierdz"
   - Po potwierdzeniu: kolor podlogi zmienia sie wg typu

**C) Pop-up budynku/pomieszczenia:**
1. Stworz `BuildingPopupUI.cs`:
   - W trybie Select: klikniecie na podloge pokoju = pop-up z danymi:
     - Typ pomieszczenia, rozmiar (m2)
     - Lista pracownikow wewnatrz (na razie pusta)
     - Stan wyposazenia (na razie "brak mebli")
     - Przycisk "Zmien typ", "Wyburz pomieszczenie"

**D) Myjnia:**
1. Myjnia wymaga drzwi/otwarcia z dwoch przeciwnych stron (tory przejezdne)
2. Walidacja: jesli brak dwoch otwarc — komunikat w pop-upie

**Jak testowac:** Zbuduj 4 sciany tworzace prostokat. Automatycznie pojawia sie podloga + pop-up z wyborem typu. Wybierz "Warsztat" — podloga zmienia kolor. Kliknij na podloge w trybie Select — widac pop-up z danymi.

**Gotowe gdy:** Zamkniecie scian = automatyczny pokoj + pop-up typu. Pop-up budynku dziala. Walidacja rozmiaru dziala. Myjnia wymaga dwoch otwarc.

---

### >>> CHECKPOINT FAZY 2 <<<
**Co powinno dzialac po Fazie 2:**
- Budowanie scian z toolbara (nie z klawiatury!)
- Drzwi i okna z toolbara
- Wyburzanie z toolbara
- Automatyczne wykrywanie pomieszczen z pop-upem wyboru typu
- Pop-up budynku po kliknieciu
- Podlogi w pomieszczeniach, bez dachow

---

## FAZA 3 — WYPOSAZENIE

Cel: gracz moze meblowac pomieszczenia i budowac chodniki — przez UI.

---

### KROK 3.1: System meblowania + panel mebli
**Referencja:** TODO punkt 8, 15.2
**Tworzy:**
- `Assets/Scripts/Depot/FurniturePlacementSystem.cs`
- `Assets/Scripts/Depot/UI/FurniturePanelUI.cs`
**Modyfikuje:** `ToolbarUI.cs`

**Co zrobic:**

**A) Odblokuj przycisk "Meble" w toolbarze:**
1. Odblokuj przycisk Meble = `ToolMode.PlaceFurniture`
2. Klikniecie otwiera panel boczny z lista mebli

**B) Panel mebli:**
1. Stworz `FurniturePanelUI.cs`:
   - Panel boczny (prawy) — pojawia sie po wejsciu w tryb meblowania
   - Kategorie: Biuro dyspozytora, Biuro badan, Biuro ogolne, Socjalne, Lazienka, Szatnia, Kuchnia, Warsztat, Magazyn
   - Kazda kategoria rozwijana: lista mebli z ikonka i rozmiarem (np. "Biurko 2x1", "Krzeslo 1x1")
   - Klikniecie mebla w panelu = aktywacja stawiania tego mebla
   - Panel zamyka sie po wyjsciu z trybu meblowania (klikniecie innego narzedzia)

**C) System meblowania:**
1. Stworz `FurniturePlacementSystem.cs`:
   - Reaguje na `CurrentTool == PlaceFurniture` + wybrany mebel z panelu
   - Siatka 1x1m wewnatrz pokoju podswietlona
   - Preview mebla (polprzezroczysty) podazajacy za kursorem na siatce
   - Obracanie: klawisz R (0/90/180/270) — preview obraca sie
   - Klikniecie = postawienie mebla
   - Kolizje: mebel nie moze nachodzic na inny mebel, sciane, drzwi, okno
   - Mebel musi byc wewnatrz pokoju
   - Preview czerwony jesli pozycja nieprawidlowa
2. Meble jako prefaby:
   - Na razie kazdy mebel to kolorowy Box z odpowiednim rozmiarem
   - Stworz prefaby w `Assets/Prefabs/Furniture/`
   - Kazdy prefab ma komponent `FurnitureData`: `furnitureType`, `size` (Vector2Int), `allowedRoomTypes`
3. Edycja (w trybie Select):
   - Klikniecie na mebel = selekcja + highlight
   - Drag = przesuwanie (snap do siatki)
   - R = obrot
4. Usuwanie (w trybie Demolish): klikniecie na mebel = usuwanie

**Jak testowac:** Kliknij "Meble" w toolbarze. Otwiera sie panel boczny z lista. Wybierz "Biurko 2x1" — polprzezroczysty preview podaza za myszka w pokoju. R obraca. Klikniecie = postawienie.

**Gotowe gdy:** Panel mebli otwiera sie z toolbara. Mozna wybrac mebel, postawic na siatce, obracac, przesuwac, usuwac. Kolizje dzialaja.

---

### KROK 3.2: Chodniki + przycisk w toolbarze
**Referencja:** TODO punkt 12.1, 15.2
**Tworzy:** `Assets/Scripts/Depot/PedestrianPathSystem.cs`
**Modyfikuje:** `ToolbarUI.cs`

**Co zrobic:**

**A) Odblokuj przycisk "Chodnik" w toolbarze:**
1. Odblokuj przycisk = `ToolMode.BuildPath`

**B) System chodnikow:**
1. Stworz `PedestrianPathSystem.cs`:
   - Reaguje na `CurrentTool == BuildPath`
   - Klikniecie = poczatek chodnika, przesuniecie + klikniecie = koniec
   - Preview chodnika miedzy kliknieciem (polprzezroczysty)
   - Chodnik: plaski Quad na Y=0.02, szary material, szerokosc 2m
   - Tylko proste linie (0/90/180/270)
2. Graf chodnikow:
   - Kazdy chodnik to edge w grafie pieszych
   - Skrzyzowania tworza sie automatycznie (crossing nodes)
   - Metoda `FindPedestrianPath(Vector3 from, Vector3 to)` — pathfinding po sieci chodnikow
3. Walidacja:
   - Chodnik musi byc wewnatrz obszaru budowlanego
   - Chodnik nie moze nachodzic na budynki
   - Chodnik moze laczyc brame gorna z budynkami
4. Usuwanie: tryb Demolish + klikniecie na chodnik

**Jak testowac:** Kliknij "Chodnik" w toolbarze. Kliknij na siatke, przesuniecie, kliknij — chodnik pojawia sie. Przelacz na Demolish — kliknij na chodnik — znika.

**Gotowe gdy:** Mozna budowac chodniki z toolbara. Graf chodnikow dziala — pathfinding zwraca sciezke. Wyburzanie chodnikow dziala.

---

### >>> CHECKPOINT FAZY 3 <<<
**Co powinno dzialac po Fazie 3:**
- Meblowanie przez panel boczny z toolbara
- Chodniki z toolbara
- Wszystkie narzedzia budowania dostepne z toolbara (zadnych klawiszonow)
- Wyburzanie dziala na wszystko (sciany, drzwi, okna, meble, chodniki, tory)

---

## FAZA 4 — STEROWANIE I POSTACIE

Cel: pociagi poruszaja sie po torach, manewry dzialaja, pracownicy chodza, game loop dziala. UI rozszerzony o pop-upy pracownikow i okno obiegow.

---

### KROK 4.1: Sterowanie ruchem pociagow + tryby w UI
**Referencja:** TODO punkt 10.1, 10.2
**Tworzy:** `Assets/Scripts/Depot/DepotTrafficController.cs`
**Modyfikuje:** `TrainPopupUI.cs`, `TrainListPanelUI.cs`

**Co zrobic:**

**A) System sterowania:**
1. Stworz `DepotTrafficController.cs`:
   - Rejestr wszystkich pociagow w zajezdni
   - W trybie Select: klikniecie na pociag = selekcja, klikniecie na tor = wyslanie pociagu
   - Pathfinding po TrackGraph: pociag sam wybiera trase
   - Pathfinding uwzglednia siec trakcyjna (pojazdy elektryczne preferuja tory z siecia)
   - Automatyczne przelaczanie zwrotnic na trasie
   - Kolejka zadan per pociag: `MoveTo(tor) -> WaitAt(tor) -> MoveTo(myjnia)` itd.
   - Wizualizacja trasy: podswietlone tory po ktorych pojedzie pociag (po wybraniu celu)
2. Kolizje:
   - Pociagi nie moga wjechac na siebie (semafor per segment toru)
   - System rezerwacji torow: przed ruszeniem pociag rezerwuje cala trase

**B) Tryby sterowania w UI:**
1. Dodaj przelacznik w gornym pasku lub panelu bocznym: "Reczny" / "Automatyczny"
2. Reczny (domyslny): gracz klika kazdy ruch
3. Automatyczny: system sam wykonuje zaplanowane obiegi (na razie placeholder)
4. Mozliwosc hybrydowa: per-pociag override

**C) Rozszerz pop-up pociagu:**
1. W `TrainPopupUI.cs` dodaj:
   - Aktualny stan (kolorowa etykieta: Gotowy/Wymaga czyszczenia/W naprawie itd.)
   - Kolejka zadan (lista zaplanowanych ruchow)
   - Przycisk "Anuluj ruch" (jesli pociag jest w drodze)

**D) Rozszerz panel boczny:**
1. W `TrainListPanelUI.cs`:
   - Kolorowe kropki statusow obok kazdego pociagu
   - Filtrowanie: "Wszystkie" / "Gotowe" / "Wymagaja obslugi"

**Gotowe gdy:** Mozna kliknac pociag, kliknac tor, pociag jedzie (z wizualizacja trasy). Pop-up pokazuje stan i kolejke zadan. Przelacznik reczny/automatyczny w UI.

---

### KROK 4.2: System manewrow + UI
**Referencja:** TODO punkt 11
**Tworzy:** `Assets/Scripts/Depot/ShuntingSystem.cs`
**Modyfikuje:** `TrainPopupUI.cs`

**Co zrobic:**

**A) System manewrow:**
1. Stworz `ShuntingSystem.cs`:
   - Wagony w skladzie: lista polaczonych pojazdow
   - Wypinanie wagonu: oddziela wagon od skladu, wagon stoi w miejscu
   - Przepinanie: wymaga wolnej lokomotywy manewrowej (spalinowej)
   - Animacja: lokomotywa podjedza -> sprzegli -> jedzie -> rozsprzegli
2. Zestawianie skladu:
   - Walidacja: kompatybilnosc sprzegow, max dlugosc, typ trakcji
3. Kolejka manewrow: lista zaplanowanych przepinek

**B) UI manewrow:**
1. W `TrainPopupUI.cs` dodaj:
   - Przycisk "Odepnij wagon" (widoczny jesli pociag jest w skladzie z wagonami)
   - Po odepnieciu: wagon dostaje wlasny wpis w panelu bocznym
2. Stworz okienko "Zestawianie skladu":
   - Lista dostepnych lokomotyw i wagonow
   - Drag & drop: lokomotywa + wagony = sklad
   - Walidacja na zywo (czerwony tekst jesli niezgodnosc)
   - Przycisk "Zatwierdz sklad"
3. W panelu bocznym: ikona manewru (strzalki lewo-prawo) przy aktywnie manerowowanym taborze

**Gotowe gdy:** Mozna odepniac wagony przez pop-up. Lokomotywa manewrowa przepina wagony. Mozna zestawiac sklad przez UI.

---

### KROK 4.3: Pracownicy 3D + pop-up pracownika
**Referencja:** TODO punkt 12.2, 12.3, 15.3
**Tworzy:**
- `Assets/Scripts/Depot/WorkerSystem.cs`
- `Assets/Scripts/Depot/WorkerAI.cs`
- `Assets/Scripts/Depot/UI/WorkerPopupUI.cs`

**Co zrobic:**

**A) System pracownikow:**
1. Stworz `WorkerSystem.cs`:
   - Lista pracownikow z przypisanymi zmianami i rolami
   - System czasu: godziny w grze, zmiany (ranna, popoludniowa, nocna)
2. Stworz `WorkerAI.cs`:
   - Model: na razie Capsule z kolorem wg roli (czerwony=maszynista, niebieski=mechanik, zielony=sprzatacz, szary=biurowy)
   - Pathfinding po sieci chodnikow
   - State machine: Spawning -> GoToLocker -> GoToDispatcher -> GoToWork -> Break -> GoToWork -> GoToLocker -> Despawn
3. Cykl dnia (patrz TODO punkt 12.3)

**B) Pop-up pracownika:**
1. Stworz `WorkerPopupUI.cs`:
   - W trybie Select: klikniecie na pracownika = pop-up:
     - Imie, rola, ikona
     - Aktualny stan: w drodze / pracuje / przerwa / koniec zmiany (kolorowa etykieta)
     - Morale (%), zmeczenie (%) — paski postępu
     - Przypisana zmiana (godziny od-do)
     - Przypisane zadanie / pociag
   - Przycisk "Sledz" = kamera podaza za pracownikiem

**Jak testowac:** Poczekaj az zegar gry dojdzie do 6:00. Pracownicy (Capsule) pojawiaja sie przy bramie, ida chodnikami. Kliknij na pracownika — pop-up z danymi i stanem.

**Gotowe gdy:** Pracownicy pojawiaja sie, chodza chodnikami, wykonuja cykl dnia. Pop-up pracownika dziala. Mozna sledzic pracownika kamera.

---

### KROK 4.4: Game loop + okno obiegow
**Referencja:** TODO punkt 13, 15.5
**Tworzy:**
- `Assets/Scripts/Depot/DepotGameLoop.cs`
- `Assets/Scripts/Depot/UI/CirculationWindowUI.cs`

**Co zrobic:**

**A) Game loop:**
1. Stworz `DepotGameLoop.cs` — glowny orkiestrator:
   - Zegar gry: godzina, minuta, predkosc (1x, 2x, 3x, pauza) — sterowany z TopBarUI
   - Cykl dnia:
     - Rano: spawn pracownikow wg harmonogramu
     - W ciagu dnia: wyjazdy/przyjazdy pociagow wg obiegow
     - Wieczor: koniec zmian, despawn pracownikow
     - Noc: mniej ruchu, obsluga techniczna
   - Stan taboru: pociagi wracajace z trasy -> czyszczenie -> mycie -> gotowy
   - Integracja: WorkerSystem + DepotTrafficController + ShuntingSystem + TrainSpawnSystem
2. Placeholder schedule:
   - 6:00: 3 pracownikow przychodzi
   - 7:00: pociag wyjezdza na kurs
   - 14:00: pociag wraca -> wymaga czyszczenia
   - 22:00: pracownicy koncza zmiane

**B) Okno obiegow i zestawien:**
1. Stworz `CirculationWindowUI.cs`:
   - Otwierane przyciskiem w gornym pasku lub panelu bocznym
   - Zakladka "Zestawienia skladow":
     - Lista dostepnych pojazdow
     - Drag & drop: lokomotywa + wagony = sklad
     - Walidacja (kompatybilnosc, dlugosc, trakcja)
   - Zakladka "Obiegi":
     - Lista zaplanowanych kursow (na razie placeholder)
     - Przypisanie skladu do kursu
     - Wizualizacja obiegu (schemat)
   - Alerty:
     - "Za 30 min musi wyjechac sklad X na kurs Y" (powiadomienie w gornym pasku)
     - "Sklad nie jest gotowy!" (czerwony alert)
     - "Brak maszynisty!" (czerwony alert)

**C) System powiadomien:**
1. W `TopBarUI.cs` dodaj obszar powiadomien:
   - Kolorowe komunikaty przesuwajace sie (toast notifications)
   - Zolte = ostrzezenie, czerwone = blad, zielone = sukces
   - Np. "Pociag EN57 wrocil z trasy", "Brak wolnej lokomotywy manewrowej"

**Jak testowac:** Uruchom scene, ustaw predkosc 3x. Obserwuj: o 6:00 przychodza pracownicy, o 7:00 pociag wyjezdza, o 14:00 wraca, sprzatacz go czyści. Powiadomienia w gornym pasku informuja o wydarzeniach. Okno obiegow pozwala planowac.

**Gotowe gdy:** Pelny cykl dnia dziala z powiadomieniami w UI. Okno obiegow pozwala planowac zestawienia i obiegi. Alerty ostrzegaja o problemach.

---

### >>> CHECKPOINT FAZY 4 <<<
**Co powinno dzialac po Fazie 4:**
- Pelne sterowanie ruchem pociagow z UI (klik pociag -> klik tor, wizualizacja trasy)
- Manewry wagonami z pop-upow
- Pracownicy chodza po chodnikach z pop-upami
- Game loop: pelny dzien w petli z powiadomieniami
- Okno obiegow i zestawien
- Przelacznik reczny/automatyczny

---

## FAZA 5 — POLISH I ROZSZERZENIA

Cel: dekoracje, dokupowanie terenu, wiele zajezdni, porzadki, zapis.

---

### KROK 5.1: Tlo i dekoracje 3D
**Referencja:** TODO punkt 16
**Tworzy:** `Assets/Scripts/Depot/BackgroundGenerator.cs`

**Co zrobic:**
1. Zastap placeholder budynki tla z kroku 1.2 ladniejszymi modelami (rozne fasady)
2. Dodaj drzewa (cylinder + kula/stozek) wokol zajezdni
3. Dodaj latarnie przy drodze
4. Ambient na drodze: proste modele samochodow poruszajace sie w obie strony
5. Piesi na chodniku przy drodze (Capsule poruszajace sie)
6. Spawning/despawning poza kamera

**Gotowe gdy:** Scena wyglada ladnie — budynki tla, drzewa, ruch na drodze, latarnie.

---

### KROK 5.2: Dokupowanie terenu + UI
**Referencja:** TODO punkt 2 (rozszerzanie w prawo)

**Co zrobic:**
1. Przycisk na prawej krawedzi obszaru budowlanego "Kup teren (+200m) — 50 000 zl"
2. Pop-up potwierdzenia z cena
3. Po kliknieciu: obszar budowlany rosnie o 200m w prawo
4. Plot regeneruje sie (DepotFenceSystem.RegenerateFence)
5. Granice kamery sie aktualizuja (DepotOrbitCamera.UpdateBounds)
6. Siatka rozszerza sie
7. Koszt rosnie z kazda kupiona dzialka

**Gotowe gdy:** Przycisk widoczny na krawedzi, po kliknieciu teren rosnie, plot sie przebudowuje, kamera aktualizuje granice.

---

### KROK 5.3: Menu kontekstowe (prawy klik)
**Referencja:** TODO punkt 15.6
**Tworzy:** `Assets/Scripts/Depot/UI/ContextMenuUI.cs`

**Co zrobic:**
1. Prawy klik na roznych obiektach = menu kontekstowe:
   - Pusty teren: "Buduj sciane", "Postaw tor", "Postaw chodnik"
   - Sciana: "Info", "Wyburz", "Zmien material"
   - Tor: "Info", "Usun", "Zmien nazwe", "Dodaj/usun siec trakcyjna"
   - Pociag: "Wyslij na tor", "Info", "Do myjni", "Do warsztatu"
   - Pracownik: "Info", "Sledz"
   - Budynek: "Info", "Zmien typ", "Wyburz"
2. Menu znika po kliknieciu opcji lub kliknieciu poza menu

**Gotowe gdy:** Prawy klik na dowolnym obiekcie otwiera odpowiednie menu kontekstowe.

---

### KROK 5.4: Wiele zajezdni
**Referencja:** TODO punkt 14

**Co zrobic:**
1. Kazda zajezdnia = oddzielna instancja z wlasnym save file
2. Na mapie 2D: ikony zajezdni, klikniecie = ladowanie sceny 3D
3. Tabor moze jezdzic miedzy zajezdniami w ramach obiegow
4. Pracownicy przypisani do konkretnej zajezdni
5. Finanse wspolne
6. Limit: 1 na poczatku, kolejne odblokowane przez ulepszenia

---

### KROK 5.5: Zapis/wczytywanie + porzadki
**Referencja:** TODO rozdzial III, V

**Co zrobic:**
1. Serializacja stanu zajezdni (JSON lub binarny):
   - Wszystkie sciany, drzwi, okna z pozycjami
   - Wszystkie meble z pozycjami i obrotem
   - Wszystkie tory z siecia trakcyjna
   - Wszystkie chodniki
   - Stan taboru (pozycje, stany, przypisania)
   - Harmonogramy pracownikow
   - Rozmiar terenu (ile dokupiony)
   - Oddzielny plik per zajezdnia
2. Porzadki w kodzie:
   - Usun: `BuildingPlacer.cs`, `FreeCameraController.cs`, `DepotDemo.cs`, `TrackBuilder.cs`
   - Usun nieuzywany kod, TODO, debug logi
   - Upewnij sie ze namespace `DepotSystem` jest konsekwentny

**Gotowe gdy:** Mozna zapisac i wczytac stan zajezdni. Stare skrypty usuniete. Kod czysty.

---

### >>> CHECKPOINT FAZY 5 (FINAL) <<<
**Co powinno dzialac po Fazie 5:**
- Ladne tlo z budynkami, drzewami, ruchem na drodze
- Dokupowanie terenu z UI
- Menu kontekstowe (prawy klik)
- Obsluga wielu zajezdni
- Zapis/wczytywanie stanu
- Czysty kod, bez starych skryptow

---

# CZĘŚĆ 2: PEŁNA SPECYFIKACJA (TODO)

# TODO - Zajezdnia 3D (scena Depot)

Dokument opisuje wszystko co trzeba zrobic w scenie zajezdni 3D.
Bazuje na koncepcji ze screena, istniejacych skryptach i planie z Milestony.md (Grupa B: M2-M2.6).

---

## I. STAN OBECNY - co juz istnieje

### Skrypty (Assets/Scripts/Depot/):
- `DepotManager.cs` - orkiestrator, 8 torow postojowych, auto-placement budynkow
- `TrackBuilder.cs` - proceduralne tory (szyny 1435mm, podklady, podsypka, krzywe Beziera)
- `PrefabTrackBuilder.cs` - tory z FBX, tryb budowania (B), snap-to-grid 1x1m
- `BuildingPlacer.cs` - proceduralne budynki (stale rozmiary: warsztat 40x15x80m, myjnia 25x8x15m itd.)
- `VehicleController.cs` - spawning 4 typow (lokomotywa, wagon towarowy, osobowy, cysterna)
- `DepotVehicle.cs` - klikanie na pojazd, selekcja
- `DepotVehicleManager.cs` - klik pojazd -> klik tor = wyslanie
- `ExitTrackController.cs` - 3 tory wyjsciowe w lewo, despawn na krawedzi
- `GroundGenerator.cs` - teren 4000x600m, siatka 1x1m, major lines co 10
- `FreeCameraController.cs` - kamera WSAD/QE, scroll, prawy mysz = obrot
- `DepotRailwayIntegration.cs` - opcjonalny lacznik z mapa 2D
- `DepotDemo.cs` - demo 5-krokowe z GUI

### Modele 3D (Assets/Models/Depot/):
- `szyna_podklad.fbx` - segment toru 0.65m z teksturami (albedo, normal, AO, metallic)
- `Rozjazd50mr300.fbx` - rozjazd
- `Podklad.fbx`, `Szyna.fbx`, `Szyna1.fbx` - pojedyncze elementy
- `Pociag.fbx` - model pociagu z 22 teksturami

### Materialy:
- Podsypka_material.mat
- Tekstury trawy (Seamless Grass Textures)

---

## II. CO TRZEBA ZROBIC

Sekcje pogrupowane tematycznie:
- **A. Scena i infrastruktura** (1-5) - teren, kamera, tory, siec trakcyjna, ogrodzenie
- **B. Budowanie** (6-8) - sciany, pomieszczenia, meble
- **C. Tabor i ruch** (9-11) - spawn/despawn, sterowanie, manewry
- **D. Pracownicy** (12) - sciezki, zmiany, meldowanie
- **E. Game loop i systemy** (13-14) - codzienny cykl, wiele zajezdni
- **F. Interfejs uzytkownika** (15) - UI
- **G. Wizualia i otoczenie** (16) - tlo, dekoracje, ambient

---

### A. SCENA I INFRASTRUKTURA

---

### 1. KAMERA ORBITUJACA (zastepuje FreeCameraController)

**Problem:** Obecna kamera to FPS-style (WSAD + mysz). Potrzebna jest kamera orbitujaca jak w City Bus Manager - widok z gory/pod katem na zajezdnie z mozliwoscia obracania.

- [ ] Nowy skrypt `DepotOrbitCamera.cs`
- [ ] Obracanie kamery wokol punktu centralnego (srodek zajezdni)
- [ ] Lewy przycisk myszy przytrzymany = obrot (orbit) wokol punktu
- [ ] Prawy przycisk myszy przytrzymany = przesuwanie (pan) punktu centralnego
- [ ] Scroll = zoom (przyblizanie/oddalanie)
- [ ] Ograniczenia kata: min ~20 stopni od horyzontu, max ~85 stopni (prawie z gory)
- [ ] Ograniczenia zoomu: min ~5m, max ~300m od ziemi
- [ ] Gladkie przejscia (lerp/smooth damp)
- [ ] **Granice przesuwania - kamera TYLKO nad obszarem budowlanym + tlo**
  - [ ] Kamera nie moze wyjechac poza budynki tla (gracz nie moze zobaczyc co jest za nimi)
  - [ ] Granice ustawiaja sie dynamicznie przy dokupowaniu terenu w prawo
  - [ ] Margines na tlo z kazdej strony (np. 100-200m) ale nie dalej niz budynki 3D w tle
- [ ] Klawiatura: WSAD jako alternatywne przesuwanie (pan)
- [ ] Opcjonalnie: podwojne klikniecie na obiekt = kamera centruje sie na nim

---

### 2. TEREN I UKLAD SCENY

**Problem:** Obecny GroundGenerator tworzy plaski teren 4000x600m. Trzeba to dostosowac do layoutu ze screena.

- [ ] Teren glowny - zielona trawa jako tlo
- [ ] **Droga na gorze** - szary pas na gorze sceny, biegnie horyzontalnie (nieintegralna, tylko wizualna dekoracja)
- [ ] **Obszar budowlany** - prostokat otoczony plotem 3D
  - [ ] **Plot 3D** wokol calej zajezdni (metalowy/siatka ogrodzeniowa)
  - [ ] **Brama wjazdowa od drogi** (gora) - dla pracownikow i samochodow
  - [ ] **Brama wjazdowa od torow** (lewa strona) - dla pociagow
  - [ ] Siatka 1x1m widoczna TYLKO wewnatrz obszaru budowlanego
  - [ ] Podstawowy rozmiar: **2000m (X) x 400m (Z)**
  - [ ] **Dokupowanie terenu z prawej strony** - gracz moze rozszerzac zajezdnie w prawo za pieniadze
    - [ ] UI: przycisk "kup dodatkowy teren" na prawej krawedzi obszaru
    - [ ] Koszt rosnie z kazda kupiona dzialka
    - [ ] Plot automatycznie przesuwa sie po rozszerzeniu
    - [ ] Nowy teren od razu dostepny do budowania
- [ ] **Tory zewnetrzne (lewa strona)** - tory wjazdu/wyjazdu pociagow laczace sie z glowna siecia
  - [ ] ExitTrackController juz to czesciowo robi - dostosowac do layoutu
- [ ] **Tlo za obszarem budowlanym** - budynki 3D w tle (placeholder na razie)
  - [ ] Proste prostopadlosciany jako budynki tla
  - [ ] Rozne wysokosci i kolory
  - [ ] Nie interaktywne, tylko dekoracja
  - [ ] Umieszczone za plotem/granica zajezdni

---

### 3. OGRODZENIE I BRAMY

**Decyzja:** Cala zajezdnia otoczona plotem 3D z dwoma bramami.

- [ ] Nowy skrypt `DepotFenceSystem.cs`
- [ ] **Plot 3D** wokol calego obszaru budowlanego
  - [ ] Metalowa siatka ogrodzeniowa lub beton na dole + siatka na gorze
  - [ ] Slupki co 3-4m
  - [ ] Wysokosc ~2.5m
  - [ ] Plot automatycznie dopasowuje sie do rozmiaru zajezdni (w tym po dokupowaniu terenu)
- [ ] **Brama gorna (od drogi)** - wjazd dla pracownikow i pojazdow drogowych
  - [ ] Szlaban lub brama przesuwna
  - [ ] Polaczenie z chodnikiem prowadzacym od drogi
  - [ ] Pracownicy wchodza tedy na zajezdnie
- [ ] **Brama lewa (od torow)** - wjazd/wyjazd pociagow
  - [ ] Brama otwiera sie automatycznie gdy pociag sie zbliza
  - [ ] Tory przechodza przez brame
- [ ] Plot generowany proceduralnie wzdluz granic obszaru budowlanego
- [ ] Plot omija bramy i przejazdy torowe

---

### 4. SYSTEM TOROW WEWNATRZ ZAJEZDNI

**Problem:** Obecny system torow jest czesciowo gotowy (PrefabTrackBuilder + ExitTrackController), ale trzeba go zintegrowac z nowym layoutem.

- [ ] Tory postojowe - proste, rownolegle, wewnatrz obszaru budowlanego
- [ ] Tory wjazdowe/wyjazdowe - lacza tory postojowe z torami zewnetrznymi (lewa strona)
- [ ] Rozjazdy - automatyczne generowanie w miejscach rozgalezien
  - [ ] Prefab rozjazdu juz jest (Rozjazd50mr300.fbx)
  - [ ] Logika przelaczania zwrotnicy (gracz lub automatycznie)
- [ ] Tory w myjni - przejazdowe, wchodza i wychodza z budynku myjni
- [ ] Tory w warsztacie - koncowe, pociag wjezdza i stoi
- [ ] Snap torow do siatki 1x1m
- [ ] Wizualne polaczenia miedzy segmentami torow
- [ ] Numeracja torow (gracz moze nadawac nazwy/numery)

---

### 5. SIEC TRAKCYJNA

**Problem:** Pojazdy elektryczne (np. EU07, EP09, EN57) potrzebuja sieci trakcyjnej nad torami. Bez niej moga poruszac sie tylko ciagniete przez trakcje spalinowa.

#### 5.1 Budowanie sieci trakcyjnej
- [ ] Nowy skrypt `CatenarySystem.cs`
- [ ] **Dwa sposoby dodania sieci trakcyjnej:**
  - [ ] **Budowanie nad istniejacym torem** - gracz wybiera narzedzie "siec trakcyjna" i klika na tor -> tor zostaje ulepszony o siec trakcyjna
  - [ ] **Budowanie nowego toru od razu z siecia** - opcja w narzedziu budowania torow (checkbox "z siecia trakcyjna")
- [ ] Slupy trakcyjne co 50-60m wzdluz toru (proceduralnie generowane)
- [ ] Przewody jezdne miedzy slupami (linia/mesh nad torem, wysokosc ~5.5m)
- [ ] Wizualne oznaczenie torow z siecia (np. ikona blyskawicy na torze w UI, inny kolor podswietlenia)
- [ ] Mozliwosc usuwania sieci trakcyjnej z toru (tor wraca do wersji bez sieci)
- [ ] Koszt budowy sieci trakcyjnej (drozszy niz tor bez sieci)

#### 5.2 Logika trakcji i pojazdow
- [ ] Kazdy pojazd ma typ trakcji: **elektryczny**, **spalinowy**, **szynobus (spalinowy)**
- [ ] **Pojazd elektryczny:**
  - [ ] Moze poruszac sie TYLKO po torach z siecia trakcyjna (samodzielnie)
  - [ ] Na torze bez sieci trakcyjnej: NIE moze jechac o wlasnych silach
  - [ ] Moze byc **ciagniety przez lokomotywe spalinowa** po torach bez sieci (np. manewrowa SM42 ciagnie EN57 na tor bez sieci)
- [ ] **Pojazd spalinowy:**
  - [ ] Moze poruszac sie po WSZYSTKICH torach (z siecia i bez)
  - [ ] Kluczowa rola lokomotywy manewrowej spalinowej - moze manewrowac pojazdami elektrycznymi na torach bez sieci
- [ ] **Walidacja przy wysylaniu pociagu na tor:**
  - [ ] System sprawdza czy trasa od obecnego toru do docelowego ma ciagla siec trakcyjna (jesli pojazd elektryczny)
  - [ ] Jesli nie: komunikat "Brak sieci trakcyjnej na trasie - wymagana lokomotywa spalinowa do manewru"
  - [ ] Automatyczne przydzielenie lokomotywy manewrowej do przepchniecia (tryb automatyczny)
- [ ] **Pathfinding uwzglednia siec trakcyjna:**
  - [ ] Dla pojazdow elektrycznych: preferuje trasy z siecia trakcyjna
  - [ ] Jesli jedyna trasa prowadzi przez tor bez sieci: wymaga lokomotywy spalinowej

#### 5.3 Wizualizacja
- [ ] Slupy trakcyjne jako proste modele 3D (na razie cylinder + ramie)
- [ ] Przewod jezdny jako linia/cienki mesh nad torem
- [ ] Pantograf na pojazdach elektrycznych (podniesiony na torach z siecia, opuszczony na torach bez)
- [ ] Oznaczenie w panelu bocznym: ikona blyskawicy przy torach z siecia trakcyjna

---

### B. BUDOWANIE

---

### 6. SYSTEM BUDOWANIA SCIAN (NOWY - zastepuje BuildingPlacer)

**Problem:** Obecny BuildingPlacer stawia gotowe budynki o stalych rozmiarach. Nowy plan to w pelni proceduralne budowanie - gracz stawia kazda sciane osobno i moze je przesuwac.

#### 6.1 System scian
- [ ] Nowy skrypt `WallBuildingSystem.cs`
- [ ] Gracz wybiera narzedzie "postaw sciane" z UI
- [ ] Klikniecie na siatke = poczatek sciany
- [ ] Przesuniecie myszy + drugie klikniecie = koniec sciany
- [ ] Sciana snap-uje do siatki 1x1m (grubosc sciany np. 0.2m)
- [ ] **Tylko katy 0, 90, 180, 270 stopni** - bez scian pod dowolnym katem
- [ ] Sciany sa obiektami 3D (prostopadloscian) z materialem (cegla/metal/beton)
- [ ] Sciana ma domyslna wysokosc np. 3m (konfigurowalnie, bez dachu - widok z gory)
- [ ] Renderowanie sciany w czasie rzeczywistym podczas stawiania (preview)
- [ ] Walidacja: sciana nie moze nachodzic na tory ani inne obiekty

#### 6.2 Przesuwanie i edycja scian
- [ ] Klikniecie na istniejaca sciane = selekcja (highlight)
- [ ] Drag jednego konca sciany = przesuwanie/wydluzanie (snap do siatki)
- [ ] Drag calej sciany = przesuwanie rownolegle (snap do siatki)
- [ ] Klawisz Delete = usuwanie sciany
- [ ] Mozliwosc zmiany wysokosci wybranej sciany
- [ ] Mozliwosc zmiany materialu wybranej sciany

#### 6.3 Automatyczne wykrywanie pomieszczen
- [ ] Gdy sciany tworza zamkniety prostokat/wielokat = automatyczne wykrycie "pokoju"
- [ ] System nadaje pokojowi typ (poczatkowo brak, gracz przypisuje)
- [ ] Automatyczne generowanie podlogi wewnatrz zamknietego pokoju
- [ ] **BEZ DACHU** - budynki nie maja dachow, z kamery orbitujacej widac wnetrze z gory
- [ ] Sciany pelnia role wizualnej granicy pomieszczen (widoczne z gory jak plan budynku)

#### 6.4 Drzwi i okna
- [ ] Narzedzie "dodaj drzwi" - klikniecie na sciane = wstawienie drzwi (otwor w scianie)
- [ ] Narzedzie "dodaj okno" - klikniecie na sciane = wstawienie okna
- [ ] Drzwi/okna snap-uja do siatki na scianie
- [ ] Mozliwosc przesuwania drzwi/okien wzdluz sciany

#### 6.5 Typy budynkow/pomieszczen
Gracz po zbudowaniu scian i zamknieciu pomieszczenia przypisuje mu funkcje:
- [ ] **Warsztat** - naprawa pociagow, wymaga min. rozmiaru np. 10x5m
- [ ] **Myjnia** - mycie pociagow, wymaga min. 8x4m, otwarte z dwoch stron (tory przejezdne)
- [ ] **Magazyn czesci** - przechowywanie czesci zamiennych
- [ ] **Punkt sprzedazy biletow** - obsluga klientow
- [ ] Walidacja minimalnego rozmiaru dla kazdego typu
- [ ] Wizualne oznaczenie typu (ikona/kolor na mapie budynku)

---

### 7. BUDYNEK SEKCJI - pomieszczenia wewnetrzne

Budynek sekcji (biura, pomieszczenia socjalne) buduje sie tak samo jak inne budynki - sciany + meble. Wewnatrz gracz sam dzieli przestrzen na pomieszczenia:

- [ ] **Biuro dyspozytora** - centralne pomieszczenie gdzie pracownicy melduja sie na poczatek zmiany
  - [ ] Biurko dyspozytora z komputerem
  - [ ] Tablica/monitor z rozkladem zmian
  - [ ] Pracownicy przychodza tutaj na poczatku zmiany po instrukcje
- [ ] **Biuro badan/ulepszien** - stanowiska dla pracownikow badajacych ulepszenia
  - [ ] Stanowiska badawcze (biurko + komputer specjalistyczny)
  - [ ] Odblokowywanie nowych pojazdow
  - [ ] Badanie wiekszej automatyzacji zajezdni
  - [ ] Ulepszenia techniczne (szybsze naprawy, lepsza myjnia itp.)
  - [ ] Kazde stanowisko = 1 pracownik badawczy
- [ ] **Pomieszczenie socjalne/pokoj odpoczynku** - pracownicy odpoczywaja w przerwach
  - [ ] Kanapy/fotele, stol, ekspres do kawy, lodowka
  - [ ] Pracownicy regeneruja tu morale/energie
- [ ] **Lazienka** - toalety, umywalki, prysznice
  - [ ] Wymagana aby pracownicy nie tracili morale
  - [ ] Min. 1 toaleta na X pracownikow
- [ ] **Szatnia** - szafki pracownicze
  - [ ] Pracownicy przebrieraja sie tu na poczatku i koncu zmiany
  - [ ] Szafka na kazdy etat
- [ ] **Kuchnia/jadalnia** - stoly, krzesla, kuchenka, lodowka
  - [ ] Pracownicy jedza tu posilki w przerwie

---

### 8. MEBLE I WYPOSAZENIE WNETRZ (po siatce 1x1m)

**Problem:** Wewnatrz budynkow gracz ustawia meble i wyposazenie po siatce 1x1m.

#### 8.1 System meblowania
- [ ] Nowy skrypt `FurniturePlacementSystem.cs`
- [ ] Tryb meblowania - aktywuje sie po kliknieciu na budynek/pokoj
- [ ] Kamera wchodzi "do srodka" budynku (widok z gory na pokoj)
- [ ] Siatka 1x1m wewnatrz pokoju
- [ ] Panel boczny z lista dostepnych mebli/wyposazenia
- [ ] Drag & drop z panelu na siatke
- [ ] Obracanie mebli (R lub scroll)
- [ ] Kolizje - meble nie moga na siebie nachodzic
- [ ] Usuwanie mebli (prawy klik lub Delete)

#### 8.2 Lista mebli/wyposazenia

**Biuro dyspozytora:**
- [ ] Biurko dyspozytora (2x1) z komputerem
- [ ] Krzeslo biurowe (1x1)
- [ ] Tablica rozkladu zmian / monitor (na scianie, 1x1)
- [ ] Szafa/regal (1x1)

**Biuro badan/ulepszien:**
- [ ] Stanowisko badawcze (2x1) - biurko + komputer specjalistyczny
- [ ] Krzeslo (1x1)
- [ ] Regal z dokumentacja (1x1)
- [ ] Tablica/whiteboard (na scianie, 1x1)

**Biuro ogolne:**
- [ ] Biurko (1x1 lub 2x1)
- [ ] Krzeslo biurowe (1x1)
- [ ] Komputer (na biurku)
- [ ] Szafa/regal (1x1)
- [ ] Drukarka (1x1)

**Socjalne/pokoj odpoczynku:**
- [ ] Kanapa/fotel (2x1)
- [ ] Stolik kawowy (1x1)
- [ ] Ekspres do kawy (na blacie, 1x1)
- [ ] Telewizor (na scianie)

**Lazienka:**
- [ ] Toaleta (1x1)
- [ ] Umywalka (1x1)
- [ ] Prysznic (1x1)
- [ ] Suszarka do rak (na scianie)

**Szatnia:**
- [ ] Szafka pracownicza (1x1) - jedna na pracownika
- [ ] Lawka (2x1)

**Kuchnia/jadalnia:**
- [ ] Stol (2x1 lub 2x2)
- [ ] Krzeslo (1x1)
- [ ] Lodowka (1x1)
- [ ] Kuchenka/mikrofalowka (1x1)
- [ ] Zlew (1x1)

**Warsztat:**
- [ ] Stol roboczy/warsztatowy (2x1)
- [ ] Stojak na narzedzia (1x1)
- [ ] Podnosnik (2x2)
- [ ] Skrzynia z czesciami (1x1)

**Magazyn:**
- [ ] Regal magazynowy (1x2)
- [ ] Paleta z czesciami (1x1)
- [ ] Wozek transportowy (1x1)

Meble na razie moga byc prostymi ksztaltami 3D (boxy z kolorami), docelowo modele FBX.

---

### C. TABOR I RUCH

---

### 9. SPAWN I WYJAZD POCIAGOW

**Problem:** ExitTrackController czesciowo to robi (3 tory, despawn), ale trzeba pelny cykl.

- [ ] **Spawn (przybycie pociagu):**
  - [ ] Pociag pojawia sie na torach zewnetrznych (z lewej strony)
  - [ ] Jedzie po torze wjazdowym do zajezdni
  - [ ] Zatrzymuje sie na wyznaczonym torze postojowym
  - [ ] Animacja dojazdu (z daleka, plynnie zwalniajac)
- [ ] **Despawn (wyjazd pociagu):**
  - [ ] Pociag rusza z toru postojowego
  - [ ] Jedzie na tory wjazdowe/wyjazdowe
  - [ ] Wyjezdza w lewo na tory zewnetrzne
  - [ ] Znika za krawedzia mapy
  - [ ] Pojawia sie na mapie 2D (integracja z DepotRailwayIntegration)
- [ ] Spawning z mapy 2D - gdy pociag wraca z trasy, pojawia sie na torach zewnetrznych
- [ ] Kolejka wjazdowa - jesli wiele pociagow jednoczesnie wraca, czekaja na torze zewnetrznym

---

### 10. STEROWANIE RUCHEM POCIAGOW W ZAJEZDNI

**Problem:** DepotVehicleManager ma podstawy (klik pojazd -> klik tor), ale potrzeba pelnego systemu sterowania jak w City Bus Manager.

#### 10.1 Podstawy ruchu
- [ ] Nowy/rozbudowany `DepotTrafficController.cs`
- [ ] Panel z lista pociagow w zajezdni (po lewej stronie ekranu)
- [ ] Klikniecie na pociag w panelu = selekcja + podswietlenie na mapie
- [ ] Klikniecie na tor na mapie = wyslanie pociagu na ten tor
- [ ] System pathfindingu wewnatrz zajezdni:
  - [ ] A* lub prosty pathfinding po grafie torow zajezdni
  - [ ] Pociag sam wybiera droge od obecnego toru do docelowego
  - [ ] Automatyczne przelaczanie zwrotnic na trasie
  - [ ] Pathfinding uwzglednia siec trakcyjna (pojazdy elektryczne preferuja tory z siecia)
- [ ] Kolejka zadan - pociag moze miec zaplanowane: jedz na myjnie -> jedz na tor 5 -> czekaj
- [ ] Wizualizacja trasy (podswietlone tory po ktorych pojedzie pociag)
- [ ] Predkosc w zajezdni: **max 25 km/h**, przez myjnie: **max 5 km/h**
- [ ] Kolizje - pociagi nie moga wjechac na siebie, czekaja
- [ ] Animacja ruchu - plynne przejazdy z hamowaniem

#### 10.2 Tryby sterowania ruchem: reczny i automatyczny
- [ ] **Tryb reczny:**
  - [ ] Gracz klika na kazdy pociag/lokomotywe i recznie wskazuje na jaki tor ma jechac
  - [ ] Pelna kontrola nad kazdym ruchem w zajezdni
  - [ ] Gracz sam decyduje o kolejnosci i priorytetach
  - [ ] Przydatne przy skomplikowanych manewrach lub w sytuacjach awaryjnych
- [ ] **Tryb automatyczny:**
  - [ ] System sam kieruje pociagami na podstawie zaplanowanych obiegow (punkt 15.5)
  - [ ] Pociagi automatycznie jada na myjnie, do warsztatu, na tory postojowe wg harmonogramu
  - [ ] Automatyczne zestawianie skladow przed wyjazdem na kurs
  - [ ] Automatyczne przydzielanie torow postojowych
  - [ ] Gracz moze w kazdej chwili przejac kontrole reczna nad konkretnym pociagiem
- [ ] **Przelaczanie trybow:**
  - [ ] Przycisk w UI: "Reczny" / "Automatyczny"
  - [ ] Mozliwosc hybrydowa - czesc pociagow automatycznie, czesc recznie
  - [ ] Automatyka wymaga odpowiedniego poziomu ulepszenia "automatyzacja" z biura badan

#### 10.3 System stanow taboru
Kazdy pojazd/wagon w zajezdni ma status:
- [ ] **Gotowy** - oczekuje na torze postojowym, moze wyjechac na kurs
- [ ] **W trasie** - poza zajezdnia, na kursie (niewidoczny w 3D, widoczny na mapie 2D)
- [ ] **Wymaga czyszczenia** - po przybyciu z trasy, czeka na sprzatacza
- [ ] **Wymaga mycia** - brudny, czeka na myjnie
- [ ] **Wymaga przegladu** - po X km wymaga przegladu technicznego
- [ ] **W naprawie** - w warsztacie, mechanik pracuje nad nim
- [ ] **W myjni** - przejezdza przez myjnie
- [ ] **Manewrowany** - lokomotywa manewrowa go przenosi
- [ ] **Uszkodzony** - niesprawny, wymaga naprawy (nie moze wyjechac na kurs)
- [ ] UI: kolorowe oznaczenia stanow w panelu bocznym i na pop-upach

---

### 11. SYSTEM MANEWROW WAGONAMI

Kazdy wagon moze byc wypiety z jednego skladu i przepiety do innego. Do manewrow ZAWSZE potrzebna jest lokomotywa manewrowa:

- [ ] **Wypinanie wagonu:**
  - [ ] Klikniecie na wagon w skladzie -> pop-up -> przycisk "odepnij"
  - [ ] Wagon zostaje na torze, reszta skladu sie odsuwa (lub wagon jest odciagany)
  - [ ] Odpiety wagon nie moze sie sam poruszac - stoi w miejscu
- [ ] **Lokomotywa manewrowa:**
  - [ ] Do kazdego manewru (przepiecie, przestawienie wagonu) potrzebna jest lokomotywa
  - [ ] Gracz musi posiadac lokomotywe manewrowa (np. SM42) lub uzyc wolnej lokomotywy
  - [ ] Lokomotywa podjedza do wagonu, podepnie go, przewiezie na docelowy tor i odepnie
  - [ ] Animacja: podjazd lokomotywy -> sprzegniecie -> jazda -> rozsprzegniecie
- [ ] **Przepinanie wagonu do innego skladu:**
  - [ ] Gracz wskazuje wagon do przepiecia i docelowy sklad/tor
  - [ ] System sprawdza czy jest wolna lokomotywa manewrowa
  - [ ] Jesli tak: lokomotywa wykonuje manewr automatycznie (pathfinding)
  - [ ] Jesli nie: komunikat "Brak wolnej lokomotywy manewrowej"
- [ ] **Zestawianie nowego skladu:**
  - [ ] Gracz wybiera lokomotywe + wagony (z pop-upow lub z panelu obiegow)
  - [ ] Lokomotywa manewrowa kolejno sciaga wagony z roznych torow i ustawia je w sklad
  - [ ] Walidacja skladu: kompatybilnosc sprzegow, max dlugosc, typ trakcji
- [ ] **Wizualizacja manewrow:**
  - [ ] Podswietlenie trasy manewru na torach
  - [ ] Ikona lokomotywy manewrowej na mapie
  - [ ] Kolejka manewrow - lista zaplanowanych ruchow

---

### D. PRACOWNICY

---

### 12. PRACOWNICY I SCIEZKI

**Problem:** Pracownicy musza chodzic po zajezdni - od bramy do budynku sekcji, od sekcji do warsztatu itp.

#### 12.1 Sciezki/chodniki
- [ ] Nowy skrypt `PedestrianPathSystem.cs`
- [ ] Gracz buduje sciezki/chodniki po siatce (analogicznie do scian)
- [ ] Chodniki to plaskie prostokaty na ziemi (szary kolor, material chodnikowy)
- [ ] **Pracownicy jako widoczne postacie 3D** poruszajace sie po sciezkach
- [ ] Pathfinding pracownikow po sieci chodnikow
- [ ] Pracownicy musza dojsc z bramy wjazdowej (od drogi) do miejsc pracy
- [ ] Brak chodnika = pracownicy chodza wolniej (po trawie) lub nie moga dojsc
- [ ] Animacja chodzenia (idle, walk, work, sit)

#### 12.2 System zmian i meldowanie u dyspozytora
Pracownicy maja przypisane zmiany (konfigurowane w osobnym oknie/pop-upie):
- [ ] **Okno/pop-up planowania zmian:**
  - [ ] Lista pracownikow z przypisanymi zmianami (ranna, popoludniowa, nocna)
  - [ ] Godziny rozpoczecia i zakonczenia zmiany
  - [ ] Przypisanie pracownika do konkretnego zadania/pociagu

#### 12.3 Cykl dnia pracownika
- [ ] **Cykl dnia pracownika:**
  1. Pracownik pojawia sie przy gornej bramie (od drogi) o wyznaczonej godzinie
  2. Idzie sciezka do **szatni** - przebiera sie
  3. Idzie do **biura dyspozytora** - melduje sie na poczatek zmiany
  4. Dyspozytor przydziela zadanie (lub jest ono juz zaplanowane)
  5. Pracownik idzie do przypisanego miejsca pracy:
     - **Maszynista/konduktor/kierownik pociagu** -> idzie sciezka do przypisanego pociagu na torze
     - **Mechanik** -> idzie do warsztatu, przypisany do naprawy konkretnego pociagu
     - **Sprzatacz** -> idzie do przypisanego wagonu/pociagu do sprzatania
     - **Pracownik myjni** -> idzie do myjni
     - **Pracownik biurowy/badawczy** -> idzie do swojego biurka
     - **Kasjer** -> idzie do punktu sprzedazy biletow
  6. Przerwa -> idzie do pomieszczenia socjalnego/kuchni
  7. Powrot do pracy
  8. Koniec zmiany -> idzie do szatni -> wychodzi brama gorna
- [ ] Wizualne wykonywanie pracy:
  - [ ] Maszynista siedzi w kabinie pociagu
  - [ ] Mechanik pracuje przy pociagu w warsztacie (animacja z narzedziami)
  - [ ] Sprzatacz sprzata wewnatrz wagonu
  - [ ] Biurowi siedza przy biurkach
  - [ ] Kasjerzy siedza w punkcie sprzedazy

---

### E. GAME LOOP I SYSTEMY

---

### 13. GLOWNY GAME LOOP ZAJEZDNI

**Problem:** Zajezdnia to centralny element gry. Trzeba zdefiniowac pelny cykl dzialania zajezdni - od budowy do codziennej eksploatacji.

#### 13.1 Etap budowy (jednorazowy, potem rozbudowa)
- [ ] Gracz buduje zajezdnie od zera:
  1. Stawia tory (postojowe, wjazdowe/wyjazdowe, rozjazdy)
  2. Buduje budynki (sciany, pomieszczenia, meble)
  3. Buduje sciezki/chodniki dla pracownikow
  4. Opcjonalnie: buduje siec trakcyjna nad wybranymi torami
- [ ] Zajezdnia moze byc rozbudowywana w dowolnym momencie (nowe tory, nowe budynki, dokupienie terenu)

#### 13.2 Zakup taboru (powiazane z innymi systemami)
- [ ] Gracz kupuje tabor w osobnym ekranie/pop-upie (nie bezposrednio w zajezdni)
- [ ] Zakupiony tabor przybywa na zajezdnie torami zewnetrznymi (spawn z lewej)
- [ ] AI automatycznie podstawia nowo zakupiony tabor na wolny tor postojowy

#### 13.3 Planowanie pociagow i pracownikow (poza zajezdnia, ale powiazane)
- [ ] Gracz planuje rozklady jazdy na mapie 2D (nie w scenie zajezdni)
- [ ] Gracz zatrudnia i planuje zmiany pracownikow w osobnym oknie HR
- [ ] Te plany przekladaja sie na dzialanie zajezdni (obiegi, zestawienia, harmonogramy zmian)

#### 13.4 Codzienny cykl zajezdni (game loop)
Powtarza sie codziennie w grze, 24/7:

**Rano / poczatek zmiany:**
- [ ] Pracownicy pojawiaja sie przy gornej bramie (od drogi) zgodnie z zaplanowanym harmonogramem zmian
- [ ] Ida sciezka do szatni -> przebieraja sie
- [ ] Ida do biura dyspozytora -> melduja sie na poczatek zmiany
- [ ] Dyspozytor (automatycznie lub gracz recznie) przydziela zadania:
  - Maszynisci/konduktorzy/kierownicy -> przypisani do konkretnych pociagow
  - Mechanicy/technicy -> przypisani do napraw/przegladow w warsztacie
  - Sprzatacze -> przypisani do czyszczenia konkretnych wagonow
  - Pracownicy myjni -> obsluga myjni
  - Biurowi/badawczy -> praca przy biurkach
- [ ] Pracownicy ida sciezkami do wyznaczonych miejsc pracy

**W ciagu dnia - eksploatacja taboru:**
- [ ] **Wyjazdy pociagow:**
  - [ ] Na podstawie zaplanowanych obiegow taborowych, pociagi musza byc zestawione i gotowe do wyjazdu
  - [ ] Gracz (lub automatyka) zestawia sklady: lokomotywa + wagony na torze
  - [ ] Maszynista i konduktor melduja sie przy pociagu
  - [ ] Gotowy pociag wyjezdza torami zewnetrznymi z zajezdni (w lewo)
  - [ ] System podpowiedzi: "Za 30 min musi wyjechac sklad X na kurs Y"
- [ ] **Przyjazdy pociagow:**
  - [ ] Pociagi po zakonczonych trasach przyjezdzaja z powrotem do zajezdni (spawn z lewej)
  - [ ] AI podstawia je na wybrany przez usera tor (lub automatycznie na wolny)
  - [ ] W zaleznosci od eksploatacji, pociag wymaga obslugi:
    - **Czyszczenie** - sprzatacz idzie do wagonu/pociagu i sprzata
    - **Mycie** - pociag jest manewrowany na tor myjni, przejezdza przez myjnie (5 km/h)
    - **Przeglad techniczny** - mechanik sprawdza stan techniczny w warsztacie
    - **Naprawa** - jesli cos jest uszkodzone, mechanik naprawia w warsztacie
    - **Tankowanie/ladowanie** - uzupelnienie paliwa lub ladowanie akumulatorow
  - [ ] Tabor ktory czegos wymaga stoi na torach w zajezdni i jest obslugiwany przez pracownikow technicznych
- [ ] **Manewry wewnetrzne:**
  - [ ] Lokomotywy manewrowe przestawiaja wagony miedzy torami
  - [ ] Zestawianie i rozsprzeganie skladow wg zaplanowanych obiegow taborowych
  - [ ] Wypelnianie zaplanowanych obiegow: wagon A z Kursu 1 idzie do Kursu 3 itp.
- [ ] **Praca pracownikow:**
  - [ ] Pracownicy technicy (mechanicy, sprzatacze, pracownicy myjni) tez musza sie przed sluzba zglosic do dyspozytora
  - [ ] Dyspozytor przydziela im konkretne zadania (ktory pociag naprawic, ktory wyczyscic)
  - [ ] Pracownicy wykonuja zadania, robia przerwy (socjalne/kuchnia), wracaja do pracy
  - [ ] Pracownicy biurowi/badawczy pracuja w swoich biurach (badania, ulepszenia)

**Koniec zmiany / wieczor:**
- [ ] Pracownicy konczacy zmiane ida do szatni -> przebieraja sie
- [ ] Wychodza gorna brama z zajezdni
- [ ] Nastepna zmiana pracownikow (jesli zaplanowana) juz przychodzi i przejmuje obowiazki

**Noc:**
- [ ] Zajezdnia dziala 24/7 (jesli sa zaplanowane zmiany nocne)
- [ ] Nocne kursy wyjezdzaja i przyjezdzaja normalnie
- [ ] Mniej ruchu, ale obsluga techniczna moze pracowac noca (naprawy, czyszczenie)

**Nastepny dzien:**
- [ ] Cykl powtarza sie od nowa
- [ ] Pociagi ktore noca nie zdazyly zostac obsluzone - kontynuuja obsluge rano
- [ ] Nowe kursy wg rozkladu jazdy

---

### 14. WIELE ZAJEZDNI ROWNOLEGLE

**Problem:** Gracz moze potrzebowac wiecej niz jednej zajezdni - np. jedna w Warszawie, druga w Krakowie.

- [ ] **Tworzenie nowych zajezdni:**
  - [ ] Kazda zajezdnia to oddzielna scena/instancja Depot
  - [ ] Gracz kupuje dzialke pod nowa zajezdnie na mapie 2D (w poblizu torow)
  - [ ] Nowa zajezdnia zaczyna sie od pustego ogrodzonego terenu
- [ ] **Przelaczanie miedzy zajezdniami:**
  - [ ] Na mapie 2D widoczne sa wszystkie zajezdnie gracza (ikony)
  - [ ] Klikniecie na ikone zajezdni = przelaczenie do sceny 3D tej zajezdni
  - [ ] Przycisk "powrot do mapy" w gornym pasku
- [ ] **Niezalezne dzialanie:**
  - [ ] Kazda zajezdnia ma wlasne tory, budynki, pracownikow, tabor
  - [ ] Kazda zajezdnia dziala niezaleznie (swoj cykl dzienny, swoi pracownicy)
  - [ ] Zajezdnie moga "wymieniac" tabor - pociag wyjezdza z zajezdni A, jedzie trasa i przyjezdza do zajezdni B
- [ ] **Tabor miedzy zajezdniami:**
  - [ ] Pociag moze byc przypisany do jednej zajezdni (bazowa)
  - [ ] Obieg taborowy moze obejmowac przejazd miedzy zajezdniami (np. poranny kurs z Warszawy do Krakowa i powrot wieczorem)
  - [ ] Jesli pociag konczy kurs w miescie z inna zajezdnia gracza - moze tam zanocowac/byc obsluzony
- [ ] **Wspoldzielenie zasobow:**
  - [ ] Pracownicy sa przypisani do konkretnej zajezdni (nie przenosza sie miedzy nimi automatycznie)
  - [ ] Czesci zamienne/magazyn - oddzielny dla kazdej zajezdni
  - [ ] Finanse - wspolne (jedna firma, wiele zajezdni)
- [ ] **Limit zajezdni:**
  - [ ] Poczatkowo gracz moze miec 1 zajezdnie
  - [ ] Odblokowanie kolejnych zajezdni przez rozwoj firmy / ulepszenia
  - [ ] Max liczba zajezdni ograniczona (np. 5-10) ze wzgledu na wydajnosc

---

### F. INTERFEJS UZYTKOWNIKA

---

### 15. INTERFEJS UZYTKOWNIKA (UI)

**Problem:** Brak jakiegokolwiek UI. Trzeba stworzyc od zera.

#### 15.1 Gorny pasek (Top Bar)
- [ ] Nazwa zajezdni (edytowalna)
- [ ] Godzina w grze + kontrola predkosci (1x, 2x, 3x, pauza)
- [ ] Saldo pieniedzy
- [ ] Przycisk powrotu do mapy 2D

#### 15.2 Dolny pasek narzedziowy (Bottom Toolbar)
Ikony narzedziowe do budowania:
- [ ] Narzedzie selekcji (strzalka) - domyslne
- [ ] Buduj sciane
- [ ] Buduj drzwi/okno
- [ ] Postaw tor
- [ ] Buduj siec trakcyjna
- [ ] Postaw chodnik
- [ ] Postaw meble (otwiera panel mebli)
- [ ] Wyburz/usun
- [ ] Obroc (R)

#### 15.3 Pop-upy po kliknieciu na obiekty (kazdy obiekt klikalny)
Kazdy pracownik, pociag, wagon, budynek jest klikalny i wyswietla pop-up z aktualnymi danymi:
- [ ] **Pop-up pociagu/wagonu:**
  - [ ] Model, numer identyfikacyjny
  - [ ] Stan techniczny (%), czystosc (%)
  - [ ] Aktualny tor / pozycja
  - [ ] Przypisany obieg/kurs (jesli jest)
  - [ ] Przypisany maszynista (jesli jest)
  - [ ] Lista wagonow w skladzie (jesli lokomotywa)
  - [ ] Przebieg (km), data ostatniego przegladu
  - [ ] Typ trakcji (elektryczny/spalinowy) + ikona
  - [ ] Przyciski akcji: wyslij na tor, wyslij do myjni/warsztatu, odepnij wagon
- [ ] **Pop-up pracownika:**
  - [ ] Imie, nazwisko, rola
  - [ ] Aktualny stan: w drodze / pracuje / przerwa / koniec zmiany
  - [ ] Morale (%), zmeczenie (%)
  - [ ] Przypisana zmiana (godziny)
  - [ ] Przypisane zadanie / pociag
  - [ ] Wynagrodzenie, poziom umiejetnosci
- [ ] **Pop-up budynku/pomieszczenia:**
  - [ ] Typ pomieszczenia, rozmiar
  - [ ] Lista pracownikow wewnatrz
  - [ ] Stan wyposazenia
  - [ ] Pojemnosc (ile stanowisk / ile osob moze byc)

#### 15.4 Panel boczny lewy - lista pociagow
- [ ] Lista wszystkich pociagow w zajezdni
- [ ] Ikona + nazwa + numer
- [ ] Stan: postojowy / w ruchu / w naprawie / w myjni
- [ ] Typ trakcji (ikona blyskawicy lub ikona silnika)
- [ ] Klikniecie = selekcja i centrowanie kamery
- [ ] Przycisk "wyslij na tor" / "wyslij do myjni" / "wyslij do warsztatu"

#### 15.5 Okno obiegow i zestawien skladow (osobny ekran/pop-up)
Gracz planuje obiegi i zestawienia skladow w osobnym oknie, na podstawie ktorych wysyla pociagi na tory zewnetrzne:
- [ ] **Ekran/pop-up zestawien skladow:**
  - [ ] Lista dostepnych pojazdow w zajezdni
  - [ ] Drag & drop: lokomotywa + wagony = sklad
  - [ ] Walidacja skladu (kompatybilnosc, max dlugosc, wymagania trakcyjne)
  - [ ] Przypisanie skladu do rozkladu/obiegu
- [ ] **Ekran/pop-up obiegow:**
  - [ ] Lista zaplanowanych kursow (z systemu rozkladow jazdy na mapie 2D)
  - [ ] Przypisanie skladu do kursu
  - [ ] Laczenie kursow w obiegi (Kurs A -> Kurs B -> powrot do zajezdni)
  - [ ] Wizualizacja obiegu (schemat graficzny)
  - [ ] Sprawdzanie czasu pomiedzy kursami (czy sklad zdazy)
- [ ] **Powiazanie z zajezdnia:**
  - [ ] Na podstawie zaplanowanego obiegu gracz musi:
    - [ ] Zestawic odpowiedni sklad (lokomotywa + wagony) na torze
    - [ ] Wyslac zestawiony sklad na tory zewnetrzne w odpowiednim czasie
  - [ ] System podpowiedzi: "Za 30 min musi wyjechac sklad EN57 na kurs Warszawa-Lodz"
  - [ ] Ostrzezenie gdy sklad nie jest gotowy na czas
  - [ ] Ostrzezenie gdy brak maszynisty/konduktora przypisanego do kursu

#### 15.6 Menu kontekstowe (prawy klik)
- [ ] Na pustym terenie: opcje budowania
- [ ] Na budynku: info, wyburz, zmien typ
- [ ] Na torze: info, usun, zmien nazwe, dodaj/usun siec trakcyjna
- [ ] Na pociagu: wyslij, info, przypisz do rozkladu

#### 15.7 Okno przypisywania funkcji pokoju
- [ ] Otwiera sie po zamknieciu pomieszczenia scianami
- [ ] Lista typow: warsztat, myjnia, biuro, socjalne, magazyn, punkt sprzedazy
- [ ] Podswietlenie wymagan (min. rozmiar, potrzebne wyposazenie)
- [ ] Przycisk potwierdz

---

### G. WIZUALIA I OTOCZENIE

---

### 16. TLO I DEKORACJE 3D

**Problem:** Za obszarem budowlanym powinna byc dekoracja - budynki 3D, drzewa itp.

- [ ] `BackgroundGenerator.cs` - generowanie tla
- [ ] Proste budynki 3D wokol zajezdni (prostopadlosciany z oknami)
  - [ ] Rozne wysokosci (5-30m)
  - [ ] Rozne kolory/materialy (cegla, beton, szklo)
  - [ ] Losowe rozmieszczenie za granica zajezdni
- [ ] Drzewa (proste ksztalty - cylinder + stozek/kula, zielony)
- [ ] Latarnie/slupy (przy drodze na gorze)
- [ ] **Droga na gorze z ruchem ambient** - samochody i piesi poruszajacy sie po drodze
  - [ ] Proste modele samochodow (rozne kolory, rozne typy)
  - [ ] Piesi na chodniku przy drodze
  - [ ] Ruch w obie strony
  - [ ] Spawning/despawning poza kamera
- [ ] Niebo/skybox - prosty gradient lub Unity skybox
- [ ] Oswietlenie - directional light jako slonce, cien

---

## III. PORZADKI W ISTNIEJACYM KODZIE

**Problem:** Istniejace skrypty trzeba czesc przebudowac, czesc usunac, czesc zostawic.

### Do przebudowy:
- [ ] `DepotManager.cs` - usunac auto-placement budynkow, dodac obsluge nowego systemu budowania
- [ ] `PrefabTrackBuilder.cs` - zintegrowac z nowym layoutem i UI (zamiast klawisza B)
- [ ] `ExitTrackController.cs` - dostosowac do nowego layoutu (tory z lewej)
- [ ] `DepotVehicleManager.cs` - rozbudowac o panel UI zamiast prostego klik-klik

### Do wyrzucenia/zastapienia:
- [ ] `BuildingPlacer.cs` - zastapiony przez WallBuildingSystem
- [ ] `FreeCameraController.cs` - zastapiony przez DepotOrbitCamera
- [ ] `DepotDemo.cs` - tymczasowy, do wyrzucenia gdy bedzie UI
- [ ] `TrackBuilder.cs` - zastapiony przez PrefabTrackBuilder (uzywamy FBX)

### Do zostawienia:
- [ ] `GroundGenerator.cs` - dostosowac rozmiar do 2000x400m + margines na tlo, siatka tylko w obszarze budowlanym, obsluga rozszerzania terenu w prawo
- [ ] `DepotVehicle.cs` - OK, selekcja pojazdow
- [ ] `DepotRailwayIntegration.cs` - OK, integracja z mapa 2D
- [ ] `VehicleController.cs` - potrzebny, ale zamiast proceduralnych modeli uzyc FBX

---

## IV. KOLEJNOSC IMPLEMENTACJI (PRIORYTET)

**Faza 1 - Fundament (najpierw to):**
1. Kamera orbitujaca (punkt 1)
2. Uklad sceny - teren 2000x400, droga, ogrodzenie z bramami (punkt 2, 3)
3. System torow wewnatrz (punkt 4) - bo bez torow nie ma zajezdni
4. Siec trakcyjna nad torami (punkt 5) - razem z torami, bo wplywa na logike pojazdow
5. Spawn/despawn pociagow (punkt 9)

**Faza 2 - Budowanie:**
6. System budowania scian 0/90/180/270 (punkt 6)
7. Drzwi i okna (punkt 6.4)
8. Wykrywanie pomieszczen bez dachow i przypisywanie typow (punkt 6.3, 6.5)

**Faza 3 - Wyposazenie:**
9. Meble i wyposazenie wnetrz po siatce 1x1 (punkt 8)
10. Sciezki dla pracownikow (punkt 12)

**Faza 4 - Sterowanie i postacie:**
11. Sterowanie ruchem pociagow w zajezdni z uwzglednieniem sieci trakcyjnej (punkt 10)
12. System manewrow wagonami (punkt 11)
13. Pracownicy 3D - modele, animacje, pathfinding (punkt 12)
14. Glowny game loop zajezdni - pelny cykl dzienny (punkt 13)

**Faza 5 - UI i polish:**
15. Interfejs uzytkownika (punkt 15)
16. Tlo i dekoracje 3D + ambient na drodze (punkt 16)
17. Dokupowanie terenu w prawo (punkt 2)
18. Obsluga wielu zajezdni rownoczesnie (punkt 14)
19. Porzadki w kodzie (rozdzial III)

---

## V. SZCZEGOLY TECHNICZNE

- **Rozmiar podstawowy:** 2000m (X) x 400m (Z), rozszerzalny w prawo
- **Siatka budowlana:** 1x1m, snap dla scian (tylko 0/90/180/270), mebli, torow, chodnikow
- **Budowanie:** dostepne w czasie rzeczywistym i po pauzie
- **Predkosc pociagow:** 25 km/h (6.944 m/s) na zajezdni, 5 km/h (1.389 m/s) przez myjnie
- **Namespace:** `DepotSystem` (juz istnieje)
- **Rendering:** URP (Universal Render Pipeline 17.4, aktywny od 2026-06-17 / M-URP) — materiały URP/Lit (PBR) i URP/Unlit tworzone przez `MaterialFactory` (wcześniej shader `Standard` na Built-in RP)
- **Kolizje:** raycast do siatki (Plane na Y=0), warstwa "Ground" dla kolizji budowania
- **Zapis:** kazdy element budowlany musi byc serializowalny (zapis/wczytywanie zajezdni)
  - [ ] Struktura danych: lista scian, mebli, torow, chodnikow, sieci trakcyjnej z pozycjami
  - [ ] Format JSON lub binarny
  - [ ] Obsluga wielu zajezdni (oddzielne pliki zapisu per zajezdnia)
- **Wydajnosc:**
  - [ ] LOD dla budynkow tla (dalej = prostsze)
  - [ ] Frustum culling
  - [ ] Batching materialow (jeden material dla wszystkich scian tego samego typu)
  - [ ] Siatka renderowana tylko w widocznym obszarze (juz czesc zrobiona w GroundGenerator)
  - [ ] Zajezdnie poza aktywna scena symulowane uproszczenie (bez renderingu 3D)

---

## VI. PODJETE DECYZJE

1. **Ogrodzenie:** Cala zajezdnia ogrodzona plotem 3D z bramami wjazdowymi - jedna brama przy polaczeniu z gorna droga (dla pracownikow/samochodow), druga przy polaczeniu z torami zewnetrznymi (dla pociagow).
2. **Sciany:** Tylko po siatce 1x1m, tylko katy 0, 90, 180, 270 stopni. Bez scian pod dowolnym katem.
3. **Myjnia:** W pelni przejezdna - tor wchodzi z jednej strony budynku i wychodzi z drugiej.
4. **Dachy:** Budynki NIE maja dachow. Z kamery orbitujacej gracz widzi wnetrze budynku z gory (meble, wyposazenie, pracownikow). Sciany pelnia role wizualnej granicy pomieszczen.
5. **Rozmiar zajezdni:**
   - Podstawowy obszar budowlany: **2000m dlugosci (X, lewo-prawo) x 400m szerokosci (Z, gora-dol)**
   - Dodatkowy obszar na tlo i dekoracje wokol
   - Z prawej strony gracz moze **dokupywac teren** pod dalsza rozbudowe zajezdni
6. **Pracownicy:** Widoczne postacie 3D ktore sie poruszaja i wykonuja swoja prace (chodza po chodnikach, siedza przy biurkach, pracuja w warsztacie).
7. **Droga na gorze:** Poruszaja sie po niej samochody i ludzie jako ambient/dodatek ozywajacy scene.
8. **Budowanie:** Dostepne zarowno w czasie rzeczywistym jak i po zapauzowaniu gry.
9. **Predkosc w zajezdni:** 25 km/h na torach zajezdniowych, 5 km/h przez myjnie.
10. **Budynek sekcji:** Gracz sam buduje pomieszczenia wewnatrz po siatce 1x1m - biuro dyspozytora, biuro badan/ulepszien, pokoj socjalny, lazienka, szatnia, kuchnia/jadalnia.
11. **Obiegi i zestawienia skladow:** Planowane w osobnym ekranie/pop-upie. Na ich podstawie gracz zestawia pociagi i wysyla je na tory zewnetrzne.
12. **Meldowanie pracownikow:** Pracownicy stawiaja sie o wyznaczonej godzinie (wg systemu zmian) u dyspozytora, nastepnie ida sciezka do przypisanego pociagu (maszynisci, konduktorzy, kierownicy) lub miejsca pracy (mechanicy do warsztatu, sprzatacze do wagonu).
13. **Klikalnosc:** Kazdy pracownik, pociag, wagon, budynek jest klikalny i wyswietla pop-up z aktualnymi danymi (stan techniczny, przypisanie, morale itp.).
14. **Manewry wagonami:** Wagony mozna wypinac z jednego skladu i przepinac do innego, ale do kazdego manewru potrzebna jest lokomotywa manewrowa (np. SM42) ktora podpina wagon i przewozi go na docelowy tor.
15. **Tryby sterowania:** Ruch w zajezdni moze byc prowadzony recznie (klikanie na kazdy pociag gdzie ma jechac) albo automatycznie (na podstawie zaplanowanych obiegow). Mozliwy tryb hybrydowy.
16. **Granice kamery:** Kamera moze poruszac sie TYLKO nad obszarem budowlanym i tlem - gracz nie moze zobaczyc co jest za budynkami 3D w tle.
17. **Siec trakcyjna:** Mozna budowac nad torami lub ulepszac istniejace tory. Pojazdy elektryczne moga poruszac sie samodzielnie TYLKO po torach z siecia trakcyjna. Na torach bez sieci moga byc ciagniete przez lokomotywe spalinowa (np. manewrowa SM42).
18. **Game loop zajezdni:** Pelny cykl: budowa zajezdni -> zakup taboru -> planowanie pociagow i pracownikow -> tabor przyjezdza z torow zewnetrznych -> AI podstawia na tor -> gracz przydziela tabor do kursow -> pracownicy przychodza na zmiany i melduja sie u dyspozytora -> pociagi wyjezdzaja na kursy -> pociagi wracaja i sa obslugiwane (czyszczenie, mycie, naprawa, manewry) -> pracownicy koncza zmiane i wychodza -> cykl powtarza sie nastepnego dnia.
19. **Wiele zajezdni:** Gracz moze miec kilka rownoczesnie dzialajacych zajezdni (np. Warszawa + Krakow). Kazda niezalezna (wlasne tory, budynki, pracownicy). Tabor moze przemieszczac sie miedzy zajezdniami w ramach obiegow. Finanse wspolne (jedna firma). Limit zajezdni rosnie z rozwojem firmy.

---

# CZĘŚĆ 3: LISTA ASSETÓW 3D

# Assety i modele 3D - Zajezdnia (checklista)

Lista wszystkich modeli, tekstur, materialow i assetow potrzebnych do sceny zajezdni 3D.
Statusy: JUZ JEST = dostepny w projekcie, POTRZEBNE = do stworzenia/kupienia.

---

## 1. TORY I INFRASTRUKTURA TOROWA

### Modele 3D:
- [x] Segment toru prosty (szyna + podklad + podsypka, 0.65m) — `szyna_podklad.fbx` JUZ JEST
- [x] Rozjazd (zwrotnica) — `Rozjazd50mr300.fbx` JUZ JEST
- [x] Podklad pojedynczy — `Podklad.fbx` JUZ JEST
- [x] Szyna pojedyncza — `Szyna.fbx`, `Szyna1.fbx` JUZ JEST
- [ ] Koziol (koniec toru / zderzak torowy)
- [ ] Segment toru zakrzywionego (luk)

### Tekstury/materialy:
- [x] Podsypka — `Podsypka_material.mat` JUZ JEST
- [ ] Szyna — material metaliczny (albedo, normal, metallic, AO)
- [ ] Podklad drewniany — material drewna
- [ ] Podklad betonowy — material betonu

---

## 2. SIEC TRAKCYJNA

### Modele 3D:
- [ ] Slup trakcyjny (stalowy, ~7-8m wysokosci, z ramieniem wsporczym)
- [ ] Ramie wsporcze (mocowanie przewodu do slupa)
- [ ] Izolator (element na ramieniu)

### Elementy proceduralne/liniowe:
- [ ] Przewod jezdny (linia/cienki mesh, generowany miedzy slupami, wys. ~5.5m)
- [ ] Linka nosna (gorny przewod miedzy slupami)

---

## 3. OGRODZENIE I BRAMY

### Modele 3D:
- [ ] Segment ogrodzenia (siatka metalowa na slupku, ~2.5m wys., szer. 3-4m)
- [ ] Slupek ogrodzeniowy (metalowy/betonowy)
- [ ] Brama przesuwna / szlaban (gorna brama - od drogi, dla pracownikow)
- [ ] Brama torowa (lewa brama - szerokosc na tor, otwierana automatycznie)
- [ ] Naroznik ogrodzenia (element laczacy dwa segmenty pod katem 90 stopni)

### Tekstury/materialy:
- [ ] Siatka ogrodzeniowa — material metalowy z przezroczystoscia (alpha cutout)
- [ ] Slupek — material beton/metal

---

## 4. TEREN I NAWIERZCHNIE

### Tekstury/materialy:
- [x] Trawa — tekstury trawy (Seamless Grass Textures) JUZ JEST
- [ ] Droga asfaltowa (szary asfalt, do drogi na gorze sceny)
- [ ] Chodnik/sciezka (szary beton, dla chodnikow wewnatrz zajezdni)
- [ ] Podloga wewnetrzna (beton/posadzka, generowana w zamknietych pomieszczeniach)
- [ ] Linie siatki budowlanej (material 1x1m grid, renderowany na ziemi)

---

## 5. SCIANY I BUDYNKI

### Elementy proceduralne (generowane z kodu, potrzebne materialy):
- [ ] Material sciany — cegla (albedo, normal)
- [ ] Material sciany — beton (albedo, normal)
- [ ] Material sciany — metal/blacha (albedo, normal, metallic)
- [ ] Material sciany — szklo (polprzezroczysty, do okien)

### Modele 3D:
- [ ] Drzwi (framuga + skrzydlo, wstawiane w sciane, ~1m szer. x 2m wys.)
- [ ] Drzwi brama przemyslowa (szerokie, do warsztatu/myjni, ~4m szer. x 3.5m wys.)
- [ ] Okno (framuga + szyba, wstawiane w sciane, ~1m szer. x 1m wys.)

---

## 6. MEBLE I WYPOSAZENIE WNETRZ

Na razie moga byc prostymi ksztaltami 3D (boxy z kolorami), docelowo modele FBX.

### Biuro dyspozytora:
- [ ] Biurko z komputerem (2x1)
- [ ] Krzeslo biurowe (1x1)
- [ ] Tablica rozkladu zmian / monitor scienny (1x1, na scianie)
- [ ] Szafa/regal biurowy (1x1)

### Biuro badan/ulepszien:
- [ ] Stanowisko badawcze — biurko + komputer specjalistyczny (2x1)
- [ ] Krzeslo (1x1)
- [ ] Regal z dokumentacja (1x1)
- [ ] Tablica/whiteboard (1x1, na scianie)

### Biuro ogolne:
- [ ] Biurko (1x1 lub 2x1)
- [ ] Krzeslo biurowe (1x1)
- [ ] Komputer (na biurku, element biurka lub osobny)
- [ ] Szafa/regal (1x1)
- [ ] Drukarka (1x1)

### Socjalne/pokoj odpoczynku:
- [ ] Kanapa/fotel (2x1)
- [ ] Stolik kawowy (1x1)
- [ ] Ekspres do kawy (1x1, na blacie)
- [ ] Telewizor (na scianie)

### Lazienka:
- [ ] Toaleta (1x1)
- [ ] Umywalka (1x1)
- [ ] Prysznic (1x1)
- [ ] Suszarka do rak (na scianie)

### Szatnia:
- [ ] Szafka pracownicza (1x1)
- [ ] Lawka (2x1)

### Kuchnia/jadalnia:
- [ ] Stol (2x1 lub 2x2)
- [ ] Krzeslo kuchenne (1x1)
- [ ] Lodowka (1x1)
- [ ] Kuchenka/mikrofalowka (1x1)
- [ ] Zlew kuchenny (1x1)

### Warsztat:
- [ ] Stol roboczy/warsztatowy (2x1)
- [ ] Stojak na narzedzia (1x1)
- [ ] Podnosnik samochodowy/torowy (2x2)
- [ ] Skrzynia z czesciami (1x1)

### Magazyn:
- [ ] Regal magazynowy (1x2)
- [ ] Paleta z czesciami (1x1)
- [ ] Wozek transportowy (1x1)

---

## 7. TABOR (uproszczony zestaw)

### Modele 3D:
- [x] Pociag (model ogolny) — `Pociag.fbx` JUZ JEST (22 tekstury)
- [ ] Lokomotywa elektryczna (np. EU07/EP09, z pantografem podnoszonym/opuszczanym)
- [ ] Lokomotywa spalinowa/manewrowa (np. SM42, bez pantografu)
- [ ] Wagon osobowy (standardowy)

### Elementy taboru:
- [ ] Pantograf (ruchomy element na lokomotywie elektrycznej, podniesiony/opuszczony)
- [ ] Sprzeg (element laczacy wagony/lokomotywy, widoczny przy manewrach)
- [ ] Zderzak (na koncu wagonu/lokomotywy)

### Tekstury/materialy taboru:
- [x] Tekstury pociagu — 22 tekstur JUZ JEST
- [ ] Material lokomotywy elektrycznej (malowanie, albedo, normal)
- [ ] Material lokomotywy spalinowej (malowanie, albedo, normal)
- [ ] Material wagonu osobowego (malowanie, albedo, normal)

---

## 8. PRACOWNICY (postacie 3D)

### Modele postaci:
- [ ] Pracownik — model bazowy (low-poly, humanoid, rigged)
- [ ] Wariant: maszynista (mundur, czapka)
- [ ] Wariant: mechanik (kombinezon roboczy)
- [ ] Wariant: sprzatacz (stroj roboczy)
- [ ] Wariant: pracownik biurowy (koszula/garnitur)
- [ ] Wariant: pracownik myjni (stroj wodoodporny/roboczy)

### Animacje:
- [ ] Idle (stoi)
- [ ] Walk (chodzi)
- [ ] Sit (siedzi — przy biurku, w kabinie)
- [ ] Work — mechanik (naprawa, narzedzia)
- [ ] Work — sprzatanie (mop/sciagaczka)
- [ ] Work — biurko (pisanie na klawiaturze)
- [ ] Przebieranie sie (szatnia)

---

## 9. TLO I DEKORACJE

### Budynki tla (proste, nieinteraktywne):
- [ ] Budynek mieszkalny — prostopadloscian, rozne wysokosci (5-15m), z tekstura okien
- [ ] Budynek biurowy — prostopadloscian, wyzszy (15-30m), z tekstura szkla/okien
- [ ] Budynek przemyslowy — prostopadloscian, niski (5-10m), z tekstura blachy

### Tekstury budynkow tla:
- [ ] Fasada cegla z oknami (tileable)
- [ ] Fasada beton z oknami (tileable)
- [ ] Fasada szklo/biurowa (tileable)
- [ ] Fasada blacha przemyslowa (tileable)

### Roslinnosc:
- [ ] Drzewo lisciaste (prosty model: pien cylinder + korona kula/stozek)
- [ ] Drzewo iglaste (pien cylinder + korona stozek)
- [ ] Krzew (nisza kulista/owalna forma)

### Elementy drogi (gorna krawedz sceny):
- [ ] Samochod osobowy (low-poly, kilka wariantow kolorow)
- [ ] Samochod dostawczy/bus (low-poly)
- [ ] Latarnia uliczna (slup + lampa)
- [ ] Slup energetyczny / slup oswietleniowy
- [ ] Pieszy (bardzo prosty model, do ruchu ambient na drodze)

---

## 10. MYJNIA (elementy specjalne)

### Modele 3D:
- [ ] Szczotki myjni (obrotowe, pionowe, po bokach toru)
- [ ] Szczotka myjni gorna (obrotowa, nad torem)
- [ ] Dysze wodne (boczne i gorne)
- [ ] Rampa/prowadnica myjni (element nad torem)

---

## 11. ELEMENTY UI / IKONY (2D)

### Ikony narzedziowe (dolny pasek):
- [ ] Ikona selekcji (strzalka/kursor)
- [ ] Ikona budowania sciany
- [ ] Ikona drzwi/okna
- [ ] Ikona toru
- [ ] Ikona sieci trakcyjnej (blyskawica + tor)
- [ ] Ikona chodnika
- [ ] Ikona mebli
- [ ] Ikona wyburzania (mlot/krzyzyk)
- [ ] Ikona obracania

### Ikony statusow taboru:
- [ ] Gotowy (zielone kolo/checkmark)
- [ ] W trasie (strzalka/samolot)
- [ ] Wymaga czyszczenia (miotla)
- [ ] Wymaga mycia (kropla wody)
- [ ] Wymaga przegladu (klucz)
- [ ] W naprawie (klucz + kolo zebate)
- [ ] W myjni (kropla + szczotka)
- [ ] Manewrowany (strzalki lewo-prawo)
- [ ] Uszkodzony (czerwony wykrzyknik)

### Ikony typow trakcji:
- [ ] Elektryczny (blyskawica)
- [ ] Spalinowy (silnik/komin)

### Ikony typow pomieszczen:
- [ ] Warsztat (klucz)
- [ ] Myjnia (kropla)
- [ ] Magazyn (skrzynia)
- [ ] Punkt sprzedazy biletow (bilet)
- [ ] Biuro dyspozytora (osoba z mikrofonem)
- [ ] Biuro badan (mikroskop/zarowka)
- [ ] Socjalne (kawa)
- [ ] Lazienka (prysznic)
- [ ] Szatnia (wieszak)
- [ ] Kuchnia (sztucce)

### Ikony pracownikow:
- [ ] Maszynista (czapka)
- [ ] Mechanik (klucz)
- [ ] Sprzatacz (miotla)
- [ ] Biurowy (teczka)
- [ ] Pracownik myjni (kropla)
- [ ] Kasjer (kasa)

### Inne elementy UI:
- [ ] Ikona prędkości gry (1x, 2x, 3x, pauza)
- [ ] Ikona pieniedzy/portfela
- [ ] Ikona powrotu do mapy 2D
- [ ] Ikona kupowania terenu (strzalka w prawo + zlotowka)

---

## PODSUMOWANIE ILOSCIOWE

| Kategoria                    | JUZ JEST | POTRZEBNE | RAZEM |
|------------------------------|----------|-----------|-------|
| Tory i infrastruktura torowa | 5        | 2         | 7     |
| Siec trakcyjna               | 0        | 5         | 5     |
| Ogrodzenie i bramy           | 0        | 5         | 5     |
| Teren i nawierzchnie         | 1        | 4         | 5     |
| Sciany i budynki             | 0        | 7         | 7     |
| Meble i wyposazenie          | 0        | 30        | 30    |
| Tabor                        | 1        | 9         | 10    |
| Pracownicy (modele + anim.)  | 0        | 13        | 13    |
| Tlo i dekoracje              | 0        | 12        | 12    |
| Myjnia (elementy specjalne)  | 0        | 4         | 4     |
| UI / ikony 2D                | 0        | ~40       | ~40   |
| **SUMA**                     | **7**    | **~131**  | **~138** |

### Priorytety assetow (wg faz implementacji):

**Faza 1 — musza byc pierwsze:**
Tory (JUZ SA), koziol, segment zakrzywiony, siec trakcyjna (slup + przewod), ogrodzenie (segment + bramy), teren (droga, chodnik, siatka), tabor (3 modele + pantograf + sprzeg)

**Faza 2 — budowanie:**
Materialy scian (cegla, beton, metal, szklo), drzwi, okna

**Faza 3 — wyposazenie:**
Meble (30 modeli — moga byc placeholder boxy), chodnik material

**Faza 4 — postacie:**
Pracownicy (model bazowy + 5 wariantow + 7 animacji)

**Faza 5 — polish:**
Budynki tla, drzewa, samochody, latarnie, piesi, myjnia, wszystkie ikony UI

---

## PRZYSZŁE FEATURES TRACK TOOLA (post-EA / M12d / post-launch)

Pomysły dodane 2026-04-17 — do rozważenia po EA, przy rozbudowie narzędzi budowy zajezdni.

### 1. Multi-track parallel placement

**Pomysł:** budowa kilku równoległych torów jednocześnie podczas jednego drag'a. Zamiast kliknąć tor, kliknąć drugi, kliknąć trzeci (z manualnym spacing) — wybierasz liczbę równoległych torów + odstęp, ciągniesz jeden raz, system generuje N równoległych.

**UX:**
- W TrackTool toolbar: opcja "Tory równoległe" (slider 1-8)
- Slider "Odstęp" (domyślnie 4.5m — PKP standard między torami szlakowymi)
- Draw tool: ciągniesz jedną linię, preview pokazuje wszystkie N równoległych
- Confirm → generuje wszystkie naraz w jednej akcji (jeden undo cofnął wszystkie)

**Kiedy:** M12d (polish tools) albo post-EA (nice-to-have, nie blocker EA)

**Priorytet:** średni — oszczędza czas przy budowie dużych zajezdni / stacji, ale nie krytyczny na EA

### 2. Schematy rozjazdów i stacji (Steam Workshop-ready)

**Pomysł:** zamiast budowa każdego rozjazdu / głowicy rozjazdowej / układu stacyjnego z pojedynczych torów — wybór z biblioteki gotowych schematów. Inspiracja: Cities Skylines intersection presets + Workshop.

**UX:**
- Nowa zakładka toolbara "Schematy" obok Track/Catenary/Path/Room
- Biblioteka: wbudowane schematy (głowica rozjazdowa 4-torowa, stacja przelotowa, stacja kończąca, bocznica, lokomotywownia…)
- Preview przy hover, klik → placement mode (rotacja Q/E, mirror H/V)
- Confirm → generuje wszystkie tory + rozjazdy + catenary schematu w jednej akcji
- **Export własnego schematu:** zaznaczasz prostokąt → "Save as schematic" → nadajesz nazwę → ląduje w bibliotece gracza
- **Steam Workshop** (post-launch): publikacja schematów, subskrybowanie cudzych

**Dane schematu:**
- JSON + thumbnail PNG
- Zawiera: tory (Bezier nodes), rozjazdy (typ + orientacja), catenary (jeśli zaznaczone), metadata (nazwa, autor, tagi)
- Lokalnie: `%USERPROFILE%/RailwayManager/Schematics/*.json`
- Workshop: sync z `steamapps/workshop/content/<appid>/`

**Kiedy:**
- **Schematy lokalne** (wbudowane + user-made): post-EA, **M15** (post-launch, Year 1 Support)
- **Steam Workshop integration**: **M16-M17** (dużo później, wymaga Steamworks API + moderacja + bandwidth)

**Priorytet:** niski na EA (duży feature), wysoki post-launch (driver retencji — gracz dzieli się schematami = community, więcej content bez developera)

**Zależności:**
- Wbudowane schematy wymagają ~10-20 hand-crafted presetów (godziny projektowania)
- Workshop wymaga Steamworks SDK integracji + ToS moderacji content'u

**Nie robimy tego teraz — tylko zapisujemy pomysł.** Przy rozpoczęciu M15 wyciągnąć tę sekcję, rozpisać jako osobny design spec.
