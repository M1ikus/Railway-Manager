#ifndef GAME_H
#define GAME_H

#include <memory>
#include <string>
#include <QObject>
#include <QTimer>

// Forward declarations
class GameState;
class DataLoader;
class SimulationEngine;
class SaveManager;
class EconomyManager;
class EventManager;

class Game : public QObject {
    Q_OBJECT
    
public:
    Game();
    ~Game();
    
    // Inicjalizacja i zamknięcie
    bool initialize();
    void shutdown();
    
    // Zarządzanie stanem gry
    void newGame(const std::string& scenarioId = "default");
    bool loadGame(const std::string& saveName);
    bool saveGame(const std::string& saveName);
    
    // Kontrola symulacji
    void startSimulation();
    void pauseSimulation();
    void stopSimulation();
    void setSimulationSpeed(float speed);
    
    // Gettery
    GameState* getGameState() const { return gameState.get(); }
    SimulationEngine* getSimulation() const { return simulation.get(); }
    DataLoader* getDataLoader() const { return dataLoader.get(); }
    EconomyManager* getEconomyManager() const { return economyManager.get(); }
    EventManager* getEventManager() const { return eventManager.get(); }
    
    bool isRunning() const { return running; }
    bool isPaused() const { return paused; }
    float getSimulationSpeed() const { return simulationSpeed; }
    
signals:
    // Sygnały Qt dla UI
    void gameStarted();
    void gamePaused();
    void gameResumed();
    void gameStopped();
    void gameLoaded();
    void gameSaved();
    void simulationTick(float deltaTime);
    void dateChanged(int year, int month, int day);
    void moneyChanged(double amount);
    void messageReceived(const QString& message, const QString& type);
    
private slots:
    void update();
    
private:
    // Komponenty gry
    std::unique_ptr<GameState> gameState;
    std::unique_ptr<DataLoader> dataLoader;
    std::unique_ptr<SimulationEngine> simulation;
    std::unique_ptr<SaveManager> saveManager;
    std::unique_ptr<EconomyManager> economyManager;
    std::unique_ptr<EventManager> eventManager;
    
    // Stan gry
    bool initialized = false;
    bool running = false;
    bool paused = false;
    float simulationSpeed = 1.0f;
    
    // Timer dla głównej pętli
    QTimer* gameTimer;
    
    // Czas
    std::chrono::steady_clock::time_point lastUpdateTime;
    float accumulator = 0.0f;
    const float FIXED_TIMESTEP = 1.0f / 60.0f; // 60 FPS
};

#endif // GAME_H