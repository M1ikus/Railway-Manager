#ifndef GAMESTATE_H
#define GAMESTATE_H

#include <string>
#include <vector>
#include <unordered_map>
#include <memory>

// Forward declarations
class Station;
class Train;
class Line;
class Personnel;
class Timetable;

struct GameDate {
    int year;
    int month;
    int day;
    int hour;
    int minute;
};

struct CompanyInfo {
    std::string name;
    std::string logo;
    int foundedYear;
    int reputation;
};

class GameState {
public:
    GameState();
    ~GameState();
    
    // Reset do stanu początkowego
    void reset();
    
    // Zarządzanie czasem
    void setCurrentDate(int year, int month, int day);
    void updateTime(float deltaTime);
    GameDate getCurrentDate() const { return currentDate; }
    float getGameTimeElapsed() const { return gameTimeElapsed; }
    
    // Finanse
    double getMoney() const { return money; }
    void setMoney(double amount) { money = amount; }
    void addMoney(double amount) { money += amount; }
    bool canAfford(double amount) const { return money >= amount; }
    
    // Firma
    const CompanyInfo& getCompanyInfo() const { return companyInfo; }
    void setCompanyName(const std::string& name) { companyInfo.name = name; }
    void setReputation(int rep) { companyInfo.reputation = rep; }
    void changeReputation(int delta) { companyInfo.reputation += delta; }
    
    // Stacje
    void addStation(std::shared_ptr<Station> station);
    void removeStation(const std::string& stationId);
    std::shared_ptr<Station> getStation(const std::string& stationId) const;
    const std::vector<std::shared_ptr<Station>>& getAllStations() const { return stations; }
    
    // Pociągi
    void addTrain(std::shared_ptr<Train> train);
    void removeTrain(const std::string& trainId);
    std::shared_ptr<Train> getTrain(const std::string& trainId) const;
    const std::vector<std::shared_ptr<Train>>& getAllTrains() const { return trains; }
    std::vector<std::shared_ptr<Train>> getAvailableTrains() const;
    
    // Linie
    void addLine(std::shared_ptr<Line> line);
    void removeLine(const std::string& lineId);
    std::shared_ptr<Line> getLine(const std::string& lineId) const;
    const std::vector<std::shared_ptr<Line>>& getAllLines() const { return lines; }
    
    // Personel
    void addPersonnel(std::shared_ptr<Personnel> person);
    void removePersonnel(const std::string& personId);
    std::shared_ptr<Personnel> getPersonnel(const std::string& personId) const;
    const std::vector<std::shared_ptr<Personnel>>& getAllPersonnel() const { return personnel; }
    std::vector<std::shared_ptr<Personnel>> getAvailablePersonnel(const std::string& role) const;
    
    // Rozkłady
    void addTimetable(std::shared_ptr<Timetable> timetable);
    void removeTimetable(const std::string& timetableId);
    std::shared_ptr<Timetable> getTimetable(const std::string& timetableId) const;
    const std::vector<std::shared_ptr<Timetable>>& getAllTimetables() const { return timetables; }
    
    // Statystyki
    struct Statistics {
        int totalPassengersTransported = 0;
        int totalPassengersLost = 0;
        double totalRevenue = 0.0;
        double totalExpenses = 0.0;
        int totalTrainsOwned = 0;
        int totalPersonnelHired = 0;
        int totalAccidents = 0;
        int totalDelays = 0;
    };
    
    const Statistics& getStatistics() const { return statistics; }
    void updateStatistics(const Statistics& stats) { statistics = stats; }
    
    // Ustawienia gry
    struct Settings {
        bool pauseOnEvent = true;
        bool autoSave = true;
        int autoSaveInterval = 5; // minuty
        float difficultLevel = 1.0f;
    };
    
    const Settings& getSettings() const { return settings; }
    void updateSettings(const Settings& newSettings) { settings = newSettings; }
    
private:
    // Czas gry
    GameDate currentDate;
    float gameTimeElapsed = 0.0f;
    float timeAccumulator = 0.0f;
    
    // Dane firmy
    CompanyInfo companyInfo;
    double money = 0.0;
    
    // Kolekcje obiektów
    std::vector<std::shared_ptr<Station>> stations;
    std::vector<std::shared_ptr<Train>> trains;
    std::vector<std::shared_ptr<Line>> lines;
    std::vector<std::shared_ptr<Personnel>> personnel;
    std::vector<std::shared_ptr<Timetable>> timetables;
    
    // Mapy dla szybkiego dostępu
    std::unordered_map<std::string, std::shared_ptr<Station>> stationMap;
    std::unordered_map<std::string, std::shared_ptr<Train>> trainMap;
    std::unordered_map<std::string, std::shared_ptr<Line>> lineMap;
    std::unordered_map<std::string, std::shared_ptr<Personnel>> personnelMap;
    std::unordered_map<std::string, std::shared_ptr<Timetable>> timetableMap;
    
    // Statystyki i ustawienia
    Statistics statistics;
    Settings settings;
};

#endif // GAMESTATE_H