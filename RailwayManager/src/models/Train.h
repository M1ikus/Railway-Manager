#ifndef TRAIN_H
#define TRAIN_H

#include <string>
#include <vector>
#include <memory>
#include <chrono>

enum class TrainType {
    PASSENGER_LOCAL,      // Osobowy
    PASSENGER_REGIONAL,   // Regionalny
    PASSENGER_FAST,       // Pośpieszny
    PASSENGER_INTERCITY,  // InterCity
    PASSENGER_EXPRESS,    // Express/Pendolino
    FREIGHT,             // Towarowy
    MAINTENANCE          // Techniczny
};

enum class TrainStatus {
    AVAILABLE,           // Dostępny
    IN_SERVICE,          // W trasie
    MAINTENANCE,         // W naprawie
    CLEANING,            // Czyszczenie
    WAITING,             // Oczekuje na stacji
    BROKEN               // Uszkodzony
};

struct TrainUnit {
    std::string id;
    std::string series;      // Seria (np. "EP09", "EN57")
    std::string number;      // Numer jednostki
    int manufacturingYear;
    int seats;              // Liczba miejsc
    int standingRoom;       // Miejsca stojące
    float length;           // Długość w metrach
    float weight;           // Masa w tonach
    bool hasEngine;         // Czy ma napęd
    bool isElectric;        // Elektryczny czy spalinowy
    float maxSpeed;         // Prędkość maksymalna
    float power;            // Moc w kW
};

struct MaintenanceRecord {
    std::chrono::system_clock::time_point date;
    std::string type;       // "inspection", "repair", "overhaul"
    std::string description;
    float cost;
};

class Train {
public:
    Train(const std::string& id, const std::string& name);
    ~Train();
    
    // Podstawowe gettery
    const std::string& getId() const { return id; }
    const std::string& getName() const { return name; }
    TrainType getType() const { return type; }
    TrainStatus getStatus() const { return status; }
    
    // Podstawowe settery
    void setName(const std::string& name) { this->name = name; }
    void setType(TrainType type) { this->type = type; }
    void setStatus(TrainStatus status);
    
    // Skład pociągu
    void addUnit(const TrainUnit& unit);
    void removeUnit(const std::string& unitId);
    const std::vector<TrainUnit>& getUnits() const { return units; }
    TrainUnit* getUnit(const std::string& unitId);
    
    // Parametry całego składu
    int getTotalSeats() const;
    int getTotalStandingRoom() const;
    int getTotalCapacity() const { return getTotalSeats() + getTotalStandingRoom(); }
    float getTotalLength() const;
    float getTotalWeight() const;
    float getMaxSpeed() const;
    float getTotalPower() const;
    bool isElectric() const;
    
    // Pasażerowie
    int getCurrentPassengers() const { return currentPassengers; }
    void setCurrentPassengers(int count);
    void boardPassengers(int count);
    void alightPassengers(int count);
    float getOccupancyRate() const;
    bool isFull() const { return currentPassengers >= getTotalCapacity(); }
    
    // Pozycja i ruch
    void setCurrentPosition(double lat, double lon);
    double getCurrentLatitude() const { return currentLat; }
    double getCurrentLongitude() const { return currentLon; }
    void setCurrentSpeed(float speed) { currentSpeed = std::min(speed, getMaxSpeed()); }
    float getCurrentSpeed() const { return currentSpeed; }
    void setCurrentLine(const std::string& lineId) { currentLineId = lineId; }
    const std::string& getCurrentLine() const { return currentLineId; }
    void setCurrentStation(const std::string& stationId) { currentStationId = stationId; }
    const std::string& getCurrentStation() const { return currentStationId; }
    
    // Rozkład jazdy
    void setAssignedTimetable(const std::string& timetableId) { assignedTimetableId = timetableId; }
    const std::string& getAssignedTimetable() const { return assignedTimetableId; }
    void setDelay(int minutes) { delayMinutes = minutes; }
    int getDelay() const { return delayMinutes; }
    bool isDelayed() const { return delayMinutes > 0; }
    
    // Stan techniczny
    float getCondition() const { return condition; }
    void setCondition(float cond) { condition = std::max(0.0f, std::min(1.0f, cond)); }
    void deteriorate(float amount);
    void repair(float amount) { setCondition(condition + amount); }
    bool needsMaintenance() const { return condition < 0.4f || kmSinceLastMaintenance > 50000; }
    
    float getCleanliness() const { return cleanliness; }
    void setCleanliness(float clean) { cleanliness = std::max(0.0f, std::min(1.0f, clean)); }
    void clean() { cleanliness = 1.0f; }
    bool needsCleaning() const { return cleanliness < 0.5f; }
    
    float getFuelLevel() const { return fuelLevel; }
    void setFuelLevel(float fuel) { fuelLevel = std::max(0.0f, std::min(1.0f, fuel)); }
    void consumeFuel(float amount) { fuelLevel = std::max(0.0f, fuelLevel - amount); }
    void refuel() { fuelLevel = 1.0f; }
    
    // Przebieg
    float getTotalKilometers() const { return totalKm; }
    void addKilometers(float km);
    float getKmSinceLastMaintenance() const { return kmSinceLastMaintenance; }
    void resetMaintenanceKm() { kmSinceLastMaintenance = 0.0f; }
    
    // Personel
    void assignDriver(const std::string& driverId) { assignedDriverId = driverId; }
    void assignConductor(const std::string& conductorId) { assignedConductorId = conductorId; }
    const std::string& getAssignedDriver() const { return assignedDriverId; }
    const std::string& getAssignedConductor() const { return assignedConductorId; }
    bool hasRequiredCrew() const;
    
    // Historia utrzymania
    void addMaintenanceRecord(const MaintenanceRecord& record);
    const std::vector<MaintenanceRecord>& getMaintenanceHistory() const { return maintenanceHistory; }
    
    // Finanse
    float getPurchasePrice() const { return purchasePrice; }
    void setPurchasePrice(float price) { purchasePrice = price; }
    float getCurrentValue() const;
    float getDailyOperatingCost() const;
    float getMaintenanceCost() const;
    
    // Status
    bool isAvailable() const { return status == TrainStatus::AVAILABLE; }
    bool isOperational() const;
    bool canDepart() const;
    
private:
    // Podstawowe dane
    std::string id;
    std::string name;
    TrainType type;
    TrainStatus status;
    
    // Skład
    std::vector<TrainUnit> units;
    
    // Pasażerowie
    int currentPassengers = 0;
    
    // Pozycja i ruch
    double currentLat = 0.0;
    double currentLon = 0.0;
    float currentSpeed = 0.0f;
    std::string currentLineId;
    std::string currentStationId;
    
    // Rozkład
    std::string assignedTimetableId;
    int delayMinutes = 0;
    
    // Stan techniczny
    float condition = 1.0f;      // 0.0 - 1.0
    float cleanliness = 1.0f;    // 0.0 - 1.0
    float fuelLevel = 1.0f;      // 0.0 - 1.0 (dla spalinowych)
    
    // Przebieg
    float totalKm = 0.0f;
    float kmSinceLastMaintenance = 0.0f;
    
    // Personel
    std::string assignedDriverId;
    std::string assignedConductorId;
    
    // Historia i finanse
    std::vector<MaintenanceRecord> maintenanceHistory;
    float purchasePrice = 0.0f;
    std::chrono::system_clock::time_point purchaseDate;
};

#endif // TRAIN_H