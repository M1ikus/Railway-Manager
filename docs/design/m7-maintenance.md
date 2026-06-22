# M7 — Maintenance (utrzymanie taboru)

> **Status:** Plan (pre-implementation), 2026-04-19
> **Zależności:** M-Fleet ✅ (data model, 12 komponentów, reliability), M6 ✅ (Ekonomia, koszty), M9 ✅ (ruch — eventy km traveled), M8 Personel (częściowe — placeholder pensji warsztatu do M7, realne do M8)
> **Cel:** Pełny gameplay loop utrzymania — degradacja, awarie, przeglądy, naprawy.

---

## 1. Filozofia

Tabor w realu nie jest perpetuum mobile — **każdy kurs to zużycie**. Gracz musi planować:
- **Krótkotrwałe przeglądy** (P1, P2) — codzienne/tygodniowe, w depocie, tanie
- **Okresowe** (P3, P4) — kosztowne, wyłączają pojazd na dni/tygodnie
- **Wielkie rewizje** (P5) — raz na dekady, drogie, długotrwałe

**Dwie ścieżki per pojazd:**
1. **Własny warsztat** — player buduje infrastrukturę w depocie, kupuje części, ale przegląd "kosztuje" tylko parts + pensje (taniej długoterminowo)
2. **Zewnętrzny ZNTK** — wyślij pojazd do Mińska/Nowego Sącza/Bydgoszczy, zapłać fixed fee, nie potrzebujesz infrastruktury (ale drożej per przegląd + pojazd OOS przez delivery time + praca + delivery back)

**Awarie na trasie = dramat:**
- Pociąg staje, próbuje się sam naprawić
- Jeśli self-repair fail → player musi wysłać rescue (dowolna wolna loko z kompatybilną trakcją)
- Blokuje sekcję, opóźnia kolejne kursy, reputacja -15 do -30

**Tempo degradacji** zależy od:
- Przebieg (km/tick) → każdy komponent zużywa się inaczej
- `reliabilityScore` pojazdu (nowszy = wolniej)
- `componentRiskFactors` per-seria (EN57 drzwi 3× częściej niż norma; EU160 wszystko 0.7×)

---

## 2. Rozstrzygnięte decyzje

| ID | Pytanie | Decyzja |
|---|---|---|
| **D1** | Inspection intervals | Per-seria, plik `Assets/StreamingAssets/Fleet/inspection_intervals.json` keyed by seriesId, fallback na `FleetBalanceConstants.Default*` |
| **D2** | Self-repair mechanika | Pociąg staje → mechanic attempt przez 5-15 min game time → success chance = `50% × componentHealth/100`. 100% = 50% szansa, 20% = 10%, 0% = nigdy. Balance w M6.5 |
| **D3** | Rescue locomotive | Dowolna wolna loko z kompatybilną trakcją. Post-1.0: odczepienie lok ze stojącego składu (udrożnienie szlaku) |
| **D4** | Warsztaty | **System budynków** w depocie — player buduje pomieszczenia (`WallBuildingSystem` + `RoomDetectionSystem`), stawia equipment (maszyny), po spełnieniu wymogów pomieszczenie staje się warsztatem danego poziomu |
| **D5** | External ZNTK | Hardcoded lista 5-6 realnych ZNTK (Mińsk Maz., NEWAG Nowy Sącz, PESA Bydgoszcz, ZNTK Poznań, H. Cegielski, Kolzam Racibórz) w `external_workshops.json` z specjalizacjami + lokalizacjami OSM |
| **D6** | Component risk per-seria | Pole `componentRiskFactors` w `NewVehicleModel`/`FleetMarketVehicle`/`FleetVehicleData` — 12 floatów per-komponent. Dane początkowe w `fleet_catalog_ea.json`, rebalans w M6.5 |
| **D7** | Parts inventory | 12 typów części 1:1 z komponentami (silnik, hamulec, drzwi, klima, pudło, koła, instalacja, wnętrze, światła, toalety, pantograf, sprzęg) |
| **D8** | Koszty: własny warsztat | Parts wymienione + pensje pracowników (M8, placeholder do M7) — brak bezpośredniego fee |
| **D9** | Koszty: ZNTK | Fixed rate per przegląd (wszystko wliczone — parts, pensje, zysk ZNTK). ~40% drożej niż self-build longterm |

---

## 3. Komponenty — 12 typów

Z `VehicleComponents` (po M-Fleet-1):

| Komponent | Degradacja | Typowe awarie |
|---|---|---|
| `engineCondition` | 0.008%/km (Electric), 0.015%/km (Diesel) | Awaria napędu → pociąg staje |
| `brakeCondition` | 0.012%/km | Awaria hamulca → safety stop, reputation -20 |
| `doorsCondition` | 0.006%/km + 1.0%/cykl drzwi | Drzwi się nie zamykają → dwell +60s |
| `acCondition` | 0.003%/km (zima 0.005%) | Klimatyzacja padła → comfort -20% |
| `bodyCondition` | 0.001%/km | Korozja pudła → wizualne, wpływ reputation |
| `wheelsCondition` | 0.020%/km | Obcieranie kół → vibracje, dwell penalty. P3 = obrócenie. P4 = wymiana |
| `electricalCondition` | 0.005%/km | Instalacja padła → różne problemy (oświetlenie, info pasażerskie) |
| `interiorCondition` | 0.004%/km + pasażerowie | Zużyte wnętrze → comfort spadek (tapicerka, monitoring) |
| `lightsCondition` | 0.008%/km | Padłe reflektory → safety restrict przy złej widoczności |
| `toiletsCondition` | 0.005%/km + użycie | WC padły → comfort spadek, reputation -2 per kurs |
| `pantographCondition` | 0.007%/km (tylko Electric) | Padły pantograf → pociąg traci zasilanie → stop |
| `couplingCondition` | 0.004%/km + 0.5%/sprzęganie | Sprzęg padł → nie można podpiąć wagonów |

Wszystkie multiplikowane przez:
- `(2 - reliabilityScore/100)` — nowy pojazd (rel=90) ma 1.1×, stary (rel=40) = 1.6×
- `componentRiskFactors.X` — starsze serie mają specific componenty gorsze

---

## 4. Inspection intervals per-seria (D1)

### Data model

```csharp
[Serializable]
public class InspectionIntervals
{
    public int p1LimitHours;      // default 72
    public int p2LimitDays;       // default 28
    public int p3LimitKm;         // default 250_000
    public int p4LimitKm;         // default 500_000
    public int p4LimitYears;      // default 5
    public int p5LimitKm;         // default 3_000_000
    public int p5LimitYears;      // default 30
}
```

### Plik `inspection_intervals.json`

```json
{
  "intervals": [
    {
      "seriesId": "EN57",
      "p1LimitHours": 48,
      "p2LimitDays": 14,
      "p3LimitKm": 150000,
      "p4LimitKm": 400000,
      "p4LimitYears": 4,
      "p5LimitKm": 2000000,
      "p5LimitYears": 20
    },
    {
      "seriesId": "FLIRT_ED160",
      "p1LimitHours": 120,
      "p2LimitDays": 42,
      "p3LimitKm": 350000,
      "p4LimitKm": 700000,
      "p4LimitYears": 8,
      "p5LimitKm": 4000000,
      "p5LimitYears": 40
    }
  ]
}
```

Stare pojazdy → **krótsze intervals** (wymagają częściej). Nowe → dłuższe.

### Lookup

`InspectionCatalog.GetIntervals(seriesId)` zwraca `InspectionIntervals` z pliku lub `FleetBalanceConstants.DefaultInspection*` jako fallback.

### Refactor `InspectionSchedule`

- `GetStatus(level, intervals, nowGameTime, mileage)` — przyjmuje intervals jako arg
- Const'y `P1_LIMIT_HOURS` etc. zostają jako **defaulty** w `FleetBalanceConstants`
- UI `FleetPanelUI.Inspection` pobiera intervals z catalog per-vehicle

---

## 5. Component risks per-seria (D6)

### Data model

```csharp
[Serializable]
public class ComponentRiskFactors
{
    public float engine = 1.0f;
    public float brake = 1.0f;
    public float doors = 1.0f;
    public float ac = 1.0f;
    public float body = 1.0f;
    public float wheels = 1.0f;
    public float electrical = 1.0f;
    public float interior = 1.0f;
    public float lights = 1.0f;
    public float toilets = 1.0f;
    public float pantograph = 1.0f;
    public float coupling = 1.0f;
}
```

### W `fleet_catalog_ea.json` przykłady

**EN57 stara:**
```json
"componentRisk": {
  "engine": 1.5, "brake": 1.3, "doors": 3.0, "ac": 1.0,
  "body": 1.8, "wheels": 1.4, "electrical": 1.6, "interior": 2.0,
  "lights": 1.2, "toilets": 2.5, "pantograph": 1.3, "coupling": 1.4
}
```
"EN57 — drzwi padają notorycznie, wnętrze zużyte, toalety zawsze problem"

**EU160 Griffin:**
```json
"componentRisk": {
  "engine": 0.6, "brake": 0.7, "doors": 0.7, "ac": 0.7,
  "body": 0.6, "wheels": 0.8, "electrical": 0.6, "interior": 0.7,
  "lights": 0.6, "toilets": 0.7, "pantograph": 0.6, "coupling": 0.7
}
```

### Balance uwagi

Wartości **wytyczne** (do M6.5 Rebalance):
- Nowoczesne (2010+) → 0.5-0.8
- Standard średnie (1990-2010) → 0.9-1.1 (baseline 1.0)
- Stare (1970-1990) → 1.3-1.8
- Historyczne/wysłużone (<1970) → 2.0+

---

## 6. Degradacja per-km

`DegradationService` (Fleet/Runtime, singleton subskrybuje ruch):

```csharp
public class DegradationService : MonoBehaviour
{
    // Subskrybuje VehicleLocationService.UpdateRoutePosition (M9c hook)
    // lub TrainRunSimulator events.
    // Tracking last mileage per vehicleId → delta km → per-component degradation

    void OnVehicleMoved(int vehicleId, float deltaKm)
    {
        var v = FleetService.GetOwnedById(vehicleId);
        if (v == null) return;
        var risks = v.componentRisk ?? new ComponentRiskFactors();
        float relFactor = 2f - v.reliabilityScore / 100f; // 1.1 dla 90, 1.6 dla 40

        v.components.engineCondition    -= deltaKm * 0.00008f * risks.engine    * relFactor;
        v.components.brakeCondition     -= deltaKm * 0.00012f * risks.brake     * relFactor;
        // ... i tak dalej per komponent
        
        Clamp all to 0-100.
        v.conditionPercent = v.components.Average();
        v.mileageKm += deltaKm;
    }
}
```

---

## 7. Awarie runtime

### Breakdown event generation

Per tick (co sekundę game time):
```
foreach vehicle w OnRoute:
    foreach component:
        failureThreshold = (100 - componentHealth) / 100  // 0 at 100% health, 1 at 0%
        perSecondRisk = failureThreshold² × riskFactor × BreakdownBaseRate × 0.001
        if random() < perSecondRisk:
            triggerBreakdown(component)
```

### Types of breakdown → consequences

| Komponent | Gdy zepsuty w trasie |
|---|---|
| Engine | **CRITICAL** — pociąg staje, self-repair attempt |
| Pantograph (Electric) | **CRITICAL** — jak engine, pociąg traci zasilanie |
| Brake | **SAFETY** — pociąg zatrzymuje się na najbliższej stacji, reputation -20, kurs cancelled |
| Doors | **INCONVENIENCE** — dwell +60s per postój |
| AC | **COMFORT** — komfort -20% dla pasażerów (mniej revenue) |
| Wheels | **SAFETY** — pociąg ogranicza prędkość do 80 km/h |
| Lights | **NIGHT SAFETY** — po zmroku limit 60 km/h |
| Toilets | **COMFORT** — reputation -2 per kurs |
| Coupling | **BLOCKS SHUNTING** — nie można zmieniać składu |
| Electrical | **RANDOM** — losowe zepsucie: lights/doors/info |
| Body/Interior | Slow degradation, nie przerywa kursu |

---

## 8. Self-repair + Rescue flow (D2, D3)

### Self-repair

Gdy engine/pantograph padnie w trasie:
1. Pociąg staje między stacjami (blokuje sekcję)
2. UI notification: "Pociąg #123 awaria — mechanic próbuje"
3. Timer 5-15 min game time (random 300-900s)
4. `successChance = 0.5 × (componentHealth / 100)`
5. Random roll:
   - **Success** → komponent +20% health restore, pociąg rusza z delayed
   - **Fail** → "Wymaga ratownika" — lista wolnych lok pokazana
6. Reputation -3 nawet po sukcesie (opóźnienie)

### Rescue

Player wybiera lokomotywę z listy wolnych:
- Filter: status != OnRoute, != InRepair, != OnRoute; trakcja kompatybilna
- Dostawa rescue loko: pathfinding do miejsca awarii (TrainRunSim), ETA
- Dojechała → "podczepienie" → holowanie do **najbliższego depot** (ze slot'ami warsztatu dla tego poziomu naprawy)
- Reputation hit total: -15 (całość incydentu)
- Section blocked przez cały czas → ripple delays na kolejne kursy

### Auto-dispatch

Jeśli gracz nie zareaguje przez 15 minut game time, system wybiera dowolną kompatybilną wolną lok automatycznie. Log warn: "Auto-dispatch rescue #X".

### Post-1.0 (Q-B)

Odczepianie loko stojącego składu na szlaku → zostawia wagon, leci ratować, wraca, podpina → drożący reputation hit (pasażerowie w opuszczonym wagonie źli), ale szybsza reakcja.

---

## 9. Przeglądy — 2 ścieżki (D4, D5)

### Ścieżka A: Własny warsztat w depocie

**Wymagania:**
- Wybudowane pomieszczenie warsztatowe (D4) — patrz §10
- Odpowiedni poziom warsztatu dla poziomu przeglądu
- Parts w magazynie (D7) — patrz §12
- Pracownicy (M8) — placeholder do M7

**Flow:**
1. UI "Warsztat" w Depot panel → lista pojazdów overdue
2. Kliknij pojazd → wybierz slot (wolny warsztat odpowiedniego poziomu)
3. Pojazd jedzie na tor Workshop (DepotMovementSim), parkuje
4. Timer biegnie (czas przeglądu z §11 tabela)
5. Parts wymieniane automatycznie (odejmowane z magazynu; jeśli brak → pauza + alert)
6. Po ukończeniu:
   - `InspectionSchedule.Perform(level)` — reset
   - Komponenty restore (zależy od poziomu — P1 tylko drobne, P5 jak nowy)
   - Vehicle status = ReadyToSchedule

**Koszty:** parts + pensje (M8)

### Ścieżka B: External ZNTK

**Wymagania:** brak infrastruktury w depocie.

**Flow:**
1. UI "Warsztat" → "Wyślij do ZNTK"
2. Lista ZNTK z kompatybilnością (specjalizacja + level wymagany)
3. Pojazd jedzie do ZNTK (TrainRunSim na mapie 2D) — dostawa X godzin
4. Na miejscu → timer przeglądu (jak własny, ale +10-20%)
5. Wraca do home depot

**Koszty:** fixed fee ZNTK (wszystko wliczone). Plus opłata dostawy = paliwo × 2 × dystans.

### External ZNTK — hardcoded lista

Plik `Assets/StreamingAssets/Fleet/external_workshops.json`:

```json
{
  "workshops": [
    {
      "id": "ZNTK_MINSK_MAZ",
      "name": "ZNTK Mińsk Mazowiecki",
      "locationStationName": "Mińsk Mazowiecki",
      "canPerform": ["P1", "P2", "P3", "P4", "P5"],
      "specializations": ["ElectricLocomotive", "EMU"],
      "priceMultiplier": 1.0,
      "durationMultiplier": 1.1
    },
    {
      "id": "NEWAG_NOWY_SACZ",
      "name": "NEWAG — Nowy Sącz",
      "locationStationName": "Nowy Sącz",
      "canPerform": ["P3", "P4", "P5"],
      "specializations": ["EMU", "ElectricLocomotive", "PassengerCar"],
      "priceMultiplier": 1.15,
      "durationMultiplier": 0.9,
      "modernizationAvailable": true
    },
    {
      "id": "PESA_BYDGOSZCZ",
      "name": "PESA — Bydgoszcz",
      "locationStationName": "Bydgoszcz",
      "canPerform": ["P3", "P4", "P5"],
      "specializations": ["EMU", "DMU"],
      "priceMultiplier": 1.15,
      "durationMultiplier": 0.9,
      "modernizationAvailable": true
    },
    {
      "id": "ZNTK_POZNAN",
      "name": "ZNTK Poznań",
      "locationStationName": "Poznań Franowo",
      "canPerform": ["P1", "P2", "P3", "P4"],
      "specializations": ["PassengerCar", "DieselLocomotive"],
      "priceMultiplier": 0.95
    },
    {
      "id": "CEGIELSKI_POZNAN",
      "name": "H. Cegielski — Poznań",
      "locationStationName": "Poznań Główny",
      "canPerform": ["P3", "P4", "P5"],
      "specializations": ["PassengerCar"],
      "priceMultiplier": 1.1
    }
  ]
}
```

Modernizacje post-1.0 (upgrade EN57 → EN57AL, EU07 → EU07A) via NEWAG/PESA.

---

## 10. Warsztat — system budynków (D4)

Budynki warsztatów **integrują się z istniejącym WallBuildingSystem + RoomDetectionSystem** (M2.6 depot):

### Wymagania per poziom

| Poziom | Min. obszar pomieszczenia | Wymagane equipment | Max slotów |
|---|---|---|---|
| **Workshop_Basic** | 100 m² (10×10 min) | Podstawowe narzędzia, oświetlenie, tor warsztatowy `DepotTrackType.Workshop` | 1 |
| **Workshop_Medium** | 400 m² (20×20) | + Kanał rewizyjny (`UndergroundInspection` equipment), podnośnik lekki | 2 |
| **Workshop_Major** | 1000 m² (40×25) | + Podnośnik ciężki, obrabiarka do kół (`WheelLathe`), spawalnia | 3 |
| **Workshop_MainOverhaul** | 2500 m² (50×50 = ~1/4 depot) | + Suwnica mostowa, stanowisko demontażu wózków, malarnia | 1-2 |

### Capabilities matrix

| Warsztat | P1 | P2 | P3 | P4 | P5 |
|---|---|---|---|---|---|
| Basic | ✅ | ✅ | ❌ | ❌ | ❌ |
| Medium | ✅ | ✅ | ✅ | ❌ | ❌ |
| Major | ✅ | ✅ | ✅ | ✅ | ❌ |
| MainOverhaul | ✅ | ✅ | ✅ | ✅ | ✅ |

Każdy większy **zawiera capabilities** mniejszego (hierarchia).

### Detection algorithm

`WorkshopDetectionSystem` (nowy):
1. Iteruje pomieszczenia (`RoomDetectionSystem.Rooms`)
2. Dla każdego: oblicza powierzchnię, szuka equipment prefab'ów (już stawianych w depocie)
3. Matchuje wymagania → assigns `workshopLevel` do pomieszczenia
4. Update'uje dostępne sloty

### UI

Panel "Warsztaty" w `FleetPanelUI` lub oddzielny tab w Depot UI:
- Lista pomieszczeń + ich poziom
- Lista slotów (wolne/zajęte + pojazd + ETA)
- Przycisk "Przypisz pojazd" → lista overdue

---

## 11. Czasy i koszty przeglądów (D8, D9)

### Czasy (placeholder — M6.5 Rebalance)

| Poziom | Własny warsztat | External ZNTK |
|---|---|---|
| P1 | 2 h | 3 h |
| P2 | 8 h (1 dzień roboczy) | 10 h |
| P3 | 2 dni | 2.5 dni |
| P4 | 2 tygodnie | 2.5 tyg. |
| P5 | 2 miesiące | 2.5 mies. |

### Koszty własnego warsztatu (parts + pensje)

**Szacunkowo** (parts sum + pensje M8 placeholder):
- P1: 2k zł (drobne materiały + 2h mechanika)
- P2: 10k zł (zamiana elementów eksploatacyjnych)
- P3: 40k zł (wymiana kół, ~30k parts + 10k pracy)
- P4: 300k zł (wymiana silnika, hamulców, drzwi, ~200k parts + 100k pracy)
- P5: 2M zł (pełna rewizja, wymiana wszystkiego, ~1.5M parts + 500k pracy)

### Koszty ZNTK (fixed fee)

Multiplier × baseline:
- P1: 3k zł
- P2: 14k zł
- P3: 65k zł
- P4: 450k zł
- P5: 2.8M zł

+ koszt dostawy (paliwo × 2 × dystans).

**Oszczędność własnego warsztatu** (po amortyzacji infrastruktury): ~30-40% longterm dla P3+.

---

## 12. Parts inventory (D7)

### 12 typów części

Lista zgodna z komponentami (1:1):

| Typ części | Cena katalogowa | Waga/Zakres |
|---|---|---|
| Silnik trakcyjny | 150k zł | Ciężki, rzadko wymieniany (P4/P5) |
| Hamulec (zestaw tarczowy) | 25k zł | Średnio (P3/P4) |
| Drzwi kompletne | 15k zł | Często (P2+) |
| Klimatyzacja | 35k zł | Rzadko |
| Pudło (naprawa) | 50k zł | Bardzo rzadko (P5 → malowanie/spawanie) |
| Zestaw kołowy | 18k zł | Regularnie (P3 = obrócenie, P4 = wymiana) |
| Instalacja elektryczna (pakiet) | 40k zł | Okresowo |
| Tapicerka/wnętrze (pakiet) | 20k zł | P4/P5 |
| Reflektory + oświetlenie | 8k zł | Często |
| Toalety (pakiet) | 12k zł | Okresowo |
| Pantograf | 45k zł | Średnio (P3-P4) |
| Sprzęg UIC | 22k zł | Rzadko |

### UI Magazyn

Panel "Magazyn części" w Fleet UI:
- Lista typów + stan (np. "Silnik trakcyjny: 2 szt.")
- Przycisk "Zamów" → wybrać ilość → płatność → dostawa X dni
- Alerts: "Brak części X — pociąg #Y czeka na naprawę"

### Dostawa części

Dostarczane do **home depot** w N dni (default 3 dni). Część order → licznik → dodane do magazynu.

---

## 13. Architektura

### Nowe klasy

**`Fleet/Data/`:**
- `InspectionIntervals.cs` — POCO
- `ComponentRiskFactors.cs` — POCO
- `PartInventory.cs` — stan magazynu
- `ExternalWorkshop.cs` — POCO
- `WorkshopLevel.cs` — enum (Basic/Medium/Major/MainOverhaul)

**`Fleet/Catalogs/`:**
- `InspectionCatalog.cs` — static loader JSON `inspection_intervals.json`, lookup by seriesId
- `ExternalWorkshopCatalog.cs` — static loader `external_workshops.json`
- `PartCatalog.cs` — 12 typów z cenami (hardcoded w M7, potem JSON)

**`Fleet/Runtime/`:**
- `DegradationService.cs` — MonoBehaviour, subscribe ruch, per-km degradation
- `BreakdownService.cs` — per-tick probability check, emit breakdown events
- `RescueService.cs` — obsługa flow self-repair + rescue dispatch
- `InspectionScheduler.cs` — daily check overdue, alerts, UI trigger
- `WorkshopManager.cs` — zarządzanie slotami, wykonanie przeglądów
- `PartInventoryService.cs` — magazyn, zamówienia, alerts
- `WorkshopDetectionSystem.cs` (Depot) — matchuje pomieszczenia na poziomy warsztatu

**`Depot/`:**
- Nowe equipment prefaby (placeholder 3D): `Equipment_Kanal_Rewizyjny`, `Equipment_Podnosnik_Lekki`, `Equipment_Podnosnik_Ciezki`, `Equipment_WheelLathe`, `Equipment_Spawalnia`, `Equipment_Suwnica`
- `WorkshopBuildingType` enum — typ budynku

### Modyfikacje

**`Fleet/Data/FleetVehicleData.cs`:**
- `componentRisk: ComponentRiskFactors`
- Już ma `reliabilityScore`, `breakdownRiskFactor`, `maintenanceCostFactor`, `inspections`

**`Fleet/Data/NewVehicleModel.cs`, `FleetMarketVehicle.cs`:**
- `componentRisk: ComponentRiskFactors` — per-seria defaults (load z fleet_catalog)
- `inspectionIntervals: InspectionIntervals` (jeśli override per-seria, inaczej z catalog)

**`Fleet/Data/InspectionSchedule.cs`:**
- `GetStatus(level, intervals, nowGameTime, mileage)` — intervals jako argument
- Stare const'y P1_LIMIT_HOURS etc. → przeniesione do FleetBalanceConstants (default fallback)

**`Assets/StreamingAssets/Fleet/fleet_catalog_ea.json`:**
- Rozszerzenie 17 serii o `componentRisk`

**Nowe JSON:**
- `inspection_intervals.json` — per-seria intervals
- `external_workshops.json` — hardcoded 5-6 ZNTK

**`Depot/` integration:**
- `DepotTrackType.Workshop` — już istnieje ✅
- `WorkshopDetectionSystem` — nowy service

---

## 14. UI

### Panel "Warsztat" w Depot UI (nowa zakładka / tab)

- Lista pomieszczeń warsztatowych + ich poziomy
- Sloty (wolne/zajęte)
- Overdue vehicles queue
- Historia przeglądów

### Panel "Magazyn części" w Fleet UI

- 12 typów z stanem
- Zamówienia pending
- Historia zakupów

### Drill-down w "Mój tabor"

- Klik pojazd → szczegóły komponentów (12 pasków)
- Status każdego (OK / Usterki / Awaria)
- Historia przeglądów (MaintenanceRecord)
- Szacunek następnego przeglądu

### Alerty TopBar

- Licznik overdue przeglądów
- Breakdown event notifications
- Parts low-stock warnings

---

## 15. Podetapy implementacji

### M7-1 — Inspection intervals per-seria (2 sesje)
- POCO `InspectionIntervals` + `ComponentRiskFactors`
- Loader `InspectionCatalog`, plik `inspection_intervals.json` z danymi 17 serii
- Refactor `InspectionSchedule.GetStatus` na per-vehicle intervals
- Update UI `FleetPanelUI.Inspection`

### M7-2 — Component risks + degradacja (2 sesje)
- Dodać `componentRisk` do `fleet_catalog_ea.json` (17 serii)
- `DegradationService` subskrybuje `TrainRunSimulator.UpdateVisualPosition` (M9 delta km)
- Per-km per-component degradation
- Update conditionPercent aggregate

### M7-3 — Awarie + self-repair + rescue (3 sesje)
- `BreakdownService` per-tick probability ✅
- Breakdown types → konsekwencje (engine/brake/doors etc.) ✅
- Self-repair flow (timer + chance) ✅
- Rescue dispatch UI + auto-dispatch ✅ (MVP teleport)
- Section blocking + reputation hits ✅

### M7-3c — Rescue pathfinding (PRE-EA, user decision 2026-04-19) ✅ IMPLEMENTED 2026-04-20

**MVP zrobione (2026-04-20):**
- `RescueService` (MonoBehaviour singleton, Timetable/Maintenance) — per-update loop
- A* pathfinding (`RailwayPathfinder.FindPathByPosition`) od home depot → broken train
- ETA = pathLengthM / 80 km/h + 60s coupling (per fazę: Inbound + Returning)
- 2 fazy: **Inbound** (rescue loko jedzie do awarii) → **Returning** (holowanie do depot)
- Po Inbound: broken train despawn (state=Completed), rescue loko task=„Holowanie"
- Po Returning: wszystkie vehicles (rescue+broken) → InRepair, TrainRun cancelled,
  reputation hit (CanceledRun event)
- `RescueDispatchUI` — 2 tryby: dispatch (lista lok) + InProgress (countdown z phase label)
- Fallback instant-despawn jeśli graph/home-station niedostępne

**Nie w M7-3c MVP (post-EA polish):**
- Wizualizacja fizycznego ruchu rescue loko na mapie 2D (IdleVehicleVisualizer interpolation)
- Dedicated SimulatedTrain dla rescue runu via ad-hoc TrainRun
- Linked pair coupling w symulatorze

### M7-4 — Przeglądy + warsztaty (3 sesje)
- `InspectionScheduler` + `WorkshopManager`
- `WorkshopDetectionSystem` — analyze rooms, match to levels
- Equipment prefaby + UI budowania
- Scheduled inspections → slot assignment → timer → completion
- `InspectionSchedule.Perform` + component restore

### M7-5 — External ZNTK ✅ IMPLEMENTED 2026-04-20
- `ExternalWorkshop` POCO (Fleet/Data) — id, nazwa, stacja, canPerform, specializations,
  priceMultiplier, durationMultiplier, modernizationAvailable
- `external_workshops.json` (StreamingAssets/Fleet) — 5 realnych zakładów:
  ZNTK Mińsk Mazowiecki, NEWAG Nowy Sącz, PESA Bydgoszcz, ZNTK Poznań, H. Cegielski
- `ExternalWorkshopCatalog` (Fleet/Catalogs) — JsonUtility loader, GetById,
  FindCompatible(level, vehicleType)
- `WorkshopManager.SendToExternal(vehicleId, level, workshopId)` — A* pathfinding
  home depot → workshop station (via TimetableInitializer.FindStation + RailwayPathfinder),
  3 fazy: DeliveringOut → InInspection → DeliveringBack, per-tick advance
- Koszty: fee (baseline × priceMultiplier) + delivery (2 × km × op cost) przez
  EconomyManager.AddCost('maintenance_external')
- Inspection completion: InspectionSchedule.Perform, component restore, history record,
  reputation bonus dla P3+ (VehicleUpgrade event)
- UI WorkshopsPanelUI:
  - Drugi przycisk "ZNTK ▸" obok "Przydziel" w rzędzie overdue
  - Modal picker z listą kompatybilnych zakładów (cena, czas, stacja)
  - Sekcja "Zadania zewnętrzne (ZNTK)" w liście slotów (phase + ETA)

### M7-6 — Parts inventory ✅ IMPLEMENTED 2026-04-20
- `PartCatalog` (Fleet/Catalogs) — 12 typów 1:1 z ComponentType, hardcoded ceny + deliveryDays
  (Engine 150k/5d, Brake 25k/3d, Doors 15k/3d, AC 35k/4d, Body 50k/7d, Wheels 18k/3d,
  Electrical 40k/4d, Interior 20k/3d, Lights 8k/2d, Toilets 12k/3d, Pantograph 45k/4d, Coupling 22k/3d)
- `PartInventoryService` (Timetable/Maintenance) — singleton:
  - Stock per ComponentType (Dictionary)
  - Pending orders list z daysRemaining, OnDayEnded decrement → dodaj do stock
  - OrderParts(type, quantity) — sprawdza kasę, AddCost('parts'), dodaje pending + history
  - ConsumePart(type, count) — decrement, log warning gdy brak (MVP nie blokuje)
  - HasStock / GetStock helpers
- `PartsPanelUI` (Timetable/Maintenance) — fullscreen panel:
  - Lewa: lista 12 typów z stock + cena + deliveryDays + przyciski +1/+5/+20
  - Prawa góra: pending orders (ETA, koszt), dolna: historia ostatnich 30 zakupów
  - Bg coloring: brak (czerwony), low <3 (bursztynowy), OK (niebieski)
- `SceneController.PendingPartsPanel` flag + zakładka "Magazyn" (U+25A9) w MainTabBarUI
- WorkshopManager.CompleteInspection → `ConsumePartsForInspection(level)`:
  - P1/P2: 0 parts (drobne materiały)
  - P3: 1× Wheels
  - P4: 1× Brake + 1× Doors + 1× Wheels
  - P5: Engine + Interior + Electrical + Body + Wheels + Pantograph
- MVP: gdy brak części → log warning, przegląd NIE jest blokowany (pełna blokada w M7-7)

**Nie w M7-6 MVP (post-EA / M7-7):**
- Blokada przeglądów przy brak parts
- UI alerty low-stock w TopBar
- Auto-reorder (threshold-based) — już zagadka na M6.5 Rebalance
- External ZNTK parts usage (teraz tylko internal consumes)

### M7-7 — UI polish ✅ IMPLEMENTED 2026-04-20
- **Parts blocking w AssignVehicle** (WorkshopManager) — P3-P5 wymaga zapasu części,
  blokada + log gdy brak. UI w WorkshopsPanelUI: przycisk "Przydziel" → "Brak części"
  (grayout) + tooltip pokazuje co brakuje
- **GetMissingPartsLabel(level)** — static helper, zwraca string z brakującymi (dla UI) lub null
- **MaintenanceAlertsUI** (Timetable/Maintenance) — floating badges w prawym górnym rogu:
  - "⚠ N overdue" (czerwony) → klik otwiera Warsztaty
  - "✖ N awarie" (jasnoczerwony) → klik otwiera Warsztaty
  - "⊞ N brak części" (bursztynowy) → klik otwiera Magazyn
  Refresh co 1s, badges ukryte gdy count=0, sortingOrder 220 (pod fullscreen panels)
- **Drill-down komponentów w FleetPanelUI.DetailPopup** — 12 pasków (label + bar +
  procent), auto-skrywa komponenty z -1 (N/A dla typu pojazdu), kolorystyka wg
  GetConditionColor (zielony>70, pomarańczowy 40-70, czerwony<40)
- **Historia przeglądów w DetailPopup** — ostatnie 6 MaintenanceRecord (najnowsze
  na górze, format "D{day} {recordType} -{cost}zł")

**Nie w M7-7 MVP (post-EA polish):**
- Slot picker (zamiast auto-pick) — czekać na feedback playtest'u czy auto wystarczy
- Auto-reorder parts threshold-based — czekać na M6.5 Rebalance
- Alerty z sound notifications
- TopBar integration (teraz floating pod TopBar'em, nie w samym pasku)

**Razem: 15-16 sesji** — duży milestone.

---

## 16. Integracja z innymi systemami

### M6 Ekonomia
- Koszty przeglądów i napraw → `EconomyManager.AddCost('maintenance')`
- Koszty parts → `EconomyManager.AddCost('parts')`
- Koszty external ZNTK → `EconomyManager.AddCost('external_workshop')`

### M8 Personel
- Pracownicy warsztatu = mechanic (rola z M8)
- `skill` wpływa na czas naprawy
- Pensje = cost w naprawach "u siebie"

### M9 Ruch
- `TrainRunSimulator.UpdateVisualPosition` emit km traveled → DegradationService
- Breakdown blocks section
- Rescue uses `DepotMovementSimulator.SpawnConsistAtEntry` + `TrainRunSim.SpawnTrainFromVehicles`

### M-Fleet
- `componentRisk` w `fleet_catalog_ea.json`
- `inspection_intervals.json` per-seria

### M12c Random events
- Rare breakdowns przy 100% health (~0.01%/day) — fluff
- Seasonal: zima AC mniej się psuje, lato więcej

### M13 Save/Load
- `VehicleComponents`, `InspectionSchedule` per-pojazd → serialize
- `PartInventory` state
- `WorkshopManager` sloty + timers

---

## 17. Balance constants

`FleetBalanceConstants.Maintenance`:

```csharp
// Base degradation rates (per km, per 1.0 risk, per 1.0 rel factor)
public const float DegradeEnginePerKm = 0.00008f;        // 80k km for 100→0
public const float DegradeBrakePerKm = 0.00012f;
// ... per każdy komponent

// Breakdown base rates
public const float BreakdownBaseRatePerSecond = 0.00002f;  // × failureThreshold² × risk

// Self-repair
public const float SelfRepairBaseChance = 0.5f;
public const int SelfRepairMinSeconds = 300;
public const int SelfRepairMaxSeconds = 900;
public const float SelfRepairHealthRestore = 20f;

// Rescue
public const int AutoDispatchRescueAfterSeconds = 900;

// Reputation hits
public const int RepBreakdownOnRoute = -15;
public const int RepRescueRequired = -10;
public const int RepBrakeEmergencyStop = -20;
public const int RepPunctualInspectionBonus = +2;

// Czasy przeglądów [game hours]
public const int InspectionHoursP1_Internal = 2;
public const int InspectionHoursP2_Internal = 8;
public const int InspectionDaysP3_Internal = 2;
public const int InspectionDaysP4_Internal = 14;
public const int InspectionDaysP5_Internal = 60;

// Koszty własnego warsztatu (gr, parts + pensje baseline)
public const int InspectionCostP1_Internal_Groszy = 200_000;     // 2k zł
public const int InspectionCostP2_Internal_Groszy = 1_000_000;   // 10k
public const int InspectionCostP3_Internal_Groszy = 4_000_000;   // 40k
public const int InspectionCostP4_Internal_Groszy = 30_000_000;  // 300k
public const int InspectionCostP5_Internal_Groszy = 200_000_000; // 2M

// External ZNTK mnożnik (1.5x = 50% drożej)
public const float ExternalZNTKMultiplier = 1.5f;

// Parts (gr)
public const int PartPriceEngine = 15_000_000;   // 150k
// ...
```

Wszystko do weryfikacji w **M6.5 Rebalance** (post-M13).

---

## 18. Pytania otwarte (do dopracowania)

- **Q-H** Zawartość equipment prefabs — czy implementujemy placeholder 3D (proste kubiki) czy zostawiamy do M-Models?
- **Q-I** Jak gracz wybiera który kurs wysłać do przeglądu — ręcznie czy auto-suggest gdy overdue?
- **Q-J** Warsztat jako stacjonarny tor (Workshop type) — czy pojazd musi dojechać do konkretnego toru w pomieszczeniu? Czy dowolny tor w pomieszczeniu liczy się?
- **Q-K** Losowe awarie (post-EA M12d) — random events: "zła pogoda, wczoraj było -20°C, wszystkie klimatyzacje się wyłączyły"?
