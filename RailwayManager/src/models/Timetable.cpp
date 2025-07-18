#include "Timetable.h"
#include "utils/Logger.h"
#include <algorithm>
#include <sstream>
#include <iomanip>

Timetable::Timetable(const std::string& id, const std::string& name)
    : id(id), name(name), type(TimetableType::REGULAR), active(true),
      runningDays(TimetableDays::EVERYDAY) {
    
    // Ustaw domyślną ważność na rok
    validFrom = std::chrono::system_clock::now();
    validTo = validFrom + std::chrono::hours(24 * 365);
}

Timetable::~Timetable() {
}

bool Timetable::runsOnDay(int dayOfWeek) const {
    // dayOfWeek: 0 = niedziela, 1 = poniedziałek, itd.
    TimetableDays day;
    switch (dayOfWeek) {
        case 0: day = TimetableDays::SUNDAY; break;
        case 1: day = TimetableDays::MONDAY; break;
        case 2: day = TimetableDays::TUESDAY; break;
        case 3: day = TimetableDays::WEDNESDAY; break;
        case 4: day = TimetableDays::THURSDAY; break;
        case 5: day = TimetableDays::FRIDAY; break;
        case 6: day = TimetableDays::SATURDAY; break;
        default: return false;
    }
    
    return (runningDays & day) != TimetableDays::NONE;
}

bool Timetable::isValidOn(const std::chrono::system_clock::time_point& date) const {
    return date >= validFrom && date <= validTo;
}

void Timetable::addStop(const TimetableStop& stop) {
    stops.push_back(stop);
    sortStops();
}

void Timetable::insertStop(size_t index, const TimetableStop& stop) {
    if (index <= stops.size()) {
        stops.insert(stops.begin() + index, stop);
    }
}

void Timetable::removeStop(size_t index) {
    if (index < stops.size()) {
        stops.erase(stops.begin() + index);
    }
}

void Timetable::updateStop(size_t index, const TimetableStop& stop) {
    if (index < stops.size()) {
        stops[index] = stop;
        sortStops();
    }
}

TimetableStop* Timetable::getStop(size_t index) {
    return (index < stops.size()) ? &stops[index] : nullptr;
}

int Timetable::findStopIndex(const std::string& stationId) const {
    for (size_t i = 0; i < stops.size(); ++i) {
        if (stops[i].stationId == stationId) {
            return static_cast<int>(i);
        }
    }
    return -1;
}

TimetableStop* Timetable::findStop(const std::string& stationId) {
    int index = findStopIndex(stationId);
    return (index >= 0) ? &stops[index] : nullptr;
}

std::vector<TimetableStop> Timetable::getStopsBetween(const std::string& fromStation, 
                                                      const std::string& toStation) const {
    std::vector<TimetableStop> result;
    
    int fromIndex = findStopIndex(fromStation);
    int toIndex = findStopIndex(toStation);
    
    if (fromIndex >= 0 && toIndex >= 0 && fromIndex < toIndex) {
        for (int i = fromIndex; i <= toIndex; ++i) {
            result.push_back(stops[i]);
        }
    }
    
    return result;
}

int Timetable::getFirstDepartureTime() const {
    if (stops.empty()) return -1;
    return stops[0].departureTime;
}

int Timetable::getLastArrivalTime() const {
    if (stops.empty()) return -1;
    return stops.back().arrivalTime;
}

int Timetable::getTotalTravelTime() const {
    if (stops.size() < 2) return 0;
    return stops.back().arrivalTime - stops[0].departureTime;
}

int Timetable::getTravelTimeBetween(const std::string& fromStation, 
                                   const std::string& toStation) const {
    int fromIndex = findStopIndex(fromStation);
    int toIndex = findStopIndex(toStation);
    
    if (fromIndex >= 0 && toIndex >= 0 && fromIndex < toIndex) {
        return stops[toIndex].arrivalTime - stops[fromIndex].departureTime;
    }
    
    return -1;
}

std::vector<int> Timetable::getDepartureTimes() const {
    std::vector<int> times;
    
    if (frequency == 0) {
        // Pojedynczy kurs
        if (!stops.empty()) {
            times.push_back(stops[0].departureTime);
        }
    } else {
        // Wielokrotne kursy
        for (int time = firstRun; time <= lastRun; time += frequency) {
            times.push_back(time);
        }
    }
    
    return times;
}

void Timetable::createInstance(const std::chrono::system_clock::time_point& date) {
    // Sprawdź czy rozkład jest ważny w tym dniu
    if (!isValidOn(date)) {
        LOG_WARNING("Rozkład " + name + " nie jest ważny w podanym dniu");
        return;
    }
    
    // Sprawdź dzień tygodnia
    auto time_t = std::chrono::system_clock::to_time_t(date);
    std::tm* tm = std::localtime(&time_t);
    if (!runsOnDay(tm->tm_wday)) {
        LOG_INFO("Rozkład " + name + " nie kursuje w tym dniu tygodnia");
        return;
    }
    
    // Utwórz instancje
    auto departureTimes = getDepartureTimes();
    for (int departureTime : departureTimes) {
        TimetableInstance instance;
        instance.id = id + "_" + std::to_string(std::chrono::system_clock::to_time_t(date)) + 
                     "_" + std::to_string(departureTime);
        instance.date = date;
        instance.actualDepartureTime = departureTime;
        instance.delay = 0;
        instance.cancelled = false;
        instance.trainId = trainId;
        
        instances.push_back(instance);
        statistics.totalRuns++;
    }
}

void Timetable::cancelInstance(const std::string& instanceId) {
    auto it = std::find_if(instances.begin(), instances.end(),
        [&instanceId](const TimetableInstance& inst) { return inst.id == instanceId; });
    
    if (it != instances.end()) {
        it->cancelled = true;
        statistics.cancelledRuns++;
        LOG_INFO("Odwołano kurs: " + instanceId);
    }
}

TimetableInstance* Timetable::getInstance(const std::string& instanceId) {
    auto it = std::find_if(instances.begin(), instances.end(),
        [&instanceId](const TimetableInstance& inst) { return inst.id == instanceId; });
    
    return (it != instances.end()) ? &(*it) : nullptr;
}

std::vector<TimetableInstance> Timetable::getInstancesForDate(
    const std::chrono::system_clock::time_point& date) const {
    
    std::vector<TimetableInstance> result;
    
    // Porównaj tylko datę (bez czasu)
    auto dateOnly = std::chrono::floor<std::chrono::days>(date);
    
    for (const auto& instance : instances) {
        auto instanceDateOnly = std::chrono::floor<std::chrono::days>(instance.date);
        if (instanceDateOnly == dateOnly) {
            result.push_back(instance);
        }
    }
    
    return result;
}

bool Timetable::validate() const {
    return validateStopTimes() && validatePlatforms() && !stops.empty();
}

std::vector<std::string> Timetable::getValidationErrors() const {
    std::vector<std::string> errors;
    
    if (name.empty()) {
        errors.push_back("Brak nazwy rozkładu");
    }
    
    if (trainId.empty()) {
        errors.push_back("Nie przypisano pociągu");
    }
    
    if (lineId.empty()) {
        errors.push_back("Nie przypisano linii");
    }
    
    if (stops.empty()) {
        errors.push_back("Brak przystanków");
    } else if (stops.size() < 2) {
        errors.push_back("Rozkład musi mieć przynajmniej 2 przystanki");
    }
    
    // Sprawdź czasy
    if (!validateStopTimes()) {
        errors.push_back("Nieprawidłowa kolejność czasów przystanków");
    }
    
    // Sprawdź perony
    if (!validatePlatforms()) {
        errors.push_back("Nieprawidłowe numery peronów");
    }
    
    // Sprawdź częstotliwość
    if (frequency > 0) {
        if (firstRun >= lastRun) {
            errors.push_back("Nieprawidłowy zakres godzin kursowania");
        }
        if (frequency < 5) {
            errors.push_back("Częstotliwość musi być co najmniej 5 minut");
        }
    }
    
    return errors;
}

bool Timetable::hasConflicts(const Timetable* other) const {
    if (!other || other->trainId != trainId) {
        return false; // Różne pociągi
    }
    
    // Sprawdź czy okresy ważności się nakładają
    if (validTo < other->validFrom || validFrom > other->validTo) {
        return false; // Nie nakładają się
    }
    
    // Sprawdź dni kursowania
    if ((runningDays & other->runningDays) == TimetableDays::NONE) {
        return false; // Nie kursują w te same dni
    }
    
    // Sprawdź czasy
    auto thisTimes = getDepartureTimes();
    auto otherTimes = other->getDepartureTimes();
    
    int thisEnd = getLastArrivalTime();
    int otherEnd = other->getLastArrivalTime();
    
    for (int thisTime : thisTimes) {
        int thisStart = thisTime;
        int thisFinish = thisEnd - getFirstDepartureTime() + thisTime;
        
        for (int otherTime : otherTimes) {
            int otherStart = otherTime;
            int otherFinish = otherEnd - other->getFirstDepartureTime() + otherTime;
            
            // Sprawdź nakładanie
            if ((thisStart >= otherStart && thisStart <= otherFinish) ||
                (thisFinish >= otherStart && thisFinish <= otherFinish) ||
                (thisStart <= otherStart && thisFinish >= otherFinish)) {
                return true; // Konflikt czasowy
            }
        }
    }
    
    return false;
}

void Timetable::shiftTimes(int minutes) {
    for (auto& stop : stops) {
        stop.arrivalTime += minutes;
        stop.departureTime += minutes;
        
        // Zapewnij że czasy nie przekraczają 24h
        stop.arrivalTime = stop.arrivalTime % (24 * 60);
        stop.departureTime = stop.departureTime % (24 * 60);
    }
    
    if (frequency > 0) {
        firstRun += minutes;
        lastRun += minutes;
        
        firstRun = firstRun % (24 * 60);
        lastRun = lastRun % (24 * 60);
    }
}

void Timetable::optimizeDwellTimes() {
    for (auto& stop : stops) {
        // Domyślny czas postoju w zależności od typu stacji
        // TODO: Implementacja na podstawie typu stacji
        int optimalDwell = 2; // 2 minuty domyślnie
        
        stop.dwellTime = optimalDwell;
        stop.departureTime = stop.arrivalTime + optimalDwell;
    }
}

Timetable* Timetable::duplicate() const {
    Timetable* copy = new Timetable(id + "_copy", name + " (kopia)");
    
    copy->trainId = trainId;
    copy->lineId = lineId;
    copy->type = type;
    copy->active = active;
    copy->runningDays = runningDays;
    copy->validFrom = validFrom;
    copy->validTo = validTo;
    copy->frequency = frequency;
    copy->firstRun = firstRun;
    copy->lastRun = lastRun;
    copy->stops = stops;
    
    return copy;
}

std::string Timetable::exportToCSV() const {
    std::stringstream ss;
    
    // Nagłówek
    ss << "Stacja,Przyjazd,Odjazd,Peron,Opcjonalny\n";
    
    // Przystanki
    for (const auto& stop : stops) {
        ss << stop.stationId << ","
           << std::setfill('0') << std::setw(2) << (stop.arrivalTime / 60) << ":"
           << std::setfill('0') << std::setw(2) << (stop.arrivalTime % 60) << ","
           << std::setfill('0') << std::setw(2) << (stop.departureTime / 60) << ":"
           << std::setfill('0') << std::setw(2) << (stop.departureTime % 60) << ","
           << stop.platform << ","
           << (stop.optional ? "TAK" : "NIE") << "\n";
    }
    
    return ss.str();
}

bool Timetable::importFromCSV(const std::string& csvData) {
    stops.clear();
    
    std::istringstream ss(csvData);
    std::string line;
    
    // Pomiń nagłówek
    std::getline(ss, line);
    
    while (std::getline(ss, line)) {
        std::istringstream lineStream(line);
        std::string field;
        std::vector<std::string> fields;
        
        while (std::getline(lineStream, field, ',')) {
            fields.push_back(field);
        }
        
        if (fields.size() >= 5) {
            TimetableStop stop;
            stop.stationId = fields[0];
            
            // Parsuj czasy
            int h, m;
            char colon;
            
            std::istringstream arrivalStream(fields[1]);
            arrivalStream >> h >> colon >> m;
            stop.arrivalTime = h * 60 + m;
            
            std::istringstream departureStream(fields[2]);
            departureStream >> h >> colon >> m;
            stop.departureTime = h * 60 + m;
            
            stop.platform = std::stoi(fields[3]);
            stop.optional = (fields[4] == "TAK");
            stop.dwellTime = stop.departureTime - stop.arrivalTime;
            
            stops.push_back(stop);
        }
    }
    
    return !stops.empty();
}

void Timetable::sortStops() {
    std::sort(stops.begin(), stops.end(),
        [](const TimetableStop& a, const TimetableStop& b) {
            return a.departureTime < b.departureTime;
        });
}

bool Timetable::validateStopTimes() const {
    if (stops.size() < 2) return true;
    
    for (size_t i = 0; i < stops.size(); ++i) {
        // Sprawdź czy odjazd jest po przyjeździe
        if (stops[i].arrivalTime > stops[i].departureTime) {
            return false;
        }
        
        // Sprawdź kolejność
        if (i > 0) {
            if (stops[i].arrivalTime < stops[i-1].departureTime) {
                return false;
            }
        }
    }
    
    return true;
}

bool Timetable::validatePlatforms() const {
    for (const auto& stop : stops) {
        if (stop.platform < 1 || stop.platform > 20) {
            return false;
        }
    }
    return true;
}