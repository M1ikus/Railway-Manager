# GameCreator + DepotLocationPicker — setup nowej gry

> **Status:** GameCreator częściowo zaimplementowany (wybór mapy, nazwy, ustawień). **Brakuje:** osobnej nakładki `DepotLocationPicker` między GameCreator a Depot/Map scenes — gracz wybiera lokalizację swojej zajezdni na interaktywnej mapie.
> **Pliki istniejące:** `Assets/Scripts/GameCreator/GameCreatorUI.cs`, `Assets/Scripts/MainMenu/GameCreatorContext.cs`
> **Sceny:** `MainMenu.unity` → `GameCreator.unity` → **(NEW)** `DepotLocationPicker` overlay → `Depot.unity` + `MapScene.unity`

---

## 1. Flow gry od menu do rozgrywki

```
[MainMenu.unity]
        ↓ klik "Nowa gra"
[GameCreator.unity]
   Krok 1: wybór mapy (region/DLC)
   Krok 2: nazwa firmy przewozowej
   Krok 3: ustawienia (timescale, pauza, autosave)
        ↓ klik "Rozpocznij grę"
[Depot.unity + MapScene.unity load]
        ↓ po załadowaniu — ZANIM gracz zobaczy depot
[DepotLocationPicker overlay] (NEW — nakładka full-screen, blokuje input do gry)
   - Pokazuje mapę wybranego regionu (renderowana z MapScene)
   - Gracz klika stację → panel info
   - "Potwierdź wybór" → GameState.HomeDepotStationId set + overlay znika
        ↓
[Gra rusza — MapScene aktywna, gracz może przełączać na Depot]
```

**Dlaczego osobna nakładka, a nie krok w GameCreator?**
- Gracz widzi **swoją mapę** — tę samą której będzie używał w rozgrywce, z tymi samymi danymi OSM, tym samym zoom/pan. Nie ma "kreatora w kreatorze".
- Mapa jest już załadowana (MapScene load kończy się zanim overlay się pokazuje) — renderowanie real-time, nie duplikat statyczny.
- Możliwe pokazanie dodatkowych informacji z MapScene (budynki, podświetlenie linii, fizyczny kontekst) które w kreatorze nie byłyby dostępne.

---

## 2. GameCreator — kroki (już zaimplementowane)

### Wybór mapy/regionu — **odłożone do M-DLC**

Sekcja Mapa wyrzucona 2026-05-14. Wcześniejsza implementacja była mockiem (5 fake'owych nazw "Depot Alpha"/"Centralny Węzeł"/itd., `_selectedMap` bez konsumenta).

EA launch ma jeden region — **cała Polska** (M-PL zamknięte 2026-04-25, `init-state-pl.bin`). Wybór regionu/DLC wraca jako osobny krok w **M-DLC** (post-EA), gdy dostępne będą Czechy/Niemcy/Słowacja/Ukraina/Białoruś/Litwa (patrz `docs/design/country-dlc-map-packages.md`). API gotowe: `GameState.AddDlcCountry`/`ResetDlcCountries`/`ActiveDlcCountries` (z protection na ostatni kraj).

### Krok 1 — Nazwa firmy przewozowej

Input tekstowy (max 40 chars, `MaxGameNameLength`), domyślnie pusty. Trafia do `GameState.DepotName` (legacy — rename na `OperatorName` w M-Balance).

### Krok 2 — Ustawienia (SP) / Serwer + ustawienia (MP)

**SP — zakładka Rozgrywka:**
- Pauza na start (toggle)
- Autosave on/off + interwał (5/10/15/30 min)
- Seed (deterministyczne ziarno RNG; 0 = baseline, dowolna liczba = powtarzalna sekwencja awarii/random events — debugowanie crashów)
- Difficulty preset (Easy/Normal/Hard/Realistic/Custom) + 10 modifierów + tooltip + live preview
- 6 game rules (toggleable)

**MP — zakładka Serwer (host-authoritative):**
- Server name (max 40 chars), Max players (2/4/6/8/12/16), Password (max 32 chars, opcjonalnie), Visibility (Public/Private/Hidden)
- Seed (host MUSI ustawić — klienci dostają to co host, inaczej MP-9 determinizm desyncuje)
- Difficulty + Game rules (jak SP, host wybiera dla całej sesji)

**Co WYRZUCONE 2026-05-14:**
- ~~Speed slider 0.5-3x~~ — niezgodny z dyskretnymi przyciskami x1/x5/x25/x150/x500 w `TopBarUI`. Speed steruje się z top bara po wejściu do Depot.

### Koniec: "Rozpocznij grę"

Modal confirmation przed exit (TYLKO gdy `_isDirty == true`, czyli gracz dotknął jakiejkolwiek kontrolki — brak zmian = direct exit).

Po Start: ApplyOnStart → ResetRuntimeForNewGame hook → write GameName/IsPaused/AutoSave/Difficulty/Rules/Money → load `Depot.unity`. `DepotLocationPicker` overlay pokazuje się w scenie Depot (osobny system w Timetable asmdef).

---

## 3. DepotLocationPicker — **nowa nakładka**

### Kiedy się pokazuje

- Po GameCreator confirm (sceny załadowane, systemy inicjalizowane)
- **Przed** pozwoleniem na input do gameplay (TopBarUI disabled, kamera zablokowana na mapie w initial zoom)
- Renderuje się jako full-screen overlay (Canvas ScreenSpaceOverlay, sortingOrder = 500 — nad wszystkim)

### Co widzi gracz

**Tło:** MapScene w pełnej wielkości (żywa, nie snapshot), domyślny zoom = cały region, kamera centered na środek mapy. Polyline sieci kolejowej widoczna, stacje widoczne jako ikony.

**Overlay elementy:**

- **Pasek górny** (sticky top):
  - Tytuł: "Wybierz lokalizację zajezdni"
  - Podtytuł: "Ta decyzja jest permanentna na tę rozgrywkę. Zastanów się — tam będą zaczynać i kończyć dzień twoje pociągi."

- **Mapa** (80% pionu, środek):
  - Wszystkie stacje jako **klikalne POI**
  - Kwalifikujące stacje: duży niebieski okrąg + ikona zajezdni (pełny kolor)
  - Niekwalifikujące: mały wyblakły okrąg (hover tooltip "Za mała stacja — min. 2 perony")
  - Hover over kwalifikującą → tooltip (nazwa, miasto, województwo, liczba peronów, klasa ruchowa)
  - Klik → highlight pomarańczowym pierścieniem + pokazuje panel info po prawej

- **Panel info po prawej** (300px szeroki, pojawia się po kliknięciu stacji):
  - Duży header z nazwą stacji + miasto + województwo
  - Liczba peronów / torów stacyjnych
  - Klasa ruchowa (kategorie IRJ które obsługuje, np. "EN/OR/MP")
  - Elektryfikacja (tak/nie + napięcie)
  - Liczba wychodzących linii (i gdzie prowadzą: "→ Warszawa", "→ Gdańsk")
  - Szacowana wielkość ruchu: low / medium / high (wizualny indicator — 1/2/3 słupki)
  - Kontrolka: "Potwierdź wybór: [Stacja Nazwa]" → disable dopóki nie kliknie stacji

- **Pasek dolny** (sticky bottom):
  - "← Wróć do kreatora" (re-open GameCreator, traci wybór)
  - "Potwierdź wybór" (disabled dopóki stacja nie wybrana)

- **Zoom/pan controls** (analogicznie do normalnego MapScene UI — mouse wheel zoom, drag pan)

- **Filter checkboxy** (w lewym panelu):
  - "Tylko elektryfikowane"
  - "Tylko duże miasta (>50k mieszkańców)" — implementacja: voivodeship DB lookup
  - "Tylko stacje węzłowe" (≥3 linii wychodzących)

- **Recommended stations chip-list** (górny pasek, obok tytułu):
  - Dla warmińsko-mazurskie: "Olsztyn Główny", "Ełk", "Iława Główna"
  - Dla całej Polski: "Warszawa Centralna", "Kraków Główny", "Poznań Główny", "Wrocław Główny", "Gdańsk Główny"
  - Klik na chip → auto-pan mapa, auto-select stacji

### Po confirm

1. `GameState.HomeDepotStationId = station.nodeId`
2. `GameState.HomeDepotStationName = station.name`
3. `GameState.HomeDepotCityName = station.cityName`
4. `GameState.HomeDepotVoivodeship = station.voivodeship`
5. Overlay fade out (0.3s)
6. TopBarUI enable, kamera unlocked, gracz widzi normalną mapę
7. Log: "[DepotLocationPicker] Selected: {station.name} ({station.city}, {station.voivodeship})"

### Kryteria kwalifikacji stacji

| Kryterium | Wartość | Powód |
|---|---|---|
| Liczba peronów | ≥ 2 | Minimalny ruch |
| Wychodzące linie | ≥ 1 | Musi być w sieci |
| Klasa ruchowa | brak limitu w EA | W post-EA DLC: może limit |

**Rozbudowa post-EA** (M-Balance):
- **Easy:** wszystkie stacje, 150k PLN start
- **Normal:** duże miasta (>50k), 100k PLN start
- **Hard:** tylko węzłowe (10-15 opcji na Polsce), 80k PLN start

### Ograniczenia MVP (pre-EA)

- Brak filtrowania (wszystkie stacje kwalifikujące się pokazane)
- Brak recommended chips (przyjdzie w post-launch polish)
- Brak animacji fade — instant show/hide
- Brak "wróć do kreatora" — tylko forward flow (gracz chce cofnąć → quit to menu)

---

## 3. UX principles

1. **Wizualne** — nie lista stacji, tylko mapa. Gracz widzi geograficzny kontekst swojej decyzji.
2. **Jednorazowe** — wybór jest permanentny na daną rozgrywkę. Jasne komunikaty "Zastanów się — nie da się zmienić później".
3. **Brak paraliżu wyboru** — podpowiedzi (recommended for beginners: Warszawa Centralna, Kraków Główny, Poznań Główny — duże węzły z dobrze rozwiniętą siatką).
4. **Re-usable assets** — renderowanie mapy z GameCreator wykorzystuje te same dane OSM co MapScene (StationLoader, RailwayGraph). Nie dublujemy.

---

## 4. Integracja z innymi systemami

### M5 Circulations (walidacja)

`CirculationValidator` przy aktywacji obiegu sprawdza:
- Pierwszy step startuje z `GameState.HomeDepotStationId` (hard requirement — jeśli nie, Error)
- Ostatni step kończy w home (soft requirement — jeśli nie, Warning "Obieg nie wraca do depot, pojazdy zostają na trasie")

### M9c Handshake (patrz `docs/design/m9c-handshake.md`)

- Wyjazd z depot: spawn na mapie na pozycji home station
- Powrót do depot: wjazd przez bramę tylko jeśli run.endStation == HomeDepotStationId
- Widoczny node stacji na mapie = jedyne miejsce skąd consist może wyjechać z depot i jedyne miejsce gdzie wraca

### Save/Load (M13)

`HomeDepotStationId` serializowany jako część `GameState` snapshot. Przy load'zie:
- Jeśli `HomeDepotStationId` niepoprawny (stacja usunięta z mapy przez update DLC) → graceful fallback: pokaż komunikat "Stacja home nie istnieje, wybierz nową" (zatrzymanie gry, re-prompt).

---

## 5. Edge cases

- **Zmiana depot post-game-start:** niedozwolone w pre-EA (spowoduje chaos w circulations). Post-EA może jako premium feature z kosztem znacznym (miliony PLN, przenosiny, 7-dniowe zamknięcie).
- **Brak kwalifikujących stacji na mapie:** nie powinno się zdarzyć przy sensownym mapowaniu DLC. Fallback: pokaż wszystkie stacje + warning.
- **Multiplayer (M10):** każdy gracz wybiera własne home — kolizja (ten sam ID) jest OK bo inny operator, ale mogą być wizualne konflikty w 3D (nie realistyczne). Post-EA zaplanowane jako "każdy operator ma swoją dzielnicę zajezdni w tej samej stacji" albo "różne stacje per gracz".
- **Save z poprzedniej wersji bez HomeDepotStationId:** migrator ustawia domyślną wartość (np. pierwsza stacja alfabetycznie z wybranej mapy) + prompt dla gracza żeby potwierdził.

---

## 6. Implementacja — podetapy

### Etap 1 — DepotLocationPicker MonoBehaviour + overlay UI szkielet
- [ ] `DepotLocationPickerUI.cs` w assembly Map (widzi Core, MapScene types)
- [ ] Overlay Canvas ScreenSpaceOverlay + pasek górny/dolny + info panel po prawej
- [ ] Bootstrap: wywołany po `SceneController.Initialize` gdy `GameState.HomeDepotStationId == -1`
- [ ] Blokada input'u do gameplay (TopBarUI.enabled=false, camera lock)

### Etap 2 — Integracja z mapą
- [ ] Zmodyfikuj `StationMarker.cs` (lub dedykowany overlay markers GO) — renderuj większe klikalne ikony przez czas overlay'a
- [ ] Filter "kwalifikujące / niekwalifikujące" wizualny (kolor + alpha)
- [ ] Click detection (raycast lub OnClick na ikonach, w zależności od jak StationMarker działa teraz)
- [ ] Hover tooltip (nazwa stacji)

### Etap 3 — Panel info + confirm
- [ ] Panel po prawej z detalami wybranej stacji
- [ ] Dane stacji z `StationLoader`, liczba peronów z `StationTrackData`, linie z `RailwayGraph`
- [ ] Przycisk "Potwierdź wybór" (enabled tylko po kliku)
- [ ] On confirm: set `GameState.HomeDepotStationId` + pola pomocnicze, fade out overlay, unlock input

### Etap 4 — Walidacja downstream (M5)
- [ ] `CirculationValidator.ValidateHomeStation(circulation)` — error gdy step[0].startStation ≠ HomeDepotStationId
- [ ] Wyświetlane w M5 UI jako czerwony error message przy aktywacji obiegu

### Etap 5 — Polish
- [ ] Recommended stations chips (3-5 per mapa, hardcoded lista)
- [ ] Filter checkboxy (elektryfikowane / duże miasta / węzłowe)
- [ ] Animacje (fade in/out overlay, highlight pulsing na klikniętej stacji)
- [ ] Zoom/pan locked do regionu mapy (nie dać graczowi lecieć za granicę DLC)

### Etap 6 — Edge cases + save/load
- [ ] Fallback gdy brak kwalifikujących stacji (warn, pokaż wszystkie)
- [ ] Save/load migracja dla starych save'ów (jeśli `HomeDepotStationId==-1` → re-open overlay, force player to choose)
- [ ] Quit to menu action (gracz zrezygnował)

---

## 7. Pytania otwarte

- **Kiedy implementować?** Obecnie M9c-2 testujemy z debug ContextMenu (manual set HomeDepotStationId). Porządny UI wyboru depot to **osobny milestone** — proponuję **M9c-2.5** między M9c-2 a M9c-3, albo po całym M9c w ramach polishu UX przed EA.
- **Dane potrzebne do kryteriów** — ile peronów per stacja? Jest w `StationTrackData` już załadowane. Klasa ruchowa — pochodna z `irjCategory` poprzez `CategoryClassifier` dla obsługiwanych kursów (ale ilu musi obsługiwać żeby się kwalifikować?).
- **Estymowana wielkość ruchu** (low/medium/high) — obliczyć w preprocessing OSM data? Lub uproszczenie: liczba peronów × liczba linii.
