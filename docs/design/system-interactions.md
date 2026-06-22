# Mapa połączeń systemów — Railway Manager

> **Cel:** Opis jak poszczególne systemy gry się ze sobą łączą — który moduł
> mówi do którego, które dane przechodzą między systemami, które wydarzenia
> w jednym systemie triggerują reakcje w innym.
>
> **Kiedy używać:** Przed implementacją nowego milestone'a sprawdź który system
> z którym potrzebuje rozmawiać. Przy debug'u problemu z przepływem danych szukaj
> tu ścieżki interakcji.
>
> **Uzupełnianie:** Każdy kolejny milestone dodaje swoją sekcję "Integracje z..."
> i rozszerza tabelę głównych wiązek.
>
> **Status dokumentu:** mapa kontraktów i przepływów danych. Statusy sekcji
> zsynchronizowane 2026-05-29.

---

## Diagram wysokopoziomowy

```
                         ┌──────────────┐
                         │  Core (Log,  │
                         │ GameState,   │
                         │SceneController)
                         └──────┬───────┘
                                │
         ┌──────────────────────┼──────────────────────┐
         │                      │                      │
    ┌────▼────┐           ┌─────▼────┐           ┌─────▼────┐
    │   Map   │           │ SharedUI │           │ MainMenu │
    │ (2D OSM │           │ (TopBar) │           │GameCreator
    │ binary) │           └──────────┘           └──────────┘
    └────┬────┘
         │
    ┌────▼────┐           ┌──────────┐
    │  Depot  │◄──────────│  Fleet   │  (Depot używa FleetService dla taboru)
    │  (3D)   │           │          │
    └────┬────┘           └────┬─────┘
         │                     │
         └──────────┬──────────┘
                    │
              ┌─────▼─────┐
              │ Timetable │ (M4 — używa Map/Fleet/Depot)
              └─────┬─────┘
                    │
    ┌───────────────┼───────────────┐
    │               │               │
┌───▼────┐   ┌──────▼──────┐  ┌─────▼─────┐
│Circulat│   │TrainMovement│  │  Economy  │
│  M5    │◄──│    M9       │◄─│    M6     │
└────────┘   └──────┬──────┘  └───────────┘
                    │
              ┌─────▼─────┐
              │Maintenance│
              │    M7     │
              └─────┬─────┘
                    │
              ┌─────▼─────┐
              │ Personnel │
              │    M8     │
              └───────────┘
```

---

## Systemy — kto z kim gada

### Core (fundament)
**Eksponuje:** `GameState` (Money, GameTime, TimeScale), `Log`, `SceneController`, `InputActions`

**Konsumentów:** WSZYSTKIE moduły (Core to baza)

**Events:** brak (Core jest pasywny — pojemnik stanu)

---

### Map (moduł 2D OSM)
**Eksponuje:** `MapLoader`, `TileManager`, `MapRenderer`, `CameraController`, `RailwayGraph`, binary format features

**Używa:** Core (Log, GameState)

**Konsumenty:**
- **Depot** (przez `DepotRailwayIntegration`) — lokalizacja zajezdni, connection do sieci
- **Timetable** — `RailwayGraph` (pathfinding), features (stacje, platformy, sygnały)
- **M9 TrainMovement** — renderowanie pociągów na mapie, polyline tras
- **M-PL** — granice kraju, miasta (Places layer)
- **M6 Economy** — Places layer dla popytu pasażerów
- **M10 Multiplayer** — synchronizacja mapy między graczami

**Events:** brak (wszystko przez public API)

---

### Depot (3D)
**Eksponuje:** `DepotManager`, `DepotRailwayIntegration`, `TrackGraph`, `PathGraph`, `CatenaryGenerator`, ToolbarUI/StateMachines, `ExitTrackController`

**Używa:**
- Core (GameState, Log, Input)
- Fleet (kupno pojazdów w FleetPanelUI — zakładki taboru)
- SharedUI (TopBar)
- Map (przez DepotRailwayIntegration — opcjonalnie)

**Konsumenty:**
- **M9 TrainMovement** — spawn/despawn pociągów przy przejściu Depot ↔ Mapa
- **M7 Maintenance** — UI warsztatu, przeglądy w zajezdni
- **M8 Personnel** — 3D postacie chodzą chodnikami, pomieszczenia
- **M12d Features** — multi-depot i expand grid

**Events:** `DepotUIManager.OnToolChanged`, `OnTrackSubModeChanged`

---

### Fleet (tabor)
**Eksponuje:** `FleetService` (singleton), `FleetCatalog`, `CartProcessor`, `EvnGenerator`, `SeatLayoutGenerator`, `MaintenanceCostCalculator`, `ComfortCalculator`

**Używa:** Core

**Konsumenty:**
- **Depot** (FleetPanelUI — MyFleet/Market/Configurator/Cart/SeatLayout/Consists)
- **Timetable** — `PlannedComposition` referuje typy z `FleetCatalog`
- **M5 Circulations** — `assignedFleetVehicleIds` lock pojazdów w obiegach
- **M9 TrainMovement** — wybór prefaba 3D na podstawie `FleetVehicleData.modelId`
- **M7 Maintenance** — `VehicleComponents` degradacja + historia napraw
- **M6 Economy** — koszty zakupu + miesięczne utrzymanie
- **M8 Personnel** — mechanicy (skill wpływa na naprawy w M7)

**Events:** `FleetService.OnFleetChanged`

---

### Timetable (M4 rozkłady)
**Eksponuje:** `TimetableService` (singleton), `Route`, `Timetable`, `TrainRun`, `ReservationManager`, pathfinding (`PathfindingGraph`, `RailwayPathfinder`), `CategoryClassifier`, `TrainNumberValidator`, block sections

**Używa:**
- Core, Map (graf), Fleet (`CompositionMode`, `assignedFleetVehicleIds`), SharedUI

**Konsumenty:**
- **M5 Circulations** — łączy `TrainRun`y w sekwencje
- **M9 TrainMovement** — `TrainRun` runtime fields (`currentDelaySec`, `currentPositionOnRouteM`), bloki semaforowe
- **M6 Economy** — `CommercialCategory` ceny biletów, dystans trasy, pasażerowie per stacja
- **M10 Multiplayer** — sync rozkładów + rezerwacji między graczami

**Events:** `TimetableService.OnTimetablesChanged`, `OnRoutesChanged`

---

### M5 Circulations (Obiegi) ✅
**Eksponuje:** `CirculationService`, `Circulation` data, `CirculationValidator`, zakładka UI Circulations

**Używa:**
- **Timetable** — lista `Timetable`/`TrainRun` do łączenia w obieg
- **Fleet** — `FleetService.OwnedVehicles` do przypisywania konkretnych pojazdów
- **TimetableService** — update `Timetable.compositionMode` Symbolic → Concrete gdy obieg przypisany

**Konsumenci:**
- **M9 TrainMovement** — TrainRunSimulator pyta o Circulation żeby wiedzieć który FleetVehicle wykonuje run, spawn właściwego prefaba 3D
- **M7 Maintenance** — zużycie pojazdu kumulowane po całym obiegu (nie per kurs)
- **M6 Economy** — koszty eksploatacji naliczane per obieg, nie per kurs
- **M8 Personnel** — przydział maszynisty per obieg lub per kurs (wymiana na stacji)

**Kluczowe dane przechodzące między Timetable ↔ M5:**
- Timetable → M5: lista `TrainRun` do zgrupowania
- M5 → Timetable: update `compositionMode = Concrete`, `assignedVehicleIds`
- M5 → Fleet: lock pojazdów (`vehicle.inCirculationId = N`)

---

### M9 TrainMovement (Ruch pociągów) ✅
**Eksponuje:** `TrainRunSimulator`, wizualizacja pociągów na mapie 2D, Depot↔Mapa transitions, placeholder visual logic

**Używa:**
- **Timetable** — aktywne `TrainRun` + `BlockSectionReservations`
- **M5 Circulations** — który pojazd wykonuje który TrainRun
- **Fleet** — `FleetVehicleData` (prefab 3D, pantograf type, engine type)
- **Depot** — `ExitTrackController` (spawn z zajezdni), sceneria home depot
- **Map** — polyline tras, wizualizacja na warstwie Map
- **Catenary (Depot)** — sprawdzanie czy pociąg elektryczny ma sieć nad sobą (pantograf up/down)

**Konsumenci:**
- **M6 Economy** — raport pasażerów wsiadających/wysiadających, naliczanie biletów
- **M7 Maintenance** — raport przejechanych km, zużycie kondycji
- **M10 Multiplayer** — sync pozycji między graczami

**Events:**
- `OnTrainRunStarted(TrainRun)` — spawn na mapie
- `OnTrainRunCompleted(TrainRun)` — despawn, ewentualne przejście do Depot
- `OnStationReached(TrainRun, station)` — dla M6 ekonomii (pasażerowie)
- `OnBlockEntered(TrainRun, blockKey)` — dla M9 rezerwacji
- `OnBlockConflict(TrainRun, blockKey, blockingTrainRunId)` — dla M9 kolizji

---

### M6 Economy ✅
**Eksponuje:** `EconomyService`, `PassengerDemandGenerator`, `TicketPricingCalculator`, bilans UI

**Używa:**
- **Map** — Places layer (popyt per miasto), aglomeracje
- **Timetable** — `CommercialCategory` ceny, trasy, rozkłady
- **M5 Circulations** — koszty per obieg (pojazd, personel, paliwo)
- **M9 TrainMovement** — events `OnStationReached` (zbieranie pasażerów, naliczanie biletów)
- **M7 Maintenance** — koszty napraw, części, przeglądów
- **M8 Personnel** — wypłaty miesięczne
- **Fleet** — koszt zakupu, monthlyMaintenanceCost

**Konsumenci:**
- **GameState.Money** — update po każdej transakcji
- **TopBarUI** — wyświetlanie salda + dzienny bilans
- **M12d Random events** — awarie/strajki generują dodatkowe koszty

**Events:**
- `OnBalanceUpdated(delta, reason)` — każda transakcja
- `OnDailyBalance(income, expense, net)` — podsumowanie dnia
- `OnBankruptcy()` — saldo = 0, game over?

---

### M-PL Pełna Polska ✅
**Nie jest typowym systemem** — to krok skalowania. Ale ma swoje wpływy:

**Wpływa na:**
- **Map** — regeneracja `poland.bin`, tile loading performance
- **Timetable** — testy pathfindingu na długich trasach
- **M9** — testy ruchu w skali (dziesiątki pociągów)
- **MainMenu/GameCreator** — ekran wyboru home depot

**Nie wpływa na kod:**
- Fleet, Depot, M5 Circulations — działają tak samo na full mapie

---

### M7 Maintenance ✅
**Eksponuje:** `MaintenanceService`, `VehicleDegradationTracker`, auto-scheduler, UI warsztatu

**Używa:**
- **Fleet** — `FleetVehicleData.components`, `InspectionSchedule`, `MaintenanceRecord`
- **M9 TrainMovement** — events `OnKmTraveled` do degradacji
- **M5 Circulations** — po zakończeniu obiegu sprawdź czy pojazd wymaga przeglądu
- **Depot** — wizualizacja napraw w warsztacie (M12d+)
- **M8 Personnel** — skill mechanika wpływa na tempo napraw
- **M6 Economy** — koszty napraw (pieniądze + części)

**Konsumenci:**
- **Fleet** — zmiana `conditionPercent`, `components.*`
- **M9** — pojazd z breakdown'em staje na trasie

---

### M8 Personnel ✅
**Eksponuje:** `PersonnelService`, `Employee` data, HR UI, cykl dnia w 3D

**Używa:**
- **Depot** — 3D postacie chodzą chodnikami, pomieszczenia (biuro, szatnia)
- **M5 Circulations** — przydział maszynisty per obieg
- **M9 TrainMovement** — pociąg nie wyjeżdża bez maszynisty
- **M7 Maintenance** — mechanicy pracują w warsztacie
- **M6 Economy** — wypłaty miesięczne

**Konsumenci:**
- **M9** — bez maszynisty nie ma `OnTrainRunStarted`
- **M7** — mechanik skill wpływa na tempo naprawy

---

### M10 Multiplayer 📋
**Będzie eksponować:** `NetworkManager` (Mirror), lobby, sync systems

**Będzie używać:**
- **Wszystkie core systems** — każdy musi mieć możliwość sync/serialization
- **Fleet** — prywatne per gracz
- **Timetable** — prywatne per gracz
- **M5 Circulations** — prywatne
- **M9 TrainMovement** — **wspólne** (wszyscy widzą wszystkie pociągi)
- **Map + blocks** — **wspólne** (rezerwacje tras shared)

**Konsumenci:**
- Wszystkie powyższe muszą mieć "multiplayer-aware" API

---

### M11 Asystent gracza 📋
**Będzie używać:**
- **Wszystkie core systems** — guidance prowadzi przez każdy; capability registry wykonuje w nich akcje (Plan→Apply)
- **Capability registry w Core** + persona/UI w SharedUI; mózgi/plannery rejestrowane przez moduły (wzór SaveActionsHook/ISavable). Gotowe adaptery: `CirculationAutoGenerator` (Timetable), `CrewCirculationAutoGenerator` (Personnel)
- **SuggestionMemoryService + PlayerProgressService (SharedUI)** — advisor (snooze/dismiss) + poziom zaawansowania gracza
- **M13 Settings/GameCreator** — toggle onboarding (gated difficulty), persist per-save (jak SuggestionMemory)

**Konsumenty:** brak bezpośrednich. **Uwaga (przebudowa 2026-06-03):** w odróżnieniu od scripted tutorial asystent jest też **"nadawcą"** — wykonuje akcje gracza w innych systemach przez capability registry, nie tylko czyta stan.

---

### M12 Polish 📋 (4 sub-milestone'y)

**M12a Graphics performance** — wpływa na systemy wizualne (LOD modeli 3D, pooling/batching/culling). CPU/symulacja została wyciągnięta do M-Performance.

**M12b Audio** — używa events ze wszystkich systemów (SFX per event)

**M12c Visual** — dekoracje do Map (drogi, piesi, ambient), particles do M9 (dym, iskry)

**M12d Features:**
- **Livery** — dodaje `liveryId` do `CartItem` + FleetVehicleData, UI wyboru przy zakupie
- **Multi-depot** — rozbudowa Depot o druga/trzecia zajezdnia, expand grid
- **Achievements** — listener na events ze wszystkich systemów
- **Random events** — interwencje w M6 Economy + M7 Maintenance

---

### M13 Settings + Save/Load ✅
**Eksponuje:** `SettingsManager`, `SaveManager`, menu UI

**Używa:**
- **Wszystkie systemy** — serializacja stanu per system do save
- **Core** — `GameState` jako baza save
- **Input System** — rebinding

**Konsumenci:**
- Każdy system musi mieć metody `Serialize()` / `Deserialize()` dla save/load

---

### M14 Beta + Launch 📋
**Nie jest systemem** — to proces uruchomienia. Wpływa na:
- **Localization** — wszystkie UI teksty
- **Performance** — bazuje na M12a
- **Marketing** — screenshots, trailer
- **Bug fixing** — patches per system

---

## Tabela głównych przepływów danych

| Od | Do | Co | Kiedy |
|----|----|----|-------|
| Fleet | Timetable | Typy pojazdów, parametry | Przy tworzeniu rozkładu |
| Timetable | M5 | `TrainRun` lista | Przy tworzeniu obiegu |
| M5 | Fleet | Lock pojazdów | Przy zatwierdzeniu obiegu |
| M5 | Timetable | update `compositionMode = Concrete` | Przy zatwierdzeniu obiegu |
| Timetable | M9 | Aktywny `TrainRun` + trasa + bloki | Przy starcie kursu |
| M5 | M9 | Który pojazd wykonuje run | Przy spawn |
| Fleet | M9 | Prefab 3D, pantograf type | Przy spawn |
| Catenary | M9 | Czy jest sieć nad pozycją pociągu | Każdy tick |
| M9 | M6 | Event `OnStationReached` (pasażerowie) | Na każdej stacji |
| M9 | M7 | Event `OnKmTraveled` (degradacja) | Co N km |
| M7 | Fleet | Update `conditionPercent`, `components` | Po naprawie/przejeździe |
| M6 | Core | Update `GameState.Money` | Po każdej transakcji |
| M8 | M9 | "Maszynista dostępny dla tego kursu" | Przy starcie kursu |
| M8 | M7 | "Mechanik pracuje w warsztacie" | Przy naprawach |
| M8 | M6 | Wypłaty miesięczne | Co miesiąc gry |

---

## Zasady integracji (wnioski)

1. **Fleet jest hub'em dla taboru** — każdy system który operuje na pojazdach używa `FleetService`
2. **Timetable jest hub'em dla rozkładów** — M5, M6, M9 wszystkie referują do `TimetableService`
3. **M5 jest bridge'em Timetable ↔ Fleet** — bez tego Timetable nie wie o fizycznych pojazdach
4. **M9 jest konsumentem wszystkiego** — ruch pociągów potrzebuje danych ze wszystkich systemów
5. **M6 jest nad wszystkim** — ekonomia reaguje na events ze wszystkich innych systemów
6. **M7/M8 są "utilities"** — modyfikują Fleet/Personnel ale nie mają własnego gameplay loop
7. **M10 Multiplayer jest layer'em nad wszystkim** — wymaga że każdy system jest "sync-able"
8. **M11/M13 są "meta"** — uczą/zapisują stan wszystkich systemów

## Antywzorce do unikania

- **Cykliczne zależności:** np. M6 → M9 → M6. Używaj events lub intermediate services.
- **Bezpośrednie odwołania między "równoległymi" systemami:** M7 nie powinien wiedzieć o M6 — komunikacja przez Core/events.
- **Timetable wiedzący o konkretnych pojazdach:** to rola M5. Timetable zawiera tylko `compositionMode` i lookup przez M5.
- **M9 wiedzący o pasażerach:** to rola M6. M9 emituje events, M6 je konsumuje.
- **Fleet wiedzący o rozkładach:** tylko przez M5. Fleet ma `inCirculationId: int?` ale nie `assignedTimetableIds`.
