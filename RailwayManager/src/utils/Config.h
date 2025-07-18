#ifndef CONFIG_H
#define CONFIG_H

#include <string>
#include <unordered_map>
#include <variant>
#include <vector>
#include <nlohmann/json.hpp>

using ConfigValue = std::variant<bool, int, float, double, std::string>;

class Config {
public:
    // Singleton
    static Config& getInstance();
    
    // Usuń konstruktor kopiujący i operator przypisania
    Config(const Config&) = delete;
    Config& operator=(const Config&) = delete;
    
    // Ładowanie i zapisywanie
    bool load(const std::string& filename = "config.json");
    bool save(const std::string& filename = "config.json");
    void reset();
    
    // Gettery
    bool getBool(const std::string& key, bool defaultValue = false) const;
    int getInt(const std::string& key, int defaultValue = 0) const;
    float getFloat(const std::string& key, float defaultValue = 0.0f) const;
    double getDouble(const std::string& key, double defaultValue = 0.0) const;
    std::string getString(const std::string& key, const std::string& defaultValue = "") const;
    
    // Settery
    void setBool(const std::string& key, bool value);
    void setInt(const std::string& key, int value);
    void setFloat(const std::string& key, float value);
    void setDouble(const std::string& key, double value);
    void setString(const std::string& key, const std::string& value);
    
    // Sprawdzanie istnienia
    bool hasKey(const std::string& key) const;
    
    // Kategorie konfiguracji
    void setCategory(const std::string& category);
    std::string getCategory() const { return currentCategory; }
    
    // Pobieranie z kategorii
    bool getBoolCat(const std::string& category, const std::string& key, bool defaultValue = false) const;
    int getIntCat(const std::string& category, const std::string& key, int defaultValue = 0) const;
    float getFloatCat(const std::string& category, const std::string& key, float defaultValue = 0.0f) const;
    double getDoubleCat(const std::string& category, const std::string& key, double defaultValue = 0.0) const;
    std::string getStringCat(const std::string& category, const std::string& key, const std::string& defaultValue = "") const;
    
    // Ustawianie w kategorii
    void setBoolCat(const std::string& category, const std::string& key, bool value);
    void setIntCat(const std::string& category, const std::string& key, int value);
    void setFloatCat(const std::string& category, const std::string& key, float value);
    void setDoubleCat(const std::string& category, const std::string& key, double value);
    void setStringCat(const std::string& category, const std::string& key, const std::string& value);
    
    // Listy wartości
    std::vector<std::string> getKeys() const;
    std::vector<std::string> getCategories() const;
    std::vector<std::string> getKeysInCategory(const std::string& category) const;
    
    // Profile konfiguracji
    bool loadProfile(const std::string& profileName);
    bool saveProfile(const std::string& profileName);
    std::vector<std::string> getProfiles() const;
    bool deleteProfile(const std::string& profileName);
    
    // Walidacja
    bool validate() const;
    std::vector<std::string> getValidationErrors() const;
    
    // Domyślne wartości
    void loadDefaults();
    
private:
    Config();
    ~Config();
    
    nlohmann::json configData;
    std::string currentCategory = "general";
    std::string configPath = "config/";
    bool modified = false;
    
    // Pomocnicze
    std::string getCategoryKey(const std::string& category, const std::string& key) const;
    ConfigValue jsonToConfigValue(const nlohmann::json& j) const;
    nlohmann::json configValueToJson(const ConfigValue& value) const;
    
    // Domyślne ustawienia
    void setDefaultGraphics();
    void setDefaultAudio();
    void setDefaultGameplay();
    void setDefaultControls();
    void setDefaultPaths();
};

// Klasa pomocnicza do automatycznego ustawiania kategorii
class ConfigCategory {
public:
    ConfigCategory(Config& config, const std::string& category) 
        : config(config), previousCategory(config.getCategory()) {
        config.setCategory(category);
    }
    
    ~ConfigCategory() {
        config.setCategory(previousCategory);
    }
    
private:
    Config& config;
    std::string previousCategory;
};

// Makra pomocnicze
#define CONFIG Config::getInstance()
#define CONFIG_CAT(category) ConfigCategory _configCat(Config::getInstance(), category)

#endif // CONFIG_H