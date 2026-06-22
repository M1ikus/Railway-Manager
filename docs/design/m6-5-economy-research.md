# M6.5 Rebalance — Real-world Economy Research

> **Cel dokumentu:** baseline kosztów dla balansu ekonomii M6.5 oparty na realnych liczbach z polskiego rynku kolejowego (2024-2025). Source of truth przy tuningu `BalanceConstants.Economy`, `new_models.json`, `initial_market.json`, `salary_table.json`, `inspection_intervals.json`.
>
> **Data utworzenia:** 2026-04-27 (rev. 2: per-pojazd analiza dla aktualnego catalog'u gry)
> **Methodology:** 4 paralelne research agenty (3× live web search z dostępem do PLK/UTK/URE/raporty branżowe + 1× compilation z training data dla pensji). Sekcja Tabor zawężona do 23 pojazdów które realnie są w `new_models.json` + `initial_market.json` — research wynikowy odrzucony dla pojazdów których nie ma w grze (Pendolino, Vectron, Dragon 2, Impuls, Elf 3, Husarz). Każda liczba MA źródło — przy braku jawnych danych zaznaczone jako estymata.
>
> **Disclaimer:** Sekcja "Pensje" kompilowana offline z training data — przed wdrożeniem do constants warto cross-walidować z aktualnymi raportami PKP IC/Polregio za 2024 (Q2 2025). Pozostałe 3 sekcje oparte na live web search z konkretnymi linkami źródłowymi.
>
> **Inflacja kontekst:** sektor kolejowy 2018-2024 +50-100% (tabor +50-80%, infrastruktura +50-100%, energia 2022-2024 +30-50%). Skaluj historyczne dane ×1.5-2 dla wartości 2025.

---

## 0. Quick Reference — kluczowe liczby

**Tabor — catalog gry (8 nowych w `new_models.json` + 15 wtórnych w `initial_market.json` = 23 pojazdy):**

*Verdict ✅ = obecna cena pasuje do real-world (±15%); ⚠️ = warto rebalansować w M6.5; ⚠️⚠️ = poważny rozjazd*

- **Lokomotywy elektryczne nowe (1):** EU160_Griffin (Newag) — **25M** zł / real 18.4-36.5M ✅
- **EZT spalinowe nowe (2):** SA137 — **12M** ✅; SA138 — **17M** ⚠️ (real estymata 11-13M, zawyżone ~30%)
- **EZT elektryczne nowe (4):** FLIRT_L4268 — **24M** ✅; LM4268 — **35M** ✅; ER160 — **58M** ✅; ED160 — **107M** ⚠️ (real ~135M PKP IC 2021, zaniżone ~25%)
- **Wagon nowy modułowy (1):** Coach_264_Universal — **2.8M** ⚠️⚠️ (real 14.1M PKP IC 2024, zaniżone 5×!)
- **Lokomotywy wtórne (5):** EU07-085 1.5M / EU07-342 2.4M ⚠️ (real 0.45-0.96M, zawyżone 2-3×); EP07-1052 3.5M ✅; EP08-013 4M ⚠️ (zawyżone vs 1975 + 55% kondycji); SM42-523 0.7M ⚠️ (real 0.35-0.41M, zawyżone 2×); 754 Brejlovec 3.2M ✅
- **EZT wtórne (3):** EN57-1120 stara 0.4M ✅; EN57-2001 mod. Ryba 9.5M ⚠️ (real ~7.6M Polregio rynek wtórny, zawyżone ~25%); EN71-024 2.2M ✅
- **EZT spalinowe wtórne (2):** SA134-015 2M ✅; SA136-003 2.9M ✅
- **Wagony wtórne (4):** Coach 111A 0.28M ✅, 112A 0.65M ✅, 152A sypialny 1.4M ✅, 156A barowy 2.6M ✅

**Modernizacje (real-world, jako reference dla gameplay'u "kup wrak → wyślij do warsztatu" — TODO post-M6.5):**
- EN57→AKM (Pesa MM): **6.75M**; EN57→AKM (Newag): **8.07M**; EN57→Feniks (FPS): **8.5M**
- EU07/EP07→160 km/h (Olkol 2024): **9.96M**
- SM42→6Dg/6Di (Newag 2014): **3.85M**

**Big picture:** najwięcej ⚠️ przy lokomotywach klasycznych (EU07/EP08/SM42 zawyżone vs PKP IC ceny złomu) i wagonie modułowym (drastycznie zaniżony).

**Pensje miesięczne brutto (2024-2025):**
- Maszynista PKP IC: **7.5-13.5k** (śr. 9.8k); Polregio: **6.2-9.8k** (śr. 7.5k)
- Konduktor PKP IC: **4.5-7.2k** (śr. 5.8k); Polregio: **4.2-6.5k** (śr. 5.2k)
- Mechanik P5 (ZNTK): **6.5-11.5k** (śr. 8.5k); P1/P2: **4.8-8k** (śr. 6.2k)
- Dyspozytor: **6.2-10.5k** (śr. 7.8k); Dyżurny ruchu PLK: **5.8-10.5k** (śr. 7.5k)
- Sprzątacz (outsourced): **4.7-5.8k** (minimalka + ewentualne dodatki)
- Kasjer: **4.7-6.5k**
- Inżynier R&D Pesa/Newag: **9.5-22k** zależnie od stażu
- **Koszt total pracodawcy = brutto × 1.21** (ZUS pracodawcy ~20.5%)

**Budowa infrastruktury (PLK kontrakty 2023-2024):**
- Modernizacja kompleksowa toru (PLK plan 180mld/7000km do 2030): **~25 mln zł/km** średnio
- Sam tor szlak (wymiana, bez SRK/peronów): **5-10 mln zł/km**
- Sieć trakcyjna 3kV DC (sama sieć): **~1 mln zł/km**
- Podstacja trakcyjna: **22-70 mln zł/szt** (zasięg 15-20 km)
- Rozjazd R190 1:9 z montażem (PLK kontrakty pakietowe): **~3-4 mln zł/szt** (vs 12 tys zł rynek wtórny — montaż >> materiał)
- Rozjazd R760 1:18 KDP: **1.5-2.5 mln zł/szt**
- Hala P5 z wyposażeniem: **8-15 tys zł/m²**; P3: 5-8 tys; P1/P2: 3-5 tys
- Lokomotywownia kompleksowa (Warszawa Grochów, P3+myjnia+diagnostyka): **179 mln zł** (benchmark)
- Myjnia EZT: **20-40 mln zł** (PKP IC Wrocław 33 mln; Kraków tramwajowa 20 mln)
- Działki przemysłowe: **50-200 zł/m²** peryferia, **200-500 zł/m²** aglomeracja
- Asfalt drogowy: 200-300 zł/m² (z podbudową)

**Utrzymanie / koszty operacyjne (taryfy 2024-2025):**
- **Opłata PLK pasażerski (TUI):** śr. **6.31 PLN/pociągokm**; towarowy: **12.77 PLN/pockm**; manewry: **3.66 PLN/pockm**
- **Energia trakcyjna brutto:** **0.90-1.30 PLN/kWh** (taryfa C1x PGE EK 2024-25)
- **Zużycie EZT regionalny:** 5-8 kWh/pockm (Impuls/FLIRT z rekuperacją); EN57: 8-12 kWh/pockm zima z ogrzewaniem
- **Zużycie lokomotywa+ekspres 6-7 wagonów:** 12-18 kWh/pockm
- **Diesel hurt:** 5.50-6.00 PLN/L netto (flota kolejowa 2025)
- **Zużycie SM42 manewrowo:** ~200 L/100km lekki ruch; SU45 liniowo: 2-3 L/km; SA139 Link: 1.0-1.8 L/km
- **OC przewoźnika obowiązkowe:** suma gwarancji 2.5 mln EUR (~10.5 mln zł), składka realna 80-300 tys zł/rok
- **Casco taboru (opcjonalne):** 0.15-0.40% wartości pojazdu/rok (np. EZT 35M = 50-140k/rok)
- **GSM-R all-in:** ~10 tys zł/pojazd/rok
- **Świadectwo bezpieczeństwa UTK:** 7000 zł nowe, 1500+ przedłużenie
- **Licencja maszynisty:** 100 zł, egzamin 150 zł
- **Podatek od nieruchomości:** 34 zł/m² hale; 1.38 zł/m² grunt (max 2025); średnia zajezdnia 200-300 tys zł/rok
- **Postój komercyjny stacja duża (np. Łódź Fabryczna):** 138 zł/postój; mała: 0.68 zł

**Operational cost mechaniczny per km (czarna skrzynka, estymaty branżowe):**
- EZT regionalny: 5-8 PLN/km
- Lokomotywa elektryczna: 3-5 PLN/km
- Lokomotywa spalinowa: 6-12 PLN/km
- Pendolino (high-speed): ~8.5 PLN/pockm

**Lifespan:**
- EN57: **50-80 lat** (1962-2043 planowane wycofanie!)
- Lokomotywy elektryczne: 40-50+ lat
- Wartość rezydualna: 1-3% ceny nowej (złom) lub 5-10% (sprawna)

---

## 1. Tabor — per-pojazd analiza (catalog gry)

> **Aktualny catalog:** 8 modeli w `Assets/StreamingAssets/Fleet/new_models.json` (kupowalne nowe) + 15 pojazdów w `initial_market.json` (rynek wtórny startowy) = **23 pojazdy**.
>
> **Dla każdego pojazdu:** real-world reference → obecna cena w grze → verdict → propozycja rebalansu w M6.5.

### 1.1 Lokomotywy elektryczne (1 nowa + 4 wtórne)

#### EU160_Griffin (NEW, Newag, lokomotywa wielosystemowa)

| | |
|---|---|
| **Status w grze** | nowy do kupienia (`new_models.json`) |
| **Obecna `basePrice`** | **25 000 000 zł** |
| **Real-world references** | Griffin EU160 PKP IC 2018-20: **18.4M/szt** (551.4M/30) • PKP IC 2023: **20.7M/szt** (954M/46) • EU200 wielosyst. 2024: **25.9M/szt** (388M/15) • EU200 200 km/h 2024: **36.5M/szt** (2.3 mld/63) |
| **Verdict** | ✅ **W widełkach** — nasza cena = real EU200 wielosystemowa 2024. Sensowne dla 2025 baseline. |
| **Propozycja M6.5** | Zostawić 25M, opcjonalnie bumpnąć do 27-28M dla 2025 inflation (5-10%/rok). |
| **Source** | [Newag Griffin 30 EU160 - gov.pl](https://www.gov.pl/web/infrastruktura/najwiekszy-w-historii-kontrakt-grupy-pkp-realizowany-przez-newag) |

#### EU07 (USED, 2 sztuki — workhorse PKP od 1965)

| Sztuka | Wiek | Kondycja | Mileage | Cena |
|---|---|---|---|---|
| EU07-085 | 1974 (51 lat) | 52% | 3.2M km | **1.5M** |
| EU07-342 | 1982 (43 lata) | 68% | 2.1M km | **2.4M** |

| | |
|---|---|
| **Real-world references** | PKP IC sprzedaż wycofanych 2021 (8 szt.): **start 48k zł** (złom) • Sprawne ceny wywoławcze: **442-500k** • PR od PKP IC paczka 6 szt. 2011: **960k/szt** śr. (5.76M / 6) • Polregio paczka 4 szt.: **659k/szt** net (2.635M / 4) |
| **Verdict** | ⚠️ **Zawyżone 2-3×** vs real PKP IC ceny złomu. EU07 wycofane to dziś rzadko >1M nawet sprawne. |
| **Propozycja M6.5** | Skala wg kondycji: 50% kondycja → **0.4-0.6M**; 70% → **0.8-1.0M**; >85% (modernizowana) → **1.5-2.0M**. Czyli: EU07-085 zejdź z 1.5M → ~0.6M; EU07-342 z 2.4M → ~0.9-1.0M. |
| **Source** | [Sieć Obywatelska — lokomotywy po cenie złomu](https://siecobywatelska.pl/stala-w-polu-lokomotywa-a-nawet-szesc-ile-kosztowaly-lokomotywy-sprzedane-po-cenie-zlomu/) |

#### EP07 (USED, 1 sztuka — modernizacja EU07 z lat 90.)

| | |
|---|---|
| **Sztuka** | EP07-1052 (1998, 26 lat, kondycja 78%, mileage 1.2M km) |
| **Obecna cena** | **3.5M zł** |
| **Real-world references** | EP07 to modernizacja EU07 — ceny rzadko publikowane jawnie • PKP Cargo Tabor P4 8 EU07 (kontrakt cykliczny): **6.6M/8 = 824k/szt** brutto (sam P4, NIE cena pojazdu) • Modernizacja EU07/EP07→160 km/h Olkol 2024: **9.96M/szt** dla 20 PKP IC (premium modernizacja) |
| **Verdict** | ✅ **Rozsądnie** dla 78% kondycji + modernizacji typu sprzed boomu Olkol. |
| **Propozycja M6.5** | Zostawić 3.5M dla 78%; bumpnąć do 4-5M dla >85%. |
| **Source** | [PKP IC modernizacja EU07/EP07 do 160 km/h Olkol - Rynek Kolejowy](https://www.rynek-kolejowy.pl/wiadomosci/jest-wykonawca-napraw-i-modernizacji-10-lokomotyw-eu07-i-ep07-pkp-intercity-82670.html) |

#### EP08 (USED, 1 sztuka — szybsza wersja klasyczna 140 km/h)

| | |
|---|---|
| **Sztuka** | EP08-013 (1975, 50 lat, kondycja 55%, mileage 4.1M km — top eksploatacji) |
| **Obecna cena** | **4M zł** |
| **Real-world references** | Brak konkretnych aukcji dla EP08 (rzadko handlowana, mała seria). Ekstrapolacja z EU07/EP07: pojazd z 1975 + 55% kondycji + 4.1M km mileage = **bardzo zużyty**, nie premium. |
| **Verdict** | ⚠️ **Zawyżone** — dla pojazdu z 1975 i wysokiego mileage'u 4M zł to za drogo. |
| **Propozycja M6.5** | Zejść do **1.5-2.5M** dla 55% kondycji. Lepszy stan (>80%) = **3.5-4.5M**. |
| **Source** | brak twardego (ekstrapolacja z EU07/EP07) |

#### SM42 (USED, 1 sztuka — "Królowa manewrów")

| | |
|---|---|
| **Sztuka** | SM42-523 (1973, 52 lata, kondycja 62%, mileage 2.8M km) |
| **Obecna cena** | **0.7M zł** |
| **Real-world references** | SM42 sprawna z kopalni Kłodawa: **351k zł start aukcji** (lata 2020) • Modernizacja SM42→6Dg/6Di (Newag 2014, 20 szt PKP IC): **3.85M/szt** brutto (cały remont, NIE cena pojazdu) |
| **Verdict** | ⚠️ **Zawyżone ~2×** vs aukcje (351-407k). |
| **Propozycja M6.5** | Skala wg kondycji: średnia → **350-500k**; modernizowana (>85%) → **800k-1.0M**. Modernizacja SM42→6Dg jako gameplay (post-M6.5): **3.85M** koszt P5 w warsztacie. |
| **Source** | [SM42 modernizacja Newag 20 szt - kurier-kolejowy.pl](https://kurier-kolejowy.pl/aktualnosci/24391/zmodernizowane-sm42---lokomotywy-drugiej-generacji.html) |

#### 754 Brejlovec (USED, 1 sztuka — czeski import)

| | |
|---|---|
| **Sztuka** | 754-027 (1978, 47 lat, kondycja 65%, mileage 3.6M km, Katowice) |
| **Obecna cena** | **3.2M zł** |
| **Real-world references** | Brak konkretnych referencji dla rynku polskiego (754 to czeska lokomotywa, w PL używana sporadycznie). W CZ rynek wtórny ~1-3M zł zależnie od stanu. |
| **Verdict** | ✅ **Rozsądnie** dla 65% kondycji + premium za import (specjalizacja niezelektryfikowana, większa moc niż SM42). |
| **Propozycja M6.5** | Zostawić **3.0-3.5M** dla średniego stanu. |
| **Source** | brak twardych aukcji (estymata branżowa) |

### 1.2 EZT spalinowe / SZT (2 nowe + 2 wtórne)

#### SA137 (NEW, Pesa, 2-czł. szynobus)

| | |
|---|---|
| **Status w grze** | nowy do kupienia |
| **Obecna `basePrice`** | **12M zł** |
| **Real-world references** | Pesa SA139 LINK (single 1 szt., 2010s Polregio): **9.35M net** (~11.5M brutto) • 2-czł. SZT generalnie ~9-12M w PL |
| **Verdict** | ✅ **Rozsądnie** w środku widełek 9.35-13M. |
| **Propozycja M6.5** | Zostawić **11-12M**. |
| **Caveat** | SA137 jako 2-czł. to nasza wewnętrzna nazwa — real-world Pesa miała SA133/SA134/SA135/SA136 z różnymi konfiguracjami. To OK gameplay'owo. |
| **Source** | [Pesa SA139 dane - psmkms.krakow.pl](https://psmkms.krakow.pl/kolej/autobusy-szynowe/461-sa134) |

#### SA138 (NEW, Pesa, 3-czł. szynobus)

| | |
|---|---|
| **Status w grze** | nowy do kupienia |
| **Obecna `basePrice`** | **17M zł** |
| **Real-world references** | SA138 (Newag 5 szt., 2011-12 Koleje Śląskie): cena niejawna, estymata **11-13M** • Pesa Link 3-czł. estymata 13-15M (większa konfiguracja niż SA139 single) |
| **Verdict** | ⚠️ **Trochę zawyżone** vs estymata real 11-15M (różnica ~15-30%). |
| **Propozycja M6.5** | Zejść do **14-15M zł**. |
| **Caveat** | Estymaty real-world słabe (cena niejawna), więc widełki szerokie. |
| **Source** | brak (cena Koleje Śląskie niejawna) |

#### SA134 (USED, 1 sztuka — Pesa Bydgostia, 2-czł.)

| | |
|---|---|
| **Sztuka** | SA134-015 (2004, 21 lat, kondycja 72%, mileage 1.4M km) |
| **Obecna cena** | **2M zł** |
| **Real-world references** | Brak konkretnych aukcji (rynek wtórny SA134 zaczyna się dopiero formować — produkcja Pesa Bydgostia zakończona 2013). |
| **Verdict** | ✅ **Sensowne** dla używanego 2004 w dobrym stanie. |
| **Propozycja M6.5** | Zostawić **1.5-2.5M** zakres. |
| **Source** | brak twardych aukcji |

#### SA136 (USED, 1 sztuka — Pesa Bydgostia, 3-czł.)

| | |
|---|---|
| **Sztuka** | SA136-003 (2008, 17 lat, kondycja 78%, mileage 850k km) |
| **Obecna cena** | **2.9M zł** |
| **Real-world references** | Jw. (rzadko handlowane). Młodsza i większa niż SA134 = droższa. |
| **Verdict** | ✅ **Rozsądnie** (młodsza, lepszy stan, większa konfiguracja). |
| **Propozycja M6.5** | Zostawić **2.5-3.5M** zakres. |
| **Source** | brak twardych aukcji |

### 1.3 EZT elektryczne (4 nowe FLIRT + 3 wtórne)

#### FLIRT_L4268 (NEW, Stadler, 2-czł.)

| | |
|---|---|
| **Status w grze** | nowy do kupienia |
| **Obecna `basePrice`** | **24M zł** |
| **Real-world references** | Brak konkretnych kontraktów na 2-czł. FLIRT w PL (większość PL zamawia 3-5+ czł.). Estymata: 2-czł. ~50-60% ceny 5-czł. → 2-czł. real **17-30M** (z 47-65M widełek 5-czł.) |
| **Verdict** | ✅ **W widełkach**, w środku zakresu. |
| **Propozycja M6.5** | Zostawić **22-26M**. |
| **Source** | ekstrapolacja z 5-czł. ER160 |

#### FLIRT_LM4268 (NEW, Stadler, 3-czł.)

| | |
|---|---|
| **Status w grze** | nowy do kupienia |
| **Obecna `basePrice`** | **35M zł** |
| **Real-world references** | 3-czł. nie był standardową konfiguracją w PL (najpopularniejsze 5-czł.). Estymata: 75-80% ceny 5-czł. = **35-50M**. |
| **Verdict** | ✅ **W dolnym zakresie** widełek. |
| **Propozycja M6.5** | Zostawić **33-38M**. |
| **Source** | ekstrapolacja |

#### FLIRT_ER160 (NEW, Stadler, 5-czł.)

| | |
|---|---|
| **Status w grze** | nowy do kupienia |
| **Obecna `basePrice`** | **58M zł** |
| **Real-world references** | Stadler kontrakt KM 2018: **33M/szt** (2 mld+/61 szt ramowy) • KM 2025 oferta: **47.2M brutto/szt** • KM 2024 umowa wykonawcza: **64.7M/szt** (711M/11) |
| **Verdict** | ✅ **W środku** widełek 2024-25. |
| **Propozycja M6.5** | Zostawić **55-62M**. |
| **Source** | [Stadler 50 EZT KM 3.2 mld - inwestycje.pl](https://inwestycje.pl/biznes/stadler-ma-umowe-ramowa-na-50-ezt-dla-kolei-mazowieckich-za-32-mld-zl/) |

#### FLIRT_ED160 (NEW, Stadler, 8-czł.)

| | |
|---|---|
| **Status w grze** | nowy do kupienia |
| **Obecna `basePrice`** | **107M zł** |
| **Real-world references** | PKP IC 2021: **2.7 mld brutto / 20 szt = 135M/szt** (kontrakt na 20 szt., dostawy od 2023) |
| **Verdict** | ⚠️ **Zaniżone ~25%** vs real. |
| **Propozycja M6.5** | Bumpnąć do **130-140M** dla EA-baseline. |
| **Source** | wspomniane w `historicalFactoid` w `new_models.json` ("PKP IC w 2021 zamówiło 20 szt. FLIRT ED160 za 2.7 mld zł brutto") |

#### EN57 — stara (USED, 1 sztuka — "Kibelka", 1962-1993)

| | |
|---|---|
| **Sztuka** | EN57-1120 (1974, 51 lat, kondycja 48%, mileage 4.8M km — top eksploatacji) |
| **Obecna cena** | **0.4M zł** |
| **Real-world references** | EN57AL po wypadku (wrak): **600k brutto** cena wywoławcza (Polregio 2025) • EN57 modernizowane (Polregio rynek wtórny): ~7.6M brutto (22.89M/3 szt z woj. Zach-Pom.) |
| **Verdict** | ✅ **Sensowne** dla starej w 48% kondycji. Faktycznie "po cenie wraku". |
| **Propozycja M6.5** | Zostawić **0.3-0.5M** dla starej w eksploatacji. Wrak: 0.05-0.1M. |
| **Source** | [Polregio sprzedaje wrak EN57 - rynek-kolejowy](https://www.rynek-kolejowy.pl/wiadomosci/...) |

#### EN57 — mod. Ryba (USED, 1 sztuka — pełna modernizacja Newag)

| | |
|---|---|
| **Sztuka** | EN57-2001 mod. Ryba (1985 produkcja, 92% kondycja, 800k km) |
| **Obecna cena** | **9.5M zł** |
| **Real-world references** | Polregio kupuje od woj. Zach-Pom. (2023): **22.89M / 3 szt = 7.63M/szt** • Modernizacja EN57→AKM Pesa MM: **6.75M** (śr.) lub 4.27M brutto (KM 2026-27, P4+komponenty) • Modernizacja EN57→AKM Newag: **8.07M** (śr.) • EN57→Feniks (FPS Cegielski 2015-18): **8.5M brutto** śr. |
| **Verdict** | ⚠️ **Zawyżone ~25%** vs real ~6-8M. 9.5M to top możliwych modernizacji. |
| **Propozycja M6.5** | Zejść do **7.0-8.0M** dla 92% kondycji. |
| **Source** | [Polregio EN57 z Zach-Pom.](https://www.rynek-kolejowy.pl/wiadomosci/polregio-chce-kupic-uzywane-en57-od-zachodniego-pomorza-95699.html) |

#### EN71 (USED, 1 sztuka — 4-czł. wariant EN57)

| | |
|---|---|
| **Sztuka** | EN71-024 (1982, 43 lata, kondycja 58%, mileage 3.4M km) |
| **Obecna cena** | **2.2M zł** |
| **Real-world references** | Brak konkretnych aukcji dla EN71 (znacznie rzadsza niż EN57). Ekstrapolacja: 4-czł. wariant ~30-40% droższy niż 3-czł. EN57. |
| **Verdict** | ✅ **Rozsądnie** pomiędzy starą EN57 (0.4M) a mod. (9.5M). |
| **Propozycja M6.5** | Zostawić **2-2.5M** dla 58% kondycji. |
| **Source** | brak twardych (ekstrapolacja) |

### 1.4 Wagony pasażerskie (1 nowy modułowy + 4 wtórne)

#### Coach_264_Universal (NEW, modułowy 26.4m)

| | |
|---|---|
| **Status w grze** | nowy do kupienia (player wybiera funkcję — 2kl/1kl/sypialny/bar/restauracyjny) |
| **Obecna `basePrice`** | **2.8M zł** |
| **Real-world references** | PKP IC 2024 kontrakt na 300 wagonów (mix 7 typów) za **4.239 mld brutto = 14.1M/szt** • Wagon COMBO (P5+modern. 112A): **9.2M brutto/szt** (461M/50, modernizacja) |
| **Verdict** | ⚠️⚠️ **Drastycznie zaniżone (5×!)** vs real 14.1M nowy z FPS Cegielski. |
| **Propozycja M6.5 — decyzja design** | 3 opcje: |
| | **A:** bumpnąć do **14M** = real-world parity, ale gracz trzyma się EZT bo proporcjonalnie tańsze |
| | **B:** zostawić **2.8M** jako "wagon goły", konfigurator dorzuca opcje (~10M w opcjach) |
| | **C (rekomendowane):** **8M baseline + opcje** — kompromis "bazowy wagon, opcje +50-75%" |
| **Source** | [PKP IC 300 wagonów Cegielski 4.239 mld - intercity.pl](https://www.intercity.pl/pl/site/o-nas/dzial-prasowy/aktualnosci/nawet-450-nowych-wagonow-dla-pkp-intercity.-umowa-podpisana.html) |

#### Coach 111A (USED, 1 sztuka — standard 2 kl. Bdhpumn)

| | |
|---|---|
| **Sztuka** | 111A-3010 (1985, 40 lat, kondycja 55%, mileage 5.2M km) |
| **Obecna cena** | **0.28M zł** |
| **Real-world references** | Brak konkretnych aukcji (używane wagony rzadko handlowane). Ekstrapolacja: stary wagon 2 kl. = wartość rezydualna ~2-5% nowego. 14M × 2% = **280k**. |
| **Verdict** | ✅ **Sensowne** dla starego z dużym mileage'em. |
| **Propozycja M6.5** | Zostawić **200-350k** dla starych. |
| **Source** | ekstrapolacja |

#### Coach 112A (USED, 1 sztuka — 1/2 kl. dla TLK/IC, 200 km/h)

| | |
|---|---|
| **Sztuka** | 112A-1005 (1990, 35 lat, kondycja 65%, mileage 4.1M km) |
| **Obecna cena** | **0.65M zł** |
| **Real-world references** | Brak twardych. Wagon przedziałowy 1/2 kl., młodszy + lepszy stan = ~5% real new (14M × 5% = 700k). |
| **Verdict** | ✅ **Rozsądnie**. |
| **Propozycja M6.5** | Zostawić **500-800k**. |
| **Source** | ekstrapolacja |

#### Coach 152A — sypialny (USED, 1 sztuka — niche WL)

| | |
|---|---|
| **Sztuka** | 152A-0020 (1998, 27 lat, kondycja 70%, mileage 2.8M km) |
| **Obecna cena** | **1.4M zł** |
| **Real-world references** | Wagon sypialny WL — niche, mało używanych w obrocie. Zwykle droższy o 40-60% vs zwykły dzięki specjalizacji. |
| **Verdict** | ✅ **Rozsądnie** (~10% nowego, niche premium). |
| **Propozycja M6.5** | Zostawić **1.2-1.6M**. |
| **Source** | ekstrapolacja |

#### Coach 156A — barowy (USED, 1 sztuka — WR z 2015, niemal nowy)

| | |
|---|---|
| **Sztuka** | 156A-0005 (2015, 10 lat, kondycja 85%, mileage 600k km) |
| **Obecna cena** | **2.6M zł** |
| **Real-world references** | Wagon restauracyjny WR z 2015 — niemal nowy, niche. 14M × ~18% = 2.5M. |
| **Verdict** | ✅ **Rozsądnie**. |
| **Propozycja M6.5** | Zostawić **2.5-3.0M** (może lekko podbić bo młody i dobry stan). |
| **Source** | ekstrapolacja |

### 1.5 Operational cost per km — analiza naszego catalog'u

**Czarna skrzynka.** Operatorzy (PKP IC, Polregio, KM) nie publikują rozdzielonych komponentów. Dostępne tylko proxy: Pendolino ED250 ~8.5 PLN/pociągokm; Polregio Łódzkie dopłata samorządu 20.92 PLN/pockm (subsydium, NIE czysty koszt).

**Estymaty branżowe (do walidacji w grze):**
- EZT regionalny nowy: **5-8 PLN/km**
- EN57 modernizowany: **6-10 PLN/km**
- Lokomotywa elektryczna: **3-5 PLN/km**
- Lokomotywa spalinowa: **6-12 PLN/km**

**Nasze obecne wartości** `operationalCostPerKmGroszy` (groszy → PLN dzieląc przez 100):

| Pojazd | Obecne (gr/km) | PLN/km | Real estymata | Verdict |
|---|---|---|---|---|
| EU160_Griffin (NEW) | 380 | 3.80 | 3-5 (loko elektryczna) | ✅ |
| FLIRT_L4268 (NEW, 2-czł.) | 280 | 2.80 | 5-8 (EZT regionalny) | ⚠️ zaniżone |
| FLIRT_LM4268 (NEW, 3-czł.) | 320 | 3.20 | 5-8 | ⚠️ zaniżone |
| FLIRT_ER160 (NEW, 5-czł.) | 420 | 4.20 | 5-8 | ⚠️ lekko zaniżone |
| FLIRT_ED160 (NEW, 8-czł.) | 580 | 5.80 | 5-8 | ✅ |
| SA137 (NEW, 2-czł.) | 420 | 4.20 | 6-12 (spalinowa) | ⚠️ zaniżone |
| SA138 (NEW, 3-czł.) | 510 | 5.10 | 6-12 | ⚠️ zaniżone |
| Coach_264_Universal (NEW) | 60 | 0.60 | n/a (sam wagon = niewielki utrzymanie) | ✅ |
| EU07-085 / EU07-342 (USED) | 310 / 300 | 3.0-3.1 | 3-5 | ✅ |
| EP07-1052 (USED) | 290 | 2.90 | 3-5 | ✅ |
| EP08-013 (USED) | 340 | 3.40 | 3-5 | ✅ |
| EN57-1120 stara (USED) | 260 | 2.60 | 6-10 (zła kondycja drożej) | ⚠️ zaniżone |
| EN57-2001 mod. Ryba (USED) | 240 | 2.40 | 6-10 | ⚠️ zaniżone (modernizacja taniej w utrzymaniu, ale 2.4 to ekstremum) |
| EN71-024 (USED) | 320 | 3.20 | 6-10 | ⚠️ zaniżone |
| SM42-523 (USED) | 360 | 3.60 | 6-12 (spalinowa) | ⚠️ zaniżone |
| 754 Brejlovec (USED) | 440 | 4.40 | 6-12 | ⚠️ zaniżone |
| SA134-015 (USED) | 420 | 4.20 | 6-12 | ⚠️ zaniżone |
| SA136-003 (USED) | 490 | 4.90 | 6-12 | ⚠️ lekko zaniżone |
| Coach 111A (USED) | 55 | 0.55 | n/a | ✅ |
| Coach 112A (USED) | 60 | 0.60 | n/a | ✅ |
| Coach 152A (USED) | 75 | 0.75 | n/a | ✅ |
| Coach 156A (USED) | 70 | 0.70 | n/a | ✅ |

**Insight:** **operational cost generalnie zaniżone w grze** (głównie EZT i spalinowych). Powód może być design'erski — żeby gra była grywalna przy obecnych cenach biletów. M6.5 musi zdecydować: bumpnąć opex do real (5-12 zł/km) i podnieść też dotacje, czy zostawić obecne i obniżyć dotacje. **Rekomendacja: real values + agresywne dotacje** — ekonomia będzie autentyczna, gracz uczy się że osobowe = subsydium, ekspres = marża.

### 1.6 Lifespan (real-world reference)

| Typ | Lifespan | Komentarz |
|---|---|---|
| EN57 (oryg.) | **50-80 lat** | produkcja 1962-93, KM/SKM Trójmiasto wycofanie 2042-43 — UNIKALNE w Europie |
| EN57 po modernizacji (Ryba/AKM) | **+20-30 lat** od modernizacji | KM: po modern. min. 20 lat |
| EU07/EP07/EP08 (oryg.) | **40-50+ lat** | produkcja 1965-93, nadal w użyciu |
| EU07 modernizowane (Olkol, 160 km/h) | **+20-25 lat** | po modernizacji 2024 |
| FLIRT (Stadler) | **30-40 lat** (typowy nowoczesny EZT) | brak długoterminowych danych dla PL |
| Coach pasażerski klasyczny | **35-50 lat** | po P5 ~+15-20 lat każdy |
| EN57 cykl P4 | **500k km / 5 lat** | Polregio |
| EN57 cykl P5 | **3.2M km / 30 lat** | Polregio |
| Średni wiek lokomotyw cargo (UTK 2023) | 30-37 lat | bardzo stary park |
| Średni wiek wagonów towarowych (UTK 2023) | 30.4 lat | jw. |

**Insight dla balansu:** EN57 to "niezniszczalny" workhorse. W grze warto eksponować ekonomiczną przewagę "tani używany EN57 + tania modernizacja" vs "drogi nowy FLIRT" — to autentyczne dla polskiego rynku.

### 1.7 Wartość rezydualna

- Lokomotywa wycofana → **1-3% ceny nowej** (czysty złom). EU07-106: 48k zł (~1% ekwiwalent ceny nowej).
- Lokomotywa sprawna do prywatnego klienta (bocznice przemysłowe) → **5-10% ceny nowej**.
- EN57 modernizowany sprawny → **6-7M** rynek wtórny B2B (~20-25% ceny nowoczesnego EZT).
- Wagon pasażerski stary → **2-5%** ceny nowej (bardzo niska wartość).

**Implikacja dla balansu:** sprzedaż zużytego pojazdu = sunk cost. Nie liczyć jako "investment hedge" w grze. Wyjątek: modernizacja (gracz może kupić wrak za 0.5M, wysłać do warsztatu za 7M, dostać sprawny za 7-8M wartości).

### 1.8 Insights dla balansu — konkretne case'y

**Sumaryczne ⚠️ flagi w naszym catalog'u (do M6.5 rebalansu):**

1. **EU07/EP08/SM42 (lokomotywy klasyczne wtórne) — zawyżone 2-3×** vs real PKP IC ceny złomu/aukcji. To nie kwestia mała, gracz może się zdziwić "dlaczego SM42 z 1973 kosztuje 700k a nowy SA138 17M = SM42 stanowi 4% ceny nowego SZT?". Real: SM42 = 2-3% SA138.
2. **EN57 mod. Ryba — zawyżone ~25%.** 9.5M vs real Polregio 7.6M. Łatwy fix.
3. **FLIRT_ED160 — zaniżone ~25%.** 107M vs real 135M PKP IC 2021. Bumpnąć dla EA-baseline.
4. **Coach_264_Universal — drastycznie zaniżone (5×).** Wymaga decyzji design (real parity / scaled / kompromis 8M+opcje).
5. **Operational cost zaniżone dla większości pojazdów.** EZT i spalinowe poniżej real estymat 5-12 zł/km. Decyzja: bumpnąć opex + dotacje, czy zostawić.
6. **SA138 — lekko zawyżone ~25%.** 17M vs estymata 11-15M.
7. **Reszta (12 pojazdów) — w widełkach ±15%.** Można zostawić.

**Modernizacje jako gameplay (TODO post-M6.5):** mechanika "kup wrak EN57 (0.4M) → wyślij do warsztatu (P5 ~7M) → dostań mod. Ryba (sprawny 7.5M)" zamiast obecnego "kup gotową mod. Ryba 9.5M jako 2 odrębne pojazdy w market". To głębsza decyzja gameplay'owa, ale fragmentaryczne wartości już są w research'u.

### 1.9 Sources

**Per-pojazd (bezpośrednie linki do kontraktów dla pojazdów które MAMY):**
- [Stadler 50 EZT KM 3.2 mld - inwestycje.pl](https://inwestycje.pl/biznes/stadler-ma-umowe-ramowa-na-50-ezt-dla-kolei-mazowieckich-za-32-mld-zl/) — FLIRT_L4268/LM4268/ER160
- [Newag Griffin 30 EU160 - gov.pl](https://www.gov.pl/web/infrastruktura/najwiekszy-w-historii-kontrakt-grupy-pkp-realizowany-przez-newag) — EU160_Griffin
- [PKP IC 300 wagonów Cegielski 4.2 mld - intercity.pl](https://www.intercity.pl/pl/site/o-nas/dzial-prasowy/aktualnosci/nawet-450-nowych-wagonow-dla-pkp-intercity.-umowa-podpisana.html) — Coach_264_Universal
- [Sieć Obywatelska — lokomotywy po cenie złomu](https://siecobywatelska.pl/stala-w-polu-lokomotywa-a-nawet-szesc-ile-kosztowaly-lokomotywy-sprzedane-po-cenie-zlomu/) — EU07/EP07/EP08
- [SM42 modernizacja Newag 20 szt - kurier-kolejowy.pl](https://kurier-kolejowy.pl/aktualnosci/24391/zmodernizowane-sm42---lokomotywy-drugiej-generacji.html) — SM42
- [Polregio EN57 z Zach-Pom.](https://www.rynek-kolejowy.pl/wiadomosci/polregio-chce-kupic-uzywane-en57-od-zachodniego-pomorza-95699.html) — EN57 mod. Ryba
- [PKP IC modernizacja EU07/EP07 do 160 km/h Olkol](https://www.rynek-kolejowy.pl/wiadomosci/jest-wykonawca-napraw-i-modernizacji-10-lokomotyw-eu07-i-ep07-pkp-intercity-82670.html) — EP07
- [PKP IC EN57 KM do 2043](https://www.rynek-kolejowy.pl/wiadomosci/koleje-mazowieckie-en57-beda-jezdzic-do-2043-r-125458.html) — EN57 lifespan

**Operational cost / lifespan:**
- [Polregio Łódzkie pociągokm 20.92 PLN](https://www.rynek-kolejowy.pl/wiadomosci/lodzkie-odkrywamy-tajemnice-polregio--ile-kosztuje-pociagokilometr-95427.html)
- [Pendolino utrzymanie 64 350 EUR/mc](https://kurier-kolejowy.pl/aktualnosci/22750/ile-kosztuje-utrzymanie-pendolino.html)
- [UTK statystyka tabor pasażerski](https://www.dane.utk.gov.pl/sts/tabor/tabor-pasazerski/17610,Tabor-kolejowy-przewoznikow-pasazerskich.html)
- [Modernizacje lokomotyw - transportszynowy.pl](https://www.transportszynowy.pl/Kolej/lokemodernizacje)

---

## 2. Pensje pracowników kolei (2024-2025)

> **Disclaimer:** Sekcja kompilowana offline z training data — bazuje na układach zbiorowych pracy PKP IC/PLK, raportach rocznych, ogłoszeniach Pracuj.pl/OLX, raportach branżowych SITKol. Przed wdrożeniem do `salary_table.json` warto cross-walidować z aktualnymi danymi (Q2 2025).

### 2.1 Maszyniści

| Rola | Min | Średnia | Max | Pracodawca |
|---|---|---|---|---|
| Maszynista PKP IC (B1+B2, EZT/loko) | 7 500 | 9 800 | 13 500 | PKP IC |
| Maszynista Polregio (B1) | 6 200 | 7 500 | 9 800 | Polregio |
| Maszynista KM/SKM/Koleje Śląskie | 6 800 | 8 200 | 11 000 | KM, SKM W-wa, KŚ |
| Maszynista cargo (PKP Cargo, DB, Lotos) | 7 000 | 9 500 | 14 000 | PKP Cargo, DB Cargo |
| Maszynista pomocniczy / stażysta | 4 800 | 5 800 | 7 000 | wszyscy |
| Manewrowy / maszynista zajezdnia | 5 500 | 6 800 | 8 500 | PKP Cargo, IC |

**Dodatki maszynistów (typowe):**
- Nocne: +20% stawki godzinowej (KP art. 151⁸)
- Niedziele/święta: +100% (KP art. 151¹¹)
- Staż: 1%/rok, max 20% (UZP PKP)
- Premia kwartalna/roczna: 10-25% pensji rocznej

### 2.2 Drużyna pociągowa

| Rola | Min | Średnia | Max | Pracodawca |
|---|---|---|---|---|
| Kierownik pociągu PKP IC | 5 200 | 6 800 | 8 500 | PKP IC |
| Konduktor PKP IC | 4 500 | 5 800 | 7 200 | PKP IC |
| Konduktor Polregio/KM | 4 200 | 5 200 | 6 500 | Polregio, KM |
| Asystent konduktora | 4 000 | 4 500 | 5 200 | wszyscy (minimalka 2025: 4 666) |

### 2.3 Mechanicy warsztatowi

| Rola | Min | Średnia | Max | Pracodawca |
|---|---|---|---|---|
| Mechanik P1/P2 | 4 800 | 6 200 | 8 000 | PKP IC, Polregio |
| Mechanik P3/P4 | 5 500 | 7 200 | 9 500 | PKP IC, ZNTK |
| Mechanik P5 / naprawy główne | 6 500 | 8 500 | 11 500 | ZNTK, Pesa SU |
| Elektryk taborowy (3kV/AC) | 5 800 | 7 800 | 10 500 | PKP IC, ZNTK |
| Mechanik silników spalinowych | 5 500 | 7 000 | 9 500 | PKP Cargo, ZNTK Łapy |
| Blacharz/spawacz pojazdów szyn. | 5 200 | 6 800 | 9 000 | ZNTK |
| Lakiernik | 5 000 | 6 500 | 8 500 | ZNTK, Newag, Pesa |
| Brygadzista warsztatu | 7 000 | 8 800 | 11 000 | wszyscy |

### 2.4 Personel pomocniczy

| Rola | Min | Średnia | Max | Pracodawca |
|---|---|---|---|---|
| Sprzątacz pociągów (outsourcing) | 4 666 | 5 000 | 5 800 | Impel, Clar, Ever |
| Pracownik myjni automatycznej | 4 666 | 5 200 | 6 200 | przewoźnicy / outsourcing |
| Pracownik gospodarczy zajezdni | 4 666 | 5 000 | 5 800 | wszyscy |

### 2.5 Dyspozytorzy i ruch

| Rola | Min | Średnia | Max | Pracodawca |
|---|---|---|---|---|
| Dyspozytor przewoźnika | 6 200 | 7 800 | 10 500 | PKP IC, Polregio, Cargo |
| Dyspozytor zajezdni | 5 500 | 7 000 | 9 000 | wszyscy |
| **Dyżurny ruchu PKP PLK** | 5 800 | 7 500 | 10 500 | **PKP PLK SA** (osobna firma) |
| Nastawniczy PLK | 5 200 | 6 500 | 8 500 | PKP PLK |

### 2.6 Kasjerzy i obsługa pasażera

| Rola | Min | Średnia | Max | Pracodawca |
|---|---|---|---|---|
| Kasjer biletowy stacja duża | 4 666 | 5 200 | 6 500 | PKP IC, Polregio |
| Kasjer biletowy stacja regionalna | 4 666 | 4 900 | 5 800 | Polregio, KM |
| Informacja kolejowa | 4 800 | 5 600 | 7 000 | PKP IC |

### 2.7 Administracja / biuro

| Rola | Min | Średnia | Max | Pracodawca |
|---|---|---|---|---|
| Sekretarka / asystent | 4 666 | 5 500 | 7 000 | wszyscy |
| Specjalista HR | 6 500 | 8 000 | 11 000 | PKP IC, PLK |
| Księgowy | 6 800 | 8 500 | 12 000 | wszyscy |
| Główny księgowy | 12 000 | 16 000 | 22 000 | duzi przewoźnicy |
| Specjalista taryf/sprzedaży | 6 000 | 7 500 | 10 000 | PKP IC, Polregio |
| Manager / kierownik działu | 12 000 | 16 500 | 24 000 | duzi |

### 2.8 R&D / inżynieria (Pesa, Newag)

| Rola | Min | Średnia | Max |
|---|---|---|---|
| Inżynier konstruktor młodszy | 7 500 | 9 500 | 12 500 |
| Inżynier konstruktor (5+ lat) | 10 500 | 13 500 | 17 500 |
| Inżynier elektryk pojazdów | 11 000 | 14 500 | 19 000 |
| Inżynier automatyk/software | 12 000 | 16 000 | 22 000 |
| Główny konstruktor / lider | 18 000 | 24 000 | 35 000 |

### 2.9 ZUS pracodawcy + benefity

**Składki pracodawcy (ponad brutto, 2025):**

| Składka | Stawka |
|---|---|
| Emerytalna | 9.76% |
| Rentowa | 6.50% |
| Wypadkowa | 1.67-1.80% (sektor transport) |
| Fundusz Pracy + Solidarnościowy | 2.45% |
| FGŚP | 0.10% |
| **Razem** | **~20.5%** |
| PPK (jeśli pracownik nie zrezygnował) | +1.5% |

**Praktyczna kalkulacja:** 1 PLN brutto = **~1.2048 PLN total cost** (bez PPK), ~1.22 PLN z PPK.

**Premie i bonusy:**

| Element | % pensji rocznej | Komentarz |
|---|---|---|
| 13. pensja (gratyfikacja) | ~8.3% (1/12) | Standard PKP IC, Polregio, PLK (UZP) |
| Premia kwartalna za wyniki | 5-15% | KPI firmy |
| Barbórka kolejowa (25.11) | 1 000-3 000 zł brutto | UZP IC/PLK |
| Dodatek funkcyjny | 5-15% pensji zasadniczej | UZP |
| Dodatek za staż | 1%/rok, max 20% | UZP PKP |
| Pakiet medyczny | wartość 150-300 zł/mies | bardziej w prywatnych |
| Karta sportowa | 100-150 zł/mies | powszechne |
| Bilety kolejowe ulgowe | wartość 200-500 zł/mies | tradycja UZP PKP |

**Sumarycznie:** typowy pracownik kolei = **~13-15 pensji w roku** (12 + 13. + premie kwartalne).

### 2.10 Niedobory rynkowe (2024-2025)

**Krytyczne — pensje rosną najszybciej:**
- **Maszyniści** — chroniczny deficyt (17k maszynistów, śr. wiek 50, odchodzi 600-800/rok, kształci się 400-500). Kursy SOK + B1 = 18-24 mies. ZZM negocjuje 8-12% rocznie. Polregio 2024: premia startowa 10-20k zł.
- **Mechanicy P5/ZNTK** — niedobór elektryków taborowych (3kV/AC). ZNTK Mińsk Maz./Łapy zgłaszały braki 2023-24.
- **Dyżurni ruchu PLK** — fala odejść po reformie wynagrodzeń 2022-23. Strajk PLK luty 2023.
- **Inżynierowie R&D** — konkurencja z motoryzacją (Aptiv, Bosch, ZF). Pensje +10-15%/rok.

**Stabilne (wolniejsze podwyżki):** konduktorzy, kasjerzy, sprzątacze (duża podaż pracy).

### 2.11 Insights dla balansu

1. **Hierarchia płac: PKP IC > Polregio > prywatni regionalni.** Wyjątek: cargo specjalistyczne (Lotos, CTL) potrafi płacić więcej IC.
2. **Outsourcing sprzątania = minimalka**. Sprzątacze formalnie u Impel/Clar, brak benefitów PKP — to NIE etat kolejarski.
3. **"Dyżurny ruchu" jest przy PLK, nie u przewoźnika.** Ważne dla M8 — w grze gracz zatrudnia "dyżurnego ruchu" symbolizującego koszt usług PLK, nie etat własny.
4. **W pierwszym roku gry pensje rosną** ~+8-12% (zgodnie z trendem 2024-2025) — w grze może być balansowy "annual review" wymuszający podwyżki.
5. **Total cost = brutto × 1.21** — jeden multiplikator dla całego salary_table.json + opcjonalnie PPK.

### 2.12 Sources

- ZUS - wskaźniki i składki 2025
- Ustawa z 13.10.1998 o systemie ubezpieczeń społecznych
- UZP PKP IC 2019 z aneksami; UZP PKP PLK
- Raport roczny PKP IC 2023; sprawozdanie Polregio 2023
- ZZM (Związek Zawodowy Maszynistów) negocjacje 2024
- Pracuj.pl, OLX Praca, Praca.pl ogłoszenia 2024-2025
- GUS sektor transportowy

---

## 3. Koszty budowy infrastruktury

### 3.1 Tory (per km)

| Typ | Cena | Rok | Kontekst |
|---|---|---|---|
| Tor szlakowy modernizacja (E59) | ~7.9 mln/km netto | 2019-22 | PORR LK 351, kompleks 47 km torów + 12 rozjazdów + 9 mostów + 9 wiaduktów / 374 mln |
| Tor szlakowy modernizacja (E20) | ~40 mln/km | 2014-15 | Siedlce-Łuków kompleks (tor+sieć+SRK+stacje), 49.77 mln EUR / ~5.4 km |
| Linia 202 modernizacja | ~13 mln/km | 2024 | Gdynia-Słupsk 199 km / 2.6 mld zł (rozszerzony do 5 mld) |
| Tor towarowy szlak (Torpol) | ~5-7 mln/km | 2024 | 312 mln zł kontrakt |
| **Średnia branżowa modernizacja PLK** | **15-25 mln/km** | 2023-24 | **plan PLK 180 mld zł / 7000 km do 2030** ← najlepszy benchmark |
| Nowa linia w czystym terenie | ~30-50 mln/km | 2022+ | bez obiektów inżynieryjnych |
| Nowa linia z obiektami (CPK/Podłęże) | 100-300 mln/km | 2022-30 | tunele/mosty/wiadukty teren górski |
| Stacja w aglomeracji (Katowice) | ~385 mln/km | 2023-30 | 13 km węzeł / 5 mld zł — outlier |

### 3.2 Sieć trakcyjna 3kV DC

| Element | Cena | Rok | Kontekst |
|---|---|---|---|
| Sieć trakcyjna nowa (Ocice-Rzeszów L71) | ~860 tys/km netto | 2018-20 | 57.6 mln net / 67 km — sama sieć |
| Elektryfikacja kompleksowa | 1-2 mln/km | 2020-24 | sieć + słupy + przewody + powrót |
| Podstacja trakcyjna (Lizawice) | 70 mln/szt | 2010 | nowa stacja przekształcania 110/3kV + budynki + drogi |
| Podstacja modernizacja (PLK+PGE EK MUZa II) | ~22 mln/szt | 2024-29 | 2 mld / 90 podstacji (modernizacja vs nowa) |
| Zasięg podstacji 3kV DC | 15-25 km | n/d | typowo 15-20 km dwutorowa |

### 3.3 Rozjazdy

| Typ | Cena z montażem | Rok | Kontekst |
|---|---|---|---|
| Rz S60 1:9 R190 (rynek wtórny) | 12.6 tys zł netto | 2023 | sam materiał używany — metale.pl |
| **Rozjazd R190 1:9 z montażem (PLK)** | **~3-4 mln/szt** | 2017-22 | dekompozycja kontraktów pakietowych (E59: 12 rozjazdów + 47 km torów / 374 mln) |
| R500 1:12 (nowy) | 600-900 tys (estymata) | n/d | wzrost wagi/materiału ~2× względem R190 |
| **R760 1:18 KDP (do 200 km/h)** | **1.5-2.5 mln/szt** | 2022-24 | KZN katalog dla linii 250 km/h |
| Krzyżownica podwójna (scissors) | 1.5-3× cena 2× pojedynczego | n/d | brak konkretnych kontraktów |

### 3.4 Budynki

| Typ | Cena | Rok | Kontekst |
|---|---|---|---|
| Hala stalowa magazynowa | 2 800-3 500 zł/m² | 2025 | 1000 m² = 2.8-3.5 mln |
| Hala produkcyjna | 3 500-5 000 zł/m² | 2025 | instalacje + ppoż |
| Hala P1/P2 (przeglądy) | 3-5 tys zł/m² | 2024 | hala + kanały rewizyjne |
| Hala P3 (średnia naprawa) | 5-8 tys zł/m² | 2024 | + suwnice, dźwigi, tokarka podtorowa |
| Hala P5 (główna naprawa) | 8-15 tys zł/m² | 2024 | suwnice 2× 50t, podnośniki, pełen warsztat |
| **Lokomotywownia W-wa Grochów** | **179 mln zł** | 2024-27 | hala P3 + myjnia + budynek socjalny + diagnostyka ← **benchmark "pełnej zajezdni"** |
| **Centrum serwisowe Przemyśl PKP IC** | ~100-150 mln (estymata) | 2023-24 | hala P3 + myjnia + diagnostyka |
| **Myjnia PKP IC Wrocław** | **33 mln zł** | 2023 | hala 200m + portale samojezdne + zamknięty obieg wody |
| Myjnia tramwajowa Kraków (referencja) | 20 mln net | 2015-19 | 108 m × 16 m × 9.5 m, Budimex |
| Zajezdnia tramwajowa Gdańsk (referencja) | 321 mln zł | 2024-30 | hala 30 tramwajów + stacja + myjnia + paliwa + ładowanie |
| Biuro administracyjne | 4-7 tys zł/m² | 2024 | klasa A/B |
| Pomieszczenie socjalne | 3-5 tys zł/m² | 2024 | szatnie/jadalnia/prysznice |
| Magazyn części | 2-3 tys zł/m² | 2024 | hala stalowa standard |
| Stacja paliw / tankowanie | 5-10 mln zł | 2024 | zbiornik 50-100 m³ + dystrybutory + przeciwpoż. |

### 3.5 Powierzchnie utwardzone

| Typ | Cena | Komentarz |
|---|---|---|
| Asfalt drogowy (sama warstwa) | 50-150 zł/m² | 4-5 cm SMA |
| Asfalt + podbudowa (kompleks) | 200-300 zł/m² | korytowanie + stabilizacja + kruszywo + asfalt |
| Plac manewrowy ciężki (TIR/loko) | 300-500 zł/m² | grubsza warstwa, nośność dla pojazdów |
| Droga wewnętrzna (1m liniowy, 6m szer) | 1 500-3 000 zł/m | przelicznik z m² |
| Parking samochodowy (stanowisko) | 3-8 tys zł/szt | nawierzchnia + oznakowanie + kanalizacja |
| Płot przemysłowy panelowy 2m | 200-400 zł/m | siatka + słupki + cokół + brama |
| Płot kolejowy (ciężki, antywandal) | 400-800 zł/m | wyższy, antywspinaczowy |

### 3.6 Ziemia (działki przemysłowe)

| Lokalizacja | Cena PLN/m² | Rok |
|---|---|---|
| Warszawa I strefa | 450-650 | 2024 |
| Warszawa II strefa | 250-350 | 2024 |
| Warszawa III strefa | ~100 | 2024 |
| Wrocław | 180-300 | 2024 |
| Kraków | 400-450 | 2024 |
| Poznań/Gdańsk | 200-400 | 2024 |
| Płock/Radom (średnie miasta) | 60-150 | 2024 |
| **Działka 40 ha z bocznicą Wrocław** | **~50-100 zł/m²** | 2024 ← grunt poprzemysłowy |
| Działka Chorzów 2.5 ha z bocznicą + hale | ~160 zł/m² | 2024 (4 mln zł) |

**Komentarz:** działki **z istniejącą bocznicą kolejową** to rynek niszowy (kilkadziesiąt ofert w PL). Często grunty poprzemysłowe (cukrownie, huty, papiernie) sprzedawane "w pakiecie" za kilka mln zł niezależnie od wielkości. Realnie: **50-200 zł/m²** dla peryferyjnych miast, **300-500 zł/m²** dla aglomeracji. Centra dużych miast praktycznie niedostępne dla nowej zajezdni.

### 3.7 Insights dla balansu

1. **Skala 5-50× między typami inwestycji.** Tor szlak (5-10M/km) vs stacja w aglomeracji (200-400M/km). W grze: NIE traktuj "1 km toru" jako jednej liczby — rozdziel: szlak (~10M) / stacja regionalna (~30-60M) / stacja aglomeracyjna (~200-400M).
2. **Modernizacja vs nowa: 2-3× różnicy.** Modernizacja istniejącej linii 15-25M/km, nowa linia czysty teren 30-50M/km, nowa z obiektami 100-300M/km.
3. **Inflacja sektorowa 2020-2024 +50-100%.** Kontrakty z 2017-19 są zaniżone o ~50-80% wzg. cen 2024. Skaluj historyczne ×1.5-2.
4. **Rozjazd to NIE 1:1 cena z katalogu.** Realny koszt R190 z wkolejeniem = ~3-4 mln zł (×100 ceny używanego — montaż >> materiał).
5. **Sieć trakcyjna ekonomiczna.** Sama sieć ~1 mln/km = 5-10% kosztu modernizacji toru. Decyzja o elektryfikacji 50 km linii: ~50M (sieć) + 3 podstacje × 30M = +140M.
6. **Hala P5 ≫ hala P1.** P1 = 3-5k zł/m², P5 = 8-15k zł/m². **Lokomotywownia Warszawa Grochów (179M)** to dobry benchmark "pełnej zajezdni" (P3+myjnia+diagnostyka).
7. **Działka z bocznicą = rynek niszowy.** Zwykle grunty poprzemysłowe za 50-200 zł/m² peryferia. W centrach aglomeracji nowej zajezdni praktycznie się nie zbuduje.
8. **Brak transparentności jednostkowych cen.** PLK rzadko podaje "cena za rozjazd X" jawnie — szacuj z dekompozycji kontraktów pakietowych z ±30% widełkami.
9. **Skala makro:** plan PLK 2030 = **180 mld zł / 7000 km torów = ~25 mln zł/km średnio dla "modernizacji kompleksowej"**. Sam tor szlak to 30-50% tej kwoty (~10M/km), reszta to perony+SRK+obiekty+sieć.

### 3.8 Sources

- [PORR LK 351 E59 374 mln](https://porr.pl/pl/media/informacje-prasowe/przeglad/informacja-prasowa/news/porr-sa-zmodernizuje-fragment-strategicznej-lk-e590/)
- [Trakcja kontrakt E59 398 mln - Money.pl](https://www.money.pl/gielda/konsorcjum-trakcji-ma-umowe-z-pkp-plk-na-roboty-na-e59-za-398-34-mln-zl-netto-6434014756259969a.html)
- [Linia 202 modernizacja - Inżynieria.com](https://inzynieria.com/drogi/modernizacja_kolei/wiadomosci/99275,linia-nr-202-do-przebudowy-pkp-plk-podpisaly-kontrakt-za-2-6-mld-zl)
- [Elektryfikacja Ocice-Rzeszów](https://www.rynek-kolejowy.pl/wiadomosci/jest-przetarg-na-elektryfikacje-linii-ocice--rzeszow-koszt-to-nawet-70-mln-zl-85825.html)
- [PKP PLK plan 180 mld / 7000 km - Forsal](https://forsal.pl/transport/kolej/artykuly/8794868,miliardy-zlotych-na-modernizacje-torow-tak-zmieni-sie-kolejowa-polska.html)
- [Lokomotywownia W-wa Grochów 179 mln](https://www.gov.pl/web/infrastruktura/nowoczesna-lokomotywownia-pkp-intercity)
- [Myjnia PKP IC Wrocław 33 mln](https://www.sektorkolejowy.pl/nowa-myjnia-pkp-intercity-we-wroclawiu-juz-gotowa/)
- [Centrum serwisowe Przemyśl - NBI](https://nbi.com.pl/wiadomosci/centrum-serwisowe-pkp-intercity-przemysl/)
- [Podstacja Lizawice 70 mln](https://www.plk-sa.pl/o-spolce/biuro-prasowe/informacje-prasowe/szczegoly/podstacja-trakcyjna-lizawice-617)
- [PLK + PGE EK 2 mld / 90 podstacji](https://enerad.pl/pkp-plk-i-pge-ek-lacza-sily-2-mld-inwestycji-w-siec-trakcyjna/)
- [KZN rozjazdy katalog](https://kzn.pl/wp-content/uploads/2018/04/KZN_Kolej_A4_int.pdf)
- [Ceny działek przemysłowych 2024 - Bankier](https://www.bankier.pl/wiadomosc/Ceny-transakcyjne-dzialek-budowlanych-I-kw-2024-r-Raport-8763048.html)
- [Hale stalowe 2025 - Konspekt](https://www.konspekt.eu/blog/koszt-budowy-hali-stalowej-w-2025-roku-aktualne-ceny-przyklady-i-metraze/)
- [Inflacja kosztów budowy 2016-2023 - Inżynier Budownictwa](https://inzynierbudownictwa.pl/koszty-w-budownictwie-2016-2023/)

---

## 4. Koszty utrzymania operacyjnego (taryfy 2024-2025)

### 4.1 Opłaty PLK za dostęp (TUI)

Stawki obowiązują od 2022 r., niezmienione w rozkładzie 2024/2025 (decyzja UTK XII 2023).

| Wskaźnik | Wartość PLN/pociągokm |
|---|---|
| **Średnia jednostkowa cała sieć** | **8.01** |
| Średnia pociągi pasażerskie | **6.31** |
| Średnia pociągi towarowe | **12.77** |
| Średnia opłata manewrowa (trakcja własna) | 3.66 |
| Średnia opłata manewrowa (inna trakcja) | 3.37 |

**Struktura cennika:** macierz **kategoria linii (1-7) × przedział masy pociągu** (przedziały co 60 ton). PLK klasyfikuje linie wg prędkości max (60% wagi) + natężenia ruchu (40%). Pełna macierz w **Załączniku 9.1 Regulaminu sieci 2024/2025** (PDF na plk-sa.pl).

**Insight:** dla pasażera na liniach średniej kategorii ~5-8 PLN/pockm. Magistrale (CMK, E20/E30) w górnym przedziale. Lokalne 8/9 — w dolnym (3-4 PLN/pockm).

**Roczny przychód PLK z dostępu:** ~2.3 mld PLN (NaKolei).

### 4.2 Opłaty postojowe na stacjach

PLK pobiera opłatę postojową naliczaną per zatrzymanie handlowe. Bardzo zróżnicowane wg kategorii:

| Stacja | Opłata postojowa PLN |
|---|---|
| Bystra Podhalańska (mała lokalna) | 0.68 |
| Jerzmanice Lubuskie | 0.68 |
| Włoszczowa Północ (CMK) | 10.13 |
| **Łódź Fabryczna (duży węzeł)** | **138.72** (z możliwą zniżką -75%) |

**Kategoryzacja stacji** (PLK Regulamin OIU 2024): Premium / I / II / III / IV / V — w grze najlepiej zmapować jako 4-5 kategorii.

**Postój techniczny / overnight:** osobny cennik OIU, ~2-5 PLN/godz × długość pociągu.

### 4.3 Energia trakcyjna

**Dostawca:** PGE Energetyka Kolejowa (dawniej PKP Energetyka). URE zatwierdza taryfę rokrocznie. Grupa taryfowa **C1x** (zasilanie z sieci PKP).

| Składnik | PLN/MWh |
|---|---|
| Cena energii brutto (bez dystrybucji) | 600-900 |
| Dystrybucja + opłata mocowa + akcyza | 200-400 |
| **Razem realny koszt brutto** | **900-1300** = **0.90-1.30 PLN/kWh** |

Stawki maksymalne URE 2025 dla biznesu ograniczone do 693 PLN/MWh (część energetyczna, dystrybucja PGE EK nieobjęta limitem).

**Zużycie kWh per pociągokm:**

| Typ taboru | kWh/pockm |
|---|---|
| EZT lekki (EN57) z rekuperacją | 4-6 |
| EZT lekki bez rekuperacji + ogrzewanie zima | 8-12 |
| EZT średni (Impuls 36WE, FLIRT) | 5-8 |
| EZT podmiejski 14WE Halny (faktyczne) | 7.0 (SKM Warszawa pomiar) |
| Lokomotywa elektryczna + 6-7 wagonów (EU07+TLK) | 12-18 |
| Pendolino ED250 (160-250 km/h) | 18-25 |
| Vectron + IC ekspres 160 km/h | 14-20 |

**Insight:**
- Prędkości >160 km/h zwiększają zużycie ~+30% vs <120 km/h
- Ogrzewanie zimą może podwoić zużycie (EN57: 70 kW na 3 wagony, 800 kWh dziennie)
- Rekuperacja w nowoczesnym taborze odzyskuje ~15-25% energii hamowania

### 4.4 Paliwo (tabor spalinowy)

**Cena ON Ekodiesel hurtowo (Orlen B2B):** 5500-6700 PLN/m³ netto (kwiecień 2025-2026). Karty paliwowe flotowe -5 do -15 gr/L. Przewoźnicy kolejowi przez przetargi większe partie -3 do -7%. **Roboczo: 5.50-6.00 PLN/L netto** dla floty 2025.

| Pojazd | Zużycie ON |
|---|---|
| SM42 (manewrowa, bieg jałowy) | 4.5 L/h |
| SM42 (lekki ruch z postojami) | do 200 L/100km |
| SM42 (manewry standardowe) | 30-60 L/h |
| SU45/SU46 (liniowa) | 2-3 L/km |
| SU42 (lekka liniowa) | 1.5-2.5 L/km |
| SA134/SA138 (autobus szynowy 2-czł.) | 1.2-2.0 L/km |
| SA139 LINK 2-czł. | 1.0-1.8 L/km |
| SU160 (Newag Dragon-spalinowy/Vectron Diesel) | 2.5-4.0 L/km |

**Insight:** SM42 manewrówka bardzo żre (silnik a8C22 590 kW pracuje ze złą sprawnością przy częstych rozruchach). Modernizacje SM42-18D (Newag) -30-40% zużycia. SA138/SA139 = "tańsze niż samochód osobowy per pasażerokm" przy 60 pasażerach.

### 4.5 Woda (mycie pojazdów)

**Taryfa wody przemysłowej Polska 2024-2025** (woda + ścieki):

| Miasto | PLN/m³ |
|---|---|
| Białystok | 8.20 |
| Bydgoszcz | 10.42 |
| Wrocław | 11.80 |
| Łódź | 12.01 |
| Gdańsk | 12.58 |
| Kraków | 13.73 |
| Warszawa | 13.72 |
| Poznań | 14.92 |
| Katowice | 17.05 |

**Średnia roboczo:** ~12-14 PLN/m³ łącznie.

**Zużycie m³/cykl (estymaty):**
- EZT 200m bez recyrkulacji: 8-15 m³/cykl
- EZT 200m z recyrkulacją (nowoczesna myjnia): 2-4 m³
- Lokomotywa solo: 1-3 m³

**Koszt jednego mycia EZT:** ~30-200 PLN per cykl (zależnie od recyrkulacji i miasta).

### 4.6 Ubezpieczenia

**OC przewoźnika obowiązkowe (UTK 2026):**

| Typ operatora | Min. suma gwarancji |
|---|---|
| Operator wąskotorowy | 100k EUR (~422k zł) |
| Operator własna infrastruktura | 250k EUR (~1.05 mln zł) |
| **Mainstream przewoźnik** | **2.5 mln EUR (~10.54 mln zł)** |

**Składka realna** dla średniego przewoźnika towarowo-pasażerskiego: **80-300k zł/rok** bazowa + extra wg historii szkodowej.

**Casco taboru (dobrowolne):** brak twardej publicznej taryfy. Standard mienia ruchomego o wysokiej wartości: **0.15-0.40% wartości pojazdu/rok**:
- EZT Impulsa 35M = ~50-140k zł/rok
- EN57 wtórny 1-3M = ~2-12k zł/rok

**OC pojazdu:** kolej w PL **nie ma OC per-pojazd** (analog auta) — odpowiedzialność u przewoźnika jako całości (OC działalności).

### 4.7 GSM-R

System wdrażany do 2030, od 2025 podstawowa łączność (UTK/PLK 2024). **Brak publicznego cennika abonamentu** (dane niejawne między PLK i przewoźnikami).

**Estymaty branżowe:**

| Pozycja | Koszt |
|---|---|
| Instalacja per pojazd (terminal pokładowy + integracja) | 50-150k zł one-time |
| Abonament miesięczny | ~200-800 zł/pojazd/mies (analog DE/CZ ~50-150 EUR) |
| Terminal przenośny dla maszynisty | 5-15k zł |
| Roczne utrzymanie/kalibracja | 5-10% wartości terminalu |

**Roboczo dla balansu gry:** **~10k zł/pojazd/rok all-in** (abonament + utrzymanie + amortyzacja terminalu).

### 4.8 Licencje / certyfikacja UTK

| Pozycja | Stawka | Częstotliwość |
|---|---|---|
| Świadectwo bezpieczeństwa przewoźnika (nowe) | **7 000 zł** | one-time |
| Przedłużenie świadectwa | 1 500+ zł | cyklicznie |
| Zmiana świadectwa | 800-1 500 zł | per zmiana |
| **Licencja maszynisty** | **100 zł** | per maszynista, raz |
| Egzamin państwowy licencji maszynisty | 150 zł | per maszynista, raz |
| Egzamin świadectwa maszynisty (per kategoria/seria) | 600 zł | per egzamin |
| Jednolity certyfikat bezpieczeństwa (CSC) — duży przewoźnik | dziesiątki tys. zł | co 5 lat |
| Certyfikat zgodności pojazdu (TS interop) | 5-30 tys/pojazd | one-time + okresowe |

**Insight:** średnia spółka (3-5 typów pojazdów, 50-100 maszynistów) → wydatki UTK **~50-200k zł/rok**.

### 4.9 Podatek od nieruchomości

Stawki maksymalne (Min. Finansów); gminy mogą obniżać 70-95% maks.

| Typ nieruchomości | 2024 PLN/m² | 2025 PLN/m² |
|---|---|---|
| Budynki działalności (hala, biuro, magazyn) | 33.10 | **34.00** |
| Grunty pod działalność gospodarczą | 1.34 | **1.38** |

**Przykład średniej zajezdni:** hala 5000 m² + budynek socjalny 1500 m² + grunt 30000 m²
= 5000×34 + 1500×34 + 30000×1.38 = **221 400 zł/rok max**, realnie po uchwale gminy ~150-200k.

**Duża zajezdnia** (depo IC 30k m² hal + 100k m² gruntu): **0.8-1.5 mln zł/rok**.

### 4.10 Audyty (opcjonalne, branżowy standard)

**ISO 9001/14001/45001:**
- Certyfikacja pierwsza: 30-100k zł
- Recertyfikacja co 3 lata: 30-60% pierwszej
- Audity nadzorcze roczne: 10-30k zł
- **Roczne utrzymanie 3 ISO łącznie:** ~50-150k zł/rok

**Kontrole UTK:** bezpłatne dla przewoźnika; sankcje za naruszenia: 10-500k zł.

### 4.11 Insights dla balansu

1. **Struktura kosztów średniego przewoźnika pasażerskiego (orient.):**
   - Pensje: 40-50%
   - Energia/paliwo: 15-25%
   - Dostęp PLK (TUI): 10-15%
   - Utrzymanie taboru: 10-15%
   - Reszta (ubezpieczenia/podatki/admin/GSM-R): 5-10%
   - **Cost ratio PKP IC 2023: ~96%** (4.78 mld PLN koszty / 5.0 mld PLN przychód)
2. **PLK opłata = main bottleneck.** 8 PLN/pockm × 1000 km dziennie × 365 = **~3 mln/rok per pociąg** (duży operator). Regionalny (200 km/dzień) = ~580k/rok.
3. **Ceny PLK stabilne** — niezmienione od 2022. Można hardcodować jako stałą "stan 2024".
4. **Energia vs paliwo:** EZT ~3-7 PLN/pockm (energia), spalinówka ~6-12 PLN/pockm (paliwo). Tabor spalinowy 2× droższy w eksploatacji — uzasadnia preferencję elektryfikacji.
5. **Mycie i woda mało istotne** (rzędu pojedynczych % budżetu). Można uprościć do flat-rate per cykl serwisu.
6. **Casco opcjonalne** — wielu mniejszych przewoźników w PL casca nie ma (składka 0.2-0.4% × flota = miliony rocznie). To dobre miejsce na decyzję strategiczną w grze.
7. **Obowiązkowe OC = 2.5 mln EUR sumy** → składki w setki tys zł/rok jako fixed cost.
8. **GSM-R = czarna skrzynka.** Roboczo 10k/pojazd/rok all-in. Dla EA 15-25 pojazdów = ~150-250k/rok.
9. **Świadectwa UTK przewidywalne** — średni przewoźnik ~50-200k zł/rok.
10. **Podatek od nieruchomości skaluje z wielkością.** Średnia zajezdnia 200-300k zł/rok, duża 0.8-1.5 mln/rok.
11. **Postoje drogie tylko w dużych węzłach** (Łódź Fabryczna 138 zł vs Bystra 0.68 zł). Stacje końcowe ekspresów = drogie; lokalne mijanki = symboliczne.
12. **Inflacja energii 2022-2024 znacząca** — udział energii w kosztach 12% (2021) → 20-25% (2023-24). 2024 = "high energy" baseline.
13. **Game-design:** trzymaj wszystkie wartości w `EconomyConstants.cs` z komentarzem `// stan: PLK rozkład 2024/2025`. Dla EA precyzja ±20% wystarczy.

### 4.12 Sources

- [UTK: Cennik PKP PLK 2024/2025 bez zmian](https://utk.gov.pl/pl/aktualnosci/20936,Cennik-PKP-PLK-bez-zmian-w-rozkladzie-20242025.html)
- [PLK Załącznik 9.1 Regulamin sieci 2024/2025](https://www.plk-sa.pl/files/public/user_upload/pdf/Reg_przydzielania_tras/Regulamin_sieci_2024_2025/v.1/Zal_9.1_Reg24_25_v1_PL_czyst.pdf)
- [PLK Regulamin OIU postoje](https://www.plk-sa.pl/files/public/user_upload/pdf/Obiekty_infrastruktury_uslugowej/20.09.2024/OIU_23-24_Z_7_w.2-w._edytowalna.pdf)
- [PGE EK Taryfy](https://pgeenergetykakolejowa.pl/strona/taryfy-i-cenniki)
- [URE Taryfy energii 2024](https://bip.ure.gov.pl/bip/taryfy-i-inne-decyzje-b/energia-elektryczna/4570,Taryfy-opublikowane-w-2024-r.html)
- [Orlen Hurtowe ceny paliw](https://www.orlen.pl/pl/dla-biznesu/hurtowe-ceny-paliw)
- [UTK OC przewoźnika 2025](https://utk.gov.pl/pl/aktualnosci/22005,Minimalna-suma-gwarancyjna-ubezpieczenia-OC-dla-umow-zawieranych-w-2025-roku.html)
- [UTK Świadectwo bezpieczeństwa](https://utk.gov.pl/pl/uslugi/koleje-turystyczne/uslugi-dla-kolei-turyst/11718,Wydanie-swiadectwa-bezpieczenstwa-dla-przewoznika-kolejowego-zwolnionego-z-obowi.html)
- [UTK Licencja maszynisty](https://utk.gov.pl/pl/uslugi/maszynisci/uslugi-dla-maszynistow/15423,Wydawanie-licencji-maszynisty.html)
- [UTK GSM-R](https://utk.gov.pl/pl/interoperacyjnosc/ertms/gsm-r-1/)
- [Min. Finansów Stawki podatku od nieruchomości 2025](https://www.gov.pl/attachment/025dfbd5-ff42-408a-8db2-b644933dbfe9)
- [SKM Warszawa 14WE Halny dane](https://www.skm.warszawa.pl/o-nas/tabor/ezt-14we-halny/)
- [Frutiger Polska myjnia kolejowa](https://frutiger.pl/offer/mycie-pojazdow-technologicznych/mycie-pojazdow-szynowych/)

---

## 5. Implications for game balance — synthesis

### 5.1 Hierarchia skali finansowej

| Skala | Real-world equivalent | Gracz w grze |
|---|---|---|
| **Setki tys zł** | Drobne zakupy używanych EU07/SM42, ~2-3 mies. pensji małej firmy | Pierwszy miesiąc gry, mała firma startup |
| **Miliony zł** | EZT regionalny używany, modernizacja, hala P1, podatek roczny dużej zajezdni | Pierwszy rok, mała firma |
| **Dziesiątki milionów** | Nowy EZT 4-5-czł., lokomotywownia kompleksowa, podstacja trakcyjna, hala P5 | Rok 2-5, średnia firma |
| **Setki milionów** | Kontrakt ramowy 10-30 EZT, modernizacja stacji aglomeracyjnej, długa elektryfikacja | Rok 5-10, duża firma |
| **Miliardy zł** | Polregio scale (~1-2 mld rocznego obrotu), PKP IC scale (~3-4 mld rocznego), modernizacja całej linii | Rok 10+ gameplay, dominacja regionalna |

**Implikacja:** EA-target = dziesiątki milionów miesięcznie. Pierwsze 30 minut real time = setki tys zł. Skala miliardów to długoterminowy progres (rok 10+).

### 5.2 Mapa kosztów dla balansu M6.5

**Constants do ustawienia w grze:**

| Plik | Co tuningować | Reference numbers |
|---|---|---|
| `BalanceConstants.Economy` | TUI per-km wg klasy linii, opłaty postojowe, energia kWh→zł, casco %, OC fixed | Sekcja 4 |
| `fleet_catalog_ea.json` | Ceny new + used per seria, operationalCostPerKm, lifespan, residualValue | Sekcja 1 |
| `salary_table.json` | Pensje brutto per (rola × skill 1-5★), ZUS multiplier, premie | Sekcja 2 |
| `inspection_intervals.json` | Koszty przeglądów P1-P5 (na bazie kosztów modernizacji ÷ ilość przeglądów w cyklu) | Sekcja 1.3 |
| Nowy: `BalanceConstants.Construction` | Koszty per km toru, sieci, rozjazdy, hale per m² | Sekcja 3 |
| Nowy: `BalanceConstants.Maintenance` | Koszty mediów (energia, paliwo, woda), GSM-R, podatek nieruchomości | Sekcja 4 |

### 5.3 Decyzje balansowe

1. **Czy pełna realność cen (miliony za nowy EZT) czy "scaled-down"?**
   - **Argument PRO realne:** wciąga gracza, edukuje, daje "wow" (FLIRT 47 mln zł!)
   - **Argument PRO scaled-down:** liczby łatwiejsze do operowania umysłem, mniej zerów na ekranie
   - **Rekomendacja:** PEŁNE realne ceny — gra symuluje firmę kolejową, miliony to atut tematyki. Format wyświetlania: **"47.2M zł"** zamiast 47 200 000 zł.

2. **Czy pierwsze 30 min real time = pełna skala czy ograniczona?**
   - Starting budget Normal preset ~250-300k zł — gracz nie kupi za to nawet używanej EU07 (442k+)
   - **Opcja A:** podnieść starter do 500-1000k zł (gracz kupuje 1 SM42 ~350k LUB 2 SA138 wtórne)
   - **Opcja B:** trzymać 250-300k zł ale pierwszy zakup to **modernizacja istniejącej infrastruktury** (gracz ma od starta 1-2 EN57 wtórne ~200k każda — niskie ROI ale stabilne)
   - **Rekomendacja:** test obu w playtesting — Opcja A daje "wow agency" zakupu, Opcja B uczy "nie masz pieniędzy żeby latać".

3. **Czy operationalCostPerKm = real-world (5-12 PLN) czy scaled?**
   - Real wartości DZIAŁAJĄ z real cenami biletów (25-300 zł)
   - **Rekomendacja:** real values, kalibracja dotacjami nie cenami biletów (player-controlled).

4. **Czy dotacje wojewódzkie wzorować na real PSC contracts?**
   - Real model: stawka per pasażerokilometr (0.5-2 zł/pkm) + warunki min liczby kursów + max ceny biletu
   - Polregio Łódzkie 20.92 zł/pociągokm jako reference dotacji
   - **Rekomendacja:** TAK, real model. SubsidyCalculator (M6-6) już to obsługuje, w M6.5 kalibracja stawek per województwo.

### 5.4 Pacing finansowy (od starter do dominacji)

| Etap | Czas gameplay | Skala | Co gracz kupuje |
|---|---|---|---|
| Starter | 0-30 min real | Setki tys zł | Pierwsze 1-2 wtórne pojazdy (SM42 ~350k, EU07 ~500k) |
| Konsolidacja | 30 min - 2h real (≈3-6 mies. game x150) | Miliony zł | EN57 modernizowany 6-7M, drobne tory, Pierwsza dotacja |
| Wzrost | 2-5h real (≈1-2 lata game) | Dziesiątki milionów | Nowy EZT 35-45M, hala P3, modernizacja linii |
| Ekspansja | 5-15h real (≈3-7 lat game) | Setki milionów | Kontrakt ramowy 5-10 EZT, lokomotywownia kompleksowa, podstacje trakcyjne |
| Dominacja | 15h+ real (≈10+ lat game) | Miliardy zł | Polregio/IC scale, modernizacja kompletnych linii |

### 5.5 Co Z TYM zrobić w M6.5

**Sesje (sugerowany porządek):**

1. **Sesja 1 — Fleet pricing rebalance:** edycja `new_models.json` + `initial_market.json` na bazie konkretnych verdictów z Sekcji 1.1-1.4. Najważniejsze zmiany:
   - **Coach_264_Universal:** decyzja design 14M / 2.8M / 8M+opcje
   - **FLIRT_ED160:** bumpnąć 107M → 130-140M
   - **EN57 mod. Ryba:** zejść 9.5M → 7-8M
   - **EU07/EP08/SM42 (wtórne):** zejść do real PKP IC ceny złomu (0.4-1.5M zamiast 1.5-4M)
   - **SA138:** zejść 17M → 14-15M
2. **Sesja 2 — Operational cost calibration:** podnieść `operationalCostPerKmGroszy` dla EZT i spalinowych do real estymat (5-12 zł/km). Plus decyzja: bumpnąć też dotacje czy zostawić. Sekcja 1.5 ma porównanie obecne vs real.
3. **Sesja 3 — Salary calibration:** `salary_table.json` per (rola × skill). 10 ról × 5 poziomów. ZUS multiplier 1.21. Sekcja 2.
4. **Sesja 4 — TUI / energia / paliwo:** `BalanceConstants.Economy.TrackUseIndemnity` per klasa linii. Energia kWh→zł. Paliwo L→zł. GSM-R fixed. Sekcja 4.
5. **Sesja 5 — Construction constants:** nowy `BalanceConstants.Construction` — koszty per km tor/sieć/rozjazd, hala per m². Sekcja 3.
6. **Sesja 6 — Subsidy calibration:** SubsidyCalculator stawki per województwo + warunki (min kursów, max cena, jakość punktualności). Reference: Polregio Łódzkie 20.92 zł/pociągokm.
7. **Sesja 7 — Difficulty modifier ranges:** kalibracja 10 mnożników z M13-13 — co znaczy 0.5x, 1.0x, 2.0x w realnych liczbach gameplay'u.
8. **Sesja 8-10 — In-game iteracje:** save-checkpointing → tweak constants → load → play 30 min → compare. Krytyczne scenariusze: starter 30 min, mała firma rok 1, średnia firma rok 5.

**Output dokumenty:**
- `new_models.json` (rebalans cen 8 modeli)
- `initial_market.json` (rebalans cen 15 wtórnych)
- `BalanceConstants.Economy` (rozbudowa: TUI, dotacje, energia)
- `BalanceConstants.Construction` (nowy plik)
- `BalanceConstants.Maintenance` (nowy plik)
- `salary_table.json` (rebalans 10 ról × 5 skill)
- `inspection_intervals.json` (rebalans kosztów P1-P5)
- `subsidy_rules.json` (nowy, per województwo)
- `docs/design/m6-5-economy-balance.md` (decyzje balance: konkretne wartości + uzasadnienia)

---

## 6. Caveats i TODO

1. **Pensje wymagają cross-walidacji** z Q2 2025 raportami PKP IC/Polregio gdy będą dostępne.
2. **Operational cost per km dla taboru = czarna skrzynka** — nie ma jawnych liczb publicznych. Trzeba kalibrować in-game gameplay'em z szerokimi widełkami.
3. **GSM-R abonament = niejawny** — używaj estymaty 10k zł/pojazd/rok all-in.
4. **Inflacja sektorowa 2024-2026** może być istotna (energetyka, infrastruktura — szybsze tempo niż CPI ogólne). Weryfikuj baseline 2025 przed startem M6.5.
5. **Postoje na stacjach** — pełen cennik OIU PLK dostępny tylko jako PDF z ~kilkudziesięcioma kategoriami. Dla EA wystarczy 4-5 kategorii (Premium / I / II / III / IV).
6. **Towar (cargo)** — runtime cargo nie jest jeszcze w M6 (data tylko). Jak dojdzie, real numbers towarowe (Lotos Kolej, DB Cargo, PKP Cargo) wymagają osobnego researchu. M6.5 skupia się na pasażerskim.
7. **Multi-currency dla DLC** (Niemcy, Czechy w post-1.0) — póki co PL only, później może warto research analogiczny dla DE (Deutsche Bahn) i CZ (ČD).
8. **Brakujące pojazdy w grze** — research zidentyfikował też pojazdy których w `new_models.json`/`initial_market.json` NIE MA. Świadomie wycięte z tego dokumentu. Lista do rozważenia w przyszłości:

### 6.1 Potential additions (post-M6.5 / M-Fleet+)

Jeśli w przyszłości chcesz rozszerzyć catalog gry, rozważ:

| Pojazd | Real-world reference | Uzasadnienie dorzucenia |
|---|---|---|
| **Newag Dragon 2** (loko towarowa) | 15-16.7M net (PKP Cargo 2018-19) | Wymaga gameplay'u towarowego — odłożone do post-EA |
| **Siemens Vectron MS** (loko towarowa) | 21-27.8M (PKP Cargo 2015-21) | Jw. — alternatywa dla Dragon 2 |
| **Newag Impuls 2** (EZT polski) | 19-33M (3-4 czł., ŁKA/Podkarpacie) | Konkurencja Stadler-monoculture FLIRT-ów |
| **Pesa Elf 3** (EZT polski) | 36-44M (2/5-czł., Polregio 2024) | Jw. |
| **Pesa Husarz** (loko elektryczna pasażerska Gama) | 34.7M (PKP IC 2024, **NIE hybrydowa** — to halucynacja research'u, real Husarz to standardowa elektryczna) | Konkurencja Griffin'a od Newag'a |
| **Pendolino ED250** (high-speed) | high-speed niche, ~60-80M brutto/szt | Niski priorytet pre-EA, niche segment |
| **Modernizacje gameplay'owe** | EN57→AKM 6.75-8M, EU07→160 km/h 10M, SM42→6Dg 3.85M | Mechanika "kup wrak → wyślij do warsztatu → dostań mod." zamiast obecnego "kup gotową mod. jako 2 odrębne pojazdy w market" |
| **Wagony specyficzne nowe** (sypialny WL, restauracyjny WR jako osobne SKU) | 14M brutto każdy z PKP IC 2024 mix | Obecnie tylko Coach_264_Universal modułowy + wtórne 152A/156A — można dodać nowe |

**Decyzja gameplay'owa do podjęcia:** czy w EA gracz ma się trzymać 23 pojazdów, czy dorzucić ~5-8 nowych (Impuls + Elf + Husarz + Dragon 2 lub Vectron + ewentualnie Pendolino). To dotyczy M-Fleet+ jako follow-up post-M6.5 — research dla tych pojazdów jest gotowy w transcript'cie research agentów (zachowany ale poza tym dokumentem).

---

**Document size:** ~10k słów (rev. 2 zwiększyło z 9.5k przez per-pojazd analizę). Source of truth dla M6.5 Rebalance — jeden plik zawężony do realnego catalog'u gry.
