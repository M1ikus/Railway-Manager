# M6 — Ekonomia (agent-based pasażerowie, pełny cykl dochodowy)

> **Status:** Plan (pre-implementation), 2026-04-19
> **Zależności:** M3 (Fleet — ceny zakupu), M4 (Timetable — kategorie IRJ, trasy), M5 (Circulations — assignment), M9 (Movement — boarding/alighting przy arrival)
> **Cel:** Zamknąć pierwszy pełny game loop: buy → schedule → operate → earn → reinvest.
> **Ostatnia aktualizacja:** 2026-04-19

---

## 1. Kontekst i filozofia

M6 to **fundament gameplay'u ekonomicznego**. Dotychczas gracz może kupować pociągi, tworzyć rozkłady, aktywować obiegi i obserwować ruch — ale bez jakichkolwiek konsekwencji finansowych. M6 dokłada:

1. **Pasażerowie** — fizyczne byty stojące na stacjach, chcący dojechać do konkretnych celów
2. **System biletów** — gracz ustala ceny, pasażer decyduje czy warto jechać
3. **Dochody** — pociąg przejeżdżając bierze pasażerów → pieniądze do kasy
4. **Koszty operacyjne** — paliwo/energia, opłaty za używanie infrastruktury, overhead zajezdni
5. **Dotacje regionalne** — osobowe mogą być nierentowne "rynkowo" ale otrzymują dopłaty od województw (warunki: minimum kursów, cena ≤ limit)
6. **Bilans dzienny** — UI panel pokazuje P&L za każdy dzień, ranking linii/obiegów

### 1.1 Model pasażera — agent-based (lekka wersja)

**Rozstrzygnięte:** agent-based, nie agregaty. Każdy pasażer to osobny obiekt z:
- Pochodzenie (stationId)
- Cel (stationId)
- Preferowany czas wyjazdu (godzina)
- Pula pieniędzy (skłonność do płacenia wyższej ceny za wygodę)
- Cierpliwość (ile minut poczeka na opóźniony pociąg zanim zrezygnuje)
- Preferencje (prefers comfort / cheapest / fastest)

**Dlaczego agent-based (nie agregaty):**
- Realna konkurencja w MP (pasażer wybiera operatora per-podróż)
- Emergent behavior — korki, kolejki, frustracja
- Łatwiejsze balansowanie (widzisz konkretne decisions, nie tylko liczby)
- Lekka wersja = ~1000-5000 agents aktywnych jednocześnie (nie 10000+)

**Performance target:** **50 000 aktywnych agentów** na mapie Polska przy 60 FPS na średnim PC. Spawn demand-driven — agenty pojawiają się tylko tam gdzie istnieją aktywne rozkłady (brak oferty = brak agentów). Liczba rośnie organicznie z rozwojem firmy gracza (więcej linii = więcej pasażerów).

### 1.2 Filozofia balance

- **Mała firma (1 linia) musi się utrzymać** przy optymalizacji
- **Duża (10+ linii) zarabia realistycznie** — może rozbudowywać
- **Towarowy > Ekspres > Pospieszny > Osobowy** w profitability
- **Osobowy break-even z dotacją**, dochód bez niej w godzinach szczytu
- **Bankructwo jest możliwe** ale wymaga serii złych decyzji

---

## 2. Rozstrzygnięte decyzje designowe

| ID | Pytanie | Decyzja |
|---|---|---|
| **D1** | Model pasażerów | Agent-based. **Target 50k aktywnych agentów na mapie Polska**, spawn demand-driven (tylko tam gdzie są rozkłady), skaluje z ofertą |
| **D2** | Bilety per kurs vs odcinek | **Per odcinek** — przesiadki rozliczane osobno, każdy operator dostaje swoją część. **Bez transfer discount** (D-Q3 rozstrzygnięte) |
| **D3** | MP popyt | **Wspólny pool pasażerów** — konkurencja o tego samego klienta |
| **D4** | Cennik — forma | **Step pricing** per kategoria handlowa (user-defined z M4.5 CategoryEditorUI). Tiers: np. `[(0-25km: 5zł), (25-50: 10zł), (50-100: 15zł), (>100: 20zł + 0.1zł/km)]`. Brak klas wagonu; promocje per linia → post-EA |
| **D5** | Towarowy (freight) | **MVP M6 = tylko pasażerski.** Freight oddzielny system → post-EA |
| **D6** | Kredyty / pożyczki | Post-EA |
| **D7** | Reputation System | **Multi-level:** globalna + per-województwo + per-miejscowość. Start 50/100, zakres 0-100. **Brak decay** (nie spada sama). Tabor swap old→new na trasie → **+X boost** |
| **D8** | Dotacje regionalne | Osobowe → dopłaty wojewódzkie przy warunkach (≥N kursów/dzień, cena ≤ limit, kategoria Regional) |
| **D9** | Modyfikatory popytu | **Wszystkie z listy w MVP:** sezonowość/godziny/święta + cena vs baseline (elastyczność) + częstotliwość oferty + reputation + konkurencja + long-term trend + B5 commute patterns (małe→duże rano, odwrotnie wieczorem) |
| **D10** | Baza popytu | **Station-based, nie city-based.** `stationImportance` obliczany z OSM metadata (isMajorStation, perony, węzeł, populacja najbliższego miasta, nazwa "Główny/Centralna"). Plus override file JSON dla strategicznych relacji |
| **D11** | Revenue split przy przesiadce | Każdy odcinek → cena według taryfy kategorii tego pociągu. Bez transfer discount |
| **D12** | Koszty operacyjne | Paliwo/energia per km + **track access fee** per km + **platform fee** za każdy postój + miesięczne pensje (post-M8, placeholder teraz) |
| **D13** | UI bilansu | **Nowa zakładka "Finanse"** w Depot UI (nie popup). Heatmap aktualnego popytu via klik na stację → ilość oczekujących pasażerów |
| **D14** | Abandon rate | **60 min** baseline. Per-kategoria modifier → post-EA |

---

## 3. Reputation — szczegóły (D7)

**Start:** 50/100 globalna, 50/100 per każde województwo, 50/100 per każda stacja.
**Zakres:** 0-100 (hard clamp).
**Decay:** BRAK — reputation zmienia się tylko po zdarzeniach.

### Eventy wpływające

| Event | Zmiana | Gdzie |
|---|---|---|
| Opóźnienie 1-5min na kursie | -0.5 | globalna + województwa trasy + stacji końcowych |
| Opóźnienie 5-15min | -2 | |
| Opóźnienie >15min | -5 | |
| Odwołanie kursu | -10 | globalna + województwa + wszystkie stacje trasy |
| Niesprawny skład (awaria w trasie, blokada sekcji) | -15 | |
| Wypadek (kolizja / wykolejenie) | -30 | |
| Punktualność 100% w ciągu dnia (wszystkie kursy operatora) | +1 | globalna |
| Swap pojazdu old (>15 lat) → new (<5 lat) na linii | +3 | per linia (województwa/stacji trasy) |

**Rozchodzenie eventu per-level:**
- Globalna: zawsze triggerowana
- Województwo: rekurencyjnie na województwach które kurs przejeżdża
- Stacja: na stacjach uczestniczących w evencie (startowa, końcowa, ew. stacja awarii)

### Wpływ na popyt (B3)

```
reputationFactor(fromStation, toStation) =
  0.5 × (stationRep(from) + stationRep(to)) / 50
  × globalRep / 50
  × avg(voivodeshipsOnRoute) / 50
```

Reputation 50 = neutral factor 1.0. Reputation 80 = factor ~1.5. Reputation 20 = factor ~0.5.

---

## 4. Algorytm popytu — formuła pełna (D9)

Zamiast prostych mnożników — kompozycja czynników:

```
effectiveDemand(fromStation, toStation, now) =
      baseDemand(fromStation, toStation)             // D10 stationImportance × importance / f(dist)
  ×   timeModifier(now)                              // rush hours + day + season + holiday + commute B5
  ×   priceElasticity(currentPrice, perceivedValue)  // B1 elastyczność cenowa
  ×   offerFrequencyBonus(runsPerDay)                // B2 więcej kursów = więcej chętnych
  ×   reputationFactor(from, to)                     // B3 — patrz sekcja 3
  ×   competitionFactor(other operators MP)          // B4
  ×   longTermTrend(rolling 30d rep avg)             // B6
```

Szczegóły każdego czynnika w podetapie M6-5.

---

## 5. Cennik — step pricing (D4)

### Data model

`PricingTier` — pojedynczy krok cennika:
```csharp
public class PricingTier
{
    public int fromKm;           // np. 0
    public int toKm;             // np. 25 (exclusive)
    public int priceGroszy;      // cena w tym przedziale (fixed)
    public int perKmAboveGroszy; // dodatek za każdy km powyżej (dla ostatniego tier'u)
}
```

`CommercialCategory` (rozszerzenie istniejącego typu z M4.5) — dostaje pole:
```csharp
public List<PricingTier> pricingTiers = new();
```

### Przykład dla kategorii "Pospieszny (TLK)"

```
[(0, 25, 500, 0),        // 0-25km: 5zł fixed
 (25, 50, 1000, 0),       // 25-50km: 10zł fixed
 (50, 100, 1500, 0),      // 50-100km: 15zł fixed
 (100, 99999, 2000, 10)]  // >100km: 20zł + 10gr za każdy km powyżej 100
```

Pasażer jadący 150 km: 2000 + 50×10 = 2500 gr = 25 zł.

### UI edycja

`CategoryEditorUI` (istniejący z M4.5) dostaje sekcję "Cennik":
- Lista tier'ów (row per tier z fromKm/toKm/price/perKmAbove)
- Przyciski "+ Dodaj próg", "× Usuń"
- Walidacja: ciągłość tier'ów (next.fromKm == prev.toKm), ostatni tier musi mieć toKm=∞ + perKmAbove

---

## 6. Architektura (uaktualnione)

---

## 4. Architektura

### 4.1 Nowe typy

#### `Core/Economy/PassengerAgent.cs` (nowy)

Lekki POCO (nie MonoBehaviour) — tylko dane + logika state'u. Silnik trzyma pool agentów w `PassengerManager`.

```csharp
public enum PassengerState
{
    WaitingAtStation,    // Na peronie, czeka na pociąg
    Boarding,             // Wsiada (krótka animacja / 1 tick)
    OnTrain,              // Jedzie (w środku pociągu)
    Alighting,            // Wysiada
    Arrived,              // Dojechał do celu — punkt zdobyty, agent removal
    Abandoned,            // Zrezygnował (przekroczona cierpliwość)
}

public class PassengerAgent
{
    public int agentId;
    public int originStationId;
    public int destinationStationId;
    public float spawnTimeSec;        // game time kiedy pojawił się na peronie
    public float abandonTimeSec;      // game time kiedy zrezygnuje (spawnTime + cierpliwość)
    public PassengerState state;
    public int currentStationId;      // aktualna pozycja (zmienia się przy przesiadce)
    public int currentTrainRunId = -1; // -1 gdy na stacji, id kursu gdy on train
    public float walletPln;           // budżet (dla rezygnacji przy drogich biletach)
    public PassengerPreference preference; // comfort/cheapest/fastest
    public int paidTotalPln;          // ile już zapłacił (do statistics)
}

public enum PassengerPreference { Cheapest, Fastest, MostComfort }
```

#### `Core/Economy/StationImportance.cs` (nowy)

Per-station factor wyliczony z OSM metadata.

```csharp
public static class StationImportance
{
    /// <summary>Oblicza factor ważności stacji przy starcie gry (cached).</summary>
    public static float Calculate(RailwayStation station, IReadOnlyList<CityPlace> places,
                                   IReadOnlyList<StationPlatform> platforms,
                                   PathfindingGraph graph);

    // Komponenty:
    // + 1.0 base
    // + 2.0 jeśli isMajorStation (railway=station w OSM)
    // + 0.3 × platformCount
    // + 1.0 jeśli węzeł (≥3 linie wychodzące z grafu)
    // + log(population) × 0.5 dla najbliższego miasta w 3km
    // + 2.0 jeśli nazwa zawiera "Główna"/"Centralna"/"Główny"
}
```

Wyniki typowe:
- Warszawa Centralna: ~8-10
- Olsztyn Główny: ~4-6
- Halt w małej miejscowości: ~1.5
- Halt bez przypisanego miasta: ~1

#### `Core/Economy/OriginDestinationMatrix.cs` (nowy)

Static lookup base demand między parami stacji. Generowany raz przy starcie gry.

```csharp
public class OriginDestinationMatrix
{
    /// <summary>Key: hash(fromStationId, toStationId), Value: base passengers/day.</summary>
    Dictionary<long, float> _baseDemand;

    /// <summary>Overrides z pliku JSON — strategiczne relacje tuning'owane manualnie.</summary>
    Dictionary<long, float> _overrides;

    public float GetBaseDemand(int fromStationId, int toStationId);
    public void Build(IReadOnlyList<RailwayStation> stations);
    public void LoadOverrides(string path = "StreamingAssets/Economy/demand_overrides.json");
}
```

Algorytm Build (station-based, nie city-based):
```
baseDemand(from, to) =
    stationImportance(from) × stationImportance(to)
  × distanceFactor(kmDist)  // peak ~50-200km, spadek dla bardzo bliskich i bardzo dalekich
  ÷ calibrationK
```

Override file (JSON) pozwala ustawić konkretne pary ręcznie (np. `"Warszawa Centralna → Kraków Główny": 8000`).

#### `Core/Economy/ReputationManager.cs` (nowy)

Multi-level reputation (D7).

```csharp
public class ReputationManager : MonoBehaviour
{
    public static ReputationManager Instance;

    public int Global;                                    // 0-100, start 50
    public Dictionary<string /*voivodeship*/, int> ByVoivodeship;
    public Dictionary<int /*stationId*/, int> ByStation;

    public void ApplyEvent(ReputationEvent evt);
    public int GetForRoute(IReadOnlyList<int> stationsOnRoute);
    public int GetForStation(int stationId);

    public event Action<ReputationEvent> OnReputationChanged;
}

public struct ReputationEvent
{
    public ReputationEventType type; // DelayMinor/Medium/Major, CanceledRun, Breakdown, Accident, Punctual, VehicleUpgrade
    public int valueDelta;            // pre-defined w BalanceConstants
    public IReadOnlyList<int> affectedStations;
    public IReadOnlyList<string> affectedVoivodeships;
    public string reason;             // debug / UI
}
```

#### `Core/Economy/PassengerManager.cs` (nowy)

Central manager — spawnuje, tick'uje, despawnuje agentów.

```csharp
public class PassengerManager : MonoBehaviour
{
    public static PassengerManager Instance;

    List<PassengerAgent> _activeAgents;
    OriginDestinationMatrix _odMatrix;

    // Spawn control
    float _spawnAccumulator;
    const float SpawnIntervalSec = 10f; // co 10 sek game time = evaluate spawns

    void FixedUpdate()
    {
        if (GameState.IsPaused) return;
        TickAgents(Time.fixedDeltaTime * GameState.TimeScale);
        MaybeSpawnAgents();
    }

    void TickAgents(float deltaSec) { ... }
    void MaybeSpawnAgents() { ... } // używa OD matrix + modifiers

    // API dla M9 movement — gdy pociąg przyjeżdża na stację
    public void OnTrainArriving(int trainRunId, int stationId);
    public void OnTrainDeparting(int trainRunId, int stationId);
    public List<PassengerAgent> GetWaitingAt(int stationId, int toStationId);
}
```

#### `Core/Economy/TicketSystem.cs` (nowy)

Kalkulacja ceny biletu dla danego odcinka (fromStation → toStation).

```csharp
public static class TicketSystem
{
    public static int CalculatePriceGroszy(
        int fromStationId, int toStationId,
        IrjCategory category,
        float kmDistance);

    // Komponenty: base per km × category multiplier × route-specific fees
    // Example EIP: 40 gr/km, OS: 12 gr/km
}
```

#### `Core/Economy/EconomyManager.cs` (nowy)

Zbiera przychody + koszty, liczy bilans dzienny, archiwizuje.

```csharp
public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance;

    // Running state (dzisiaj)
    long _revenueTodayGroszy;
    long _costsTodayGroszy;
    Dictionary<int /*circulationId*/, LineBalance> _lineBalances;

    // History
    List<DailyBalance> _dailyBalanceHistory;

    void OnDayEnded() // wywoływane gdy GameDay inkrementuje
    {
        // Archiwizuj do history, reset running, pay subsidies
    }

    public void AddRevenue(int circulationId, int amountGroszy, string source);
    public void AddCost(string category, int amountGroszy, string source);
    public DailyBalance GetRunningToday();
}

public class DailyBalance
{
    public string dateIso;
    public long revenueGroszy;
    public long costsGroszy;
    public long subsidiesGroszy;
    public long netGroszy => revenueGroszy - costsGroszy + subsidiesGroszy;
    public Dictionary<int, LineBalance> perLine;
}
```

#### `Core/Economy/CostCalculator.cs` (nowy)

Przychody są event-driven (passenger boards → pay). Koszty są time-driven + event-driven:
- Per km (paliwo/energia) — naliczane przy `TrainRunSimulator.UpdatePosition`
- Per dzień (overhead zajezdni, pensje — jeśli M8 nie istnieje to placeholder)
- Per transakcja (kupno/sprzedaż taboru, dotacje)

```csharp
public static class CostCalculator
{
    public static int GetKmCostGroszy(TractionType traction, int speedKmh);
    public static int GetDailyOverheadGroszy(); // placeholder do M8
    public static int GetPlatformFeeGroszy(int stationId); // opłata za użytkowanie peronu
}
```

#### `Core/Economy/SubsidyCalculator.cs` (nowy)

Sprawdza na koniec dnia czy obieg kwalifikuje się do dotacji wojewódzkiej.

```csharp
public static class SubsidyCalculator
{
    public static int CalculateDailySubsidy(Circulation c, DailyBalance balance);
    // Warunki: >=N kursów dziennie + średnia cena biletu <= limit + category = Regional
    // Wartość: fixed per tramwaj lub %revenue
}
```

#### `Core/Economy/DemandModifiers.cs` (nowy)

Wszystkie mnożniki popytu w jednym miejscu.

```csharp
public static class DemandModifiers
{
    public static float GetHourOfDay(int hour); // rush hours 7-9 + 16-19
    public static float GetDayOfWeek(DayOfWeek day); // weekend vs workday
    public static float GetSeason(DateTime date); // wakacje, zima
    public static float GetHoliday(DateTime date); // święta, urlopy

    public static float Combined(DateTime gameTime) =>
        GetHourOfDay(gameTime.Hour) * GetDayOfWeek(gameTime.DayOfWeek)
      * GetSeason(gameTime) * GetHoliday(gameTime);
}
```

### 4.2 Modyfikacje istniejących klas

#### `Timetable/Runtime/Simulation/TrainRunSimulator.cs`
- Event `OnTrainArrivingAtStop(int trainRunId, int stationId)` emitowany przy każdym postoju
- Event `OnTrainDepartingFromStop(int trainRunId, int stationId)` — po odjeździe
- `PassengerManager.OnTrainArriving/OnTrainDeparting` subskrybują

#### `Timetable/Data/Timetable.cs`
- Nowe pole: `public int pricePerKmGroszyOverride = -1;` (gdy ustawione, nadpisuje cenę z kategorii)

#### `Timetable/UI/TimetableCreatorUI.cs`
- Dodać sekcję "Ceny" — numeric input dla price override

#### `SharedUI/TopBarUI.cs`
- Dodać pieniądze przycisk otwiera bilans dzienny popup

### 4.3 Diagram flow

```
Start gry:
  OriginDestinationMatrix.Build() — gravity model na miastach

Game loop (co FixedUpdate):
  PassengerManager.FixedUpdate:
    _spawnAccumulator += delta
    if _spawnAccumulator >= SpawnIntervalSec:
      MaybeSpawnAgents() — iteruj OD matrix × current modifiers, spawn agents
      TickAgents() — update state (check abandon, board if train arrives)

Pociąg przyjeżdża na stację:
  TrainRunSimulator emit OnTrainArrivingAtStop(runId, stationId)
  PassengerManager.OnTrainArriving:
    Dla każdego agenta on-train → czy jego cel = current stop?
      Tak → Alighting → Arrived → remove agent, no more revenue
    Dla każdego agenta waiting at this station → czy jego cel jest dalej w trasie?
      Tak → Boarding → OnTrain → TicketSystem.CalculatePrice → EconomyManager.AddRevenue

Pociąg odjeżdża:
  OnTrainDeparting(runId, stationId) — wszyscy on-train tego runu ruszają z nim

Koniec dnia (GameState.GameDay++):
  EconomyManager.OnDayEnded:
    Dla każdej Active Circulation: SubsidyCalculator → Add subsidy if eligible
    Archiwizuj DailyBalance, reset running
    Show notification "Bilans dnia: +/- X zł"
```

---

## 5. Podetapy implementacji

### M6-1: Foundation (2-3 sesje)

**Cel:** infrastruktura — agenty, manager, OD matrix.

- [ ] `PassengerAgent` POCO + `PassengerState` enum
- [ ] `OriginDestinationMatrix` z gravity model (pop × pop / dist²)
- [ ] `PassengerManager` MonoBehaviour singleton (bootstrap w TrainRunSim.Awake jak M9c services)
- [ ] `MaybeSpawnAgents` — spawn per OD pair co interval
- [ ] `TickAgents` — update state, abandon check
- [ ] Debug ContextMenu: "Dump all passengers", "Spawn 10 test agents"
- [ ] Wizualizacja debug: kropka na stacji z licznikiem agentów (overlay)

**Exit:** agents spawn'ują się na stacjach, stoją, abandon po 30min, debug UI pokazuje stan.

### M6-2: Boarding/Alighting (1-2 sesje)

**Cel:** pociąg zabiera pasażerów.

- [ ] `TrainRunSimulator.OnTrainArrivingAtStop/DepartingFromStop` events
- [ ] `PassengerManager.OnTrainArriving` handler — boarding logic
- [ ] Pathfinding pasażera: prosty heurystyka "jakikolwiek pociąg jadący w moim kierunku" (D-Q2 decision)
- [ ] Capacity check (pociąg ma X miejsc — z FleetVehicleData.passengerSeats sumowane z consist'u)
- [ ] Passenger.currentTrainRunId updated, moved to OnTrain list
- [ ] Alighting przy celu — agent state=Arrived, remove

**Exit:** pociąg zatrzymuje się, X pasażerów wsiada, Y wysiada. Licznik debug.

### M6-3: Ticket + Revenue (1 sesja)

**Cel:** gracz zarabia.

- [ ] `TicketSystem.CalculatePriceGroszy` — base × category × distance
- [ ] Per odcinek (D2): jeśli przesiadka, 2 tickets — każdy operator dostaje swoją część
- [ ] `EconomyManager.AddRevenue` wywoływane przy boarding
- [ ] `Timetable.pricePerKmGroszyOverride` field + UI w TimetableCreator
- [ ] `GameState.Money` aktualizowane live (albo per event albo na koniec dnia — decyzja D-Q?)

**Exit:** `GameState.Money` rośnie gdy pociągi jeżdżą. TopBar pokazuje live.

### M6-4: Costs + Balance (1-2 sesje)

**Cel:** koszty i bilans dzienny.

- [ ] `CostCalculator` — per km (paliwo/energia), daily overhead (placeholder), platform fees
- [ ] `EconomyManager.AddCost` wywoływane w `TrainRunSimulator.UpdatePosition` (per km)
- [ ] `DailyBalance` struktura + archiwizacja
- [ ] `EconomyManager.OnDayEnded` — reset running, add subsidies
- [ ] UI popup "Bilans dzienny" z TopBar (tabela: przychody/koszty/dotacje/net, per linia)

**Exit:** UI pokazuje bilans dzienny. Gracz widzi czy zarabia.

### M6-5: Modifiers (1 sesja)

**Cel:** dynamika popytu.

- [ ] `DemandModifiers.GetHourOfDay` (rush hour peaks)
- [ ] `DemandModifiers.GetDayOfWeek` (workday vs weekend)
- [ ] `DemandModifiers.GetSeason` (wakacje, zima)
- [ ] `DemandModifiers.GetHoliday` (lista świąt PL w `BalanceConstants`)
- [ ] `PassengerManager.MaybeSpawnAgents` używa Combined modifier
- [ ] Debug: overlay na mapie "current demand multiplier: 1.5x (rush hour)"

**Exit:** spawn rate pasażerów fluktuuje wg pory dnia/dnia/sezonu.

### M6-6: Subsidies + basic reputation (1 sesja)

**Cel:** osobowe uzyskują dotacje, reputation tracked.

- [ ] `SubsidyCalculator` — rules (N kursów/dzień, cena ≤ limit, kategoria Regional)
- [ ] Reputation value 0-100 w `GameState`
- [ ] Reputation events (D-Q5): +1 punktualność, -5 odwołanie, etc.
- [ ] UI: reputation bar w TopBar (basic, post-M12d unlocki)

**Exit:** osobowe obiegi dostają dotacje. Reputation zmienia się widocznie.

### M6-7: UI polish (1-2 sesje)

**Cel:** gracz ma wgląd w biznes.

- [ ] `DailyBalancePopupUI` — tabela per linia + wykres 7-dniowy
- [ ] `PassengerInfoOverlay` — hover stacji → popup "pasażerowie czekający: 5"
- [ ] `LineStatisticsUI` — rozszerzenie Circulation UI o stats (P&L tej linii, liczba pasażerów)
- [ ] Accessibility: kolory zgodne z balance, high contrast

**Exit:** gracz używa UI do decyzji biznesowych.

---

## 6. Edge cases

- **Brak pasażerów na stacji** — gracz nie wie czy trasa jest słaba, czy brak na mapie. Heat map overlay (post-MVP, M12).
- **Pociąg pełny** — kolejni nie wsiadają, frustracja. Abandon może być cancelled (agent czeka następny). D-Q7 rozważenie.
- **Pociąg się spóźnia** — agent abandon calculated from actual arrival, nie planowanej. Punktualność wpływa na reputation (M6-6).
- **Wiele operatorów (MP)** — agent wybiera najtańszego w tej samej godzinie, jeśli oba dostępne. Draw → random choice.
- **Tymczasowy brak pociągu na linii** — obieg paused, OD pair bez realizacji. Pasażerowie czekają → abandon. Gracz pewnie straci reputation.

---

## 7. Testy ręczne

Każdy etap kończy się smoke testem:

1. **M6-1:** ContextMenu "Spawn 10 agents na Olsztyn Główny, cel: Ełk". Debug dump pokazuje. Po 30 min gry — agents abandoned.
2. **M6-2:** Stwórz 1 circulation Olsztyn → Ełk. Spawn agents. Przy przyjeździe — wsiadają. Przy Ełku — wysiadają. Liczniki OK.
3. **M6-3:** Po pełnym kursie sprawdź `GameState.Money` wzrost. Kontrola ręczna: N passengers × price = oczekiwany profit.
4. **M6-4:** Po 1 dniu gry otwórz bilans dzienny popup. Sprawdź breakdown.
5. **M6-5:** TimeScale 150 żeby dzień minął szybko. Spawn rate fluktuuje wg godziny.
6. **M6-6:** Stwórz osobowy obieg z 3+ kursami, cena 10gr/km → dotacja aktywowana.
7. **M6-7:** UI pokazuje wszystko czytelnie.

---

## 8. Integracja z innymi systemami

### M4 Timetable
- `Timetable.pricePerKmGroszyOverride` — nowe pole, UI w kreatorze
- `IrjCategory` → base price mapping w `TicketSystem`

### M5 Circulations
- `Circulation.steps` — wykorzystywane do dotacji (liczba kroków dziennie)
- Passenger pathfinding iteruje TrainRun'y z active circulations

### M9 Movement
- `TrainRunSimulator.OnTrainArrivingAtStop/DepartingFromStop` events (nowe)
- `FleetVehicleData.passengerSeats` — capacity check przy boarding

### M7 Maintenance (przyszłe)
- `CostCalculator` zostanie rozszerzony o koszty napraw (naliczane per breakdown event)

### M8 Personel (przyszłe)
- `CostCalculator.GetDailyOverhead` → realne pensje personelu (place holder do M8)

### M13 Save/Load
- `PassengerManager._activeAgents` — serializowane
- `EconomyManager._dailyBalanceHistory` — serializowany
- `OriginDestinationMatrix` — rebuildowany przy load (nie serializowany, deterministic z danych miast)

---

## 9. Balance constants (draft)

Wszystko w `BalanceConstants.Economy` — do weryfikacji w M6.5 Rebalance.

```csharp
public static class EconomyBalance
{
    // Agent spawn
    public const int MaxActiveAgents = 50_000;                // target performance
    public const float SpawnIntervalSec = 10f;                // co 10s eval spawns
    public const float StationImportanceCalibrationK = 0.05f; // normalizacja OD matrix

    // Patience
    public const float AbandonPatienceMinutes = 60f;          // D14

    // Cennik — default step pricing (per kategoria — user może edytować w CategoryEditorUI)
    // Przykład default dla "Osobowy (OS)":
    //   [(0-25km, 4zł), (25-50, 7zł), (50-100, 11zł), (100-∞, 15zł + 8gr/km)]
    // Definiowane w default category templates w FleetCatalog.

    // Operating costs (per km)
    public const int CostPerKmElectricGroszy = 250;           // 2.50 zł/km
    public const int CostPerKmDieselGroszy = 400;             // 4.00 zł/km
    public const int TrackAccessFeePerKmGroszy = 150;         // 1.50 zł/km (PKP PLK-like, D12)

    // Platform fees (per postój)
    public const int PlatformFeeMajorStationGroszy = 5000;    // 50 zł — duża stacja
    public const int PlatformFeeSmallStationGroszy = 1000;    // 10 zł — mała
    public const int PlatformFeeHaltGroszy = 200;             // 2 zł — halt

    // Overhead (gr/dzień) — placeholder do M8 (realne pensje)
    public const int DailyOverheadGroszy = 500_000;           // 5000 zł

    // Subsidies (dotacje wojewódzkie)
    public const int MinRunsPerDayForSubsidy = 4;
    public const int MaxPricePerKmForSubsidyGroszy = 14;
    public const int SubsidyPerRunGroszy = 15_000;            // 150 zł per qualifying run

    // Reputation events — delta per event (D7 table)
    public const int RepDelayMinor = -1;        // <5min (halved w tabeli: -0.5, ale int, więc -1 rzadziej applied)
    public const int RepDelayMedium = -2;       // 5-15min
    public const int RepDelayMajor = -5;        // >15min
    public const int RepCanceledRun = -10;
    public const int RepBreakdownOnRoute = -15;
    public const int RepAccident = -30;
    public const int RepPunctualDay = +1;
    public const int RepVehicleUpgrade = +3;    // swap old→new na linii

    // Reputation start
    public const int RepStart = 50;
    public const int RepMin = 0;
    public const int RepMax = 100;
}
```

---

## 10. Follow-ups (post-M6)

- **Cargo system** (freight) — osobny milestone post-EA
- **Kredyty / pożyczki** — post-EA
- **Reputation unlocki** — M12d
- **Advanced passenger AI** (multi-operator choice, learning preferences) — post-EA
- **Heat map overlay** popytu — M12
- **Dynamic pricing** (algorithm suggesting prices) — post-EA

---

## 11. M-PaxV2 — klasy biletowe, cel podróży, przesiadki (2026-06-03)

Rozszerzenie modelu pasażera ponad M6 (gap-fix po audycie ekonomii). Trzy fazy, wszystkie z testami EditMode.

### Faza A — klasy biletowe + cennik per-klasa
- **`ClassFare`** (`Data/ClassFare.cs`): stawka per `SeatZoneType` (zone + pricingTiers LUB base+perKm).
- **`CommercialCategory.classFares`**: lista `ClassFare` — taryfa per klasa. `firstClassMultiplier` legacy.
- **`TicketSystem.CalculatePriceGroszy(category, SeatZoneType, km)`**: per-klasa (FindClassFare → tiers/base+perKm, fallback do stawki domyślnej kategorii). `GetClassRate(category, zone, out base, out perKm)` dla through-fare.
- **Boarding per-klasa** (`PassengerManager`): `CalculateCapacityByClass(run)` sumuje `seatBreakdown` per typ (pusty → SecondClassOpen fallback; cache). Pasażer wsiada tylko gdy wolne miejsce w `desiredClass`; płaci stawkę swojej klasy. Backward-compat: brak seatBreakdown → cała pojemność jako SecondClassOpen.

### Faza B — cel podróży napędza klasę / budżet / porę
- **`PassengerPurposeModel`** (`Runtime/Economy/`): `TripPurpose {Commute, Business, Leisure, Tourism}`. `Pick(rng, hour, day)` time-weighted (rush/offpeak/weekend), `PreferredClass(rng, purpose)` (Business 1. klasa-heavy, Commute zawsze 2.), `WillingnessGroszy(rng, purpose)` (base per cel × jitter).
- **`PassengerAgent.purpose` + `desiredClass`**: przydzielane przy spawn. `SpawnAgent(from, to, gameTime)` — portfel/klasa z celu (nie losowe).
- Stałe w `TimetableTuningConstants` (PurposeWeights*, PurposeFirstClassShare*, PurposeWillingness*).

### Faza C — przesiadki (through-fare)
- **Graf osiągalności** (`JourneyGraphBuilder`): stacja → `DirectEdge` (dystans + kategoria) z par przystanków PH każdego rozkładu; dedup → najkrótszy. Budowany w `TryBuildODMatrix` (rebuild razem z OD matrix).
- **Planer** (`PassengerJourneyPlanner.FindJourney`): direct → 1 → 2 przesiadki, preferuje najmniej, deterministyczny (≤2 przesiadki = ≤3 odcinki). `PassengerJourney`/`JourneyLeg`/`DirectEdge` w `PassengerJourney.cs`.
- **Through-fare** (`ThroughFareCalculator`, decyzja user'a): per-km KAŻDEGO odcinka × dystans + opłata bazowa RAZ (najwyższa wśród odcinków). `ComputeTotalGroszy` = Σ `LegContributionGroszy` (spójność: ile płaci pasażer == ile trafia do obiegów; base przypisany do odcinka o najwyższym base).
- **Integracja w żywej symulacji** (C.2c, `PassengerManager`):
  - Cache podróży per para OD (`_journeyCache`; null = brak trasy też cache'owana). `PassengerAgent.currentLegIndex` — jedyne nowe pole (podróż z cache, zero per-agent GC).
  - **Spawn:** brak trasy ≤2 przesiadki → NIE spawnuj (pasażer bez połączenia się nie pojawia). Sprawdzane dopiero po bramce prawdopodobieństwa.
  - **Maszyna stanów boardingu:** wysiadka gdy stacja == cel bieżącego odcinka (`CurrentLegTarget`); finalny cel → Arrived, węzeł → `currentLegIndex++` + `transferCount++` + wraca WaitingAtStation (czeka na następny kurs). Multi-leg → through-fare (stać-na-całość sprawdzane RAZ na 1. odcinku, płaci wkład bieżącego odcinka per obieg); direct (1 odcinek / brak trasy) → `TicketSystem` tiers-aware bez zmian.

### Testy
`TicketClassPricingTests`, `PassengerBoardingRevenueTests` (A), `PassengerPurposeModelTests` (B), `PassengerJourneyPlannerTests`, `JourneyGraphAndFareTests` (C.2a/b), `PassengerTransferJourneyTests` (C.2c end-to-end: A→B→przesiadka→B→C, through-fare 2300 gr split 300/2000 per obieg).

---

## 12. M-Economy — osobny asmdef + waluta + pełna pętla pieniędzy (2026-06-07)

Audyt ekonomii (user) wykazał luki: **zakup taboru był DARMOWY**, budowa DARMOWA, część wydatków
Fleet/Depot omijała bilans. Naprawione + ekstrakcja architektoniczna. Gałąź `feat/economy`.

### Architektura — osobny asmdef
- **`RailwayManager.Economy`** (refs tylko Core) — warstwa 2 obok Fleet/SharedUI. Powód: `EconomyManager`
  mieszka w Timetable; Fleet/Depot są poniżej i nie mogły go wołać → zakup/budowa nie trafiały do bilansu.
- **`MoneyLedger`** (static, w Economy) = JEDYNY ruchacz `GameState.Money` + sumy dzienne + breakdown
  per-kategoria + `Spend/Earn/CanAfford` + difficulty mult (operacyjne) + akumulator sub-zł. `long`
  (koszty budowy >21M zł przekraczają int). `AddCost/AddRevenue/AddSubsidy` int→long.
- **`EconomyManager`** (zostaje w Timetable) = fasada operacyjna (per-obieg/historia/dotacje) — deleguje
  ruch pieniędzy do MoneyLedger, sumy pass-through. 19 callerów + FinancePanelUI + EconomySavable bez zmian.

### Waluta wyświetlania (Faza 2)
- **`Currency`** {PLN/EUR/USD/CZK} + **`CurrencyService`** (Core, obok Money): kursy stałe (tunable),
  `ConvertFromPln`, `Symbol` (zł/€/$/Kč). Baza wewnętrzna ZAWSZE PLN-grosze; konwersja tylko do prezentacji
  (NIE forex — pełny forex post-EA). Wybór w Settings→Język (persist PlayerPrefs), `NumberFormatService`
  konwertuje + symbol, TopBar live-refresh. Default PLN = backward-compat.

### Naprawione wydatki/przychody (Fazy 3-4)
- **Zakup taboru (KRYTYCZNY bug):** `CartProcessor.TryCheckout` — affordability + ProcessOrder +
  `MoneyLedger.Spend("vehicle_purchase")`; blokada „nie stać → nie kupuj" (UI: przycisk nieaktywny +
  suma czerwona). Wcześniej checkout tylko logował sumę, nie pobierał.
- **Przepięcie wydatków Fleet/Depot** (modernizacja/modyfikacja/malowanie/self-paint/outdoor/dostawa/
  rozbudowa) z bezpośredniego `GameState.Money -=` na `MoneyLedger` → trafiają do bilansu. Personnel/
  Maintenance już były śledzone (`AddCost`).

### Koszty budowy (Faza 5) — `ConstructionCosts` + `ConstructionBilling`
- Kalkulatory [gr] wg `ConstructionConstants` (tory/sieć per km, rozjazdy per typ, pomieszczenia per m²,
  outdoor per typ) + placeholdery (mebel/turntable/pitlift/washzone/water — WYMYŚLONE, tunable M-Balance).
- **`ConstructionBilling`**: `TryCharge` (polityka „nie stać → nie buduj") + `Refund` (undo/usunięcie) +
  **`SuppressCharging`** (init zajezdni + load save = setup, nie budowa → darmowe; wrap DepotManager.Start
  + DepotSavable.DeserializeIntoScene).
- **6 builderów wpiętych:** meble (charge place / refund delete / suppress-move), outdoor (charge place),
  pomieszczenia (delta per m², auto-type + popup), tory (charge w PlaceTrackVisuals / refund
  RemoveTrackInternal / suppress split-merge-rebuild / block w SM), rozjazdy (mechanizm + refund),
  sieć (per km electrify/de-electrify/remove). Undo = net-zero (build→remove→refund, remove→replace→charge).
- **Limitacje rozjazdów DOMKNIĘTE (TD-035, 2026-06-10):** krzyżownica pobiera mechanizm 900M gr
  (`KrzyzownicaPodwojnaGroszy` zmapowana w `TurnoutGroszy` — wcześniej martwa stała, fallback łapał
  „190"→350M); rozjazd powrotny brancha pobiera mechanizm; para/branch atomowe (pre-check sumy);
  remove = pełne lustro place (members+pre/post refundowane, restore CHARGED → cykl place→remove
  i undo net-zero z konstrukcji — wcześniej dwie drukarki pieniędzy: +T(pre+post)−T(odnóg) na zwykłym
  rozjeździe i +3.5 mln zł/cykl na krzyżownicy z dziurą w torze).
- **Limitacje (M-Balance/follow-up):** budżet startowy (100M) vs realne ceny = kalibracja przy M-Balance
  (TD-036); procent odzysku <100% przy rozbiórce = ew. jedna stała na cały system budowy.

### Testy
`MoneyLedgerTests`, `CurrencyAndFormatTests`, `CartCheckoutTests`, `ConstructionCostsBillingTests`,
`DepotConstructionBillingPlayTests` (PlayMode, TD-035: net-zero cykli place→remove)
(czyste foundationy + capital). Wiring builderów scenicznych = compile + code-reading (nie unit-test).
Regresja końcowa 512 EditMode + 24 PlayMode.
