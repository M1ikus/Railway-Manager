#ifndef LOGGER_H
#define LOGGER_H

#include <string>
#include <fstream>
#include <mutex>
#include <chrono>

enum class LogLevel {
    DEBUG,
    INFO,
    WARNING,
    ERROR,
    CRITICAL
};

class Logger {
public:
    // Singleton
    static Logger& getInstance();
    
    // Usuń konstruktor kopiujący i operator przypisania
    Logger(const Logger&) = delete;
    Logger& operator=(const Logger&) = delete;
    
    // Inicjalizacja loggera
    void init(const std::string& filename);
    
    // Logowanie
    void log(LogLevel level, const std::string& message);
    
    // Ustawienia
    void setLogLevel(LogLevel level) { minLevel = level; }
    void enableConsoleOutput(bool enable) { consoleOutput = enable; }
    
    // Pomocnicze metody
    void debug(const std::string& msg) { log(LogLevel::DEBUG, msg); }
    void info(const std::string& msg) { log(LogLevel::INFO, msg); }
    void warning(const std::string& msg) { log(LogLevel::WARNING, msg); }
    void error(const std::string& msg) { log(LogLevel::ERROR, msg); }
    void critical(const std::string& msg) { log(LogLevel::CRITICAL, msg); }
    
private:
    Logger();
    ~Logger();
    
    std::string getCurrentTimestamp();
    std::string levelToString(LogLevel level);
    
    std::ofstream logFile;
    std::mutex logMutex;
    LogLevel minLevel = LogLevel::INFO;
    bool consoleOutput = true;
};

// Makra pomocnicze
#define LOG_DEBUG(msg) Logger::getInstance().debug(msg)
#define LOG_INFO(msg) Logger::getInstance().info(msg)
#define LOG_WARNING(msg) Logger::getInstance().warning(msg)
#define LOG_ERROR(msg) Logger::getInstance().error(msg)
#define LOG_CRITICAL(msg) Logger::getInstance().critical(msg)

#endif // LOGGER_H