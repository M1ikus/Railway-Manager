#ifndef SAVEMANAGER_H
#define SAVEMANAGER_H

#include <string>
#include <vector>
#include <chrono>
#include <nlohmann/json.hpp>

// Forward declarations
class GameState;
class Station;
class Train;
class Line;
class Personnel;
class Timetable;

struct SaveInfo {
    std::string filename;
    std::string saveName;
    std::string companyName;
    std::chrono::system_clock::time_point saveDate;
    std::chrono::system_clock::time_point gameDate;
    int version;
    double money;
    int reputation;
    int trains;
    int stations;
    int personnel;
    std::string thumbnailPath;
};

class SaveManager {
public:
    SaveManager();
    ~SaveManager();
    
    // Główne operacje
    bool saveGame(const std::string& filename, GameState* gameState);
    bool loadGame(const std::string& filename, GameState* gameState);
    
    // Autozapis
    bool autoSave(GameState* gameState);
    void setAutoSaveEnabled(bool enabled) { autoSaveEnabled = enabled; }
    void setAutoSaveInterval(int minutes) { autoSaveInterval = minutes; }
    bool isAutoSaveEnabled() const { return autoSaveEnabled; }
    
    // Zarządzanie zapisami
    std::vector<SaveInfo> getSavesList() const;
    bool deleteSave(const std::string& filename);
    bool renameSave(const std::string& oldFilename, const std::string& newFilename);
    bool saveExists(const std::string& filename) const;
    
    // Informacje o zapisie
    SaveInfo getSaveInfo(const std::string& filename) const;
    std::string generateSaveName() const;
    
    // Import/Export
    bool exportSave(const std::string& filename, const std::string& exportPath);
    bool importSave(const std::string& importPath);
    
    // Wersjonowanie
    int getCurrentSaveVersion() const { return SAVE_VERSION; }
    bool isCompatibleVersion(int version) const;
    
    // Kompresja
    void setCompressionEnabled(bool enabled) { compressionEnabled = enabled; }
    bool isCompressionEnabled() const { return compressionEnabled; }
    
private:
    static const int SAVE_VERSION = 1;
    static const std::string SAVE_EXTENSION;
    static const std::string SAVE_DIRECTORY;
    static const std::string AUTOSAVE_PREFIX;
    
    bool autoSaveEnabled = true;
    int autoSaveInterval = 5; // minuty
    int maxAutoSaves = 3;
    bool compressionEnabled = true;
    
    // Serializacja głównych komponentów
    nlohmann::json serializeGameState(GameState* gameState);
    bool deserializeGameState(const nlohmann::json& data, GameState* gameState);
    
    // Serializacja modeli
    nlohmann::json serializeStation(const Station* station);
    nlohmann::json serializeTrain(const Train* train);
    nlohmann::json serializeLine(const Line* line);
    nlohmann::json serializePersonnel(const Personnel* person);
    nlohmann::json serializeTimetable(const Timetable* timetable);
    
    // Deserializacja modeli
    std::shared_ptr<Station> deserializeStation(const nlohmann::json& data);
    std::shared_ptr<Train> deserializeTrain(const nlohmann::json& data);
    std::shared_ptr<Line> deserializeLine(const nlohmann::json& data);
    std::shared_ptr<Personnel> deserializePersonnel(const nlohmann::json& data);
    std::shared_ptr<Timetable> deserializeTimetable(const nlohmann::json& data);
    
    // Pomocnicze
    std::string getSavePath(const std::string& filename) const;
    std::string generateThumbnail(GameState* gameState);
    void cleanupOldAutoSaves();
    
    // Kompresja/dekompresja
    std::vector<uint8_t> compressData(const std::string& data);
    std::string decompressData(const std::vector<uint8_t>& compressedData);
    
    // Walidacja
    bool validateSaveData(const nlohmann::json& data) const;
    void migrateSaveData(nlohmann::json& data, int fromVersion);
};

#endif // SAVEMANAGER_H