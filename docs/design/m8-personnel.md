# M8 — Personel (pracownicy firmy przewozowej)

> **Status:** Plan (pre-implementation), 2026-04-20
> **Zależności:** M5 ✅ (Obiegi — punkt integracji dla obiegów pracowniczych), M6 ✅ (Ekonomia — koszty pensji), M7 ✅ (Maintenance — mechanicy w warsztatach, self-repair), M9 ✅ (Ruch — runtime dla służb na trasach), M2/M2.6 ✅ (Depot — PathGraph + RoomDetectionSystem dla cyklu 3D)
> **Niezależny od:** M-Models (pracownicy są placeholderami 3D — kapsułki + kolor per rola, real modele swap w M-Models)
> **Cel:** Pełny system personelu — 8 ról z własnym gameplay'em, obiegi pracownicze niezależne od taborowych, indywidualne harmonogramy, cykl dnia w 3D, morale/fatigue/strajki.

---

## 1. Filozofia

Pracownicy to **trzecia oś zarządzania** obok taboru (M3/M-Fleet) i rozkładów (M4). Gracz nie tylko kupuje pociągi i układa linie — musi też zatrudniać, planować turnusy, pilnować morale. Zaniedbanie personelu = strajki, awarie, opóźnienia, rotacja.

**Zasada przewodnia: pracownicy nie są "abstrakcyjnym kosztem"** jak w Transport Fever. Każdy ma imię, skill, kalendarz, widać go w zajezdni. Ale **nie są mikromanagementem jak w Prison Architect** — gracz zarządza per rola/dział, nie per akcja. Poziom szczegółowości: "zatrudnij 3 maszynistów 3★ na zmianę poranną", nie "kliknij żeby maszynista podniósł kawę".

**Dwa poziomy planowania:**
1. **Makro** — zmiany bazowe, pula pracowników per rola, ogólne przypisania działami (warsztat, biuro, kasy)
2. **Mikro (opcjonalny dla zaawansowanych graczy)** — indywidualny harmonogram + obiegi pracownicze dla maszynistów/konduktorów (analog M5 ale dla ludzi)

**Kluczowe różnice względem M5 Obiegi:**
- Obieg taboru = fizyczny pojazd realizuje sekwencję TrainRunów danego dnia
- **Obieg pracowniczy (turnus)** = pracownik realizuje sekwencję służb, gdzie każda służba to udział w pewnym TrainRun (może być z różnych obiegów taboru!)
- Jeden maszynista w ciągu dnia może prowadzić 2-3 różne pociągi z różnych obiegów taboru
- Jeden obieg taboru może być obsługiwany przez 3 różne załogi w ciągu doby (rano/popoł./noc)

**Dlaczego placeholderzy 3D a nie abstrakt:** gameplay z widocznymi postaciami w zajezdni jest immersyjny (Cities Skylines, Transport Fever) i stanowi naturalny sink for M-Models. Abstrakt ikon byłby "mikro-jobbing" bez klimatu. Kapsułki z kolorem per rola = zero lead-time'u od modelarzy, a wygląd "jest lepszy niż nic" do czasu swap'u.

---

## 2. Rozstrzygnięte decyzje

| ID | Pytanie | Decyzja |
|---|---|---|
| **D1** | Zakres ról w EA | **10 ról:** Maszynista, Konduktor, Mechanik, Sprzątacz, Pracownik myjni, Biurowy, R&D, Kasjer, **Dyspozytor** (zarządza personel→pociąg), **Dyżurny ruchu** (zarządza ruch pojazdów w zajezdni). Każda ma własny gameplay hook w innym systemie (M5/M7/M6/M9/M9b). |
| **D2** | Skill model | **1-5★**, wpływa na: czas wykonania zadania (`baseTime / (0.5 + skill/5)`), szansa błędu, szansa na przychylenie się do propozycji podwyżki, pensja oczekiwana. Wzór startowy, balans M6.5. |
| **D3** | Morale + Fatigue | **Dwie niezależne skale 0-100.** Morale = satysfakcja długoterminowa (pensja vs rynek, ciągłość pensji, przepracowanie). Fatigue = zmęczenie krótkoterminowe (rośnie na zmianie, regen w dni wolne). |
| **D4** | Zmiany bazowe | **3 zmiany (Morning 6-14, Afternoon 14-22, Night 22-6)** + cykl dni pracujących/wolnych (domyślnie 5+2, konfigurowalne per pracownik). |
| **D5** | Obiegi pracownicze | **Osobny byt `CrewCirculation`**, analog `Circulation` z M5 ale dla pracownika. Sekwencja `CrewDuty` per dzień (służby + przerwy + powroty służbowe). Jeden maszynista może mieć turnus obejmujący TrainRun'y z różnych obiegów taboru. |
| **D6** | Indywidualne harmonogramy | **Warstwa nad zmianami bazowymi.** `EmployeeSchedule` per pracownik zawiera overrides: urlopy, L4, szkolenia, indywidualne rotacje zmian, dni wolne poza cyklem bazowym. Edytor kalendarza w UI. |
| **D7** | Rekrutacja | **Market of candidates** — rotacja co 7 dni gry. Każdy kandydat: imię, rola, skill, oczekiwane wynagrodzenie, hireBonus (opcjonalny). Dodatkowo **Job Posting** (premium, płatne) → zwiększa pulę konkretnej roli na 14 dni. |
| **D8** | Cykl 3D w depocie | **TAK** — pracownicy fizycznie pojawiają się w zajezdni, chodzą po `PathGraph`, zmierzają do pomieszczeń zgodnie z rolą (`RoomDetectionSystem`). Gdy brak pomieszczenia → `RemoteWork` (-10% wydajność, -5 morale/dzień). |
| **D9** | Placeholder 3D | **Kapsułki** (Unity CapsuleMesh ~1.8m) + materiał `URP/Unlit` (przez MaterialFactory) + kolor per rola + `TextMesh` floating label z imieniem. Animacja chodzenia: vertical bob + rotacja wg tangent polyline, bez skeletal anim. Podmiana na real modele w M-Models. |
| **D10** | Pensje | **Miesięczne** (co 30 dni gry, pierwszego dnia miesiąca). `EconomyManager.AddCost(0, amount, "Personnel", employeeName)`. Brak pieniędzy → opóźnienie wypłaty → morale -20 → po 3 opóźnieniach odejście dobrowolne. |
| **D11** | Szkolenia | **POST-EA (M8.5).** W M8 brak — tylko zatrudnianie z rynku z gotowym skillem, awans przez doświadczenie także post-EA. Stub w data model żeby nie blokować save/load migration. |
| **D12** | Związki + strajki | **POST-1.0** (decyzja 2026-04-21). Morale wchodzi w EA jako skala 0-100 wpływająca na wydajność/rotację (§5.1), ale **brak triggera strajkowego** — gracz nie dostaje eventów "strajk generalny 72h". Powód: scope EA + system negocjacji wymagałby 3-5 dodatkowych sesji za mały payoff. W M8: morale monitoring + odejścia dobrowolne przy morale<20% (1-3%/dzień). Strajki + negocjacje → po 1.0 (M15+). |
| **D13** | Wiek, emerytury, choroby | **TAK, lekko.** Pracownicy 25-65 lat. >60 lat: 5%/dzień szansa na emeryturę. Krótkie choroby: 0.5%/dzień L4 1-3 dni (pensja pełna). Długie (>7 dni) post-EA. |
| **D14** | Asmdef | **Nowy `RailwayManager.Personnel.asmdef`** z ref do Core, Depot, Fleet, Timetable, SharedUI. |
| **D15** | Save/Load | **Integracja w M13 Save/Load** (osobny milestone, decyzja 2026-04-21). M8 konczy sie na M8-15 jako kod działający bez persistentcji. Personnel dostarczy metody Serialize/Deserialize per module do M13 registry gdy M13 zaczyna. POCO (<see cref="Employee"/>, <see cref="CrewCirculation"/>, <see cref="EmployeeSchedule"/>, <see cref="EmployeeCandidate"/>, static dispatcher/traffic/workshop/clerk assignments) są juz `[Serializable]` gotowe do JSON export. |
| **D16** | Konduktor — wymóg | **Wymagany gdy którykolwiek z warunków: >3 wagony pasażerskie, LUB >1 EMU, LUB >1 DMU.** Trzy typy liczone **osobno** (D31). Single EMU/DMU lub lok+≤3 wagony = konduktor opcjonalny (gracz decyduje, brak = +15% fare evasion penalty). Settings firmowy override post-EA. |
| **D17** | Seed błędów maszynisty | **Deterministyczny:** `hash(trainRunId + dateIso + employeeId)`. MP-safe, reprodukowalny do debug. |
| **D18** | R&D Lab — equipment | **Analogicznie do warsztatów M7** — detekcja przez equipment w RoomType.Lab. Poziomy Lab_Basic / Lab_Advanced (odblokowują różne ścieżki badań). Prefaby placeholder: "Stanowisko R&D", "Biblioteka techniczna", "Tablica projektowa". |
| **D19** | Kasjer — visualizacja | **Tylko w popupie stacji/przystanku** (sekcja "Obsługa: Jan K., Kasjer 3★"). Brak flagi na StationMarker, brak kapsułki poza depot. Agent widzialny = jest wymieniony w popupie. |
| **D20** | Turnusy multi-day | **TAK, 2 warianty:** (a) zamknięcie 12h (przejścia między pociągami, powrót do depotu do północy); (b) multi-day z **noclegami w hotelach** (płatne, 80/150/250 zł/noc wg tier). Max 3 dni w EA, więcej post-EA. Multi-day musi kończyć się w home depot. |
| **D21** | L4 replacement | **Notification + 3 opcje:** [Auto-dyspozytor], [Przypisz ręcznie...] (modal z filtrem), [Zignoruj = pociąg cancel]. Auto-timeout 1h game time → jeśli dyspozytor dostępny = auto-assign, inaczej = pociąg cancel. Brak dyspozytora w firmie = player musi reagować manualnie. |
| **D22** | Pensje — forma | **Umownie netto w EA.** Brutto/ZUS symulacja post-EA (M15+ Polish finansowy). |
| **D23** | Staże / praktyki | **Brak w EA.** Post-EA (M8.5) jako junior 0★ wymagający mentora. |
| **D24** | Edytor harmonogramu | **Widok miesiąca + nav prev/next + date picker.** Rok scrollable post-EA. |
| **D25** | Imersja wizualna | **Tylko Depot 3D.** Na mapie 2D (OSM): informacja o załodze pokazywana wyłącznie w popupach pociągu/stacji, brak visualnych agentów na mapie. Kasjer identycznie — popup stacji. |
| **D26** | Performance | **Brak hard limit pracowników.** Priorytet na optymalizację: (a) instant-resolve wszystkich walk-tasków gdy scena Depot inactive; (b) object pooling capsules; (c) LOD distance-based (daleko = label-only sprite); (d) spatial hashing dla pathfinding; (e) update rate: idle=1Hz, active=10Hz. Target: 500+ pracowników bez spadku FPS. Monitoring M12a. |
| **D27** | Dyspozytor — rola | **9. rola (Dispatcher).** Capacity `50 + 5×(skill-1)` pracowników per dyspozytor (1★=50, 3★=60, 5★=70). Obsługuje auto-assign maszynistów do TrainRun ad-hoc, L4 replacement, alerty o rozjeżdżających się turnusach. Over capacity = delays (auto-akcje dochodzą z opóźnieniem 2-6h); over 150% = random missed assignments. Ręczny override zawsze dostępny. Brak dyspozytora w firmie = wszystko manualne (lean play). Pomieszczenie: `RoomType.DispatchCenter` (nowy) lub Office z equipment "Stanowisko dyspozytorskie". |
| **D28** | Dyżurny ruchu — rola | **10. rola (TrafficController).** Zarządza ruchem pojazdów **w zajezdni** (M9b DepotMovementSimulator integration): priorytetyzuje manewry (warsztat overdue > myjnia planowa > wyjazd rozkładowy > parking), wybiera tory wyjściowe, koordynuje kolejność wyjazdów rano. Capacity: `10 + 5×(skill-1)` równoczesnych manewrów (1★=10, 5★=30). Over capacity = manewry kolejkują się, opóźnienia wyjazdów. Brak dyżurnego w firmie = M9b DepotMovementSim działa default FCFS z efektywnością 70% (random opóźnienia + brak priority). Pomieszczenie: `RoomType.TrafficControl` (nastawnia, nowy) + equipment "Pulpit nastawczy" + "Monitor CCTV". Bez nastawni: RemoteWork -15% capacity. |
| **D29** | Hotel tier — wpływ | **Poziom hotelu wpływa na morale + regen fatigue + performance następnej służby.** Basic (80 zł/noc): morale -1/noc, fatigue regen -20%, chance błędu maszynisty ×1.1. Standard (150 zł): baseline (neutralne). Premium (250 zł): morale +2/noc, fatigue regen +20%, chance błędu ×0.9. Trade-off gameplay: oszczędność vs performance. |
| **D30** | Auto-assign dyspozytora — preferencje | **Weighted score** `0.4×proximity + 0.3×skillMatch + 0.3×restedness`. Proximity = inverse dystans geograficzny dyspozytor↔stacja zastępstwa. SkillMatch = pracownik skill ≥ wymagany. Restedness = 1 - (fatigue/100). Wagi configurable w tab "Dyspozytura" → Settings (advanced, domyślne OK). Skill dyspozytora modyfikuje jakość decyzji: 1★ ignoruje restedness, 5★ uwzględnia wszystkie. |
| **D31** | Konduktor — liczenie składów | **Wagon jako wagon, EMU jako EMU, DMU jako DMU — osobno.** Trzy warunki OR: `passengerCars > 3` OR `emuCount > 1` OR `dmuCount > 1`. Mieszane składy (lok+wagony+EMU na końcu) liczą tylko pasażerskie wagony (EMU w takim składzie jest nietypowe, rzadki edge case). |
| **D32** | Dyżurny — multi-depot | **1 controller per depot w EA.** Post-EA (multi-depot expansion M2.6) — model sektorowy (N controllerów per duża zajezdnia >30 torów). |
| **D33** | DepotMovementSim integracja | **Minimal interface** `IDepotTaskPriorityProvider { int ComputePriority(DepotMoveTask); bool CanAdmitNewTask(); }`. `DepotMovementSimulator` pobiera provider z `TrafficControlService.GetProviderForDepot(depotId)`. `null` = default FCFS + random delay (backward-compat). Minimalna inwazja w M9b. |
| **D34** | Priorytety nastawni — konfiguracja | **Defaulty w `PersonnelBalanceConstants` + UI sliders (collapsed "Zaawansowane")** w tab "Nastawnia". 95% graczy użyje defaultów. Persistent w save. Tooltipy pokazują default value. |
| **D35** | Dyspozytor + dyżurny ruchu — 24/7 | **Oba wymagają całodobowej obsady.** Zajezdnia 24/7 = minimum 3 dyspozytorów + 3 dyżurnych ruchu (3 zmiany × 1 osoba każdej roli). Default shift nowo zatrudnionego: Morning. Luki w obsadzie: dyspozytor nieobsadzony → auto-akcje niedostępne w tych godzinach (manual only); dyżurny nieobsadzony → `DepotMovementSimulator` na default FCFS. Alert w TopBar: "Nastawnia nieobsadzona 22:00-6:00 — dyżurny wymagany". |
| **D36** | Hotel — regionalne ceny | **NIE w EA (jednostajne `HotelCostBasic/Standard/Premium`).** Post-EA (M12d Polish): regionalne multipliers (Warszawa/Kraków +50%, regionalne -20%, mniejsze miejscowości -30%). Dane z `Places` (AdminBoundary population tier). |

---

## 3. Role — 8 typów

### 3.1 Maszynista (Driver)

**Funkcja:** prowadzi pociąg — wymagany dla każdego TrainRun.

**Integracja:**
- M5 Obiegi: pole `assignedDriverIds` per dzień w `CrewCirculation` lub bezpośrednio w `Circulation` (preferowane przez obieg pracowniczy)
- M9 Ruch: `TrainRunSimulator` sprawdza czy pociąg ma maszynistę na starcie — brak = nie startuje
- Kategoria IRJ wymaga minimum skill:
  - Osobowy (`RO`, `RE`), Ekspres (`EI`, `EC`), Towarowy (`TL`) → 1★ min
  - Międzynarodowy, Pospieszny z prędkością >160 km/h → 3★ min
  - Pociągi specjalne / historyczne → 4★ min

**Fatigue rate:** 1.2%/h na zmianie (jeden z wyższych)
**Pensja bazowa:** 4500 zł/mies (1★), do 8000 zł/mies (5★)
**Skill check:** każdy błąd maszynisty → chance (1 - skill/5) × 0.02 /kurs → opóźnienie +3-10 min / brak zatrzymania na przystanku / lekkie przebicie semafora (reputacja -5)

### 3.2 Konduktor (Conductor)

**Funkcja:** kontrola biletów, obsługa pasażerów w pociągu.

**Integracja:**
- M6 Ekonomia: brak wymaganego konduktora na składzie osobowym → `fareEvasionRate += 0.15` (pasażerowie na gapę) → revenue penalty
- M8 Comfort: obecność konduktora = pełny komfort. Brak → -10%.
- Ekspresy i międzynarodowe = wymagany konduktor główny + opcjonalnie stewardzi (post-1.0)

**Wymagany (D16/D31)** gdy którykolwiek warunek:
- `passengerCars > 3` (więcej niż 3 wagony pasażerskie w składzie), LUB
- `emuCount > 1` (więcej niż 1 EMU/EZT), LUB
- `dmuCount > 1` (więcej niż 1 DMU/SZT)

**Opcjonalny** gdy single EMU/DMU lub lok+≤3 wagony (gracz decyduje, brak = +15% fare evasion).

**Nie wymagany dla:**
- Towarowych (`TL`, `TM`)
- Służbowych (`SL`, deadheady)

**Fatigue rate:** 0.9%/h
**Pensja bazowa:** 3200 zł/mies (1★), 5500 zł/mies (5★)

### 3.3 Mechanik (Mechanic)

**Funkcja:** naprawy i przeglądy w warsztatach zajezdni (integracja z M7).

**Integracja M7:**
- `WorkshopManager.AssignMechanic(mechanicId, slotId)` — slot warsztatu może mieć 1-2 mechaników
- Czas naprawy: `base / (0.5 + avgMechanicSkill / 5)` — wzór D2
- Szansa self-repair w trasie (`BreakdownService`): `SelfRepairBaseChance × (0.7 + mechanicAvailableSkill/10)`
  - Potrzebna logika: "który mechanik z depotu najbliższego awarii jest dostępny"
  - Skill 1★ obniża szansę z 50% → 42%, 5★ podnosi do 60%
- Wymagany **minimum 1 mechanik** per poziom warsztatu żeby slot był aktywny
- Bez mechanika → slot "Workshop idle", napraw trzeba zrobić przez ZNTK

**Fatigue rate:** 0.8%/h (stacjonarny, mniej zmęczenia niż prowadzenie pociągu)
**Pensja bazowa:** 3800 zł/mies (1★), 6500 zł/mies (5★)

### 3.4 Sprzątacz (Cleaner)

**Funkcja:** czyszczenie wnętrza pojazdu po kursie (podnosi komfort następnych pasażerów).

**Integracja:**
- Pojazd po kursie osobowym dostaje `interiorCondition -= 0.3%` (dodatek do naturalnej degradacji z M7, plus brud)
- Sprzątacz w depocie może przywrócić `interiorCleanliness` (nowe pole na `FleetVehicleData`, 0-100%) do 100%
- Cleanliness < 50% → comfort -5%, reputation -1/kurs
- Auto-assignment: gdy pojazd parkuje w depocie + czysty mechanik dostępny + wolny slot czyszczenia → automatyczny task (konfigurowalne via toggle)

**Nie wymaga dedykowanego pomieszczenia** — sprząta bezpośrednio przy torze parkingowym (pozycja przy `DepotTrackType.Parking`).

**Fatigue rate:** 1.0%/h
**Pensja bazowa:** 2400 zł/mies (1★), 3600 zł/mies (5★)

### 3.5 Pracownik myjni (WashBayWorker)

**Funkcja:** obsługa myjni zewnętrznej pojazdu.

**Integracja:**
- Wymaga pomieszczenia `RoomType.WashBay` (nowy typ) + equipment "Myjnia przejazdowa" (torowe)
- Pojazd przejeżdża przez myjnię → `exteriorCondition` bonus, reputation +1 /kurs (świeże pojazdy lepiej wyglądają w PR)
- Brak myjni w depocie = można wysłać do zewnętrznego (analog ZNTK, post-1.0)

**Fatigue rate:** 0.8%/h
**Pensja bazowa:** 2200 zł/mies (1★), 3300 zł/mies (5★)

### 3.6 Biurowy (OfficeClerk)

**Funkcja:** administracja firmy — obniża fixed costs, odblokowuje akcje managerskie.

**Integracja:**
- Wymaga `RoomType.Office` (już istnieje w `RoomDetectionSystem`)
- Każdy biurowy obsługuje "slot biurowy" — wpływa na:
  - Koszty administracyjne: `-5% per biurowy × avgSkill/5` (max -30%)
  - Szybkość rekrutacji: -1 dzień refresh cyklu /biurowy (min 3 dni)
  - Unlock akcji: "Negocjuj dotację" (potrzeba 2+ biurowych 3★+, M6 Ekonomia hook)
- Bez biurowego: wszystko działa ale drożej i wolniej

**Fatigue rate:** 0.5%/h (najniższy, siedząca praca)
**Pensja bazowa:** 3000 zł/mies (1★), 5200 zł/mies (5★)

### 3.7 R&D (Researcher)

**Funkcja:** badania i rozwój — odblokowuje upgrade'y i modernizacje.

**Integracja:**
- Wymaga `RoomType.Lab` (nowy typ, oznaczenie przez equipment "Stanowisko R&D")
- Slot badań: jedna aktywna ścieżka R&D naraz
- Ścieżki (post-M12d, w M8 tylko framework + 2 przykłady):
  - **"Lepsze przeglądy"** — -10% czasu napraw przez własny warsztat (30 dni, 3 badaczy 2★+)
  - **"Optymalizacja trakcji"** — -5% zużycia energii per km (60 dni, 2 badaczy 4★+)
- Efekt: globalny modifier zapisany w `ResearchUnlocks` (osobny moduł, M8 dodaje szkielet)

**Fatigue rate:** 0.4%/h
**Pensja bazowa:** 5000 zł/mies (1★), 9000 zł/mies (5★) — najdrożsi po maszynistach

### 3.8 Kasjer (TicketClerk)

**Funkcja:** sprzedaż biletów na stacji — poprawia revenue na stacjach z kasami.

**Integracja:**
- Kasjer nie jest przypisany do depotu — przypisany do **stacji** (pierwszy system `StationAssignment` w kodzie, M8 wprowadza)
- Stacja z obsadzoną kasą: revenue +8% z tej stacji (pasażerowie chętniej kupują z kasy niż automatu)
- Stacja bez kasjera: tylko automaty, revenue bazowy
- Wymaga `RoomType.TicketOffice` (nowy enum) na stacji (abstrakt dla M8 — po prostu flaga `hasTicketOffice` na `StationMarker`, M12c da tej fladze realny 3D)

**Fatigue rate:** 0.8%/h
**Pensja bazowa:** 2600 zł/mies (1★), 4200 zł/mies (5★)

### 3.9 Dyspozytor (Dispatcher)

**Funkcja:** zarządza przypisaniami personel ↔ pociąg. "HR + operacje planowania" — automatyzuje to, co bez niego gracz musi klikać ręcznie.

**Integracja:**
- **Auto-assign ad-hoc** — gdy TrainRun startuje bez załogi z turnusu (np. kurs rozkładowy nie obsadzony, tabor zapasowy): dyspozytor dobiera wolnego maszynistę o kompatybilnym skill'u
- **L4 replacement** — event `OnCrewVacancyDetected` → dyspozytor proponuje zastępstwo (skill-match, availability, minimum travel time)
- **Monitoring turnusów** — alerty: "Turnus M-KR-01 nie zacznie się o 6:00 bo przypisany maszynista Jan na L4 i nie ma zastępcy"
- **Priorytetyzacja** — gdy brakuje maszynistów i trzeba wybrać: ekspresy > pospieszne > osobowe > towarowe

**Capacity** (sekcja D27): `50 + 5×(skill-1)` pracowników per dyspozytor:
- 1★: 50 pracowników
- 3★: 60
- 5★: 70

**Stany przeciążenia:**
- Under capacity (headcount ≤ cap × N): pełna automatyzacja, instant actions
- Over capacity (cap × N < headcount ≤ cap × N × 1.5): auto-akcje dochodzą z opóźnieniem 2-6h game time
- Critical over (>150%): random 20% akcji missed — pociągi bez załogi cancelled

**Brak dyspozytora w firmie:** wszystko manualne — gracz widzi listę TrainRun'ów bez załogi, musi klikać zastępstwa. Możliwe ale męczące przy >20 pociągach dziennie.

**Pomieszczenie:** `RoomType.DispatchCenter` (nowy) lub `RoomType.Office` z equipment "Stanowisko dyspozytorskie". Bez pomieszczenia: RemoteWork.

**Fatigue rate:** 0.6%/h (biurowa ale intensywna, rosnąca przy over-capacity)
**Pensja bazowa:** 3500 zł/mies (1★), 6000 zł/mies (5★)

### 3.10 Dyżurny ruchu (TrafficController)

**Funkcja:** zarządza ruchem pojazdów **fizycznie w zajezdni** (nastawnia / pulpit dyspozytorski). Decyduje: który pojazd jedzie na warsztat pierwszy, kiedy wjeżdża na myjnię, w jakiej kolejności rano wyjeżdżają składy z parkingów. "Wieża kontroli ruchu zajezdni".

**Integracja z M9b DepotMovementSimulator:**
- Obecny `DepotMovementSimulator` działa FCFS (kto pierwszy Enqueue, ten pierwszy). Dyżurny ruchu wprowadza **priority-aware scheduling**:
  - `WorkshopOverdue` (pojazd czekający na przegląd z wygasłym terminem) → priority 100
  - `ScheduledDeparture` (wyjazd rozkładowy w <30min) → priority 80
  - `WashBayPlanned` → priority 60
  - `ParkingReshuffle` (zwolnienie miejsca) → priority 40
- Dyżurny dobiera tory wyjściowe (ExitTrack multi-slot) tak by nie tworzyć deadlocków
- Bufor manewrów: gdy `ActiveTasks.Count` osiąga capacity → kolejka `PendingTasks` (opóźnienia)

**Integracja z M7:**
- Pojazd oznaczony do przeglądu → dyżurny ruchu koordynuje trasę: parking → workshop slot → (po naprawie) → parking
- Bez dyżurnego: M7 "request inspection" = pojazd czeka niekontrolowanie, inne zadania ją wyprzedzają

**Capacity** (sekcja D28): `10 + 5×(skill-1)` równoczesnych manewrów:
- 1★: 10 aktywnych tasków naraz
- 3★: 20
- 5★: 30

**Stany przeciążenia:**
- Over capacity: manewry kolejkują się w `PendingTasks`, Delay kumulowany
- Over 150%: random 25% tasków dostaje +50% czas wykonania (błędne trasy, cofanie)

**Brak dyżurnego w firmie:** M9b DepotMovementSim działa default FCFS z efektywnością 70%:
- Random opóźnienia ±20% na każdym manewrze
- Brak priority: warsztat overdue może czekać za parkowaniem
- Czasem suboptymalna trasa (dłuższa o 15-30%)

**Multi-depot (post-M2.6):** jeden dyżurny per sektor zajezdni. Duża zajezdnia (30+ torów) może wymagać 2-3 dyżurnych.

**Pomieszczenie:** `RoomType.TrafficControl` (nastawnia, nowy enum) + equipment: "Pulpit nastawczy", "Monitor CCTV", "Tablica świetlna torów". Bez nastawni: RemoteWork -15% capacity (dyżurny pracuje z biura, gorzej widzi sytuację).

**Fatigue rate:** 0.7%/h
**Pensja bazowa:** 3200 zł/mies (1★), 5500 zł/mies (5★)

---

## 4. Skills 1-5★

### 4.1 Wpływ na performance

**Wzór uniwersalny (czas wykonania / szansa sukcesu):**
```
effectiveFactor = 1 / (0.5 + skill/5)
// 1★ → 1.66, 2★ → 1.43, 3★ → 1.25, 4★ → 1.11, 5★ → 1.00
// Czas: baseTime × effectiveFactor
// Błąd: baseErrorRate × effectiveFactor
```

**Wzór pensji oczekiwanej:**
```
salaryMultiplier = 0.7 + 0.15 × skill
// 1★ → 0.85, 2★ → 1.00, 3★ → 1.15, 4★ → 1.30, 5★ → 1.45
// Pensja = baseSalary[role] × salaryMultiplier
```

### 4.2 Dystrybucja w rynku kandydatów

Generator kandydatów rozkład:
- 1★ — 35% (dużo juniorów)
- 2★ — 30%
- 3★ — 20%
- 4★ — 12%
- 5★ — 3% (rzadkość, często ex-konkurencja)

### 4.3 Awans przez doświadczenie (post-EA, M8.5)

Post-EA: pracownik zdobywa XP za pracę bez błędów, po progu może awansować (z kosztem szkolenia / bez).

---

## 5. Morale + Fatigue + stan zdrowia

### 5.1 Morale (0-100)

**Wartość początkowa:** 70 przy zatrudnieniu.

**Wpływy per tick (dzień):**

| Czynnik | Wpływ |
|---|---|
| Pensja wyższa od rynkowej o >10% | +1 /dzień |
| Pensja niższa od rynkowej o >10% | -2 /dzień |
| Wypłata opóźniona | -20 (jednorazowo per miesiąc) |
| Przepracowanie (fatigue >80 przez 2+ dni) | -3 /dzień |
| Nadgodziny (zmiana nocna 2+ razy tydzień) | -1 /dzień |
| Awans skill (szkolenie) | +15 (jednorazowo) |
| Premia jednorazowa | +0.5 /1000zł bonusu (max +20 per bonus) |
| Kolega zwolniony (nie emeryt) | -2 /dzień × 3 dni |
| Strajk sukces | +25 (jednorazowo) |
| Strajk porażka | -15 (jednorazowo) |

**Konsekwencje poziomu:**
- **>80:** "Entuzjasta" — +5% wydajność, -50% chance błędu
- **60-80:** baseline
- **40-60:** "Niezadowolony" — -5% wydajność, brak efektów
- **20-40:** "Zagrożony odejściem" — 1%/dzień szansa na wypowiedzenie, +20% chance błędu
- **<20:** "Krytyczny" — 3%/dzień wypowiedzenie, triger strajku zespołowego

### 5.2 Fatigue (0-100)

**Wartość początkowa:** 0 po każdym dniu wolnym.

**Przyrost na zmianie** per rola (patrz sekcja 3).
**Regen:**
- Dzień wolny: -80 (praktycznie pełen reset)
- Urlop: -100
- Zmiana pracownicza zakończona: -20 (nocleg)

**Konsekwencje:**
- 0-50: baseline
- 50-80: -10% wydajność
- 80-100: -25% wydajność, 2× chance błędu
- 100: force dzień wolny (pracownik Unavailable przez 24h, morale -5)

### 5.3 Stan zdrowia

**Stany:**
- `Healthy` (default)
- `Sick` (L4, 1-3 dni, pensja pełna)
- `LongSick` (post-EA, 7-14 dni, pensja 60%)
- `Retired` (terminal, pracownik removed)

**Przejścia:**
- Healthy → Sick: 0.5%/dzień (podwaja się gdy fatigue >80)
- Sick → Healthy: po expiry days
- Healthy → Retired: 5%/dzień gdy wiek >60, 20%/dzień gdy >65 (max age 70)

---

## 6. Zmiany bazowe + indywidualne harmonogramy

### 6.1 Zmiany bazowe (Shift Templates)

**3 typy:**
- **Morning** — 6:00 - 14:00 (8h pracy)
- **Afternoon** — 14:00 - 22:00
- **Night** — 22:00 - 6:00 (następnego dnia)

**Cykl dni pracujących/wolnych:**
- Default: **5+2** (klasyczne 5 dni pracy, 2 wolne)
- Alternatywy: 4+2, 6+2, 7+7 (tygodniowy rytm — nocna zmiana dla maszynistów obiegów dalekobieżnych)
- Konfigurowalne per pracownik przy zatrudnieniu, edytowalne później

### 6.2 Indywidualny harmonogram (`EmployeeSchedule`)

Warstwa **override'ów** nad zmianami bazowymi per pracownik.

```csharp
[Serializable]
public class EmployeeSchedule
{
    public int employeeId;
    public ShiftType defaultShift;         // Morning/Afternoon/Night
    public WorkCyclePattern cycle;         // 5+2, 4+2, 6+2, Custom
    public int cycleStartDayOffset;        // od którego dnia cyklu zaczął
    public List<ScheduleOverride> overrides;  // urlopy, L4, szkolenia, zmiana zmiany
}

[Serializable]
public class ScheduleOverride
{
    public string dateIsoStart;
    public string dateIsoEnd;
    public ScheduleOverrideType type;  // Vacation, SickLeave, Training, ShiftSwap, ExtraDutyDay, FreeDay
    public ShiftType? replacementShift; // dla ShiftSwap
    public string notes;
}
```

**UI Edytor:** kalendarz miesięczny per pracownik, drag-drop wydarzeń. Konflikty auto-wykryte (np. nakładające się urlopy).

**Walidacja obiegów pracowniczych:** podczas przypisania do `CrewDuty` (patrz §7) system sprawdza czy pracownik nie jest już zajęty / na urlopie / na L4 w tym dniu.

---

## 7. Obiegi pracownicze (CrewCirculation)

**KLUCZOWA SEKCJA.** Osobny byt od obiegów taboru (M5).

### 7.1 Dlaczego osobne?

W realnej kolei **turnus maszynisty ≠ obieg składu**:
- Maszynista Jan w ciągu jednego dnia: prowadzi EIC "Tatry" Kraków→Warszawa (obieg taboru A) → 2h przerwy → prowadzi IC "Mazury" Warszawa→Olsztyn (obieg taboru B) → nocleg w Olsztynie → wraca rano służbowo przez KM
- Jeden skład EIC "Tatry" w ciągu doby obsługiwany przez 3 różnych maszynistów (krakowski rano, warszawski popołudnie, kolejny wieczorem)

W grze:
- `Circulation` (M5) = sekwencja TrainRunów per pojazd/dzień
- `CrewCirculation` (M8) = sekwencja służb per pracownik/dzień — służby odnoszą się do konkretnych `TrainRun` (które mogą pochodzić z różnych obiegów taboru)

### 7.2 Model danych

```csharp
[Serializable]
public class CrewCirculation
{
    public int crewCirculationId;
    public string name;                    // "Turnus M-KR-01"
    public EmployeeRole role;              // Driver / Conductor (inne role nie mają turnusów)
    public int? assignedEmployeeId;        // null = niepowiązany szkielet
    public DayMask calendarDays;           // Pn-Nd toggle
    public List<string> specificDates;     // override dat (analog Circulation)
    public List<CrewDuty> duties;          // sekwencja służb
    public CirculationStatus status;       // Draft/Active/Archived
    public string notes;
}

[Serializable]
public class CrewDuty
{
    public CrewDutyKind kind;              // Service / Break / Deadhead / Handover
    public string startTimeIso;            // HH:MM:SS (w ramach doby; rollover przez północ)
    public string endTimeIso;
    public string startStationName;
    public string endStationName;
    public int? referencedTrainRunId;      // dla Service/Deadhead — który TrainRun
    public int? referencedCirculationId;   // z którego obiegu taboru (info)
    public int dutyIndex;
}

public enum CrewDutyKind
{
    Service,      // właściwe prowadzenie pociągu (TrainRun z obiegu taboru)
    Break,        // przerwa na stacji (czeka w kantynie/peronie)
    Deadhead,     // powrót służbowy (nie prowadzi, tylko pasażerem)
    Handover      // przekazanie zmiany innemu maszyniście (1-5 min)
}
```

### 7.3 Validator obiegu pracowniczego

Analogiczny do `CirculationValidator` z M5, ale dla turnusów:

1. **Spójność stacji:** koniec służby N = początek służby N+1 (albo między nimi musi być `Deadhead` spinający)
2. **Spójność czasowa:** `duty[N].endTimeIso < duty[N+1].startTimeIso`, plus minimum 10 min buforu na `Handover`
3. **Maksymalny czas pracy:** sumarycznie `Service + Deadhead` ≤ 12h / doba (regulamin maszynisty, post-EA configurable)
4. **Przerwa minimalna:** po 4h ciągłej pracy wymagana `Break` ≥ 30 min (inaczej warning)
5. **Skill check:** `Service` na TrainRun kategorii `EI/EC` wymaga maszynisty ≥3★
6. **Availability:** `assignedEmployeeId` nie może mieć `ScheduleOverride` typu Vacation/SickLeave/Training w `specificDates` lub aktywnych `calendarDays`
7. **Uniqueness:** jeden maszynista = jeden aktywny turnus per dzień (zmiany są w `EmployeeSchedule`)

### 7.4 Auto-generator turnusów

Analog `CirculationAutoGenerator` z M5 dla obiegów taboru, ale dla turnusów pracowniczych.

**Algorytm:**
1. Zbierz wszystkie aktywne `TrainRun` per dzień (z `Circulation.Active`)
2. Dla każdego TrainRun → wymaga 1 driver + warunkowo konduktor (§3.2)
3. Rozdziel TrainRun'y na "bloki dzienne" per geografia (cluster stacji — np. pomorskie, mazowieckie)
4. Dla każdego bloku → chain building (jak w M5): pierwsza służba → wyszukaj kolejny TrainRun z kompatybilną lokalizacją startu + min gap → dodaj do turnusu → powtarzaj aż do limitu 12h lub brak chainowania
5. Jeśli koniec dnia daleko od bazy domowej → `Deadhead` powrotny (jeśli istnieje TrainRun w tym kierunku) lub wygeneruj placeholder "Powrót służbowy" (wymaga dedykowanej kategorii `SL` w M4)
6. Preview modal przed commitem

### 7.5 UI

**Nowy tab w panelu Depot: "Turnusy":**
- Master-detail (analog M5 CirculationListUI)
- Lewa kolumna: lista turnusów (Active/Draft/Archived, filtr per rola)
- Prawa kolumna: pula TrainRun'ów nie-obsadzonych (drag source)
- Drag&drop TrainRun → turnus → utworzenie `CrewDuty.Service`
- Między służbami auto-wstawiane `Break` (edytowalne)
- Klik "Wygeneruj powrót" → modal kategorii `SL` lub `Deadhead` (jeśli istnieje relevant TrainRun)
- Przycisk "Przypisz pracownika" → dropdown wolnych maszynistów (filtr skill ≥ max kategorii w turnusie, availability w `calendarDays`)

### 7.6 Integracja z obiegami taboru

- Obieg taboru `Active` → TrainRun'y wygenerowane → dostępne jako źródło dla turnusów
- Turnus pracowniczy `Active` + przypisany employeeId → `CrewAssignmentService.GetDriverForTrainRun(trainRunId, date)` zwraca pracownika
- M9 `TrainRunSimulator.Spawn`: sprawdza `CrewAssignmentService` → brak maszynisty = pociąg nie startuje (opóźnienie kumuluje jak M9a decyzja)
- Feature flag `RequireCrewForCirculation` (globalny): OFF domyślnie (M8 dev), ON po playtest sanity

---

## 8. Rynek kandydatów + rekrutacja

### 8.1 CandidateMarket

Analog `FleetMarketRefreshService` z M3/M7.

**Schema kandydata:**
```csharp
[Serializable]
public class EmployeeCandidate
{
    public int candidateId;
    public string firstName;               // seed-random PL imiona
    public string lastName;                // seed-random PL nazwiska
    public int age;                        // 25-60 (kandydaci młodsi, emeryci nie szukają pracy)
    public EmployeeRole role;
    public int skill;                      // 1-5
    public int expectedSalaryGroszy;       // = baseSalary × salaryMult + random ±10%
    public int hireBonusGroszy;            // 0 lub 5-25% rocznej pensji (~5% candidates)
    public string resumeNotes;             // fluff text ("5 lat w Przewozy Regionalne")
    public string availableUntilDateIso;   // expires po 7 dniach na market
}
```

**Refresh:**
- Co 7 dni gry (subskrybuje `GameState.OnDayEnded`)
- Usuwa expired (kandydaci sami "znajdują inną pracę")
- Dodaje 3-6 nowych per cykl (losowa dystrybucja per rola, ważona popytem playera)
- Max size: 30 kandydatów naraz
- Seed: `(gameId + cycleNumber).GetHashCode()` — deterministyczne dla save/load

**Pula:** zrównoważona na starcie — gwarantuje minimum 2 każdej z 8 ról (reszta losowa).

### 8.2 Job Posting (płatne ogłoszenia)

**Mechanizm:**
- UI "Dodaj ogłoszenie" → wybór roli + skill target (np. "Mechanik 3★+")
- Koszt: 3000 zł (base role) - 15000 zł (specjalista 5★+)
- Czas: 14 dni gry — zwiększa pulę kandydatów tej roli (3× więcej + skew w kierunku target skill)
- Max 3 aktywne ogłoszenia

**Po 14 dniach:** ogłoszenie wygasa, powrót do normalnej rotacji.

### 8.3 Proces zatrudnienia

1. UI "Rekrutacja" → lista kandydatów z filtrami (rola, skill, pensja max)
2. Klik kandydat → popup z detalami (CV + stats)
3. Przycisk "Zatrudnij" → modal:
   - Pokazuje pensję oczekiwaną, hire bonus
   - Opcja **negocjuj** (skill check biurowy): chance obniżenia -5% do -15% pensji (wymaga 1+ biurowego 3★+)
   - Potwierdzenie → `PersonnelService.Hire(candidateId, terms)`
4. Nowo zatrudniony: default schedule (5+2, shift = Morning), morale 70, fatigue 0
5. Hire bonus zapłacony natychmiast (jednorazowo)

### 8.4 Zwalnianie

- UI "Zwolnij" w detailach pracownika
- Odprawa zależna od stażu:
  - <1 mies: brak
  - 1-6 mies: 1× pensja
  - 6-24 mies: 2× pensja
  - >2 lata: 3× pensja
- Morale pozostałych pracowników tej samej roli: -2 przez 3 dni (sekcja 5.1)

---

## 9. Cykl dnia w 3D (pathfinding + placeholder visuals)

### 9.1 Pipeline

Dla każdego pracownika, który ma dziś służbę/pracę (nie urlop/L4):

1. **Spawn** o `shift.startTimeIso - 30min` → na obiekcie `DepotGateMarker` (brama wjazdowa zajezdni, nowy marker M8)
2. **Go to WorkDestination** — pathfind po `PathGraph` do `RoomDetectionSystem.GetRoomByTypeForRole(role)` (najbliższe)
3. **Work** — siedzi w pomieszczeniu przez czas zmiany (okresowe "wyjście" do pojazdu przy tasku, np. mechanik do warsztat slot'u z pojazdem)
4. **Go home** — powrót do `DepotGateMarker`
5. **Despawn** na shift.endTimeIso

**Przy pracownikach z turnusem (Driver/Conductor):**
- Spawn w zajezdni, pathfind do parkingu pojazdu
- **Embed** w pojazd gdy ten startuje (hide capsule, subscribe `TrainRunSimulator.Position`)
- Po zakończeniu służby / przerwy: widoczny na stacji (jeśli mapa 2D) — abstrakt ikoną
- Wrócenie do zajezdni (Deadhead lub rzeczywisty pasażer): unembed, visible capsule

### 9.1a Docelowy loop postaci w depot

**Status implementacji 2026-05-11:** ✅ **DONE (TD-025 full scope).** Patrz
`docs/TECH_DEBT.md` TD-025 → 🟢 Resolved. Implementacja w `Personnel/Runtime/Workflows/`
(5 klas + `IEmployeeWorkflow` orchestrator w `PersonnelDispatcher3D`). Decyzje user'a
2026-05-11: skala 200 postaci, animacja minimalna (placeholder do M-Models),
lead time 30 min stały (future morale-driven), brak chodników → straight-line
z końca grafu, idle → Social room, embed maszynisty w pociągu (hidden visual),
universal meldunek u dyspozytora dla wszystkich oprócz Cleaner/TicketClerk/Dispatcher (self).

**Implementacja niżej zachowana jako referencja design — flow execution zgadza się z kodem.**

Docelowy wspólny flow dla widocznych pracowników:

1. `EmployeeSchedule` odpowiada na pytanie: czy pracownik może dziś pracować
   (dzień roboczy, urlop, L4, szkolenie, zmiana).
2. Przypisanie roli odpowiada na pytanie: po co ma przyjść do depot:
   `CrewDuty` dla maszynisty/konduktora, slot warsztatowy dla mechanika,
   zadanie czyszczenia/myjni dla sprzątacza i pracownika myjni, stanowisko
   biurowe/R&D/dyspozytorskie/nastawnia dla ról stacjonarnych.
3. Przed rozpoczęciem zmiany albo konkretnego zadania runtime tworzy stan
   `ComingToDepot`.
4. `PersonnelDispatcher3D` spawnuje normalną postać 3D przy `DepotGateMarker`
   lub chodniku/wejściu do zajezdni. To nie jest abstrakt ikonowy: pracownik ma być
   zwykłym agentem 3D poruszającym się po depot.
5. `EmployeeWalkSimulator` prowadzi go pathfindingiem po `PathGraph` do pierwszego
   celu dnia: dyspozytor, szatnia, warsztat, biuro, lab, nastawnia, parking pojazdu
   albo inne miejsce wynikające z przypisania.
6. Po dojściu pracownik przechodzi w stan pracy, oczekiwania albo odprawy:
   `WorkingAtStation`, `WorkingAtWorkshop`, `ReportingToDispatcher`,
   `AwaitingTask`, `GoingToVehicle`, itd.
7. Zadania w trakcie dnia zmieniają destination: mechanik idzie do konkretnego
   slotu/pojazdu, sprzątacz do brudnego pojazdu, pracownik myjni do myjni,
   dyspozytor i biurowi zostają przy stanowisku, a maszynista/konduktor idą
   do pojazdu/peronu/wyjścia z depot.
8. Na koniec zmiany albo po zakończeniu ostatniego zadania postać wraca do
   `DepotGateMarker` i despawnuje się jako `GoingHome`.
9. Jeśli scena Depot jest nieaktywna, cały flow instant-resolve'uje się logicznie,
   ale zachowuje te same stany, opóźnienia i skutki gameplayowe.

Dla maszynisty i konduktora trigger jest bardziej precyzyjny niż sam `OnShift`:
zbliżające się `CrewDuty` powinno tworzyć `ComingToDepot` około
`CrewDuty.startTimeIso - CrewReportLeadMinutes`, następnie `ReportingToDispatcher`,
potem `AwaitingDeparture` albo `GoingToVehicle`, a na starcie kursu powiązanie
z obsługiwanym `TrainRun`. Grafik mówi "może pracować"; przypisanie/turnus mówi
"ma przyjść na tę konkretną pracę".

### 9.2 Pathfinding

**Reuse** `PathGraph` z Depot:
- `PathGraph.FindNearestNode(position)` → start node
- `PathGraph.FindPath(startNodeId, endNodeId)` → lista nodów z polyline
- Interpolacja: nowy `EmployeeWalkSimulator` analogiczny do `DepotMovementSimulator`
  - Prędkość: 1.4 m/s (spacer), 2.5 m/s (pośpiech gdy opóźnienie)
  - Własny clock: `GameState.DepotTimeScale` (zajezdnia max x5)
  - Instant-resolve gdy gracz nie w scenie Depot (jak M9c manewry)

**Gdy brak ścieżki:**
- Fallback: straight-line walk "przez trawę" (teleport-friendly)
- Warning log: `[EmployeeWalkSimulator] No path from X to Y, using fallback`
- Optimize post-EA

### 9.3 Destynacje per rola (mapowanie RoomType)

| Rola | Preferowany RoomType | Fallback |
|---|---|---|
| Maszynista | `RoomType.Locker` (szatnia) → idzie do pojazdu | `RemoteWork` |
| Konduktor | `RoomType.Locker` → pojazd | `RemoteWork` |
| Mechanik | `RoomType.Workshop` (M7) | `RemoteWork` |
| Sprzątacz | przy `DepotTrackType.Parking` (przy pojazdach) | `RemoteWork` |
| Pracownik myjni | `RoomType.WashBay` (nowy enum) | Disabled (nie ma myjni) |
| Biurowy | `RoomType.Office` | `RemoteWork` |
| R&D | `RoomType.Lab` (nowy enum) | Disabled (nie ma lab) |
| Kasjer | **Brak spawnu w Depot/Map** (D19/D25) — wyłącznie info w popupie stacji | N/A |
| Dyspozytor | `RoomType.DispatchCenter` (nowy enum) lub `Office` z equipment "Stanowisko dyspozytorskie" | `RemoteWork` -10% capacity |
| Dyżurny ruchu | `RoomType.TrafficControl` (nastawnia, nowy enum) + equipment "Pulpit nastawczy" + "Monitor CCTV" | `RemoteWork` -15% capacity |

**RemoteWork mode:** pracownik istnieje logicznie ale nie spawnuje się w depocie. Morale -5/dzień, performance -10%. Banner w UI: "Biurowy X pracuje zdalnie — brak biura".

### 9.4 Placeholder visuals

**`EmployeeVisual`** (MonoBehaviour):
- `CapsuleMesh` primitive (height 1.8, radius 0.3)
- Materiał `URP/Unlit` (przez MaterialFactory; perf — nie potrzebujemy lightingu na kapsułkach)
- Kolor per rola:

| Rola | Kolor | Hex |
|---|---|---|
| Maszynista | Granatowy | `#1E3A8A` |
| Konduktor | Zielony | `#15803D` |
| Mechanik | Pomarańczowy | `#EA580C` |
| Sprzątacz | Szary | `#6B7280` |
| Pracownik myjni | Jasnoniebieski | `#38BDF8` |
| Biurowy | Biały | `#F3F4F6` |
| R&D | Fioletowy | `#7C3AED` |
| Kasjer | Żółty | `#FACC15` (używany tylko w popupie stacji, nie spawnuje się w 3D) |
| Dyspozytor | Turkusowy | `#14B8A6` |
| Dyżurny ruchu | Czerwony | `#DC2626` (wysoka widoczność jak kamizelka ruchu) |

- `TextMesh` floating label (0.5m nad głową): "{firstName} {lastName[0]}.\n{role}" — np. "Jan K.\nMaszynista"
- Animacja chodzenia: vertical bob (sine wave ±5cm @ 2Hz) + rotacja capsule wg tangent polyline
- LOD: single mesh, no LOD switching (kapsułka jest już low-poly)

**Swap path w M-Models:**
- Real modele zachowują `EmployeeVisual` controller, tylko podmieniają mesh
- Animacje: walk cycle, idle, praca (mechanic ma animację z kluczem, kasjer siedzi za biurkiem)
- Wymaganie dla modelarzy: rig skeletalny (standard Humanoid Unity)

---

## 10. Szkolenia — POST-EA (M8.5)

> **Status (D11 decyzja):** W EA **brak szkoleń**. Pracownicy zatrudniani z rynku z gotowym skillem — jeśli potrzebujesz 5★ mechanika, musisz go znaleźć na rynku (Job Posting pomaga). Awans przez doświadczenie też post-EA.
>
> **Powód:** scope M8 już duży (10 ról + obiegi + 3D). Szkolenia dodają system osobny (edytor modalny + scheduler + success roll + integracja skill update). Można dodać w M8.5 / M12d bez breakage — stub w `Employee` pozostaje (pole `skillXp`, domyślnie 0).
>
> Poniższe sekcje zostawione jako **referencja do M8.5** — nie implementować w M8.

### 10.1 Model

```csharp
[Serializable]
public class Training
{
    public int trainingId;
    public int employeeId;
    public TrainingType type;              // External / Internal
    public string startDateIso;
    public string endDateIso;
    public int targetSkillLevel;
    public int costGroszy;
    public float successChance;            // External: 1.0, Internal: 0.8
}

public enum TrainingType { External, Internal }
```

### 10.2 Ścieżki

**External (szkolenie zewnętrzne):**
- 100% success
- Czas: 14 dni (dla 1→2), do 30 dni (4→5)
- Koszt:
  - 1→2: 8000 zł
  - 2→3: 15000 zł
  - 3→4: 30000 zł
  - 4→5: 60000 zł
- Pracownik Unavailable przez cały czas (pensja pełna)

**Internal (mentoring z seniorem firmy):**
- 80% success (fail → pracownik wraca bez awansu, koszt poniesiony, morale -5)
- Wymaga w firmie 1+ pracownika tej roli 2 skill wyżej niż target (mentor)
- Czas: 20 dni (1→2) - 45 dni (4→5)
- Koszt: 30% external
- Mentor zyskuje +10 morale (docenienie) i w 5% przypadków -1 skill (demotywacja?) — balance M6.5

### 10.3 UI

- W detailach pracownika: "Wyślij na szkolenie"
- Modal: wybór External/Internal, target skill, preview kosztu i czasu
- Po potwierdzeniu: `ScheduleOverride` typu `Training` wstawiony do `EmployeeSchedule`
- W dniu zakończenia: roll (External auto-success) → `Employee.skill += 1` + morale +15 + log

---

## 11. Wiek, emerytura, choroby, urlopy

### 11.1 Wiek + emerytura

- `Employee.birthDateIso` (seed gen 25-60 lat przy zatrudnieniu)
- Co 1. dzień miesiąca: check wieku
- Wiek 60-64: 5% szans na ogłoszenie emerytury (`OnRetirementAnnounced` event, 30-dniowe wypowiedzenie)
- Wiek 65+: 20%/dzień (praktycznie wymuszone)
- Wiek 70: force retire natychmiast

**Efekt emerytury:**
- 30 dni gry zapowiedzi (pracownik aktywny, ale gracz może szukać zastępcy)
- Po 30 dniach: `Employee.status = Retired`, usunięty z przypisań
- Odprawa emerytalna: 3× pensja miesięczna
- Morale innych: neutral (emerytura to nie zwolnienie, brak -2)

### 11.2 Urlopy

**Limit roczny:** 26 dni urlopu płatnego (Kodeks Pracy PL).

**Procedura:**
- Pracownik w detailach: "Urlopy pozostałe: X/26 dni"
- UI "Udziel urlopu" → kalendarz, zaznacz dni
- Automatycznie wstawia `ScheduleOverride` typu `Vacation`
- Pracownik Unavailable, pensja pełna
- Blok: nie można udzielić >7 dni ciągiem bez ostrzeżenia (gameplay-wise OK, bez hard block)
- Nieużyte dni na koniec roku: rollover do 10 dni, reszta przepada (realistyczne)

### 11.3 L4 (choroby krótkie)

- Auto-trigger: codziennie roll 0.5% × (1 + fatigueOver50 × 0.02) na `Healthy → Sick`
- Czas: random 1-3 dni
- Pensja pełna (EA), post-EA: 80% po 3. dniu (ZUS)

**Flow zastępstwa (decyzja D21):**

Gdy pracownik na L4 a ma aktywny turnus / shift dzisiaj:

1. System emit event `OnCrewVacancyDetected(employeeId, date, affectedDuties)`
2. UI notification pops (top-right, yellow urgent):
   > "Jan Kowalski (Maszynista 3★) — L4 do 2026-04-23. Turnus M-KR-01 (Kraków→Warszawa→Olsztyn) jutro bez maszynisty."
   > **Przyciski:** [Auto-dyspozytor] [Przypisz ręcznie...] [Pociąg cancel]
3. Akcja:
   - **Auto-dyspozytor** — jeśli firma ma wolnego dyspozytora z capacity → wybiera zastępstwo w tle (skill-match, availability, minimum travel time). Dyspozytor over capacity = akcja z delay 2-6h. Brak dyspozytora = przycisk disabled z tooltipem "Zatrudnij dyspozytora dla automatyzacji"
   - **Przypisz ręcznie** — modal z listą Available pracowników tej roli, filtr skill ≥ wymagany, sortowane po preferencji (blisko geograficznie, najmniej fatigue)
   - **Pociąg cancel** — TrainRun tego turnusu skasowany, reputation -10
4. **Timeout 1h game time** bez akcji:
   - Dyspozytor dostępny + capacity OK → auto-assign (loguje "Auto-assigned by dispatcher X")
   - Brak dyspozytora LUB over 150% → pociąg cancelled automatycznie + reputation -15 (gracz "zapomniał zareagować")

### 11.4 Długie choroby

Post-EA (M12d lub M15): 0.1%/dzień na `Sick → LongSick` (7-14 dni), pensja 60%, reputation firmy -5 jeśli >30 dni rocznie.

---

## 12. Związki zawodowe + strajki

### 12.1 Model

**Morale zespołowe** = średnia morale aktywnych pracowników per rola.

**Monitoring:**
- Codziennie: oblicz średnią morale globalnie i per rola
- Trigery (global):
  - Avg <30% przez 5 dni → Strajk ostrzegawczy zapowiedziany (48h okno negocjacji)
  - Avg <20% przez 7 dni → Strajk generalny zapowiedziany
  - Avg specific role <25% → Strajk tej roli (np. tylko maszyniści)

### 12.2 Strajk ostrzegawczy

- **Czas:** 24h
- **Efekt:** wszyscy pracownicy Unavailable
- **Konsekwencje:** wszystkie TrainRun'y tego dnia cancelled, reputation -15, opóźnienia + kary wg M6
- **Negocjacja (okno 48h przed strajkiem):**
  - Modal "Żądania związków": lista 3-5 randomized żądań (podwyżka 10%, bonus, skróć zmiany, więcej urlopów)
  - Akceptuj wszystkie / część / żadne
  - Akceptacja ≥50% żądań = strajk odwołany, morale +25
  - Odrzucenie = strajk idzie jak planowany
- **Mitygacja awaryjna (po starcie strajku):**
  - Można oferować "premia za powrót" (200% dzienna pensja per pracownik) — 50% szans na odwołanie
  - Lub czekać 24h

### 12.3 Strajk generalny

- **Czas:** 72h
- **Efekt:** jak wyżej ale dłużej, plus reputation -40
- **Negocjacja:** tylko hardcore — akceptacja **wszystkich** żądań, koszt znaczący
- **Post-strajk:** morale +15 jeśli wygrany (przez pracowników), -15 jeśli złamany (gracz przetrzymał)

### 12.4 Strajk roli

- Tylko maszyniści (lub inna rola) Unavailable
- Efekt mniejszy ale ukierunkowany (np. brak maszynistów = zero TrainRun, brak biurowych = tylko wyższe fixed cost)

---

## 13. Architektura

### 13.1 Assembly

**Nowy:** `Assets/Scripts/Personnel/RailwayManager.Personnel.asmdef`

**References:**
- `RailwayManager.Core`
- `RailwayManager.Depot` (PathGraph, RoomDetectionSystem, DepotMovementSimulator)
- `RailwayManager.Fleet` (FleetVehicleData dla przypisań, WorkshopManager hook)
- `RailwayManager.Timetable` (TrainRun, Circulation, EconomyManager)
- `RailwayManager.SharedUI`
- `Unity.TextMeshPro`
- `UnityEngine.UI`

### 13.2 Klasy — Data (POCO)

**`Personnel/Data/`:**
- `Employee.cs` — główny byt
- `EmployeeRole.cs` — enum (Driver/Conductor/Mechanic/Cleaner/WashBay/Office/Research/TicketClerk)
- `EmployeeStatus.cs` — enum (Available/OnShift/Resting/Sick/LongSick/Training/Retired/Fired)
- `ShiftType.cs` — enum (Morning/Afternoon/Night)
- `WorkCyclePattern.cs` — enum (Cycle5_2/Cycle4_2/Cycle6_2/Cycle7_7/Custom)
- `EmployeeSchedule.cs` — cykl + overrides
- `ScheduleOverride.cs` — Vacation/SickLeave/Training/ShiftSwap/ExtraDutyDay/FreeDay
- `CrewCirculation.cs` — turnus pracowniczy
- `CrewDuty.cs` — pojedyncza służba w turnusie
- `CrewDutyKind.cs` — enum (Service/Break/Deadhead/Handover)
- `EmployeeCandidate.cs` — kandydat na rynku
- `Training.cs` — aktywne szkolenie (stub w M8, implementacja M8.5)
- `TrainingType.cs` — enum (stub)
- `StationAssignment.cs` — przypisanie kasjera do stacji
- `HotelBooking.cs` — nocleg pracownika w multi-day turnusie (D20)
- `HotelTier.cs` — enum (Basic/Standard/Premium)
- `DispatcherWorkload.cs` — runtime snapshot capacity vs headcount (D27)
- `TrafficControllerWorkload.cs` — runtime snapshot capacity vs active tasks (D28)

**Enum `EmployeeRole`:** Driver, Conductor, Mechanic, Cleaner, WashBay, Office, Research, TicketClerk, Dispatcher, TrafficController.

**Enum `CrewDutyKind`:** Service, Break, Deadhead, Handover, **Overnight** (nocleg w hotelu, multi-day D20).

### 13.3 Klasy — Catalogs

**`Personnel/Catalogs/`:**
- `PersonnelBalanceConstants.cs` — wszystkie const (analogicznie do `FleetBalanceConstants`)
- `PolishNamesCatalog.cs` — pule imion i nazwisk PL (seed gen)
- `RoleDefinitions.cs` — base salary per role, fatigue rates, skill requirements

### 13.4 Klasy — Runtime

**`Personnel/Runtime/`:**

- `PersonnelService` (static singleton):
  - `HireCandidate(int candidateId, HireTerms terms)`
  - `FireEmployee(int employeeId, bool immediate)`
  - `GetAll()`, `GetByRole(role)`, `GetById(id)`
  - `GetAvailableFor(role, dateIso, shift)` — dla przypisań
  
- `CandidateMarketService` (MonoBehaviour):
  - Subskrybuje `GameState.OnDayEnded`
  - `Refresh()`, `DebugForceRefresh()`
  - `EnsureExists()`
  
- `PersonnelMarketGenerator`:
  - Seed-based generation
  - Distribution per role/skill
  
- `JobPostingService`:
  - `CreatePosting(role, skillTarget)`
  - Expires po 14 dniach, modyfikuje `MarketGenerator` weights
  
- `CrewAssignmentService`:
  - `GetDriverForTrainRun(trainRunId, dateIso)` → Employee
  - `GetConductorForTrainRun(...)` → Employee
  - `AssignEmployeeToCrewCirculation(employeeId, crewCircId)`
  - `ValidateAvailability(employeeId, dateIso)` — check overrides
  
- `CrewCirculationService`:
  - CRUD turnusów
  - `ActivateCirculation` / `DeactivateCirculation`
  
- `CrewCirculationValidator`:
  - 7 warstw walidacji (patrz §7.3)
  
- `CrewCirculationAutoGenerator`:
  - Analog M5 generator
  
- `ShiftManager`:
  - Daily tick → dla każdego employee: compute current shift status
  - Sets `Employee.currentStatus` (OnShift/Resting/Sick)
  
- `FatigueMoraleTickService`:
  - Codzienny update morale i fatigue
  
- ~~`TrainingService`~~ — **POST-EA (M8.5, D11).** W M8 tylko stub data model, brak runtime.
  
- `DispatcherService` (D27):
  - `GetActiveDispatchers()` — lista aktywnych dyspozytorów
  - `GetTotalCapacity()` — sum `50 + 5×(skill-1)` across active
  - `GetWorkload()` → `DispatcherWorkload` (current vs capacity)
  - `TryAutoAssignReplacement(employeeId, crewCircId, dutyIndex)` → success/delay/fail
  - `TryAutoAssignAdHoc(trainRunId, dateIso)` — TrainRun bez turnusu
  - Subskrybuje `OnCrewVacancyDetected`, `OnTrainRunSpawnMissingCrew`
  
- `TrafficControlService` (D28):
  - Integracja z `DepotMovementSimulator` — wstawia `IPriorityProvider` do scheduler'a
  - `GetActiveControllers()` — per depot (multi-depot post-M2.6)
  - `GetTotalCapacity()` — sum `10 + 5×(skill-1)` across active
  - `ComputePriority(DepotMoveTask)` → 0-100 wg typu (workshop-overdue/departure/wash/reshuffle)
  - Gdy brak controller'a w firmie: simulator używa default FCFS + random delay ±20%
  - Over capacity: wstawia zadania do `PendingTasks` zamiast `ActiveTasks`
  
- `HotelBookingService` (D20):
  - Multi-day turnusy — nocleg per `Overnight` duty
  - Koszt: `HotelTier × nights × employees`
  - `EconomyManager.AddCost(0, amount, "Personnel", "Hotel stay")`
  - Bez dostępnego hotelu w danym mieście → fallback "delegacja prywatna" 2× cost (+10% morale penalty)
  
- `RetirementService`:
  - Daily check age-based retirement rolls
  
- `SickLeaveService`:
  - Daily roll L4
  
- `StrikeService`:
  - Monitoring avg morale
  - `TriggerStrikeWarning`, `StartStrike`, `EndStrike`
  - Negocjacje, premie
  
- `PayrollService`:
  - Monthly paycheck (pierwszego dnia miesiąca)
  - `EconomyManager.AddCost(0, sum, "Personnel", "Monthly payroll")`
  - Obsługa opóźnień (brak środków)

- `EmployeeWalkSimulator` (MonoBehaviour, runtime ruchu 3D):
  - Analog `DepotMovementSimulator` ale dla postaci
  - Task queue per Employee
  - Interpolacja po `PathGraph` polyline
  - Instant-resolve gdy scena Depot inactive
  
- `PersonnelDispatcher3D`:
  - Koordynuje cykl dnia: per employee ustala kolejne taski (spawn → work → despawn)
  - Subskrybuje `GameState.OnDayEnded` + co X ticków

### 13.5 Klasy — UI

**`Personnel/UI/`:**
- `PersonnelMainTabUI` — główny panel z sub-tabami
- `RecruitmentUI` — lista kandydatów + filtry
- `EmployeeListUI` — mój personel
- `EmployeeDetailsUI` — drill-down
- `EmployeeScheduleEditorUI` — kalendarz month view, drag-drop overrides
- `CrewCirculationListUI` — turnusy (analog `CirculationListUI`)
- `CrewCirculationEditorUI` — drag-drop TrainRun → służba
- ~~`TrainingModal`~~ — POST-EA (M8.5, D11)
- `DispatcherWorkloadUI` — widget w tab "Dyspozytura", pokazuje capacity/headcount/pending queue
- `TrafficControlPanelUI` — tab "Nastawnia", lista controllerów + priority settings sliders
- `HotelBookingModal` — przy tworzeniu multi-day turnusu (D20), wybór hotel tier per miasto
- `CrewVacancyNotificationUI` — toast notification dla L4 replacement (D21) z przyciskami [Auto-dyspozytor] [Manual] [Cancel]
- `StrikeNegotiationModal` — żądania związków
- `PayrollReportUI` — historia pensji per miesiąc

### 13.6 Modyfikacje istniejących plików

**Minimalne** — M7 już zakończony, unikamy rewrite'u.

- `RoomDetectionSystem.cs` (Depot) — dodać `RoomType.WashBay`, `RoomType.Lab`, `RoomType.TicketOffice`, `RoomType.DispatchCenter`, `RoomType.TrafficControl`
- `DepotMovementSimulator.cs` (Depot) — hook `IPriorityProvider` interface, integracja `TrafficControlService`. Bez providera: default FCFS (behavior backward-compat)
- `StationMarker.cs` lub equivalent — pole `hasTicketOffice` (bool)
- `FleetVehicleData.cs` — pole `interiorCleanliness` (0-100, 100 default)
- `CirculationService.cs` (M5) — hook `GetRequiredRolesForTrainRun(trainRunId)` — zwraca `[Driver, Conductor]` lub `[Driver]` wg kategorii
- `TrainRunSimulator.cs` (M9) — pre-spawn check: `CrewAssignmentService.GetDriverForTrainRun` nie null (jeśli flag ON)
- `WorkshopManager.cs` (M7, po zakończeniu M7-4) — hook `GetAssignedMechanicsForSlot`, używa ich skill w `CalculateDuration`
- `BreakdownService.cs` (M7) — `selfRepairChance` używa skill mechanika z najbliższego depotu
- `EconomyManager.cs` (M6) — kategoria "Personnel" w kosztach
- `SceneController.cs` — `SceneDepotActivated` event → `PersonnelDispatcher3D` spawnuje visuals; deactivated → ukrywa

---

## 14. UI — pełny layout

### 14.1 Wejście: TopBar Depot

Ikona "Personel" w `TopBarUI` → otwiera `PersonnelMainTabUI` (modal fullscreen).

### 14.2 `PersonnelMainTabUI` — sub-taby

| Tab | Zawartość |
|---|---|
| **Mój personel** | Lista zatrudnionych, filtry per rola/shift/status. Wiersze: imię, rola, skill, morale bar, shift, aktualny stan (OnShift/Resting/Sick/...) |
| **Rekrutacja** | Lista kandydatów + filtry. Przyciski zatrudnij/negocjuj, aktywne job postings. |
| **Turnusy** | `CrewCirculationListUI` — master-detail dla turnusów maszynistów i konduktorów. |
| **Warsztaty** | Lista mechaników + przypisania do slotów (M7 integration). |
| **Biuro + R&D** | Lista biurowych + R&D, aktywne ścieżki badań. |
| **Dyspozytura** | Lista dyspozytorów + widget workload (aktualna capacity vs headcount firmy). Kolejka auto-akcji pending. Przycisk "Auto-assign all ad-hoc" (manual trigger). |
| **Nastawnia** | Lista dyżurnych ruchu + workload per depot (capacity vs `DepotMovementSimulator.ActiveTasks.Count`). Wizualizacja priorytetów aktualnych manewrów. Settings: "Priorytet warsztat overdue" (slider), "Priorytet myjni" (slider). |
| **Kasy stacyjne** | Mapa stacji + ikonki obsadzonych kas. Drag&drop kasjera na stację. (D19: kasjer widoczny **tylko** w popupie stacji, nie jako agent w świecie) |
| **Raport płac** | Historia miesięcznych wypłat, breakdown per rola, projekcja następnego miesiąca. |

### 14.3 Details pracownika

**Drill-down po kliknięciu wiersza:**

- Header: avatar placeholder + imię/nazwisko + rola + wiek + staż w firmie
- Stats: skill stars, morale bar, fatigue bar, zdrowie
- Pensja: aktualna / rynkowa (comparison) + przyciski "Podwyżka", "Premia jednorazowa"
- Schedule: kalendarz miesiąca z zaznaczonymi dniami pracy/wolnymi/override'ami + przycisk "Edytuj harmonogram"
- Turnusy (tylko Driver/Conductor): lista aktualnie przypisanych turnusów
- Historia: pensje, szkolenia, urlopy, L4 — chronological log
- Akcje: Wyślij na szkolenie | Urlop | Zwolnij | Ustaw zmianę

### 14.4 Edytor harmonogramu (`EmployeeScheduleEditorUI`)

- Kalendarz miesiąca, każdy dzień = kafelek
- Kolor wg typu dnia: zielony (praca poranna), niebieski (popołudniowa), czarny (nocna), szary (wolne), fioletowy (urlop), czerwony (L4), żółty (szkolenie)
- Prawy-klik dzień → menu override: ustaw urlop / swap zmianę / extra duty / remove override
- Conflict detection: jeśli override pokrywa się z aktywnym turnusem → warning "Konflikt z turnusem M-KR-01"
- Baseline cycle selection: 5+2 / 4+2 / 6+2 / 7+7 / Custom (modal dla custom pattern)

### 14.5 Strike negotiation modal

- Header: "Strajk ostrzegawczy za 48h" + ikona
- Lista żądań (3-5, losowane):
  - "Podwyżka 10% dla maszynistów" (koszt: +X zł/mies)
  - "Jednorazowa premia 2000 zł dla mechaników" (koszt: N × 2000)
  - "Skróć zmianę do 6h" (efekt: fatigue rate -20%, fewer duties possible)
- Dla każdego: checkbox "Zaakceptuj"
- Podsumowanie: total cost + projected morale change
- Przyciski: "Zaakceptuj wybrane" / "Odrzuć wszystkie / idź na strajk"

---

## 15. Podetapy implementacji

### M8-1 — Data model + katalogi (2 sesje)

- POCO wszystkie z sekcji 13.2
- `PersonnelBalanceConstants` z const'ami dla salary/fatigue/morale/shift times
- `PolishNamesCatalog` — pule imion/nazwisk PL (50+50 początkowe)
- `RoleDefinitions` — baseSalary, fatigueRate, skillMin
- Asmdef `RailwayManager.Personnel.asmdef` + references
- Log wrapper konwencja `[ClassNameX]`

**Deliverable:** kompiluje się, żaden runtime.

### M8-2 — PersonnelService + Employee CRUD (1 sesja)

- `PersonnelService.Instance` + Add/Remove/Get
- Testy manualne: `[ContextMenu("Debug: Hire Dummy Driver")]`
- `EnsureExists()` bootstrap

**Deliverable:** można zatrudnić pracownika via context menu, zobaczyć go w liście (placeholder log).

### M8-3 — CandidateMarket + rekrutacja + UI (2 sesje)

- `PersonnelMarketGenerator` z dystrybucją + seed
- `CandidateMarketService` subskrybuje `OnDayEnded`, refresh co 7 dni
- `JobPostingService` (skeleton)
- `RecruitmentUI` w ramach `PersonnelMainTabUI`
- Zatrudnianie z negocjacją

**Deliverable:** gracz otwiera panel, widzi kandydatów, zatrudnia, pracownik pojawia się w "Mój personel".

### M8-4 — EmployeeListUI + Details + schedule editor (2 sesje)

- `EmployeeListUI` z filtrami
- `EmployeeDetailsUI` drill-down
- `EmployeeScheduleEditorUI` — edytor kalendarza
- Zwalnianie + odprawa (hook EconomyManager)

**Deliverable:** pełen CRUD UI, można zarządzać personelem od A do Z bez obiegów.

### M8-5 — Shifts + fatigue + morale + payroll (2 sesje)

- `ShiftManager` — daily tick
- `FatigueMoraleTickService` — compute stats daily
- `PayrollService` — pierwszy dzień miesiąca, `EconomyManager.AddCost`
- UI bars morale/fatigue live-updated

**Deliverable:** pracownicy mają życie — morale się zmienia, pensje płacone, shift switching.

### M8-6 — Training + retirement + sick leave (1-2 sesje)

- `RetirementService` age-based + 30-dniowe wypowiedzenie
- `SickLeaveService` L4 roll + emit `OnCrewVacancyDetected`
- `CrewVacancyNotificationUI` — toast z [Auto-dyspozytor]/[Manual]/[Cancel] (D21)
- Notyfikacje urlopu/L4/emerytury w TopBar
- ~~TrainingService~~ → POST-EA (D11)

**Deliverable:** pracownicy chorują (L4 notif + 3 opcje replacement), starzeją się, idą na emeryturę.

### M8-7 — Dyspozytor (Dispatcher) service + UI (2 sesje) — **NOWY**

- `DispatcherService` — capacity calc, auto-assign logic (ad-hoc + L4)
- `DispatcherWorkload` runtime snapshot
- `DispatcherWorkloadUI` — tab "Dyspozytura"
- Integracja z `CrewVacancyNotificationUI` (D21) — przycisk [Auto-dyspozytor] używa Service
- Ręczny override dla wszystkich auto-akcji

**Deliverable:** zatrudnienie dyspozytora → auto-zastępstwa L4 + auto-assign pociągów ad-hoc. Over-capacity = delays obserwowalne w UI.

### M8-8 — CrewCirculation model + validator + UI (3 sesje)

- `CrewCirculation`, `CrewDuty`, `CrewDutyKind` (w tym `Overnight` dla multi-day)
- `CrewCirculationService` CRUD
- `CrewCirculationValidator` 7 warstw
- `CrewCirculationListUI` master-detail
- `CrewCirculationEditorUI` drag&drop TrainRun → duty
- Przypisanie Employee → Circulation
- **Multi-day support (D20):** `durationDays` field, `Overnight` duty type, `HotelBookingService`, `HotelTier` selection
- `HotelBookingModal` — wybór hotel tier per miasto przy tworzeniu multi-day

**Deliverable:** gracz tworzy turnusy (1-day i multi-day z hotelami), system waliduje, działają niezależnie od obiegów taboru.

### M8-9 — CrewCirculation auto-generator (1-2 sesje)

- `CrewCirculationAutoGenerator`
- Modal settings (min gap, max work hours, geographical clustering, multi-day toggle)
- Preview + commit

**Deliverable:** gracz klika "Wygeneruj turnusy" → system tworzy szkielet dla wszystkich TrainRun'ów (w tym multi-day gdzie geografia wymaga).

### M8-10 — Cykl dnia 3D (spawn + pathfinding + placeholders) (3 sesje)

- `EmployeeVisual` prefab + kolory per rola + TextMesh label
- `EmployeeWalkSimulator` — analog DepotMovementSimulator
- `PersonnelDispatcher3D` — daily scheduling
- `DepotGateMarker` prefab + placement tool (Depot toolbar)
- Integracja z `RoomDetectionSystem` — dodać WashBay/Lab/TicketOffice/DispatchCenter/TrafficControl enums
- Instant-resolve gdy scena Depot inactive
- **Perf optymalizacja (D26):** object pooling capsules, LOD distance-based, update rate 1Hz idle / 10Hz active, spatial hashing

**Deliverable:** wchodzisz do Depot, widzisz kapsułki pracowników chodzące od bramy do pomieszczeń. 500+ pracowników bez spadku FPS.

### M8-11 — Dyżurny ruchu (TrafficController) + M9b integracja (2 sesje) — **NOWY**

- `TrafficControlService` — priority provider dla `DepotMovementSimulator`
- Rozszerzenie `DepotMovementSimulator` o `IPriorityProvider` interface (backward compat: brak = FCFS + random delay)
- `TrafficControllerWorkload` runtime
- `TrafficControlPanelUI` — tab "Nastawnia" z workload + priority sliders
- `RoomType.TrafficControl` + equipment "Pulpit nastawczy", "Monitor CCTV"
- Bez controllera: simulator działa default (70% efficiency jak D28)

**Deliverable:** manewry zajezdni priorytetyzowane wg warsztat/myjnia/wyjazd zamiast FCFS. Zatrudnienie dyżurnego obserwowalne w wydajności zajezdni.

### M8-12 — Integracja M7 (mechanicy w warsztatach) (1-2 sesje)

- `WorkshopManager.AssignMechanic`
- Skill check w `CalculateDuration`, `BreakdownService.selfRepairChance`
- UI "Warsztaty" tab w `PersonnelMainTabUI`
- Slot assignment przez drag&drop
- Pensje mechaników zamiast placeholder w M7 costs

**Deliverable:** skill mechanika realnie wpływa na czas napraw, M7 używa M8.

### M8-13 — Integracja M5+M9 (maszyniści w obiegach taboru) (1-2 sesje)

- `CrewAssignmentService.GetDriverForTrainRun`
- Hook w `TrainRunSimulator.Spawn` — sprawdzenie załogi
- Feature flag `RequireCrewForCirculation` (settings + debug menu)
- Visual: embed capsule w pojazd (hide na mapie 2D, załoga w popupie pociągu — D25)
- Powrót służbowy (Deadhead): pracownik jako pasażer w innym pociągu
- Integracja z dyspozytorem: auto-assign dla TrainRun bez turnusu

**Deliverable:** brak maszynisty = pociąg nie startuje (gdy flag ON). Turnusy + ad-hoc działają end-to-end.

### M8-14 — Sprzątacze + myjnia + cleaning cycle (1-2 sesje)

- `CleaningService` — auto-assignment po przyjeździe pojazdu do depotu
- `interiorCleanliness` na `FleetVehicleData`
- Myjnia — `RoomType.WashBay` + equipment
- UI configure auto-cleaning toggle
- Integracja z dyżurnym ruchu: prioryty myjni / sprzątania w `DepotMovementSimulator`

**Deliverable:** pojazdy są sprzątane / myte, cleanliness wpływa na comfort i reputation.

### M8-15 — Biurowi + R&D + kasjerzy (2 sesje)

- Office clerks w `RoomType.Office` — fixed cost reduction
- R&D framework + 2 początkowe ścieżki badań w `RoomType.Lab`
- Kasjerzy przypisanie do stacji — revenue bonus (D19: tylko popup, brak spawnu)
- UI: "Biuro + R&D", "Kasy stacyjne" (mapa stacji)

**Deliverable:** 3 pozostałe role (Office/R&D/Kasjer) działa w pełni, gracz widzi efekty ekonomiczne.

### ~~M8-16 — Strajki + związki zawodowe~~ (POST-1.0)

**Decyzja 2026-04-21 (D12 zaktualizowane):** strajki i negocjacje przesunięte do post-1.0 (M15+).

W EA morale zostaje jako skala 0-100 wpływająca na:
- Wydajność (§5.1 bucket'y: >80 Entuzjasta +5%, 20-40 Zagrożony odejściem 1%/dzień, <20 Critical 3%/dzień)
- Odejścia dobrowolne (morale<20 → wypowiedzenie pracownika)
- Fatigue coupling (nadal działa)

Co usunięte/odłożone:
- `StrikeService` — brak triggerów 5-dniowego okna przed strajkiem
- `StrikeNegotiationModal` — brak modalu z 3-5 żądań
- Event "strajk ostrzegawczy 24h" / "strajk generalny 72h"
- Premia za powrót (200% dzienna pensja)

Uproszczenie wymusza na graczu monitorowanie morale jako proaktywną obronę — brak reakcyjnego "rozwiązywania" strajków. Zysk: -3-5 sesji scope'u M8 + mniej złożonego UX.

### ~~M8-17 — Save/Load hooks + polish~~ (Save/Load → M13, polish → M-Balance)

**Decyzja 2026-04-21:** Save/Load zostaje przesunięty do M13 (osobny milestone), polish UI do M-Balance (finalny balance pass całej gry).

Save/Load wszystkich modułów (Fleet/Timetable/Personnel/Economy/Maintenance) będzie scentralizowany w M13 — każdy moduł udostępni metodę `SerializeToJson()` / `DeserializeFromJson()` zgodną z rejestrowym designem M13. M8 POCO są już `[Serializable]` — zero zmian kodowych wymaganych teraz.

Polish UI (animacje, kolory, tooltips, edge cases): M-Balance (post-M13 gdy save/load umożliwi iteracyjne testy).

**M8 zamknięty na M8-15** — 10 ról aktywnych, obiegi pracownicze end-to-end, hooks M5/M7/M9/M9b, UI 9 tabów, 3D placeholder w Depot.

**Razem: 20-25 sesji** (decyzja 2026-04-21 final). Zmiany vs v0.1: usunięto Training (post-EA) = -2 sesje, dodano Dispatcher (M8-7) = +2, Multi-day hotele (M8-8 rozszerzone) = +1, TrafficController (M8-11) = +2, Perf optymalizacja (M8-10 rozszerzone) = +1, **strajki+związki M8-16 → post-1.0** = -3 sesje, **Save/Load M8-17 → M13** = -2 sesje, polish UI → M-Balance = -1 sesja.

**M8 zamknięty na M8-15.** Wszystkie 10 ról + obiegi pracownicze + cykl 3D + integracje M5/M7/M9/M9b działają end-to-end. Kolejne polishe gdy M13 i M-Balance wchodzą.

---

## 16. Integracja z innymi systemami

### M3 Fleet
- `FleetVehicleData.interiorCleanliness` — dodanie pola
- Komfort pasażerów w M6 czyta cleanliness

### M4 Timetable
- Nowa kategoria `SL` (Służbowy) już dodana w M5 — używana dla deadhead turnusów
- `TrainRun.requiredRoles` — hook wywoływany z M8 do określenia czy potrzebuje konduktora

### M5 Obiegi
- `Circulation.Active` emit event → `CrewCirculationAutoGenerator` może zareagować (auto-sugestia tworzenia turnusu)
- `CrewCirculation` używa `TrainRun` z `Circulation` — ale niezależnie (jeden TR może być w N turnusach kolejno w ciągu dnia)

### M6 Ekonomia
- `EconomyManager.AddCost(0, amount, "Personnel", employeeName)` — miesięczne pensje
- Koszty szkoleń kategoria "Training"
- Odprawy emerytalne i zwolnieniowe kategoria "Severance"
- Job postings kategoria "Recruitment"
- Premie strajkowe kategoria "Strike"

### M7 Maintenance
- Mechanicy w warsztacie → `WorkshopManager.CalculateDuration` używa avgSkill
- Self-repair → `BreakdownService.selfRepairChance` boost od mechanika w najbliższym depocie
- Placeholder pensji warsztatu z M7 zastąpiony realnymi pensjami mechaników

### M9 Ruch (M9a świat 2D)
- `TrainRunSimulator.Spawn` pre-check: maszynista dostępny (feature flag `RequireCrewForCirculation`)
- Powrót służbowy maszynisty jako Deadhead pasażer w innym TR
- Morale fatigue ticky — event `OnTrainRunCompleted` aktualizuje fatigue zaangażowanych pracowników
- Popupy pociągu na mapie 2D (D25): sekcja "Załoga" z listą przypisanych (maszynista, konduktor) — klikalne → otwiera EmployeeDetails

### M9b Ruch w zajezdni (TrafficController hook)
- `DepotMovementSimulator` dostaje `IPriorityProvider` interface
- `TrafficControlService` implementuje provider: computes priority per task
- Bez controllera: default FCFS + random ±20% (backward compat)
- Over capacity controller: tasks w `PendingTasks`, delays widoczne w UI "Nastawnia"

### M-PL / Pełna mapa
- Kasjerzy przypisywani do realnych stacji (nie tylko mazursko-warmińskich)
- Pensje rosną w centralnych miastach (post-EA, regional multipliers)

### M-Models
- Swap `EmployeeVisual` capsule → real prefab (rig Humanoid)
- Animacje: walk, idle, role-specific work (mechanic z narzędziem, kasjer siedzi)

### M12d Random events
- Specjalne wydarzenia: "Gwiazdor techniczny przychodzi z pytaniem o pracę" (1 kandydat 5★ darmowy hire bonus)
- "Pracownik dostał nagrodę branżową" (+10 morale całej firmy)

### M13 Save/Load
- Pełna serializacja (sekcja 15, M8-15)
- Migration paths przy zmianach schema

---

## 17. Balance constants (wytyczne — M6.5 Rebalance)

`Personnel/Catalogs/PersonnelBalanceConstants.cs`:

```csharp
public static class PersonnelBalanceConstants
{
    // === Salaries (base per role, 1-star, miesięczne w groszach) ===
    public const int BaseSalaryDriver            = 450_000; // 4500 zł
    public const int BaseSalaryConductor         = 320_000;
    public const int BaseSalaryMechanic          = 380_000;
    public const int BaseSalaryCleaner           = 240_000;
    public const int BaseSalaryWashBay           = 220_000;
    public const int BaseSalaryOffice            = 300_000;
    public const int BaseSalaryResearch          = 500_000;
    public const int BaseSalaryTicket            = 260_000;
    public const int BaseSalaryDispatcher        = 350_000; // 3500 zł (D27)
    public const int BaseSalaryTrafficController = 320_000; // 3200 zł (D28)
    
    public const float SkillSalaryMultBase = 0.7f;
    public const float SkillSalaryMultPerStar = 0.15f;
    // salary = base × (0.7 + 0.15 × skill)
    
    // === Fatigue ===
    public const float FatigueRateDriverPerHour    = 1.2f;
    public const float FatigueRateConductorPerHour = 0.9f;
    public const float FatigueRateMechanicPerHour  = 0.8f;
    public const float FatigueRateCleanerPerHour   = 1.0f;
    public const float FatigueRateWashPerHour      = 0.8f;
    public const float FatigueRateOfficePerHour    = 0.5f;
    public const float FatigueRateResearchPerHour  = 0.4f;
    public const float FatigueRateTicketPerHour    = 0.8f;
    public const float FatigueRateDispatcherPerHour      = 0.6f;
    public const float FatigueRateTrafficControllerPerHour = 0.7f;
    
    public const float FatigueRegenDayOff = 80f;
    public const float FatigueRegenShiftEnd = 20f;
    
    // === Morale ===
    public const int MoraleStartNewHire = 70;
    public const int MoraleDailySalaryAbove = 1;      // +1/day jeśli pensja >10% powyżej rynku
    public const int MoraleDailySalaryBelow = -2;
    public const int MoraleMissedPayment = -20;
    public const int MoraleOvertimePenalty = -1;
    public const int MoraleFatigueOverPenalty = -3;
    public const int MoraleTrainingSuccess = 15;
    public const int MoraleBonusPerThousand = 1;
    public const int MoraleColleagueFired = -2;
    public const int MoraleStrikeSuccess = 25;
    public const int MoraleStrikeFailure = -15;
    
    public const int MoraleThresholdStrikeWarning = 30;
    public const int MoraleThresholdStrikeGeneral = 20;
    public const int StrikeWarningDaysRequired = 5;
    public const int StrikeGeneralDaysRequired = 7;
    public const int StrikeWarningHours = 24;
    public const int StrikeGeneralHours = 72;
    
    // === Shifts ===
    public const int ShiftMorningStartSec = 6 * 3600;   // 21_600
    public const int ShiftMorningEndSec = 14 * 3600;
    public const int ShiftAfternoonStartSec = 14 * 3600;
    public const int ShiftAfternoonEndSec = 22 * 3600;
    public const int ShiftNightStartSec = 22 * 3600;
    public const int ShiftNightEndSec = 6 * 3600;       // rollover
    
    // === Market ===
    public const int CandidateMarketRefreshDays = 7;
    public const int CandidateMarketRefreshAddCount = 4;
    public const int CandidateMarketMaxSize = 30;
    public const int CandidateValidityDays = 7;
    
    // === Job Posting ===
    public const int JobPostingDurationDays = 14;
    public const int JobPostingBaseCost = 300_000;    // 3000 zł
    public const int JobPostingSpecialistMultPerStar = 100_000;
    public const int JobPostingMaxActive = 3;
    
    // === Training — POST-EA (M8.5), stub values ===
    // public const int TrainingExternalDaysBase = 14;
    // public const int TrainingExternalCost1to2 = 800_000;
    // ... (implementacja w M8.5, D11)
    
    // === Dispatcher (D27) ===
    public const int DispatcherBaseCapacity = 50;       // 1-star
    public const int DispatcherCapacityPerStar = 5;     // +5 per star
    public const float DispatcherOverCapacityDelayHours = 4f;  // 2-6h range
    public const float DispatcherCriticalOverThreshold = 1.5f; // >150% = missed
    public const float DispatcherMissedActionChance = 0.2f;
    public const int DispatcherAutoTimeoutHours = 1;    // L4 replacement timeout
    
    // === Traffic Controller (D28) ===
    public const int TrafficControllerBaseCapacity = 10;
    public const int TrafficControllerCapacityPerStar = 5;
    public const float TrafficControllerCriticalOverThreshold = 1.5f;
    public const float TrafficControllerOverCapacityDelayMult = 0.5f;  // +50% time
    public const float DepotMovementNoControllerEfficiency = 0.7f;
    public const float DepotMovementRandomDelayVariance = 0.2f; // ±20% bez dyżurnego
    
    // === Hotels (multi-day turnusy, D20) ===
    public const int HotelCostBasicPerNight    = 8_000;   // 80 zł
    public const int HotelCostStandardPerNight = 15_000;  // 150 zł
    public const int HotelCostPremiumPerNight  = 25_000;  // 250 zł
    public const int HotelMoralePremiumBonus   = 2;       // +2 morale/night
    public const int HotelMoraleBasicPenalty   = -1;
    public const int HotelPrivateFallbackMult  = 2;       // 2× koszt gdy brak hotelu
    public const int CrewMaxMultiDayDays       = 3;       // EA limit
    
    // === Conductor requirement (D16 + D31: wagon/EMU/DMU osobno) ===
    public const int ConductorRequiredFromWagonCount = 3;  // > 3 wagony pasażerskie
    public const int ConductorRequiredFromEmuCount   = 1;  // > 1 EMU osobno
    public const int ConductorRequiredFromDmuCount   = 1;  // > 1 DMU osobno
    public const float FareEvasionWithoutConductor   = 0.15f; // +15% gapowiczów
    
    // === Hotel performance modifiers (D29) ===
    public const float HotelBasicFatigueRegenMult    = 0.8f;   // -20% regen
    public const float HotelStandardFatigueRegenMult = 1.0f;   // baseline
    public const float HotelPremiumFatigueRegenMult  = 1.2f;   // +20% regen
    public const float HotelBasicErrorChanceMult     = 1.1f;   // +10% błędów następnego dnia
    public const float HotelStandardErrorChanceMult  = 1.0f;
    public const float HotelPremiumErrorChanceMult   = 0.9f;   // -10% błędów
    
    // === Dispatcher auto-assign weights (D30, configurable UI) ===
    public const float DispatcherWeightProximity   = 0.4f;
    public const float DispatcherWeightSkillMatch  = 0.3f;
    public const float DispatcherWeightRestedness  = 0.3f;
    // Skill dyspozytora modyfikuje — 1★ tylko proximity, 5★ wszystko
    
    // === Traffic Controller priority defaults (D34, sliders w UI) ===
    public const int TrafficPriorityWorkshopOverdue   = 100;
    public const int TrafficPriorityScheduledDeparture = 80;
    public const int TrafficPriorityWashBayPlanned    = 60;
    public const int TrafficPriorityParkingReshuffle  = 40;
    
    // === 24/7 coverage requirement (D35) ===
    public const int DispatcherMin24_7Count     = 3;  // minimum 3 zmiany
    public const int TrafficControllerMin24_7Count = 3;
    
    // === Retirement + Sick ===
    public const int RetirementAgeMin = 60;
    public const int RetirementAgeMax = 65;
    public const int RetirementAgeForce = 70;
    public const float RetirementChance60to64PerMonth = 0.05f;
    public const float RetirementChance65plusPerDay = 0.20f;
    public const int RetirementNoticeDays = 30;
    public const int RetirementSeveranceMonths = 3;
    
    public const float SickLeaveChancePerDay = 0.005f;
    public const float SickLeaveFatigueModifier = 0.02f; // × fatigue over 50
    public const int SickLeaveMinDays = 1;
    public const int SickLeaveMaxDays = 3;
    
    // === Severance (zwolnienie) ===
    public const int SeveranceUnder1Month = 0;
    public const int Severance1To6Months = 1;  // multiplier pensji
    public const int Severance6To24Months = 2;
    public const int SeveranceOver2Years = 3;
    
    // === Vacation ===
    public const int VacationDaysPerYear = 26;
    public const int VacationRolloverMaxDays = 10;
    
    // === Crew Circulation ===
    public const int CrewMaxWorkHoursPerDay = 12;
    public const int CrewMinBreakAfterHours = 4;
    public const int CrewMinBreakMinutes = 30;
    public const int CrewHandoverMinMinutes = 5;
    
    // === Cleaning ===
    public const float CleaningInteriorRestorePerMinute = 20f;  // % /minuta
    public const float CleaningDegradationPerKm = 0.0003f;
    
    // === Office clerks ===
    public const float OfficeFixedCostReductionPerClerkPerStar = 0.01f; // 1% per clerk per star
    public const float OfficeFixedCostReductionMax = 0.30f;
    
    // === Ticket clerks ===
    public const float TicketClerkRevenueBonus = 0.08f;  // +8% from that station
    
    // === Walk simulator (3D) ===
    public const float WalkSpeedNormalMps = 1.4f;
    public const float WalkSpeedHurryMps = 2.5f;
}
```

Wszystkie wartości to **wytyczne startowe** — docelowy balance w **M6.5 Rebalance** po całości M6+M-Fleet+M7+M8+M13.

---

## 18. Pytania otwarte

**Rozstrzygnięte** (2026-04-20):
- Q-A..Q-L + TBD dyspozytor → **D16-D28** w §2
- Q-M..Q-U → **D29-D36** w §2

**Pozostałe otwarte** (do rozstrzygnięcia przy implementacji konkretnego podetapu):

- Żadne critical path na ten moment. Pytania z §18 v0.2 wszystkie rozstrzygnięte.
- Nowe pytania będą pojawiać się przy implementacji poszczególnych podetapów (typowo related do edge cases, performance trade-offs, UX detali) — dopisujemy jako Q-V, Q-W, etc. i rozstrzygamy w trakcie.

---

## 19. Success metrics / Definition of Done

**M8 uważamy za zamknięte gdy:**
1. Wszystkie **10 ról** ma działający gameplay (zatrudnienie, praca, morale, payoff)
2. Obieg pracowniczy można stworzyć ręcznie + auto-generator produkuje sensowne szkielety (1-day + multi-day z hotelami)
3. Indywidualny harmonogram edytor działa (urlopy, L4, shift swap)
4. Pracownicy widoczni w Depot 3D jako kapsułki, chodzą po pathgraphie, docierają do pomieszczeń
5. Integracja M5 (maszyniści+konduktorzy) i M7 (mechanicy) aktywna i przetestowana
6. Dyspozytor — auto-assign działa (ad-hoc + L4 replacement), capacity scaling obserwowalny
7. Dyżurny ruchu — priorytety manewrów widoczne w M9b DepotMovementSim (workshop overdue wyprzedza parking)
8. Strajki trigerują się przy morale <30%, negocjacje działają
9. Save/Load kompatybilne
10. Sanity playtest ≥2h bez crashu
11. Flag `RequireCrewForCirculation` można włączyć bez breakage
12. Performance: 500+ pracowników w zajezdni bez spadku FPS (target D26)

**Nie-cele (celowo poza scope M8):**
- Szkolenia / awans skilla (→ M8.5, D11)
- Detailed cykl dnia (animacje role-specific, trzymanie narzędzi, rozmowy)
- Real modele 3D (to M-Models)
- Pełne związki zawodowe (negocjacje wielopoziomowe, wieloletnie układy zbiorowe) → post-EA
- Emigracja / relokacja pracowników między zajezdniami (przy multi-depot) → post-M2.6
- Staże / praktykanci → post-EA (D23)
- Brutto/ZUS symulacja pensji → post-EA (D22)
