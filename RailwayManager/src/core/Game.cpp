#include "Game.h"
#include "GameState.h"
#include "SaveManager.h"
#include "data/DataLoader.h"
#include "simulation/SimulationEngine.h"
#include "simulation/EconomyManager.h"
#include "simulation/EventManager.h"
#include "utils/Logger.h"
#include <chrono>

Game::Game() : QObject(nullptr) {
    gameTimer = new QTimer(this);
    connect(gameTimer, &QTimer::timeout, this, &Game::update);
}

Game::~Game() {
    shutdown();
}

bool Game::initialize() {
    LOG_INFO("Inicjalizacja gry...");
    
    try {
        // Stwórz komponenty
        gameState = std::make_unique<GameState>();
        dataLoader = std::make_unique<DataLoader>();
        saveManager = std::make_unique<SaveManager>();
        economyManager = std::make_unique<EconomyManager>();
        eventManager = std::make_unique<EventManager>();
        simulation = std::make_unique<SimulationEngine>(gameState.get());
        
        // Wczytaj dane bazowe
        if (!dataLoader->loadBaseData()) {
            LOG_ERROR("Nie udało się wczytać danych bazowych");
            return false;
        }
        
        // Inicjalizuj komponenty
        economyManager->initialize(gameState.get());
        eventManager->initialize(gameState.get(), dataLoader.get());
        simulation->initialize();
        
        // Połącz sygnały
        connect(eventManager.get(), &EventManager::eventTriggered,
                [this](const QString& message, const QString& type) {
                    emit messageReceived(message, type);
                });
        
        initialized = true;
        LOG_INFO("Gra zainicjalizowana pomyślnie");
        return true;
        
    } catch (const std::exception& e) {
        LOG_ERROR("Błąd podczas inicjalizacji: " + std::string(e.what()));
        return false;
    }
}

void Game::shutdown() {
    if (running) {
        stopSimulation();
    }
    
    LOG_INFO("Zamykanie gry...");
    
    simulation.reset();
    eventManager.reset();
    economyManager.reset();
    saveManager.reset();
    dataLoader.reset();
    gameState.reset();
    
    initialized = false;
}

void Game::newGame(const std::string& scenarioId) {
    LOG_INFO("Rozpoczynanie nowej gry ze scenariuszem: " + scenarioId);
    
    // Zatrzymaj aktualną symulację
    if (running) {
        stopSimulation();
    }
    
    // Wyczyść stan gry
    gameState->reset();
    
    // Wczytaj scenariusz
    if (!dataLoader->loadScenario(scenarioId, gameState.get())) {
        LOG_ERROR("Nie udało się wczytać scenariusza: " + scenarioId);
        emit messageReceived("Błąd podczas ładowania scenariusza!", "error");
        return;
    }
    
    // Zainicjuj komponenty dla nowej gry
    economyManager->reset();
    eventManager->reset();
    simulation->reset();
    
    // Ustaw datę startową
    gameState->setCurrentDate(2024, 1, 1);
    
    emit gameStarted();
    emit dateChanged(2024, 1, 1);
    emit moneyChanged(gameState->getMoney());
    emit messageReceived("Nowa gra rozpoczęta!", "info");
    
    LOG_INFO("Nowa gra rozpoczęta pomyślnie");
}

bool Game::loadGame(const std::string& saveName) {
    LOG_INFO("Wczytywanie zapisu: " + saveName);
    
    // Zatrzymaj aktualną symulację
    if (running) {
        stopSimulation();
    }
    
    // Wczytaj stan gry
    if (!saveManager->loadGame(saveName, gameState.get())) {
        LOG_ERROR("Nie udało się wczytać zapisu: " + saveName);
        emit messageReceived("Błąd podczas wczytywania zapisu!", "error");
        return false;
    }
    
    // Odtwórz stan komponentów
    economyManager->restoreState(gameState.get());
    eventManager->restoreState(gameState.get());
    simulation->restoreState(gameState.get());
    
    emit gameLoaded();
    auto date = gameState->getCurrentDate();
    emit dateChanged(date.year, date.month, date.day);
    emit moneyChanged(gameState->getMoney());
    emit messageReceived("Gra wczytana pomyślnie!", "info");
    
    LOG_INFO("Gra wczytana pomyślnie");
    return true;
}

bool Game::saveGame(const std::string& saveName) {
    LOG_INFO("Zapisywanie gry: " + saveName);
    
    // Zapisz aktualny stan
    if (!saveManager->saveGame(saveName, gameState.get())) {
        LOG_ERROR("Nie udało się zapisać gry: " + saveName);
        emit messageReceived("Błąd podczas zapisywania gry!", "error");
        return false;
    }
    
    emit gameSaved();
    emit messageReceived("Gra zapisana pomyślnie!", "info");
    
    LOG_INFO("Gra zapisana pomyślnie");
    return true;
}

void Game::startSimulation() {
    if (!initialized || running) {
        return;
    }
    
    LOG_INFO("Uruchamianie symulacji");
    
    running = true;
    paused = false;
    lastUpdateTime = std::chrono::steady_clock::now();
    
    // Uruchom timer (60 FPS)
    gameTimer->start(16);
    
    emit gameResumed();
}

void Game::pauseSimulation() {
    if (!running || paused) {
        return;
    }
    
    LOG_INFO("Pauzowanie symulacji");
    
    paused = true;
    gameTimer->stop();
    
    emit gamePaused();
}

void Game::stopSimulation() {
    if (!running) {
        return;
    }
    
    LOG_INFO("Zatrzymywanie symulacji");
    
    running = false;
    paused = false;
    gameTimer->stop();
    
    emit gameStopped();
}

void Game::setSimulationSpeed(float speed) {
    simulationSpeed = std::max(0.0f, std::min(speed, 10.0f));
    LOG_INFO("Prędkość symulacji ustawiona na: " + std::to_string(simulationSpeed));
}

void Game::update() {
    if (!running || paused) {
        return;
    }
    
    // Oblicz delta time
    auto currentTime = std::chrono::steady_clock::now();
    float deltaTime = std::chrono::duration<float>(currentTime - lastUpdateTime).count();
    lastUpdateTime = currentTime;
    
    // Zastosuj prędkość symulacji
    deltaTime *= simulationSpeed;
    
    // Fixed timestep z interpolacją
    accumulator += deltaTime;
    
    while (accumulator >= FIXED_TIMESTEP) {
        // Aktualizuj symulację
        simulation->update(FIXED_TIMESTEP);
        
        // Aktualizuj ekonomię
        economyManager->update(FIXED_TIMESTEP);
        
        // Sprawdź eventy
        eventManager->update(FIXED_TIMESTEP);
        
        // Aktualizuj czas gry
        gameState->updateTime(FIXED_TIMESTEP);
        
        // Sprawdź czy zmienił się dzień
        static int lastDay = -1;
        auto date = gameState->getCurrentDate();
        if (date.day != lastDay) {
            lastDay = date.day;
            emit dateChanged(date.year, date.month, date.day);
            
            // Codzienne obliczenia
            economyManager->dailyUpdate();
            eventManager->checkDailyEvents();
        }
        
        accumulator -= FIXED_TIMESTEP;
    }
    
    // Emituj sygnał aktualizacji dla UI
    emit simulationTick(deltaTime);
    
    // Sprawdź zmiany w finansach
    static double lastMoney = -1;
    double currentMoney = gameState->getMoney();
    if (std::abs(currentMoney - lastMoney) > 0.01) {
        lastMoney = currentMoney;
        emit moneyChanged(currentMoney);
    }
}