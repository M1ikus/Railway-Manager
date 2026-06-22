# Predykcyjny dispatcher ruchu na mapie OSM (M-Dispatch)

> Status: ✅ logika Faz 1–4 + persystencja + UI done (2026-06-02). Autonomiczny, deterministyczny,
> deadlock-safe. Pełna lista plików/testów na dole.

## Cel / kontekst

**Problem (pytanie projektowe):** osobowy ma planowy postój na stacji X, gdzie pośpieszny ma go
wyprzedzić — ale pośpieszny jedzie spóźniony. Wypuszczenie osobowego opóźni pośpieszny jeszcze
bardziej; trzymanie osobowego opóźni jego. Jak system ma się zachować?

**Stan przed M-Dispatch (charakteryzacja w `OvertakePriorityTests`):**
- Priorytet IRJ rozstrzygał konflikt **tylko reaktywnie** — gdy oba pociągi fizycznie stały
  (`BlockedBySignal`) przed tym samym blokiem (`TrainRunSimulator.HasHigherPriorityWaiting`).
- Odjazd z planowego postoju (`Advance` → `StoppedAtStation`) był **czysto czasowy** — pociąg
  ruszał o swojej godzinie, bez patrzenia na nadjeżdżające pociągi.
- Skutek: spóźniony pośpieszny, jeszcze nieobecny przy X, **nie istniał** dla logiki ustępowania
  → osobowy ruszał, pośpieszny był blokowany za nim = jeszcze większe opóźnienie. Wyprzedzenie
  zaplanowane w rozkładzie przepadało.

**Cel M-Dispatch:** autonomiczny, **predykcyjny** (patrzy w przyszłość), **deadlock-safe**,
wielo-czynnikowy dispatcher — wyłącznie dla **ruchu na mapie OSM**. Ruch w zajezdni ma osobnego
dyżurnego (`Personnel.TrafficControlService`) — **nie mylić**, to dwa rozłączne systemy.

## Architektura — 4-warstwowy potok

Decyzja zapada w **punkcie bezpiecznego postoju** (stacja, gdzie pociąg może stanąć) — czyli przy
odjeździe z postoju. Event-driven: liczona tylko gdy pociąg jest gotów do odjazdu, nie co tick.

### 1. Forecast czasoprzestrzenny — `TrainForecastService` (Faza 1)
Dla pociągu z bieżącego `SimulatedTrain` liczy sekwencję rezerwacji bloków `(blockKey, enterSec,
exitSec)` w horyzoncie (`DispatchForecastHorizonSec` = 30 min). Model **v1 zgrubny**: per-blok
cruise przy Vmax segmentu (cap `composition.maxSpeedKmh`) + **trzymanie do planowej godziny
odjazdu** na postojach (osobowy okupuje blok peronowy do swojego odjazdu; spóźniony pociąg NIE
czeka → hold 0). Pomija accel/decel i kompresję postoju — refine jeśli rozstrzyganie będzie
wymagać większej dokładności.

### 2. Detekcja konfliktu — `BlockConflictDetector` (Faza 1)
Ze zbioru prognoz znajduje pary pociągów z nakładającą się rezerwacją tego samego bloku.
Network-wide (caller decyduje o zakresie/klastrze). Deterministyczny output (stabilne sortowanie).

### 3. Decyzja trzymaj/puść — `PredictiveDispatchDecider` (Fazy 2 + 4)
Jeśli `me`, wyjeżdżając teraz, wszedłby na blok PRZED wyżej-ważonym rywalem i zablokował go:
```
releaseCost = effWeight(rywal) × opóźnienie rywala (gdy me jedzie)
holdCost    = effWeight(me)    × ile me poczeka (ustępując)
TRZYMAJ  ⇔  holdCost < releaseCost × holdBias  ∧  hold ≤ DispatchMaxExtraHoldSec  ∧  ¬deadlock
```
- **Waga** (`DispatchWeight`, Faza 4c): `priorytetIRJ × DispatchPriorityScale + clamp(obłożenie, 0,
  DispatchMaxLoadWeight)`. Zapchany osobowy może przeważyć pusty pośpieszny. Obłożenie czytane O(1)
  z `PassengerManager.CountOnTrain` (indeks MP-3).
- **Efektywna waga + kaskada** (Faza 4a): `effWeight(T) = waga(T) + Σ wag pociągów czekających na
  T` (z grafu wait-for; kontrpartner wykluczony). Długa/cięższa kolejka ZA pociągiem przeważa na
  „puść" — anty-korek na poziomie kosztu.
- **`holdBias`** (Faza 4b): mnożnik z polityki — Punktualność = 1.5 (trzymaj chętniej).
- **Twardy limit** `DispatchMaxExtraHoldSec` (30 min): anti-starvation. To tylko sufit — realny
  tuning robi funkcja kosztu.

### 4. Bezpieczeństwo / anty-deadlock — `DispatchSafeState` (Faza 3a)
**Gwarancja „nie zakorkuje się":** HOLD wykonywany TYLKO jeśli nie domknie cyklu wait-for.
Graf wait-for z prognoz (`X→Y` gdy Y zajmuje wspólny blok ściśle wcześniej i blokuje X). HOLD
inwertuje naturalne `rywal→me` na `me→rywal`; jeśli rywal nadal dociera do `me` INNYMI krawędziami
(`rywal→…→me`), `me→rywal` domyka cykl → zakleszczenie → **nie wolno trzymać** (puść, rozbij korek).
Deterministyczne (osiągalność niezależna od kolejności krawędzi).

## Integracja z symulacją
- **`TrainRunSimulator.Dispatch`** (partial) — `ShouldHoldForPredictiveDispatch(me, nowSec)`: liczy
  forecast `me` + **lokalnego klastra** (pociągi dzielące nadchodzący blok, prefilter po
  `routeBlockKeys`), deterministycznie sortuje po `trainRunId`, pyta decidera. Bufory reużywalne.
- **`TrainRunSimulator.Movement`** (`Advance` → `StoppedAtStation`) — przed odjazdem (normalnym i
  z kompresją): jeśli `ShouldHold` → defer (akumuluj delay) zamiast `Running`.
- **Reaktywna warstwa blokowa** (`HasHigherPriorityWaiting`, `Priority.cs`) — **nietknięta**.
  Predykcja to osobna, wcześniejsza warstwa; bezpieczeństwo semaforowe pozostaje nadrzędne
  (dispatcher tylko *opóźnia* wjazd, nigdy nie łamie zajętości bloku).

## Polityka gracza (knob)
- **`DispatchPolicy`** (enum w **Core**, jak `DifficultyPreset`): `Off` / `Balanced` (default) /
  `Punctuality`. Wartość: **`GameState.MapDispatchPolicy`** (Core — bo ustawiają ją GameCreator i
  ekran ustawień, asmdef bez Timetable; czyta dispatcher w Timetable).
- **`DispatchPolicyService`** (Timetable) — read-through do GameState + mapowanie polityka →
  `HoldingEnabled` (Off=false) / `HoldBias` (Punctuality=1.5).
- **Persystencja:** `WorldSavable` (klucz `dispatchPolicy`); reset do Balanced w `InitializeDefault`
  (nowa gra). Stary save bez pola → Balanced.
- **UI:** kreator gry sekcja **Rozgrywka** (dropdown → `ApplyDispatchPolicyOnStart`) + **ustawienia
  ogólne in-game** (`SettingsScreenUI` sekcja Ogólne, otwierana z PauseMenu; dropdown zbindowany
  wprost do GameState, efekt natychmiastowy jak rebindy, pokazany tylko poza MainMenu).

## Determinizm i wydajność (niepodważalne kontrakty)
- **Determinizm (MP-9):** brak RNG; klaster sortowany po `trainRunId`; wynik niezależny od
  kolejności iteracji słownika.
- **Wydajność (1000+ pociągów):** event-driven (tylko przy gotowości do odjazdu) + ograniczenie do
  lokalnego klastra (prefilter block-share) + horyzont. Nigdy globalny re-plan co tick. „Pełna sieć
  w zamyśle" — obecnie lokalny klaster skaluje; central planner = przyszłość jeśli perf pozwoli.
- **Bezpieczeństwo:** dispatcher tylko opóźnia wjazd; warstwa semaforowa nadrzędna.

## Stałe (`TimetableTuningConstants`, tuning M-Balance)
`DispatchForecastHorizonSec`=1800, `DispatchMinForecastSpeedMps`=2, `DispatchMaxExtraHoldSec`=1800,
`DispatchPriorityScale`=100, `DispatchMaxLoadWeight`=300, `DispatchPunctualityHoldBias`=1.5.

## Świadome deferrale / decyzje otwarte
- **Ochrona przesiadek:** runtime **nie modeluje** skomunikowań („pociąg B czeka na pasażerów z A").
  Jest tylko planistyczna sugestia obiegów (`CirculationSuggestionService`). Ważenie przesiadek
  wymagałoby najpierw zbudowania modelu skomunikowań — osobna funkcja, NIE dolepka. Poza zakresem.
- **Forecast v1 zgrubny** (block-level cruise + scheduled hold, bez accel/decel). Refine jeśli
  rozstrzyganie okaże się za niedokładne.
- **Zasięg = lokalny klaster + horyzont.** „Pełna sieć / central planner" rozważany, ale bounding
  pod perf był priorytetem (decyzja usera: „w zamyśle cała sieć ale zobaczymy przy optymalizacji").
- **DE/CZ tłumaczenia** kluczy `dispatch.policy.*` — best-effort, do weryfikacji przez tłumaczy.

## Pliki
**Logika (`Timetable/Runtime/Simulation/Dispatch/`):** `TrainForecast.cs` (POCO),
`TrainForecastService.cs`, `BlockConflictDetector.cs`, `PredictiveDispatchDecider.cs`,
`DispatchSafeState.cs`, `DispatchWeight.cs`, `DispatchPolicy.cs` (DispatchPolicyService).
**Integracja:** `Simulation/TrainRunSimulator.Dispatch.cs` (orchestracja), `.Movement.cs` (wpięcie).
**Core:** `DispatchPolicy.cs` (enum), `GameState.MapDispatchPolicy`.
**Persist/UI:** `SaveLoad/Modules/WorldSavable.cs`, `GameCreator/GameCreatorUI.*`,
`MainMenu/SettingsScreenUI.Sections.cs`, `Resources/Locale/{pl,en,de,cz}/strings.json`.

## Testy (EditMode, ~31)
`DispatchForecastTests` (7), `DispatchDecisionTests` (7, +kaskada), `DispatchSafeStateTests` (4,
deadlock), `DispatchWeightTests` (4, obłożenie), `DispatchPolicyTests` (4, holdBias+persist),
`DispatchIntegrationTests` (3, orchestracja + Off-gate), `OvertakePriorityTests` (4, charakteryzacja
reaktywnej warstwy — stan sprzed M-Dispatch).

## Odpowiedź na pierwotne pytanie (po M-Dispatch)
Spóźniony pośpieszny nadjeżdża → forecast wykrywa konflikt na wspólnym bloku → dispatcher **trzyma
osobowego** (do 30 min, jeśli `waga×czas` się opłaca względem opóźnienia pośpiesznego), **chyba że**:
trzymanie domknęłoby zakleszczenie (→ puść, rozbij korek), za osobowym stoi cięższa kolejka
(kaskada → puść), albo osobowy jest zapchany pasażerami (jego waga rośnie → może przeważyć). W
godzinach szczytu osobowy dostaje boost priorytetu (9) i sam może mieć pierwszeństwo. Polityka
gracza (Off/Balanced/Punctuality) modyfikuje skłonność do trzymania.
