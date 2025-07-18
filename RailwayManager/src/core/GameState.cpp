#include "GameState.h"
#include "models/Station.h"
#include "models/Train.h"
#include "models/Line.h"
#include "models/Personnel.h"
#include "models/Timetable.h"
#include "utils/Logger.h"

GameState::GameState() {
    reset();
}

GameState::~GameState() {
    // Destruktor - smart pointery automatycznie zwolnią pamięć
}

void GameState::reset() {
    // Reset czasu
    currentDate = {2024, 1, 1, 6, 0}; // 1 stycznia 2024, 6:00
    gameTimeElapsed = 0.0f;
    timeAccumulator = 0.0f;
    
    // Reset firmy
    companyInfo = {
        "Nowa Firma Kolejowa",
        "",
        2024,
        50 // Neutralna reputacja
    };
    money = 1000000.0; // 1 milion PLN startowego kapitału
    
    // Wyczyść kolekcje
    stations.clear();
    stationMap.clear();
    trains.clear();
    trainMap.clear();
    lines.clear();
    lineMap.clear();
    personnel.clear();
    personnelMap.clear();
    timetables.clear();
    timetableMap.clear();
    
    // Reset statystyk
    statistics = Statistics{};
    
    // Domyślne ustawienia
    settings = Settings{};
    
    LOG_INFO("Stan gry zresetowany");
}

void GameState::setCurrentDate(int year, int month, int day) {
    currentDate.year = year;
    currentDate.month = month;
    currentDate.day = day;
    currentDate.hour = 6;
    currentDate.minute = 0;
}

void GameState::updateTime(float deltaTime) {
    gameTimeElapsed += deltaTime;
    timeAccumulator += deltaTime;
    
    // 1 sekunda rzeczywista = 1 minuta w grze (przy normalnej prędkości)
    const float SECONDS_TO_MINUTES = 1.0f;
    
    while (timeAccumulator >= SECONDS_TO_MINUTES) {
        currentDate.minute++;
        
        if (currentDate.minute >= 60) {
            currentDate.minute = 0;
            currentDate.hour++;
            
            if (currentDate.hour >= 24) {
                currentDate.hour = 0;
                currentDate.day++;
                
                // Sprawdź liczbę dni w miesiącu
                int daysInMonth = 31;
                if (currentDate.month == 2) {
                    // Luty - sprawdź czy rok przestępny
                    bool isLeapYear = (currentDate.year % 4 == 0 && currentDate.year % 100 != 0) 
                                    || (currentDate.year % 400 == 0);
                    daysInMonth = isLeapYear ? 29 : 28;
                } else if (currentDate.month == 4 || currentDate.month == 6 || 
                          currentDate.month == 9 || currentDate.month == 11) {
                    daysInMonth = 30;
                }
                
                if (currentDate.day > daysInMonth) {
                    currentDate.day = 1;
                    currentDate.month++;
                    
                    if (currentDate.month > 12) {
                        currentDate.month = 1;
                        currentDate.year++;
                    }
                }
            }
        }
        
        timeAccumulator -= SECONDS_TO_MINUTES;
    }
}

// Stacje
void GameState::addStation(std::shared_ptr<Station> station) {
    if (!station || station->getId().empty()) {
        LOG_ERROR("Próba dodania nieprawidłowej stacji");
        return;
    }
    
    stations.push_back(station);
    stationMap[station->getId()] = station;
}

void GameState::removeStation(const std::string& stationId) {
    auto it = stationMap.find(stationId);
    if (it != stationMap.end()) {
        stationMap.erase(it);
        stations.erase(
            std::remove_if(stations.begin(), stations.end(),
                [&stationId](const auto& s) { return s->getId() == stationId; }),
            stations.end()
        );
    }
}

std::shared_ptr<Station> GameState::getStation(const std::string& stationId) const {
    auto it = stationMap.find(stationId);
    return (it != stationMap.end()) ? it->second : nullptr;
}

// Pociągi
void GameState::addTrain(std::shared_ptr<Train> train) {
    if (!train || train->getId().empty()) {
        LOG_ERROR("Próba dodania nieprawidłowego pociągu");
        return;
    }
    
    trains.push_back(train);
    trainMap[train->getId()] = train;
    statistics.totalTrainsOwned++;
}

void GameState::removeTrain(const std::string& trainId) {
    auto it = trainMap.find(trainId);
    if (it != trainMap.end()) {
        trainMap.erase(it);
        trains.erase(
            std::remove_if(trains.begin(), trains.end(),
                [&trainId](const auto& t) { return t->getId() == trainId; }),
            trains.end()
        );
    }
}

std::shared_ptr<Train> GameState::getTrain(const std::string& trainId) const {
    auto it = trainMap.find(trainId);
    return (it != trainMap.end()) ? it->second : nullptr;
}

std::vector<std::shared_ptr<Train>> GameState::getAvailableTrains() const {
    std::vector<std::shared_ptr<Train>> available;
    for (const auto& train : trains) {
        if (train->isAvailable()) {
            available.push_back(train);
        }
    }
    return available;
}

// Linie
void GameState::addLine(std::shared_ptr<Line> line) {
    if (!line || line->getId().empty()) {
        LOG_ERROR("Próba dodania nieprawidłowej linii");
        return;
    }
    
    lines.push_back(line);
    lineMap[line->getId()] = line;
}

void GameState::removeLine(const std::string& lineId) {
    auto it = lineMap.find(lineId);
    if (it != lineMap.end()) {
        lineMap.erase(it);
        lines.erase(
            std::remove_if(lines.begin(), lines.end(),
                [&lineId](const auto& l) { return l->getId() == lineId; }),
            lines.end()
        );
    }
}

std::shared_ptr<Line> GameState::getLine(const std::string& lineId) const {
    auto it = lineMap.find(lineId);
    return (it != lineMap.end()) ? it->second : nullptr;
}

// Personel
void GameState::addPersonnel(std::shared_ptr<Personnel> person) {
    if (!person || person->getId().empty()) {
        LOG_ERROR("Próba dodania nieprawidłowego pracownika");
        return;
    }
    
    personnel.push_back(person);
    personnelMap[person->getId()] = person;
    statistics.totalPersonnelHired++;
}

void GameState::removePersonnel(const std::string& personId) {
    auto it = personnelMap.find(personId);
    if (it != personnelMap.end()) {
        personnelMap.erase(it);
        personnel.erase(
            std::remove_if(personnel.begin(), personnel.end(),
                [&personId](const auto& p) { return p->getId() == personId; }),
            personnel.end()
        );
    }
}

std::shared_ptr<Personnel> GameState::getPersonnel(const std::string& personId) const {
    auto it = personnelMap.find(personId);
    return (it != personnelMap.end()) ? it->second : nullptr;
}

std::vector<std::shared_ptr<Personnel>> GameState::getAvailablePersonnel(const std::string& role) const {
    std::vector<std::shared_ptr<Personnel>> available;
    for (const auto& person : personnel) {
        if (person->getRole() == role && person->isAvailable()) {
            available.push_back(person);
        }
    }
    return available;
}

// Rozkłady
void GameState::addTimetable(std::shared_ptr<Timetable> timetable) {
    if (!timetable || timetable->getId().empty()) {
        LOG_ERROR("Próba dodania nieprawidłowego rozkładu");
        return;
    }
    
    timetables.push_back(timetable);
    timetableMap[timetable->getId()] = timetable;
}

void GameState::removeTimetable(const std::string& timetableId) {
    auto it = timetableMap.find(timetableId);
    if (it != timetableMap.end()) {
        timetableMap.erase(it);
        timetables.erase(
            std::remove_if(timetables.begin(), timetables.end(),
                [&timetableId](const auto& t) { return t->getId() == timetableId; }),
            timetables.end()
        );
    }
}

std::shared_ptr<Timetable> GameState::getTimetable(const std::string& timetableId) const {
    auto it = timetableMap.find(timetableId);
    return (it != timetableMap.end()) ? it->second : nullptr;
}