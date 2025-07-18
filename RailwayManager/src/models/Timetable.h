#ifndef TIMETABLE_H
#define TIMETABLE_H

#include <string>
#include <vector>
#include <chrono>

struct TimetableStop {
    std::string stationId;
    int arrivalTime;      // Minuty od północy
    int departureTime;    // Minuty od północy  
    int platform;         // Numer peronu
    bool optional;        // Czy przystanek jest opcjonalny
    int dwellTime;        // Czas postoju w minutach
};

struct TimetableInstance {
    std::string id;
    std::chrono::system_clock::time_point date;
    int actualDepartureTime;  // Rzeczywisty czas odjazdu
    int delay;                // Opóźnienie w minutach
    bool cancelled;           // Czy kurs został odwołany
    std::string trainId;      // Przypisany pociąg
    std::string driverId;     // Przypisany maszynista
    std::string conductorId;  // Przypisany konduktor
};

enum class TimetableType {
    REGULAR,         // Regularny rozkład
    SEASONAL,        // Sezonowy (np. wakacyjny)
    SPECIAL,         // Specjalny (np. świąteczny)
    TEMPORARY        // Tymczasowy
};

enum class TimetableDays {
    NONE = 0,
    MONDAY = 1 << 0,
    TUESDAY = 1 << 1,
    WEDNESDAY = 1 << 2,
    THURSDAY = 1 << 3,
    FRIDAY = 1 << 4,
    SATURDAY = 1 << 5,
    SUNDAY = 1 << 6,
    WEEKDAYS = MONDAY | TUESDAY | WEDNESDAY | THURSDAY | FRIDAY,
    WEEKEND = SATURDAY | SUNDAY,
    EVERYDAY = WEEKDAYS | WEEKEND
};

inline TimetableDays operator|(TimetableDays a, TimetableDays b) {
    return static_cast<TimetableDays>(static_cast<int>(a) | static_cast<int>(b));
}

inline TimetableDays operator&(TimetableDays a, TimetableDays b) {
    return static_cast<TimetableDays>(static_cast<int>(a) & static_cast<int>(b));
}

class Timetable {
public:
    Timetable(const std::string& id, const std::string& name);
    ~Timetable();
    
    // Podstawowe gettery
    const std::string& getId() const { return id; }
    const std::string& getName() const { return name; }
    const std::string& getTrainId() const { return trainId; }
    const std::string& getLineId() const { return lineId; }
    TimetableType getType() const { return type; }
    
    // Podstawowe settery
    void setName(const std::string& n) { name = n; }
    void setTrainId(const std::string& id) { trainId = id; }
    void setLineId(const std::string& id) { lineId = id; }
    void setType(TimetableType t) { type = t; }
    
    // Status
    bool isActive() const { return active; }
    void setActive(bool a) { active = a; }
    
    // Dni kursowania
    TimetableDays getRunningDays() const { return runningDays; }
    void setRunningDays(TimetableDays days) { runningDays = days; }
    bool runsOnDay(int dayOfWeek) const;
    
    // Okres ważności
    void setValidFrom(const std::chrono::system_clock::time_point& date) { validFrom = date; }
    void setValidTo(const std::chrono::system_clock::time_point& date) { validTo = date; }
    std::chrono::system_clock::time_point getValidFrom() const { return validFrom; }
    std::chrono::system_clock::time_point getValidTo() const { return validTo; }
    bool isValidOn(const std::chrono::system_clock::time_point& date) const;
    
    // Zarządzanie przystankami
    void addStop(const TimetableStop& stop);
    void insertStop(size_t index, const TimetableStop& stop);
    void removeStop(size_t index);
    void updateStop(size_t index, const TimetableStop& stop);
    const std::vector<TimetableStop>& getStops() const { return stops; }
    TimetableStop* getStop(size_t index);
    size_t getStopCount() const { return stops.size(); }
    
    // Wyszukiwanie przystanków
    int findStopIndex(const std::string& stationId) const;
    TimetableStop* findStop(const std::string& stationId);
    std::vector<TimetableStop> getStopsBetween(const std::string& fromStation, 
                                               const std::string& toStation) const;
    
    // Czasy
    int getFirstDepartureTime() const;
    int getLastArrivalTime() const;
    int getTotalTravelTime() const;
    int getTravelTimeBetween(const std::string& fromStation, const std::string& toStation) const;
    
    // Częstotliwość
    int getFrequency() const { return frequency; }
    void setFrequency(int freq) { frequency = freq; }
    bool isMultipleRuns() const { return frequency > 0; }
    std::vector<int> getDepartureTimes() const;
    
    // Instancje rozkładu
    void createInstance(const std::chrono::system_clock::time_point& date);
    void cancelInstance(const std::string& instanceId);
    const std::vector<TimetableInstance>& getInstances() const { return instances; }
    TimetableInstance* getInstance(const std::string& instanceId);
    std::vector<TimetableInstance> getInstancesForDate(
        const std::chrono::system_clock::time_point& date) const;
    
    // Walidacja
    bool validate() const;
    std::vector<std::string> getValidationErrors() const;
    bool hasConflicts(const Timetable* other) const;
    
    // Statystyki
    struct Statistics {
        int totalRuns = 0;
        int cancelledRuns = 0;
        int delayedRuns = 0;
        float averageDelay = 0.0f;
        float punctualityRate = 0.0f;
        int totalPassengers = 0;
    };
    
    const Statistics& getStatistics() const { return statistics; }
    void updateStatistics(const Statistics& stats) { statistics = stats; }
    
    // Operacje
    void shiftTimes(int minutes);  // Przesuń wszystkie czasy
    void optimizeDwellTimes();     // Optymalizuj czasy postojów
    Timetable* duplicate() const;  // Stwórz kopię rozkładu
    
    // Eksport/Import
    std::string exportToCSV() const;
    bool importFromCSV(const std::string& csvData);
    
private:
    // Podstawowe dane
    std::string id;
    std::string name;
    std::string trainId;
    std::string lineId;
    TimetableType type;
    bool active;
    
    // Przystanki
    std::vector<TimetableStop> stops;
    
    // Dni kursowania
    TimetableDays runningDays;
    
    // Okres ważności
    std::chrono::system_clock::time_point validFrom;
    std::chrono::system_clock::time_point validTo;
    
    // Częstotliwość
    int frequency = 0;        // Co ile minut (0 = pojedynczy kurs)
    int firstRun = 0;        // Pierwsza godzina kursu (minuty od północy)
    int lastRun = 0;         // Ostatnia godzina kursu
    
    // Instancje
    std::vector<TimetableInstance> instances;
    
    // Statystyki
    Statistics statistics;
    
    // Pomocnicze
    void sortStops();
    bool validateStopTimes() const;
    bool validatePlatforms() const;
};

#endif // TIMETABLE_H