#include "SimulationEngine.h"
#include "PassengerAI.h"
#include "TrainMovement.h"
#include "core/GameState.h"
#include "models/Train.h"
#include "models/Station.h"
#include "models/Line.h"
#include "models/Timetable.h"
#include "models/Personnel.h"
#include "utils/Logger.h"
#include <algorithm>
#include <random>

SimulationEngine::SimulationEngine(GameState* gameState) 
    : gameState(gameState) {
    
    passengerAI = std::make_unique<PassengerAI>(gameState);
    trainMovement = std::make_unique<TrainMovement>(gameState);
}

SimulationEngine::~SimulationEngine() {
}

bool SimulationEngine::initialize() {
    LOG_INFO("Inicjalizacja silnika symulacji");
    
    // Inicjalizuj komponenty
    if (!passengerAI->initialize()) {
        LOG_ERROR("Nie udało się zainicjalizować PassengerAI");
        return false;
    }
    
    if (!trainMovement->initialize()) {
        LOG_ERROR("Nie udało się zainicjalizować TrainMovement");
        return false;
    }
    
    // Ustaw początkowy stan
    reset();
    
    LOG_INFO("Silnik symulacji zainicjalizowany");
    return true;
}

void SimulationEngine::reset() {
    paused = false;
    timeScale = 1.0f;
    simulationTime = 0.0f;
    
    // Wyczyść kolejkę wydarzeń
    while (!eventQueue.empty()) {
        eventQueue.pop();
    }
    
    // Reset timerów
    passengerGenerationTimer = 0.0f;
    timetableUpdateTimer = 0.0f;
    statisticsUpdateTimer = 0.0f;
    
    // Reset statystyk
    statistics = Statistics{};
    
    // Aktualizuj aktywne jednostki
    updateActiveEntities();
}

void SimulationEngine::restoreState(GameState* state) {
    gameState = state;
    reset();
    
    // Odtwórz stan symulacji
    updateActiveEntities();
    
    // Zaplanuj wydarzenia dla aktywnych rozkładów
    createTimetableInstances();
}

void SimulationEngine::update(float deltaTime) {
    if (paused) return;
    
    // Zastosuj skalę czasu
    deltaTime *= timeScale;
    simulationTime += deltaTime;
    
    // Aktualizuj timery
    passengerGenerationTimer += deltaTime;
    timetableUpdateTimer += deltaTime;
    statisticsUpdateTimer += deltaTime;
    
    // Przetwarzaj zaplanowane wydarzenia
    processEvents();
    
    // Aktualizuj pociągi
    updateTrains(deltaTime);
    
    // Aktualizuj stacje
    updateStations(deltaTime);
    
    // Generuj pasażerów
    if (passengerGenerationTimer >= PASSENGER_GENERATION_INTERVAL) {
        generatePassengers();
        passengerGenerationTimer = 0.0f;
    }
    
    // Aktualizuj rozkłady
    if (timetableUpdateTimer >= TIMETABLE_UPDATE_INTERVAL) {
        updateTimetables();
        timetableUpdateTimer = 0.0f;
    }
    
    // Aktualizuj statystyki
    if (statisticsUpdateTimer >= STATISTICS_UPDATE_INTERVAL) {
        updateStatistics();
        statisticsUpdateTimer = 0.0f;
    }
    
    // Sprawdź kolizje i konflikty
    checkTrainCollisions();
}

void SimulationEngine::updateTrains(float deltaTime) {
    for (auto& train : activeTrains) {
        if (!train) continue;
        
        // Aktualizuj ruch pociągu
        trainMovement->updateTrain(train, deltaTime);
        
        // Sprawdź stan techniczny
        if (train->needsMaintenance()) {
            LOG_WARNING("Pociąg " + train->getName() + " wymaga konserwacji");
            
            // Zaplanuj konserwację
            SimulationEvent event;
            event.time = std::chrono::system_clock::now() + std::chrono::hours(1);
            event.type = "maintenance";
            event.entityId = train->getId();
            scheduleEvent(event);
        }
        
        // Sprawdź paliwo (dla spalinowych)
        if (!train->isElectric() && train->getFuelLevel() < 0.1f) {
            LOG_WARNING("Pociąg " + train->getName() + " ma niski poziom paliwa");
            
            // Zatrzymaj pociąg na najbliższej stacji
            emergencyStop(train->getId());
        }
        
        // Aktualizuj stan
        train->deteriorate(deltaTime * 0.00001f); // Zużycie
        
        // Sprawdź opóźnienia
        if (train->isDelayed()) {
            statistics.trainsDelayed++;
        }
    }
}

void SimulationEngine::updateStations(float deltaTime) {
    for (auto& station : activeStations) {
        if (!station) continue;
        
        // Aktualizuj pasażerów na stacji
        passengerAI->updateStation(station, deltaTime);
        
        // Przetwórz przyjazdy i odjazdy
        processArrivals();
        processDepartures();
        
        // Sprawdź przepełnienie
        if (station->getCurrentPassengers() > station->getMaxPassengers() * 0.9f) {
            handleStationCongestion(station);
        }
        
        // Aktualizuj stan stacji
        station->deteriorate(deltaTime * 0.000001f); // Wolniejsze zużycie niż pociągi
    }
}

void SimulationEngine::dispatchTrain(const std::string& trainId, 
                                   const std::string& timetableId) {
    auto train = gameState->getTrain(trainId);
    auto timetable = gameState->getTimetable(timetableId);
    
    if (!train || !timetable) {
        LOG_ERROR("Nie można wysłać pociągu - brak pociągu lub rozkładu");
        return;
    }
    
    // Sprawdź czy pociąg może odjechać
    if (!train->canDepart()) {
        LOG_WARNING("Pociąg " + train->getName() + " nie może odjechać");
        return;
    }
    
    // Ustaw rozkład
    train->setAssignedTimetable(timetableId);
    train->setStatus(TrainStatus::IN_SERVICE);
    
    // Pobierz pierwszy przystanek
    const auto& stops = timetable->getStops();
    if (!stops.empty()) {
        auto firstStation = gameState->getStation(stops[0].stationId);
        if (firstStation) {
            // Ustaw pozycję początkową
            train->setCurrentPosition(firstStation->getLatitude(), 
                                    firstStation->getLongitude());
            train->setCurrentStation(stops[0].stationId);
            
            // Zaplanuj odjazd
            SimulationEvent event;
            event.time = std::chrono::system_clock::now() + 
                        std::chrono::minutes(stops[0].departureTime);
            event.type = "departure";
            event.entityId = trainId;
            event.data = stops[0].stationId;
            scheduleEvent(event);
        }
    }
    
    LOG_INFO("Wysłano pociąg " + train->getName() + " według rozkładu " + 
             timetable->getName());
}

void SimulationEngine::stopTrain(const std::string& trainId) {
    auto train = gameState->getTrain(trainId);
    if (!train) return;
    
    train->setCurrentSpeed(0.0f);
    train->setStatus(TrainStatus::WAITING);
    
    LOG_INFO("Zatrzymano pociąg " + train->getName());
}

void SimulationEngine::emergencyStop(const std::string& trainId) {
    auto train = gameState->getTrain(trainId);
    if (!train) return;
    
    stopTrain(trainId);
    
    // Znajdź najbliższą stację
    auto stations = gameState->getAllStations();
    Station* nearestStation = nullptr;
    double minDistance = std::numeric_limits<double>::max();
    
    for (const auto& station : stations) {
        double lat1 = train->getCurrentLatitude();
        double lon1 = train->getCurrentLongitude();
        double lat2 = station->getLatitude();
        double lon2 = station->getLongitude();
        
        // Uproszczone obliczenie odległości
        double distance = sqrt(pow(lat2 - lat1, 2) + pow(lon2 - lon1, 2));
        
        if (distance < minDistance) {
            minDistance = distance;
            nearestStation = station.get();
        }
    }
    
    if (nearestStation) {
        // Przekieruj do najbliższej stacji
        train->setCurrentStation(nearestStation->getId());
        LOG_WARNING("Awaryjne zatrzymanie pociągu " + train->getName() + 
                   " na stacji " + nearestStation->getName());
    }
}

void SimulationEngine::generatePassengers() {
    static std::random_device rd;
    static std::mt19937 gen(rd());
    
    for (auto& station : activeStations) {
        if (!station) continue;
        
        // Oblicz popyt na podstawie typu i rozmiaru stacji
        int baseDemand = 10;
        
        switch (station->getType()) {
            case StationType::MAJOR:
                baseDemand = 100;
                break;
            case StationType::REGIONAL:
                baseDemand = 50;
                break;
            case StationType::LOCAL:
                baseDemand = 20;
                break;
            default:
                baseDemand = 10;
        }
        
        // Modyfikatory czasowe
        auto now = gameState->getCurrentDate();
        float timeModifier = 1.0f;
        
        // Godziny szczytu
        if ((now.hour >= 6 && now.hour <= 9) || (now.hour >= 16 && now.hour <= 19)) {
            timeModifier = 2.0f;
        } else if (now.hour >= 22 || now.hour <= 5) {
            timeModifier = 0.3f;
        }
        
        // Generuj pasażerów
        std::poisson_distribution<> dist(baseDemand * timeModifier);
        int newPassengers = dist(gen);
        
        station->addPassengers(newPassengers);
        
        // Generuj cele podróży dla nowych pasażerów
        passengerAI->generateDestinations(station, newPassengers);
    }
}

void SimulationEngine::boardPassengers(Train* train, Station* station) {
    if (!train || !station) return;
    
    // Sprawdź dostępną pojemność
    int availableSeats = train->getTotalCapacity() - train->getCurrentPassengers();
    if (availableSeats <= 0) return;
    
    // Pobierz pasażerów czekających na ten pociąg
    int waitingPassengers = passengerAI->getWaitingPassengers(station, train);
    int boardingPassengers = std::min(availableSeats, waitingPassengers);
    
    // Wsiadanie
    train->boardPassengers(boardingPassengers);
    station->removePassengers(boardingPassengers);
    
    statistics.passengersTransported += boardingPassengers;
    
    LOG_INFO("Na stacji " + station->getName() + " do pociągu " + 
             train->getName() + " wsiadło " + std::to_string(boardingPassengers) + 
             " pasażerów");
}

void SimulationEngine::alightPassengers(Train* train, Station* station) {
    if (!train || !station) return;
    
    // Pobierz liczbę pasażerów wysiadających na tej stacji
    int alightingPassengers = passengerAI->getAlightingPassengers(train, station);
    
    // Wysiadanie
    train->alightPassengers(alightingPassengers);
    station->addPassengers(alightingPassengers);
    
    LOG_INFO("Na stacji " + station->getName() + " z pociągu " + 
             train->getName() + " wysiadło " + std::to_string(alightingPassengers) + 
             " pasażerów");
}

void SimulationEngine::updateTimetables() {
    // Utwórz nowe instancje rozkładów na kolejny okres
    createTimetableInstances();
    
    // Sprawdź opóźnienia
    checkDelays();
    
    // Aktualizuj aktywne rozkłady
    for (const auto& timetable : gameState->getAllTimetables()) {
        if (!timetable->isActive()) continue;
        
        // Sprawdź konflikty
        for (const auto& other : gameState->getAllTimetables()) {
            if (other->getId() != timetable->getId() && 
                timetable->hasConflicts(other.get())) {
                LOG_WARNING("Konflikt rozkładów: " + timetable->getName() + 
                           " i " + other->getName());
            }
        }
    }
}

void SimulationEngine::createTimetableInstances() {
    auto currentDate = std::chrono::system_clock::now();
    
    for (const auto& timetable : gameState->getAllTimetables()) {
        if (!timetable->isActive()) continue;
        
        // Sprawdź czy trzeba utworzyć nowe instancje
        auto instances = timetable->getInstancesForDate(currentDate);
        if (instances.empty()) {
            timetable->createInstance(currentDate);
        }
    }
}

void SimulationEngine::checkDelays() {
    int totalDelays = 0;
    float totalDelayTime = 0.0f;
    
    for (const auto& train : activeTrains) {
        if (train->isDelayed()) {
            totalDelays++;
            totalDelayTime += train->getDelay();
        }
    }
    
    if (activeTrains.size() > 0) {
        statistics.averageDelay = totalDelayTime / activeTrains.size();
        statistics.punctualityRate = 1.0f - (float)totalDelays / activeTrains.size();
    }
}

void SimulationEngine::scheduleEvent(const SimulationEvent& event) {
    eventQueue.push(event);
}

void SimulationEngine::processEvents() {
    auto now = std::chrono::system_clock::now();
    
    while (!eventQueue.empty() && eventQueue.top().time <= now) {
        SimulationEvent event = eventQueue.top();
        eventQueue.pop();
        
        if (event.type == "departure") {
            auto train = gameState->getTrain(event.entityId);
            auto station = gameState->getStation(event.data);
            if (train && station) {
                processTrainDeparture(train.get(), station.get());
            }
            
        } else if (event.type == "arrival") {
            auto train = gameState->getTrain(event.entityId);
            auto station = gameState->getStation(event.data);
            if (train && station) {
                processTrainArrival(train.get(), station.get());
            }
            
        } else if (event.type == "maintenance") {
            auto train = gameState->getTrain(event.entityId);
            if (train) {
                train->setStatus(TrainStatus::MAINTENANCE);
                LOG_INFO("Pociąg " + train->getName() + " rozpoczął konserwację");
            }
            
        } else if (event.type == "breakdown") {
            auto train = gameState->getTrain(event.entityId);
            if (train) {
                handleTrainBreakdown(train.get());
            }
        }
    }
}

void SimulationEngine::processArrivals() {
    // Przetwarzane w processEvents przez wydarzenia "arrival"
}

void SimulationEngine::processDepartures() {
    // Przetwarzane w processEvents przez wydarzenia "departure"
}

void SimulationEngine::processTrainArrival(Train* train, Station* station) {
    LOG_INFO("Pociąg " + train->getName() + " przyjechał na stację " + 
             station->getName());
    
    // Zatrzymaj pociąg
    train->setCurrentSpeed(0.0f);
    train->setCurrentStation(station->getId());
    
    // Zajmij peron
    auto timetable = gameState->getTimetable(train->getAssignedTimetable());
    if (timetable) {
        auto stop = timetable->findStop(station->getId());
        if (stop) {
            station->occupyPlatform(stop->platform, train->getId());
        }
    }
    
    // Wysadzanie pasażerów
    alightPassengers(train, station);
    
    // Wsiadanie pasażerów
    boardPassengers(train, station);
    
    // Zaplanuj odjazd
    if (timetable) {
        auto stop = timetable->findStop(station->getId());
        if (stop && stop->departureTime > stop->arrivalTime) {
            SimulationEvent event;
            event.time = std::chrono::system_clock::now() + 
                        std::chrono::minutes(stop->departureTime - stop->arrivalTime);
            event.type = "departure";
            event.entityId = train->getId();
            event.data = station->getId();
            scheduleEvent(event);
        }
    }
}

void SimulationEngine::processTrainDeparture(Train* train, Station* station) {
    LOG_INFO("Pociąg " + train->getName() + " odjeżdża ze stacji " + 
             station->getName());
    
    // Zwolnij peron
    auto timetable = gameState->getTimetable(train->getAssignedTimetable());
    if (timetable) {
        auto stop = timetable->findStop(station->getId());
        if (stop) {
            station->freePlatform(stop->platform);
        }
    }
    
    // Znajdź następną stację
    if (timetable) {
        const auto& stops = timetable->getStops();
        int currentIndex = timetable->findStopIndex(station->getId());
        
        if (currentIndex >= 0 && currentIndex < static_cast<int>(stops.size()) - 1) {
            // Jest następna stacja
            const auto& nextStop = stops[currentIndex + 1];
            auto nextStation = gameState->getStation(nextStop.stationId);
            
            if (nextStation) {
                // Oblicz czas podróży
                int travelTime = nextStop.arrivalTime - stops[currentIndex].departureTime;
                
                // Zaplanuj przyjazd
                SimulationEvent event;
                event.time = std::chrono::system_clock::now() + 
                            std::chrono::minutes(travelTime);
                event.type = "arrival";
                event.entityId = train->getId();
                event.data = nextStop.stationId;
                scheduleEvent(event);
                
                // Rozpocznij ruch
                train->setStatus(TrainStatus::IN_SERVICE);
                
                // Oblicz optymalną prędkość
                float distance = station->calculateTicketPrice(nextStation.get(), "base") / 0.3f;
                float optimalSpeed = calculateOptimalSpeed(train.get(), distance);
                train->setCurrentSpeed(optimalSpeed);
            }
        } else {
            // Koniec trasy
            train->setStatus(TrainStatus::AVAILABLE);
            train->setAssignedTimetable("");
            LOG_INFO("Pociąg " + train->getName() + " zakończył trasę");
        }
    }
}

void SimulationEngine::handleTrainBreakdown(Train* train) {
    LOG_ERROR("Awaria pociągu " + train->getName());
    
    train->setStatus(TrainStatus::BROKEN);
    train->setCurrentSpeed(0.0f);
    
    // Odwołaj pozostałe kursy
    // TODO: Implementacja
    
    // Ewakuuj pasażerów
    train->setCurrentPassengers(0);
    
    // Zmniejsz reputację
    gameState->changeReputation(-10);
}

void SimulationEngine::handleStationCongestion(Station* station) {
    LOG_WARNING("Przepełnienie na stacji " + station->getName());
    
    // Zmniejsz zadowolenie pasażerów
    // TODO: Implementacja systemu zadowolenia
    
    // Możliwe opóźnienia
    for (auto& train : activeTrains) {
        if (train->getCurrentStation() == station->getId()) {
            train->setDelay(train->getDelay() + 5); // +5 minut opóźnienia
        }
    }
}

bool SimulationEngine::checkTrainCollisions() {
    // Uproszczone sprawdzanie kolizji
    for (size_t i = 0; i < activeTrains.size(); ++i) {
        for (size_t j = i + 1; j < activeTrains.size(); ++j) {
            Train* train1 = activeTrains[i];
            Train* train2 = activeTrains[j];
            
            if (!train1 || !train2) continue;
            
            // Sprawdź czy są na tej samej linii
            if (train1->getCurrentLine() == train2->getCurrentLine()) {
                // Sprawdź odległość
                double dist = sqrt(
                    pow(train1->getCurrentLatitude() - train2->getCurrentLatitude(), 2) +
                    pow(train1->getCurrentLongitude() - train2->getCurrentLongitude(), 2)
                );
                
                // Minimalna bezpieczna odległość (w stopniach, około 1km)
                const double MIN_SAFE_DISTANCE = 0.01;
                
                if (dist < MIN_SAFE_DISTANCE) {
                    LOG_ERROR("UWAGA! Ryzyko kolizji między pociągami " + 
                             train1->getName() + " i " + train2->getName());
                    
                    // Zatrzymaj pociągi
                    emergencyStop(train1->getId());
                    emergencyStop(train2->getId());
                    
                    return true;
                }
            }
        }
    }
    
    return false;
}

bool SimulationEngine::checkPlatformConflicts(Station* station) {
    if (!station) return false;
    
    int conflicts = 0;
    const auto& platforms = station->getPlatforms();
    
    for (const auto& platform : platforms) {
        if (platform.occupied) {
            // Sprawdź czy pociąg nadal jest na peronie
            auto train = gameState->getTrain(platform.trainId);
            if (!train || train->getCurrentStation() != station->getId()) {
                // Pociąg już odjechał, zwolnij peron
                const_cast<Station*>(station)->freePlatform(platform.number);
            }
        }
    }
    
    return conflicts > 0;
}

bool SimulationEngine::checkLineCapacity(Line* line) {
    if (!line) return false;
    
    // Policz pociągi na linii
    int trainsOnLine = 0;
    for (const auto& train : activeTrains) {
        if (train->getCurrentLine() == line->getId()) {
            trainsOnLine++;
        }
    }
    
    // Sprawdź pojemność (uproszczone - max 10 pociągów na linię)
    const int MAX_TRAINS_PER_LINE = 10;
    return trainsOnLine < MAX_TRAINS_PER_LINE;
}

void SimulationEngine::optimizeTrainSpeed(Train* train) {
    if (!train) return;
    
    // Pobierz następną stację
    auto timetable = gameState->getTimetable(train->getAssignedTimetable());
    if (!timetable) return;
    
    auto currentStation = gameState->getStation(train->getCurrentStation());
    if (!currentStation) return;
    
    int currentIndex = timetable->findStopIndex(currentStation->getId());
    if (currentIndex < 0 || currentIndex >= static_cast<int>(timetable->getStops().size()) - 1) {
        return;
    }
    
    const auto& nextStop = timetable->getStops()[currentIndex + 1];
    auto nextStation = gameState->getStation(nextStop.stationId);
    if (!nextStation) return;
    
    // Oblicz odległość
    float distance = currentStation->calculateTicketPrice(nextStation.get(), "base") / 0.3f;
    
    // Oblicz optymalną prędkość
    float optimalSpeed = calculateOptimalSpeed(train.get(), distance);
    train->setCurrentSpeed(optimalSpeed);
}

float SimulationEngine::calculateOptimalSpeed(Train* train, float distanceToNext) {
    if (!train) return 0.0f;
    
    // Podstawowa prędkość to 80% maksymalnej
    float baseSpeed = train->getMaxSpeed() * 0.8f;
    
    // Modyfikatory
    float weatherModifier = 1.0f; // TODO: System pogody
    float conditionModifier = 0.5f + train->getCondition() * 0.5f;
    float loadModifier = 1.0f - train->getOccupancyRate() * 0.1f;
    
    // Jeśli pociąg jest opóźniony, przyspiesz
    if (train->isDelayed()) {
        baseSpeed *= 1.1f;
    }
    
    float finalSpeed = baseSpeed * weatherModifier * conditionModifier * loadModifier;
    
    // Nie przekraczaj maksymalnej prędkości
    return std::min(finalSpeed, train->getMaxSpeed());
}

void SimulationEngine::updateActiveEntities() {
    activeTrains.clear();
    activeStations.clear();
    activeTimetables.clear();
    
    if (!gameState) return;
    
    // Pobierz aktywne pociągi
    for (const auto& train : gameState->getAllTrains()) {
        if (train->getStatus() == TrainStatus::IN_SERVICE ||
            train->getStatus() == TrainStatus::WAITING) {
            activeTrains.push_back(train.get());
        }
    }
    
    // Pobierz aktywne stacje
    for (const auto& station : gameState->getAllStations()) {
        activeStations.push_back(station.get());
    }
    
    // Pobierz aktywne rozkłady
    for (const auto& timetable : gameState->getAllTimetables()) {
        if (timetable->isActive()) {
            activeTimetables.push_back(timetable.get());
        }
    }
}

void SimulationEngine::updateStatistics() {
    statistics.trainsRunning = 0;
    statistics.trainsDelayed = 0;
    statistics.passengersWaiting = 0;
    
    // Policz pociągi
    for (const auto& train : activeTrains) {
        if (train->getStatus() == TrainStatus::IN_SERVICE) {
            statistics.trainsRunning++;
            if (train->isDelayed()) {
                statistics.trainsDelayed++;
            }
        }
    }
    
    // Policz pasażerów
    for (const auto& station : activeStations) {
        statistics.passengersWaiting += station->getCurrentPassengers();
    }
    
    // Oblicz wykorzystanie systemu
    int totalTrains = gameState->getAllTrains().size();
    if (totalTrains > 0) {
        statistics.systemUtilization = static_cast<float>(statistics.trainsRunning) / totalTrains;
    }
}