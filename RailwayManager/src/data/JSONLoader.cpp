#include "JSONLoader.h"
#include "utils/Logger.h"
#include <fstream>
#include <iomanip>

using json = nlohmann::json;

JSONLoader::JSONLoader() {
}

JSONLoader::~JSONLoader() {
}

json JSONLoader::load(const std::string& filename) {
    clearError();
    
    std::ifstream file(filename);
    if (!file.is_open()) {
        setError("Nie można otworzyć pliku JSON: " + filename);
        LOG_ERROR(lastError);
        return json();
    }
    
    json data;
    try {
        file >> data;
        LOG_INFO("Wczytano plik JSON: " + filename);
    } catch (const json::parse_error& e) {
        setError("Błąd parsowania JSON w pliku " + filename + ": " + e.what());
        LOG_ERROR(lastError);
        return json();
    } catch (const std::exception& e) {
        setError("Błąd wczytywania pliku " + filename + ": " + e.what());
        LOG_ERROR(lastError);
        return json();
    }
    
    file.close();
    return data;
}

json JSONLoader::loadWithSchema(const std::string& filename, const std::string& schemaFile) {
    // Wczytaj dane
    json data = load(filename);
    if (hasError()) {
        return json();
    }
    
    // Wczytaj schemat
    json schema = load(schemaFile);
    if (hasError()) {
        return json();
    }
    
    // Waliduj
    if (!validate(data, schema)) {
        return json();
    }
    
    return data;
}

bool JSONLoader::save(const std::string& filename, const json& data, int indent) {
    clearError();
    
    std::ofstream file(filename);
    if (!file.is_open()) {
        setError("Nie można utworzyć pliku JSON: " + filename);
        LOG_ERROR(lastError);
        return false;
    }
    
    try {
        if (indent >= 0) {
            file << std::setw(indent) << data << std::endl;
        } else {
            file << data << std::endl;
        }
        LOG_INFO("Zapisano plik JSON: " + filename);
    } catch (const std::exception& e) {
        setError("Błąd zapisywania pliku " + filename + ": " + e.what());
        LOG_ERROR(lastError);
        file.close();
        return false;
    }
    
    file.close();
    return true;
}

bool JSONLoader::validate(const json& data, const json& schema) {
    clearError();
    
    // Podstawowa walidacja typów
    try {
        if (schema.contains("type")) {
            std::string expectedType = schema["type"];
            
            if (expectedType == "object" && !data.is_object()) {
                setError("Oczekiwano obiektu");
                return false;
            } else if (expectedType == "array" && !data.is_array()) {
                setError("Oczekiwano tablicy");
                return false;
            } else if (expectedType == "string" && !data.is_string()) {
                setError("Oczekiwano stringa");
                return false;
            } else if (expectedType == "number" && !data.is_number()) {
                setError("Oczekiwano liczby");
                return false;
            } else if (expectedType == "boolean" && !data.is_boolean()) {
                setError("Oczekiwano wartości logicznej");
                return false;
            }
        }
        
        // Sprawdź wymagane pola dla obiektów
        if (schema.contains("required") && data.is_object()) {
            for (const auto& required : schema["required"]) {
                std::string field = required;
                if (!data.contains(field)) {
                    setError("Brak wymaganego pola: " + field);
                    return false;
                }
            }
        }
        
        // Sprawdź właściwości obiektu
        if (schema.contains("properties") && data.is_object()) {
            const auto& properties = schema["properties"];
            
            for (const auto& [key, value] : data.items()) {
                if (properties.contains(key)) {
                    // Rekurencyjna walidacja
                    if (!validate(value, properties[key])) {
                        setError("Błąd walidacji pola '" + key + "': " + lastError);
                        return false;
                    }
                } else if (schema.contains("additionalProperties") && 
                          schema["additionalProperties"].is_boolean() && 
                          !schema["additionalProperties"]) {
                    setError("Nieoczekiwane pole: " + key);
                    return false;
                }
            }
        }
        
        // Sprawdź elementy tablicy
        if (schema.contains("items") && data.is_array()) {
            const auto& itemSchema = schema["items"];
            
            for (size_t i = 0; i < data.size(); ++i) {
                if (!validate(data[i], itemSchema)) {
                    setError("Błąd walidacji elementu tablicy [" + std::to_string(i) + "]: " + lastError);
                    return false;
                }
            }
        }
        
        // Sprawdź minimum/maksimum dla liczb
        if (data.is_number()) {
            if (schema.contains("minimum")) {
                double min = schema["minimum"];
                if (data.get<double>() < min) {
                    setError("Wartość poniżej minimum: " + std::to_string(min));
                    return false;
                }
            }
            if (schema.contains("maximum")) {
                double max = schema["maximum"];
                if (data.get<double>() > max) {
                    setError("Wartość powyżej maksimum: " + std::to_string(max));
                    return false;
                }
            }
        }
        
        // Sprawdź długość stringów
        if (data.is_string()) {
            std::string str = data;
            if (schema.contains("minLength") && str.length() < schema["minLength"].get<size_t>()) {
                setError("String za krótki");
                return false;
            }
            if (schema.contains("maxLength") && str.length() > schema["maxLength"].get<size_t>()) {
                setError("String za długi");
                return false;
            }
        }
        
        // Sprawdź enum
        if (schema.contains("enum")) {
            bool found = false;
            for (const auto& validValue : schema["enum"]) {
                if (data == validValue) {
                    found = true;
                    break;
                }
            }
            if (!found) {
                setError("Wartość nie znajduje się w dozwolonych wartościach");
                return false;
            }
        }
        
    } catch (const std::exception& e) {
        setError("Błąd podczas walidacji: " + std::string(e.what()));
        return false;
    }
    
    return true;
}

json JSONLoader::merge(const json& base, const json& overlay) {
    json result = base;
    
    // Rekurencyjne łączenie
    if (base.is_object() && overlay.is_object()) {
        for (auto& [key, value] : overlay.items()) {
            if (result.contains(key) && result[key].is_object() && value.is_object()) {
                // Rekurencyjne łączenie obiektów
                result[key] = merge(result[key], value);
            } else {
                // Nadpisz wartość
                result[key] = value;
            }
        }
    } else if (base.is_array() && overlay.is_array()) {
        // Dla tablic - dodaj elementy z overlay
        for (const auto& item : overlay) {
            result.push_back(item);
        }
    } else {
        // Dla innych typów - nadpisz
        result = overlay;
    }
    
    return result;
}