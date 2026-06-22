# Fleet Catalog — EA + 1.0 lista taboru

> **Status:** Spec M-Fleet (data-only), 2026-04-19
> **Zakres:** Wszystkie pojazdy planowane na EA i launch 1.0. Data-only — modele 3D żyją w M-Models (pre-M14).
> **Single source of truth** dla danych taboru. Zastępuje rozproszone wzmianki w GDD/asset-pipeline/new_models.json.

---

## 1. Filozofia listy

Podział wynika z `docs/design/asset-pipeline.md` (rodziny modelowe).

**EA:** minimum do zamkniętego game loop — tani klasyk z rynku wtórnego + nowoczesny zakup z konfiguratora. Kontrast gameplayowy ("stary gracz vs nowy gracz") z czytelną drabinką upgrade'ów.

**1.0:** poszerzenie wyboru, moderyzacje klasyków, drabinka prestiżu (EU200 200km/h, Newag Impuls I, Pesa Elf I).

**Post-1.0 / DLC / updates:** premium (Pendolino), wielosystemowe (Vectron), fan-service (EP05).

**Brak w ogóle:** pojazdy towarowe (nie ma towarów w EA/1.0), ciężkie lokomotywy freight (ET22/ET41 poza zakresem).

---

## 2. Zakres EA (17 serii + wagony modularne)

### 2.1 Lokomotywy elektryczne (4 serie, 2 rodziny modelowe)

**Rodzina klasyka (jeden model 3D w M-Models):**
- **EU07** — workhorse PKP, 1965
- **EP07** — bliski wariant EU07 (1995 modernizacja)
- **EP08** — szybszy klasyczny (1972)

**Rodzina nowoczesna:**
- **Newag EU160** — nowoczesna 160 km/h, dalekobieżna

### 2.2 Lokomotywy spalinowe (2 serie)

- **754** — liniowa pasażerska (linie niezelektryfikowane, "Brejlovec" import z CZ)
- **SM42** — manewrowa/techniczna (klasyka polskiej kolei, "królowa manewrów")

### 2.3 EZT elektryczne (6 serii, 2 rodziny)

**Klasyka:**
- **EN57** — 3 człony, 1962 (workhorse osobowy)
- **EN71** — 4 człony, 1976 (rozszerzenie EN57)

**Stadler FLIRT (jeden model 3D modularny A/B/C):**
- **L-4268** — 2 człony (aglomeracja)
- **LM-4268** — 3 człony (średni nowy zakup)
- **ER160** — 5 członów (regionalny mocny)
- **ED160** — 8 członów (dalekobieżny premium — segment przed Pendolino)

### 2.4 SZT spalinowe (5 serii, 2 rodziny)

**Rodzina bazowa (jeden model 3D modularny):**
- **SA135** — 1 człon (tanie wejście na lokalne)
- **SA134** — 2 człony (podstawowy)
- **SA136** — 3 człony (mocniejsze potoki)

**Rodzina nowsza (jeden model 3D modularny):**
- **SA137** — 2 człony (lepszy komfort)
- **SA138** — 3 człony (droższy, pojemniejszy)

### 2.5 Wagony pasażerskie (modularnie, 3 pudła × 2 wózki)

Liczba modeli 3D ograniczona — różnice gameplayowe w DANYCH per `functionalType`.

**Pudła:**
- `CoachBody_245_Old` — 24.5m, stare okna/drzwi (rynek wtórny)
- `CoachBody_245_Modern` — 24.5m, nowe okna/drzwi
- `CoachBody_264_Modern` — 26.4m, nowe (dłuższy)

**Wózki:**
- `CoachBogie_Old_BlockBrake` — klasyczny
- `CoachBogie_Modern_DiscBrake` — nowszy, hamulec tarczowy

**Typy funkcjonalne (dane, nie osobne modele):**
- `SecondClassOpen` (2. kl otwarty) — 80 miejsc
- `SecondClassCompartment` (2. kl przedziałowy) — 66 miejsc
- `FirstClassOpen` (1. kl otwarty) — 50 miejsc
- `FirstClassCompartment` (1. kl przedziałowy) — 42 miejsc
- `Mixed12` (1+2 mieszany) — 56 miejsc
- `Couchette` (kuszetka) — 54 miejsc
- `Sleeping` (sypialny WL) — 36 łóżek
- `Bar/Catering` (WR) — 30 miejsc (bonus satisfaction)
- `Bicycle` (rowerowy) — 24 miejsc + rowery
- `Family` (rodzinny) — 60 miejsc + strefa dziecięca
- `Disabled` (WC+wózki) — 54 miejsc (req. dostępny)

---

## 2.6 Quick-reference tabela EA (kluczowe parametry)

Ceny **zgodne z rzeczywistymi zamówieniami publicznymi** PKP Intercity, PolRegio, Koleje Mazowieckie, SKM i innych operatorów 2015-2024.

| Seria | Typ | Trakcja | Vmax | Moc kW | Długość m | Miejsca | Rok | Cena new (mln zł) | Cena used (mln zł) |
|---|---|---|---|---|---|---|---|---|---|
| EU07 | ElLoco | Electric | 125 | 2000 | 15.9 | — | 1965 | — (retired) | 1-3 |
| EP07 | ElLoco | Electric | 125 | 2000 | 15.9 | — | 1995 | — (mod ścieżka) | 2-4 |
| EP08 | ElLoco | Electric | 140 | 2480 | 15.9 | — | 1972 | — (retired) | 3-5 |
| Newag EU160 "Griffin" | ElLoco | Electric | 160 | 5600 | 19.75 | — | 2015+ | 22-28 | 15-22 |
| 754 (Brejlovec) | DsLoco | Diesel | 100 | 1460 | 16.8 | — | 1971 | — (retired) | 2-5 |
| SM42 | DsLoco | Diesel | 90 | 590 | 14.2 | — | 1965 | — (retired) | 0.3-1.5 |
| EN57 | EMU | Electric | 110 | 800 | 64.7 | 212 | 1962 | — | 0.2-15 (mod) |
| EN71 | EMU | Electric | 110 | 1220 | 86.6 | 313 | 1976 | — | 0.5-18 (mod) |
| FLIRT L-4268 (2 czł) | EMU | Electric | 160 | 2000 | 41.4 | 120 | 2014+ | 20-28 | 15-22 |
| FLIRT LM-4268 (3 czł) | EMU | Electric | 160 | 2000 | 62.5 | 180 | 2014+ | 30-40 | 22-32 |
| FLIRT ER160 (5 czł) | EMU | Electric | 160 | 2600 | 103.0 | 300 | 2014+ | 50-65 | 38-55 |
| FLIRT ED160 (8 czł) | EMU | Electric | 160 | 4000 | 160.0 | 450 | 2014+ | 85-120 | 65-95 |
| SA135 | DMU | Diesel | 100 | 261 | 14.5 | 52 | 1994 | — (retired) | 0.5-2 |
| SA134 | DMU | Diesel | 100 | 373 | 28.5 | 112 | 1998 | — (retired) | 1-3 |
| SA136 | DMU | Diesel | 100 | 522 | 42.5 | 170 | 2005 | — | 2-5 |
| SA137 | DMU | Diesel | 120 | 410 | 42.0 | 120 | 2008+ | 10-14 | 7-11 |
| SA138 | DMU | Diesel | 120 | 600 | 56.0 | 180 | 2009+ | 14-20 | 10-15 |

**Ceny wagonów pasażerskich** (orientacyjne):
- `Body_245_Old + SecondClassOpen` — **0.1-0.5 mln zł** used (stare PKP)
- `Body_245_Modern + Mixed12 + AC` — **2-3 mln zł** new (nowa dostawa)
- `Body_264_Modern + Sleeping + AC` — **3-5 mln zł** new (sypialny WL)
- `Body_264_Modern + Bar/Catering` — **2.5-4 mln zł** new (restauracyjny WR)

Uwagi realiów:
- **EN57 ma ogromny rozstrzał** — stara z końca lat 60. kosztuje kilkaset tys. (wartość lomu), modernizacja "Cebula" / "Ryba" / kompleksowa to 15-20 mln zł (de facto nowy pojazd). Dlatego zakres `0.2-15` w tabeli.
- **Griffin EU160** — ceny z zamówień PKP Intercity / Cargo ostatnich lat: ~23-26 mln zł za sztukę.
- **FLIRT ED160** — zamówienie PKP IC 2021 (20 sztuk, 2.7 mld zł brutto) ≈ ~107 mln zł/szt. — potwierdzone.
- **Pesa SA138** — zamówienie PolRegio ostatnich lat: ~14-18 mln zł.

Wszystkie wartości mogą być tweakowane w **M6.5 Rebalance** (post-M13 Save/Load).

**Wagony pasażerskie** — wartości zależą od `bodyStyle` + `functionalType`. Przykłady:
- `Body_245_Old + SecondClassOpen`: 80 miejsc, 0.15-0.5M zł used
- `Body_245_Modern + Mixed12 + AC`: 66 miejsc, 1.5-2.5M zł new
- `Body_264_Modern + Sleeping + AC`: 36 łóżek, 3-4M zł new

---

## 3. Zakres 1.0 (launch updates)

**Lokomotywy spalinowe:**
- **SU160** (PESA) — nowoczesna dla linii niezelektryfikowanych
- **SU4210 / SU4220** — modernizacja SM42 (ścieżka upgrade'u, nie osobny zakup)

**SZT spalinowe:**
- **SA139 Link** (Pesa) — nowoczesny, w konfiguratorze
- **SD85 / SN84** — dłuższe relacje bez sieci (alternatywa do 754+wagony)

**EZT:**
- **Newag Impuls I** — wielowariantowe długości
- **Pesa Elf I** — wielowariantowe długości
- **Modernizacje EN57/EN71** — ścieżka rozwoju starych

**Lokomotywy elektryczne:**
- **EP09** — stara szybka (ale bardziej kaprysna)
- **Pesa Gama** — alternatywa producenta wobec EU160
- **Newag EU200** — premium 200 km/h

---

## 4. Post-1.0 / DLC / updates

- **Stadler Dart ED161** — pierwsze większe update dalekobieżne
- **Pendolino ED250** — high-speed/premium, wymaga infra + popyt
- **Elf II / Impuls II** — rozszerzenia producentów
- **EU07A** — modernizacja (ścieżka upgrade'u, nie osobny zakup)
- **SU160 rozbudowa**
- **Siemens Vectron** — DLC Niemcy, wielosystemowa międzynarodowa
- **EP05** — historyczny/fan-service

**Poza zakresem całkowicie:**
- Ciężkie lokomotywy towarowe (ET22/ET41/Dragon freight variant) — tylko gdy M6 obejmie towary
- Inne warianty historyczne/niszowe — dopóki nie ma społeczności/modów

---

## 5. Schema danych per pojazd

Każdy pojazd opisany jest pełnym blokiem. Pola rozłożone wg warstw:

### 5.1 Identyfikacja
| Pole | Typ | Przykład |
|---|---|---|
| `seriesId` | string | `"EN57"`, `"FLIRT_ED160"` |
| `displayName` | string | `"EN57 — stary klasyk"` |
| `manufacturer` | string | `"ZNTK Poznań"`, `"Stadler"`, `"Newag"` |
| `country` | string ISO | `"PL"`, `"CH"` (Stadler), `"CZ"` (754) |
| `family` | string | `"EN57_family"`, `"FLIRT"`, `"EU07_family"` |
| `type` | `FleetVehicleType` enum | `EMU`, `DMU`, `ElectricLocomotive`, `DieselLocomotive`, `PassengerCar` |

### 5.2 Trakcja (w tym hybrydy)
| Pole | Typ | Opis |
|---|---|---|
| `supportedTractions` | `List<TractionType>` | `[Electric]`, `[Diesel]`, lub `[Electric, Diesel]` dla hybrydów/bimode |
| `voltages` | `List<string>` | Napięcia obsługiwane: `"3kV DC"`, `"15kV AC 16.7Hz"`, `"25kV AC 50Hz"` |
| `safetySystemsInstalled` | `List<string>` | `"CA"`, `"SHP"`, `"ETCS L1"`, `"ETCS L2"`, `"GSM-R"`, `"PKP Radio R3"` |
| `requiresCatenary` | bool | Computed: `true` jeśli `supportedTractions == [Electric]` |

**Obsługa hybrydów (bimode):** pojazd z `supportedTractions = [Electric, Diesel]` może jeździć zarówno po zelektryfikowanych jak i niezelektryfikowanych liniach. Przykłady z realu: Stadler FLIRT BMU/WINK, Alstom Coradia Polyvalent. W EA nie planowane (post-1.0), ale pola istnieją.

### 5.3 Parametry techniczne
| Pole | Typ | Przykład EN57 |
|---|---|---|
| `maxSpeedKmh` | int | 110 |
| `powerKw` | int | 800 |
| `lengthM` | float | 64.7 |
| `emptyMassTons` | float | 125 |
| `maxLoadedMassTons` | float | 148 |
| `brakingMassTons` | float | 142 |
| `brakeRegime` | `BrakeRegime` enum | `P` (pasażerski) |
| `wheelbase` | string | `"Bo'2'2'2'Bo'"` |
| `accelerationMps2` | float | 0.7 |
| `decelerationMps2` | float | 0.9 |
| `coachCount` | int | 3 (człony EMU) lub `N/A` dla loko |
| `maxCoachesInTrain` | int | Dla loko: max pociągniętych wagonów (np. 12) |

### 5.4 Pojemność (seat breakdown)
| Pole | Typ | Opis |
|---|---|---|
| `passengerSeats` | int | Suma łączna (denormalizowane) |
| `seatBreakdown` | `List<SeatCount>` | Lista per `SeatZoneType`: `{ zone: SecondClassOpen, count: 180 }, { zone: Bicycle, count: 8 }` itp. |

`SeatZoneType` enum (z kodu): `SecondClassOpen`, `SecondClassCompartment`, `FirstClassOpen`, `FirstClassCompartment`, `Bicycle`, `SmallCatering`, `LargeCatering`, `Sleeping`, `Reclining` (kuszetka), `Family`, `WheelchairAccessible`, `ManagerCompartment`.

### 5.5 Udogodnienia pasażerskie
| Pole | Typ | Wartości |
|---|---|---|
| `comfortFeatures` | `List<string>` | `"Klimatyzacja"`, `"Wi-Fi"`, `"Gniazdka 230V"`, `"USB"`, `"Info pasażerskie"` (monitory/audio), `"Toalety"`, `"WC dostępne dla niepełnosprawnych"`, `"Przedział rowerowy"`, `"Bar/restauracja"`, `"Przewijak"`, `"Strefa dziecka"`, `"Przedział ciszy"`, `"Monitoring CCTV"` |
| `comfortClass` | int 1-5 | 1=minimalne (EN57), 3=standard (FLIRT L-4268), 5=premium (EU160 + wagony 1. kl) |

### 5.6 Ekonomia
| Pole | Typ | Uwagi |
|---|---|---|
| `basePriceGroszy` | long | Cena nowego z konfiguratora |
| `usedMarketPriceMinGroszy` | long | Dolny zakres rynku wtórnego (stary, wysłużony) |
| `usedMarketPriceMaxGroszy` | long | Górny zakres (modernizowany, dobry stan) |
| `operationalCostPerKmGroszy` | int | Finalny koszt per km (zastępuje heurystykę z `CostCalculator`) |
| `depreciationRatePerYear` | float | Np. 0.02 = 2%/rok (rynek wtórny) |

### 5.7 Niezawodność (M7 runtime)
| Pole | Typ | Opis |
|---|---|---|
| `reliabilityScore` | int 0-100 | Bazowa niezawodność (modernizowane = wyższa) |
| `breakdownRiskFactor` | float | Mnożnik prawdopodobieństwa awarii (`EN57 = 1.5`, `EU160 = 0.7`) |
| `maintenanceCostFactor` | float | Mnożnik kosztu napraw (starsze = więcej) |
| `inspectionIntervalKm_P1` | int | Km między P1 |
| `inspectionIntervalKm_P2` | int | Km między P2 |
| `inspectionIntervalKm_P3` | int | Km między P3 |
| `inspectionIntervalYears_P4` | int | Lata między P4 |
| `inspectionIntervalYears_P5` | int | Lata między P5 (rewizja główna) |

### 5.8 Komponenty (`VehicleComponents`)
Każdy pojazd ma 12 komponentów (stan 0-100 lub -1 = N/A):

| Komponent | Dotyczy | Uwagi |
|---|---|---|
| `engineCondition` | EMU/DMU/Loko | -1 dla `PassengerCar` |
| `brakeCondition` | Wszystkie | |
| `doorsCondition` | Pasażerskie | -1 dla `ShuntingLocomotive` bez drzwi pasażerskich |
| `acCondition` | Gdy zainstalowana | -1 jeśli `!comfortFeatures.Contains("Klimatyzacja")` |
| `bodyCondition` | Wszystkie | Pudło/nadwozie |
| `wheelsCondition` | Wszystkie | Koła/wózki |
| `electricalCondition` | Wszystkie | Instalacja (oświetlenie, sterowanie) |
| `interiorCondition` | Pasażerskie | Tapicerka, monitoring |
| `lightsCondition` | Wszystkie | Reflektory + oświetlenie wewnętrzne |
| `toiletsCondition` | Gdy zainstalowane | -1 jeśli `!comfortFeatures.Contains("Toalety")` |
| `pantographCondition` | Electric only | -1 dla diesel/wagonów |
| `couplingCondition` | Wszystkie | Sprzęg UIC lub automatyczny |

Każdy komponent może osobno się zepsuć (M7 runtime) i wymagać osobnej naprawy. Przy przeglądzie P5 — pełna rewizja wszystkich.

### 5.9 Historia i dane produkcyjne
| Pole | Typ | Opis |
|---|---|---|
| `inProductionFromYear` | int | Od kiedy producent seryjnie |
| `inProductionToYear` | int | Do kiedy (jeśli production zatrzymana) |
| `introducedToPolandYear` | int | Od kiedy w PL |
| `status` | enum | `InProduction`, `Retired`, `InModernization`, `Modernized` |

### 5.10 Wymagania infrastruktury
| Pole | Typ | Opis |
|---|---|---|
| `minPlatformLengthM` | int | Min długość peronu do obsługi (np. FLIRT ED160 = 200m) |
| `requiresMaintenanceCapabilities` | `List<string>` | `"ElectricWorkshop"`, `"DieselWorkshop"`, `"UndergroundInspection"`, `"WheelLathe"` |

### 5.11 Gameplay (sugestie UI)
| Pole | Typ | Opis |
|---|---|---|
| `suggestedCategoryGroups` | `List<IrjGroup>` | Dla UI "Ten pojazd nadaje się do: Regional Local, Interregional Fast" |
| `canBePulledByDiesel` | bool | EMU może być ciągnięty przez SM42 na torach bez sieci |
| `isShuntingLocomotive` | bool | Flag SM42 — nie nadaje się do regularnych kursów liniowych |
| `historicalFactoid` | string | "Zbudowana w ZNTK Poznań, prototypowa w 1962. Była najpopularniejszym EZT w PRL." |

### 5.12 Wagony — dodatkowe pola
Poza schematem ogólnym, wagony mają:
| Pole | Typ | Opis |
|---|---|---|
| `bodyStyle` | string | `CoachBody_245_Old`, `CoachBody_245_Modern`, `CoachBody_264_Modern` |
| `bogieStyle` | string | `Old_BlockBrake`, `Modern_DiscBrake` |
| `functionalType` | `SeatZoneType`/string | Determinuje seatBreakdown i udogodnienia |
| `needsElectricSupply` | bool | Wagon sypialny/restauracyjny często wymaga zasilania z loko |

---

## 6. Przykład pełnego wpisu (EN57)

```json
{
  "seriesId": "EN57",
  "displayName": "EN57 — stary klasyk",
  "manufacturer": "Pafawag Wrocław",
  "country": "PL",
  "family": "EN57_family",
  "type": "EMU",
  "supportedTractions": ["Electric"],
  "voltages": ["3kV DC"],
  "safetySystemsInstalled": ["CA", "SHP", "PKP Radio R3"],
  "requiresCatenary": true,
  "maxSpeedKmh": 110,
  "powerKw": 800,
  "lengthM": 64.7,
  "emptyMassTons": 125,
  "maxLoadedMassTons": 148,
  "brakingMassTons": 142,
  "brakeRegime": "P",
  "wheelbase": "Bo'2'2'2'Bo'",
  "accelerationMps2": 0.7,
  "decelerationMps2": 0.9,
  "coachCount": 3,
  "passengerSeats": 212,
  "seatBreakdown": [
    { "zone": "SecondClassOpen", "count": 180 },
    { "zone": "SecondClassCompartment", "count": 32 }
  ],
  "comfortFeatures": ["Toalety"],
  "comfortClass": 1,
  "basePriceGroszy": 0,  // nie produkowana nowa
  "usedMarketPriceMinGroszy": 8000000,   // 80k zł — złomowa
  "usedMarketPriceMaxGroszy": 25000000,  // 250k zł — po modernizacji
  "operationalCostPerKmGroszy": 320,
  "depreciationRatePerYear": 0.02,
  "reliabilityScore": 55,
  "breakdownRiskFactor": 1.5,
  "maintenanceCostFactor": 1.3,
  "inspectionIntervalKm_P1": 20000,
  "inspectionIntervalKm_P2": 80000,
  "inspectionIntervalKm_P3": 300000,
  "inspectionIntervalYears_P4": 6,
  "inspectionIntervalYears_P5": 20,
  "inProductionFromYear": 1962,
  "inProductionToYear": 1993,
  "introducedToPolandYear": 1962,
  "status": "Retired",  // produkcja wstrzymana, ale istnieją setki sztuk
  "minPlatformLengthM": 70,
  "requiresMaintenanceCapabilities": ["ElectricWorkshop", "UndergroundInspection"],
  "suggestedCategoryGroups": ["RegionalLocal", "RegionalFast", "RegionalAgglomeration"],
  "canBePulledByDiesel": true,
  "isShuntingLocomotive": false,
  "historicalFactoid": "Najpopularniejszy EZT w historii PKP. Produkowany 1962-1993. Do dziś podstawa ruchu regionalnego, często modernizowany."
}
```

---

## 7. Mapping na kod

- **JSON data** — `Assets/StreamingAssets/Fleet/fleet_catalog_ea.json` (nowy, scala `new_models.json` + `initial_market.json`)
- **Loader** — rozszerzyć `FleetCatalog.LoadAll` o wczytywanie z nowego JSONa
- **Enum `FleetVehicleType`** — bez zmian (Electric/DieselLocomotive, EMU, DMU, PassengerCar)
- **Enum `TractionType`** — **NOWY** (w `FleetEnums.cs`): `Electric`, `Diesel`, `None` (dla wagonów). Lista supportedTractions umożliwia hybrydy.
- **`VehicleComponents`** — już rozszerzone do 12 komponentów (lights, toilets, pantograph, coupling). Konwencja `-1 = N/A`.
- **`SeatZoneType`** — bez zmian
- **`SeatCount`** — bez zmian
- **`BalanceConstants.Fleet`** — nowy plik na spec default values (`DEFAULT_INSPECTION_KM_P1 = 20000` etc.)
- **`SuggestedCategoryGroups`** → wymaga mapping na `IrjGroup` enum (już istnieje)

---

## 8. Status implementacji M-Fleet

### Podetapy
1. **M-Fleet-1:** Extend data model
   - [ ] Dodać `TractionType` enum
   - [x] Rozszerzyć `VehicleComponents` o 4 nowe (✅ done w tej sesji)
   - [ ] Dodać pola do `NewVehicleModel` + `FleetMarketVehicle` + `FleetVehicleData` zgodnie ze spec'em §5
2. **M-Fleet-2:** Write JSON data
   - [ ] `fleet_catalog_ea.json` z pełnymi danymi 17 serii + 6-12 typów wagonów
   - [ ] Sprawdzone realne wartości (Vmax, moc, masa z Wikipedii / dane PKP)
3. **M-Fleet-3:** Update loader + UI
   - [ ] `FleetCatalog` czyta nowy format
   - [ ] Konfigurator nowego taboru pokazuje tylko modele `status=InProduction` z odpowiednimi presetami
   - [ ] Rynek wtórny pokazuje `status=Retired`/`Modernized` z losowym stanem
4. **M-Fleet-4:** Integracja z M6/M7/M9
   - [ ] `CostCalculator` używa `operationalCostPerKmGroszy` ze spec'u zamiast heurystyki
   - [ ] `M7` (przyszły) używa `reliabilityScore`/`breakdownRiskFactor`
   - [ ] `M9b` (placeholder teraz, później M9d modele) — `VehicleScale` z `lengthM` zamiast hardcoded 20m

---

## 9. Rozstrzygnięte pytania

### Q1 ✅ Ceny
Zgodne z rzeczywistymi zamówieniami publicznymi (tabela §2.6). Tweakowalne w M6.5 Rebalance post-M13.

### Q2 ✅ Hybrydy — post-1.0
Pola `supportedTractions: List<TractionType>` istnieją w M-Fleet data model od początku (future-proof). Ale gameplay bimode (Stadler FLIRT WINK, Alstom Coradia, Pesa Link hybrid) wchodzi **post-1.0** razem z DLC:
- Możliwa alternatywa dla linii niezelektryfikowanych
- Realna przewaga nad `754 + wagony` tylko przy odpowiednio zbalansowanych kosztach

### Q3 ✅ Konfigurator nowego taboru — co user modyfikuje
Dostępne w konfiguratorze (ekran "Zamów nowy tabor"):
- **Wybór modelu** — z listy `status=InProduction` (nowe Griffin/EU160, FLIRT, SA137/138, Impuls I/Elf I od 1.0)
- **Długość (modular EMU/DMU):** L-4268 (2) / LM-4268 (3) / ER160 (5) / ED160 (8) dla FLIRT; SA135 (1) / SA134 (2) / SA136 (3) dla rodziny SZT bazowej
- **Seat breakdown** — wybór typów stref (ile 1. kl, 2. kl, sypialne, rowerowe). Wypełnia automatycznie `seatBreakdown: List<SeatCount>`
- **Comfort features** — checkboxy (klimatyzacja, Wi-Fi, gniazdka, monitoring, strefa ciszy, przewijak, przedział rodzinny). Wpływa na cenę finalną + koszt utrzymania
- **Systemy bezpieczeństwa** — bazowy (CA/SHP) w cenie, ETCS L1/L2 + GSM-R jako opcje premium
- **Napięcia** (dla Electric): 3kV DC standard, 15kV AC/25kV AC za dopłatą (wielosystemowe, post-1.0)
- **Nazwa taboru** (np. "EN57-001 Mój pierwszy")
- **Livery** — początkowy preset + opcja własnego koloru/pasa (→ **M12d** pełny edytor liveries, do tej pory tylko color picker)

### Q4 ✅ Rynek wtórny — hybryda: seed + generator
Rynek wtórny działa dwupoziomowo:

**1. Początkowy seed** (ręczny, `initial_market.json`):
- Ręcznie dobrane ~10-15 pozycji o znanej jakości
- Pokazuje gracze różnorodność taboru na start (EN57 stara + EN57 zmodernizowana, EU07 wysłużone, jeden 754 z Czech, para SM42 manewrowych)
- Daje deterministyczny start (balance przewidywalny, QA łatwiejsze)
- Gracz może kupić wszystko z tej puli jeśli ma fundusze

**2. Auto-refresh generator** (co 30 dni gry):
- Co 30 dni: usuwa N najstarszych niekupionych pozycji, dodaje N losowych
- Losowanie z puli `status=Retired` lub `status=Modernized` w catalog
- Per pojazd generuje losowy stan: rocznik produkcji (z zakresu inProduction), mileage (proporcjonalny do wieku), `conditionPercent` + `VehicleComponents` per-komponent (niektóre komponenty mogą być zepsute)
- Cena z zakresu `usedMarketPriceMin/Max` modulowana stanem (0% = min, 100% = max)
- Tworzy iluzję żywego rynku + zapobiega "wszystkie dobre pojazdy już wybrane"

Parametry generatora w `BalanceConstants.Fleet`:
- `MarketRefreshIntervalDays = 30`
- `MarketRefreshRemoveCount = 3` (usuwa 3 najstarsze niekupione)
- `MarketRefreshAddCount = 5` (dodaje 5 nowych losowych)
- `MarketMaxSize = 20` (cap)

Pełna implementacja w **M-Fleet-3**.

---

## 10. Reference

- `docs/design/asset-pipeline.md` — rodziny modelowe, decyzje 3D (baseline dla tego dokumentu)
- `docs/design/m6-economy.md` — TicketSystem, CostCalculator używają pól ekonomicznych
- `Assets/StreamingAssets/Fleet/` — docelowa lokalizacja JSON
- `Assets/Scripts/Fleet/Data/` — schema classes
