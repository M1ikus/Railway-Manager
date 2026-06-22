# Balance constants & tuning

> **Cel:** Dokumentacja wszystkich wartości kalibracyjnych w grze.
> **Dlaczego** konkretne wartości? Kiedy je zmieniać? Jak testować regresję?

---

## Zasada: wartości w jednym miejscu

**Konwencja:** Wszystkie magic numbers wpływające na balans/gameplay są w dedykowanych plikach `*Constants.cs`. NIE wrzucaj hardkodowanych wartości w logikę biznesową.

**Istniejące pliki:**
- `Assets/Scripts/Timetable/Catalogs/TimetableTuningConstants.cs` — M4 Timetable
- `Assets/Scripts/Fleet/Catalogs/FleetConstants.cs` — M3 Fleet
- (planowane) `EconomyConstants.cs` — M6
- (planowane) `MaintenanceConstants.cs` — M7
- (planowane) `PersonnelConstants.cs` — M8

**Anti-patterns:**
- ❌ `transform.Rotate(Vector3.right, 180f * Time.deltaTime)` — magic 180
- ❌ `if (pociag.mileageKm > 250000)` — magic 250000
- ✅ `if (pociag.mileageKm > MaintenanceConstants.P3MileageKm)`

---

## M4 Timetable (`TimetableTuningConstants.cs`)

### Fizyka uproszczona

| Stała | Wartość | Uzasadnienie |
|-------|---------|--------------|
| `DefaultAccelerationMps2` | 0.8 | Średnia dla polskich pociągów. EZT szybciej (0.9-1.0), loko+wagony wolniej (0.6-0.7) |
| `DefaultDecelerationMps2` | 1.0 | R regime hamulec (pospieszny). G byłby wolniejszy (0.5), R_Mg szybszy (1.5) |
| `EmuAccelBonus` | 1.15 | +15% dla `CompositionMode.MultipleUnit` (napęd rozłożony na wielu członach) |
| `DecelMultiplierByBrake.G` | 0.5 | Hamulec towarowy — wolne napełnianie przewodu |
| `DecelMultiplierByBrake.P` | 0.8 | Pasażerski |
| `DecelMultiplierByBrake.R` | 1.0 | Pospieszny (baseline) |
| `DecelMultiplierByBrake.R_Mg` | 1.4 | R + magnetyczny — krótszy czas hamowania |
| `DecelMultiplierByBrake.R_E` | 1.3 | R + elektrodynamiczny |

**Jak kalibrować:** Jeśli czas jazdy na trasie testowej (Olsztyn → Elbląg 97km) znacząco odbiega od realnego rozkładu PKP (np. ~75 min dla RO), modyfikuj `DefaultAcceleration` / `DefaultDeceleration`. Nie zmieniaj bez triangulacji z rzeczywistością.

### Vmax fallback (`LineUsageSpeedCatalog`)

Gdy OSM edge nie ma `maxspeed` tag:

| Kategoria OSM | Vmax [km/h] |
|---------------|-------------|
| `usage=main` | 140 |
| `usage=branch` | 120 |
| `usage=industrial` / brak | 100 |
| `service=spur` | 60 |
| `service=siding` / `service=yard` | 40 |

**Uzasadnienie:** Realne Vmax na polskich liniach (PKP PLK). Main lines (E20/E30/E65) = 140-160, ale konserwatywnie 140.

### Reverse times

| Composition | Czas [min] | Uzasadnienie |
|-------------|------------|--------------|
| `EmuReverseMin` | 2 | Maszynista przechodzi do drugiej kabiny, test hamulca |
| `LocoReverseMin` | 10 | Odczepienie lok, objazd składu, doczepienie od drugiego końca, test hamulca, podpięcie przewodu |

### Klasyfikacja kategorii (`CategoryClassifier`)

| Parametr | Wartość | Uzasadnienie |
|----------|---------|--------------|
| `StopRatioPassenger` | 0.80 | ≥80% postojów → osobowy (RO/MO) |
| `StopRatioExpress` | 0.40 | <40% postojów + Vmax ≥160 → ekspres (EI) |
| Pomiędzy → pospieszny (RP/MP) | | Domyślnie |
| `NightHoursStart` | 22 | 22:00 — nocne (MH, EN) |
| `NightHoursEnd` | 4 | 04:00 |
| `ExpressMinVmaxKmh` | 160 | Minimum dla kategorii ekspresowych |

### Aglomeracje

| Parametr | Wartość |
|----------|---------|
| `AgglomerationMinStationCount` | 2 |

**Uzasadnienie:** Miasto jest "aglomeracyjne" jeśli ma ≥2 stacje (np. Warszawa Centralna + Warszawa Wschodnia). Mniej → zwykła stacja miejska.

---

## M3 Fleet (`FleetConstants.cs`)

### Start gry

| Parametr | Wartość |
|----------|---------|
| `StartingMoneyPln` | 150000 |

**Uzasadnienie:** Pozwala kupić 1-2 używane pojazdy + małe inwestycje. EN57 używany kosztuje ~80k, EU07 używany ~150k.

### Rynek wtórny

Parametry losowości:
- **TBD**: scatter dla cen używanych vs cena nowego
- **TBD**: współczynnik stanu (condition) wpływu na cenę

### Seat layout constraints

| Parametr | Wartość |
|----------|---------|
| `MinCorridorWidthM` | 0.6 | Minimalna szerokość korytarza w wagonie |
| `MinSeatSpacingM` | 0.7 | Minimalna odległość między rzędami foteli |
| `MaxDoorPairsPerCoach` | 2 | Max 2 pary drzwi (lewa+prawa) per człon |

---

## M6 Ekonomia (planowane, `EconomyConstants.cs`)

### Popyt pasażerski (agent-based)

| Parametr | Wartość propozycja | Uzasadnienie |
|----------|---------|--------------|
| `BasePassengersPerCapita` | 0.05 | 5% populacji miasta/dzień generuje popyt na transport kolejowy |
| `RushHourMultiplier` | 1.5 | Godziny szczytu (6-9, 15-18) |
| `WeekendMultiplier` | 0.8 | Weekend — mniej dojazdów do pracy |
| `AgglomerationBonus` | 1.3 | Aglomeracje mają wyższy popyt |
| `TourismSeasonMultiplier` | 1.2 | Lipiec-sierpień, weekendy majowe |

### Ceny biletów

| Kategoria | Base [PLN] | Per km [PLN] |
|-----------|------------|--------------|
| `Os` | 8 | 0.25 |
| `Przysp` (RP) | 12 | 0.35 |
| `IC` | 30 | 0.50 |
| `EIP` | 60 | 0.70 |

(Wartości w `CommercialCategory`, nie hardkodowane — gracz może tworzyć własne kategorie)

### Koszty

| Parametr | Wartość propozycja |
|----------|---------|
| `ElectricityPerKwhPln` | 0.8 |
| `DieselPerLiterPln` | 6.5 |
| `InfraAccessFeePerKm` | 15 |
| `MonthlyBasicMaintenance%` | 1 |

---

## M7 Utrzymanie (planowane, `MaintenanceConstants.cs`)

### Degradacja

| Parametr | Wartość |
|----------|---------|
| `ConditionDegradationPer100Km` | 0.5 |
| `OverloadDegradationMultiplier` | 2.0 |
| `BreakdownThreshold` | 10 | <10% = awaria |
| `WarningThreshold` | 30 | <30% = alert |

### Przeglądy (`InspectionSchedule.cs` — już istnieje)

| Poziom | Limit |
|--------|-------|
| P1 | ≥72h gry |
| P2 | ≥28 dni gry |
| P3 | ≥250 000 km |
| P4 | ≥500 000 km LUB 5 lat |
| P5 | ≥3 000 000 km LUB 30 lat |

**Uzasadnienie:** Real PKP standards dla taboru pasażerskiego.

---

## M8 Personel (planowane, `PersonnelConstants.cs`)

### Wynagrodzenia miesięczne

| Rola | Base skill 3★ | Skill 1★ | Skill 5★ |
|------|-------|------|------|
| `MaszynistaSalaryBasePln` | 6000 | 4500 | 9000 |
| `MechanikSalaryBasePln` | 4500 | 3500 | 7000 |
| `KonduktorSalaryBasePln` | 3500 | 2800 | 5000 |
| `SprzątaczSalaryBasePln` | 2800 | 2300 | 4000 |

**Uzasadnienie:** Przybliżone real polskie stawki (2025) + scaling per skill.

### Fatigue

| Parametr | Wartość |
|----------|---------|
| `FatigueAccumulationPerHour` | 8 (0-100%, 12.5h max shift) |
| `FatigueReductionPerHourRest` | 12 (8h rest = pełna regeneracja) |
| `MaxFatigueForOperation` | 80 | Powyżej → pracownik nie przychodzi |

---

## M12d Random events

| Event | Prawdopodobieństwo/dzień | Efekt |
|-------|-------------------------|-------|
| `VehicleBreakdown` | 1% per pojazd | Pociąg staje na trasie |
| `EmployeeStrike` | 0.1% | 20% personelu nie przychodzi |
| `HolidayRush` | 5% na święta | Popyt x1.5 |
| `Storm` | 0.5% | Wyłączenie odcinka na 2-6h |

---

## Jak testować regresję balansu

Gdy zmieniasz stałą z tej listy:

1. **Uruchom smoke test** zgodnie z `docs/TESTING.md` sekcja odpowiedniego milestone'u
2. **Zmierz kluczowe metryki:**
   - Czas gry do zrównoważenia finansowego (start 150k, kiedy balans dodatni?)
   - Ile pociągów gracz kupuje w pierwszych 10 dniach gry?
   - Ile pasażerów przewozi dziennie?
3. **Porównaj z baseline** (poprzedni commit)
4. **Jeśli regresja** (np. gra staje się niemożliwa do przejścia) — rollback lub dostosuj inne stałe

**Notuj zmiany balansu w commit message:**
```
balance: +10% RushHourMultiplier, -5% BasicMaintenance
Rationale: gra była zbyt trudna ekonomicznie w pierwsze 30 dni.
Tested: smoke test playthrough 60 min, saldo +8% vs baseline.
```

---

## Changelog zmian kalibracji

**Format:** Data — co zmieniono — dlaczego — efekt.

- 2026-04-14 — initial draft — — —
- (TBA) — zmiany wprowadzone w czasie iteracji M4.5+ — —
