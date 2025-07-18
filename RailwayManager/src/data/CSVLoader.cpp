#include "CSVLoader.h"
#include "utils/Logger.h"
#include <fstream>
#include <sstream>
#include <algorithm>

CSVLoader::CSVLoader() {
}

CSVLoader::~CSVLoader() {
}

std::vector<std::vector<std::string>> CSVLoader::load(const std::string& filename, const Options& options) {
    std::vector<std::vector<std::string>> data;
    
    std::ifstream file(filename);
    if (!file.is_open()) {
        LOG_ERROR("Nie można otworzyć pliku CSV: " + filename);
        return data;
    }
    
    std::string line;
    int lineNumber = 0;
    
    while (std::getline(file, line)) {
        lineNumber++;
        
        // Pomiń puste linie jeśli opcja włączona
        if (options.skipEmptyLines && line.empty()) {
            continue;
        }
        
        // Parsuj linię
        auto row = parseLine(line, options);
        
        // Pomiń puste wiersze
        if (options.skipEmptyLines && row.empty()) {
            continue;
        }
        
        data.push_back(row);
    }
    
    file.close();
    
    LOG_INFO("Wczytano " + std::to_string(data.size()) + " wierszy z pliku: " + filename);
    return data;
}

std::vector<std::unordered_map<std::string, std::string>> CSVLoader::loadWithHeaders(
    const std::string& filename, const Options& options) {
    
    std::vector<std::unordered_map<std::string, std::string>> result;
    
    auto data = load(filename, options);
    if (data.empty()) {
        return result;
    }
    
    // Pierwszy wiersz to nagłówki
    std::vector<std::string> headers;
    if (options.hasHeader && !data.empty()) {
        headers = data[0];
        data.erase(data.begin());
    } else {
        // Generuj domyślne nagłówki
        if (!data.empty()) {
            for (size_t i = 0; i < data[0].size(); ++i) {
                headers.push_back("column_" + std::to_string(i));
            }
        }
    }
    
    // Konwertuj wiersze na mapy
    for (const auto& row : data) {
        std::unordered_map<std::string, std::string> rowMap;
        
        for (size_t i = 0; i < headers.size() && i < row.size(); ++i) {
            rowMap[headers[i]] = row[i];
        }
        
        result.push_back(rowMap);
    }
    
    return result;
}

bool CSVLoader::save(const std::string& filename, const std::vector<std::vector<std::string>>& data, 
                     const Options& options) {
    std::ofstream file(filename);
    if (!file.is_open()) {
        LOG_ERROR("Nie można utworzyć pliku CSV: " + filename);
        return false;
    }
    
    for (const auto& row : data) {
        for (size_t i = 0; i < row.size(); ++i) {
            if (i > 0) {
                file << options.delimiter;
            }
            file << escapeCSV(row[i], options.delimiter, options.quote);
        }
        file << "\n";
    }
    
    file.close();
    LOG_INFO("Zapisano " + std::to_string(data.size()) + " wierszy do pliku: " + filename);
    return true;
}

bool CSVLoader::saveWithHeaders(const std::string& filename, 
                               const std::vector<std::string>& headers,
                               const std::vector<std::vector<std::string>>& data, 
                               const Options& options) {
    std::vector<std::vector<std::string>> fullData;
    fullData.push_back(headers);
    fullData.insert(fullData.end(), data.begin(), data.end());
    
    return save(filename, fullData, options);
}

std::vector<std::string> CSVLoader::parseLine(const std::string& line, const Options& options) {
    std::vector<std::string> result;
    std::string currentField;
    bool inQuotes = false;
    bool fieldStarted = false;
    
    for (size_t i = 0; i < line.length(); ++i) {
        char ch = line[i];
        
        if (inQuotes) {
            if (ch == options.quote) {
                // Sprawdź czy to podwójny cudzysłów (escape)
                if (i + 1 < line.length() && line[i + 1] == options.quote) {
                    currentField += options.quote;
                    i++; // Pomiń następny cudzysłów
                } else {
                    inQuotes = false;
                }
            } else {
                currentField += ch;
            }
        } else {
            if (ch == options.quote && (!fieldStarted || currentField.empty())) {
                inQuotes = true;
                fieldStarted = true;
            } else if (ch == options.delimiter) {
                // Koniec pola
                if (options.trimSpaces) {
                    currentField = trim(currentField);
                }
                result.push_back(currentField);
                currentField.clear();
                fieldStarted = false;
            } else {
                currentField += ch;
                fieldStarted = true;
            }
        }
    }
    
    // Dodaj ostatnie pole
    if (options.trimSpaces) {
        currentField = trim(currentField);
    }
    result.push_back(currentField);
    
    return result;
}

std::string CSVLoader::trim(const std::string& str) {
    size_t first = str.find_first_not_of(" \t\r\n");
    if (first == std::string::npos) {
        return "";
    }
    size_t last = str.find_last_not_of(" \t\r\n");
    return str.substr(first, (last - first + 1));
}

std::string CSVLoader::escapeCSV(const std::string& value, char delimiter, char quote) {
    // Sprawdź czy wartość wymaga cytowania
    bool needsQuoting = false;
    if (value.find(delimiter) != std::string::npos ||
        value.find(quote) != std::string::npos ||
        value.find('\n') != std::string::npos ||
        value.find('\r') != std::string::npos) {
        needsQuoting = true;
    }
    
    if (!needsQuoting) {
        return value;
    }
    
    // Cytuj i escapuj
    std::string result;
    result += quote;
    
    for (char ch : value) {
        if (ch == quote) {
            result += quote; // Podwójny cudzysłów jako escape
        }
        result += ch;
    }
    
    result += quote;
    return result;
}

std::string CSVLoader::unescapeCSV(const std::string& value, char quote) {
    if (value.length() < 2) {
        return value;
    }
    
    // Sprawdź czy wartość jest cytowana
    if (value.front() != quote || value.back() != quote) {
        return value;
    }
    
    // Usuń zewnętrzne cudzysłowy
    std::string result = value.substr(1, value.length() - 2);
    
    // Zamień podwójne cudzysłowy na pojedyncze
    std::string unescaped;
    for (size_t i = 0; i < result.length(); ++i) {
        if (result[i] == quote && i + 1 < result.length() && result[i + 1] == quote) {
            unescaped += quote;
            i++; // Pomiń następny cudzysłów
        } else {
            unescaped += result[i];
        }
    }
    
    return unescaped;
}