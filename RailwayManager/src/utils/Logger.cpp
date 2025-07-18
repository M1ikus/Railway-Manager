#include "Logger.h"
#include <iostream>
#include <iomanip>
#include <sstream>

Logger& Logger::getInstance() {
    static Logger instance;
    return instance;
}

Logger::Logger() {
    // Konstruktor prywatny dla singletona
}

Logger::~Logger() {
    if (logFile.is_open()) {
        logFile.close();
    }
}

void Logger::init(const std::string& filename) {
    std::lock_guard<std::mutex> lock(logMutex);
    
    if (logFile.is_open()) {
        logFile.close();
    }
    
    logFile.open(filename, std::ios::out | std::ios::app);
    if (!logFile.is_open()) {
        std::cerr << "Nie można otworzyć pliku logów: " << filename << std::endl;
    }
}

void Logger::log(LogLevel level, const std::string& message) {
    if (level < minLevel) {
        return;
    }
    
    std::lock_guard<std::mutex> lock(logMutex);
    
    std::string timestamp = getCurrentTimestamp();
    std::string levelStr = levelToString(level);
    std::string logLine = "[" + timestamp + "] [" + levelStr + "] " + message;
    
    // Zapis do pliku
    if (logFile.is_open()) {
        logFile << logLine << std::endl;
        logFile.flush();
    }
    
    // Wyświetlanie w konsoli
    if (consoleOutput) {
        switch (level) {
            case LogLevel::ERROR:
            case LogLevel::CRITICAL:
                std::cerr << logLine << std::endl;
                break;
            default:
                std::cout << logLine << std::endl;
                break;
        }
    }
}

std::string Logger::getCurrentTimestamp() {
    auto now = std::chrono::system_clock::now();
    auto time_t = std::chrono::system_clock::to_time_t(now);
    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        now.time_since_epoch()) % 1000;
    
    std::stringstream ss;
    ss << std::put_time(std::localtime(&time_t), "%Y-%m-%d %H:%M:%S");
    ss << '.' << std::setfill('0') << std::setw(3) << ms.count();
    
    return ss.str();
}

std::string Logger::levelToString(LogLevel level) {
    switch (level) {
        case LogLevel::DEBUG:    return "DEBUG";
        case LogLevel::INFO:     return "INFO";
        case LogLevel::WARNING:  return "WARNING";
        case LogLevel::ERROR:    return "ERROR";
        case LogLevel::CRITICAL: return "CRITICAL";
        default:                 return "UNKNOWN";
    }
}