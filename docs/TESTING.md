# Testing Strategy

> **Cel:** Jak testujemy grę. Manual checklists, smoke tests, playtesty.
> Uzupełniaj gdy wprowadzasz nowe scenariusze testowe.

> **Aktualna matryca regresji:** `docs/QA_REGRESSION_MATRIX.md` odpowiada na
> pytanie "co testowac po zmianie danego obszaru?". Ten plik zostaje jako
> strategia ogolna i historia smoke testow per milestone.

---

## Filozofia

**Railway Manager jest game'em** — nie ma jednoznacznego "pass/fail" jak w biznesowym systemie. Testujemy:
1. **Technical stability** — czy nic się nie crashuje, performance jest OK
2. **Gameplay correctness** — czy mechaniki działają zgodnie z designem
3. **User experience** — czy gra się przyjemnie, UI nie przeszkadza
4. **Regression** — czy nowe zmiany nie psują starego

**Używamy:**
- **Unity Test Framework — EditMode NUnit** (od 2026-05-30) — `Assets/Tests/EditMode/`
  (asmdef `RailwayManager.Tests.EditMode`, Editor-only, refs Core/Fleet/Timetable/
  GameCreator/SaveLoad). Dla **czystej logiki i kontraktów**, bez sceny. Pokryte (2026-05-31):
  - **Core:** GameState/Time/Random/Pause/Utility, VehicleLocationService (+ stany handshake)
  - **Dostawa (M9c-D):** DeliveryLocator, CartProcessor, DeliveryPipeline, WagonFetch,
    TrainRunShouldStart, DeliverySaveRoundtrip
  - **Rozkłady/obiegi (M4-M5):** CategoryClassifier, TrainNumberValidator, BlockSectionGraph,
    Circulation (model+home+sequence), ConsistValidator
  - **Ekonomia (M6):** Ticket/Cost/Demand/StationImportance/Subsidy/Reputation/OD-Matrix
  - **Fleet (M7):** Breakdown (ścieżki deterministyczne), Degradation, MaintenanceCost
  - **SaveLoad:** roundtrip/registry/serialization/migration
- **PlayMode NUnit** — `Assets/Tests/PlayMode/` (asmdef `RailwayManager.Tests.PlayMode`,
  `includePlatforms: []`). Dla logiki wymagającej **realnej sceny/grafu**: ładuje
  `MapScene` (realny PathfindingGraph z init-state-pl.bin) lub `Depot.unity` (TrackGraph 3D).
  Pokrywa: symulację ruchu dostawy, pathfinder na realnym grafie, bloki na trasie, wjazd/wyjazd
  3D z depot. **Ciężkie (~minuty, ładowanie scen)** — na CLI/przed-merge, nie w szybkiej pętli.
- **Runner CLI:** `Tools/run-tests.ps1 -Platform EditMode|PlayMode|Both [-Filter ...]` —
  headless batchmode, parsuje wynik XML i wypisuje PASS/FAIL na stdout, exit 0/1/2.
  (lub bezpośrednio `Unity.exe -runTests -testPlatform EditMode -batchmode`).
- Manual smoke tests + `[ContextMenu]` per milestone (UI, edge cases wymagające oka)
- Playtesty (własne + M14 closed beta), log monitoring (Console, Sentry w M14)

> **Historia:** do 2026-05-30 obowiązywała decyzja z M4 „bez UTF" (gra nie ma
> jednoznacznego pass/fail jak system biznesowy → smoke tests wystarczą). Zniesiona
> gdy okazało się, że czysta logika (save/load, kontrakty Core, pipeline dostawy)
> *jest* deterministycznie testowalna i UTF eliminuje regresje taniej niż manual smoke.
> Smoke tests **nie znikają** — pokrywają warstwę scena/Play mode, której EditMode nie dosięga.

---

## Smoke test per milestone

Przy każdym commit'ie kończącym sub-milestone **przeprowadź smoke test** zgodnie z poniższym:

### M4 Timetable

- [ ] Uruchom MainMenu → New Game → Depot.unity ładuje się <10s
- [ ] Przełącz do MapScene (przycisk mapa w TopBar) → ładuje się <15s (warmińsko-mazurskie)
- [ ] Otwórz zakładkę Rozkłady → klik "+ Nowy rozkład"
- [ ] W kreatorze: wybierz stacje start/koniec (np. Olsztyn Główny → Elbląg)
- [ ] "Oblicz postoje" → generuje listę postojów z czasami
- [ ] Zmień typ postoju (Passenger/Technical/PassThrough) → UI się aktualizuje
- [ ] Wybierz tor dla postoju → dropdown działa
- [ ] "Zatwierdź" → rozkład zapisany, wraca do listy
- [ ] Lista rozkładów pokazuje nowy wiersz z nr/trasą/czasem
- [ ] Stwórz drugi rozkład na tej samej trasie, godzinę później → brak kolizji
- [ ] Stwórz trzeci na tej samej trasie, 2 minuty po pierwszym → pojawia się oczekiwanie lub kolizja

### M5 Circulations (gdy będzie gotowe)

- [ ] Utwórz 2 rozkłady: A→B i B→A
- [ ] Otwórz zakładkę Circulations → "+ Nowy obieg"
- [ ] Dodaj oba rozkłady do obiegu
- [ ] Wybierz pojazd z floty → pojazd "lockuje się" dla obiegu
- [ ] Walidacja: czas między kursami wystarcza na reverse? → TAK/NIE
- [ ] Zatwierdź obieg → `Timetable.compositionMode = Concrete`
- [ ] Spróbuj dodać ten sam pojazd do drugiego obiegu → error
- [ ] Usuń pojazd z obiegu → unlocked, znów dostępny

### M9 Train movement (gdy będzie gotowe)

- [ ] Uruchom grę z jednym rozkładem Iława → Działdowo
- [ ] Przyśpiesz czas do godziny startu rozkładu
- [ ] Pociąg pojawia się na mapie, rozpoczyna ruch
- [ ] Pociąg przejeżdża przez stacje zgodnie z rozkładem
- [ ] Na każdej stacji krótki postój (czas zgodny z rozkładem)
- [ ] Pociąg dojeżdża do końca trasy, znika z mapy
- [ ] Uruchom dwa rozkłady w kolizyjnym slot — drugi pociąg czeka lub jedzie wolniej
- [ ] Na dwutorowej linii dwa pociągi w przeciwnych kierunkach nie kolidują
- [ ] Pantograf elektryczny — up gdy jest sieć, down gdy brak
- [ ] Depot ↔ Mapa transitions działają dla home depot

### M6 Economy (gdy będzie gotowe)

- [ ] Start: 150k PLN w TopBarUI
- [ ] Kup pociąg → saldo się zmniejsza o cenę
- [ ] Pociąg jedzie zaplanowanym kursem → saldo się zwiększa o przychód z biletów
- [ ] Koszty miesięczne naliczane na koniec miesiąca gry
- [ ] Bilans dzienny pokazywany w UI
- [ ] Saldo < 0 → ostrzeżenie game over (lub kredyt jeśli zaimplementowany)

---

## Manual playtesty (po M9)

Po M9+M6 gra jest **playable end-to-end**. Od tego momentu regularnie graj:

### Session testowa (30 min)
1. Start nowej gry z wybranym home depot
2. Kup 2-3 pociągi (różne typy)
3. Utwórz 3-5 rozkładów
4. Zbuduj 1 obieg łączący rozkłady
5. Uruchom gra, przyśpiesz czas x25
6. Obserwuj ruch pociągów, sprawdź czy wszystko działa
7. Po 30 min **zapisz notatki:**
   - Co działało dobrze? Czego brakuje?
   - Co było frustrujące / nieintuicyjne?
   - Performance: ile FPS, ile RAM?
   - Czy była motywacja żeby grać dalej?

### Stress test (raz na milestone)
- 20-50 pociągów równocześnie na pełnej Polsce
- Sprawdź FPS, RAM, stabilność, hit na loading tile
- Zanotuj regression vs poprzednia sesja

---

## Debug tooling

### `[ContextMenu]` w komponentach

Dodawaj do komponentów dev-only akcje:

```csharp
[ContextMenu("Debug: Print all timetables")]
private void DebugPrintTimetables()
{
    foreach (var t in TimetableService.Timetables)
        Debug.Log($"[{t.id}] {t.name} — {t.stops.Count} stops");
}
```

Używane w: `TimetableDebugTools`, `DepotManager`, `FleetService`.

### Logi

Wzorzec: `Log.Info("[ClassName] Message with context")`

**Zbyt dużo logów:**
- `MapRenderer` i `TileManager` mają flag `showDebugInfo` w Inspektorze
- Wyłączaj gdy nie debug'ujesz — spamują konsolę

**Zbyt mało logów:**
- Jeśli debug'ujesz, dodaj `Log.Debug()` (tylko w DEV builds)
- Zdejmuj przed commitem

### Scena debug

Można utworzyć `Debug.unity` — scena wyłącznie do testowania pojedynczych mechanik bez całej gry. Obecnie **nie istnieje** — można stworzyć przy okazji M12a Performance.

---

## CI / automatyzacja

**Obecnie:** brak CI.

**Plan:**
- **M14 Beta:** rozważyć GitHub Actions z Unity build automation
- **Build smoke test:** sprawdza czy projekt się buduje i otwiera MainMenu bez crash'a
- **Performance regression:** porównanie FPS baseline z poprzedniego commit'u

Patrz `docs/design/performance-targets.md` sekcja "Regresja testowa".

---

## Bug tracking

### Obecnie

- **Git log** — historia commitów jako bug history
- Brak dedykowanego trackera

### Docelowo (M14)

Wybór między:
- **GitHub Issues** — jeśli repo publiczne po launch
- **Trello / Notion** — podczas development
- **Discord bug reports channel** — dla testerów

---

## Closed beta (M14)

Planowane:
- **Steam Playtest** — oficjalny program, 50-100 testerów
- **Discord** — feedback channel, bug reports
- **1 miesiąc trwania**
- **Weekly builds** dla testerów

### Co testujemy w closed beta

1. **Stabilność** — crashe, corrupted saves, infinite loops
2. **Onboarding** — czy tutorial jest zrozumiały
3. **Balance** — czy ekonomia działa, gra jest wyzwaniem ale nie frustrująca
4. **UX** — czy UI jest czytelny, skróty działają, brak confusion
5. **Localization** — czy tłumaczenia są poprawne per locale
6. **Edge cases** — co robi gracz poza "happy path"? Np. trasa do samego siebie, koszt = 0 PLN
7. **Performance** — różne konfiguracje sprzętowe, od minimum do high-end

### Feedback format

Testerzy podają:
- Opis bugu / feedback
- Kroki reprodukcji
- Specs PC
- Screenshot / video (opcjonalnie)
- Priorytet (1-5)
