#ifndef LINE_H
#define LINE_H

#include <string>
#include <vector>
#include <memory>

enum class LineType {
    MAIN,           // Magistrala
    REGIONAL,       // Regionalna
    LOCAL,          // Lokalna
    INDUSTRIAL,     // Przemysłowa/bocznica
    HIGH_SPEED      // Kolej dużych prędkości
};

enum class LineStatus {
    OPERATIONAL,    // W pełni operacyjna
    PARTIAL,        // Częściowo operacyjna
    MAINTENANCE,    // W remoncie
    CLOSED,         // Zamknięta
    BLOCKED         // Zablokowana (np. wypadek)
};

enum class ElectrificationType {
    NONE,           // Niezelektryfikowana
    DC_3000V,       // 3kV DC (standard polski)
    AC_25KV,        // 25kV AC
    DUAL            // Podwójny system
};

struct TrackSection {
    std::string id;
    std::string fromStationId;
    std::string toStationId;
    float length;               // Długość w km
    int maxSpeed;               // Maksymalna prędkość w km/h
    int tracks;                 // Liczba torów (1 lub 2)
    bool isElectrified;
    float gradient;             // Nachylenie w promilach
    float curvature;            // Krzywizna (promień łuku)
    LineStatus status;
    float condition;            // Stan toru 0.0-1.0
};

struct Signal {
    std::string id;
    float position;             // Pozycja na linii w km
    std::string type;           // "entry", "exit", "block", "warning"
    bool isActive;
    std::string currentAspect;  // "green", "yellow", "red"
};

struct LineStatistics {
    int totalTrainsToday = 0;
    int totalTrainsMonth = 0;
    int totalDelays = 0;
    float averageDelay = 0.0f;
    float totalTonnage = 0.0f;
    int incidents = 0;
};

class Line {
public:
    Line(const std::string& id, const std::string& number, const std::string& name);
    ~Line();
    
    // Podstawowe gettery
    const std::string& getId() const { return id; }
    const std::string& getNumber() const { return number; }
    const std::string& getName() const { return name; }
    LineType getType() const { return type; }
    LineStatus getStatus() const { return status; }
    ElectrificationType getElectrification() const { return electrification; }
    
    // Podstawowe settery
    void setName(const std::string& name) { this->name = name; }
    void setType(LineType type) { this->type = type; }
    void setStatus(LineStatus status) { this->status = status; }
    void setElectrification(ElectrificationType elec) { electrification = elec; }
    
    // Zarządzanie odcinkami
    void addSection(const TrackSection& section);
    void removeSection(const std::string& sectionId);
    TrackSection* getSection(const std::string& sectionId);
    const std::vector<TrackSection>& getSections() const { return sections; }
    TrackSection* getSectionBetween(const std::string& fromStation, const std::string& toStation);
    
    // Parametry linii
    float getTotalLength() const;
    int getMaxSpeed() const;
    int getMinSpeed() const;
    bool isFullyElectrified() const;
    bool isDoubleTrack() const;
    float getAverageCondition() const;
    
    // Stacje na linii
    std::vector<std::string> getStationIds() const;
    bool hasStation(const std::string& stationId) const;
    float getDistanceBetween(const std::string& station1, const std::string& station2) const;
    
    // Sygnalizacja
    void addSignal(const Signal& signal);
    void removeSignal(const std::string& signalId);
    Signal* getSignal(const std::string& signalId);
    const std::vector<Signal>& getSignals() const { return signals; }
    void updateSignalAspect(const std::string& signalId, const std::string& aspect);
    
    // Zajętość linii
    void occupySection(const std::string& sectionId, const std::string& trainId);
    void freeSection(const std::string& sectionId);
    bool isSectionOccupied(const std::string& sectionId) const;
    bool canTrainEnter(const std::string& sectionId) const;
    std::vector<std::string> getOccupiedSections() const;
    
    // Ograniczenia prędkości
    struct SpeedRestriction {
        std::string id;
        float fromKm;
        float toKm;
        int speedLimit;
        std::string reason;
        bool temporary;
    };
    
    void addSpeedRestriction(const SpeedRestriction& restriction);
    void removeSpeedRestriction(const std::string& restrictionId);
    const std::vector<SpeedRestriction>& getSpeedRestrictions() const { return speedRestrictions; }
    int getSpeedLimitAt(float position) const;
    
    // Utrzymanie
    void scheduleMaintenanceForSection(const std::string& sectionId);
    void completeMaintenance(const std::string& sectionId);
    std::vector<std::string> getSectionsNeedingMaintenance(float threshold = 0.5f) const;
    
    // Wydarzenia i blokady
    void blockSection(const std::string& sectionId, const std::string& reason);
    void unblockSection(const std::string& sectionId);
    bool isSectionBlocked(const std::string& sectionId) const;
    
    // Statystyki
    const LineStatistics& getStatistics() const { return statistics; }
    void updateStatistics(const LineStatistics& stats) { statistics = stats; }
    void recordTrainPassage(const std::string& trainId, int delayMinutes = 0);
    
    // Planowanie tras
    std::vector<std::string> findRoute(const std::string& fromStation, const std::string& toStation) const;
    float calculateTravelTime(const std::string& fromStation, const std::string& toStation, float trainMaxSpeed) const;
    
private:
    // Podstawowe dane
    std::string id;
    std::string number;         // Numer linii (np. "1", "131")
    std::string name;           // Nazwa (np. "Warszawa - Kraków")
    LineType type;
    LineStatus status;
    ElectrificationType electrification;
    
    // Infrastruktura
    std::vector<TrackSection> sections;
    std::vector<Signal> signals;
    std::vector<SpeedRestriction> speedRestrictions;
    
    // Zajętość
    std::unordered_map<std::string, std::string> sectionOccupancy; // sectionId -> trainId
    
    // Blokady
    std::unordered_map<std::string, std::string> blockedSections; // sectionId -> reason
    
    // Statystyki
    LineStatistics statistics;
};

#endif // LINE_H