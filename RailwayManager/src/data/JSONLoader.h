#ifndef JSONLOADER_H
#define JSONLOADER_H

#include <string>
#include <nlohmann/json.hpp>

class JSONLoader {
public:
    JSONLoader();
    ~JSONLoader();
    
    // Załaduj plik JSON
    nlohmann::json load(const std::string& filename);
    
    // Załaduj z walidacją schematu
    nlohmann::json loadWithSchema(const std::string& filename, const std::string& schemaFile);
    
    // Zapisz do pliku JSON
    bool save(const std::string& filename, const nlohmann::json& data, int indent = 2);
    
    // Walidacja
    bool validate(const nlohmann::json& data, const nlohmann::json& schema);
    
    // Pomocnicze
    static nlohmann::json merge(const nlohmann::json& base, const nlohmann::json& overlay);
    
    // Obsługa błędów
    std::string getLastError() const { return lastError; }
    bool hasError() const { return !lastError.empty(); }
    
private:
    std::string lastError;
    
    void clearError() { lastError.clear(); }
    void setError(const std::string& error) { lastError = error; }
};

#endif // JSONLOADER_H