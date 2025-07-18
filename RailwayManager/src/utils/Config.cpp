#include "Config.h"
#include "Logger.h"
#include <fstream>
#include <filesystem>

namespace fs = std::filesystem;
using json = nlohmann::json;

Config& Config::getInstance() {
    static Config instance;
    return instance;
}

Config::Config() {
    // Utwórz katalog konfiguracji jeśli nie istnieje
    if (!fs::exists(configPath)) {
        fs::create_directories(configPath);
    }
    
    // Załaduj domyślne wartości
    loadDefaults();
}

Config::~Config() {
    // Zapisz jeśli były modyfikacje
    if (modified) {
        save();
    }
}

bool Config::load(const std::string& filename) {
    try {
        std::string fullPath = configPath + filename;
        
        if (!fs::exists(fullPath)) {
            LOG_WARNING("Plik konfiguracji nie istnieje: " + fullPath);
            // Użyj domyślnych wartości
            return true;
        }
        
        std::ifstream file(fullPath);
        if (!file.is_open()) {
            LOG_ERROR("Nie można otworzyć pliku konfiguracji: " + fullPath);
            return false;
        }
        
        file >> configData;
        file.close();
        
        modified = false;
        LOG_INFO("Wczytano konfigurację z: " + fullPath);
        return true;
        
    } catch (const std::exception& e) {
        LOG_ERROR("Błąd wczytywania konfiguracji: " + std::string(e.what()));
        return false;
    }
}

bool Config::save(const std::string& filename) {
    try {
        std::string fullPath = configPath + filename;
        
        std::ofstream file(fullPath);
        if (!file.is_open()) {
            LOG_ERROR("Nie można utworzyć pliku konfiguracji: " + fullPath);
            return false;
        }
        
        file << std::setw(4) << configData << std::endl;
        file.close();
        
        modified = false;
        LOG_INFO("Zapisano konfigurację do: " + fullPath);
        return true;
        
    } catch (const std::exception& e) {
        LOG_ERROR("Błąd zapisywania konfiguracji: " + std::string(e.what()));
        return false;
    }
}

void Config::reset() {
    configData.clear();
    loadDefaults();
    modified = true;
}

bool Config::getBool(const std::string& key, bool defaultValue) const {
    return getBoolCat(currentCategory, key, defaultValue);
}

int Config::getInt(const std::string& key, int defaultValue) const {
    return getIntCat(currentCategory, key, defaultValue);
}

float Config::getFloat(const std::string& key, float defaultValue) const {
    return getFloatCat(currentCategory, key, defaultValue);
}

double Config::getDouble(const std::string& key, double defaultValue) const {
    return getDoubleCat(currentCategory, key, defaultValue);
}

std::string Config::getString(const std::string& key, const std::string& defaultValue) const {
    return getStringCat(currentCategory, key, defaultValue);
}

void Config::setBool(const std::string& key, bool value) {
    setBoolCat(currentCategory, key, value);
}

void Config::setInt(const std::string& key, int value) {
    setIntCat(currentCategory, key, value);
}

void Config::setFloat(const std::string& key, float value) {
    setFloatCat(currentCategory, key, value);
}

void Config::setDouble(const std::string& key, double value) {
    setDoubleCat(currentCategory, key, value);
}

void Config::setString(const std::string& key, const std::string& value) {
    setStringCat(currentCategory, key, value);
}

bool Config::hasKey(const std::string& key) const {
    std::string fullKey = getCategoryKey(currentCategory, key);
    return configData.contains(fullKey);
}

void Config::setCategory(const std::string& category) {
    currentCategory = category;
}

bool Config::getBoolCat(const std::string& category, const std::string& key, bool defaultValue) const {
    std::string fullKey = getCategoryKey(category, key);
    
    if (configData.contains(fullKey) && configData[fullKey].is_boolean()) {
        return configData[fullKey];
    }
    
    return defaultValue;
}

int Config::getIntCat(const std::string& category, const std::string& key, int defaultValue) const {
    std::string fullKey = getCategoryKey(category, key);
    
    if (configData.contains(fullKey) && configData[fullKey].is_number_integer()) {
        return configData[fullKey];
    }
    
    return defaultValue;
}

float Config::getFloatCat(const std::string& category, const std::string& key, float defaultValue) const {
    std::string fullKey = getCategoryKey(category, key);
    
    if (configData.contains(fullKey) && configData[fullKey].is_number()) {
        return configData[fullKey];
    }
    
    return defaultValue;
}

double Config::getDoubleCat(const std::string& category, const std::string& key, double defaultValue) const {
    std::string fullKey = getCategoryKey(category, key);
    
    if (configData.contains(fullKey) && configData[fullKey].is_number()) {
        return configData[fullKey];
    }
    
    return defaultValue;
}

std::string Config::getStringCat(const std::string& category, const std::string& key, const std::string& defaultValue) const {
    std::string fullKey = getCategoryKey(category, key);
    
    if (configData.contains(fullKey) && configData[fullKey].is_string()) {
        return configData[fullKey];
    }
    
    return defaultValue;
}

void Config::setBoolCat(const std::string& category, const std::string& key, bool value) {
    std::string fullKey = getCategoryKey(category, key);
    configData[fullKey] = value;
    modified = true;
}

void Config::setIntCat(const std::string& category, const std::string& key, int value) {
    std::string fullKey = getCategoryKey(category, key);
    configData[fullKey] = value;
    modified = true;
}

void Config::setFloatCat(const std::string& category, const std::string& key, float value) {
    std::string fullKey = getCategoryKey(category, key);
    configData[fullKey] = value;
    modified = true;
}

void Config::setDoubleCat(const std::string& category, const std::string& key, double value) {
    std::string fullKey = getCategoryKey(category, key);
    configData[fullKey] = value;
    modified = true;
}

void Config::setStringCat(const std::string& category, const std::string& key, const std::string& value) {
    std::string fullKey = getCategoryKey(category, key);
    configData[fullKey] = value;
    modified = true;
}

std::vector<std::string> Config::getKeys() const {
    std::vector<std::string> keys;
    
    for (auto& [key, value] : configData.items()) {
        keys.push_back(key);
    }
    
    return keys;
}

std::vector<std::string> Config::getCategories() const {
    std::vector<std::string> categories;
    std::set<std::string> uniqueCategories;
    
    for (auto& [key, value] : configData.items()) {
        size_t dotPos = key.find('.');
        if (dotPos != std::string::npos) {
            uniqueCategories.insert(key.substr(0, dotPos));
        }
    }
    
    categories.assign(uniqueCategories.begin(), uniqueCategories.end());
    return categories;
}

std::vector<std::string> Config::getKeysInCategory(const std::string& category) const {
    std::vector<std::string> keys;
    std::string prefix = category + ".";
    
    for (auto& [key, value] : configData.items()) {
        if (key.find(prefix) == 0) {
            keys.push_back(key.substr(prefix.length()));
        }
    }
    
    return keys;
}

bool Config::loadProfile(const std::string& profileName) {
    return load("profile_" + profileName + ".json");
}

bool Config::saveProfile(const std::string& profileName) {
    return save("profile_" + profileName + ".json");
}

std::vector<std::string> Config::getProfiles() const {
    std::vector<std::string> profiles;
    
    for (const auto& entry : fs::directory_iterator(configPath)) {
        std::string filename = entry.path().filename().string();
        if (filename.find("profile_") == 0 && filename.find(".json") != std::string::npos) {
            std::string profileName = filename.substr(8); // Usuń "profile_"
            profileName = profileName.substr(0, profileName.length() - 5); // Usuń ".json"
            profiles.push_back(profileName);
        }
    }
    
    return profiles;
}

bool Config::deleteProfile(const std::string& profileName) {
    try {
        std::string filename = configPath + "profile_" + profileName + ".json";
        if (fs::exists(filename)) {
            fs::remove(filename);
            return true;
        }
    } catch (const std::exception& e) {
        LOG_ERROR("Błąd usuwania profilu: " + std::string(e.what()));
    }
    return false;
}

bool Config::validate() const {
    // Sprawdź wymagane klucze
    std::vector<std::string> requiredKeys = {
        "graphics.resolution_width",
        "graphics.resolution_height",
        "graphics.fullscreen",
        "audio.master_volume",
        "gameplay.difficulty",
        "paths.data",
        "paths.saves"
    };
    
    for (const auto& key : requiredKeys) {
        if (!configData.contains(key)) {
            return false;
        }
    }
    
    return true;
}

std::vector<std::string> Config::getValidationErrors() const {
    std::vector<std::string> errors;
    
    // Sprawdź rozdzielczość
    int width = getIntCat("graphics", "resolution_width", 0);
    int height = getIntCat("graphics", "resolution_height", 0);
    if (width < 800 || height < 600) {
        errors.push_back("Nieprawidłowa rozdzielczość");
    }
    
    // Sprawdź głośność
    float volume = getFloatCat("audio", "master_volume", -1);
    if (volume < 0 || volume > 1) {
        errors.push_back("Nieprawidłowa głośność");
    }
    
    // Sprawdź ścieżki
    std::string dataPath = getStringCat("paths", "data", "");
    if (dataPath.empty() || !fs::exists(dataPath)) {
        errors.push_back("Nieprawidłowa ścieżka danych");
    }
    
    return errors;
}

void Config::loadDefaults() {
    setDefaultGraphics();
    setDefaultAudio();
    setDefaultGameplay();
    setDefaultControls();
    setDefaultPaths();
}

void Config::setDefaultGraphics() {
    setStringCat("graphics", "renderer", "OpenGL");
    setIntCat("graphics", "resolution_width", 1280);
    setIntCat("graphics", "resolution_height", 800);
    setBoolCat("graphics", "fullscreen", false);
    setBoolCat("graphics", "vsync", true);
    setIntCat("graphics", "fps_limit", 60);
    setIntCat("graphics", "antialiasing", 2);
    setFloatCat("graphics", "render_scale", 1.0f);
    
    // Jakość
    setStringCat("graphics", "quality_preset", "medium");
    setBoolCat("graphics", "shadows", true);
    setIntCat("graphics", "shadow_quality", 2);
    setBoolCat("graphics", "reflections", true);
    setBoolCat("graphics", "post_processing", true);
    setIntCat("graphics", "texture_quality", 2);
    setIntCat("graphics", "model_quality", 2);
    
    // Mapa
    setBoolCat("graphics", "map_smooth_zoom", true);
    setBoolCat("graphics", "map_antialiasing", true);
    setIntCat("graphics", "map_cache_size", 256);
}

void Config::setDefaultAudio() {
    setFloatCat("audio", "master_volume", 0.8f);
    setFloatCat("audio", "effects_volume", 0.7f);
    setFloatCat("audio", "music_volume", 0.5f);
    setFloatCat("audio", "ui_volume", 0.6f);
    setFloatCat("audio", "ambient_volume", 0.4f);
    
    setBoolCat("audio", "enable_3d_sound", true);
    setStringCat("audio", "output_device", "default");
    setIntCat("audio", "sample_rate", 44100);
    setIntCat("audio", "channels", 2);
}

void Config::setDefaultGameplay() {
    setStringCat("gameplay", "difficulty", "normal");
    setBoolCat("gameplay", "tutorial_enabled", true);
    setBoolCat("gameplay", "autosave_enabled", true);
    setIntCat("gameplay", "autosave_interval", 5);
    setBoolCat("gameplay", "pause_on_event", true);
    setBoolCat("gameplay", "show_hints", true);
    setFloatCat("gameplay", "game_speed", 1.0f);
    
    // Ekonomia
    setFloatCat("gameplay", "money_multiplier", 1.0f);
    setFloatCat("gameplay", "passenger_multiplier", 1.0f);
    setFloatCat("gameplay", "maintenance_cost_multiplier", 1.0f);
    
    // Wydarzenia
    setBoolCat("gameplay", "random_events", true);
    setFloatCat("gameplay", "event_frequency", 1.0f);
    setBoolCat("gameplay", "weather_effects", true);
    setBoolCat("gameplay", "realistic_breakdowns", true);
    
    // AI
    setBoolCat("gameplay", "ai_competitors", false);
    setIntCat("gameplay", "ai_difficulty", 2);
}

void Config::setDefaultControls() {
    // Kamera
    setFloatCat("controls", "camera_sensitivity", 1.0f);
    setBoolCat("controls", "camera_invert_y", false);
    setBoolCat("controls", "camera_smooth", true);
    setFloatCat("controls", "zoom_speed", 1.0f);
    setFloatCat("controls", "pan_speed", 1.0f);
    
    // Mysz
    setBoolCat("controls", "edge_scrolling", true);
    setIntCat("controls", "edge_scroll_speed", 20);
    setBoolCat("controls", "middle_button_pan", true);
    
    // Klawiatura - mapy klawiszy
    setStringCat("controls.keys", "pause", "Space");
    setStringCat("controls.keys", "speed_1x", "1");
    setStringCat("controls.keys", "speed_2x", "2");
    setStringCat("controls.keys", "speed_5x", "3");
    setStringCat("controls.keys", "speed_10x", "4");
    setStringCat("controls.keys", "zoom_in", "Plus");
    setStringCat("controls.keys", "zoom_out", "Minus");
    setStringCat("controls.keys", "reset_view", "0");
    setStringCat("controls.keys", "screenshot", "F12");
    setStringCat("controls.keys", "quick_save", "F5");
    setStringCat("controls.keys", "quick_load", "F9");
}

void Config::setDefaultPaths() {
    setStringCat("paths", "data", "data/");
    setStringCat("paths", "saves", "saves/");
    setStringCat("paths", "mods", "mods/");
    setStringCat("paths", "screenshots", "screenshots/");
    setStringCat("paths", "logs", "logs/");
    setStringCat("paths", "cache", "cache/");
}

std::string Config::getCategoryKey(const std::string& category, const std::string& key) const {
    return category + "." + key;
}

ConfigValue Config::jsonToConfigValue(const json& j) const {
    if (j.is_boolean()) return j.get<bool>();
    if (j.is_number_integer()) return j.get<int>();
    if (j.is_number_float()) return j.get<float>();
    if (j.is_string()) return j.get<std::string>();
    return 0;
}

json Config::configValueToJson(const ConfigValue& value) const {
    return std::visit([](auto&& arg) -> json {
        return arg;
    }, value);
}