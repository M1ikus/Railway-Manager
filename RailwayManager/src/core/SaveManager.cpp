#include "SaveManager.h"
#include "GameState.h"
#include "models/Station.h"
#include "models/Train.h"
#include "models/Line.h"
#include "models/Personnel.h"
#include "models/Timetable.h"
#include "utils/Logger.h"
#include <fstream>
#include <filesystem>
#include <iomanip>
#include <zlib.h>

namespace fs = std::filesystem;
using json = nlohmann::json;

const std::string SaveManager::SAVE_EXTENSION = ".sav";
const std::string SaveManager::SAVE_DIRECTORY = "saves/";
const std::string SaveManager::AUTOSAVE_PREFIX = "autosave_";

SaveManager::SaveManager() {
    // Utwórz katalog zapisów jeśli nie istnieje
    if (!fs::exists(SAVE_DIRECTORY)) {
        fs::create_directories(SAVE_DIRECTORY);
    }
}

SaveManager::~SaveManager() {
}

bool SaveManager::saveGame(const std::string& filename, GameState* gameState) {
    if (!gameState) {
        LOG_ERROR("Brak stanu gry do zapisania");
        return false;
    }
    
    try {
        LOG_INFO("Zapisywanie gry: " + filename);
        
        // Serializuj stan gry
        json saveData = serializeGameState(gameState);
        
        // Dodaj metadane
        saveData["metadata"]["version"] = SAVE_VERSION;
        saveData["metadata"]["saveDate"] = std::chrono::system_clock::to_time_t(
            std::chrono::system_clock::now());
        saveData["metadata"]["gameName"] = "Railway Manager";
        
        // Konwertuj do stringa
        std::string jsonStr = saveData.dump(2);
        
        // Kompresuj jeśli włączone
        if (compressionEnabled) {
            auto compressed = compressData(jsonStr);
            
            // Zapisz skompresowane dane
            std::ofstream file(getSavePath(filename), std::ios::binary);
            if (!file.is_open()) {
                LOG_ERROR("Nie można otworzyć pliku do zapisu: " + filename);
                return false;
            }
            
            file.write(reinterpret_cast<const char*>(compressed.data()), compressed.size());
            file.close();
        } else {
            // Zapisz nieskompresowane
            std::ofstream file(getSavePath(filename));
            if (!file.is_open()) {
                LOG_ERROR("Nie można otworzyć pliku do zapisu: " + filename);
                return false;
            }
            
            file << jsonStr;
            file.close();
        }
        
        LOG_INFO("Gra zapisana pomyślnie");
        return true;
        
    } catch (const std::exception& e) {
        LOG_ERROR("Błąd podczas zapisywania gry: " + std::string(e.what()));
        return false;
    }
}

bool SaveManager::loadGame(const std::string& filename, GameState* gameState) {
    if (!gameState) {
        LOG_ERROR("Brak stanu gry do wczytania");
        return false;
    }
    
    try {
        LOG_INFO("Wczytywanie gry: " + filename);
        
        std::string savePath = getSavePath(filename);
        if (!fs::exists(savePath)) {
            LOG_ERROR("Plik zapisu nie istnieje: " + filename);
            return false;
        }
        
        json saveData;
        
        // Sprawdź czy plik jest skompresowany
        std::ifstream file(savePath, std::ios::binary);
        if (!file.is_open()) {
            LOG_ERROR("Nie można otworzyć pliku: " + filename);
            return false;
        }
        
        // Odczytaj dane
        std::string content((std::istreambuf_iterator<char>(file)),
                           std::istreambuf_iterator<char>());
        file.close();
        
        // Sprawdź czy to JSON czy skompresowane dane
        if (content[0] == '{') {
            // Nieskompresowane JSON
            saveData = json::parse(content);
        } else {
            // Skompresowane
            std::vector<uint8_t> compressed(content.begin(), content.end());
            std::string decompressed = decompressData(compressed);
            saveData = json::parse(decompressed);
        }
        
        // Sprawdź wersję
        int version = saveData["metadata"]["version"];
        if (!isCompatibleVersion(version)) {
            LOG_ERROR("Niekompatybilna wersja zapisu: " + std::to_string(version));
            return false;
        }
        
        // Migruj dane jeśli potrzeba
        if (version < SAVE_VERSION) {
            migrateSaveData(saveData, version);
        }
        
        // Waliduj dane
        if (!validateSaveData(saveData)) {
            LOG_ERROR("Nieprawidłowe dane zapisu");
            return false;
        }
        
        // Deserializuj stan gry
        if (!deserializeGameState(saveData, gameState)) {
            LOG_ERROR("Błąd deserializacji stanu gry");
            return false;
        }
        
        LOG_INFO("Gra wczytana pomyślnie");
        return true;
        
    } catch (const std::exception& e) {
        LOG_ERROR("Błąd podczas wczytywania gry: " + std::string(e.what()));
        return false;
    }
}

bool SaveManager::autoSave(GameState* gameState) {
    if (!autoSaveEnabled || !gameState) {
        return false;
    }
    
    // Generuj nazwę autozapisu
    std::string filename = AUTOSAVE_PREFIX + generateSaveName();
    
    // Zapisz
    bool result = saveGame(filename, gameState);
    
    if (result) {
        // Usuń stare autozapisy
        cleanupOldAutoSaves();
    }
    
    return result;
}

std::vector<SaveInfo> SaveManager::getSavesList() const {
    std::vector<SaveInfo> saves;
    
    try {
        for (const auto& entry : fs::directory_iterator(SAVE_DIRECTORY)) {
            if (entry.path().extension() == SAVE_EXTENSION) {
                SaveInfo info = getSaveInfo(entry.path().stem().string());
                if (!info.filename.empty()) {
                    saves.push_back(info);
                }
            }
        }
        
        // Sortuj według daty zapisu (najnowsze pierwsze)
        std::sort(saves.begin(), saves.end(),
            [](const SaveInfo& a, const SaveInfo& b) {
                return a.saveDate > b.saveDate;
            });
            
    } catch (const std::exception& e) {
        LOG_ERROR("Błąd podczas listowania zapisów: " + std::string(e.what()));
    }
    
    return saves;
}

SaveInfo SaveManager::getSaveInfo(const std::string& filename) const {
    SaveInfo info;
    info.filename = filename;
    
    try {
        std::string savePath = getSavePath(filename);
        if (!fs::exists(savePath)) {
            return info;
        }
        
        // Wczytaj tylko metadane
        std::ifstream file(savePath, std::ios::binary);
        if (!file.is_open()) {
            return info;
        }
        
        std::string content((std::istreambuf_iterator<char>(file)),
                           std::istreambuf_iterator<char>());
        file.close();
        
        json saveData;
        if (content[0] == '{') {
            saveData = json::parse(content);
        } else {
            std::vector<uint8_t> compressed(content.begin(), content.end());
            std::string decompressed = decompressData(compressed);
            saveData = json::parse(decompressed);
        }
        
        // Wypełnij informacje
        info.saveName = saveData["gameState"]["companyName"];
        info.companyName = saveData["gameState"]["companyName"];
        info.version = saveData["metadata"]["version"];
        info.money = saveData["gameState"]["money"];
        info.reputation = saveData["gameState"]["reputation"];
        info.trains = saveData["gameState"]["trains"].size();
        info.stations = saveData["gameState"]["stations"].size();
        info.personnel = saveData["gameState"]["personnel"].size();
        
        // Daty
        info.saveDate = std::chrono::system_clock::from_time_t(
            saveData["metadata"]["saveDate"]);
        
        // Data w grze
        auto gameDate = saveData["gameState"]["currentDate"];
        std::tm tm = {};
        tm.tm_year = gameDate["year"].get<int>() - 1900;
        tm.tm_mon = gameDate["month"].get<int>() - 1;
        tm.tm_mday = gameDate["day"].get<int>();
        tm.tm_hour = gameDate["hour"].get<int>();
        tm.tm_min = gameDate["minute"].get<int>();
        info.gameDate = std::chrono::system_clock::from_time_t(std::mktime(&tm));
        
    } catch (const std::exception& e) {
        LOG_ERROR("Błąd podczas odczytu informacji o zapisie: " + std::string(e.what()));
    }
    
    return info;
}

bool SaveManager::deleteSave(const std::string& filename) {
    try {
        std::string savePath = getSavePath(filename);
        if (fs::exists(savePath)) {
            fs::remove(savePath);
            LOG_INFO("Usunięto zapis: " + filename);
            return true;
        }
    } catch (const std::exception& e) {
        LOG_ERROR("Błąd podczas usuwania zapisu: " + std::string(e.what()));
    }
    return false;
}

json SaveManager::serializeGameState(GameState* gameState) {
    json data;
    
    // Podstawowe dane
    data["companyName"] = gameState->getCompanyInfo().name;
    data["companyLogo"] = gameState->getCompanyInfo().logo;
    data["foundedYear"] = gameState->getCompanyInfo().foundedYear;
    data["reputation"] = gameState->getCompanyInfo().reputation;
    data["money"] = gameState->getMoney();
    
    // Data i czas
    auto date = gameState->getCurrentDate();
    data["currentDate"]["year"] = date.year;
    data["currentDate"]["month"] = date.month;
    data["currentDate"]["day"] = date.day;
    data["currentDate"]["hour"] = date.hour;
    data["currentDate"]["minute"] = date.minute;
    
    data["gameTimeElapsed"] = gameState->getGameTimeElapsed();
    
    // Stacje
    json stationsArray = json::array();
    for (const auto& station : gameState->getAllStations()) {
        stationsArray.push_back(serializeStation(station.get()));
    }
    data["stations"] = stationsArray;
    
    // Pociągi
    json trainsArray = json::array();
    for (const auto& train : gameState->getAllTrains()) {
        trainsArray.push_back(serializeTrain(train.get()));
    }
    data["trains"] = trainsArray;
    
    // Linie
    json linesArray = json::array();
    for (const auto& line : gameState->getAllLines()) {
        linesArray.push_back(serializeLine(line.get()));
    }
    data["lines"] = linesArray;
    
    // Personel
    json personnelArray = json::array();
    for (const auto& person : gameState->getAllPersonnel()) {
        personnelArray.push_back(serializePersonnel(person.get()));
    }
    data["personnel"] = personnelArray;
    
    // Rozkłady
    json timetablesArray = json::array();
    for (const auto& timetable : gameState->getAllTimetables()) {
        timetablesArray.push_back(serializeTimetable(timetable.get()));
    }
    data["timetables"] = timetablesArray;
    
    // Statystyki
    auto stats = gameState->getStatistics();
    data["statistics"]["totalPassengersTransported"] = stats.totalPassengersTransported;
    data["statistics"]["totalPassengersLost"] = stats.totalPassengersLost;
    data["statistics"]["totalRevenue"] = stats.totalRevenue;
    data["statistics"]["totalExpenses"] = stats.totalExpenses;
    data["statistics"]["totalTrainsOwned"] = stats.totalTrainsOwned;
    data["statistics"]["totalPersonnelHired"] = stats.totalPersonnelHired;
    data["statistics"]["totalAccidents"] = stats.totalAccidents;
    data["statistics"]["totalDelays"] = stats.totalDelays;
    
    // Ustawienia
    auto settings = gameState->getSettings();
    data["settings"]["pauseOnEvent"] = settings.pauseOnEvent;
    data["settings"]["autoSave"] = settings.autoSave;
    data["settings"]["autoSaveInterval"] = settings.autoSaveInterval;
    data["settings"]["difficultyLevel"] = settings.difficultLevel;
    
    return data;
}

bool SaveManager::deserializeGameState(const json& data, GameState* gameState) {
    try {
        // Reset stanu
        gameState->reset();
        
        // Podstawowe dane
        gameState->setCompanyName(data["gameState"]["companyName"]);
        gameState->setMoney(data["gameState"]["money"]);
        gameState->setReputation(data["gameState"]["reputation"]);
        
        // Data i czas
        auto date = data["gameState"]["currentDate"];
        gameState->setCurrentDate(date["year"], date["month"], date["day"]);
        
        // Stacje
        for (const auto& stationData : data["gameState"]["stations"]) {
            auto station = deserializeStation(stationData);
            if (station) {
                gameState->addStation(station);
            }
        }
        
        // Pociągi
        for (const auto& trainData : data["gameState"]["trains"]) {
            auto train = deserializeTrain(trainData);
            if (train) {
                gameState->addTrain(train);
            }
        }
        
        // Linie
        for (const auto& lineData : data["gameState"]["lines"]) {
            auto line = deserializeLine(lineData);
            if (line) {
                gameState->addLine(line);
            }
        }
        
        // Personel
        for (const auto& personData : data["gameState"]["personnel"]) {
            auto person = deserializePersonnel(personData);
            if (person) {
                gameState->addPersonnel(person);
            }
        }
        
        // Rozkłady
        for (const auto& timetableData : data["gameState"]["timetables"]) {
            auto timetable = deserializeTimetable(timetableData);
            if (timetable) {
                gameState->addTimetable(timetable);
            }
        }
        
        // Statystyki
        if (data["gameState"].contains("statistics")) {
            GameState::Statistics stats;
            auto s = data["gameState"]["statistics"];
            stats.totalPassengersTransported = s["totalPassengersTransported"];
            stats.totalPassengersLost = s["totalPassengersLost"];
            stats.totalRevenue = s["totalRevenue"];
            stats.totalExpenses = s["totalExpenses"];
            stats.totalTrainsOwned = s["totalTrainsOwned"];
            stats.totalPersonnelHired = s["totalPersonnelHired"];
            stats.totalAccidents = s["totalAccidents"];
            stats.totalDelays = s["totalDelays"];
            gameState->updateStatistics(stats);
        }
        
        // Ustawienia
        if (data["gameState"].contains("settings")) {
            GameState::Settings settings;
            auto s = data["gameState"]["settings"];
            settings.pauseOnEvent = s["pauseOnEvent"];
            settings.autoSave = s["autoSave"];
            settings.autoSaveInterval = s["autoSaveInterval"];
            settings.difficultLevel = s["difficultyLevel"];
            gameState->updateSettings(settings);
        }
        
        return true;
        
    } catch (const std::exception& e) {
        LOG_ERROR("Błąd deserializacji stanu gry: " + std::string(e.what()));
        return false;
    }
}

json SaveManager::serializeStation(const Station* station) {
    json data;
    
    data["id"] = station->getId();
    data["name"] = station->getName();
    data["code"] = station->getCode();
    data["type"] = static_cast<int>(station->getType());
    data["size"] = static_cast<int>(station->getSize());
    data["latitude"] = station->getLatitude();
    data["longitude"] = station->getLongitude();
    data["region"] = station->getRegion();
    data["condition"] = station->getCondition();
    data["maxPassengers"] = station->getMaxPassengers();
    data["currentPassengers"] = station->getCurrentPassengers();
    
    // Perony
    json platformsArray = json::array();
    for (const auto& platform : station->getPlatforms()) {
        json p;
        p["number"] = platform.number;
        p["length"] = platform.length;
        p["hasRoof"] = platform.hasRoof;
        p["isElectrified"] = platform.isElectrified;
        p["occupied"] = platform.occupied;
        p["trainId"] = platform.trainId;
        platformsArray.push_back(p);
    }
    data["platforms"] = platformsArray;
    
    // Udogodnienia
    auto facilities = station->getFacilities();
    data["facilities"]["hasTicketOffice"] = facilities.hasTicketOffice;
    data["facilities"]["hasWaitingRoom"] = facilities.hasWaitingRoom;
    data["facilities"]["hasRestaurant"] = facilities.hasRestaurant;
    data["facilities"]["hasParking"] = facilities.hasParking;
    data["facilities"]["hasToilets"] = facilities.hasToilets;
    data["facilities"]["hasBikeRacks"] = facilities.hasBikeRacks;
    data["facilities"]["hasElevators"] = facilities.hasElevators;
    data["facilities"]["isAccessible"] = facilities.isAccessible;
    data["facilities"]["parkingSpaces"] = facilities.parkingSpaces;
    
    // Połączenia
    data["connections"] = station->getConnections();
    
    return data;
}

json SaveManager::serializeTrain(const Train* train) {
    json data;
    
    data["id"] = train->getId();
    data["name"] = train->getName();
    data["type"] = static_cast<int>(train->getType());
    data["status"] = static_cast<int>(train->getStatus());
    data["condition"] = train->getCondition();
    data["cleanliness"] = train->getCleanliness();
    data["fuelLevel"] = train->getFuelLevel();
    data["totalKm"] = train->getTotalKilometers();
    data["purchasePrice"] = train->getPurchasePrice();
    data["currentPassengers"] = train->getCurrentPassengers();
    data["currentLatitude"] = train->getCurrentLatitude();
    data["currentLongitude"] = train->getCurrentLongitude();
    data["currentSpeed"] = train->getCurrentSpeed();
    data["currentLine"] = train->getCurrentLine();
    data["currentStation"] = train->getCurrentStation();
    data["assignedTimetable"] = train->getAssignedTimetable();
    data["delay"] = train->getDelay();
    data["assignedDriver"] = train->getAssignedDriver();
    data["assignedConductor"] = train->getAssignedConductor();
    
    // Jednostki
    json unitsArray = json::array();
    for (const auto& unit : train->getUnits()) {
        json u;
        u["id"] = unit.id;
        u["series"] = unit.series;
        u["number"] = unit.number;
        u["manufacturingYear"] = unit.manufacturingYear;
        u["seats"] = unit.seats;
        u["standingRoom"] = unit.standingRoom;
        u["length"] = unit.length;
        u["weight"] = unit.weight;
        u["hasEngine"] = unit.hasEngine;
        u["isElectric"] = unit.isElectric;
        u["maxSpeed"] = unit.maxSpeed;
        u["power"] = unit.power;
        unitsArray.push_back(u);
    }
    data["units"] = unitsArray;
    
    return data;
}

json SaveManager::serializeLine(const Line* line) {
    json data;
    
    data["id"] = line->getId();
    data["number"] = line->getNumber();
    data["name"] = line->getName();
    data["type"] = static_cast<int>(line->getType());
    data["status"] = static_cast<int>(line->getStatus());
    data["electrification"] = static_cast<int>(line->getElectrification());
    
    // Sekcje
    json sectionsArray = json::array();
    for (const auto& section : line->getSections()) {
        json s;
        s["id"] = section.id;
        s["fromStationId"] = section.fromStationId;
        s["toStationId"] = section.toStationId;
        s["length"] = section.length;
        s["maxSpeed"] = section.maxSpeed;
        s["tracks"] = section.tracks;
        s["isElectrified"] = section.isElectrified;
        s["gradient"] = section.gradient;
        s["curvature"] = section.curvature;
        s["status"] = static_cast<int>(section.status);
        s["condition"] = section.condition;
        sectionsArray.push_back(s);
    }
    data["sections"] = sectionsArray;
    
    return data;
}

json SaveManager::serializePersonnel(const Personnel* person) {
    json data;
    
    data["id"] = person->getId();
    data["firstName"] = person->getFirstName();
    data["lastName"] = person->getLastName();
    data["role"] = static_cast<int>(person->getRole());
    data["status"] = static_cast<int>(person->getStatus());
    data["age"] = person->getAge();
    data["experienceYears"] = person->getExperienceYears();
    data["skillLevel"] = person->getSkillLevel();
    data["homeStation"] = person->getHomeStationId();
    data["baseSalary"] = person->getBaseSalary();
    data["satisfaction"] = person->getSatisfaction();
    data["performance"] = person->getPerformance();
    data["assignedTrain"] = person->getAssignedTrainId();
    data["assignedStation"] = person->getAssignedStationId();
    data["remainingVacationDays"] = person->getRemainingVacationDays();
    
    // Certyfikaty
    data["certifications"] = person->getCertifications();
    
    return data;
}

json SaveManager::serializeTimetable(const Timetable* timetable) {
    json data;
    
    data["id"] = timetable->getId();
    data["name"] = timetable->getName();
    data["trainId"] = timetable->getTrainId();
    data["lineId"] = timetable->getLineId();
    data["type"] = static_cast<int>(timetable->getType());
    data["active"] = timetable->isActive();
    data["runningDays"] = static_cast<int>(timetable->getRunningDays());
    data["frequency"] = timetable->getFrequency();
    
    // Przystanki
    json stopsArray = json::array();
    for (const auto& stop : timetable->getStops()) {
        json s;
        s["stationId"] = stop.stationId;
        s["arrivalTime"] = stop.arrivalTime;
        s["departureTime"] = stop.departureTime;
        s["platform"] = stop.platform;
        s["optional"] = stop.optional;
        s["dwellTime"] = stop.dwellTime;
        stopsArray.push_back(s);
    }
    data["stops"] = stopsArray;
    
    return data;
}

std::string SaveManager::getSavePath(const std::string& filename) const {
    std::string name = filename;
    if (name.find(SAVE_EXTENSION) == std::string::npos) {
        name += SAVE_EXTENSION;
    }
    return SAVE_DIRECTORY + name;
}

std::string SaveManager::generateSaveName() const {
    auto now = std::chrono::system_clock::now();
    auto time_t = std::chrono::system_clock::to_time_t(now);
    
    std::stringstream ss;
    ss << std::put_time(std::localtime(&time_t), "%Y%m%d_%H%M%S");
    
    return ss.str();
}

void SaveManager::cleanupOldAutoSaves() {
    std::vector<std::pair<std::string, std::filesystem::file_time_type>> autosaves;
    
    // Znajdź wszystkie autozapisy
    for (const auto& entry : fs::directory_iterator(SAVE_DIRECTORY)) {
        std::string filename = entry.path().filename().string();
        if (filename.find(AUTOSAVE_PREFIX) == 0) {
            autosaves.push_back({filename, entry.last_write_time()});
        }
    }
    
    // Sortuj według czasu (najstarsze pierwsze)
    std::sort(autosaves.begin(), autosaves.end(),
        [](const auto& a, const auto& b) {
            return a.second < b.second;
        });
    
    // Usuń najstarsze jeśli przekroczono limit
    while (autosaves.size() > static_cast<size_t>(maxAutoSaves)) {
        deleteSave(autosaves[0].first);
        autosaves.erase(autosaves.begin());
    }
}

std::vector<uint8_t> SaveManager::compressData(const std::string& data) {
    std::vector<uint8_t> compressed;
    
    // Szacuj rozmiar skompresowanych danych
    uLongf compressedSize = compressBound(data.size());
    compressed.resize(compressedSize + sizeof(uLongf));
    
    // Zapisz oryginalny rozmiar
    *reinterpret_cast<uLongf*>(compressed.data()) = data.size();
    
    // Kompresuj
    int result = compress(compressed.data() + sizeof(uLongf), &compressedSize,
                         reinterpret_cast<const Bytef*>(data.data()), data.size());
    
    if (result != Z_OK) {
        LOG_ERROR("Błąd kompresji danych");
        return std::vector<uint8_t>();
    }
    
    compressed.resize(compressedSize + sizeof(uLongf));
    return compressed;
}

std::string SaveManager::decompressData(const std::vector<uint8_t>& compressedData) {
    if (compressedData.size() < sizeof(uLongf)) {
        LOG_ERROR("Nieprawidłowe skompresowane dane");
        return "";
    }
    
    // Odczytaj oryginalny rozmiar
    uLongf originalSize = *reinterpret_cast<const uLongf*>(compressedData.data());
    
    // Dekompresuj
    std::string decompressed(originalSize, '\0');
    int result = uncompress(reinterpret_cast<Bytef*>(&decompressed[0]), &originalSize,
                           compressedData.data() + sizeof(uLongf), 
                           compressedData.size() - sizeof(uLongf));
    
    if (result != Z_OK) {
        LOG_ERROR("Błąd dekompresji danych");
        return "";
    }
    
    return decompressed;
}

bool SaveManager::validateSaveData(const json& data) const {
    try {
        // Sprawdź wymagane pola
        if (!data.contains("metadata") || !data.contains("gameState")) {
            return false;
        }
        
        // Sprawdź metadane
        if (!data["metadata"].contains("version") || 
            !data["metadata"].contains("saveDate")) {
            return false;
        }
        
        // Sprawdź podstawowe dane gry
        if (!data["gameState"].contains("companyName") ||
            !data["gameState"].contains("money") ||
            !data["gameState"].contains("currentDate")) {
            return false;
        }
        
        return true;
        
    } catch (const std::exception& e) {
        LOG_ERROR("Błąd walidacji danych zapisu: " + std::string(e.what()));
        return false;
    }
}

bool SaveManager::isCompatibleVersion(int version) const {
    // Obecnie akceptujemy tylko bieżącą wersję
    // W przyszłości można dodać wsparcie dla starszych wersji
    return version == SAVE_VERSION;
}

void SaveManager::migrateSaveData(json& data, int fromVersion) {
    // Migracja danych ze starszych wersji
    LOG_INFO("Migracja danych z wersji " + std::to_string(fromVersion) + 
             " do " + std::to_string(SAVE_VERSION));
    
    // TODO: Implementacja migracji gdy będą nowe wersje
}