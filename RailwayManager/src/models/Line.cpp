#include "Line.h"
#include "utils/Logger.h"
#include <algorithm>
#include <numeric>
#include <set>

Line::Line(const std::string& id, const std::string& number, const std::string& name)
    : id(id), number(number), name(name), type(LineType::REGIONAL), 
      status(LineStatus::OPERATIONAL), electrification(ElectrificationType::NONE) {
}

Line::~Line() {
    // Destruktor
}

void Line::addSection(const TrackSection& section) {
    // Sprawdź czy sekcja o tym ID już istnieje
    auto it = std::find_if(sections.begin(), sections.end(),
        [&section](const TrackSection& s) { return s.id == section.id; });
    
    if (it != sections.end()) {
        LOG_WARNING("Sekcja " + section.id + " już istnieje na linii " + name);
        return;
    }
    
    sections.push_back(section);
    LOG_INFO("Dodano sekcję " + section.id + " do linii " + name);
}

void Line::removeSection(const std::string& sectionId) {
    // Zwolnij sekcję jeśli jest zajęta
    freeSection(sectionId);
    
    // Usuń blokadę jeśli istnieje
    blockedSections.erase(sectionId);
    
    // Usuń sekcję
    sections.erase(
        std::remove_if(sections.begin(), sections.end(),
            [&sectionId](const TrackSection& s) { return s.id == sectionId; }),
        sections.end()
    );
}

TrackSection* Line::getSection(const std::string& sectionId) {
    auto it = std::find_if(sections.begin(), sections.end(),
        [&sectionId](const TrackSection& s) { return s.id == sectionId; });
    
    return (it != sections.end()) ? &(*it) : nullptr;
}

TrackSection* Line::getSectionBetween(const std::string& fromStation, const std::string& toStation) {
    auto it = std::find_if(sections.begin(), sections.end(),
        [&fromStation, &toStation](const TrackSection& s) {
            return (s.fromStationId == fromStation && s.toStationId == toStation) ||
                   (s.fromStationId == toStation && s.toStationId == fromStation);
        });
    
    return (it != sections.end()) ? &(*it) : nullptr;
}

float Line::getTotalLength() const {
    return std::accumulate(sections.begin(), sections.end(), 0.0f,
        [](float sum, const TrackSection& section) { return sum + section.length; });
}

int Line::getMaxSpeed() const {
    if (sections.empty()) return 0;
    
    return std::max_element(sections.begin(), sections.end(),
        [](const TrackSection& a, const TrackSection& b) {
            return a.maxSpeed < b.maxSpeed;
        })->maxSpeed;
}

int Line::getMinSpeed() const {
    if (sections.empty()) return 0;
    
    return std::min_element(sections.begin(), sections.end(),
        [](const TrackSection& a, const TrackSection& b) {
            return a.maxSpeed < b.maxSpeed;
        })->maxSpeed;
}

bool Line::isFullyElectrified() const {
    return std::all_of(sections.begin(), sections.end(),
        [](const TrackSection& s) { return s.isElectrified; });
}

bool Line::isDoubleTrack() const {
    return std::all_of(sections.begin(), sections.end(),
        [](const TrackSection& s) { return s.tracks >= 2; });
}

float Line::getAverageCondition() const {
    if (sections.empty()) return 1.0f;
    
    float totalCondition = std::accumulate(sections.begin(), sections.end(), 0.0f,
        [](float sum, const TrackSection& s) { return sum + s.condition; });
    
    return totalCondition / sections.size();
}

std::vector<std::string> Line::getStationIds() const {
    std::set<std::string> stations;
    
    for (const auto& section : sections) {
        stations.insert(section.fromStationId);
        stations.insert(section.toStationId);
    }
    
    return std::vector<std::string>(stations.begin(), stations.end());
}

bool Line::hasStation(const std::string& stationId) const {
    return std::any_of(sections.begin(), sections.end(),
        [&stationId](const TrackSection& s) {
            return s.fromStationId == stationId || s.toStationId == stationId;
        });
}

float Line::getDistanceBetween(const std::string& station1, const std::string& station2) const {
    // Uproszczona implementacja - zakłada liniową strukturę
    float distance = 0.0f;
    bool counting = false;
    
    for (const auto& section : sections) {
        if (section.fromStationId == station1 || section.toStationId == station1) {
            counting = !counting;
        }
        
        if (counting) {
            distance += section.length;
        }
        
        if (section.fromStationId == station2 || section.toStationId == station2) {
            if (counting) {
                return distance;
            }
        }
    }
    
    return -1.0f; // Nie znaleziono połączenia
}

void Line::addSignal(const Signal& signal) {
    signals.push_back(signal);
    
    // Sortuj sygnały według pozycji
    std::sort(signals.begin(), signals.end(),
        [](const Signal& a, const Signal& b) { return a.position < b.position; });
}

void Line::removeSignal(const std::string& signalId) {
    signals.erase(
        std::remove_if(signals.begin(), signals.end(),
            [&signalId](const Signal& s) { return s.id == signalId; }),
        signals.end()
    );
}

Signal* Line::getSignal(const std::string& signalId) {
    auto it = std::find_if(signals.begin(), signals.end(),
        [&signalId](const Signal& s) { return s.id == signalId; });
    
    return (it != signals.end()) ? &(*it) : nullptr;
}

void Line::updateSignalAspect(const std::string& signalId, const std::string& aspect) {
    Signal* signal = getSignal(signalId);
    if (signal) {
        signal->currentAspect = aspect;
    }
}

void Line::occupySection(const std::string& sectionId, const std::string& trainId) {
    if (isSectionBlocked(sectionId)) {
        LOG_WARNING("Próba zajęcia zablokowanej sekcji " + sectionId);
        return;
    }
    
    sectionOccupancy[sectionId] = trainId;
    LOG_INFO("Sekcja " + sectionId + " zajęta przez pociąg " + trainId);
}

void Line::freeSection(const std::string& sectionId) {
    sectionOccupancy.erase(sectionId);
}

bool Line::isSectionOccupied(const std::string& sectionId) const {
    return sectionOccupancy.find(sectionId) != sectionOccupancy.end();
}

bool Line::canTrainEnter(const std::string& sectionId) const {
    return !isSectionOccupied(sectionId) && !isSectionBlocked(sectionId);
}

std::vector<std::string> Line::getOccupiedSections() const {
    std::vector<std::string> occupied;
    for (const auto& pair : sectionOccupancy) {
        occupied.push_back(pair.first);
    }
    return occupied;
}

void Line::addSpeedRestriction(const SpeedRestriction& restriction) {
    speedRestrictions.push_back(restriction);
}

void Line::removeSpeedRestriction(const std::string& restrictionId) {
    speedRestrictions.erase(
        std::remove_if(speedRestrictions.begin(), speedRestrictions.end(),
            [&restrictionId](const SpeedRestriction& r) { return r.id == restrictionId; }),
        speedRestrictions.end()
    );
}

int Line::getSpeedLimitAt(float position) const {
    int limit = 999; // Brak ograniczenia
    
    // Sprawdź ograniczenia prędkości
    for (const auto& restriction : speedRestrictions) {
        if (position >= restriction.fromKm && position <= restriction.toKm) {
            limit = std::min(limit, restriction.speedLimit);
        }
    }
    
    return limit;
}

void Line::scheduleMaintenanceForSection(const std::string& sectionId) {
    TrackSection* section = getSection(sectionId);
    if (section) {
        section->status = LineStatus::MAINTENANCE;
        LOG_INFO("Zaplanowano konserwację sekcji " + sectionId);
    }
}

void Line::completeMaintenance(const std::string& sectionId) {
    TrackSection* section = getSection(sectionId);
    if (section) {
        section->status = LineStatus::OPERATIONAL;
        section->condition = 1.0f;
        LOG_INFO("Zakończono konserwację sekcji " + sectionId);
    }
}

std::vector<std::string> Line::getSectionsNeedingMaintenance(float threshold) const {
    std::vector<std::string> needsMaintenance;
    
    for (const auto& section : sections) {
        if (section.condition < threshold) {
            needsMaintenance.push_back(section.id);
        }
    }
    
    return needsMaintenance;
}

void Line::blockSection(const std::string& sectionId, const std::string& reason) {
    blockedSections[sectionId] = reason;
    
    TrackSection* section = getSection(sectionId);
    if (section) {
        section->status = LineStatus::BLOCKED;
    }
    
    LOG_WARNING("Zablokowano sekcję " + sectionId + ": " + reason);
}

void Line::unblockSection(const std::string& sectionId) {
    blockedSections.erase(sectionId);
    
    TrackSection* section = getSection(sectionId);
    if (section && section->status == LineStatus::BLOCKED) {
        section->status = LineStatus::OPERATIONAL;
    }
    
    LOG_INFO("Odblokowano sekcję " + sectionId);
}

bool Line::isSectionBlocked(const std::string& sectionId) const {
    return blockedSections.find(sectionId) != blockedSections.end();
}

void Line::recordTrainPassage(const std::string& trainId, int delayMinutes) {
    statistics.totalTrainsToday++;
    statistics.totalTrainsMonth++;
    
    if (delayMinutes > 0) {
        statistics.totalDelays++;
        // Aktualizuj średnie opóźnienie
        statistics.averageDelay = (statistics.averageDelay * (statistics.totalDelays - 1) + delayMinutes) 
                                 / statistics.totalDelays;
    }
}

std::vector<std::string> Line::findRoute(const std::string& fromStation, const std::string& toStation) const {
    // Uproszczony algorytm - zakłada liniową strukturę
    std::vector<std::string> route;
    
    bool found = false;
    for (const auto& section : sections) {
        if (section.fromStationId == fromStation) {
            found = true;
        }
        
        if (found) {
            route.push_back(section.id);
            
            if (section.toStationId == toStation) {
                break;
            }
        }
    }
    
    return route;
}

float Line::calculateTravelTime(const std::string& fromStation, const std::string& toStation, float trainMaxSpeed) const {
    float totalTime = 0.0f;
    float distance = 0.0f;
    
    auto route = findRoute(fromStation, toStation);
    for (const auto& sectionId : route) {
        TrackSection* section = const_cast<Line*>(this)->getSection(sectionId);
        if (section) {
            float sectionSpeed = std::min(trainMaxSpeed, static_cast<float>(section->maxSpeed));
            
            // Uwzględnij stan toru
            sectionSpeed *= section->condition;
            
            // Uwzględnij nachylenie
            if (section->gradient > 10) { // Powyżej 10 promili
                sectionSpeed *= 0.8f;
            }
            
            totalTime += (section->length / sectionSpeed) * 60.0f; // Minuty
        }
    }
    
    return totalTime;
}