#ifndef CSVLOADER_H
#define CSVLOADER_H

#include <string>
#include <vector>
#include <unordered_map>

class CSVLoader {
public:
    CSVLoader();
    ~CSVLoader();
    
    // Opcje parsowania
    struct Options {
        char delimiter = ',';
        char quote = '"';
        bool hasHeader = true;
        bool trimSpaces = true;
        bool skipEmptyLines = true;
        std::string encoding = "UTF-8";
    };
    
    // Załaduj plik CSV
    std::vector<std::vector<std::string>> load(const std::string& filename, const Options& options = Options());
    
    // Załaduj z nagłówkami jako mapa
    std::vector<std::unordered_map<std::string, std::string>> loadWithHeaders(const std::string& filename, const Options& options = Options());
    
    // Zapisz dane do pliku CSV
    bool save(const std::string& filename, const std::vector<std::vector<std::string>>& data, const Options& options = Options());
    
    // Zapisz z nagłówkami
    bool saveWithHeaders(const std::string& filename, 
                        const std::vector<std::string>& headers,
                        const std::vector<std::vector<std::string>>& data, 
                        const Options& options = Options());
    
    // Pomocnicze
    static std::string escapeCSV(const std::string& value, char delimiter = ',', char quote = '"');
    static std::string unescapeCSV(const std::string& value, char quote = '"');
    
private:
    std::vector<std::string> parseLine(const std::string& line, const Options& options);
    std::string trim(const std::string& str);
};

#endif // CSVLOADER_H