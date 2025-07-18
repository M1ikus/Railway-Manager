#include "DataLoader.h"
#include "CSVLoader.h"
#include "JSONLoader.h"
#include "GeoJSONLoader.h"
#include "core/GameState.h"
#include "models/Station.h"
#include "models/Train.h"
#include "models/Line.h"
#include "models/Personnel.h"
#include "models/Event.h"
#include "utils/Logger.h"
#include <filesystem>
#include <algorithm>

namespace fs = std::filesystem;

DataLoader::DataLoader() {
    csvLoader = std::make_unique<CSVLoader>();
    jsonLoader = std::make_unique<JSONLoader>();
    geoLoader = std::make_unique<GeoJSONLoader>();
}

DataLoader::~DataLoader() {
    // Destruktor
}

bool DataLoader::loadBaseData() {
    LOG_INFO("Rozpoczynam ładowanie danych bazowych...");
    loadStats = LoadStatistics{}; // Reset statystyk
    
    bool success = true;
    
    // Wczytaj stacje
    if (!loadStations()) {
        LOG_ERROR("Błąd ładowania stacji");
        success = false;
    }
    
    // Wczytaj linie
    if (!loadLines()) {
        LOG_ERROR("Błąd ładowania linii");
        success = false;
    }
    
    // Połącz stacje z liniami
    connectStationsAndLines();
    
    // Wczytaj infrastrukturę
    if (!loadInfrastructure()) {
        LOG_ERROR("Błąd ładowania infrastruktury");
        success = false;
    }
    
    // Wczytaj tabor
    if (!loadTrainStock()) {
        LOG_ERROR("Błąd ładowania taboru");
        success = false;
    }
    
    // Wczytaj szablony personelu
    if (!loadPersonnelTemplates()) {
        LOG_ERROR("Błąd ładowania szablonów personelu");
        success = false;
    }
    
    // Wczytaj eventy
    if (!loadEvents()) {
        LOG_ERROR("Błąd ładowania eventów");
        success = false;
    }
    
    // Wczytaj taryfy
    if (!loadFares()) {
        LOG_ERROR("Błąd ładowania taryf");
        success = false;
    }
    
    // Wczytaj scenariusze
    if (!loadScenarios()) {
        LOG_ERROR("Błąd ładowania scenariuszy");
        success = false;
    }
    
    // Wczytaj domyślny język
    if (!loadLanguage("pl")) {
        LOG_ERROR("Błąd ładowania języka");
        success = false;
    }
    
    // Waliduj dane
    if (success) {
        success = validateData();
    }
    
    LOG_INFO("Załadowano: " + std::to_string(loadStats.stationsLoaded) + " stacji, " +
             std::to_string(loadStats.linesLoaded) + " linii, " +
             std::to_string(loadStats.trainStockLoaded) + " typów taboru");
    
    return success;
}

bool DataLoader::loadScenario(const std::string& scenarioId, GameState* gameState) {
    LOG_INFO("Ładowanie scenariusza: " + scenarioId);
    
    auto scenario = findScenario(scenarioId);
    if (!scenario) {
        LOG_ERROR("Nie znaleziono scenariusza: " + scenarioId);
        return false;
    }
    
    // Ustaw dane początkowe
    gameState->setMoney(scenario->startMoney);
    gameState->setCurrentDate(scenario->startYear, 1, 1);
    
    // Wczytaj dostępne stacje
    for (const auto& stationId : scenario->availableStations) {
        auto station = findStation(stationId);
        if (station) {
            gameState->addStation(station);
        }
    }
    
    // Przygotuj dostępny tabor
    // (tabor jest tylko w sklepie, nie w stanie gry na starcie)
    
    LOG_INFO("Scenariusz załadowany pomyślnie");
    return true;
}

bool DataLoader::loadStations(const std::string& filename) {
    return loadStationsFromCSV(filename);
}

bool DataLoader::loadStationsFromCSV(const std::string& filename) {
    LOG_INFO("Ładowanie stacji z: " + filename);
    
    auto data = csvLoader->load(filename);
    if (data.empty()) {
        LOG_ERROR("Brak danych w pliku stacji");
        return false;
    }
    
    for (size_t i = 1; i < data.size(); ++i) { // Pomijamy nagłówek
        const auto& row = data[i];
        if (row.size() < 8) continue;
        
        // Format: id,name,code,type,size,lat,lon,region
        auto station = std::make_shared<Station>(row[0], row[1]);
        station->setCode(row[2]);
        
        // Typ stacji
        if (row[3] == "MAJOR") station->setType(StationType::MAJOR);
        else if (row[3] == "REGIONAL") station->setType(StationType::REGIONAL);
        else if (row[3] == "LOCAL") station->setType(StationType::LOCAL);
        else if (row[3] == "TECHNICAL") station->setType(StationType::TECHNICAL);
        else if (row[3] == "FREIGHT") station->setType(StationType::FREIGHT);
        
        // Rozmiar stacji
        if (row[4] == "SMALL") station->setSize(StationSize::SMALL);
        else if (row[4] == "MEDIUM") station->setSize(StationSize::MEDIUM);
        else if (row[4] == "LARGE") station->setSize(StationSize::LARGE);
        else if (row[4] == "HUGE") station->setSize(StationSize::HUGE);
        
        // Współrzędne
        try {
            double lat = std::stod(row[5]);
            double lon = std::stod(row[6]);
            station->setCoordinates(lat, lon);
        } catch (...) {
            LOG_WARNING("Błędne współrzędne dla stacji: " + row[1]);
        }
        
        // Region
        station->setRegion(row[7]);
        
        // Dodaj perony w zależności od rozmiaru
        int platformCount = 2;
        switch (station->getSize()) {
            case StationSize::SMALL: platformCount = 2; break;
            case StationSize::MEDIUM: platformCount = 4; break;
            case StationSize::LARGE: platformCount = 8; break;
            case StationSize::HUGE: platformCount = 12; break;
        }
        
        for (int p = 1; p <= platformCount; ++p) {
            Platform platform;
            platform.number = p;
            platform.length = (station->getSize() == StationSize::SMALL) ? 200 : 400;
            platform.hasRoof = (p <= 2);
            platform.isElectrified = true;
            platform.occupied = false;
            station->addPlatform(platform);
        }
        
        stations.push_back(station);
        stationMap[station->getId()] = station;
        loadStats.stationsLoaded++;
    }
    
    return true;
}

bool DataLoader::loadLines(const std::string& filename) {
    return loadLinesFromCSV(filename);
}

bool DataLoader::loadLinesFromCSV(const std::string& filename) {
    LOG_INFO("Ładowanie linii z: " + filename);
    
    auto data = csvLoader->load(filename);
    if (data.empty()) {
        LOG_ERROR("Brak danych w pliku linii");
        return false;
    }
    
    for (size_t i = 1; i < data.size(); ++i) {
        const auto& row = data[i];
        if (row.size() < 6) continue;
        
        // Format: id,number,name,type,electrification,sections
        auto line = std::make_shared<Line>(row[0], row[1], row[2]);
        
        // Typ linii
        if (row[3] == "MAIN") line->setType(LineType::MAIN);
        else if (row[3] == "REGIONAL") line->setType(LineType::REGIONAL);
        else if (row[3] == "LOCAL") line->setType(LineType::LOCAL);
        else if (row[3] == "INDUSTRIAL") line->setType(LineType::INDUSTRIAL);
        else if (row[3] == "HIGH_SPEED") line->setType(LineType::HIGH_SPEED);
        
        // Elektryfikacja
        if (row[4] == "DC_3000V") line->setElectrification(ElectrificationType::DC_3000V);
        else if (row[4] == "AC_25KV") line->setElectrification(ElectrificationType::AC_25KV);
        else if (row[4] == "DUAL") line->setElectrification(ElectrificationType::DUAL);
        else line->setElectrification(ElectrificationType::NONE);
        
        lines.push_back(line);
        lineMap[line->getId()] = line;
        loadStats.linesLoaded++;
    }
    
    return true;
}

bool DataLoader::loadTrainStock(const std::string& directory) {
    LOG_INFO("Ładowanie taboru z: " + directory);
    
    // Wczytaj używany tabor
    if (!loadTrainStockFromJSON(directory + "used_stock.json")) {
        return false;
    }
    
    // Wczytaj nowy tabor
    if (!loadTrainStockFromJSON(directory + "new_stock.json")) {
        return false;
    }
    
    return true;
}

bool DataLoader::loadTrainStockFromJSON(const std::string& filename) {
    auto data = jsonLoader->load(filename);
    if (!data.contains("stock")) {
        LOG_ERROR("Brak sekcji 'stock' w pliku: " + filename);
        return false;
    }
    
    for (const auto& item : data["stock"]) {
        TrainStockData stock;
        stock.id = item["id"];
        stock.series = item["series"];
        stock.manufacturer = item.value("manufacturer", "Unknown");
        stock.yearBuilt = item["year"];
        stock.type = item["type"];
        stock.seats = item.value("seats", 0);
        stock.standingRoom = item.value("standing", 0);
        stock.length = item["length"];
        stock.weight = item["weight"];
        stock.maxSpeed = item["max_speed"];
        stock.power = item.value("power", 0.0f);
        stock.isElectric = item.value("electric", true);
        stock.basePrice = item["price"];
        stock.condition = item.value("condition", "new");
        
        trainStock.push_back(stock);
        stockMap[stock.id] = stock;
        loadStats.trainStockLoaded++;
    }
    
    return true;
}

bool DataLoader::loadPersonnelTemplates(const std::string& filename) {
    return loadPersonnelFromCSV(filename);
}

bool DataLoader::loadPersonnelFromCSV(const std::string& filename) {
    LOG_INFO("Ładowanie szablonów personelu z: " + filename);
    
    auto data = csvLoader->load(filename);
    if (data.empty()) {
        LOG_ERROR("Brak danych w pliku personelu");
        return false;
    }
    
    for (size_t i = 1; i < data.size(); ++i) {
        const auto& row = data[i];
        if (row.size() < 6) continue;
        
        // Format: first_name,last_name,role,experience,salary,home_station
        PersonnelTemplate templ;
        templ.firstName = row[0];
        templ.lastName = row[1];
        templ.role = row[2];
        templ.experience = std::stoi(row[3]);
        templ.salary = std::stof(row[4]);
        templ.homeStation = row[5];
        
        personnelTemplates.push_back(templ);
        loadStats.personnelTemplatesLoaded++;
    }
    
    return true;
}

bool DataLoader::loadEvents(const std::string& filename) {
    return loadEventsFromJSON(filename);
}

bool DataLoader::loadEventsFromJSON(const std::string& filename) {
    LOG_INFO("Ładowanie eventów z: " + filename);
    
    auto data = jsonLoader->load(filename);
    if (!data.contains("events")) {
        LOG_ERROR("Brak sekcji 'events' w pliku: " + filename);
        return false;
    }
    
    // TODO: Implementacja ładowania eventów po stworzeniu klasy Event
    loadStats.eventsLoaded = data["events"].size();
    
    return true;
}

bool DataLoader::loadFares(const std::string& filename) {
    return loadFaresFromCSV(filename);
}

bool DataLoader::loadFaresFromCSV(const std::string& filename) {
    LOG_INFO("Ładowanie taryf z: " + filename);
    
    auto data = csvLoader->load(filename);
    if (data.empty()) {
        LOG_ERROR("Brak danych w pliku taryf");
        return false;
    }
    
    for (size_t i = 1; i < data.size(); ++i) {
        const auto& row = data[i];
        if (row.size() < 6) continue;
        
        FareData fare;
        fare.type = row[0];
        fare.category = row[1];
        fare.basePrice = std::stof(row[2]);
        fare.perKm = std::stof(row[3]);
        fare.minPrice = std::stof(row[4]);
        fare.maxPrice = std::stof(row[5]);
        
        fares.push_back(fare);
    }
    
    return true;
}

bool DataLoader::loadScenarios(const std::string& filename) {
    return loadScenariosFromJSON(filename);
}

bool DataLoader::loadScenariosFromJSON(const std::string& filename) {
    LOG_INFO("Ładowanie scenariuszy z: " + filename);
    
    auto data = jsonLoader->load(filename);
    if (!data.contains("scenarios")) {
        LOG_ERROR("Brak sekcji 'scenarios' w pliku: " + filename);
        return false;
    }
    
    for (const auto& item : data["scenarios"]) {
        ScenarioData scenario;
        scenario.id = item["id"];
        scenario.name = item["name"];
        scenario.description = item["description"];
        scenario.startYear = item["start_year"];
        scenario.startMoney = item["start_money"];
        scenario.difficulty = item["difficulty"];
        
        if (item.contains("stations")) {
            for (const auto& station : item["stations"]) {
                scenario.availableStations.push_back(station);
            }
        }
        
        if (item.contains("trains")) {
            for (const auto& train : item["trains"]) {
                scenario.availableTrains.push_back(train);
            }
        }
        
        if (item.contains("objectives")) {
            for (const auto& objective : item["objectives"]) {
                scenario.objectives.push_back(objective);
            }
        }
        
        scenarios.push_back(scenario);
        scenarioMap[scenario.id] = scenario;
        loadStats.scenariosLoaded++;
    }
    
    return true;
}

bool DataLoader::loadLanguage(const std::string& langCode) {
    std::string filename = "lang/" + langCode + ".json";
    return loadTranslationsFromJSON(filename);
}

bool DataLoader::loadTranslationsFromJSON(const std::string& filename) {
    LOG_INFO("Ładowanie tłumaczeń z: " + filename);
    
    auto data = jsonLoader->load(filename);
    if (data.empty()) {
        LOG_ERROR("Brak danych w pliku tłumaczeń");
        return false;
    }
    
    // Rekurencyjna funkcja do spłaszczania zagnieżdżonego JSON
    std::function<void(const nlohmann::json&, const std::string&)> flatten;
    flatten = [this, &flatten](const nlohmann::json& obj, const std::string& prefix) {
        for (auto& [key, value] : obj.items()) {
            std::string fullKey = prefix.empty() ? key : prefix + "." + key;
            
            if (value.is_string()) {
                translations[fullKey] = value;
                loadStats.translationsLoaded++;
            } else if (value.is_object()) {
                flatten(value, fullKey);
            }
        }
    };
    
    flatten(data, "");
    return true;
}

void DataLoader::connectStationsAndLines() {
    LOG_INFO("Łączenie stacji z liniami...");
    
    // TODO: Implementacja na podstawie danych infrastruktury
}

bool DataLoader::validateData() {
    LOG_INFO("Walidacja wczytanych danych...");
    validationErrors.clear();
    
    validateStationConnections();
    validateLineIntegrity();
    
    if (!validationErrors.empty()) {
        LOG_ERROR("Znaleziono " + std::to_string(validationErrors.size()) + " błędów walidacji");
        for (const auto& error : validationErrors) {
            LOG_ERROR("  - " + error);
        }
        return false;
    }
    
    LOG_INFO("Walidacja zakończona pomyślnie");
    return true;
}

void DataLoader::validateStationConnections() {
    // Sprawdź czy każda stacja ma przynajmniej jedno połączenie
    for (const auto& station : stations) {
        if (station->getConnections().empty()) {
            validationErrors.push_back("Stacja " + station->getName() + " nie ma żadnych połączeń");
        }
    }
}

void DataLoader::validateLineIntegrity() {
    // Sprawdź czy linie mają poprawne sekcje
    for (const auto& line : lines) {
        if (line->getSections().empty()) {
            validationErrors.push_back("Linia " + line->getName() + " nie ma żadnych sekcji");
        }
    }
}

std::shared_ptr<Station> DataLoader::findStation(const std::string& id) const {
    auto it = stationMap.find(id);
    return (it != stationMap.end()) ? it->second : nullptr;
}

std::shared_ptr<Line> DataLoader::findLine(const std::string& id) const {
    auto it = lineMap.find(id);
    return (it != lineMap.end()) ? it->second : nullptr;
}

TrainStockData* DataLoader::findTrainStock(const std::string& id) {
    auto it = stockMap.find(id);
    return (it != stockMap.end()) ? &it->second : nullptr;
}

ScenarioData* DataLoader::findScenario(const std::string& id) {
    auto it = scenarioMap.find(id);
    return (it != scenarioMap.end()) ? &it->second : nullptr;
}

std::shared_ptr<Train> DataLoader::createTrainFromStock(const std::string& stockId, const std::string& name) {
    auto stockData = findTrainStock(stockId);
    if (!stockData) {
        LOG_ERROR("Nie znaleziono typu taboru: " + stockId);
        return nullptr;
    }
    
    // Generuj unikalne ID
    static int trainCounter = 1;
    std::string trainId = "train_" + std::to_string(trainCounter++);
    
    auto train = std::make_shared<Train>(trainId, name);
    
    // Ustaw typ
    if (stockData->type == "local") train->setType(TrainType::PASSENGER_LOCAL);
    else if (stockData->type == "regional") train->setType(TrainType::PASSENGER_REGIONAL);
    else if (stockData->type == "fast") train->setType(TrainType::PASSENGER_FAST);
    else if (stockData->type == "intercity") train->setType(TrainType::PASSENGER_INTERCITY);
    else if (stockData->type == "express") train->setType(TrainType::PASSENGER_EXPRESS);
    else if (stockData->type == "freight") train->setType(TrainType::FREIGHT);
    
    // Dodaj jednostkę
    TrainUnit unit;
    unit.id = trainId + "_unit_1";
    unit.series = stockData->series;
    unit.number = std::to_string(1000 + trainCounter);
    unit.manufacturingYear = stockData->yearBuilt;
    unit.seats = stockData->seats;
    unit.standingRoom = stockData->standingRoom;
    unit.length = stockData->length;
    unit.weight = stockData->weight;
    unit.hasEngine = (stockData->power > 0);
    unit.isElectric = stockData->isElectric;
    unit.maxSpeed = stockData->maxSpeed;
    unit.power = stockData->power;
    
    train->addUnit(unit);
    train->setPurchasePrice(stockData->basePrice);
    
    // Ustaw stan na podstawie kondycji
    if (stockData->condition == "new") {
        train->setCondition(1.0f);
    } else if (stockData->condition == "good") {
        train->setCondition(0.8f);
    } else if (stockData->condition == "fair") {
        train->setCondition(0.6f);
    } else if (stockData->condition == "poor") {
        train->setCondition(0.4f);
    }
    
    return train;
}

std::shared_ptr<Personnel> DataLoader::createPersonnelFromTemplate(const PersonnelTemplate& templ) {
    // TODO: Implementacja po stworzeniu klasy Personnel
    return nullptr;
}

std::string DataLoader::getText(const std::string& key) const {
    auto it = translations.find(key);
    return (it != translations.end()) ? it->second : key;
}

bool DataLoader::hasText(const std::string& key) const {
    return translations.find(key) != translations.end();
}

bool DataLoader::loadModData(const std::string& modPath) {
    LOG_INFO("Ładowanie moda z: " + modPath);
    
    // TODO: Implementacja ładowania modów
    
    return true;
}