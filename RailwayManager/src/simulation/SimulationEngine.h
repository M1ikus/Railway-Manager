#ifndef SIMULATIONENGINE_H
#define SIMULATIONENGINE_H

#include <memory>
#include <vector>
#include <queue>
#include <chrono>

// Forward declarations
class GameState;
class Train;
class Station;
class Line;
class Timetable;
class PassengerAI;
class TrainMovement;
class EventManager;

struct SimulationEvent {
    std::chrono::system_clock::time_point time;
    std::string type;
    std::string entityId;
    std::string data;
    
    bool operator>(const SimulationEvent& other) const {
        return time > other.time;
    }
};

class SimulationEngine {
public:
    SimulationEngine(GameState* gameState);
    ~SimulationEngine();
    
    // Inicjalizacja i reset
    bool initialize();
    void reset();
    void restoreState(GameState* state);
    
    // Główna pętla symulacji
    void update(float deltaTime);
    
    // Kontrola symulacji
    void pause() { paused = true; }
    void resume() { paused = false; }
    bool isPaused() const { return paused; }
    
    void setTimeScale(float scale) { timeScale = scale; }
    float getTimeScale() const { return timeScale; }
    
    // Zarządzanie pociągami
    void updateTrains(float deltaTime);
    void dispatchTrain(const std::string& trainId, const std::string& timetableId);
    void stopTrain(const std::string& trainId);
    void emergencyStop(const std::string& trainId);
    
    // Zarządzanie stacjami
    void updateStations(float deltaTime);
    void processArrivals();
    void processDepartures();
    
    // Zarządzanie pasażerami
    void updatePassengers(float deltaTime);
    void generatePassengers();
    void boardPassengers(Train* train, Station* station);
    void alightPassengers(Train* train, Station* station);
    
    // Zarządzanie rozkładami
    void updateTimetables();
    void createTimetableInstances();
    void checkDelays();
    
    // Wydarzenia symulacji
    void scheduleEvent(const SimulationEvent& event);
    void processEvents();
    
    // Statystyki
    struct Statistics {
        int trainsRunning = 0;
        int trainsDelayed = 0;
        int passengersTransported = 0;
        int passengersWaiting = 0;
        float averageDelay = 0.0f;
        float punctualityRate = 0.0f;
        float systemUtilization = 0.0f;
    };
    
    const Statistics& getStatistics() const { return statistics; }
    
    // Sprawdzanie kolizji i konfliktów
    bool checkTrainCollisions();
    bool checkPlatformConflicts(Station* station);
    bool checkLineCapacity(Line* line);
    
    // Optymalizacja
    void optimizeTrainSpeed(Train* train);
    void optimizeStationOperations(Station* station);
    void balancePassengerLoad();
    
private:
    GameState* gameState;
    
    // Komponenty symulacji
    std::unique_ptr<PassengerAI> passengerAI;
    std::unique_ptr<TrainMovement> trainMovement;
    
    // Stan symulacji
    bool paused = false;
    float timeScale = 1.0f;
    float simulationTime = 0.0f;
    
    // Kolejka wydarzeń
    std::priority_queue<SimulationEvent, 
                       std::vector<SimulationEvent>, 
                       std::greater<SimulationEvent>> eventQueue;
    
    // Cache dla wydajności
    std::vector<Train*> activeTrains;
    std::vector<Station*> activeStations;
    std::vector<Timetable*> activeTimetables;
    
    // Statystyki
    Statistics statistics;
    
    // Timery
    float passengerGenerationTimer = 0.0f;
    float timetableUpdateTimer = 0.0f;
    float statisticsUpdateTimer = 0.0f;
    
    // Stałe czasowe
    const float PASSENGER_GENERATION_INTERVAL = 60.0f; // Co minutę
    const float TIMETABLE_UPDATE_INTERVAL = 300.0f;    // Co 5 minut
    const float STATISTICS_UPDATE_INTERVAL = 10.0f;    // Co 10 sekund
    
    // Pomocnicze metody
    void updateActiveEntities();
    void updateStatistics();
    void processTrainArrival(Train* train, Station* station);
    void processTrainDeparture(Train* train, Station* station);
    void handleTrainBreakdown(Train* train);
    void handleStationCongestion(Station* station);
    float calculateOptimalSpeed(Train* train, float distanceToNext);
    int calculatePassengerDemand(Station* from, Station* to);
};

#endif // SIMULATIONENGINE_H