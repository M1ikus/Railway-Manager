#ifndef STATION_H
#define STATION_H

#include <string>
#include <vector>
#include <memory>

enum class StationType {
    MAJOR,      // Duża stacja (np. Warszawa Centralna)
    REGIONAL,   // Stacja regionalna
    LOCAL,      // Stacja lokalna
    TECHNICAL,  // Stacja techniczna/bocznica
    FREIGHT     // Stacja towarowa
};

enum class StationSize {
    SMALL,      // 1-2 perony
    MEDIUM,     // 3-5 peronów
    LARGE,      // 6-10 peronów
    HUGE        // 10+ peronów
};

struct Platform {
    int number;
    int length;          // Długość w metrach
    bool hasRoof;        // Czy ma zadaszenie
    bool isElectrified;  // Czy ma trakcję elektryczną
    bool occupied;       // Czy zajęty przez pociąg
    std::string trainId; // ID pociągu na peronie
};

struct StationFacilities {
    bool hasTicketOffice;
    bool hasWaitingRoom;
    bool hasRestaurant;
    bool hasParking;
    bool hasToilets;
    bool hasBikeRacks;
    bool hasElevators;
    bool isAccessible;   // Dla niepełnosprawnych
    int parkingSpaces;
};

class Station {
public:
    Station(const std::string& id, const std::string& name);
    ~Station();
    
    // Gettery podstawowe
    const std::string& getId() const { return id; }
    const std::string& getName() const { return name; }
    const std::string& getCode() const { return code; }
    StationType getType() const { return type; }
    StationSize getSize() const { return size; }
    
    // Settery podstawowe
    void setName(const std::string& name) { this->name = name; }
    void setCode(const std::string& code) { this->code = code; }
    void setType(StationType type) { this->type = type; }
    void setSize(StationSize size) { this->size = size; }
    
    // Lokalizacja
    void setCoordinates(double lat, double lon);
    double getLatitude() const { return latitude; }
    double getLongitude() const { return longitude; }
    
    // Region/województwo
    void setRegion(const std::string& region) { this->region = region; }
    const std::string& getRegion() const { return region; }
    
    // Zarządzanie peronami
    void addPlatform(const Platform& platform);
    void removePlatform(int platformNumber);
    Platform* getPlatform(int platformNumber);
    const std::vector<Platform>& getPlatforms() const { return platforms; }
    int getAvailablePlatform(int requiredLength) const;
    void occupyPlatform(int platformNumber, const std::string& trainId);
    void freePlatform(int platformNumber);
    
    // Pojemność
    int getMaxTrains() const { return platforms.size(); }
    int getCurrentTrains() const;
    bool hasCapacity() const { return getCurrentTrains() < getMaxTrains(); }
    
    // Pasażerowie
    int getCurrentPassengers() const { return currentPassengers; }
    int getMaxPassengers() const { return maxPassengers; }
    void setMaxPassengers(int max) { maxPassengers = max; }
    void addPassengers(int count);
    void removePassengers(int count);
    
    // Udogodnienia
    const StationFacilities& getFacilities() const { return facilities; }
    void updateFacilities(const StationFacilities& newFacilities) { facilities = newFacilities; }
    
    // Połączenia
    void addConnection(const std::string& lineId);
    void removeConnection(const std::string& lineId);
    const std::vector<std::string>& getConnections() const { return connectedLines; }
    bool hasConnection(const std::string& lineId) const;
    
    // Stan i utrzymanie
    float getCondition() const { return condition; }
    void setCondition(float cond) { condition = std::max(0.0f, std::min(1.0f, cond)); }
    void deteriorate(float amount) { setCondition(condition - amount); }
    void repair(float amount) { setCondition(condition + amount); }
    bool needsMaintenance() const { return condition < 0.5f; }
    
    // Statystyki
    struct Statistics {
        int totalPassengersToday = 0;
        int totalPassengersMonth = 0;
        int totalPassengersYear = 0;
        int totalTrainsToday = 0;
        int totalTrainsMonth = 0;
        float averageDelay = 0.0f;
        float satisfaction = 0.0f;
    };
    
    const Statistics& getStatistics() const { return stats; }
    void updateStatistics(const Statistics& newStats) { stats = newStats; }
    
    // Operacje
    bool canAcceptTrain(int trainLength) const;
    float calculateTicketPrice(const Station* destination, const std::string& trainType) const;
    
private:
    // Podstawowe dane
    std::string id;
    std::string name;
    std::string code;        // Kod stacji (np. "WAW" dla Warszawy)
    StationType type;
    StationSize size;
    
    // Lokalizacja
    double latitude = 0.0;
    double longitude = 0.0;
    std::string region;
    
    // Infrastruktura
    std::vector<Platform> platforms;
    StationFacilities facilities;
    float condition = 1.0f;  // 0.0 - 1.0
    
    // Pojemność
    int maxPassengers = 1000;
    int currentPassengers = 0;
    
    // Połączenia
    std::vector<std::string> connectedLines;
    
    // Statystyki
    Statistics stats;
};

#endif // STATION_H