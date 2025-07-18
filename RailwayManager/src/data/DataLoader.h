#ifndef DATALOADER_H
#define DATALOADER_H

#include <string>
#include <memory>
#include <vector>
#include <unordered_map>

// Forward declarations
class GameState;
class CSVLoader;
class JSONLoader;
class GeoJSONLoader;
class Station;
class Train;
class Line;
class Personnel;
class Event;

struct ScenarioData {
    std::string id;
    std::string name;
    std::string description;
    int startYear;
    double startMoney;
    std::string difficulty;
    std::vector<std::string> availableStations;
    std::vector<std::string> availableTrains;
    std::vector<std::string> objectives;
};

struct TrainStockData {
    std::string id;
    std::string series;
    std::string manufacturer;
    int yearBuilt;
    std::string type;
    int seats;
    int standingRoom;
    float length;
    float weight;
    float maxSpeed;
    float power;
    bool isElectric;
    float basePrice;
    std::string condition; // "new", "good", "fair", "poor"
};

struct PersonnelTemplate {
    std::string firstName;
    std::string lastName;
    std::string role;
    int experience;
    float salary;
    std::string homeStation;
};

struct FareData {
    std::string type;
    std::string category;
    float basePrice;
    float perKm;
    float minPrice;
    float maxPrice;
    std::vector<std::string> discounts;
};

class DataLoader {
public:
    DataLoader();
    ~DataLoader();
    
    // Główne funkcje ładowania
    bool loadBaseData();
    bool loadScenario(const std::string& scenarioId, GameState* gameState);
    bool loadModData(const std::string& modPath);
    
    // Ładowanie konkretnych typów danych
    bool loadStations(const std::string& filename = "data/world/stations.csv");
    bool loadLines(const std::string& filename = "data/world/lines.csv");
    bool loadInfrastructure(const std::string& filename = "data/world/infrastructure.geojson");
    bool loadTrainStock(const std::string& filename = "data/stock/");
    bool loadPersonnelTemplates(const std::string& filename = "data/personnel/personnel_templates.csv");
    bool loadEvents(const std::string& filename = "data/gameplay/events.json");
    bool loadFares(const std::string& filename = "data/economy/fares.csv");
    bool loadScenarios(const std::string& filename = "data/gameplay/scenarios.json");
    bool loadLanguage(const std::string& langCode = "pl");
    
    // Gettery dla wczytanych danych
    const std::vector<std::shared_ptr<Station>>& getStations() const { return stations; }
    const std::vector<std::shared_ptr<Line>>& getLines() const { return lines; }
    const std::vector<TrainStockData>& getAvailableStock() const { return trainStock; }
    const std::vector<PersonnelTemplate>& getPersonnelTemplates() const { return personnelTemplates; }
    const std::vector<std::shared_ptr<Event>>& getEvents() const { return events; }
    const std::vector<ScenarioData>& getScenarios() const { return scenarios; }
    
    // Wyszukiwanie
    std::shared_ptr<Station> findStation(const std::string& id) const;
    std::shared_ptr<Line> findLine(const std::string& id) const;
    TrainStockData* findTrainStock(const std::string& id);
    ScenarioData* findScenario(const std::string& id);
    
    // Fabryki obiektów
    std::shared_ptr<Train> createTrainFromStock(const std::string& stockId, const std::string& name);
    std::shared_ptr<Personnel> createPersonnelFromTemplate(const PersonnelTemplate& templ);
    
    // Tłumaczenia
    std::string getText(const std::string& key) const;
    bool hasText(const std::string& key) const;
    
    // Statystyki
    struct LoadStatistics {
        int stationsLoaded = 0;
        int linesLoaded = 0;
        int trainStockLoaded = 0;
        int eventsLoaded = 0;
        int personnelTemplatesLoaded = 0;
        int scenariosLoaded = 0;
        int translationsLoaded = 0;
        int errorsCount = 0;
    };
    
    const LoadStatistics& getLoadStatistics() const { return loadStats; }
    
    // Walidacja danych
    bool validateData();
    std::vector<std::string> getValidationErrors() const { return validationErrors; }
    
private:
    // Loadery
    std::unique_ptr<CSVLoader> csvLoader;
    std::unique_ptr<JSONLoader> jsonLoader;
    std::unique_ptr<GeoJSONLoader> geoLoader;
    
    // Wczytane dane
    std::vector<std::shared_ptr<Station>> stations;
    std::vector<std::shared_ptr<Line>> lines;
    std::vector<TrainStockData> trainStock;
    std::vector<PersonnelTemplate> personnelTemplates;
    std::vector<std::shared_ptr<Event>> events;
    std::vector<ScenarioData> scenarios;
    std::vector<FareData> fares;
    
    // Mapy dla szybkiego dostępu
    std::unordered_map<std::string, std::shared_ptr<Station>> stationMap;
    std::unordered_map<std::string, std::shared_ptr<Line>> lineMap;
    std::unordered_map<std::string, TrainStockData> stockMap;
    std::unordered_map<std::string, ScenarioData> scenarioMap;
    
    // Tłumaczenia
    std::unordered_map<std::string, std::string> translations;
    
    // Ścieżki
    std::string dataPath = "data/";
    std::string modPath = "mods/";
    
    // Statystyki i błędy
    LoadStatistics loadStats;
    std::vector<std::string> validationErrors;
    
    // Pomocnicze funkcje
    bool loadStationsFromCSV(const std::string& filename);
    bool loadLinesFromCSV(const std::string& filename);
    bool loadTrainStockFromJSON(const std::string& filename);
    bool loadPersonnelFromCSV(const std::string& filename);
    bool loadEventsFromJSON(const std::string& filename);
    bool loadFaresFromCSV(const std::string& filename);
    bool loadScenariosFromJSON(const std::string& filename);
    bool loadTranslationsFromJSON(const std::string& filename);
    
    void connectStationsAndLines();
    void validateStationConnections();
    void validateLineIntegrity();
};

#endif // DATALOADER_H